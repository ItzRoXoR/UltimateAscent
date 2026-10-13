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
using Zorro.Core;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// Plays through the start of a solo run on its own and checks the rules of Ultimate Ascent from
/// inside the game: title screen, airport and boarding pass, then a run on today's map (the beach,
/// then a jump to the Alpine segment). Only active when PEAK is started with "--ua-smoketest";
/// "--ua-smoketest-ascent=N" picks the ascent (default 25) and "--ua-smoketest-noapocalypse" switches
/// True Final Ascent's Apocalypse mode off for this session (in memory only).
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

    private void Awake()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!args.Contains(Switch))
        {
            Logger.LogInfo("Inactive. Start PEAK with " + Switch + " to run the smoke test.");
            return;
        }
        int ascent = Lvl.Last;
        foreach (string arg in args)
        {
            if (arg.StartsWith(AscentSwitch) && int.TryParse(arg.Substring(AscentSwitch.Length), out int parsed))
            {
                ascent = Mathf.Clamp(parsed, Lvl.First, Lvl.Last);
            }
        }
        GameObject host = new GameObject("UltimateAscentSmokeTest");
        DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent<Runner>().Begin(Logger, ascent, args.Contains(NoApocalypseSwitch));
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
    private string secondWeatherSeen;

    private Item held;

    private ConfigFile config;
    private bool configChanged;
    private bool savedSaveOnSet;
    private bool savedUnlockAll;
    private int savedDefault;

    private MethodInfo apocalypseRestore;
    private bool apocalypseChanged;
    private bool savedApocalypse;

    internal void Begin(ManualLogSource log, int target, bool noApocalypse)
    {
        this.log = log;
        this.target = target;
        this.noApocalypse = noApocalypse;
        resultPath = Path.Combine(Paths.BepInExRootPath, "UltimateAscent.SmokeTest.txt");
        File.WriteAllText(resultPath, $"Ultimate Ascent smoke test, ascent {target}{(noApocalypse ? ", Apocalypse off" : "")}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
        startedAt = Time.realtimeSinceStartup;
        Harmony harmony = new Harmony(SmokeTestPlugin.Guid);
        harmony.CreateClassProcessor(typeof(ShakeCounter)).Patch();
        harmony.CreateClassProcessor(typeof(PoisonFxCounter)).Patch();
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

    /// <summary>Collects status additions over time (optionally only matching ones); <see cref="Stop"/> ends it.</summary>
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
            if (added == type && (filter == null || filter(amount)))
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

    private void Update()
    {
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

        bool departed = false;
        Step("boarding pass", () => departed = BoardingPassChecks());
        if (!departed)
        {
            Finish();
            yield break;
        }

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
        if (apocalypse)
        {
            StartCoroutine(WatchSecondWeather(wake));
        }
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
            ("Bing Bong (20)", () => BingBong(wake)),
            ("fog (9)", Fog),
            ("weather (25)", () => Weather(wake)),
            ("bee ambush (17)", BeeAmbush),
            ("bee moments (17)", BeeMoments),
            ("jellyfish (17)", Jellyfish),
            ("second segment (16, 17)", SecondSegment),
            ("Alpine (18, 23)", Alpine),
            ("after the Alps (18)", AfterAlps),
            ("fall damage (12)", FallDrowsiness),
            ("rising lava (9)", RisingHazard),
            ("bees knock a scout out (17, 24)", BeeKnockout),
        };
        foreach ((string name, Func<IEnumerator> run) in steps)
        {
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

    /// <summary>With TFA's Apocalypse mode, ascent 25 adds a second Apocalypse weather to each of TFA's.</summary>
    private IEnumerator WatchSecondWeather(float wake)
    {
        while (!finished && Time.time < wake + 120f && secondWeatherSeen == null)
        {
            string second = Compat.Tfa.CurrentSecondary;
            if (!string.IsNullOrEmpty(second))
            {
                secondWeatherSeen = $"\"{second}\" next to TFA's \"{Net.GetRoom("APOW") as string}\" after {Time.time - wake:F0} s";
            }
            yield return new WaitForSeconds(0.5f);
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
        float end = Time.time + 30f;
        while (secondWeatherSeen == null && Time.time < end && !ScoutLost)
        {
            yield return null;
        }
        Check("a second Apocalypse weather joins TFA's (25)", secondWeatherSeen != null, secondWeatherSeen ?? "none within 66 s of waking up");
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
            string reward = entry.localizedReward;
            if (string.IsNullOrEmpty(title) || !title.Contains(level.ToString()))
            {
                problems.Add($"{level}: title '{title}'");
            }
            if (string.IsNullOrEmpty(description) || description.IndexOf("ULTIMATEASCENT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                problems.Add($"{level}: description '{description}'");
            }
            if (string.IsNullOrEmpty(reward) || reward.IndexOf("ULTIMATEASCENT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                problems.Add($"{level}: reward '{reward}'");
            }
            foreach (string key in new[] { AscentCatalog.TitleKey(level), LocalizedText.GetDescriptionIndex(AscentCatalog.TitleKey(level)), AscentCatalog.RewardKey(level) })
            {
                if (!LocalizedText.mainTable.TryGetValue(key.ToUpperInvariant(), out List<string> texts) || texts.Count < languages || texts.Any(string.IsNullOrEmpty))
                {
                    problems.Add($"{level}: '{key}' is missing in some languages");
                }
            }
        }
        Check("every ascent has a title, description and reward in every language", problems.Count == 0,
            problems.Count == 0 ? $"{Lvl.Count} ascents, {languages} languages" : string.Join("; ", problems.Take(8)));
        Info("current language", LocalizedText.CURRENT_LANGUAGE.ToString());
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
            Check("left of Tenderfoot is the highest ascent", pass.ascentIndex == Progress.MaxSelectable, $"index {pass.ascentIndex}");
        }
        pass.ascentIndex = target;
        pass.UpdateAscent();
        Check("the start button can be used", pass.startGameButton.interactable);
        if (noApocalypse)
        {
            Step("switch TFA's Apocalypse off", ApocalypseOff);
        }
        pass.StartGame();
        Info("departure", $"ascent {target} started from the boarding pass");
        return true;
    }

    /// <summary>TFA's Apocalypse mode replaces the vanilla weather; switched off in memory to test the storms of ascent 25.</summary>
    private void ApocalypseOff()
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
        apocalypseRestore.Invoke(null, new object[] { false });
        apocalypseChanged = true;
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "APO", false } });
        }
        Info("TFA's Apocalypse mode is off for this test", $"it was {(savedApocalypse ? "on" : "off")}; nothing is saved");
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
    /// one keeps the sun's heat off and smokes while it roasts (45 s), then is fully burned in one go
    /// and stays in the hand.
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
        float expected = seconds / 45f;
        Check("an open parasol in the sun roasts in 45 s (23)", Mathf.Abs(progress - expected) < expected * 0.15f, $"{progress:0.000} roasted in {seconds:F1} s (expected {expected:0.000})");
        Check("the open parasol keeps the sun's heat off (23)", sunHeat.Total < 0.001f, $"{sunHeat.Total:0.0000} heat in {seconds:F1} s");
        if (!A23_HotSun.exposures.TryGetValue(item, out A23_HotSun.Exposure exposure))
        {
            yield break;
        }
        ParticleSystem smoke = item.particles != null ? item.particles.smoke : null;
        Check("the roasting parasol smokes, still uncooked (23)", exposure.smoking && smoke != null && smoke.isEmitting && BurnedItems.CookedLevel(item) == 0,
            $"smoking {exposure.smoking}, smoke {(smoke == null ? "none" : smoke.isEmitting ? "rising" : "off")}, cooked {BurnedItems.CookedLevel(item)}");
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
        MapHandler.JumpToSegment(Segment.Tropics);
        yield return new WaitForSeconds(4f);
        Info("jumped to the second segment", $"place {Map.CurrentPlace}, biome {Map.CurrentBiome}");
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
        Check("one cooking cycle at a campfire burns the item (16)", burned, detail);
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
        // A bite lands 10 poison (ascent 15's x1.75 included), every 0.5 s (the game: 5 every 29 s), and
        // the tick keeps biting (the game stops after 10 bites once your poison is 50).
        float perBite = poison.Count > 0 ? poison.Total / poison.Count : 0f;
        Check("a tick bite is 10 poison (17)", poison.Count >= 3 && Mathf.Abs(perBite - 0.1f) < 0.0005f,
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
        // A normal Bing Bong held without a break, past both timers (10 s: the world shakes, 15 s: crab).
        ShakeCounter.Count = 0;
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
        Check("holding a normal Bing Bong keeps the evil away for good (20)", longest < 0.5f && ShakeCounter.Count == 0 && heldCrab.Total <= 0f,
            $"held for {heldCrab.Seconds:F1} s: timer at most {longest:F1} s, {ShakeCounter.Count} shake(s), {heldCrab.Total:0.000} crab");

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
        Shield(true);
        Meter burnedCrab = new Meter(Status.Crab);
        Meter burnedHeat = new Meter(Status.Hot, amount => Mathf.Abs(amount - BurnTickHeat) < 0.0005f);
        try
        {
            yield return new WaitForSeconds(17.5f);
        }
        finally
        {
            burnedCrab.Stop();
            burnedHeat.Stop();
            Protect(true);
        }
        float neglected = A20_BingBong.neglectedFor;
        Check("holding a burned Bing Bong, the world shakes after 10 s (20)", ShakeCounter.Count > 0, $"{ShakeCounter.Count} shake(s), neglected for {neglected:F1} s");
        Check("holding a burned Bing Bong, crab status comes after 15 s (20)", burnedCrab.Total > 0.05f, $"{burnedCrab.Total:0.000} crab, neglected for {neglected:F1} s");
        float lastBurn = burnedHeat.LastAt - burnTime;
        Check("a burned Bing Bong burns the hand for 10 s, then cools down (17, 20)", burnedHeat.Count >= 15 && lastBurn <= BurnedItems.HotSeconds + 0.6f,
            $"{burnedHeat.Count} burn ticks, the last {lastBurn:F1} s after it got burned, none in the {burnedHeat.Seconds - lastBurn:F1} s after");
        Check("the burned Bing Bong stays burned in the hand (20)", doll != null && BurnedItems.IsBurned(doll) && Local.data.currentItem == doll,
            doll == null ? "Bing Bong is gone" : $"cooked {BurnedItems.CookedLevel(doll)}, in the hand {Local.data.currentItem == doll}");
        // Hands empty again: a burned item in the hand would heat the scout during the next checks.
        Local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.5f);
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
        Check("the ambush swarm stays angry for 35 s and follows up to 40 m (17)", swarm.beehiveDangerTick > 33f && Mathf.Abs(swarm.maxHiveDistance - 40f) < 0.01f,
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
        Check("bees sting within 1.5 m (17)", stinger != null && Mathf.Abs(stinger.radius - 1.5f) < 0.01f, stinger == null ? "no stinger field" : $"radius {stinger.radius:0.0#} m (the game: 1.3 m)");
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
    /// <summary>A real fall: the scout is put 30 m up and drops; the fall damage makes it 80 drowsy.</summary>
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
        Check("fall damage makes the scout 80 drowsy (12)", injury.Total > 0f && Mathf.Abs(drowsy.Total - 0.8f) < 0.005f,
            $"{injury.Total * 100f:F1} fall damage, {drowsy.Total * 100f:F1} drowsiness");
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
