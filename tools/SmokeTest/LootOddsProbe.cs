using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>What the Alpine's suitcases and spawners give, read from the game with the mod's tables (lootodds, only when asked).</summary>
internal sealed partial class Runner
{
    /// <summary>The items asked about: the First Aid Kit (the Medkit), the Portable Stove, the Lantern, the Heat Pack, the Fortified Milk, the Faerie Lantern, the Cure-All and the Torch.</summary>
    private static readonly string[] OddsAsked = { "FirstAidKit", "PortableStovetopItem", "Lantern", "Heat Pack", "FortifiedMilk", "Lantern_Faerie", "Cure-All", "Torch" };

    private const int OddsFills = 20000;

    /// <summary>
    /// Every pool the Alpine's spawners draw from (each item's weight and share of a pick), then each kind of spawner there:
    /// how many on this map, and from many fills as the game fills them (with ascent 10's lighter big suitcases) how often
    /// each asked item is in one and how many on average; the expected count of each in the whole Alpine.
    /// </summary>
    private IEnumerator LootOddsProbe()
    {
        if (!Map.InAlpine)
        {
            MapHandler.JumpToSegment(Segment.Alpine);
            yield return new WaitForSeconds(10f);
        }
        Biome alpine = Resources.FindObjectsOfTypeAll<Biome>().FirstOrDefault(b => b != null && b.gameObject.scene.IsValid() && b.biomeType == Biome.BiomeType.Alpine);
        if (alpine == null)
        {
            Info("the loot odds probe needs the Alpine", $"place {Map.CurrentPlace}; run on a map with the Alpine (-Biome 0)");
            yield break;
        }
        MoreLanterns.Sync();
        ItemDatabase db = SingletonAsset<ItemDatabase>.Instance;
        List<Item> asked = new List<Item>();
        foreach (string name in OddsAsked)
        {
            Item item = ItemByName(name) ?? db.itemLookup.Values.FirstOrDefault(i => i != null && i.gameObject.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                Info($"odds: no item {name}", string.Join(", ", db.itemLookup.Values.Where(i => i != null).Select(i => i.gameObject.name).Where(n => n.IndexOf(name.Substring(0, 4), System.StringComparison.OrdinalIgnoreCase) >= 0)));
                continue;
            }
            LootData loot = item.GetComponent<LootData>();
            asked.Add(item);
            Info($"odds: {item.gameObject.name} '{Plain(item.GetItemName())}'", $"id {item.itemID}, rarity {(loot != null ? loot.Rarity.ToString() : "-")}, pools {(loot != null ? loot.spawnLocations.ToString() : "-")}, banned in solo {(loot != null && loot.banInSolo)}");
        }
        Info("odds: testing", $"ascent {target}, place {Map.CurrentPlace}, ascent 10's light luggage {Rules.Enabled(Lvl.LightLuggage)}, players {Photon.Pun.PhotonNetwork.PlayerList.Length}");

        // Every suitcase on the map: what it is, its pool and where it stands.
        string Where(Component c)
        {
            Biome parent = c.GetComponentInParent<Biome>(true);
            string ground = Map.TryGetGroundBiome(c.transform.position, out Biome.BiomeType b) ? b.ToString() : "no ground";
            string segment = c.transform.GetComponentsInParent<Transform>(true).Select(t => t.name).FirstOrDefault(n => n.EndsWith("_Segment")) ?? "no segment";
            return $"biome {(parent != null ? parent.biomeType.ToString() : "none")}, ground {ground}, {segment}";
        }
        foreach (var group in Object.FindObjectsByType<Luggage>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(l => l != null && l.gameObject.scene.IsValid())
            .GroupBy(l => $"{l.GetType().Name} {Regex.Replace(l.gameObject.name, @"\s*\(\d+\)$", "")} '{l.displayName}': pool {l.GetSpawnPool()}; {Where(l)}")
            .OrderBy(g => g.Key))
        {
            Info($"odds census: {group.Count()} x {group.Key}", "");
        }
        // The Ancient Luggage's and the Ancient Statues' tables: the Faerie Lantern and the Cure-All.
        foreach (SpawnPool pool in new[] { SpawnPool.LuggageAncient, SpawnPool.LuggageCursed, SpawnPool.RespawnCoffin })
        {
            if (!LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> weights))
            {
                Info($"odds pool {pool}", "no table");
                continue;
            }
            int total = weights.Values.Sum();
            Info($"odds pool {pool}: {weights.Count} items, total weight {total}", string.Join(", ", weights.OrderByDescending(p => p.Value).Select(p => $"{(ItemDatabase.TryGetItem(p.Key, out Item it) ? it.gameObject.name : p.Key.ToString())} {p.Value} ({100f * p.Value / total:0.0}%)")));
        }

        // The spawners of the Alpine, by kind.
        Dictionary<string, List<Spawner>> kinds = new Dictionary<string, List<Spawner>>();
        foreach (Spawner spawner in alpine.GetComponentsInChildren<Spawner>(true))
        {
            string name = Regex.Replace(spawner.gameObject.name, @"\s*\(\d+\)$", "");
            string key = $"{spawner.GetType().Name} {name}: pool {spawner.GetSpawnPool()}, mode {spawner.spawnMode}/{spawner.spawnPointMode}, chance {spawner.baseSpawnChance:0.##}, on start {spawner.spawnOnStart}, repeats {spawner.canRepeatSpawns}, players >= {spawner.playersInRoomRequirement}, below ascent {spawner.belowAscentRequirement}";
            if (!kinds.TryGetValue(key, out List<Spawner> list))
            {
                kinds[key] = list = new List<Spawner>();
            }
            list.Add(spawner);
        }

        // Each pool they draw from: the asked items' weights and shares, then every item in it.
        foreach (SpawnPool pool in kinds.Values.Select(l => l[0].GetSpawnPool()).Where(p => p != SpawnPool.None).Distinct().OrderBy(p => p.ToString()))
        {
            if (!LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> weights))
            {
                Info($"odds pool {pool}", "no table");
                continue;
            }
            int total = weights.Values.Sum();
            string mine = string.Join("; ", asked.Select(i => weights.TryGetValue(i.itemID, out int w) ? $"{i.gameObject.name} {w}/{total} = {100f * w / total:0.00}% a pick" : $"{i.gameObject.name} not in it"));
            Info($"odds pool {pool}: {weights.Count} items, total weight {total}", mine);
            string all = string.Join(", ", weights.OrderByDescending(p => p.Value).Select(p => $"{(ItemDatabase.TryGetItem(p.Key, out Item it) ? it.gameObject.name : p.Key.ToString())} {p.Value} ({100f * p.Value / total:0.0}%)"));
            Info($"odds pool {pool}: all items", all);
        }

        // Fills as the game makes them: the spots (a weighted list picks its own), ascent 10's single item in half the big and
        // Explorer's suitcases, then the picks without repeats unless the spawner repeats.
        float[] expected = new float[asked.Count];
        foreach (KeyValuePair<string, List<Spawner>> kind in kinds.OrderBy(k => k.Key))
        {
            Spawner spawner = kind.Value[0];
            if (spawner is RespawnChest)
            {
                Info($"odds: {kind.Value.Count} x {kind.Key}", "a revive statue (not simulated)");
                continue;
            }
            int[] holding = new int[asked.Count];
            int[] count = new int[asked.Count];
            int items = 0;
            bool light = A10_LightLuggage.Lightens(spawner);
            for (int fill = 0; fill < OddsFills; fill++)
            {
                List<Transform> spots = spawner.GetSpawnSpots();
                int spotCount = spots != null ? spots.Count : 0;
                if (light && spotCount >= 2 && Random.Range(0, 2) == 0)
                {
                    spotCount = 1;
                }
                if (spotCount == 0)
                {
                    continue;
                }
                List<GameObject> got = spawner.GetObjectsToSpawn(spotCount, spawner.canRepeatSpawns);
                items += got.Count(g => g != null);
                for (int i = 0; i < asked.Count; i++)
                {
                    int n = got.Count(g => g != null && g.name == asked[i].gameObject.name);
                    count[i] += n;
                    if (n > 0)
                    {
                        holding[i]++;
                    }
                }
            }
            // Suitcases are filled when opened; other spawners as the map loads, at their chance.
            float chance = spawner is Luggage ? 1f : spawner.spawnOnStart ? spawner.baseSpawnChance : 0f;
            for (int i = 0; i < asked.Count; i++)
            {
                expected[i] += kind.Value.Count * chance * count[i] / (float)OddsFills;
            }
            string odds = string.Join("; ", asked.Select((it, i) => $"{it.gameObject.name} in {100f * holding[i] / OddsFills:0.00}% (avg {count[i] / (float)OddsFills:0.000})").Where((s, i) => count[i] > 0));
            Info($"odds: {kind.Value.Count} x {kind.Key}", $"{items / (float)OddsFills:0.00} items a fill; {(odds.Length > 0 ? odds : "none of the asked items")}");
            yield return null;
        }
        Info("odds: expected in the whole Alpine (every suitcase opened)", string.Join("; ", asked.Select((it, i) => $"{it.gameObject.name} {expected[i]:0.00}")));

        // The Alpine's own loot (AlpineLoot): the four's averages, the Explorer's suitcases, the Ancient Luggage.
        Dictionary<SpawnPool, Dictionary<ushort, int>> alpineTables = AlpineLoot.Tables();
        foreach ((string prefab, float wanted) in AlpineLoot.Targets)
        {
            int i = asked.FindIndex(a => a.gameObject.name == prefab);
            Check($"the whole Alpine holds {wanted:0} of {prefab} on average, every suitcase opened, from many fills as the game fills them",
                i >= 0 && Mathf.Abs(expected[i] - wanted) <= wanted * 0.04f,
                $"{(i >= 0 ? expected[i].ToString("0.00") : "not found")} (worked out {(AlpineLoot.Expected.TryGetValue(prefab, out float e) ? e.ToString("0.00") : "-")}, weight {(AlpineLoot.Weights.TryGetValue(prefab, out int w) ? w.ToString() : "-")} of the Alpine's table x100)");
        }
        Dictionary<ushort, int> gameClimber = LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageClimber, out Dictionary<ushort, int> gc) ? gc : new Dictionary<ushort, int>();
        Dictionary<ushort, int> alpineClimber = alpineTables != null && alpineTables.TryGetValue(SpawnPool.LuggageClimber, out Dictionary<ushort, int> ac) ? ac : null;
        List<string> explorers = new List<string>();
        bool explorersRight = alpineClimber != null;
        foreach (string prefab in AlpineLoot.InExplorers)
        {
            Item item = asked.FirstOrDefault(a => a.gameObject.name == prefab);
            int rarity = item != null && item.TryGetComponent(out LootData ld) ? LootData.RarityWeights[ld.Rarity] : -1;
            int inAlpine = item != null && alpineClimber != null && alpineClimber.TryGetValue(item.itemID, out int aw) ? aw : 0;
            bool elsewhere = item != null && gameClimber.ContainsKey(item.itemID);
            explorersRight &= inAlpine == rarity && !elsewhere;
            explorers.Add($"{prefab} {inAlpine} (its rarity's {rarity}){(elsewhere ? ", ALSO in the game's own table" : "")}");
        }
        Item torchItem = TorchItem.Template;
        int torchWeight = torchItem != null && alpineClimber != null && alpineClimber.TryGetValue(torchItem.itemID, out int tw) ? tw : 0;
        explorersRight &= torchItem == null || torchWeight == MoreLanterns.TorchWeight;
        Check("the Explorer's suitcases in the Alpine hold the Portable Stove, the Lantern, the Heat Pack and the Fortified Milk at their rarity's weight (the Torch beside the Lantern), only there",
            explorersRight, string.Join("; ", explorers) + $"; Torch {torchWeight}; the Alpine's Explorer's table total {(alpineClimber != null ? alpineClimber.Values.Sum() : 0)} (the game's {gameClimber.Values.Sum()})");
        Dictionary<ushort, int> gameCursed = LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageCursed, out Dictionary<ushort, int> gcu) ? gcu : null;
        Dictionary<ushort, int> alpineCursed = alpineTables != null && alpineTables.TryGetValue(SpawnPool.LuggageCursed, out Dictionary<ushort, int> acu) ? acu : null;
        float Share(Dictionary<ushort, int> table, string prefab)
        {
            Item it = asked.FirstOrDefault(a => a.gameObject.name == prefab);
            if (table == null || it == null || !table.TryGetValue(it.itemID, out int weight))
            {
                return 0f;
            }
            long total = table.Where(p => ItemDatabase.TryGetItem(p.Key, out Item x) && x.IsValidToSpawn()).Sum(p => (long)p.Value);
            return total > 0 ? weight / (float)total : 0f;
        }
        string[] ancient = { AlpineLoot.FaerieLanternPrefab, AlpineLoot.CureAllPrefab };
        Check("the Ancient Luggage in the Alpine gives the Faerie Lantern and the Cure-All twice as often a pick as the game's",
            gameCursed != null && alpineCursed != null && ancient.All(p => Share(gameCursed, p) > 0f && Mathf.Abs(Share(alpineCursed, p) - 2f * Share(gameCursed, p)) < 1e-5f),
            string.Join("; ", ancient.Select(p => $"{p} {Share(gameCursed, p):P2} -> {Share(alpineCursed, p):P2}")));
    }
}
