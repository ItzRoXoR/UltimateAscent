using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>Where the bananas (Berrynanas) and the Hot Dog come from, and how many lie in the Gloom (read from the game).</summary>
internal sealed partial class Runner
{
    private static bool Wanted(GameObject prefab)
    {
        if (prefab == null)
        {
            return false;
        }
        Item item = prefab.GetComponent<Item>();
        string text = (prefab.name + " " + (item != null ? item.GetItemName() : "")).ToLowerInvariant();
        return text.Contains("nana") || text.Contains("banana") || text.Contains("hot dog") || text.Contains("hotdog");
    }

    /// <summary>
    /// The bananas and the Hot Dog: every item like them with its rarity, its pools and its share of a pick in each; every
    /// spawner in the Gloom's biome (the Gloom and the Citadel) that can give one (how many such spawners, what share);
    /// then, with the Gloom loaded, how many of each lie about there (suitcases still closed).
    /// </summary>
    private IEnumerator FoodProbe()
    {
        List<Item> foods = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.Where(i => i != null && Wanted(i.gameObject)).OrderBy(i => i.gameObject.name).ToList();
        foreach (Item item in foods)
        {
            LootData loot = item.GetComponent<LootData>();
            List<string> pools = new List<string>();
            if (LootData.AllSpawnWeightData != null)
            {
                foreach (KeyValuePair<SpawnPool, Dictionary<ushort, int>> pool in LootData.AllSpawnWeightData)
                {
                    if (pool.Value.TryGetValue(item.itemID, out int weight))
                    {
                        int total = pool.Value.Values.Sum();
                        pools.Add($"{pool.Key} {weight}/{total} = {100f * weight / Mathf.Max(1, total):0.0}% of a pick");
                    }
                }
            }
            Info($"food: {item.gameObject.name} '{Plain(item.GetItemName())}'", $"id {item.itemID}, rarity {(loot != null ? loot.Rarity.ToString() : "-")}, pools {(loot != null ? loot.spawnLocations.ToString() : "-")}; {(pools.Count > 0 ? string.Join("; ", pools) : "in no pool")}");
        }

        // Loaded once only: loading a segment again would have the game fill its nests again.
        if (!(Map.InVolcanoCaldera || Map.InGloom))
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(10f);
        }
        Biome swamp = Resources.FindObjectsOfTypeAll<Biome>().FirstOrDefault(b => b != null && b.gameObject.scene.IsValid() && b.biomeType == Biome.BiomeType.Swamp);
        if (swamp == null)
        {
            Info("the food probe needs the Gloom", "run with -Biome MS");
            yield break;
        }
        // Every spawner there that can give one, grouped by what it is.
        var groups = new Dictionary<string, (int count, string odds)>();
        foreach (Spawner spawner in swamp.GetComponentsInChildren<Spawner>(true))
        {
            string odds = null;
            if (spawner.spawnMode == Spawner.SpawnMode.SingleItem && Wanted(spawner.spawnedObjectPrefab))
            {
                odds = $"always {spawner.spawnedObjectPrefab.name}";
            }
            else if (spawner.spawns != null && spawner.spawns.items != null && spawner.spawns.items.Any(e => Wanted(e.prefab)))
            {
                int total = spawner.spawns.items.Sum(e => e.weight);
                odds = "list " + string.Join(", ", spawner.spawns.items.Where(e => Wanted(e.prefab)).Select(e => $"{e.prefab.name} {e.weight}/{total}"));
            }
            else
            {
                SpawnPool pool = spawner.GetSpawnPool();
                if (pool != SpawnPool.None && LootData.AllSpawnWeightData != null && LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> weights))
                {
                    int total = weights.Values.Sum();
                    List<string> parts = foods.Where(f => weights.ContainsKey(f.itemID)).Select(f => $"{f.gameObject.name} {weights[f.itemID]}/{total}").ToList();
                    if (parts.Count > 0)
                    {
                        odds = $"pool {pool}: " + string.Join(", ", parts);
                    }
                }
            }
            if (odds == null)
            {
                continue;
            }
            string segment = spawner.transform.GetComponentsInParent<Transform>(true).Select(t => t.name).FirstOrDefault(n => n.EndsWith("_Segment")) ?? "?";
            string key = $"{segment} {spawner.GetType().Name} ({spawner.spawnSpots?.Count ?? 0} spots, chance {spawner.baseSpawnChance:0.##}, players >= {spawner.playersInRoomRequirement}): {odds}";
            groups[key] = (groups.TryGetValue(key, out var g) ? g.count + 1 : 1, odds);
        }
        foreach (var group in groups.OrderBy(g => g.Key))
        {
            Info($"  {group.Value.count} x {group.Key}", "");
        }
        // Other kinds of spawners (patches, vines) with spawn lists.
        foreach (SpawnList list in swamp.GetComponentsInChildren<SpawnList>(true).Where(l => l.items != null && l.items.Any(e => Wanted(e.prefab))).GroupBy(l => l.name).Select(g => g.First()))
        {
            int total = list.items.Sum(e => e.weight);
            int users = swamp.GetComponentsInChildren<SpawnList>(true).Count(l => l.name == list.name);
            Info($"  spawn list {list.name} ({users} on the biome)", string.Join(", ", list.items.Select(e => $"{(e.prefab != null ? e.prefab.name : "none")} {e.weight}/{total}")));
        }
        foreach (var group in swamp.GetComponentsInChildren<SingleItemSpawner>(true).Where(s => Wanted(s.prefab)).GroupBy(s => $"{s.prefab.name}, players >= {s.playersInRoomRequirement}, below ascent {s.belowAscentRequirement}"))
        {
            Info($"  {group.Count()} x single-item spawner: {group.Key}", "");
        }
        foreach (var group in swamp.GetComponentsInChildren<BerryVine>(true).GroupBy(v => $"{v.possibleBerries.x:0}-{v.possibleBerries.y:0} berries (pow {v.randomPow:0.##}), chance {v.baseSpawnChance:0.##}, {v.GetComponentsInChildren<Collider>().Length - 2} spots"))
        {
            Info($"  {group.Count()} x banana vine: {group.Key}", "");
        }
        int suitcases = swamp.GetComponentsInChildren<Luggage>(true).Count(l => l.GetComponentsInParent<Transform>(true).Any(t => t.name == "Swamp_Segment"));
        Info("suitcases in the Gloom", $"{suitcases} (filled only when opened)");
        // What lies there now.
        yield return new WaitForSeconds(2f);
        List<Item> all = Object.FindObjectsByType<Item>(FindObjectsSortMode.None).Where(i => i.itemState == ItemState.Ground && Wanted(i.gameObject)).ToList();
        foreach (var kind in all.GroupBy(i => i.gameObject.name.Replace("(Clone)", "")))
        {
            Info($"lying about now: {kind.Key}", $"{kind.Count()} (z {kind.Min(i => i.transform.position.z):F0}-{kind.Max(i => i.transform.position.z):F0}, y {kind.Min(i => i.transform.position.y):F0}-{kind.Max(i => i.transform.position.y):F0})");
        }
    }
}
