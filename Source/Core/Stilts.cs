using System.Collections.Generic;
using RimWorld;
using Verse;

namespace StiltsBionics
{
    [DefOf]
    public static class StiltsDefOf
    {
        public static ThingDef SB_BionicStilts;
        public static HediffDef SB_BionicStilt;
        public static HediffDef SB_StiltsControl;

        static StiltsDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(StiltsDefOf));
        }
    }

    /// <summary>Lookups shared by every part of the mod. A pawn with stilts carries one SB_BionicStilt hediff per leg
    /// and one SB_StiltsControl hediff (whole body) whose comp holds the extend state; the comps register here so hot
    /// paths (drawing, sight, projectiles) find a pawn's state with one dictionary lookup.</summary>
    public static class Stilts
    {
        // Written on the main thread only (add, remove, load); read from drawing threads too, which never write.
        private static readonly Dictionary<Pawn, HediffComp_Stilts> controllers = new Dictionary<Pawn, HediffComp_Stilts>();
        private static Game registryGame;

        internal static void Register(Pawn pawn, HediffComp_Stilts comp)
        {
            if (pawn == null)
            {
                return;
            }
            if (registryGame != Current.Game)
            {
                controllers.Clear();
                registryGame = Current.Game;
            }
            controllers[pawn] = comp;
        }

        internal static void Unregister(Pawn pawn, HediffComp_Stilts comp)
        {
            if (pawn != null && controllers.TryGetValue(pawn, out HediffComp_Stilts current) && current == comp)
            {
                controllers.Remove(pawn);
            }
        }

        /// <summary>The pawn's stilts controller, or null when it has no stilts.</summary>
        public static HediffComp_Stilts ControllerOf(Pawn pawn)
        {
            if (pawn == null || controllers.Count == 0 || registryGame != Current.Game)
            {
                return null;
            }
            controllers.TryGetValue(pawn, out HediffComp_Stilts comp);
            return comp;
        }

        /// <summary>True when the thing is a spawned pawn standing on fully extended stilts (6 m tall).</summary>
        public static bool IsExtended(Thing thing)
        {
            return thing is Pawn pawn && pawn.Spawned && ControllerOf(pawn) is HediffComp_Stilts comp && comp.Extended;
        }

        /// <summary>True while any pawn of the current game has stilts: lets hot paths return at once otherwise.</summary>
        public static bool AnyStilts => controllers.Count > 0 && registryGame == Current.Game;

        /// <summary>The tunables (lift height, timings, fall), from the controller hediff's comp properties.</summary>
        public static HediffCompProperties_Stilts Props => props ??= StiltsDefOf.SB_StiltsControl.CompProps<HediffCompProperties_Stilts>()
            ?? new HediffCompProperties_Stilts();
        private static HediffCompProperties_Stilts props;

        /// <summary>The leg parts the stilts replace: every Leg of the body whose parent part is still there.</summary>
        public static List<BodyPartRecord> LegsOf(Pawn pawn)
        {
            var legs = new List<BodyPartRecord>();
            if (pawn?.RaceProps?.body == null)
            {
                return legs;
            }
            foreach (BodyPartRecord part in pawn.RaceProps.body.AllParts)
            {
                if (part.def == BodyPartDefOf.Leg && (part.parent == null || !pawn.health.hediffSet.PartIsMissing(part.parent)))
                {
                    legs.Add(part);
                }
            }
            return legs;
        }

        public static bool HasStiltOn(Pawn pawn, BodyPartRecord part)
        {
            if (pawn?.health?.hediffSet == null || part == null)
            {
                return false;
            }
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].def == StiltsDefOf.SB_BionicStilt && hediffs[i].Part == part)
                {
                    return true;
                }
            }
            return false;
        }

        public static int StiltCount(Pawn pawn)
        {
            int count = 0;
            if (pawn?.health?.hediffSet == null)
            {
                return 0;
            }
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].def == StiltsDefOf.SB_BionicStilt)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Every leg (at least two) carries a stilt: only then can they extend.</summary>
        public static bool HasAllStilts(Pawn pawn)
        {
            List<BodyPartRecord> legs = LegsOf(pawn);
            if (legs.Count < 2)
            {
                return false;
            }
            foreach (BodyPartRecord leg in legs)
            {
                if (!HasStiltOn(pawn, leg))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
