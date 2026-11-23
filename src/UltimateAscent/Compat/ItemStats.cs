using System;
using System.Collections.Generic;
using System.Reflection;
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

    /// <summary>
    /// The game draws the inventory's slots every frame (GUIManager.Update, UpdateItems), and Item Stats rebuilds its numbers
    /// on every slot each time: some 70 labels set to "0" and written again, its icons switched off and on, a search of the
    /// scene by path, every frame (4-5 ms a frame on the Shore, measured with Item Stats alone: 82 FPS without it, 58-62
    /// with it). Its rebuild of a slot is skipped while nothing it reads there has changed: the slot's item, the held
    /// item, how cooked it is, the slot's name shown, the scout a skeleton, the ascent's weight modifier; at least twice
    /// a second it rebuilds anyway, for anything else (its settings, an item's numbers changed in place).
    /// <para>And its numbers are kept drawn. Item Stats makes a slot's first number by copying the slot's name, then sets
    /// its outline and switches it on; when the slot's name is hidden at that moment (the temporary slot's often is) the
    /// copy is off too, setting its outline on it throws, and it is never switched on: that number, and every number
    /// copied from it later, stays invisible on that slot for the rest of the game (only the icons show), and the throw
    /// cut the game's drawing of the rest of the inventory short that frame. After each rebuild a number left off is
    /// switched on with the outline, alignment and colour Item Stats meant to give it, and its throw is caught there
    /// (logged once).</para>
    /// </summary>
    [HarmonyPatch]
    private static class OnlyWhenChanged
    {
        private const float RefreshSeconds = 0.5f;

        private static MethodBase target;

        private struct Seen
        {
            internal int item;
            internal int held;
            internal int cooked;
            internal bool named;
            internal bool skeleton;
            internal int weight;
            internal float at;
        }

        private static readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();

        private static bool Prepare()
        {
            if (!Present)
            {
                return false;
            }
            target ??= AccessTools.Method(AccessTools.TypeByName("ItemStats.ItemStats"), "SetItemPatch");
            if (target == null)
            {
                Plugin.Log.LogWarning("Item Stats: its slot rebuild was not found; it keeps rebuilding every frame.");
            }
            return target != null;
        }

        private static MethodBase TargetMethod() => target;

        // Item Stats' own parameter is named __instance, so it is taken by position here.
        private static bool Prefix(object[] __args, out bool __state)
        {
            __state = true;
            if (!SkipUnchanged || __args == null || __args.Length == 0 || !(__args[0] is InventoryItemUI slot) || slot == null)
            {
                return true;
            }
            Character local = Character.localCharacter;
            Item held = local != null && local.data != null ? local.data.currentItem : null;
            Seen now = new Seen
            {
                item = slot._itemPrefab != null ? slot._itemPrefab.GetInstanceID() : 0,
                held = held != null ? held.GetInstanceID() : 0,
                cooked = held != null && held.cooking != null ? held.cooking.timesCookedLocal : 0,
                named = slot.nameText != null && slot.nameText.enabled,
                skeleton = local != null && local.data != null && local.data.isSkeleton,
                weight = Ascents.itemWeightModifier,
                at = Time.unscaledTime,
            };
            int id = slot.GetInstanceID();
            if (seen.TryGetValue(id, out Seen last) && last.item == now.item && last.held == now.held && last.cooked == now.cooked
                && last.named == now.named && last.skeleton == now.skeleton && last.weight == now.weight && now.at - last.at < RefreshSeconds)
            {
                Skipped++;
                __state = false;
                return false;
            }
            seen[id] = now;
            Rebuilt++;
            return true;
        }

        /// <summary>After a rebuild (one that threw too): its numbers left off are switched on; its throw is caught.</summary>
        private static Exception Finalizer(Exception __exception, object[] __args, bool __state)
        {
            InventoryItemUI slot = __args != null && __args.Length > 0 ? __args[0] as InventoryItemUI : null;
            if (__state && slot != null && RepairNumbers)
            {
                Repair(slot);
            }
            if (__exception == null)
            {
                return null;
            }
            if (!RepairNumbers)
            {
                return __exception;
            }
            Caught++;
            if (slot != null)
            {
                // Rebuilt again next frame, with the number now on.
                seen.Remove(slot.GetInstanceID());
            }
            Item held = Character.localCharacter != null ? Character.localCharacter.data.currentItem : null;
            if (failed.Add("rebuild:" + __exception.GetType().Name + ":" + __exception.TargetSite?.Name))
            {
                Plugin.Log.LogWarning($"Item Stats failed building its numbers on a slot (holding {(held != null ? held.gameObject.name : "nothing")}); caught, and its numbers there switched on: {__exception.GetType().Name} in {__exception.TargetSite?.DeclaringType?.Name}.{__exception.TargetSite?.Name}");
            }
            return null;
        }

        /// <summary>The slot's numbers that Item Stats left off: on, with the outline, alignment and colour it gives them.</summary>
        private static void Repair(InventoryItemUI slot)
        {
            Transform holder = slot.fuelBar != null ? slot.fuelBar.transform.parent : null;
            if (holder == null)
            {
                return;
            }
            foreach (Transform stat in holder)
            {
                if (!stat.name.EndsWith("Stat"))
                {
                    continue;
                }
                foreach (TextMeshProUGUI number in stat.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (number.enabled)
                    {
                        continue;
                    }
                    number.enabled = true;
                    number.alignment = (TextAlignmentOptions)516;
                    Image icon = stat.GetComponent<Image>();
                    if (icon != null)
                    {
                        number.color = icon.color;
                    }
                    if (number.isActiveAndEnabled)
                    {
                        try
                        {
                            number.outlineWidth = 0.1f;
                        }
                        catch (Exception)
                        {
                            // Only the outline; the number shows without it.
                        }
                    }
                    Repaired++;
                }
            }
        }
    }

    /// <summary>For the tests: numbers Item Stats had left off and the mod switched on, its rebuilds that threw and were caught; the repair on.</summary>
    internal static int Repaired;
    internal static int Caught;
    internal static bool RepairNumbers = true;

    /// <summary>For the tests: Item Stats' slot rebuilds skipped (nothing changed) and let through; whether the skipping is on.</summary>
    internal static int Skipped;
    internal static int Rebuilt;
    internal static bool SkipUnchanged = true;

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
