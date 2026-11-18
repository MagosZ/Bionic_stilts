using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace StiltsBionics
{
    /// <summary>Mod entry: settings and the Harmony patches of the main DLL. The CE DLL patches itself.</summary>
    public class StiltsMod : Mod
    {
        public const string HarmonyId = "magosz.stiltsbionics";

        public static StiltsSettings Settings { get; private set; }

        /// <summary>True when Combat Extended is loaded: the vanilla combat rules then stand down and the CE DLL takes
        /// over (CE/Assemblies, loaded by LoadFolders.xml only with CE).</summary>
        public static readonly bool CEActive = ModsConfig.IsActive("CETeam.CombatExtended");

        public StiltsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<StiltsSettings>();
            PatchAllSafely(new Harmony(HarmonyId), typeof(StiltsMod).Assembly, "Stilts Bionics");
        }

        /// <summary>Applies every [HarmonyPatch] class of the assembly one by one, so a patch whose target is missing in
        /// this game version is skipped with a warning instead of stopping the rest.</summary>
        public static void PatchAllSafely(Harmony harmony, Assembly assembly, string label)
        {
            foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Log.Warning("[" + label + "] patch " + type.Name + " skipped: " + e.Message);
                }
            }
        }

        public override string SettingsCategory() => "Stilts Bionics";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("StiltsBionics_SettingRange".Translate(Settings.rangeBonus.ToStringPercent()));
            Settings.rangeBonus = Mathf.Round(list.Slider(Settings.rangeBonus, 0f, 1f) * 20f) / 20f;
            list.Gap();
            list.Label("StiltsBionics_SettingPoleHit".Translate(Settings.poleHitChance.ToStringPercent()));
            Settings.poleHitChance = Mathf.Round(list.Slider(Settings.poleHitChance, 0f, 1f) * 20f) / 20f;
            list.Gap();
            list.CheckboxLabeled("StiltsBionics_SettingDebug".Translate(), ref Settings.debugOverlay,
                "StiltsBionics_SettingDebugTip".Translate());
            list.End();
        }
    }

    public class StiltsSettings : ModSettings
    {
        /// <summary>Extra range of a held ranged weapon while the stilts are extended (0.25 = +25%).</summary>
        public float rangeBonus = 0.25f;

        /// <summary>Combat Extended only: chance that a round crossing the stilt zone below the lifted body hits a pole.</summary>
        public float poleHitChance = 0.15f;

        /// <summary>Dev mode only: flash the cells of the 3D line checks with the line's height.</summary>
        public bool debugOverlay;

        public bool DebugOverlayActive => debugOverlay && Prefs.DevMode;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref rangeBonus, "rangeBonus", 0.25f);
            Scribe_Values.Look(ref poleHitChance, "poleHitChance", 0.15f);
            Scribe_Values.Look(ref debugOverlay, "debugOverlay", false);
        }
    }

    internal static class StiltsLog
    {
        private static readonly HashSet<string> warned = new HashSet<string>();

        public static void WarnOnce(string key, string text)
        {
            if (warned.Add(key))
            {
                Log.Warning("[Stilts Bionics] " + text);
            }
        }
    }
}
