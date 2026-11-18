using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StiltsBionics
{
    /// <summary>One surgery puts the pair on: every leg gets a stilt (any part there before, natural or artificial, is
    /// removed as vanilla does, artificial ones dropped as items), plus the controller hediff.</summary>
    public class Recipe_InstallBionicStilts : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!(thing is Pawn pawn) || !base.AvailableOnNow(thing, part))
            {
                return false;
            }
            return pawn.RaceProps.Humanlike && Stilts.LegsOf(pawn).Count >= 2 && !Stilts.HasAllStilts(pawn);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            List<BodyPartRecord> legs = Stilts.LegsOf(pawn);
            if (legs.Count < 2)
            {
                return;
            }
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, legs[0], bill))
                {
                    return;
                }
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
                if (ModsConfig.IdeologyActive)
                {
                    Find.HistoryEventsManager.RecordEvent(new HistoryEvent(HistoryEventDefOf.InstalledProsthetic,
                        billDoer.Named(HistoryEventArgsNames.Doer)));
                }
            }
            foreach (BodyPartRecord leg in legs)
            {
                if (Stilts.HasStiltOn(pawn, leg))
                {
                    continue;
                }
                if (billDoer != null && billDoer.Map != null)
                {
                    MedicalRecipesUtility.RestorePartAndSpawnAllPreviousParts(pawn, leg, billDoer.Position, billDoer.Map);
                }
                else if (pawn.Map != null)
                {
                    MedicalRecipesUtility.RestorePartAndSpawnAllPreviousParts(pawn, leg, pawn.Position, pawn.Map);
                }
                else
                {
                    pawn.health.RestorePart(leg);
                }
                pawn.health.AddHediff(StiltsDefOf.SB_BionicStilt, leg);
            }
            if (!pawn.health.hediffSet.HasHediff(StiltsDefOf.SB_StiltsControl))
            {
                pawn.health.AddHediff(StiltsDefOf.SB_StiltsControl);
            }
        }
    }

    /// <summary>Takes the stilts off (both legs end up missing). The pair comes back as an item only when every leg
    /// still had its stilt; a lone stilt is scrap.</summary>
    public class Recipe_RemoveBionicStilts : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            return thing is Pawn pawn && base.AvailableOnNow(thing, part) && Stilts.StiltCount(pawn) > 0;
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            List<Hediff> stilts = pawn.health.hediffSet.hediffs.Where(h => h.def == StiltsDefOf.SB_BionicStilt).ToList();
            if (stilts.Count == 0)
            {
                return;
            }
            bool wholePair = Stilts.HasAllStilts(pawn);
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, stilts[0].Part, bill))
                {
                    return;
                }
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }
            Map map = billDoer?.Map ?? pawn.MapHeld;
            IntVec3 cell = billDoer?.Position ?? pawn.PositionHeld;
            foreach (Hediff stilt in stilts)
            {
                // As vanilla's part removal does: the part is cut off and becomes missing.
                pawn.TakeDamage(new DamageInfo(DamageDefOf.SurgicalCut, 99999f, 999f, -1f, null, stilt.Part));
            }
            if (wholePair && map != null)
            {
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(StiltsDefOf.SB_BionicStilts), cell, map, ThingPlaceMode.Near);
            }
        }
    }

    /// <summary>Vanilla's per-part surgeries (remove a part, install a part or implant) skip legs that hold a stilt, so
    /// the pair is only ever taken off whole, by the stilts' own surgery.</summary>
    [HarmonyPatch]
    public static class RecipePatch_GetPartsToApplyOn
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();
            foreach (System.Type type in new[] { typeof(Recipe_RemoveBodyPart), typeof(Recipe_InstallArtificialBodyPart),
                typeof(Recipe_InstallNaturalBodyPart), typeof(Recipe_InstallImplant) })
            {
                MethodInfo method = AccessTools.Method(type, nameof(RecipeWorker.GetPartsToApplyOn),
                    new[] { typeof(Pawn), typeof(RecipeDef) });
                if (method != null && method.DeclaringType != typeof(RecipeWorker) && seen.Add(method))
                {
                    yield return method;
                }
            }
        }

        public static IEnumerable<BodyPartRecord> Postfix(IEnumerable<BodyPartRecord> __result, Pawn __0)
        {
            foreach (BodyPartRecord part in __result)
            {
                if (!Stilts.HasStiltOn(__0, part))
                {
                    yield return part;
                }
            }
        }
    }
}
