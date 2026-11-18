using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CombatExtended;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace StiltsBionics.CE
{
    /// <summary>Roof rule for rounds fired from extended stilts (Elevated Gun Platform P2, ported): CE's early-out in
    /// CheckCellForCollision (cover lower than the round) skips the roof check for rounds above the cover height, so
    /// a roofed cell is tested against the roof first; a hit ends the cell check. It also marks the cell scan for the
    /// suppression postfix (Suppression.cs).</summary>
    [HarmonyPatch]
    public static class ProjectilePatch_CheckCellForCollision
    {
        internal static readonly MethodInfo Target = ResolveTarget();

        // protected virtual bool TryCollideWithRoof(IntVec3 cell), called virtually (SOS2 overrides it).
        private static readonly Func<ProjectileCE, IntVec3, bool> TryCollideWithRoof = ResolveTryCollideWithRoof();

        private static MethodInfo ResolveTarget()
        {
            MethodInfo method = AccessTools.Method(typeof(ProjectileCE), "CheckCellForCollision", new[] { typeof(IntVec3) });
            return method != null && !method.IsStatic && method.ReturnType == typeof(bool) ? method : null;
        }

        private static Func<ProjectileCE, IntVec3, bool> ResolveTryCollideWithRoof()
        {
            MethodInfo method = AccessTools.Method(typeof(ProjectileCE), "TryCollideWithRoof", new[] { typeof(IntVec3) });
            if (method == null || method.IsStatic || method.ReturnType != typeof(bool))
            {
                return null;
            }
            try
            {
                return AccessTools.MethodDelegate<Func<ProjectileCE, IntVec3, bool>>(method);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool Prepare()
        {
            if (Target == null)
            {
                Log.Warning("[Stilts Bionics CE] ProjectileCE.CheckCellForCollision not found; rounds fired from stilts can fly through roofs and do not suppress above height 3.");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => Target;

        public static bool Prefix(ProjectileCE __instance, IntVec3 cell, ref bool __result)
        {
            if (!Stilts.AnyStilts || !CELine.FiredFromStilts(__instance))
            {
                return true;
            }
            StiltsSuppression.MarkCellScan(__instance);
            if (TryCollideWithRoof == null)
            {
                return true;
            }
            IntVec3 flat = Line3D.Flat(cell);
            if (!CELine.Roofed(flat, __instance.Map))
            {
                return true;
            }
            if (TryCollideWithRoof(__instance, flat))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    /// <summary>What a round can hit, three rules:
    /// - rounds fired from extended stilts never hit a pawn standing in a roofed cell (their line of fire never reaches
    ///   under a roof; Elevated Gun Platform P3);
    /// - rounds fired from extended stilts pass through partial cover (sandbags, barricades) that is not their target:
    ///   the shooter looks down over it;
    /// - below the lifted body of a pawn on extended stilts there are only two thin poles: a round crossing that zone
    ///   hits a pole with the pole-hit chance (mod setting, 15%), rolled once per round and pawn; a hit there damages
    ///   the legs, which are the stilts (Heights.cs).</summary>
    [HarmonyPatch]
    public static class ProjectilePatch_CanCollideWith
    {
        private static readonly MethodInfo Target = AccessTools.Method(typeof(ProjectileCE), "CanCollideWith",
            new[] { typeof(Thing), typeof(float).MakeByRefType() });

        private static readonly ConditionalWeakTable<ProjectileCE, HashSet<int>> rolled =
            new ConditionalWeakTable<ProjectileCE, HashSet<int>>();

        public static bool Prepare()
        {
            if (Target == null)
            {
                Log.Warning("[Stilts Bionics CE] ProjectileCE.CanCollideWith not found; stilt poles can't be hit and rounds from stilts hit partial cover.");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => Target;

        public static void Postfix(ProjectileCE __instance, Thing thing, ref float dist, ref bool __result)
        {
            if (!Stilts.AnyStilts || thing == null)
            {
                return;
            }
            if (__result)
            {
                if (CELine.FiredFromStilts(__instance))
                {
                    if (thing is Pawn pawn)
                    {
                        if (CELine.Roofed(pawn.Position, pawn.Map))
                        {
                            __result = false;
                        }
                    }
                    else if (thing.def.Fillage == FillCategory.Partial && !thing.IsPlant()
                        && thing != __instance.intendedTarget.Thing)
                    {
                        __result = false;
                    }
                }
                return;
            }
            if (thing is Pawn target && thing != __instance.launcher && Stilts.IsExtended(target) && HitsPole(__instance, target, out float d))
            {
                __result = true;
                dist = d;
            }
        }

        private static bool HitsPole(ProjectileCE projectile, Pawn pawn, out float dist)
        {
            dist = 0f;
            float chance = StiltsMod.Settings?.poleHitChance ?? 0.15f;
            if (chance <= 0f)
            {
                return false;
            }
            float length = (projectile.ExactPosition - projectile.LastPos).magnitude;
            if (length < 1e-4f)
            {
                return false;
            }
            float lift = new CollisionVertical(pawn).Min;
            if (lift <= 0f)
            {
                return false;
            }
            float width = CE_Utility.GetCollisionBodyFactors(pawn).x;
            Vector3 pos = pawn.DrawPos;
            var poles = new Bounds(new Vector3(pos.x, lift * 0.5f, pos.z), new Vector3(width, lift, width));
            // The same ray CE measures its own collision distances on (TryCollideWith puts the impact there).
            if (!poles.IntersectRay(projectile.ShotLine, out float d) || d > length)
            {
                return false;
            }
            HashSet<int> done = rolled.GetOrCreateValue(projectile);
            if (!done.Add(pawn.thingIDNumber) || !Rand.Chance(chance))
            {
                return false;
            }
            dist = Mathf.Max(0f, d);
            return true;
        }
    }
}
