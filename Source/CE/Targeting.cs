using System.Reflection;
using CombatExtended;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace StiltsBionics.CE
{
    /// <summary>Target choice with CE's non-snap finder: no flat cover penalty while an extended pawn scores targets
    /// (the main DLL zeroes CoverUtility.CalculateOverallBlockChance inside this scope).</summary>
    [HarmonyPatch]
    public static class TargetingPatch_NonSnapScore
    {
        private static readonly MethodInfo Target = StiltsCover.ResolveScore(typeof(NonSnapAttackTargetFinder),
            new[] { typeof(IAttackTarget), typeof(IAttackTargetSearcher), typeof(Verb), typeof(Vector3) });

        public static bool Prepare()
        {
            if (Target == null)
            {
                Log.Warning("[Stilts Bionics CE] NonSnapAttackTargetFinder.GetShootingTargetScore not found; stilt pawns keep the cover penalty when choosing targets.");
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
