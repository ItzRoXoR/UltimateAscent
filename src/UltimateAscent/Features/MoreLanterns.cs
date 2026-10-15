using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// From ascent 9 the Lantern turns up in the suitcases of every part of the climb up to the Caldera and
/// the Gloom, at its own rarity (Epic). The game puts it only in the Tropics' and the Alpine's. The game
/// builds its loot tables from the items' own data, so before a pick the Lantern's own spawn places are
/// set for the climb (the Shore's, the Roots', the Mesa's, the Caldera's and the Gloom's added, or in other
/// climbs taken away again), and the tables are rebuilt when that changes.
/// </summary>
internal static class MoreLanterns
{
    internal const string LanternPrefab = "Lantern";

    /// <summary>The suitcase pools of the Shore, the Roots, the Mesa, the Caldera and the Gloom (the Tropics' and the Alpine's have it).</summary>
    internal const SpawnPool Extra = SpawnPool.LuggageBeach | SpawnPool.LuggageRoots | SpawnPool.LuggageMesa | SpawnPool.LuggageCaldera | SpawnPool.LuggageGloom;

    private static Item lantern;
    private static SpawnPool own;
    private static bool ownKnown;

    private static bool On => Rules.InExtraAscent;

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
        if (!ownKnown)
        {
            own = loot.spawnLocations;
            ownKnown = true;
        }
        SpawnPool wanted = On ? own | Extra : own;
        if (loot.spawnLocations != wanted)
        {
            loot.spawnLocations = wanted;
            LootData.PopulateLootData();
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
