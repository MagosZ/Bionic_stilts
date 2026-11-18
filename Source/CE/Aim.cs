using System;
using System.Collections.Generic;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace StiltsBionics.CE
{
    /// <summary>CE's aim height, three changes:
    /// 1. An extended shooter looks down over partial cover, so CE does not lower its aim window to the part of the
    ///    target above the cover (prefix drops the partial cover).
    /// 2. Shots at a pawn on extended stilts aim at its lifted body: CE measures aim heights as fractions of the top
    ///    only (it assumes a body standing on the ground), which would put them among the poles.
    /// 3. Elevated Gun Platform's aim raise (DESIGN 3.2, ported): a line from a high muzzle can clear a wall partway
    ///    along the shot by the target's top but not by its centre, so the aim is raised to the lowest height that
    ///    clears every full-height obstacle and roof slab on the line. Used when either end is on extended stilts.</summary>
    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.GetTargetHeight))]
    public static class AimPatch_GetTargetHeight
    {
        private const float Margin = 0.05f;

        // protected Vector2 sourceLoc, newTargetLoc: the shot's start and aim point on the map plane. ShiftTarget, the
        // only caller of GetTargetHeight, sets both just before the call, then aims from sourceLoc at ShotHeight to
        // newTargetLoc at the returned height.
        private static readonly AccessTools.FieldRef<Verb_LaunchProjectileCE, Vector2> SourceLoc = ResolveLoc("sourceLoc");
        private static readonly AccessTools.FieldRef<Verb_LaunchProjectileCE, Vector2> NewTargetLoc = ResolveLoc("newTargetLoc");

        [ThreadStatic]
        private static HashSet<IntVec3> seen;

        private static AccessTools.FieldRef<Verb_LaunchProjectileCE, Vector2> ResolveLoc(string name)
        {
            FieldInfo field = AccessTools.Field(typeof(Verb_LaunchProjectileCE), name);
            if (field == null || field.IsStatic || field.FieldType != typeof(Vector2))
            {
                return null;
            }
            try
            {
                return AccessTools.FieldRefAccess<Verb_LaunchProjectileCE, Vector2>(field);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Prefix(Verb_LaunchProjectileCE __instance, ref Thing cover)
        {
            if (cover == null || !Stilts.AnyStilts || cover is Pawn || cover.def.Fillage == FillCategory.Full)
            {
                return;
            }
            if (Stilts.IsExtended(__instance.caster))
            {
                cover = null;
            }
        }

        public static void Postfix(Verb_LaunchProjectileCE __instance, LocalTargetInfo target, ref float __result)
        {
            if (!Stilts.AnyStilts || !target.HasThing)
            {
                return;
            }
            if (__instance.projectilePropsCE != null && __instance.projectilePropsCE.flyOverhead)
            {
                return;
            }
            Thing caster = __instance.caster;
            Thing targetThing = target.Thing;
            bool shooterUp = Stilts.IsExtended(caster);
            bool targetUp = Stilts.IsExtended(targetThing);
            if (!shooterUp && !targetUp)
            {
                return;
            }
            if (targetUp)
            {
                // CE's window is in fractions of the top; put the same fractions on the lifted body.
                var vert = new CollisionVertical(targetThing);
                float top = vert.Max;
                if (top > 0f)
                {
                    __result = vert.Min + Mathf.Clamp01(__result / top) * (top - vert.Min);
                }
            }
            if (SourceLoc != null && NewTargetLoc != null)
            {
                RaiseOverObstacles(__instance, caster, targetThing, ref __result);
            }
        }

        private static void RaiseOverObstacles(Verb_LaunchProjectileCE verb, Thing caster, Thing targetThing, ref float aim)
        {
            Map map = caster?.Map;
            if (map == null || targetThing.Map != map)
            {
                return;
            }
            // CE's shot: from source at height m, through its aim point (aimDistance along dir) at height aim.
            Vector2 source = SourceLoc(verb);
            Vector2 chord = NewTargetLoc(verb) - source;
            float aimDistance = chord.magnitude;
            if (aimDistance < 1f)
            {
                return;
            }
            Vector2 dir = chord / aimDistance;
            Vector3 targetPos = targetThing.DrawPos;
            // Where the shot reaches the target.
            float targetDistance = (targetPos.x - source.x) * dir.x + (targetPos.z - source.y) * dir.y;
            if (targetDistance < 1f)
            {
                return;
            }
            float m = verb.ShotHeight;
            float top = new CollisionVertical(targetThing).Max;

            // Heights from here on are the shot's height where it reaches the target.
            float needed = LowestClearHeight(new Vector3(source.x, m, source.y), dir, targetDistance, map, caster, targetThing);
            float current = m + (aim - m) * targetDistance / aimDistance;
            if (needed <= current || needed >= top)
            {
                // Already clear, or the target is not visible over the obstacle (line of fire handles that).
                return;
            }
            // Clear by the margin, but never closer to the target's top than to the obstacle line.
            float wanted = Mathf.Min(needed + Margin, (needed + top) * 0.5f);
            // Back to a height at CE's aim point; never lower than CE's own aim.
            aim = Mathf.Max(aim, m + (wanted - m) * aimDistance / targetDistance);
        }

        /// <summary>Lowest height at the target (targetDistance along dir from the muzzle) of a line from the muzzle
        /// that stays above every full-height obstacle and roof slab the shot crosses before the target. Obstacles
        /// taller than the muzzle are ignored when the shot climbs (line of fire already rejects shots through them).</summary>
        private static float LowestClearHeight(Vector3 from, Vector2 dir, float targetDistance, Map map, Thing caster,
            Thing targetThing)
        {
            float m = from.y;
            Vector3 to = new Vector3(from.x + dir.x * targetDistance, m, from.z + dir.y * targetDistance);
            CellRect casterRect = caster.OccupiedRect();
            CellRect targetRect = targetThing.OccupiedRect();
            HashSet<IntVec3> cells = seen ?? (seen = new HashSet<IntVec3>());
            cells.Clear();

            float needed = float.NegativeInfinity;
            foreach (IntVec3 walked in GenSightCE.PointsOnLineOfSight(from, to))
            {
                IntVec3 cell = Line3D.Flat(walked);
                if (!cells.Add(cell) || !cell.InBounds(map) || casterRect.Contains(cell) || targetRect.Contains(cell))
                {
                    continue;
                }
                // The obstacle's far edge on the line: where the shot leaves the cell.
                if (!Line3D.CrossingInCell(from, to, cell, out _, out float leave))
                {
                    continue;
                }
                float farEdge = leave * targetDistance;
                if (farEdge <= 0f)
                {
                    continue;
                }
                float highest = Mathf.Max(ObstacleTop(cell, map, caster, targetThing), CELine.RoofTop(cell, map));
                if (highest <= 0f)
                {
                    continue;
                }
                // An obstacle above the muzzle only matters for a shot climbing to a taller target (one on stilts),
                // which can still pass it near the target.
                if (highest >= m && TopOf(targetThing) <= m)
                {
                    continue;
                }
                needed = Mathf.Max(needed, m - (m - highest) * targetDistance / farEdge);
            }
            cells.Clear();
            return needed;
        }

        private static float TopOf(Thing thing) => new CollisionVertical(thing).Max;

        /// <summary>Top of the full-height building in a cell (walls, closed doors, rock), or 0.</summary>
        private static float ObstacleTop(IntVec3 cell, Map map, Thing caster, Thing targetThing)
        {
            Building edifice = cell.GetEdifice(map);
            if (edifice == null || edifice == caster || edifice == targetThing || edifice.def.Fillage != FillCategory.Full)
            {
                return 0f;
            }
            return CE_Utility.GetBoundsFor(edifice).max.y;
        }
    }
}
