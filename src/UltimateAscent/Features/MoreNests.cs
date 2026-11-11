using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// More egg nests in the Caldera and the Gloom, on every ascent: the Caldera 2-3 times as many nests as the map has there,
/// the Gloom 14-17 more (the map's own stay as they are). Each new nest is a copy of one of that biome's own nests that
/// the game has filled with eggs this climb, picked at random, and it is always filled (as many eggs as its template may
/// hold; at least one). The host picks the places right after the game has filled the biome's nests as its segment loads,
/// and every player's game puts the same nests there (a room property, so players joining later get them too).
/// The Caldera: near the tops of the rock islands standing out of the lava, at least 18 m above the lava (above the
/// highest the tide rises) and below the tall lava pillars where the condors nest (not on those, nor within 30 m of a
/// condor's nest). The Gloom: on hills rising out of the Gloom's sleepy fog, at least 2 m above its top. Nowhere near a
/// campfire, under an overhang, or on ruins, the Citadel, the Kiln or the map's border walls; apart from every other nest.
/// Where a map has fewer such places than nests wanted, it gets as many as there are places.
/// </summary>
internal static class MoreNests
{
    internal const string RoomKey = Net.Prefix + "nests";
    private const string HolderName = "UltimateAscent.MoreNests";

    /// <summary>The Caldera: this many times as many nests as the map has there, in all.</summary>
    internal const float LeastTimes = 2f;
    internal const float MostTimes = 3f;

    /// <summary>The Gloom: this many new nests.</summary>
    internal const int GloomLeast = 14;
    internal const int GloomMost = 17;
    private const float Step = 2f;

    /// <summary>The Caldera: how high above the lava a new nest may sit (the tide rises up to 14 m, see <see cref="CalderaTide"/>).</summary>
    internal const float AboveLavaLeast = 18f;
    internal const float AboveLavaMost = 56f;

    /// <summary>The Gloom: how high above the top of the sleepy fog a new nest may sit.</summary>
    internal const float AboveFogLeast = 2f;
    internal const float AboveFogMost = 46f;

    internal const float CondorNestClearance = 30f;
    internal const float CampfireClearance = 25f;

    private sealed class Kind
    {
        internal Biome.BiomeType biome;
        internal string name;
        /// <summary>How far below the highest ground within 10 m a spot may lie (near its hill's top).</summary>
        internal float nearTop;
        internal float apart;
    }

    private static readonly Kind Caldera = new Kind { biome = Biome.BiomeType.Volcano, name = "Caldera", nearTop = 2.5f, apart = 25f };
    private static readonly Kind Gloom = new Kind { biome = Biome.BiomeType.Swamp, name = "Gloom", nearTop = 5f, apart = 10f };

    /// <summary>The biomes (by their place in <see cref="Biomes"/>) whose new nests are placed on this game.</summary>
    private static readonly HashSet<int> placed = new HashSet<int>();

    /// <summary>The biomes whose nests the game is filling now (host): their new nests are placed right after.</summary>
    private static readonly List<int> pending = new List<int>();

    /// <summary>The room property last built from.</summary>
    private static string built;

    /// <summary>For the tests: per biome, the map's nests, the nests wanted in all, the spots found, and the new nests (host).</summary>
    internal sealed class Report
    {
        internal Biome.BiomeType biome;
        internal int before;
        internal int wanted;
        internal int spots;
        internal float low;
        internal float high;
        internal float apart;
        /// <summary>The map's nests the game filled with eggs (the templates to pick from).</summary>
        internal int filled;
        internal readonly List<BerryBush> added = new List<BerryBush>();
        internal readonly List<BerryBush> templates = new List<BerryBush>();
    }

    internal static readonly List<Report> Reports = new List<Report>();

    /// <summary>For the tests: the new nests on this game (every player's).</summary>
    internal static readonly List<BerryBush> Built = new List<BerryBush>();

    internal static void Reset()
    {
        placed.Clear();
        pending.Clear();
        built = null;
        Reports.Clear();
        Built.Clear();
    }

    internal static bool IsNest(Spawner spawner) => spawner is BerryBush && spawner.spawnPool.HasFlag(SpawnPool.Nest);

    /// <summary>One of the new nests (not the map's own).</summary>
    internal static bool IsAdded(Component component) => component != null && component.GetComponentsInParent<Transform>(true).Any(t => t.name == HolderName);

    /// <summary>The Caldera's and the Gloom's biomes on this map (True Final Ascent's Builder may have several), in an order every game agrees on.</summary>
    private static List<Biome> Biomes() => Resources.FindObjectsOfTypeAll<Biome>()
        .Where(b => b != null && b.gameObject.scene.IsValid() && (b.biomeType == Biome.BiomeType.Volcano || b.biomeType == Biome.BiomeType.Swamp))
        .OrderBy(b => Key(b.transform.position)).ToList();

    /// <summary>A biome's own nests, in an order every game agrees on.</summary>
    private static List<BerryBush> OwnNests(Biome biome) => biome.GetComponentsInChildren<BerryBush>(true)
        .Where(n => IsNest(n) && !IsAdded(n)).OrderBy(n => Key(n.transform.position)).ToList();

    private static string Key(Vector3 p) => $"{Mathf.RoundToInt(p.x * 10f),9}{Mathf.RoundToInt(p.y * 10f),9}{Mathf.RoundToInt(p.z * 10f),9}";

    private static string RunTag => (Net.RunKey ?? "") + "|" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

    /// <summary>As the game fills a biome's first nest (host), its new nests are due: placed once it has filled them all (<see cref="Tick"/>).</summary>
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.TrySpawnItems))]
    private static class AsNestsFill
    {
        private static void Prefix(Spawner __instance)
        {
            if (!IsNest(__instance) || !PhotonNetwork.IsMasterClient || !Rules.InRun || IsAdded(__instance))
            {
                return;
            }
            Biome biome = __instance.GetComponentInParent<Biome>(true);
            if (biome == null || (biome.biomeType != Biome.BiomeType.Volcano && biome.biomeType != Biome.BiomeType.Swamp))
            {
                return;
            }
            List<Biome> biomes = Biomes();
            int index = biomes.IndexOf(biome);
            if (index < 0 || placed.Contains(index))
            {
                return;
            }
            placed.Add(index);
            if (Records().Any(r => r.biome == index))
            {
                // Placed already (by a host before this one): this game has built them from the room.
                return;
            }
            // The game fills all of the segment's nests in one go: the new ones are placed after (the next look).
            pending.Add(index);
        }
    }

    /// <summary>The host places the new nests of the biomes whose nests the game has filled.</summary>
    private static void PlacePending()
    {
        if (pending.Count == 0)
        {
            return;
        }
        List<int> due = new List<int>(pending);
        pending.Clear();
        if (!PhotonNetwork.IsMasterClient)
        {
            return;
        }
        List<Biome> biomes = Biomes();
        foreach (int index in due)
        {
            if (index < 0 || index >= biomes.Count)
            {
                continue;
            }
            try
            {
                Place(biomes[index], index);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"More nests: placing them failed: {ex}");
            }
        }
    }

    /// <summary>The nests holding eggs now (the game's eggs lie in their spots).</summary>
    private static List<int> Filled(List<BerryBush> own)
    {
        List<Vector3> eggs = Object.FindObjectsByType<Item>(FindObjectsSortMode.None)
            .Where(i => i != null && i.itemState == ItemState.Ground && i.gameObject.name.StartsWith("NestEgg")).Select(i => i.transform.position).ToList();
        List<int> filled = new List<int>();
        for (int i = 0; i < own.Count; i++)
        {
            Vector3 at = own[i].transform.position;
            if (eggs.Any(e => (e - at).sqrMagnitude < 1.5f * 1.5f))
            {
                filled.Add(i);
            }
        }
        return filled;
    }

    private struct Record
    {
        internal int biome;
        internal int template;
        internal Vector3 at;
        internal float yaw;
    }

    private static void Place(Biome biome, int index)
    {
        Kind kind = biome.biomeType == Biome.BiomeType.Volcano ? Caldera : Gloom;
        List<BerryBush> own = OwnNests(biome);
        if (own.Count == 0)
        {
            return;
        }
        Report report = new Report { biome = kind.biome, before = own.Count, apart = kind.apart };
        report.wanted = kind == Gloom ? own.Count + Random.Range(GloomLeast, GloomMost + 1) : Mathf.RoundToInt(own.Count * Random.Range(LeastTimes, MostTimes));
        Reports.Add(report);
        List<int> filled = Filled(own);
        report.filled = filled.Count;
        if (filled.Count == 0)
        {
            Plugin.Log.LogWarning($"More nests: none of the {kind.name}'s nests holds eggs; no new nests there.");
            return;
        }
        if (!Area(biome, kind, out Rect area, out float low, out float high, out float hillFoot))
        {
            Plugin.Log.LogWarning($"More nests: the {kind.name} has no lava or fog to go by; no new nests there.");
            return;
        }
        area = WithinEdgeWalls(area);
        report.low = low;
        report.high = high;
        Physics.SyncTransforms();
        List<Vector3> spots = Spots(biome, kind, area, low, high, hillFoot);
        report.spots = spots.Count;
        List<Vector3> taken = own.Select(n => n.transform.position).ToList();
        List<Record> records = new List<Record>();
        foreach (Vector3 spot in spots.OrderBy(_ => Random.value))
        {
            if (own.Count + records.Count >= report.wanted)
            {
                break;
            }
            if (taken.Any(t => Flat(t, spot) < kind.apart))
            {
                continue;
            }
            taken.Add(spot);
            records.Add(new Record { biome = index, template = filled[Random.Range(0, filled.Count)], at = spot, yaw = Random.Range(0f, 360f) });
        }
        Publish(records);
        MapHandler map = Singleton<MapHandler>.Instance;
        foreach (Record record in records)
        {
            BerryBush nest = Build(biome, own, record);
            if (nest == null)
            {
                continue;
            }
            report.added.Add(nest);
            report.templates.Add(own[record.template]);
            List<PhotonView> eggs = nest.TrySpawnItems();
            if (map != null && eggs != null)
            {
                map.viewsToDestoryIfNotAlreadyWhenSwitchingSegments.AddRange(eggs);
            }
        }
        Plugin.Log.LogInfo($"More nests: the {kind.name} has {own.Count} nests; {report.wanted} wanted, {records.Count} new ones put on {spots.Count} spots found ({low:F0}-{high:F0} m).");
    }

    /// <summary>
    /// Where new nests may go: the Caldera's lava (its river) and the Gloom's sleepy fog give the area (inside their edges)
    /// and the heights.
    /// </summary>
    private static bool Area(Biome biome, Kind kind, out Rect area, out float low, out float high, out float hillFoot)
    {
        area = default;
        low = high = hillFoot = 0f;
        const float Inside = 8f;
        if (kind == Caldera)
        {
            Lava river = biome.GetComponentsInChildren<Lava>(true).Where(l => l.GetComponentInParent<LavaRising>(true) == null)
                .Select(l => (lava: l, plane: l.GetComponentInChildren<MeshRenderer>(true))).Where(x => x.plane != null)
                .OrderByDescending(x => x.plane.bounds.size.x * x.plane.bounds.size.z).Select(x => x.lava).FirstOrDefault();
            if (river == null)
            {
                return false;
            }
            Bounds b = river.GetComponentInChildren<MeshRenderer>(true).bounds;
            area = Rect.MinMaxRect(b.min.x + Inside, b.min.z + Inside, b.max.x - Inside, b.max.z - Inside);
            float lava = b.max.y;
            low = lava + AboveLavaLeast;
            high = lava + AboveLavaMost;
            hillFoot = float.NaN;
            return true;
        }
        global::Peak.StatusFieldGloom fog = biome.GetComponentsInChildren<global::Peak.StatusFieldGloom>(true)
            .Where(f => f.GetComponentInParent<LavaRising>(true) == null).OrderByDescending(f => f.size.x * f.size.z).FirstOrDefault();
        if (fog == null)
        {
            return false;
        }
        Vector3 c = fog.transform.position;
        area = Rect.MinMaxRect(c.x - fog.size.x / 2f + Inside, c.z - fog.size.z / 2f + Inside, c.x + fog.size.x / 2f - Inside, c.z + fog.size.z / 2f - Inside);
        low = c.y + AboveFogLeast;
        high = c.y + AboveFogMost;
        // A hill rises out of the fog: its foot lies in it.
        hillFoot = c.y;
        return true;
    }

    /// <summary>
    /// The area cut to the map's edge walls (invisible, along both sides of the climb): the ground beyond them is the
    /// valley's outer cliffs, out of the scouts' way. Under the walls' caps it is still the climb (the game has nests there).
    /// </summary>
    private static Rect WithinEdgeWalls(Rect area)
    {
        int layer = LayerMask.NameToLayer("InvisWall");
        List<Bounds> walls = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
            .Where(c => c.gameObject.layer == layer && c.bounds.max.z > area.yMin && c.bounds.min.z < area.yMax && c.bounds.max.x > area.xMin - 100f && c.bounds.min.x < area.xMax + 100f)
            .Select(c => c.bounds).ToList();
        if (walls.Count == 0)
        {
            return area;
        }
        float left = walls.Min(b => b.min.x);
        float right = walls.Max(b => b.max.x);
        return Rect.MinMaxRect(Mathf.Max(area.xMin, left), area.yMin, Mathf.Min(area.xMax, right), area.yMax);
    }

    private struct Cell
    {
        internal bool hit;
        internal bool fit;
        internal float y;
        internal Vector3 point;
    }

    /// <summary>The spots near hill tops in the area, every 2 m, within the heights; open to the sky, room for a nest.</summary>
    private static List<Vector3> Spots(Biome biome, Kind kind, Rect area, float low, float high, float hillFoot)
    {
        int w = Mathf.Max(0, Mathf.FloorToInt(area.width / Step)) + 1;
        int h = Mathf.Max(0, Mathf.FloorToInt(area.height / Step)) + 1;
        Cell[,] cells = new Cell[w, h];
        float from = high + 80f;
        float length = high - low + 200f;
        for (int i = 0; i < w; i++)
        {
            for (int j = 0; j < h; j++)
            {
                Vector3 start = new Vector3(area.xMin + i * Step, from, area.yMin + j * Step);
                if (!Physics.Raycast(start, Vector3.down, out RaycastHit ground, length, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                Cell cell = new Cell { hit = true, y = ground.point.y, point = ground.point };
                cell.fit = ground.point.y >= low && ground.point.y <= high && ground.normal.y >= 0.9f && Natural(ground.collider, biome)
                    && !(Physics.Raycast(start, Vector3.down, out RaycastHit first, length, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore) && first.point.y > ground.point.y + 0.5f);
                cells[i, j] = cell;
            }
        }
        Vector3[] condorNests = biome.GetComponentsInChildren<Condor>(true).Where(c => c.nest != null).Select(c => c.nest.transform.position).ToArray();
        Vector3[] campfires = biome.GetComponentsInChildren<Campfire>(true).Select(c => c.transform.position)
            .Concat(Object.FindObjectsByType<Campfire>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(c => c.transform.position)).ToArray();
        int top = Mathf.RoundToInt(10f / Step);
        int foot = Mathf.RoundToInt(24f / Step);
        List<Vector3> spots = new List<Vector3>();
        for (int i = 0; i < w; i++)
        {
            for (int j = 0; j < h; j++)
            {
                Cell cell = cells[i, j];
                if (!cell.fit)
                {
                    continue;
                }
                float highest = Around(cells, i, j, top).Max();
                if (cell.y < highest - kind.nearTop)
                {
                    continue;
                }
                float lowest = Around(cells, i, j, foot).Min();
                // A hill: in the Caldera 10 m of drop around it; in the Gloom its foot in the fog.
                if (float.IsNaN(hillFoot) ? lowest > cell.y - 10f : lowest >= hillFoot)
                {
                    continue;
                }
                Vector3 p = cell.point;
                if (condorNests.Any(n => Flat(n, p) < CondorNestClearance) || campfires.Any(c => Flat(c, p) < CampfireClearance))
                {
                    continue;
                }
                if (Physics.Raycast(p + Vector3.up, Vector3.up, 30f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)
                    || Physics.CheckSphere(p + Vector3.up * 1.1f, 0.7f, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                spots.Add(p);
            }
        }
        return spots;
    }

    private static IEnumerable<float> Around(Cell[,] cells, int i, int j, int radius)
    {
        yield return cells[i, j].y;
        for (int di = -radius; di <= radius; di++)
        {
            for (int dj = -radius; dj <= radius; dj++)
            {
                int a = i + di, b = j + dj;
                if (di * di + dj * dj <= radius * radius && a >= 0 && b >= 0 && a < cells.GetLength(0) && b < cells.GetLength(1) && cells[a, b].hit)
                {
                    yield return cells[a, b].y;
                }
            }
        }
    }

    /// <summary>Rock or the biome's own ground: not ruins, the Kiln, the Citadel, the border walls, a tree's roots or a condor's lava pillar.</summary>
    private static bool Natural(Collider ground, Biome biome)
    {
        string name = ground.name;
        bool rock = name == "Cliff" || name == "SwampGround" || name.StartsWith("Rock") || name.StartsWith("Bl_Rock");
        if (!rock || ground.GetComponentInParent<Biome>(true) != biome)
        {
            return false;
        }
        for (Transform t = ground.transform; t != null; t = t.parent)
        {
            if (t.name.StartsWith("LavaPillar") || t.name == "UltraTall")
            {
                return false;
            }
        }
        return true;
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    /// <summary>The Gloom's floor area (its sleepy fog's, inside the edge walls) and the fog's top (for <see cref="GloomFood"/>).</summary>
    internal static bool GloomArea(Biome biome, out Rect area, out float fogTop)
    {
        fogTop = 0f;
        if (!Area(biome, Gloom, out area, out _, out _, out fogTop))
        {
            return false;
        }
        area = WithinEdgeWalls(area);
        return true;
    }

    /// <summary>Rock or the biome's own ground (see <see cref="Natural"/>).</summary>
    internal static bool NaturalGround(Collider ground, Biome biome) => Natural(ground, biome);

    /// <summary>A new nest: a copy of one of the biome's own (its look, its spots, how many eggs it holds and how often).</summary>
    private static BerryBush Build(Biome biome, List<BerryBush> own, Record record)
    {
        if (record.template < 0 || record.template >= own.Count)
        {
            return null;
        }
        BerryBush template = own[record.template];
        Transform holder = Holder(template);
        GameObject copy = Object.Instantiate(template.gameObject, holder);
        copy.name = template.gameObject.name;
        // The nest stands upright where the template stood upright on its ground.
        Quaternion upright = Quaternion.FromToRotation(template.transform.up, Vector3.up) * template.transform.rotation;
        copy.transform.SetPositionAndRotation(record.at, Quaternion.Euler(0f, record.yaw, 0f) * upright);
        // As big as the template (a condor pillar's nest takes its size from the scaled pillar it sits on).
        Vector3 size = template.transform.lossyScale;
        Vector3 under = holder.lossyScale;
        copy.transform.localScale = new Vector3(size.x / under.x, size.y / under.y, size.z / under.z);
        foreach (global::Peak.SpawnedItemTracker tracker in copy.GetComponentsInChildren<global::Peak.SpawnedItemTracker>(true))
        {
            Object.DestroyImmediate(tracker);
        }
        if (copy.GetComponentInChildren<MeshRenderer>(true) == null)
        {
            // The nest's look is beside its spawner (on a basalt column): it comes along.
            MeshRenderer look = template.transform.parent != null
                ? template.transform.parent.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.Contains("Nest")).OrderBy(r => (r.transform.position - template.transform.position).sqrMagnitude).FirstOrDefault()
                : null;
            if (look != null)
            {
                GameObject nestLook = Object.Instantiate(look.gameObject, copy.transform);
                nestLook.transform.localPosition = template.transform.InverseTransformPoint(look.transform.position);
                nestLook.transform.localRotation = Quaternion.Inverse(template.transform.rotation) * look.transform.rotation;
            }
        }
        BerryBush nest = copy.GetComponent<BerryBush>();
        nest._hasTriedToSpawn = false;
        nest._hasSuccessfullySpawned = false;
        // Always filled: at least one egg, as many as its template may hold.
        nest.baseSpawnChance = 1f;
        nest.possibleBerries = new Vector2(Mathf.Max(1f, template.possibleBerries.x), Mathf.Max(1f, template.possibleBerries.y));
        Built.Add(nest);
        return nest;
    }

    /// <summary>The new nests sit in the template's segment, so they show and hide with it.</summary>
    private static Transform Holder(BerryBush template)
    {
        Transform segment = template.transform;
        while (segment.parent != null && !segment.name.EndsWith("_Segment"))
        {
            segment = segment.parent;
        }
        if (!segment.name.EndsWith("_Segment"))
        {
            segment = template.transform.parent;
        }
        Transform holder = segment.Find(HolderName);
        if (holder == null)
        {
            holder = new GameObject(HolderName).transform;
            holder.SetParent(segment, false);
        }
        return holder;
    }

    // The room property: "run|scene" then one "biome,template,x,y,z,yaw" per nest.

    private static void Publish(List<Record> records)
    {
        List<Record> all = Records().Concat(records).ToList();
        StringBuilder text = new StringBuilder(RunTag);
        foreach (Record r in all)
        {
            text.Append(';').Append(r.biome).Append(',').Append(r.template).Append(',')
                .Append(r.at.x.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(r.at.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.at.z.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(r.yaw.ToString("R", CultureInfo.InvariantCulture));
        }
        string value = text.ToString();
        built = value;
        Net.SetRoom(RoomKey, value);
    }

    private static List<Record> Records() => Parse(Net.GetRoom(RoomKey) as string);

    private static List<Record> Parse(string value)
    {
        List<Record> records = new List<Record>();
        if (string.IsNullOrEmpty(value))
        {
            return records;
        }
        string[] parts = value.Split(';');
        if (parts[0] != RunTag)
        {
            return records;
        }
        for (int i = 1; i < parts.Length; i++)
        {
            string[] f = parts[i].Split(',');
            if (f.Length != 6)
            {
                continue;
            }
            float N(int k) => float.Parse(f[k], CultureInfo.InvariantCulture);
            records.Add(new Record { biome = int.Parse(f[0], CultureInfo.InvariantCulture), template = int.Parse(f[1], CultureInfo.InvariantCulture), at = new Vector3(N(2), N(3), N(4)), yaw = N(5) });
        }
        return records;
    }

    /// <summary>Every frame: a player's game builds the nests the host has placed (once each).</summary>
    internal static void Tick()
    {
        PlacePending();
        if (!Rules.InRun || !PhotonNetwork.InRoom)
        {
            return;
        }
        string value = Net.GetRoom(RoomKey) as string;
        if (value == null || value == built || !MapHandler.ExistsAndInitialized)
        {
            return;
        }
        List<Record> records = Parse(value);
        if (records.Count == 0)
        {
            return;
        }
        int already = Parse(built).Count;
        built = value;
        List<Biome> biomes = Biomes();
        foreach (Record record in records.Skip(already))
        {
            if (record.biome < 0 || record.biome >= biomes.Count)
            {
                continue;
            }
            Biome biome = biomes[record.biome];
            placed.Add(record.biome);
            Build(biome, OwnNests(biome), record);
        }
        Plugin.Debug($"More nests: {records.Count - already} new nests built from the host's places.");
    }
}
