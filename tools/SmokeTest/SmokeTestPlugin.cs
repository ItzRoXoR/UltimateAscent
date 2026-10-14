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
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// Plays through the start of a solo run on its own and checks the rules of Ultimate Ascent from
/// inside the game: title screen, airport and boarding pass, then a run on today's map (the beach,
/// then a jump to the Alpine segment). Only active when PEAK is started with "--ua-smoketest";
/// "--ua-smoketest-ascent=N" picks the ascent (default 25) and "--ua-smoketest-noapocalypse" switches
/// True Final Ascent's Apocalypse mode off for this session (in memory only); "--ua-smoketest-only=a,b"
/// runs only the run steps whose names contain one of the words (for quick checks of one rule).
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
        foreach (string arg in args)
        {
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
        host.AddComponent<Runner>().Begin(Logger, ascent, args.Contains(NoApocalypseSwitch), args.Contains(ApocalypseSwitch), only);
    }
}

internal sealed class Runner : MonoBehaviour
{
    private const float WatchdogSeconds = 900f;

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

    /// <summary>Statuses apply, but are cleared every frame before they can add up to passing out.</summary>
    private bool shielded;

    /// <summary>TFA's Apocalypse mode runs the weather of this climb.</summary>
    private bool apocalypse;
    private bool expectingDeath;

    /// <summary>The player's master volume while the test has it at 0 (in memory only; put back in any case).</summary>
    private float? mutedMasterVolume;

    private Item held;

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

    internal void Begin(ManualLogSource log, int target, bool noApocalypse, bool apocalypseOn, string[] only)
    {
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
        harmony.CreateClassProcessor(typeof(FogCalls)).Patch();
        harmony.CreateClassProcessor(typeof(CustomRunSpawn)).Patch();
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
                    yield break;
                }
                current = body.Current;
            }
            catch (Exception ex)
            {
                Fail(name, "exception: " + ex);
                yield break;
            }
            yield return current;
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
        yield return CustomRunScreen();
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
        yield return new WaitForSeconds(2f);

        (string name, Func<IEnumerator> run)[] steps =
        {
            ("storms (25)", () => Now(CheckStorms)),
            ("extra tornadoes (25)", () => Tornadoes(wake)),
            ("no revive statues (19)", () => Now(CheckRevives)),
            ("Tropics only (17)", () => Now(CheckTropicsOnly)),
            ("sun on the beach (23)", () => SunHeat(true, "on the beach")),
            ("roasting (23)", Roasting),
            ("stronger ailments (15)", () => Now(CheckAilments)),
            ("painful luggage (21)", () => Now(CheckLuggage)),
            ("half food (14)", HalfFood),
            ("ticks (17)", TickBites),
            ("eating ticks (14)", EatTick),
            ("burned items (17)", BurnedItemHeat),
            ("burned backpack (17)", BackpackHeat),
            ("Climbing Chalk (9, 14)", ClimbingChalk),
            ("custom run: chalk and Lightweight Parasol switches", () => Now(() => Step("custom-run switches", CustomRunSwitches))),
            ("custom run: suitcases follow the switches", CustomRunSuitcases),
            ("Lightweight Parasol look (9)", ParasolLook),
            ("lanterns (14)", Lanterns),
            ("ascent switches (config)", AscentSwitches),
            ("checkpoint flag (14, 15, 24)", CheckpointFlagSave),
            ("Mandrake heard (17)", MandrakeHeard),
            ("TFA's Apocalypse events stay TFA's", TfaEvents),
            ("Bing Bong (20)", () => BingBong(wake)),
            ("fog (9, 18)", Fog),
            ("weather (25)", () => Weather(wake)),
            ("bee ambush (17)", BeeAmbush),
            ("bee moments (17)", BeeMoments),
            ("jellyfish (17)", Jellyfish),
            ("first campfire and the fog after it (18)", FirstCampfire),
            ("second segment (17)", SecondSegment),
            ("Alpine (18, 23)", Alpine),
            ("after the Alps (18)", AfterAlps),
            ("recovery rates and the Citadel (16, 17, 22)", RecoveryAndCitadel),
            ("fall damage (12)", FallDrowsiness),
            ("falls with flying items (12, 13)", FallsWithItems),
            ("rising lava (9)", RisingHazard),
            ("bees knock a scout out (17, 24)", BeeKnockout),
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
            yield return Guard(name, run());
        }
        Finish();
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
        Check($"the Lightweight Parasol weighs {expectedWeight * 2.5f}% of the bar, and Item Stats shows that (nothing below 14, 5% from 14)",
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
    /// cold, injury, poison, drowsiness and heat; from ascent 14 at 1.5 and 2.5 a second (the game: 2.5
    /// and 5). Each is held and lit the way a scout lights it (the use held for its 0.5 s). The Faerie
    /// Lantern is measured on injury, which nothing else heals; the Lantern, which only warms, on cold,
    /// less what the same spot takes off while it is unlit (the start's fires warm a little).
    /// </summary>
    private IEnumerator Lanterns()
    {
        bool worse = Rules.Enabled(Lvl.WorseItems);
        yield return LanternCase("Lantern", worse ? 1.5f : 2.5f, false);
        yield return LanternCase("Lantern_Faerie", worse ? 2.5f : 5f, true);
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
        Check($"a lit {name} takes {perSecond:0.#} {(faerie ? "injury (and of each other status)" : "cold")} a second off the scouts near it (14)",
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
    /// climb stays on its ascent. With ascent 14 off, a new Lantern warms at the game's 2.5 again.
    /// </summary>
    private IEnumerator AscentSwitches()
    {
        string file = UltimateAscent.Plugin.Instance.Config.ConfigFilePath;
        string text = File.Exists(file) ? File.ReadAllText(file) : "";
        int switches = Enumerable.Range(Lvl.First, Lvl.Count).Count(level => text.Contains("Ascent " + level + " = "));
        Check("the config has a switch for each of ascents 9-25 and none of the retired entries",
            switches == Lvl.Count && !text.Contains("[Big Lobbies]") && !text.Contains("Nerfed Revives") && !text.Contains("Double Apocalypse Weather"),
            $"{switches} switches, Big Lobbies {text.Contains("[Big Lobbies]")}, Double Apocalypse Weather {text.Contains("Double Apocalypse Weather")}");
        int off = target >= Lvl.BingBong ? Lvl.BingBong : Lvl.Drowsy;
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
        Check("a Lantern made while Ascent 14 is switched off warms at the game's 2.5 a second", field != null && Mathf.Abs(field.statusAmountPerSecond + 0.025f) < 0.0001f,
            field == null ? "no Lantern" : $"field {field.statusAmountPerSecond * 100f:0.##}/s");
        if (spawned != null)
        {
            PhotonNetwork.Destroy(spawned);
        }
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

        (string prefab, string after, string label)[] ours =
        {
            (ClimbingChalkItem.PrefabName, ClimbingChalkItem.EnergyDrinkPrefab, "CLIMBING CHALK"),
            (A14_Items.RootsParasolPrefab, "Parasol", "LIGHTWEIGHT PARASOL"),
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
        return true;
    }

    private void Depart()
    {
        BoardingPass pass = GUIManager.instance.boardingPass;
        if (RunSettings.IsCustomRun)
        {
            Fail("the boarding pass is back on a normal run before departure");
            RunSettings.IsCustomRun = false;
        }
        pass.ascentIndex = target;
        pass.UpdateAscent();
        if (noApocalypse)
        {
            Step("switch TFA's Apocalypse off", () => SetApocalypse(false));
        }
        else if (apocalypseOn)
        {
            Step("switch TFA's Apocalypse on", () => SetApocalypse(true));
        }
        pass.StartGame();
        Info("departure", $"ascent {target} started from the boarding pass");
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
        // Ascent 23's sun heat (0.005/s) x ascent 15's heat factor (1.5).
        float expected = 0.005f * 1.5f * heat.Seconds;
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
        Check("the burned parasol stays in the hand and stops smoking (23)", item != null && local.data.currentItem == item && (smoke == null || !smoke.isEmitting) && !A23_HotSun.exposures.ContainsKey(item),
            item == null ? "the parasol is gone" : $"in the hand {local.data.currentItem == item}, smoke {(smoke == null ? "none" : smoke.isEmitting ? "still rising" : "stopped")}");
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
        float expectedHeat = target >= Lvl.SlowRecovery && volcano ? 0.006f : 0.013f;
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
                // 10 on the bar, ascent 15's boost included.
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
                Check($"{type} applies x{factor} (15)", Mathf.Abs(added - expected) < 0.0001f, $"{added:0.0000} for 0.1 (expected {expected:0.0000})");
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
            Check("food restores half as much (14)", Mathf.Abs(restored - expected) < 0.002f,
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

            // Control: the same tick below ascent 14 does feed.
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
            Check("eating a tick restores no hunger (14)", Mathf.Abs(atTarget - 0.5f) < 0.0005f, $"hunger 0.500 -> {atTarget:0.000}");
            Check("control: below ascent 14 a tick still feeds", hungerActions.Count > 0 && below < 0.499f, $"hunger 0.500 -> {below:0.000} at ascent {Lvl.WorseItems - 1}");
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
        for (float t = 0f; t < 1f; t += Time.deltaTime)
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

        // The first real use: held for its whole 2 s; the rub is sampled every frame of it, and what
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
        float expectedShare = Rules.Enabled(Lvl.WorseItems) ? 0.85f : 0.75f;
        float expectedSeconds = Rules.Enabled(Lvl.WorseItems) ? 12f : 16f;
        Step("chalk used once", () =>
        {
            Check("the hand-rub sound plays while the chalk is used", playing >= useSeconds * 0.9f && Mathf.Abs(volume - 0.25f) < 0.026f && farthest < 2f,
                $"playing {playing:0.00} s of the {useSeconds:0.00} s use, volume {volume:0.000}, at most {farthest:0.0} m from the scout");
            Afflictions.HasAfflictionType(Peak.Afflictions.Affliction.AfflictionType.ClimbingChalk, out Peak.Afflictions.Affliction applied);
            Peak.Afflictions.Affliction_ClimbingChalk chalked = applied as Peak.Afflictions.Affliction_ClimbingChalk;
            bool kept = !UsedUp(chalk, prefab);
            int left = Rules.Enabled(Lvl.WorseItems) ? 1 : 2;
            Check($"a 2 s use chalks the scout and leaves {left} of its 3 uses (ascent 14 takes two per use)", chalked != null && kept && UsesLeft(chalk) == left && Mathf.Abs(UseBar(chalk) - left / 3f) < 0.001f,
                $"effect {(chalked != null ? "on" : "missing")}, still in the hand {kept}, uses left {(kept ? UsesLeft(chalk) : 0)}, use bar {(kept ? UseBar(chalk) : 0f):0.00}");
            Check("chalk: climbing costs " + Mathf.RoundToInt(expectedShare * 100f) + "% stamina for " + expectedSeconds + " s (9, 14)",
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
        // effect's time over; the last uses the chalk up (three uses, two from ascent 14).
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
            Check($"the chalk is used up after {expectedUses} uses, each puffing dust and starting the effect's time over (3; 2 from 14)",
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

    /// <summary>A Jetpack on the ground burns the chalk in the hand as fuel, as its backpack wheel does it (35% less from ascent 14).</summary>
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
            return renderer != null ? renderer.material.GetColor("_BaseColor") : Color.clear;
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
        Check("cooking darkens the chalk bag like any item: brown once cooked, almost black when burned",
            Near(fresh, Color.white) && Near(cooked, ItemCooking.GetCookColor(1)) && Near(burned, ItemCooking.GetCookColor(4)) && times == 4,
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

    private IEnumerator BurnedItemHeat()
    {
        if (held == null || Local.data.currentItem != held)
        {
            Info("burned item check skipped: nothing in the hand");
            yield break;
        }
        Item item = held;
        // One burn tick: campfire flames (0.1 heat/s) for half a second, x1.5 (ascent 15). Other heat
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
        // A bite lands 2.5 poison (ascent 15's x1.75 included), every 0.5 s (the game: 5 every 29 s), and
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
        yield return WaitUntilTime(wake + 17f);
        // Nobody has held Bing Bong since waking up: from 10 s he talked in his burned voice, and fell
        // silent when the crab status came at 15 s.
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
        // given by the game is boosted by ascent 15.
        Shield(true);
        float fromTfa = Added(Status.Cold, () => inflict.Invoke(null, new object[] { Afflictions, Status.Cold, 0.3f }));
        float fromGame = Added(Status.Cold, () => Afflictions.AddStatus(Status.Cold, 0.3f));
        Protect(true);
        float boost = A15_StrongerAilments.Factor(Status.Cold);
        Check("a status TFA's code gives lands as TFA gives it, the game's own is boosted (15)", Mathf.Abs(fromTfa - 0.3f) < 0.001f && Mathf.Abs(fromGame - 0.3f * boost) < 0.001f,
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
    /// (ascent 15's poison boost on top). The fog is brought over the scout the way it closes in on
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
        Check($"the rising fog poisons {where} (18): 3x the game's cold, as poison, ascent 15's boost on top", poison.Total > expected * 0.9f && poison.Total < expected * 1.1f,
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
        Step("light the first campfire", () => campfire.Interact_CastFinished(local));
        float end = Time.realtimeSinceStartup + 30f;
        while (fog.currentID == 0 && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
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
        BeeSwarm template = A17_BeeAmbush.template;
        Info("bee swarm ranges", template == null
            ? "the beehives' swarm was not found"
            : $"attacks within {template.hiveAggroDistance:F1} m when angry ({template.defaultAggroDistance:F1} m calm), gives up beyond {template.deAggroDistance:F1} m, stays within {template.maxHiveDistance:F1} m of its spot");
        yield return new WaitForSeconds(0.5f);
        List<BeeSwarm> swarms = FindObjectsByType<BeeSwarm>(FindObjectsSortMode.None).Where(A17_BeeAmbush.IsAmbush).ToList();
        Check("a bee ambush swarm appears (17)", swarms.Count == 1, $"{swarms.Count} ambush swarm(s) from prefab '{A17_BeeAmbush.prefabName}'");
        DayNightManager clock = DayNightManager.instance;
        if (clock != null)
        {
            A17_BeeAmbush.Moments(clock, out float midday, out float midnight);
            float dayMiddle = (clock.dayStart + clock.dayEnd) / 2f;
            float nightMiddle = Mathf.Repeat(clock.dayEnd + (24f - (clock.dayEnd - clock.dayStart)) / 2f, 24f);
            Check("bee ambushes come at the middle of the day and of the night (17)", Mathf.Abs(midday - dayMiddle) < 0.01f && Mathf.Abs(midnight - nightMiddle) < 0.01f,
                $"day {clock.dayStart:0.#}-{clock.dayEnd:0.#}: ambush moments at {clock.FloatToTimeString(midday)} and {clock.FloatToTimeString(midnight)}");
        }
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

        // The stings: angry bees land 15 poison a second (calm ones 10), ascent 15's x1.75 included.
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
        if (swarm != null)
        {
            PhotonNetwork.Destroy(swarm.gameObject);
        }
    }

    /// <summary>
    /// The clock brings the bee moments: it is put just before the middle of the day and of the night
    /// and runs past it on its own (outside the Tropics a moment brings no swarm). Setting the clock
    /// back must not count as midnight going by.
    /// </summary>
    private IEnumerator BeeMoments()
    {
        DayNightManager clock = DayNightManager.instance;
        if (clock == null || Map.CurrentPlace == Place.Tropics)
        {
            Info("the bee moment checks are skipped", clock == null ? "no day-night clock" : "the scout is in the Tropics");
            yield break;
        }
        float original = clock.timeOfDay;
        A17_BeeAmbush.Moments(clock, out float midday, out float midnight);
        foreach ((string name, float moment) in new[] { ("day", midday), ("night", midnight) })
        {
            float before = Mathf.Repeat(moment - 0.5f, 24f);
            float back = Mathf.Repeat(moment - 0.6f, 24f);
            float start = Mathf.Repeat(moment - 0.02f, 24f);
            clock.timeOfDay = before;
            yield return null;
            yield return null;
            A17_BeeAmbush.lastMomentAt = -1000f;
            clock.timeOfDay = back;
            yield return null;
            yield return null;
            Check($"a clock set back before the middle of the {name} brings no bees (17)", A17_BeeAmbush.lastMomentAt < 0f,
                $"{clock.FloatToTimeString(before)} -> {clock.FloatToTimeString(back)}: {(A17_BeeAmbush.lastMomentAt < 0f ? "no moment" : "a moment")}");
            A17_BeeAmbush.lastMomentAt = -1000f;
            clock.timeOfDay = start;
            float deadline = Time.time + 5f;
            while (A17_BeeAmbush.lastMomentAt < 0f && Time.time < deadline)
            {
                yield return null;
            }
            float now = clock.timeOfDay;
            if (A17_BeeAmbush.lastMomentAt < 0f && Mathf.Abs(Mathf.DeltaAngle(now * 15f, start * 15f)) < 0.01f)
            {
                Info($"the bee moment of the {name} is not checked", $"the clock stands at {clock.FloatToTimeString(now)} (True Final Ascent's weather holds it)");
                continue;
            }
            float off = Mathf.Abs(Mathf.DeltaAngle(now * 15f, moment * 15f)) / 15f;
            Check($"the bee moment comes at the middle of the {name} (17)", A17_BeeAmbush.lastMomentAt >= 0f && off < 0.05f,
                A17_BeeAmbush.lastMomentAt >= 0f ? $"at {clock.FloatToTimeString(now)} (expected {clock.FloatToTimeString(moment)})" : $"none by {clock.FloatToTimeString(now)}");
        }
        clock.timeOfDay = original;
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
            // Night cold (0.005/s) x 1.5 (ascent 18) x 1.3 (ascent 15's cold factor).
            float expected = dark ? Ascents.nightColdRate * 1.5f * 1.3f * cold.Seconds : 0f;
            Check($"{(dark ? "night cold" : "no night cold")} in the {name} (18)", dark ? Mathf.Abs(cold.Total - expected) < expected * 0.2f : cold.Total < 0.0005f,
                $"{cold.Total:0.0000} night cold in {cold.Seconds:F1} s (expected {expected:0.0000})");
            if (!dark)
            {
                yield return Guard("sun in the Alpine (23)", SunHeat(false, "in the Alpine"));
            }
        }
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
            $"{poison * 100f:0.0} poison, ascent 15's x1.75 included (the game: 5)");
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
        // 60 from ascent 15 on; below 15 without that ascent's boost (46.2, of which the first fall lands 45).
        float expected = A15_StrongerAilments.FromAscent15(CharacterAfflictions.STATUSTYPE.Drowsy, A12_Drowsy.FallDrowsiness);
        Check("fall damage makes the scout 60 drowsy, less below 15 (12)", injury.Total > 0f && Mathf.Abs(drowsy.Total - expected) < 0.026f,
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

    /// <summary>One fall from 30 m, or from the given point (statuses land but never add up), set up and watched as given.</summary>
    private IEnumerator FallCase(string name, Func<IEnumerator> prepare, Action<float> eachFrame, Vector3? from = null)
    {
        Character local = Local;
        Shield(true);
        yield return prepare();
        yield return new WaitForSeconds(0.5f);
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
            yield return new WaitForSeconds(0.5f);
        }
        finally
        {
            drowsy.Stop();
            injury.Stop();
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
            float travel = lava ? Mathf.Min(700f, gameTravel) : gameTravel / 2.5f;
            Check(lava ? "the Kiln's lava starts after 30 s and rises in 700 s (9)" : "the Citadel's gloom starts after 30 s and rises 2.5x as fast (9)",
                Mathf.Abs(rising.initialWaitTime - 30f) < 0.01f && Mathf.Abs(rising.travelTime - travel) < 0.5f,
                $"starts after {rising.initialWaitTime:F0} s, rises in {rising.travelTime:F0} s (the game: {gameWait:F0} s and {gameTravel:F0} s)");
        }
    }

    /// <summary>
    /// A checkpoint flag that saves the scout puts back exactly the statuses it saved when it was
    /// planted (ascent 15's boost does not grow them again), plus its 10 curse (14). The flag is planted
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
        Protect(false);
        ClearStatuses(Afflictions);
        // Statuses for the flag to save (they land boosted, like anything from ascent 15).
        Afflictions.AddStatus(Status.Drowsy, 0.2f);
        Afflictions.AddStatus(Status.Cold, 0.1f);
        Afflictions.AddStatus(Status.Poison, 0.1f);
        yield return SpawnIntoHand(flagPrefab);
        Item flagItem = held;
        if (flagItem == null)
        {
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
            Protect(true);
            yield break;
        }
        float[] saved = (float[])flag.currentStatuses.Clone();
        Vector3 flagAt = flag.transform.position;
        Screenshot("checkpoint-flag-planted");
        // More statuses after planting, then a full bar.
        Afflictions.AddStatus(Status.Drowsy, 0.3f);
        Afflictions.AddStatus(Status.Spores, 0.1f);
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
            Check("a checkpoint flag saves the scout with exactly the statuses saved when it was planted (15), plus 10 curse (14)",
                alive && wrong.Count == 0 && Mathf.Abs(curse - expectedCurse) < 0.001f,
                $"alive {alive}; {(wrong.Count == 0 ? "every status as saved" : string.Join(", ", wrong))}; curse {curse * 100f:0.#} (expected {expectedCurse * 100f:0.#}); saved drowsiness {saved[(int)Status.Drowsy] * 100f:0.#}, cold {saved[(int)Status.Cold] * 100f:0.#}, poison {saved[(int)Status.Poison] * 100f:0.#}");
            Check("the saved scout stands at the flag", Vector3.Distance(local.Center, flagAt) < 3f, $"{Vector3.Distance(local.Center, flagAt):0.0} m from it");
        });
        // Something in the hand: a flag that saves the scout must not knock them down (and drop it) afterwards.
        yield return SpawnIntoHand(ItemByName("Energy Drink"));
        Item kept = held;
        float watchUntil = Time.time + 3f;
        bool knockedDown = false;
        while (Time.time < watchUntil)
        {
            knockedDown |= local.data.passedOut || local.data.fullyPassedOut;
            yield return null;
        }
        Check("after the save the scout stays on their feet and keeps what they hold (24)", !knockedDown && kept != null && local.data.currentItem == kept,
            $"knocked down {knockedDown}, still holding {(local.data.currentItem != null ? local.data.currentItem.gameObject.name : "nothing")}");
        ClearStatuses(Afflictions);
        Protect(true);
    }

    /// <summary>
    /// A Mandrake's scream (17) makes drowsy only players who can hear it, as in the game: held in the
    /// hand it screams, but with the master volume at 0 (set in memory for the test, never saved) it
    /// gives no drowsiness; with the volume back it does.
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
        Shield(true);
        int mutedScreams = 0;
        int loudScreams = 0;
        Meter muted;
        Meter loud;
        mutedMasterVolume = master.Value;
        master.Value = 0f;
        master.ApplyValue();
        Info("master volume set to 0 for the Mandrake check", $"it was {mutedMasterVolume:0.00}; nothing is saved");
        muted = new Meter(Status.Drowsy);
        try
        {
            bool screaming = false;
            for (float until = Time.time + 8f; Time.time < until && item != null;)
            {
                mutedScreams += mandrake.screaming && !screaming ? 1 : 0;
                screaming = mandrake.screaming;
                yield return null;
            }
        }
        finally
        {
            muted.Stop();
            RestoreMasterVolume();
        }
        // The scream ends before the sound comes back on.
        yield return new WaitForSeconds(0.2f);
        loud = new Meter(Status.Drowsy);
        try
        {
            bool screaming = mandrake.screaming;
            for (float until = Time.time + 8f; Time.time < until && item != null;)
            {
                loudScreams += mandrake.screaming && !screaming ? 1 : 0;
                screaming = mandrake.screaming;
                yield return null;
            }
        }
        finally
        {
            loud.Stop();
        }
        Check("a Mandrake's scream gives no drowsiness with the sound off (17; as in the game)", mutedScreams > 0 && muted.Total == 0f,
            $"{mutedScreams} screams in {muted.Seconds:0.#} s with the master volume at 0: {muted.Total * 100f:0.#} drowsiness in {muted.Count} additions");
        Check("with the sound on, the same Mandrake's screams make the scout drowsy (17)", loudScreams > 0 && loud.Total > 0.1f,
            $"{loudScreams} screams in {loud.Seconds:0.#} s: {loud.Total * 100f:0.#} drowsiness in {loud.Count} additions");
        if (item != null)
        {
            Local.refs.items.EquipSlot(Optionable<byte>.None);
            yield return new WaitForSeconds(0.3f);
            for (byte slot = 0; slot < Local.player.itemSlots.Length; slot++)
            {
                if (!Local.player.itemSlots[slot].IsEmpty() && Local.player.itemSlots[slot].prefab != null && Local.player.itemSlots[slot].prefab.itemID == mandrakePrefab.itemID)
                {
                    Local.player.EmptySlot(Optionable<byte>.Some(slot));
                }
            }
        }
        Shield(false);
        ClearStatuses(Afflictions);
        Protect(true);
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
