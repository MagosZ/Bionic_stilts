using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace StiltsBionics
{
    /// <summary>Vanilla (non-CE) sight: when the seer or the target stands on extended stilts, AttackTargetFinder.CanSee
    /// uses the 3D test from the seer's muzzle to the target's top. With CE the CE DLL does this with CE's heights.</summary>
    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.CanSee),
        new[] { typeof(Thing), typeof(Thing), typeof(Func<IntVec3, bool>) })]
    public static class VanillaPatch_CanSee
    {
        public static bool Prepare() => !StiltsMod.CEActive;

        public static void Postfix(Thing seer, Thing target, Func<IntVec3, bool> validator, ref bool __result)
        {
            if (!Stilts.AnyStilts || seer == null || target == null || (!Stilts.IsExtended(seer) && !Stilts.IsExtended(target)))
            {
                return;
            }
            Map map = seer.Map;
            if (map == null || target.Map != map || target is Skyfaller || target is Projectile)
            {
                return;
            }
            __result = VanillaSight.Clear(seer, seer.DrawPos, target, map, validator);
        }
    }

    /// <summary>Vanilla (non-CE) line of fire for direct-fire shooting verbs, when the shooter or the target stands on
    /// extended stilts: the 3D test from the shooter's cell (then each lean cell, as vanilla tries them) replaces the
    /// flat line of sight. Range, flying-overhead rounds, melee and open-ground verbs keep vanilla's answer.</summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryFindShootLineFromTo),
        new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(ShootLine), typeof(bool) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    public static class VanillaPatch_TryFindShootLineFromTo
    {
        [ThreadStatic]
        private static List<IntVec3> leanCells;

        public static bool Prepare() => !StiltsMod.CEActive;

        public static void Postfix(Verb __instance, IntVec3 __0, LocalTargetInfo __1, ref ShootLine __2, bool __3,
            ref bool __result)
        {
            if (!Stilts.AnyStilts)
            {
                return;
            }
            Thing caster = __instance.caster;
            Thing targetThing = __1.Thing;
            if (caster == null || (!Stilts.IsExtended(caster) && !Stilts.IsExtended(targetThing)))
            {
                return;
            }
            Map map = caster.Map;
            if (map == null || (targetThing != null && targetThing.Map != map))
            {
                return;
            }
            if (!(__instance is Verb_LaunchProjectile launch) || !__instance.verbProps.requireLineOfSight
                || __instance.verbProps.mustCastOnOpenGround)
            {
                return;
            }
            ThingDef projectile = launch.Projectile;
            if (projectile?.projectile == null || projectile.projectile.flyOverhead
                || __instance.EffectiveRange <= ShootTuning.MeleeRange)
            {
                return;
            }
            if (!__3 && OutOfRange(__instance, __0, __1))
            {
                return;
            }
            IntVec3 root = __0;
            IntVec3 targetCell = __1.Cell;
            Vector3 rootPos = root.ToVector3Shifted();
            if (VanillaSight.Clear(caster, rootPos, __1, map, null))
            {
                __2 = new ShootLine(root, targetCell);
                __result = true;
                return;
            }
            List<IntVec3> leans = leanCells ?? (leanCells = new List<IntVec3>());
            leans.Clear();
            ShootLeanUtility.LeanShootingSourcesFromTo(root, targetCell, map, leans);
            foreach (IntVec3 lean in leans)
            {
                // Leaning puts the muzzle half way to the lean cell.
                Vector3 leanPos = Vector3.Lerp(rootPos, lean.ToVector3Shifted(), 0.5f);
                if (VanillaSight.Clear(caster, leanPos, __1, map, null))
                {
                    __2 = new ShootLine(lean, targetCell);
                    __result = true;
                    return;
                }
            }
            __2 = new ShootLine(root, targetCell);
            __result = false;
        }

        private static bool OutOfRange(Verb verb, IntVec3 root, LocalTargetInfo target)
        {
            CellRect rect = target.HasThing ? target.Thing.OccupiedRect() : CellRect.SingleCell(target.Cell);
            IntVec3 closest = rect.ClosestCellTo(root);
            float dist = (closest - root).LengthHorizontal;
            float max = verb.EffectiveRange;
            return dist > max || dist < verb.verbProps.minRange;
        }
    }

    /// <summary>Vanilla (non-CE) rounds in flight: vanilla stops a round at the first wall in its path. A round fired by
    /// or at an extended stilt pawn flies the 3D line from the shooter's muzzle to the target's top, and passes over
    /// any cell where that line is above everything standing in the cell.</summary>
    [HarmonyPatch]
    public static class VanillaPatch_CheckForFreeIntercept
    {
        private static readonly MethodInfo Target = AccessTools.Method(typeof(Projectile), "CheckForFreeIntercept",
            new[] { typeof(IntVec3) });

        private static readonly AccessTools.FieldRef<Projectile, Vector3> Origin = FieldRef<Vector3>("origin");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> Destination = FieldRef<Vector3>("destination");
        private static readonly AccessTools.FieldRef<Projectile, LocalTargetInfo> IntendedTarget =
            FieldRef<LocalTargetInfo>("intendedTarget");

        private static AccessTools.FieldRef<Projectile, T> FieldRef<T>(string name)
        {
            FieldInfo field = AccessTools.Field(typeof(Projectile), name);
            if (field == null || field.IsStatic || field.FieldType != typeof(T))
            {
                return null;
            }
            try
            {
                return AccessTools.FieldRefAccess<Projectile, T>(field);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool Prepare()
        {
            if (StiltsMod.CEActive)
            {
                return false;
            }
            if (Target == null || Origin == null || Destination == null || IntendedTarget == null)
            {
                StiltsLog.WarnOnce("CheckForFreeIntercept",
                    "Projectile.CheckForFreeIntercept or its fields not found; rounds fired over walls by stilt pawns may hit the wall.");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => Target;

        public static bool Prefix(Projectile __instance, IntVec3 __0, ref bool __result)
        {
            IntVec3 c = __0;
            if (!Stilts.AnyStilts)
            {
                return true;
            }
            Thing launcher = __instance.Launcher;
            LocalTargetInfo intended = IntendedTarget(__instance);
            Thing intendedThing = intended.Thing;
            if (!Stilts.IsExtended(launcher) && !Stilts.IsExtended(intendedThing))
            {
                return true;
            }
            Map map = __instance.Map;
            if (map == null || !c.InBounds(map) || (intendedThing != null && intendedThing.OccupiedRect().Contains(c)))
            {
                return true;
            }
            Vector3 origin = Origin(__instance);
            Vector3 destination = Destination(__instance);
            float fromY = launcher != null ? VanillaHeights.MuzzleOf(launcher) : VanillaHeights.GunHeightFactor;
            float toY = intendedThing != null ? VanillaHeights.TopOf(intendedThing) : 0f;
            Vector3 from = new Vector3(origin.x, fromY, origin.z);
            Vector3 to = new Vector3(destination.x, toY, destination.z);
            Line3D.HeightRangeInCell(from, to, c, out float minY, out _);
            if (minY < HighestIn(c, map, launcher))
            {
                return true;
            }
            // Above everything in this cell: nothing here can stop the round.
            __result = false;
            return false;
        }

        private static float HighestIn(IntVec3 cell, Map map, Thing launcher)
        {
            float highest = 0f;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == launcher || (!(thing is Pawn) && thing.def.Fillage == FillCategory.None))
                {
                    continue;
                }
                highest = Mathf.Max(highest, VanillaHeights.TopOf(thing));
            }
            return highest;
        }
    }
}
