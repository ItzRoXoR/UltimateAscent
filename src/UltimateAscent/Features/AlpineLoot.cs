using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltimateAscent.Features;

/// <summary>
/// The Alpine's loot (the user's), on every ascent, custom runs and True Final Ascent's FINAL ASCENT too (not TFA's supply
/// drops, which are its own):
/// <list type="bullet">
/// <item>On average the whole Alpine (every suitcase opened) holds 4 Portable Stoves, 2 Lanterns, 7 Heat Packs, 4
/// Fortified Milks and 3 Torches, whatever the map: their weights in the Alpine's suitcases are worked out from that
/// map's own Alpine suitcases (how many, how many items each holds, ascent 10's lighter ones, no item twice in one
/// suitcase), with what the Explorer's suitcases there bring counted in.</item>
/// <item>The Explorer's suitcases in the Alpine hold the four too, each at its rarity's weight (as the game weighs any item
/// in a table); the Torch with them, as it turns up wherever the Lantern does (<see cref="MoreLanterns"/>).</item>
/// <item>The Ancient Luggage in the Alpine gives the Faerie Lantern and the Cure-All twice as often a pick (the rest of
/// that table keeps its odds against each other).</item>
/// </list>
/// The game's own tables stay as they are: while a suitcase in the Alpine picks, its table is the Alpine's copy.
/// </summary>
internal static class AlpineLoot
{
    internal const string StovePrefab = "PortableStovetopItem";
    internal const string HeatPackPrefab = "Heat Pack";
    internal const string MilkPrefab = "FortifiedMilk";
    internal const string FaerieLanternPrefab = "Lantern_Faerie";
    internal const string CureAllPrefab = "Cure-All";

    /// <summary>The average count of each in the whole Alpine (the user's).</summary>
    internal static readonly (string prefab, float count)[] Targets =
    {
        (StovePrefab, 4f),
        (MoreLanterns.LanternPrefab, 2f),
        (HeatPackPrefab, 7f),
        (MilkPrefab, 4f),
        (TorchItem.PrefabName, 3f),
    };

    /// <summary>What the Explorer's suitcases in the Alpine hold besides the game's, at their rarity's weight (the user's four).</summary>
    internal static readonly string[] InExplorers = { StovePrefab, MoreLanterns.LanternPrefab, HeatPackPrefab, MilkPrefab };

    /// <summary>The Faerie Lantern's and the Cure-All's share of an Ancient Luggage's pick in the Alpine, against the game's.</summary>
    internal const int AncientFactor = 2;

    /// <summary>The Alpine's suitcase table is taken this many times over, so that the four's worked-out weights are whole.</summary>
    private const int Scale = 100;

    private const int SolveRounds = 80;

    /// <summary>The spawner now picking what it spawns (the game picks inside <see cref="Spawner.GetObjectsToSpawn"/>).</summary>
    private static Spawner picking;

    private static string cachedKey;
    private static Dictionary<SpawnPool, Dictionary<ushort, int>> cachedTables;

    /// <summary>For the tests: what the last working-out expected in the whole Alpine, by prefab, and the weights it gave.</summary>
    internal static readonly Dictionary<string, float> Expected = new Dictionary<string, float>();
    internal static readonly Dictionary<string, int> Weights = new Dictionary<string, int>();
    internal static int Solves;

    /// <summary>The spawner stands in the Alpine: under the Alpine's biome (without a biome above it, on the Alpine's ground).</summary>
    internal static bool InAlpine(Spawner spawner)
    {
        if (spawner == null)
        {
            return false;
        }
        Biome biome = spawner.GetComponentInParent<Biome>(true);
        if (biome != null)
        {
            return biome.biomeType == Biome.BiomeType.Alpine;
        }
        return Map.TryGetGroundBiome(spawner.transform.position, out Biome.BiomeType ground) && ground == Biome.BiomeType.Alpine;
    }

    /// <summary>The Alpine's own copy of a table, for a spawner in the Alpine; null where the Alpine keeps the game's.</summary>
    internal static Dictionary<ushort, int> TableFor(Spawner spawner, SpawnPool pool)
    {
        if (pool != SpawnPool.LuggageTundra && pool != SpawnPool.LuggageClimber && pool != SpawnPool.LuggageCursed)
        {
            return null;
        }
        if (!(spawner is Luggage) || spawner is RespawnChest || !InAlpine(spawner) || Compat.Tfa.Spawned(spawner))
        {
            return null;
        }
        Dictionary<SpawnPool, Dictionary<ushort, int>> tables = Tables();
        return tables != null && tables.TryGetValue(pool, out Dictionary<ushort, int> table) ? table : null;
    }

    /// <summary>The Alpine's tables for this map and room, worked out again when the game's tables or what may spawn change.</summary>
    internal static Dictionary<SpawnPool, Dictionary<ushort, int>> Tables()
    {
        if (LootData.AllSpawnWeightData == null)
        {
            return null;
        }
        Dictionary<ushort, int> tundra = LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageTundra, out Dictionary<ushort, int> t) ? t : null;
        Dictionary<ushort, int> climber = LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageClimber, out Dictionary<ushort, int> c) ? c : null;
        Dictionary<ushort, int> cursed = LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageCursed, out Dictionary<ushort, int> a) ? a : null;
        string key = $"{SceneManager.GetActiveScene().handle}|{Rules.Enabled(Lvl.LightLuggage)}|{Signature(tundra)}|{Signature(climber)}|{Signature(cursed)}";
        if (key == cachedKey && cachedTables != null)
        {
            return cachedTables;
        }
        cachedKey = key;
        cachedTables = new Dictionary<SpawnPool, Dictionary<ushort, int>>();
        if (climber != null)
        {
            cachedTables[SpawnPool.LuggageClimber] = ExplorersTable(climber, tundra);
        }
        if (cursed != null)
        {
            cachedTables[SpawnPool.LuggageCursed] = AncientTable(cursed);
        }
        if (tundra != null)
        {
            cachedTables[SpawnPool.LuggageTundra] = SuitcaseTable(tundra, cachedTables);
        }
        return cachedTables;
    }

    /// <summary>A table's items, weights and which of them may spawn now (the game drops the others before a pick).</summary>
    private static string Signature(Dictionary<ushort, int> table)
    {
        if (table == null)
        {
            return "-";
        }
        StringBuilder text = new StringBuilder();
        foreach (KeyValuePair<ushort, int> entry in table.OrderBy(e => e.Key))
        {
            text.Append(entry.Key).Append(':').Append(entry.Value).Append(Valid(entry.Key) ? '+' : '-').Append(',');
        }
        return text.ToString();
    }

    private static bool Valid(ushort id) => ItemDatabase.TryGetItem(id, out Item item) && item != null && item.IsValidToSpawn();

    private static bool TryId(string prefab, out ushort id)
    {
        id = 0;
        if (!ItemDatabase.TryGetItem(prefab, out Item item) || item == null)
        {
            return false;
        }
        id = item.itemID;
        return true;
    }

    private static int RarityWeight(ushort id) => ItemDatabase.TryGetItem(id, out Item item) && item != null && item.TryGetComponent(out LootData loot) ? LootData.RarityWeights[loot.Rarity] : 0;

    /// <summary>The Explorer's suitcases' table in the Alpine: the game's, with the four (and the Torch beside the Lantern) at their rarity's weight.</summary>
    private static Dictionary<ushort, int> ExplorersTable(Dictionary<ushort, int> climber, Dictionary<ushort, int> tundra)
    {
        Dictionary<ushort, int> table = new Dictionary<ushort, int>(climber);
        foreach (string prefab in InExplorers)
        {
            if (TryId(prefab, out ushort id) && !table.ContainsKey(id))
            {
                table[id] = RarityWeight(id);
            }
        }
        // The Torch goes wherever the Lantern does (only where the climb has it: the Alpine's suitcases hold it then).
        Item torch = TorchItem.Template;
        if (torch != null && tundra != null && tundra.ContainsKey(torch.itemID) && !table.ContainsKey(torch.itemID))
        {
            table[torch.itemID] = MoreLanterns.TorchWeight;
        }
        return table;
    }

    /// <summary>
    /// The Ancient Luggage's table in the Alpine: the Faerie Lantern's and the Cure-All's share of a pick twice the game's,
    /// in whole weights (the rest taken as many times over as needed, so their odds against each other stay).
    /// </summary>
    private static Dictionary<ushort, int> AncientTable(Dictionary<ushort, int> cursed)
    {
        Dictionary<ushort, int> table = new Dictionary<ushort, int>(cursed);
        List<ushort> doubled = new List<ushort>();
        foreach (string prefab in new[] { FaerieLanternPrefab, CureAllPrefab })
        {
            if (TryId(prefab, out ushort id) && table.ContainsKey(id) && Valid(id))
            {
                doubled.Add(id);
            }
        }
        if (doubled.Count == 0)
        {
            return table;
        }
        // Of what may spawn: the rest R and theirs D; each of theirs w becomes AncientFactor * w * R, the rest R - (F-1) * D times over.
        long rest = table.Where(e => !doubled.Contains(e.Key) && Valid(e.Key)).Sum(e => (long)e.Value);
        long theirs = doubled.Sum(id => (long)table[id]);
        long times = rest - (AncientFactor - 1) * theirs;
        if (rest <= 0 || times <= 0)
        {
            // Their share would pass the whole table: as near as it goes, only they.
            foreach (ushort id in table.Keys.ToList())
            {
                if (!doubled.Contains(id))
                {
                    table[id] = 0;
                }
            }
            return table;
        }
        long divisor = Gcd(times, AncientFactor * rest);
        long others = times / divisor;
        long mine = AncientFactor * rest / divisor;
        foreach (ushort id in table.Keys.ToList())
        {
            table[id] = (int)(table[id] * (doubled.Contains(id) ? mine : others));
        }
        return table;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a == 0 ? 1 : a;
    }

    /// <summary>One kind of spawner in the Alpine: how many (times its chance to spawn), its table, its picks.</summary>
    private sealed class Kind
    {
        internal float count;
        internal SpawnPool pool;
        internal bool repeats;
        internal bool lighter;
        internal List<(float share, int picks)> spots;
    }

    /// <summary>The kinds of spawners in the Alpine that draw from a table (the scout statues apart).</summary>
    private static List<Kind> AlpineKinds()
    {
        Dictionary<string, Kind> kinds = new Dictionary<string, Kind>();
        foreach (Spawner spawner in Object.FindObjectsByType<Spawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (spawner == null || !spawner.gameObject.scene.IsValid() || spawner is RespawnChest || Compat.Tfa.Spawned(spawner) || !InAlpine(spawner))
            {
                continue;
            }
            SpawnPool pool = spawner.GetSpawnPool();
            if (pool == SpawnPool.None || spawner.spawnMode == Spawner.SpawnMode.SingleItem)
            {
                continue;
            }
            float chance = spawner is Luggage ? 1f : spawner.spawnOnStart && spawner.belowAscentRequirement == -1 ? spawner.baseSpawnChance : 0f;
            if (chance <= 0f)
            {
                continue;
            }
            List<(float share, int picks)> spots = new List<(float, int)>();
            if (spawner.spawnPointMode == Spawner.SpawnPointMode.WeightedLists)
            {
                float total = spawner.weightedSpawnSpots.Sum(e => Mathf.Max(0, e.weight));
                foreach (Spawner.WeightedSpawnPointEntry entry in spawner.weightedSpawnSpots)
                {
                    if (entry.weight > 0 && total > 0f)
                    {
                        spots.Add((entry.weight / total, entry.spawnSpots?.Count ?? 0));
                    }
                }
            }
            else
            {
                spots.Add((1f, spawner.spawnSpots?.Count ?? 0));
            }
            bool lighter = A10_LightLuggage.Lightens(spawner);
            string key = $"{pool}|{spawner.canRepeatSpawns}|{lighter}|{chance}|{string.Join(",", spots.Select(s => $"{s.share:0.####}x{s.picks}"))}";
            if (!kinds.TryGetValue(key, out Kind kind))
            {
                kinds[key] = kind = new Kind { pool = pool, repeats = spawner.canRepeatSpawns, lighter = lighter, spots = spots };
            }
            kind.count += chance;
        }
        return kinds.Values.ToList();
    }

    /// <summary>
    /// The Alpine's suitcase table: the game's taken <see cref="Scale"/> times over, with the four's weights worked out so
    /// the whole Alpine holds the targets on average.
    /// </summary>
    private static Dictionary<ushort, int> SuitcaseTable(Dictionary<ushort, int> tundra, Dictionary<SpawnPool, Dictionary<ushort, int>> alpine)
    {
        Dictionary<ushort, int> table = tundra.ToDictionary(e => e.Key, e => e.Value * Scale);
        List<(ushort id, string prefab, float target)> wanted = new List<(ushort, string, float)>();
        foreach ((string prefab, float count) in Targets)
        {
            if (TryId(prefab, out ushort id) && tundra.ContainsKey(id) && Valid(id))
            {
                wanted.Add((id, prefab, count));
            }
        }
        List<Kind> kinds = AlpineKinds();
        Solves++;
        Expected.Clear();
        Weights.Clear();
        List<Kind> mine = kinds.Where(k => k.pool == SpawnPool.LuggageTundra).ToList();
        if (wanted.Count == 0 || mine.Count == 0)
        {
            return table;
        }

        // What the other spawners in the Alpine bring of the four (the Explorer's suitcases above all), at their tables.
        float[] fixedCounts = new float[wanted.Count];
        foreach (Kind kind in kinds.Where(k => k.pool != SpawnPool.LuggageTundra))
        {
            Dictionary<ushort, int> other = alpine.TryGetValue(kind.pool, out Dictionary<ushort, int> own) ? own
                : LootData.AllSpawnWeightData.TryGetValue(kind.pool, out Dictionary<ushort, int> game) ? game : null;
            if (other == null)
            {
                continue;
            }
            Odds odds = new Odds(other);
            for (int i = 0; i < wanted.Count; i++)
            {
                fixedCounts[i] += Count(kind, odds, wanted[i].id);
            }
        }

        // The four's weights (in the game's units), from the game's, until the Alpine holds the targets.
        float[] weights = wanted.Select(w => (float)tundra[w.id]).ToArray();
        float[] got = new float[wanted.Count];
        for (int round = 0; round < SolveRounds; round++)
        {
            Odds odds = new Odds(tundra, wanted.Select(w => w.id).ToArray(), weights);
            float worst = 0f;
            for (int i = 0; i < wanted.Count; i++)
            {
                got[i] = mine.Sum(k => Count(k, odds, wanted[i].id));
            }
            for (int i = 0; i < wanted.Count; i++)
            {
                float target = Mathf.Max(0.001f, wanted[i].target - fixedCounts[i]);
                if (got[i] <= 0f)
                {
                    continue;
                }
                float ratio = target / got[i];
                worst = Mathf.Max(worst, Mathf.Abs(ratio - 1f));
                weights[i] = Mathf.Clamp(weights[i] * Mathf.Pow(ratio, 0.85f), 0.01f, 1e5f);
            }
            if (worst < 0.0005f)
            {
                break;
            }
        }
        for (int i = 0; i < wanted.Count; i++)
        {
            table[wanted[i].id] = Mathf.Max(1, Mathf.RoundToInt(weights[i] * Scale));
            Weights[wanted[i].prefab] = table[wanted[i].id];
            Expected[wanted[i].prefab] = got[i] + fixedCounts[i];
        }
        Plugin.Debug($"Alpine loot: {mine.Sum(k => k.count):0} suitcases drawing from the Alpine's table, the others bringing "
            + string.Join(", ", wanted.Select((w, i) => $"{w.prefab} {fixedCounts[i]:0.00}")) + "; weights (x" + Scale + ") "
            + string.Join(", ", wanted.Select(w => $"{w.prefab} {table[w.id]}")) + "; expected " + string.Join(", ", wanted.Select((w, i) => $"{w.prefab} {Expected[w.prefab]:0.00}")) + ".");
        return table;
    }

    /// <summary>How many of the item a kind of spawner holds on average, all of that kind together.</summary>
    private static float Count(Kind kind, Odds odds, ushort id)
    {
        float each = 0f;
        foreach ((float share, int picks) in kind.spots)
        {
            if (picks <= 0)
            {
                continue;
            }
            float held = kind.lighter && picks >= 2 ? 0.5f * odds.Held(id, 1, kind.repeats) + 0.5f * odds.Held(id, picks, kind.repeats) : odds.Held(id, picks, kind.repeats);
            each += share * held;
        }
        return kind.count * each;
    }

    /// <summary>A table of what may spawn, for working out how many of an item some picks give.</summary>
    private sealed class Odds
    {
        private readonly ushort[] ids;
        private readonly double[] weights;

        internal Odds(Dictionary<ushort, int> table, ushort[] changed = null, float[] changedWeights = null)
        {
            List<ushort> keep = table.Keys.Where(Valid).ToList();
            ids = keep.ToArray();
            weights = new double[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                int at = changed != null ? System.Array.IndexOf(changed, ids[i]) : -1;
                weights[i] = at >= 0 ? changedWeights[at] : table[ids[i]];
            }
        }

        /// <summary>How many of the item so many picks give on average (the game's picks: none twice unless it repeats).</summary>
        internal float Held(ushort id, int picks, bool repeats)
        {
            int at = System.Array.IndexOf(ids, id);
            if (at < 0)
            {
                return 0f;
            }
            double total = weights.Sum();
            if (total <= 0)
            {
                return 0f;
            }
            if (repeats)
            {
                return (float)(picks * weights[at] / total);
            }
            bool[] gone = new bool[ids.Length];
            int exact = Mathf.Min(picks, 3);
            double held = Without(at, exact, gone, total);
            if (picks > exact)
            {
                held *= picks / (double)exact;
            }
            return (float)held;
        }

        private double Without(int at, int picks, bool[] gone, double total)
        {
            if (picks <= 0 || total <= 0)
            {
                return 0;
            }
            double held = weights[at] / total;
            if (picks == 1)
            {
                return held;
            }
            for (int j = 0; j < ids.Length; j++)
            {
                if (j == at || gone[j] || weights[j] <= 0)
                {
                    continue;
                }
                gone[j] = true;
                held += weights[j] / total * Without(at, picks - 1, gone, total - weights[j]);
                gone[j] = false;
            }
            return held;
        }
    }

    /// <summary>The spawner picking, for the table swap below.</summary>
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.GetObjectsToSpawn))]
    private static class Picking
    {
        private static void Prefix(Spawner __instance)
        {
            picking = __instance;
        }

        private static void Finalizer()
        {
            picking = null;
        }
    }

    /// <summary>While a suitcase in the Alpine picks, its table is the Alpine's (after the mod's other prefixes have set the tables).</summary>
    [HarmonyPatch(typeof(LootData), nameof(LootData.GetRandomItems))]
    private static class AlpineTables
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(SpawnPool spawnPool, out (Dictionary<SpawnPool, Dictionary<ushort, int>> all, Dictionary<ushort, int> game, Dictionary<ushort, int> alpine) __state)
        {
            __state = default;
            if (picking == null || LootData.AllSpawnWeightData == null || RunSettings.GetValue(RunSettings.SETTINGTYPE.ChaosItems) == 1)
            {
                return;
            }
            Dictionary<ushort, int> alpine = TableFor(picking, spawnPool);
            if (alpine == null || !LootData.AllSpawnWeightData.TryGetValue(spawnPool, out Dictionary<ushort, int> game))
            {
                return;
            }
            __state = (LootData.AllSpawnWeightData, game, alpine);
            LootData.AllSpawnWeightData[spawnPool] = alpine;
        }

        private static void Finalizer(SpawnPool spawnPool, (Dictionary<SpawnPool, Dictionary<ushort, int>> all, Dictionary<ushort, int> game, Dictionary<ushort, int> alpine) __state)
        {
            // Put back only into the same tables (a rebuild meanwhile made fresh ones).
            if (__state.all != null && __state.all == LootData.AllSpawnWeightData && __state.all.TryGetValue(spawnPool, out Dictionary<ushort, int> now) && now == __state.alpine)
            {
                __state.all[spawnPool] = __state.game;
            }
        }
    }
}
