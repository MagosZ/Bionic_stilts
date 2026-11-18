using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace StiltsBionics
{
    /// <summary>3D line helpers for the over-the-wall rules (ported from Elevated Gun Platform's ElevatedLine). A line
    /// is a segment from a to b: x/z on the map, y = height in Combat Extended units (a wall is 2.0, a human 1.0).
    /// Shared by the vanilla rules here and the CE DLL.</summary>
    public static class Line3D
    {
        /// <summary>Cells carry a height in y in some walks; map lookups and comparisons need y = 0.</summary>
        public static IntVec3 Flat(IntVec3 cell) => new IntVec3(cell.x, 0, cell.z);

        /// <summary>Min and max height of segment a-b over the square of the cell. Returns false when the segment does
        /// not cross the square; then both heights are the height at the segment's point nearest the cell centre.</summary>
        public static bool HeightRangeInCell(Vector3 a, Vector3 b, IntVec3 cell, out float minY, out float maxY)
        {
            if (CrossingInCell(a, b, cell, out float t0, out float t1))
            {
                float y0 = Mathf.Lerp(a.y, b.y, t0);
                float y1 = Mathf.Lerp(a.y, b.y, t1);
                minY = Mathf.Min(y0, y1);
                maxY = Mathf.Max(y0, y1);
                return true;
            }
            minY = maxY = HeightNear(a, b, cell.ToVector3Shifted());
            return false;
        }

        /// <summary>Parameters (0..1) at which segment a-b enters and leaves the cell's square on the map plane.</summary>
        public static bool CrossingInCell(Vector3 a, Vector3 b, IntVec3 cell, out float t0, out float t1)
        {
            t0 = 0f;
            t1 = 1f;
            return Clip(a.x, b.x - a.x, cell.x, cell.x + 1f, ref t0, ref t1)
                && Clip(a.z, b.z - a.z, cell.z, cell.z + 1f, ref t0, ref t1);
        }

        /// <summary>Height of segment a-b at its point closest (on the map plane) to p.</summary>
        public static float HeightNear(Vector3 a, Vector3 b, Vector3 p) => Mathf.Lerp(a.y, b.y, ClosestT(a, b, p));

        /// <summary>Parameter (0..1) of the point of segment a-b closest to p on the map plane.</summary>
        public static float ClosestT(Vector3 a, Vector3 b, Vector3 p)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float len2 = dx * dx + dz * dz;
            if (len2 < 1e-6f)
            {
                return 0f;
            }
            return Mathf.Clamp01(((p.x - a.x) * dx + (p.z - a.z) * dz) / len2);
        }

        /// <summary>Every cell whose square the segment a-b crosses on the map plane, in order from a. Where the
        /// segment passes exactly through a corner, both side cells are listed too (they only touch the line).</summary>
        public static void CellsCrossed(Vector3 a, Vector3 b, List<IntVec3> cells)
        {
            cells.Clear();
            int cx = Mathf.FloorToInt(a.x), cz = Mathf.FloorToInt(a.z);
            int ex = Mathf.FloorToInt(b.x), ez = Mathf.FloorToInt(b.z);
            float dx = b.x - a.x, dz = b.z - a.z;
            int stepX = dx > 0f ? 1 : (dx < 0f ? -1 : 0);
            int stepZ = dz > 0f ? 1 : (dz < 0f ? -1 : 0);
            float tDeltaX = stepX != 0 ? 1f / Mathf.Abs(dx) : float.PositiveInfinity;
            float tDeltaZ = stepZ != 0 ? 1f / Mathf.Abs(dz) : float.PositiveInfinity;
            float tMaxX = stepX > 0 ? (cx + 1 - a.x) * tDeltaX : (stepX < 0 ? (a.x - cx) * tDeltaX : float.PositiveInfinity);
            float tMaxZ = stepZ > 0 ? (cz + 1 - a.z) * tDeltaZ : (stepZ < 0 ? (a.z - cz) * tDeltaZ : float.PositiveInfinity);
            cells.Add(new IntVec3(cx, 0, cz));
            int guard = 0;
            while ((cx != ex || cz != ez) && guard++ < 4096)
            {
                if (Mathf.Abs(tMaxX - tMaxZ) < 1e-5f)
                {
                    if (tMaxX > 1f)
                    {
                        break;
                    }
                    cells.Add(new IntVec3(cx + stepX, 0, cz));
                    cells.Add(new IntVec3(cx, 0, cz + stepZ));
                    cx += stepX;
                    cz += stepZ;
                    tMaxX += tDeltaX;
                    tMaxZ += tDeltaZ;
                }
                else if (tMaxX < tMaxZ)
                {
                    if (tMaxX > 1f)
                    {
                        break;
                    }
                    cx += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    if (tMaxZ > 1f)
                    {
                        break;
                    }
                    cz += stepZ;
                    tMaxZ += tDeltaZ;
                }
                cells.Add(new IntVec3(cx, 0, cz));
            }
        }

        /// <summary>Debug overlay (dev mode and the mod setting): CE's colours (0 blocked, 0.7 clear, 0.4 path) and
        /// the line's height as text.</summary>
        public static void Flash(Map map, IntVec3 cell, float colorPct, float height)
        {
            if (map == null || !DebugActive)
            {
                return;
            }
            map.debugDrawer.FlashCell(Flat(cell), colorPct, height.ToString("0.00"), 50);
        }

        public static bool DebugActive => StiltsMod.Settings != null && StiltsMod.Settings.DebugOverlayActive;

        // Liang-Barsky clip of the parameter range against one axis slab.
        private static bool Clip(float p, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-6f)
            {
                return p >= lo && p <= hi;
            }
            float ta = (lo - p) / d;
            float tb = (hi - p) / d;
            if (ta > tb)
            {
                float swap = ta;
                ta = tb;
                tb = swap;
            }
            if (ta > t0)
            {
                t0 = ta;
            }
            if (tb < t1)
            {
                t1 = tb;
            }
            return t0 <= t1;
        }
    }
}
