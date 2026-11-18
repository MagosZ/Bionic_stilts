using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace StiltsBionics.CE
{
    /// <summary>Loaded only with Combat Extended (LoadFolders.xml). Applies the CE rules for extended stilts.</summary>
    [StaticConstructorOnStartup]
    public static class CEStartup
    {
        static CEStartup()
        {
            StiltsMod.PatchAllSafely(new Harmony(StiltsMod.HarmonyId + ".ce"), typeof(CEStartup).Assembly, "Stilts Bionics CE");
        }
    }

    /// <summary>CE geometry shared by the CE rules.</summary>
    public static class CELine
    {
        /// <summary>Top of the roof slab over a cell (CE's slab: built 2.1, thin rock 4.0, overhead mountain 8.0), or
        /// negative infinity without a roof.</summary>
        public static float RoofTop(IntVec3 cell, Map map)
        {
            cell = Line3D.Flat(cell);
            if (map == null || !cell.InBounds(map))
            {
                return float.NegativeInfinity;
            }
            RoofDef roof = cell.GetRoof(map);
            return roof == null ? float.NegativeInfinity : CE_Utility.GetBoundsFor(cell, roof).max.y;
        }

        /// <summary>A roofed cell blocks the line when the line is inside or below the slab there.</summary>
        public static bool RoofBlocks(Vector3 a, Vector3 b, IntVec3 cell, Map map)
        {
            float top = RoofTop(cell, map);
            if (float.IsNegativeInfinity(top))
            {
                return false;
            }
            Line3D.HeightRangeInCell(a, b, Line3D.Flat(cell), out float minY, out _);
            return minY < top;
        }

        /// <summary>A round fired by a pawn standing on extended stilts (fragments of its shells are not).</summary>
        public static bool FiredFromStilts(ProjectileCE projectile)
        {
            return projectile != null && projectile.equipment != projectile && Stilts.IsExtended(projectile.launcher);
        }

        public static bool Roofed(IntVec3 cell, Map map)
        {
            cell = Line3D.Flat(cell);
            return map != null && cell.InBounds(map) && cell.Roofed(map);
        }

        /// <summary>Airborne targets (skyfallers, rounds for CIWS) keep CE's own rules.</summary>
        public static bool Airborne(Thing target) => target is Skyfaller || target is ProjectileCE;
    }
}
