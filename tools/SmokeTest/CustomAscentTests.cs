using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using TMPro;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using UnityEngine.EventSystems;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the Custom Run screen's Ascent setting: its row in the airport, and a custom run on a picked ascent.</summary>
internal sealed partial class Runner
{
    /// <summary>With "-Only customascent" the climb is a custom run on the tested ascent, its gameplay settings and crabs set against it (in memory).</summary>
    private bool customClimb;

    /// <summary>Puts back the run settings the custom-run checks changed (in memory, then the game's saved copy of them).</summary>
    private Action customAscentRestore;

    /// <summary>The Custom Run screen's gameplay settings, and each at the other end from what the ascents do.</summary>
    private static readonly (RunSettings.SETTINGTYPE setting, int against)[] Gameplay =
    {
        (RunSettings.SETTINGTYPE.Fog, 0),
        (RunSettings.SETTINGTYPE.HungerRate, 0),
        (RunSettings.SETTINGTYPE.FallDamage, 0),
        (RunSettings.SETTINGTYPE.EtcDamage, 0),
        (RunSettings.SETTINGTYPE.ColdNight, 1),
        (RunSettings.SETTINGTYPE.ItemWeight, 0),
        (RunSettings.SETTINGTYPE.FlaresAtPeak, 1),
        (RunSettings.SETTINGTYPE.ClimbingStaminaUsage, 0),
    };

    private static bool CustomClimbAsked(IEnumerable<string> words) => words != null && words.Any(w => w.Equals("customascent", StringComparison.OrdinalIgnoreCase));

    /// <summary>With "-Only minirun" the climb is a custom Mini Run in the Tropics on the tested ascent (in memory).</summary>
    private bool miniRunClimb;

    private static bool MiniRunAsked(IEnumerable<string> words) => words != null && words.Any(w => w.Equals("minirun", StringComparison.OrdinalIgnoreCase));

    private static bool IsBingBong(Item item) => item != null && (item.itemTags & Item.ItemTags.BingBong) != 0;

    /// <summary>
    /// "-Only minirun": the boarding pass on a custom run that picks the tested ascent, a Mini Run in the Tropics (past the
    /// beach, where the game's Bing Bong lies); set in memory, put back afterwards.
    /// </summary>
    private IEnumerator MiniRunSetup()
    {
        BoardingPass pass = GUIManager.instance.boardingPass;
        bool wasCustom = RunSettings.IsCustomRun;
        int savedPick = RunSettings.GetValue(CustomRunAscent.Setting, forceGetCustomValue: true);
        int savedMiniRun = RunSettings.GetValue(RunSettings.SETTINGTYPE.MiniRun, forceGetCustomValue: true);
        int savedBiome = RunSettings.GetValue(RunSettings.SETTINGTYPE.MiniRunBiome, forceGetCustomValue: true);
        Action earlier = customAscentRestore;
        customAscentRestore = () =>
        {
            earlier?.Invoke();
            RunSettings.SetValue(CustomRunAscent.Setting, savedPick);
            RunSettings.SetValue(RunSettings.SETTINGTYPE.MiniRun, savedMiniRun);
            RunSettings.SetValue(RunSettings.SETTINGTYPE.MiniRunBiome, savedBiome);
        };
        Step($"a custom Mini Run in the Tropics on ascent {target} (in memory)", () =>
        {
            if (!pass.isOpen)
            {
                pass.Open();
            }
            if (!RunSettings.IsCustomRun)
            {
                pass.customRunButton.onClick.Invoke();
            }
            RunSettings.SetValue(CustomRunAscent.Setting, CustomRunAscent.ValueOf(target));
            RunSettings.SetValue(RunSettings.SETTINGTYPE.MiniRun, 1);
            RunSettings.SetValue(RunSettings.SETTINGTYPE.MiniRunBiome, 1);
            pass.UpdateAscent();
        });
        yield return new WaitForSecondsRealtime(1f);
        miniRunClimb = RunSettings.IsCustomRun;
        Info("the custom run for the climb", $"custom {RunSettings.IsCustomRun} (was {wasCustom}), ascent {CustomRunAscent.Chosen}, mini run {RunSettings.GetValue(RunSettings.SETTINGTYPE.MiniRun, forceGetCustomValue: true)}, "
            + $"biome {RunSettings.GetValue(RunSettings.SETTINGTYPE.MiniRunBiome, forceGetCustomValue: true)}; before: ascent {savedPick}, mini run {savedMiniRun}, biome {savedBiome}");
    }

    /// <summary>
    /// The custom Mini Run in the Tropics on the tested ascent (20 or higher): the host is handed a Bing Bong once they are
    /// up, holds it, only one is about, and holding it keeps his timer at zero.
    /// </summary>
    private IEnumerator MiniRunBingBongCheck()
    {
        if (!miniRunClimb)
        {
            Info("the Mini Run's Bing Bong check is skipped", "run with -Only minirun");
            yield break;
        }
        Character local = Local;
        float from = Time.time;
        for (float until = Time.time + 20f; Time.time < until && !IsBingBong(local.data.currentItem);)
        {
            yield return null;
        }
        Item held = local.data.currentItem;
        float after = Time.time - from;
        List<Item> all = FindObjectsByType<Item>(FindObjectsSortMode.None).Where(IsBingBong).ToList();
        int near = all.Count(i => Vector3.Distance(i.transform.position, local.Center) < 100f);
        Segment segment = Singleton<MapHandler>.Instance.GetCurrentSegment();
        Check($"in a custom Mini Run past the beach on ascent {target}, the host is handed a Bing Bong to hold (20)",
            RunSettings.IsCustomRun && RunSettings.isMiniRun && segment == Segment.Tropics && Rules.Enabled(Lvl.BingBong) && A20_BingBong.HandedOver && IsBingBong(held) && near == 1,
            $"custom {RunSettings.IsCustomRun}, mini run {RunSettings.isMiniRun}, in {segment}; Bing Bong's rule {Rules.Enabled(Lvl.BingBong)}; handed over {A20_BingBong.HandedOver}; "
            + $"holding '{(held != null ? held.GetItemName() : "nothing")}' {after:0.0} s into the check; Bing Bongs about: {string.Join(", ", all.Select(i => $"{Vector3.Distance(i.transform.position, local.Center):0} m away"))}");
        float longest = 0f;
        for (float until = Time.time + 5f; Time.time < until;)
        {
            longest = Mathf.Max(longest, A20_BingBong.neglectedFor);
            yield return null;
        }
        Check("held, Bing Bong's timer stays at zero (20)", IsBingBong(local.data.currentItem) && longest == 0f, $"still holding him {IsBingBong(local.data.currentItem)}; the timer's highest in 5 s: {longest:0.0} s");
        Screenshot("minirun-bingbong");
        yield return new WaitForSeconds(1f);
    }

    /// <summary>
    /// The Custom Run screen, opened as a player does (the pass's custom-run button, then its Custom Options button): the
    /// Ascent setting right under the climbing stamina's, in the gameplay panel, named, off by default. Clicked with the
    /// mouse: on from Tenderfoot, a right click back, from off back to the highest ascent, round to off again; up to the
    /// tested ascent, the other gameplay settings faded. Pictured, then the boarding pass naming it. With "-Only
    /// customascent" the custom run is kept for the climb, a full expedition (no mini run) with its gameplay settings
    /// set against the ascent and its crabs and eruptions off (in memory); otherwise everything is put back.
    /// </summary>
    private IEnumerator CustomRunAscentScreen()
    {
        BoardingPass pass = GUIManager.instance != null ? GUIManager.instance.boardingPass : null;
        AirportCheckInKiosk kiosk = UnityEngine.Object.FindAnyObjectByType<AirportCheckInKiosk>();
        CustomOptionsWindow window = pass != null ? pass.customOptionsWindow : null;
        if (kiosk == null || window == null)
        {
            Fail("the Custom Run screen can be opened", $"kiosk {kiosk != null}, screen {window != null}");
            yield break;
        }
        bool wasCustom = RunSettings.IsCustomRun;
        int savedPick = RunSettings.GetValue(CustomRunAscent.Setting, forceGetCustomValue: true);
        int[] savedGameplay = Gameplay.Select(g => RunSettings.GetValue(g.setting, forceGetCustomValue: true)).ToArray();
        int savedCrabs = RunSettings.GetValue(NewMobs.CrabsThreat, forceGetCustomValue: true);
        int savedMiniRun = RunSettings.GetValue(RunSettings.SETTINGTYPE.MiniRun, forceGetCustomValue: true);
        int savedEruptions = RunSettings.GetValue(UltimateAscent.Features.CustomRunThreats.Eruptions, forceGetCustomValue: true);
        customAscentRestore = () =>
        {
            RunSettings.SetValue(RunSettings.SETTINGTYPE.MiniRun, savedMiniRun);
            RunSettings.SetValue(UltimateAscent.Features.CustomRunThreats.Eruptions, savedEruptions);
            for (int i = 0; i < Gameplay.Length; i++)
            {
                RunSettings.SetValue(Gameplay[i].setting, savedGameplay[i]);
            }
            RunSettings.SetValue(NewMobs.CrabsThreat, savedCrabs);
            RunSettings.SetValue(CustomRunAscent.Setting, savedPick);
            // The clicks saved the pick with the game's other settings: the saved copy back as it was.
            RunSettings.SaveToDisk();
        };
        Info("the custom run's settings before the test", $"ascent {savedPick}, gameplay {string.Join(", ", Gameplay.Select((g, i) => $"{g.setting} {savedGameplay[i]}"))}, crabs {savedCrabs}, eruptions {savedEruptions}, mini run {savedMiniRun}");

        Step("the boarding pass on a custom run", () =>
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
        });
        yield return new WaitForSecondsRealtime(1.5f);
        Step("open the Custom Run screen", () => pass.customOptionsButton.onClick.Invoke());
        yield return new WaitForSecondsRealtime(1.5f);

        AscentOption row = window.GetComponentInChildren<AscentOption>(true);
        List<CustomOptionMulti> multis = window.GetComponentsInChildren<CustomOptionMulti>(true).ToList();
        CustomOptionMulti climbing = multis.FirstOrDefault(m => m.settingType == RunSettings.SETTINGTYPE.ClimbingStaminaUsage);
        string RowText(Component option) => string.Join("/", option.GetComponentsInChildren<TMP_Text>(true).Select(t => Plain(t.text)));
        Info("the Custom Run screen's settings", string.Join("; ", window.GetComponentsInChildren<CustomOptionBase>(true)
            .Where(o => !(o is CustomOptionItemToggle))
            .Select(o => $"{(o is CustomOptionMulti m ? m.settingType.ToString() : o is CustomOptionToggle t ? t.settingType.ToString() : o.GetType().Name)} '{RowText(o)}' in {o.transform.parent.name} #{o.transform.GetSiblingIndex()} at {((RectTransform)o.transform).anchoredPosition}")));
        if (row == null || climbing == null)
        {
            Fail("the Custom Run screen has the Ascent setting", $"row {row != null}, climbing stamina's row {climbing != null}");
            yield return CloseCustomScreen(pass, window, wasCustom);
            yield break;
        }
        RectTransform rowRect = (RectTransform)row.transform;
        RectTransform climbingRect = (RectTransform)climbing.transform;
        RectTransform list = (RectTransform)row.transform.parent;
        RectTransform panel = list.parent as RectTransform;
        Info("the gameplay panel", $"list '{list.name}' ({string.Join(", ", list.GetComponents<Component>().Select(c => c.GetType().Name))}) {list.rect.size}; panel '{(panel != null ? panel.name : "")}' {(panel != null ? panel.rect.size : Vector2.zero)}");
        Vector3[] rowCorners = Corners(rowRect);
        Vector3[] climbingCorners = Corners(climbingRect);
        Vector3 rowMiddle = (rowCorners[0] + rowCorners[2]) * 0.5f;
        bool under = rowMiddle.y < climbingCorners[0].y + 0.01f && Mathf.Abs(rowCorners[0].x - climbingCorners[0].x) < 0.5f && !Overlap(rowRect, climbingRect);
        bool inPanel = panel != null && Inside(panel, rowMiddle) && Inside(panel, rowCorners[0]) && Inside(panel, rowCorners[2]);
        string label = row.GetComponentsInChildren<TMP_Text>(true).Select(t => Plain(t.text))
            .FirstOrDefault(t => t.Equals("Ascent", StringComparison.OrdinalIgnoreCase) || t.Equals("Восхождение", StringComparison.OrdinalIgnoreCase)) ?? RowText(row);
        string offText = Plain(LocalizedText.GetText(multis.First(m => m.settingType == RunSettings.SETTINGTYPE.Fog).labels[0]));
        Check("the Custom Run screen has the Ascent setting right under the climbing stamina's, in the gameplay panel, named, off by default",
            row.transform.parent == climbing.transform.parent && row.transform.GetSiblingIndex() == climbing.transform.GetSiblingIndex() + 1 && under && inPanel
            && (label.Equals("Ascent", StringComparison.OrdinalIgnoreCase) || label.Equals("Восхождение", StringComparison.OrdinalIgnoreCase)) && RunSettings.GetDefaultValue(CustomRunAscent.Setting) == 0,
            $"#{row.transform.GetSiblingIndex()} after the climbing stamina's #{climbing.transform.GetSiblingIndex()}; under it {under} (row's middle {rowMiddle}, the climbing row's bottom {climbingCorners[0].y}); inside the panel {inPanel}; "
            + $"named '{label}'; default {RunSettings.GetDefaultValue(CustomRunAscent.Setting)}; now '{Plain(row.Shown)}' ({CustomRunAscent.Chosen})");

        // Clicked with the mouse, the way a player does.
        RectTransform button = (RectTransform)row.Button.transform;
        for (int i = 0; i <= CustomRunAscent.Most && CustomRunAscent.Chosen != 0; i++)
        {
            ClickOn(button);
            yield return null;
        }
        string offShown = Plain(row.Shown);
        float OthersAlpha() => list.Cast<Transform>().Where(t => t != row.transform && t.GetComponent<CustomOptionBase>() != null)
            .Select(t => t.GetComponent<CanvasGroup>() != null ? t.GetComponent<CanvasGroup>().alpha : 1f).DefaultIfEmpty(1f).Max();
        float alphaOff = OthersAlpha();
        ClickOn(button);
        yield return null;
        int first = CustomRunAscent.Chosen;
        string firstShown = Plain(row.Shown);
        float alphaOn = list.Cast<Transform>().Where(t => t != row.transform && t.GetComponent<CustomOptionBase>() != null)
            .Select(t => t.GetComponent<CanvasGroup>() != null ? t.GetComponent<CanvasGroup>().alpha : 1f).DefaultIfEmpty(1f).Min();
        ClickOn(button, PointerEventData.InputButton.Right);
        yield return null;
        int back = CustomRunAscent.Chosen;
        string backShown = Plain(row.Shown);
        ClickOn(button, PointerEventData.InputButton.Right);
        yield return null;
        int highest = CustomRunAscent.Chosen;
        string highestShown = Plain(row.Shown);
        ClickOn(button);
        yield return null;
        int round = CustomRunAscent.Chosen;
        Check("a click picks Tenderfoot, a right click goes back to off, another to the highest ascent, a click round to off; picked, the other gameplay settings fade",
            offShown == offText && first == 1 && firstShown == Plain(CustomRunAscent.Title(-1)) && back == 0 && backShown == offText
            && highest == CustomRunAscent.ValueOf(CustomRunAscent.Highest) && highestShown == Plain(CustomRunAscent.Title(CustomRunAscent.Highest)) && round == 0
            && alphaOff > 0.99f && alphaOn < 0.6f,
            $"off '{offShown}' (the game's '{offText}'); click: {first} '{firstShown}'; right click: {back} '{backShown}'; again: {highest} '{highestShown}' (highest {CustomRunAscent.Highest}); click: {round}; "
            + $"the other settings' alpha off {alphaOff:0.00}, on {alphaOn:0.00}");

        for (int i = 0; i <= CustomRunAscent.Most && CustomRunAscent.Chosen != CustomRunAscent.ValueOf(target); i++)
        {
            ClickOn(button);
            yield return null;
        }
        Check($"clicked up to ascent {target}, the button names it", CustomRunAscent.Chosen == CustomRunAscent.ValueOf(target) && Plain(row.Shown) == Plain(CustomRunAscent.Title(target)),
            $"{CustomRunAscent.Chosen} '{Plain(row.Shown)}'");
        yield return new WaitForSecondsRealtime(0.5f);
        Screenshot("custom-run-ascent");
        yield return new WaitForSecondsRealtime(1f);

        Step("close the Custom Run screen", () => window.closeButtonCustom.onClick.Invoke());
        yield return new WaitForSecondsRealtime(1.5f);
        string description = Plain(pass.ascentDesc.text);
        Check($"the custom run's boarding pass names ascent {target}; it starts there", pass.isOpen && RunSettings.IsCustomRun && description.EndsWith(Plain(CustomRunAscent.Title(target)))
            && CustomRunAscent.OnPass(pass) == target && ChallengeModes.SunByAscent(CustomRunAscent.OnPass(pass)) == (target >= Lvl.HotSun),
            $"open {pass.isOpen}, custom {RunSettings.IsCustomRun}; '{description}'; starts on {CustomRunAscent.OnPass(pass)}");
        Screenshot("boarding-pass-custom-ascent");
        yield return new WaitForSecondsRealtime(1f);

        customClimb = CustomClimbAsked(only);
        if (customClimb)
        {
            Step("for the climb: a full expedition, the custom run's gameplay settings set against the ascent, its crabs and eruptions off (in memory)", () =>
            {
                foreach ((RunSettings.SETTINGTYPE setting, int against) in Gameplay)
                {
                    RunSettings.SetValue(setting, against);
                }
                RunSettings.SetValue(NewMobs.CrabsThreat, 0);
                RunSettings.SetValue(UltimateAscent.Features.CustomRunThreats.Eruptions, 0);
                RunSettings.SetValue(RunSettings.SETTINGTYPE.MiniRun, 0);
            });
            Info("the custom run's settings for the climb", string.Join(", ", Gameplay.Select(g => $"{g.setting} {RunSettings.GetValue(g.setting, forceGetCustomValue: true)}"))
                + $", crabs {RunSettings.GetValue(NewMobs.CrabsThreat, forceGetCustomValue: true)}, eruptions {RunSettings.GetValue(UltimateAscent.Features.CustomRunThreats.Eruptions, forceGetCustomValue: true)}");
            yield break;
        }
        Step("put the custom run's settings back", () =>
        {
            customAscentRestore();
            customAscentRestore = null;
            if (RunSettings.IsCustomRun != wasCustom)
            {
                pass.customRunButton.onClick.Invoke();
            }
        });
        yield return new WaitForSecondsRealtime(1f);
    }

    private IEnumerator CloseCustomScreen(BoardingPass pass, CustomOptionsWindow window, bool wasCustom)
    {
        window.closeButtonCustom.onClick.Invoke();
        yield return new WaitForSecondsRealtime(1f);
        customAscentRestore?.Invoke();
        customAscentRestore = null;
        if (RunSettings.IsCustomRun != wasCustom)
        {
            pass.customRunButton.onClick.Invoke();
        }
    }

    /// <summary>
    /// The custom run started from the screen on the tested ascent ("-Only customascent"): the climb is on that ascent and
    /// still a custom run; the game reads its gameplay settings (set against the ascent) as the ascent's, the cold nights
    /// as the host's config has them; the ascents' rules apply as in a regular run (the new mobs at full strength from 17);
    /// the crabs switched off stay away while the snakes are allowed; the top right names the ascent under "Custom", the
    /// modifiers line under it. Then, with the pick off for a moment (in memory), the custom run's own settings apply again
    /// and the ascents' rules are off. Last, in the Caldera (a map with the volcano: "-Biome 80"), on its last stretch: with
    /// the eruptions switched off nothing erupts for 20 s, neither the game's eruptions nor ascent 17's; switched on (in
    /// memory) they break out again.
    /// </summary>
    private IEnumerator CustomAscentClimb()
    {
        if (!customClimb)
        {
            Info("the custom run on a picked ascent is skipped", "run with -Only customascent");
            yield break;
        }
        Check($"the custom run started on the ascent picked on its screen ({target})", RunSettings.IsCustomRun && Ascents.currentAscent == target && CustomRunAscent.On && Rules.Level == target,
            $"custom {RunSettings.IsCustomRun}, Ascents.currentAscent {Ascents.currentAscent}, picked {RunSettings.GetValue(CustomRunAscent.Setting)}, on {CustomRunAscent.On}, rules' level {Rules.Level}");

        string Read() => $"fog {Ascents.fogEnabled}, hunger x{Ascents.hungerRateMultiplier:0.##}, fall damage x{Ascents.fallDamageMultiplier:0.##}, other damage x{Ascents.etcDamageMultiplier:0.##}, "
            + $"item weight {Ascents.itemWeightModifier:+0;-0;0}, flares {Ascents.shouldSpawnFlare}, cold nights {Ascents.isNightCold}, climbing stamina x{Ascents.climbStaminaMultiplier:0.##}, revives {Ascents.canReviveDead}";
        bool coldNights = target >= 5 && HostOptions.ColdNightsOn;
        bool byAscent = Ascents.fogEnabled == (target >= 0)
            && Mathf.Approximately(Ascents.hungerRateMultiplier, target >= 2 ? 1.6f : target == -1 ? 0.7f : 1f)
            && Mathf.Approximately(Ascents.fallDamageMultiplier, target >= 1 ? 2f : 1f)
            && Mathf.Approximately(Ascents.etcDamageMultiplier, 1f)
            && Ascents.itemWeightModifier == (target >= 3 ? 1 : 0)
            && Ascents.shouldSpawnFlare == (target < 4)
            && Ascents.isNightCold == coldNights
            && Mathf.Approximately(Ascents.climbStaminaMultiplier, target >= 6 ? 1.4f : target == -1 ? 0.7f : 1f)
            && Ascents.canReviveDead;
        Check("its gameplay settings (set against the ascent) are ignored: the game plays by the ascent, the cold nights as the host's config has them", byAscent,
            Read() + $"; the host's Cold Nights {HostOptions.ColdNightsOn}");
        bool hazards = target >= Lvl.Hazards && HostOptions.AscentOn(Lvl.Hazards);
        Check("the ascents' rules apply as in a regular run on it (the new mobs at full strength from 17)",
            Rules.Enabled(Lvl.Hazards) == hazards && Rules.InExtraAscent == (target >= Lvl.First) && Mathf.Approximately(NewMobs.Strength, hazards ? 1f : 0.5f),
            $"ascent 17's rule {Rules.Enabled(Lvl.Hazards)} (its switch {HostOptions.AscentOn(Lvl.Hazards)}), one of the mod's ascents {Rules.InExtraAscent}, the new mobs' strength {NewMobs.Strength}");

        for (float until = Time.time + 5f; Time.time < until && BeachCrabs().Count == 0;)
        {
            yield return null;
        }
        Check("on the beach, the crabs switched off on the Custom Run screen stay away on this ascent; the snakes, switched on, are allowed",
            Map.CurrentPlace == Place.Shore && !Crabs.On && BeachCrabs().Count == 0 && Crabs.Spawned == 0 && Snakes.On,
            $"here {Map.CurrentPlace}; crabs allowed {Crabs.On}, crabs about {BeachCrabs().Count} (spawned {Crabs.Spawned}); snakes allowed {Snakes.On}");

        AscentUI ui = UnityEngine.Object.FindAnyObjectByType<AscentUI>();
        string shown = ui != null && ui.text != null ? ui.text.text : "";
        string custom = SingletonAsset<AscentData>.Instance.ascents[0].localizedTitle;
        ModifiersLine line = ui != null && ui.text != null ? ui.text.GetComponent<ModifiersLine>() : null;
        string modifiers = line != null ? line.Shown : "";
        Check("the ascent at the top right names the picked ascent under the custom run's, the modifiers line under them",
            shown == custom + "\n" + CustomRunAscent.Title(target) && (target < 5 || modifiers.Length > 0),
            $"'{Plain(shown)}' (custom '{custom}', ascent '{CustomRunAscent.Title(target)}'); modifiers line '{modifiers}'");
        Screenshot("custom-ascent-climb");
        yield return new WaitForSeconds(1f);

        int picked = RunSettings.GetValue(CustomRunAscent.Setting);
        RunSettings.SetValue(CustomRunAscent.Setting, 0);
        yield return null;
        bool bySettings = !Ascents.fogEnabled && Mathf.Approximately(Ascents.hungerRateMultiplier, 0f) && Mathf.Approximately(Ascents.etcDamageMultiplier, 0f)
            && Ascents.itemWeightModifier == -1 && Ascents.shouldSpawnFlare && Ascents.isNightCold && Mathf.Approximately(Ascents.climbStaminaMultiplier, 0f);
        string offRead = Read();
        bool rulesOff = !CustomRunAscent.On && !Rules.Enabled(Lvl.Hazards) && !Rules.InExtraAscent && Rules.Level == 0 && ModifiersLabel.Text().Length == 0 && Mathf.Approximately(NewMobs.Strength, 0.5f);
        string offRules = $"ascent 17's rule {Rules.Enabled(Lvl.Hazards)}, the mod's ascents {Rules.InExtraAscent}, level {Rules.Level}, modifiers line '{ModifiersLabel.Text()}', the new mobs' strength {NewMobs.Strength}";
        RunSettings.SetValue(CustomRunAscent.Setting, picked);
        yield return null;
        Check("with the pick off (for a moment, in memory) the same custom run plays by its own gameplay settings again, the ascents' rules off (fall damage stays the climb's ascent's)",
            bySettings && rulesOff && CustomRunAscent.On,
            $"{offRead}; {offRules}; picked again: {CustomRunAscent.On}");

        if (!Map.InVolcanoCaldera)
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(8f);
        }
        if (!Map.InVolcanoCaldera)
        {
            Info("the eruptions' switch check is skipped", $"this map has no Caldera (place {Map.CurrentPlace}); run it with -Biome 80");
            yield break;
        }
        Character local = Local;
        if (CalderaPathEruptions.Build() && CalderaPathEruptions.Ground.Count > 0)
        {
            // On the last stretch, halfway, where ascent 17 has it erupt every 2-6 s and the game's own eruptions reach too.
            Vector3 middle = Vector3.Lerp(CalderaPathEruptions.Statue, CalderaPathEruptions.Campfire, 0.5f);
            Vector3 standAt = CalderaPathEruptions.Ground.OrderBy(g => Flat(g, middle)).First();
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standAt + Vector3.up * 1.5f, false);
        }
        yield return new WaitForSeconds(2f);
        int before = EruptionsSeen.All.Count;
        int sentBefore = CalderaPathEruptions.Spawned;
        yield return new WaitForSeconds(20f);
        int whileOff = EruptionsSeen.All.Count - before;
        int sentOff = CalderaPathEruptions.Spawned - sentBefore;
        bool onStretch = CalderaPathEruptions.OnTheStretch(local.Center);
        RunSettings.SetValue(UltimateAscent.Features.CustomRunThreats.Eruptions, 1);
        int onFrom = EruptionsSeen.All.Count;
        float switchedOn = Time.time;
        for (float until = Time.time + 20f; Time.time < until && EruptionsSeen.All.Count == onFrom;)
        {
            yield return null;
        }
        int whileOn = EruptionsSeen.All.Count - onFrom;
        float first = whileOn > 0 ? EruptionsSeen.All[onFrom].at - switchedOn : float.NaN;
        if (whileOn > 0)
        {
            yield return new WaitForSeconds(1.5f);
            Screenshot("custom-ascent-eruptions-on");
            yield return new WaitForSeconds(1f);
        }
        RunSettings.SetValue(UltimateAscent.Features.CustomRunThreats.Eruptions, 0);
        Check("in the Caldera, with the Custom Run screen's eruptions switched off nothing erupts on this ascent (neither the game's eruptions nor ascent 17's on the last stretch); switched on, they break out",
            onStretch && whileOff == 0 && sentOff == 0 && whileOn > 0,
            $"the scout on the last stretch {onStretch} at {local.Center}; switched off: {whileOff} eruptions in 20 s (ascent 17's sent {sentOff}); switched on: the first {first:0.0} s later");
    }
}
