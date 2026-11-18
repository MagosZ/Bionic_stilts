using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace StiltsBionics
{
    /// <summary>"Ignores cover" in vanilla terms (used by vanilla verbs and by every mod's target scoring, CE's
    /// included): a shooter on extended stilts sees over partial cover, so cover gives its target no protection and its
    /// rounds never miss into cover; and a pawn on extended stilts is too tall to hide behind any.</summary>
    public static class StiltsCover
    {
        [ThreadStatic]
        private static int depth;

        /// <summary>True while a hit report or a target score is worked out for an extended shooter.</summary>
        public static bool Active => depth > 0;

        public static bool Enter(Thing shooter)
        {
            if (!Stilts.AnyStilts || !Stilts.IsExtended(shooter))
            {
                return false;
            }
            depth++;
            return true;
        }

        public static void Exit(bool entered)
        {
            if (entered && depth > 0)
            {
                depth--;
            }
        }

        /// <summary>A GetShootingTargetScore overload binding 'IAttackTargetSearcher searcher', or null.</summary>
        public static MethodInfo ResolveScore(Type type, Type[] parameters)
        {
            MethodInfo method;
            try
            {
                method = AccessTools.Method(type, "GetShootingTargetScore", parameters)
                    ?? AccessTools.Method(type, "GetShootingTargetScore");
            }
            catch (Exception)
            {
                method = null;
            }
            if (method == null || !method.IsStatic)
            {
                return null;
            }
            foreach (ParameterInfo p in method.GetParameters())
            {
                if (p.Name == "searcher" && p.ParameterType == typeof(IAttackTargetSearcher))
                {
                    return method;
                }
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(CoverUtility), nameof(CoverUtility.CalculateOverallBlockChance),
        new[] { typeof(LocalTargetInfo), typeof(IntVec3), typeof(Map) })]
    public static class CoverPatch_CalculateOverallBlockChance
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(LocalTargetInfo __0, ref float __result)
        {
            if (__result > 0f && (StiltsCover.Active || Stilts.IsExtended(__0.Thing)))
            {
                __result = 0f;
            }
        }
    }

    /// <summary>Vanilla hit chance: no cover between an extended shooter and its target, or around an extended target.</summary>
    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class CoverPatch_HitReportFor
    {
        private static readonly AccessTools.StructFieldRef<ShotReport, float> BlockChance = ResolveBlockChance();

        private static AccessTools.StructFieldRef<ShotReport, float> ResolveBlockChance()
        {
            FieldInfo field = AccessTools.Field(typeof(ShotReport), "coversOverallBlockChance");
            if (field == null || field.FieldType != typeof(float))
            {
                return null;
            }
            try
            {
                return AccessTools.StructFieldRefAccess<ShotReport, float>(field);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Prefix(Thing caster, out bool __state)
        {
            __state = StiltsCover.Enter(caster);
        }

        public static void Postfix(Thing caster, LocalTargetInfo target, ref ShotReport __result)
        {
            if (BlockChance != null && Stilts.AnyStilts && (Stilts.IsExtended(caster) || Stilts.IsExtended(target.Thing)))
            {
                BlockChance(ref __result) = 0f;
            }
        }

        public static void Finalizer(bool __state)
        {
            StiltsCover.Exit(__state);
        }
    }

    /// <summary>Target choice: no cover penalty while an extended pawn scores targets (vanilla's finder).</summary>
    [HarmonyPatch]
    public static class CoverPatch_VanillaScore
    {
        private static readonly MethodInfo Target = StiltsCover.ResolveScore(typeof(AttackTargetFinder),
            new[] { typeof(IAttackTarget), typeof(IAttackTargetSearcher), typeof(Verb) });

        public static bool Prepare()
        {
            if (Target == null)
            {
                StiltsLog.WarnOnce("VanillaScore",
                    "AttackTargetFinder.GetShootingTargetScore not found; stilt pawns keep the cover penalty when choosing targets.");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => Target;

        public static void Prefix(IAttackTargetSearcher searcher, out bool __state)
        {
            __state = StiltsCover.Enter(searcher?.Thing);
        }

        public static void Finalizer(bool __state)
        {
            StiltsCover.Exit(__state);
        }
    }
}
