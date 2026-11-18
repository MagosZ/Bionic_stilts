using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StiltsBionics.CE
{
    /// <summary>A pawn on extended stilts: its body (CE's own body height, 1.0 for a human) is lifted by the stilts'
    /// lift, so it spans (2.43, 3.43) and shoots from 2.43 + 0.85 = 3.28. Below the body there is no solid hitbox,
    /// only the poles (Projectiles.cs). Every CE height consumer (bounds, cover heights, shot height, aim) builds a
    /// CollisionVertical, so this one postfix covers them all; it also overrides CE's 2.0 cap on human height.</summary>
    [HarmonyPatch]
    public static class HeightsPatch_CalculateHeightRange
    {
        private static readonly MethodInfo Target = AccessTools.Method(typeof(CollisionVertical), "CalculateHeightRange",
            new[] { typeof(Thing), typeof(FloatRange).MakeByRefType(), typeof(float).MakeByRefType() });

        public static bool Prepare()
        {
            if (Target == null)
            {
                Log.Warning("[Stilts Bionics CE] CollisionVertical.CalculateHeightRange not found; extended stilts keep a normal height under CE.");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => Target;

        public static void Postfix(Thing thing, ref FloatRange heightRange, ref float shotHeight)
        {
            if (!Stilts.AnyStilts || !(thing is Pawn pawn) || !Stilts.IsExtended(pawn))
            {
                return;
            }
            float body = CE_Utility.GetCollisionBodyFactors(pawn).y;
            float lift = Stilts.Props.lift;
            heightRange = new FloatRange(lift, lift + body);
            shotHeight = lift + body * CollisionVertical.BodyRegionMiddleHeight;
        }
    }

    /// <summary>Which body region a bullet hits on a lifted body: CE reads the height against the top only (it assumes
    /// a body starting at the ground). For a pawn on extended stilts the regions are measured on the lifted body, and
    /// anything below it (a pole hit) is the legs, which are the stilts.</summary>
    [HarmonyPatch(typeof(BulletCE), nameof(BulletCE.Impact))]
    public static class BodyRegionPatch_Impact
    {
        [System.ThreadStatic]
        internal static Pawn current;

        public static void Prefix(Thing hitThing, out Pawn __state)
        {
            __state = current;
            current = Stilts.AnyStilts && hitThing is Pawn pawn && Stilts.IsExtended(pawn) ? pawn : null;
        }

        public static void Finalizer(Pawn __state)
        {
            current = __state;
        }
    }

    [HarmonyPatch(typeof(CollisionVertical), nameof(CollisionVertical.GetCollisionBodyHeight))]
    public static class BodyRegionPatch_GetCollisionBodyHeight
    {
        public static void Postfix(ref CollisionVertical __instance, float projectileHeight, ref BodyPartHeight __result)
        {
            if (BodyRegionPatch_Impact.current == null)
            {
                return;
            }
            float bottom = __instance.Min;
            float body = __instance.Max - bottom;
            if (body <= 0f)
            {
                return;
            }
            float rel = (projectileHeight - bottom) / body;
            if (rel < CollisionVertical.BodyRegionBottomHeight)
            {
                __result = BodyPartHeight.Bottom;
            }
            else if (rel < CollisionVertical.BodyRegionMiddleHeight)
            {
                __result = BodyPartHeight.Middle;
            }
            else
            {
                __result = BodyPartHeight.Top;
            }
        }
    }
}
