using System;
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
[BepInIncompatibility("MoreAscents")]
[BepInIncompatibility("13dastardlyascents")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "UltimateAscent";
    public const string Name = "Ultimate Ascent";
    public const string Version = "1.1.0";

    internal static ManualLogSource Log;
    internal static Plugin Instance;
    internal static bool Ready;

    internal static ConfigEntry<bool> UnlockAll;
    internal static ConfigEntry<int> ExtendedProgress;
    internal static ConfigEntry<int> DefaultAscent;
    internal static ConfigEntry<bool> WrapAround;
    internal static ConfigEntry<bool> NerfedRevives;
    internal static ConfigEntry<bool> CursePunishment;
    internal static ConfigEntry<bool> VerboseLogging;

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
            "The ascent arrows wrap around: left from Tenderfoot jumps to your highest unlocked ascent, right from the last entry goes back to Tenderfoot.");
        UnlockAll = Config.Bind("Unlocks", "Unlock All Ascents", false,
            "Makes ascents 9-25 selectable on the boarding pass without beating them in order. Turning it off again brings back your normal progress.");
        ExtendedProgress = Config.Bind("Unlocks", "Highest Unlocked Ascent", 0,
            new ConfigDescription("Progress through ascents 9-25, saved by the mod (the game's own Steam stat stops at ascent 8). Beating ascent N unlocks N+1. 0 means only the vanilla progress counts.",
                new AcceptableValueRange<int>(0, Lvl.Last + 1)));
        NerfedRevives = Config.Bind("Big Lobbies", "Nerfed Revives", true,
            "Ascent 14+: reviving and status clearing keep 25% of the cleared statuses (the Cursed Skull's clear 10%), and revives that would add no status add curse and hunger instead. Only the host's setting counts.");
        CursePunishment = Config.Bind("Big Lobbies", "Curse On Teammate Death", true,
            "Ascent 11+: when a scout dies, every survivor gets curse (less in bigger lobbies). Only the host's setting counts.");
        VerboseLogging = Config.Bind("Debug", "Verbose Logging", false, "Writes extra diagnostic lines to the BepInEx log.");
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
