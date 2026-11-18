using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace StiltsBionics
{
    /// <summary>Heights for the vanilla (non-CE) over-the-wall rules. Vanilla has none, so these are Combat
    /// Extended's defaults, fixed: a wall (any full-fillage building or rock) 2.0 = 3.5 m, a human 1.0 = 1.75 m holding
    /// the gun at 0.85 of its height, a built roof slab topping out at 2.1, thin rock roof 4.0, overhead mountain 8.0.
    /// An extended stilt pawn's body is lifted by the stilts' lift (2.43): top 3.43 = 6 m, gun at 3.28.</summary>
    public static class VanillaHeights
    {
        public const float WallTop = 2f;
        public const float BuiltRoofTop = 2.1f;
        public const float ThinRockRoofTop = 4f;
        public const float ThickRockRoofTop = 8f;
        public const float GunHeightFactor = 0.85f;

        /// <summary>Body height of a pawn standing on the ground.</summary>
        public static float BodyOf(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike)
            {
                return 1f;
            }
            return Mathf.Clamp(pawn.BodySize, 0.2f, 2f);
        }

        /// <summary>Top of a thing: what a line must pass above to clear it, and where sight aims.</summary>
        public static float TopOf(Thing thing)
        {
            if (thing is Pawn pawn)
            {
                float body = BodyOf(pawn);
                if (Stilts.IsExtended(pawn))
                {
                    return Stilts.Props.lift + body;
                }
                return pawn.Downed ? body * 0.3f : body;
            }
            if (thing == null)
            {
                return 0f;
            }
            if (thing.def.Fillage == FillCategory.Full)
            {
                return thing is Building_Door door && door.Open ? 0f : WallTop;
            }
            return thing.def.fillPercent;
        }

        /// <summary>Height a thing shoots and looks from.</summary>
        public static float MuzzleOf(Thing thing)
        {
            if (thing is Pawn pawn)
            {
                float body = BodyOf(pawn);
                return (Stilts.IsExtended(pawn) ? Stilts.Props.lift : 0f) + body * GunHeightFactor;
            }
            return Mathf.Max(0.5f, TopOf(thing));
        }

        /// <summary>Top of the roof slab over a cell, or negative infinity without a roof. Only rock roofs are thick
        /// (as Elevated Gun Platform's plain-roofs rule).</summary>
        public static float RoofTop(IntVec3 cell, Map map)
        {
            RoofDef roof = map.roofGrid.RoofAt(cell);
            if (roof == null)
            {
                return float.NegativeInfinity;
            }
            if (roof == RoofDefOf.RoofRockThick)
            {
                return ThickRockRoofTop;
            }
            if (roof == RoofDefOf.RoofRockThin)
            {
                return ThinRockRoofTop;
            }
            return BuiltRoofTop;
        }

        /// <summary>Top of the full-height obstacle in a cell (wall, closed door, rock), or 0.</summary>
        public static float WallTopAt(IntVec3 cell, Map map)
        {
            Building edifice = cell.GetEdifice(map);
            if (edifice == null || edifice.def.Fillage != FillCategory.Full)
            {
                return 0f;
            }
            return edifice is Building_Door door && door.Open ? 0f : WallTop;
        }
    }

    /// <summary>The 3D sight test of Elevated Gun Platform (DESIGN 3.2/3.3) with the vanilla heights: from a muzzle to
    /// a target point, roofs block where the line is below the slab top (on every cell, the two ends included), walls
    /// block where the line is below their top, gas blocks through the validator; pawns and partial cover never
    /// block sight. Symmetric by construction.</summary>
    public static class VanillaSight
    {
        [ThreadStatic]
        private static List<IntVec3> cellBuffer;

        public static bool Clear(Vector3 from, Vector3 to, CellRect sourceRect, CellRect targetRect, Map map,
            Func<IntVec3, bool> validator)
        {
            List<IntVec3> cells = cellBuffer ?? (cellBuffer = new List<IntVec3>());
            Line3D.CellsCrossed(from, to, cells);
            // The walk ends where the line ends; the end cell's roof matters too.
            IntVec3 endCell = Line3D.Flat(to.ToIntVec3());
            if (cells.Count == 0 || cells[cells.Count - 1] != endCell)
            {
                cells.Add(endCell);
            }
            bool debug = Line3D.DebugActive;
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(map))
                {
                    continue;
                }
                Line3D.HeightRangeInCell(from, to, cell, out float minY, out _);
                bool blocked = VanillaHeights.RoofTop(cell, map) > minY;
                if (!blocked && !sourceRect.Contains(cell) && !targetRect.Contains(cell))
                {
                    blocked = (validator != null && !validator(cell)) || VanillaHeights.WallTopAt(cell, map) > minY;
                }
                if (debug)
                {
                    Line3D.Flash(map, cell, blocked ? 0f : 0.7f, minY);
                }
                if (blocked)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Sight from a thing's muzzle (or a lean point of it) to the target's top.</summary>
        public static bool Clear(Thing seer, Vector3 seerPos, LocalTargetInfo target, Map map, Func<IntVec3, bool> validator)
        {
            Vector3 from = new Vector3(seerPos.x, VanillaHeights.MuzzleOf(seer), seerPos.z);
            Vector3 to;
            CellRect targetRect;
            if (target.HasThing)
            {
                Vector3 tp = target.Thing.DrawPos;
                to = new Vector3(tp.x, VanillaHeights.TopOf(target.Thing), tp.z);
                targetRect = target.Thing.OccupiedRect();
            }
            else
            {
                Vector3 tc = target.Cell.ToVector3Shifted();
                to = new Vector3(tc.x, 0f, tc.z);
                targetRect = CellRect.SingleCell(target.Cell);
            }
            return Clear(from, to, seer.OccupiedRect(), targetRect, map, validator);
        }
    }
}
