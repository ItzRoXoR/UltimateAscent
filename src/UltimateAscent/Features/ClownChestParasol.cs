using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The clown chest's pool holds the Lightweight Parasol too: 0.5% of what a clown chest picks (the config's "New
/// Items" switch decides, as for the other new items; off, the regular Parasol takes its place). Weights are whole
/// numbers, so the pool's own weights are scaled up first (their odds against each other stay the game's) and the
/// parasol gets its share of the new total.
/// </summary>
internal static class ClownChestParasol
{
    /// <summary>The Lightweight Parasol's share of a clown chest's picks.</summary>
    internal const float Share = 0.005f;

    /// <summary>The pool's weights are multiplied by this first, so the parasol's weight comes out close to its share.</summary>
    private const int Scale = 100;

    /// <summary>After every other change to the tables (the chalk's, the Torch's), so their weights are scaled too.</summary>
    [HarmonyPatch(typeof(LootData), nameof(LootData.PopulateLootData))]
    [HarmonyPriority(Priority.Last)]
    private static class InTheClownPool
    {
        private static void Postfix()
        {
            if (LootData.AllSpawnWeightData == null || !LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageClown, out Dictionary<ushort, int> pool)
                || pool.Count == 0 || !ItemDatabase.TryGetItem(A14_Items.RootsParasolPrefab, out Item parasol) || parasol == null)
            {
                return;
            }
            pool.Remove(parasol.itemID);
            foreach (ushort id in pool.Keys.ToList())
            {
                pool[id] *= Scale;
            }
            int total = pool.Values.Sum();
            pool[parasol.itemID] = Mathf.Max(1, Mathf.RoundToInt(total * Share / (1f - Share)));
        }
    }
}
