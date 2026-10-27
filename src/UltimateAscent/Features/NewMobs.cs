using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The mod's new creatures: the beach's crabs (<see cref="Crabs"/>), the Tropics' snakes (<see cref="Snakes"/>) and the
/// Alps' mountain goats (<see cref="Goats"/>). With the config's "New Mobs" on (the host's) they live in regular runs on
/// every ascent; custom runs have a switch for each among the Custom Run screen's threats, in its biome's group (on
/// any ascent the screen picks). Below ascent 17 (with its rule off, and in custom runs without a picked ascent, where
/// the ascents' rules are off) all they do to a scout is half
/// as strong: half the injury, poison and knockback, knocked down or paralysed half as long; and snakes chase a third
/// slower. Seven in ten of the crabs, the ground snakes and the goats live on the climb itself, the rest on the way to
/// it (see <see cref="Place"/>).
/// </summary>
internal static class NewMobs
{
    /// <summary>Their switches among the Custom Run screen's threats (see <see cref="CustomRunThreats"/>).</summary>
    internal static RunSettings.SETTINGTYPE CrabsThreat => CustomRunThreats.Threats[0].setting;
    internal static RunSettings.SETTINGTYPE SnakesThreat => CustomRunThreats.Threats[1].setting;
    internal static RunSettings.SETTINGTYPE GoatsThreat => CustomRunThreats.Threats[2].setting;

    /// <summary>The config's switch lets them live in this climb: a regular run on any ascent, unless the host switched them off.</summary>
    internal static bool On => NewItems.RegularRun && HostOptions.NewMobsOn;

    /// <summary>This one lives in this climb: in a regular run with the config's switch on; in a custom run with its threat on.</summary>
    internal static bool Allowed(RunSettings.SETTINGTYPE threat)
    {
        if (!Plugin.Ready || !Rules.InRun)
        {
            return false;
        }
        return RunSettings.IsCustomRun ? RunSettings.GetValue(threat) == 1 : HostOptions.NewMobsOn;
    }

    /// <summary>In full from ascent 17 (hazards are more dangerous), weaker below it.</summary>
    private static bool Full => Rules.Enabled(Lvl.Hazards);

    /// <summary>How hard they hit: in full from ascent 17, half below it.</summary>
    internal static float Strength => Full ? 1f : 0.5f;

    /// <summary>How fast snakes chase: in full from ascent 17, two thirds below it.</summary>
    internal static float SnakeChase => Full ? 1f : 2f / 3f;

    /// <summary>
    /// Of the crabs, the ground snakes and the goats this share lives on the climb itself (the segment's wall, up to its
    /// campfire), the rest on the way to it (the segment's ground); the tree snakes live in their trees.
    /// </summary>
    internal const float ClimbShare = 0.7f;

    /// <summary>How many of so many live on the climb itself.</summary>
    internal static int OnClimb(int count) => Mathf.RoundToInt(count * ClimbShare);

    /// <summary>
    /// Whether a spot is on the climb itself: the map's terrain nearest to it (under it, or beside it within 10 m, as the
    /// cliff's face beside a rock on it) is a wall (the game's "Wall..." meshes: the segment's wall, its top at the
    /// campfire, the Alps' two walls, the props laid on them), not the ground the way to it runs over ("Ground").
    /// Anything else counts as what lies under it; false where neither is found.
    /// </summary>
    internal static bool OnTheClimb(Vector3 spot)
    {
        // The nearest of the map's terrain: below the spot, or beside it (a rock on a cliff has the cliff's face beside
        // it, the ground far below).
        float nearest = float.PositiveInfinity;
        int nearestKind = -1;
        RaycastHit[] hits = Physics.RaycastAll(spot + Vector3.up * 0.5f, Vector3.down, 80f, HelperFunctions.terrainMapMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            int kind = TerrainKind(hit.collider);
            if (kind >= 0)
            {
                nearest = Mathf.Max(0f, hit.distance - 0.5f);
                nearestKind = kind;
                break;
            }
        }
        for (int turn = 0; turn < 8; turn++)
        {
            Vector3 side = Quaternion.Euler(0f, turn * 45f, 0f) * Vector3.forward;
            foreach (RaycastHit hit in Physics.RaycastAll(spot + Vector3.up * 0.5f, side, Mathf.Min(nearest, BesideReach), HelperFunctions.terrainMapMask))
            {
                int kind = TerrainKind(hit.collider);
                if (kind >= 0 && hit.distance < nearest)
                {
                    nearest = hit.distance;
                    nearestKind = kind;
                }
            }
        }
        return nearestKind == 1;
    }

    /// <summary>How far beside a spot the map's terrain is looked for (see <see cref="OnTheClimb"/>).</summary>
    private const float BesideReach = 10f;

    /// <summary>
    /// Whether a spot is under the map: on ground buried under the map's surface, inside the mountain, or below a gap
    /// between the map's pieces (a ray from above can fall through one). Looked at from the spot up into the sky,
    /// straight up and slanting up all round, the map's surface seen from below: its upper side, facing away. Ground
    /// under an overhang or in a cave is not under the map (its roof faces down at it, or the sky shows).
    /// </summary>
    internal static bool UnderTheMap(Vector3 spot) => UnderTheMap(spot, Vector3.up);

    /// <summary>
    /// The same, looked at from a little way out from the surface the spot is on (this way out: up from ground, out from a
    /// cliff's face); with only the map's own ground and walls seen (not its rocks and trees, whose open undersides an
    /// open-air spot under them sees from below too), for a mob found there to have fallen through the map.
    /// </summary>
    internal static bool UnderTheMap(Vector3 spot, Vector3 outward, bool terrainOnly = false)
    {
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            int under = 0;
            Vector3 from = spot + outward.normalized * 0.3f;
            foreach (Vector3 look in SkyLooks)
            {
                if (Physics.Raycast(from, look, out RaycastHit hit, 400f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)
                    && Vector3.Dot(hit.normal, look) > 0f && (!terrainOnly || IsTerrain(hit.collider)))
                {
                    under++;
                }
            }
            return under >= UnderLooks;
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
    }

    /// <summary>The looks up from a spot: straight up, and all round 30 and 60 degrees from it; this many seeing the map from below is under it.</summary>
    private static readonly Vector3[] SkyLooks = MakeSkyLooks();
    private const int UnderLooks = 5;

    private static Vector3[] MakeSkyLooks()
    {
        List<Vector3> looks = new List<Vector3> { Vector3.up };
        foreach (float tilt in new[] { 30f, 60f })
        {
            for (int turn = 0; turn < 6; turn++)
            {
                looks.Add(Quaternion.Euler(0f, turn * 60f + tilt, 0f) * Quaternion.Euler(tilt, 0f, 0f) * Vector3.up);
            }
        }
        return looks.ToArray();
    }

    /// <summary>For the tests and the log: the new mobs found under the map (fallen through a gap) and put back at their spot.</summary>
    internal static int PutBack;

    /// <summary>The map's own ground or wall (its terrain), not a rock, a log, a tree or anything else laid on it.</summary>
    internal static bool IsTerrain(Collider collider) => collider != null && TerrainKind(collider) >= 0;

    private static readonly Dictionary<int, int> terrainKinds = new Dictionary<int, int>();

    /// <summary>The terrain a collider is part of: 1 a wall, 0 the ground, -1 neither (a prop, a tree, a test block). Remembered per collider.</summary>
    private static int TerrainKind(Collider collider)
    {
        int id = collider.GetInstanceID();
        if (terrainKinds.TryGetValue(id, out int kind))
        {
            return kind;
        }
        kind = -1;
        for (Transform part = collider.transform; part != null && part.GetComponent<Biome>() == null; part = part.parent)
        {
            if (part.name.StartsWith("Wall", System.StringComparison.Ordinal))
            {
                kind = 1;
                break;
            }
            if (part.name == "Ground" || part.name == "GroundMesh")
            {
                kind = 0;
                break;
            }
        }
        if (terrainKinds.Count > 8192)
        {
            terrainKinds.Clear();
        }
        terrainKinds[id] = kind;
        return kind;
    }

    /// <summary>
    /// So many spots out of those found along a part of the map's way (each with how far along it lies): the climb's
    /// share of them on the climb itself, spread all the way up it (see <see cref="Split"/>), the rest on the way to it,
    /// spread along it, all this far apart; where one part has too little room, the other takes the rest.
    /// </summary>
    internal static List<Vector3> Place(List<(Vector3 spot, float share)> found, int count, float apart, out int onClimb)
    {
        Split(found, out List<(Vector3 spot, float share)> climb, out List<(Vector3 spot, float share)> way);
        List<Vector3> taken = new List<Vector3>();
        List<Vector3> high = Spread(climb, OnClimb(count), apart, taken);
        List<Vector3> low = Spread(way, count - high.Count, apart, taken);
        high.AddRange(Spread(climb, count - high.Count - low.Count, apart, taken));
        onClimb = high.Count;
        LastClimbPlaced = high.Select(h => h.y).ToList();
        high.AddRange(low);
        return high;
    }

    /// <summary>
    /// The spots found, split into those on the climb itself and those on the way to it (see <see cref="OnTheClimb"/>).
    /// The climb's are measured by their height, so that spread (see <see cref="Spread"/>) they go all the way up it, from
    /// its foot through halfway up to its top; the way's by how far along the way they lie.
    /// </summary>
    internal static void Split(List<(Vector3 spot, float share)> found, out List<(Vector3 spot, float share)> climb, out List<(Vector3 spot, float share)> way)
    {
        climb = new List<(Vector3, float)>();
        way = new List<(Vector3, float)>();
        foreach ((Vector3 spot, float share) f in found)
        {
            if (OnTheClimb(f.spot))
            {
                climb.Add((f.spot, f.spot.y));
            }
            else
            {
                way.Add(f);
            }
        }
        LastClimb = climb.Count > 0 ? (climb.Min(c => c.share), climb.Max(c => c.share)) : (0f, 0f);
        int[] bins = new int[10];
        float span = Mathf.Max(LastClimb.high - LastClimb.low, 1e-3f);
        foreach ((Vector3 spot, float height) c in climb)
        {
            bins[Mathf.Clamp((int)((c.height - LastClimb.low) / span * 10f), 0, 9)]++;
        }
        LastClimbSpots = string.Join(" ", bins);
    }

    /// <summary>For the tests: the heights of the lowest and the highest spot found on the climb, and how many were found in each tenth of its height, the last time spots were split.</summary>
    internal static (float low, float high) LastClimb;
    internal static string LastClimbSpots = "";

    /// <summary>For the tests: the heights of the spots put on the climb, the last time spots were placed (see <see cref="Place"/>).</summary>
    internal static List<float> LastClimbPlaced = new List<float>();

    /// <summary>
    /// Spots out of those found along a stretch of the way (each with how far along it lies): this many, one at each such
    /// share of the stretch they cover (the first near its start, the last near its end), each at least this far from
    /// those already taken (and added to them), never under the map (see <see cref="UnderTheMap"/>; such a spot is
    /// dropped and another taken). Fewer where there is no more room.
    /// </summary>
    internal static List<Vector3> Spread(List<(Vector3 spot, float share)> found, int count, float apart, List<Vector3> taken)
    {
        List<Vector3> picked = new List<Vector3>();
        if (found.Count == 0 || count <= 0)
        {
            return picked;
        }
        found = new List<(Vector3 spot, float share)>(found);
        float from = found.Min(f => f.share);
        float length = Mathf.Max(found.Max(f => f.share) - from, 1e-3f);
        for (int c = 0; c < count; c++)
        {
            float wanted = from + (c + 0.5f) / count * length;
            float band = 0.5f / count * length;
            while (true)
            {
                List<(Vector3 spot, float share)> open = found.Where(f => taken.All(t => Goats.Flat(t, f.spot) >= apart)).ToList();
                List<(Vector3 spot, float share)> inBand = open.Where(f => Mathf.Abs(f.share - wanted) <= band).ToList();
                List<(Vector3 spot, float share)> pick = inBand.Count > 0 ? inBand : open.OrderBy(f => Mathf.Abs(f.share - wanted)).Take(5).ToList();
                if (pick.Count == 0)
                {
                    return picked;
                }
                (Vector3 spot, float share) chosen = pick[Random.Range(0, pick.Count)];
                if (UnderTheMap(chosen.spot))
                {
                    UnderMapSpots++;
                    found.Remove(chosen);
                    continue;
                }
                picked.Add(chosen.spot);
                taken.Add(chosen.spot);
                break;
            }
        }
        return picked;
    }

    /// <summary>For the tests and the log: spots found for the new mobs that lay under the map, and were passed over.</summary>
    internal static int UnderMapSpots;
}
