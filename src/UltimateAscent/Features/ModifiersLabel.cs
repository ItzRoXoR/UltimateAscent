using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Under the ascent shown at the top right of a climb (the game's <see cref="AscentUI"/>), a small line in its lettering:
/// "ALL MODIFIERS ON" when every switch in the config's [Ascents] list that this ascent takes in is on (the host's), or
/// which are off ("MODIFIERS OFF: 14, 17, COLD NIGHTS"): the cold nights from ascent 5, the game's ascent 8, ascents
/// 9-25 up to this one, and the hot campfires from 17. Shown from ascent 5 (below it no switch applies), not in custom
/// runs unless they play by a picked ascent; it follows a change of the switches during the climb.
/// </summary>
[HarmonyPatch(typeof(AscentUI), "Start")]
internal static class ModifiersLabel
{
    /// <summary>The first ascent a switch applies to: the cold nights' (the game's rule from ascent 5).</summary>
    private const int FromAscent = 5;

    private static bool Russian => LocalizedText.CURRENT_LANGUAGE == LocalizedText.Language.Russian;

    private static void Postfix(AscentUI __instance)
    {
        if (__instance != null && __instance.text != null && __instance.text.GetComponent<ModifiersLine>() == null)
        {
            __instance.text.gameObject.AddComponent<ModifiersLine>().main = __instance.text;
        }
    }

    /// <summary>The switches off that this ascent takes in, in the config's order of ascents (the cold nights and hot campfires by name, last).</summary>
    internal static List<string> Off()
    {
        List<string> off = new List<string>();
        int ascent = Mathf.Min(Ascents._currentAscent, Lvl.Last);
        if (ascent >= Lvl.VanillaLast && !HostOptions.Ascent8On)
        {
            off.Add(Lvl.VanillaLast.ToString());
        }
        for (int level = Lvl.First; level <= ascent; level++)
        {
            if (!HostOptions.AscentOn(level))
            {
                off.Add(level.ToString());
            }
        }
        if (ascent >= FromAscent && !HostOptions.ColdNightsOn)
        {
            off.Add(Russian ? "ХОЛОДНЫЕ НОЧИ" : "COLD NIGHTS");
        }
        if (ascent >= Lvl.HotCampfires && HostOptions.AscentOn(Lvl.HotCampfires) && !HostOptions.HotCampfiresOn)
        {
            off.Add(Russian ? "ГОРЯЧИЕ КОСТРЫ" : "HOT CAMPFIRES");
        }
        return off;
    }

    /// <summary>The line for this climb: empty where none is shown.</summary>
    internal static string Text()
    {
        if (!Rules.InRun || !Rules.ByAscent || Ascents._currentAscent < FromAscent)
        {
            return "";
        }
        List<string> off = Off();
        if (off.Count == 0)
        {
            return Russian ? "ВСЕ МОДИФИКАТОРЫ ВКЛЮЧЕНЫ" : "ALL MODIFIERS ON";
        }
        return (Russian ? "ВЫКЛЮЧЕНЫ: " : "MODIFIERS OFF: ") + string.Join(", ", off);
    }
}

/// <summary>The line itself: a child of the ascent's text, just under its letters (wherever they end, a second line of True Final Ascent's too), aligned with them.</summary>
internal sealed class ModifiersLine : MonoBehaviour
{
    private const float Size = 0.5f;
    private const float Gap = 2f;
    private const float Every = 0.25f;

    internal TMP_Text main;
    private TextMeshProUGUI line;
    private string shownMain;
    private float next;

    /// <summary>For the tests: the line's text (empty when it is hidden).</summary>
    internal string Shown => line != null && line.gameObject.activeSelf ? line.text : "";

    private void LateUpdate()
    {
        if (main == null || Time.unscaledTime < next)
        {
            return;
        }
        next = Time.unscaledTime + Every;
        string text = ModifiersLabel.Text();
        if (line == null)
        {
            line = Make();
        }
        bool shown = text.Length > 0 && main.text.Length > 0;
        if (line.gameObject.activeSelf != shown)
        {
            line.gameObject.SetActive(shown);
        }
        if (!shown)
        {
            return;
        }
        line.color = main.color;
        if (line.text != text || shownMain != main.text)
        {
            line.text = text;
            shownMain = main.text;
            Place();
        }
    }

    private TextMeshProUGUI Make()
    {
        GameObject made = new GameObject("UltimateAscent Modifiers", typeof(RectTransform));
        made.transform.SetParent(main.transform, false);
        TextMeshProUGUI text = made.AddComponent<TextMeshProUGUI>();
        text.font = main.font;
        text.fontSharedMaterial = main.fontSharedMaterial;
        text.fontSize = main.fontSize * Size;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>Under the ascent's letters as drawn now, lined up with them the way they are aligned.</summary>
    private void Place()
    {
        main.ForceMeshUpdate();
        Bounds letters = main.textBounds;
        HorizontalAlignmentOptions across = main.horizontalAlignment;
        float x = across == HorizontalAlignmentOptions.Right ? 1f : across == HorizontalAlignmentOptions.Left ? 0f : 0.5f;
        line.alignment = x > 0.75f ? TextAlignmentOptions.TopRight : x < 0.25f ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Top;
        RectTransform rect = line.rectTransform;
        // Anchored at the ascent text's pivot, so positions are in its own space (where its letters' bounds are).
        rect.anchorMin = rect.anchorMax = main.rectTransform.pivot;
        rect.pivot = new Vector2(x, 1f);
        rect.sizeDelta = new Vector2(Mathf.Max(main.rectTransform.rect.width, 600f), line.fontSize * 1.4f);
        float edge = x > 0.75f ? letters.max.x : x < 0.25f ? letters.min.x : letters.center.x;
        rect.anchoredPosition = new Vector2(edge, letters.min.y - Gap);
    }
}
