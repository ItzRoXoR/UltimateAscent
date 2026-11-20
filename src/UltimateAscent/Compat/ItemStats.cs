using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using TMPro;
using UltimateAscent.Core;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateAscent.Compat;

/// <summary>
/// Item Stats (boxofbiscuits97) lists the held item's numbers next to the inventory when a slot is
/// drawn, reading the item's own fields in its prefix on <see cref="InventoryItemUI.SetItem"/>. This
/// prefix runs before it and puts this ascent's numbers in place (<see cref="ItemFigures"/>), the
/// weight included; the finalizer puts the item's own back after it.
/// </summary>
internal static class ItemStats
{
    internal const string Guid = "com.github.boxofbiscuits97.ItemStats";

    private static bool? present;

    internal static bool Present => present ??= Chainloader.PluginInfos.ContainsKey(Guid);

    [HarmonyPatch(typeof(InventoryItemUI), nameof(InventoryItemUI.SetItem))]
    private static class HeldItemNumbers
    {
        private static bool Prepare() => Present;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(out bool __state)
        {
            Character local = Character.localCharacter;
            ItemFigures.Begin(local != null ? local.data.currentItem : null, weight: true);
            __state = true;
        }

        private static Exception Finalizer(bool __state, Exception __exception)
        {
            if (__state)
            {
                ItemFigures.End();
            }
            if (__exception != null)
            {
                Failures++;
                Item held = Character.localCharacter != null ? Character.localCharacter.data.currentItem : null;
                string what = (held != null ? held.gameObject.name : "nothing") + ":" + __exception.GetType().Name;
                if (failed.Add(what))
                {
                    Plugin.Log.LogWarning($"Drawing an inventory slot failed (Item Stats is on), holding {(held != null ? held.gameObject.name : "nothing")}: {__exception}");
                }
            }
            return __exception;
        }
    }

    /// <summary>For the tests: redraws of a slot that failed, and the temporary slot's numbers found not drawn.</summary>
    internal static int Failures;
    internal static int Faults;

    private static readonly HashSet<string> failed = new HashSet<string>();
    private static readonly HashSet<string> reported = new HashSet<string>();
    private static float nextWatch;

    /// <summary>
    /// Once a second, with Item Stats: the numbers it draws by the temporary (fourth) slot's icons. One whose icon shows
    /// but whose number cannot be drawn is written to the log, once for each way it fails, with everything that draws it
    /// (to find out why that happens now and then).
    /// </summary>
    internal static void Watch()
    {
        if (!Present || Time.unscaledTime < nextWatch || GUIManager.instance == null)
        {
            return;
        }
        nextWatch = Time.unscaledTime + 1f;
        InventoryItemUI slot = GUIManager.instance.temporaryItem;
        if (slot == null || !slot.gameObject.activeInHierarchy || slot.fuelBar == null || slot.fuelBar.transform.parent == null)
        {
            return;
        }
        foreach (Transform stat in slot.fuelBar.transform.parent)
        {
            if (!stat.name.EndsWith("Stat") || !stat.gameObject.activeInHierarchy || stat.childCount == 0)
            {
                continue;
            }
            TextMeshProUGUI number = stat.GetChild(0).GetComponent<TextMeshProUGUI>();
            string fault = Fault(number);
            if (fault == null || !reported.Add(stat.name + ":" + fault))
            {
                continue;
            }
            Faults++;
            Plugin.Log.LogWarning($"Item Stats: the temporary slot's {stat.name} shows its icon but not its number ({fault}). {Describe(number, slot)}");
        }
    }

    private static string Fault(TextMeshProUGUI number)
    {
        if (number == null)
        {
            return "no text";
        }
        if (!number.isActiveAndEnabled)
        {
            return "its text is off";
        }
        if (string.IsNullOrEmpty(number.text))
        {
            return "empty";
        }
        if (number.font == null)
        {
            return "no font";
        }
        if (number.fontSharedMaterial == null)
        {
            return "no material";
        }
        if (number.color.a < 0.05f)
        {
            return "transparent";
        }
        if (number.canvasRenderer.GetAlpha() < 0.05f)
        {
            return "faded out";
        }
        if (number.canvasRenderer.cull)
        {
            return "masked off";
        }
        if (number.rectTransform.lossyScale.x < 0.001f)
        {
            return "no size";
        }
        if (number.textInfo != null && number.textInfo.characterCount == 0)
        {
            return "no letters drawn";
        }
        if (number.maxVisibleCharacters < number.text.Length || number.firstVisibleCharacter > 0)
        {
            return $"letters cut off (from {number.firstVisibleCharacter}, at most {number.maxVisibleCharacters})";
        }
        return null;
    }

    private static string Describe(TextMeshProUGUI number, InventoryItemUI slot)
    {
        StringBuilder text = new StringBuilder();
        Item held = Character.localCharacter != null ? Character.localCharacter.data.currentItem : null;
        text.Append($"Text '{number?.text}'");
        if (number != null)
        {
            Material material = number.fontSharedMaterial;
            text.Append($", font {(number.font != null ? number.font.name : "none")} (atlas {(number.font != null && number.font.atlasTexture != null ? number.font.atlasTexture.name : "none")})");
            text.Append($", material {(material != null ? material.name + " / " + material.shader?.name : "none")}");
            text.Append($", the slot's name's material {(slot.nameText != null && slot.nameText.fontSharedMaterial != null ? slot.nameText.fontSharedMaterial.name : "none")} (the same {slot.nameText != null && slot.nameText.fontSharedMaterial == material})");
            text.Append($", colour {number.color}, renderer alpha {number.canvasRenderer.GetAlpha():0.##}, culled {number.canvasRenderer.cull}, maskable {number.maskable}");
            text.Append($", size {number.fontSize}, letters drawn {(number.textInfo != null ? number.textInfo.characterCount : -1)}, enabled {number.enabled}, active {number.gameObject.activeInHierarchy}");
            text.Append($", rect {number.rectTransform.rect.size}, at {number.rectTransform.localPosition}, scale {number.rectTransform.lossyScale.x:0.###}");
            List<string> masks = new List<string>();
            for (Transform part = number.transform.parent; part != null; part = part.parent)
            {
                if (part.GetComponent<RectMask2D>() != null || part.GetComponent<Mask>() != null)
                {
                    masks.Add(part.name);
                }
            }
            text.Append($", masks above it: {(masks.Count > 0 ? string.Join("/", masks) : "none")}");
        }
        text.Append($"; holding {(held != null ? held.gameObject.name : "nothing")}, the slot's name '{(slot.nameText != null ? slot.nameText.text : "")}', place {Map.CurrentPlace}");
        return text.ToString();
    }
}
