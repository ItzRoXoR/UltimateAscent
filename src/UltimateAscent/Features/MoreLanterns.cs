using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Lantern turns up in the suitcases of the Shore, the Tropics, the Roots, the Alpine and the Mesa, and nowhere
/// else, on every ascent (custom runs and TFA's FINAL ASCENT too; the user's). The game puts it only in the Tropics'
/// and the Alpine's, at its rarity's weight (Epic: 20). The Alpine's keep that weight (as in the game); the Shore's,
/// the Tropics' and the Roots' get half of it, the Mesa's a third. The game builds its loot tables from the items' own
/// data: before a pick the Lantern's own spawn places are set to these, and after each rebuild its weight in each table
/// is set (the Mesa's table taken three times over, so that its third is exact in whole weights). The Torch turns up
/// wherever the Lantern does and nowhere else, equally often in each of those tables (the user's): at half the
/// Lantern's full weight (10), as often as before (see <see cref="TorchItem"/>).
/// </summary>
internal static class MoreLanterns
{
    internal const string LanternPrefab = "Lantern";

    /// <summary>The suitcase pools the Lantern is in: the Shore's, the Tropics', the Roots', the Alpine's and the Mesa's.</summary>
    internal const SpawnPool Places = SpawnPool.LuggageBeach | SpawnPool.LuggageJungle | SpawnPool.LuggageRoots | SpawnPool.LuggageTundra | SpawnPool.LuggageMesa;

    /// <summary>
    /// The Lantern's share of its rarity's weight in a pool (the user's), as a fraction: all of it in the Alpine's, a
    /// third in the Mesa's, half elsewhere.
    /// </summary>
    internal static (int over, int under) ShareIn(SpawnPool pool) => pool == SpawnPool.LuggageTundra ? (1, 1) : pool == SpawnPool.LuggageMesa ? (1, 3) : (1, 2);

    /// <summary>The Torch's weight in each of the Lantern's tables, the same in all of them (the user's): half the Lantern's Epic weight.</summary>
    internal const int TorchWeight = 10;

    private static Item lantern;

    /// <summary>The Lantern's spawn places (and the loot tables) are the ones for this climb.</summary>
    internal static void Sync()
    {
        if (lantern == null && (!ItemDatabase.TryGetItem(LanternPrefab, out lantern) || lantern == null))
        {
            return;
        }
        if (!lantern.TryGetComponent(out LootData loot))
        {
            return;
        }
        SpawnPool wanted = Places;
        bool changed = false;
        if (loot.spawnLocations != wanted)
        {
            loot.spawnLocations = wanted;
            changed = true;
        }
        if (TorchItem.Template != null && TorchItem.Template.TryGetComponent(out LootData torch) && torch.spawnLocations != wanted)
        {
            torch.spawnLocations = wanted;
            changed = true;
        }
        if (changed)
        {
            LootData.PopulateLootData();
        }
    }

    /// <summary>
    /// After each rebuild of the loot tables (last, after the mod's other items are in): the Lantern's weight in each of
    /// its tables, and the Torch's, the same in all of them (half the Lantern's full weight). A share that is no whole
    /// weight (the Mesa's third of 20) takes the table that many times over (every item's weight, so the odds of the
    /// rest stay as they were; the Torch's too).
    /// </summary>
    [HarmonyPatch(typeof(LootData), nameof(LootData.PopulateLootData))]
    private static class Weights
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (LootData.AllSpawnWeightData == null || !ItemDatabase.TryGetItem(LanternPrefab, out Item found) || found == null || !found.TryGetComponent(out LootData loot))
            {
                return;
            }
            int full = LootData.RarityWeights[loot.Rarity];
            Item torch = TorchItem.Template;
            foreach (KeyValuePair<SpawnPool, Dictionary<ushort, int>> table in LootData.AllSpawnWeightData)
            {
                Dictionary<ushort, int> weights = table.Value;
                if (!weights.ContainsKey(found.itemID))
                {
                    continue;
                }
                (int over, int under) = ShareIn(table.Key);
                int times = full * over % under == 0 ? 1 : under;
                if (times > 1)
                {
                    foreach (ushort id in weights.Keys.ToList())
                    {
                        weights[id] *= times;
                    }
                }
                int weight = full * over * times / under;
                weights[found.itemID] = weight;
                if (torch != null && weights.ContainsKey(torch.itemID))
                {
                    weights[torch.itemID] = TorchWeight * times;
                }
            }
        }
    }

    [HarmonyPatch(typeof(LootData), nameof(LootData.GetRandomItems))]
    private static class SuitcasePicks
    {
        private static void Prefix()
        {
            Sync();
        }
    }

    [HarmonyPatch(typeof(LootData), nameof(LootData.GetRandomItem))]
    private static class SinglePicks
    {
        private static void Prefix()
        {
            Sync();
        }
    }
}
