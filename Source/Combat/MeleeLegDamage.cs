using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StiltsBionics
{
    /// <summary>The price of the long strides (user): while the stilts are extended, MELEE damage to a stilted leg is
    /// multiplied by legDamageFactor (1.5). Ranged, blast and every other damage is unchanged.
    /// "Melee" = damage dealt while a melee verb casts: vanilla Verb_MeleeAttack.TryCastShot here, CE's own
    /// Verb_MeleeAttackCE.TryCastShot override in the CE DLL. The multiplier is applied in
    /// DamageWorker_AddInjury.FinalizeAndAddInjury(Pawn, float, DamageInfo, DamageResult), the step every damage
    /// worker (cuts spreading over parts too) and CE's armour code go through once the part is chosen and armour is
    /// done (CE's Harmony_DamageWorker_AddInjury.cs calls it the same way).</summary>
    public static class StiltsMelee
    {
        [ThreadStatic]
        private static int depth;

        public static bool Active => depth > 0;

        public static bool Enter()
        {
            if (!Stilts.AnyStilts)
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

        /// <summary>The part is a stilted leg, or lies below one.</summary>
        public static bool OnStilt(Pawn pawn, BodyPartRecord part)
        {
            for (BodyPartRecord p = part; p != null; p = p.parent)
            {
                if (Stilts.HasStiltOn(pawn, p))
                {
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    public static class MeleePatch_TryCastShot
    {
        public static void Prefix(out bool __state)
        {
            __state = StiltsMelee.Enter();
        }

        public static void Finalizer(bool __state)
        {
            StiltsMelee.Exit(__state);
        }
    }

    [HarmonyPatch]
    public static class MeleePatch_FinalizeAndAddInjury
    {
        private static readonly MethodInfo Target = AccessTools.Method(typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury",
            new[] { typeof(Pawn), typeof(float), typeof(DamageInfo), typeof(DamageWorker.DamageResult) });

        public static bool Prepare()
        {
            if (Target == null)
            {
                StiltsLog.WarnOnce("FinalizeAndAddInjury",
                    "DamageWorker_AddInjury.FinalizeAndAddInjury not found; extended stilts take normal melee damage.");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => Target;

        public static void Prefix(Pawn __0, ref float __1, DamageInfo __2)
        {
            if (!StiltsMelee.Active || __1 <= 0f || __2.HitPart == null || !Stilts.IsExtended(__0))
            {
                return;
            }
            if (StiltsMelee.OnStilt(__0, __2.HitPart))
            {
                __1 *= Stilts.Props.legDamageFactor;
            }
        }
    }
}
