using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateAscent.Features;

/// <summary>
/// The Custom Run screen lists the Climbing Chalk and the Lightweight Parasol among its item
/// switches, after the Energy Drink and the Parasol, and the Auto-Parachute after the Glider. The game lists only items whose own loot data
/// puts them in suitcases: the chalk joins the Energy Drink's pools in code (see
/// <see cref="ClimbingChalkItem"/>), and the Roots parasol is left out on purpose, as a copy of the
/// Parasol that the Parasol's switch also covers.
/// <list type="bullet">
/// <item>In a custom run each of them follows its own switch (on by default, like every item); the
/// Parasol's switch no longer covers the Roots parasol.</item>
/// <item>In a custom run the Roots parasol is the Lightweight Parasol (its name, no weight).</item>
/// <item>The Pink Lollipop and the Vanilla Scout Cookies (each half of an item's spawns) have switches of
/// their own after their item's (see <see cref="VariantShare"/>); the Warp Compass and the Toxic Drink are
/// listed among the mystical items.</item>
/// <item>The extra switches make the item list longer; the dotted divider and the mystical items below it
/// move down to make room, and the rows get shorter if that is what it takes to stay inside the panel.</item>
/// </list>
/// </summary>
internal static class CustomRunItems
{
    private const string ParasolPrefab = "Parasol";
    private const string GliderPrefab = "Glider";

    /// <summary>A custom run is being played (every ascent rule is off in one).</summary>
    internal static bool InCustomRun => Rules.InRun && RunSettings.IsCustomRun;

    /// <summary>Built once, when the game starts; the Custom Run screen makes a switch for each item in it.</summary>
    [HarmonyPatch(typeof(RunSettings), nameof(RunSettings.InitModifiableItemNames))]
    private static class OnTheScreen
    {
        private static void Postfix()
        {
            List<Item> items = RunSettings.ModifiableItemPrefabs;
            if (items == null)
            {
                return;
            }
            if (ItemDatabase.TryGetItem(ClimbingChalkItem.PrefabName, out Item chalk))
            {
                // Its own icon and name, and no "mystical" tag (the screen lists mystical items apart).
                ClimbingChalkItem.Prepare(chalk);
                Insert(items, chalk, ClimbingChalkItem.EnergyDrinkPrefab);
            }
            if (ItemDatabase.TryGetItem(A14_Items.RootsParasolPrefab, out Item parasol))
            {
                Insert(items, parasol, ParasolPrefab);
            }
            // The variants that take half of an item's spawns, after their item (they are in no pool of their own).
            if (PinkLollipop.Template != null)
            {
                Insert(items, PinkLollipop.Template, PinkLollipop.LollipopPrefab);
            }
            if (ItemDatabase.TryGetItem(VanillaScoutCookies.VanillaPrefab, out Item vanilla) && vanilla != null)
            {
                Insert(items, vanilla, VanillaScoutCookies.CookiesPrefab);
            }
            if (ToxicDrink.Template != null)
            {
                // The game lists it by itself (it spawns in suitcases); among the mystical items, after the Warp Compass.
                items.Remove(ToxicDrink.Template);
                Insert(items, ToxicDrink.Template, WarpCompass.PrefabName);
            }
            if (AutoParachute.Template != null)
            {
                // The game lists it by itself (it spawns in suitcases), at the end.
                items.Remove(AutoParachute.Template);
                Insert(items, AutoParachute.Template, GliderPrefab);
            }
        }

        private static void Insert(List<Item> items, Item item, string after)
        {
            if (items.Contains(item))
            {
                return;
            }
            int index = items.FindIndex(i => i != null && i.gameObject.name == after);
            items.Insert(index >= 0 ? index + 1 : items.Count, item);
        }
    }

    /// <summary>The items that come from Ancient Luggage and the Ancient Statues only (the mystical items' places).</summary>
    private static IEnumerable<Item> Mystical()
    {
        if (WarpCompass.Template != null)
        {
            yield return WarpCompass.Template;
        }
        if (ToxicDrink.Template != null)
        {
            yield return ToxicDrink.Template;
        }
    }

    /// <summary>
    /// The screen sorts the items into the regular and the mystical list by the game's "mystical" mark. The Warp
    /// Compass and the Toxic Drink come from where the mystical items come from, so they are marked for the
    /// sorting only (the mark also counts toward a badge, which stays the game's).
    /// </summary>
    [HarmonyPatch(typeof(CustomOptionsWindow), nameof(CustomOptionsWindow.SpawnItemToggles))]
    private static class MysticalOnTheScreen
    {
        private static void Prefix(out List<Item> __state)
        {
            __state = new List<Item>();
            foreach (Item item in Mystical())
            {
                if ((item.itemTags & Item.ItemTags.Mystical) == 0)
                {
                    item.itemTags |= Item.ItemTags.Mystical;
                    __state.Add(item);
                }
            }
        }

        private static Exception Finalizer(List<Item> __state, Exception __exception)
        {
            if (__state != null)
            {
                foreach (Item item in __state)
                {
                    item.itemTags &= ~Item.ItemTags.Mystical;
                }
            }
            return __exception;
        }
    }

    /// <summary>
    /// The screen puts the dotted divider and the mystical items' list at fixed places, right under the
    /// 17 rows of the game's own items, and the panel ends right under the mystical items. A longer list
    /// would run under the divider, which is drawn on top of it and takes the clicks meant for the last row
    /// (the Rope Spool's): whatever comes after the list moves down by the height the list has grown. If the
    /// mystical items would then run past the panel's bottom, the rows of both lists are made just as much
    /// shorter as it takes for everything to fit (at least 22 of the game's 28 pixels).
    /// </summary>
    [HarmonyPatch(typeof(CustomOptionsWindow), nameof(CustomOptionsWindow.SpawnItemToggles))]
    private static class RoomForLongerList
    {
        private const float ShortestRow = 22f;
        private const float BottomMargin = 10f;

        private static int Rows(Transform list, GridLayoutGroup grid)
        {
            int columns = grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? Mathf.Max(1, grid.constraintCount) : 1;
            return Mathf.CeilToInt(list.childCount / (float)columns);
        }

        private static float Height(Transform list, GridLayoutGroup grid)
        {
            int rows = Rows(list, grid);
            return grid.padding.top + rows * grid.cellSize.y + Mathf.Max(0, rows - 1) * grid.spacing.y;
        }

        private static void Postfix(CustomOptionsWindow __instance)
        {
            if (!(__instance.spawnPoolList is RectTransform list) || list.parent == null || !list.TryGetComponent(out GridLayoutGroup grid))
            {
                return;
            }
            Transform panel = list.parent;
            List<RectTransform> after = new List<RectTransform>();
            for (int i = list.GetSiblingIndex() + 1; i < panel.childCount; i++)
            {
                if (panel.GetChild(i) is RectTransform below)
                {
                    after.Add(below);
                }
            }
            if (after.Count == 0)
            {
                return;
            }
            float room = Top(list, panel) - after.Max(below => Top(below, panel));
            float grown = Height(list, grid) - room;
            if (__instance.mysticalSpawnPoolList is RectTransform mystical && mystical.parent == panel && mystical.TryGetComponent(out GridLayoutGroup mysticalGrid) && panel is RectTransform frame)
            {
                // Where the mystical items would end once moved, against the panel's bottom.
                float bottom = frame.rect.yMin + BottomMargin;
                float end = Top(mystical, panel) - Mathf.Max(0f, grown) - Height(mystical, mysticalGrid);
                if (end < bottom)
                {
                    float shorter = Mathf.Min(grid.cellSize.y - ShortestRow, (bottom - end) / Mathf.Max(1, Rows(list, grid) + Rows(mystical, mysticalGrid)));
                    if (shorter > 0f)
                    {
                        grid.cellSize = new Vector2(grid.cellSize.x, grid.cellSize.y - shorter);
                        mysticalGrid.cellSize = new Vector2(mysticalGrid.cellSize.x, mysticalGrid.cellSize.y - shorter);
                        grown = Height(list, grid) - room;
                    }
                }
            }
            if (grown <= 0.5f)
            {
                return;
            }
            foreach (RectTransform below in after)
            {
                below.anchoredPosition -= new Vector2(0f, grown);
            }
        }

        /// <summary>A rect's top edge, in the panel's space.</summary>
        private static float Top(RectTransform rect, Transform panel)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return panel.InverseTransformPoint(corners[1]).y;
        }
    }

    /// <summary>
    /// The Roots parasol shares the Parasol's name and the Vanilla Scout Cookies the Scout Cookies'; their
    /// switches show names of their own.
    /// </summary>
    [HarmonyPatch(typeof(CustomOptionItemToggle), nameof(CustomOptionItemToggle.Init))]
    private static class LightweightParasolLabel
    {
        private static void Postfix(CustomOptionItemToggle __instance, Item item)
        {
            if (__instance.text == null || item == null)
            {
                return;
            }
            if (A14_Items.IsRootsParasol(item))
            {
                __instance.text.SetIndex(A14_Items.LightweightParasolKey);
            }
            else if (item.gameObject.name == VanillaScoutCookies.VanillaPrefab)
            {
                __instance.text.SetIndex(VanillaScoutCookies.SwitchKey);
            }
        }
    }

    /// <summary>
    /// In a custom run a variant that takes half of an item's spawns (the Pink Lollipop, the Vanilla Scout
    /// Cookies) follows its own switch: with both on it takes half of them, with the item off all of them, with
    /// itself off none. Elsewhere: the share given.
    /// </summary>
    internal static float VariantShare(string itemPrefab, string variantPrefab, float share)
    {
        if (!RunSettings.IsCustomRun)
        {
            return share;
        }
        if (!RunSettings.IsItemEnabled(variantPrefab))
        {
            return 0f;
        }
        return RunSettings.IsItemEnabled(itemPrefab) ? share : 1f;
    }

    /// <summary>In a custom run an item whose variant is switched on stays in its pools, for the variant to take its place.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.IsValidToSpawn))]
    private static class VariantKeepsItsItem
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result || !RunSettings.IsCustomRun || __instance == null)
            {
                return;
            }
            string name = __instance.gameObject.name;
            string variant = name == PinkLollipop.LollipopPrefab && PinkLollipop.Template != null ? PinkLollipop.PrefabName
                : name == VanillaScoutCookies.CookiesPrefab ? VanillaScoutCookies.VanillaPrefab : null;
            if (variant != null && RunSettings.IsItemEnabled(variant) && (!__instance.TryGetComponent(out LootData loot) || loot.IsValidToSpawn()))
            {
                __result = true;
            }
        }
    }

    /// <summary>The game checks the Roots parasol against the Parasol's switch; in a custom run it has its own.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.IsValidToSpawn))]
    private static class LightweightParasolSwitch
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (RunSettings.IsCustomRun && A14_Items.IsRootsParasol(__instance))
            {
                LootData loot = __instance.GetComponent<LootData>();
                __result = RunSettings.IsItemEnabled(A14_Items.RootsParasolPrefab) && (loot == null || loot.IsValidToSpawn());
            }
        }
    }
}
