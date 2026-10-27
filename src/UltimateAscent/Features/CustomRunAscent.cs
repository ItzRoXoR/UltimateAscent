using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// The Custom Run screen's "Ascent" setting, the last of its gameplay settings: off (the default), or an ascent from
/// Tenderfoot up to the highest one unlocked. With an ascent picked, a custom run plays by it: the gameplay settings
/// above it on the screen (the fog, hunger, damage, cold nights, item weight, flares and climbing stamina) are set aside
/// and the ascent's own modifiers apply instead, the game's and this mod's, with the host's config switches; the
/// screen's threats and items still decide what is there (a threat switched off stays away on any ascent). It is a run
/// setting of the game's own kind (saved, synced and reset with the others). The climb starts on the picked ascent
/// (<see cref="Ascents.currentAscent"/>, so everything that asks the game's ascent follows it) and stays a custom run,
/// which counts for no unlock, badge or Steam stat. The boarding pass and the ascent shown during the climb name it.
/// </summary>
internal static class CustomRunAscent
{
    /// <summary>A run setting under a number the game's setting type does not use (after the new mobs' threats).</summary>
    internal static readonly RunSettings.SETTINGTYPE Setting = (RunSettings.SETTINGTYPE)91100;

    /// <summary>The setting's name, as the game keys it (an unnamed setting type is written as its number).</summary>
    private static readonly string Key = ((int)Setting).ToString();

    internal const string LabelKey = "UA_CUSTOM_ASCENT";

    /// <summary>The screen's row, by this name.</summary>
    internal const string RowName = "Option_UltimateAscentAscent";

    /// <summary>
    /// The setting's values: 0 is off, any other the ascent two below it, as the game lists its ascents (1 Tenderfoot,
    /// 2 Peak, 3 ascent 1, ... 27 ascent 25), so a value is also the ascent's place in <see cref="AscentData"/>.
    /// </summary>
    internal const int Most = Lvl.Last + 2;

    internal static int AscentOf(int value) => value - 2;

    internal static int ValueOf(int ascent) => ascent + 2;

    /// <summary>The highest ascent that can be picked: the boarding pass's highest.</summary>
    internal static int Highest => Mathf.Clamp(Progress.MaxSelectable, -1, Lvl.Last);

    /// <summary>The setting on the Custom Run screen (in the airport), within what can be picked now (0 off).</summary>
    internal static int Chosen
    {
        get
        {
            if (GameUtils.instance == null)
            {
                return 0;
            }
            return Mathf.Clamp(RunSettings.GetValue(Key, forceGetCustomValue: true), 0, ValueOf(Highest));
        }
    }

    /// <summary>The ascent the boarding pass starts: its own, or in a custom run the one picked (int.MinValue when none is).</summary>
    internal static int OnPass(BoardingPass pass)
    {
        if (!RunSettings.IsCustomRun)
        {
            return pass.ascentIndex;
        }
        int chosen = Chosen;
        return chosen > 0 ? AscentOf(chosen) : int.MinValue;
    }

    /// <summary>This climb is a custom run on a picked ascent (in the airport the game reads its settings as defaults: off).</summary>
    private static bool Picked => RunSettings.IsCustomRun && GameUtils.instance != null && RunSettings.GetValue(Key) > 0;

    /// <summary>A custom run on a picked ascent is being climbed.</summary>
    internal static bool On => Rules.InRun && Picked;

    /// <summary>An ascent's name, as the game shows it.</summary>
    internal static string Title(int ascent)
    {
        List<AscentData.AscentInstanceData> ascents = SingletonAsset<AscentData>.Instance?.ascents;
        int index = ValueOf(ascent);
        return ascents != null && index >= 0 && index < ascents.Count && ascents[index] != null ? ascents[index].localizedTitle : "";
    }

    /// <summary>Its run setting, set up with the game's own (before it orders, loads and syncs them).</summary>
    [HarmonyPatch(typeof(RunSettings), nameof(RunSettings.InitializeDefaultValues))]
    private static class SettingAdded
    {
        private static void Postfix()
        {
            RunSettings.AddDefaultSetting(Setting, 0, Most);
        }
    }

    /// <summary>The climb starts on the picked ascent (the game starts every custom run on the Peak's).</summary>
    [HarmonyPatch(typeof(AirportCheckInKiosk), nameof(AirportCheckInKiosk.StartGame))]
    private static class StartsOnIt
    {
        private static void Prefix(ref int ascent)
        {
            if (!RunSettings.IsCustomRun)
            {
                return;
            }
            int chosen = Chosen;
            if (chosen > 0)
            {
                ascent = AscentOf(chosen);
                Plugin.Log.LogInfo($"Custom run on ascent {ascent}: its modifiers replace the gameplay settings.");
            }
        }
    }

    /// <summary>
    /// The game's gameplay settings as the game works them out (its <see cref="Ascents"/> rules): in a custom run on a
    /// picked ascent, as in a regular run on it. The custom run is set aside only while the game reads them.
    /// </summary>
    [HarmonyPatch]
    private static class GameplayByAscent
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.fogEnabled));
            yield return AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.fallDamageMultiplier));
            yield return AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.etcDamageMultiplier));
            yield return AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.hungerRateMultiplier));
            yield return AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.itemWeightModifier));
            yield return AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.shouldSpawnFlare));
            yield return AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.isNightCold));
            yield return AccessTools.PropertyGetter(typeof(Ascents), nameof(Ascents.canReviveDead));
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(out bool __state)
        {
            __state = On;
            if (__state)
            {
                RunSettings._isCustomRun = false;
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                RunSettings._isCustomRun = true;
            }
            return __exception;
        }
    }

    /// <summary>Climbing stamina the same way; Grapple Mode (a mode of the screen, not a gameplay setting) still multiplies it.</summary>
    [HarmonyPatch(typeof(Ascents), "get_" + nameof(Ascents.climbStaminaMultiplier))]
    private static class ClimbingByAscent
    {
        /// <summary>0: as the game reads it; 1: by the ascent; 2: by the ascent, in Grapple Mode.</summary>
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out int __state)
        {
            __state = !On ? 0 : RunSettings.GetValue(RunSettings.SETTINGTYPE.GrappleMode) == 1 ? 2 : 1;
            if (__state > 0)
            {
                RunSettings._isCustomRun = false;
            }
        }

        private static void Postfix(ref float __result, int __state)
        {
            if (__state == 2)
            {
                __result *= Ascents.STAMINA_MULTIPLIER_GRAPPLEMODE;
            }
        }

        private static Exception Finalizer(Exception __exception, int __state)
        {
            if (__state > 0)
            {
                RunSettings._isCustomRun = true;
            }
            return __exception;
        }
    }

    /// <summary>On Tenderfoot no Scoutmaster comes, as in a regular run (with his threat switched off he never does).</summary>
    [HarmonyPatch(typeof(Scoutmaster), "get_" + nameof(Scoutmaster.preventSpawning))]
    private static class NoScoutmasterOnTenderfoot
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && On && Ascents.currentAscent < 0)
            {
                __result = true;
            }
        }
    }

    /// <summary>The boarding pass of a custom run names the picked ascent, in the small line the game ends an ascent's text with.</summary>
    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.UpdateAscent))]
    private static class NamedOnThePass
    {
        private static void Postfix(BoardingPass __instance)
        {
            int chosen = RunSettings.IsCustomRun ? Chosen : 0;
            if (chosen > 0 && __instance.ascentDesc != null)
            {
                __instance.ascentDesc.text += "\n\n<alpha=#CC><size=70%>" + Title(AscentOf(chosen));
            }
        }
    }

    /// <summary>During the climb the ascent at the top right ("Custom") has the picked ascent's name under it.</summary>
    [HarmonyPatch(typeof(AscentUI), "Start")]
    private static class NamedInTheClimb
    {
        private static void Postfix(AscentUI __instance)
        {
            if (Picked && __instance.text != null)
            {
                __instance.text.text += "\n" + Title(Ascents._currentAscent);
            }
        }
    }

    /// <summary>
    /// The row on the Custom Run screen: the climbing stamina's row copied, right under it, with this setting, its own
    /// name and the ascents' names on its button.
    /// </summary>
    [HarmonyPatch(typeof(CustomOptionsWindow), nameof(CustomOptionsWindow.Initialize))]
    private static class Row
    {
        private static void Postfix(CustomOptionsWindow __instance)
        {
            if (__instance.GetComponentInChildren<AscentOption>(true) != null)
            {
                return;
            }
            CustomOptionMulti[] rows = __instance.GetComponentsInChildren<CustomOptionMulti>(true);
            CustomOptionMulti climbing = rows.FirstOrDefault(r => r.settingType == RunSettings.SETTINGTYPE.ClimbingStaminaUsage);
            if (climbing == null || climbing.button == null || climbing.buttonText == null)
            {
                Plugin.Log.LogWarning("Custom run ascent: the Custom Run screen has no climbing stamina row to copy; the Ascent setting is not on it (custom runs keep their gameplay settings).");
                return;
            }
            CustomOptionMulti fog = rows.FirstOrDefault(r => r.settingType == RunSettings.SETTINGTYPE.Fog);
            string offKey = fog != null && fog.labels != null && fog.labels.Length > 0 ? fog.labels[0] : "OFF";
            GameObject made = UnityEngine.Object.Instantiate(climbing.gameObject, climbing.transform.parent);
            made.name = RowName;
            made.transform.SetSiblingIndex(climbing.transform.GetSiblingIndex() + 1);
            PlaceUnder((RectTransform)made.transform, climbing);
            CustomOptionMulti copy = made.GetComponent<CustomOptionMulti>();
            AscentOption option = made.AddComponent<AscentOption>();
            option.Take(copy, offKey);
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    /// <summary>Where the rows are not laid out by the panel, one row's step below the climbing stamina's.</summary>
    private static void PlaceUnder(RectTransform row, CustomOptionMulti climbing)
    {
        Transform list = climbing.transform.parent;
        if (list.GetComponent<LayoutGroup>() != null)
        {
            return;
        }
        RectTransform last = (RectTransform)climbing.transform;
        int index = last.GetSiblingIndex();
        RectTransform before = index > 0 ? list.GetChild(index - 1) as RectTransform : null;
        float step = before != null ? last.anchoredPosition.y - before.anchoredPosition.y : -last.rect.height;
        row.anchoredPosition = last.anchoredPosition + new Vector2(0f, step);
    }

}

/// <summary>
/// The Ascent row's own switch: a click steps on to the next ascent (after the highest, off again), a right click back;
/// off it looks like the game's settings at their defaults, on like those changed, and the gameplay settings above it
/// then show faded, being set aside.
/// </summary>
internal sealed class AscentOption : CustomOptionBase
{
    private const float FadedAlpha = 0.55f;

    private Button button;
    private LocalizedText buttonText;
    private Image buttonImage;
    private Color defaultColor;
    private Color adjustedColor;
    private string offKey;

    /// <summary>For the tests: the button's text as shown.</summary>
    internal string Shown => buttonText != null && buttonText.tmp != null ? buttonText.tmp.text : "";

    /// <summary>For the tests: the button.</summary>
    internal Button Button => button;

    internal void Take(CustomOptionMulti from, string off)
    {
        sfxClick = from.sfxClick;
        sfxHover = from.sfxHover;
        button = from.button;
        buttonText = from.buttonText;
        buttonImage = from.buttonImage;
        defaultColor = from.defaultColor;
        adjustedColor = from.adjustedColor;
        offKey = off;
        if (from.label != null)
        {
            from.label.SetIndex(CustomRunAscent.LabelKey);
        }
        if (from.tooltip != null)
        {
            // The game's gameplay rows have no tips; one copied would tell the climbing stamina's.
            UnityEngine.Object.Destroy(from.tooltip);
        }
        // The copied button would still call the copied row (being removed); it calls this one instead.
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        }
        button.onClick.AddListener(OnClick);
        button.gameObject.AddComponent<AscentOptionBack>().option = this;
        foreach (CustomOptionButtonHelper helper in GetComponentsInChildren<CustomOptionButtonHelper>(true))
        {
            helper.option = this;
        }
        if (buttonText != null && buttonText.tmp != null)
        {
            // The ascents' names are longer than the game's settings: they shrink to fit the button.
            buttonText.tmp.enableAutoSizing = true;
            buttonText.tmp.fontSizeMax = buttonText.tmp.fontSize;
            buttonText.tmp.fontSizeMin = buttonText.tmp.fontSize * 0.6f;
        }
        Refresh();
    }

    private void OnEnable()
    {
        Refresh();
    }

    public override void OnClick()
    {
        base.OnClick();
        Step(1);
    }

    /// <summary>A right click: the ascent before (from off, the highest).</summary>
    internal void Back()
    {
        base.OnClick();
        Step(-1);
    }

    private void Step(int by)
    {
        int count = CustomRunAscent.ValueOf(CustomRunAscent.Highest) + 1;
        int value = ((CustomRunAscent.Chosen + by) % count + count) % count;
        RunSettings.SetValue(CustomRunAscent.Setting, value);
        RunSettings.SaveToDisk();
        Refresh();
    }

    public override void RestoreDefault()
    {
        Refresh();
    }

    internal void Refresh()
    {
        if (button == null)
        {
            return;
        }
        int value = CustomRunAscent.Chosen;
        List<AscentData.AscentInstanceData> ascents = SingletonAsset<AscentData>.Instance?.ascents;
        string key = value > 0 && ascents != null && value < ascents.Count ? ascents[value].title : offKey;
        if (buttonText != null)
        {
            buttonText.SetIndex(key);
        }
        if (buttonImage != null)
        {
            buttonImage.color = value == 0 ? defaultColor : adjustedColor;
        }
        Fade(value != 0);
    }

    /// <summary>The other gameplay settings (the rows beside this one) faded while an ascent sets them aside.</summary>
    private void Fade(bool aside)
    {
        foreach (Transform row in transform.parent)
        {
            if (row == transform || row.GetComponent<CustomOptionBase>() == null)
            {
                continue;
            }
            CanvasGroup group = row.GetComponent<CanvasGroup>();
            if (group == null)
            {
                if (!aside)
                {
                    continue;
                }
                group = row.gameObject.AddComponent<CanvasGroup>();
            }
            group.alpha = aside ? FadedAlpha : 1f;
        }
    }
}

/// <summary>The right click on the Ascent row's button (the game's buttons only take the left one).</summary>
internal sealed class AscentOptionBack : MonoBehaviour, IPointerClickHandler
{
    internal AscentOption option;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right && option != null)
        {
            option.Back();
        }
    }
}
