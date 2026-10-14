using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltimateAscent;

[BepInPlugin(Guid, Name, Version)]
[BepInDependency(Compat.Tfa.Guid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(Compat.ItemStats.Guid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(Compat.EffectPreview.Guid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(Compat.PlayersInfo.Guid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInIncompatibility("MoreAscents")]
[BepInIncompatibility("13dastardlyascents")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "UltimateAscent";
    public const string Name = "Ultimate Ascent";
    public const string Version = "1.1.2";

    internal static ManualLogSource Log;
    internal static Plugin Instance;
    internal static bool Ready;

    internal static ConfigEntry<bool> UnlockAll;
    internal static ConfigEntry<int> ExtendedProgress;
    internal static ConfigEntry<int> DefaultAscent;
    internal static ConfigEntry<bool> WrapAround;
    internal static ConfigEntry<bool> VerboseLogging;

    /// <summary>One switch per ascent 9-25 (indexed by the ascent): off leaves that ascent's own rule out.</summary>
    internal static ConfigEntry<bool>[] AscentSwitches;

    /// <summary>Off: the night itself no longer makes anyone cold (the game's rule from ascent 5).</summary>
    internal static ConfigEntry<bool> ColdNights;

    /// <summary>Off: the mod's new items (Climbing Chalk, Lightweight Parasol) do not spawn in regular runs.</summary>
    internal static ConfigEntry<bool> NewItems;

    private Harmony harmony;

    private void Awake()
    {
        Log = Logger;
        Instance = this;
        BindConfig();

        if (!AscentCatalog.EnsureDefault())
        {
            Log.LogError("Ultimate Ascent is disabled because its ascents could not be added to the game.");
            return;
        }

        harmony = new Harmony(Guid);
        int applied = 0;
        int failed = 0;
        foreach (Type type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
        {
            if (!type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Any())
            {
                continue;
            }
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.LogError($"Patch {type.FullName} could not be applied; that rule is off. {ex.GetBaseException().Message}");
            }
        }

        GameObject runtime = new GameObject("UltimateAscentRuntime");
        DontDestroyOnLoad(runtime);
        runtime.hideFlags = HideFlags.HideAndDontSave;
        runtime.AddComponent<Runtime>();

        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        Rules.OnActiveSceneChanged(SceneManager.GetActiveScene());

        Ready = true;
        Log.LogInfo($"{Name} {Version} loaded: ascents {Lvl.First}-{Lvl.Last} added, {applied} patch classes applied" + (failed > 0 ? $", {failed} failed." : ".") +
                    (Compat.Tfa.Present ? " True Final Ascent detected." : ""));
    }

    private void BindConfig()
    {
        DefaultAscent = Config.Bind("Boarding Pass", "Default Ascent", 0,
            new ConfigDescription("The ascent the boarding pass shows when you first open it after arriving at the airport. -1 = Tenderfoot, 0 = Peak (the game's normal choice, also leaves True Final Ascent's own start option alone), 1-25 = that ascent. Values above your highest unlocked ascent pick the highest unlocked one.",
                new AcceptableValueRange<int>(-1, Lvl.Last)));
        WrapAround = Config.Bind("Boarding Pass", "Wrap Around", true,
            "The ascent arrows wrap around: left from Tenderfoot jumps to the end of the list (True Final Ascent's FINAL ASCENT if installed, otherwise your highest unlocked ascent), right from the end of the list goes back to Tenderfoot.");
        UnlockAll = Config.Bind("Unlocks", "Unlock All Ascents", false,
            "Makes ascents 9-25 selectable on the boarding pass without beating them in order. Turning it off again brings back your normal progress.");
        ExtendedProgress = Config.Bind("Unlocks", "Highest Unlocked Ascent", 0,
            new ConfigDescription("Progress through ascents 9-25, saved by the mod (the game's own Steam stat stops at ascent 8). Beating ascent N unlocks N+1. 0 means only the vanilla progress counts.",
                new AcceptableValueRange<int>(0, Lvl.Last + 1)));
        AscentSwitches = new ConfigEntry<bool>[Lvl.Last + 1];
        for (int level = Lvl.First; level <= Lvl.Last; level++)
        {
            AscentSwitches[level] = Config.Bind("Ascents", "Ascent " + level, true,
                AscentCatalog.Description(level) + " Off: this ascent's own rule is left out; the other ascents keep theirs (ascent 25 with Ascent 20 off plays without Bing Bong's rule). Only the host's settings count.");
            AscentSwitches[level].SettingChanged += (_, _) => HostOptions.Invalidate();
        }
        ColdNights = Config.Bind("Ascents", "Cold Nights", true,
            "Nights make you cold: the game's rule from ascent 5, until the third part of the climb (harsher from ascent 18, and longer in the Alpine). Off: the night itself no longer makes you cold, on every ascent; cold from anything else (storms, snow, water, the Alpine) stays. Custom runs keep their own Cold Night setting. Only the host's settings count.");
        ColdNights.SettingChanged += (_, _) => HostOptions.Invalidate();
        NewItems = Config.Bind("Items", "New Items", true,
            "The mod's new items, the Climbing Chalk and the Lightweight Parasol (the Roots parasol from ascent 9), spawn in regular runs from ascent 9. Off: in regular runs the Climbing Chalk does not spawn, and the regular Parasol spawns wherever the Roots parasol would (custom runs keep their own switches). Only the host's settings count.");
        NewItems.SettingChanged += (_, _) => HostOptions.Invalidate();
        VerboseLogging = Config.Bind("Debug", "Verbose Logging", false, "Writes extra diagnostic lines to the BepInEx log.");
        RemoveRetiredEntries();
    }

    /// <summary>
    /// Entries this version no longer has (the "Big Lobbies" section, the dropped "Double Apocalypse
    /// Weather") leave the file: BepInEx keeps writing back every entry it read and nothing bound. Every
    /// entry of this mod is bound above, so whatever is left over is retired.
    /// </summary>
    private void RemoveRetiredEntries()
    {
        try
        {
            if (AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config, null) is Dictionary<ConfigDefinition, string> retired && retired.Count > 0)
            {
                Log.LogInfo("Removed retired config entries: " + string.Join(", ", retired.Keys.Select(key => $"[{key.Section}] {key.Key}")));
                retired.Clear();
                Config.Save();
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning("Could not remove retired entries from the config file: " + ex.GetBaseException().Message);
        }
    }

    private static void OnActiveSceneChanged(Scene previous, Scene next)
    {
        Rules.OnActiveSceneChanged(next);
        RunLifecycle.OnSceneChanged(next);
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    internal static void Debug(string message)
    {
        if (VerboseLogging != null && VerboseLogging.Value)
        {
            Log.LogInfo(message);
        }
    }
}
