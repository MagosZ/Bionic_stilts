using Verse;

namespace StiltsBionics
{
    /// <summary>One stilt, replacing one leg. Losing one while the stilts are up (shot off, cut off) makes the pawn fall;
    /// the fall itself runs on the next tick, outside the damage code that removed the stilt.</summary>
    public class Hediff_BionicStilt : Hediff_AddedPart
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            // The controller (extend state, button) comes with the first stilt, however the stilt was added.
            if (pawn?.health?.hediffSet != null && !pawn.health.hediffSet.HasHediff(StiltsDefOf.SB_StiltsControl))
            {
                pawn.health.AddHediff(StiltsDefOf.SB_StiltsControl);
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            Stilts.ControllerOf(pawn)?.Notify_StiltLost();
        }
    }
}
