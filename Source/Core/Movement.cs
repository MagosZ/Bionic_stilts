using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace StiltsBionics
{
    /// <summary>The stilts' clock, and the roof rule: the path follower runs every tick for every spawned pawn, so the
    /// stilts tick here. A pawn whose stilts are moving stands still; an extended pawn about to step under a roof stops
    /// at the edge and retracts first (no change to path finding, which is Burst-compiled in 1.6).</summary>
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class MovementPatch_PatherTick
    {
        public static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            if (!Stilts.AnyStilts)
            {
                return true;
            }
            HediffComp_Stilts comp = Stilts.ControllerOf(___pawn);
            return comp == null || comp.PatherTick(__instance);
        }
    }

    /// <summary>Loading a save: the controller comps register once their hediff set has given every hediff its pawn
    /// (a comp's own PostLoadInit can run before that).</summary>
    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.ExposeData))]
    public static class MovementPatch_HediffSetExposeData
    {
        public static void Postfix(HediffSet __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || __instance.pawn == null)
            {
                return;
            }
            List<Hediff> hediffs = __instance.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is HediffWithComps withComps)
                {
                    HediffComp_Stilts comp = withComps.TryGetComp<HediffComp_Stilts>();
                    if (comp != null)
                    {
                        Stilts.Register(__instance.pawn, comp);
                    }
                }
            }
        }
    }

    /// <summary>No attack (ranged or melee) while the stilts are moving.</summary>
    [HarmonyPatch]
    public static class MovementPatch_TryStartCastOn
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(Verb))
                .Where(m => m.Name == nameof(Verb.TryStartCastOn) && m.ReturnType == typeof(bool) && !m.IsStatic);
        }

        public static bool Prefix(Verb __instance, ref bool __result)
        {
            if (!Stilts.AnyStilts || !(__instance.caster is Pawn pawn))
            {
                return true;
            }
            HediffComp_Stilts comp = Stilts.ControllerOf(pawn);
            if (comp != null && comp.InTransition)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>The extend/retract button on colonists with stilts.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class MovementPatch_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result)
            {
                yield return gizmo;
            }
            if (!Stilts.AnyStilts || !__instance.IsColonistPlayerControlled)
            {
                yield break;
            }
            HediffComp_Stilts comp = Stilts.ControllerOf(__instance);
            if (comp != null && Stilts.StiltCount(__instance) > 0)
            {
                yield return comp.GetToggleGizmo();
            }
        }
    }
}
