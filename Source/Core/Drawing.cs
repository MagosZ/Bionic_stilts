using HarmonyLib;
using UnityEngine;
using Verse;

namespace StiltsBionics
{
    /// <summary>A pawn on stilts is drawn lifted up the screen (like a tree, height goes north on the map), with two
    /// telescopic poles from its cell to its hips. The pawn's own position, selection box, label and health bar stay
    /// at its feet. Every render phase gets the same lift, so body, head, apparel and weapon move together.</summary>
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
    public static class DrawingPatch_DynamicDrawPhaseAt
    {
        public static void Prefix(DrawPhase __0, ref Vector3 __1, Pawn ___pawn)
        {
            if (!Stilts.AnyStilts || ___pawn == null || !___pawn.Spawned)
            {
                return;
            }
            HediffComp_Stilts comp = Stilts.ControllerOf(___pawn);
            if (comp == null)
            {
                return;
            }
            float fraction = comp.Fraction;
            if (fraction <= 0f)
            {
                return;
            }
            float lift = Stilts.Props.lift * fraction;
            if (__0 == DrawPhase.Draw)
            {
                StiltPoles.Draw(__1, lift, ___pawn.Rotation);
            }
            __1.z += lift;
        }
    }

    [StaticConstructorOnStartup]
    public static class StiltPoles
    {
        // Where the feet and the hips are, relative to the pawn's draw position (cell centre) on screen.
        private const float FootBelow = 0.34f;
        private const float HipBelow = 0.2f;
        private const float Spread = 0.12f;
        private const float SideSpread = 0.035f;
        private const float Overlap = 0.06f;
        private static readonly float[] SegmentWidths = { 0.13f, 0.11f, 0.09f };

        private static readonly Material Tube = MaterialPool.MatFrom("StiltsBionics/Things/StiltTube", ShaderDatabase.Cutout);
        private static readonly Material Foot = MaterialPool.MatFrom("StiltsBionics/Things/StiltFoot", ShaderDatabase.Cutout);

        /// <summary>Draws both poles under a pawn whose body is drawn lift cells above its cell.</summary>
        public static void Draw(Vector3 ground, float lift, Rot4 rot)
        {
            float footZ = ground.z - FootBelow;
            float hipZ = ground.z + lift - HipBelow;
            if (hipZ <= footZ + 0.02f)
            {
                return;
            }
            float y = ground.y - 0.012f;
            if (rot.IsHorizontal)
            {
                // Side view: one pole behind the other.
                Pole(ground.x - SideSpread, footZ, hipZ, y - 0.001f);
                Pole(ground.x + SideSpread, footZ, hipZ, y);
            }
            else
            {
                Pole(ground.x - Spread, footZ, hipZ, y);
                Pole(ground.x + Spread, footZ, hipZ, y);
            }
        }

        private static void Pole(float x, float footZ, float hipZ, float y)
        {
            float length = hipZ - footZ;
            int count = SegmentWidths.Length;
            float step = length / count;
            // Outer tube at the hips, thinner tubes sliding out below it; each overlaps the next a little.
            for (int i = 0; i < count; i++)
            {
                float top = hipZ - step * i;
                float bottom = Mathf.Max(footZ, top - step - Overlap);
                Quad(Tube, x, (top + bottom) * 0.5f, SegmentWidths[i], top - bottom, y + 0.0002f * (count - i));
            }
            Quad(Foot, x, footZ, 0.2f, 0.09f, y + 0.001f);
        }

        private static void Quad(Material mat, float x, float z, float width, float length, float y)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, new Vector3(width, 1f, length));
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
        }
    }
}
