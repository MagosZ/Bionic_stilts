using System;
using System.Collections.Generic;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace StiltsBionics.CE
{
    /// <summary>Spotting (Elevated Gun Platform DESIGN 3.3, ported): when the seer or the target stands on extended
    /// stilts, sight is the 3D line from the seer's muzzle to the target's top instead of vanilla's flat line of sight.
    /// Gas (the validator), roofs and buildings/cover (by CE bounds) block; pawns don't; leaning is ignored.
    /// Symmetric by construction.</summary>
    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.CanSee),
        new[] { typeof(Thing), typeof(Thing), typeof(Func<IntVec3, bool>) })]
    public static class SightPatch_CanSee
    {
        public static void Postfix(Thing seer, Thing target, Func<IntVec3, bool> validator, ref bool __result)
        {
            if (!Stilts.AnyStilts || seer == null || target == null || (!Stilts.IsExtended(seer) && !Stilts.IsExtended(target)))
            {
                return;
            }
            if (CELine.Airborne(target))
            {
                return;
            }
            Map map = seer.Map;
            if (map == null || target.Map != map)
            {
                return;
            }
            __result = StiltsSightCE.CanSee(seer, target, validator, map);
        }
    }

    public static class StiltsSightCE
    {
        private const float DebugBlocked = 0f;
        private const float DebugClear = 0.7f;
        private const float DebugPath = 0.4f;

        [ThreadStatic]
        private static List<IntVec3> cellBuffer;
        [ThreadStatic]
        private static HashSet<IntVec3> seenBuffer;
        [ThreadStatic]
        private static bool buffersInUse;

        public static bool CanSee(Thing seer, Thing target, Func<IntVec3, bool> validator, Map map)
        {
            Vector3 seerPos = seer.DrawPos;
            Vector3 targetPos = target.DrawPos;
            Vector3 from = new Vector3(seerPos.x, new CollisionVertical(seer).shotHeight, seerPos.z);
            Vector3 to = new Vector3(targetPos.x, new CollisionVertical(target).Max, targetPos.z);

            bool nested = buffersInUse;
            List<IntVec3> cells;
            HashSet<IntVec3> seen;
            if (nested)
            {
                cells = new List<IntVec3>();
                seen = new HashSet<IntVec3>();
            }
            else
            {
                cells = cellBuffer ?? (cellBuffer = new List<IntVec3>());
                seen = seenBuffer ?? (seenBuffer = new HashSet<IntVec3>());
                cells.Clear();
                seen.Clear();
                buffersInUse = true;
            }
            try
            {
                foreach (IntVec3 cell in GenSightCE.PointsOnLineOfSight(from, to))
                {
                    IntVec3 flat = Line3D.Flat(cell);
                    if (seen.Add(flat))
                    {
                        cells.Add(flat);
                    }
                }
                IntVec3 end = Line3D.Flat(to.ToIntVec3());
                if (seen.Add(end))
                {
                    cells.Add(end);
                }
                return Walk(seer, target, validator, map, from, to, cells);
            }
            finally
            {
                if (!nested)
                {
                    buffersInUse = false;
                }
            }
        }

        private static bool Walk(Thing seer, Thing target, Func<IntVec3, bool> validator, Map map, Vector3 from, Vector3 to,
            List<IntVec3> cells)
        {
            CellRect seerRect = seer.OccupiedRect();
            CellRect targetRect = target.OccupiedRect();
            IntVec3 fromCell = Line3D.Flat(from.ToIntVec3());
            IntVec3 toCell = Line3D.Flat(to.ToIntVec3());

            Vector3 dir = to - from;
            float length = dir.magnitude;
            bool hasSegment = length > 1e-4f;
            Ray ray = hasSegment ? new Ray(from, dir) : default(Ray);
            bool debug = Line3D.DebugActive;
            Thing lastClear = null;

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(map))
                {
                    continue;
                }
                float status = DebugPath;
                bool blocked = false;
                if (CELine.RoofBlocks(from, to, cell, map))
                {
                    blocked = true;
                }
                else if (cell != fromCell && cell != toCell && !seerRect.Contains(cell) && !targetRect.Contains(cell))
                {
                    if (validator != null && !validator(cell))
                    {
                        blocked = true;
                    }
                    else if (hasSegment)
                    {
                        Thing edifice = cell.GetEdifice(map);
                        blocked = TestObstacle(edifice, seer, target, seerRect, targetRect, from, ray, length, ref lastClear, ref status);
                        if (!blocked)
                        {
                            Thing cover = cell.GetCover(map);
                            blocked = cover != edifice && TestObstacle(cover, seer, target, seerRect, targetRect, from, ray,
                                length, ref lastClear, ref status);
                        }
                    }
                }
                if (debug)
                {
                    Line3D.HeightRangeInCell(from, to, cell, out float minY, out _);
                    Line3D.Flash(map, cell, blocked ? DebugBlocked : status, minY);
                }
                if (blocked)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>True when the thing is an obstacle whose CE bounds the segment crosses.</summary>
        private static bool TestObstacle(Thing thing, Thing seer, Thing target, CellRect seerRect, CellRect targetRect,
            Vector3 from, Ray ray, float length, ref Thing lastClear, ref float status)
        {
            if (thing == null || thing == lastClear || thing is Pawn || thing == seer || thing == target
                || thing is Plant || thing.def.category == ThingCategory.Plant)
            {
                return false;
            }
            IntVec3 pos = thing.Position;
            if (seerRect.Contains(pos) || targetRect.Contains(pos))
            {
                return false;
            }
            Bounds bounds = CE_Utility.GetBoundsFor(thing);
            if (bounds.Contains(from) || (bounds.IntersectRay(ray, out float dist) && dist <= length))
            {
                return true;
            }
            // Multi-cell obstacles show up in several cells of the walk; test each once.
            lastClear = thing;
            status = DebugClear;
            return false;
        }
    }
}
