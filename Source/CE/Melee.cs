using CombatExtended;
using HarmonyLib;

namespace StiltsBionics.CE
{
    /// <summary>CE's melee verb overrides TryCastShot (it calls its own ApplyMeleeDamageToTarget, parries and
    /// ripostes included), so the melee scope of the main DLL (MeleeLegDamage.cs) is opened here too.</summary>
    [HarmonyPatch(typeof(Verb_MeleeAttackCE), "TryCastShot")]
    public static class MeleePatch_TryCastShotCE
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
}
