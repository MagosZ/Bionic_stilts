using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace StiltsBionics.CE
{
    /// <summary>CE's line-of-fire check with real heights when the shooter or the target stands on extended stilts
    /// (Elevated Gun Platform DESIGN 3.2, ported). A copy of CE's CanHitCellFromCellIgnoringRange (same rules, same
    /// order) with: walk cells flattened to y = 0; CE's cover shortcut compares against the line's min height in each
    /// cell instead of the shooter's height (CE alone would let a 3.28 muzzle fire past every wall, into the wall by
    /// the target); roofed cells block where the line is below the slab; a friendly pawn holds fire only when the line
    /// crosses it; an extended shooter ignores partial cover (it looks down over it).</summary>
    [HarmonyPatch]
    public static class LineOfFirePatch_CanHitCellFromCellIgnoringRange
    {
        private static readonly MethodInfo Target = AccessTools.Method(typeof(Verb_LaunchProjectileCE),
            "CanHitCellFromCellIgnoringRange", new[] { typeof(Vector3), typeof(IntVec3), typeof(Thing) });

        public static bool Prepare()
        {
            if (Target == null)
            {
                Log.Warning("[Stilts Bionics CE] Verb_LaunchProjectileCE.CanHitCellFromCellIgnoringRange not found; stilt pawns use CE's plain line of fire.");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => Target;

        public static bool Prefix(Verb_LaunchProjectileCE __instance, Vector3 shotSource, IntVec3 targetLoc,
            Thing targetThing, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal)
            {
                // Another mod (Elevated Gun Platform) already answered with the same 3D logic.
                return false;
            }
            if (!Stilts.AnyStilts)
            {
                return true;
            }
            Thing caster = __instance.caster;
            bool shooterUp = Stilts.IsExtended(caster);
            if (!shooterUp && !Stilts.IsExtended(targetThing))
            {
                return true;
            }
            if (CELine.Airborne(targetThing))
            {
                return true;
            }
            Map map = caster?.Map;
            LightingTracker tracker = map?.GetLightingTracker();
            if (tracker == null)
            {
                return true;
            }
            __result = CanHitCellFromCell(__instance, caster, shooterUp, map, tracker, shotSource, targetLoc, targetThing);
            return false;
        }

        private static bool CanHitCellFromCell(Verb_LaunchProjectileCE verb, Thing caster, bool shooterUp, Map map,
            LightingTracker tracker, Vector3 shotSource, IntVec3 targetLoc, Thing targetThing)
        {
            VerbProperties verbProps = verb.verbProps;
            // Vanilla checks
            if (verbProps.mustCastOnOpenGround && (!targetLoc.Standable(map) || map.thingGrid.CellContains(targetLoc, ThingCategory.Pawn)))
            {
                return false;
            }
            if (!verbProps.requireLineOfSight)
            {
                return true;
            }

            // Shot vector, as CE builds it
            Vector3 targetPos;
            if (targetThing != null)
            {
                float shotHeight = shotSource.y;
                verb.AdjustShotHeight(caster, targetThing, ref shotHeight);
                shotSource.y = shotHeight;
                Vector3 targDrawPos = targetThing.DrawPos;
                targetPos = new Vector3(targDrawPos.x, new CollisionVertical(targetThing).Max, targDrawPos.z);
                if (targetThing is Pawn targPawn)
                {
                    targetPos += targPawn.Drawer.leaner.LeanOffset * 0.6f;
                }
            }
            else
            {
                targetPos = targetLoc.ToVector3Shifted();
            }

            bool ceDebug = Controller.settings != null && Controller.settings.DebugDrawPartialLoSChecks;
            bool ownDebug = Line3D.DebugActive;
            VerbPropertiesCE verbPropsCE = verb.VerbPropsCE;
            var check = new CellCheck
            {
                map = map,
                tracker = tracker,
                caster = caster,
                shooterUp = shooterUp,
                shooterPawn = verb.ShooterPawn,
                targetThing = targetThing,
                shotSource = shotSource,
                shotLine = new Ray(shotSource, targetPos - shotSource),
                segmentLength = (targetPos - shotSource).magnitude,
                sourceCell = Line3D.Flat(shotSource.ToIntVec3()),
                targetCell = Line3D.Flat(targetLoc),
                ignorePartialLoSBlocker = verbPropsCE != null && verbPropsCE.ignorePartialLoSBlocker,
                aimMode = verb.CompFireModes?.CurrentAimMode,
                ceDebug = ceDebug,
            };

            bool sawTarget = false;
            foreach (IntVec3 curCell in GenSightCE.PointsOnLineOfSight(shotSource, targetLoc.ToVector3Shifted()))
            {
                IntVec3 cell = Line3D.Flat(curCell);
                if (ceDebug)
                {
                    map.debugDrawer.FlashCell(cell, 0.4f);
                }
                Line3D.HeightRangeInCell(shotSource, targetPos, cell, out float minY, out _);
                bool isEnd = cell == check.sourceCell || cell == check.targetCell;
                if (cell == check.targetCell)
                {
                    sawTarget = true;
                }
                // Roofs block on every cell, the shooter's and the target's included.
                bool blocked = CELine.RoofBlocks(shotSource, targetPos, cell, map)
                    || (!isEnd && !check.CanShootThroughCell(cell, minY));
                if (ownDebug)
                {
                    Line3D.Flash(map, cell, blocked ? 0f : 0.7f, minY);
                }
                if (blocked)
                {
                    return false;
                }
            }
            if (!sawTarget && CELine.RoofBlocks(shotSource, targetPos, check.targetCell, map))
            {
                if (ownDebug)
                {
                    Line3D.HeightRangeInCell(shotSource, targetPos, check.targetCell, out float minY, out _);
                    Line3D.Flash(map, check.targetCell, 0f, minY);
                }
                return false;
            }
            return true;
        }

        /// <summary>CE's per-cell CanShootThroughCell closure as a struct (no allocation per call).</summary>
        private struct CellCheck
        {
            public Map map;
            public LightingTracker tracker;
            public Thing caster;
            public bool shooterUp;
            public Pawn shooterPawn;
            public Thing targetThing;
            public Vector3 shotSource;
            public Ray shotLine;
            public float segmentLength;
            public IntVec3 sourceCell;
            public IntVec3 targetCell;
            public bool ignorePartialLoSBlocker;
            public AimMode? aimMode;
            public bool ceDebug;

            public bool CanShootThroughCell(IntVec3 cell, float minY)
            {
                float ch = tracker.HighestCoverAt(cell);
                if (ch < minY)
                {
                    return true;
                }
                Thing cover = cell.GetFirstPawn(map) ?? cell.GetCover(map);

                if (cover != null && cover != shooterPawn && cover != caster && cover != targetThing && !cover.IsPlant() && !(cover is Pawn && cover.HostileTo(caster)))
                {
                    // CE holds fire for non-adjacent friendlies and allies; here only when the line crosses their body.
                    if (cover is Pawn { Downed: false, Faction: not null } cellPawn && shooterPawn?.Faction != null
                        && (shooterPawn.Faction == cellPawn.Faction || shooterPawn.Faction.RelationKindWith(cellPawn.Faction) == FactionRelationKind.Ally)
                        && !cellPawn.AdjacentTo8WayOrInside(caster)
                        && CrossesSegment(CE_Utility.GetBoundsFor(cellPawn)))
                    {
                        return false;
                    }

                    // An extended shooter looks down over partial cover; suppressive fire and verbs that ignore
                    // partial blockers skip it too.
                    if ((shooterUp || ignorePartialLoSBlocker || aimMode == AimMode.SuppressFire)
                        && !(cover is Pawn) && cover.def.Fillage != FillCategory.Full)
                    {
                        return true;
                    }

                    Bounds bounds = CE_Utility.GetBoundsFor(cover);

                    // CE's simplified rule for cover adjacent to the shooter
                    if (cover.def.Fillage != FillCategory.Full && cover.AdjacentTo8WayOrInside(caster))
                    {
                        // Sanity check to prevent stuff behind us blocking LoS
                        float cellTargDist = cell.DistanceTo(targetCell);
                        float shotTargDist = sourceCell.DistanceTo(targetCell);

                        if (shotTargDist > cellTargDist)
                        {
                            return cover is Pawn || bounds.size.y < shotSource.y;
                        }
                    }

                    if (bounds.IntersectRay(shotLine))
                    {
                        if (ceDebug)
                        {
                            map.debugDrawer.FlashCell(cell, 0, bounds.extents.y.ToString());
                        }
                        return false;
                    }

                    if (ceDebug)
                    {
                        map.debugDrawer.FlashCell(cell, 0.7f, bounds.extents.y.ToString());
                    }
                }

                return true;
            }

            // The ray hits the bounds between shot source and target (a negative distance means the source is inside).
            private bool CrossesSegment(Bounds bounds)
            {
                return bounds.IntersectRay(shotLine, out float distance) && distance <= segmentLength;
            }
        }
    }
}
