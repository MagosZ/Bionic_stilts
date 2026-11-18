using System;
using System.Linq;
using System.Reflection;
using CombatExtended;
using CombatExtended.Utilities;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace StiltsBionics.CE
{
    /// <summary>Suppression by rounds fired from extended stilts (Elevated Gun Platform DESIGN 3.4, ported): CE's
    /// RayCastSuppression loop, which also skips a pawn standing in a roofed cell when the round passes it at or above
    /// the roof bottom, and the same suppression for segments CE skips because the round is above its height gate (3):
    /// a round leaves a 3.28 muzzle above that gate.</summary>
    public static class StiltsSuppression
    {
        // ProjectileCE.SuppressionRadius (protected const): the suppression corridor and CE's height gate.
        private const int SuppressionRadius = 3;

        // protected void ApplySuppression(Pawn pawn, float suppressionMultiplier = 1f)
        private static readonly Action<ProjectileCE, Pawn, float> ApplySuppression = ResolveApplySuppression();

        // The last CheckForCollisionBetween cell scan by a round fired from stilts: its id and flight tick.
        private static int scanId = int.MinValue;
        private static int scanTick;

        public static bool Ready => ApplySuppression != null;

        private static Action<ProjectileCE, Pawn, float> ResolveApplySuppression()
        {
            MethodInfo method = AccessTools.Method(typeof(ProjectileCE), "ApplySuppression", new[] { typeof(Pawn), typeof(float) });
            if (method == null || method.IsStatic || method.ReturnType != typeof(void))
            {
                return null;
            }
            try
            {
                return AccessTools.MethodDelegate<Action<ProjectileCE, Pawn, float>>(method);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static void MarkCellScan(ProjectileCE projectile)
        {
            scanId = projectile.thingIDNumber;
            scanTick = projectile.FlightTicks;
        }

        private static bool TakeCellScanMark(ProjectileCE projectile)
        {
            if (scanId != projectile.thingIDNumber)
            {
                return false;
            }
            scanId = int.MinValue;
            return scanTick == projectile.FlightTicks;
        }

        /// <summary>CE's RayCastSuppression, with the roof rule.</summary>
        internal static void Suppress(ProjectileCE projectile, IntVec3 muzzle, IntVec3 destination, Map map)
        {
            if (muzzle == destination)
            {
                return;
            }
            var props = projectile.def.projectile as ProjectilePropertiesCE;
            if (props == null || props.suppressionFactor <= 0f || props.airborneSuppressionFactor <= 0f)
            {
                return;
            }
            map ??= projectile.Map;
            if (map == null)
            {
                return;
            }
            Vector3 from, to;
            if (props.isInstant)
            {
                InstantBeam(projectile, destination, out from, out to);
            }
            else
            {
                from = projectile.LastPos;
                to = projectile.ExactPosition;
            }
            foreach (Pawn pawn in muzzle.PawnsNearSegment(destination, map, SuppressionRadius, false, true)
                .Except(muzzle.PawnsInRange(map, SuppressionRadius)))
            {
                if (pawn == null || RoundAboveRoofOver(pawn, from, to, map))
                {
                    continue;
                }
                ApplySuppression(projectile, pawn, 1f);
            }
        }

        /// <summary>CheckForCollisionBetween postfix: CE suppresses only when the round ends the segment at or below
        /// height 3. Above that, the same suppression runs here, so a segment is never suppressed twice.</summary>
        internal static void AfterCollisionCheck(ProjectileCE projectile)
        {
            if (!TakeCellScanMark(projectile) || projectile.Destroyed)
            {
                return;
            }
            Map map = projectile.Map;
            Vector3 exact = projectile.ExactPosition;
            if (map == null || !(exact.y > SuppressionRadius))
            {
                return;
            }
            Suppress(projectile, Line3D.Flat(projectile.LastPos.ToIntVec3()), Line3D.Flat(exact.ToIntVec3()), map);
        }

        private static bool RoundAboveRoofOver(Pawn pawn, Vector3 from, Vector3 to, Map map)
        {
            return CELine.Roofed(pawn.Position, map)
                && Line3D.HeightNear(from, to, pawn.DrawPos) >= CollisionVertical.WallCollisionHeight;
        }

        /// <summary>An instant (RayCast) round's beam, from its start to the destination cell.</summary>
        private static void InstantBeam(ProjectileCE projectile, IntVec3 destination, out Vector3 start, out Vector3 end)
        {
            start = new Vector3(projectile.origin.x, projectile.shotHeight, projectile.origin.y);
            float dirX = Mathf.Cos(projectile.shotRotation);
            float dirZ = Mathf.Sin(projectile.shotRotation);
            Vector3 dest = destination.ToVector3Shifted();
            float length = Mathf.Max(0f, (dest.x - start.x) * dirX + (dest.z - start.z) * dirZ);
            end = new Vector3(start.x + dirX * length, start.y + Mathf.Tan(projectile.shotAngle) * length, start.z + dirZ * length);
        }
    }

    [HarmonyPatch]
    public static class SuppressionPatch_RayCastSuppression
    {
        private static readonly MethodInfo Target = ResolveTarget();

        private static MethodInfo ResolveTarget()
        {
            MethodInfo method = AccessTools.Method(typeof(ProjectileCE), "RayCastSuppression",
                new[] { typeof(IntVec3), typeof(IntVec3), typeof(Map) });
            return method != null && !method.IsStatic ? method : null;
        }

        public static bool Prepare()
        {
            if (Target == null || !StiltsSuppression.Ready)
            {
                Log.Warning("[Stilts Bionics CE] ProjectileCE.RayCastSuppression or ApplySuppression not found; rounds fired from stilts suppress pawns under roofs.");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => Target;

        public static bool Prefix(ProjectileCE __instance, IntVec3 muzzle, IntVec3 destination, Map map)
        {
            if (!Stilts.AnyStilts || !CELine.FiredFromStilts(__instance) || !(__instance.def.projectile is ProjectilePropertiesCE))
            {
                return true;
            }
            StiltsSuppression.Suppress(__instance, muzzle, destination, map);
            return false;
        }
    }

    [HarmonyPatch]
    public static class SuppressionPatch_CheckForCollisionBetween
    {
        private static readonly MethodInfo Target = ResolveTarget();

        private static MethodInfo ResolveTarget()
        {
            MethodInfo method = AccessTools.Method(typeof(ProjectileCE), "CheckForCollisionBetween", Type.EmptyTypes);
            return method != null && !method.IsStatic && method.ReturnType == typeof(bool) ? method : null;
        }

        public static bool Prepare()
        {
            if (Target == null || !StiltsSuppression.Ready || ProjectilePatch_CheckCellForCollision.Target == null)
            {
                Log.Warning("[Stilts Bionics CE] ProjectileCE.CheckForCollisionBetween, CheckCellForCollision or ApplySuppression not found; rounds fired from stilts do not suppress above height 3.");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => Target;

        public static void Postfix(ProjectileCE __instance)
        {
            StiltsSuppression.AfterCollisionCheck(__instance);
        }
    }
}
