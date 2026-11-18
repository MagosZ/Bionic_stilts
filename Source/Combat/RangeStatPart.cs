using RimWorld;
using Verse;

namespace StiltsBionics
{
    /// <summary>On RangedWeapon_RangeMultiplier (added by Patches/Stats.xml): a ranged weapon held by a pawn on
    /// extended stilts reaches further. Vanilla and Combat Extended both read this stat for a weapon's range.</summary>
    public class StatPart_StiltsRange : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (Applies(req))
            {
                val *= 1f + StiltsMod.Settings.rangeBonus;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!Applies(req))
            {
                return null;
            }
            return "StiltsBionics_RangeExplanation".Translate() + ": x" + (1f + StiltsMod.Settings.rangeBonus).ToStringPercent();
        }

        private static bool Applies(StatRequest req)
        {
            if (!req.HasThing || !Stilts.AnyStilts || StiltsMod.Settings == null || StiltsMod.Settings.rangeBonus == 0f)
            {
                return false;
            }
            Pawn holder = (req.Thing.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            return holder != null && Stilts.IsExtended(holder);
        }
    }
}
