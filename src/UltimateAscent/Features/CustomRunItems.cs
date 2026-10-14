using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// The Custom Run screen lists the Climbing Chalk and the Lightweight Parasol among its item
/// switches, after the Energy Drink and the Parasol. The game lists only items whose own loot data
/// puts them in suitcases: the chalk joins the Energy Drink's pools in code (see
/// <see cref="ClimbingChalkItem"/>), and the Roots parasol is left out on purpose, as a copy of the
/// Parasol that the Parasol's switch also covers.
/// <list type="bullet">
/// <item>In a custom run each of them follows its own switch (on by default, like every item); the
/// Parasol's switch no longer covers the Roots parasol.</item>
/// <item>In a custom run the Roots parasol is the Lightweight Parasol (its name, no weight).</item>
/// </list>
/// </summary>
internal static class CustomRunItems
{
    private const string ParasolPrefab = "Parasol";

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

    /// <summary>The Roots parasol shares the Parasol's name; its switch shows the Lightweight Parasol's.</summary>
    [HarmonyPatch(typeof(CustomOptionItemToggle), nameof(CustomOptionItemToggle.Init))]
    private static class LightweightParasolLabel
    {
        private static void Postfix(CustomOptionItemToggle __instance, Item item)
        {
            if (A14_Items.IsRootsParasol(item) && __instance.text != null)
            {
                __instance.text.SetIndex(A14_Items.LightweightParasolKey);
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
