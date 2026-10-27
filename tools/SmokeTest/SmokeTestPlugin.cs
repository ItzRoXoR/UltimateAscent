using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zorro.Core;
using Zorro.Core.Serizalization;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// Plays through the start of a solo run on its own and checks the rules of Ultimate Ascent from
/// inside the game: title screen, airport and boarding pass, then a run on today's map (the beach,
/// then a jump to the Alpine segment). Only active when PEAK is started with "--ua-smoketest";
/// "--ua-smoketest-ascent=N" picks the ascent (default 25) and "--ua-smoketest-noapocalypse" switches
/// True Final Ascent's Apocalypse mode off for this session (in memory only); "--ua-smoketest-only=a,b"
/// runs only the run steps whose names contain one of the words (for quick checks of one rule);
/// "--ua-smoketest-biome=V" plays the next map whose biomes include that letter (the game's own map
/// codes: S shore, T tropics or R roots, A alpine or M mesa, V the Caldera and the Kiln) instead of today's.
/// Results go to the BepInEx log and to BepInEx/UltimateAscent.SmokeTest.txt; the game quits at the end.
/// </summary>
[BepInPlugin(Guid, "Ultimate Ascent Smoke Test", "1.0.0")]
[BepInDependency(UltimateAscent.Plugin.Guid)]
public sealed class SmokeTestPlugin : BaseUnityPlugin
{
    public const string Guid = "UltimateAscent.SmokeTest";
    public const string Switch = "--ua-smoketest";
    private const string AscentSwitch = Switch + "-ascent=";
    private const string NoApocalypseSwitch = Switch + "-noapocalypse";
    private const string ApocalypseSwitch = Switch + "-apocalypse";
    private const string OnlySwitch = Switch + "-only=";
    private const string BiomeSwitch = Switch + "-biome=";

    private void Awake()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!args.Contains(Switch))
        {
            Logger.LogInfo("Inactive. Start PEAK with " + Switch + " to run the smoke test.");
            return;
        }
        int ascent = Lvl.Last;
        string[] only = null;
        string biome = null;
        foreach (string arg in args)
        {
            if (arg.StartsWith(BiomeSwitch))
            {
                biome = arg.Substring(BiomeSwitch.Length);
            }
            if (arg.StartsWith(AscentSwitch) && int.TryParse(arg.Substring(AscentSwitch.Length), out int parsed))
            {
                ascent = Mathf.Clamp(parsed, Lvl.First, Lvl.Last);
            }
            else if (arg.StartsWith(OnlySwitch))
            {
                only = arg.Substring(OnlySwitch.Length).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }
        GameObject host = new GameObject("UltimateAscentSmokeTest");
        DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent<Runner>().Begin(Logger, ascent, args.Contains(NoApocalypseSwitch), args.Contains(ApocalypseSwitch), only, biome);
    }
}

internal sealed partial class Runner : MonoBehaviour
{
    private const float WatchdogSeconds = 2400f;

    private static readonly Status[] CareStatuses =
    {
        Status.Injury, Status.Hunger, Status.Cold, Status.Poison, Status.Crab, Status.Curse, Status.Drowsy, Status.Hot, Status.Spores
    };

    private ManualLogSource log;
    private int target;
    private bool noApocalypse;

    /// <summary>Switch TFA's Apocalypse mode on for the test (in memory only), whatever the player last chose.</summary>
    private bool apocalypseOn;
    private string resultPath;
    private int passed;
    private int failed;
    private bool finished;
    private bool aborted;
    private float startedAt;

    /// <summary>Keeps the scout alive between the checks (statuses locked and cleared).</summary>
    private bool care;
    private float nextCare;
    private bool reportedPassOut;

    /// <summary>The run step under way, and whether the scout is down (reported once each time).</summary>
    private string stepUnderWay;
    private bool reportedDeath;

    /// <summary>Statuses apply, but are cleared every frame before they can add up to passing out.</summary>
    private bool shielded;

    /// <summary>TFA's Apocalypse mode runs the weather of this climb.</summary>
    private bool apocalypse;
    private bool expectingDeath;

    /// <summary>The player's master volume while the test has it at 0 (in memory only; put back in any case).</summary>
    private float? mutedMasterVolume;

    private Item held;

    /// <summary>The map to play: the next one whose biomes include this letter (null: today's).</summary>
    private string biome;

    private ConfigFile config;
    private bool configChanged;
    private bool savedSaveOnSet;
    private bool savedUnlockAll;
    private int savedDefault;

    private MethodInfo apocalypseRestore;
    private bool apocalypseChanged;
    private bool savedApocalypse;

    /// <summary>Only the run steps whose names contain one of these words (null: all).</summary>
    private string[] only;

    internal void Begin(ManualLogSource log, int target, bool noApocalypse, bool apocalypseOn, string[] only, string biome)
    {
        this.biome = biome;
        this.log = log;
        this.target = target;
        this.noApocalypse = noApocalypse;
        this.apocalypseOn = apocalypseOn;
        this.only = only;
        resultPath = Path.Combine(Paths.BepInExRootPath, "UltimateAscent.SmokeTest.txt");
        File.WriteAllText(resultPath, $"Ultimate Ascent smoke test, ascent {target}{(noApocalypse ? ", Apocalypse off" : "")}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
        startedAt = Time.realtimeSinceStartup;
        Harmony harmony = new Harmony(SmokeTestPlugin.Guid);
        harmony.CreateClassProcessor(typeof(ShakeCounter)).Patch();
        harmony.CreateClassProcessor(typeof(PoisonFxCounter)).Patch();
        harmony.CreateClassProcessor(typeof(NoSteamStats)).Patch();
        harmony.CreateClassProcessor(typeof(NoAchievements)).Patch();
        harmony.CreateClassProcessor(typeof(NoRunSave)).Patch();
        harmony.CreateClassProcessor(typeof(NoSteamStatWrites)).Patch();
        harmony.CreateClassProcessor(typeof(NoRunSaveDeletion)).Patch();
        harmony.CreateClassProcessor(typeof(HeatSources)).Patch();
        harmony.CreateClassProcessor(typeof(ShowOwnBackpack)).Patch();
        harmony.CreateClassProcessor(typeof(FogCalls)).Patch();
        harmony.CreateClassProcessor(typeof(CustomRunSpawn)).Patch();
        harmony.CreateClassProcessor(typeof(ForceRecorder)).Patch();
        harmony.CreateClassProcessor(typeof(FallRecorder)).Patch();
        PatchRoundRecorders(harmony);
        log.LogInfo($"Smoke test started (ascent {target}).");
        StartCoroutine(Guard("smoke test", Main()));
    }

    // ------------------------------------------------------------------ plumbing

    private void Record(string kind, string name, string detail)
    {
        if (kind == "PASS")
        {
            passed++;
        }
        else if (kind == "FAIL")
        {
            failed++;
        }
        string line = $"{kind} {name}" + (string.IsNullOrEmpty(detail) ? "" : ": " + detail);
        if (kind == "FAIL")
        {
            log.LogError(line);
        }
        else
        {
            log.LogInfo(line);
        }
        try
        {
            File.AppendAllText(resultPath, line + Environment.NewLine);
        }
        catch (Exception)
        {
        }
    }

    private void Check(string name, bool ok, string detail = "")
    {
        if (!ok && ScoutLost)
        {
            Record("INFO", name, "skipped: True Final Ascent's Apocalypse weather knocked the scout out");
            return;
        }
        Record(ok ? "PASS" : "FAIL", name, detail);
    }

    /// <summary>
    /// With TFA's Apocalypse mode on, its weather can knock the scout out (thorns get past the status
    /// lock) and ascent 24 makes that death; the checks after that cannot run.
    /// </summary>
    private bool ScoutLost => apocalypse && !expectingDeath && (Local == null || Local.data == null || Local.data.dead || Local.data.passedOut);

    private void Info(string name, string detail = "") => Record("INFO", name, detail);

    private void Fail(string name, string detail = "") => Record("FAIL", name, detail);

    private void Step(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Fail(name, "exception: " + ex);
        }
    }

    /// <summary>Runs a coroutine step; an exception fails the step instead of ending the whole test.</summary>
    private IEnumerator Guard(string name, IEnumerator body)
    {
        while (true)
        {
            object current;
            try
            {
                if (!body.MoveNext())
                {
                    TidyThorns();
                    yield break;
                }
                current = body.Current;
            }
            catch (Exception ex)
            {
                Fail(name, "exception: " + ex);
                TidyThorns();
                yield break;
            }
            yield return current;
        }
    }

    /// <summary>A long step that runs only when its word is given with -Only (a plain run skips it).</summary>
    private IEnumerator OnlyWhenAsked(string word, Func<IEnumerator> step)
    {
        if (only == null || !only.Any(w => w.Equals(word, StringComparison.OrdinalIgnoreCase)))
        {
            Info($"the '{word}' step is skipped", $"run with -Only {word}");
            yield break;
        }
        yield return step();
    }

    /// <summary>
    /// Thorns left in the scout after a step are pulled out: from ascent 17 thorns on the body never fall out by
    /// themselves, so they would pile up from step to step until the bar is full.
    /// </summary>
    private static void TidyThorns()
    {
        Character local = Character.localCharacter;
        CharacterAfflictions afflictions = local != null && local.refs != null ? local.refs.afflictions : null;
        if (afflictions != null && afflictions.physicalThorns != null && afflictions.physicalThorns.Any(t => t != null && t.stuckIn))
        {
            afflictions.RemoveAllThorns();
        }
        // Items stuck to the scout (a cactus ball puts 40 on the bar while it sticks) go too.
        foreach (StickyItemComponent stuck in StickyItemComponent.ALL_STUCK_ITEMS.ToList())
        {
            if (stuck != null && local != null && stuck.stuckToCharacter == local && stuck.photonView != null && stuck.photonView.IsMine)
            {
                PhotonNetwork.Destroy(stuck.gameObject);
            }
        }
        if (afflictions != null)
        {
            afflictions.UpdateWeight();
        }
    }

    private IEnumerator Until(string what, float timeout, Func<bool> condition)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (true)
        {
            bool ok;
            try
            {
                ok = condition();
            }
            catch (Exception)
            {
                ok = false;
            }
            if (ok)
            {
                Info("reached " + what, $"after {Time.realtimeSinceStartup - startedAt:F0} s");
                yield break;
            }
            if (Time.realtimeSinceStartup > end)
            {
                Fail("reach " + what, $"not reached within {timeout:F0} s (scene {SceneManager.GetActiveScene().name})");
                aborted = true;
                Finish();
                yield break;
            }
            yield return null;
        }
    }

    private static IEnumerator WaitUntilTime(float time)
    {
        while (Time.time < time)
        {
            yield return null;
        }
    }

    private static string Plain(string text) => text == null ? "" : Regex.Replace(text, "<.*?>", "").Replace("\n", " ").Trim();

    private static Character Local => Character.localCharacter;

    private static CharacterAfflictions Afflictions => Character.localCharacter.refs.afflictions;

    private static string GroundBiome(Character character) => Map.TryGetGroundBiome(character.Center, out Biome.BiomeType biome) ? biome.ToString() : "none";

    private static string BiomeOf(Component component) => Map.TryGetBiome(component, out Biome.BiomeType biome) ? biome.ToString() : "none";

    /// <summary>Everything of one status the game is asked to add while the action runs (after every multiplier, before its 0.025 steps).</summary>
    private static float Added(Status type, Action action)
    {
        float total = 0f;
        Action<Status, float> listener = (added, amount) =>
        {
            if (added == type)
            {
                total += amount;
            }
        };
        Afflictions.OnAddedIncrementalStatus += listener;
        try
        {
            action();
        }
        finally
        {
            Afflictions.OnAddedIncrementalStatus -= listener;
        }
        return total;
    }

    /// <summary>
    /// Collects status additions over time (optionally only matching ones); <see cref="Stop"/> ends it.
    /// Without a filter it leaves out what the rising fog adds, which can reach the scout during any check.
    /// </summary>
    private sealed class Meter
    {
        private readonly CharacterAfflictions afflictions;
        private readonly Status type;
        private readonly Func<float, bool> filter;
        private readonly Action<Status, float> listener;
        private readonly float started;
        internal float Total;
        internal int Count;
        internal float Seconds;
        internal float LastAt = -1f;

        internal Meter(Status type, Func<float, bool> filter = null)
        {
            afflictions = Afflictions;
            this.type = type;
            this.filter = filter;
            listener = OnAdded;
            afflictions.OnAddedIncrementalStatus += listener;
            started = Time.time;
        }

        private void OnAdded(Status added, float amount)
        {
            if (added == type && (filter == null ? FogCalls.Depth == 0 : filter(amount)))
            {
                Total += amount;
                Count++;
                LastAt = Time.time;
            }
        }

        internal Meter Stop()
        {
            afflictions.OnAddedIncrementalStatus -= listener;
            Seconds = Time.time - started;
            return this;
        }
    }

    private static bool InSunlight(Character character)
    {
        DayNightManager clock = DayNightManager.instance;
        if (clock == null || clock.sun == null || !clock.sun.enabled || clock.sun.intensity <= 1f)
        {
            return false;
        }
        RaycastHit hit = HelperFunctions.LineCheck(character.Center + clock.sun.transform.forward * -1000f, character.Center, HelperFunctions.LayerType.AllPhysical);
        return hit.transform == null || hit.transform.root == character.transform.root;
    }

    /// <summary>Locks the scout's statuses between checks, so the harsh ascent cannot end the test early.</summary>
    private void Protect(bool on)
    {
        care = on;
        shielded = false;
        if (Local != null)
        {
            Local.statusesLocked = on;
        }
    }

    /// <summary>Lets statuses through (to measure them) while clearing them before they add up.</summary>
    private void Shield(bool on)
    {
        care = false;
        shielded = on;
        if (Local != null)
        {
            Local.statusesLocked = false;
            if (on)
            {
                ClearStatuses(Afflictions);
            }
        }
    }

    private static void ClearStatuses(CharacterAfflictions afflictions)
    {
        foreach (Status type in CareStatuses)
        {
            if (afflictions.GetCurrentStatus(type) > 0f)
            {
                afflictions.SetStatus(type, 0f);
            }
        }
    }

    private void Unlocked(Action action)
    {
        bool wasLocked = Local.statusesLocked;
        Local.statusesLocked = false;
        try
        {
            action();
        }
        finally
        {
            Local.statusesLocked = wasLocked;
        }
    }

    /// <summary>
    /// The frame rate the test runs at, at most: PEAK is known to crash now and then at very high frame
    /// rates (its developers advise a cap). Set in memory only; the player's setting stays as it is.
    /// </summary>
    private const int TestFrameRate = 120;

    private int savedFrameRate = int.MinValue;

    private void CapFrameRate()
    {
        int current = Application.targetFrameRate;
        if (current > 0 && current <= TestFrameRate)
        {
            return;
        }
        if (savedFrameRate == int.MinValue)
        {
            savedFrameRate = current;
            Info("frame rate capped for the test", $"{TestFrameRate} fps (the game's setting: {(current > 0 ? current.ToString() : "uncapped")}; PEAK is known to crash at very high frame rates)");
        }
        Application.targetFrameRate = TestFrameRate;
    }

    private void Update()
    {
        if (!finished)
        {
            CapFrameRate();
        }
        if (!finished && Time.realtimeSinceStartup - startedAt > WatchdogSeconds)
        {
            Fail("watchdog", $"the test took longer than {WatchdogSeconds:F0} s");
            Finish();
        }
        bool down = Local != null && Local.data != null && (Local.data.dead || Local.data.fullyPassedOut);
        if (down && !reportedDeath)
        {
            Info($"the scout went down during \"{stepUnderWay}\"", StatusesAndEffects());
        }
        reportedDeath = down;
        if (shielded)
        {
            Character scout = Local;
            if (scout != null && scout.data != null && !scout.data.dead && scout.refs.afflictions.statusSum >= 0.4f)
            {
                ClearStatuses(scout.refs.afflictions);
            }
            return;
        }
        if (!care || Time.time < nextCare)
        {
            return;
        }
        nextCare = Time.time + 0.25f;
        Character local = Local;
        if (local == null || local.data == null || local.data.dead || !Rules.InRun)
        {
            return;
        }
        local.statusesLocked = true;
        if (local.data.passedOut && !reportedPassOut)
        {
            reportedPassOut = true;
            Info("the scout passed out while protected", $"statuses {Afflictions.statusSum:0.00}");
        }
        CharacterAfflictions afflictions = local.refs.afflictions;
        if (afflictions.statusSum < 0.3f)
        {
            return;
        }
        ClearStatuses(afflictions);
    }

    private void Finish()
    {
        if (finished)
        {
            return;
        }
        finished = true;
        care = false;
        shielded = false;
        if (savedFrameRate != int.MinValue)
        {
            Application.targetFrameRate = savedFrameRate;
        }
        RestoreMasterVolume();
        HoldBingBongRule(false);
        if (customAscentRestore != null)
        {
            customAscentRestore();
            customAscentRestore = null;
        }
        if (configChanged)
        {
            // The test settings were never written to the config file; put the old values back.
            UltimateAscent.Plugin.UnlockAll.Value = savedUnlockAll;
            UltimateAscent.Plugin.DefaultAscent.Value = savedDefault;
            config.SaveOnConfigSet = savedSaveOnSet;
        }
        if (apocalypseChanged)
        {
            try
            {
                apocalypseRestore.Invoke(null, new object[] { savedApocalypse });
            }
            catch (Exception ex)
            {
                Fail("restore TFA's Apocalypse switch", ex.GetBaseException().Message);
            }
        }
        Record("DONE", $"{passed} passed, {failed} failed", aborted ? "aborted" : "");
        StartCoroutine(QuitSoon());
    }

    private static IEnumerator QuitSoon()
    {
        yield return new WaitForSecondsRealtime(3f);
        Application.Quit();
    }

    /// <summary>The Ascent 20 switch as it was before the test held Bing Bong's rule back (null: not held).</summary>
    private bool? savedBingBongRule;

    private void HoldBingBongRule(bool hold)
    {
        ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches?[Lvl.BingBong];
        if (rule == null)
        {
            return;
        }
        if (hold && savedBingBongRule == null)
        {
            savedBingBongRule = rule.Value;
            rule.Value = false;
            Info("Bing Bong's rule (20) held back for the climb's checks (in memory)", "on again for its own check");
        }
        else if (!hold && savedBingBongRule != null)
        {
            rule.Value = savedBingBongRule.Value;
            savedBingBongRule = null;
        }
        HostOptions.Invalidate();
    }

    /// <summary>Bing Bong's own check, with his rule as the player has it.</summary>
    private IEnumerator WithBingBongRule(IEnumerator check)
    {
        ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches?[Lvl.BingBong];
        if (rule == null || savedBingBongRule == null)
        {
            yield return check;
            yield break;
        }
        rule.Value = savedBingBongRule.Value;
        HostOptions.Invalidate();
        try
        {
            yield return check;
        }
        finally
        {
            rule.Value = false;
            HostOptions.Invalidate();
        }
    }

    // ------------------------------------------------------------------ the test

    private IEnumerator Main()
    {
        yield return Until("the title screen", 240f, () => SceneManager.GetActiveScene().name == "Title" && FindAnyObjectByType<MainMenu>() != null);
        if (aborted)
        {
            yield break;
        }
        yield return new WaitForSecondsRealtime(5f);
        Step("ascent catalog", CheckCatalog);
        if (Selected("challenges: no darkness before the climb"))
        {
            yield return Guard("challenges: no darkness before the climb", ClearBeforeTheClimb());
        }

        Step("play solo", () => FindAnyObjectByType<MainMenu>().PlaySoloClicked());
        yield return Until("the airport", 240f, () => SceneManager.GetActiveScene().name == "Airport" && !LoadingScreenHandler.loading && Local != null &&
                                                       GUIManager.instance != null && GUIManager.instance.boardingPass != null);
        if (aborted)
        {
            yield break;
        }
        yield return new WaitForSecondsRealtime(3f);
        Check("solo session is offline", PhotonNetwork.OfflineMode && PhotonNetwork.InRoom, $"offline {PhotonNetwork.OfflineMode}, in room {PhotonNetwork.InRoom}");

        bool ready = false;
        Step("boarding pass", () => ready = BoardingPassChecks());
        if (!ready)
        {
            Finish();
            yield break;
        }
        if (Selected("challenges and cosmetics in the airport"))
        {
            yield return Guard("challenges and cosmetics in the airport", AirportCosmetics());
        }
        if (only != null && only.Contains("cosmeticslist"))
        {
            Step("the scouts' cosmetics, listed", DumpCosmetics);
            yield return Guard("the scouts' faces, pictured", FacePictures());
        }
        if (Selected("custom run threats in the airport"))
        {
            yield return Guard("custom run threats in the airport", CustomRunThreats());
        }
        if (Selected("the custom run's Ascent setting in the airport (customascent)"))
        {
            yield return Guard("the custom run's Ascent setting in the airport (customascent)", CustomRunAscentScreen());
        }
        Step("departure", Depart);

        yield return Until("the island", 300f, () => Rules.InRun && !LoadingScreenHandler.loading && MapHandler.ExistsAndInitialized && Local != null && Local.data != null);
        if (aborted)
        {
            yield break;
        }
        Step("run", RunBasics);
        yield return Until("waking up on the beach", 120f, () => Local.data.passedOutOnTheBeach <= 0f && Local.data.fullyConscious);
        if (aborted)
        {
            yield break;
        }
        float wake = Time.time;
        Protect(true);
        // Bing Bong's evil force (20) brings every scout crab while nobody holds him: at 24 a check that lets statuses
        // through could end in death. Off for the whole climb (in memory), on again for Bing Bong's own check.
        HoldBingBongRule(true);
        yield return new WaitForSeconds(2f);

        (string name, Func<IEnumerator> run)[] steps =
        {
            ("storms (25)", () => Now(CheckStorms)),
            ("extra tornadoes (25)", () => Tornadoes(wake)),
            ("no revive statues (19)", () => Now(CheckRevives)),
            ("Tropics only (17)", () => Now(CheckTropicsOnly)),
            ("crabs on the beach (17)", CrabChecks),
            ("a close look at the crabs (crabview, only when asked)", () => OnlyWhenAsked("crabview", CrabPreview)),
            ("the crabs' icons (crabicons, only when asked)", () => OnlyWhenAsked("crabicons", CrabIcons)),
            ("the new mobs below ascent 17: half as strong (mobs)", NewMobsBelow17),
            ("a placed piton keeps its yellow look (pitons, 15)", PitonChecks),
            ("sun on the beach (23)", () => SunHeat(true, "on the beach")),
            ("roasting (23)", Roasting),
            ("stronger ailments (14)", () => Now(CheckAilments)),
            ("painful luggage (21)", () => Now(CheckLuggage)),
            ("half food (15)", HalfFood),
            ("ticks (17)", TickBites),
            ("eating ticks (15)", EatTick),
            ("campfire save petrification (17)", () => Now(CampfireSavePetrify)),
            ("burned items (17)", BurnedItemHeat),
            ("burned backpack (17)", BackpackHeat),
            ("Climbing Chalk (9, 15)", ClimbingChalk),
            ("Climbing Chalk look in sun and shade", ChalkLook),
            ("custom run: chalk and Lightweight Parasol switches", () => Now(() => Step("custom-run switches", CustomRunSwitches))),
            ("custom run: suitcases follow the switches", CustomRunSuitcases),
            ("Lightweight Parasol look (9)", ParasolLook),
            ("lanterns (15)", Lanterns),
            ("ascent switches (config)", AscentSwitches),
            ("cold nights switch (config)", ColdNightsSwitch),
            ("the Hot Campfires switch (hotcampfires, config)", HotCampfiresSwitch),
            ("the modifiers line under the ascent (modifiers, config)", ModifiersLineCheck),
            ("a custom run on a picked ascent (customascent)", CustomAscentClimb),
            ("new items switch (config)", NewItemsSwitch),
            ("Magic Bean vine length (15)", BeanVineLength),
            ("Magic Bean thorns (15)", BeanThorns),
            ("thrown fungi (15)", ThrownFungi),
            ("checkpoint flag (15, 14, 24)", CheckpointFlagSave),
            ("Mandrake heard (17)", MandrakeHeard),
            ("held snowball (15)", HeldSnowball),
            ("Auto-Parachute (9, 15)", AutoParachuteCheck),
            ("six-slot backpack (single-player)", SixSlotBackpackCheck),
            ("six-slot backpack in multiplayer", SixSlotMultiplayerCheck),
            ("Vanilla Wafers (9)", VanillaCookies),
            ("wild foods (9)", WildFoodsCheck),
            ("Stones and Sticks (9, 17)", StonesAndSticks),
            ("zombie bite (17)", ZombieBiteCheck),
            ("Ritual Dagger on zombies (9, 15)", DaggerOnZombies),
            ("Pandora's crab (15)", PandoraCrabCheck),
            ("Warp Compass (9)", WarpCompassCheck),
            ("Climbing Chalk in the hands, like the Granola Bar", ChalkGrip),
            ("Cherry Lollipop (9)", CherryLollipopCheck),
            ("custom run: the variants' own switches", VariantSwitchesCheck),
            ("Toxic Drink (9, 15)", ToxicDrinkCheck),
            ("Torch (9, 15)", TorchCheck),
            ("Scout's Generosity: the mystical new items (15)", GenerosityCheck),
            ("banana peel (17)", BananaPeelCheck),
            ("a held cactus ball (17)", HeldCactus),
            ("Russian names of the new items", () => Now(RussianNames)),
            ("breaking free from webs and fly traps (9)", StrugglingCheck),
            ("Fortified Milk, Scout's Tenacity and the Weird Shroom (15)", MilkTenacityShroom),
            ("Lanterns in every part (9)", LanternPoolsCheck),
            ("the new items darken when cooked", NewItemsCooking),
            ("storms between Apocalypse weather (25)", ApocalypseBreaks),
            ("Energy Drink burst knocks nobody out (17)", EnergyDrinkBurstCheck),
            ("snowstorm heat, sunscreen and the sun (18, 23)", StormAndSun),
            ("snowstorm and wind shelter (every ascent)", SnowstormShelterCheck),
            ("TFA's Apocalypse events stay TFA's", TfaEvents),
            ("Bing Bong (20)", () => WithBingBongRule(BingBong(wake))),
            ("fog (9, 18)", Fog),
            ("weather (25)", () => Weather(wake)),
            ("bee ambush (17)", BeeAmbush),
            ("bee moments (17)", BeeMoments),
            ("jellyfish (17)", Jellyfish),
            ("first campfire and the fog after it (18)", FirstCampfire),
            ("rain pull (18)", RainPull),
            ("second segment (17)", SecondSegment),
            ("Stones and Sticks in the Tropics or the Roots (9)", StonesAndSticksSecond),
            ("Alpine (18, 23)", Alpine),
            ("ascent 18 weather: Gloom fog, tornado grip, Mesa sun (18, 25)", Weather18),
            ("Alpine snowfall", AlpineSnow),
            ("after the Alps (18)", AfterAlps),
            ("Caldera lava tide (18)", CalderaTideHeight),
            ("recovery rates and the Citadel (16, 17, 22)", RecoveryAndCitadel),
            ("ghost and the Torch in the Gloom (9, 17)", GhostAndTorchCheck),
            ("ravens and Early Worms in the Gloom (17)", RavensCheck),
            ("fall damage (12)", FallDrowsiness),
            ("falls with flying items (12, 13)", FallsWithItems),
            ("rising lava (9)", RisingHazard),
            ("spinning traps, saws and pressure plates in the Citadel (17)", SpinningTraps),
            ("ladders in the Citadel (17)", LaddersCheck),
            ("mimic chests in the Citadel (17)", MimicChestsCheck),
            ("arrow traps and spike balls in the Citadel (9)", ArrowTrapCheck),
            ("Kiln: ash and eruptions (17, 18)", KilnCheck),
            ("the Parasol wears out (15)", ParasolWearsOut),
            ("the clown chest's Lightweight Parasol", () => Now(ClownChestParasolShare)),
            ("blindness cured by the First Aid Kit and the Cure-All", BlindnessCuresCheck),
            ("new items on every ascent", () => Now(NewItemsOnEveryAscent)),
            ("zombies on the peak's hill (17)", PeakZombiesCheck),
            ("waiting bee swarms on the peak's hill (17)", WaitingBeesCheck),
            ("a parasol under lowgravity", ParasolLowGravityCheck),
            ("ascent 22's episodes (22)", Ascent22Episodes),
            ("the caldera's last stretch ablaze (17)", CalderaStretchCheck),
            ("the caldera's storms (25)", CalderaStormsCheck),
            ("the caldera's condors (17)", FasterCondorsCheck),
            ("eruptions burn (17)", EruptionHeatCheck),
            ("zombies share out the scouts (17)", () => Now(ZombieSharingCheck)),
            ("shorter reach of the rope cannons and the rescue claw (15)", ShorterReachCheck),
            ("what the drinks take off (15)", DrinkNumbers),
            ("the map picker's map (picker)", () => Now(PickedMapCheck)),
            ("multiplayer round: ascent 8, Scoutmaster, PlayersInfo, thorns, wild food, revive statue (8, 9, 15, 17)", MultiplayerRoundChecks),
            ("Warp Fungus thrown, jellyfish slip before it lands (warpslip, only when asked)", () => OnlyWhenAsked("warpslip", WarpSlipRepro)),
            ("snakes in the Tropics (17)", SnakeChecks),
            ("how fast a scout walks and runs, against the snakes (speeds, only when asked)", () => OnlyWhenAsked("speeds", SpeedProbe)),
            ("a slow look at the snakes' attack (snakeview, only when asked)", () => OnlyWhenAsked("snakeview", SnakeAttackPreview)),
            ("mountain goats in the Alps (17)", GoatChecks),
            ("the new mobs' placement: seven in ten on the climb itself (17)", NewMobPlacement),
            ("snakes crawl up and down the Tropics' cliffs (snakeclimb, 17)", SnakeClimbChecks),
            ("goats roam far, come down cliffs and leap across gaps (17)", GoatCliffChecks),
            ("the borders: goats keep to the Alps, snakes to the Tropics (17)", BorderChecks),
            ("a close look at the mountain goats (goatview, only when asked)", () => OnlyWhenAsked("goatview", GoatPreview)),
            ("the sounds of the Roots' beetles (beetleprobe, only when asked)", () => OnlyWhenAsked("beetleprobe", BeetleSoundProbe)),
            ("waterfalls: their hitboxes, a stream and a splash ours against the game's (every ascent; waterfalls, only when asked)", () => OnlyWhenAsked("waterfalls", WaterfallLook)),
            ("what makes the scorpion's tail shimmer (scorpionprobe, only when asked)", () => OnlyWhenAsked("scorpionprobe", ScorpionProbe)),
            ("the Tropics' giant trees and burning sounds (treeprobe, only when asked)", () => OnlyWhenAsked("treeprobe", TropicsProbe)),
            ("bees knock a scout out (17, 24)", BeeKnockout),
            ("Nadir (17, 18)", NadirCheck),
            // Last: it ends the climb.
            ("challenges and cosmetics in the climb", ChallengesInClimb),
        };
        foreach ((string name, Func<IEnumerator> run) in steps)
        {
            if (only != null && !only.Any(word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                continue;
            }
            if (ScoutLost)
            {
                Info("the remaining checks are skipped", "True Final Ascent's Apocalypse weather knocked the scout out");
                break;
            }
            stepUnderWay = name;
            yield return Guard(name, run());
        }
        Finish();
    }

    /// <summary>
    /// The scouts' skins, outfits and hats as the passport offers them: each one's index, asset name (and a skin's colour)
    /// and its passport icon saved as BepInEx/cosmetics/TYPE_INDEX.png (only with -Only cosmeticslist).
    /// </summary>
    private void DumpCosmetics()
    {
        Customization customization = Singleton<Customization>.Instance;
        string folder = Path.Combine(Paths.BepInExRootPath, "cosmetics");
        Directory.CreateDirectory(folder);
        List<string> lines = new List<string>();
        foreach ((string type, CustomizationOption[] options) in new[] { ("skin", customization.skins), ("fit", customization.fits), ("hat", customization.hats), ("eyes", customization.eyes), ("mouth", customization.mouths), ("accessory", customization.accessories) })
        {
            for (int i = 0; i < options.Length; i++)
            {
                CustomizationOption option = options[i];
                if (option == null)
                {
                    continue;
                }
                string file = $"{type}_{i:00}.png";
                if (option.texture != null)
                {
                    RenderTexture rt = RenderTexture.GetTemporary(option.texture.width, option.texture.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(option.texture, rt);
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    Texture2D copy = new Texture2D(option.texture.width, option.texture.height, TextureFormat.RGBA32, false);
                    copy.ReadPixels(new Rect(0, 0, option.texture.width, option.texture.height), 0, 0);
                    copy.Apply();
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                    File.WriteAllBytes(Path.Combine(folder, file), copy.EncodeToPNG());
                    Destroy(copy);
                }
                lines.Add($"{type}	{i}	{option.name}	{(type == "skin" ? "#" + ColorUtility.ToHtmlStringRGB(option.color) : "")}	{(option.texture != null ? file : "")}	{(option.isBlank ? "blank" : "")}	{(option.IsLocked ? "locked" : "")}");
            }
        }
        File.WriteAllLines(Path.Combine(folder, "list.tsv"), lines);
        Info("the scouts' cosmetics", $"{lines.Count} written to {folder}");
        // Every item: its prefab, its shown name and what using it does.
        List<string> items = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.Where(i => i != null).OrderBy(i => i.gameObject.name)
            .Select(i => $"{i.itemID}	{i.gameObject.name}	{Plain(i.GetItemName())}	{string.Join(", ", i.GetComponents<ItemActionBase>().Select(Describe))}").ToList();
        File.WriteAllLines(Path.Combine(folder, "items.tsv"), items);
    }

    /// <summary>
    /// Every eye and mouth on the passport's scout (lime skin, as the game draws them), rendered by the passport's own
    /// camera and saved as BepInEx/cosmetics/face_eyes_NN.png and face_mouth_NN.png (only with -Only cosmeticslist).
    /// </summary>
    private IEnumerator FacePictures()
    {
        PassportManager passport = PassportManager.instance;
        Customization options = Singleton<Customization>.Instance;
        if (passport == null || passport.dummy == null || passport.dummyCamera == null)
        {
            Info("the faces are not pictured", "no passport");
            yield break;
        }
        passport.ToggleOpen();
        yield return new WaitForSecondsRealtime(1.5f);
        CustomizationRefs refs = passport.dummy.refs;
        Color lime = options.skins.First(o => o != null && o.name == "Skin_Lime").color;
        foreach (Renderer r in refs.PlayerRenderers.Concat(refs.EyeRenderers))
        {
            r.material.SetColor("_SkinColor", lime);
        }
        string folder = Path.Combine(Paths.BepInExRootPath, "cosmetics");
        Directory.CreateDirectory(folder);
        Camera camera = passport.dummyCamera;
        RenderTexture target = camera.targetTexture;
        RenderTexture shot = target ?? new RenderTexture(512, 512, 24);
        void Save(string file)
        {
            camera.targetTexture = shot;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = shot;
            Texture2D copy = new Texture2D(shot.width, shot.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, shot.width, shot.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = target;
            File.WriteAllBytes(Path.Combine(folder, file), copy.EncodeToPNG());
            Destroy(copy);
        }
        int mouth = Array.FindIndex(options.mouths, m => m != null && m.name == "Mouth_Smile");
        for (int i = 0; i < options.eyes.Length; i++)
        {
            foreach (Renderer r in refs.EyeRenderers)
            {
                r.material.SetTexture(CharacterCustomization.MainTex, options.eyes[i].texture);
            }
            yield return null;
            Save($"face_eyes_{i:00}.png");
        }
        for (int i = 0; i < options.mouths.Length; i++)
        {
            refs.mouthRenderer.material.SetTexture(CharacterCustomization.MainTex, options.mouths[i].texture);
            yield return null;
            Save($"face_mouth_{i:00}.png");
        }
        for (int i = 0; i < options.hats.Length && i < refs.playerHats.Length; i++)
        {
            for (int j = 0; j < refs.playerHats.Length; j++)
            {
                refs.playerHats[j].gameObject.SetActive(i == j);
            }
            yield return null;
            Save($"face_hat_{i:00}.png");
        }
        passport.ToggleOpen();
        Info("the scouts' faces", $"{options.eyes.Length} eyes, {options.mouths.Length} mouths, hats pictured in {folder}");
    }

    /// <summary>A step whose name holds one of the -Only words (every step without -Only).</summary>
    private bool Selected(string name) => only == null || only.Any(word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>A click on a UI element's middle, the way the mouse would (the left button unless told).</summary>
    private static void ClickOn(RectTransform rect, UnityEngine.EventSystems.PointerEventData.InputButton button = UnityEngine.EventSystems.PointerEventData.InputButton.Left)
    {
        Vector3[] corners = Corners(rect);
        Canvas canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
        ClickAt(RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, (corners[0] + corners[2]) * 0.5f), button);
    }

    private static PersistentPlayerData MyData => GameHandler.GetService<PersistentPlayerDataService>().GetPlayerData(PhotonNetwork.LocalPlayer);

    /// <summary>
    /// The challenges and the mod's cosmetics in the airport. The Blind Mode and Hot Mode boxes in the boarding pass's
    /// header: both click on and off, and on ascent 23 Hot Mode shows ticked and cannot be changed. The passport: the Orange Helmet
    /// and the VR Headset are hats 45 and 46 (the ice hoods moved two along), the Orange Spacesuit the last outfit;
    /// locked, a click shows how to unlock them; unlocked (in the mod's save), each is worn on the passport's scout,
    /// saved in the mod's save and not in a Steam stat. Then, for the climb: Blind Mode and Hot Mode on, the Astronaut
    /// suit on, nothing unlocked (smoketest.ps1 puts the mod's save back afterwards).
    /// </summary>
    private IEnumerator AirportCosmetics()
    {
        Info("the mod's save", ModSave.FilePath);
        BoardingPass pass = GUIManager.instance.boardingPass;
        if (!pass.isOpen)
        {
            pass.Open();
        }
        yield return new WaitForSecondsRealtime(1.5f);
        Check("the Blind Mode and Hot Mode boxes are in the boarding pass header", ChallengeModesUi.Shown && ChallengeModesUi.BlindBox != null && ChallengeModesUi.HotBox != null);
        if (!ChallengeModesUi.Shown)
        {
            yield break;
        }
        ChallengeModes.Blind = false;
        ChallengeModes.Hot = false;
        pass.ascentIndex = 10;
        pass.UpdateAscent();
        yield return new WaitForSecondsRealtime(0.3f);
        ClickOn((RectTransform)ChallengeModesUi.BlindBox.transform);
        yield return new WaitForSecondsRealtime(0.3f);
        ClickOn((RectTransform)ChallengeModesUi.HotBox.transform);
        yield return new WaitForSecondsRealtime(0.3f);
        Check("a click ticks Blind Mode and Hot Mode", ChallengeModes.Blind && ChallengeModes.Hot && ChallengeModesUi.BlindBox.isOn && ChallengeModesUi.HotBox.isOn,
            $"Blind Mode {ChallengeModes.Blind} (shown {ChallengeModesUi.BlindBox.isOn}), Hot Mode {ChallengeModes.Hot} (shown {ChallengeModesUi.HotBox.isOn})");
        Screenshot("challenge-boxes");
        yield return new WaitForSecondsRealtime(1f);
        // True Final Ascent puts its light/dark switch left of the airline's logo (its own rule, worked out here: the
        // test runs without TFA); the boxes' rows, which take the clicks, must end before it.
        {
            RectTransform close = pass.closeButton != null ? pass.closeButton.transform as RectTransform : null;
            Image logo = pass.GetComponentsInChildren<Image>(true).FirstOrDefault(i => (i.sprite != null && (i.sprite.name.ToLowerInvariant().Contains("bingbong") || i.sprite.name.ToLowerInvariant().Contains("airline"))) || i.name.ToLowerInvariant().Contains("logo"));
            RectTransform anchor = logo != null ? logo.rectTransform : close;
            Transform layer = close != null ? close.parent : pass.transform;
            float size = close != null ? Mathf.Clamp(close.rect.height, 40f, 90f) : 56f;
            Vector3[] corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            Vector3 middleLeft = layer.InverseTransformPoint((corners[0] + corners[1]) * 0.5f);
            float switchLeft = layer.TransformPoint(new Vector3(middleLeft.x - size * 0.9f - size * 0.5f, middleLeft.y, 0f)).x;
            float rowsRight = new[] { ChallengeModesUi.BlindBox, ChallengeModesUi.HotBox }.Max(t =>
            {
                Vector3[] row = new Vector3[4];
                ((RectTransform)t.transform).GetWorldCorners(row);
                return row[2].x;
            });
            Check("the challenge boxes take the click over their box and name only, ending before where True Final Ascent puts its light/dark switch",
                rowsRight < switchLeft, $"rows end at {rowsRight:0}, TFA's switch would start at {switchLeft:0} (screen x, left of '{anchor.name}')");
        }
        // The map picker clicked like a player: one to the right and back.
        Transform next = MapPicker.Line != null ? MapPicker.Line.transform.Find("UltimateAscent Map Next") : null;
        Transform previous = MapPicker.Line != null ? MapPicker.Line.transform.Find("UltimateAscent Map Previous") : null;
        if (next != null && previous != null)
        {
            int before = MapPicker.Picked;
            (SFX_Instance gameClick, SFX_Instance gameHover) = MapPicker.ButtonSounds(pass);
            SoundsPlayed.Heard.Clear();
            SoundsPlayed.Listening = true;
            UnityEngine.EventSystems.ExecuteEvents.Execute(next.gameObject, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            yield return null;
            bool hovered = gameHover != null && SoundsPlayed.Heard.Contains(gameHover);
            SoundsPlayed.Heard.Clear();
            ClickOn((RectTransform)next);
            yield return null;
            bool clicked = gameClick != null && SoundsPlayed.Heard.Contains(gameClick);
            SoundsPlayed.Listening = false;
            Check("the map picker's arrows have the game's button sounds: the hover sound when pointed at, the click sound when clicked",
                hovered && clicked, $"hover {hovered} ('{gameHover?.name}'), click {clicked} ('{gameClick?.name}')");
            yield return new WaitForSecondsRealtime(0.4f);
            string shown = Plain(MapPicker.Label.text);
            Screenshot("map-picker-next");
            yield return new WaitForSecondsRealtime(1f);
            ClickOn((RectTransform)previous);
            yield return new WaitForSecondsRealtime(0.4f);
            Check("a click on the map picker's arrows goes to the next map and back to today's", before == MapPicker.Today && shown != Plain(MapPicker.Name(before)) && MapPicker.Picked == before,
                $"after the right arrow '{shown}', after the left '{Plain(MapPicker.Label.text)}'");
            Screenshot("map-picker-today");
            yield return new WaitForSecondsRealtime(1f);
            // Held down like a mouse button: the right arrow runs through the maps, faster and faster; letting go (the
            // button's click) adds no step; a quick click after it is one map again.
            {
                List<int> order = MapPicker.Order();
                UnityEngine.EventSystems.PointerEventData press = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                {
                    button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
                    clickCount = 1,
                };
                int start = order.IndexOf(MapPicker.Picked);
                UnityEngine.EventSystems.ExecuteEvents.Execute(next.gameObject, press, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                yield return new WaitForSecondsRealtime(MapPicker.Hold.Delay * 0.75f);
                int beforeDelay = order.IndexOf(MapPicker.Picked);
                yield return new WaitForSecondsRealtime(2f - MapPicker.Hold.Delay * 0.75f);
                Screenshot("map-picker-held");
                int held = order.IndexOf(MapPicker.Picked);
                press.pointerPress = next.gameObject;
                UnityEngine.EventSystems.ExecuteEvents.Execute(next.gameObject, press, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                UnityEngine.EventSystems.ExecuteEvents.Execute(next.gameObject, press, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                yield return new WaitForSecondsRealtime(0.5f);
                int released = order.IndexOf(MapPicker.Picked);
                int ran = ((held - start) % order.Count + order.Count) % order.Count;
                ClickOn((RectTransform)next);
                yield return new WaitForSecondsRealtime(0.3f);
                int clickedOnce = ((order.IndexOf(MapPicker.Picked) - released) % order.Count + order.Count) % order.Count;
                Check("holding a map picker arrow runs through the maps after a short wait; letting go adds no step, a click after it is one map",
                    beforeDelay == start && ran >= 15 && released == held && clickedOnce == 1,
                    $"{order.Count} maps; {beforeDelay - start} step(s) before the wait ended, {ran} in 2 s held ({MapPicker.Name(order[held])}), {released - held} on letting go, {clickedOnce} on the click after");
                MapPicker.Picked = MapPicker.Today;
                pass.UpdateAscent();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            // The die after the arrows: each click a map at random, another than the one shown, with the click sound.
            Transform dice = MapPicker.Line.transform.Find("UltimateAscent Map Dice");
            if (dice == null)
            {
                Fail("the map picker has its die after the arrows");
            }
            else
            {
                Vector3[] arrowCorners = new Vector3[4], diceCorners = new Vector3[4];
                ((RectTransform)next).GetWorldCorners(arrowCorners);
                ((RectTransform)dice).GetWorldCorners(diceCorners);
                List<string> rolls = new List<string>();
                bool allOthers = true;
                SoundsPlayed.Heard.Clear();
                SoundsPlayed.Listening = true;
                for (int roll = 0; roll < 6; roll++)
                {
                    int was = MapPicker.Picked;
                    ClickOn((RectTransform)dice);
                    yield return new WaitForSecondsRealtime(0.35f);
                    allOthers &= MapPicker.Picked != was && Plain(MapPicker.Label.text) == Plain(MapPicker.Name(MapPicker.Picked));
                    rolls.Add(Plain(MapPicker.Label.text));
                    if (roll == 0)
                    {
                        Screenshot("map-picker-dice");
                    }
                }
                SoundsPlayed.Listening = false;
                (SFX_Instance diceClick, _) = MapPicker.ButtonSounds(pass);
                bool sounded = diceClick != null && SoundsPlayed.Heard.Contains(diceClick);
                Check("the die after the map picker's arrows picks a map at random, another than the one shown, with the game's click sound",
                    allOthers && rolls.Distinct().Count() >= 3 && sounded && diceCorners[0].x >= arrowCorners[2].x - 1f,
                    $"six rolls: {string.Join(", ", rolls)}; click sound {sounded}; the die right of the arrow {diceCorners[0].x >= arrowCorners[2].x - 1f}");
                MapPicker.Picked = MapPicker.Today;
                pass.UpdateAscent();
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }
        else
        {
            Fail("the map picker's arrows are on the boarding pass");
        }
        ClickOn((RectTransform)ChallengeModesUi.HotBox.transform);
        yield return new WaitForSecondsRealtime(0.3f);
        Check("another click takes Hot Mode off", !ChallengeModes.Hot && !ChallengeModesUi.HotBox.isOn);
        pass.ascentIndex = 23;
        pass.UpdateAscent();
        yield return new WaitForSecondsRealtime(0.3f);
        ClickOn((RectTransform)ChallengeModesUi.HotBox.transform);
        yield return new WaitForSecondsRealtime(0.3f);
        Check("on ascent 23 Hot Mode shows ticked and cannot be changed", ChallengeModesUi.HotBox.isOn && !ChallengeModesUi.HotBox.interactable && !ChallengeModes.Hot,
            $"shown {ChallengeModesUi.HotBox.isOn}, can be changed {ChallengeModesUi.HotBox.interactable}, own box {ChallengeModes.Hot}");
        Screenshot("challenge-boxes-ascent-23");
        yield return new WaitForSecondsRealtime(1f);
        // The config's "Boarding Pass Extras" off (in memory): no boxes, no map picker, both modes off and today's map
        // although a box is ticked and another map picked; on again, all back as they were.
        {
            ConfigEntry<bool> extras = UltimateAscent.Plugin.BoardingPassExtras;
            bool savedBlind = ModSave.GetBool("blindmode");
            ModSave.SetBool("blindmode", true);
            MapPicker.Picked = MapPicker.Order().First(m => m != MapPicker.Today);
            int picked = MapPicker.Picked;
            pass.ascentIndex = 10;
            pass.UpdateAscent();
            extras.Value = false;
            yield return new WaitForSecondsRealtime(0.4f);
            bool hidden = !ChallengeModesUi.Shown && (MapPicker.Line == null || !MapPicker.Line.activeInHierarchy);
            bool modesOff = !ChallengeModes.Blind && !ChallengeModes.Hot;
            bool todays = MapPicker.Picked == MapPicker.Today;
            Screenshot("boarding-pass-extras-off");
            yield return new WaitForSecondsRealtime(1f);
            extras.Value = true;
            yield return new WaitForSecondsRealtime(0.4f);
            bool back = ChallengeModesUi.Shown && MapPicker.Line != null && MapPicker.Line.activeInHierarchy && ChallengeModes.Blind && MapPicker.Picked == picked;
            Check("with Boarding Pass Extras off the pass shows neither the challenge boxes nor the map picker, Blind Mode is off and the climb on today's map; on again, all as they were",
                hidden && modesOff && todays && back, $"hidden {hidden}, modes off {modesOff}, today's map {todays}; back on: {back} ({MapPicker.Name(MapPicker.Picked)})");
            ModSave.SetBool("blindmode", savedBlind);
            MapPicker.Picked = MapPicker.Today;
        }
        pass.ascentIndex = target;
        pass.UpdateAscent();
        pass.Close();
        yield return new WaitForSecondsRealtime(1f);

        Customization customization = Singleton<Customization>.Instance;
        string savedUnlocks = ModSave.Get("unlocked");
        ModSave.Set("unlocked", "");
        int helmet = ModCosmetics.IndexOf("orangehelmet");
        int vr = ModCosmetics.IndexOf("vrheadset");
        int suit = ModCosmetics.IndexOf("orangespacesuit");
        CustomizationRefs refs = Local.refs.customization.refs;
        string Row(int i) => i >= 0 && i < refs.playerHats.Length ? refs.playerHats[i].name : "none";
        CustomizationOption blue = customization.fits.FirstOrDefault(f => f != null && f.name == "Fit_Bundled_Blue");
        Check("the Orange Helmet and the VR Headset are the last hats, at their models' places; the ice hoods two along",
            helmet == 45 && vr == 46 && customization.hats.Length == 47 && Row(45) == "SpaceHelmet Orange" && Row(46) == "VRHeadset" && Row(47) == "Ice Hood Blue" && blue != null && blue.overrideHatIndex == 47,
            $"hats {helmet}/{vr} of {customization.hats.Length}; models 45-48: {Row(45)}, {Row(46)}, {Row(47)}, {Row(48)}; Bundled Blue wears {blue?.overrideHatIndex}");
        Check("the Orange Spacesuit is the last outfit: the Astronaut suit in orange",
            suit == customization.fits.Length - 1 && customization.fits[suit].fitMaterial != null && customization.fits[suit].fitMaterial.name == "M_Scout_Space_Orange",
            $"outfit {suit} of {customization.fits.Length}, material '{(suit >= 0 ? customization.fits[suit].fitMaterial?.name : "")}'");
        Check("all three are locked, with no badge of their own", helmet >= 0 && vr >= 0 && suit >= 0 && customization.hats[helmet].IsLocked && customization.hats[vr].IsLocked && customization.fits[suit].IsLocked
            && customization.hats[vr].requiredAchievement == ACHIEVEMENTTYPE.NONE && customization.fits[suit].requiredAchievement == ACHIEVEMENTTYPE.NONE);

        PassportManager passport = PassportManager.instance;
        passport.ToggleOpen();
        yield return new WaitForSecondsRealtime(1.5f);
        IEnumerator ToOption(Customization.Type type, int index)
        {
            passport.OpenTab(type);
            yield return new WaitForSecondsRealtime(0.5f);
            for (int page = 0; page < index / 28; page++)
            {
                passport.PageNext();
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
        PassportButton ButtonOf(CustomizationOption option) => passport.buttons.FirstOrDefault(b => b.gameObject.activeInHierarchy && b.currentOption == option);

        yield return ToOption(Customization.Type.Hat, vr);
        PassportButton vrButton = ButtonOf(customization.hats[vr]);
        int hatBefore = MyData.customizationData.currentHat;
        if (vrButton != null)
        {
            ClickOn((RectTransform)vrButton.button.transform);
        }
        yield return new WaitForSecondsRealtime(0.5f);
        string note = ModCosmetics.Requirement.Shown;
        Check("a click on the locked VR Headset tells how to unlock it, and it stays off",
            vrButton != null && vrButton.lockedIcon.gameObject.activeSelf && note != null && note.Contains("ULTIMATE ASCENT:") && note.Contains("Blind Mode") && MyData.customizationData.currentHat == hatBefore,
            $"button {vrButton != null}, note '{note}'");
        Screenshot("passport-locked-vr-headset");
        yield return new WaitForSecondsRealtime(1f);
        PassportButton helmetButton = ButtonOf(customization.hats[helmet]);
        if (helmetButton != null)
        {
            ClickOn((RectTransform)helmetButton.button.transform);
        }
        yield return new WaitForSecondsRealtime(0.5f);
        note = ModCosmetics.Requirement.Shown;
        Check("the locked Orange Helmet tells its own way", note != null && note.Contains("Astronaut suit and helmet") && note.Contains("Hot Mode"), $"note '{note}'");
        Screenshot("passport-locked-orange-helmet");
        yield return new WaitForSecondsRealtime(1f);
        PassportButton gameLocked = passport.buttons.FirstOrDefault(b => b.gameObject.activeInHierarchy && b.currentOption != null && b.currentOption.IsLocked
            && Array.IndexOf(customization.hats, b.currentOption) >= 0 && Array.IndexOf(customization.hats, b.currentOption) < helmet);
        if (gameLocked != null)
        {
            ClickOn((RectTransform)gameLocked.button.transform);
            yield return new WaitForSecondsRealtime(0.5f);
            Check("a click on a locked hat of the game's then takes the note away", ModCosmetics.Requirement.Shown == null, $"clicked '{gameLocked.currentOption.name}', note '{ModCosmetics.Requirement.Shown}'");
        }
        else
        {
            Info("the other-item note check is skipped", "no locked game hat on this page");
        }

        ModSave.Set("unlocked", "vrheadset,orangespacesuit");
        passport.SetButtons();
        yield return new WaitForSecondsRealtime(0.5f);
        NoSteamStatWrites.Asked.Clear();
        vrButton = ButtonOf(customization.hats[vr]);
        if (vrButton != null)
        {
            ClickOn((RectTransform)vrButton.button.transform);
        }
        yield return new WaitForSecondsRealtime(1f);
        Check("unlocked, the VR Headset is worn: on the passport's scout, in the mod's save, no Steam stat",
            MyData.customizationData.currentHat == vr && passport.dummy.refs.playerHats[vr].gameObject.activeSelf && ModSave.Get("hat") == "vrheadset" && !NoSteamStatWrites.Asked.Any(a => a.StartsWith("Cosmetic_Hat", StringComparison.Ordinal)),
            $"hat {MyData.customizationData.currentHat}, passport scout wears {passport.dummy.refs.playerHats.FirstOrDefault(h => h.gameObject.activeSelf)?.name}, saved '{ModSave.Get("hat")}', Steam stats asked: {string.Join(", ", NoSteamStatWrites.Asked)}");
        Screenshot("passport-vr-headset");
        yield return new WaitForSecondsRealtime(1f);

        yield return ToOption(Customization.Type.Fit, suit);
        PassportButton suitButton = ButtonOf(customization.fits[suit]);
        if (suitButton != null)
        {
            ClickOn((RectTransform)suitButton.button.transform);
        }
        yield return new WaitForSecondsRealtime(0.5f);
        yield return ToOption(Customization.Type.Hat, helmet);
        helmetButton = ButtonOf(customization.hats[helmet]);
        if (helmetButton != null)
        {
            ClickOn((RectTransform)helmetButton.button.transform);
        }
        yield return new WaitForSecondsRealtime(1f);
        Check("the Orange Spacesuit and the Orange Helmet are worn", MyData.customizationData.currentOutfit == suit && MyData.customizationData.currentHat == helmet && ModSave.Get("outfit") == "orangespacesuit",
            $"outfit {MyData.customizationData.currentOutfit}, hat {MyData.customizationData.currentHat}, saved '{ModSave.Get("outfit")}'/'{ModSave.Get("hat")}'");
        passport.OpenTab(Customization.Type.Fit);
        yield return new WaitForSecondsRealtime(1f);
        Screenshot("passport-orange-spacesuit");
        yield return new WaitForSecondsRealtime(1f);

        // For the climb: the Astronaut suit and helmet (game ones), both challenges, nothing unlocked yet.
        int astronaut = Array.FindIndex(customization.fits, f => f != null && f.name == ModCosmetics.AstronautFit);
        int astronautHat = Array.FindIndex(customization.hats, h => h != null && h.name == ModCosmetics.AstronautHat);
        CharacterCustomization.SetCharacterOutfit(astronaut);
        CharacterCustomization.SetCharacterHat(0);
        yield return new WaitForSecondsRealtime(0.3f);
        bool suitAlone = CosmeticUnlocks.WearsAstronaut();
        CharacterCustomization.SetCharacterHat(astronautHat);
        passport.dummy.UpdateDummy();
        yield return new WaitForSecondsRealtime(0.5f);
        Check("a game outfit and hat put on again leave the mod's save; only suit and helmet together count as the Astronaut's",
            ModSave.Get("outfit") == "" && ModSave.Get("hat") == "" && !suitAlone && CosmeticUnlocks.WearsAstronaut(),
            $"saved '{ModSave.Get("outfit")}'/'{ModSave.Get("hat")}', the suit with the cap {suitAlone}, with the helmet (hat {astronautHat}) {CosmeticUnlocks.WearsAstronaut()}");
        passport.ToggleOpen();
        ChallengeModes.Blind = true;
        ChallengeModes.Hot = true;
        ModSave.Set("unlocked", "");
        Info("for the climb", $"Blind Mode, Hot Mode and the Astronaut suit and helmet on, nothing unlocked (the save had '{savedUnlocks}'); Blind Mode does not darken the screen until its own checks");
        yield return new WaitForSecondsRealtime(1f);
    }

    /// <summary>
    /// Before any climb (the title screen, like the developers' logos before it) Blind Mode does not darken the
    /// screen: it is no climb, though neither the title nor the airport by name.
    /// </summary>
    private IEnumerator ClearBeforeTheClimb()
    {
        bool saved = ChallengeModes.Blind;
        BlindModeUnseen.Shown = true;
        Info("Blind Mode in the player's save", saved.ToString());
        ChallengeModes.Blind = true;
        yield return new WaitForSecondsRealtime(0.5f);
        GameObject prefab = Resources.Load<GameObject>("Character_Scoutmaster");
        Scoutmaster template = prefab != null ? prefab.GetComponentInChildren<Scoutmaster>(true) : null;
        float strength = template != null && template.mat != null ? template.mat.GetFloat("_Strength") : -1f;
        Check("with Blind Mode on, the title screen (no climb) stays clear", !ChallengeModes.Blinding && strength >= 0f && strength < 0.05f,
            $"darkening {ChallengeModes.Blinding}, the Scoutmaster's screen effect at {strength:0.000}, scene '{SceneManager.GetActiveScene().name}', a climb by the scene's name {Rules.InRun}");
        Screenshot("title-with-blind-mode");
        ChallengeModes.Blind = saved;
        BlindModeUnseen.Shown = false;
    }

    /// <summary>
    /// The challenges in the climb (the last step: it ends the climb). Blind Mode keeps the Scoutmaster's screen effect at
    /// its strongest. Hot Mode: below ascent 23 an open parasol held up in the sun roasts (without the sun heating), from
    /// 23 it adds nothing. Then the game's own debug win (as at the peak): the VR Headset and the orange pieces unlock,
    /// the statistics show each as a small icon where badges go and the game's unlock window for each. Last, Blind Mode
    /// off clears the screen.
    /// </summary>
    private IEnumerator ChallengesInClimb()
    {
        BlindModeUnseen.Shown = true;
        Info("the challenges this climb", CosmeticUnlocks.State);
        Scoutmaster scoutmaster = FindAnyObjectByType<Scoutmaster>();
        Material effect = scoutmaster != null ? scoutmaster.mat : null;
        yield return new WaitForSeconds(1f);
        float strength = effect != null ? effect.GetFloat("_Strength") : -1f;
        Check("Blind Mode keeps the screen as dark as the Scoutmaster makes it at his closest", ChallengeModes.Blinding && strength >= 0.999f,
            $"Blind Mode {ChallengeModes.Blind}, the Scoutmaster's screen effect at {strength:0.000} (Scoutmaster in the level {scoutmaster != null})");
        Screenshot("blind-mode");
        if (scoutmaster != null)
        {
            // The game clears the effect when a Scoutmaster is switched off: read it in the same moment.
            scoutmaster.gameObject.SetActive(false);
            float afterLeaving = effect.GetFloat("_Strength");
            scoutmaster.gameObject.SetActive(true);
            Check("when the Scoutmaster is switched off, Blind Mode keeps the screen dark in that same moment", afterLeaving >= 0.999f, $"{afterLeaving:0.000} right after");
        }
        yield return new WaitForSeconds(2f);
        Vector2 look = Local.data.lookValues;
        Local.data.lookValues = new Vector2(look.x, -45f);
        yield return new WaitForSeconds(1.5f);
        Screenshot("blind-mode-looking-down");
        yield return new WaitForSeconds(1f);
        Local.data.lookValues = look;

        Character local = Local;
        if (Rules.Level >= Lvl.HotSun)
        {
            Check("from ascent 23 Hot Mode adds nothing (that ascent's own sun roasts)", !ChallengeModes.HotRoasts, $"ascent {Rules.Level}");
        }
        else
        {
            yield return SpawnIntoHand(ItemByName("Parasol"));
            Item item = held;
            Parasol parasol = item != null ? item.GetComponent<Parasol>() : null;
            DayNightManager clock = DayNightManager.instance;
            clock.timeOfDay = (clock.dayStart + clock.dayEnd) / 2f;
            yield return new WaitForSeconds(2f);
            if (!InSunlight(local))
            {
                yield return IntoSunlight(40f);
            }
            if (parasol == null || !InSunlight(local))
            {
                Info("the Hot Mode roasting check is skipped", $"parasol {parasol != null}, in direct sunlight {InSunlight(local)}");
            }
            else
            {
                if (!parasol.isOpen)
                {
                    parasol.ToggleOpen();
                }
                Protect(false);
                Meter heat = new Meter(Status.Hot);
                float start = Time.time;
                try
                {
                    yield return new WaitForSeconds(3f);
                }
                finally
                {
                    heat.Stop();
                    Protect(true);
                }
                float seconds = Time.time - start;
                float expected = seconds / (Map.InMesa ? 15f : 30f);
                Check($"Hot Mode on ascent {Rules.Level}: an open parasol in the sun roasts as on ascent 23 (and keeps the heat off)",
                    ChallengeModes.HotRoasts && Mathf.Abs(Roasted(item) - expected) < expected * 0.2f && heat.Total < 0.001f,
                    $"{Roasted(item):0.000} roasted in {seconds:F1} s (expected {expected:0.000}), {heat.Total:0.0000} heat");
                Screenshot("hot-mode-parasol");
                parasol.ToggleOpen();
                local.refs.items.EquipSlot(Optionable<byte>.None);
                yield return new WaitForSeconds(0.5f);
                if (InSunlight(local))
                {
                    Protect(false);
                    Meter bare = new Meter(Status.Hot);
                    try
                    {
                        yield return new WaitForSeconds(3f);
                    }
                    finally
                    {
                        bare.Stop();
                        Protect(true);
                    }
                    // The sun's heat lands as it comes (no ascent 14 boost).
                    float share = A23_HotSun.HeatPerSecond * 0.65f * bare.Seconds;
                    Check($"Hot Mode on ascent {Rules.Level}: the sun heats at 65% of ascent 23's strength",
                        Mathf.Abs(bare.Total - share) <= share * 0.15f, $"{bare.Total * 100f:0.###} heat in {bare.Seconds:F1} s (expected {share * 100f:0.###})");
                }
                else
                {
                    Info("the Hot Mode heat check is skipped", "not in direct sunlight");
                }
            }
            local.refs.items.EquipSlot(Optionable<byte>.None);
            yield return new WaitForSeconds(0.5f);
        }

        Info("the challenges before the peak", CosmeticUnlocks.State);
        for (float until = Time.time + 30f; Time.time < until && (!local.refs.stats.IsInitialized || local.refs.stats.timelineInfo.Count == 0);)
        {
            yield return null;
        }
        Character.TestWin();
        EndScreen screen = null;
        for (float until = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < until && (screen == null || !screen.isOpen);)
        {
            screen = GUIManager.instance != null ? GUIManager.instance.endScreen : null;
            yield return null;
        }
        Check("the win unlocks the VR Headset and the orange pieces", CosmeticUnlocks.Has(CosmeticUnlocks.VrHeadset) && CosmeticUnlocks.Has(CosmeticUnlocks.OrangeSpacesuit),
            $"unlocked '{ModSave.Get("unlocked")}', won {local.refs.stats.won}");
        if (screen == null || !screen.isOpen)
        {
            Fail("the climb's statistics open");
            yield break;
        }
        int windows = 0;
        for (float until = Time.realtimeSinceStartup + 90f; Time.realtimeSinceStartup < until && !screen.buttons.activeSelf;)
        {
            if (screen.cosmeticUnlockObject.activeSelf && screen.inPopupView)
            {
                windows++;
                yield return new WaitForSecondsRealtime(1.5f);
                Screenshot($"statistics-unlock-{windows}");
                yield return new WaitForSecondsRealtime(1f);
                screen.PopupNext();
                yield return new WaitForSecondsRealtime(0.6f);
            }
            else if ((screen.ascentsUnlockObject.activeSelf || screen.promotionUnlockObject.activeSelf) && screen.inPopupView)
            {
                screen.PopupNext();
                yield return new WaitForSecondsRealtime(0.6f);
            }
            yield return null;
        }
        int ours = 0;
        foreach (Transform icon in screen.badgeParentTF)
        {
            if (icon.name.StartsWith("UltimateAscentCosmetic", StringComparison.Ordinal))
            {
                ours++;
            }
        }
        yield return new WaitForSecondsRealtime(1f);
        Screenshot("statistics-icons");
        Check("the statistics show the three as small icons where badges go, each with the game's unlock window", ours == 3 && windows >= 3, $"{ours} icons, {windows} unlock windows");
        ChallengeModes.Blind = false;
        yield return new WaitForSecondsRealtime(0.5f);
        Check("Blind Mode off clears the screen", effect == null || effect.GetFloat("_Strength") < 0.05f, $"{(effect != null ? effect.GetFloat("_Strength") : -1f):0.000}");
    }

    private static IEnumerator Now(Action check)
    {
        check();
        yield break;
    }

    /// <summary>
    /// What the Custom Run switches decide in a custom run, checked within one frame of this run: the
    /// game's custom-run flag and the two switches are set in memory (nothing is saved) and put back.
    /// </summary>
    private void CustomRunSwitches()
    {
        Item chalk = ItemByName(ClimbingChalkItem.PrefabName);
        Item roots = ItemByName(A14_Items.RootsParasolPrefab);
        Item parasol = ItemByName("Parasol");
        if (chalk == null || roots == null || parasol == null)
        {
            Fail("custom-run switches", "an item is missing from the item database");
            return;
        }
        Check("in this run (ascent 9+) the chalk may spawn and the Roots parasol is the Lightweight Parasol", chalk.IsValidToSpawn() && Plain(roots.GetName()) == "LIGHTWEIGHT PARASOL",
            $"chalk may spawn {chalk.IsValidToSpawn()}, Roots parasol '{Plain(roots.GetName())}'");
        // Item Stats shows the held item's raw weight field plus the run's weight setting, in steps of 2.5%.
        int expectedWeight = Rules.Enabled(Lvl.WorseItems) ? A14_Items.WorseLightweightParasolWeight : 0;
        int rootsOwn = roots.carryWeight;
        int rootsShown;
        Compat.ItemFigures.Begin(roots, weight: true);
        try
        {
            rootsShown = roots.carryWeight + Ascents.itemWeightModifier;
        }
        finally
        {
            Compat.ItemFigures.End();
        }
        Check($"the Lightweight Parasol weighs {expectedWeight * 2.5f}% of the bar, and Item Stats shows that (nothing below 15, 5% from 15)",
            roots.CarryWeight == expectedWeight && rootsShown == expectedWeight && roots.carryWeight == rootsOwn,
            $"weight {roots.CarryWeight * 2.5f}%, Item Stats shown {rootsShown * 2.5f}%, the item's own field {roots.carryWeight} (was {rootsOwn})");
        string[] names = { chalk.gameObject.name, roots.gameObject.name, parasol.gameObject.name };
        int[] saved = names.Select(n => RunSettings.GetItemValue(n, forceGetCustomValue: true)).ToArray();
        bool wasCustom = RunSettings.IsCustomRun;
        try
        {
            RunSettings.IsCustomRun = true;
            bool Spawns(Item item, int chalkOn, int rootsOn, int parasolOn)
            {
                RunSettings.SetItemValue(names[0], chalkOn);
                RunSettings.SetItemValue(names[1], rootsOn);
                RunSettings.SetItemValue(names[2], parasolOn);
                return item.IsValidToSpawn();
            }
            Check("custom run: the chalk spawns when its switch is on, not when it is off", Spawns(chalk, 1, 1, 1) && !Spawns(chalk, 0, 1, 1),
                $"on {Spawns(chalk, 1, 1, 1)}, off {Spawns(chalk, 0, 1, 1)}");
            Check("custom run: the Lightweight Parasol follows its own switch, not the Parasol's", Spawns(roots, 1, 1, 0) && !Spawns(roots, 1, 0, 1) && Spawns(parasol, 1, 0, 1),
                $"own on + Parasol off {Spawns(roots, 1, 1, 0)}, own off + Parasol on {Spawns(roots, 1, 0, 1)}, the Parasol itself {Spawns(parasol, 1, 0, 1)}");
            Check("custom run: the Roots parasol is the Lightweight Parasol (its name, no weight)", Plain(roots.GetName()) == "LIGHTWEIGHT PARASOL" && roots.CarryWeight == 0,
                $"'{Plain(roots.GetName())}', weight {roots.CarryWeight}");
            // Item Stats reads the held item's raw weight field (plus the run's weight setting).
            int own = roots.carryWeight;
            int shown;
            Compat.ItemFigures.Begin(roots, weight: true);
            try
            {
                shown = roots.carryWeight + Ascents.itemWeightModifier;
            }
            finally
            {
                Compat.ItemFigures.End();
            }
            Check("custom run: Item Stats is shown the Lightweight Parasol weighing nothing", shown == 0 && roots.carryWeight == own,
                $"shown weight {shown}, the item's own {roots.carryWeight} (was {own})");
        }
        finally
        {
            for (int i = 0; i < names.Length; i++)
            {
                RunSettings.SetItemValue(names[i], saved[i]);
            }
            RunSettings.IsCustomRun = wasCustom;
        }
    }

    /// <summary>
    /// A custom run's suitcases, opened the way a scout opens them, with one item switched on: the
    /// switches and the custom-run flag are set in memory only while each suitcase fills (nothing is
    /// saved). With only the Blowgun on, a solo run gets Bing Bong: the game never hands a Blowgun out
    /// in solo, and a suitcase with nothing it may give holds Bing Bong. With only the Energy Drink on,
    /// it holds Energy Drinks.
    /// </summary>
    private IEnumerator CustomRunSuitcases()
    {
        Item blowgun = ItemByName("HealingDart Variant");
        Item drink = ItemByName("Energy Drink");
        Item bingBong = ItemByName("BingBong");
        if (blowgun == null || drink == null || bingBong == null)
        {
            Fail("custom-run suitcases", "the Blowgun, the Energy Drink or Bing Bong is missing from the item database");
            yield break;
        }
        List<Item> listed = RunSettings.ModifiableItemPrefabs ?? new List<Item>();
        string Saved(Item item) => item != null && RunSettings.GetItemValue(item, forceGetCustomValue: true) == 1 ? "on" : "off";
        Info("the saved Custom Run switches", $"{listed.Count(i => i != null && RunSettings.GetItemValue(i, forceGetCustomValue: true) == 1)} of the {listed.Count} listed items on; "
            + $"the Blowgun {Saved(blowgun)}, the Energy Drink {Saved(drink)}, the Climbing Chalk {Saved(ItemByName(ClimbingChalkItem.PrefabName))}");
        LootData blowgunLoot = blowgun.GetComponent<LootData>();
        yield return SuitcaseInCustomRun(blowgun, "custom-run-blowgun", spawned =>
            Check("custom run, only the Blowgun on, solo: the suitcase holds Bing Bong (the game bans the Blowgun in solo)",
                spawned.Count > 0 && spawned.All(i => i.itemID == bingBong.itemID) && blowgunLoot != null && blowgunLoot.banInSolo && PhotonNetwork.OfflineMode,
                $"{Names(spawned)}; the Blowgun banned in solo {blowgunLoot != null && blowgunLoot.banInSolo}, solo {PhotonNetwork.OfflineMode}"));
        yield return SuitcaseInCustomRun(drink, "custom-run-energy-drink", spawned =>
            Check("custom run, only the Energy Drink on: the suitcase holds Energy Drinks", spawned.Count > 0 && spawned.All(i => i.itemID == drink.itemID), Names(spawned)));
    }

    /// <summary>
    /// The Lightweight Parasol's own look (from ascent 9), next to a Parasol: both put down closed in
    /// front of the scout, then the Lightweight Parasol held and opened, then the one on the ground
    /// cooked once. Its canopy has the user's stripes and its own material's neutral tint (the frame
    /// keeps the game's), its icon is its own, cooking darkens it from that tint, and the Parasol keeps
    /// the game's look.
    /// </summary>
    private IEnumerator ParasolLook()
    {
        Item roots = ItemByName(A14_Items.RootsParasolPrefab);
        Item parasol = ItemByName("Parasol");
        if (roots == null || parasol == null)
        {
            Fail("the Parasol and the Roots parasol are in the item database");
            yield break;
        }
        Character local = Local;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized * 0.45f;
        Vector3 spot = local.Center + forward * 1.4f + Vector3.up * 0.3f;
        Quaternion facing = Quaternion.LookRotation(-forward);
        Item light = PhotonNetwork.InstantiateItemRoom(roots.gameObject.name, spot + side, facing).GetComponent<Item>();
        Item plain = PhotonNetwork.InstantiateItemRoom(parasol.gameObject.name, spot - side, facing).GetComponent<Item>();
        Vector2 look = local.data.lookValues;
        local.data.lookValues = new Vector2(Quaternion.LookRotation(forward).eulerAngles.y, -35f);
        yield return new WaitForSeconds(4f);
        Screenshot("lightweight-parasol-next-to-parasol");
        const string OwnName = "(Lightweight Parasol)";
        int OwnCanopies(Item item) => item.GetComponentsInChildren<MeshFilter>(true).Count(f => f.sharedMesh != null && f.sharedMesh.name.EndsWith(OwnName));
        int OwnMaterials(Item item) => item.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("M_Parasol") && r.sharedMaterial.name.Contains(OwnName));
        Color Tint(Item item)
        {
            MeshRenderer canopy = item.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.TryGetComponent(out MeshFilter f) && f.sharedMesh != null && f.sharedMesh.name.StartsWith("Parasol.Close"));
            return canopy != null ? canopy.material.GetColor("_Tint") : Color.clear;
        }
        bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
        Step("Lightweight Parasol look", () =>
        {
            Texture2D lightIcon = light.UIData.GetIcon();
            Texture2D prefabIcon = roots.UIData.GetIcon();
            Texture2D plainIcon = parasol.UIData.GetIcon();
            Check("the Lightweight Parasol has its own canopy (open and closed) and canopy material, the frame keeps the game's (9)",
                OwnCanopies(light) == 2 && OwnMaterials(light) == 2 && light.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("M_Parasol") && !r.sharedMaterial.name.Contains(OwnName)) == 1
                && Near(Tint(light), LightweightParasolLook.CanopyTint),
                $"own canopies {OwnCanopies(light)}, own materials {OwnMaterials(light)}, canopy tint {Tint(light)}");
            Check("the Lightweight Parasol has its own icon, on the ground and in the inventory (9)",
                lightIcon != null && lightIcon.name == "LightweightParasol_icon" && prefabIcon == lightIcon,
                $"icon '{(lightIcon != null ? lightIcon.name : "none")}', the prefab's '{(prefabIcon != null ? prefabIcon.name : "none")}'");
            Check("the Parasol keeps the game's look and icon", OwnCanopies(plain) == 0 && OwnMaterials(plain) == 0 && plainIcon != null && plainIcon.name == "Parasol",
                $"own canopies {OwnCanopies(plain)}, own materials {OwnMaterials(plain)}, icon '{(plainIcon != null ? plainIcon.name : "none")}'");
        });

        // Held and opened, the way a scout opens it (holding the use for its 0.2 s).
        local.data.lookValues = look;
        yield return SpawnIntoHand(roots);
        if (held != null)
        {
            Parasol canopy = held.GetComponent<Parasol>();
            yield return new WaitForSeconds(1f);
            Screenshot("lightweight-parasol-held");
            held.StartUsePrimary();
            float end = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < end && held != null && !held.finishedCast)
            {
                held.ContinueUsePrimary();
                yield return null;
            }
            held.CancelUsePrimary();
            local.data.lookValues = new Vector2(look.x, 35f);
            yield return new WaitForSeconds(1f);
            Screenshot("lightweight-parasol-open");
            Check("the held Lightweight Parasol opens with its own canopy", canopy != null && canopy.isOpen && OwnCanopies(held) == 2,
                $"open {canopy != null && canopy.isOpen}, own canopies {OwnCanopies(held)}");
            local.data.lookValues = look;
        }

        // Cooked once on the ground: darker, from its own tint.
        local.data.lookValues = new Vector2(Quaternion.LookRotation(forward).eulerAngles.y, -35f);
        light.cooking.FinishCooking();
        yield return new WaitForSeconds(0.5f);
        Screenshot("lightweight-parasol-cooked");
        Color cooked = Tint(light);
        Check("cooking darkens the Lightweight Parasol's canopy from its own tint", Near(cooked, LightweightParasolLook.CanopyTint * ItemCooking.GetCookColor(1)),
            $"canopy tint {cooked}, expected {LightweightParasolLook.CanopyTint * ItemCooking.GetCookColor(1)}");
        local.data.lookValues = look;
        foreach (Item item in new[] { light, plain })
        {
            if (item != null && item.photonView != null)
            {
                PhotonNetwork.Destroy(item.gameObject);
            }
        }
    }

    /// <summary>
    /// Lit lanterns warm and heal the scouts near them: the Lantern takes cold off, the Faerie Lantern
    /// cold, injury, poison, drowsiness and heat; from ascent 15 at 1.5 and 2.5 a second (the game: 2.5
    /// and 5). Each is held and lit the way a scout lights it (the use held for its 0.5 s). The Faerie
    /// Lantern is measured on injury, which nothing else heals; the Lantern, which only warms, on cold,
    /// less what the same spot takes off while it is unlit (the start's fires warm a little).
    /// </summary>
    private IEnumerator Lanterns()
    {
        // Ascent 15's rule on (in memory) from ascent 15: it sets a lantern's warmth when the lantern comes into being.
        ConfigEntry<bool> worseRule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool saved = worseRule.Value;
        bool worse = Rules.Level >= Lvl.WorseItems;
        worseRule.Value = worse || saved;
        HostOptions.Invalidate();
        Action undo = CalmForMeasuring();
        try
        {
            yield return LanternCase("Lantern", worse ? 2f : 2.5f, false);
            yield return LanternCase("Lantern_Faerie", worse ? 2.5f : 5f, true);
            yield return LanternCase(TorchItem.PrefabName, worse ? 2f : 2.5f, false);
        }
        finally
        {
            undo();
            worseRule.Value = saved;
            HostOptions.Invalidate();
        }
    }

    private IEnumerator LanternCase(string prefabName, float perSecond, bool faerie)
    {
        Item prefab = ItemByName(prefabName);
        if (prefab == null)
        {
            Fail("the " + prefabName + " is in the item database");
            yield break;
        }
        yield return SpawnIntoHand(prefab);
        if (held == null)
        {
            yield break;
        }
        Item lantern = held;
        string name = Plain(lantern.GetItemName());
        Status measured = faerie ? Status.Injury : Status.Cold;
        const float Seconds = 8f;
        Protect(false);

        // Unlit: what this spot takes off by itself.
        float baseline = 0f;
        if (!faerie)
        {
            Afflictions.SetStatus(measured, 0.4f);
            yield return null;
            float before = Afflictions.GetCurrentStatus(measured);
            float from = Time.time;
            yield return new WaitForSeconds(Seconds);
            baseline = (before - Afflictions.GetCurrentStatus(measured)) * 100f / (Time.time - from);
        }

        lantern.StartUsePrimary();
        float end = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < end && lantern != null && !lantern.finishedCast)
        {
            lantern.ContinueUsePrimary();
            yield return null;
        }
        lantern.CancelUsePrimary();
        yield return new WaitForSeconds(1.5f);
        StatusField field = lantern.GetComponentsInChildren<StatusField>(true).FirstOrDefault();
        Afflictions.SetStatus(measured, 0.4f);
        yield return null;
        float value = Afflictions.GetCurrentStatus(measured);
        float started = Time.time;
        yield return new WaitForSeconds(Seconds);
        float seconds = Time.time - started;
        float lit = (value - Afflictions.GetCurrentStatus(measured)) * 100f / seconds;
        Screenshot("lantern-" + prefabName.ToLowerInvariant());
        Afflictions.SetStatus(measured, 0f);
        Protect(true);
        // The bar takes statuses off in 2.5-point steps, so each 8 s can be one step off.
        float slack = (faerie ? 1 : 2) * 2.5f / seconds + 0.05f;
        float rate = lit - baseline;
        bool on = field != null && field.gameObject.activeInHierarchy;
        Check($"a lit {name} takes {perSecond:0.#} {(faerie ? "injury (and of each other status)" : "cold")} a second off the scouts near it (15)",
            on && Mathf.Abs(field.statusAmountPerSecond + perSecond / 100f) < 0.0001f && Mathf.Abs(rate - perSecond) <= slack,
            $"lit {on}, field {(field != null ? field.statusAmountPerSecond * 100f : 0f):0.##}/s, {measured.ToString().ToLowerInvariant()} {lit:0.00}/s while lit"
            + (faerie ? "" : $", {baseline:0.00}/s unlit: the lantern's {rate:0.00}/s") + $" over {seconds:0.0} s");
        if (lantern != null && lantern.gameObject.activeInHierarchy)
        {
            lantern.GetComponent<Lantern>()?.SnuffLantern();
        }
    }

    /// <summary>
    /// The config's ascent switches: the file has one per ascent and none of the retired entries, and
    /// switching one ascent off (in memory; nothing is saved) leaves out only that ascent's rule while the
    /// climb stays on its ascent. With ascent 15 off, a new Lantern warms at the game's 2.5 again.
    /// </summary>
    private IEnumerator AscentSwitches()
    {
        string file = UltimateAscent.Plugin.Instance.Config.ConfigFilePath;
        string text = File.Exists(file) ? File.ReadAllText(file) : "";
        int switches = Enumerable.Range(Lvl.First, Lvl.Count).Count(level => text.Contains("Ascent " + level + " = "));
        Check("the config has a switch for each of ascents 9-25 and none of the retired entries",
            switches == Lvl.Count && !text.Contains("[Big Lobbies]") && !text.Contains("Nerfed Revives") && !text.Contains("Double Apocalypse Weather"),
            $"{switches} switches, Big Lobbies {text.Contains("[Big Lobbies]")}, Double Apocalypse Weather {text.Contains("Double Apocalypse Weather")}");
        // Not Ascent 20: its rule is held back for the climb (Bing Bong's crab), see HoldBingBongRule.
        int off = target >= Lvl.HotSun ? Lvl.UniqueAilments : Lvl.Drowsy;
        BepInEx.Configuration.ConfigEntry<bool> entry = UltimateAscent.Plugin.AscentSwitches[off];
        bool saved = entry.Value;
        Step("one ascent switched off", () =>
        {
            try
            {
                entry.Value = false;
                Check($"with Ascent {off} switched off, the climb on ascent {target} leaves out only that ascent's rule",
                    !Rules.Enabled(off) && Rules.Enabled(off - 1) && (target == off || Rules.Enabled(off + 1)) && Ascents._currentAscent == target && Rules.InExtraAscent,
                    $"rule {off - 1} {Rules.Enabled(off - 1)}, rule {off} {Rules.Enabled(off)}, rule {off + 1} {Rules.Enabled(off + 1)}, ascent {Ascents._currentAscent}");
            }
            finally
            {
                entry.Value = saved;
            }
            Check($"Ascent {off} switched on again applies again", Rules.Enabled(off) == saved);
        });
        if (target < Lvl.WorseItems)
        {
            yield break;
        }
        Item lanternPrefab = ItemByName("Lantern");
        BepInEx.Configuration.ConfigEntry<bool> worse = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool savedWorse = worse.Value;
        GameObject spawned = null;
        try
        {
            worse.Value = false;
            spawned = lanternPrefab != null ? SpawnInFront(lanternPrefab, 1.2f) : null;
        }
        finally
        {
            worse.Value = savedWorse;
        }
        yield return new WaitForSeconds(0.5f);
        StatusField field = spawned != null ? spawned.GetComponentsInChildren<StatusField>(true).FirstOrDefault() : null;
        Check("a Lantern made while Ascent 15 is switched off warms at the game's 2.5 a second", field != null && Mathf.Abs(field.statusAmountPerSecond + 0.025f) < 0.0001f,
            field == null ? "no Lantern" : $"field {field.statusAmountPerSecond * 100f:0.##}/s");
        if (spawned != null)
        {
            PhotonNetwork.Destroy(spawned);
        }
    }

    /// <summary>
    /// The config's Cold Nights switch, on the beach with the clock put at night: switched on, the night
    /// itself makes the scout cold (the game's rule from ascent 5); switched off (in memory, then back),
    /// nothing does. The clock goes back afterwards.
    /// </summary>
    private IEnumerator ColdNightsSwitch()
    {
        DayNightManager clock = DayNightManager.instance;
        BepInEx.Configuration.ConfigEntry<bool> entry = UltimateAscent.Plugin.ColdNights;
        string file = UltimateAscent.Plugin.Instance.Config.ConfigFilePath;
        bool inFile = File.Exists(file) && File.ReadAllText(file).Contains("Cold Nights = ");
        Check("the config has the Cold Nights switch", entry != null && inFile, $"bound {entry != null}, in the file {inFile}");
        if (clock == null || entry == null)
        {
            yield break;
        }
        float original = clock.timeOfDay;
        bool saved = entry.Value;
        bool coldOn = false;
        bool coldOff = true;
        Meter on = null;
        Meter off = null;
        Shield(true);
        try
        {
            clock.timeOfDay = Mathf.Repeat(clock.dayEnd + 2f, 24f);
            entry.Value = true;
            yield return null;
            coldOn = Ascents.isNightCold;
            on = new Meter(Status.Cold);
            yield return new WaitForSeconds(6f);
            on.Stop();
            entry.Value = false;
            yield return null;
            coldOff = Ascents.isNightCold;
            off = new Meter(Status.Cold);
            yield return new WaitForSeconds(6f);
            off.Stop();
        }
        finally
        {
            entry.Value = saved;
            clock.timeOfDay = original;
        }
        Check("with Cold Nights on, the night itself makes the scout cold (the game's rule from ascent 5)", coldOn && on.Total > 0f,
            $"night cold {coldOn}: {on.Total * 100f:0.##} cold in {on.Seconds:0.#} s at {clock.FloatToTimeString(Mathf.Repeat(clock.dayEnd + 2f, 24f))}");
        Check("with Cold Nights off, the night itself makes nobody cold", !coldOff && off.Total == 0f,
            $"night cold {coldOff}: {off.Total * 100f:0.##} cold in {off.Seconds:0.#} s");
        Check("the Cold Nights switch set back applies again", Ascents.isNightCold == saved, $"switch {entry.Value}, night cold {Ascents.isNightCold}");
        Protect(true);
    }

    /// <summary>
    /// A Magic Bean's vine (15), two side by side 5 m apart in the shallow sea behind the scouts' spawn
    /// point, where nothing stands. Each is grown the way a bean that took root there grows it (the
    /// bean's own vine length, then the game's vine): the first with Ascent 15 switched off (in
    /// memory), so it is the game's vine, the second under ascent 15's rule. Once both have grown each is
    /// measured against what the game grows on its spot, and the scout watches both tops standing in the
    /// water 18 m closer to the shore, looking out to sea (and is put back afterwards).
    /// </summary>
    private IEnumerator BeanVineLength()
    {
        Item prefab = ItemByName("MagicBean");
        MagicBean bean = prefab != null ? prefab.GetComponent<MagicBean>() : null;
        MagicBeanVine vinePrefab = bean != null ? bean.plantPrefab : null;
        if (vinePrefab == null)
        {
            Fail("a Magic Bean and its vine are in the item database");
            yield break;
        }
        if (!Rules.Enabled(Lvl.WorseItems))
        {
            Info("the Magic Bean check is skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        float longest = vinePrefab.maxLength;
        const float Apart = 5f;
        const float Back = 18f;
        // In the sea behind the scouts' spawn point, where nothing stands: the scout watches from the
        // first water deep enough to stand in, looking out to sea at two spots 5 m apart 18 m further out.
        SpawnPoint spawn = SpawnPoint.allSpawnPoints.FirstOrDefault(p => p != null);
        if (spawn == null || !Ground(spawn.transform.position, out RaycastHit beach))
        {
            Info("the Magic Bean check is skipped", "no spawn point to plant behind");
            yield break;
        }
        Vector3 back = Vector3.ProjectOnPlane(-spawn.transform.forward, Vector3.up).normalized;
        float? sea = SeaLevel(spawn.transform.position + back * 60f);
        float under = sea ?? beach.point.y - 2f;
        Vector3 wade = Vector3.zero;
        for (float d = 3f; d <= 80f; d += 0.5f)
        {
            if (Ground(beach.point + back * d, out RaycastHit floor) && floor.point.y < under - 0.6f)
            {
                wade = floor.point + back * 1f;
                break;
            }
        }
        Vector3 side = Vector3.Cross(Vector3.up, back).normalized;
        Vector3 row = wade + back * Back;
        if (wade == Vector3.zero || !Ground(row - side * Apart * 0.5f, out RaycastHit a) || !Ground(row + side * Apart * 0.5f, out RaycastHit b)
            || !Ground(wade, out RaycastHit w))
        {
            Info("the Magic Bean check is skipped", $"no shallow sea found behind the spawn point (sea level {(sea.HasValue ? sea.Value.ToString("0.0") : "unknown")})");
            yield break;
        }
        Vector3 gameSpot = a.point;
        Vector3 modSpot = b.point;
        Vector3 watch = w.point;
        Info("Magic Bean vines in the sea", $"sea level {(sea.HasValue ? sea.Value.ToString("0.0") : $"unknown (beach - 2 m: {under:0.0})")}, sea floor {a.point.y:0.0} and {b.point.y:0.0} under the vines, {w.point.y:0.0} where the scout watches from, {Vector3.Distance(watch, row):0} m away");
        Vector3 home = local.Center;
        Vector2 look = local.data.lookValues;
        BepInEx.Configuration.ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool ruleSaved = rule.Value;
        MagicBeanVine gameVine = null;
        MagicBeanVine modVine = null;
        try
        {
            rule.Value = false;
            gameVine = GrowVine(bean, gameSpot, a.normal);
            rule.Value = ruleSaved;
            modVine = GrowVine(bean, modSpot, b.normal);
        }
        finally
        {
            rule.Value = ruleSaved;
        }
        if (gameVine == null || modVine == null)
        {
            Fail("both Magic Bean vines grow in the sea", $"the game's vine {gameVine != null}, the mod's {modVine != null}");
            foreach (MagicBeanVine vine in new[] { gameVine, modVine }.Where(v => v != null))
            {
                PhotonNetwork.Destroy(vine.gameObject);
            }
            yield break;
        }
        for (float until = Time.time + 15f; Time.time < until && (gameVine._isGrowing || modVine._isGrowing);)
        {
            yield return null;
        }
        // Stepped back to see both tops; long enough for the area's title, which the move brings up, to fade.
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, watch + Vector3.up * 1.5f, false);
        yield return new WaitForSeconds(7f);
        Vector3 between = (gameVine.transform.position + modVine.transform.position) * 0.5f;
        Vector3 toward = Vector3.ProjectOnPlane(between - local.Head, Vector3.up);
        float aim = Mathf.Atan2(longest * 0.5f - (local.Head.y - between.y), toward.magnitude) * Mathf.Rad2Deg;
        local.data.lookValues = new Vector2(Quaternion.LookRotation(toward).eulerAngles.y, aim);
        yield return new WaitForSeconds(1.5f);
        Screenshot("magic-bean-vines");
        // The screenshot is written a few frames later; the vines stay until then.
        yield return new WaitForSeconds(1.5f);
        foreach ((string name, MagicBeanVine vine, float expected) in new[]
        {
            ("with Ascent 15 off (the game's)", gameVine, VineReach(gameVine.transform.position, gameVine.transform.up, longest)),
            ("under ascent 15's rule", modVine, Mathf.Min(VineReach(modVine.transform.position, modVine.transform.up, longest), longest * 0.75f)),
        })
        {
            float grown = vine.vineOriginTransform.lossyScale.y;
            Check($"a Magic Bean's vine {name} grows {expected:0.#} m (15: at most {longest * 0.75f:0.#} m, the game: {longest:0.#} m)",
                !vine._isGrowing && Mathf.Abs(vine.maxLength - expected) < 0.01f && Mathf.Abs(grown - expected) < 0.05f,
                $"vine {vine.maxLength:0.##} m long, grown to {grown:0.##} m (still growing {vine._isGrowing}), {Vector3.Distance(local.Head, vine.transform.position):0.#} m from the camera");
        }
        Info("the Magic Bean vines stand", $"{Vector3.Distance(gameVine.transform.position, modVine.transform.position):0.#} m apart");
        PhotonNetwork.Destroy(gameVine.gameObject);
        PhotonNetwork.Destroy(modVine.gameObject);
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home, false);
        yield return new WaitForSeconds(1f);
        local.data.lookValues = look;
    }

    /// <summary>
    /// A Magic Bean's vine (15) sticks 2.5 thorns into a scout every 3 s, whether the scout climbs it or
    /// stands on it. A vine is grown on open ground by the spawn point, the way a bean that took root
    /// there grows, and the scout climbs its stalk (the game's own wall grab, facing it); then a vine is
    /// grown out of a wall, as a bean thrown at a wall grows, and the scout stands on it. Each time the
    /// scout stays on the vine for 10 s, and when each 2.5 thorns go in is noted. With Ascent 15 switched
    /// off in the config, it is switched on for the check (in memory only).
    /// </summary>
    private IEnumerator BeanThorns()
    {
        Item prefab = ItemByName("MagicBean");
        MagicBean bean = prefab != null ? prefab.GetComponent<MagicBean>() : null;
        if (bean == null || bean.plantPrefab == null)
        {
            Fail("a Magic Bean and its vine are in the item database");
            yield break;
        }
        if (Rules.Level < Lvl.WorseItems)
        {
            Info("the Magic Bean thorn checks are skipped", $"testing ascent {target}");
            yield break;
        }
        BepInEx.Configuration.ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool ruleSaved = rule.Value;
        if (!ruleSaved)
        {
            rule.Value = true;
            Info("Ascent 15 is switched on for the Magic Bean thorn checks", "in memory only; the config file keeps it off");
        }
        try
        {
            yield return BeanThornsOn(bean);
        }
        finally
        {
            rule.Value = ruleSaved;
        }
    }

    private IEnumerator BeanThornsOn(MagicBean bean)
    {
        Character local = Local;
        Vector3 home = local.Center;
        Vector2 look = local.data.lookValues;
        SpawnPoint spawn = SpawnPoint.allSpawnPoints.FirstOrDefault(p => p != null);
        Vector3 near = spawn != null ? spawn.transform.position : local.Center;

        // Climbing: a vine standing on open ground, the scout facing its stalk.
        Vector3? open = OpenSky(near);
        if (open == null || !Ground(open.Value, out RaycastHit plot))
        {
            Info("the Magic Bean climbing check is skipped", "no open ground near the spawn point");
        }
        else
        {
            MagicBeanVine vine = GrowVine(bean, plot.point, Vector3.Angle(plot.normal, Vector3.up) < bean.snapToVerticalAngle ? Vector3.up : plot.normal);
            for (float until = Time.time + 15f; Time.time < until && vine._isGrowing;)
            {
                yield return null;
            }
            // The stalk where the camera will be: found from 3 m away, at head height.
            Vector3 axis = vine.transform.position;
            Vector3 away = Vector3.ProjectOnPlane(near - axis, Vector3.up).normalized;
            if (away == Vector3.zero)
            {
                away = Vector3.forward;
            }
            Vector3 eye = axis + Vector3.up * 1.6f;
            RaycastHit stalk = HelperFunctions.LineCheck(eye + away * 3f, eye - away * 1f, HelperFunctions.LayerType.TerrainMap);
            bool onStalk = stalk.collider != null && stalk.collider.GetComponentInParent<MagicBeanVine>() == vine;
            Info("Magic Bean vine to climb", $"{vine.maxLength:0.#} m tall, stalk found from the side {onStalk} ({(stalk.collider != null ? stalk.collider.name + ", layer " + LayerMask.LayerToName(stalk.collider.gameObject.layer) : "nothing")})");
            if (onStalk)
            {
                // Right next to the stalk, then the grab towards its middle, from where the camera is.
                float radius = Vector3.ProjectOnPlane(stalk.point - axis, Vector3.up).magnitude;
                Vector3 beside = axis + away * (radius + 0.45f);
                Vector3 stand = Ground(beside, out RaycastHit floor) ? floor.point : beside;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(1.5f);
                for (float until = Time.time + 2f; Time.time < until && !local.data.isGrounded;)
                {
                    yield return null;
                }
                Vector3 toward = -away;
                for (float until = Time.time + 3f; Time.time < until && !local.data.isClimbing;)
                {
                    toward = Vector3.ProjectOnPlane(axis - MainCamera.instance.transform.position, Vector3.up).normalized;
                    local.data.lookValues = new Vector2(Quaternion.LookRotation(toward).eulerAngles.y, 0f);
                    local.refs.climbing.TryToStartWallClimb(forceAttempt: true, toward);
                    yield return null;
                }
                if (!local.data.isClimbing)
                {
                    Vector3 eyes = MainCamera.instance.transform.position;
                    RaycastHit reach = HelperFunctions.LineCheck(eyes, eyes + toward * 3f, HelperFunctions.LayerType.TerrainMap);
                    Info("the scout did not get hold of the stalk", $"the stalk {(reach.collider != null ? reach.distance.ToString("0.00") + " m away (" + reach.collider.name + ")" : "not straight ahead")}, grounded {local.data.isGrounded}, can climb {local.refs.climbing.CanClimb()}");
                }
                yield return WatchBeanThorns("climbing the vine", () => local.AddStamina(1f), "magic-bean-climbing");
                local.refs.climbing.StopAnyClimbing();
                yield return new WaitForSeconds(1f);
            }
            PhotonNetwork.Destroy(vine.gameObject);
        }

        // Standing: a vine grown out of a wall, the scout on top of it.
        if (!Ground(near, out RaycastHit beach) || !FindLowWall(beach.point, out RaycastHit wall))
        {
            Info("the Magic Bean standing check is skipped", "no wall with room for a vine near the spawn point");
        }
        else
        {
            MagicBeanVine vine = GrowVine(bean, wall.point, wall.normal);
            for (float until = Time.time + 15f; Time.time < until && vine._isGrowing;)
            {
                yield return null;
            }
            float along = Mathf.Min(3f, vine.maxLength * 0.4f);
            Info("Magic Bean vine to stand on", $"{vine.maxLength:0.#} m out of a wall {wall.point.y - beach.point.y:0.0} m above the beach (wall facing {wall.normal}), standing {along:0.#} m out");
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, wall.point + wall.normal * along + Vector3.up * 2.5f, false);
            local.data.lookValues = new Vector2(Quaternion.LookRotation(Vector3.ProjectOnPlane(wall.normal, Vector3.up)).eulerAngles.y, -35f);
            // Watched from when the scout has landed on the vine and stays on it.
            float steady = 0f;
            for (float until = Time.time + 6f; Time.time < until && steady < 0.5f;)
            {
                steady = local.data.isGrounded && A14_Items.OnBeanVine(local) ? steady + Time.deltaTime : 0f;
                yield return null;
            }
            yield return WatchBeanThorns("standing on the vine", null, "magic-bean-standing");
            PhotonNetwork.Destroy(vine.gameObject);
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home, false);
        yield return new WaitForSeconds(1f);
        local.data.lookValues = look;
    }

    /// <summary>
    /// The scout on a Magic Bean's vine for 10 s: each time the thorns go up, by how much and when, and
    /// how much of the time the rule saw the scout on the vine.
    /// </summary>
    private IEnumerator WatchBeanThorns(string how, Action eachFrame, string screenshot)
    {
        Character local = Local;
        const float Watch = 10f;
        List<(float at, float amount)> thorns = new List<(float, float)>();
        float last = Afflictions.GetCurrentStatus(Status.Thorns);
        float start = Time.time;
        float onVine = 0f;
        bool climbing = true;
        bool pictured = false;
        while (Time.time - start < Watch)
        {
            eachFrame?.Invoke();
            if (A14_Items.OnBeanVine(local))
            {
                onVine += Time.deltaTime;
            }
            climbing &= local.data.isClimbing;
            float now = Afflictions.GetCurrentStatus(Status.Thorns);
            if (now > last + 0.001f)
            {
                thorns.Add((Time.time - start, now - last));
            }
            last = now;
            if (!pictured && Time.time - start > 4f)
            {
                pictured = true;
                Screenshot(screenshot);
            }
            yield return null;
        }
        List<float> gaps = new List<float>();
        for (int i = 1; i < thorns.Count; i++)
        {
            gaps.Add(thorns[i].at - thorns[i - 1].at);
        }
        float every = A14_Items.BeanThornEvery;
        bool steady = thorns.Count >= Mathf.FloorToInt(Watch / every) && thorns.All(t => Mathf.Abs(t.amount - A15_StrongerAilments.Step) < 0.001f)
                      && gaps.All(g => Mathf.Abs(g - every) < 0.1f);
        Check($"{how}: 2.5 thorns go in every {every:0} s (15)", steady && onVine >= Watch * 0.95f,
            $"thorns at {string.Join(", ", thorns.Select(t => $"{t.at:0.00} s (+{t.amount * 100f:0.#})"))}; on the vine {onVine:0.0} of {Watch:0} s" +
            (how.StartsWith("climbing") ? $", climbing all the time {climbing}" : ""));
    }

    /// <summary>A steep face near this point, 1.5-3 m above it, with room in front of it for a vine at least 6 m long.</summary>
    private static bool FindLowWall(Vector3 around, out RaycastHit wall)
    {
        for (int angle = 0; angle < 360; angle += 10)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            for (float height = 1.5f; height <= 3f; height += 0.5f)
            {
                Vector3 from = around + Vector3.up * height;
                RaycastHit hit = HelperFunctions.LineCheck(from, from + direction * 40f, HelperFunctions.LayerType.TerrainMap);
                // A vine's rotation is worked out against the world's x axis, so a face looking along it is left out.
                if (hit.transform == null || Mathf.Abs(hit.normal.y) > 0.3f || Mathf.Abs(hit.normal.x) > 0.9f)
                {
                    continue;
                }
                if (VineReach(hit.point, hit.normal, 20f) >= 6f)
                {
                    wall = hit;
                    return true;
                }
            }
        }
        wall = default;
        return false;
    }

    /// <summary>
    /// What a Magic Bean does on the host once it has taken root here: the game's vine length for this
    /// spot (its own GetVineDistance, with the mod's rule on it), then the game's vine grown to it. In
    /// the sea a dropped bean does not take root, so the step runs here directly.
    /// </summary>
    private static MagicBeanVine GrowVine(MagicBean bean, Vector3 spot, Vector3 normal)
    {
        float length = bean.GetVineDistance(spot, normal);
        Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(normal, Vector3.right), normal);
        GameObject vine = PhotonNetwork.InstantiateRoomObject(bean.plantPrefab.name, spot, rotation, 0);
        vine.GetComponent<PhotonView>().RPC("RPC_GrowVine", RpcTarget.All, length, (float)PhotonNetwork.Time);
        return vine.GetComponent<MagicBeanVine>();
    }

    /// <summary>The sea's surface under this point: the highest water collider found from 50 m above it, if any.</summary>
    private static float? SeaLevel(Vector3 point)
    {
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 50f, Vector3.down, 150f, Physics.AllLayers, QueryTriggerInteraction.Collide);
        float? level = null;
        foreach (RaycastHit hit in hits)
        {
            string name = hit.collider != null ? hit.collider.gameObject.name : "";
            bool water = hit.collider != null && (hit.collider.gameObject.layer == LayerMask.NameToLayer("Water")
                || name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("ocean", StringComparison.OrdinalIgnoreCase) >= 0);
            if (water && (level == null || hit.point.y > level.Value))
            {
                level = hit.point.y;
            }
        }
        return level;
    }

    /// <summary>The ground under a point (the terrain or the map), looked for from 10 m above it.</summary>
    private static bool Ground(Vector3 point, out RaycastHit hit)
    {
        hit = HelperFunctions.LineCheck(point + Vector3.up * 10f, point - Vector3.up * 20f, HelperFunctions.LayerType.TerrainMap);
        return hit.collider != null;
    }

    /// <summary>
    /// Rain (18): a scout in the air under open sky in a rainstorm is pulled down an extra 40 m/s², 60 in
    /// the Tropics. One tick of a rainstorm (the game's per-frame storm effect, on a rain zone made for
    /// the check) on the scout falling over the sea behind the spawn point, then in the Tropics, is
    /// measured on the hips: the upward speed it takes off within that frame.
    /// </summary>
    private IEnumerator RainPull()
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather))
        {
            Info("the rain checks are skipped", $"testing ascent {target}");
            yield break;
        }
        GameObject storm = new GameObject("RainStorm");
        storm.SetActive(false);
        WindChillZone rain = storm.AddComponent<WindChillZone>();
        try
        {
            SpawnPoint spawn = SpawnPoint.allSpawnPoints.FirstOrDefault(p => p != null);
            Vector3 sea = spawn != null ? spawn.transform.position - Vector3.ProjectOnPlane(spawn.transform.forward, Vector3.up).normalized * 40f : Local.Center;
            yield return RainTick(rain, sea, A18_ExtremeWeather.RainPull, "over the sea at the beach");
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(4f);
            Vector3? open = OpenSky(Local.Center);
            if (open == null)
            {
                Info("the Tropics rain check is skipped", "no open sky near the scout");
            }
            else
            {
                yield return RainTick(rain, open.Value, A18_ExtremeWeather.TropicsRainPull, "in the Tropics");
            }
        }
        finally
        {
            Destroy(storm);
        }
    }

    /// <summary>One rain tick on the scout falling 12 m above this spot, measured on the hips.</summary>
    private IEnumerator RainTick(WindChillZone rain, Vector3 spot, float expected, string where)
    {
        Character local = Local;
        Vector3 ground = Ground(spot, out RaycastHit hit) ? hit.point : spot;
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, ground + Vector3.up * 12f, false);
        yield return new WaitForSeconds(0.4f);
        bool airborne = local.data.groundedFor <= 0.5f && !local.data.isClimbing;
        Rigidbody hip = local.GetBodypart(BodypartType.Hip).rig;
        float before = hip.linearVelocity.y;
        rain.ApplyStatus(local);
        float pull = (before - hip.linearVelocity.y) / Time.deltaTime;
        Check($"rain pulls a scout in the air {where} down an extra {expected:0} m/s² (18)",
            airborne && Mathf.Abs(pull - expected) < 0.05f, $"{pull:0.##} m/s² on the hips (in the air {airborne}, segment {Map.CurrentSegment})");
        yield return new WaitForSeconds(2.5f);
    }

    /// <summary>A spot near this point with nothing above it (open sky over the ground), if there is one.</summary>
    private static Vector3? OpenSky(Vector3 near)
    {
        foreach (float distance in new[] { 0f, 5f, 10f, 15f, 20f, 30f })
        {
            for (int i = 0; i < 16; i++)
            {
                Vector3 point = near + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * distance;
                if (Ground(point, out RaycastHit hit) && HelperFunctions.LineCheck(hit.point + Vector3.up * 500f, hit.point + Vector3.up * 12f, HelperFunctions.LayerType.AllPhysical).transform == null)
                {
                    return hit.point;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Thrown fungi (15): the Shelf, Cloud and Warp Fungus are each thrown the game's way, level and at
    /// full charge, and the speed they leave the hand with is measured against the game's throw:
    /// divided by how many times as heavy each flies (1.5, 1.5, 1.35). Each is taken away right after,
    /// before it lands (a Warp Fungus would warp the scout).
    /// </summary>
    private IEnumerator ThrownFungi()
    {
        if (!Rules.Enabled(Lvl.WorseItems))
        {
            Info("the thrown fungi checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Vector2 look = local.data.lookValues;
        foreach ((string prefabName, float heavier) in new[] { ("ShelfShroom", 1.5f), ("CloudFungus", 1.5f), ("WarpFungus", 1.35f) })
        {
            Item prefab = ItemByName(prefabName);
            if (prefab == null)
            {
                Fail(prefabName + " is in the item database");
                continue;
            }
            yield return SpawnIntoHand(prefab);
            if (held == null)
            {
                continue;
            }
            local.data.lookValues = new Vector2(look.x, 0f);
            yield return new WaitForSeconds(0.5f);
            string name = Plain(prefab.GetItemName());
            float game = local.refs.items.maxThrowForce * 0.5f * prefab.throwForceMultiplier;
            HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
            Step("throw a " + name, () =>
            {
                byte slot = local.refs.items.currentSelectedSlot.Value;
                local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 1f, slot, held.transform.position, Vector3.zero, held.transform.rotation, local.player.GetItemSlot(slot).data, false);
            });
            Item thrown = null;
            for (float until = Time.time + 2f; Time.time < until && thrown == null;)
            {
                thrown = Item.ALL_ITEMS.FirstOrDefault(i => i != null && !before.Contains(i) && i.itemID == prefab.itemID && i.rig != null);
                if (thrown == null)
                {
                    yield return null;
                }
            }
            float speed = thrown != null ? Vector3.ProjectOnPlane(thrown.rig.linearVelocity, Vector3.up).magnitude : -1f;
            if (thrown != null && thrown.photonView != null)
            {
                PhotonNetwork.Destroy(thrown.gameObject);
            }
            Check($"a thrown {name} leaves the hand as if {heavier:0.##} times as heavy (15)", speed > 0f && Mathf.Abs(speed - game / heavier) < game * 0.02f,
                $"{speed:0.##} m/s (the game: {game:0.##} m/s; {game / heavier:0.##} expected)");
            yield return new WaitForSeconds(0.5f);
        }
        local.data.lookValues = look;
    }

    /// <summary>
    /// The config's New Items switch in a regular run: on, the Climbing Chalk and the Lightweight
    /// Parasol may spawn; off (in memory, then back), the nearest suitcase, given a pool with only the
    /// chalk in it for one opening, gives something else, and the next one, given only the Roots
    /// parasol, gives the regular Parasol instead.
    /// </summary>
    private IEnumerator NewItemsSwitch()
    {
        BepInEx.Configuration.ConfigEntry<bool> entry = UltimateAscent.Plugin.NewItems;
        string file = UltimateAscent.Plugin.Instance.Config.ConfigFilePath;
        bool inFile = File.Exists(file) && File.ReadAllText(file).Contains("New Items = ");
        Check("the config has the New Items switch", entry != null && inFile, $"bound {entry != null}, in the file {inFile}");
        Item chalk = ItemByName(ClimbingChalkItem.PrefabName);
        Item roots = ItemByName(A14_Items.RootsParasolPrefab);
        if (entry == null || chalk == null || roots == null || !Rules.InExtraAscent)
        {
            Info("the New Items checks are skipped", $"switch {entry != null}, chalk {chalk != null}, Roots parasol {roots != null}, ascent {Ascents._currentAscent}");
            yield break;
        }
        bool saved = entry.Value;
        entry.Value = true;
        bool chalkOn = chalk.IsValidToSpawn();
        bool rootsOn = roots.IsValidToSpawn();
        List<Item> withChalk = null;
        List<Item> withRoots = null;
        try
        {
            entry.Value = false;
            yield return OpenWithOnly(chalk, "the chalk", items => withChalk = items);
            yield return OpenWithOnly(roots, "the Roots parasol", items => withRoots = items);
        }
        finally
        {
            entry.Value = saved;
        }
        Check("with New Items on, the Climbing Chalk and the Lightweight Parasol may spawn in a regular run (9)", chalkOn && rootsOn,
            $"chalk {chalkOn}, Lightweight Parasol {rootsOn}");
        if (withChalk != null)
        {
            Check("with New Items off, a suitcase that could only give the chalk gives something else", withChalk.Count > 0 && !withChalk.Any(ClimbingChalkItem.IsChalk),
                Names(withChalk));
        }
        if (withRoots != null)
        {
            Check("with New Items off, a suitcase that could only give the Lightweight Parasol gives the regular Parasol",
                withRoots.Count > 0 && withRoots.All(i => i.gameObject.name.StartsWith("Parasol", StringComparison.Ordinal) && !A14_Items.IsRootsParasol(i)),
                string.Join(", ", withRoots.Select(i => $"{Plain(i.GetItemName())} ({i.gameObject.name.Replace("(Clone)", "")})")));
        }
        foreach (Item item in (withChalk ?? new List<Item>()).Concat(withRoots ?? new List<Item>()).Where(i => i != null && i.photonView != null))
        {
            PhotonNetwork.Destroy(item.gameObject);
        }
        Check("the New Items switch set back applies again", chalk.IsValidToSpawn() == saved, $"switch {entry.Value}");
    }

    /// <summary>
    /// The nearest closed suitcase, opened the game's way with only this item in its pool for one opening
    /// (the pool is put back as soon as its items are out); hands over what it gave.
    /// </summary>
    private IEnumerator OpenWithOnly(Item only, string what, Action<List<Item>> done)
    {
        Character local = Local;
        Luggage luggage = Luggage.ALL_LUGGAGE
            .Where(l => l != null && !(l is RespawnChest) && !l.CompareTag("ClownLuggage") && l.state == Luggage.LuggageState.Closed && l.gameObject.activeInHierarchy)
            .OrderBy(l => Vector3.Distance(l.transform.position, local.Center))
            .FirstOrDefault();
        if (luggage == null)
        {
            Info("no closed suitcase near the scout for " + what);
            yield break;
        }
        SpawnPool pool = luggage.GetSpawnPool();
        LootData.PopulateLootData();
        Dictionary<ushort, int> own = LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> weights) ? weights : null;
        HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
        List<Item> Spawned() => Item.ALL_ITEMS.Where(i => i != null && !before.Contains(i) && Vector3.Distance(i.transform.position, luggage.transform.position) < 5f).ToList();
        LootData.AllSpawnWeightData[pool] = new Dictionary<ushort, int> { [only.itemID] = 15 };
        try
        {
            Step("open a suitcase that could only give " + what, () => luggage.Interact_CastFinished(local));
            for (float until = Time.realtimeSinceStartup + 3f; Time.realtimeSinceStartup < until && Spawned().Count == 0;)
            {
                yield return null;
            }
        }
        finally
        {
            if (own != null)
            {
                LootData.AllSpawnWeightData[pool] = own;
            }
            else
            {
                LootData.AllSpawnWeightData.Remove(pool);
            }
        }
        yield return new WaitForSeconds(0.5f);
        done(Spawned());
    }

    /// <summary>How far the game lets a Magic Bean's vine grow from this spot: up to its longest, or to the first rock beyond 0.7 m.</summary>
    private static float VineReach(Vector3 start, Vector3 direction, float longest)
    {
        RaycastHit[] hits = new RaycastHit[128];
        int count = HelperFunctions.LineCheckAll(start, start + direction * longest, HelperFunctions.LayerType.TerrainMap, hits);
        float reach = longest;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider != null && hits[i].distance > 0.7f && hits[i].distance < reach)
            {
                reach = hits[i].distance;
            }
        }
        return reach;
    }

    private static string Names(List<Item> items) => items.Count == 0 ? "nothing" : string.Join(", ", items.Select(i => Plain(i.GetItemName())));

    /// <summary>The nearest closed suitcase, opened with only this item switched on, and what it gave.</summary>
    private IEnumerator SuitcaseInCustomRun(Item only, string shot, Action<List<Item>> check)
    {
        Character local = Local;
        Luggage luggage = Luggage.ALL_LUGGAGE
            .Where(l => l != null && !(l is RespawnChest) && !l.CompareTag("ClownLuggage") && l.state == Luggage.LuggageState.Closed && l.gameObject.activeInHierarchy)
            .OrderBy(l => Vector3.Distance(l.transform.position, local.Center))
            .FirstOrDefault();
        string name = Plain(only.GetItemName());
        if (luggage == null)
        {
            Info("no closed suitcase found for a custom run with only the " + name);
            yield break;
        }
        SpawnPool pool = luggage.GetSpawnPool();
        LootData.PopulateLootData();
        if (!LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> weights) || !weights.ContainsKey(only.itemID))
        {
            Info("custom run with only the " + name, $"the nearest suitcase's pool {pool} does not have it");
            yield break;
        }
        HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
        List<Item> Spawned() => Item.ALL_ITEMS.Where(i => i != null && !before.Contains(i) && Vector3.Distance(i.transform.position, luggage.transform.position) < 5f).ToList();
        List<Item> spawned = new List<Item>();
        CustomRunSpawn.Target = luggage;
        CustomRunSpawn.OnlyItem = only.gameObject.name;
        try
        {
            Step("open a suitcase in a custom run with only the " + name + " on", () => luggage.Interact_CastFinished(local));
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && spawned.Count == 0)
            {
                yield return null;
                spawned = Spawned();
            }
        }
        finally
        {
            CustomRunSpawn.Target = null;
            CustomRunSpawn.OnlyItem = null;
        }
        yield return new WaitForSeconds(0.5f);
        spawned = Spawned();
        Screenshot(shot);
        Step("custom-run suitcase: " + name, () => check(spawned));
        Check("the custom-run switches are back as they were, and this run is not a custom run", !RunSettings.IsCustomRun && CustomRunSpawn.Restored,
            $"custom run {RunSettings.IsCustomRun}, switches restored {CustomRunSpawn.Restored}");
        yield return new WaitForSeconds(1.5f);
        foreach (Item item in spawned)
        {
            if (item != null && item.photonView != null)
            {
                PhotonNetwork.Destroy(item.gameObject);
            }
        }
    }

    private IEnumerator Weather(float wake)
    {
        if (target < Lvl.AllWeather)
        {
            Info("ascent 25's weather checks are skipped", $"testing ascent {target}");
            yield break;
        }
        yield return WaitUntilTime(wake + 36f);
        if (!apocalypse)
        {
            CheckWeatherPhases();
            yield break;
        }
        // With Apocalypse mode the weather is TFA's: ascent 25 adds no weather of its own (only its tornadoes).
        Check("the weather is left to TFA's Apocalypse (25)", !A25_AllWeather.DrivesVanillaWeather,
            $"vanilla weather driven {A25_AllWeather.DrivesVanillaWeather}, TFA's weather now '{Compat.Tfa.Weather}'");
    }

    private void CheckCatalog()
    {
        List<AscentData.AscentInstanceData> list = SingletonAsset<AscentData>.Instance.ascents;
        Check("ascents 9-25 are registered", AscentCatalog.Registered && list.Count >= Lvl.Last + 3, $"{list.Count} entries in the game's ascent list");
        int languages = Enum.GetValues(typeof(LocalizedText.Language)).Length;
        List<string> problems = new List<string>();
        for (int level = Lvl.First; level <= Lvl.Last && level + 2 < list.Count; level++)
        {
            AscentData.AscentInstanceData entry = list[level + 2];
            if (entry.title != AscentCatalog.TitleKey(level))
            {
                problems.Add($"{level}: slot holds '{entry.title}'");
            }
            string title = entry.localizedTitle;
            string description = entry.localizedDescription;
            if (string.IsNullOrEmpty(title) || !title.Contains(level.ToString()))
            {
                problems.Add($"{level}: title '{title}'");
            }
            if (string.IsNullOrEmpty(description) || description.IndexOf("ULTIMATEASCENT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                problems.Add($"{level}: description '{description}'");
            }
            if (!string.IsNullOrEmpty(entry.titleReward))
            {
                problems.Add($"{level}: has a reward '{entry.titleReward}'");
            }
            foreach (string key in new[] { AscentCatalog.TitleKey(level), LocalizedText.GetDescriptionIndex(AscentCatalog.TitleKey(level)) })
            {
                if (!LocalizedText.mainTable.TryGetValue(key.ToUpperInvariant(), out List<string> texts) || texts.Count < languages || texts.Any(string.IsNullOrEmpty))
                {
                    problems.Add($"{level}: '{key}' is missing in some languages");
                }
            }
        }
        Check("every ascent has a title and description in every language, and no reward", problems.Count == 0,
            problems.Count == 0 ? $"{Lvl.Count} ascents, {languages} languages" : string.Join("; ", problems.Take(8)));
        Info("current language", LocalizedText.CURRENT_LANGUAGE.ToString());
    }

    /// <summary>
    /// The Custom Run screen, opened as a player does: the boarding pass's custom-run button, then its
    /// edit button. The Climbing Chalk and the Lightweight Parasol have switches of their own; each is
    /// clicked off and on again (the game saves the switches), and the boarding pass is left as it was.
    /// </summary>
    private IEnumerator CustomRunScreen()
    {
        BoardingPass pass = GUIManager.instance.boardingPass;
        AirportCheckInKiosk kiosk = FindAnyObjectByType<AirportCheckInKiosk>();
        CustomOptionsWindow window = pass != null ? pass.customOptionsWindow : null;
        if (kiosk == null || window == null)
        {
            Fail("the Custom Run screen can be opened", $"kiosk {kiosk != null}, screen {window != null}");
            yield break;
        }
        bool wasCustom = RunSettings.IsCustomRun;
        Step("open the Custom Run screen", () =>
        {
            pass.kiosk = kiosk;
            if (!pass.isOpen)
            {
                pass.Open();
            }
            if (!RunSettings.IsCustomRun)
            {
                pass.customRunButton.onClick.Invoke();
            }
            pass.customOptionsButton.onClick.Invoke();
        });
        yield return new WaitForSecondsRealtime(1.5f);
        yield return CustomRunClicks(window);

        (string prefab, string after, string label)[] ours =
        {
            (ClimbingChalkItem.PrefabName, ClimbingChalkItem.EnergyDrinkPrefab, "CLIMBING CHALK"),
            (A14_Items.RootsParasolPrefab, "Parasol", "LIGHTWEIGHT PARASOL"),
            (AutoParachute.PrefabName, "Glider", "AUTO-PARACHUTE"),
        };
        foreach ((string prefab, string after, string label) in ours)
        {
            CustomOptionItemToggle toggle = null;
            Step("Custom Run switch: " + label, () =>
            {
                List<CustomOptionItemToggle> listed = window.spawnPoolList.GetComponentsInChildren<CustomOptionItemToggle>(true).ToList();
                bool mystical = window.mysticalSpawnPoolList.GetComponentsInChildren<CustomOptionItemToggle>(true).Any(t => t.item != null && t.item.gameObject.name == prefab);
                int index = listed.FindIndex(t => t.item != null && t.item.gameObject.name == prefab);
                int previous = listed.FindIndex(t => t.item != null && t.item.gameObject.name == after);
                toggle = index >= 0 ? listed[index] : null;
                string shown = toggle != null && toggle.text != null && toggle.text.tmp != null ? Plain(toggle.text.tmp.text) : "";
                Check($"the Custom Run screen lists the {label.ToLowerInvariant()} (after the {after.ToLowerInvariant()})",
                    toggle != null && toggle.gameObject.activeInHierarchy && shown == label && index == previous + 1 && !mystical,
                    $"listed at {index} of {listed.Count} (the {after} at {previous}), named '{shown}', among the mystical items {mystical}");
                if (toggle == null)
                {
                    return;
                }
                ScrollRect scroll = toggle.GetComponentInParent<ScrollRect>();
                if (scroll != null && listed.Count > 1)
                {
                    scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - (float)index / (listed.Count - 1));
                }
            });
            if (toggle == null)
            {
                continue;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Screenshot("custom-run-" + label.ToLowerInvariant().Replace(' ', '-'));
            // The picture is taken at the end of the frame: the click waits for it.
            yield return new WaitForSecondsRealtime(0.5f);
            int before = RunSettings.GetItemValue(toggle.item, forceGetCustomValue: true);
            Step("Custom Run switch clicked: " + label, () => toggle.OnClick());
            yield return new WaitForSecondsRealtime(0.75f);
            int clicked = RunSettings.GetItemValue(toggle.item, forceGetCustomValue: true);
            bool shownOn = toggle.toggle.isOn;
            Step("Custom Run switch clicked back: " + label, () => toggle.OnClick());
            yield return new WaitForSecondsRealtime(0.5f);
            int back = RunSettings.GetItemValue(toggle.item, forceGetCustomValue: true);
            Check($"the {label.ToLowerInvariant()}'s switch turns it off and on", before != clicked && shownOn == (clicked == 1) && back == before && toggle.toggle.isOn == (back == 1),
                $"{before} -> {clicked} (shown {(shownOn ? "on" : "off")}) -> {back}");
        }

        Step("Custom Run switches: the mystical new items and the halves of items", () =>
        {
            List<CustomOptionItemToggle> regular = window.spawnPoolList.GetComponentsInChildren<CustomOptionItemToggle>(true).ToList();
            List<CustomOptionItemToggle> mysticalList = window.mysticalSpawnPoolList.GetComponentsInChildren<CustomOptionItemToggle>(true).ToList();
            string Shown(CustomOptionItemToggle t) => t != null && t.text != null && t.text.tmp != null ? Plain(t.text.tmp.text) : "";
            foreach ((string prefab, string label) in new[] { (WarpCompass.PrefabName, "WARP COMPASS"), (ToxicDrink.PrefabName, "TOXIC DRINK") })
            {
                CustomOptionItemToggle toggle = mysticalList.FirstOrDefault(x => x.item != null && x.item.gameObject.name == prefab);
                bool inRegular = regular.Any(x => x.item != null && x.item.gameObject.name == prefab);
                Check($"the Custom Run screen lists the {label.ToLowerInvariant()} among the mystical items", toggle != null && !inRegular && Shown(toggle) == label,
                    $"among the mystical items {toggle != null}, among the regular ones {inRegular}, named '{Shown(toggle)}'");
            }
            foreach ((string prefab, string after, string label) in new[] { (CherryLollipop.PrefabName, CherryLollipop.LollipopPrefab, "CHERRY LOLLIPOP"), (VanillaWafers.VanillaPrefab, VanillaWafers.CookiesPrefab, "VANILLA WAFERS") })
            {
                int index = regular.FindIndex(x => x.item != null && x.item.gameObject.name == prefab);
                int previous = regular.FindIndex(x => x.item != null && x.item.gameObject.name == after);
                Check($"the Custom Run screen lists the {label.ToLowerInvariant()} after its item", index > 0 && index == previous + 1 && Shown(regular[index]) == label,
                    $"at {index} (its item at {previous}), named '{(index >= 0 ? Shown(regular[index]) : "")}'");
            }
        });
        yield return new WaitForSecondsRealtime(0.3f);
        Screenshot("custom-run-screen");
        yield return new WaitForSecondsRealtime(0.5f);

        Step("close the Custom Run screen", () =>
        {
            // Back on the boarding pass, which the screen reopens, as a normal run again.
            window.closeButtonCustom.onClick.Invoke();
            if (RunSettings.IsCustomRun != wasCustom)
            {
                pass.customRunButton.onClick.Invoke();
            }
        });
        yield return new WaitForSecondsRealtime(1f);
        Check("closing the Custom Run screen goes back to the boarding pass", pass.isOpen && !window.isOpen && RunSettings.IsCustomRun == wasCustom,
            $"boarding pass open {pass.isOpen}, screen open {window.isOpen}, custom run {RunSettings.IsCustomRun}");
    }

    /// <summary>
    /// The item switches as a mouse meets them: the item list (an 18th row with the mod's two items)
    /// keeps clear of the dotted divider under it, the box of every switch on the screen takes the
    /// clicks on it, and the Rope Spool's (the last of the list's first column, which ran under the
    /// divider) is clicked off and on again through the screen's own pointer events.
    /// </summary>
    private IEnumerator CustomRunClicks(CustomOptionsWindow window)
    {
        List<CustomOptionItemToggle> items = window.spawnPoolList.GetComponentsInChildren<CustomOptionItemToggle>(true).ToList();
        List<CustomOptionItemToggle> all = items.Concat(window.mysticalSpawnPoolList.GetComponentsInChildren<CustomOptionItemToggle>(true)).ToList();
        Transform list = window.spawnPoolList;
        RectTransform divider = list.parent != null && list.GetSiblingIndex() + 1 < list.parent.childCount ? list.parent.GetChild(list.GetSiblingIndex() + 1) as RectTransform : null;
        CustomOptionItemToggle ropeSpool = items.FirstOrDefault(t => t.item != null && t.item.gameObject.name == "RopeSpool");
        Step("Custom Run screen: rows and divider", () =>
        {
            float lowest = items.Min(t => Corners(t.transform as RectTransform)[0].y);
            float line = divider != null ? Corners(divider)[1].y : float.NaN;
            float mystical = Corners(window.mysticalSpawnPoolList.GetComponentsInChildren<CustomOptionItemToggle>(true).First().transform as RectTransform)[1].y;
            Check("the Custom Run screen's item list keeps clear of the divider under it", divider != null && lowest >= line - 0.5f && mystical <= Corners(divider)[0].y + 0.5f,
                $"{items.Count} items in {Mathf.CeilToInt(items.Count / 2f)} rows, lowest row down to {lowest:0}, divider '{(divider != null ? divider.name : "none")}' {line:0}..{(divider != null ? Corners(divider)[0].y : float.NaN):0}, mystical items from {mystical:0} (screen pixels, up is higher)");
            List<CustomOptionItemToggle> mysticalToggles = window.mysticalSpawnPoolList.GetComponentsInChildren<CustomOptionItemToggle>(true).ToList();
            float mysticalLowest = mysticalToggles.Min(t => Corners(t.transform as RectTransform)[0].y);
            float panelBottom = list.parent is RectTransform panel ? Corners(panel)[0].y : float.NaN;
            Check("the mystical items stay inside the Custom Run screen's panel", mysticalLowest >= panelBottom - 0.5f,
                $"{mysticalToggles.Count} mystical items, lowest row down to {mysticalLowest:0}, the panel's bottom at {panelBottom:0}, rows {(list.TryGetComponent(out GridLayoutGroup grid) ? grid.cellSize.y : 0f):0.#} px (screen pixels, up is higher)");
            List<string> blocked = new List<string>();
            foreach (CustomOptionItemToggle t in all)
            {
                GameObject hit = TopHitAt(Box(t));
                if (hit == null || !hit.transform.IsChildOf(t.transform))
                {
                    blocked.Add($"{Plain(t.text.tmp.text)} (under {(hit != null ? hit.name : "nothing")})");
                }
            }
            Check("every switch on the Custom Run screen takes the clicks on its box", blocked.Count == 0,
                $"{all.Count} switches" + (blocked.Count > 0 ? ", blocked: " + string.Join(", ", blocked) : ""));
        });
        if (ropeSpool == null)
        {
            Fail("the Custom Run screen lists the Rope Spool");
            yield break;
        }
        yield return new WaitForSecondsRealtime(0.5f);
        Screenshot("custom-run-rope-spool");
        yield return new WaitForSecondsRealtime(0.5f);
        int before = RunSettings.GetItemValue(ropeSpool.item, forceGetCustomValue: true);
        Step("Rope Spool clicked", () => ClickAt(Box(ropeSpool)));
        yield return new WaitForSecondsRealtime(0.75f);
        int clicked = RunSettings.GetItemValue(ropeSpool.item, forceGetCustomValue: true);
        bool shownOn = ropeSpool.toggle.isOn;
        Step("Rope Spool clicked back", () => ClickAt(Box(ropeSpool)));
        yield return new WaitForSecondsRealtime(0.5f);
        int back = RunSettings.GetItemValue(ropeSpool.item, forceGetCustomValue: true);
        Check("a click on the Rope Spool's box switches it off and on", before != clicked && shownOn == (clicked == 1) && back == before && ropeSpool.toggle.isOn == (back == 1),
            $"{before} -> {clicked} (shown {(shownOn ? "on" : "off")}) -> {back}");
    }

    private static Vector3[] Corners(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return corners;
    }

    /// <summary>The middle of a switch's box, on the screen.</summary>
    private static Vector2 Box(CustomOptionItemToggle toggle)
    {
        RectTransform box = toggle.toggle.transform.Find("Background") as RectTransform ?? toggle.toggle.graphic.rectTransform;
        Vector3[] corners = Corners(box);
        Canvas canvas = toggle.GetComponentInParent<Canvas>().rootCanvas;
        return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, (corners[0] + corners[2]) * 0.5f);
    }

    /// <summary>What the mouse would land on at this point of the screen.</summary>
    private static GameObject TopHitAt(Vector2 screen)
    {
        UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
        if (events == null)
        {
            return null;
        }
        List<UnityEngine.EventSystems.RaycastResult> hits = new List<UnityEngine.EventSystems.RaycastResult>();
        events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events) { position = screen }, hits);
        return hits.Count > 0 ? hits[0].gameObject : null;
    }

    /// <summary>A click at this point of the screen (the left button unless told), sent the way the event system sends a mouse click.</summary>
    private static void ClickAt(Vector2 screen, UnityEngine.EventSystems.PointerEventData.InputButton button = UnityEngine.EventSystems.PointerEventData.InputButton.Left)
    {
        UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
        GameObject hit = TopHitAt(screen);
        if (events == null || hit == null)
        {
            return;
        }
        UnityEngine.EventSystems.PointerEventData click = new UnityEngine.EventSystems.PointerEventData(events)
        {
            position = screen,
            button = button,
            clickCount = 1,
        };
        GameObject pressed = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hit, click, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        GameObject clickable = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(hit);
        click.pointerPress = pressed ?? clickable;
        UnityEngine.EventSystems.ExecuteEvents.Execute(click.pointerPress, click, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
        if (clickable != null && click.pointerPress == clickable)
        {
            UnityEngine.EventSystems.ExecuteEvents.Execute(clickable, click, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
        }
    }

    private bool BoardingPassChecks()
    {
        BoardingPass pass = GUIManager.instance.boardingPass;
        AirportCheckInKiosk kiosk = FindAnyObjectByType<AirportCheckInKiosk>();
        Check("the airport has its check-in kiosk", kiosk != null);
        if (kiosk == null)
        {
            return false;
        }
        if (RunSettings.IsCustomRun)
        {
            Info("a custom run was selected; the test switches to a normal run");
            RunSettings.IsCustomRun = false;
        }

        // Test settings only live in memory: the config file is not touched.
        config = UltimateAscent.Plugin.Instance.Config;
        savedSaveOnSet = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        savedUnlockAll = UltimateAscent.Plugin.UnlockAll.Value;
        savedDefault = UltimateAscent.Plugin.DefaultAscent.Value;
        configChanged = true;
        Info("unlock progress", $"Steam MaxAscent {Progress.VanillaStat}, mod progress {Progress.Earned}, selectable up to {Progress.MaxSelectable}");
        UltimateAscent.Plugin.UnlockAll.Value = true;
        UltimateAscent.Plugin.DefaultAscent.Value = target;

        pass.kiosk = kiosk;
        pass.Open();
        Check("the boarding pass opens on the default ascent", pass.ascentIndex == target, $"index {pass.ascentIndex}, title '{Plain(pass.ascentTitle.text)}'");
        Check("the boarding pass shows the ascent's title", Plain(pass.ascentTitle.text).Contains(target.ToString()), Plain(pass.ascentTitle.text));
        Check("the boarding pass shows the ascent's rules", Plain(pass.ascentDesc.text).Length > 20, Plain(pass.ascentDesc.text));
        pass.DecrementAscent();
        Check("the left arrow goes down one ascent", pass.ascentIndex == target - 1, $"index {pass.ascentIndex}");
        pass.IncrementAscent();
        Check("the right arrow goes up one ascent", pass.ascentIndex == target, $"index {pass.ascentIndex}");
        {
            // The wrap-around starts from the last ascent, whatever ascent is being tested.
            pass.ascentIndex = Lvl.Last;
            pass.UpdateAscent();
            pass.IncrementAscent();
            if (Compat.Tfa.Present)
            {
                Check("right of ascent 25 comes TFA's FINAL ASCENT", pass.ascentIndex == BoardingPassUi.FinalAscentIndex, $"index {pass.ascentIndex}, title '{Plain(pass.ascentTitle.text)}'");
                pass.IncrementAscent();
            }
            Check("the right arrow wraps around to Tenderfoot", pass.ascentIndex == -1, $"index {pass.ascentIndex}, title '{Plain(pass.ascentTitle.text)}'");
            pass.DecrementAscent();
            if (Compat.Tfa.Present)
            {
                Check("left of Tenderfoot is TFA's FINAL ASCENT", pass.ascentIndex == BoardingPassUi.FinalAscentIndex, $"index {pass.ascentIndex}, title '{Plain(pass.ascentTitle.text)}'");
                pass.DecrementAscent();
            }
            Check("left of Tenderfoot" + (Compat.Tfa.Present ? " and FINAL ASCENT" : "") + " is the highest ascent", pass.ascentIndex == Progress.MaxSelectable, $"index {pass.ascentIndex}");
        }
        pass.ascentIndex = target;
        pass.UpdateAscent();
        Check("the start button can be used", pass.startGameButton.interactable);
        MapPickerChecks(pass);
        return true;
    }

    /// <summary>
    /// The map picker on the boarding pass: it shows today's map, its arrows go through the rest of the rotation
    /// (today's number not among them) and back, and the pick is kept in the mod's save for the day. Back on today's
    /// map afterwards, so the climb is today's (or the -Biome map), unless "-Only picker" asks for the picked one.
    /// </summary>
    private void MapPickerChecks(BoardingPass pass)
    {
        int count = MapPicker.Count;
        int today = MapPicker.Today;
        MapPicker.Picked = today;
        pass.UpdateAscent();
        GameObject line = MapPicker.Line;
        string shownToday = MapPicker.Label != null ? Plain(MapPicker.Label.text) : "";
        Check("the boarding pass has the map picker at the bottom right of its card, on today's map", line != null && line.activeInHierarchy && shownToday == Plain(MapPicker.Name(today)) && shownToday.Contains("(" + MapPicker.Number(today) + ")"),
            $"shown {(line != null && line.activeInHierarchy)}, '{shownToday}'; {count} maps in the rotation, today's {MapPicker.Number(today)} (day {MapPicker.Day})");
        List<string> names = new List<string>();
        for (int i = 0; i < count; i++)
        {
            names.Add(Plain(MapPicker.Label.text));
            MapPicker.Step(1);
        }
        string back = Plain(MapPicker.Label.text);
        List<string> others = names.Skip(1).ToList();
        int number = MapPicker.Number(today);
        List<int> numbers = MapPicker.Order().Select(MapPicker.Number).ToList();
        string Expected(int n) => n == number ? shownToday : "MAP " + n;
        int at = numbers.IndexOf(number);
        string right = Expected(numbers[(at + 1) % numbers.Count]);
        string left = Expected(numbers[(at - 1 + numbers.Count) % numbers.Count]);
        bool byNumber = others.All(n => n.StartsWith("MAP ", StringComparison.Ordinal)) && !others.Contains("MAP " + number) && others.Distinct().Count() == others.Count && names.Count > 1 && names[1] == right;
        MapPicker.Step(-1);
        string leftShown = Plain(MapPicker.Label.text);
        MapPicker.Step(1);
        Check($"its arrows go by number: from today's map (here {number}) right to {right}, left to {left}, through every map once, today's under its own name, and round again",
            names.Count == count && byNumber && leftShown == left && back == shownToday,
            $"{string.Join(", ", names)}; after them '{back}'; one to the left of today's '{leftShown}'");
        MapPicker.Step(1);
        int picked = MapPicker.Picked;
        ModSave.Reload();
        bool kept = MapPicker.Picked == picked && ModSave.Get("map.day") == MapPicker.Day.ToString();
        MapPicker.Step(-1);
        Check("a pick is kept in the mod's save with its day (read back from the file), and the left arrow goes back", kept && MapPicker.Picked == today,
            $"picked {MapPicker.Name(picked)}, read back {kept}; one to the left: {MapPicker.Name(MapPicker.Picked)}");
        Screenshot("map-picker");
        MapBaker baker = SingletonAsset<MapBaker>.Instance;
        List<string> built = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings)
            .Select(i => System.IO.Path.GetFileNameWithoutExtension(UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i))).ToList();
        Info("the game's maps", $"rotation ({baker.ScenePaths.Length}): {string.Join(", ", baker.ScenePaths.Select(System.IO.Path.GetFileNameWithoutExtension))}; biome codes {baker.BiomeIDs.Count}; "
            + $"scenes in the build ({built.Count}): {string.Join(", ", built)}");
        if (only != null && only.Any(w => w.Equals("picker", StringComparison.OrdinalIgnoreCase)))
        {
            pickedForClimb = MapPicker.Order()[Mathf.Min(3, count - 1)];
            MapPicker.Picked = pickedForClimb;
            pass.UpdateAscent();
            Info("map for the climb", $"{MapPicker.Name(pickedForClimb)}: {SingletonAsset<MapBaker>.Instance.GetLevel(pickedForClimb)}");
            Screenshot("map-picker-picked");
        }
    }

    private int pickedForClimb = -1;

    /// <summary>With "-Only picker": the climb is on the map picked on the boarding pass, not today's.</summary>
    private void PickedMapCheck()
    {
        if (pickedForClimb < 0)
        {
            Info("the picked map check is skipped", "run with -Only picker");
            return;
        }
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string wanted = SingletonAsset<MapBaker>.Instance.GetLevel(pickedForClimb);
        Check("the climb is on the map picked on the boarding pass, not today's", scene == wanted && pickedForClimb != MapPicker.Today,
            $"scene {scene}, picked {wanted} ({MapPicker.Name(pickedForClimb)}), today's {SingletonAsset<MapBaker>.Instance.GetLevel(MapPicker.Today)}");
        MapPicker.Picked = MapPicker.Today;
    }

    private void Depart()
    {
        BoardingPass pass = GUIManager.instance.boardingPass;
        if (RunSettings.IsCustomRun && !customClimb)
        {
            Fail("the boarding pass is back on a normal run before departure");
            RunSettings.IsCustomRun = false;
        }
        pass.ascentIndex = target;
        pass.UpdateAscent();
        if (!string.IsNullOrEmpty(biome))
        {
            MapBaker baker = SingletonAsset<MapBaker>.Instance;
            int today = GameHandler.GetService<NextLevelService>().NextLevelIndexOrFallback;
            // A number is a map's number (Level_N): PEAK 3.0.0's biome codes no longer match its 208 maps, so a map known
            // to have the biomes wanted is named directly.
            int offset = int.TryParse(biome, out int level) && level >= 0 && level < baker.ScenePaths.Length
                ? ((level - today) % baker.ScenePaths.Length + baker.ScenePaths.Length) % baker.ScenePaths.Length
                : Enumerable.Range(0, baker.ScenePaths.Length).Select(i => (int?)i).FirstOrDefault(i => baker.GetBiomeID(today + i.Value).Contains(biome)) ?? -1;
            Info("the maps' biome codes", string.Join(", ", Enumerable.Range(0, baker.ScenePaths.Length).Select(i => $"{baker.GetLevel(i)} {baker.GetBiomeID(i)}")));
            if (offset < 0)
            {
                Fail("a map with the biome " + biome, "none of the maps has it; today's map is played");
            }
            else
            {
                NextLevelService.debugLevelIndexOffset = offset;
                Info("map for the test", $"{offset} after today's: {baker.GetLevel(today + offset)}, biomes {baker.GetBiomeID(today + offset)} (today's: {baker.GetBiomeID(today)})");
            }
        }
        if (noApocalypse)
        {
            Step("switch TFA's Apocalypse off", () => SetApocalypse(false));
        }
        else if (apocalypseOn)
        {
            Step("switch TFA's Apocalypse on", () => SetApocalypse(true));
        }
        pass.StartGame();
        Info("departure", $"ascent {target} started from the boarding pass" + (RunSettings.IsCustomRun ? $" as a custom run (its Ascent setting: {CustomRunAscent.Chosen})" : ""));
    }

    /// <summary>
    /// TFA's Apocalypse mode replaces the vanilla weather: switched off in memory to test the storms of
    /// ascent 25, or on to test TFA's events; put back afterwards.
    /// </summary>
    private void SetApocalypse(bool on)
    {
        if (!Compat.Tfa.Present)
        {
            return;
        }
        Type apocalypse = AccessTools.TypeByName("TrueFinalAscent.FinalAscentApocalypse");
        apocalypseRestore = apocalypse != null ? AccessTools.Method(apocalypse, "Restore") : null;
        PropertyInfo wanted = apocalypse != null ? AccessTools.Property(apocalypse, "Wanted") : null;
        if (apocalypseRestore == null || wanted == null)
        {
            Info("TFA's Apocalypse switch was not found");
            return;
        }
        savedApocalypse = (bool)wanted.GetValue(null);
        apocalypseRestore.Invoke(null, new object[] { on });
        apocalypseChanged = true;
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "APO", on } });
        }
        Info($"TFA's Apocalypse mode is {(on ? "on" : "off")} for this test", $"it was {(savedApocalypse ? "on" : "off")}; nothing is saved");
    }

    private void RunBasics()
    {
        Check("the run uses the chosen ascent", Ascents.currentAscent == target, $"Ascents.currentAscent = {Ascents.currentAscent}");
        Check("the rules see the run", Rules.InRun && Rules.Level == target, $"in run {Rules.InRun}, level {Rules.Level}, scene {Rules.ActiveScene}");
        MapHandler map = Singleton<MapHandler>.Instance;
        Info("map", $"scene {SceneManager.GetActiveScene().name}, biomes {string.Join(", ", map.biomes)}, segment {Map.CurrentSegment}, place {Map.CurrentPlace}");
        apocalypse = Compat.Tfa.ApocalypseActive;
        Info("True Final Ascent", $"present {Compat.Tfa.Present}, FINAL ASCENT {Compat.Tfa.FinalAscentActive}, Apocalypse {apocalypse}");
        DayNightManager clock = DayNightManager.instance;
        Info("day", $"time {clock.timeOfDay:F2}, day {clock.dayStart:F1}-{clock.dayEnd:F1}, {clock.dayLengthInMinutes:F0} min per day");
    }

    private void CheckStorms()
    {
        MapHandler map = Singleton<MapHandler>.Instance;
        WindChillZone[] zones = map.GetComponentsInChildren<WindChillZone>(includeInactive: true);
        Info("weather zones", string.Join(", ", zones.Select(z => z.name + (z.gameObject.activeInHierarchy ? "" : " (off)") + (A25_AllWeather.IsCreated(z) ? " (added)" : ""))));
        if (target < Lvl.AllWeather)
        {
            Check("below ascent 25 the map keeps its own weather", !A25_AllWeather.DrivesVanillaWeather && !zones.Any(A25_AllWeather.IsCreated),
                $"{zones.Count(A25_AllWeather.IsCreated)} storm(s) added");
            return;
        }
        if (Compat.Tfa.ApocalypseActive)
        {
            Info("TFA's Apocalypse mode runs the weather in this climb; the storm checks are skipped");
            return;
        }
        Check("ascent 25 drives the storms", A25_AllWeather.DrivesVanillaWeather);
        foreach (string name in new[] { "RainStorm", "WindStorm", "SnowStorm" })
        {
            WindChillZone zone = zones.FirstOrDefault(z => z != null && z.name == name && z.gameObject.activeInHierarchy);
            Check($"{name} is on this map", zone != null,
                zone == null ? "missing" : $"{(A25_AllWeather.IsCreated(zone) ? "added by the mod" : "the map's own")}, {zone.GetComponentsInChildren<ParticleSystem>(true).Length} particle system(s)");
            if (zone != null)
            {
                Vector3 size = zone.windZoneBounds.size;
                Check($"{name} covers the whole mountain", size.x >= 10000f && size.y >= 10000f && size.z >= 10000f, size.ToString("F0"));
            }
        }
    }

    private IEnumerator Tornadoes(float wake)
    {
        Tornado[] extra = Array.Empty<Tornado>();
        if (target < Lvl.AllWeather)
        {
            yield return WaitUntilTime(wake + 10f);
            extra = FindObjectsByType<Tornado>(FindObjectsSortMode.None).Where(ExtraTornadoes.IsCustom).ToArray();
            Check("below ascent 25 no extra tornadoes", extra.Length == 0, $"{extra.Length} extra tornado(es) 10 s after waking up");
            yield break;
        }
        while (Time.time < wake + 20f)
        {
            extra = FindObjectsByType<Tornado>(FindObjectsSortMode.None).Where(ExtraTornadoes.IsCustom).ToArray();
            if (extra.Length > 0)
            {
                break;
            }
            yield return new WaitForSeconds(0.5f);
        }
        Check("an extra tornado roams near the scout", extra.Length > 0,
            extra.Length > 0 ? $"{extra.Length} at {Vector3.Distance(extra[0].transform.position, Local.Center):F0} m" : "none within 20 s of waking up");
        // Keep the rest of the test on the ground.
        ExtraTornadoes.untilNext = 100000f;
        foreach (Tornado tornado in extra)
        {
            if (tornado != null)
            {
                PhotonNetwork.Destroy(tornado.gameObject);
            }
        }
    }

    private void CheckRevives()
    {
        List<RespawnChest> statues = Luggage.ALL_LUGGAGE.OfType<RespawnChest>().Where(s => s != null).ToList();
        int visible = statues.Count(s => s.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled));
        int solid = statues.Count(s => s.GetComponentsInChildren<Collider>(true).Any(c => c.enabled));
        int usable = statues.Count(s => s.IsInteractible(Local));
        Check("revive statues are gone (19)", statues.Count > 0 && visible == 0 && solid == 0 && usable == 0,
            $"{statues.Count} statue(s): {visible} visible, {solid} solid, {usable} usable");
        float curse = Afflictions.GetCurrentStatus(Status.Curse);
        Check("scouts start with 20 curse (19)", Mathf.Abs(Ascents.startingCurse - A19_NoReviveStatues.StartingCurse) < 0.0001f && curse >= A19_NoReviveStatues.StartingCurse - 0.001f,
            $"starting curse {Ascents.startingCurse * 100f:F0}, the scout has {curse * 100f:F1}");

        // The game's own revives, on 30 petrification: one that gives status (a skeleton coming back) and
        // one that gives none (a scout effigy).
        Character local = Local;
        Unlocked(() =>
        {
            int petrify = local.data.petrifyAmount;
            float hunger = Afflictions.GetCurrentStatus(Status.Hunger);
            float before = Afflictions.GetCurrentStatus(Status.Curse);
            local.data.SetPetrify(30);
            local.ReviveCharacter(applyStatus: true);
            float added = Afflictions.GetCurrentStatus(Status.Curse) - before;
            int after = local.data.petrifyAmount;
            Afflictions.SetStatus(Status.Curse, before);
            local.ReviveCharacter(applyStatus: false);
            float addedPlain = Afflictions.GetCurrentStatus(Status.Curse) - before;
            int afterPlain = local.data.petrifyAmount;
            Check("every revive adds 15 curse (19)", Mathf.Abs(added - A19_NoReviveStatues.ReviveCurse) < 0.001f && Mathf.Abs(addedPlain - A19_NoReviveStatues.ReviveCurse) < 0.001f,
                $"{added * 100f:F1} curse with status, {addedPlain * 100f:F1} without (the game: 15 and 0)");
            Check("a revive keeps petrification (19)", after == 30 && afterPlain == 30, $"petrification 30 -> {after}, then {afterPlain} (the game takes 75 off)");
            local.data.SetPetrify(petrify);
            Afflictions.SetStatus(Status.Curse, before);
            Afflictions.SetStatus(Status.Hunger, hunger);
        });
    }

    private void CheckTropicsOnly()
    {
        TickTrigger[] spots = FindObjectsByType<TickTrigger>(FindObjectsSortMode.None);
        int outside = spots.Count(s => Map.TryGetBiome(s, out Biome.BiomeType biome) && biome != Biome.BiomeType.Tropics);
        string census = spots.Length == 0 ? "none" : string.Join(", ", spots.GroupBy(BiomeOf).Select(g => $"{g.Key} {g.Count()}"));
        Info("armed tick spots", $"{census}; {A17_Hazards.TickSpotsRemoved} removed outside the Tropics so far");
        Check("no tick spots outside the Tropics (17)", outside == 0, $"{outside} armed outside the Tropics");
        List<Beehive> hives = Beehive.ALL_BEEHIVES.Where(h => h != null).ToList();
        Info("beehives", hives.Count == 0 ? "none" : string.Join(", ", hives.GroupBy(BiomeOf).Select(g => $"{g.Key} {g.Count()}")));
        Info("ground below the scout", $"{GroundBiome(Local)} (place {Map.CurrentPlace})");
    }

    private IEnumerator SunHeat(bool expectHeat, string where)
    {
        Character local = Local;
        if (!InSunlight(local))
        {
            DayNightManager clock = DayNightManager.instance;
            Info($"sun check {where} skipped: the scout is not in direct sunlight", $"sun on {clock.sun.enabled}, intensity {clock.sun.intensity:0.00}");
            yield break;
        }
        Protect(false);
        Afflictions.SetStatus(Status.Hot, 0f);
        Afflictions.SetStatus(Status.Cold, 0f);
        Meter heat = new Meter(Status.Hot);
        try
        {
            yield return new WaitForSeconds(2f);
        }
        finally
        {
            heat.Stop();
            Protect(true);
        }
        // Ascent 23's sun heat (0.005/s), as it comes: ascent 14's boost leaves the sun alone.
        float expected = 0.005f * heat.Seconds;
        if (expectHeat)
        {
            Check($"the sun heats {where} (23)", heat.Total > expected * 0.75f && heat.Total < expected * 1.25f, $"{heat.Total:0.0000} heat in {heat.Seconds:F1} s (expected {expected:0.0000})");
        }
        else
        {
            Check($"no sun heat {where} (23)", heat.Total < 0.0005f, $"{heat.Total:0.0000} heat in {heat.Seconds:F1} s in direct sunlight");
        }
    }

    private static float Roasted(Item item) => item != null && A23_HotSun.exposures.TryGetValue(item, out A23_HotSun.Exposure exposure) ? exposure.progress : 0f;

    /// <summary>
    /// Only what shields the scout from the sun roasts: a closed parasol in the sun stays cool, an open
    /// one keeps the sun's heat off, roasts clean for its first quarter, then smokes heavily (30 s in
    /// all for a parasol, half in the Mesa), is fully burned in one go and stays in the hand.
    /// </summary>
    private IEnumerator Roasting()
    {
        Character local = Local;
        Item parasolPrefab = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i != null && i.GetComponent<Parasol>() != null);
        if (parasolPrefab == null)
        {
            Info("no parasol in the item database");
            yield break;
        }
        yield return SpawnIntoHand(parasolPrefab);
        Item item = held;
        Parasol parasol = item != null ? item.GetComponent<Parasol>() : null;
        if (parasol == null)
        {
            yield break;
        }
        if (!InSunlight(local))
        {
            Info("roasting check skipped: the scout is not in direct sunlight");
            yield break;
        }
        if (parasol.isOpen)
        {
            parasol.ToggleOpen();
        }
        yield return new WaitForSeconds(2f);
        Check("a closed parasol in the sun does not roast (23)", Roasted(item) <= 0f, $"{Roasted(item):0.000} roasted after 2 s in direct sunlight");

        parasol.ToggleOpen();
        yield return new WaitForSeconds(0.2f);
        Check("the parasol opens", parasol.isOpen);
        Shield(true);
        Meter sunHeat = new Meter(Status.Hot);
        float start = Time.time;
        try
        {
            yield return new WaitForSeconds(3f);
        }
        finally
        {
            sunHeat.Stop();
            Protect(true);
        }
        float seconds = Time.time - start;
        float progress = Roasted(item);
        float total = Map.InMesa ? 15f : 30f;
        float expected = seconds / total;
        Check($"an open parasol in the sun roasts in {total:F0} s (23)", Mathf.Abs(progress - expected) < expected * 0.15f, $"{progress:0.000} roasted in {seconds:F1} s (expected {expected:0.000})");
        Check("the open parasol keeps the sun's heat off (23)", sunHeat.Total < 0.001f, $"{sunHeat.Total:0.0000} heat in {seconds:F1} s");
        if (!A23_HotSun.exposures.TryGetValue(item, out A23_HotSun.Exposure exposure))
        {
            yield break;
        }
        ParticleSystem SunSmoke() => item != null && item.transform.Find(A23_HotSun.SunSmokeName) is Transform t ? t.GetComponent<ParticleSystem>() : null;
        if (!Map.InMesa)
        {
            Check("no smoke for the first quarter of its time (23)", !exposure.smoking && (SunSmoke() == null || !SunSmoke().isEmitting),
                $"smoking {exposure.smoking} after {seconds:F1} s");
        }
        // Past the clean quarter: it smokes from here on.
        exposure.progress = Mathf.Max(exposure.progress, 0.3f);
        yield return new WaitForSeconds(0.5f);
        ParticleSystem smoke = SunSmoke();
        Check("the roasting parasol then smokes heavily, still uncooked (23)", exposure.smoking && smoke != null && smoke.isEmitting && BurnedItems.CookedLevel(item) == 0,
            $"smoking {exposure.smoking}, smoke {(smoke == null ? "none" : smoke.isEmitting ? $"rising, {smoke.emission.rateOverTimeMultiplier:F0}/s" : "off")}, cooked {BurnedItems.CookedLevel(item)}");
        // Close to the end of its time: from here it burns in the sun as usual.
        exposure.progress = Mathf.Max(exposure.progress, 0.97f);
        float end = Time.realtimeSinceStartup + 5f;
        List<int> levels = new List<int>();
        while (Time.realtimeSinceStartup < end && item != null && !BurnedItems.IsBurned(item))
        {
            int level = BurnedItems.CookedLevel(item);
            if (levels.Count == 0 || levels[levels.Count - 1] != level)
            {
                levels.Add(level);
            }
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        int final = item != null ? BurnedItems.CookedLevel(item) : -1;
        levels.Add(final);
        bool skipped = levels.All(level => level == 0 || level >= BurnedItems.BurntCookLevel);
        Check("when its time is up the parasol is fully burned in one go (23)", item != null && BurnedItems.IsBurned(item) && skipped,
            $"cooked levels seen: {string.Join(" -> ", levels)}");
        // Burned and still held up to the sun: it keeps shielding, smokes, and crumbles away after a quarter of its roasting time.
        float burnedAt = Time.time - 0.5f;
        float crumbleExpected = total * A23_HotSun.CrumbleSeconds / A23_HotSun.BurnSeconds;
        Meter burnedHeat = new Meter(Status.Hot);
        bool smokedWhileBurned = false;
        int crumbledBefore = A23_HotSun.Crumbled;
        int soundsBefore = A23_HotSun.CrumbleSoundPlays;
        int ashBefore = A23_HotSun.AshPuffs;
        int flakesBefore = A23_HotSun.AshFlakes;
        float crumbledAt = -1f;
        while (Time.time - burnedAt < crumbleExpected + 3f && local.data.currentItem != null && local.data.currentItem.itemID == parasolPrefab.itemID)
        {
            Item held = local.data.currentItem;
            smokedWhileBurned |= A23_HotSun.exposures.TryGetValue(held, out A23_HotSun.Exposure crumbling) && crumbling.crumbling && crumbling.smoking;
            if (crumbledAt < 0f && A23_HotSun.Crumbled > crumbledBefore)
            {
                crumbledAt = Time.time - burnedAt;
            }
            yield return null;
        }
        float lasted = crumbledAt >= 0f ? crumbledAt : Time.time - burnedAt;
        float lingered = Time.time - burnedAt - lasted;
        burnedHeat.Stop();
        yield return new WaitForSeconds(0.5f);
        bool gone = (local.data.currentItem == null || local.data.currentItem.itemID != parasolPrefab.itemID)
            && local.player.itemSlots.All(s => s.IsEmpty() || s.prefab == null || s.prefab.itemID != parasolPrefab.itemID);
        Check($"the burned parasol still shields, smokes and crumbles away after {crumbleExpected:0.#} s more in the sun (23)",
            gone && smokedWhileBurned && A23_HotSun.Crumbled == crumbledBefore + 1 && Mathf.Abs(lasted - crumbleExpected) < 0.8f && burnedHeat.Total < 0.001f,
            $"lasted {lasted:0.0} s (expected {crumbleExpected:0.#}), smoked {smokedWhileBurned}, gone from hand and slots {gone}, sun heat meanwhile {burnedHeat.Total * 100f:0.##}");
        Check("it goes up with the sound of burning and turns to the Kiln's ash at once (23)",
            A23_HotSun.CrumbleSoundPlays == soundsBefore + 1 && A23_HotSun.AshPuffs == ashBefore + 1 && A23_HotSun.AshFlakes - flakesBefore >= 60 && lingered < 0.2f,
            $"burning sounds {A23_HotSun.CrumbleSoundPlays - soundsBefore}, ash {A23_HotSun.AshPuffs - ashBefore} ({A23_HotSun.AshFlakes - flakesBefore} flakes), still in the hand {lingered:0.00} s after");
        Screenshot("crumbled-ash");
        // Put it away so the next checks start with free hands.
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        for (byte slot = 0; slot < local.player.itemSlots.Length; slot++)
        {
            ItemSlot itemSlot = local.player.itemSlots[slot];
            if (!itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == parasolPrefab.itemID)
            {
                local.player.EmptySlot(Optionable<byte>.Some(slot));
            }
        }
        Afflictions.UpdateWeight();
        held = null;
    }

    /// <summary>
    /// The map's second segment (Tropics or Roots): ticks, beehives and bee ambushes only where the
    /// ground is Tropics. The jump lights the beach's campfire next to the scout: one cooking cycle
    /// there burns an item (ascent 16).
    /// </summary>
    private IEnumerator SecondSegment()
    {
        Character local = Local;
        // The first campfire, when lit, has already brought the scouts here.
        if (Singleton<MapHandler>.Instance.currentSegment != (int)Segment.Tropics)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(4f);
        }
        Info("in the second segment", $"place {Map.CurrentPlace}, biome {Map.CurrentBiome}");
        Step("Tropics only in the second segment (17)", CheckTropicsOnly);
        if (Map.CurrentPlace != Place.Tropics)
        {
            Check("no bee ambushes outside the Tropics (17)", !A17_BeeAmbush.InTropics(local), $"ground {GroundBiome(local)}, place {Map.CurrentPlace}");
        }

        Campfire campfire = Campfire.ALL_CAMPFIRES.Where(c => c != null && c.Lit).OrderBy(c => Vector3.Distance(c.transform.position, local.Center)).FirstOrDefault();
        if (campfire == null)
        {
            Info("campfire check skipped: no lit campfire");
            yield break;
        }
        Item food = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values
            .Where(i => i != null && i.UIData != null && i.UIData.canPocket && i.GetComponent<ItemCooking>() is ItemCooking cooking && cooking.canBeCooked && !cooking.wreckWhenCooked && i.GetComponent<Action_RestoreHunger>() != null)
            .OrderBy(i => i.itemID)
            .FirstOrDefault();
        if (food == null)
        {
            Info("campfire check skipped: no cookable food found");
            yield break;
        }
        yield return SpawnIntoHand(food);
        Item item = held;
        if (item == null)
        {
            yield break;
        }
        // Step up to the campfire (cooking needs it within the game's 2 m reach), on the camp side,
        // and cook right away, before wind or storms can push the scout off again.
        Vector3 toCamp = MapHandler.CurrentBaseCampSpawnPoint != null ? MapHandler.CurrentBaseCampSpawnPoint.position - campfire.transform.position : local.Center - campfire.transform.position;
        toCamp.y = 0f;
        Vector3 standAt = campfire.transform.position + (toCamp.sqrMagnitude > 0.01f ? toCamp.normalized : Vector3.forward) * 1.6f + Vector3.up;
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standAt, false);
        yield return null;
        yield return null;
        float distance = Vector3.Distance(campfire.transform.position, local.Center);
        // One cooking cycle at the campfire: it starts cooking the held item and, when the cast is
        // done, finishes it (called directly; the game's own cast would also count a Steam stat).
        campfire.Interact(local);
        item.cooking.FinishCooking();
        campfire.CancelCast(local);
        yield return new WaitForSeconds(0.5f);
        bool burned = item != null && BurnedItems.IsBurned(item);
        string detail = item == null ? "the item is gone" : $"{item.gameObject.name}: cooked {item.GetData<IntItemData>(DataEntryKey.CookedAmount).Value} after one cycle, {distance:F1} m from the campfire";
        Check("one cooking cycle at a campfire burns the item (17)", burned, detail);
    }

    /// <summary>After the Alps the sky and the night cold are back to the normal day.</summary>
    private IEnumerator AfterAlps()
    {
        MapHandler.JumpToSegment(Segment.Caldera);
        yield return new WaitForSeconds(4f);
        Info("jumped past the Alps", $"place {Map.CurrentPlace}");
        DayNightManager clock = DayNightManager.instance;
        Check("the Alpine sky has faded out (18)", A18_AlpineNights.blend <= 0f, $"blend {A18_AlpineNights.blend:0.00}");
        float quarter = (clock.dayEnd - clock.dayStart) / 4f;
        foreach ((string name, float time) in new[] { ("first", clock.dayStart + quarter * 0.5f), ("last", clock.dayEnd - quarter * 0.5f) })
        {
            clock.timeOfDay = time;
            yield return new WaitForSeconds(1f);
            Check($"after the Alps the {name} quarter of the day is daylight again (18)", clock.sun.enabled && !clock.moon.enabled && !A18_AlpineNights.DarkQuarterNow(clock),
                $"{clock.FloatToTimeString(time)}: sun {clock.sun.enabled}, moon {clock.moon.enabled}");
        }
    }

    /// <summary>
    /// The Caldera's lava river (18) rises and falls between the game's lowest level and 15 m above it,
    /// about once a minute: watched from the Caldera's start for a whole cycle (63 s), facing the river.
    /// </summary>
    private IEnumerator CalderaTideHeight()
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather))
        {
            Info("the Caldera lava check is skipped", $"testing ascent {target}");
            yield break;
        }
        if (!Map.InVolcano)
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(4f);
        }
        Lava river = FindObjectsByType<Lava>(FindObjectsSortMode.None).FirstOrDefault(lava => lava.name == "River");
        if (!Map.InVolcanoCaldera || river == null)
        {
            Info("the Caldera lava check is skipped", $"no Caldera lava river here ({Map.CurrentPlace}, river {river != null})");
            yield break;
        }
        Character local = Local;
        Vector2 look = local.data.lookValues;
        Vector3 toward = Vector3.ProjectOnPlane(river.transform.position - local.Head, Vector3.up);
        if (toward.sqrMagnitude > 0.01f)
        {
            local.data.lookValues = new Vector2(Quaternion.LookRotation(toward).eulerAngles.y, -20f);
        }

        // The small rocks standing in the lava (their foot in it, at most 12 m across, by their colliders): a camera on
        // the screen looks at a group of them from low down, just above the high tide, the whole cycle.
        Vector3 riverAt = river.transform.position;
        List<Bounds> rocks = Physics.OverlapBox(new Vector3(riverAt.x, 790f, riverAt.z), new Vector3(170f, 20f, 240f), Quaternion.identity, HelperFunctions.terrainMapMask)
            .Select(c => c.bounds)
            .Where(b => b.size.x < 12f && b.size.z < 12f && b.size.y > 1f && b.min.y < 786f && b.max.y > 781f && b.max.y < 806f)
            .ToList();
        GameObject eye = null;
        if (rocks.Count > 0)
        {
            Vector3 scout = local.Center;
            // The rock with the most other small rocks within 12 m.
            Bounds focus = rocks.OrderByDescending(b => rocks.Count(o => HelperFunctions.FlatDistance(o.center, b.center) < 12f)).First();
            Vector3 from = Vector3.ProjectOnPlane(scout - focus.center, Vector3.up).normalized;
            if (from.sqrMagnitude < 0.01f)
            {
                from = Vector3.back;
            }
            eye = new GameObject("UltimateAscentTestCamera");
            Camera camera = eye.AddComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            // Low: 2.5 m above the high tide, looking nearly level at the rocks.
            Vector3 target = new Vector3(focus.center.x, 789f, focus.center.z);
            eye.transform.position = new Vector3(focus.center.x, 795.5f, focus.center.z) + from * 18f;
            eye.transform.LookAt(target);
            int covered = rocks.Count(b => b.max.y <= 793f);
            Info("the small rocks in the lava river", $"{rocks.Count} rocks, tops from {rocks.Min(b => b.max.y):0.0} to {rocks.Max(b => b.max.y):0.0} (half below {rocks.Select(b => b.max.y).OrderBy(y => y).ElementAt(rocks.Count / 2):0.0}); at the high tide (793) {covered} of them are under the lava; the camera looks at the one at {focus.center} (top {focus.max.y:0.0})");
        }
        else
        {
            Info("the small rocks in the lava river", "none found");
        }
        const float Watch = 63f;
        float lowest = float.MaxValue, highest = float.MinValue, lowAt = 0f, highAt = 0f;
        bool lowShown = false, highShown = false;
        float start = Time.time;
        while (Time.time - start < Watch)
        {
            float y = river.transform.position.y;
            if (eye != null && !highShown && y > 792.9f)
            {
                highShown = true;
                RenderView(eye, "caldera-lava-high");
            }
            if (eye != null && !lowShown && y < 779.1f)
            {
                lowShown = true;
                RenderView(eye, "caldera-lava-low");
            }
            if (y < lowest)
            {
                lowest = y;
                lowAt = Time.time - start;
            }
            if (y > highest)
            {
                highest = y;
                highAt = Time.time - start;
            }
            yield return null;
        }
        local.data.lookValues = look;
        if (eye != null)
        {
            Destroy(eye);
        }
        Check("the Caldera's lava river rises and falls 14 m, from 779 to 793 (18)", Mathf.Abs(lowest - 779f) < 0.1f && Mathf.Abs(highest - 793f) < 0.1f,
            $"lowest {lowest:0.00} at {lowAt:0} s, highest {highest:0.00} at {highAt:0} s, watched {Watch:0} s");
    }

    /// <summary>The Caldera's condors fly 1.35 times as fast (17): circling, diving and carrying, each against its own first values.</summary>
    private IEnumerator FasterCondorsCheck()
    {
        if (!Rules.Enabled(Lvl.Hazards))
        {
            Info("the condor check is skipped", $"testing ascent {target}");
            yield break;
        }
        if (!Map.InVolcanoCaldera)
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(6f);
        }
        Condor[] condors = FindObjectsByType<Condor>(FindObjectsSortMode.None);
        if (condors.Length == 0)
        {
            Info("the condor check is skipped", $"no condors here ({Map.CurrentPlace})");
            yield break;
        }
        yield return new WaitForFixedUpdate();
        List<string> seen = new List<string>();
        bool all = true;
        foreach (Condor condor in condors)
        {
            float loop = Overrides.Original(condor, "loopDuration", -1f);
            float swoop = Overrides.Original(condor, "swoopTime", -1f);
            float nest = Overrides.Original(condor, "moveToNestSpeed", -1f);
            bool faster = Mathf.Abs(condor.loopDuration * A17_Hazards.CondorSpeed - loop) < 0.01f && Mathf.Abs(condor.swoopTime * A17_Hazards.CondorSpeed - swoop) < 0.01f
                && Mathf.Abs(condor.moveToNestSpeed - nest * A17_Hazards.CondorSpeed) < 0.01f;
            all &= faster;
            seen.Add($"loop {loop:0.#} -> {condor.loopDuration:0.##} s, dive {swoop:0.##} -> {condor.swoopTime:0.##} s, carry {nest:0.#} -> {condor.moveToNestSpeed:0.##} m/s");
        }
        Check("the Caldera's condors fly 1.35 times as fast: circling, diving and carrying (17)", all, $"{condors.Length} condors: {string.Join("; ", seen)}");
    }

    /// <summary>The seconds a snowball has left in the hands (its use bar's), or -1 without one.</summary>
    private static float SnowballLeft(Item item) =>
        item != null && item.data != null && item.data.TryGetDataEntry(DataEntryKey.Fuel, out FloatItemData left) ? left.Value : -1f;

    /// <summary>
    /// Holding a snowball chills 2.5 a second from ascent 15 (17 before): a snowball in the hand, the chill
    /// counted for 4 s, then 3 s more with Ascent 15 switched off (in memory), which stops it and its melting.
    /// Then it melts: its use bar shows the seconds left of 20, stashed for 2 s it keeps them, and taken out again
    /// it is gone (from the hand and the slot) when they run out.
    /// </summary>
    private IEnumerator HeldSnowball()
    {
        if (Rules.Level < Lvl.WorseItems)
        {
            Info("the held snowball check is skipped", $"testing ascent {target}");
            yield break;
        }
        Item snowball = ItemByName("Snowball");
        if (snowball == null)
        {
            Fail("a snowball is in the item database");
            yield break;
        }
        Character local = Local;
        yield return SpawnIntoHand(snowball);
        if (held == null)
        {
            Fail("a snowball can be taken into the hand");
            yield break;
        }
        BepInEx.Configuration.ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool saved = rule.Value;
        float leftBeforeOff = -1f;
        float leftAfterOff = -1f;
        List<(float at, float amount)> chills = new List<(float, float)>();
        float started = 0f;
        Action<Status, float> listener = (type, amount) =>
        {
            // Amounts far below one step come from elsewhere (the snowball's chill lands in whole steps).
            if (type == Status.Cold && FogCalls.Depth == 0 && amount >= 0.001f)
            {
                chills.Add((Time.time - started, amount));
            }
        };
        int withRuleOff = 0;
        Shield(true);
        Afflictions.OnAddedIncrementalStatus += listener;
        try
        {
            rule.Value = true;
            started = Time.time;
            yield return new WaitForSeconds(4.2f);
            int withRule = chills.Count;
            rule.Value = false;
            leftBeforeOff = SnowballLeft(local.data.currentItem);
            yield return new WaitForSeconds(3f);
            leftAfterOff = SnowballLeft(local.data.currentItem);
            withRuleOff = chills.Count - withRule;
            chills.RemoveRange(withRule, withRuleOff);
        }
        finally
        {
            Afflictions.OnAddedIncrementalStatus -= listener;
            rule.Value = saved;
            Protect(true);
        }
        List<float> gaps = new List<float>();
        for (int i = 1; i < chills.Count; i++)
        {
            gaps.Add(chills[i].at - chills[i - 1].at);
        }
        Check("holding a snowball chills 2.5 a second (15), and not with Ascent 15 switched off",
            chills.Count >= 3 && chills.All(c => Mathf.Abs(c.amount - A15_StrongerAilments.Step) < 0.001f) && gaps.All(g => Mathf.Abs(g - 1f) < 0.1f) && withRuleOff == 0,
            $"cold at {string.Join(", ", chills.Select(c => $"{c.at:0.00} s (+{c.amount * 100f:0.#})"))}; {withRuleOff} with Ascent 15 off");

        // Melting (Ascent 15 on again): the time left, kept while stashed, and gone when it runs out.
        Item inHand = local.data.currentItem;
        float leftNow = SnowballLeft(inHand);
        float bar = inHand != null && inHand.data.TryGetDataEntry(DataEntryKey.UseRemainingPercentage, out FloatItemData shown) ? shown.Value : -1f;
        Check("a held snowball's 20 s run down only with Ascent 15 on, and its use bar shows what is left",
            leftBeforeOff > 0f && leftBeforeOff < A14_Items.SnowballSeconds - 3.5f && leftBeforeOff > A14_Items.SnowballSeconds - 6f && Mathf.Abs(leftAfterOff - leftBeforeOff) < 0.05f
            && Mathf.Abs(bar - leftNow / A14_Items.SnowballSeconds) < 0.02f,
            $"{leftBeforeOff:0.00} s left after 4.2 s held, {leftAfterOff:0.00} s after 3 s more with Ascent 15 off; bar {bar:0.00} for {leftNow:0.00} s");
        byte? snowSlot = null;
        for (byte slot = 0; slot < local.player.itemSlots.Length; slot++)
        {
            ItemSlot itemSlot = local.player.itemSlots[slot];
            if (!itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == snowball.itemID)
            {
                snowSlot = slot;
            }
        }
        if (snowSlot.HasValue)
        {
            try
            {
                rule.Value = true;
                foreach (object step in SnowballMelts(local, snowball, snowSlot.Value))
                {
                    yield return step;
                }
            }
            finally
            {
                rule.Value = saved;
            }
        }
        else
        {
            Fail("the held snowball is in a slot");
        }
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        for (byte slot = 0; slot < local.player.itemSlots.Length; slot++)
        {
            ItemSlot itemSlot = local.player.itemSlots[slot];
            if (!itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == snowball.itemID)
            {
                local.player.EmptySlot(Optionable<byte>.Some(slot));
            }
        }
        Afflictions.UpdateWeight();
        held = null;
    }

    /// <summary>The held snowball stashed for 2 s, taken out again, then held until it melts.</summary>
    private IEnumerable SnowballMelts(Character local, Item snowball, byte snowSlot)
    {
        {
            float beforeStash = SnowballLeft(local.data.currentItem);
            local.refs.items.EquipSlot(Optionable<byte>.None);
            yield return new WaitForSeconds(2f);
            local.refs.items.lastEquippedSlotTime = 0f;
            local.refs.items.EquipSlot(Optionable<byte>.Some(snowSlot));
            float until = Time.time + 3f;
            while (Time.time < until && (local.data.currentItem == null || local.data.currentItem.itemID != snowball.itemID))
            {
                yield return null;
            }
            float afterStash = SnowballLeft(local.data.currentItem);
            float takenOut = Time.time;
            Check("a stashed snowball keeps its time", afterStash > 0f && afterStash <= beforeStash && beforeStash - afterStash < 0.3f,
                $"{beforeStash:0.00} s left when stashed, {afterStash:0.00} s after 2 s in the slot and taken out again");
            Shield(true);
            bool pictured = false;
            float deadline = takenOut + afterStash + 3f;
            while (Time.time < deadline && local.data.currentItem != null && local.data.currentItem.itemID == snowball.itemID)
            {
                if (!pictured && SnowballLeft(local.data.currentItem) < A14_Items.SnowballSeconds * 0.5f)
                {
                    pictured = true;
                    Screenshot("snowball-melting");
                }
                yield return null;
            }
            float lasted = Time.time - takenOut;
            Protect(true);
            yield return new WaitForSeconds(0.5f);
            bool gone = (local.data.currentItem == null || local.data.currentItem.itemID != snowball.itemID)
                && local.player.itemSlots.All(s => s.IsEmpty() || s.prefab == null || s.prefab.itemID != snowball.itemID);
            Check("a held snowball melts when its 20 s run out: gone from the hand and the slot", gone && Mathf.Abs(lasted - afterStash) < 0.6f,
                $"{afterStash:0.00} s were left, it lasted {lasted:0.00} s more in the hand; gone {gone}");
        }
    }

    /// <summary>
    /// The Auto-Parachute (9, 15): worn in slot 4, 3 charges, spawning where the Jetpack does and as
    /// rarely. One is put on the ground and put on the way a scout puts on a backpack, its model on the
    /// scout's back is pictured from behind, then the scout falls from 30 m twice: it opens by itself and
    /// the landing hurts nothing. The first fall (Ascent 15 switched off, in memory) takes one charge,
    /// the second (switched on: two a save) the last two, and it is gone.
    /// </summary>
    /// <summary>
    /// The six-slot Backpack (single-player; the test plays solo, offline): a Backpack put on the ground has
    /// six slots and the six-slot model, and its wheel seven slices spread evenly; six items stashed the
    /// game's way hang in the bag and the pouches; its contents written and read back keep all six; worn,
    /// the scout's back shows the model and keeps all six. Told to behave as in multiplayer (in memory), a
    /// new backpack has the game's four slots and the game's model.
    /// </summary>
    private IEnumerator SixSlotBackpackCheck()
    {
        Item template = ItemByName("Backpack");
        // Small things in the bag, tall ones in the pouches, to see the lower pouches' items past the upper ones'.
        string[] stash = { "Airplane Food", "Granola Bar", "Lantern", "FortifiedMilk", "Energy Drink", "Sunscreen" };
        List<Item> things = stash.Select(ItemByName).ToList();
        if (template == null || things.Any(thing => thing == null))
        {
            Fail("the Backpack and six items to stash are in the item database", $"Backpack {template != null}, missing: {string.Join(", ", stash.Where((name, i) => things[i] == null))}");
            yield break;
        }
        Character local = Local;
        Vector2 look = local.data.lookValues;
        Check("the test plays single-player (offline), where the backpack has six slots", SixSlotBackpack.On, $"offline {PhotonNetwork.OfflineMode}");
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        Backpack backpack = SpawnInFront(template, 1.4f)?.GetComponent<Backpack>();
        if (backpack == null)
        {
            Fail("a Backpack can be put on the ground");
            yield break;
        }
        yield return new WaitForSeconds(1.5f);
        BackpackData data = backpack.GetData<BackpackData>(DataEntryKey.BackpackData);
        MeshFilter groundFilter = backpack.groundMesh != null ? backpack.groundMesh.GetComponent<MeshFilter>() : null;
        MeshFilter wornFilter = backpack.heldMesh != null ? backpack.heldMesh.GetComponent<MeshFilter>() : null;
        BackpackVisuals visuals = backpack.GetComponent<BackpackVisuals>();
        float anchorsOff = -1f;
        if (wornFilter != null && visuals != null && visuals.backpackSlots.Length >= 6 && SixSlotBackpack.pouches != null)
        {
            anchorsOff = Enumerable.Range(0, 4).Max(i => Vector3.Distance(visuals.backpackSlots[i + 2].position, wornFilter.transform.TransformPoint(SixSlotBackpack.pouches[i])));
        }
        Check("a Backpack on the ground has six slots and the six-slot model, slots 3-6 hanging in its pouches",
            data.itemSlots.Length == 6 && groundFilter != null && groundFilter.sharedMesh == SixSlotBackpack.ground && wornFilter != null && wornFilter.sharedMesh == SixSlotBackpack.worn && anchorsOff >= 0f && anchorsOff < 0.01f,
            $"{data.itemSlots.Length} slots, ground mesh '{(groundFilter != null && groundFilter.sharedMesh != null ? groundFilter.sharedMesh.name : "none")}', worn mesh '{(wornFilter != null && wornFilter.sharedMesh != null ? wornFilter.sharedMesh.name : "none")}', pouch anchors at most {anchorsOff:0.000} m off");
        Texture shownIcon = backpack.UIData.GetIcon();
        Check("the Backpack has the six-slot icon, in the inventory and on the ground",
            SixSlotBackpack.icon != null && template.UIData.icon == SixSlotBackpack.icon && shownIcon == SixSlotBackpack.icon,
            $"the Backpack's icon '{(template.UIData.icon != null ? template.UIData.icon.name : "none")}', this one's '{(shownIcon != null ? shownIcon.name : "none")}'");
        local.data.lookValues = new Vector2(look.x, -45f);
        yield return new WaitForSeconds(1f);
        Screenshot("six-slot-backpack-ground-empty");
        yield return new WaitForSeconds(0.3f);
        Step("open the six-slot backpack's wheel", () =>
        {
            backpack.Interact(local);
            BackpackWheel wheel = GUIManager.instance.backpackWheel;
            int active = wheel.slices.Count(slice => slice != null && slice.gameObject.activeSelf);
            List<float> angles = wheel.slices.Select(slice => Mathf.Repeat(slice.transform.localEulerAngles.z, 360f)).OrderBy(a => a).ToList();
            float step = 360f / 7f;
            float worst = 0f;
            for (int i = 0; i < angles.Count; i++)
            {
                float gap = (i + 1 < angles.Count ? angles[i + 1] : angles[0] + 360f) - angles[i];
                worst = Mathf.Max(worst, Mathf.Abs(gap - step));
            }
            Texture wear = wheel.slices[0].image != null ? wheel.slices[0].image.texture : null;
            Check("its wheel has seven slices (wearing it and six slots), spread evenly, the wearing slice with the six-slot icon",
                wheel.gameObject.activeSelf && wheel.slices.Length == 7 && active == 7 && worst < 0.5f && wear == SixSlotBackpack.icon,
                $"open {wheel.gameObject.activeSelf}, {wheel.slices.Length} slices, {active} shown, angles {string.Join(", ", angles.Select(a => a.ToString("0.0")))}, furthest from even {worst:0.0}°, wearing slice '{(wear != null ? wear.name : "none")}'");
            Screenshot("six-slot-backpack-wheel");
        });
        yield return null;
        GUIManager.instance.CloseBackpackWheel();
        yield return new WaitForSeconds(0.3f);
        // Six items, stashed the game's way: each in the hand, then into its slot.
        for (byte slot = 0; slot < 6; slot++)
        {
            yield return SpawnIntoHand(things[slot]);
            if (held == null)
            {
                yield break;
            }
            byte into = slot;
            Step($"stash {stash[slot]} in slot {slot + 1}", () => backpack.Stash(local, into));
            yield return new WaitForSeconds(0.6f);
        }
        yield return new WaitForSeconds(1f);
        List<string> contents = data.itemSlots.Select(s => s.IsEmpty() ? "empty" : s.prefab.gameObject.name).ToList();
        int hanging = Enumerable.Range(0, 6).Count(i => visuals.TryGetSpawnedItem((byte)i, out Item shown) && shown.backpackSlotTransform == visuals.backpackSlots[i]);
        Check("six items stashed the game's way fill all six slots and hang on the backpack, 3-6 in the pouches",
            Enumerable.Range(0, 6).All(i => !data.itemSlots[i].IsEmpty() && data.itemSlots[i].prefab.itemID == things[i].itemID) && hanging == 6,
            $"slots: {string.Join(", ", contents)}; {hanging} of 6 shown at their anchors");
        local.data.lookValues = new Vector2(look.x, -45f);
        yield return new WaitForSeconds(1f);
        Screenshot("six-slot-backpack-ground-full");
        yield return new WaitForSeconds(0.3f);
        local.data.lookValues = look;
        yield return FrontPicture(backpack, 0f, "six-slot-backpack-pouches-front");
        yield return FrontPicture(backpack, 40f, "six-slot-backpack-pouches-side");
        Step("the full backpack's wheel", () =>
        {
            backpack.Interact(local);
            BackpackWheel wheel = GUIManager.instance.backpackWheel;
            int icons = Enumerable.Range(1, 6).Count(i => wheel.slices[i].image != null && wheel.slices[i].image.enabled && wheel.slices[i].image.texture == things[i - 1].UIData.GetIcon());
            Check("its wheel shows each slot's item", icons == 6, $"{icons} of 6 slices show their item");
            Screenshot("six-slot-backpack-wheel-full");
        });
        yield return null;
        GUIManager.instance.CloseBackpackWheel();
        // Written and read back (as a save or a sync does): all six stay.
        Step("six-slot contents written and read back", () =>
        {
            BinarySerializer writer = new BinarySerializer(4000, Unity.Collections.Allocator.Temp);
            data.Serialize(writer);
            byte[] bytes = writer.buffer.ToByteArray();
            writer.Dispose();
            BackpackData copy = new BackpackData();
            copy.Init();
            BinaryDeserializer reader = new BinaryDeserializer(bytes, Unity.Collections.Allocator.Temp);
            copy.Deserialize(reader);
            reader.Dispose();
            Check("written and read back, the six-slot contents keep all six items", copy.itemSlots.Length == 6 && Enumerable.Range(0, 6).All(i => !copy.itemSlots[i].IsEmpty() && copy.itemSlots[i].prefab.itemID == things[i].itemID),
                $"{bytes.Length} bytes; read back: {string.Join(", ", copy.itemSlots.Select(s => s.IsEmpty() ? "empty" : s.prefab.gameObject.name))}");
        });
        local.data.lookValues = look;
        local.refs.items.lastEquippedSlotTime = 0f;
        backpack.Wear(local);
        for (float until = Time.realtimeSinceStartup + 4f; Time.realtimeSinceStartup < until && local.player.backpackSlot.IsEmpty();)
        {
            yield return null;
        }
        yield return new WaitForSeconds(1f);
        BackpackOnBackVisuals back = local.GetComponentsInChildren<BackpackOnBackVisuals>(true).FirstOrDefault(v => v.backpackType == BackpackSlot.BackpackType.Backpack);
        MeshFilter backFilter = back != null ? back.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh == SixSlotBackpack.worn) : null;
        BackpackData worn = null;
        if (!local.player.backpackSlot.IsEmpty())
        {
            local.player.backpackSlot.data.TryGetDataEntry(DataEntryKey.BackpackData, out worn);
        }
        int onBack = back == null ? 0 : Enumerable.Range(0, 6).Count(i => back.TryGetSpawnedItem((byte)i, out Item shown) && shown.backpackSlotTransform == back.backpackSlots[i]);
        Check("worn, the six-slot backpack keeps all six items and the scout's back shows the six-slot model",
            worn != null && worn.itemSlots.Length == 6 && worn.itemSlots.All(s => !s.IsEmpty()) && backFilter != null && onBack == 6 && local.player.backpackSlot.prefab.UIData.icon == SixSlotBackpack.icon,
            $"worn {worn != null}, {(worn != null ? worn.itemSlots.Count(s => !s.IsEmpty()) : 0)} of {(worn != null ? worn.itemSlots.Length : 0)} slots full, back model six-slot {backFilter != null}, {onBack} of 6 items shown on the back, slot 4's icon six-slot {local.player.backpackSlot.prefab.UIData.icon == SixSlotBackpack.icon}");
        yield return BackPicture(local, "six-slot-backpack-back");
        // Off the back again: dropped the game's way and taken away, with what it holds.
        local.refs.items.lastEquippedSlotTime = 0f;
        local.refs.items.EquipSlot(Optionable<byte>.Some(3));
        yield return new WaitForSeconds(0.7f);
        Item inHand = local.data.currentItem;
        HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
        if (inHand is Backpack)
        {
            Step("drop the six-slot backpack", () =>
            {
                local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 0f, (byte)3, inHand.transform.position + Vector3.up * 0.25f, Vector3.zero, inHand.transform.rotation, local.player.GetItemSlot(3).data, false);
                local.refs.items.EquipSlot(Optionable<byte>.None);
            });
            yield return new WaitForSeconds(1f);
        }
        Backpack dropped = Item.ALL_ITEMS.OfType<Backpack>().FirstOrDefault(i => i != null && !before.Contains(i));
        int keptOnGround = dropped != null ? dropped.GetData<BackpackData>(DataEntryKey.BackpackData).itemSlots.Count(s => !s.IsEmpty()) : 0;
        Check("dropped again, the six-slot backpack still holds all six", dropped != null && keptOnGround == 6 && local.player.backpackSlot.IsEmpty(),
            dropped == null ? "nothing dropped" : $"{keptOnGround} items, slot 4 empty {local.player.backpackSlot.IsEmpty()}");
        if (dropped != null)
        {
            PhotonNetwork.Destroy(dropped.gameObject);
        }
        // As in multiplayer: the game's four slots and the game's model.
        SixSlotBackpack.AsMultiplayer = true;
        try
        {
            BackpackData plain = new BackpackData();
            plain.Init();
            Backpack other = SpawnInFront(template, 1.4f)?.GetComponent<Backpack>();
            yield return new WaitForSeconds(1.5f);
            MeshFilter otherGround = other != null && other.groundMesh != null ? other.groundMesh.GetComponent<MeshFilter>() : null;
            MeshFilter otherWorn = other != null && other.heldMesh != null ? other.heldMesh.GetComponent<MeshFilter>() : null;
            int otherSlots = other != null ? other.GetData<BackpackData>(DataEntryKey.BackpackData).itemSlots.Length : -1;
            Check("as in multiplayer, a backpack has the game's four slots and the game's model",
                plain.itemSlots.Length == 4 && otherSlots == 4 && otherGround != null && otherGround.sharedMesh != SixSlotBackpack.ground && otherWorn != null && otherWorn.sharedMesh != SixSlotBackpack.worn,
                $"new contents {plain.itemSlots.Length} slots, a new backpack {otherSlots} slots, ground mesh '{(otherGround != null && otherGround.sharedMesh != null ? otherGround.sharedMesh.name : "none")}', worn mesh '{(otherWorn != null && otherWorn.sharedMesh != null ? otherWorn.sharedMesh.name : "none")}'");
            // A six-slot wheel was open before: this four-slot one shows four slots only, not the two spare slices.
            if (other != null)
            {
                Step("a four-slot backpack's wheel after a six-slot one", () =>
                {
                    other.Interact(local);
                    BackpackWheel wheel = GUIManager.instance.backpackWheel;
                    int shown = wheel.slices.Skip(1).Count(s => s != null && s.gameObject.activeSelf);
                    Check("after a six-slot backpack's wheel, a four-slot backpack's wheel shows four slots (no spare slices)",
                        shown == 4 && wheel.slices.Length >= 7, $"{shown} slot slices shown of {wheel.slices.Length - 1}");
                    Screenshot("four-slot-wheel-after-six");
                });
                yield return null;
                GUIManager.instance.CloseBackpackWheel();
                PhotonNetwork.Destroy(other.gameObject);
            }
        }
        finally
        {
            SixSlotBackpack.AsMultiplayer = false;
        }
        // Switched off in the config (in memory): single-player gets the game's four-slot Backpack and its icon.
        ConfigEntry<bool> sixSlots = UltimateAscent.Plugin.SixSlotBackpack;
        bool sixSaved = sixSlots.Value;
        try
        {
            sixSlots.Value = false;
            yield return null;
            BackpackData plain = new BackpackData();
            plain.Init();
            Backpack other = SpawnInFront(template, 1.4f)?.GetComponent<Backpack>();
            yield return new WaitForSeconds(1.5f);
            MeshFilter otherGround = other != null && other.groundMesh != null ? other.groundMesh.GetComponent<MeshFilter>() : null;
            int otherSlots = other != null ? other.GetData<BackpackData>(DataEntryKey.BackpackData).itemSlots.Length : -1;
            Texture gameIcon = template.UIData.icon;
            Check("with \"Six-Slot Backpack\" off, single-player has the game's four-slot Backpack, its model and its icon",
                !SixSlotBackpack.On && plain.itemSlots.Length == 4 && otherSlots == 4 && otherGround != null && otherGround.sharedMesh != SixSlotBackpack.ground && gameIcon != null && gameIcon != SixSlotBackpack.icon,
                $"on {SixSlotBackpack.On}, new contents {plain.itemSlots.Length} slots, a new backpack {otherSlots} slots, ground mesh '{(otherGround != null && otherGround.sharedMesh != null ? otherGround.sharedMesh.name : "none")}', icon '{(gameIcon != null ? gameIcon.name : "none")}'");
            if (other != null)
            {
                PhotonNetwork.Destroy(other.gameObject);
            }
        }
        finally
        {
            sixSlots.Value = sixSaved;
        }
        yield return null;
        Check("switched back on, the Backpack has the six-slot icon again", template.UIData.icon == SixSlotBackpack.icon,
            $"icon '{(template.UIData.icon != null ? template.UIData.icon.name : "none")}'");
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>A picture of a backpack on the ground from in front of its flap (turned by <paramref name="turn"/> degrees), a little above.</summary>
    private IEnumerator FrontPicture(Backpack backpack, float turn, string name)
    {
        MeshFilter mesh = backpack.groundMesh != null ? backpack.groundMesh.GetComponent<MeshFilter>() : null;
        Renderer look = mesh != null ? mesh.GetComponent<Renderer>() : null;
        Vector3 centre = look != null ? look.bounds.center : backpack.transform.position;
        // The backpack meshes' -Y is the front of the bag (its flap), their +Z up.
        Vector3 front = Vector3.ProjectOnPlane((mesh != null ? mesh.transform : backpack.transform).TransformDirection(Vector3.down), Vector3.up);
        front = Quaternion.Euler(0f, turn, 0f) * (front.sqrMagnitude > 0.001f ? front.normalized : Vector3.forward);
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        try
        {
            Camera camera = holder.AddComponent<Camera>();
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.05f;
            holder.transform.position = centre + front * 1.4f + Vector3.up * 0.8f;
            holder.transform.LookAt(centre);
            yield return new WaitForSeconds(0.5f);
            Screenshot(name);
            yield return new WaitForSeconds(0.5f);
        }
        finally
        {
            Destroy(holder);
        }
    }

    private IEnumerator AutoParachuteCheck()
    {
        Item template = AutoParachute.Template;
        Item jetpack = ItemByName("Jetpack");
        if (template == null || jetpack == null)
        {
            Fail("the Auto-Parachute is in the item database", $"parachute {template != null}, Jetpack {jetpack != null}");
            yield break;
        }
        if (Rules.Level < Lvl.First)
        {
            Info("the Auto-Parachute checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Step("Auto-Parachute item", () =>
        {
            LootData.PopulateLootData();
            int mythic = LootData.RarityWeights[Rarity.Mythic];
            int pools = 0;
            int matching = 0;
            bool elsewhere = false;
            foreach (KeyValuePair<SpawnPool, Dictionary<ushort, int>> pool in LootData.AllSpawnWeightData)
            {
                bool hasJetpack = pool.Value.ContainsKey(jetpack.itemID);
                bool hasParachute = pool.Value.TryGetValue(template.itemID, out int weight);
                pools += hasJetpack ? 1 : 0;
                matching += hasJetpack && hasParachute && weight == mythic ? 1 : 0;
                elsewhere |= hasParachute && !hasJetpack;
            }
            Backpack wearable = template as Backpack;
            Check("the Auto-Parachute is worn in slot 4, has 3 charges and spawns where the Jetpack does, as rarely (Mythic, 9)",
                wearable != null && wearable.backpackType == AutoParachute.Type && template.totalUses == AutoParachute.Charges && pools > 0 && matching == pools && !elsewhere && template.IsValidToSpawn(),
                $"slot-4 kind {(wearable != null ? ((int)wearable.backpackType).ToString() : "none")}, charges {template.totalUses}, in {matching} of the Jetpack's {pools} pools at weight {mythic}, elsewhere {elsewhere}, may spawn {template.IsValidToSpawn()}, name '{LocalizedText.GetText("NAME_" + template.UIData.itemName)}'");
        });
        Character local = Local;
        Vector2 look = local.data.lookValues;
        Vector3 home = local.Center;
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        Backpack parachute = SpawnInFront(template, 1.4f)?.GetComponent<Backpack>();
        if (parachute == null)
        {
            Fail("an Auto-Parachute can be put on the ground");
            yield break;
        }
        local.data.lookValues = new Vector2(look.x, -40f);
        yield return new WaitForSeconds(1.5f);
        Screenshot("auto-parachute-ground");
        yield return new WaitForSeconds(0.5f);
        // Its backpack wheel, as a scout opens it on the ground: the slice to put it on shows the parachute
        // (read as it opens; the wheel closes again as soon as the interact key is not held).
        Step("open the Auto-Parachute's wheel", () =>
        {
            parachute.Interact(local);
            BackpackWheel wheel = GUIManager.instance.backpackWheel;
            Texture shown = wheel != null && wheel.slices.Length > 0 && wheel.slices[0].image != null ? wheel.slices[0].image.texture : null;
            Check("its backpack wheel offers to put it on", wheel != null && wheel.gameObject.activeSelf && shown == template.UIData.icon,
                $"wheel open {wheel != null && wheel.gameObject.activeSelf}, the put-on slice shows '{(shown != null ? shown.name : "nothing")}'");
            // Pointed at, the slice says what it puts on (the game showed "loc: name_5" for this fifth kind).
            if (wheel != null && wheel.slices.Length > 0)
            {
                wheel.slices[0].Hover();
            }
            string said = wheel != null && wheel.chosenItemText != null ? wheel.chosenItemText.text : "";
            string expectedWear = LocalizedText.GetText("WEAR").Replace("#", LocalizedText.GetText("NAME_" + template.UIData.itemName));
            Check("pointed at, the slice says it puts on the Auto-Parachute", said == expectedWear && said.IndexOf("loc", StringComparison.OrdinalIgnoreCase) < 0,
                $"'{Plain(said)}' (expected '{Plain(expectedWear)}')");
            GUIManager.instance.CloseBackpackWheel();
        });
        yield return new WaitForSeconds(0.3f);
        local.data.lookValues = look;
        local.refs.items.lastEquippedSlotTime = 0f;
        parachute.Wear(local);
        for (float until = Time.realtimeSinceStartup + 4f; Time.realtimeSinceStartup < until && local.player.backpackSlot.IsEmpty();)
        {
            yield return null;
        }
        yield return new WaitForSeconds(0.3f);
        BackpackSlot slot = local.player.backpackSlot;
        Check("put on, the Auto-Parachute sits in slot 4 with 3 charges",
            slot.backpackType == AutoParachute.Type && AutoParachute.ChargesLeft(slot.data) == AutoParachute.Charges && local.data.hasParachute,
            $"slot 4: kind {(int)slot.backpackType} ({slot.GetPrefabName()}), charges {AutoParachute.ChargesLeft(slot.data)}, the game sees a parachute {local.data.hasParachute}");
        if (slot.backpackType != AutoParachute.Type)
        {
            yield break;
        }
        yield return BackPicture(local, "auto-parachute-back");

        BepInEx.Configuration.ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool saved = rule.Value;
        try
        {
            rule.Value = false;
            yield return ParachuteFall("Ascent 15 off: one charge a save", AutoParachute.Charges - 1, true);
            yield return DropAndWearAgain(AutoParachute.Charges - 1);
            rule.Value = true;
            yield return ParachuteFall("ascent 15: two charges a save", 0, false);
        }
        finally
        {
            rule.Value = saved;
        }
        Check("with no charges left the Auto-Parachute is gone from slot 4", local.player.backpackSlot.IsEmpty() && !local.data.hasParachute,
            $"slot 4: kind {(int)local.player.backpackSlot.backpackType}, the game sees a parachute {local.data.hasParachute}");
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home, false);
        yield return new WaitForSeconds(1f);
        local.data.lookValues = look;
    }

    /// <summary>
    /// The worn parachute taken into the hand (slot 4 selected) and dropped the way the game drops it (its
    /// drop with the slot's data): on the ground it still has its charges, and it is put on again.
    /// </summary>
    private IEnumerator DropAndWearAgain(int charges)
    {
        Character local = Local;
        local.refs.items.lastEquippedSlotTime = 0f;
        local.refs.items.EquipSlot(Optionable<byte>.Some(3));
        yield return new WaitForSeconds(0.7f);
        Item inHand = local.data.currentItem;
        if (inHand == null || !AutoParachute.IsParachute(inHand))
        {
            Fail("the worn Auto-Parachute comes into the hand", $"in the hand: {(inHand != null ? inHand.gameObject.name : "nothing")}");
            yield break;
        }
        HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
        Step("drop the Auto-Parachute", () =>
        {
            local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 0f, (byte)3, inHand.transform.position + Vector3.up * 0.25f, Vector3.zero, inHand.transform.rotation, local.player.GetItemSlot(3).data, false);
            local.refs.items.EquipSlot(Optionable<byte>.None);
        });
        yield return new WaitForSeconds(1f);
        Item dropped = Item.ALL_ITEMS.FirstOrDefault(i => i != null && !before.Contains(i) && AutoParachute.IsParachute(i));
        Check("dropped, the Auto-Parachute keeps its charges", dropped != null && UsesLeft(dropped) == charges && local.player.backpackSlot.IsEmpty(),
            dropped == null ? "nothing dropped" : $"{UsesLeft(dropped)} charges on the ground (expected {charges}), slot 4 empty {local.player.backpackSlot.IsEmpty()}");
        if (dropped is Backpack again)
        {
            local.refs.items.lastEquippedSlotTime = 0f;
            again.Wear(local);
            for (float until = Time.realtimeSinceStartup + 4f; Time.realtimeSinceStartup < until && local.player.backpackSlot.IsEmpty();)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
        }
    }

    /// <summary>One fall from 30 m with the parachute on: it opens by itself and the landing hurts nothing.</summary>
    private IEnumerator ParachuteFall(string how, int chargesAfter, bool picture)
    {
        Character local = Local;
        Protect(false);
        Meter injury = new Meter(Status.Injury);
        Meter drowsy = new Meter(Status.Drowsy);
        bool opened = false;
        float openedAt = 0f;
        float landedAt = 0f;
        float folded = -1f;
        try
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, local.Center + Vector3.up * 30f, false);
            yield return new WaitForSeconds(0.5f);
            float start = Time.time;
            bool airborne = false;
            while (Time.time - start < 30f)
            {
                airborne |= !local.data.isGrounded;
                if (!opened && local.refs.balloons._isParachuteOpen)
                {
                    opened = true;
                    openedAt = Time.time - start;
                }
                if (opened && picture && Time.time - start > openedAt + 1f)
                {
                    picture = false;
                    Vector2 look = local.data.lookValues;
                    local.data.lookValues = new Vector2(look.x, 70f);
                    yield return new WaitForSeconds(0.3f);
                    Screenshot("auto-parachute-open");
                    yield return new WaitForSeconds(0.3f);
                    local.data.lookValues = look;
                }
                if (airborne && local.data.isGrounded && Time.time - start > 1f)
                {
                    landedAt = Time.time - start;
                    break;
                }
                yield return null;
            }
            // The canopy folds away once the scout lands (the game: 2 s).
            GameObject canopy = local.refs.parachute != null ? local.refs.parachute.gameObject : null;
            float landedTime = Time.time;
            for (float until = Time.time + 3f; Time.time < until && canopy != null && canopy.activeSelf;)
            {
                yield return null;
            }
            folded = canopy != null && !canopy.activeSelf ? Time.time - landedTime : -1f;
            yield return new WaitForSeconds(0.5f);
        }
        finally
        {
            injury.Stop();
            drowsy.Stop();
        }
        BackpackSlot slot = local.player.backpackSlot;
        int left = slot.backpackType == AutoParachute.Type ? AutoParachute.ChargesLeft(slot.data) : 0;
        Check($"the worn Auto-Parachute opens by itself and the fall hurts nothing ({how})", opened && injury.Total <= 0f && drowsy.Total <= 0f && left == chargesAfter,
            $"opened {opened} after {openedAt:0.0} s, landed after {landedAt:0.0} s, {injury.Total * 100f:0.#} injury, {drowsy.Total * 100f:0.#} drowsiness, {left} charges left (expected {chargesAfter})");
        float foldTarget = AutoParachute.GameFoldSeconds / AutoParachute.FoldSpeed;
        Check($"after the landing the canopy folds away 3 times as fast as in the game ({how})", folded > 0f && folded <= foldTarget + 0.25f,
            folded < 0f ? "still out 3 s after the landing" : $"gone {folded:0.00} s after the landing (target {foldTarget:0.00} s; the game: {AutoParachute.GameFoldSeconds:0} s)");
        Protect(true);
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>The scout seen from behind (another camera for a moment), its own back model shown for the picture.</summary>
    private IEnumerator BackPicture(Character local, string name)
    {
        AutoParachute.ShowOwnBackModel = true;
        ShowOwnBackpack.On = true;
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        try
        {
            Camera camera = holder.AddComponent<Camera>();
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.05f;
            Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
            Vector3 chest = local.Center + Vector3.up * 0.4f;
            holder.transform.position = chest - forward * 2.2f + Vector3.up * 0.6f;
            holder.transform.LookAt(chest);
            yield return new WaitForSeconds(0.5f);
            Screenshot(name);
            yield return new WaitForSeconds(1f);
        }
        finally
        {
            AutoParachute.ShowOwnBackModel = false;
            ShowOwnBackpack.On = false;
            Destroy(holder);
        }
    }

    /// <summary>
    /// The Vanilla Wafers (9) take half of the Scout Cookies' spawns: a suitcase picks 200 times, the
    /// game's way, from a pool that holds only the Scout Cookies. They are the Scout Cookies in another look
    /// (the same name, food, extra stamina, bites and cooking): both are put down side by side.
    /// </summary>
    private IEnumerator VanillaCookies()
    {
        Item cookies = ItemByName(VanillaWafers.CookiesPrefab);
        Item vanilla = ItemByName(VanillaWafers.VanillaPrefab);
        if (cookies == null || vanilla == null)
        {
            Fail("the Scout Cookies and the Vanilla Wafers are in the item database");
            yield break;
        }
        if (Rules.Level < Lvl.First)
        {
            Info("the Vanilla Wafers checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Luggage luggage = Luggage.ALL_LUGGAGE
            .Where(l => l != null && !(l is RespawnChest) && !l.CompareTag("ClownLuggage") && l.gameObject.activeInHierarchy)
            .OrderBy(l => Vector3.Distance(l.transform.position, local.Center))
            .FirstOrDefault();
        if (luggage == null)
        {
            Info("no suitcase found for the Scout Cookies' picks");
        }
        else
        {
            Step("Scout Cookies picks", () =>
            {
                SpawnPool pool = luggage.GetSpawnPool();
                LootData.PopulateLootData();
                // The Lantern's spawn places are brought up to date before a pick (MoreLanterns), which may rebuild every
                // pool: done first, then held while the picks run, so the pools below stay as set.
                MoreLanterns.Sync();
                HoldLootTables.Hold = true;
                LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> own);
                LootData.AllSpawnWeightData[pool] = new Dictionary<ushort, int> { [cookies.itemID] = LootData.RarityWeights[cookies.GetComponent<LootData>().Rarity] };
                int plain = 0;
                int other = 0;
                try
                {
                    for (int i = 0; i < 50; i++)
                    {
                        foreach (GameObject pick in luggage.GetObjectsToSpawn(4, true))
                        {
                            if (pick != null && pick.name == VanillaWafers.VanillaPrefab)
                            {
                                other++;
                            }
                            else if (pick != null && pick.name == VanillaWafers.CookiesPrefab)
                            {
                                plain++;
                            }
                        }
                    }
                }
                finally
                {
                    if (own != null)
                    {
                        LootData.AllSpawnWeightData[pool] = own;
                    }
                    else
                    {
                        LootData.AllSpawnWeightData.Remove(pool);
                    }
                }
                Check("half of the Scout Cookies' spawns are the Vanilla Wafers (9)", other >= 70 && plain >= 70 && other + plain == 200,
                    $"{other} vanilla and {plain} plain of 200 picks from {luggage.gameObject.name}'s pool ({pool})");
            });
        }
        Step("Vanilla Wafers item", () =>
        {
            float Hunger(Item item) => item.GetComponents<Action_RestoreHunger>().Sum(a => a.restorationAmount);
            float Stamina(Item item) => item.GetComponents<Action_GiveExtraStamina>().Sum(a => a.amount);
            string Skin(Item item) => item.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).Where(m => m != null && !m.name.StartsWith("M_Player")).Select(m => m.name).FirstOrDefault() ?? "none";
            Check("the Vanilla Wafers: the Scout Cookies in another look, under their own name, 5 hunger and 20 extra stamina a bite (the Scout Cookies: 10 and 10), 4 bites, cooking",
                Plain(LocalizedText.GetText("NAME_" + vanilla.UIData.itemName)) == "VANILLA WAFERS" && Mathf.Abs(Hunger(vanilla) - VanillaWafers.Hunger) < 0.0001f
                && Mathf.Abs(Stamina(vanilla) - VanillaWafers.ExtraStamina) < 0.0001f && vanilla.totalUses == cookies.totalUses && vanilla.GetComponent<ItemCooking>() != null && Skin(vanilla) != Skin(cookies),
                $"names '{LocalizedText.GetText("NAME_" + vanilla.UIData.itemName)}' / '{LocalizedText.GetText("NAME_" + cookies.UIData.itemName)}', hunger {Hunger(vanilla) * 100f:0.#} / {Hunger(cookies) * 100f:0.#}, extra stamina {Stamina(vanilla) * 100f:0.#} / {Stamina(cookies) * 100f:0.#}, bites {vanilla.totalUses} / {cookies.totalUses}, cooks {vanilla.GetComponent<ItemCooking>() != null}, looks '{Skin(vanilla)}' / '{Skin(cookies)}'");
        });
        yield return SideBySide(new[] { cookies, vanilla }, "scout-cookies-and-vanilla-wafers");
        // Cooked the game's way: twice the food, 1.5 times the extra stamina.
        GameObject wafers = SpawnInFront(vanilla, 1.4f);
        Item cookedWafers = wafers != null ? wafers.GetComponent<Item>() : null;
        if (cookedWafers != null)
        {
            yield return new WaitForSeconds(0.5f);
            cookedWafers.cooking.FinishCooking();
            yield return new WaitForSeconds(0.4f);
            float hunger = cookedWafers.GetComponents<Action_RestoreHunger>().Sum(a => a.restorationAmount);
            float stamina = cookedWafers.GetComponents<Action_GiveExtraStamina>().Sum(a => a.amount);
            Check("cooked, a bite of the Vanilla Wafers is 10 hunger and 30 extra stamina", Mathf.Abs(hunger - 0.1f) < 0.0001f && Mathf.Abs(stamina - 0.3f) < 0.0001f,
                $"{hunger * 100f:0.#} hunger, {stamina * 100f:0.#} extra stamina");
            PhotonNetwork.Destroy(wafers);
        }
    }

    /// <summary>
    /// The wild foods (9), picked the game's own way as many times as it takes to see their share: a berry
    /// bush's kind (0.2% Wonderberries), a thorny vine's berry (the Green Clusterberry as often as each other
    /// Clusterberry: 25%) and a mushroom patch's pick from its own list (the Weird Shroom as often as the safe
    /// Cluster Shroom). With the config's New Items off (in memory) none is picked, in a custom run too; on, a
    /// custom run picks them. The Wonderberry looks like the game's own; the three are put down side by side.
    /// Then eaten, with ascent 15's rule off and on (in memory): the Wonderberry (20 hunger, 10 s of infinite
    /// stamina, no crash; from 15: 10 and 5 s), the Green Clusterberry (10 hunger, removes 30 poison; from 15:
    /// 5 and 15), and the Weird Shroom (the scout glows, with its light).
    /// </summary>
    private IEnumerator WildFoodsCheck()
    {
        Item berry = ItemByName(WildFoods.WonderberryPrefab);
        Item cluster = ItemByName(WildFoods.GreenClusterberryPrefab);
        Item shroom = ItemByName(WildFoods.WeirdShroomPrefab);
        if (berry == null || cluster == null || shroom == null)
        {
            Fail("the wild foods are in the item database", $"Wonderberry {berry != null}, Green Clusterberry {cluster != null}, Weird Shroom {shroom != null}");
            yield break;
        }
        if (Rules.Level < Lvl.First)
        {
            Info("the wild food checks are skipped", $"testing ascent {target}");
            yield break;
        }
        int Picks(Func<GameObject> pick, Item item, int times)
        {
            int found = 0;
            for (int i = 0; i < times; i++)
            {
                GameObject picked = pick();
                if (picked != null && picked.name == item.gameObject.name)
                {
                    found++;
                }
            }
            return found;
        }
        SpawnList patch = FindObjectsByType<GroundPlaceSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(s => s.spawns).FirstOrDefault(l => l != null && WildFoods.WeirdShroomShare(l) > 0f);
        Step("wild food picks", () =>
        {
            LootData.PopulateLootData();
            const int BushPicks = 50000;
            int beach = Picks(() => LootData.GetRandomItem(SpawnPool.BerryBushBeach), berry, BushPicks);
            int jungle = Picks(() => LootData.GetRandomItem(SpawnPool.BerryBushJungle), berry, BushPicks);
            Check("0.2% of the Crispberry bushes grow Wonderberries (9)", beach >= 70 && beach <= 130 && jungle >= 70 && jungle <= 130,
                $"{beach} of {BushPicks} beach bushes, {jungle} of {BushPicks} Tropics bushes (about 100 expected)");
            const int VinePicks = 4000;
            int vines = Picks(() => LootData.GetRandomItem(SpawnPool.SpikyVine), cluster, VinePicks);
            int others = LootData.AllSpawnWeightData[SpawnPool.SpikyVine].Count;
            float vineShare = 1f / (others + 1);
            Check("thorny vines grow Green Clusterberries as often as each other Clusterberry (9)", Mathf.Abs(vines / (float)VinePicks - vineShare) < 0.03f,
                $"{vines} of {VinePicks} vines ({vines * 100f / VinePicks:0.#}%; {vineShare * 100f:0.#}% expected with {others} other Clusterberries)");
            if (patch == null)
            {
                Info("no mushroom patch on this map; the Weird Shroom's picks are not checked");
            }
            else
            {
                const int PatchPicks = 8000;
                int weird = 0;
                int safe = 0;
                for (int i = 0; i < PatchPicks; i++)
                {
                    GameObject picked = patch.GetSpawns(1)[0];
                    if (picked == null)
                    {
                        continue;
                    }
                    if (picked.name == shroom.gameObject.name)
                    {
                        weird++;
                    }
                    else if (picked.name == WildFoods.ClusterShroomPrefab)
                    {
                        safe++;
                    }
                }
                float share = WildFoods.WeirdShroomShare(patch);
                Check("mushroom patches grow Weird Shrooms as often as the safe Cluster Shroom (9)",
                    Mathf.Abs(weird / (float)PatchPicks - share) < 0.02f && Mathf.Abs(weird - safe) < PatchPicks * 0.03f,
                    $"'{patch.name}': {weird} Weird Shrooms and {safe} Cluster Shrooms in {PatchPicks} picks ({share * 100f:0.#}% each expected)");
            }

            ConfigEntry<bool> newItems = UltimateAscent.Plugin.NewItems;
            bool savedSwitch = newItems.Value;
            bool wasCustom = RunSettings.IsCustomRun;
            try
            {
                int Grown() => Picks(() => LootData.GetRandomItem(SpawnPool.BerryBushBeach), berry, 20000)
                    + Picks(() => LootData.GetRandomItem(SpawnPool.SpikyVine), cluster, 2000)
                    + (patch != null ? Picks(() => patch.GetSpawns(1)[0], shroom, 2000) : 0);
                newItems.Value = false;
                HostOptions.Invalidate();
                int offRegular = Grown();
                RunSettings.IsCustomRun = true;
                int offCustom = Grown();
                newItems.Value = true;
                HostOptions.Invalidate();
                int onCustom = Grown();
                Check("with New Items off none of them grows, in a custom run too; on, a custom run grows them (it is their only switch there)",
                    offRegular == 0 && offCustom == 0 && onCustom > 0, $"off: {offRegular} in this run, {offCustom} in a custom run; on, custom run: {onCustom}");
            }
            finally
            {
                newItems.Value = savedSwitch;
                RunSettings.IsCustomRun = wasCustom;
                HostOptions.Invalidate();
            }
            Material skin = berry.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.name.Contains("Rainbow"));
            Check("the Wonderberry has the game's own look and icon", skin != null && skin.name == "M_RainbowBerry" && berry.UIData.icon != null && berry.UIData.icon.name == WildFoods.WonderberryPrefab,
                $"skin '{(skin != null ? skin.name : "none")}', icon '{(berry.UIData.icon != null ? berry.UIData.icon.name : "none")}'");
        });
        yield return SideBySide(new[] { berry, cluster, shroom }, "wild-foods");

        Character local = Local;
        ConfigEntry<bool> worseRule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool worseSaved = worseRule.Value;
        // The rising fog poisons at 18 (5.5 a second at 24): kept away while the foods' cures are measured.
        Action calm = CalmForMeasuring();
        Action nausea = NoNausea();
        Protect(false);
        try
        {
            foreach (bool worse in Rules.Level >= Lvl.WorseItems ? new[] { false, true } : new[] { false })
            {
                worseRule.Value = worse;
                HostOptions.Invalidate();
                string level = worse ? "14" : "below 15 (ascent 15's rule off)";
                yield return SpawnIntoHand(berry);
                if (held != null)
                {
                    Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.InfiniteStamina);
                    Afflictions.SetStatus(Status.Hunger, 0.5f);
                    yield return EatHeld();
                    Peak.Afflictions.Affliction_InfiniteStamina rush = Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_InfiniteStamina>().FirstOrDefault();
                    float food = 0.5f - Afflictions.GetCurrentStatus(Status.Hunger);
                    float expectedFood = worse ? 0.1f : 0.2f;
                    float expectedRush = worse ? WildFoods.WonderberryRushWorse : WildFoods.WonderberryRush;
                    Check($"a Wonderberry eaten restores {expectedFood * 100f:0} hunger and gives {expectedRush:0} s of infinite stamina with no crash ({level})",
                        Mathf.Abs(food - expectedFood) < 0.006f && rush != null && Mathf.Abs(rush.totalTime - expectedRush) < 0.01f && rush.drowsyAffliction == null && local.infiniteStam,
                        $"{food * 100f:0.#} hunger restored, infinite stamina {(rush != null ? rush.totalTime.ToString("0.#") + " s" : "none")}, crash afterwards {(rush != null && rush.drowsyAffliction != null)}, running {local.infiniteStam}");
                    Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.InfiniteStamina);
                    Afflictions.SetStatus(Status.Hunger, 0f);
                }
                yield return SpawnIntoHand(cluster);
                if (held != null)
                {
                    // Room left on the bar (weight and the weather's cold come on top): a full bar would pass the scout out.
                    Afflictions.SetStatus(Status.Hunger, 0.3f);
                    Afflictions.SetStatus(Status.Poison, 0.4f);
                    string beforeEating = StatusesAndEffects();
                    yield return EatHeld();
                    float food = 0.3f - Afflictions.GetCurrentStatus(Status.Hunger);
                    float cured = 0.4f - Afflictions.GetCurrentStatus(Status.Poison);
                    Info($"the Green Clusterberry ({level})", $"its actions [{string.Join(", ", cluster.GetComponents<ItemActionBase>().Select(Describe))}]; before eating: {beforeEating}; after: {StatusesAndEffects()}");
                    float expectedFood = worse ? 0.05f : 0.1f;
                    float expectedCure = worse ? WildFoods.GreenClusterberryCureWorse : 0.3f;
                    Check($"a Green Clusterberry eaten restores {expectedFood * 100f:0} hunger and removes {expectedCure * 100f:0} poison ({level})",
                        Mathf.Abs(food - expectedFood) < 0.006f && Mathf.Abs(cured - expectedCure) < 0.006f,
                        $"{food * 100f:0.#} hunger restored, {cured * 100f:0.#} poison removed");
                    Afflictions.SetStatus(Status.Hunger, 0f);
                    Afflictions.SetStatus(Status.Poison, 0f);
                }
            }
            worseRule.Value = worseSaved;
            HostOptions.Invalidate();
            yield return SpawnIntoHand(shroom);
            if (held != null)
            {
                yield return EatHeld();
                yield return new WaitForSeconds(1f);
                Peak.Afflictions.Affliction_Glowing glow = Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_Glowing>().FirstOrDefault();
                Check("a Weird Shroom eaten makes the scout glow, with its light", glow != null && glow.pointLightInstance != null,
                    glow == null ? "no glow on the scout" : $"glow for {glow.totalTime:0} s, light {(glow.pointLightInstance != null ? glow.pointLightInstance.name : "missing")}");
                Screenshot("weird-shroom-glow");
                yield return new WaitForSeconds(1f);
            }
        }
        finally
        {
            worseRule.Value = worseSaved;
            HostOptions.Invalidate();
            Protect(true);
            calm();
            nausea();
        }
    }

    /// <summary>
    /// Stones and Sticks (9, 17). On the beach the host laid out 20 of each near the game's mushroom and berry
    /// spots, on the ground, out of the water, 4 m apart; the nearest of each is pictured where it lies. A
    /// Stone dropped on the scout from 7 m (the game's own fall and bonk) knocks them down for 2 s, injures
    /// them for 10 (20 with ascent 17's rule on; both switched in memory) and shatters with the rock smash and
    /// the gray dust; one thrown hard at the ground shatters, one dropped gently stays whole. A Stick is held
    /// in both hands; its use is "swing"; a swing costs 33% stamina, raises it over the head and chops
    /// (pictured from the side) and knocks a zombie in front of the scout down for 4 s without hurting it;
    /// out of stamina it does not swing. With the config's New Items off they are not laid out, in a custom
    /// run too.
    /// </summary>
    private IEnumerator StonesAndSticks()
    {
        Item stone = ItemByName(StoneItem.PrefabName);
        Item stick = ItemByName(StickItem.PrefabName);
        if (stone == null || stick == null)
        {
            Fail("the Stone and the Stick are in the item database", $"Stone {stone != null}, Stick {stick != null}");
            yield break;
        }
        if (Rules.Level < Lvl.First)
        {
            Info("the Stone and Stick checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        MapHandler map = Singleton<MapHandler>.Instance;
        string beach = map.segments[0].segmentParent.name;
        Info("the Stone and the Stick", $"names '{Plain(stone.GetItemName())}' / '{Plain(stick.GetItemName())}', icons '{(stone.UIData.icon != null ? stone.UIData.icon.name : "none")}' / '{(stick.UIData.icon != null ? stick.UIData.icon.name : "none")}'");
        List<Item> stones = new List<Item>();
        List<Item> sticks = new List<Item>();
        Step("Stones and Sticks on the beach", () =>
        {
            GroundItems.Placed.TryGetValue(beach, out List<Item> placed);
            placed = (placed ?? new List<Item>()).Where(i => i != null).ToList();
            stones.AddRange(placed.Where(StoneItem.IsStone));
            sticks.AddRange(placed.Where(StickItem.IsStick));
            Check("20 Stones and 20 Sticks lie on the beach (9)", stones.Count == GroundItems.PerBiome && sticks.Count == GroundItems.PerBiome,
                $"{stones.Count} Stones and {sticks.Count} Sticks in '{beach}'");
            List<Item> all = stones.Concat(sticks).ToList();
            int onGround = 0;
            int dry = 0;
            float closest = float.MaxValue;
            foreach (Item item in all)
            {
                Vector3 at = item.transform.position;
                if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 2f, HelperFunctions.terrainMapMask) && ground.distance - 0.5f < 0.45f)
                {
                    onGround++;
                }
                if (!GroundItems.InWater(at))
                {
                    dry++;
                }
                foreach (Item other in all)
                {
                    if (other != item)
                    {
                        closest = Mathf.Min(closest, Vector3.Distance(at, other.transform.position));
                    }
                }
            }
            Check("each lies on the ground, out of the water, apart from the others", all.Count > 0 && onGround == all.Count && dry == all.Count && closest >= GroundItems.Spacing - 0.6f,
                $"{onGround} of {all.Count} on the ground, {dry} out of the water, the closest two {closest:0.0} m apart (laid out {GroundItems.Spacing:0} m apart; sticks settle)");
            if (all.Count > 0)
            {
                Vector3 min = all.Aggregate(all[0].transform.position, (m, i) => Vector3.Min(m, i.transform.position));
                Vector3 max = all.Aggregate(all[0].transform.position, (m, i) => Vector3.Max(m, i.transform.position));
                Info("where they lie", $"over {max.x - min.x:0} m x {max.z - min.z:0} m, {min.y:0}-{max.y:0} m high; the nearest Stone {stones.Select(i => Vector3.Distance(i.transform.position, local.Center)).DefaultIfEmpty(-1f).Min():0} m and Stick {sticks.Select(i => Vector3.Distance(i.transform.position, local.Center)).DefaultIfEmpty(-1f).Min():0} m from the scout");
            }
        });
        foreach ((List<Item> lying, string what) in new[] { (stones, "stone"), (sticks, "stick") })
        {
            Item nearest = lying.Where(i => i != null).OrderBy(i => Vector3.Distance(i.transform.position, local.Center)).FirstOrDefault();
            if (nearest != null)
            {
                yield return CloseUp(nearest.transform.position, "a-" + what + "-lying-on-the-beach");
            }
        }
        yield return SideBySide(new[] { stone, stick }, "stone-and-stick");

        yield return StoneOnTheScout(stone);
        yield return StoneThrows(stone);
        yield return StoneAtZombie(stone);
        yield return SmashLoudness();
        yield return StickSwing(stick);

        Step("Stones and Sticks follow New Items", () =>
        {
            ConfigEntry<bool> newItems = UltimateAscent.Plugin.NewItems;
            bool savedSwitch = newItems.Value;
            bool wasCustom = RunSettings.IsCustomRun;
            try
            {
                newItems.Value = false;
                HostOptions.Invalidate();
                int offRegular = GroundItems.Spawn(map.segments[0].segmentParent.transform).Count;
                RunSettings.IsCustomRun = true;
                int offCustom = GroundItems.Spawn(map.segments[0].segmentParent.transform).Count;
                bool onCustom = NewItems.WildOn;
                newItems.Value = true;
                HostOptions.Invalidate();
                onCustom = NewItems.WildOn;
                Check("with New Items off none are laid out, in a custom run too; on, a custom run has them", offRegular == 0 && offCustom == 0 && onCustom,
                    $"off: {offRegular} laid out in this run, {offCustom} in a custom run; on, custom run: {(onCustom ? "laid out" : "none")}");
            }
            finally
            {
                newItems.Value = savedSwitch;
                RunSettings.IsCustomRun = wasCustom;
                HostOptions.Invalidate();
            }
        });
    }

    /// <summary>
    /// The second part of the map (the Tropics or the Roots, reached the game's way): the host laid out 20
    /// Stones and 20 Sticks there too when the game filled it, on the ground, out of the water, apart.
    /// </summary>
    private IEnumerator StonesAndSticksSecond()
    {
        if (Rules.Level < Lvl.First)
        {
            Info("the second part's Stone and Stick checks are skipped", $"testing ascent {target}");
            yield break;
        }
        if (Singleton<MapHandler>.Instance.currentSegment != (int)Segment.Tropics)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(4f);
        }
        string part = Singleton<MapHandler>.Instance.segments[1].segmentParent.name;
        Step("Stones and Sticks in " + part, () =>
        {
            GroundItems.Placed.TryGetValue(part, out List<Item> placed);
            List<Item> all = (placed ?? new List<Item>()).Where(i => i != null).ToList();
            int stones = all.Count(StoneItem.IsStone);
            int sticks = all.Count(StickItem.IsStick);
            int onGround = all.Count(i => Physics.Raycast(i.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 2f, HelperFunctions.terrainMapMask) && ground.distance - 0.5f < 0.45f);
            int dry = all.Count(i => !GroundItems.InWater(i.transform.position));
            float closest = all.SelectMany(a => all.Where(b => b != a).Select(b => Vector3.Distance(a.transform.position, b.transform.position))).DefaultIfEmpty(0f).Min();
            Check($"20 Stones and 20 Sticks lie in the {Map.CurrentPlace} too, on the ground, out of the water, apart (9)",
                stones == GroundItems.PerBiome && sticks == GroundItems.PerBiome && onGround == all.Count && dry == all.Count && closest >= GroundItems.Spacing - 0.6f,
                $"{stones} Stones and {sticks} Sticks in '{part}'; {onGround} on the ground, {dry} out of the water, the closest two {closest:0.0} m apart");
        });
    }

    /// <summary>
    /// A picture of a spot from a camera about 2 m away and 1.2 m up, on a side with a clear view of it (the
    /// scout stays where they are).
    /// </summary>
    private IEnumerator CloseUp(Vector3 point, string picture, float wait = 1.2f)
    {
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        try
        {
            Camera camera = holder.AddComponent<Camera>();
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.05f;
            Vector3 toward = Vector3.ProjectOnPlane(Local.Center - point, Vector3.up);
            toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
            Vector3 best = point + toward * 2f + Vector3.up * 1.2f;
            for (int i = 0; i < 16; i++)
            {
                Vector3 side = Quaternion.AngleAxis(i * 45f, Vector3.up) * toward;
                Vector3 at = point + side * (i < 8 ? 2f : 1.4f) + Vector3.up * (i < 8 ? 1.2f : 2f);
                if (!Physics.Linecast(point + Vector3.up * 0.3f, at, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore)
                    && !Physics.CheckSphere(at, 0.25f, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore))
                {
                    best = at;
                    break;
                }
            }
            holder.transform.position = best;
            holder.transform.LookAt(point);
            yield return new WaitForSeconds(wait);
            Screenshot(picture);
            yield return new WaitForSeconds(1f);
        }
        finally
        {
            Destroy(holder);
        }
    }

    /// <summary>A camera beside the scout (their right), looking at their chest; removed by the caller.</summary>
    private static GameObject SideCamera(Character local, float distance = 2.6f)
    {
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        Camera camera = holder.AddComponent<Camera>();
        camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
        camera.fieldOfView = 50f;
        camera.nearClipPlane = 0.05f;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 chest = local.Center + Vector3.up * 0.3f + forward * 0.5f;
        holder.transform.position = chest + right * distance + Vector3.up * 0.3f;
        holder.transform.LookAt(chest);
        return holder;
    }

    /// <summary>
    /// A Stone dropped from 7 m onto the scout's head (it lands at about 11.7 m/s): the game's own fall and
    /// bonk, with ascent 17's rule on (20 injury) and off (10), in memory.
    /// </summary>
    private IEnumerator StoneOnTheScout(Item stone)
    {
        Character local = Local;
        ConfigEntry<bool> hazards = UltimateAscent.Plugin.AscentSwitches[Lvl.Hazards];
        bool saved = hazards.Value;
        try
        {
            foreach (bool on in Rules.Level >= Lvl.Hazards ? new[] { true, false } : new[] { false })
            {
                hazards.Value = on;
                HostOptions.Invalidate();
                float expected = on ? StoneItem.InjuryHazards : StoneItem.Injury;
                Protect(false);
                Afflictions.SetStatus(Status.Injury, 0f);
                yield return new WaitForSeconds(0.5f);
                int dustBefore = StoneItem.DustClouds;
                float soundBefore = StoneItem.BreakSound() != null ? StoneItem.BreakSound().lastTimePlayed : 0f;
                Vector3 above = local.Center + Vector3.up * 7.5f;
                Item dropped = null;
                Step("drop a Stone from 7 m onto the scout" + (on ? "" : " (ascent 17's rule off)"), () =>
                {
                    dropped = PhotonNetwork.InstantiateItemRoom(stone.gameObject.name, above, Quaternion.identity).GetComponent<Item>();
                });
                float longest = 0f;
                for (float until = Time.time + 4f; Time.time < until;)
                {
                    longest = Mathf.Max(longest, local.data.fallSeconds);
                    if (dropped == null && longest > 0f)
                    {
                        break;
                    }
                    yield return null;
                }
                yield return new WaitForSeconds(0.3f);
                float injury = Afflictions.GetCurrentStatus(Status.Injury);
                Protect(true);
                bool heard = StoneItem.BreakSound() != null && StoneItem.BreakSound().lastTimePlayed > soundBefore;
                Check($"a Stone falling on a scout knocks them down for 2 s and injures them for {expected * 100f:0} ({(on ? "17" : "9; ascent 17's rule off")})",
                    Mathf.Abs(longest - StoneItem.Knockdown) < 0.1f && Mathf.Abs(injury - expected) < 0.006f,
                    $"knocked down for {longest:0.00} s, {injury * 100f:0.#} injury");
                Check("it shatters with the rock smash and a gray dust cloud", dropped == null && StoneItem.DustClouds > dustBefore && heard,
                    $"stone {(dropped == null ? "gone" : "still there")}, dust clouds left {StoneItem.DustClouds - dustBefore}, smash played {heard}");
                if (dropped != null && dropped.photonView != null)
                {
                    PhotonNetwork.Destroy(dropped.gameObject);
                }
                Afflictions.SetStatus(Status.Injury, 0f);
                yield return new WaitForSeconds(2.5f);
            }
        }
        finally
        {
            hazards.Value = saved;
            HostOptions.Invalidate();
            Protect(true);
        }
    }

    /// <summary>A Stone thrown hard at the ground (the game's throw, full charge) shatters; one dropped gently stays whole.</summary>
    private IEnumerator StoneThrows(Item stone)
    {
        Character local = Local;
        Vector2 look = local.data.lookValues;
        try
        {
            foreach ((float charge, string how) in new[] { (1f, "thrown hard at the ground"), (0f, "dropped gently") })
            {
                yield return SpawnIntoHand(stone);
                if (held == null)
                {
                    continue;
                }
                local.data.lookValues = new Vector2(look.x, charge > 0f ? -55f : -20f);
                yield return new WaitForSeconds(0.5f);
                int dustBefore = StoneItem.DustClouds;
                int breaksBefore = StoneItem.Breaks;
                HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
                Step("a Stone " + how, () =>
                {
                    byte slot = local.refs.items.currentSelectedSlot.Value;
                    local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, charge, slot, held.transform.position, Vector3.zero, held.transform.rotation, local.player.GetItemSlot(slot).data, false);
                });
                Item flying = null;
                for (float until = Time.time + 1f; Time.time < until && flying == null;)
                {
                    flying = Item.ALL_ITEMS.FirstOrDefault(i => i != null && !before.Contains(i) && i.itemID == stone.itemID);
                    if (flying == null)
                    {
                        yield return null;
                    }
                }
                float fastest = 0f;
                Vector3 last = held != null ? held.transform.position : local.Center;
                for (float until = Time.time + 2.5f; Time.time < until && flying != null;)
                {
                    if (flying.rig != null)
                    {
                        fastest = Mathf.Max(fastest, flying.rig.linearVelocity.magnitude);
                    }
                    last = flying.transform.position;
                    yield return null;
                }
                if (charge > 0f && flying == null)
                {
                    yield return CloseUp(last, "stone-shatters-dust", 0.25f);
                }
                if (charge > 0f)
                {
                    Check("a Stone thrown hard at the ground shatters (any hard impact), with the gray dust cloud", flying == null && StoneItem.Breaks > breaksBefore && StoneItem.DustClouds > dustBefore,
                        $"stone {(flying == null ? "gone" : "still there")} (flew at up to {fastest:0.#} m/s), breaks {StoneItem.Breaks - breaksBefore}, dust clouds {StoneItem.DustClouds - dustBefore}");
                }
                else
                {
                    Check("a Stone dropped gently stays whole", flying != null && StoneItem.Breaks == breaksBefore, $"stone {(flying != null ? "on the ground" : "gone")}, fell at up to {fastest:0.#} m/s, breaks {StoneItem.Breaks - breaksBefore}");
                    if (flying != null && flying.photonView != null)
                    {
                        PhotonNetwork.Destroy(flying.gameObject);
                    }
                }
                yield return new WaitForSeconds(0.5f);
            }
        }
        finally
        {
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// A Stone thrown hard at a zombie standing in front of the scout (the game's throw, full charge): it knocks the
    /// zombie down and shatters, with the rock smash and the dust, as on the ground.
    /// </summary>
    private IEnumerator StoneAtZombie(Item stone)
    {
        Character local = Local;
        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        GameObject made = null;
        try
        {
            Vector3 spot = local.Center + forward * 2.2f;
            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f, HelperFunctions.terrainMapMask))
            {
                spot = ground.point + Vector3.up * 0.9f;
            }
            made = PhotonNetwork.Instantiate("MushroomZombie", spot, Quaternion.LookRotation(-forward), 0);
            MushroomZombie zombie = made != null ? made.GetComponent<MushroomZombie>() : null;
            if (zombie == null)
            {
                Fail("a zombie to throw a Stone at");
                yield break;
            }
            zombie.distanceBeforeWakeup = 0f;
            yield return new WaitForSeconds(4f);
            yield return SpawnIntoHand(stone);
            if (held == null)
            {
                yield break;
            }
            Character body = zombie.character;
            local.data.lookValues = HelperFunctions.DirectionToLook(body.Center - MainCamera.instance.transform.position);
            yield return new WaitForSeconds(0.6f);
            float soundBefore = StoneItem.BreakSound() != null ? StoneItem.BreakSound().lastTimePlayed : 0f;
            SFX_Instance[] coconutBonks = ItemByName("Item_Coconut")?.GetComponent<Bonkable>()?.bonk ?? new SFX_Instance[0];
            float LastBonk() => coconutBonks.Where(b => b != null).Select(b => b.lastTimePlayed).DefaultIfEmpty(0f).Max();
            float bonkBefore = LastBonk();
            int breaksBefore = StoneItem.Breaks;
            int dustBefore = StoneItem.DustClouds;
            FallRecorder.Clear();
            FallRecorder.Of = body;
            // What the scout hears is recorded (from just before the throw), and the source playing the smash looked at.
            ListenerRecorder recorder = ListenerRecorder.Attach();
            recorder?.Begin();
            Step("throw a Stone hard at a zombie", () =>
            {
                byte slot = local.refs.items.currentSelectedSlot.Value;
                local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 1f, slot, held.transform.position, Vector3.zero, held.transform.rotation, local.player.GetItemSlot(slot).data, false);
            });
            for (float until = Time.time + 2f; Time.time < until && StoneItem.Breaks == breaksBefore;)
            {
                yield return null;
            }
            yield return null;
            yield return null;
            AudioListener listener = FindObjectOfType<AudioListener>();
            List<string> smashSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                .Where(a => a != null && a.clip != null && a.clip.name == "Stone_break")
                .Select(a => $"playing {a.isPlaying} at {a.time:0.00} s, volume {a.volume:0.###}, mute {a.mute}, enabled {a.enabled && a.gameObject.activeInHierarchy}, "
                    + $"{(listener != null ? Vector3.Distance(a.transform.position, listener.transform.position) : -1f):0.0} m from the listener, range {a.minDistance:0.#}-{a.maxDistance:0.#} m ({a.rolloffMode}), "
                    + $"blend {a.spatialBlend:0.##}, mixer {(a.outputAudioMixerGroup != null ? a.outputAudioMixerGroup.name : "none")}, clip {a.clip.length:0.00} s {a.clip.frequency} Hz {a.clip.loadState}")
                .ToList();
            Info("the rock smash's sources", smashSources.Count == 0 ? "none playing the Stone_break clip" : string.Join("; ", smashSources));
            yield return new WaitForSeconds(1.6f);
            if (recorder != null)
            {
                string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "UltimateAscent.SmokeTest.stone-hits-zombie.wav");
                Info("recording", $"{System.IO.Path.GetFileName(path)}: {recorder.End(path)} samples at {AudioSettings.outputSampleRate} Hz, from just before the throw");
            }
            FallRecorder.Of = null;
            bool heard = StoneItem.BreakSound() != null && StoneItem.BreakSound().lastTimePlayed > soundBefore;
            bool bonked = LastBonk() > bonkBefore;
            Screenshot("stone-hits-zombie");
            Check("a Stone thrown hard at a zombie knocks it down and shatters, with the rock smash (no coconut bonk) and the dust (9)",
                FallRecorder.Seconds.Count > 0 && StoneItem.Breaks > breaksBefore && StoneItem.DustClouds > dustBefore && heard && !bonked && coconutBonks.Length > 0,
                $"zombie knocked down {FallRecorder.Seconds.Count > 0} ({string.Join(", ", FallRecorder.Seconds.Select(x => x.ToString("0.00")))} s), breaks {StoneItem.Breaks - breaksBefore}, dust {StoneItem.DustClouds - dustBefore}, smash played {heard}, coconut bonk played {bonked} (of {coconutBonks.Length})");
        }
        finally
        {
            FallRecorder.Of = null;
            local.data.lookValues = look;
            if (made != null && made.GetPhotonView() != null)
            {
                PhotonNetwork.Destroy(made);
            }
        }
    }

    /// <summary>
    /// The Stick in both hands, its "swing", 33% stamina per swing, the chop over the head (pictured from the
    /// side), a zombie in front knocked down for 3 s and not hurt, and no swing out of stamina.
    /// </summary>
    private IEnumerator StickSwing(Item stick)
    {
        Character local = Local;
        yield return SpawnIntoHand(stick);
        if (held == null)
        {
            yield break;
        }
        Item inHand = held;
        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        local.data.lookValues = new Vector2(Quaternion.LookRotation(forward).eulerAngles.y, -5f);
        string prompt = LocalizedText.GetText(inHand.UIData.mainInteractPrompt);
        Check("the Stick's use is to swing it", inHand.UIData.hasMainInteract && Plain(prompt).ToLowerInvariant() == "swing", $"use '{prompt}'");
        GameObject camera = SideCamera(local);
        GameObject zombie = null;
        try
        {
            yield return new WaitForSeconds(1f);
            Screenshot("stick-held");
            yield return new WaitForSeconds(0.8f);

            Vector3 spot = local.Center + forward * 1.7f;
            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f, HelperFunctions.terrainMapMask))
            {
                spot = ground.point + Vector3.up * 0.9f;
            }
            zombie = PhotonNetwork.Instantiate("MushroomZombie_Player", spot, Quaternion.LookRotation(-forward), 0);
            MushroomZombie walker = zombie != null ? zombie.GetComponent<MushroomZombie>() : null;
            yield return new WaitForSeconds(1.5f);
            if (walker != null && ZombieManager.Instance != null && !ZombieManager.Instance.zombies.Contains(walker))
            {
                // A risen zombie is registered once it rises from a scout; the Roots' own are registered when they spawn.
                ZombieManager.Instance.RegisterZombie(walker);
                Info("the test zombie is counted among the zombies, as the Roots' own are");
            }
            Character target = walker != null ? walker.character : null;
            float targetInjury = target != null ? target.refs.afflictions.GetCurrentStatus(Status.Injury) : 0f;
            int swings = StickItem.Swings;
            local.data.currentStamina = 1f;
            float before = local.data.currentStamina;
            Step("swing the Stick", () => inHand.StartUsePrimary());
            float swungAt = Time.time;
            float cost = before - local.data.currentStamina;
            Check("a swing costs 33% stamina", StickItem.Swings == swings + 1 && Mathf.Abs(cost - StickItem.SwingStamina) < 0.003f, $"{cost * 100f:0.#}% stamina, {StickItem.Swings - swings} swing");
            yield return new WaitForSeconds(StickItem.Swing[1].at + 0.02f);
            Screenshot("stick-swing-raised");
            yield return new WaitForSeconds(Mathf.Max(0f, StickItem.StrikeDelay + 0.08f - (Time.time - swungAt)));
            Screenshot("stick-swing-strike");
            float longest = 0f;
            for (float until = Time.time + 0.6f; Time.time < until;)
            {
                if (target != null)
                {
                    longest = Mathf.Max(longest, target.data.fallSeconds);
                }
                yield return null;
            }
            inHand.CancelUsePrimary();
            bool hit = target != null && StickItem.LastHits.Contains(target);
            float hurt = target != null ? target.refs.afflictions.GetCurrentStatus(Status.Injury) - targetInjury : 0f;
            Check("the swing knocks down the zombie in front for 4 s without hurting it",
                hit && longest > StickItem.Knockdown - 0.7f && longest <= StickItem.Knockdown + 0.01f && hurt < 0.001f,
                target == null ? "no zombie" : $"hit {hit}, knocked down {longest:0.00} s right after the hit, +{hurt * 100f:0.#} injury");
            yield return new WaitForSeconds(1f);

            local.data.currentStamina = 0f;
            local.data.extraStamina = 0f;
            swings = StickItem.Swings;
            Step("try to swing out of stamina", () => inHand.StartUsePrimary());
            yield return new WaitForSeconds(0.2f);
            inHand.CancelUsePrimary();
            Check("out of stamina the Stick does not swing", StickItem.Swings == swings, $"{StickItem.Swings - swings} swings");
            local.data.currentStamina = 1f;

            local.data.currentStamina = 1f;
        }
        finally
        {
            Destroy(camera);
            if (zombie != null && zombie.GetPhotonView() != null)
            {
                PhotonNetwork.Destroy(zombie);
            }
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// A mushroom zombie's bite knocks the scout down for 4 s (17), with ascent 17's rule on and off (in
    /// memory). The zombie is the Roots' kind, made straight from its prefab, so it is set up like any
    /// player's copy of the host's zombie (the zombie spot sets up only the host's): it wakes when the
    /// scout looks at it, then chases and lunges by itself.
    /// </summary>
    private IEnumerator ZombieBiteCheck()
    {
        if (Rules.Level < Lvl.Hazards)
        {
            Info("the zombie bite check is skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        ConfigEntry<bool> hazards = UltimateAscent.Plugin.AscentSwitches[Lvl.Hazards];
        bool saved = hazards.Value;
        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        GameObject camera = SideCamera(local, 4f);
        GameObject made = null;
        try
        {
            foreach (bool on in new[] { true, false })
            {
                hazards.Value = on;
                HostOptions.Invalidate();
                local.data.lookValues = new Vector2(Quaternion.LookRotation(forward).eulerAngles.y, -5f);
                Vector3 spot = local.Center + forward * 12f;
                if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f, HelperFunctions.terrainMapMask))
                {
                    spot = ground.point + Vector3.up * 0.9f;
                }
                made = PhotonNetwork.Instantiate("MushroomZombie", spot, Quaternion.LookRotation(-forward), 0);
                MushroomZombie zombie = made != null ? made.GetComponent<MushroomZombie>() : null;
                MushroomZombieBiteCollider bite = zombie != null ? zombie.GetComponentInChildren<MushroomZombieBiteCollider>(true) : null;
                if (bite == null)
                {
                    Fail("a mushroom zombie for the bite check", "the game's MushroomZombie could not be made");
                    yield break;
                }
                float game = zombie.biteStunTime;
                // How it moves while it chases: running or walking, in its first 3 s of the chase and after.
                float chaseStarted = -1f;
                int early = 0;
                int earlyRunning = 0;
                int moving = 0;
                int running = 0;
                for (float until = Time.time + 30f; Time.time < until && bite.lastBitLocalCharacter <= 0f;)
                {
                    // The chase's 3 s count from when it starts chasing (the game's timeSpentChasing), not its first step.
                    if (zombie.currentState == MushroomZombie.State.Chasing && chaseStarted < 0f)
                    {
                        chaseStarted = Time.time;
                    }
                    if (zombie.currentState == MushroomZombie.State.Chasing && zombie.character.input.movementInput.y > 0.01f && zombie.character.data.isGrounded)
                    {
                        bool run = zombie.character.input.sprintIsPressed;
                        moving++;
                        running += run ? 1 : 0;
                        if (Time.time - chaseStarted < 3f)
                        {
                            early++;
                            earlyRunning += run ? 1 : 0;
                        }
                    }
                    yield return null;
                }
                Check(on ? "a mushroom zombie runs from the first moment of its chase, as when it lunges (17)" : "with ascent 17's rule off a zombie walks for its first 3 s of the chase (the game's)",
                    on ? moving > 0 && running >= moving * 0.95f && early > 0 && earlyRunning >= early * 0.95f : early > 0 && earlyRunning == 0,
                    $"running in {running} of {moving} chasing frames; in the first 3 s {earlyRunning} of {early}");
                bool bitten = bite.lastBitLocalCharacter > 0f;
                float longest = 0f;
                if (bitten)
                {
                    for (float until = Time.time + 0.3f; Time.time < until;)
                    {
                        longest = Mathf.Max(longest, local.data.fallSeconds);
                        yield return null;
                    }
                    Screenshot(on ? "zombie-bite-rule-on" : "zombie-bite-rule-off");
                }
                float expected = on ? A17_Hazards.ZombieStunSeconds : game;
                Check(on ? "a zombie's bite knocks the scout down for 4 s (17)" : "with ascent 17's rule off a zombie's bite knocks the scout down for the game's 3 s",
                    bitten && longest > expected - 0.25f && longest <= expected + 0.01f && Mathf.Approximately(zombie.biteStunTime, game),
                    bitten ? $"knocked down {longest:0.00} s right after the bite (the zombie's own value stays the game's {zombie.biteStunTime:0.#} s)" : $"no bite in 30 s (the zombie is {zombie.currentState})");
                yield return new WaitForSeconds(1f);
                PhotonNetwork.Destroy(made);
                made = null;
                Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.ZombieBite);
                for (float until = Time.time + 6f; Time.time < until && local.data.fallSeconds > 0f;)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(1.5f);
            }
        }
        finally
        {
            hazards.Value = saved;
            HostOptions.Invalidate();
            Destroy(camera);
            if (made != null && made.GetPhotonView() != null)
            {
                PhotonNetwork.Destroy(made);
            }
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// The Ritual Dagger on a mushroom zombie (9, 15). A zombie of the Roots' kind is made asleep 1.1 m in
    /// front of the scout and kept asleep (one sneaked up on). Facing the scout it can't be stabbed; with
    /// its back to the scout it can. Each stab is the game's own (the use button held while aimed at it, as
    /// on a teammate). It takes 5 stabs (8 with ascent 15's rule on, in memory), then dies the game's way
    /// and is gone 10 s later, leaving a skeleton. Nobody gets anything (the scout's injury and extra
    /// stamina stay) and the dagger is kept.
    /// </summary>
    private IEnumerator DaggerOnZombies()
    {
        if (!Rules.InExtraAscent)
        {
            Info("the Ritual Dagger zombie checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Item dagger = ItemByName("RitualDagger");
        if (dagger == null)
        {
            Fail("the Ritual Dagger is in the item database");
            yield break;
        }
        Character local = Local;
        ConfigEntry<bool> worse = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool saved = worse.Value;
        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        yield return SpawnIntoHand(dagger);
        if (held == null)
        {
            Fail("a Ritual Dagger in the scout's hands");
            yield break;
        }
        Item inHand = held;
        GameObject camera = SideCamera(local, 3.5f);
        GameObject made = null;
        try
        {
            foreach (bool worseOn in new[] { false, true })
            {
                worse.Value = worseOn;
                HostOptions.Invalidate();
                int needed = worseOn ? ZombieDagger.StabsWorse : ZombieDagger.Stabs;
                Vector3 spot = local.Center + forward * 1.1f;
                if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f, HelperFunctions.terrainMapMask))
                {
                    spot = ground.point + Vector3.up * 0.9f;
                }
                made = PhotonNetwork.Instantiate("MushroomZombie", spot, Quaternion.LookRotation(-forward), 0);
                MushroomZombie zombie = made != null ? made.GetComponent<MushroomZombie>() : null;
                if (zombie == null)
                {
                    Fail("a mushroom zombie for the dagger check", "the game's MushroomZombie could not be made");
                    yield break;
                }
                // It sleeps on: the scout looking at it from so close would wake it.
                zombie.distanceBeforeWakeup = 0f;
                Character body = zombie.character;
                yield return new WaitForSeconds(2.5f);

                body.data.lookValues = HelperFunctions.DirectionToLook(-forward);
                local.data.lookValues = HelperFunctions.DirectionToLook(body.Center - MainCamera.instance.transform.position);
                yield return new WaitForSeconds(0.6f);
                bool front = Interaction.instance.bestCharacter != null && Interaction.instance.bestCharacter.character == body;
                body.data.lookValues = HelperFunctions.DirectionToLook(forward);
                local.data.lookValues = HelperFunctions.DirectionToLook(body.Center - MainCamera.instance.transform.position);
                yield return new WaitForSeconds(0.6f);
                CharacterInteractible aimed = Interaction.instance.bestCharacter;
                bool back = aimed != null && aimed.character == body;
                string prompt = back ? Plain(aimed.GetSecondaryInteractionText()) : "";
                Check("the Ritual Dagger can't be used on a zombie facing the scout, only in its back (9)",
                    !front && back && prompt.IndexOf(ZombieDagger.ZombieName, StringComparison.OrdinalIgnoreCase) >= 0,
                    $"aimed at it facing the scout: {(front ? "offered" : "not offered")}; at its back: {(back ? $"'{prompt}'" : "not offered")}");
                if (!back)
                {
                    break;
                }

                Protect(false);
                Action calm = CalmForMeasuring();
                Afflictions.SetStatus(Status.Injury, 0.2f);
                Afflictions.SetStatus(Status.Crab, 0.3f);
                local.data.extraStamina = 0.1f;
                float injury = Afflictions.GetCurrentStatus(Status.Injury);
                float extra = local.data.extraStamina;
                int kills = ZombieDagger.Kills;
                int heard = ZombieDagger.HitsHeard;
                List<int> counted = new List<int>();
                bool keptDagger = true;
                bool aliveUntilLast = true;
                for (int stab = 1; stab <= needed; stab++)
                {
                    local.data.lookValues = HelperFunctions.DirectionToLook(body.Center - MainCamera.instance.transform.position);
                    yield return null;
                    inHand.StartUseSecondary();
                    for (float until = Time.time + 3f; Time.time < until && !inHand.finishedCast;)
                    {
                        inHand.ContinueUseSecondary();
                        yield return null;
                    }
                    inHand.CancelUseSecondary();
                    yield return new WaitForSeconds(0.5f);
                    counted.Add(ZombieDagger.StabsTaken(zombie));
                    keptDagger &= inHand != null && local.data.currentItem == inHand;
                    if (stab < needed)
                    {
                        aliveUntilLast &= zombie.currentState != MushroomZombie.State.Dead;
                    }
                }
                bool dead = zombie.currentState == MushroomZombie.State.Dead;
                float injuryAfter = Afflictions.GetCurrentStatus(Status.Injury);
                float extraAfter = local.data.extraStamina;
                float crabAfter = Afflictions.GetCurrentStatus(Status.Crab);
                Check("every stab on a zombie is heard: the game's sound of a hit, at the zombie (9)", ZombieDagger.HitsHeard - heard == needed,
                    $"{ZombieDagger.HitsHeard - heard} hits heard for {needed} stabs");
                if (!worseOn)
                {
                    // How loud a stab is where the scout hears it: at the zombie, next to the Coconut's bonk there (recorded).
                    float[] stabLevels = null;
                    float[] bonkLevels = null;
                    string stabLine = "";
                    string bonkLine = "";
                    Item coconut = ItemByName("Item_Coconut");
                    SFX_Instance bonk = coconut != null && coconut.TryGetComponent(out Bonkable bonkable) ? bonkable.bonk.FirstOrDefault(b => b != null) : null;
                    SFX_Instance stabSound = ZombieDagger.HitSfx().FirstOrDefault();
                    Info("the stab's sounds", string.Join("; ", ZombieDagger.HitSfx().Select(h => $"{h.name}: volume {h.settings.volume:0.##}, clips {string.Join("/", h.clips.Where(c => c != null).Select(c => c.name + " " + c.length.ToString("0.00") + " s"))}")));
                    Vector3 at = body.Center;
                    int stabberId = local.photonView.ViewID;
                    yield return MeasureSound("the dagger stab", stabSound, at, (levels, line) => { stabLevels = levels; stabLine = line + " (as the scout who stabs hears it)"; }, spot => ZombieDagger.PlayHit(spot, stabberId));
                    yield return MeasureSound("the coconut bonk at the zombie", bonk, at, (levels, line) => { bonkLevels = levels; bonkLine = line; });
                    Check("the scout who stabs a zombie hears the stab at their own ears, like their own hits: 6 dB or more over the Coconut's bonk at the zombie (recorded)",
                        stabLevels != null && bonkLevels != null && stabLevels[1] >= bonkLevels[1] + 6f, stabLine + "; " + bonkLine);
                }
                calm();
                Afflictions.SetStatus(Status.Crab, 0f);
                Protect(true);
                Check(worseOn ? "the stab that kills a zombie takes exactly 15 crab off the scout who made it, with ascent 15's rule too (15)" : "the stab that kills a zombie takes 15 crab off the scout who made it (9)",
                    Mathf.Abs(crabAfter - 0.15f) < 0.001f, $"crab 30 -> {crabAfter * 100f:0.#}");
                Screenshot(worseOn ? "dagger-zombie-dead-after-8" : "dagger-zombie-dead-after-5");
                Check(worseOn ? "a zombie dies after 8 stabs with ascent 15's rule (15)" : "a zombie dies after 5 stabs in the back (9)",
                    dead && aliveUntilLast && ZombieDagger.Kills == kills + 1 && counted.SequenceEqual(Enumerable.Range(1, needed)),
                    $"counted {string.Join(", ", counted)}; standing until the last {aliveUntilLast}, dead after it {dead}");
                Check("stabbing a zombie gives no cure or extra stamina and keeps the dagger",
                    keptDagger && Mathf.Abs(injuryAfter - injury) < 0.001f && Mathf.Abs(extraAfter - extra) < 0.001f,
                    $"dagger kept {keptDagger}; injury {injury * 100f:0.#} -> {injuryAfter * 100f:0.#}, extra stamina {extra * 100f:0.#}% -> {extraAfter * 100f:0.#}%");
                int skeletons = FindObjectsByType<Skelleton>(FindObjectsSortMode.None).Length;
                for (float until = Time.time + 14f; Time.time < until && made != null;)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(0.5f);
                int skeletonsAfter = FindObjectsByType<Skelleton>(FindObjectsSortMode.None).Length;
                Check("the dead zombie is gone 10 s later and leaves a skeleton, as the game's zombies do", made == null && skeletonsAfter == skeletons + 1,
                    $"zombie gone {made == null}, skeletons {skeletons} -> {skeletonsAfter}");
                if (made == null)
                {
                    Screenshot(worseOn ? "dagger-zombie-skeleton-8" : "dagger-zombie-skeleton-5");
                }
                Afflictions.SetStatus(Status.Injury, 0f);
                local.data.extraStamina = 0f;
                yield return new WaitForSeconds(1f);
            }
        }
        finally
        {
            worse.Value = saved;
            HostOptions.Invalidate();
            Protect(true);
            Destroy(camera);
            if (made != null && made.GetPhotonView() != null)
            {
                PhotonNetwork.Destroy(made);
            }
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// The Warp Compass (9): it spawns only where the Ritual Dagger does (Ancient Luggage's pool and the
    /// Ancient Statues'), as rarely, never in solo, and has one use. Used in solo (no teammate) it does
    /// nothing and keeps its use. Where it lands: the scout stands in for a teammate on the beach (it must
    /// have stood on the ground for a second; in the air it does not count), a landing spot beside it is
    /// found, and the scout, moved 20 m away, is warped there the way the compass warps (the game's warp).
    /// </summary>
    private IEnumerator WarpCompassCheck()
    {
        Item compass = WarpCompass.Template;
        LootData loot = compass != null ? compass.GetComponent<LootData>() : null;
        if (loot == null)
        {
            Fail("the Warp Compass is set up", "no compass or no loot data");
            yield break;
        }
        LootData.PopulateLootData();
        List<SpawnPool> pools = LootData.AllSpawnWeightData.Where(kv => kv.Value.ContainsKey(compass.itemID)).Select(kv => kv.Key).ToList();
        int weight = pools.Count > 0 ? LootData.AllSpawnWeightData[pools[0]][compass.itemID] : 0;
        Item dagger = ItemByName("RitualDagger");
        int daggerWeight = dagger != null && LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageCursed, out Dictionary<ushort, int> cursed) && cursed.TryGetValue(dagger.itemID, out int w) ? w : -1;
        Check("the Warp Compass spawns only in Ancient Luggage and the Ancient Statues, as rarely as the Ritual Dagger, never in solo, with one use",
            pools.Count == 2 && pools.Contains(SpawnPool.LuggageCursed) && pools.Contains(SpawnPool.RespawnCoffin) && weight == daggerWeight && loot.banInSolo && compass.totalUses == 1,
            $"pools {string.Join(", ", pools)}, weight {weight} (the dagger's {daggerWeight}, {loot.Rarity}), banned in solo {loot.banInSolo}, {compass.totalUses} use");
        Check("in solo the game keeps it out of every suitcase and statue", !compass.IsValidToSpawn(), $"valid to spawn {compass.IsValidToSpawn()}");

        Character local = Local;
        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        yield return SpawnIntoHand(compass);
        if (held == null)
        {
            Fail("a Warp Compass in the scout's hands");
            yield break;
        }
        Item inHand = held;
        Vector3 before = local.Center;
        int kept = WarpCompass.Kept;
        inHand.StartUsePrimary();
        for (float until = Time.time + 6f; Time.time < until && !inHand.finishedCast;)
        {
            inHand.ContinueUsePrimary();
            yield return null;
        }
        inHand.CancelUsePrimary();
        yield return new WaitForSeconds(1.5f);
        bool stillHeld = inHand != null && local.data.currentItem == inHand;
        int uses = stillHeld && inHand.GetData<OptionableIntItemData>(DataEntryKey.ItemUses) is OptionableIntItemData data && data.HasData ? data.Value : -1;
        float moved = Vector3.Distance(before, local.Center);
        Check("used with no teammate to warp to, it does nothing and keeps its use", WarpCompass.Kept == kept + 1 && stillHeld && uses == 1 && moved < 1f,
            $"kept {WarpCompass.Kept - kept}, still in hand {stillHeld}, {uses} use left, moved {moved:0.0} m");
        WarpNoticeCheck();
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(1f);

        for (float until = Time.time + 5f; Time.time < until && !WarpCompass.Grounded(local);)
        {
            yield return null;
        }
        bool standing = WarpCompass.Grounded(local);
        float groundedFor = local.data.groundedFor;
        Vector3 standingAt = local.Center;
        bool found = WarpCompass.TryFindLanding(local, standingAt + forward * 20f, out Vector3 landing);
        float apart = HelperFunctions.FlatDistance(landing, standingAt);
        Check("beside a teammate who has stood on the ground for a second there is free ground to land on", standing && found && apart > 1.3f && apart < 3.2f,
            $"on the ground {standing} ({groundedFor:0.0} s), landing spot {(found ? $"{apart:0.0} m away, {landing.y - standingAt.y:+0.0;-0.0} m up" : "none")}");

        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standingAt + Vector3.up * 8f, false);
        yield return new WaitForSeconds(0.6f);
        bool inAir = !WarpCompass.Grounded(local);
        Check("a teammate in the air is not warped to", inAir, $"8 m up: on the ground {local.data.isGrounded}, for {local.data.groundedFor:0.0} s");
        yield return new WaitForSeconds(2.5f);

        if (found)
        {
            Vector3 away = OpenSky(standingAt + forward * 20f) ?? standingAt + forward * 20f;
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, away + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(2f);
            float injury = Afflictions.GetCurrentStatus(Status.Injury);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, landing, true);
            yield return new WaitForSeconds(2f);
            Screenshot("warp-compass-landing");
            float off = HelperFunctions.FlatDistance(local.Center, landing);
            float injuryAfter = Afflictions.GetCurrentStatus(Status.Injury);
            Check("warped there the compass's way, the scout lands standing on free ground", local.data.isGrounded && off < 0.8f && injuryAfter <= injury + 0.001f,
                $"{off:0.0} m from the spot, on the ground {local.data.isGrounded}, injury {injury * 100f:0.#} -> {injuryAfter * 100f:0.#}");
        }
        local.data.lookValues = look;
    }

    /// <summary>
    /// Lets statuses be measured for a while: the rising fog is held back from the scout (its safe circle grown) and
    /// ascent 20's Bing Bong rule is off in memory (a neglected Bing Bong brings 5 crab a second), until the undo runs;
    /// the fog then keeps what it has moved meanwhile.
    /// </summary>
    private Action CalmForMeasuring()
    {
        OrbFogHandler fog = Singleton<OrbFogHandler>.Instance;
        FogSphere sphere = fog != null ? fog.sphere : null;
        float size = fog != null ? fog.currentSize : 0f;
        float held = size;
        if (sphere != null)
        {
            held = Mathf.Max(size, Vector3.Distance(sphere.fogPoint, Local.Center) + 80f);
            fog.currentSize = held;
        }
        ConfigEntry<bool> bingBong = UltimateAscent.Plugin.AscentSwitches[Lvl.BingBong];
        bool saved = bingBong.Value;
        bingBong.Value = false;
        HostOptions.Invalidate();
        Afflictions.SetStatus(Status.Crab, 0f);
        return () =>
        {
            bingBong.Value = saved;
            HostOptions.Invalidate();
            if (sphere != null)
            {
                fog.currentSize = size - Mathf.Max(0f, held - fog.currentSize);
            }
        };
    }

    /// <summary>
    /// Ascent 22's nausea off in memory until the undo runs: eating with poison on the bar can bring it on, and then
    /// the food gives no hunger, so checks that measure what a food restores turn it off.
    /// </summary>
    private static Action NoNausea()
    {
        ConfigEntry<bool> episodes = UltimateAscent.Plugin.AscentSwitches[Lvl.UniqueAilments];
        bool saved = episodes.Value;
        episodes.Value = false;
        HostOptions.Invalidate();
        return () =>
        {
            episodes.Value = saved;
            HostOptions.Invalidate();
        };
    }

    /// <summary>Puts the item away: the hands are emptied and its slot cleared, so the next checks start with free hands.</summary>
    private IEnumerator PutAway(Item prefab)
    {
        Character local = Local;
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        foreach (byte slot in new byte[] { 0, 1, 2, 250 })
        {
            ItemSlot itemSlot = local.player.GetItemSlot(slot);
            if (itemSlot != null && !itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == prefab.itemID)
            {
                local.player.EmptySlot(Optionable<byte>.Some(slot));
            }
        }
        Afflictions.UpdateWeight();
        held = null;
    }

    /// <summary>
    /// The Climbing Chalk held like the Granola Bar (upright, the hands round the bag's lower front corners): both
    /// pictured in the hands in first person, looking ahead and looking down, the Granola Bar first to compare.
    /// </summary>
    private IEnumerator ChalkGrip()
    {
        Character local = Local;
        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        Item bar = ItemByName("Granola Bar");
        Item chalk = ItemByName(ClimbingChalkItem.PrefabName);
        if (bar == null || chalk == null)
        {
            Fail("the Granola Bar and the Climbing Chalk are in the item database");
            yield break;
        }
        try
        {
            foreach (Item prefab in new[] { bar, chalk })
            {
                yield return SpawnIntoHand(prefab);
                if (held == null)
                {
                    continue;
                }
                if (prefab == chalk)
                {
                    Transform left = held.transform.Find("Hand_L");
                    Transform right = held.transform.Find("Hand_R");
                    Transform barLeft = bar.transform.Find("Hand_L");
                    bool pose = left != null && right != null && barLeft != null && Quaternion.Angle(left.localRotation, barLeft.localRotation) < 0.5f
                        && left.localPosition.y < -0.05f && right.localPosition.y < -0.05f && left.localPosition.z < 0f && held.defaultPos == ClimbingChalkModel.HeldAt;
                    Check("the chalk is held like the Granola Bar: upright, the hands round its lower front corners with the Granola Bar's hand poses", pose,
                        left == null ? "no hands" : $"hands at {left.localPosition:F3} / {right.localPosition:F3}, held at {held.defaultPos:F2}");
                }
                foreach ((string where, Vector3 direction) in new[] { ("ahead", forward + Vector3.down * 0.18f), ("down", forward + Vector3.down) })
                {
                    local.data.lookValues = HelperFunctions.DirectionToLook(direction);
                    yield return new WaitForSeconds(2f);
                    Screenshot((prefab == bar ? "grip-granola-bar" : "grip-climbing-chalk") + "-" + where);
                    yield return new WaitForSeconds(0.8f);
                }
                yield return PutAway(prefab);
            }
        }
        finally
        {
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// The Cherry Lollipop (9): half of the Big Lollipop's picks from a suitcase (its pool holding only the Big
    /// Lollipop, in memory); the same name, food, effects and cooking in another look; eaten, the Big Lollipop's 8 s
    /// of infinite stamina.
    /// </summary>
    private IEnumerator CherryLollipopCheck()
    {
        Item plain = ItemByName(CherryLollipop.LollipopPrefab);
        Item pink = CherryLollipop.Template;
        if (plain == null || pink == null)
        {
            Fail("the Big Lollipop and the Cherry Lollipop are in the item database", $"Big Lollipop {plain != null}, cherry {pink != null}");
            yield break;
        }
        if (!Rules.InExtraAscent)
        {
            Info("the Cherry Lollipop checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Luggage luggage = Luggage.ALL_LUGGAGE
            .Where(l => l != null && !(l is RespawnChest) && !l.CompareTag("ClownLuggage") && l.gameObject.activeInHierarchy)
            .OrderBy(l => Vector3.Distance(l.transform.position, local.Center))
            .FirstOrDefault();
        if (luggage != null)
        {
            Step("Big Lollipop picks", () =>
            {
                SpawnPool pool = luggage.GetSpawnPool();
                LootData.PopulateLootData();
                // Every pool holds only the Big Lollipop (in memory): a suitcase can pick from a pool of its height instead of its own.
                Dictionary<SpawnPool, Dictionary<ushort, int>> own = new Dictionary<SpawnPool, Dictionary<ushort, int>>(LootData.AllSpawnWeightData);
                foreach (SpawnPool each in own.Keys.Append(pool).Distinct())
                {
                    LootData.AllSpawnWeightData[each] = new Dictionary<ushort, int> { [plain.itemID] = LootData.RarityWeights[plain.GetComponent<LootData>().Rarity] };
                }
                int builtBefore = LootTablesBuilt.Count;
                Dictionary<ushort, int> ours = LootData.AllSpawnWeightData[pool];
                int plainPicks = 0;
                int pinkPicks = 0;
                Dictionary<string, int> others = new Dictionary<string, int>();
                try
                {
                    for (int i = 0; i < 50; i++)
                    {
                        foreach (GameObject pick in luggage.GetObjectsToSpawn(4, true))
                        {
                            if (pick != null && pick.name == CherryLollipop.PrefabName)
                            {
                                pinkPicks++;
                            }
                            else if (pick != null && pick.name == CherryLollipop.LollipopPrefab)
                            {
                                plainPicks++;
                            }
                            else
                            {
                                string other = pick != null ? pick.name : "nothing";
                                others[other] = others.TryGetValue(other, out int n) ? n + 1 : 1;
                            }
                        }
                    }
                }
                finally
                {
                    HoldLootTables.Hold = false;
                    Info("the Big Lollipop picks", $"loot tables built {LootTablesBuilt.Count - builtBefore} time(s) during the picks; the pool still the one set: {LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> now) && ReferenceEquals(now, ours)}, the suitcase spawns from a pool {luggage.isSpawnPool}, by height {luggage.isHeightBasedSpawnPool}, single {luggage.isSingleItem}");
                    LootData.AllSpawnWeightData.Clear();
                    foreach (KeyValuePair<SpawnPool, Dictionary<ushort, int>> entry in own)
                    {
                        LootData.AllSpawnWeightData[entry.Key] = entry.Value;
                    }
                }
                Check("half of the Big Lollipop's spawns are the Cherry Lollipop (9)", pinkPicks >= 70 && plainPicks >= 70 && pinkPicks + plainPicks == 200,
                    $"{pinkPicks} pink and {plainPicks} plain of 200 picks from {luggage.gameObject.name}'s pool ({pool}){(others.Count > 0 ? "; also picked: " + string.Join(", ", others.Select(o => $"{o.Key} {o.Value}")) : "")}");
            });
        }
        Item energyItem = ItemByName("Energy Drink");
        Peak.Afflictions.Affliction_FasterBoi energy = energyItem != null ? energyItem.GetComponents<Action_ApplyAffliction>().Select(a => a.affliction).OfType<Peak.Afflictions.Affliction_FasterBoi>().FirstOrDefault() : null;
        Step("Cherry Lollipop item", () =>
        {
            float Hunger(Item item) => item.GetComponents<Action_RestoreHunger>().Sum(a => a.restorationAmount);
            string Skin(Item item) => item.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).Where(m => m != null && m.name.Contains("Lollipop")).Select(m => m.name + "/" + (m.GetTexture("_BaseTexture") != null ? m.GetTexture("_BaseTexture").name : "none")).FirstOrDefault() ?? "none";
            string Effects(Item item) => string.Join("; ", item.GetComponents<Action_ApplyAffliction>().Select(a => (a.enabled ? "" : "cooked: ") + string.Join(" + ",
                new[] { a.affliction }.Concat(a.extraAfflictions ?? new Peak.Afflictions.Affliction[0]).Select(x => x switch
                {
                    Peak.Afflictions.Affliction_InfiniteStamina r => $"infinite stamina {r.totalTime:0.#} s (hold {r.climbDelay:0.#} s)" + (r.drowsyAffliction is Peak.Afflictions.Affliction_AdjustDrowsyOverTime c && c.statusPerSecond > 0f ? $", then {c.statusPerSecond * c.totalTime * 100f:0.#} drowsiness over {c.totalTime:0.#} s" : ""),
                    Peak.Afflictions.Affliction_FasterBoi b => $"boost walk +{b.moveSpeedMod:0.##} climb +{b.climbSpeedMod:0.##} {b.totalTime:0.#} s (hold {b.climbDelay:0.#} s), crash {b.drowsyOnEnd * 100f:0.#}",
                    _ => x?.GetType().Name ?? "null",
                }))));
            List<Action_ApplyAffliction> raw = pink.GetComponents<Action_ApplyAffliction>().ToList();
            bool built = raw.Count == 2 && raw.All(a => a.affliction is Peak.Afflictions.Affliction_InfiniteStamina r && Mathf.Approximately(r.totalTime, CherryLollipop.Seconds) && r.climbDelay == 0f
                && a.extraAfflictions != null && a.extraAfflictions.Length == 1 && a.extraAfflictions[0] is Peak.Afflictions.Affliction_FasterBoi b && Mathf.Approximately(b.totalTime, CherryLollipop.Seconds)
                && b.climbDelay == 0f && b.drowsyOnEnd == 0f && energy != null && Mathf.Approximately(b.moveSpeedMod, energy.moveSpeedMod) && Mathf.Approximately(b.climbSpeedMod, energy.climbSpeedMod));
            Check("the Cherry Lollipop: the Big Lollipop's food, its rush and the Energy Drink's boost together for 6 s without a hold, then the Big Lollipop's drowsiness (cooked: none)",
                Plain(pink.GetItemName()) == "CHERRY LOLLIPOP" && Mathf.Approximately(Hunger(pink), Hunger(plain)) && built && pink.GetComponent<ItemCooking>() != null && Skin(pink) != Skin(plain),
                $"name '{Plain(pink.GetItemName())}', hunger {Hunger(pink) * 100f:0.#} (the Big Lollipop's {Hunger(plain) * 100f:0.#}), effects [{Effects(pink)}], the Big Lollipop's [{Effects(plain)}], look '{Skin(pink)}'");
        });
        yield return SideBySide(new[] { plain, pink }, "big-lollipop-and-cherry");

        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        ConfigEntry<bool> worseRule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool worseSaved = worseRule.Value;
        Action undo = CalmForMeasuring();
        Protect(false);
        try
        {
            foreach (bool worse in new[] { false, true })
            {
                worseRule.Value = worse;
                HostOptions.Invalidate();
                string level = worse ? "14" : "9";
                float seconds = worse ? CherryLollipop.SecondsWorse : CherryLollipop.Seconds;
                yield return SpawnIntoHand(pink);
                if (held == null)
                {
                    continue;
                }
                if (!worse)
                {
                    local.data.lookValues = HelperFunctions.DirectionToLook(forward + Vector3.down * 0.18f);
                    yield return new WaitForSeconds(1.2f);
                    Screenshot("cherry-lollipop-held");
                    yield return new WaitForSeconds(0.8f);
                }
                Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.InfiniteStamina);
                Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.FasterBoi);
                Afflictions.SetStatus(Status.Drowsy, 0f);
                yield return null;
                float walk = local.refs.movement.movementModifier;
                yield return EatHeld();
                Peak.Afflictions.Affliction_InfiniteStamina rush = Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_InfiniteStamina>().FirstOrDefault();
                Peak.Afflictions.Affliction_FasterBoi boost = Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_FasterBoi>().FirstOrDefault();
                float startedAt = Time.time - (rush != null ? rush.timeElapsed : 0f);
                float walkGain = local.refs.movement.movementModifier - walk;
                float expectedWalk = worse ? A14_Items.EnergyDrinkWalking : (energy != null ? energy.moveSpeedMod : 0.5f);
                Check($"eaten, the Cherry Lollipop gives infinite stamina and the Energy Drink's boost at once, for {seconds:0} s ({level})",
                    rush != null && boost != null && Mathf.Abs(rush.totalTime - seconds) < 0.01f && Mathf.Abs(boost.totalTime - seconds) < 0.01f && Mathf.Abs(walkGain - expectedWalk) < 0.01f
                    && boost.drowsyOnEnd == 0f && local.infiniteStam,
                    $"infinite stamina {(rush != null ? rush.totalTime.ToString("0.#") + " s" : "none")}, boost {(boost != null ? boost.totalTime.ToString("0.#") + " s" : "none")}, walking +{walkGain:0.##} (expected +{expectedWalk:0.##}), the boost's crash {(boost != null ? boost.drowsyOnEnd * 100f : -1f):0.#}, running {local.infiniteStam}");
                float rushEnded = -1f;
                float boostEnded = -1f;
                Peak.Afflictions.Affliction_AdjustDrowsyOverTime crash = null;
                for (float until = startedAt + seconds + 4f; Time.time < until && (rushEnded < 0f || boostEnded < 0f || crash == null);)
                {
                    if (rushEnded < 0f && !Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_InfiniteStamina>().Any())
                    {
                        rushEnded = Time.time - startedAt;
                    }
                    if (boostEnded < 0f && !Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_FasterBoi>().Any())
                    {
                        boostEnded = Time.time - startedAt;
                    }
                    crash ??= Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_AdjustDrowsyOverTime>().FirstOrDefault();
                    yield return null;
                }
                float crashSeconds = worse ? A14_Items.LollipopDrowsySeconds : 10f;
                Check($"both last just {seconds:0} s (no 3 s hold), then the Big Lollipop's drowsiness follows ({level})",
                    Mathf.Abs(rushEnded - seconds) < 0.3f && Mathf.Abs(boostEnded - seconds) < 0.3f && crash != null && Mathf.Abs(crash.totalTime - crashSeconds) < 0.01f,
                    $"infinite stamina ended after {rushEnded:0.00} s, the boost after {boostEnded:0.00} s; drowsiness after it {(crash != null ? $"over {crash.totalTime:0.#} s" : "none")} (expected over {crashSeconds:0} s)");
                // The drowsiness: the Big Lollipop's and 20 more, with the ascents' changes on the 20 too.
                if (!worse)
                {
                    Peak.Afflictions.Affliction_AdjustDrowsyOverTime game = plain.GetComponents<Action_ApplyAffliction>().Select(a => a.affliction).OfType<Peak.Afflictions.Affliction_InfiniteStamina>()
                        .Select(r => r.drowsyAffliction).OfType<Peak.Afflictions.Affliction_AdjustDrowsyOverTime>().FirstOrDefault(c => c.statusPerSecond > 0f);
                    float total = crash != null ? crash.statusPerSecond * crash.totalTime : 0f;
                    float expected = game != null ? game.statusPerSecond * game.totalTime + CherryLollipop.ExtraDrowsiness : -1f;
                    Check("without ascent 15's rule the Cherry Lollipop's drowsiness is the Big Lollipop's and 20 more (9)", crash != null && Mathf.Abs(total - expected) < 0.001f,
                        $"{total * 100f:0.#} drowsiness over {(crash != null ? crash.totalTime : 0f):0.#} s (the Big Lollipop's {(expected - CherryLollipop.ExtraDrowsiness) * 100f:0.#} + 20)");
                }
                else
                {
                    // Ascent 15's crash (85, what lands from ascent 14 on) and the 20 in the same terms: 26.
                    float total = crash != null ? crash.statusPerSecond * crash.totalTime : 0f;
                    float extra = CherryLollipop.ExtraDrowsiness * A15_StrongerAilments.FullFactor(Status.Drowsy);
                    float expected = A14_Items.LollipopDrowsiness + extra;
                    Check("with ascent 15's rule the Cherry Lollipop's drowsiness is that ascent's Big Lollipop's and 20 more, with ascent 14's boost on the 20 too (15, 14)",
                        crash != null && Mathf.Abs(total - expected) < 0.001f,
                        $"{total * 100f:0.#} drowsiness over {(crash != null ? crash.totalTime : 0f):0.#} s (the Big Lollipop's {A14_Items.LollipopDrowsiness * 100f:0.#} + {extra * 100f:0.#}; at 15 itself 1.3 times less)");
                }
                if (crash != null)
                {
                    Afflictions.RemoveAffliction(crash.GetAfflictionType());
                }
                Afflictions.SetStatus(Status.Drowsy, 0f);
            }
        }
        finally
        {
            worseRule.Value = worseSaved;
            HostOptions.Invalidate();
            Protect(true);
            undo();
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// The Toxic Drink (9, 15): it spawns only in Ancient Luggage and the Ancient Statues, as rarely as the Faerie
    /// Lantern, and has its own Custom Run switch. Drunk, walking and climbing are 4 times as fast for 10 s (with
    /// ascent 15's rule on, in memory: 4 times for 8 s); once the boost is over it poisons for 4 over 5 s (8).
    /// Cooked (the game's own cooking), it bursts when drunk and no poison follows.
    /// </summary>
    private IEnumerator ToxicDrinkCheck()
    {
        Item drink = ToxicDrink.Template;
        Item energy = ItemByName(ToxicDrink.EnergyDrinkPrefab);
        Item faerie = ItemByName("Lantern_Faerie");
        if (drink == null || energy == null)
        {
            Fail("the Toxic Drink and the Energy Drink are in the item database", $"Toxic Drink {drink != null}, Energy Drink {energy != null}");
            yield break;
        }
        if (!Rules.InExtraAscent)
        {
            Info("the Toxic Drink checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Step("Toxic Drink item", () =>
        {
            LootData.PopulateLootData();
            List<SpawnPool> pools = LootData.AllSpawnWeightData.Where(kv => kv.Value.ContainsKey(drink.itemID)).Select(kv => kv.Key).ToList();
            int weight = pools.Count > 0 ? LootData.AllSpawnWeightData[pools[0]][drink.itemID] : 0;
            int faerieWeight = faerie != null && LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageCursed, out Dictionary<ushort, int> cursed) && cursed.TryGetValue(faerie.itemID, out int w) ? w : -1;
            int listed = RunSettings.ModifiableItemPrefabs.IndexOf(drink);
            int compassAt = RunSettings.ModifiableItemPrefabs.FindIndex(i => i != null && i.gameObject.name == WarpCompass.PrefabName);
            Check("the Toxic Drink spawns only in Ancient Luggage and the Ancient Statues, as rarely as the Faerie Lantern, with its own Custom Run switch after the Warp Compass's",
                pools.Count == 2 && pools.Contains(SpawnPool.LuggageCursed) && pools.Contains(SpawnPool.RespawnCoffin) && weight == faerieWeight && listed > 0 && listed == compassAt + 1,
                $"pools {string.Join(", ", pools)}, weight {weight} (the Faerie Lantern's {faerieWeight}), on the Custom Run screen at {listed} (the Warp Compass at {compassAt})");
            Check("it is called the Toxic Drink and spawns in this run", Plain(drink.GetItemName()) == "TOXIC DRINK" && drink.IsValidToSpawn(),
                $"name '{Plain(drink.GetItemName())}', valid to spawn {drink.IsValidToSpawn()}, icon '{(drink.UIData.icon != null ? drink.UIData.icon.name : "none")}'");
            Material canLook = drink.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.name == "M_ToxicDrink");
            Material energyLook = energy.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.name.StartsWith("M_EnergyDrink", StringComparison.Ordinal));
            Texture Layer(Material m, string name) => m != null && m.HasProperty(name) ? m.GetTexture(name) : null;
            Check("its can wears its own label, drawn without the item shader's checker layer (the Energy Drink keeps it)",
                canLook != null && energyLook != null && Layer(canLook, "_BaseTexture") != null && Layer(canLook, "_BaseTexture").name == "ToxicDrink"
                && Layer(canLook, ToxicDrink.CheckerLayer)?.name == Texture2D.whiteTexture.name && Layer(energyLook, ToxicDrink.CheckerLayer) != null && Layer(energyLook, ToxicDrink.CheckerLayer).name != Texture2D.whiteTexture.name,
                $"label '{Layer(canLook, "_BaseTexture")?.name}', checker layer '{Layer(canLook, ToxicDrink.CheckerLayer)?.name}' (the Energy Drink's '{Layer(energyLook, ToxicDrink.CheckerLayer)?.name}')");
            Color Tint(Material m) => m != null && m.HasProperty("_Tint") ? m.GetColor("_Tint") : Color.clear;
            Color cookedOnce = Tint(energyLook) * ItemCooking.GetCookColor(1);
            Color canTint = Tint(canLook);
            Check("its can wears the tint of a cooked Energy Drink's (the game's first cooking), not the bright label",
                canLook != null && Mathf.Abs(canTint.r - cookedOnce.r) < 0.01f && Mathf.Abs(canTint.g - cookedOnce.g) < 0.01f && Mathf.Abs(canTint.b - cookedOnce.b) < 0.01f,
                $"tint {canTint} (the Energy Drink's {Tint(energyLook)}, cooked once {cookedOnce})");
            // What Item Stats and Effect Preview read: the game's poison action on it, with this ascent's numbers in place.
            Action_InflictPoison shownPoison = drink.GetComponent<Action_InflictPoison>();
            UltimateAscent.Compat.ItemFigures.Begin(drink);
            float shownTotal = shownPoison != null ? shownPoison.poisonPerSecond * shownPoison.inflictionTime : 0f;
            float shownSeconds = shownPoison != null ? shownPoison.inflictionTime : 0f;
            UltimateAscent.Compat.ItemFigures.End();
            bool triggers = shownPoison != null && (shownPoison.OnPressed || shownPoison.OnHeld || shownPoison.OnReleased || shownPoison.OnCastFinished || shownPoison.OnCancelled || shownPoison.OnConsumed
                || shownPoison.OnSecondaryCastFinished || shownPoison.OnSecondaryPressed || shownPoison.OnSecondaryHeld || shownPoison.OnSecondaryCancelled);
            Check("the mods that show item effects see its poison: a poison action with what follows the boost, which never runs itself",
                shownPoison != null && shownPoison.enabled && !triggers && Mathf.Abs(shownTotal - ToxicDrink.PoisonNow) < 0.001f && Mathf.Abs(shownSeconds - ToxicDrink.PoisonSeconds) < 0.001f,
                shownPoison == null ? "no poison action" : $"{shownTotal * 100f:0.#} poison over {shownSeconds:0} s (this ascent: {ToxicDrink.PoisonNow * 100f:0.#}), enabled {shownPoison.enabled}, runs on its own {triggers}");
            SFX_Instance sound = drink.GetComponent<ItemUseFeedback>()?.sfxUsed;
            SFX_Instance game = energy.GetComponent<ItemUseFeedback>()?.sfxUsed;
            Check("its drinking sound is the Energy Drink's, played 1.5 times as fast",
                sound != null && game != null && sound != game && sound.clips.SequenceEqual(game.clips) && Mathf.Abs(sound.settings.pitch - game.settings.pitch * ToxicDrink.SoundSpeed) < 0.001f,
                sound == null || game == null ? "no sound" : $"'{sound.name}' at pitch {sound.settings.pitch:0.##} (the Energy Drink's '{game.name}' at {game.settings.pitch:0.##}), same clips {sound.clips.SequenceEqual(game.clips)}");
        });
        yield return SideBySide(new[] { energy, drink }, "energy-drink-and-toxic-drink");

        Character local = Local;
        ConfigEntry<bool> worseRule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool worseSaved = worseRule.Value;
        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        Action undo = CalmForMeasuring();
        Protect(false);
        try
        {
            foreach ((bool worse, bool cooked) in new[] { (false, false), (true, false), (false, true) })
            {
                if (worse && Rules.Level < Lvl.WorseItems)
                {
                    continue;
                }
                worseRule.Value = worse;
                HostOptions.Invalidate();
                string level = cooked ? "cooked" : worse ? "14" : "9";
                yield return SpawnIntoHand(drink);
                if (held == null)
                {
                    continue;
                }
                Item can = held;
                if (cooked)
                {
                    can.GetComponent<ItemCooking>().FinishCooking();
                    yield return new WaitForSeconds(0.5f);
                    Action_InflictPoison shown = can.GetComponent<Action_InflictPoison>();
                    Check("cooked, its poison action is off too (the item mods show no poison)", shown != null && !shown.enabled,
                        shown == null ? "no poison action" : $"enabled {shown.enabled}");
                }
                else if (!worse)
                {
                    local.data.lookValues = HelperFunctions.DirectionToLook(forward + Vector3.down * 0.18f);
                    yield return new WaitForSeconds(1.2f);
                    Screenshot("toxic-drink-held");
                    yield return new WaitForSeconds(0.8f);
                }
                Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.FasterBoi);
                yield return null;
                Afflictions.SetStatus(Status.Poison, 0f);
                Afflictions.SetStatus(Status.Cold, 0f);
                float walk = local.refs.movement.movementModifier;
                float climb = local.refs.climbing.climbSpeedMod;
                int bursts = FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith(ToxicDrink.BurstName, StringComparison.Ordinal));
                float drowsyBefore = Afflictions.GetCurrentStatus(Status.Drowsy);
                yield return EatHeld();
                Peak.Afflictions.Affliction_FasterBoi boost = Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_FasterBoi>().FirstOrDefault();
                // When it began: its clock has run since (a held clock would show here as a boost lasting longer).
                float startedAt = Time.time - (boost != null ? boost.timeElapsed : 0f);
                float walkGain = local.refs.movement.movementModifier - walk;
                float climbGain = local.refs.climbing.climbSpeedMod - climb;
                int burstsAfter = FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith(ToxicDrink.BurstName, StringComparison.Ordinal));
                float speed = worse ? ToxicDrink.SpeedWorse : ToxicDrink.Speed;
                float seconds = worse ? ToxicDrink.SecondsWorse : ToxicDrink.Seconds;
                Check(cooked ? "a cooked Toxic Drink bursts when drunk; its boost is the same (4 times for 10 s)" : $"the Toxic Drink makes walking and climbing {speed:0} times as fast for {seconds:0} s ({level})",
                    boost != null && Mathf.Abs(boost.totalTime - seconds) < 0.01f && Mathf.Abs(walkGain - (speed - 1f)) < 0.01f && Mathf.Abs(climbGain - (speed - 1f)) < 0.01f && (!cooked || burstsAfter > bursts),
                    $"boost {(boost != null ? boost.totalTime.ToString("0.#") + " s" : "none")}, walking +{walkGain:0.##} and climbing +{climbGain:0.##} (times {1f + walkGain:0.##} / {1f + climbGain:0.##}){(cooked ? $", bursts {burstsAfter - bursts}" : "")}");
                float duringBoost = 0f;
                float lasted = -1f;
                for (float until = startedAt + seconds + 6f; Time.time < until;)
                {
                    if (!Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_FasterBoi>().Any())
                    {
                        lasted = Time.time - startedAt;
                        break;
                    }
                    duringBoost = Mathf.Max(duringBoost, Afflictions.GetCurrentStatus(Status.Poison));
                    yield return null;
                }
                Check($"the boost lasts just its {seconds:0} s ({level})", Mathf.Abs(lasted - seconds) < 0.3f,
                    lasted < 0f ? "still running after " + (seconds + 6f).ToString("0") + " s" : $"it lasted {lasted:0.00} s");
                yield return new WaitForSeconds(ToxicDrink.PoisonSeconds + 1f);
                float poison = Afflictions.GetCurrentStatus(Status.Poison);
                float drowsyAfter = Afflictions.GetCurrentStatus(Status.Drowsy);
                Check(cooked ? "a cooked Toxic Drink makes no one drowsy and knocks no one out (its burst is no explosion for ascent 17)" : $"the Toxic Drink makes no one drowsy ({level})",
                    drowsyAfter <= drowsyBefore + 0.001f && !local.data.passedOut && !local.data.dead,
                    $"drowsiness {drowsyBefore * 100f:0.#} -> {drowsyAfter * 100f:0.#}, passed out {local.data.passedOut}, dead {local.data.dead}");
                float expected = cooked ? 0f : ToxicDrink.PoisonNow;
                Check(cooked ? "no poison follows a cooked Toxic Drink" : $"once the boost is over it poisons for {expected * 100f / ToxicDrink.PoisonSeconds:0.#} a second for 5 s, {expected * 100f:0.#} in all ({level})",
                    duringBoost < 0.001f && Mathf.Abs(poison - expected) <= 0.026f && local.refs.movement.movementModifier - walk < 0.01f,
                    $"{duringBoost * 100f:0.#} poison during the boost, {poison * 100f:0.#} after it (target {expected * 100f:0.#}; the bar fills in steps of 2.5, the rest carries over), speed back to normal {local.refs.movement.movementModifier - walk < 0.01f}");
                Afflictions.SetStatus(Status.Poison, 0f);
            }
        }
        finally
        {
            worseRule.Value = worseSaved;
            HostOptions.Invalidate();
            Protect(true);
            undo();
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// Breaking free (9): caught (a web of 40, as a spider's from ascent 17, put on the way the game puts it on; then a
    /// fly trap's grip of 45) with poison filling the rest of the bar and no stamina left, the jump button is pressed
    /// every 0.15 s (faster than a jump can come). Every jump counts without stamina, one as soon as the last allows
    /// (about every 0.4 s), until the scout is free.
    /// </summary>
    private IEnumerator StrugglingCheck()
    {
        if (!Rules.InExtraAscent)
        {
            Info("the breaking-free checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Protect(false);
        try
        {
            foreach ((Status type, float amount, string name) in new[] { (Status.Web, 0.4f, "a spider's web of 40"), (Status.FlyTrap, 0.45f, "a fly trap's grip of 45") })
            {
                ClearStatuses(Afflictions);
                Afflictions.AddStatus(type, amount);
                // Poison fills the bar to 90 with what is already there (the weight carried): at 24 a full bar is death.
                yield return null;
                Afflictions.SetStatus(Status.Poison, Mathf.Max(0f, 0.9f - Afflictions.statusSum));
                yield return new WaitForSeconds(0.3f);
                int jumpsBefore = Struggling.Jumps;
                List<float> jumpTimes = new List<float>();
                int presses = 0;
                float start = Time.time;
                float nextPress = start;
                bool staminaLeft = false;
                while (Afflictions.GetCurrentStatus(type) > 0.001f && Time.time < start + 15f)
                {
                    local.data.currentStamina = 0f;
                    local.data.extraStamina = 0f;
                    staminaLeft |= !local.OutOfStamina();
                    if (Time.time >= nextPress)
                    {
                        nextPress += 0.15f;
                        presses++;
                        local.refs.movement.TryToJump();
                    }
                    while (jumpTimes.Count < Struggling.Jumps - jumpsBefore)
                    {
                        jumpTimes.Add(Time.time - start);
                    }
                    yield return null;
                }
                float freedAt = Time.time - start;
                bool free = Afflictions.GetCurrentStatus(type) <= 0.001f;
                List<float> gaps = jumpTimes.Zip(jumpTimes.Skip(1), (a, b) => b - a).ToList();
                float longestGap = gaps.DefaultIfEmpty(0f).Max();
                Check($"{name}: with no stamina, the presses count (a jump as soon as the last allows) until the scout is free (9)",
                    free && jumpTimes.Count > 0 && longestGap < 0.5f && !staminaLeft,
                    $"free {free} after {freedAt:0.0} s: {jumpTimes.Count} jumps from {presses} presses, {gaps.DefaultIfEmpty(0f).Average():0.00} s apart (longest {longestGap:0.00} s), stamina left {staminaLeft}");
                ClearStatuses(Afflictions);
                yield return new WaitForSeconds(0.5f);
            }
        }
        finally
        {
            ClearStatuses(Afflictions);
            Protect(true);
        }
    }

    /// <summary>
    /// Ascent 15's numbers (on in memory): Fortified Milk drunk makes the scout invincible for 12 s, cooked (the game's
    /// cooking) 20 s; Scout's Tenacity used gives 8 s; a Weird Shroom eaten restores 2.5 hunger (half its 5), cooked 5.
    /// </summary>
    private IEnumerator MilkTenacityShroom()
    {
        Item milk = ItemByName("FortifiedMilk");
        Item gem = ItemByName("Amulet_Healing");
        Item shroom = ItemByName(WildFoods.WeirdShroomPrefab);
        if (milk == null || gem == null || shroom == null)
        {
            Fail("Fortified Milk, Scout's Tenacity and the Weird Shroom are in the item database", $"milk {milk != null}, gem {gem != null}, shroom {shroom != null}");
            yield break;
        }
        Character local = Local;
        ConfigEntry<bool> worseRule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool worseSaved = worseRule.Value;
        worseRule.Value = true;
        HostOptions.Invalidate();
        Action nausea = NoNausea();
        Protect(false);
        try
        {
            Peak.Afflictions.Affliction_Invincibility Shield() => Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_Invincibility>().FirstOrDefault();
            foreach (bool cooked in new[] { false, true })
            {
                yield return SpawnIntoHand(milk);
                if (held == null)
                {
                    continue;
                }
                if (cooked)
                {
                    held.cooking.FinishCooking();
                    yield return new WaitForSeconds(0.5f);
                }
                if (Shield() != null)
                {
                    Afflictions.RemoveAffliction(Shield().GetAfflictionType());
                }
                yield return EatHeld();
                Peak.Afflictions.Affliction_Invincibility drunk = Shield();
                float expected = cooked ? A14_Items.MilkSecondsCooked : A14_Items.MilkSeconds;
                Check(cooked ? "cooked Fortified Milk makes you invincible for 20 s (15; the game: 25 s)" : "Fortified Milk makes you invincible for 12 s (15; the game: 15 s)",
                    drunk != null && Mathf.Abs(drunk.totalTime - expected) < 0.01f,
                    drunk == null ? "no invincibility" : $"invincible for {drunk.totalTime:0.#} s");
                if (drunk != null)
                {
                    Afflictions.RemoveAffliction(drunk.GetAfflictionType());
                }
            }
            yield return SpawnIntoHand(gem);
            if (held != null)
            {
                Afflictions.SetStatus(Status.Injury, 0.2f);
                yield return EatHeld();
                Peak.Afflictions.Affliction_Invincibility given = Shield();
                Check("Scout's Tenacity makes you invincible for 8 s (15; the game: 10 s)", given != null && Mathf.Abs(given.totalTime - A14_Gems.TenacitySeconds) < 0.01f,
                    given == null ? "no invincibility" : $"invincible for {given.totalTime:0.#} s");
                if (given != null)
                {
                    Afflictions.RemoveAffliction(given.GetAfflictionType());
                }
                local.data.SetPetrify(0);
                ClearStatuses(Afflictions);
            }
            foreach (bool cooked in new[] { false, true })
            {
                yield return SpawnIntoHand(shroom);
                if (held == null)
                {
                    continue;
                }
                if (cooked)
                {
                    held.cooking.FinishCooking();
                    yield return new WaitForSeconds(0.5f);
                }
                Afflictions.SetStatus(Status.Hunger, 0.5f);
                yield return null;
                yield return EatHeld();
                float restored = 0.5f - Afflictions.GetCurrentStatus(Status.Hunger);
                float expected = (cooked ? 2f : 1f) * WildFoods.WeirdShroomHunger * 0.5f;
                Check(cooked ? "a cooked Weird Shroom restores 5 hunger (15: half of 10)" : "a Weird Shroom restores 2.5 hunger (15: half of its 5; the game: 10)",
                    Mathf.Abs(restored - expected) < 0.005f, $"{restored * 100f:0.#} hunger restored (expected {expected * 100f:0.#})");
                Afflictions.SetStatus(Status.Hunger, 0f);
            }
        }
        finally
        {
            worseRule.Value = worseSaved;
            HostOptions.Invalidate();
            Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.Glowing);
            Protect(true);
            nausea();
        }
    }

    /// <summary>
    /// The Citadel's spinning traps (17), where they are (a Citadel map, after the jump to its segment): a spike roller
    /// turns 1.5 times as fast as its own speed, by the run's clock, and a swinging spike ball swings 1.5 times as fast.
    /// </summary>
    private IEnumerator SpinningTraps()
    {
        if (Rules.Level < Lvl.Hazards)
        {
            Info("the spinning trap checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Check($"no scorching sun in the {Map.CurrentPlace}: ascent 23's sun stops at the Caldera and the Gloom (23)", !A23_HotSun.ScorchingHere || !(Map.InCitadel || Map.InVolcano || Map.InGloom),
            $"scorching here {A23_HotSun.ScorchingHere}");
        Peak.SpikeRoller roller = FindObjectsByType<Peak.SpikeRoller>(FindObjectsSortMode.None).FirstOrDefault(r => r != null && r.isActiveAndEnabled && r.rb != null);
        SwingingAxe axe = FindObjectsByType<SwingingAxe>(FindObjectsSortMode.None).FirstOrDefault(a => a != null && a.isActiveAndEnabled && a.rb != null);
        // Not reached yet (a check run on its own): to the last part of the climb, the Citadel on a Gloom map.
        if (roller == null && axe == null && Resources.FindObjectsOfTypeAll<Peak.SpikeRoller>().Any(r => r != null && r.gameObject.scene.IsValid())
            && Singleton<MapHandler>.Instance.GetCurrentSegment() < Segment.TheKiln)
        {
            MapHandler.JumpToSegment(Segment.TheKiln);
            yield return new WaitForSeconds(8f);
            roller = FindObjectsByType<Peak.SpikeRoller>(FindObjectsSortMode.None).FirstOrDefault(r => r != null && r.isActiveAndEnabled && r.rb != null);
            axe = FindObjectsByType<SwingingAxe>(FindObjectsSortMode.None).FirstOrDefault(a => a != null && a.isActiveAndEnabled && a.rb != null);
        }
        if (roller == null && axe == null)
        {
            Info("spinning traps skipped", $"none active here (place {Map.CurrentPlace})");
            yield break;
        }
        if (roller != null)
        {
            float t0 = RunManager.Instance.TimeSinceRunStarted;
            float a0 = roller.rb.rotation.eulerAngles.y;
            yield return new WaitForSeconds(0.5f);
            float t1 = RunManager.Instance.TimeSinceRunStarted;
            float a1 = roller.rb.rotation.eulerAngles.y;
            float turned = Mathf.Repeat(a1 - a0, 360f);
            float expected = Mathf.Repeat(roller.spinnerSpeed * A17_Traps.TrapSpeed * (t1 - t0), 360f);
            float game = Mathf.Repeat(roller.spinnerSpeed * (t1 - t0), 360f);
            Check("a spike roller sweeps round 1.5 times as fast (17)", Mathf.Abs(Mathf.DeltaAngle(turned, expected)) < 3f,
                $"'{roller.name}' turned {turned:0.0} deg in {t1 - t0:0.00} s of the run's clock: {turned / (t1 - t0):0.0} deg/s (its speed {roller.spinnerSpeed:0} deg/s x1.5 = {expected:0.0} deg; the game's {game:0.0} deg)");
            if (roller.roller.Length > 0)
            {
                float time = RunManager.Instance.TimeSinceRunStarted;
                Quaternion axle = roller.roller[0].localRotation;
                float offGame = Quaternion.Angle(axle, Quaternion.Euler(time * roller._finalRollerSpeed % 360f, 0f, 0f));
                float offFaster = Quaternion.Angle(axle, Quaternion.Euler(time * 1.5f * roller._finalRollerSpeed % 360f, 0f, 0f));
                Check("its rollers turn on their axles 1.5 times as fast too (17)", offFaster < 5f,
                    $"{offFaster:0.0} deg off 1.5 times the game's pace, {offGame:0.0} deg off the game's pace");
            }
        }
        if (axe != null)
        {
            float worst15 = 0f;
            float worst10 = 0f;
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
                float time = RunManager.Instance.TimeSinceRunStarted;
                float z = Mathf.DeltaAngle(0f, axe.rb.rotation.eulerAngles.z);
                float fast = Mathf.Sin(time * axe.swingSpeed * A17_Traps.TrapSpeed + axe.cycleOffset * Mathf.PI * 2f) * axe.swingAngle;
                float slow = Mathf.Sin(time * axe.swingSpeed + axe.cycleOffset * Mathf.PI * 2f) * axe.swingAngle;
                worst15 = Mathf.Max(worst15, Mathf.Abs(z - fast));
                worst10 = Mathf.Max(worst10, Mathf.Abs(z - slow));
                yield return new WaitForSeconds(0.1f);
            }
            Check("a swinging spike ball swings 1.5 times as fast (17)", worst15 < 4f && worst15 < worst10,
                $"'{axe.name}': off the 1.5x swing by up to {worst15:0.0} deg, off the game's swing by up to {worst10:0.0} deg (swing {axe.swingAngle:0} deg, speed {axe.swingSpeed:0.##})");
        }
        Peak.MovingSawBlade saw = FindObjectsByType<Peak.MovingSawBlade>(FindObjectsSortMode.None).FirstOrDefault(b => b != null && b.isActiveAndEnabled);
        if (saw != null)
        {
            // The game's pace (traced from its path when it starts), as this mod first found it.
            float game = UltimateAscent.Core.Overrides.Original(saw, "pathLength", saw.pathLength);
            float worst = 0f;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                float t = RunManager.Instance.TimeSinceRunStarted + saw.randomOffset;
                float track = (Mathf.Sin(t * game * A17_Traps.TrapSpeed) / 2f + 0.5f) * (1f - saw.buffer) + saw.buffer / 2f;
                worst = Mathf.Max(worst, Mathf.Abs(saw.transform.localPosition.y - Mathf.Lerp(saw.pos1, saw.pos2, track)));
                yield return new WaitForSeconds(0.1f);
            }
            Check("a wall saw runs along its wall 1.5 times as fast (17)", Mathf.Abs(saw.pathLength - game * A17_Traps.TrapSpeed) < 0.0001f && worst < 0.05f,
                $"'{saw.name}': pace {saw.pathLength:0.000} (the game's {game:0.000}), off the 1.5x course by up to {worst:0.000} m");
            float oscillateGame = UltimateAscent.Core.Overrides.Original(saw, "oscillate", saw.sawOscillateSpeed);
            Check("its blade wobbles 1.5 times as fast too (17)", Mathf.Abs(saw.sawOscillateSpeed - oscillateGame * A17_Traps.TrapSpeed) < 0.0001f,
                $"wobble {saw.sawOscillateSpeed:0.##} (the game's {oscillateGame:0.##})");
        }
        Peak.SpikeTrap plate = FindObjectsByType<Peak.SpikeTrap>(FindObjectsSortMode.None).FirstOrDefault(p => p != null && p.isActiveAndEnabled && !p._isAttacking && p.hitbox != null
            && Vector3.Distance(p.transform.position, Local.Center) > 5f);
        if (plate != null)
        {
            // The message its own game sends when a scout steps on it.
            float stepped = Time.time;
            plate.view.RPC("SpikeTrapRPC", RpcTarget.All);
            float hit = -1f;
            for (float until = Time.time + 3f; Time.time < until && hit < 0f;)
            {
                if (plate.hitbox.activeSelf)
                {
                    hit = Time.time - stepped;
                }
                yield return null;
            }
            float gameWait = UltimateAscent.Core.Overrides.Original(plate, "wait", plate.spikeWaitTime);
            Check("a pressure plate's spikes come up 1.5 times as soon after the step (17)", hit > 0f && Mathf.Abs(hit - gameWait / A17_Traps.TrapSpeed) < 0.06f,
                hit < 0f ? "no spikes within 3 s" : $"the spikes hit {hit:0.00} s after the step (the plate's own wait {gameWait:0.00} s / 1.5 = {gameWait / A17_Traps.TrapSpeed:0.00} s; spikes ready after {plate.readyTweenTime:0.00} s)");
        }
    }

    /// <summary>
    /// Scout's Generosity (the game's gem in the hands, aimed and used as a player does) clones a regular item for its
    /// price and the Warp Compass and the Toxic Drink, which come from where the mystical items come from, for a
    /// mystical item's (the game: 25 and 45 petrification; ascent 15's rule: 35 and 60).
    /// </summary>
    private IEnumerator GenerosityCheck()
    {
        Item gem = ItemByName("Amulet_Clone");
        Item food = ItemByName("Airplane Food");
        if (gem == null || food == null || ToxicDrink.Template == null || WarpCompass.Template == null)
        {
            Fail("Scout's Generosity and the items to clone are in the item database",
                $"gem {gem != null}, food {food != null}, Toxic Drink {ToxicDrink.Template != null}, Warp Compass {WarpCompass.Template != null}");
            yield break;
        }
        Character local = Local;
        Vector2 look = local.data.lookValues;
        yield return SpawnIntoHand(gem);
        Item inHand = held;
        Peak.Action_CloneSelectedItem clone = inHand != null ? inHand.GetComponent<Peak.Action_CloneSelectedItem>() : null;
        if (clone == null)
        {
            Fail("Scout's Generosity in the scout's hands");
            yield break;
        }
        local.data.SetPetrify(0);
        try
        {
            foreach ((Item target, bool mystical) in new[] { (food, false), (ToxicDrink.Template, true), (WarpCompass.Template, true) })
            {
                GameObject placed = SpawnInFront(target, 2.5f);
                Item item = placed != null ? placed.GetComponent<Item>() : null;
                if (item == null)
                {
                    Fail("put " + target.gameObject.name + " down to clone");
                    continue;
                }
                yield return new WaitForSeconds(1.2f);
                local.data.lookValues = HelperFunctions.DirectionToLook(item.rig.worldCenterOfMass - MainCamera.instance.transform.position);
                yield return new WaitForSeconds(0.5f);
                Interaction.instance.DoInteractableRaycasts(out IInteractible aimed, clone.range);
                HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
                int petrifyBefore = local.data.petrifyAmount;
                inHand.StartUsePrimary();
                for (float until = Time.realtimeSinceStartup + 5f; Time.realtimeSinceStartup < until && inHand != null && !inHand.finishedCast;)
                {
                    inHand.ContinueUsePrimary();
                    yield return null;
                }
                if (inHand != null)
                {
                    inHand.CancelUsePrimary();
                }
                yield return new WaitForSeconds(0.8f);
                int cost = local.data.petrifyAmount - petrifyBefore;
                List<Item> clones = Item.ALL_ITEMS.Where(i => i != null && !before.Contains(i) && i.itemID == target.itemID).ToList();
                int expected = mystical ? clone.petrifyMystical : clone.petrify;
                string label = Plain(target.GetItemName());
                Check(mystical ? $"cloning the {label} costs a mystical item's petrification" : $"cloning a regular item ({label}) costs the gem's regular price",
                    ReferenceEquals(aimed, item) && clones.Count == 1 && cost == expected,
                    $"aimed at it {ReferenceEquals(aimed, item)}, {clones.Count} clone(s), {cost} petrification (expected {expected}; the gem: {clone.petrify} regular, {clone.petrifyMystical} mystical)");
                local.data.SetPetrify(0);
                foreach (Item made in clones.Append(item))
                {
                    if (made != null && made.photonView.IsMine)
                    {
                        PhotonNetwork.Destroy(made.gameObject);
                    }
                }
                yield return new WaitForSeconds(0.3f);
            }
        }
        finally
        {
            local.data.SetPetrify(0);
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// A banana peel (17): a real peel on the ground is stepped on (the message its own game sends when a scout runs
    /// over it). For 3 s only the slip; then the screen darkens and every sound is muffled the way TFA's Ice Fog
    /// muffles it: the sound effects low-passed down to 900 Hz and 8 dB quieter, the music gone. 20 s later
    /// everything is as before.
    /// </summary>
    private IEnumerator BananaPeelCheck()
    {
        if (Rules.Level < Lvl.Hazards)
        {
            Info("the banana peel check is skipped", $"testing ascent {target}");
            yield break;
        }
        Item peel = ItemByName("Berrynana Peel Yellow");
        UnityEngine.Audio.AudioMixer mixer = SettingsHandler.OriginalMixer;
        if (peel == null || mixer == null)
        {
            Fail("a banana peel and the game's sound mixer", $"peel {peel != null}, mixer {mixer != null}");
            yield break;
        }
        Character local = Local;
        mixer.GetFloat("SFXVolume", out float soundBefore);
        mixer.GetFloat("MusicVolume", out float musicBefore);
        int filtersBefore = FindObjectsByType<AudioLowPassFilter>(FindObjectsSortMode.None).Length;
        GameObject spawned = SpawnInFront(peel, 1f);
        if (spawned == null)
        {
            Fail("put a banana peel on the ground");
            yield break;
        }
        yield return new WaitForSeconds(1f);
        Step("step on the banana peel", () => spawned.GetComponent<PhotonView>().RPC("RPCA_TriggerBanana", RpcTarget.All, local.refs.view.ViewID));
        float slipped = Time.time;
        float darkEarly = 0f;
        int muffledEarly = 0;
        while (Time.time < slipped + 2.8f)
        {
            darkEarly = Mathf.Max(darkEarly, A17_BananaDarkness.Strength);
            muffledEarly = Mathf.Max(muffledEarly, A17_BananaDarkness.MuffledSources);
            yield return null;
        }
        Check("for 3 s after the slip the screen stays bright and nothing is muffled (17)", darkEarly <= 0f && muffledEarly == 0,
            $"darkness up to {darkEarly:0.00}, {muffledEarly} muffled sound sources");
        while (Time.time < slipped + 5.5f)
        {
            yield return null;
        }
        Screenshot("banana-darkness");
        float strength = A17_BananaDarkness.Strength;
        int muffled = A17_BananaDarkness.MuffledSources;
        float cutoff = A17_BananaDarkness.SoundCutoffNow;
        mixer.GetFloat("SFXVolume", out float soundNow);
        mixer.GetFloat("MusicVolume", out float musicNow);
        bool tfaFog = UltimateAscent.Compat.Tfa.IceFogDrawing;
        Check("then the screen darkens and every sound is muffled: sound effects down to 900 Hz and 8 dB quieter, the music gone (17)",
            strength >= 0.99f && muffled > 0 && Mathf.Abs(cutoff - 900f) < 1f && (tfaFog || (Mathf.Abs(soundNow - (soundBefore - 8f)) < 0.1f && musicNow <= -79f)),
            $"darkness {strength:0.00}, {muffled} sound sources low-passed at {cutoff:0} Hz, sound effects {soundBefore:0.#} -> {soundNow:0.#} dB, music {musicBefore:0.#} -> {musicNow:0.#} dB{(tfaFog ? " (TFA's Ice Fog has the volumes)" : "")}");
        while (Time.time < slipped + 3f + 20.5f)
        {
            yield return null;
        }
        mixer.GetFloat("SFXVolume", out float soundAfter);
        mixer.GetFloat("MusicVolume", out float musicAfter);
        int filtersAfter = FindObjectsByType<AudioLowPassFilter>(FindObjectsSortMode.None).Length;
        Check("20 s later everything is as before: no darkness, nothing muffled, the volumes back",
            A17_BananaDarkness.Strength <= 0f && A17_BananaDarkness.MuffledSources == 0 && Mathf.Abs(soundAfter - soundBefore) < 0.01f && Mathf.Abs(musicAfter - musicBefore) < 0.01f && filtersAfter <= filtersBefore,
            $"darkness {A17_BananaDarkness.Strength:0.00}, {A17_BananaDarkness.MuffledSources} muffled, sound effects {soundAfter:0.#} dB (was {soundBefore:0.#}), music {musicAfter:0.#} dB (was {musicBefore:0.#}), low-pass filters {filtersAfter} (were {filtersBefore})");
        if (spawned != null)
        {
            PhotonNetwork.Destroy(spawned);
        }
    }

    /// <summary>
    /// The Torch (9, 15): in the Lantern's suitcases at half its weight, after the Lantern on the Custom Run screen. Held
    /// unlit it cannot swing; lit (the game's own lighting) its second use swings it: 33% stamina, 4 s of its fuel, a
    /// zombie in front knocked down for 2 s without heat. A teammate's burning hit (the knock-down message their client
    /// sends) knocks the scout down for 2 s and burns 10, 20 with ascent 15's rule (on in memory). A swing that uses up the
    /// last fuel puts it out.
    /// </summary>
    private IEnumerator TorchCheck()
    {
        Item torch = TorchItem.Template;
        Item lanternItem = ItemByName(MoreLanterns.LanternPrefab);
        if (torch == null || lanternItem == null)
        {
            Fail("the Torch and the Lantern are in the item database", $"Torch {torch != null}, Lantern {lanternItem != null}");
            yield break;
        }
        if (!Rules.InExtraAscent)
        {
            Info("the Torch checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Step("Torch in the suitcases", () =>
        {
            MoreLanterns.Sync();
            LootData.PopulateLootData();
            List<string> parts = new List<string>();
            bool all = true;
            int pools = 0;
            foreach (KeyValuePair<SpawnPool, Dictionary<ushort, int>> pool in LootData.AllSpawnWeightData)
            {
                bool hasLantern = pool.Value.TryGetValue(lanternItem.itemID, out int lw);
                bool hasTorch = pool.Value.TryGetValue(torch.itemID, out int tw);
                if (!hasLantern && !hasTorch)
                {
                    continue;
                }
                pools++;
                all &= hasLantern && hasTorch && tw * 2 == lw;
                parts.Add($"{pool.Key}: Lantern {lw}, Torch {tw}");
            }
            int listed = RunSettings.ModifiableItemPrefabs.IndexOf(torch);
            int lanternAt = RunSettings.ModifiableItemPrefabs.IndexOf(lanternItem);
            Check("the Torch turns up wherever the Lantern does, at half its weight, and spawns in this run (9)",
                all && pools >= 7 && torch.IsValidToSpawn(), string.Join("; ", parts) + $"; valid to spawn {torch.IsValidToSpawn()}");
            Check("on the Custom Run screen the Torch comes right after the Lantern", listed > 0 && listed == lanternAt + 1, $"Torch at {listed}, Lantern at {lanternAt}");
        });

        Character local = Local;
        ConfigEntry<bool> worseRule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool worseSaved = worseRule.Value;
        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        Action undo = CalmForMeasuring();
        GameObject zombie = null;
        try
        {
            yield return SpawnIntoHand(torch);
            if (held == null)
            {
                yield break;
            }
            Item inHand = held;
            Lantern flame = inHand.GetComponent<Lantern>();
            local.data.lookValues = new Vector2(Quaternion.LookRotation(forward).eulerAngles.y, -5f);
            yield return new WaitForSeconds(0.8f);
            int swings = TorchItem.Swings;
            bool unlitCan = inHand.CanUseSecondary();
            inHand.StartUseSecondary();
            inHand.CancelUseSecondary();
            Check("an unlit Torch cannot swing (no second use, no swing)", !unlitCan && TorchItem.Swings == swings, $"second use offered {unlitCan}, swings {TorchItem.Swings - swings}");

            // The game's own lighting: the use button held until the cast is done.
            inHand.StartUsePrimary();
            for (float until = Time.realtimeSinceStartup + 3f; Time.realtimeSinceStartup < until && inHand != null && !inHand.finishedCast;)
            {
                inHand.ContinueUsePrimary();
                yield return null;
            }
            inHand.CancelUsePrimary();
            yield return new WaitForSeconds(1f);
            string prompt = LocalizedText.GetText(inHand.UIData.secondaryInteractPrompt);
            Check("lit, the Torch's second use is to swing it", flame != null && flame.lit && inHand.CanUseSecondary() && Plain(prompt).ToLowerInvariant() == "swing",
                $"lit {flame != null && flame.lit}, second use offered {inHand.CanUseSecondary()}, prompt '{prompt}'");
            Screenshot("torch-lit-held");

            // Closer than the Stick's zombie: lying down it can settle a metre further, past the 2.5 m reach.
            Vector3 spot = local.Center + forward * 1.2f;
            if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f, HelperFunctions.terrainMapMask))
            {
                spot = ground.point + Vector3.up * 0.9f;
            }
            zombie = PhotonNetwork.Instantiate("MushroomZombie_Player", spot, Quaternion.LookRotation(-forward), 0);
            MushroomZombie walker = zombie != null ? zombie.GetComponent<MushroomZombie>() : null;
            yield return new WaitForSeconds(1.5f);
            if (walker != null && ZombieManager.Instance != null && !ZombieManager.Instance.zombies.Contains(walker))
            {
                ZombieManager.Instance.RegisterZombie(walker);
            }
            Character targetZombie = walker != null ? walker.character : null;
            float zombieHeat = targetZombie != null ? targetZombie.refs.afflictions.GetCurrentStatus(Status.Hot) : 0f;
            // Aimed at the zombie where it lies (on a slope it can settle lower or closer than straight ahead).
            if (targetZombie != null)
            {
                Vector3 to = targetZombie.Center - local.Center;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.sqrMagnitude > 0.01f)
                {
                    local.data.lookValues = new Vector2(Quaternion.LookRotation(flat).eulerAngles.y, Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
                    yield return new WaitForSeconds(0.2f);
                }
            }
            local.data.currentStamina = 1f;
            float staminaBefore = local.data.currentStamina;
            float fuelBefore = flame.fuel;
            float swungAt = Time.time;
            swings = TorchItem.Swings;
            FallRecorder.Clear();
            FallRecorder.Of = targetZombie;
            Step("swing the lit Torch (second use)", () => inHand.StartUseSecondary());
            float cost = staminaBefore - local.data.currentStamina;
            float fuelCost = fuelBefore - flame.fuel;
            Check("a swing costs 33% stamina and burns 4 s of the Torch's fuel (a full Torch: 10 swings)",
                TorchItem.Swings == swings + 1 && Mathf.Abs(cost - StickItem.SwingStamina) < 0.003f && Mathf.Abs(fuelCost - TorchItem.SwingFuel) < 0.1f,
                $"{TorchItem.Swings - swings} swing, {cost * 100f:0.#}% stamina, fuel {fuelBefore:0.0} -> {flame.fuel:0.0} s");
            yield return new WaitForSeconds(StickItem.Swing[1].at + 0.02f);
            Screenshot("torch-swing-raised");
            yield return new WaitForSeconds(Mathf.Max(0f, StickItem.StrikeDelay + 0.08f - (Time.time - swungAt)));
            float longest = 0f;
            for (float until = Time.time + 0.6f; Time.time < until;)
            {
                if (targetZombie != null)
                {
                    longest = Mathf.Max(longest, targetZombie.data.fallSeconds);
                }
                yield return null;
            }
            inHand.CancelUseSecondary();
            FallRecorder.Of = null;
            bool hit = targetZombie != null && StickItem.LastHits.Contains(targetZombie);
            float sent = FallRecorder.Seconds.Count > 0 ? FallRecorder.Seconds[0] : 0f;
            float zombieBurn = targetZombie != null ? targetZombie.refs.afflictions.GetCurrentStatus(Status.Hot) - zombieHeat : 0f;
            Check("the swing knocks down the zombie in front for 2 s, without heat (a plain knock-down, no burning mark)",
                hit && Mathf.Abs(sent - TorchItem.Knockdown) < 0.0001f && longest >= TorchItem.Knockdown - 0.7f && zombieBurn < 0.001f,
                targetZombie == null ? "no zombie" : $"hit {hit}, knock-down sent {sent:0.0000} s (down for {longest:0.00} s right after the hit, still from rising), +{zombieBurn * 100f:0.#} heat; "
                    + $"zombie {Vector3.Distance(local.Center, targetZombie.Center):0.00} m away, {Vector3.Angle(local.data.lookDirection, targetZombie.Center - local.Center):0} deg off the look, among the zombies {ZombieManager.Instance != null && ZombieManager.Instance.zombies.Contains(walker)}");
            yield return new WaitForSeconds(1f);

            // A teammate's burning hit on this scout: the knock-down message their client sends.
            Protect(false);
            foreach (bool worse in new[] { false, true })
            {
                worseRule.Value = worse;
                HostOptions.Invalidate();
                yield return new WaitForSeconds(2.5f);
                Afflictions.SetStatus(Status.Hot, 0f);
                Afflictions.SetStatus(Status.Cold, 0f);
                int burns = TorchItem.Burns;
                float heatBefore = Afflictions.GetCurrentStatus(Status.Hot);
                local.Fall(TorchItem.KnockdownMarked);
                yield return null;
                float down = local.data.fallSeconds;
                float burned = Afflictions.GetCurrentStatus(Status.Hot) - heatBefore;
                float expected = TorchItem.HeatNow;
                Check(worse ? "a teammate's burning hit burns 20 with ascent 15's rule (15), and knocks down for 2 s" : "a teammate's burning hit burns 10 and knocks down for 2 s (9)",
                    TorchItem.Burns == burns + 1 && Mathf.Abs(burned - expected) < 0.001f && Mathf.Abs(down - TorchItem.Knockdown) < 0.05f,
                    $"+{burned * 100f:0.#} heat (expected {expected * 100f:0.#}), knocked down {down:0.00} s");
            }
            worseRule.Value = worseSaved;
            HostOptions.Invalidate();
            Afflictions.SetStatus(Status.Hot, 0f);
            Protect(true);
            yield return new WaitForSeconds(2.5f);

            // The last of the fuel: the swing uses it up and the Torch goes out.
            local.data.currentStamina = 1f;
            flame.fuel = 2f;
            swings = TorchItem.Swings;
            inHand.StartUseSecondary();
            inHand.CancelUseSecondary();
            yield return new WaitForSeconds(0.5f);
            Check("a swing with less than 4 s of fuel left uses it up, and the Torch goes out and cannot swing",
                TorchItem.Swings == swings + 1 && flame.fuel <= 0f && !flame.lit && !inHand.CanUseSecondary(),
                $"swings {TorchItem.Swings - swings}, fuel {flame.fuel:0.0} s, lit {flame.lit}, second use offered {inHand.CanUseSecondary()}");
            yield return new WaitForSeconds(StickItem.SwingSeconds);
            yield return PutAway(torch);
        }
        finally
        {
            worseRule.Value = worseSaved;
            HostOptions.Invalidate();
            Protect(true);
            undo();
            if (zombie != null && zombie.GetPhotonView() != null)
            {
                PhotonNetwork.Destroy(zombie);
            }
            local.data.lookValues = look;
        }
    }

    /// <summary>The Lantern in the suitcases of every part of the climb up to the Caldera and the Gloom, at its own rarity (9): picks from each pool.</summary>
    private IEnumerator LanternPoolsCheck()
    {
        Item lantern = ItemByName(MoreLanterns.LanternPrefab);
        if (lantern == null)
        {
            Fail("the Lantern is in the item database");
            yield break;
        }
        if (!Rules.InExtraAscent)
        {
            Info("the Lantern spawn checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Step("Lanterns in every part's suitcases", () =>
        {
            MoreLanterns.Sync();
            int epic = LootData.RarityWeights[Rarity.Epic];
            List<string> parts = new List<string>();
            bool all = true;
            foreach (SpawnPool pool in new[] { SpawnPool.LuggageBeach, SpawnPool.LuggageJungle, SpawnPool.LuggageRoots, SpawnPool.LuggageTundra, SpawnPool.LuggageMesa, SpawnPool.LuggageCaldera, SpawnPool.LuggageGloom })
            {
                Dictionary<ushort, int> weights = LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> found) ? found : null;
                int lanternWeight = weights != null && weights.TryGetValue(lantern.itemID, out int lw) ? lw : 0;
                int total = weights != null ? weights.Values.Sum() : 0;
                int picks = 0;
                for (int i = 0; i < 2000; i++)
                {
                    GameObject pick = LootData.GetRandomItem(pool);
                    if (pick != null && pick.name == MoreLanterns.LanternPrefab)
                    {
                        picks++;
                    }
                }
                all &= lanternWeight == epic && picks > 0;
                parts.Add($"{pool}: weight {lanternWeight} of {total}, {picks} of 2000 picks");
            }
            bool notCitadel = !(LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageCitadel, out Dictionary<ushort, int> citadel) && citadel.ContainsKey(lantern.itemID));
            Check("the Lantern turns up in the suitcases of every part up to the Caldera and the Gloom, at its own rarity (Epic), and not in the Citadel's (9)",
                all && notCitadel, string.Join("; ", parts) + $"; in the Citadel's {!notCitadel}");
        });
        yield break;
    }

    /// <summary>
    /// With TFA's Apocalypse at ascent 25: during one of TFA's weather events (its own rain, started here) ascent 25's
    /// storms rest and TFA's weather is alone; in the break after it (held for 3 s, TFA's next event cleared if it comes
    /// at once) they run, and TFA's hush of the game's storms stands aside.
    /// </summary>
    private IEnumerator ApocalypseBreaks()
    {
        if (Rules.Level < Lvl.AllWeather || !Compat.Tfa.ApocalypseActive)
        {
            Info("the Apocalypse weather-break checks are skipped", $"testing ascent {target}, Apocalypse {Compat.Tfa.ApocalypseActive}");
            yield break;
        }
        Type weather = AccessTools.TypeByName("TrueFinalAscent.FinalAscentWeather");
        MethodInfo start = weather != null ? AccessTools.Method(weather, "Start", new[] { typeof(string), typeof(float) }) : null;
        MethodInfo stop = weather != null ? AccessTools.Method(weather, "Stop") : null;
        PropertyInfo hushed = AccessTools.Property(AccessTools.TypeByName("TrueFinalAscent.FinalAscentWindStormQuiet"), "Hushed");
        if (start == null || stop == null || hushed == null)
        {
            Fail("TFA's weather can be started and stopped for the check", $"start {start != null}, stop {stop != null}, hush {hushed != null}");
            yield break;
        }
        bool Hushed() => (bool)hushed.GetValue(null);
        WindChillZone Snow() => FindObjectsByType<WindChillZone>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(z => z != null && z.name == "SnowStorm");
        try
        {
            for (float until = Time.time + 40f; Time.time < until && UltimateAscent.Features.Weather.WeatherGrace.Blocking;)
            {
                yield return null;
            }
            start.Invoke(null, new object[] { "rain", 60f });
            yield return new WaitForSeconds(4f);
            WindChillZone snow = Snow();
            Check("during one of TFA's weather events ascent 25's storms rest and TFA's weather is alone (25)",
                Compat.Tfa.WeatherRunning && !A25_AllWeather.DrivesVanillaWeather && Hushed() && (snow == null || !snow.windActive),
                $"TFA weather '{Compat.Tfa.Weather}', ours driven {A25_AllWeather.DrivesVanillaWeather}, TFA's hush {Hushed()}, snowstorm {(snow != null ? (snow.windActive ? "blowing" : "calm") : "none")}");
            // A break: TFA's own cycle may start its next event at once; it is cleared until a break holds for 3 s.
            stop.Invoke(null, null);
            float calmSince = -1f;
            for (float until = Time.time + 30f; Time.time < until;)
            {
                if (Compat.Tfa.WeatherRunning)
                {
                    stop.Invoke(null, null);
                    calmSince = -1f;
                }
                else if (calmSince < 0f)
                {
                    calmSince = Time.time;
                }
                else if (Time.time - calmSince >= 3f)
                {
                    break;
                }
                yield return null;
            }
            snow = Snow();
            Check("between TFA's weather events ascent 25's storms run, and TFA's hush of the game's storms stands aside (25)",
                !Compat.Tfa.WeatherRunning && A25_AllWeather.DrivesVanillaWeather && A25_AllWeather.RunsInApocalypseBreak && !Hushed() && snow != null && UltimateAscent.Features.Weather.WeatherPhases.Handles(snow),
                $"TFA weather '{Compat.Tfa.Weather}', ours driven {A25_AllWeather.DrivesVanillaWeather}, TFA's hush {Hushed()}, snowstorm {(snow != null ? (snow.windActive ? "blowing" : "calm") : "none")}");
        }
        finally
        {
            stop.Invoke(null, null);
        }
    }

    /// <summary>
    /// A cooked Energy Drink (cooked once with the game's own cooking, then drunk) at ascent 17: its burst is still an
    /// explosion there (the drinker is knocked down three times as long as the game's at most 1 s, and pushed harder),
    /// but it knocks nobody out: no drowsiness when it goes off, and the drinker stays conscious.
    /// </summary>
    private IEnumerator EnergyDrinkBurstCheck()
    {
        Item energy = ItemByName(ToxicDrink.EnergyDrinkPrefab);
        if (energy == null)
        {
            Fail("the Energy Drink is in the item database");
            yield break;
        }
        if (!Rules.Enabled(Lvl.Hazards))
        {
            Info("the Energy Drink burst check is skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        int Bursts() => FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith(A17_Hazards.EnergyDrinkBurstName, StringComparison.Ordinal));
        Action undo = CalmForMeasuring();
        Protect(false);
        try
        {
            yield return SpawnIntoHand(energy);
            if (held == null)
            {
                yield break;
            }
            held.GetComponent<ItemCooking>().FinishCooking();
            yield return new WaitForSeconds(0.5f);
            Afflictions.SetStatus(Status.Drowsy, 0f);
            yield return null;
            float drowsyBefore = Afflictions.GetCurrentStatus(Status.Drowsy);
            int bursts = Bursts();
            int burstsAfter = bursts;
            FallRecorder.Clear();
            FallRecorder.Of = local;
            try
            {
                yield return EatHeld();
                burstsAfter = Bursts();
                for (float until = Time.time + 2f; Time.time < until && FallRecorder.Seconds.Count == 0;)
                {
                    yield return null;
                }
            }
            finally
            {
                FallRecorder.Of = null;
            }
            float seconds = FallRecorder.Seconds.Count > 0 ? FallRecorder.Seconds[0] : 0f;
            float drowsyAtBurst = FallRecorder.Drowsiness.Count > 0 ? FallRecorder.Drowsiness[0] : -1f;
            Check("a cooked Energy Drink's burst is still an explosion at ascent 17: the drinker is knocked down three times as long (the game: at most 1 s)",
                burstsAfter > bursts && seconds > 1.05f && seconds <= 3.01f,
                $"{burstsAfter - bursts} burst(s) of its own, knocked down {seconds:0.00} s");
            yield return new WaitForSeconds(4f);
            float drowsyAfter = Afflictions.GetCurrentStatus(Status.Drowsy);
            Check("but it knocks nobody out: no drowsiness when it goes off, and the drinker stays conscious (17)",
                drowsyAtBurst >= 0f && drowsyAtBurst <= drowsyBefore + 0.001f && !local.data.passedOut && !local.data.fullyPassedOut && !local.data.dead,
                $"drowsiness {drowsyBefore * 100f:0.#} before, {drowsyAtBurst * 100f:0.#} as it went off, {drowsyAfter * 100f:0.#} 4 s later; passed out {local.data.passedOut}, dead {local.data.dead}");
        }
        finally
        {
            Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.FasterBoi);
            Afflictions.SetStatus(Status.Drowsy, 0f);
            Protect(true);
            undo();
        }
    }

    /// <summary>
    /// Takes the scout (the game's own warp) to open, dry, level ground within <paramref name="reach"/> metres that the
    /// sun shines on, when it stands in shade.
    /// </summary>
    private IEnumerator IntoSunlight(float reach)
    {
        Character local = Local;
        DayNightManager clock = DayNightManager.instance;
        if (InSunlight(local) || clock == null || clock.sun == null)
        {
            yield break;
        }
        Vector3 toSun = -clock.sun.transform.forward;
        Vector3 from = local.Center;
        Vector3? spot = null;
        for (int ring = 1; ring <= 8 && spot == null; ring++)
        {
            int steps = ring * 8;
            for (int k = 0; k < steps && spot == null; k++)
            {
                Vector3 top = from + Quaternion.Euler(0f, k * 360f / steps, 0f) * Vector3.forward * (reach * ring / 8f) + Vector3.up * 30f;
                if (Physics.Raycast(top, Vector3.down, out RaycastHit ground, 60f, HelperFunctions.terrainMapMask)
                    && Vector3.Angle(ground.normal, Vector3.up) <= 25f && Mathf.Abs(ground.point.y - from.y) < 6f && !GroundItems.InWater(ground.point)
                    && HelperFunctions.LineCheck(ground.point + Vector3.up * 1.2f + toSun * 1000f, ground.point + Vector3.up * 1.2f, HelperFunctions.LayerType.AllPhysical).transform == null)
                {
                    spot = ground.point;
                }
            }
        }
        if (spot == null)
        {
            yield break;
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot.Value + Vector3.up * 1.2f, false);
        yield return new WaitForSeconds(3f);
    }

    /// <summary>A test snowstorm's strength: ascent 25's gentler snowstorm outside the Alpine, 1 a second at full exposure (before ascent 14's boost).</summary>
    private const float TestStormPerSecond = 0.01f;

    /// <summary>
    /// A snowstorm of the game's own kind (a "SnowStorm" WindChillZone) around the scout, switched on with the game's own
    /// toggle and left to run by itself. It pushes nobody (no wind force); the caller destroys it.
    /// </summary>
    private static WindChillZone TestSnowstorm(Vector3 at)
    {
        GameObject holder = new GameObject("SnowStorm");
        holder.SetActive(false);
        holder.transform.position = at;
        WindChillZone zone = holder.AddComponent<WindChillZone>();
        zone.statusType = Status.Cold;
        zone.statusApplicationPerSecond = TestStormPerSecond;
        zone.windForce = 0f;
        zone.windIntensityCurve = AnimationCurve.Constant(0f, 1f, 1f);
        zone.windZoneBounds = new Bounds(at, Vector3.one * 400f);
        holder.SetActive(true);
        zone.RPCA_ToggleWind(true, Vector3.right, 100000f);
        return zone;
    }

    /// <summary>
    /// Shelter from a snowstorm and a wind storm (on every ascent): on a flat test floor high above the map (open low ground with
    /// nothing natural within reach), a test snowstorm blowing along +X. In the open the scout is as exposed as
    /// the game says; a wall on the storm's side shelters fully, the same wall downwind does not; a low bank and
    /// a long gentle slope rising toward the storm give no shelter; a hill 30 m away on the storm's side shelters
    /// partly, and not once the wind turns around. Then, for the record, how the real ground around the
    /// scout's start would shelter from either wind; and items lying in a wind storm (see <see cref="WindBackpacks"/>).
    /// </summary>
    private IEnumerator SnowstormShelterCheck()
    {
        if (Rules.Level >= Lvl.AllWeather)
        {
            Info("the snowstorm shelter checks are skipped", "at ascent 25 a test storm would follow ascent 25's own snowstorm; the checks run on the other ascents");
            yield break;
        }
        Character local = Local;
        Vector3 start = local.Center;
        foreach ((string name, Vector3 wind) in new[] { ("+X", Vector3.right), ("-X", Vector3.left) })
        {
            Info($"the real ground at the start, wind along {name}", $"open {SnowstormShelter.Openness(local.Head, wind):0.00}");
        }
        Action undo = CalmForMeasuring();
        // As on vanilla ascent 2 (in memory, for these checks): the shelter works on every ascent.
        int climbedAscent = Ascents._currentAscent;
        Ascents._currentAscent = 2;
        Info("the shelter checks run as on ascent 2", $"in memory (the climb's ascent {climbedAscent}); ascents 9-25 rules off: {!Rules.InExtraAscent}");
        Vector3 floorAt = new Vector3(start.x, 1600f, start.z);
        List<GameObject> made = new List<GameObject>();
        GameObject Block(string name, Vector3 centre, Vector3 size, Quaternion turn)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "UltimateAscentTest" + name;
            block.layer = LayerMask.NameToLayer("Map");
            block.transform.SetPositionAndRotation(centre, turn);
            block.transform.localScale = size;
            made.Add(block);
            return block;
        }
        Block("Floor", floorAt + Vector3.down * 0.5f, new Vector3(160f, 1f, 160f), Quaternion.identity);
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, floorAt + Vector3.up * 1.2f, false);
        yield return new WaitForSeconds(3f);
        WindChillZone snow = TestSnowstorm(local.Center);
        try
        {
            yield return new WaitForSeconds(1f);
            float game = SnowstormShelter.LastGame;
            Check("in the open the snowstorm reaches the scout as fully as the game says (every ascent)",
                snow.windPlayerFactor > 0f && SnowstormShelter.LastOpen >= 0.999f && Mathf.Abs(snow.windPlayerFactor - game) < 0.001f,
                $"exposure {snow.windPlayerFactor:0.00} (the game's {game:0.00}), land open {SnowstormShelter.LastOpen:0.00}, scout at {local.Center.y:0.0}");

            IEnumerator Case(string check, GameObject block, float lowest, float highest)
            {
                yield return new WaitForSeconds(0.6f);
                float exposure = snow.windPlayerFactor;
                Check(check, exposure >= lowest - 0.001f && exposure <= highest + 0.001f,
                    $"exposure {exposure:0.00} (the game's {SnowstormShelter.LastGame:0.00}), land open {SnowstormShelter.LastOpen:0.00}");
                made.Remove(block);
                Destroy(block);
                yield return new WaitForSeconds(0.4f);
            }

            Vector3 feet = floorAt;
            yield return Case("a wall on the storm's side shelters fully (every ascent)",
                Block("Wall", feet + new Vector3(-4f, 4f, 0f), new Vector3(1f, 8f, 20f), Quaternion.identity), 0f, 0f);
            yield return Case("the same wall downwind gives no shelter (every ascent)",
                Block("Wall", feet + new Vector3(4f, 4f, 0f), new Vector3(1f, 8f, 20f), Quaternion.identity), game, game);
            yield return Case("a low bank on the storm's side gives no shelter (every ascent)",
                Block("Bank", feet + new Vector3(-6f, 0.75f, 0f), new Vector3(1f, 1.5f, 20f), Quaternion.identity), game, game);
            // 70 m of ground rising 8 degrees toward the storm, from the scout's feet.
            Quaternion rise = Quaternion.AngleAxis(-8f, Vector3.forward);
            yield return Case("a long gentle slope rising toward the storm gives no shelter (every ascent)",
                Block("Slope", feet + rise * new Vector3(-35f, -0.5f, 0f), new Vector3(70f, 1f, 40f), rise), game, game);
            // A hill 15 m high, 30 m away: seen about 24 degrees up from the head.
            GameObject hill = Block("Hill", feet + new Vector3(-32f, 7.5f, 0f), new Vector3(4f, 15f, 60f), Quaternion.identity);
            yield return new WaitForSeconds(0.6f);
            float partly = snow.windPlayerFactor;
            Check("a hill 30 m away on the storm's side shelters partly (every ascent)", partly > 0.2f * game && partly < 0.8f * game,
                $"exposure {partly:0.00} (the game's {SnowstormShelter.LastGame:0.00}), land open {SnowstormShelter.LastOpen:0.00}");
            snow.RPCA_ToggleWind(true, Vector3.left, 100000f);
            yield return Case("with the wind turned around the same hill gives no shelter (every ascent)", hill, game, game);

            // The same storm as a wind storm: the land on its side shelters from it the same way.
            snow.RPCA_ToggleWind(true, Vector3.right, 100000f);
            snow.name = "WindStorm";
            yield return new WaitForSeconds(0.8f);
            float windGame = SnowstormShelter.LastGame;
            Check("a wind storm in the open reaches the scout as fully as the game says (every ascent)",
                snow.windPlayerFactor > 0f && SnowstormShelter.LastOpen >= 0.999f && Mathf.Abs(snow.windPlayerFactor - windGame) < 0.001f,
                $"exposure {snow.windPlayerFactor:0.00} (the game's {windGame:0.00}), land open {SnowstormShelter.LastOpen:0.00}");
            yield return Case("a wall on the wind's side shelters from a wind storm fully (every ascent)",
                Block("Wall", feet + new Vector3(-4f, 4f, 0f), new Vector3(1f, 8f, 20f), Quaternion.identity), 0f, 0f);
            yield return Case("the same wall downwind gives no shelter from a wind storm (every ascent)",
                Block("Wall", feet + new Vector3(4f, 4f, 0f), new Vector3(1f, 8f, 20f), Quaternion.identity), windGame, windGame);
            yield return Case("a low bank on the wind's side gives no shelter from a wind storm (every ascent)",
                Block("Bank", feet + new Vector3(-6f, 0.75f, 0f), new Vector3(1f, 1.5f, 20f), Quaternion.identity), windGame, windGame);

        }
        finally
        {
            Destroy(snow.gameObject);
            foreach (GameObject block in made)
            {
                Destroy(block);
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, start + Vector3.up * 1.5f, false);
            Ascents._currentAscent = climbedAscent;
            undo();
        }
        yield return new WaitForSeconds(3f);
        yield return WindBackpacks(local);
    }

    /// <summary>
    /// Items in a wind storm (every ascent), on the real ground where the scout stands (the game's own measure of the open
    /// sky works there, not high above the map): a test wind storm blowing along +X, hard enough to move items (as the
    /// game's own wind storm does). A backpack lying in the open (on a flat test slab, so it does not roll) gets the storm's
    /// push at full strength; behind a wall on the wind's side none, as a scout there. The scout watches from behind a wall
    /// of its own.
    /// </summary>
    private IEnumerator WindBackpacks(Character local)
    {
        Item bagPrefab = ItemByName("Backpack");
        if (bagPrefab == null)
        {
            Fail("the Backpack is in the item database");
            yield break;
        }
        Action undo = CalmForMeasuring();
        List<GameObject> made = new List<GameObject>();
        GameObject Block(string name, Vector3 centre, Vector3 size)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "UltimateAscentTest" + name;
            block.layer = LayerMask.NameToLayer("Map");
            block.transform.SetPositionAndRotation(centre, Quaternion.identity);
            block.transform.localScale = size;
            made.Add(block);
            return block;
        }
        Vector3 feet = GroundUnder(local.Center);
        Block("Wall", feet + new Vector3(-3f, 2.5f, 0f), new Vector3(0.6f, 5f, 4f));
        Vector3 bagGround = GroundUnder(feet + new Vector3(0f, 0f, 10f));
        float slabTop = bagGround.y + 0.3f;
        Block("Slab", new Vector3(bagGround.x + 4f, slabTop - 0.5f, bagGround.z), new Vector3(14f, 1f, 4f));
        Vector3 at = new Vector3(bagGround.x, slabTop, bagGround.z);
        Physics.SyncTransforms();
        WindChillZone wind = TestSnowstorm(local.Center);
        wind.name = "WindStorm";
        wind.windForce = 20f;
        wind.windItemFactor = 1f;
        wind.useIntensityCurve = false;
        try
        {
            float[] moved = new float[2];
            float[] exposure = new float[2];
            bool[] loose = new bool[2];
            int[] pushes = new int[2];
            float[] pushedAt = new float[2];
            int sheltered = SnowstormShelter.ItemsSheltered;
            for (int i = 0; i < 2; i++)
            {
                GameObject cover = i == 1 ? Block("Wall", at + new Vector3(-3f, 2.5f, 0f), new Vector3(0.6f, 5f, 6f)) : null;
                Physics.SyncTransforms();
                Item bag = PhotonNetwork.InstantiateItemRoom(bagPrefab.gameObject.name, at + Vector3.up * 0.5f, Quaternion.identity).GetComponent<Item>();
                // Loose, as a backpack put down is (one spawned lies still until something moves it, and the game's wind
                // only moves items that have been about lately).
                bag.SetKinematicNetworked(false);
                yield return new WaitForSeconds(1.5f);
                Vector3 from = bag.transform.position;
                exposure[i] = SnowstormShelter.ItemExposure(wind, bag);
                loose[i] = !bag.rig.isKinematic && Item.ALL_ACTIVE_ITEMS.Contains(bag);
                LookAtPoint(local, from + new Vector3(3f, 0f, 0f));
                SnowstormShelter.Watched = bag;
                SnowstormShelter.WatchedPushes = 0;
                float fastest = 0f;
                wind.windMovesItems = true;
                bool shot = false;
                for (float since = 0f; since < 3f; since += Time.deltaTime)
                {
                    yield return null;
                    fastest = Mathf.Max(fastest, bag.rig.linearVelocity.magnitude);
                    if (!shot && since >= 1.5f)
                    {
                        shot = true;
                        Screenshot(i == 0 ? "wind-backpack-open" : "wind-backpack-sheltered");
                    }
                }
                wind.windMovesItems = false;
                pushes[i] = SnowstormShelter.WatchedPushes;
                pushedAt[i] = SnowstormShelter.WatchedOpen;
                Collider part = bag.GetComponentInChildren<Collider>();
                Info($"the backpack {(i == 0 ? "in the open" : "behind the wall")} in the wind",
                    $"pushed {SnowstormShelter.WatchedPushes} times (exposed {SnowstormShelter.WatchedOpen:0.00} at the last), fastest {fastest:0.00} m/s; state {bag.itemState}, "
                    + $"kinematic {bag.rig.isKinematic}, mass {bag.rig.mass:0.##}, damping {bag.rig.linearDamping:0.##}, constraints {bag.rig.constraints}, sleeping {bag.rig.IsSleeping()}, "
                    + $"collider {(part != null ? part.GetType().Name + " " + (part.sharedMaterial != null ? $"friction {part.sharedMaterial.staticFriction:0.##}/{part.sharedMaterial.dynamicFriction:0.##} {part.sharedMaterial.frictionCombine}" : "no material") : "none")}; "
                    + $"the storm: force {wind.windForce}, items x{wind.windItemFactor}, wind {wind.currentWindDirection}, moves items {wind.windMovesItems}");
                SnowstormShelter.Watched = null;
                moved[i] = bag != null ? Vector3.Distance(bag.transform.position, from) : -1f;
                if (bag != null)
                {
                    PhotonNetwork.Destroy(bag.gameObject);
                }
                if (cover != null)
                {
                    made.Remove(cover);
                    Destroy(cover);
                }
                yield return new WaitForSeconds(0.5f);
            }
            Check("a wind storm pushes a backpack lying in the open at full strength, but behind a wall on the wind's side not at all, as a scout there (every ascent)",
                loose[0] && loose[1] && pushes[0] > 50 && pushes[1] > 50 && pushedAt[0] >= 0.99f && pushedAt[1] >= 0f && pushedAt[1] <= 0.01f && moved[1] >= 0f && moved[1] <= 0.3f,
                $"in 3 s of wind the backpack in the open was pushed {pushes[0]} times at {pushedAt[0]:0.00} of the storm's strength and went {moved[0]:0.00} m (loose {loose[0]}); "
                + $"the one behind the wall {pushes[1]} times at {pushedAt[1]:0.00} and went {moved[1]:0.00} m (loose {loose[1]}) (a backpack is heavy: the game's push barely slides it on flat ground); "
                + $"pushes taken off or weakened by shelter {SnowstormShelter.ItemsSheltered - sheltered}");
        }
        finally
        {
            Destroy(wind.gameObject);
            foreach (GameObject block in made)
            {
                Destroy(block);
            }
            undo();
        }
        yield return new WaitForSeconds(1f);
    }

    /// <summary>
    /// A snowstorm by day outside the Alpine (ascent 18): a test snowstorm blowing on the scout where it stands, at
    /// midday in direct sunlight. Its heat lands with or without an open parasol; only sunscreen keeps it off (with
    /// ascent 15's rule on, in memory, all but a quarter). While it blows ascent 23's sun neither heats the scout nor
    /// roasts the open parasol; once it stops, the sun burns again.
    /// </summary>
    private IEnumerator StormAndSun()
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather) || Rules.Level >= Lvl.AllWeather)
        {
            Info("the snowstorm heat and sun checks are skipped", Rules.Level >= Lvl.AllWeather
                ? "at ascent 25 a test storm would follow ascent 25's own snowstorm; the checks run at ascents 18-24" : $"testing ascent {target}");
            yield break;
        }
        Item parasolPrefab = ItemByName("Parasol");
        Item sunscreenPrefab = ItemByName("Sunscreen");
        if (parasolPrefab == null || sunscreenPrefab == null)
        {
            Fail("the Parasol and the Sunscreen are in the item database");
            yield break;
        }
        bool sun = Rules.Enabled(Lvl.HotSun);
        Character local = Local;
        DayNightManager clock = DayNightManager.instance;
        local.refs.items.EquipSlot(Optionable<byte>.None);
        clock.timeOfDay = (clock.dayStart + clock.dayEnd) / 2f;
        yield return new WaitForSeconds(2f);
        if (sun && !InSunlight(local))
        {
            yield return IntoSunlight(40f);
        }
        Segment segment = Map.CurrentSegment;
        if (Map.IsNight || segment == Segment.Alpine || segment == Segment.Caldera || (sun && !InSunlight(local)))
        {
            Info("the snowstorm heat and sun checks are skipped", $"segment {segment}, night {Map.IsNight}, in direct sunlight {InSunlight(local)}, sun strength {clock.sun.intensity:0.0}");
            yield break;
        }
        float hotFactor = A15_StrongerAilments.Factor(Status.Hot);
        Action undo = CalmForMeasuring();
        ConfigEntry<bool> worseRule = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool worseSaved = worseRule.Value;
        worseRule.Value = false;
        HostOptions.Invalidate();
        Protect(false);
        WindChillZone snow = TestSnowstorm(local.Center);
        try
        {
            // The storm's statuses build up over its first 5 s.
            for (float until = Time.time + 10f; Time.time < until && snow.hasBeenActiveFor < 5.2f;)
            {
                yield return null;
            }
            Afflictions.SetStatus(Status.Hot, 0f);
            Afflictions.SetStatus(Status.Cold, 0f);
            Meter bare = new Meter(Status.Hot);
            yield return new WaitForSeconds(4f);
            bare.Stop();
            bool hidden = A18_ExtremeWeather.SnowstormHidesSun;
            float rate = snow.windPlayerFactor * TestStormPerSecond * hotFactor;
            float sunRate = A23_HotSun.HeatPerSecond;
            Check(sun ? "a snowstorm by day outside the Alpine scorches (18), and no sun burns through it (23): only the storm's heat lands"
                      : "a snowstorm by day outside the Alpine scorches (18)",
                rate > 0f && Mathf.Abs(bare.Total - rate * bare.Seconds) <= rate * bare.Seconds * 0.12f && (!sun || hidden),
                $"{bare.Total * 100f:0.##} heat in {bare.Seconds:0.0} s (the storm's {rate * 100f * bare.Seconds:0.##} at exposure {snow.windPlayerFactor:0.##}"
                + (sun ? $"; with the sun's it would be {(rate + sunRate) * 100f * bare.Seconds:0.##}), sun hidden {hidden}" : ")"));
            Screenshot("snowstorm-heat-bare");

            yield return SpawnIntoHand(parasolPrefab);
            Item parasol = held;
            Parasol shade = parasol != null ? parasol.GetComponent<Parasol>() : null;
            if (shade != null)
            {
                if (!shade.isOpen)
                {
                    shade.ToggleOpen();
                }
                yield return new WaitForSeconds(0.5f);
                Afflictions.SetStatus(Status.Hot, 0f);
                Afflictions.SetStatus(Status.Cold, 0f);
                Meter shaded = new Meter(Status.Hot);
                yield return new WaitForSeconds(4f);
                shaded.Stop();
                float roasted = Roasted(parasol);
                Screenshot("snowstorm-heat-parasol");
                Check("an open parasol does not keep the snowstorm's heat off (18)" + (sun ? ", and no sun roasts it while the storm blows (23)" : ""),
                    shade.isOpen && Mathf.Abs(shaded.Total - rate * shaded.Seconds) <= rate * shaded.Seconds * 0.12f && roasted <= 0f,
                    $"parasol open {shade.isOpen}, {shaded.Total * 100f:0.##} heat in {shaded.Seconds:0.0} s (the storm's {rate * 100f * shaded.Seconds:0.##}), roasted {roasted:0.000}");
            }
            yield return PutAway(parasolPrefab);

            yield return SpawnIntoHand(sunscreenPrefab);
            if (held != null)
            {
                yield return EatHeld();
            }
            yield return new WaitForSeconds(1f);
            bool ownSpray = local.data.wearingSunscreen;
            if (!ownSpray)
            {
                // The spray's cloud hangs a little ahead of the can and works on whoever is in it: step into it (the game's own warp).
                AOE cloud = FindObjectsByType<AOE>(FindObjectsSortMode.None)
                    .Where(a => a != null && a.transform.root.name.StartsWith("VFX_Sunscreen", StringComparison.Ordinal))
                    .OrderBy(a => Vector3.Distance(a.transform.position, local.Center))
                    .FirstOrDefault();
                if (cloud != null)
                {
                    Info("the sunscreen's cloud", $"{Vector3.Distance(cloud.transform.position, local.Center):0.0} m from the scout, reach {cloud.range:0.#} m; the scout steps into it");
                    Vector3 into = cloud.transform.position;
                    if (Physics.Raycast(into + Vector3.up * 2f, Vector3.down, out RaycastHit floor, 6f, HelperFunctions.terrainMapMask))
                    {
                        into = floor.point + Vector3.up * 1.2f;
                    }
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, into, false);
                    yield return new WaitForSeconds(1.5f);
                }
            }
            Screenshot("snowstorm-heat-sunscreen");
            foreach (bool worse in new[] { false, true })
            {
                worseRule.Value = worse;
                HostOptions.Invalidate();
                Afflictions.SetStatus(Status.Hot, 0f);
                Afflictions.SetStatus(Status.Cold, 0f);
                Meter screened = new Meter(Status.Hot);
                yield return new WaitForSeconds(4f);
                screened.Stop();
                // At the spray cloud the storm reaches the scout as much as it does there (not where the bare heat was measured).
                float rateHere = snow.windPlayerFactor * TestStormPerSecond * hotFactor;
                float expected = worse ? rateHere * screened.Seconds * A14_Items.SunscreenLeak : 0f;
                Check(worse ? "with ascent 15's rule sunscreen keeps off three quarters of the snowstorm's heat (15, 18)" : "sunscreen keeps the snowstorm's heat off (18)",
                    local.data.wearingSunscreen && (worse ? Mathf.Abs(screened.Total - expected) <= rateHere * screened.Seconds * 0.05f : screened.Total < 0.0005f),
                    $"wearing sunscreen {local.data.wearingSunscreen} ({(ownSpray ? "from spraying" : "from stepping into the spray")}), {screened.Total * 100f:0.##} heat in {screened.Seconds:0.0} s ({expected * 100f:0.##} expected)");
            }
            worseRule.Value = false;
            HostOptions.Invalidate();
            Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.Sunscreen);

            // The game's own toggle ends the storm.
            snow.RPCA_ToggleWind(false, Vector3.zero, 100000f);
            yield return new WaitForSeconds(0.5f);
            if (sun && !InSunlight(local))
            {
                yield return IntoSunlight(20f);
            }
            if (sun)
            {
                Afflictions.SetStatus(Status.Hot, 0f);
                Afflictions.SetStatus(Status.Cold, 0f);
                Meter after = new Meter(Status.Hot);
                yield return new WaitForSeconds(3f);
                after.Stop();
                Check("once the snowstorm stops, the sun burns again (23)",
                    !A18_ExtremeWeather.SnowstormHidesSun && !local.data.wearingSunscreen && Mathf.Abs(after.Total - sunRate * after.Seconds) <= sunRate * after.Seconds * 0.25f,
                    $"{after.Total * 100f:0.##} heat in {after.Seconds:0.0} s (the sun's {sunRate * 100f * after.Seconds:0.##}), sun hidden {A18_ExtremeWeather.SnowstormHidesSun}, in direct sunlight {InSunlight(local)}");
            }
        }
        finally
        {
            Destroy(snow.gameObject);
            worseRule.Value = worseSaved;
            HostOptions.Invalidate();
            Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.Sunscreen);
            Afflictions.SetStatus(Status.Hot, 0f);
            Afflictions.SetStatus(Status.Cold, 0f);
            Protect(true);
            undo();
        }
    }

    /// <summary>
    /// The Cherry Lollipop and the Vanilla Wafers have their own Custom Run switches, after their items' and
    /// with names of their own. In a custom run (switches set in memory, a suitcase's pool holding only the item): both
    /// on, half each; the item off, all of them the variant; the variant off, none.
    /// </summary>
    private IEnumerator VariantSwitchesCheck()
    {
        Character local = Local;
        Item lollipop = ItemByName(CherryLollipop.LollipopPrefab);
        Item cookies = ItemByName(VanillaWafers.CookiesPrefab);
        Item vanilla = ItemByName(VanillaWafers.VanillaPrefab);
        Luggage luggage = Luggage.ALL_LUGGAGE
            .Where(l => l != null && !(l is RespawnChest) && !l.CompareTag("ClownLuggage") && l.gameObject.activeInHierarchy)
            .OrderBy(l => Vector3.Distance(l.transform.position, local.Center))
            .FirstOrDefault();
        if (lollipop == null || CherryLollipop.Template == null || cookies == null || vanilla == null || luggage == null)
        {
            Fail("the items and a suitcase for the custom-run switch checks", $"Big Lollipop {lollipop != null}, pink {CherryLollipop.Template != null}, cookies {cookies != null}, vanilla {vanilla != null}, suitcase {luggage != null}");
            yield break;
        }
        foreach ((Item plain, Item variant, string key) in new[] { (lollipop, CherryLollipop.Template, CherryLollipop.NameKey), (cookies, vanilla, VanillaWafers.NameKey) })
        {
            List<Item> listed = RunSettings.ModifiableItemPrefabs;
            int at = listed.IndexOf(variant);
            int itemAt = listed.IndexOf(plain);
            string label = Plain(LocalizedText.GetText(key));
            Check($"the Custom Run screen lists '{label}' after '{Plain(plain.GetItemName())}'", at > 0 && at == itemAt + 1 && label.Length > 0 && label != Plain(plain.GetItemName()),
                $"listed at {at} (its item at {itemAt}), called '{label}'");
            Step("custom-run picks: " + label, () =>
            {
                SpawnPool pool = luggage.GetSpawnPool();
                int savedPlain = RunSettings.GetItemValue(plain.gameObject.name, forceGetCustomValue: true);
                int savedVariant = RunSettings.GetItemValue(variant.gameObject.name, forceGetCustomValue: true);
                bool wasCustom = RunSettings.IsCustomRun;
                // A custom run first (the loot tables settle for it: the Lanterns' extra places go), then the test pool.
                RunSettings.IsCustomRun = true;
                MoreLanterns.Sync();
                LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> own);
                LootData.AllSpawnWeightData[pool] = new Dictionary<ushort, int> { [plain.itemID] = LootData.RarityWeights[plain.GetComponent<LootData>().Rarity] };
                List<string> results = new List<string>();
                bool ok = true;
                try
                {
                    foreach ((int plainOn, int variantOn) in new[] { (1, 1), (0, 1), (1, 0) })
                    {
                        RunSettings.SetItemValue(plain.gameObject.name, plainOn);
                        RunSettings.SetItemValue(variant.gameObject.name, variantOn);
                        int plainPicks = 0;
                        int variantPicks = 0;
                        for (int i = 0; i < 50; i++)
                        {
                            foreach (GameObject pick in luggage.GetObjectsToSpawn(4, true))
                            {
                                if (pick != null && pick.name == variant.gameObject.name)
                                {
                                    variantPicks++;
                                }
                                else if (pick != null && pick.name == plain.gameObject.name)
                                {
                                    plainPicks++;
                                }
                            }
                        }
                        ok &= plainOn == 1 && variantOn == 1 ? plainPicks >= 70 && variantPicks >= 70 && plainPicks + variantPicks == 200
                            : plainOn == 0 ? variantPicks == 200 : plainPicks == 200;
                        results.Add($"item {(plainOn == 1 ? "on" : "off")} / variant {(variantOn == 1 ? "on" : "off")}: {variantPicks} variant, {plainPicks} item");
                    }
                }
                finally
                {
                    RunSettings.SetItemValue(plain.gameObject.name, savedPlain);
                    RunSettings.SetItemValue(variant.gameObject.name, savedVariant);
                    if (own != null)
                    {
                        LootData.AllSpawnWeightData[pool] = own;
                    }
                    else
                    {
                        LootData.AllSpawnWeightData.Remove(pool);
                    }
                    RunSettings.IsCustomRun = wasCustom;
                    MoreLanterns.Sync();
                }
                Check($"custom run: '{label}' follows its own switch (both on: half each; its item off: all; itself off: none)", ok,
                    string.Join("; ", results) + $" of 200 picks from {luggage.gameObject.name}'s pool ({pool})");
            });
        }
        yield break;
    }

    /// <summary>
    /// Every new item darkens when cooked, a stage at a time, like the game's items (the Big Lollipop beside them):
    /// each put on the ground and cooked the way a fire finishes it, once, twice and three times, its renderers'
    /// tint compared with how it was fresh.
    /// </summary>
    private IEnumerator NewItemsCooking()
    {
        string[] names =
        {
            "Lollipop", "Parasol", "Jetpack", ClimbingChalkItem.PrefabName, A14_Items.RootsParasolPrefab, AutoParachute.PrefabName, VanillaWafers.VanillaPrefab,
            WildFoods.WonderberryPrefab, WildFoods.GreenClusterberryPrefab, WildFoods.WeirdShroomPrefab, StoneItem.PrefabName, StickItem.PrefabName,
            WarpCompass.PrefabName, CherryLollipop.PrefabName, ToxicDrink.PrefabName,
        };
        List<string> results = new List<string>();
        List<string> failed = new List<string>();
        foreach (string name in names)
        {
            Item prefab = ItemByName(name);
            if (prefab == null)
            {
                failed.Add(name + " (not in the item database)");
                continue;
            }
            GameObject spawned = SpawnInFront(prefab, 2.5f);
            Item item = spawned != null ? spawned.GetComponent<Item>() : null;
            yield return new WaitForSeconds(0.5f);
            if (item == null || item.cooking == null || !item.cooking.canBeCooked)
            {
                results.Add($"{name}: cannot be cooked");
                failed.Add(name);
                if (spawned != null)
                {
                    PhotonNetwork.Destroy(spawned);
                }
                continue;
            }
            float Shade()
            {
                List<float> tints = new List<float>();
                foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is ParticleSystemRenderer || PathUnder(renderer.transform, item.transform).StartsWith("Hand_", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    foreach (Material material in renderer.materials)
                    {
                        if (material != null && material.HasProperty("_Tint"))
                        {
                            Color c = material.GetColor("_Tint");
                            tints.Add((c.r + c.g + c.b) / 3f);
                        }
                    }
                }
                return tints.Count > 0 ? tints.Average() : -1f;
            }
            Dictionary<Renderer, float> PartShades()
            {
                Dictionary<Renderer, float> parts = new Dictionary<Renderer, float>();
                foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is ParticleSystemRenderer || PathUnder(renderer.transform, item.transform).StartsWith("Hand_", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    Material material = renderer.sharedMaterials.Length > 0 ? renderer.materials[0] : null;
                    if (material != null && material.HasProperty("_Tint"))
                    {
                        Color c = material.GetColor("_Tint");
                        parts[renderer] = (c.r + c.g + c.b) / 3f;
                    }
                }
                return parts;
            }
            Dictionary<Renderer, float> freshParts = PartShades();
            List<float> shades = new List<float> { Shade() };
            for (int stage = 1; stage <= 3 && item != null; stage++)
            {
                item.cooking.FinishCooking();
                yield return new WaitForSeconds(0.3f);
                shades.Add(item != null ? Shade() : -1f);
            }
            if (item != null)
            {
                Dictionary<Renderer, float> cookedParts = PartShades();
                List<string> same = freshParts.Where(kv => kv.Key != null && cookedParts.TryGetValue(kv.Key, out float after) && after > kv.Value - 0.005f)
                    .Select(kv => $"{PathUnder(kv.Key.transform, item.transform)} ({(kv.Key.gameObject.activeInHierarchy ? "shown" : "hidden")})").ToList();
                if (same.Count > 0)
                {
                    results.Add($"{name}: not darkened: {string.Join(", ", same)}");
                }
            }
            bool darker = shades.Count == 4 && shades[0] > 0f && shades[1] < shades[0] - 0.005f && shades[2] < shades[1] - 0.005f && shades[3] < shades[2] - 0.005f;
            if (!darker)
            {
                failed.Add(name);
            }
            results.Add($"{name}: {string.Join(" > ", shades.Select(s => s.ToString("0.###")))}");
            if (item != null)
            {
                PhotonNetwork.Destroy(item.gameObject);
            }
            yield return new WaitForSeconds(0.2f);
        }
        Check("every new item darkens a stage at a time when cooked, like the game's items (fresh > once > twice > three times)", failed.Count == 0,
            string.Join("; ", results) + (failed.Count > 0 ? "; not darkening: " + string.Join(", ", failed) : ""));
    }

    private static string PathUnder(Transform t, Transform root)
    {
        List<string> names = new List<string>();
        for (Transform level = t; level != null && level != root; level = level.parent)
        {
            names.Add(level.name);
        }
        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>Uses the item in the hand until its use is done (eating it).</summary>
    private IEnumerator EatHeld()
    {
        Item item = held;
        item.StartUsePrimary();
        for (float until = Time.realtimeSinceStartup + 6f; Time.realtimeSinceStartup < until && item != null && !item.finishedCast;)
        {
            item.ContinueUsePrimary();
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        held = null;
    }

    /// <summary>Items put down in a row in front of the scout, looked at and pictured, then taken away again.</summary>
    private IEnumerator SideBySide(Item[] prefabs, string picture)
    {
        Character local = Local;
        Vector2 look = local.data.lookValues;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
        List<GameObject> placed = new List<GameObject>();
        for (int i = 0; i < prefabs.Length; i++)
        {
            Vector3 at = local.Center + forward * 1.4f + side * ((i - (prefabs.Length - 1) * 0.5f) * 0.45f) + Vector3.up * 0.3f;
            placed.Add(PhotonNetwork.InstantiateItemRoom(prefabs[i].gameObject.name, at, Quaternion.LookRotation(-forward)));
        }
        local.data.lookValues = new Vector2(look.x, -45f);
        yield return new WaitForSeconds(1.5f);
        Screenshot(picture);
        yield return new WaitForSeconds(1f);
        local.data.lookValues = look;
        foreach (GameObject item in placed.Where(p => p != null))
        {
            PhotonNetwork.Destroy(item);
        }
    }

    /// <summary>
    /// In the Caldera or the Gloom (after the Alps): the scout's own recovery fields carry this ascent's
    /// rates, which mods that show when a status wears off read (PlayersInfo, Effect Preview): heat
    /// 0.33/s in the Caldera, drowsiness 1/s in the Gloom (16), 2/s by day elsewhere (22). PlayersInfo's
    /// own countdown is checked when it is installed. On a Gloom map the Citadel's suitcases are found
    /// for its mimics (17); on a Caldera map none counts as the Citadel's.
    /// </summary>
    private IEnumerator RecoveryAndCitadel()
    {
        yield return new WaitForSeconds(0.5f);
        CharacterAfflictions afflictions = Afflictions;
        bool gloom = Map.InGloom;
        bool volcano = Map.InVolcano;
        float expectedDrowsy = target >= Lvl.SlowRecovery && gloom ? 0.01f : target >= Lvl.UniqueAilments ? (Map.IsNight ? 0f : 0.02f) : 0.025f;
        float expectedHeat = target >= Lvl.SlowRecovery && volcano ? 0.005f : 0.013f;
        Check($"the scout's own recovery fields carry this ascent's rates in the {Map.CurrentPlace} (16, 22)",
            Mathf.Abs(afflictions.drowsyReductionPerSecond - expectedDrowsy) < 0.0001f && Mathf.Abs(afflictions.hotReductionPerSecond - expectedHeat) < 0.0001f,
            $"drowsiness {afflictions.drowsyReductionPerSecond * 100f:0.##}/s (expected {expectedDrowsy * 100f:0.##}), heat {afflictions.hotReductionPerSecond * 100f:0.##}/s (expected {expectedHeat * 100f:0.##})");

        MethodInfo countdown = Compat.PlayersInfo.Present
            ? AccessTools.Method(AccessTools.TypeByName("PlayersInfo.Helpers.AfflictionTimeHelper"), "GetReductionTimeRemaining")
            : null;
        if (countdown == null)
        {
            Info("PlayersInfo's countdown check skipped", Compat.PlayersInfo.Present ? "its countdown method was not found" : "PlayersInfo is not installed");
        }
        else
        {
            Status type = gloom ? Status.Drowsy : Status.Hot;
            float rate = gloom ? expectedDrowsy : expectedHeat;
            float cooldown = gloom ? afflictions.drowsyReductionCooldown : afflictions.hotReductionCooldown;
            Unlocked(() =>
            {
                afflictions.SetStatus(Status.Cold, 0f);
                afflictions.SetStatus(type, 0f);
                // 10 on the bar, ascent 14's boost included.
                afflictions.AddStatus(type, A15_StrongerAilments.Raw(type, 0.1f));
                float shown = (float)countdown.Invoke(null, new object[] { afflictions, type });
                float expected = cooldown + afflictions.GetCurrentStatus(type) / rate;
                Check($"PlayersInfo counts down this ascent's {type} recovery", rate > 0f && Mathf.Abs(shown - expected) < 0.5f,
                    $"shows {shown:0.#} s for {afflictions.GetCurrentStatus(type) * 100f:0.#} {type} (expected {expected:0.#} s: {cooldown:0.#} s pause + {rate * 100f:0.##}/s)");
                afflictions.SetStatus(type, 0f);
            });
        }

        Luggage[] luggage = FindObjectsByType<Luggage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int citadel = luggage.Count(A17_Traps.IsCitadelChest);
        if (gloom)
        {
            Check("on a Gloom map the Citadel's suitcases are found for its mimics (17)", citadel > 0, $"{citadel} Citadel chests of {luggage.Length} suitcases");
        }
        else
        {
            Check("on a Caldera map no suitcase counts as the Citadel's (17)", citadel == 0, $"{citadel} of {luggage.Length} suitcases");
        }
    }

    private void CheckAilments()
    {
        (Status type, float factor)[] cases =
        {
            (Status.Cold, 1.3f), (Status.Hot, 1.5f), (Status.Poison, 1.75f), (Status.Spores, 1.65f), (Status.Drowsy, 1.3f)
        };
        Unlocked(() =>
        {
            foreach ((Status type, float factor) in cases)
            {
                Afflictions.SetStatus(Status.Hot, 0f);
                Afflictions.SetStatus(Status.Cold, 0f);
                float before = Afflictions.GetCurrentStatus(type);
                float added = Added(type, () => Afflictions.AddStatus(type, 0.1f));
                float expected = 0.1f * factor;
                Check($"{type} applies x{factor} (14)", Mathf.Abs(added - expected) < 0.0001f, $"{added:0.0000} for 0.1 (expected {expected:0.0000})");
                Afflictions.SetStatus(type, before);
            }
        });
    }

    private void CheckLuggage()
    {
        Character local = Local;
        Luggage luggage = Luggage.ALL_LUGGAGE
            .Where(l => l != null && !(l is RespawnChest) && l.state == Luggage.LuggageState.Closed && l.gameObject.activeInHierarchy)
            .OrderBy(l => Vector3.Distance(l.transform.position, local.Center))
            .FirstOrDefault();
        if (luggage == null)
        {
            Info("no closed suitcase found");
            return;
        }
        string name = luggage.gameObject.name;
        float expected = name.StartsWith("LuggageSmall") ? 0.06f : name.StartsWith("LuggageBig") ? 0.1f : name.StartsWith("LuggageEpic") ? 0.16f : 0.1f;
        float distance = Vector3.Distance(luggage.transform.position, local.Center);
        Unlocked(() =>
        {
            float before = Afflictions.GetCurrentStatus(Status.Injury);
            float added = Added(Status.Injury, () => luggage.Interact_CastFinished(local));
            float shown = Afflictions.GetCurrentStatus(Status.Injury) - before;
            Check("opening a suitcase hurts (21)", Mathf.Abs(added - expected) < 0.0001f,
                $"{name} ({distance:F0} m away): {added:0.000} injury (expected {expected:0.000}); the bar shows +{shown:0.000} (the game counts statuses in steps of 0.025)");
            Afflictions.SetStatus(Status.Injury, before);
        });
    }

    private IEnumerator SpawnIntoHand(Item prefab)
    {
        held = null;
        Character local = Local;
        // Earlier checks may have filled every slot: the first one (and the hands' spare one) is emptied for this one,
        // so the item lands in a slot of its own and nothing is left in the spare slot.
        if (local.player.itemSlots.All(slot => !slot.IsEmpty()))
        {
            local.refs.items.EquipSlot(Optionable<byte>.None);
            if (!local.player.GetItemSlot(250).IsEmpty())
            {
                local.player.EmptySlot(Optionable<byte>.Some((byte)250), true);
            }
            local.player.EmptySlot(Optionable<byte>.Some((byte)0), true);
            yield return new WaitForSeconds(0.3f);
        }
        local.refs.items.lastEquippedSlotTime = 0f;
        local.refs.items.SpawnItemInHand(prefab.gameObject.name);
        float end = Time.realtimeSinceStartup + 6f;
        float nextEquip = Time.realtimeSinceStartup + 1f;
        while (Time.realtimeSinceStartup < end)
        {
            Item current = local.data.currentItem;
            if (current != null && current.itemID == prefab.itemID)
            {
                held = current;
                yield return new WaitForSeconds(0.3f);
                yield break;
            }
            if (Time.realtimeSinceStartup >= nextEquip)
            {
                nextEquip = Time.realtimeSinceStartup + 1f;
                for (byte slot = 0; slot < local.player.itemSlots.Length; slot++)
                {
                    ItemSlot itemSlot = local.player.itemSlots[slot];
                    if (!itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == prefab.itemID)
                    {
                        local.refs.items.EquipSlot(Optionable<byte>.Some(slot));
                        break;
                    }
                }
            }
            yield return null;
        }
        Fail("put " + prefab.gameObject.name + " in the hand", "it never arrived");
    }

    private static string Describe(ItemActionBase action)
    {
        switch (action)
        {
            case Action_RestoreHunger restore:
                return $"RestoreHunger {restore.restorationAmount:0.###}";
            case Action_ModifyStatus modify:
                return $"ModifyStatus {modify.statusType} {modify.changeAmount:0.###}";
            default:
                return action.GetType().Name;
        }
    }

    private static Item TickItem()
    {
        Bugfix bugfix = Resources.Load<GameObject>("BugfixOnYou")?.GetComponent<Bugfix>();
        return bugfix != null ? bugfix.bugItem : null;
    }

    private IEnumerator HalfFood()
    {
        Item tick = TickItem();
        Item food = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values
            .Where(i => i != null && (tick == null || i.itemID != tick.itemID) && i.UIData != null && i.UIData.canPocket)
            .Where(i => i.GetComponent<Action_RestoreHunger>() is Action_RestoreHunger restore && restore.restorationAmount >= 0.05f && restore.restorationAmount <= 0.6f)
            // Plain food only: no poison, no random mushroom effect, no status of its own.
            .Where(i => i.GetComponent<Action_InflictPoison>() == null && i.GetComponent<Action_RandomMushroomEffect>() == null && !i.GetComponents<Action_ModifyStatus>().Any(m => m.changeAmount > 0f))
            .OrderBy(i => i.itemID)
            .FirstOrDefault();
        if (food == null)
        {
            Info("no food found for the half-food check");
            yield break;
        }
        yield return SpawnIntoHand(food);
        if (held == null)
        {
            yield break;
        }
        Action_RestoreHunger action = held.GetComponent<Action_RestoreHunger>();
        Unlocked(() =>
        {
            Afflictions.SetStatus(Status.Hunger, 0.5f);
            action.RunAction();
            float restored = 0.5f - Afflictions.GetCurrentStatus(Status.Hunger);
            float expected = action.restorationAmount * 0.5f;
            Check("food restores half as much (15)", Mathf.Abs(restored - expected) < 0.002f,
                $"{held.gameObject.name}: {restored:0.000} of {action.restorationAmount:0.000} (expected {expected:0.000})");
            Afflictions.SetStatus(Status.Hunger, 0f);
        });
    }

    /// <summary>Eating the tick that was pulled off (it is in the hand after <see cref="TickBites"/>).</summary>
    private IEnumerator EatTick()
    {
        Item tick = TickItem();
        Check("the tick item is recognised", tick != null && A14_WorseItems.Ticks.IsTick(tick), tick == null ? "BugfixOnYou has no tick item" : $"{tick.gameObject.name} (#{tick.itemID})");
        if (tick == null || held == null || held.itemID != tick.itemID || Local.data.currentItem != held)
        {
            Info("eating check skipped: no pulled-off tick in the hand");
            yield break;
        }
        List<ItemActionBase> hungerActions = held.GetComponentsInChildren<ItemActionBase>(true)
            .Where(a => a is Action_RestoreHunger || (a is Action_ModifyStatus modify && modify.statusType == Status.Hunger && modify.changeAmount < 0f))
            .ToList();
        Info("the tick's hunger actions", hungerActions.Count == 0 ? "none" : string.Join(", ", hungerActions.Select(Describe)));
        Unlocked(() =>
        {
            Afflictions.SetStatus(Status.Hunger, 0.5f);
            foreach (ItemActionBase action in hungerActions)
            {
                action.RunAction();
            }
            float atTarget = Afflictions.GetCurrentStatus(Status.Hunger);

            // Control: the same tick below ascent 15 does feed.
            int saved = Ascents._currentAscent;
            Ascents._currentAscent = Lvl.WorseItems - 1;
            Afflictions.SetStatus(Status.Hunger, 0.5f);
            try
            {
                foreach (ItemActionBase action in hungerActions)
                {
                    action.RunAction();
                }
            }
            finally
            {
                Ascents._currentAscent = saved;
            }
            float below = Afflictions.GetCurrentStatus(Status.Hunger);
            Check("eating a tick restores no hunger (15)", Mathf.Abs(atTarget - 0.5f) < 0.0005f, $"hunger 0.500 -> {atTarget:0.000}");
            Check("control: below ascent 15 a tick still feeds", hungerActions.Count > 0 && below < 0.499f, $"hunger 0.500 -> {below:0.000} at ascent {Lvl.WorseItems - 1}");
            Afflictions.SetStatus(Status.Hunger, 0f);
        });
    }

    /// <summary>
    /// The Climbing Chalk as a player meets it: it comes out of a suitcase, goes into a backpack and a
    /// fanny pack and back into the hand, a use is started and let go (the rub stops), then held for
    /// its 2 s (the rub plays all along, dust, uses left), its effect on the climbing lunge, the other
    /// uses (dust again each time, the effect's time starts over, the last uses it up), the white hands
    /// and the HUD icon, everything going away again when the effect runs out, a chalk burned as Jetpack
    /// fuel and a chalk cooked until incinerated.
    /// </summary>
    private IEnumerator ClimbingChalk()
    {
        Item prefab = ItemByName(ClimbingChalkItem.PrefabName);
        Item drink = ItemByName("Energy Drink");
        if (prefab == null || drink == null)
        {
            Fail("the Climbing Chalk and the Energy Drink are in the item database");
            yield break;
        }
        Step("chalk spawning", () =>
        {
            LootData.PopulateLootData();
            Item lollipop = ItemByName(ClimbingChalkItem.BigLollipopPrefab);
            int lollipopWeight = lollipop != null ? LootData.RarityWeights[lollipop.GetComponent<LootData>().Rarity] : -1;
            int pools = 0;
            int matching = 0;
            bool elsewhere = false;
            foreach (KeyValuePair<SpawnPool, Dictionary<ushort, int>> pool in LootData.AllSpawnWeightData)
            {
                bool hasDrink = pool.Value.ContainsKey(drink.itemID);
                bool hasChalk = pool.Value.TryGetValue(prefab.itemID, out int chalkWeight);
                pools += hasDrink ? 1 : 0;
                matching += hasDrink && hasChalk && chalkWeight == lollipopWeight ? 1 : 0;
                elsewhere |= hasChalk && !hasDrink;
            }
            Check("the chalk spawns where the Energy Drink does, as rarely as the Big Lollipop (9)", lollipopWeight > 0 && pools > 0 && matching == pools && !elsewhere && prefab.IsValidToSpawn(),
                $"{matching} of the Energy Drink's {pools} pools at the Big Lollipop's weight {lollipopWeight}, in other pools {elsewhere}, may spawn {prefab.IsValidToSpawn()}");
        });

        yield return ChalkFromSuitcase(prefab);

        yield return SpawnIntoHand(prefab);
        if (held == null)
        {
            yield break;
        }
        Step("chalk item", () =>
        {
            Item chalkItem = held;
            Action_ApplyAffliction effect = chalkItem.GetComponent<Action_ApplyAffliction>();
            Action_ReduceUses uses = chalkItem.GetComponent<Action_ReduceUses>();
            Check("the chalk has its bag, icon, name and prompt, and three uses", HasBag(chalkItem) && prefab.UIData.icon != null && chalkItem.UIData.icon != null
                && chalkItem.GetItemName() == "CLIMBING CHALK" && chalkItem.UIData.mainInteractPrompt == "use" && (chalkItem.itemTags & Item.ItemTags.Mystical) == 0
                && chalkItem.totalUses == ClimbingChalkItem.Uses && UsesLeft(chalkItem) == ClimbingChalkItem.Uses && Mathf.Abs(UseBar(chalkItem) - 1f) < 0.001f,
                $"bag {HasBag(chalkItem)}, icon {(prefab.UIData.icon != null ? prefab.UIData.icon.name : "none")}, name '{chalkItem.GetItemName()}', prompt '{chalkItem.UIData.mainInteractPrompt}', tags {chalkItem.itemTags}, uses {chalkItem.totalUses} ({UsesLeft(chalkItem)} left, use bar {UseBar(chalkItem):0.00})");
            Check("each use of the chalk applies its effect and takes one use off; the last uses it up", effect != null && effect.OnCastFinished && uses != null && uses.OnCastFinished && uses.consumeOnFullyUsed
                && chalkItem.GetComponent<Action_Consume>() == null,
                $"effect {(effect != null ? effect.OnCastFinished.ToString() : "missing")}, uses {(uses != null ? $"{uses.OnCastFinished}, used up at 0 {uses.consumeOnFullyUsed}" : "missing")}");
        });
        yield return ChalkInBackpack(prefab, BackpackSlot.BackpackType.Backpack, "backpack");
        yield return ChalkInBackpack(prefab, BackpackSlot.BackpackType.Fannypack, "fanny pack");
        if (held == null || !ClimbingChalkItem.IsChalk(held) || Local.data.currentItem != held)
        {
            yield return SpawnIntoHand(prefab);
            if (held == null)
            {
                yield break;
            }
        }
        Item chalk = held;

        // A use started and let go half way: the rub stops, nothing else happens.
        chalk.StartUsePrimary();
        for (float t = 0f; t < ClimbingChalkItem.UseSeconds * 0.5f; t += Time.deltaTime)
        {
            chalk.ContinueUsePrimary();
            yield return null;
        }
        Step("chalk let go", () =>
        {
            chalk.CancelUsePrimary();
            Check("letting go early stops the rub, and the chalk stays", ClimbingChalkItem.rubbing == null && Local.data.currentItem == chalk
                && !Afflictions.HasAfflictionType(Peak.Afflictions.Affliction.AfflictionType.ClimbingChalk, out _));
        });
        yield return new WaitForSeconds(0.5f);

        // The climbing lunge's stamina without chalk, for comparison.
        float lunge = 0f;
        Step("lunge without chalk", () => lunge = LungeCost());

        // The first real use: held for its whole 1 s; the rub is sampled every frame of it, and what
        // the scout hears is recorded (0.5 s before the use until 0.5 s after it).
        ListenerRecorder recorder = ListenerRecorder.Attach();
        recorder?.Begin();
        yield return new WaitForSeconds(0.5f);
        HashSet<GameObject> earlierDust = DustClouds();
        chalk.StartUsePrimary();
        float started = Time.realtimeSinceStartup;
        float end = started + 4f;
        float playing = 0f;
        float volume = 0f;
        float farthest = 0f;
        bool shot = false;
        while (Time.realtimeSinceStartup < end && chalk != null && !chalk.finishedCast)
        {
            SFX_Player.SoundEffectHandle rub = ClimbingChalkItem.rubbing;
            AudioSource source = rub != null && rub.source != null ? rub.source.source : null;
            if (source != null && source.isPlaying && source.clip != null && source.clip.name == "ClimbingChalk_rub")
            {
                playing += Time.deltaTime;
                volume = source.volume;
                farthest = Mathf.Max(farthest, Vector3.Distance(source.transform.position, Local.Center));
            }
            chalk.ContinueUsePrimary();
            if (!shot && Time.realtimeSinceStartup > started + 1f)
            {
                shot = true;
                Screenshot("chalk-1-using");
            }
            yield return null;
        }
        float useSeconds = Time.realtimeSinceStartup - started;
        float used = Time.realtimeSinceStartup;
        // The button is let go after the use, as a player does.
        chalk.CancelUsePrimary();
        if (recorder != null)
        {
            // For comparison in the same recording: the game's own eating sound, played the same way (at the hands).
            StartCoroutine(ReferenceSound(1f));
            StartCoroutine(StopRecording(recorder, 3f, "chalk-use"));
        }
        GameObject dust = null;
        while (Time.realtimeSinceStartup < used + 3f && (dust = NewDust(earlierDust)) == null)
        {
            yield return null;
        }
        if (dust != null)
        {
            StartCoroutine(WatchDust(dust));
        }
        float expectedShare = Rules.Enabled(Lvl.WorseItems) ? ClimbingChalkItem.WorseStamina : ClimbingChalkItem.Stamina;
        float expectedSeconds = Rules.Enabled(Lvl.WorseItems) ? ClimbingChalkItem.WorseSeconds : ClimbingChalkItem.Seconds;
        Step("chalk used once", () =>
        {
            Check("a use of the chalk takes 1 s (the game: 2 s)", Mathf.Abs(useSeconds - ClimbingChalkItem.UseSeconds) < 0.15f,
                $"used in {useSeconds:0.00} s (the item's use time {(chalk != null ? chalk.usingTimePrimary.ToString("0.##") + " s" : "gone, used up")})");
            AudioClip rubClip = ClimbingChalkItem.rub != null ? ClimbingChalkItem.rub.clips[0] : null;
            Check("the hand-rub sound plays once as the chalk is used (no repeat) and fades out", rubClip != null && rubClip.length < 1.2f && playing >= rubClip.length * 0.8f && playing <= rubClip.length * 1.15f + 0.05f
                && Mathf.Abs(volume - 0.25f) < 0.026f && farthest < 2f,
                $"the rub {(rubClip != null ? rubClip.length : 0f):0.00} s long, playing {playing:0.00} s of the {useSeconds:0.00} s use, volume {volume:0.000}, at most {farthest:0.0} m from the scout");
            Afflictions.HasAfflictionType(Peak.Afflictions.Affliction.AfflictionType.ClimbingChalk, out Peak.Afflictions.Affliction applied);
            Peak.Afflictions.Affliction_ClimbingChalk chalked = applied as Peak.Afflictions.Affliction_ClimbingChalk;
            bool kept = !UsedUp(chalk, prefab);
            int left = Rules.Enabled(Lvl.WorseItems) ? 1 : 2;
            Check($"a 1 s use chalks the scout and leaves {left} of its 3 uses (ascent 15 takes two per use)", chalked != null && kept && UsesLeft(chalk) == left && Mathf.Abs(UseBar(chalk) - left / 3f) < 0.001f,
                $"effect {(chalked != null ? "on" : "missing")}, still in the hand {kept}, uses left {(kept ? UsesLeft(chalk) : 0)}, use bar {(kept ? UseBar(chalk) : 0f):0.00}");
            Check("chalk: climbing costs " + Mathf.RoundToInt(expectedShare * 100f) + "% stamina for " + expectedSeconds + " s (9, 15)",
                chalked != null && Mathf.Abs(chalked.climbStaminaMultiplier - expectedShare) < 0.0001f && Mathf.Abs(chalked.totalTime - expectedSeconds) < 0.0001f,
                chalked == null ? "no chalk effect" : $"{chalked.climbStaminaMultiplier:0.00} for {chalked.totalTime:0.#} s");
            ParticleSystem particles = dust != null ? dust.GetComponent<ParticleSystem>() : null;
            Check("after the use, the chalk puffs dust around the scout, silently and without sun protection",
                particles != null && particles.isPlaying && dust.GetComponentInChildren<AOE>(true) == null && dust.GetComponentInChildren<SFX_PlayOneShot>(true) == null
                && Vector3.Distance(dust.transform.position, Local.Center) < 3f && !Local.data.wearingSunscreen,
                dust == null ? "no dust within 3 s" : $"playing {particles != null && particles.isPlaying}, {Vector3.Distance(dust.transform.position, Local.Center):0.0} m from the scout, sunscreen {Local.data.wearingSunscreen}");
            float chalkedLunge = LungeCost();
            Check("chalk: the climbing lunge costs " + Mathf.RoundToInt(expectedShare * 100f) + "% of its stamina", lunge > 0f && Mathf.Abs(chalkedLunge / lunge - expectedShare) < 0.01f,
                $"{chalkedLunge * 100f:0.0} instead of {lunge * 100f:0.0}");
        });

        // The other uses, a few seconds into the effect each: every one puffs dust again and starts the
        // effect's time over; the last uses the chalk up (three uses, two from ascent 15).
        int expectedUses = Rules.Enabled(Lvl.WorseItems) ? 2 : 3;
        int usesMade = 1;
        int dusts = dust != null ? 1 : 0;
        bool restarted = true;
        float elapsedBefore = 0f;
        float elapsed = 0f;
        while (usesMade < 4 && !UsedUp(chalk, prefab))
        {
            yield return new WaitForSeconds(2.5f);
            elapsedBefore = ChalkElapsed();
            earlierDust = DustClouds();
            chalk.StartUsePrimary();
            end = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < end && chalk != null && chalk.gameObject.activeInHierarchy && !chalk.finishedCast)
            {
                chalk.ContinueUsePrimary();
                yield return null;
            }
            usesMade++;
            used = Time.realtimeSinceStartup;
            GameObject next = null;
            while (Time.realtimeSinceStartup < used + 3f && (next = NewDust(earlierDust)) == null)
            {
                yield return null;
            }
            if (chalk != null && chalk.gameObject.activeInHierarchy)
            {
                chalk.CancelUsePrimary();
            }
            yield return new WaitForSeconds(0.5f);
            dusts += next != null ? 1 : 0;
            elapsed = ChalkElapsed();
            restarted &= elapsedBefore > 1f && elapsed >= 0f && elapsed < 2f;
        }
        Screenshot("chalk-1b-last-use");
        Step("chalk used up", () =>
        {
            bool gone = UsedUp(chalk, prefab);
            Check($"the chalk is used up after {expectedUses} uses, each puffing dust and starting the effect's time over (3; 2 from 15)",
                gone && usesMade == expectedUses && dusts == expectedUses && restarted,
                $"used up {gone} after {usesMade} uses, {dusts} dust clouds, the effect last ran {elapsedBefore:0.0} s and then {elapsed:0.0} s");
        });

        // Something in the hand again, so the hands show in first person.
        yield return EquipAnything();
        yield return new WaitForSeconds(WhitenCheckDelay);
        Screenshot("chalk-2-chalked");
        Step("chalked hands", () =>
        {
            SkinnedMeshRenderer hands = WhiteHands();
            Material white = hands != null ? hands.sharedMaterial : null;
            Color skin = white != null && white.HasProperty("_SkinColor") ? white.GetColor("_SkinColor") : Color.clear;
            int triangles = hands != null && hands.sharedMesh != null ? hands.sharedMesh.triangles.Length / 3 : 0;
            Check("the chalked scout's hands are white (a white copy of the hands over them)", hands != null && hands.enabled && triangles > 500 && skin.r > 0.9f && skin.g > 0.9f && skin.b > 0.85f
                && hands.bones.Length == Local.refs.customization.refs.mainRenderer.bones.Length,
                hands == null ? "no white hands" : $"{triangles} triangles, colour {skin}, shown {hands.enabled}");
            GameObject icon = ClimbingChalkItem.statusIcon;
            Check("the chalked hand shows above the stamina bar", icon != null && icon.activeInHierarchy && icon.transform.parent == GUIManager.instance.bar.transform,
                icon == null ? "no icon" : $"active {icon.activeInHierarchy}, parent {icon.transform.parent?.name}");
        });

        // The effect runs out by itself.
        float until = Time.realtimeSinceStartup + expectedSeconds + 5f;
        while (Time.realtimeSinceStartup < until && Afflictions.HasAfflictionType(Peak.Afflictions.Affliction.AfflictionType.ClimbingChalk, out _))
        {
            yield return null;
        }
        yield return new WaitForSeconds(2f);
        Screenshot("chalk-3-ran-out");
        Step("chalk ran out", () =>
        {
            bool over = !Afflictions.HasAfflictionType(Peak.Afflictions.Affliction.AfflictionType.ClimbingChalk, out _);
            bool overridden = WhiteHands() != null;
            GameObject icon = ClimbingChalkItem.statusIcon;
            Check("when the chalk runs out, the hands get their own colour back and the icon goes", over && !overridden && (icon == null || !icon.activeSelf),
                $"effect over {over}, white hands still there {overridden}, icon shown {(icon != null && icon.activeSelf)}");
            float plain = LungeCost();
            Check("without chalk the lunge costs its full stamina again", lunge > 0f && Mathf.Abs(plain - lunge) < 0.001f, $"{plain * 100f:0.0} (before: {lunge * 100f:0.0})");
        });

        yield return ChalkAsFuel(prefab);
        yield return ChalkCooked(prefab);
    }

    /// <summary>
    /// The chalk bag in the game's item shader. A chalk put on the ground has the material and lights
    /// up when looked at, as the game does it for every item. Then the bag stands in a row in front of
    /// the scout between how it looked before (Unity's Lit, the cube's material) and the Backpack, in
    /// the sun (from the scout's side, so the scout's own shadow falls beside the row) and in shade (a
    /// sheet put between the sun and the row that only casts a shadow). Screenshots of each for the eye.
    /// </summary>
    private IEnumerator ChalkLook()
    {
        Item prefab = ItemByName(ClimbingChalkItem.PrefabName);
        Item backpackPrefab = ItemByName("Backpack");
        Light sun = DayNightManager.instance != null ? DayNightManager.instance.sun : null;
        MeshRenderer backpackRenderer = backpackPrefab != null ? backpackPrefab.GetComponentsInChildren<MeshRenderer>(true)
            .FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == ClimbingChalkModel.ItemShader) : null;
        if (prefab == null || backpackRenderer == null || sun == null || !sun.isActiveAndEnabled)
        {
            Fail("the chalk, the Backpack and the sun are there", $"chalk {prefab != null}, Backpack {backpackRenderer != null}, sun {(sun != null ? sun.isActiveAndEnabled.ToString() : "none")}");
            yield break;
        }
        Character local = Local;
        Vector2 look = local.data.lookValues;
        // The sun from the scout's side: a quarter turn from the way its light goes.
        Vector3 away = Vector3.ProjectOnPlane(sun.transform.forward, Vector3.up);
        float yaw = Quaternion.LookRotation(away.sqrMagnitude > 0.001f ? away.normalized : local.transform.forward).eulerAngles.y + 90f;
        local.data.lookValues = new Vector2(yaw, -35f);
        yield return new WaitForSeconds(0.5f);

        GameObject spawned = SpawnInFront(prefab, 1.2f);
        Item chalk = spawned != null ? spawned.GetComponent<Item>() : null;
        if (chalk == null)
        {
            Fail("put a Climbing Chalk on the ground for its look");
            local.data.lookValues = look;
            yield break;
        }
        // Long enough for the screen to have faded in when this step runs right after waking up.
        yield return new WaitForSeconds(4f);
        Transform cube = chalk.transform.Find("Cube");
        MeshRenderer bag = cube != null ? cube.GetComponent<MeshRenderer>() : null;
        Material own = bag != null ? bag.sharedMaterial : null;
        Texture2D palette = own != null && own.HasProperty("_BaseTexture") ? own.GetTexture("_BaseTexture") as Texture2D : null;
        Check("the chalk bag is drawn with the game's item shader, coloured by its palette read as linear",
            HasBag(chalk) && own != null && own.shader != null && own.shader.name == ClimbingChalkModel.ItemShader && palette != null && palette.name == "ClimbingChalk_palette" && !palette.isDataSRGB,
            $"material {(own != null ? own.name : "none")}, shader {(own != null && own.shader != null ? own.shader.name : "none")}, base texture {(palette != null ? $"{palette.name} (sRGB {palette.isDataSRGB})" : "none")}");
        if (own == null || palette == null)
        {
            PhotonNetwork.Destroy(spawned);
            local.data.lookValues = look;
            yield break;
        }
        chalk.HoverEnter();
        yield return new WaitForSeconds(0.3f);
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        bag.GetPropertyBlock(block);
        Screenshot("chalk-look-highlight");
        Check("a chalk looked at lights up like any item (the game's highlight reaches the bag)",
            chalk.mainRenderer == bag && Mathf.Approximately(block.GetFloat("_Interactable"), 1f) && own.IsKeywordEnabled("_INTERACTABLE_ON"),
            $"highlighted renderer {(chalk.mainRenderer != null ? chalk.mainRenderer.name : "none")}, its _Interactable {block.GetFloat("_Interactable")}, highlight in the material {own.IsKeywordEnabled("_INTERACTABLE_ON")}");
        chalk.HoverExit();
        yield return new WaitForSeconds(0.2f);
        // The row gets a copy of the bag's mesh and material; the chalk itself goes.
        Mesh bagMesh = cube.GetComponent<MeshFilter>().sharedMesh;
        List<UnityEngine.Object> made = new List<UnityEngine.Object>();
        Material bagMaterial = new Material(own) { name = "ClimbingChalk (now)" };
        made.Add(bagMaterial);
        PhotonNetwork.Destroy(spawned);
        spawned = null;

        GameObject sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sheet.name = "UltimateAscent.SmokeTest shade";
        DestroyImmediate(sheet.GetComponent<Collider>());
        MeshRenderer sheetRenderer = sheet.GetComponent<MeshRenderer>();
        sheetRenderer.sharedMaterial = bagMaterial;
        sheetRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        sheet.SetActive(false);
        List<GameObject> row = new List<GameObject>();
        try
        {
            local.data.lookValues = new Vector2(yaw, -12f);
            yield return new WaitForSeconds(1f);
            Transform view = Camera.main != null ? Camera.main.transform : null;
            if (view == null)
            {
                Fail("the camera is there for the chalk bag's row");
                yield break;
            }
            Vector3 centre = view.position + view.forward * 1.8f;
            Vector3 across = Vector3.ProjectOnPlane(view.right, Vector3.up).normalized;
            Quaternion facing = Quaternion.LookRotation(-Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized);
            // Before: the cube's own material with the palette read as sRGB (the same pixels, copied).
            Texture2D srgbPalette = new Texture2D(palette.width, palette.height, palette.format, palette.mipmapCount > 1, false) { name = "ClimbingChalk_palette (sRGB)", wrapMode = TextureWrapMode.Clamp };
            Graphics.CopyTexture(palette, srgbPalette);
            Material before = new Material(prefab.transform.Find("Cube").GetComponent<MeshRenderer>().sharedMaterial) { name = "ClimbingChalk (Unity's Lit, before)" };
            before.SetTexture("_BaseMap", srgbPalette);
            before.SetColor("_BaseColor", Color.white);
            before.SetFloat("_Smoothness", 0.12f);
            made.Add(srgbPalette);
            made.Add(before);
            (string name, Mesh mesh, Material material, Quaternion rotation)[] shown =
            {
                ("before", bagMesh, before, Quaternion.identity),
                ("now", bagMesh, bagMaterial, Quaternion.identity),
                ("the Backpack", backpackRenderer.GetComponent<MeshFilter>().sharedMesh, backpackRenderer.sharedMaterial, backpackRenderer.transform.localRotation),
            };
            for (int i = 0; i < shown.Length; i++)
            {
                GameObject shownObject = new GameObject("UltimateAscent.SmokeTest " + shown[i].name, typeof(MeshFilter), typeof(MeshRenderer));
                shownObject.GetComponent<MeshFilter>().sharedMesh = shown[i].mesh;
                shownObject.GetComponent<MeshRenderer>().sharedMaterial = shown[i].material;
                Quaternion rotation = facing * shown[i].rotation;
                shownObject.transform.SetPositionAndRotation(centre + across * ((i - (shown.Length - 1) / 2f) * 0.42f), rotation);
                shownObject.transform.localScale = Vector3.one * (0.24f / Mathf.Max(0.001f, Height(shown[i].mesh, shown[i].rotation)));
                row.Add(shownObject);
            }
            Info("chalk bag row, left to right", string.Join(", ", shown.Select(s => s.name)));
            yield return new WaitForSeconds(0.5f);
            Screenshot("chalk-look-row-sun");
            yield return new WaitForSeconds(0.2f);
            Shade(sheet, centre, sun);
            yield return new WaitForSeconds(0.5f);
            Screenshot("chalk-look-row-shade");
            yield return new WaitForSeconds(0.2f);
        }
        finally
        {
            local.data.lookValues = look;
            row.ForEach(Destroy);
            made.ForEach(Destroy);
            Destroy(sheet);
            if (spawned != null)
            {
                PhotonNetwork.Destroy(spawned);
            }
        }
    }

    /// <summary>Puts the shade sheet 3 m toward the sun from a point, across the sunlight and big enough for the scout too.</summary>
    private static void Shade(GameObject sheet, Vector3 point, Light sun)
    {
        sheet.transform.SetPositionAndRotation(point - sun.transform.forward * 3f, Quaternion.LookRotation(sun.transform.forward));
        sheet.transform.localScale = new Vector3(4f, 4f, 0.05f);
        sheet.SetActive(true);
    }

    /// <summary>How tall a mesh stands when turned this way.</summary>
    private static float Height(Mesh mesh, Quaternion rotation)
    {
        Bounds bounds = mesh.bounds;
        float low = float.MaxValue;
        float high = float.MinValue;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
            float y = (rotation * point).y;
            low = Mathf.Min(low, y);
            high = Mathf.Max(high, y);
        }
        return high - low;
    }

    private static Item ItemByName(string prefabName)
    {
        return SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i != null && i.gameObject.name == prefabName);
    }

    private static bool HasBag(Item item)
    {
        Transform cube = item != null ? item.transform.Find("Cube") : null;
        return cube != null && cube.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && filter.sharedMesh.name == "ClimbingChalk";
    }

    /// <summary>
    /// The nearest closed suitcase is opened the game's way, with only the chalk left in its loot pool
    /// for this one opening (the pool is put back as soon as its items are out).
    /// </summary>
    private IEnumerator ChalkFromSuitcase(Item prefab)
    {
        Character local = Local;
        Luggage luggage = Luggage.ALL_LUGGAGE
            .Where(l => l != null && !(l is RespawnChest) && !l.CompareTag("ClownLuggage") && l.state == Luggage.LuggageState.Closed && l.gameObject.activeInHierarchy)
            .OrderBy(l => Vector3.Distance(l.transform.position, local.Center))
            .FirstOrDefault();
        if (luggage == null)
        {
            Info("no closed suitcase found for the chalk");
            yield break;
        }
        SpawnPool pool = luggage.GetSpawnPool();
        LootData.PopulateLootData();
        if (!LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> own) || !own.TryGetValue(prefab.itemID, out int weight))
        {
            Fail("a suitcase can give the Climbing Chalk", $"{luggage.gameObject.name}: its pool {pool} has no chalk");
            yield break;
        }
        HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
        LootData.AllSpawnWeightData[pool] = new Dictionary<ushort, int> { [prefab.itemID] = weight };
        List<Item> spawned = new List<Item>();
        try
        {
            Step("open a suitcase", () => luggage.Interact_CastFinished(local));
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && spawned.Count == 0)
            {
                yield return null;
                spawned = Item.ALL_ITEMS.Where(i => i != null && !before.Contains(i) && Vector3.Distance(i.transform.position, luggage.transform.position) < 5f).ToList();
            }
        }
        finally
        {
            LootData.AllSpawnWeightData[pool] = own;
        }
        yield return new WaitForSeconds(0.5f);
        spawned = Item.ALL_ITEMS.Where(i => i != null && !before.Contains(i) && Vector3.Distance(i.transform.position, luggage.transform.position) < 5f).ToList();
        List<Item> chalks = spawned.Where(ClimbingChalkItem.IsChalk).ToList();
        Check("a suitcase can give the Climbing Chalk, in its bag", chalks.Count > 0 && chalks.All(HasBag),
            $"{luggage.gameObject.name} ({pool}): {chalks.Count} chalk(s) of {spawned.Count} item(s), all with the bag {chalks.All(HasBag)}");
        yield return new WaitForSeconds(1.5f);
        foreach (Item chalk in chalks)
        {
            if (chalk != null && chalk.photonView != null)
            {
                PhotonNetwork.Destroy(chalk.gameObject);
            }
        }
    }

    /// <summary>
    /// A backpack or fanny pack on the ground: the chalk in the hand goes into it and comes back out,
    /// as its backpack wheel does it (which offers stashing only for items that may go in a backpack).
    /// </summary>
    private IEnumerator ChalkInBackpack(Item chalkPrefab, BackpackSlot.BackpackType type, string what)
    {
        Character local = Local;
        Item backpackPrefab = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i is Backpack b && b.backpackType == type);
        if (backpackPrefab == null)
        {
            Info("no " + what + " in the item database for the chalk");
            yield break;
        }
        if (local.data.currentItem == null || !ClimbingChalkItem.IsChalk(local.data.currentItem))
        {
            yield return SpawnIntoHand(chalkPrefab);
            if (held == null)
            {
                yield break;
            }
        }
        Backpack backpack = SpawnInFront(backpackPrefab, 1.2f)?.GetComponent<Backpack>();
        if (backpack == null)
        {
            Fail("put a " + what + " on the ground for the chalk");
            yield break;
        }
        yield return new WaitForSeconds(1f);
        BackpackReference reference = BackpackReference.GetFromBackpackItem(backpack);
        bool allowed = local.data.currentItem != null && local.data.currentItem.UIData.canBackpack;
        Step("chalk into the " + what, () => backpack.Stash(local, 0));
        yield return new WaitForSeconds(1f);
        BackpackData data = reference.GetData();
        ItemSlot slot = data != null && data.itemSlots != null && data.itemSlots.Length > 0 ? data.itemSlots[0] : null;
        bool inside = slot != null && !slot.IsEmpty() && slot.prefab != null && slot.prefab.itemID == chalkPrefab.itemID;
        Item shown = null;
        bool hasShown = reference.GetVisuals() != null && reference.GetVisuals().TryGetSpawnedItem(0, out shown);
        Check("the chalk goes into a " + what + " (and shows in it, in its bag)", allowed && inside && local.data.currentItem == null && hasShown && ClimbingChalkItem.IsChalk(shown) && HasBag(shown),
            $"may go in {allowed}, in slot 1 {inside}, still in the hand {local.data.currentItem != null}, shown {(hasShown ? shown.gameObject.name : "nothing")}");
        if (hasShown)
        {
            Check("in the " + what + " the chalk is shown at 0.8 of its size (the game: 0.5)", Mathf.Abs(shown.transform.localScale.x - 0.8f) < 0.001f, $"scale {shown.transform.localScale.x:0.00}");
            // A look down at it, for the screenshot.
            Vector2 look = local.data.lookValues;
            local.data.lookValues = new Vector2(Quaternion.LookRotation(backpack.transform.position - local.Head).eulerAngles.y, -50f);
            yield return new WaitForSeconds(0.4f);
            Screenshot("chalk-in-" + what.Replace(' ', '-'));
            yield return new WaitForSeconds(0.2f);
            local.data.lookValues = look;
        }
        if (hasShown)
        {
            Step("chalk out of the " + what, () => shown.Interact(local));
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && (local.data.currentItem == null || !ClimbingChalkItem.IsChalk(local.data.currentItem)))
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            held = local.data.currentItem;
            BackpackData after = reference.GetData();
            Check("the chalk comes back out of the " + what + " into the hand", held != null && ClimbingChalkItem.IsChalk(held) && after.itemSlots[0].IsEmpty(),
                $"holding {(held != null ? held.gameObject.name : "nothing")}, slot empty {after.itemSlots[0].IsEmpty()}");
        }
        if (backpack != null)
        {
            PhotonNetwork.Destroy(backpack.gameObject);
        }
    }

    /// <summary>A Jetpack on the ground burns the chalk in the hand as fuel, as its backpack wheel does it (35% less from ascent 15).</summary>
    private IEnumerator ChalkAsFuel(Item chalkPrefab)
    {
        Character local = Local;
        // The Jetpack (the Rocketpack is another item): the backpack with a fuel tank.
        Item jetpackPrefab = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i is Backpack && i.GetComponent<JetpackItem>() != null);
        if (jetpackPrefab == null)
        {
            Info("no Jetpack in the item database for the chalk");
            yield break;
        }
        yield return SpawnIntoHand(chalkPrefab);
        if (held == null)
        {
            yield break;
        }
        Item chalk = held;
        GameObject spawnedJetpack = SpawnInFront(jetpackPrefab, 1.2f);
        Backpack jetpack = spawnedJetpack != null ? spawnedJetpack.GetComponent<Backpack>() : null;
        JetpackItem tank = spawnedJetpack != null ? spawnedJetpack.GetComponentInChildren<JetpackItem>(true) : null;
        if (jetpack == null || tank == null)
        {
            Fail("put a Jetpack on the ground for the chalk", $"{jetpackPrefab.gameObject.name}: spawned {(spawnedJetpack != null ? spawnedJetpack.name : "nothing")}, backpack {jetpack != null}, tank {tank != null}");
            yield break;
        }
        yield return new WaitForSeconds(1f);
        float fuelBefore = 0f;
        Step("chalk as fuel", () =>
        {
            // Room in the tank for what the chalk gives.
            FloatItemData fuel = tank.GetData(DataEntryKey.Fuel, tank.SetupDefaultFuel);
            fuel.Value = 20f;
            tank.fuel = 20f;
            fuelBefore = fuel.Value;
            jetpack.AddFuel(local);
        });
        yield return new WaitForSeconds(1f);
        float added = tank.GetData(DataEntryKey.Fuel, tank.SetupDefaultFuel).Value - fuelBefore;
        float expected = 25f * (Rules.Enabled(Lvl.WorseItems) ? A14_Items.JetpackFuelShare : 1f);
        Check("the chalk burns as Jetpack fuel (" + expected + " fuel)", Mathf.Abs(added - expected) < 0.01f && UsedUp(chalk, chalkPrefab),
            $"+{added:0.##} fuel, used up {UsedUp(chalk, chalkPrefab)}");
        if (jetpack != null)
        {
            PhotonNetwork.Destroy(jetpack.gameObject);
        }
    }

    /// <summary>An item put on the ground in front of the scout, the way the game spawns items.</summary>
    private GameObject SpawnInFront(Item prefab, float distance)
    {
        Character local = Local;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        Vector3 position = local.Center + forward * distance + Vector3.up * 0.3f;
        return PhotonNetwork.InstantiateItemRoom(prefab.gameObject.name, position, Quaternion.LookRotation(-forward));
    }

    /// <summary>The uses an item has left (its own data, as the game keeps it).</summary>
    private static int UsesLeft(Item item)
    {
        OptionableIntItemData uses = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        return uses.HasData ? uses.Value : -1;
    }

    /// <summary>How full the item's use bar is.</summary>
    private static float UseBar(Item item)
    {
        return item.GetData<FloatItemData>(DataEntryKey.UseRemainingPercentage).Value;
    }

    private const string ChalkDustName = "UltimateAscent Chalk Dust(Clone)";

    /// <summary>Every chalk dust cloud there is now.</summary>
    private static HashSet<GameObject> DustClouds()
    {
        return new HashSet<GameObject>(FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
            .Where(particles => particles != null && particles.gameObject.name == ChalkDustName)
            .Select(particles => particles.gameObject));
    }

    /// <summary>A chalk dust cloud that was not there before.</summary>
    private static GameObject NewDust(HashSet<GameObject> earlier)
    {
        return DustClouds().FirstOrDefault(dust => !earlier.Contains(dust));
    }

    /// <summary>How long the scout's chalk effect has run (-1 without one).</summary>
    private float ChalkElapsed()
    {
        return Afflictions.HasAfflictionType(Peak.Afflictions.Affliction.AfflictionType.ClimbingChalk, out Peak.Afflictions.Affliction chalk) ? chalk.timeElapsed : -1f;
    }

    /// <summary>
    /// A chalk on the ground is cooked the way a fire does it: once (brown, still usable), then three
    /// more times (burned, then incinerated, almost black), like every item; its bag's colour is
    /// checked, and that cooking gave it no food changes and left a burned chalk unusable.
    /// </summary>
    private IEnumerator ChalkCooked(Item chalkPrefab)
    {
        GameObject spawned = SpawnInFront(chalkPrefab, 1.5f);
        Item chalk = spawned != null ? spawned.GetComponent<Item>() : null;
        if (chalk == null)
        {
            Fail("put a Climbing Chalk on the ground to cook it");
            yield break;
        }
        yield return new WaitForSeconds(0.5f);
        Color BagColour()
        {
            Transform cube = chalk != null ? chalk.transform.Find("Cube") : null;
            MeshRenderer renderer = cube != null ? cube.GetComponent<MeshRenderer>() : null;
            return renderer != null ? renderer.material.GetColor("_Tint") : Color.clear;
        }
        bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
        Color fresh = BagColour();
        chalk.cooking.FinishCooking();
        yield return new WaitForSeconds(0.3f);
        Color cooked = BagColour();
        bool usableCooked = chalk != null && chalk.CanUsePrimary();
        // Burned (three cookings), then incinerated (four), where the game would make food poisonous.
        for (int i = 0; i < 3 && chalk != null; i++)
        {
            chalk.cooking.FinishCooking();
            yield return new WaitForSeconds(0.3f);
        }
        Screenshot("chalk-4-burned");
        Color burned = BagColour();
        int times = chalk != null ? chalk.GetData<IntItemData>(DataEntryKey.CookedAmount).Value : -1;
        Color tint = ClimbingChalkModel.BagTint;
        Check("cooking darkens the chalk bag like any item (its tint): brown once cooked, almost black when burned",
            Near(fresh, tint) && Near(cooked, tint * ItemCooking.GetCookColor(1)) && Near(burned, tint * ItemCooking.GetCookColor(4)) && times == 4,
            $"fresh {fresh}, cooked once {cooked}, cooked {times} times {burned}");
        int poisons = chalk != null ? chalk.GetComponents<Action_ModifyStatus>().Count(a => a.statusType == CharacterAfflictions.STATUSTYPE.Poison) : -1;
        int stamina = chalk != null ? chalk.GetComponents<Action_GiveExtraStamina>().Length : -1;
        bool usableBurned = chalk != null && (chalk.CanUsePrimary() || chalk.CanUseSecondary());
        Check("cooking gives the chalk no food changes (no poison, no extra stamina), and a burned chalk cannot be used",
            usableCooked && poisons == 0 && stamina == 0 && !usableBurned,
            $"usable once cooked {usableCooked}, after {times} cookings: poison actions {poisons}, extra-stamina actions {stamina}, usable {usableBurned}");
        if (chalk != null)
        {
            PhotonNetwork.Destroy(chalk.gameObject);
        }
    }

    /// <summary>The chalk left the hand and the inventory (a used-up item is destroyed, which a plain == cannot tell from null).</summary>
    private bool UsedUp(Item chalk, Item chalkPrefab)
    {
        Character local = Local;
        bool inHand = !ReferenceEquals(local.data.currentItem, null) && ReferenceEquals(local.data.currentItem, chalk) && chalk != null && chalk.gameObject.activeInHierarchy;
        bool inSlots = local.player.itemSlots.Any(slot => !slot.IsEmpty() && slot.prefab != null && slot.prefab.itemID == chalkPrefab.itemID);
        return !inHand && !inSlots;
    }

    /// <summary>The chalk dust's particles as the camera sees them, with screenshots (diagnostics).</summary>
    private IEnumerator WatchDust(GameObject dust)
    {
        float born = Time.time;
        foreach (float at in new[] { 0.15f, 0.6f, 1.2f })
        {
            yield return new WaitForSeconds(Mathf.Max(0f, born + at - Time.time));
            if (dust == null)
            {
                Info("chalk dust at " + at + " s", "gone");
                yield break;
            }
            ParticleSystem particles = dust.GetComponent<ParticleSystem>();
            ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[particles.main.maxParticles];
            int count = particles.GetParticles(buffer);
            Camera camera = Camera.main;
            float near = float.MaxValue;
            float far = 0f;
            float alpha = 0f;
            float size = 0f;
            int inFront = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 world = particles.main.simulationSpace == ParticleSystemSimulationSpace.Local ? dust.transform.TransformPoint(buffer[i].position) : buffer[i].position;
                float distance = camera != null ? Vector3.Distance(world, camera.transform.position) : 0f;
                near = Mathf.Min(near, distance);
                far = Mathf.Max(far, distance);
                alpha += buffer[i].GetCurrentColor(particles).a / 255f;
                size += buffer[i].GetCurrentSize(particles);
                if (camera != null && Vector3.Dot(world - camera.transform.position, camera.transform.forward) > camera.nearClipPlane)
                {
                    inFront++;
                }
            }
            Info("chalk dust at " + at + " s", count == 0 ? "no particles" :
                $"{count} particles ({inFront} in front of the camera), {near:0.0}-{far:0.0} m from it, mean alpha {alpha / count:0.00}, mean size {size / count:0.0} m, space {particles.main.simulationSpace}, material {dust.GetComponent<ParticleSystemRenderer>().sharedMaterial?.name}");
            Screenshot("chalk-dust-" + Mathf.RoundToInt(at * 100));
        }
    }

    private IEnumerator ReferenceSound(float after)
    {
        yield return new WaitForSeconds(after);
        SFX_Instance eat = ItemByName("Marshmallow")?.GetComponent<ItemUseFeedback>()?.sfxUsed;
        if (eat != null)
        {
            eat.Play(Local.refs.carryPosRef.position, Local.refs.carryPosRef);
            Info("reference sound", $"{eat.name} (volume {eat.settings.volume:0.##}) at {after:0.#} s after the use");
        }
    }

    private IEnumerator StopRecording(ListenerRecorder recorder, float after, string name)
    {
        yield return new WaitForSeconds(after);
        string path = Path.Combine(Paths.BepInExRootPath, "UltimateAscent.SmokeTest." + name + ".wav");
        int written = recorder.End(path);
        Info("recording", $"{Path.GetFileName(path)}: {written} samples at {AudioSettings.outputSampleRate} Hz");
    }

    /// <summary>The whitening has faded in by then (0.4 s).</summary>
    private const float WhitenCheckDelay = 0.8f;

    /// <summary>Saves what the screen shows to BepInEx/UltimateAscent.SmokeTest.NAME.png (written at the end of the frame).</summary>
    private void Screenshot(string name)
    {
        string path = Path.Combine(Paths.BepInExRootPath, "UltimateAscent.SmokeTest." + name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        Info("screenshot", Path.GetFileName(path));
    }

    /// <summary>Takes the first item from the inventory into the hand (the inventory stays as it is); with nothing there, an Energy Drink.</summary>
    private IEnumerator EquipAnything()
    {
        Character local = Local;
        for (byte slot = 0; slot < local.player.itemSlots.Length; slot++)
        {
            if (local.player.itemSlots[slot].IsEmpty())
            {
                continue;
            }
            local.refs.items.lastEquippedSlotTime = 0f;
            local.refs.items.EquipSlot(Optionable<byte>.Some(slot));
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until && local.data.currentItem == null)
            {
                yield return null;
            }
            yield break;
        }
        // An empty inventory (a run of this step alone): something to hold.
        yield return SpawnIntoHand(ItemByName("Energy Drink"));
    }

    /// <summary>The stamina the climbing lunge (the game's own climbing method) costs right now.</summary>
    private float LungeCost()
    {
        float cost = 0f;
        Unlocked(() =>
        {
            Local.data.currentStamina = 1f;
            Local.refs.climbing.RPCA_ClimbJump();
            cost = 1f - Local.data.currentStamina;
            Local.data.currentStamina = 1f;
        });
        return cost;
    }

    /// <summary>The chalk's white copy of the scout's hands, if it is there.</summary>
    private static SkinnedMeshRenderer WhiteHands()
    {
        Transform body = Local.refs.customization.refs.mainRenderer.transform;
        Transform hands = body.Find("UltimateAscent Chalked Hands");
        return hands != null ? hands.GetComponent<SkinnedMeshRenderer>() : null;
    }

    /// <summary>
    /// A campfire save (the game writes it as a fire is lit, adding the fire's effects itself) holds the
    /// petrification lighting the fire takes off: with 40 on the scout's record, 30 saved with Ascent 17 on, 20 with
    /// it off (in memory); the arrows (the status the game's save hit instead) stay as they were, in the save and in
    /// the scout's record; and the record keeps its 40, so a second save made from it holds the same.
    /// </summary>
    private void CampfireSavePetrify()
    {
        if (Rules.Level < Lvl.HotCampfires)
        {
            Info("the campfire save check is skipped", $"testing ascent {target}");
            return;
        }
        Character local = Local;
        ReconnectHandler records = Singleton<ReconnectHandler>.Instance;
        string id = local.player != null ? Peak.Network.NetworkingUtilities.GetUserId(local.player) : null;
        if (records == null || string.IsNullOrEmpty(id))
        {
            Fail("the game keeps a record of the scout for its campfire saves", $"handler {records != null}, user id '{id}'");
            return;
        }
        ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches[Lvl.HotCampfires];
        bool saved = rule.Value;
        int petrifyBefore = local.data.petrifyAmount;
        const int Arrows = (int)Status.Arrow;
        try
        {
            local.data.SetPetrify(40);
            records.UpdateReconnectData(local);
            local.data.SetPetrify(petrifyBefore);
            if (!ReconnectHandler.TryGetReconnectData(id, out ReconnectData record, out _) || record.currentStatuses == null)
            {
                Fail("the game keeps a record of the scout for its campfire saves", "no valid record after updating it");
                return;
            }
            record.currentStatuses[Arrows] = 0.3f;
            (int petrify, float arrows) Save(bool hotCampfires)
            {
                rule.Value = hotCampfires;
                HostOptions.Invalidate();
                if (!Peak.Quicksave.PlayerRunData.TryCreateFromReconnectData(id, out Peak.Quicksave.PlayerRunData runData))
                {
                    return (-1, -1f);
                }
                ReconnectData written = runData.GetReconnectData();
                return (written.petrify, written.currentStatuses[Arrows]);
            }
            (int petrify, float arrows) on = Save(true);
            (int petrify, float arrows) again = Save(true);
            (int petrify, float arrows) off = Save(false);
            ReconnectHandler.TryGetReconnectData(id, out ReconnectData after, out _);
            Check("a campfire save holds the petrification lighting the fire takes off: 10 (17), 20 with Ascent 17 off; the arrows untouched; saved again, the same",
                on.petrify == 30 && again.petrify == 30 && off.petrify == 20 && Mathf.Abs(on.arrows - 0.3f) < 0.001f && Mathf.Abs(off.arrows - 0.3f) < 0.001f
                && after.petrify == 40 && Mathf.Abs(after.currentStatuses[Arrows] - 0.3f) < 0.001f,
                $"40 on the record; saved {on.petrify} (Ascent 17 on), {again.petrify} saved again, {off.petrify} (off); arrows saved {on.arrows * 100f:0.#}/{off.arrows * 100f:0.#} of 30; "
                + $"the record afterwards: petrification {after.petrify}, arrows {after.currentStatuses[Arrows] * 100f:0.#}");
            record.currentStatuses[Arrows] = 0f;
        }
        finally
        {
            rule.Value = saved;
            HostOptions.Invalidate();
            local.data.SetPetrify(petrifyBefore);
            records.UpdateReconnectData(local);
        }
    }

    private IEnumerator BurnedItemHeat()
    {
        if (held == null || Local.data.currentItem != held)
        {
            Info("burned item check skipped: nothing in the hand");
            yield break;
        }
        Item item = held;
        // One burn tick: campfire flames (0.1 heat/s) for half a second, x1.5 (ascent 14). Other heat
        // (the sun, Apocalypse weather) is not counted.
        const float TickHeat = 0.1f * 0.5f * 1.5f;
        Func<float, bool> burnTick = amount => Mathf.Abs(amount - TickHeat) < 0.0005f;
        Shield(true);
        Meter normal;
        Meter burned;
        float burnTime = Time.time;
        try
        {
            normal = new Meter(Status.Hot, burnTick);
            yield return new WaitForSeconds(2f);
            normal.Stop();

            // Cooked three times, the way a fire does it: the item turns black for everyone.
            for (int i = 0; i < BurnedItems.BurntCookLevel && item != null && !BurnedItems.IsBurned(item); i++)
            {
                item.cooking.FinishCooking();
                burnTime = Time.time;
                yield return new WaitForSeconds(0.2f);
            }
            Check("an item cooked 3 times is burned", item != null && BurnedItems.IsBurned(item) && item.cooking.timesCookedLocal >= BurnedItems.BurntCookLevel,
                item == null ? "the item is gone" : $"cooked {item.GetData<IntItemData>(DataEntryKey.CookedAmount).Value}");
            burned = new Meter(Status.Hot, burnTick);
            yield return new WaitForSeconds(2f);
            burned.Stop();
        }
        finally
        {
            Protect(true);
        }
        float expected = 0.1f * 1.5f * burned.Seconds;
        Check("holding a freshly burned item burns like a campfire (17)", normal.Count == 0 && burned.Total > expected * 0.6f && burned.Total < expected * 1.25f,
            $"{normal.Count} burn ticks in {normal.Seconds:F1} s holding a normal item; {burned.Count} ticks, {burned.Total:0.000} heat in {burned.Seconds:F1} s holding a burned one (campfire rate {expected:0.000})");
        yield return Cooled(burnTime, "a burned item in the hand cools down after 10 s (17)", burnTick);
    }

    /// <summary>10.5 to 12.5 s after something got burned, it no longer burns.</summary>
    private IEnumerator Cooled(float burnTime, string name, Func<float, bool> burnTick)
    {
        yield return WaitUntilTime(burnTime + BurnedItems.HotSeconds + 0.5f);
        Shield(true);
        Meter cooled = new Meter(Status.Hot, burnTick);
        try
        {
            yield return new WaitForSeconds(2f);
        }
        finally
        {
            cooled.Stop();
            Protect(true);
        }
        Check(name, cooled.Count == 0, $"{cooled.Count} burn ticks from {BurnedItems.HotSeconds + 0.5f:F1} to {BurnedItems.HotSeconds + 2.5f:F1} s after it got burned");
    }

    private static Collider BodyCollider(Character character, BodypartType type)
    {
        foreach (KeyValuePair<Collider, Character> pair in CharacterRagdoll.COLLIDERS_TO_CHARACTERS)
        {
            if (pair.Value == character && pair.Key != null)
            {
                Bodypart part = pair.Key.GetComponentInParent<Bodypart>();
                if (part != null && part.partType == type)
                {
                    return pair.Key;
                }
            }
        }
        return null;
    }

    /// <summary>The scout walks into a tick spot with one body part (the game's trigger, called directly).</summary>
    private static void BrushTickSpot(Character character, Collider bodyPart)
    {
        GameObject spot = new GameObject("UltimateAscent.SmokeTest.TickSpot");
        spot.transform.position = bodyPart.bounds.center + character.data.lookDirection_Flat * 0.4f;
        TickTrigger trigger = spot.AddComponent<TickTrigger>();
        trigger.tickChance = 1f;
        trigger.OnTriggerEnter(bodyPart);
        if (spot != null)
        {
            Destroy(spot);
        }
    }

    /// <summary>
    /// Ticks latch onto the body part that brushed them (like cacti), are visible at once, poison
    /// every half second, block poison healing, and at most five ride on a scout. Pulling one off puts
    /// it in the hand, ready to eat.
    /// </summary>
    private IEnumerator TickBites()
    {
        Character local = Local;
        RemoveTicks(local);
        yield return null;
        Collider knee = BodyCollider(local, BodypartType.Knee_L) ?? CharacterRagdoll.COLLIDERS_TO_CHARACTERS.FirstOrDefault(pair => pair.Value == local && pair.Key != null).Key;
        if (knee == null)
        {
            Fail("ticks", "no body part collider of the scout found");
            yield break;
        }
        Bodypart touched = knee.GetComponentInParent<Bodypart>();
        BrushTickSpot(local, knee);
        yield return new WaitForSeconds(1f);
        Bugfix tick = Bugfix.AllAttachedBugs.FirstOrDefault(pair => pair.Value == local).Key;
        Check("a tick latches on (17)", tick != null);
        if (tick == null)
        {
            yield break;
        }
        Transform bone = local.GetBodypartRig(touched.partType).transform;
        float gap = Vector3.Distance(tick.transform.position, knee.ClosestPoint(tick.transform.position));
        Check("the tick sits on the body part that touched it, like a cactus (17)", tick.leg == bone && gap < 0.15f,
            $"on {(tick.leg != null ? tick.leg.name : "nothing")} (touched with {touched.partType}), {gap:0.00} m off its surface");
        Check("the fresh tick is visible (17)", tick.transform.localScale.x >= 0.55f, $"size {tick.transform.localScale.x:0.00} (the game starts ticks at 0)");
        // Growth, fast-forwarded: half a minute and a whole minute on the scout.
        float age = tick.lifeTime;
        tick.lifeTime = 30f;
        yield return null;
        float halfMinute = tick.transform.localScale.x;
        tick.lifeTime = 60f;
        yield return null;
        float minute = tick.transform.localScale.x;
        tick.lifeTime = age;
        Check("a tick swells to 3x its full size within a minute (17)", Mathf.Abs(halfMinute - 1.8f) < 0.1f && Mathf.Abs(minute - 3f) < 0.1f,
            $"size {halfMinute:0.00} after 30 s, {minute:0.00} after 60 s (the game: 0.10 and 0.20)");
        Item inHand = local.data.currentItem;
        Check("no tick appears in the hands", inHand == null || !A14_WorseItems.Ticks.IsTick(inHand), $"holding {(inHand != null ? inHand.gameObject.name : "nothing")}");

        Shield(true);
        Meter poison = new Meter(Status.Poison);
        try
        {
            yield return new WaitForSeconds(6f);
        }
        finally
        {
            poison.Stop();
        }
        Protect(true);
        // A bite lands 2.5 poison (ascent 14's x1.75 included), every 0.5 s (the game: 5 every 29 s), and
        // the tick keeps biting (the game stops after 10 bites once your poison is 50).
        float perBite = poison.Count > 0 ? poison.Total / poison.Count : 0f;
        Check("a tick bite is 2.5 poison (17)", poison.Count >= 3 && Mathf.Abs(perBite - 0.025f) < 0.0005f,
            $"{poison.Count} bites in {poison.Seconds:F1} s, {perBite * 100f:0.00} poison each");
        Check("a tick keeps biting (17)", poison.Count >= 11, $"{poison.Count} bites in {poison.Seconds:F1} s (the game stops after 10)");

        // Six more tick spots brushed at once by the same foot: only four more latch on, and none
        // of them on top of another tick.
        Collider foot = BodyCollider(local, BodypartType.Foot_L) ?? knee;
        for (int i = 0; i < 6; i++)
        {
            BrushTickSpot(local, foot);
        }
        yield return new WaitForSeconds(1.5f);
        List<Bugfix> onScout = Bugfix.AllAttachedBugs.Where(pair => pair.Value == local).Select(pair => pair.Key).ToList();
        Check("at most five ticks at once (17)", onScout.Count == 5, $"{onScout.Count} ticks on the scout after six more tick spots at once");
        float closest = float.MaxValue;
        for (int a = 0; a < onScout.Count; a++)
        {
            for (int b = a + 1; b < onScout.Count; b++)
            {
                closest = Mathf.Min(closest, Vector3.Distance(onScout[a].transform.position, onScout[b].transform.position));
            }
        }
        Check("ticks picked up at once sit in different places (17)", onScout.Count > 1 && closest >= 0.12f,
            $"closest two {closest:0.00} m apart; on {string.Join(", ", onScout.Select(bug => bug.leg != null ? bug.leg.name : "?"))}");

        // Pull one off, the way a player does: it lands in the hand.
        Item tickItem = TickItem();
        Bugfix first = onScout.FirstOrDefault();
        RemoveTicks(local, keep: first);
        if (first == null || tickItem == null)
        {
            yield break;
        }
        first.Interact(local);
        float end = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < end && (local.data.currentItem == null || local.data.currentItem.itemID != tickItem.itemID))
        {
            yield return null;
        }
        // Removed ticks are gone at the end of the frame.
        yield return new WaitForSeconds(0.3f);
        held = local.data.currentItem;
        int left = Bugfix.AllAttachedBugs.Count(pair => pair.Value == local);
        Check("pulling a tick off puts it in the hand", held != null && held.itemID == tickItem.itemID && left == 0,
            $"holding {(held != null ? held.gameObject.name : "nothing")}, {left} tick(s) left on the scout");

        // Poison does not heal for 10 s after the last bite.
        Protect(false);
        Afflictions.SetStatus(Status.Poison, 0.3f);
        float poisonBefore = Afflictions.GetCurrentStatus(Status.Poison);
        yield return new WaitForSeconds(3f);
        float poisonAfter = Afflictions.GetCurrentStatus(Status.Poison);
        bool blocked = A17_Hazards.PoisonHealingBlocked;
        float sinceBite = Time.time - A17_Hazards.LastTickBite;
        Afflictions.SetStatus(Status.Poison, 0f);
        Protect(true);
        Check("after a tick bite, poison does not heal for 10 s (17)", blocked && poisonAfter >= poisonBefore - 0.001f,
            $"poison {poisonBefore * 100f:F1} -> {poisonAfter * 100f:F1} over 3 s, {sinceBite:F1} s after the last bite");
    }

    private static void RemoveTicks(Character character, Bugfix keep = null)
    {
        foreach (Bugfix bug in Bugfix.AllAttachedBugs.Where(pair => pair.Value == character).Select(pair => pair.Key).ToList())
        {
            if (bug != null && bug != keep && bug.photonView != null && bug.photonView.IsMine)
            {
                PhotonNetwork.Destroy(bug.gameObject);
            }
        }
    }

    private IEnumerator BingBong(float wake)
    {
        // Nobody has held Bing Bong since waking up: from 10 s he talked in his burned voice, and fell
        // silent when the crab status came at 15 s. His rule was held back until this step (HoldBingBongRule),
        // so his neglect counts from now.
        yield return WaitUntilTime(Mathf.Max(wake, Time.time) + 17f);
        Check("unheld, Bing Bong talks in his burned voice from 10 s, and falls silent when the crab comes (20)", A20_BingBong.LinesSpoken > 0 && !A20_BingBong.Speaking && ShakeCounter.Count == 0,
            $"{A20_BingBong.LinesSpoken} line(s) so far, talking now {A20_BingBong.Speaking} (neglected for {A20_BingBong.neglectedFor:F0} s), voice pitch {A20_BingBong.VoicePitch:0.###} volume {A20_BingBong.VoiceVolume:0.###}, {ShakeCounter.Count} shake(s)");
        Protect(false);
        Afflictions.SetStatus(Status.Crab, 0f);
        Meter crab = new Meter(Status.Crab);
        try
        {
            yield return new WaitForSeconds(1.5f);
        }
        finally
        {
            crab.Stop();
            Afflictions.SetStatus(Status.Crab, 0f);
            Protect(true);
        }
        Check("a neglected Bing Bong brings crab status (20)", crab.Total > 0.05f, $"neglected for {A20_BingBong.neglectedFor:F1} s, {crab.Total:0.000} crab in {crab.Seconds:F1} s");

        // A cure-all used on the crab status leaves it (the item's own cure, run from the hand).
        Item cureAll = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values
            .Where(i => i != null && i.GetComponent<Action_ClearAllStatus>() is Action_ClearAllStatus cure && !cure.otherExclusions.Contains(Status.Crab))
            .OrderBy(i => i.gameObject.name.IndexOf("Cure", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
            .FirstOrDefault();
        if (cureAll == null)
        {
            Info("no cure-all item in the item database");
        }
        else
        {
            yield return SpawnIntoHand(cureAll);
            Item cure = held;
            if (cure != null)
            {
                float crabBefore = 0f;
                float crabAfter = 0f;
                float poisonAfter = 0f;
                Unlocked(() =>
                {
                    Afflictions.SetStatus(Status.Crab, 0.2f);
                    Afflictions.SetStatus(Status.Poison, 0.2f);
                    crabBefore = Afflictions.GetCurrentStatus(Status.Crab);
                    cure.GetComponent<Action_ClearAllStatus>().RunAction();
                    crabAfter = Afflictions.GetCurrentStatus(Status.Crab);
                    poisonAfter = Afflictions.GetCurrentStatus(Status.Poison);
                    Afflictions.SetStatus(Status.Crab, 0f);
                    Afflictions.SetStatus(Status.Poison, 0f);
                });
                Check("a cure-all leaves Bing Bong's crab status (20)", Mathf.Abs(crabAfter - crabBefore) < 0.001f && poisonAfter < 0.2f,
                    $"{cure.gameObject.name}: crab {crabBefore * 100f:F0} -> {crabAfter * 100f:F0}, poison 20 -> {poisonAfter * 100f:F0}");
                Local.refs.items.EquipSlot(Optionable<byte>.None);
                yield return new WaitForSeconds(0.3f);
                for (byte slot = 0; slot < Local.player.itemSlots.Length; slot++)
                {
                    ItemSlot itemSlot = Local.player.itemSlots[slot];
                    if (!itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == cureAll.itemID)
                    {
                        Local.player.EmptySlot(Optionable<byte>.Some(slot));
                    }
                }
                Afflictions.UpdateWeight();
                yield return new WaitForSeconds(0.3f);
            }
        }

        Item bingBong = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i != null && (i.itemTags & Item.ItemTags.BingBong) != 0);
        if (bingBong == null)
        {
            Info("no Bing Bong item in the item database");
            yield break;
        }
        yield return SpawnIntoHand(bingBong);
        Item doll = held;
        if (doll == null)
        {
            yield break;
        }
        // A normal Bing Bong held without a break, past both timers (10 s: he talks, 15 s: crab).
        ShakeCounter.Count = 0;
        int linesBefore = A20_BingBong.LinesSpoken;
        Shield(true);
        Meter heldCrab = new Meter(Status.Crab);
        float longest = 0f;
        float until = Time.time + 17.5f;
        try
        {
            while (Time.time < until)
            {
                longest = Mathf.Max(longest, A20_BingBong.neglectedFor);
                yield return null;
            }
        }
        finally
        {
            heldCrab.Stop();
            Protect(true);
        }
        Check("holding a normal Bing Bong keeps the evil away for good (20)", longest < 0.5f && A20_BingBong.LinesSpoken == linesBefore && ShakeCounter.Count == 0 && heldCrab.Total <= 0f,
            $"held for {heldCrab.Seconds:F1} s: timer at most {longest:F1} s, {A20_BingBong.LinesSpoken - linesBefore} line(s), {ShakeCounter.Count} shake(s), {heldCrab.Total:0.000} crab");

        // Cooked once, the way a fire does it: only cooked, not burned, so it still counts.
        doll.cooking.FinishCooking();
        yield return new WaitForSeconds(3f);
        Check("a Bing Bong cooked once still counts (20)", doll != null && !BurnedItems.IsBurned(doll) && A20_BingBong.neglectedFor < 0.5f,
            doll == null ? "Bing Bong is gone" : $"cooked {BurnedItems.CookedLevel(doll)}, timer {A20_BingBong.neglectedFor:F1} s after 3 s in the hand");

        // Cooked twice more: fully burned (three cooks).
        float burnTime = Time.time;
        for (int i = 0; i < BurnedItems.BurntCookLevel && doll != null && !BurnedItems.IsBurned(doll); i++)
        {
            doll.cooking.FinishCooking();
            burnTime = Time.time;
            yield return new WaitForSeconds(0.2f);
        }
        Check("Bing Bong is burned after three cooks", doll != null && BurnedItems.IsBurned(doll), doll == null ? "Bing Bong is gone" : $"cooked {BurnedItems.CookedLevel(doll)}");

        // Hold the burned Bing Bong until both timers have run out. It burns the hand while it is
        // freshly burned (10 s), then it has cooled down.
        const float BurnTickHeat = 0.1f * 0.5f * 1.5f;
        ShakeCounter.Count = 0;
        int burnedLines = A20_BingBong.LinesSpoken;
        float talkFrom = -1f;
        float talkTo = -1f;
        Shield(true);
        Meter burnedCrab = new Meter(Status.Crab);
        Meter burnedHeat = new Meter(Status.Hot, amount => Mathf.Abs(amount - BurnTickHeat) < 0.0005f);
        try
        {
            float done = Time.time + 17.5f;
            while (Time.time < done)
            {
                if (A20_BingBong.Speaking)
                {
                    talkFrom = talkFrom < 0f ? A20_BingBong.neglectedFor : talkFrom;
                    talkTo = A20_BingBong.neglectedFor;
                }
                yield return null;
            }
        }
        finally
        {
            burnedCrab.Stop();
            burnedHeat.Stop();
            Protect(true);
        }
        float neglected = A20_BingBong.neglectedFor;
        Check("holding only a burned Bing Bong, he talks in his burned voice from 10 s until the crab comes at 15 s, no shaking (20)",
            A20_BingBong.LinesSpoken > burnedLines && talkFrom >= 9.9f && talkFrom < 10.5f && talkTo < 15.05f && talkTo > 12f && ShakeCounter.Count == 0,
            $"{A20_BingBong.LinesSpoken - burnedLines} line(s), talking from {talkFrom:F1} s to {talkTo:F1} s of neglect, neglected for {neglected:F1} s, {ShakeCounter.Count} shake(s)");
        Check("holding a burned Bing Bong, crab status comes after 15 s (20)", burnedCrab.Total > 0.05f, $"{burnedCrab.Total:0.000} crab, neglected for {neglected:F1} s");
        float lastBurn = burnedHeat.LastAt - burnTime;
        Check("a burned Bing Bong burns the hand for 10 s, then cools down (17, 20)", burnedHeat.Count >= 15 && lastBurn <= BurnedItems.HotSeconds + 0.6f,
            $"{burnedHeat.Count} burn ticks, the last {lastBurn:F1} s after it got burned, none in the {burnedHeat.Seconds - lastBurn:F1} s after");
        Check("the burned Bing Bong stays burned in the hand (20)", doll != null && BurnedItems.IsBurned(doll) && Local.data.currentItem == doll,
            doll == null ? "Bing Bong is gone" : $"cooked {BurnedItems.CookedLevel(doll)}, in the hand {Local.data.currentItem == doll}");
        // Hands empty again: a burned item in the hand would heat the scout during the next checks.
        Local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.5f);

        // A fresh Bing Bong, put away into a slot: after 10 s he talks; taking him in the hand silences him.
        yield return SpawnIntoHand(bingBong);
        Item fresh = held;
        byte freshSlot = 255;
        for (byte slot = 0; fresh != null && slot < Local.player.itemSlots.Length; slot++)
        {
            ItemSlot itemSlot = Local.player.itemSlots[slot];
            if (!itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == bingBong.itemID && Local.refs.items.currentSelectedSlot.IsSome && Local.refs.items.currentSelectedSlot.Value == slot)
            {
                freshSlot = slot;
            }
        }
        if (fresh == null || freshSlot == 255)
        {
            Info("the pick-up check is skipped", fresh == null ? "no fresh Bing Bong" : "its slot was not found");
            yield break;
        }
        Step("put Bing Bong away", () => Local.refs.items.EquipSlot(Optionable<byte>.None));
        float talkUntil = Time.time + 13f;
        while (!A20_BingBong.Speaking && Time.time < talkUntil)
        {
            yield return null;
        }
        float talkingAt = A20_BingBong.neglectedFor;
        bool talking = A20_BingBong.Speaking;
        Local.refs.items.lastEquippedSlotTime = 0f;
        Step("take Bing Bong in the hand while he talks", () => Local.refs.items.EquipSlot(Optionable<byte>.Some(freshSlot)));
        float silentUntil = Time.time + 1.5f;
        while (A20_BingBong.Speaking && Time.time < silentUntil)
        {
            yield return null;
        }
        Check("taking Bing Bong in the hand silences him at once (20)", talking && !A20_BingBong.Speaking && Time.time < silentUntil && A20_BingBong.neglectedFor < 0.5f,
            $"talking after {talkingAt:F1} s put away {talking}; in the hand {(Local.data.currentItem != null ? Local.data.currentItem.GetItemName() : "nothing")}, still talking {A20_BingBong.Speaking}, timer {A20_BingBong.neglectedFor:F1} s");
        Local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>
    /// The test leaves the player's account and save alone: no Steam stats, no achievements and no run
    /// save (lighting a campfire counts a morale boost, throws the area's achievement and saves the run,
    /// in the folder Steam Cloud syncs).
    /// </summary>
    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.IncrementSteamStat))]
    internal static class NoSteamStats
    {
        private static bool Prefix(ref int __result)
        {
            __result = 0;
            return false;
        }
    }

    /// <summary>Steam stats set directly (the cosmetics a player picks, the highest ascent at a win): not written, only noted.</summary>
    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.SetSteamStat))]
    internal static class NoSteamStatWrites
    {
        internal static readonly List<string> Asked = new List<string>();

        private static bool Prefix(STEAMSTATTYPE steamStatType, int value)
        {
            Asked.Add(steamStatType + "=" + value);
            return false;
        }
    }

    /// <summary>A climb that ends (the test's win) would delete the player's saved run.</summary>
    [HarmonyPatch(typeof(Peak.Quicksave), nameof(Peak.Quicksave.DestroySaveData))]
    internal static class NoRunSaveDeletion
    {
        private static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.ThrowAchievement))]
    internal static class NoAchievements
    {
        private static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(Peak.Quicksave), nameof(Peak.Quicksave.SaveNow))]
    internal static class NoRunSave
    {
        internal static int Skipped;

        private static bool Prefix()
        {
            Skipped++;
            return false;
        }
    }

    /// <summary>
    /// While the test's suitcase fills, the run is a custom run with only one item switched on, in
    /// memory (the game saves the switches only when one is clicked on the Custom Run screen).
    /// </summary>
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnItems))]
    internal static class CustomRunSpawn
    {
        internal static Spawner Target;
        internal static string OnlyItem;
        internal static bool Restored = true;
        private static Dictionary<string, int> saved;
        private static bool wasCustom;

        private static void Prefix(Spawner __instance)
        {
            if (Target == null || __instance != Target || OnlyItem == null)
            {
                return;
            }
            wasCustom = RunSettings.IsCustomRun;
            saved = new Dictionary<string, int>();
            foreach (KeyValuePair<string, RunSettings.RunSetting> setting in RunSettings.runSettingsDict)
            {
                if (setting.Key.StartsWith(RunSettings.ITEM_PREFIX, StringComparison.Ordinal))
                {
                    saved[setting.Key] = setting.Value.currentValue;
                    setting.Value.currentValue = setting.Key == RunSettings.ITEM_PREFIX + OnlyItem ? 1 : 0;
                }
            }
            Restored = false;
            RunSettings.IsCustomRun = true;
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (saved != null)
            {
                foreach (KeyValuePair<string, int> value in saved)
                {
                    RunSettings.runSettingsDict[value.Key].currentValue = value.Value;
                }
                RunSettings.IsCustomRun = wasCustom;
                saved = null;
                Restored = true;
            }
            return __exception;
        }
    }

    /// <summary>Marks what the rising fog adds itself: it applies its status while it updates its look, every frame.</summary>
    [HarmonyPatch(typeof(FogSphere), nameof(FogSphere.SetSharderVars))]
    internal static class FogCalls
    {
        internal static int Depth;

        private static void Prefix() => Depth++;

        private static void Finalizer() => Depth--;
    }

    /// <summary>Counts the poison screen effects (each one shakes the screen).</summary>
    [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.PoisonFX))]
    internal static class PoisonFxCounter
    {
        internal static int Count;

        private static void Postfix()
        {
            Count++;
        }
    }

    /// <summary>Counts the world shake of ascent 20 (amount 0.7).</summary>
    [HarmonyPatch(typeof(GamefeelHandler), nameof(GamefeelHandler.AddPerlinShake))]
    internal static class ShakeCounter
    {
        internal static int Count;

        private static void Postfix(float amount)
        {
            if (Mathf.Approximately(amount, 0.7f))
            {
                Count++;
            }
        }
    }

    /// <summary>
    /// A burned backpack worn on the back (the fourth slot) burns like a campfire too. The backpack is
    /// dropped next to the scout, charred three times the way a fire does it, then put on.
    /// </summary>
    private IEnumerator BackpackHeat()
    {
        Character local = Local;
        Item backpackPrefab = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i is Backpack);
        if (backpackPrefab == null)
        {
            Info("no backpack in the item database");
            yield break;
        }
        if (!local.player.backpackSlot.IsEmpty())
        {
            Info("the scout already wears a backpack");
            yield break;
        }
        // Empty hands, so only the backpack can burn the scout.
        local.refs.items.EquipSlot(Optionable<byte>.None);
        Vector3 spot = local.Center + local.data.lookDirection_Flat * 1.2f;
        Backpack bag = PhotonNetwork.InstantiateItemRoom(backpackPrefab.gameObject.name, spot, Quaternion.identity).GetComponent<Backpack>();
        yield return new WaitForSeconds(0.5f);
        float burnTime = Time.time;
        for (int i = 0; i < BurnedItems.BurntCookLevel && bag != null && !BurnedItems.IsBurned(bag); i++)
        {
            bag.cooking.FinishCooking();
            burnTime = Time.time;
            yield return new WaitForSeconds(0.2f);
        }
        Check("the backpack on the ground is burned after three cooks", bag != null && BurnedItems.IsBurned(bag), bag == null ? "the backpack is gone" : $"cooked {bag.GetData<IntItemData>(DataEntryKey.CookedAmount).Value}");
        if (bag == null)
        {
            yield break;
        }
        local.refs.items.lastEquippedSlotTime = 0f;
        bag.Wear(local);
        float end = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < end && local.player.backpackSlot.IsEmpty())
        {
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        Item inHand = local.data.currentItem;
        Check("the burned backpack is worn on the back (fourth slot), not held", !local.player.backpackSlot.IsEmpty() && BurnedItems.IsBurned(local.player.backpackSlot.data) && !(inHand is Backpack),
            $"back slot {(local.player.backpackSlot.IsEmpty() ? "empty" : local.player.backpackSlot.prefab.gameObject.name)}, burned {BurnedItems.IsBurned(local.player.backpackSlot.data)}, in the hand {(inHand != null ? inHand.gameObject.name : "nothing")}");
        if (local.player.backpackSlot.IsEmpty())
        {
            yield break;
        }
        const float TickHeat = 0.1f * 0.5f * 1.5f;
        Shield(true);
        Meter heat = new Meter(Status.Hot, amount => Mathf.Abs(amount - TickHeat) < 0.0005f);
        try
        {
            yield return new WaitForSeconds(2f);
        }
        finally
        {
            heat.Stop();
            Protect(true);
        }
        float expected = 0.1f * 1.5f * heat.Seconds;
        Check("wearing a freshly burned backpack burns like a campfire (17)", heat.Total > expected * 0.6f && heat.Total < expected * 1.25f,
            $"{heat.Count} burn ticks, {heat.Total:0.000} heat in {heat.Seconds:F1} s with empty hands (campfire rate {expected:0.000})");
        yield return Cooled(burnTime, "a burned backpack on the back cools down after 10 s (17)", amount => Mathf.Abs(amount - TickHeat) < 0.0005f);

        // Take it off again (the game's own drop), so it does not heat the scout during the next checks.
        local.refs.items.photonView.RPC("DropItemFromSlotRPC", RpcTarget.All, (byte)Player.BACKPACKSLOTINDEX, local.Center + local.data.lookDirection_Flat * 1.2f);
        yield return new WaitForSeconds(0.5f);
        Check("the burned backpack is taken off again", local.player.backpackSlot.IsEmpty());
    }

    private IEnumerator Fog()
    {
        OrbFogHandler fog = Singleton<OrbFogHandler>.Instance;
        if (fog == null)
        {
            Info("no fog on this map");
            yield break;
        }
        bool active = A09_FogAndKiln.Active(fog);
        Info("fog", $"origin {fog.currentID}, rule active {active}, waited {fog.currentWaitTime:F1} s, moving {fog.isMoving}, size {fog.currentSize:F0}, speed {fog.speed}");
        if (!active)
        {
            Fail("the fog rule is active on the beach", $"origin {fog.currentID}");
            yield break;
        }
        float end = Time.realtimeSinceStartup + 40f;
        while (!fog.isMoving && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        Check("the fog starts after 20 s (9)", fog.isMoving, $"moving {fog.isMoving}");
        if (!fog.isMoving)
        {
            yield break;
        }
        float size = fog.currentSize;
        float time = Time.time;
        yield return new WaitForSeconds(4f);
        float rate = (size - fog.currentSize) / (Time.time - time);
        Check("the fog moves twice as fast (9)", Mathf.Abs(rate - fog.speed * 2f) < fog.speed * 0.25f, $"{rate:0.000} per second (normal speed {fog.speed:0.000})");
        if (Rules.Enabled(Lvl.ExtremeWeather))
        {
            yield return FogPoisons("on the beach");
        }
    }

    /// <summary>
    /// With True Final Ascent's Apocalypse mode, TFA decides how hard its own events are: what its code
    /// gives a scout lands as given, its Blackout's torch lasts as TFA sizes it, the Scoutmaster it
    /// sends is its own, and what its events spawn is told apart on every game.
    /// </summary>
    private IEnumerator TfaEvents()
    {
        if (!Compat.Tfa.Present || !Compat.Tfa.ApocalypseActive)
        {
            Info("TFA's event checks are skipped", $"True Final Ascent present {Compat.Tfa.Present}, Apocalypse {Compat.Tfa.ApocalypseActive}");
            yield break;
        }
        Type harm = AccessTools.TypeByName("TrueFinalAscent.FinalAscentWeatherHarm");
        Type weather = AccessTools.TypeByName("TrueFinalAscent.FinalAscentWeather");
        Type torch = AccessTools.TypeByName("TrueFinalAscent.FinalAscentFlashlight");
        Type level = AccessTools.TypeByName("TrueFinalAscent.FinalAscentApoLevel");
        MethodInfo inflict = AccessTools.Method(harm, "Inflict");
        MethodInfo start = AccessTools.Method(weather, "Start");
        MethodInfo stop = AccessTools.Method(weather, "Stop");
        MethodInfo battery = AccessTools.Method(torch, "BatteryNow");
        PropertyInfo relaxed = AccessTools.Property(level, "Relaxed");
        MethodInfo atLeast = AccessTools.Method(level, "AtLeast");
        if (inflict == null || start == null || stop == null || battery == null || relaxed == null || atLeast == null)
        {
            Fail("TFA's event checks find TFA's parts", "a TFA method is missing");
            yield break;
        }

        // A status TFA's own code gives (its events use this helper) lands as given; the same status
        // given by the game is boosted by ascent 14.
        Shield(true);
        float fromTfa = Added(Status.Cold, () => inflict.Invoke(null, new object[] { Afflictions, Status.Cold, 0.3f }));
        float fromGame = Added(Status.Cold, () => Afflictions.AddStatus(Status.Cold, 0.3f));
        Protect(true);
        float boost = A15_StrongerAilments.Factor(Status.Cold);
        Check("a status TFA's code gives lands as TFA gives it, the game's own is boosted (14)", Mathf.Abs(fromTfa - 0.3f) < 0.001f && Mathf.Abs(fromGame - 0.3f * boost) < 0.001f,
            $"TFA: {fromTfa * 100f:F1} cold for 30, the game: {fromGame * 100f:F1} cold for 30 (x{boost})");

        // TFA's Blackout, started the way its weather menu starts one: the torch lasts as TFA sizes it
        // for its own Blackout, and this mod's Scoutmaster hunt steps aside.
        const float span = 60f;
        Step("start TFA's Blackout", () => start.Invoke(null, new object[] { "dark", span }));
        float end = Time.realtimeSinceStartup + 10f;
        while (Compat.Tfa.Weather != "dark" && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        yield return new WaitForSeconds(2f);
        float seconds = (float)battery.Invoke(null, null);
        float expected = (bool)relaxed.GetValue(null) ? float.PositiveInfinity : span * ((bool)atLeast.Invoke(null, new object[] { Enum.ToObject(AccessTools.TypeByName("TrueFinalAscent.ApoLevel"), 3) }) ? 0.6f : 0.7f);
        Screenshot("tfa-blackout");
        Check("TFA's Blackout torch lasts as TFA sizes it for its own Blackout", Compat.Tfa.Weather == "dark" && (float.IsInfinity(expected) ? float.IsInfinity(seconds) : Mathf.Abs(seconds - expected) < 0.5f),
            $"weather '{Compat.Tfa.Weather}', torch {seconds:0.#} s (TFA: {expected:0.#} s for a {span:0} s Blackout)");
        Check("TFA's Blackout sends its own Scoutmaster: this mod's hunt steps aside (11)", Compat.Tfa.ScoutmasterEvent && !A11_RuleZero.Hunting,
            $"Scoutmaster event {Compat.Tfa.ScoutmasterEvent}, rule zero hunting {A11_RuleZero.Hunting}");
        Step("stop TFA's Blackout", () => stop.Invoke(null, null));
        yield return new WaitForSeconds(3f);

        // What TFA's events spawn carries its tag over the network, so every game tells it apart.
        GameObject tagged = null;
        GameObject plain = null;
        Step("spawn a scorpion the way TFA's blood moon does, and a plain one", () =>
        {
            Vector3 at = Local.Center + Local.data.lookDirection_Flat * 6f + Vector3.up;
            tagged = PhotonNetwork.InstantiateRoomObject("0_Items/Scorpion", at, Quaternion.identity, 0, new object[] { "FA_BloodMoon" });
            plain = PhotonNetwork.InstantiateRoomObject("0_Items/Scorpion", at + Vector3.right * 2f, Quaternion.identity, 0);
        });
        yield return new WaitForSeconds(0.5f);
        Check("a creature TFA's events spawned keeps TFA's numbers, a plain one gets ascent 17's", tagged != null && plain != null && Compat.Tfa.Spawned(tagged.transform) && !Compat.Tfa.Spawned(plain.transform),
            $"TFA's: {(tagged != null ? Compat.Tfa.Spawned(tagged.transform).ToString() : "not spawned")}, plain: {(plain != null ? Compat.Tfa.Spawned(plain.transform).ToString() : "not spawned")}");
        foreach (GameObject spawned in new[] { tagged, plain })
        {
            if (spawned != null)
            {
                PhotonNetwork.Destroy(spawned);
            }
        }
    }

    /// <summary>The game's rising fog adds this much cold a second to a scout it has passed (skeletons: injury).</summary>
    private const float FogColdPerSecond = 0.0105f;

    /// <summary>
    /// From ascent 18 the rising fog is purple and its cold turns into three times as much poison
    /// (ascent 14's poison boost on top). The fog is brought over the scout the way it closes in on
    /// scouts left behind and what it adds itself is measured (other cold, such as a cold night's,
    /// does not count); then it goes back where it was.
    /// </summary>
    private IEnumerator FogPoisons(string where)
    {
        Character local = Local;
        OrbFogHandler fog = Singleton<OrbFogHandler>.Instance;
        FogSphere sphere = fog != null ? fog.sphere : null;
        if (sphere == null || !sphere.isActiveAndEnabled)
        {
            Info("no rising fog " + where);
            yield break;
        }
        // Once it moves, the fog fades in over 10 s; it only acts when it is fully there.
        float end = Time.realtimeSinceStartup + 60f;
        while ((!fog.isMoving || !Mathf.Approximately(sphere.ENABLE, 1f)) && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        if (!fog.isMoving || !Mathf.Approximately(sphere.ENABLE, 1f))
        {
            Fail("the rising fog comes " + where, $"origin {fog.currentID}, moving {fog.isMoving}, shown {sphere.ENABLE:0.00}");
            yield break;
        }
        float size = fog.currentSize;
        fog.currentSize = Mathf.Max(1f, Vector3.Distance(sphere.fogPoint, local.Center) - 8f);
        yield return new WaitForSeconds(1f);
        Screenshot("fog-" + where.Replace(' ', '-'));
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        sphere.rend.GetPropertyBlock(block);
        Color tint = block.GetColor("_FogtintColor");
        Material material = sphere.rend.sharedMaterial;
        Shader shader = material != null ? material.shader : null;
        List<string> colours = new List<string>();
        for (int i = 0; shader != null && i < shader.GetPropertyCount(); i++)
        {
            UnityEngine.Rendering.ShaderPropertyType type = shader.GetPropertyType(i);
            string property = shader.GetPropertyName(i);
            if (type == UnityEngine.Rendering.ShaderPropertyType.Color)
            {
                colours.Add($"{property} {material.GetColor(property)} -> {block.GetColor(property)}");
            }
            else if (type == UnityEngine.Rendering.ShaderPropertyType.Vector || type == UnityEngine.Rendering.ShaderPropertyType.Texture)
            {
                colours.Add($"{property} ({type})");
            }
        }
        Info("fog material", $"{(material != null ? material.name : "none")} / {(shader != null ? shader.name : "none")}: {string.Join("; ", colours)}");
        Shield(true);
        Meter poison = new Meter(Status.Poison, _ => FogCalls.Depth > 0);
        Meter cold = new Meter(Status.Cold, _ => FogCalls.Depth > 0);
        Meter otherCold = new Meter(Status.Cold, _ => FogCalls.Depth == 0);
        try
        {
            yield return new WaitForSeconds(3f);
        }
        finally
        {
            poison.Stop();
            cold.Stop();
            otherCold.Stop();
            Protect(true);
            fog.currentSize = size;
        }
        bool inside = fog.currentID >= 0 && Vector3.Distance(sphere.fogPoint, local.Center) > fog.currentSize;
        float expected = FogColdPerSecond * 3f * A15_StrongerAilments.Factor(Status.Poison) * poison.Seconds;
        Check($"the rising fog poisons {where} (18): 3x the game's cold, as poison, ascent 14's boost on top", poison.Total > expected * 0.9f && poison.Total < expected * 1.1f,
            $"{poison.Total * 100f:F1} poison in {poison.Seconds:F1} s = {poison.Total * 100f / Mathf.Max(0.01f, poison.Seconds):F2}/s (expected {expected * 100f / Mathf.Max(0.01f, poison.Seconds):F2}/s)");
        Check($"the rising fog does not chill {where} (18)", cold.Total == 0f && cold.Count == 0, $"{cold.Total * 100f:F2} cold from the fog in {cold.Seconds:F1} s");
        Check($"the rising fog is purple {where} (18)", tint.r > 0.4f && tint.b > 0.4f && tint.g < 0.1f, $"tint {tint}");
        Info("cold from anything else meanwhile", $"{otherCold.Total * 100f:F2} in {otherCold.Seconds:F1} s (daylight {(DayNightManager.instance != null ? DayNightManager.instance.isDay : -1f):0.00}; nights before the Caldera are cold)");
    }

    /// <summary>
    /// The first campfire, lit as a player lights it (everyone beside it, the interaction done): the
    /// game moves on to the second segment and the fog to its next start. From ascent 18 the fog there
    /// still poisons.
    /// </summary>
    private IEnumerator FirstCampfire()
    {
        Character local = Local;
        OrbFogHandler fog = Singleton<OrbFogHandler>.Instance;
        Campfire campfire = Campfire.ALL_CAMPFIRES.FirstOrDefault(c => c != null && !c.Lit && c.advanceToSegment == Segment.Tropics);
        if (campfire == null || fog == null || Singleton<MapHandler>.Instance.currentSegment != (int)Segment.Beach)
        {
            Info("first campfire check skipped", campfire == null ? "no unlit campfire at the end of the beach" : "not on the beach");
            yield break;
        }
        Vector3 away = local.Center - campfire.transform.position;
        away.y = 0f;
        Vector3 standAt = campfire.transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 1.6f + Vector3.up;
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standAt, false);
        yield return new WaitForSeconds(1f);
        int saves = NoRunSave.Skipped;
        // Lighting takes petrification off the scouts beside it: 10 from ascent 17, the game's 20 below.
        int petrifyBefore = local.data.petrifyAmount;
        Shield(true);
        local.data.SetPetrify(40);
        Step("light the first campfire", () => campfire.Interact_CastFinished(local));
        float end = Time.realtimeSinceStartup + 30f;
        while (fog.currentID == 0 && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        bool lessPetrify = Rules.Enabled(Lvl.HotCampfires);
        int healed = 40 - local.data.petrifyAmount;
        Check(lessPetrify ? "lighting a campfire takes 10 petrification off (17)" : "lighting a campfire takes the game's 20 petrification off below ascent 17",
            healed == (lessPetrify ? 10 : 20), $"ascent {target}: {healed} of 40 taken off");
        if (lessPetrify)
        {
            // The game's first frame of the lit fire once more, with Ascent 17 switched off in memory: the game's 20, at 16 too.
            BepInEx.Configuration.ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches[Lvl.HotCampfires];
            bool ruleSaved = rule.Value;
            local.data.SetPetrify(40);
            try
            {
                rule.Value = false;
                campfire.fireHasStarted = false;
                yield return null;
                yield return null;
                healed = 40 - local.data.petrifyAmount;
            }
            finally
            {
                rule.Value = ruleSaved;
            }
            Check("with Ascent 17 off, lighting a campfire takes the game's 20 petrification off (16)", healed == 20, $"{healed} of 40 taken off, ascent 16 {Rules.Enabled(Lvl.SlowRecovery)}");
        }
        local.data.SetPetrify(petrifyBefore);
        Protect(true);
        int segment = Singleton<MapHandler>.Instance.currentSegment;
        Check("lighting the first campfire moves on to the second segment and its fog", campfire.Lit && segment == (int)Segment.Tropics && fog.currentID == segment,
            $"lit {campfire.Lit}, segment {segment}, fog start {fog.currentID}, place {Map.CurrentPlace}; the game's run save skipped {NoRunSave.Skipped - saves} time(s)");
        if (fog.currentID != segment)
        {
            yield break;
        }
        if (!Rules.Enabled(Lvl.ExtremeWeather))
        {
            yield break;
        }
        // The fog waits while everyone rests at a lit campfire: the scout climbs on into the segment.
        Vector3 ahead = fog.origins[segment].transform.position - campfire.transform.position;
        ahead.y = 0f;
        Vector3 spot = campfire.transform.position + (ahead.sqrMagnitude > 0.01f ? ahead.normalized : Vector3.forward) * 30f;
        RaycastHit ground = HelperFunctions.LineCheck(spot + Vector3.up * 100f, spot + Vector3.down * 150f, HelperFunctions.LayerType.AllPhysical);
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, (ground.transform != null ? ground.point : spot) + Vector3.up * 1.5f, false);
        yield return new WaitForSeconds(1f);
        Info("the scout moves on from the campfire", $"{Vector3.Distance(campfire.transform.position, local.Center):F0} m away, resting {fog.PlayersAreResting}");
        yield return FogPoisons("after the first campfire");
        // Back to the camp, where the second segment's checks start.
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standAt, false);
        yield return new WaitForSeconds(1.5f);
    }

    private void CheckWeatherPhases()
    {
        Check("the 30 s weather grace is over", !UltimateAscent.Features.Weather.WeatherGrace.Blocking);
        List<string> missing = new List<string>();
        List<string> states = new List<string>();
        foreach (string channel in new[] { "rain", "wind", "snow" })
        {
            object value = Net.GetRoom(Net.Prefix + "wx." + channel);
            if (value is object[] data && data.Length == 5)
            {
                states.Add($"{channel}: {((bool)data[3] ? "storm" : "calm")} for {(float)data[2]:F0} s");
            }
            else
            {
                missing.Add(channel);
            }
        }
        Check("rain, wind and snow share their storm phases", missing.Count == 0, missing.Count == 0 ? string.Join(", ", states) : "missing: " + string.Join(", ", missing));
        WindChillZone[] zones = Singleton<MapHandler>.Instance.GetComponentsInChildren<WindChillZone>(includeInactive: false);
        Info("storm states", string.Join(", ", zones.Where(A18_ExtremeWeather.IsStorm).Select(z => $"{z.name} {(z.windActive ? "active" : "calm")} ({z.untilSwitch:F0} s left)")));
    }

    private IEnumerator BeeAmbush()
    {
        Character local = Local;
        Check("bees leave a scout outside the Tropics alone (17)", !A17_BeeAmbush.InTropics(local), $"ground {GroundBiome(local)}, place {Map.CurrentPlace}");
        A17_BeeAmbush.Spawn(local);
        BeeSwarm born = FindObjectsByType<BeeSwarm>(FindObjectsSortMode.None).FirstOrDefault(b => A17_BeeAmbush.IsAmbush(b) && !ScatteredBees.IsWaiting(b));
        float bornAt = born != null ? Vector3.Distance(new Vector3(born.transform.position.x, 0f, born.transform.position.z), new Vector3(local.Center.x, 0f, local.Center.z)) : -1f;
        Check("an ambush swarm appears 2-3 m from the scout (17)", bornAt >= 1.95f && bornAt <= 3.05f, $"{bornAt:0.00} m away (level distance)");
        BeeSwarm template = A17_BeeAmbush.template;
        Info("bee swarm ranges", template == null
            ? "the beehives' swarm was not found"
            : $"attacks within {template.hiveAggroDistance:F1} m when angry ({template.defaultAggroDistance:F1} m calm), gives up beyond {template.deAggroDistance:F1} m, stays within {template.maxHiveDistance:F1} m of its spot");
        yield return new WaitForSeconds(0.5f);
        List<BeeSwarm> swarms = FindObjectsByType<BeeSwarm>(FindObjectsSortMode.None).Where(b => A17_BeeAmbush.IsAmbush(b) && !ScatteredBees.IsWaiting(b)).ToList();
        Check("a bee ambush swarm appears (17)", swarms.Count == 1, $"{swarms.Count} ambush swarm(s) from prefab '{A17_BeeAmbush.prefabName}'");
        if (swarms.Count == 0)
        {
            yield break;
        }
        BeeSwarm swarm = swarms[0];
        float startDistance = Vector3.Distance(swarm.transform.position, local.Center);
        Check("the ambush swarm belongs to no hive", swarm.beehiveID == -1, $"beehive {swarm.beehiveID}");
        Check("the ambush swarm stays angry for 50 s and follows up to 120 m (17)", swarm.beehiveDangerTick > 48f && Mathf.Abs(swarm.maxHiveDistance - 120f) < 0.01f,
            $"angry for {swarm.beehiveDangerTick:F1} s more, leash {swarm.maxHiveDistance:F0} m");
        Check("the ambush swarm goes for the scout", swarm.currentAggroCharacter == local && swarm.beesAngry,
            $"target {(swarm.currentAggroCharacter != null ? swarm.currentAggroCharacter.characterName : "none")}, angry {swarm.beesAngry}, {startDistance:F1} m away");
        yield return new WaitForSeconds(4f);
        float distance = swarm != null ? Vector3.Distance(swarm.transform.position, local.Center) : -1f;
        Check("the swarm is still on the scout 4.5 s later", swarm != null && swarm.currentAggroCharacter == local && distance < 3f,
            swarm == null ? "the swarm is gone" : $"target {(swarm.currentAggroCharacter != null ? swarm.currentAggroCharacter.characterName : "none")}, {distance:F1} m away");
        if (swarm == null)
        {
            yield break;
        }

        // The stings: angry bees land 15 poison a second (calm ones 10), ascent 14's x1.75 included.
        StatusField stinger = swarm.stingerField;
        Status stingType = stinger != null ? stinger.statusType : Status.Poison;
        Check("bees sting within 2 m (17)", stinger != null && Mathf.Abs(stinger.radius - 2f) < 0.01f, stinger == null ? "no stinger field" : $"radius {stinger.radius:0.0#} m (the game: 1.3 m)");
        Shield(true);
        PoisonFxCounter.Count = 0;
        Meter sting = new Meter(stingType);
        try
        {
            yield return new WaitForSeconds(2f);
        }
        finally
        {
            sting.Stop();
            Protect(true);
        }
        float expected = 0.15f * sting.Seconds;
        Check("angry bees land 15 poison a second (17)", stingType == Status.Poison && sting.Total > expected * 0.9f && sting.Total < expected * 1.1f,
            $"{sting.Total * 100f:F1} {stingType} in {sting.Seconds:F1} s = {sting.Total * 100f / Mathf.Max(0.01f, sting.Seconds):F1}/s (expected 15/s)");
        // The game shakes the screen once per 0.025 poison: without pacing that is every frame here.
        int steps = Mathf.FloorToInt(sting.Total / 0.025f);
        Check("the stings shake the screen like a normal bee attack", PoisonFxCounter.Count > 0 && PoisonFxCounter.Count <= Mathf.CeilToInt(sting.Seconds / 0.3f) + 1,
            $"{PoisonFxCounter.Count} poison shake(s) in {sting.Seconds:F1} s for {steps} poison steps");
        // Running far away within its first 20 s: it keeps after the scout (the game drops a target beyond its range).
        if (swarm != null && A17_BeeAmbush.StillChasing(swarm))
        {
            Vector3 home = local.Center;
            Vector3 away = home + local.data.lookDirection_Flat.normalized * 40f;
            if (Physics.Raycast(away + Vector3.up * 50f, Vector3.down, out RaycastHit far, 120f, HelperFunctions.terrainMapMask))
            {
                away = far.point + Vector3.up * 1.2f;
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, away, false);
            yield return new WaitForSeconds(2f);
            float gap = swarm != null ? Vector3.Distance(swarm.transform.position, local.Center) : -1f;
            float range = swarm != null ? swarm.hiveAggroDistance : 0f;
            Check("within its first 20 s an ambush swarm keeps after a scout who runs far beyond its range (17)",
                swarm != null && swarm.currentAggroCharacter == local && gap > range,
                swarm == null ? "the swarm is gone" : $"target {(swarm.currentAggroCharacter != null ? swarm.currentAggroCharacter.characterName : "none")}, {gap:F1} m away (the game drops a target beyond {range:F1} m)");
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home, false);
            yield return new WaitForSeconds(1f);
        }
        if (swarm != null)
        {
            PhotonNetwork.Destroy(swarm.gameObject);
        }
    }

    private IEnumerator Alpine()
    {
        Character local = Local;
        // Nothing burned in the hand or on the back: it would heat the scout and hide the cold.
        local.refs.items.EquipSlot(Optionable<byte>.None);
        MapHandler.JumpToSegment(Segment.Alpine);
        yield return new WaitForSeconds(4f);
        Place place = Map.CurrentPlace;
        Info("jumped to the Alpine segment", $"place {place}, ground below the scout {GroundBiome(local)}");
        Step("Tropics only after the jump (17)", CheckTropicsOnly);
        if (place != Place.Alpine)
        {
            Info("this map's second mountain biome is not the Alpine; the Alpine checks are skipped");
            yield break;
        }
        DayNightManager clock = DayNightManager.instance;
        float quarter = (clock.dayEnd - clock.dayStart) / 4f;
        (string name, float time, bool dark)[] moments =
        {
            ("first quarter of the day", clock.dayStart + quarter * 0.5f, true),
            ("middle of the day", (clock.dayStart + clock.dayEnd) / 2f, false),
            ("last quarter of the day", clock.dayEnd - quarter * 0.5f, true),
        };
        foreach ((string name, float time, bool dark) in moments)
        {
            clock.timeOfDay = time;
            yield return new WaitForSeconds(3.5f);
            bool skyDark = !clock.sun.enabled && clock.moon.enabled;
            Check($"the Alpine sky is {(dark ? "dark" : "bright")} in the {name} (18)", skyDark == dark, $"{clock.FloatToTimeString(time)}: sun {clock.sun.enabled}, moon {clock.moon.enabled}");

            Protect(false);
            Meter cold = new Meter(Status.Cold, amount => A18_ExtremeWeather.NightColdScope.Depth > 0);
            try
            {
                yield return new WaitForSeconds(3f);
            }
            finally
            {
                cold.Stop();
                Protect(true);
            }
            // Night cold (0.005/s) x 1.5 (ascent 18); ascent 14's boost leaves the night's cold alone.
            float expected = dark ? Ascents.nightColdRate * 1.5f * cold.Seconds : 0f;
            Check($"{(dark ? "night cold" : "no night cold")} in the {name} (18)", dark ? Mathf.Abs(cold.Total - expected) < expected * 0.2f : cold.Total < 0.0005f,
                $"{cold.Total:0.0000} night cold in {cold.Seconds:F1} s (expected {expected:0.0000})");
            if (!dark)
            {
                yield return Guard("sun in the Alpine (23)", SunHeat(false, "in the Alpine"));
            }
        }
    }

    /// <summary>
    /// The Alps' light snowfall: none yet at the campfire below the Alps (where the climb already counts as
    /// the Alpine); at the Alpine's campfire, on its snowy ground, flakes fall around the view by day
    /// (pictures looking ahead and a little up); a test roof put 5 m above the view stops it, and it comes
    /// back once the roof is gone.
    /// </summary>
    private IEnumerator AlpineSnow()
    {
        MapHandler.JumpToSegment(Segment.Alpine);
        yield return new WaitForSeconds(5f);
        if (Map.CurrentPlace != Place.Alpine)
        {
            Info("the snowfall checks are skipped", $"this map's second mountain biome is not the Alpine ({Map.CurrentPlace}); run with -Biome A");
            yield break;
        }
        Character local = Local;
        Check("no snow yet at the campfire below the Alps", AlpineSnowfall.Amount <= 0f,
            $"place {Map.CurrentPlace}, ground below {GroundBiome(local)}, thickness {AlpineSnowfall.Amount:0.00}");

        MapHandler.JumpToSegment(Segment.Caldera);
        DayNightManager clock = DayNightManager.instance;
        float time = clock.timeOfDay;
        Vector2 look = local.data.lookValues;
        clock.timeOfDay = (clock.dayStart + clock.dayEnd) / 2f;
        yield return new WaitForSeconds(9f);
        Check("a light snow falls around the view on the Alpine's ground", AlpineSnowfall.Amount >= 1f && AlpineSnowfall.Flakes > 100,
            $"thickness {AlpineSnowfall.Amount:0.00}, {AlpineSnowfall.Flakes} flakes, under a roof {AlpineSnowfall.Covered}, ground below {GroundBiome(local)}, place {Map.CurrentPlace}");
        foreach ((string name, float pitch) in new[] { ("ahead", 0f), ("up", 25f) })
        {
            local.data.lookValues = new Vector2(look.x, pitch);
            yield return new WaitForSeconds(1.5f);
            Screenshot("alpine-snowfall-" + name);
            yield return new WaitForSeconds(1f);
        }
        local.data.lookValues = look;

        Transform view = MainCamera.instance.transform;
        GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "UltimateAscentTestRoof";
        roof.layer = LayerMask.NameToLayer("Map");
        roof.transform.position = view.position + Vector3.up * 5f;
        roof.transform.localScale = new Vector3(12f, 0.5f, 12f);
        try
        {
            yield return new WaitForSeconds(4.5f);
            Check("under a roof the snow stops", AlpineSnowfall.Covered && AlpineSnowfall.Amount <= 0f,
                $"under a roof {AlpineSnowfall.Covered}, thickness {AlpineSnowfall.Amount:0.00}, {AlpineSnowfall.Flakes} flakes left");
        }
        finally
        {
            Destroy(roof);
        }
        yield return new WaitForSeconds(4.5f);
        Check("without the roof it snows again", !AlpineSnowfall.Covered && AlpineSnowfall.Amount >= 1f, $"thickness {AlpineSnowfall.Amount:0.00}");
        clock.timeOfDay = time;
    }

    /// <summary>
    /// Ascent 18's weather from this round, each with ascent 18's rule on and off (in memory): the Gloom's
    /// fog (a fog of the game's own Gloom kind put around the scout, as this map may have no Gloom) makes a
    /// scout 2.5 drowsy a second (off: the game's 0.9, 1.17 with ascent 14); a tornado that has caught a
    /// scout spins, drags and lifts them 2.5 times as hard (a real tornado's grip run once each way, the
    /// forces it hands the game compared); ascent 25's gentler snowstorm outside the Alpine applies with
    /// ascent 18 switched off (at ascent 25); and in the Mesa at midday, in direct sunlight, the sun burns
    /// 6 heat a second (off: the game's, with ascent 14's boost).
    /// </summary>
    private IEnumerator Weather18()
    {
        if (Rules.Level < Lvl.ExtremeWeather)
        {
            Info("the ascent 18 weather checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches[Lvl.ExtremeWeather];
        bool saved = rule.Value;
        // At ascent 25 its storms would chill and push the scout during the measurements: only its own check runs.
        bool measure = Rules.Level < Lvl.AllWeather;
        if (!measure)
        {
            Info("the fog, tornado and sun measurements are skipped at ascent 25", "its storms would get in the way; they run at ascent 24");
            // Its storms keep calm for 30 s after waking up; the snowstorm check needs them running.
            yield return Until("the storms' calm after waking up to end", 60f, () => !UltimateAscent.Features.Weather.WeatherGrace.Blocking);
        }
        try
        {
            foreach (bool on in measure ? new[] { true, false } : new bool[0])
            {
                rule.Value = on;
                HostOptions.Invalidate();
                GameObject holder = new GameObject("SleepyFog (test)");
                holder.SetActive(false);
                Peak.StatusFieldGloom fog = holder.AddComponent<Peak.StatusFieldGloom>();
                fog.statusType = Status.Drowsy;
                fog.statusAmountPerSecond = 0.009f;
                fog.delayBeforeStatusOverTime = 2f;
                fog.entryCooldown = 1f;
                fog.additionalStatuses = new List<Peak.StatusFieldBase.StatusFieldStatus>();
                fog.size = new Vector3(30f, 12f, 30f);
                holder.transform.position = local.Head + Vector3.up * 6f;
                Protect(false);
                Afflictions.SetStatus(Status.Drowsy, 0f);
                holder.SetActive(true);
                yield return new WaitForSeconds(1f);
                float before = Afflictions.GetCurrentStatus(Status.Drowsy);
                float from = Time.time;
                yield return new WaitForSeconds(4f);
                float gained = Afflictions.GetCurrentStatus(Status.Drowsy) - before;
                float seconds = Time.time - from;
                Destroy(holder);
                Afflictions.SetStatus(Status.Drowsy, 0f);
                Protect(true);
                float rate = on ? A18_ExtremeWeather.GloomFogDrowsiness : 0.009f * A15_StrongerAilments.Factor(Status.Drowsy);
                Check(on ? "the Gloom's fog makes a scout 2.5 drowsy a second (18)" : "with ascent 18's rule off the Gloom's fog is the game's 0.9 a second (ascent 14's boost included)",
                    Mathf.Abs(gained - rate * seconds) <= 0.026f, $"{gained * 100f:0.#} drowsiness in {seconds:0.0} s ({rate * 100f * seconds:0.#} expected; the bar fills in steps of 2.5)");
                yield return new WaitForSeconds(0.5f);
            }

            Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
            GameObject twister = measure ? PhotonNetwork.Instantiate("Tornado", local.Center + forward * 40f, Quaternion.identity, 0) : null;
            Tornado tornado = twister != null ? twister.GetComponent<Tornado>() : null;
            yield return new WaitForSeconds(0.5f);
            if (tornado == null)
            {
                if (measure)
                {
                    Fail("a tornado for the grip check", "the game's Tornado could not be spawned");
                }
            }
            else
            {
                float[] first = new float[2];
                int i = 0;
                foreach (bool on in new[] { true, false })
                {
                    rule.Value = on;
                    HostOptions.Invalidate();
                    if (!tornado.caughtCharacters.Contains(local))
                    {
                        tornado.caughtCharacters.Add(local);
                    }
                    tornado.ignoredCharacters.Remove(local);
                    ForceRecorder.Moves.Clear();
                    ForceRecorder.Of = local;
                    try
                    {
                        tornado.CapturedCharacter();
                    }
                    finally
                    {
                        ForceRecorder.Of = null;
                        tornado.caughtCharacters.Remove(local);
                    }
                    first[i++] = ForceRecorder.Moves.Count > 0 ? ForceRecorder.Moves[0].magnitude : 0f;
                    yield return new WaitForSeconds(0.3f);
                }
                Check("a tornado that has caught a scout spins them 2.5 times as hard (18); the game's with the rule off",
                    Mathf.Abs(first[0] - tornado.force * A18_ExtremeWeather.TornadoPull) < 0.01f && Mathf.Abs(first[1] - tornado.force) < 0.01f,
                    $"spin force {first[0]:0.##} with the rule, {first[1]:0.##} without (the tornado's force {tornado.force:0.##})");
                PhotonNetwork.Destroy(twister);
            }

            if (Rules.Level >= Lvl.AllWeather)
            {
                rule.Value = false;
                HostOptions.Invalidate();
                GameObject snowHolder = new GameObject("SnowStorm");
                snowHolder.SetActive(false);
                WindChillZone snow = snowHolder.AddComponent<WindChillZone>();
                snow.statusType = Status.Cold;
                snow.statusApplicationPerSecond = 0.07f;
                snow.windForce = 4f;
                try
                {
                    snow.ApplyStatus(local);
                    bool gentle = Map.CurrentPlace != Place.Alpine;
                    Check("ascent 25's snowstorm is gentler outside the Alpine with ascent 18 switched off too (25)",
                        gentle ? Mathf.Approximately(snow.statusApplicationPerSecond, 0.01f) && Mathf.Approximately(snow.windForce, 12f) : Mathf.Approximately(snow.windForce, 20f),
                        $"in the {Map.CurrentPlace}: {snow.statusApplicationPerSecond * 100f:0.#} cold a second at full exposure, push {snow.windForce:0}");
                }
                finally
                {
                    Destroy(snowHolder);
                }
            }

            if (!measure)
            {
                yield break;
            }
            rule.Value = true;
            HostOptions.Invalidate();
            local.refs.items.EquipSlot(Optionable<byte>.None);
            MapHandler.JumpToSegment(Segment.Alpine);
            yield return new WaitForSeconds(4f);
            if (!Map.InMesa)
            {
                Info("the Mesa sun check is skipped", $"this map's second mountain biome is the {Map.CurrentPlace}");
                yield break;
            }
            DayNightManager clock = DayNightManager.instance;
            clock.timeOfDay = (clock.dayStart + clock.dayEnd) / 2f;
            yield return new WaitForSeconds(3f);
            HotSun sun = FindObjectsByType<HotSun>(FindObjectsSortMode.None).FirstOrDefault(s => s != null && s.isActiveAndEnabled);
            bool InBox() => sun != null && new Bounds(sun.transform.position, sun.transform.localScale).Contains(local.Center);
            if (sun != null && (!InSunlight(local) || !InBox()))
            {
                // Ground inside the sun's box with nothing between it and the sun.
                Bounds box = new Bounds(sun.transform.position, sun.transform.localScale);
                Vector3 toSun = -clock.sun.transform.forward;
                Vector3? spot = null;
                for (int r = 0; r <= 4 && spot == null; r++)
                {
                    for (int k = 0; k < Mathf.Max(1, r * 8) && spot == null; k++)
                    {
                        Vector3 offset = Quaternion.Euler(0f, k * 360f / Mathf.Max(1, r * 8), 0f) * Vector3.forward * (r * Mathf.Min(box.extents.x, box.extents.z) * 0.2f);
                        Vector3 top = new Vector3(box.center.x + offset.x, box.max.y, box.center.z + offset.z);
                        if (Physics.Raycast(top, Vector3.down, out RaycastHit ground, box.size.y, HelperFunctions.terrainMapMask)
                            && box.Contains(ground.point + Vector3.up * 1.5f)
                            && HelperFunctions.LineCheck(ground.point + Vector3.up * 1.2f + toSun * 1000f, ground.point + Vector3.up * 1.2f, HelperFunctions.LayerType.AllPhysical).transform == null)
                        {
                            spot = ground.point;
                        }
                    }
                }
                if (spot != null)
                {
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot.Value + Vector3.up * 1.2f, false);
                    yield return new WaitForSeconds(3f);
                }
            }
            if (sun == null || clock.sun.intensity < 5f || !InSunlight(local) || !InBox())
            {
                Info("the Mesa sun check is skipped", $"sun object {sun != null}, sun strength {clock.sun.intensity:0.#}, in direct sunlight {InSunlight(local)}, in its box {InBox()}");
                yield break;
            }
            foreach (bool on in new[] { true, false })
            {
                rule.Value = on;
                HostOptions.Invalidate();
                Protect(false);
                Afflictions.SetStatus(Status.Hot, 0f);
                Afflictions.SetStatus(Status.Cold, 0f);
                yield return new WaitForSeconds(1f);
                float before = Afflictions.GetCurrentStatus(Status.Hot);
                float from = Time.time;
                yield return new WaitForSeconds(4f);
                float gained = Afflictions.GetCurrentStatus(Status.Hot) - before;
                float seconds = Time.time - from;
                Afflictions.SetStatus(Status.Hot, 0f);
                Protect(true);
                float rate = on ? A18_ExtremeWeather.MesaSunHeat : sun.amount / sun.rate * A15_StrongerAilments.Factor(Status.Hot);
                Check(on ? "the Mesa's midday sun burns 6 heat a second (18)" : "with ascent 18's rule off the Mesa's sun is the game's (ascent 14's boost included)",
                    on ? Mathf.Abs(gained - rate * seconds) <= 0.026f : gained <= rate * seconds + 0.026f && gained >= rate * seconds * 0.6f - 0.026f,
                    $"{gained * 100f:0.#} heat in {seconds:0.0} s ({rate * 100f * seconds:0.#} {(on ? "expected" : "at most")}; the game's sun gives {sun.amount * 100f:0.#} every {sun.rate:0.#} s, the bar fills in steps of 2.5)");
                yield return new WaitForSeconds(0.5f);
            }

            // A snowstorm blowing over the Mesa (in the Mesa it chills): no sun burns through it.
            rule.Value = true;
            HostOptions.Invalidate();
            WindChillZone mesaStorm = TestSnowstorm(local.Center);
            try
            {
                Protect(false);
                yield return new WaitForSeconds(1f);
                Afflictions.SetStatus(Status.Hot, 0f);
                Afflictions.SetStatus(Status.Cold, 0f);
                Meter heat = new Meter(Status.Hot);
                yield return new WaitForSeconds(4f);
                heat.Stop();
                Check("no sun burns through a snowstorm: the Mesa's midday sun rests while one blows on the scout (18)",
                    A18_ExtremeWeather.SnowstormHidesSun && heat.Total < 0.0005f && InSunlight(local),
                    $"{heat.Total * 100f:0.##} heat in {heat.Seconds:0.0} s (the sun alone: {A18_ExtremeWeather.MesaSunHeat * 100f * heat.Seconds:0.#}), sun hidden {A18_ExtremeWeather.SnowstormHidesSun}, "
                    + $"in direct sunlight {InSunlight(local)}, cold {Afflictions.GetCurrentStatus(Status.Cold) * 100f:0.#} from the storm");
            }
            finally
            {
                Destroy(mesaStorm.gameObject);
                Afflictions.SetStatus(Status.Cold, 0f);
                Afflictions.SetStatus(Status.Hot, 0f);
                Protect(true);
            }
        }
        finally
        {
            rule.Value = saved;
            HostOptions.Invalidate();
            Protect(true);
        }
    }

    /// <summary>Records the forces the game's Character.AddForce receives for one character (after the mod's own changes).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.AddForce), typeof(Vector3), typeof(float), typeof(float))]
    internal static class ForceRecorder
    {
        internal static Character Of;
        internal static readonly List<Vector3> Moves = new List<Vector3>();

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Character __instance, Vector3 move)
        {
            if (Of != null && __instance == Of)
            {
                Moves.Add(move);
            }
        }
    }

    /// <summary>
    /// Records the knock-downs the game's Character.Fall receives for one character (after the mod's own changes), with
    /// the character's drowsiness at that moment (an explosion's knock-out tops it up right before).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Fall))]
    internal static class FallRecorder
    {
        internal static Character Of;
        internal static readonly List<float> Seconds = new List<float>();
        internal static readonly List<float> Drowsiness = new List<float>();

        internal static void Clear()
        {
            Seconds.Clear();
            Drowsiness.Clear();
        }

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Character __instance, float seconds)
        {
            if (Of != null && __instance == Of)
            {
                Seconds.Add(seconds);
                Drowsiness.Add(__instance.refs.afflictions.GetCurrentStatus(Status.Drowsy));
            }
        }
    }

    /// <summary>
    /// The Kiln (on a volcano map; 17, 18): standing on a ledge high in the Kiln, out of the lava's way, the scout
    /// gets the hot ash once its lava rises: 5 s of warning as it thickens, then 15 s of 3 heat a second, 45 in all (as it lands
    /// from ascent 14 on) while it is drawn in ash grey around the view, then it clears and the fog sphere is off
    /// again. Meanwhile the Kiln erupts like the Caldera, every 30-60 s, 18-32 m from the scout, inside the Kiln.
    /// </summary>
    private IEnumerator KilnCheck()
    {
        if (Rules.Level < Lvl.Hazards)
        {
            Info("the Kiln checks are skipped", $"testing ascent {target}");
            yield break;
        }
        if (Map.CurrentPlace != Place.Kiln)
        {
            MapHandler.JumpToSegment(Segment.TheKiln);
            yield return new WaitForSeconds(6f);
        }
        LavaRising rising = LavaRising.ALL_LAVA.FirstOrDefault(l => l != null && l.requiredSegment == Segment.TheKiln && l.risingFieldType == LavaRising.RisingFieldType.Lava);
        if (!Map.InVolcanoKiln || rising == null)
        {
            Info("the Kiln checks are skipped", $"no volcanic Kiln on this map ({Map.CurrentPlace}); run with -Biome V");
            yield break;
        }
        Character local = Local;
        Check("no scorching sun in the Kiln: ascent 23's sun stops at the Caldera and the Gloom (23)", !A23_HotSun.ScorchingHere || target < Lvl.HotSun,
            $"scorching here {A23_HotSun.ScorchingHere} ({Map.CurrentPlace})");
        Renderer plane = rising.lava.GetComponentsInChildren<Renderer>(true).FirstOrDefault();
        Vector3 centre = plane != null ? plane.bounds.center : rising.lava.position;
        // An open ledge (to see the ash from): clear sky above and room around at head height.
        bool Open(Vector3 at)
        {
            Vector3 head = at + Vector3.up * 1.6f;
            if (HelperFunctions.LineCheck(head, head + Vector3.up * 20f, HelperFunctions.LayerType.TerrainMap).transform != null)
            {
                return false;
            }
            int room = 0;
            for (int a = 0; a < 8; a++)
            {
                Vector3 dir = Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward;
                room += HelperFunctions.LineCheck(head, head + dir * 8f, HelperFunctions.LayerType.TerrainMap).transform == null ? 1 : 0;
            }
            return room >= 5;
        }
        Vector3 ledge = Vector3.zero;
        // The petrifying stones, within 3 m of which the ash neither shows nor burns.
        HashSet<Collider> stoneSet = new HashSet<Collider>(KilnAsh.Stones);
        bool StoneWithin(Vector3 ground, float reach) =>
            Physics.OverlapCapsule(ground + Vector3.up * 0.2f, ground + Vector3.up * 1.7f, reach, ~0, QueryTriggerInteraction.Ignore).Any(stoneSet.Contains);
        // Away from anything that heats (magma rocks' heat fields reach 11 m, more with their fade).
        List<Vector3> heaters = FindObjectsByType<StatusEmitter>(FindObjectsSortMode.None)
            .Where(e => e != null && e.statusType == Status.Hot && e.amount > 0f).Select(e => e.transform.position).ToList();
        for (float height = 140f; height >= 60f && ledge == Vector3.zero; height -= 10f)
        {
            for (int angle = 0; angle < 360 && ledge == Vector3.zero; angle += 20)
            {
                for (float radius = 10f; radius <= 50f && ledge == Vector3.zero; radius += 10f)
                {
                    Vector3 from = new Vector3(centre.x, rising.lava.position.y + height, centre.z) + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                    RaycastHit hit = HelperFunctions.LineCheck(from, from + Vector3.down * 25f, HelperFunctions.LayerType.TerrainMap);
                    if (hit.transform != null && hit.normal.y > 0.8f && hit.distance > 2f && KilnAsh.InKiln(hit.point) && heaters.All(h => Vector3.Distance(h, hit.point) > 18f) && Open(hit.point)
                        && !StoneWithin(hit.point, 6f))
                    {
                        ledge = hit.point + Vector3.up * 1.2f;
                    }
                }
            }
        }
        if (ledge == Vector3.zero)
        {
            Fail("a ledge high in the Kiln to stand on");
            yield break;
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, ledge, false);
        yield return new WaitForSeconds(2f);
        Info("standing on a ledge in the Kiln", $"at {local.Center:F1}, {local.Center.y - rising.lava.position.y:0} m above the lava, in the Kiln {KilnAsh.InKiln(local.Center)}");
        HashSet<GameObject> eruptions = new HashSet<GameObject>();
        List<string> spots = new List<string>();
        List<float> eruptedAt = new List<float>();
        List<float> outAt = new List<float>();
        int burningWithAsh = 0;
        int alive = 0;
        Vector2 look = local.data.lookValues;
        bool pictured = false;
        // Bing Bong's rule (20) off in memory, so its crab and warnings do not cover the ash on screen.
        ConfigEntry<bool> bingBong = UltimateAscent.Plugin.AscentSwitches[Lvl.BingBong];
        bool bingBongSaved = bingBong.Value;
        bingBong.Value = false;
        HostOptions.Invalidate();
        Afflictions.SetStatus(Status.Crab, 0f);
        void Eruptions()
        {
            foreach (RemoveAfterSeconds made in FindObjectsByType<RemoveAfterSeconds>(FindObjectsSortMode.None))
            {
                if (made != null && made.name.StartsWith("Eruption", StringComparison.Ordinal) && eruptions.Add(made.gameObject))
                {
                    Vector3 at = made.transform.position;
                    float away = new Vector2(at.x - local.Center.x, at.z - local.Center.z).magnitude;
                    eruptedAt.Add(KilnAsh.ClockPhase);
                    spots.Add($"{away:0} m away, {at.y - local.Center.y:+0;-0} m up, {KilnAsh.ClockPhase:0} s into the minute, burning {made.seconds:0} s, in the Kiln {KilnAsh.InKiln(at)}");
                }
            }
            float now = KilnAsh.ClockPhase;
            int burning = eruptions.Count(e => e != null);
            if (burning < alive)
            {
                outAt.Add(now);
            }
            alive = burning;
            if ((now >= KilnAsh.Period - KilnAsh.Seconds - KilnAsh.WarningSeconds || (now >= 0f && now < KilnAsh.ClearingSeconds)) && eruptions.Any(e => e != null))
            {
                burningWithAsh++;
            }
        }
        Shield(true);
        try
        {
            for (float until = Time.time + 45f; Time.time < until && !rising.started;)
            {
                Eruptions();
                yield return null;
            }
            if (!rising.started)
            {
                Fail("the Kiln's lava starts to rise", "not within 45 s");
                yield break;
            }
            // Before the ash: what else heats the scout here, as a rate.
            float comes = KilnAsh.Period - KilnAsh.Seconds - KilnAsh.WarningSeconds;
            while (KilnAsh.Phase < 0f || KilnAsh.Phase > comes - 12f)
            {
                Eruptions();
                yield return null;
            }
            Meter before = new Meter(Status.Hot);
            float beforeStart = Time.time;
            while (KilnAsh.Phase < comes)
            {
                Eruptions();
                yield return null;
            }
            float baseline = before.Total / Mathf.Max(0.1f, Time.time - beforeStart);
            before.Stop();
            while (KilnAsh.Phase < comes + KilnAsh.WarningSeconds * 0.5f)
            {
                Eruptions();
                yield return null;
            }
            float warning = KilnAsh.Drawn;
            bool burningInWarning = KilnAsh.Burning;
            while (KilnAsh.Phase < KilnAsh.Period - KilnAsh.Seconds)
            {
                Eruptions();
                yield return null;
            }
            Meter ash = new Meter(Status.Hot);
            float ashStart = Time.time;
            Vector3 stoodAt = local.Center;
            HeatSources.Clear();
            HeatSources.On = true;
            FogSphere sphere = FindAnyObjectByType<FogSphere>(FindObjectsInactive.Include);
            bool drawing = false;
            bool shot = false;
            int smokeMost = 0;
            while (KilnAsh.Phase >= KilnAsh.Period - KilnAsh.Seconds)
            {
                Eruptions();
                drawing |= KilnAsh.Drawing && sphere != null && sphere.gameObject.activeInHierarchy;
                smokeMost = Mathf.Max(smokeMost, KilnAsh.SmokeParticles);
                if (!shot && Time.time - ashStart > 4f)
                {
                    shot = true;
                    Vector3 shaft = new Vector3(centre.x - local.Center.x, 0f, centre.z - local.Center.z);
                    float yaw = shaft.sqrMagnitude > 1f ? Quaternion.LookRotation(shaft).eulerAngles.y : look.x;
                    foreach ((float y, float pitch, string name) in new[] { (yaw, 0f, "shaft"), (yaw + 180f, 5f, "across"), (yaw, 60f, "up") })
                    {
                        local.data.lookValues = new Vector2(y, pitch);
                        yield return new WaitForSeconds(0.7f);
                        Screenshot("kiln-ash-" + name);
                        yield return new WaitForSeconds(0.3f);
                    }
                    local.data.lookValues = look;
                    continue;
                }
                yield return null;
            }
            float burned = ash.Total - baseline * (Time.time - ashStart);
            float seconds = Time.time - ashStart;
            ash.Stop();
            HeatSources.On = false;
            string sources = string.Join(", ", HeatSources.Totals.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value * 100f:0.#}"));
            float moved = Vector3.Distance(stoodAt, local.Center);
            float expected = A15_StrongerAilments.FromAscent15(Status.Hot, KilnAsh.Heat) * KilnAsh.Seconds;
            Check("the Kiln's ash comes with a warning (thickening, no heat yet), then burns 3 heat a second for 15 s, 45 in all (18)",
                warning > 0.2f && warning < 0.8f && !burningInWarning && Mathf.Abs(burned - expected) <= 0.035f && drawing && smokeMost > 100,
                $"halfway through the warning drawn {warning:0.00}, burning {burningInWarning}; {burned * 100f:0.#} heat from the ash in {seconds:0.0} s (expected {expected * 100f:0.#}; {baseline * 100f:0.##}/s from elsewhere taken off), drawn with the fog sphere {drawing}, up to {smokeMost} smoke and ash particles around; heat by its source: {sources}; the scout moved {moved:0.0} m");
            for (float until = Time.time + KilnAsh.ClearingSeconds + 1f; Time.time < until;)
            {
                Eruptions();
                yield return null;
            }
            Check("then the ash clears and the fog sphere is off again", KilnAsh.Drawn <= 0f && (sphere == null || !sphere.gameObject.activeInHierarchy),
                $"drawn {KilnAsh.Drawn:0.00}, sphere on {(sphere != null && sphere.gameObject.activeInHierarchy)}");
            // The eruptions over the whole wait (the first comes 30-60 s after reaching the Kiln).
            for (float until = Time.time + 30f; Time.time < until && eruptions.Count == 0;)
            {
                Eruptions();
                yield return null;
            }
            GameObject first = eruptions.FirstOrDefault(e => e != null);
            if (first != null && !pictured)
            {
                pictured = true;
                Vector3 toward = first.transform.position + Vector3.up * 8f - local.Head;
                local.data.lookValues = HelperFunctions.DirectionToLook(toward);
                yield return new WaitForSeconds(0.6f);
                Screenshot("kiln-eruption");
                local.data.lookValues = look;
            }
            float comesAt = KilnAsh.Period - KilnAsh.Seconds - KilnAsh.WarningSeconds;
            Check("the Kiln's eruptions take turns with the ash, never with it and at most 10 s apart: one 2-9.5 s after the ash clears, out before it comes again (17)",
                eruptions.Count > 0 && spots.All(spot => spot.EndsWith("True", StringComparison.Ordinal)) && eruptedAt.All(p => p >= 1.9f && p <= 10f) && burningWithAsh == 0
                && outAt.Count > 0 && outAt.All(p => p <= comesAt && comesAt - p <= 10f),
                $"{eruptions.Count} eruptions in {Time.time - beforeStart:0} s: {string.Join("; ", spots)}; out at {string.Join(", ", outAt.Select(p => p.ToString("0.0") + " s"))} into the minute (the ash comes at {comesAt:0} s); frames with an eruption burning while the ash came or burned: {burningWithAsh}");
            // The next surge: from the ledge to beside a petrifying stone (where it neither shows nor burns), then to the
            // top of the Kiln (as high as its lava rises, or higher: it shows there but does not burn).
            float top = rising.topTransform != null ? rising.topTransform.position.y : float.NaN;
            Vector3 besideStone = Vector3.zero;
            Vector3 stoneCentre = Vector3.zero;
            foreach (Collider stone in stoneSet.OrderBy(s => Mathf.Abs(s.bounds.center.y - ledge.y)))
            {
                Bounds b = stone.bounds;
                for (float x = b.min.x - 3f; x <= b.max.x + 3f && besideStone == Vector3.zero; x += 1f)
                {
                    for (float z = b.min.z - 3f; z <= b.max.z + 3f && besideStone == Vector3.zero; z += 1f)
                    {
                        Vector3 from = new Vector3(x, b.max.y + 5f, z);
                        RaycastHit hit = HelperFunctions.LineCheck(from, from + Vector3.down * (b.size.y + 15f), HelperFunctions.LayerType.TerrainMap);
                        if (hit.transform != null && !stoneSet.Contains(hit.collider) && hit.normal.y > 0.75f && KilnAsh.InKiln(hit.point) && !KilnAsh.AtTop(hit.point + Vector3.up)
                            && HelperFunctions.LineCheck(hit.point + Vector3.up * 0.3f, hit.point + Vector3.up * 2.5f, HelperFunctions.LayerType.TerrainMap).transform == null
                            && StoneWithin(hit.point, 2f) && !StoneWithin(hit.point, 1f))
                        {
                            besideStone = hit.point + Vector3.up * 1.2f;
                            stoneCentre = b.center;
                        }
                    }
                }
                if (besideStone != Vector3.zero)
                {
                    break;
                }
            }
            Vector3 atTop = Vector3.zero;
            List<float> topGround = new List<float>();
            if (!float.IsNaN(top) && plane != null)
            {
                Bounds kb = plane.bounds;
                float nearest = float.MaxValue;
                for (float x = kb.min.x; x <= kb.max.x; x += 4f)
                {
                    for (float z = kb.min.z; z <= kb.max.z; z += 4f)
                    {
                        Vector3 from = new Vector3(x, top + 150f, z);
                        RaycastHit hit = HelperFunctions.LineCheck(from, from + Vector3.down * 200f, HelperFunctions.LayerType.TerrainMap);
                        if (hit.transform == null || hit.point.y < top - 30f)
                        {
                            continue;
                        }
                        topGround.Add(hit.point.y);
                        float off = new Vector2(x - rising.topTransform.position.x, z - rising.topTransform.position.z).magnitude;
                        if (hit.point.y >= top && hit.normal.y > 0.75f && KilnAsh.InKiln(hit.point) && off < nearest && !StoneWithin(hit.point, 6f))
                        {
                            nearest = off;
                            atTop = hit.point + Vector3.up * 1.2f;
                        }
                    }
                }
            }
            Info("the ground at the top of the Kiln", $"its lava rises to {top:0.0}; ground found from {top - 30f:0} up, by height above the lava's top: "
                + string.Join(", ", topGround.GroupBy(y => Mathf.FloorToInt((y - top) / 5f) * 5).OrderBy(g => g.Key).Select(g => $"{g.Key:+0;-0;0}..{g.Key + 5:+0;-0;0} m: {g.Count()}"))
                + $" (points every 4 m); standing at the top at {atTop:F1}");
            if (besideStone == Vector3.zero || atTop == Vector3.zero)
            {
                Fail("a spot beside a petrifying stone and one at the top of the Kiln", $"beside a stone {besideStone != Vector3.zero}, at the top {atTop != Vector3.zero} (the lava rises to {top:0.0}; {stoneSet.Count} stone colliders)");
            }
            else
            {
                float burnFrom = KilnAsh.Period - KilnAsh.Seconds;
                while (KilnAsh.Phase < 0f || KilnAsh.Phase > comesAt - 1f)
                {
                    Eruptions();
                    yield return null;
                }
                while (KilnAsh.Phase < burnFrom + 2f)
                {
                    Eruptions();
                    yield return null;
                }
                int smokeBefore = KilnAsh.SmokeParticles;
                float drawnBefore = KilnAsh.Drawn;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, besideStone, false);
                yield return new WaitForSeconds(0.3f);
                local.data.lookValues = HelperFunctions.DirectionToLook(stoneCentre - local.Head);
                HeatSources.Clear();
                HeatSources.On = true;
                float shelterStart = Time.time;
                bool alwaysSheltered = true;
                float drawnAfter = 0f;
                bool stoneShot = false;
                while (Time.time - shelterStart < 4.2f)
                {
                    Eruptions();
                    alwaysSheltered &= KilnAsh.Sheltered;
                    if (Time.time - shelterStart > 0.6f)
                    {
                        drawnAfter = Mathf.Max(drawnAfter, KilnAsh.Drawn);
                    }
                    if (!stoneShot && Time.time - shelterStart > 3f)
                    {
                        stoneShot = true;
                        Screenshot("kiln-ash-beside-stone");
                    }
                    yield return null;
                }
                float shelteredHeat = HeatSources.Totals.TryGetValue("KilnAsh.Tick", out float fromAsh) ? fromAsh : 0f;
                int smokeAfter = KilnAsh.SmokeParticles;
                bool burningThen = KilnAsh.Burning;
                Check("within 3 m of a petrifying stone the ash neither shows nor burns: the haze clears, the smoke and ash fade away (18)",
                    drawnBefore > 0.9f && smokeBefore > 100 && alwaysSheltered && drawnAfter <= 0f && smokeAfter == 0 && shelteredHeat == 0f && burningThen,
                    $"on the ledge drawn {drawnBefore:0.00} with {smokeBefore} particles; beside the stone (1-2 m from it) for 4.2 s: sheltered all along {alwaysSheltered}, drawn after 0.6 s at most {drawnAfter:0.00}, {smokeAfter} particles left, {shelteredHeat * 100f:0.#} heat from the ash, the ash still burning elsewhere {burningThen}");
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, atTop, false);
                yield return new WaitForSeconds(0.8f);
                local.data.lookValues = look;
                HeatSources.Clear();
                float topStart = Time.time;
                float drawnTop = 1f;
                bool alwaysAtTop = true;
                int smokeTop = 0;
                bool topShot = false;
                while (Time.time - topStart < 4f && KilnAsh.Phase >= burnFrom)
                {
                    Eruptions();
                    drawnTop = Mathf.Min(drawnTop, KilnAsh.Drawn);
                    alwaysAtTop &= KilnAsh.AtTop(local.Center);
                    smokeTop = Mathf.Max(smokeTop, KilnAsh.SmokeParticles);
                    if (!topShot && Time.time - topStart > 2f)
                    {
                        topShot = true;
                        Screenshot("kiln-ash-top");
                    }
                    yield return null;
                }
                float topSeconds = Time.time - topStart;
                float topHeat = HeatSources.Totals.TryGetValue("KilnAsh.Tick", out float fromAshTop) ? fromAshTop : 0f;
                HeatSources.On = false;
                Check("at the top of the Kiln (as high as its lava rises, or higher) the ash shows but does not burn (18)",
                    alwaysAtTop && drawnTop > 0.9f && smokeTop > 100 && topHeat == 0f && topSeconds > 3f,
                    $"at {local.Center:F1} ({local.Center.y - top:+0.0;-0.0} m from the lava's top) for {topSeconds:0.0} s: at the top all along {alwaysAtTop}, drawn at least {drawnTop:0.00}, up to {smokeTop} particles, {topHeat * 100f:0.#} heat from the ash");
            }
        }
        finally
        {
            HeatSources.On = false;
            bingBong.Value = bingBongSaved;
            HostOptions.Invalidate();
            Shield(false);
            Protect(true);
            local.data.lookValues = look;
        }
    }

    /// <summary>While on, the scout's own back model shows too (the game hides it in first person), for pictures from behind.</summary>
    [HarmonyPatch(typeof(CharacterBackpackHandler), nameof(CharacterBackpackHandler.LateUpdate))]
    internal static class ShowOwnBackpack
    {
        internal static bool On;

        private static void Postfix(CharacterBackpackHandler __instance)
        {
            if (On && __instance.character == Character.localCharacter && __instance.activeBackpackVisuals != null && !__instance.character.player.backpackSlot.IsEmpty())
            {
                __instance.activeBackpackVisuals.SetViewActive(true);
            }
        }
    }

    /// <summary>Who adds heat to the local scout while it is on (the calling method, summed).</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    internal static class HeatSources
    {
        internal static bool On;
        internal static readonly Dictionary<string, float> Totals = new Dictionary<string, float>();

        internal static void Clear() => Totals.Clear();

        private static void Prefix(CharacterAfflictions __instance, Status statusType, float amount)
        {
            if (!On || statusType != Status.Hot || amount <= 0f || __instance.character != Character.localCharacter)
            {
                return;
            }
            string from = "?";
            foreach (System.Diagnostics.StackFrame frame in new System.Diagnostics.StackTrace(1).GetFrames() ?? new System.Diagnostics.StackFrame[0])
            {
                MethodBase method = frame.GetMethod();
                Type type = method?.DeclaringType;
                if (type == null || type == typeof(CharacterAfflictions) || type.FullName.StartsWith("UltimateAscent.Features.ExactStatus", StringComparison.Ordinal) || type.Namespace == "HarmonyLib" || method.Name.Contains("Patch"))
                {
                    continue;
                }
                from = type.Name + "." + method.Name;
                break;
            }
            Totals.TryGetValue(from, out float sum);
            Totals[from] = sum + amount;
        }
    }

    /// <summary>A cactus ball picked up into the hand counts its thorns twice like one stuck to you: 40 on the bar (17).</summary>
    private IEnumerator HeldCactus()
    {
        Item cactus = ItemByName("CactusBall");
        if (cactus == null || Rules.Level < Lvl.Hazards)
        {
            Info("the held cactus check is skipped", cactus == null ? "no cactus ball in the item database" : $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Protect(false);
        ClearStatuses(Afflictions);
        try
        {
            yield return SpawnIntoHand(cactus);
            if (held == null)
            {
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            Afflictions.UpdateWeight();
            yield return null;
            float thorns = Afflictions.GetCurrentStatus(Status.Thorns);
            Check("a cactus ball held in the hand: 40 on the bar, like one stuck to you (17; the game: 20)", Mathf.Abs(thorns - 0.4f) < 0.01f,
                $"thorns {thorns * 100f:0.#} on the bar, held: {(local.data.currentStickyItem != null ? local.data.currentStickyItem.name : "nothing")}");
        }
        finally
        {
            local.refs.items.EquipSlot(Optionable<byte>.None);
            for (byte slot = 0; slot < local.player.itemSlots.Length; slot++)
            {
                ItemSlot itemSlot = local.player.itemSlots[slot];
                if (!itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == cactus.itemID)
                {
                    local.player.EmptySlot(Optionable<byte>.Some(slot));
                }
            }
            Afflictions.UpdateWeight();
            ClearStatuses(Afflictions);
            Protect(true);
        }
    }

    /// <summary>The new items' names (and the swing prompt) in Russian; the other languages show the English.</summary>
    private void RussianNames()
    {
        Dictionary<string, List<string>> table = LocalizedText.mainTable;
        int russian = (int)LocalizedText.Language.Russian;
        int french = (int)LocalizedText.Language.French;
        List<string> wrong = new List<string>();
        List<string> shown = new List<string>();
        foreach (KeyValuePair<string, string> name in AscentCatalog.ItemNames.Concat(AscentCatalog.Prompts))
        {
            bool prompt = AscentCatalog.Prompts.ContainsKey(name.Key);
            string expected = AscentCatalog.Russian.TryGetValue(name.Key, out string ru) ? (prompt ? ru : ru.ToUpperInvariant()) : null;
            List<string> row = table.TryGetValue(name.Key.ToUpperInvariant(), out List<string> found) ? found : null;
            string english = prompt ? name.Value : name.Value.ToUpperInvariant();
            if (row == null || expected == null || row[russian] != expected || row[french] != english && !prompt)
            {
                wrong.Add(name.Key);
            }
            shown.Add($"{name.Value} = {(row != null ? row[russian] : "none")}");
        }
        foreach (string key in new[] { "NAME_AUTOPARACHUTE", "NAME_WARP COMPASS", "NAME_WEIRD SHROOM", "NAME_GREEN CLUSTERBERRY", "NAME_TORCH", "NAME_STONE", "NAME_STICK" })
        {
            shown.Add($"{key.Substring(5)} = {(table.TryGetValue(key, out List<string> game) ? game[russian] : "none")} (the game's)");
        }
        Check("the new items have Russian names (and the swing prompt); the game's own items keep the game's Russian", wrong.Count == 0,
            (wrong.Count > 0 ? "wrong: " + string.Join(", ", wrong) + "; " : "") + string.Join("; ", shown));
    }

    /// <summary>A jellyfish sting is a flat 20 poison and a hard slip (the scout is down for 10 s).</summary>
    private IEnumerator Jellyfish()
    {
        Character local = Local;
        SlipperyJellyfish jelly = FindObjectsByType<SlipperyJellyfish>(FindObjectsSortMode.None)
            .OrderBy(j => Vector3.Distance(j.transform.position, local.Center))
            .FirstOrDefault();
        if (jelly == null)
        {
            Info("jellyfish check skipped: no jellyfish on this beach");
            yield break;
        }
        float poison = 0f;
        Unlocked(() =>
        {
            Afflictions.SetStatus(Status.Poison, 0f);
            poison = Added(Status.Poison, () => jelly.Trigger(local.photonView.ViewID));
        });
        Check("a jellyfish sting is 20 poison (17)", Mathf.Abs(poison - 0.2f) < 0.0005f,
            $"{poison * 100f:0.0} poison, ascent 14's x1.75 included (the game: 5)");
        // The slip keeps the scout down for 10 s.
        yield return new WaitForSeconds(10.5f);
        Afflictions.SetStatus(Status.Poison, 0f);
    }

    /// <summary>
    /// How long a bee attack takes to knock out a scout with 70 of 100 stamina left (30 taken by
    /// other statuses): the bar fills up, then the game's 5 s faint timer runs. At ascent 24 passing
    /// out is death.
    /// </summary>
    /// <summary>A real fall: the scout is put 30 m up and drops; the fall damage makes it 60 drowsy.</summary>
    private IEnumerator FallDrowsiness()
    {
        Character local = Local;
        RemoveTicks(local);
        Shield(true);
        Meter drowsy = new Meter(Status.Drowsy);
        Meter injury = new Meter(Status.Injury);
        try
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, local.Center + Vector3.up * 30f, false);
            yield return new WaitForSeconds(0.5f);
            float end = Time.time + 8f;
            while (Time.time < end && injury.Count == 0)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
        }
        finally
        {
            drowsy.Stop();
            injury.Stop();
            Protect(true);
        }
        // 60 from ascent 14 on; below 14 without that ascent's boost (46.2, of which the first fall lands 45).
        float expected = A15_StrongerAilments.FromAscent15(CharacterAfflictions.STATUSTYPE.Drowsy, A12_Drowsy.FallDrowsiness);
        Check("fall damage makes the scout 60 drowsy, less below 14 (12)", injury.Total > 0f && Mathf.Abs(drowsy.Total - expected) < 0.026f,
            $"{injury.Total * 100f:F1} fall damage, {drowsy.Total * 100f:F1} drowsiness (expected {expected * 100f:F1})");
    }

    /// <summary>
    /// Falls with flying items (12, 13): a fall the game hurts for brings ascent 12's drowsiness and
    /// ascent 13's dropped items, whatever the scout flew with: a plain fall, a fall with the Jetpack
    /// on, a glide that runs out of stamina, and balloons that pop mid-air.
    /// </summary>
    private IEnumerator FallsWithItems()
    {
        Character local = Local;
        RemoveTicks(local);
        Item food = ItemByName("Airplane Food");
        Item glider = ItemByName("Glider");
        Item jetpackPrefab = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i is Backpack && i.GetComponent<JetpackItem>() != null);
        if (food == null || glider == null || jetpackPrefab == null)
        {
            Fail("the items for the falls", $"food {food != null}, glider {glider != null}, jetpack {jetpackPrefab != null}");
            yield break;
        }

        // A full load (a backpack on, an item in each of the three slots), dropped the game's way once (the items
        // collide with the scout at once) and then this mod's way (they pass through the scout until clear).
        for (int i = 1; i <= 3; i++)
        {
            yield return FallCase($"a fully loaded fall, the game's way ({i})", FullLoad, null, gameWay: true);
        }
        for (int i = 1; i <= 3; i++)
        {
            yield return FallCase($"a fully loaded fall ({i})", FullLoad, null);
        }

        yield return FallCase("a plain fall", () => SpawnIntoHand(food), null);

        yield return FallCase("a fall with the Jetpack on", () => WearJetpack(jetpackPrefab, food), null);

        yield return FallCase("a glide that runs out of stamina", () => SpawnIntoHand(glider), airborne =>
        {
            if (airborne > 1.5f)
            {
                local.data.currentStamina = 0f;
                local.data.extraStamina = 0f;
            }
        });

        bool popped = false;
        yield return FallCase("balloons that pop mid-air", () => TieBalloons(food), airborne =>
        {
            if (!popped && airborne > 1.5f)
            {
                popped = true;
                foreach (TiedBalloon balloon in local.refs.balloons.tiedBalloons.ToList())
                {
                    balloon.Pop();
                }
            }
        });

        // A wall grabbed mid-fall: the scout slides down it, and the game hurts for the slide.
        if (!FindWall(local.Center, out Vector3 wall, out Vector3 normal, out float drop))
        {
            Info("wall-grab fall skipped", "no tall steep wall within 40 m of the scout");
            yield break;
        }
        Info("wall for the grab", $"{Vector3.Distance(wall, local.Center):0} m away, {drop:0} m above the ground, facing {normal}");
        Vector3 start = wall + normal * 0.9f;
        // Out of stamina, the scout cannot hold on and slides down to the ground below.
        bool grabbed = false;
        yield return FallCase("a wall grabbed mid-fall, sliding down it out of stamina", () => SpawnIntoHand(food), airborne =>
        {
            local.data.lookValues = new Vector2(Quaternion.LookRotation(-normal).eulerAngles.y, 0f);
            if (airborne > 0.6f && !grabbed && !local.data.isClimbing)
            {
                local.refs.climbing.TryToStartWallClimb(forceAttempt: true, -normal);
            }
            grabbed |= local.data.isClimbing;
            if (grabbed)
            {
                local.data.currentStamina = 0f;
                local.data.extraStamina = 0f;
            }
        }, start);
    }

    /// <summary>A steep wall near the scout, tall enough to fall along: a point on it high up, its normal and its height above the ground.</summary>
    private static bool FindWall(Vector3 around, out Vector3 point, out Vector3 normal, out float drop)
    {
        point = normal = Vector3.zero;
        drop = 0f;
        for (int angle = 0; angle < 360; angle += 10)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            for (float height = 24f; height >= 14f; height -= 2f)
            {
                Vector3 from = around + Vector3.up * height;
                RaycastHit hit = HelperFunctions.LineCheck(from, from + direction * 40f, HelperFunctions.LayerType.TerrainMap);
                if (hit.transform == null || Mathf.Abs(hit.normal.y) > 0.3f)
                {
                    continue;
                }
                // The face goes on below: steep for at least 8 m further down.
                bool tall = true;
                for (float down = 3f; down <= 9f && tall; down += 3f)
                {
                    Vector3 lower = hit.point + hit.normal * 1.5f - Vector3.up * down;
                    RaycastHit below = HelperFunctions.LineCheck(lower, lower - hit.normal * 3f, HelperFunctions.LayerType.TerrainMap);
                    tall = below.transform != null && Mathf.Abs(below.normal.y) < 0.4f;
                }
                RaycastHit ground = HelperFunctions.LineCheck(hit.point + hit.normal * 1.5f, hit.point + hit.normal * 1.5f + Vector3.down * 60f, HelperFunctions.LayerType.TerrainMap);
                if (!tall || ground.transform == null)
                {
                    continue;
                }
                point = hit.point;
                normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                drop = hit.point.y - ground.point.y;
                if (drop >= 12f)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private IEnumerator WearJetpack(Item jetpackPrefab, Item food)
    {
        yield return SpawnIntoHand(food);
        GameObject spawned = SpawnInFront(jetpackPrefab, 1.2f);
        Backpack jetpack = spawned != null ? spawned.GetComponent<Backpack>() : null;
        if (jetpack == null)
        {
            yield break;
        }
        yield return new WaitForSeconds(0.5f);
        Local.refs.items.lastEquippedSlotTime = 0f;
        jetpack.Wear(Local);
        float end = Time.realtimeSinceStartup + 4f;
        while (Time.realtimeSinceStartup < end && Local.player.backpackSlot.IsEmpty())
        {
            yield return null;
        }
        // Wearing it can take the hand's item; something to hold again.
        if (Local.data.currentItem == null)
        {
            yield return SpawnIntoHand(food);
        }
    }

    /// <summary>A backpack on (put on the game's way) and an item in each of the three slots.</summary>
    private IEnumerator FullLoad()
    {
        Character local = Local;
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.2f);
        for (byte slot = 0; slot < local.player.itemSlots.Length; slot++)
        {
            local.player.EmptySlot(Optionable<byte>.Some(slot));
        }
        Afflictions.UpdateWeight();
        Item backpack = ItemByName("Backpack");
        if (backpack != null && local.player.backpackSlot.IsEmpty())
        {
            GameObject spawned = SpawnInFront(backpack, 1.2f);
            yield return new WaitForSeconds(0.5f);
            if (spawned != null && spawned.TryGetComponent(out Backpack bag))
            {
                local.refs.items.lastEquippedSlotTime = 0f;
                bag.Wear(local);
                for (float until = Time.realtimeSinceStartup + 4f; Time.realtimeSinceStartup < until && local.player.backpackSlot.IsEmpty();)
                {
                    yield return null;
                }
            }
        }
        foreach (string name in new[] { "Lollipop", "Energy Drink", "Airplane Food" })
        {
            Item item = ItemByName(name);
            if (item != null)
            {
                yield return SpawnIntoHand(item);
            }
        }
    }

    private IEnumerator TieBalloons(Item food)
    {
        yield return SpawnIntoHand(food);
        Local.refs.balloons.TieNewBalloon(0);
        Local.refs.balloons.TieNewBalloon(2);
        float end = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < end && Local.refs.balloons.tiedBalloons.Count < 2)
        {
            yield return null;
        }
    }

    /// <summary>Everything the scout carries: the hand, the pockets and the back.</summary>
    private static int Carried(Character character)
    {
        int count = character.data.currentItem != null ? 1 : 0;
        for (byte slot = 0; slot < character.player.itemSlots.Length; slot++)
        {
            count += character.player.itemSlots[slot].IsEmpty() ? 0 : 1;
        }
        return count + (character.player.backpackSlot.IsEmpty() ? 0 : 1);
    }

    /// <summary>
    /// One fall from 30 m, or from the given point (statuses land but never add up), set up and watched as given.
    /// The items it drops are watched for 3 s: how fast each one flies off and where it comes to rest (13). The
    /// game's way: they collide with the scout at once, as the game makes them.
    /// </summary>
    private IEnumerator FallCase(string name, Func<IEnumerator> prepare, Action<float> eachFrame, Vector3? from = null, bool gameWay = false)
    {
        Character local = Local;
        // Thorns from the last landing (cacti in the Mesa) never fall out by themselves from ascent 17: out first.
        TidyThorns();
        Shield(true);
        yield return prepare();
        yield return new WaitForSeconds(0.5f);
        HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
        Dictionary<Item, float> fastest = new Dictionary<Item, float>();
        Dictionary<Item, float> fastestAt = new Dictionary<Item, float>();
        Vector3 landed = Vector3.zero;
        int clearing = 0;
        A13_ButterFingers.KeepClear = !gameWay;
        int carriedBefore = Carried(local);
        string backBefore = local.player.backpackSlot.IsEmpty() ? "nothing" : local.player.backpackSlot.prefab.gameObject.name;
        Meter drowsy = new Meter(Status.Drowsy);
        Meter injury = new Meter(Status.Injury);
        float airborne = 0f;
        bool climbed = false;
        try
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, from ?? local.Center + Vector3.up * 30f, false);
            yield return new WaitForSeconds(0.5f);
            float started = Time.time;
            float end = Time.time + 25f;
            while (Time.time < end && injury.Count == 0)
            {
                airborne = Time.time - started;
                eachFrame?.Invoke(airborne);
                climbed |= local.data.isClimbing;
                yield return null;
            }
            // What the fall dropped, for 3 s: the fastest each one moved sideways, and where it lies.
            landed = local.Center;
            float droppedAt = Time.time;
            for (float until = Time.time + 3f; Time.time < until;)
            {
                clearing = Mathf.Max(clearing, A13_ButterFingers.Clearing);
                foreach (Item item in Item.ALL_ITEMS)
                {
                    if (item != null && !before.Contains(item) && item.itemState == ItemState.Ground && item.rig != null)
                    {
                        Vector3 v = item.rig.linearVelocity;
                        float flat = new Vector2(v.x, v.z).magnitude;
                        if (!fastest.TryGetValue(item, out float top) || flat > top)
                        {
                            fastest[item] = flat;
                            fastestAt[item] = Time.time - droppedAt;
                        }
                    }
                }
                yield return null;
            }
        }
        finally
        {
            drowsy.Stop();
            injury.Stop();
            A13_ButterFingers.KeepClear = true;
        }
        float Flat(Vector3 at) => new Vector2(at.x - landed.x, at.z - landed.z).magnitude;
        List<Item> dropped = fastest.Keys.Where(i => i != null).ToList();
        float farthest = dropped.Select(i => Flat(i.transform.position)).DefaultIfEmpty(0f).Max();
        float topSpeed = fastest.Values.DefaultIfEmpty(0f).Max();
        string where = string.Join(", ", dropped.Select(i => $"{Plain(i.GetItemName())} {Flat(i.transform.position):0.0} m away (up to {fastest[i]:0.0} m/s, {fastestAt[i]:0.00} s after the landing)"));
        if (gameWay)
        {
            Info($"{name}: where the dropped items went", $"{dropped.Count} items: {where}");
        }
        else if (Rules.Enabled(Lvl.ButterFingers) && dropped.Count > 0)
        {
            Check($"{name}: the dropped items pass through the scout and stay by the landing spot (13)", clearing > 0 && farthest <= 4f && topSpeed <= 5f,
                $"{clearing} passed through the scout; {dropped.Count} items: {where}");
        }
        foreach (Item item in dropped)
        {
            if (item.photonView != null && item.photonView.IsMine)
            {
                PhotonNetwork.Destroy(item.gameObject);
            }
        }
        int carriedAfter = Carried(local);
        float expected = A15_StrongerAilments.FromAscent15(CharacterAfflictions.STATUSTYPE.Drowsy, A12_Drowsy.FallDrowsiness);
        Check($"{name}: the game's fall damage brings ascent 12's drowsiness and ascent 13's drop",
            injury.Total > 0f && Mathf.Abs(drowsy.Total - expected) < 0.026f && carriedBefore > 0 && carriedAfter == 0,
            $"{injury.Total * 100f:F1} fall damage after {airborne:0.0} s{(climbed ? " (grabbed the wall)" : "")}, {drowsy.Total * 100f:F1} drowsiness (expected {expected * 100f:F1}), carried {carriedBefore} items (back: {backBefore}), {carriedAfter} left");
        Protect(true);
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>
    /// The Kiln's lava (on a Citadel map: the gloom) as the game sets it up once the scouts reach the
    /// segment. The scout stays at the Kiln's base camp (the game cannot jump back): by the lit
    /// campfire the lava waits, so it cannot reach the scout in the last step.
    /// </summary>
    private IEnumerator RisingHazard()
    {
        MapHandler.JumpToSegment(Segment.TheKiln);
        yield return new WaitForSeconds(4f);
        LavaRising rising = LavaRising.ALL_LAVA.FirstOrDefault(l => l != null && l.isActiveAndEnabled && l.requiredSegment == Segment.TheKiln);
        Info("jumped to the Kiln segment", $"place {Map.CurrentPlace}, rising {(rising == null ? "nothing" : rising.risingFieldType.ToString())}");
        if (rising != null)
        {
            bool lava = rising.risingFieldType == LavaRising.RisingFieldType.Lava;
            float gameWait = Overrides.Original(rising, "initialWaitTime", rising.initialWaitTime);
            float gameTravel = Overrides.Original(rising, "travelTime", rising.travelTime);
            float travel = Mathf.Min(700f, gameTravel);
            float bottom = rising.lava != null ? rising.lava.position.y : float.NaN;
            float top = rising.topTransform != null ? rising.topTransform.position.y : float.NaN;
            Info(lava ? "the Kiln's lava rise" : "the Citadel's gloom rise",
                $"from y {bottom:0.0} to {top:0.0} ({top - bottom:0.0} m): {(top - bottom) / rising.travelTime:0.000} m/s here, the game's {(top - bottom) / gameTravel:0.000} m/s; the scout at y {Local.Center.y:0.0}");
            Check(lava ? "the Kiln's lava starts after 30 s and rises in 700 s (9)" : "the Citadel's gloom starts after 30 s and reaches the top in 700 s, as the Kiln's lava (9)",
                Mathf.Abs(rising.initialWaitTime - 30f) < 0.01f && Mathf.Abs(rising.travelTime - travel) < 0.5f,
                $"starts after {rising.initialWaitTime:F0} s, rises in {rising.travelTime:F0} s (the game: {gameWait:F0} s and {gameTravel:F0} s)");
        }
    }

    /// <summary>
    /// A checkpoint flag that saves the scout puts back exactly the statuses it saved when it was
    /// planted (ascent 14's boost does not grow them again), plus its 10 curse (15). The flag is planted
    /// the game's way (held, aimed at the ground, used), more statuses come on, then the bar fills:
    /// at ascent 24 passing out is death, which the flag saves.
    /// </summary>
    private IEnumerator CheckpointFlagSave()
    {
        if (target < Lvl.NoUnconscious)
        {
            Info("checkpoint flag check skipped", $"testing ascent {target}: below 24 a full bar only knocks the scout out");
            yield break;
        }
        Item flagPrefab = ItemByName("Flag_Plantable_Checkpoint");
        if (flagPrefab == null)
        {
            Fail("the checkpoint flag is in the item database");
            yield break;
        }
        Character local = Local;
        RemoveTicks(local);
        Info("before the flag: what is still at work", StatusesAndEffects());
        // Bing Bong's evil force (20) adds crab while nobody holds him: held back for this check (in memory).
        Action calm = CalmForMeasuring();
        Protect(false);
        ClearStatuses(Afflictions);
        // Statuses for the flag to save (they land boosted, like anything from ascent 14). Light: the flag puts back what
        // the scout had when it was planted, plus 10 curse, so a heavy bar here would only bring the next death.
        Afflictions.SetStatus(Status.Crab, 0f);
        Afflictions.AddStatus(Status.Drowsy, 0.1f);
        Afflictions.AddStatus(Status.Cold, 0.05f);
        Afflictions.AddStatus(Status.Poison, 0.05f);
        yield return SpawnIntoHand(flagPrefab);
        Item flagItem = held;
        if (flagItem == null)
        {
            calm();
            Protect(true);
            yield break;
        }
        // Aimed at the ground in front of the scout and used, as a player plants it.
        Vector2 look = local.data.lookValues;
        local.data.lookValues = new Vector2(look.x, -60f);
        yield return new WaitForSeconds(0.6f);
        flagItem.StartUsePrimary();
        float end = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < end && flagItem != null && flagItem.gameObject.activeInHierarchy && !flagItem.finishedCast)
        {
            flagItem.ContinueUsePrimary();
            yield return null;
        }
        local.data.lookValues = look;
        yield return new WaitForSeconds(0.5f);
        CheckpointFlag flag = local.data.checkpointFlags.LastOrDefault(f => f != null);
        if (flag == null)
        {
            Fail("plant a checkpoint flag", $"none planted (the flag item {(flagItem != null && flagItem.gameObject.activeInHierarchy ? "is still in the hand" : "is gone")})");
            ClearStatuses(Afflictions);
            calm();
            Protect(true);
            yield break;
        }
        float[] saved = (float[])flag.currentStatuses.Clone();
        Vector3 flagAt = flag.transform.position;
        Screenshot("checkpoint-flag-planted");
        // More statuses after planting (petrification too), then a full bar.
        Afflictions.AddStatus(Status.Drowsy, 0.3f);
        Afflictions.AddStatus(Status.Spores, 0.1f);
        int savedPetrify = flag.currentPetrify;
        local.data.SetPetrify(savedPetrify + 30);
        int petrifyBefore = local.data.petrifyAmount;
        yield return new WaitForSeconds(0.5f);
        Afflictions.AddStatus(Status.Injury, 1f);
        float until = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < until && local.data.checkpointFlags.Contains(flag))
        {
            yield return null;
        }
        Step("checkpoint flag", () =>
        {
            List<string> wrong = new List<string>();
            foreach (Status type in new[] { Status.Injury, Status.Hunger, Status.Cold, Status.Poison, Status.Drowsy, Status.Hot, Status.Spores })
            {
                float now = Afflictions.GetCurrentStatus(type);
                if (Mathf.Abs(now - saved[(int)type]) > 0.001f)
                {
                    wrong.Add($"{type} {now * 100f:0.#} (saved {saved[(int)type] * 100f:0.#})");
                }
            }
            float curse = Afflictions.GetCurrentStatus(Status.Curse);
            float expectedCurse = saved[(int)Status.Curse] + 0.1f;
            bool alive = !local.data.dead && !local.data.passedOut;
            Check("a checkpoint flag saves the scout with exactly the statuses saved when it was planted (14), plus 10 curse (15)",
                alive && wrong.Count == 0 && Mathf.Abs(curse - expectedCurse) < 0.001f,
                $"alive {alive}; {(wrong.Count == 0 ? "every status as saved" : string.Join(", ", wrong))}; curse {curse * 100f:0.#} (expected {expectedCurse * 100f:0.#}); saved drowsiness {saved[(int)Status.Drowsy] * 100f:0.#}, cold {saved[(int)Status.Cold] * 100f:0.#}, poison {saved[(int)Status.Poison] * 100f:0.#}");
            Check("the saved scout stands at the flag", Vector3.Distance(local.Center, flagAt) < 3f, $"{Vector3.Distance(local.Center, flagAt):0.0} m from it");
            Check("a checkpoint flag's save takes no petrification away (15)", local.data.petrifyAmount == petrifyBefore,
                $"petrification {local.data.petrifyAmount} after the save: {petrifyBefore} when the scout went down, {savedPetrify} when the flag was planted (the game puts that back)");
        });
        // Something in the hand: a flag that saves the scout must not knock them down (and drop it) afterwards.
        // First the game picks up what the scout dropped (RegainItems), which changes what is in the hand.
        for (float regainUntil = Time.time + 3f; Time.time < regainUntil && local.refs.items.droppedItems.Count > 0;)
        {
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        yield return SpawnIntoHand(ItemByName("Energy Drink"));
        Item kept = held;
        float watchUntil = Time.time + 3f;
        bool knockedDown = false;
        Info("right after the save", StatusesAndEffects());
        bool reportedLetGo = false;
        while (Time.time < watchUntil)
        {
            if (!knockedDown && (local.data.passedOut || local.data.fullyPassedOut))
            {
                Info("the scout went down after the save", StatusesAndEffects());
            }
            // A destroyed item compares equal to null: the reference itself is what was spawned.
            if (!reportedLetGo && !ReferenceEquals(kept, null) && (kept == null || local.data.currentItem != kept))
            {
                reportedLetGo = true;
                string slots = string.Join(", ", new byte[] { 0, 1, 2, 250 }.Select(s => local.player.GetItemSlot(s)).Select(s => s == null || s.IsEmpty() ? "-" : s.prefab.name));
                Info("the item left the hand after the save", $"{Time.time - (watchUntil - 3f):0.00} s after it arrived; the Energy Drink {(kept == null ? "destroyed" : kept.itemState.ToString())}, statuses {StatusesAndEffects()}, in the hand {(local.data.currentItem != null ? local.data.currentItem.name : "nothing")}, selected slot {local.refs.items.currentSelectedSlot}, slots [{slots}]");
            }
            knockedDown |= local.data.passedOut || local.data.fullyPassedOut;
            yield return null;
        }
        Check("after the save the scout stays on their feet and keeps what they hold (24)", !knockedDown && kept != null && local.data.currentItem == kept,
            $"knocked down {knockedDown}, still holding {(local.data.currentItem != null ? local.data.currentItem.gameObject.name : "nothing")}");
        local.data.SetPetrify(0);
        ClearStatuses(Afflictions);
        calm();
        Protect(true);
    }

    /// <summary>The local scout's statuses and timed effects, and the meals still poisoning them (ascent 14).</summary>
    private static string StatusesAndEffects()
    {
        CharacterAfflictions afflictions = Afflictions;
        string statuses = string.Join(", ", ((Status[])Enum.GetValues(typeof(Status))).Where(t => afflictions.GetCurrentStatus(t) > 0.001f)
            .Select(t => $"{t} {afflictions.GetCurrentStatus(t) * 100f:0.#}"));
        string effects = string.Join(", ", afflictions.afflictionList.Select(a => $"{a.GetAfflictionType()} {a.timeElapsed:0.#}/{a.totalTime:0.#}"));
        return $"statuses [{statuses}], effects [{effects}], meals poisoning {A15_FoodPoison.meals.Count}, passed out {Local.data.passedOut}, sum {afflictions.statusSum:0.00}";
    }

    /// <summary>
    /// A Mandrake's scream (17) held in the hand: one whole scream with the master volume at 0 (set in
    /// memory for the test, never saved), then one with the volume back. With the sound on a scream
    /// gives up to 75 drowsiness; with it off half of that (the game: nothing).
    /// </summary>
    private IEnumerator MandrakeHeard()
    {
        if (target < Lvl.Hazards)
        {
            Info("Mandrake check skipped", $"testing ascent {target}");
            yield break;
        }
        Item mandrakePrefab = ItemByName("Mandrake");
        MasterVolumeSetting master = GameHandler.Instance != null && GameHandler.Instance.SettingsHandler != null ? GameHandler.Instance.SettingsHandler.GetSetting<MasterVolumeSetting>() : null;
        if (mandrakePrefab == null || master == null)
        {
            Fail("a Mandrake and the master volume setting", $"Mandrake {mandrakePrefab != null}, volume setting {master != null}");
            yield break;
        }
        RemoveTicks(Local);
        yield return SpawnIntoHand(mandrakePrefab);
        Item item = held;
        Mandrake mandrake = item != null ? item.GetComponent<Mandrake>() : null;
        if (mandrake == null)
        {
            Fail("hold a Mandrake", item == null ? "nothing in the hand" : item.gameObject.name);
            yield break;
        }
        if (Compat.Tfa.Present)
        {
            bool voice = mandrake.GetComponents<MonoBehaviour>().Any(behaviour => behaviour != null && behaviour.GetType().FullName == "TrueFinalAscent.FinalAscentMandrakeLine");
            Check("with True Final Ascent the Mandrake keeps TFA's voice lines and still counts as the game's (its rules apply)",
                voice && !Compat.Tfa.Spawned(mandrake), $"TFA's voice lines on it {voice}, counted as one of TFA's {Compat.Tfa.Spawned(mandrake)}");
        }
        Shield(true);
        Meter muted = null;
        Meter loud = null;
        mutedMasterVolume = master.Value;
        master.Value = 0f;
        master.ApplyValue();
        Info("master volume set to 0 for the Mandrake check", $"it was {mutedMasterVolume:0.00}; nothing is saved");
        try
        {
            yield return OneScream(mandrake, meter => muted = meter);
        }
        finally
        {
            RestoreMasterVolume();
        }
        yield return OneScream(mandrake, meter => loud = meter);
        float share = A17_Hazards.MutedScreamShare;
        Check("with the sound on, a whole scream of a Mandrake in the hand gives up to 75 drowsiness (17)",
            loud != null && loud.Total > 0.68f && loud.Total < 0.775f,
            loud == null ? "no whole scream" : $"{loud.Total * 100f:0.#} drowsiness in {loud.Count} additions over {loud.Seconds:0.#} s");
        Check($"with the sound off, the same scream still makes the scout drowsy, at {share:0%} of that (17; the game: nothing)",
            muted != null && loud != null && muted.Total > 0.2f && Mathf.Abs(muted.Total - loud.Total * share) <= 0.05f,
            muted == null ? "no whole scream" : $"{muted.Total * 100f:0.#} drowsiness in {muted.Count} additions over {muted.Seconds:0.#} s with the master volume at 0 (sound on: {(loud != null ? loud.Total * 100f : 0f):0.#})");
        if (item != null)
        {
            Local.refs.items.EquipSlot(Optionable<byte>.None);
            yield return new WaitForSeconds(0.3f);
            // Wherever it went (a full inventory puts it in the hands' spare slot): a carried Mandrake keeps screaming.
            foreach (byte slot in new byte[] { 0, 1, 2, 250 })
            {
                ItemSlot itemSlot = Local.player.GetItemSlot(slot);
                if (itemSlot != null && !itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == mandrakePrefab.itemID)
                {
                    Local.player.EmptySlot(Optionable<byte>.Some(slot));
                }
            }
        }
        foreach (Item left in Item.ALL_ITEMS.Where(i => i != null && i.itemID == mandrakePrefab.itemID && i.photonView != null && i.photonView.IsMine).ToList())
        {
            PhotonNetwork.Destroy(left.gameObject);
        }
        yield return new WaitForSeconds(0.3f);
        Shield(false);
        ClearStatuses(Afflictions);
        Protect(true);
    }

    /// <summary>The drowsiness the Mandrake's next whole scream adds (the one under way, if any, ends first).</summary>
    private IEnumerator OneScream(Mandrake mandrake, Action<Meter> done)
    {
        for (float until = Time.time + 5f; Time.time < until && mandrake != null && mandrake.screaming;)
        {
            yield return null;
        }
        for (float until = Time.time + 10f; Time.time < until && mandrake != null && !mandrake.screaming;)
        {
            yield return null;
        }
        if (mandrake == null || !mandrake.screaming)
        {
            yield break;
        }
        Meter meter = new Meter(Status.Drowsy);
        int frames = 0;
        int ticking = 0;
        int active = 0;
        for (float until = Time.time + 5f; Time.time < until && mandrake != null && mandrake.screaming;)
        {
            frames++;
            ticking += mandrake.timeEvent != null && mandrake.timeEvent.enabled ? 1 : 0;
            active += mandrake.timeEvent != null && mandrake.timeEvent.gameObject.activeInHierarchy ? 1 : 0;
            yield return null;
        }
        yield return null;
        Info("scream ticks", $"{frames} frames screaming: drowsiness timer on in {ticking}, its object active in {active}; hears it {A17_Hazards.HearsMandrakes()}");
        done(meter.Stop());
    }

    private void RestoreMasterVolume()
    {
        if (!mutedMasterVolume.HasValue)
        {
            return;
        }
        MasterVolumeSetting master = GameHandler.Instance != null && GameHandler.Instance.SettingsHandler != null ? GameHandler.Instance.SettingsHandler.GetSetting<MasterVolumeSetting>() : null;
        if (master != null)
        {
            master.Value = mutedMasterVolume.Value;
            master.ApplyValue();
        }
        mutedMasterVolume = null;
    }

    private IEnumerator BeeKnockout()
    {
        Character local = Local;
        expectingDeath = true;
        RemoveTicks(local);
        // Empty pockets (carried items weigh on the bar too), then 30 of the bar taken by hunger.
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        for (byte slot = 0; slot < local.player.itemSlots.Length; slot++)
        {
            local.player.EmptySlot(Optionable<byte>.Some(slot));
        }
        Afflictions.UpdateWeight();
        yield return new WaitForSeconds(0.3f);
        Protect(false);
        ClearStatuses(Afflictions);
        Afflictions.SetStatus(Status.Hunger, Mathf.Max(0f, 0.3f - Afflictions.statusSum));
        Info("the scout has 70 of 100 stamina left", $"statuses take {Afflictions.statusSum * 100f:F0} of the bar");
        float spawned = Time.time;
        float firstSting = -1f;
        float barFull = -1f;
        Action<Status, float> listener = (type, amount) =>
        {
            if (type == Status.Poison && firstSting < 0f)
            {
                firstSting = Time.time;
            }
        };
        Afflictions.OnAddedIncrementalStatus += listener;
        A17_BeeAmbush.Spawn(local);
        float end = Time.time + 40f;
        try
        {
            while (Time.time < end && !local.data.passedOut && !local.data.dead)
            {
                if (barFull < 0f && Afflictions.statusSum > 0.99f)
                {
                    barFull = Time.time;
                }
                yield return null;
            }
        }
        finally
        {
            Afflictions.OnAddedIncrementalStatus -= listener;
        }
        float knockedOutAt = Time.time;
        bool knockedOut = local.data.passedOut || local.data.dead;
        BeeSwarm attacker = FindObjectsByType<BeeSwarm>(FindObjectsSortMode.None).Where(b => A17_BeeAmbush.IsAmbush(b) && !ScatteredBees.IsWaiting(b)).OrderBy(b => Vector3.Distance(b.transform.position, local.Center)).FirstOrDefault();
        Info("the knocking-out swarm", attacker == null ? "none found" :
            $"{Vector3.Distance(attacker.transform.position, local.Center):F1} m from the scout, target {(attacker.currentAggroCharacter != null ? attacker.currentAggroCharacter.characterName : "none")}, angry {attacker.beesAngry}, place {Map.CurrentPlace}, statuses locked {local.statusesLocked}, sum {Afflictions.statusSum:F2}");
        Check("a bee attack knocks out a scout with 70 stamina (17)", knockedOut && firstSting >= 0f,
            !knockedOut ? "not knocked out within 40 s" :
            $"first sting {firstSting - spawned:F1} s after the swarm appeared; bar full {barFull - firstSting:F2} s after the first sting; passed out {knockedOutAt - firstSting:F2} s after the first sting ({Application.targetFrameRate} target fps, {1f / Time.smoothDeltaTime:F0} fps)");
        if (target >= Lvl.NoUnconscious)
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!local.data.dead && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Check("passing out is instant death (24)", local.data.dead, $"dead {local.data.dead}, passed out {local.data.passedOut}");
        }
    }
}

/// <summary>Records what the AudioListener hears (the final mix) into a WAV file (diagnostics).</summary>
internal sealed class ListenerRecorder : MonoBehaviour
{
    private readonly List<float> samples = new List<float>();
    private readonly object gate = new object();
    private volatile bool recording;
    private int channels = 2;

    internal static ListenerRecorder Attach()
    {
        AudioListener listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
        if (listener == null)
        {
            return null;
        }
        return listener.GetComponent<ListenerRecorder>() ?? listener.gameObject.AddComponent<ListenerRecorder>();
    }

    internal void Begin()
    {
        lock (gate)
        {
            samples.Clear();
        }
        recording = true;
    }

    /// <summary>Stops and writes a 16-bit WAV; returns the number of samples written.</summary>
    internal int End(string path)
    {
        recording = false;
        float[] data;
        lock (gate)
        {
            data = samples.ToArray();
            samples.Clear();
        }
        int rate = AudioSettings.outputSampleRate;
        using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + data.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(data.Length * 2);
            foreach (float sample in data)
            {
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(sample * 32767f), -32768, 32767));
            }
        }
        return data.Length;
    }

    private void OnAudioFilterRead(float[] data, int channelCount)
    {
        if (!recording)
        {
            return;
        }
        channels = channelCount;
        lock (gate)
        {
            samples.AddRange(data);
        }
    }
}
