using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Core;

/// <summary>Where on the mountain a climb currently is.</summary>
internal enum Place
{
    Shore,
    Tropics,
    Roots,
    Alpine,
    Mesa,
    Caldera,
    Kiln,
    Gloom,
    Citadel,
    Peak,
    Nadir,
    Unknown
}

/// <summary>
/// Safe questions about where the climb currently is. On regular maps the place comes from the
/// segment and its biome variant: the map's fourth biome holds either the Caldera and the Kiln (the
/// Volcano biome) or the Gloom and the Citadel (both the Swamp biome; the game has no separate
/// biome for the Citadel there). During True Final Ascent's FINAL ASCENT all biomes are stacked into
/// the vanilla segments (Roots reports Tropics, Mesa reports Alpine, Gloom and Citadel report the
/// Kiln), so there TFA's own stage tracking is used instead. Its Builder packs any chain of biomes into
/// the segments the same way (a segment there can hold several blocks, and any biome).
/// </summary>
internal static class Map
{
    internal static bool Ready
    {
        get
        {
            if (!MapHandler.Exists)
            {
                return false;
            }
            MapHandler map = Singleton<MapHandler>.Instance;
            return map.segments != null && map.currentSegment >= 0 && map.currentSegment < map.segments.Length && map.segments[map.currentSegment] != null;
        }
    }

    internal static Segment CurrentSegment => Ready ? Singleton<MapHandler>.Instance.GetCurrentSegment() : Segment.Beach;

    internal static Biome.BiomeType CurrentBiome => Ready ? Singleton<MapHandler>.Instance.GetCurrentBiome() : Biome.BiomeType.Shore;

    private static int placeFrame = -1;
    private static Place placeCached = Place.Unknown;

    /// <summary>Where the scouts are; computed at most once per frame.</summary>
    internal static Place CurrentPlace
    {
        get
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame != placeFrame)
            {
                placeFrame = frame;
                placeCached = ComputePlace();
            }
            return placeCached;
        }
    }

    private static Place ComputePlace()
    {
        if (!Ready)
        {
            return Place.Unknown;
        }
        if (Compat.Tfa.TryGetFinalAscentPlace(out Place tfaPlace))
        {
            return tfaPlace;
        }
        MapHandler map = Singleton<MapHandler>.Instance;
        Biome.BiomeType biome = map.GetCurrentBiome();
        switch (map.GetCurrentSegment())
        {
            case Segment.Beach:
                return Place.Shore;
            case Segment.Tropics:
                return biome == Biome.BiomeType.Roots ? Place.Roots : Place.Tropics;
            case Segment.Alpine:
                return biome == Biome.BiomeType.Mesa ? Place.Mesa : Place.Alpine;
            case Segment.Caldera:
                return biome == Biome.BiomeType.Swamp ? Place.Gloom : Place.Caldera;
            case Segment.TheKiln:
                // Above the Gloom, the Kiln's place is the Citadel's (still reported as the Swamp biome).
                return biome == Biome.BiomeType.Temple || biome == Biome.BiomeType.Swamp ? Place.Citadel : Place.Kiln;
            case Segment.Peak:
                return Place.Peak;
            case Segment.Void:
                return Place.Nadir;
            default:
                return Place.Unknown;
        }
    }

    /// <summary>The volcanic Caldera (not the Gloom).</summary>
    internal static bool InVolcanoCaldera => CurrentPlace == Place.Caldera;

    /// <summary>The Gloom, wherever the map puts it.</summary>
    internal static bool InGloom => CurrentPlace == Place.Gloom;

    /// <summary>The volcanic Kiln (not the Citadel).</summary>
    internal static bool InVolcanoKiln => CurrentPlace == Place.Kiln;

    internal static bool InCitadel => CurrentPlace == Place.Citadel;

    /// <summary>The Citadel's segment object in the game's map (under the Gloom's biome).</summary>
    private const string CitadelSegmentName = "Temple_Segment";

    /// <summary>
    /// A scene object that belongs to the Citadel: on a regular map it sits under the segment the
    /// game uses for the Kiln's place on a map whose fourth biome is the Gloom's (Swamp). True Final
    /// Ascent's FINAL ASCENT moves that segment elsewhere; there it is found by its name.
    /// </summary>
    internal static bool InCitadelSegment(Component component)
    {
        if (component == null)
        {
            return false;
        }
        if (Ready)
        {
            MapHandler map = Singleton<MapHandler>.Instance;
            int kiln = (int)Segment.TheKiln;
            if (kiln < map.segments.Length && map.segments[kiln] != null && map.segments[kiln].biome == Biome.BiomeType.Swamp)
            {
                GameObject citadel = map.segments[kiln].segmentParent;
                if (citadel != null && component.transform.IsChildOf(citadel.transform))
                {
                    return true;
                }
            }
        }
        for (Transform parent = component.transform; parent != null; parent = parent.parent)
        {
            if (parent.name == CitadelSegmentName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The volcanic Caldera or Kiln.</summary>
    internal static bool InVolcano
    {
        get
        {
            Place place = CurrentPlace;
            return place == Place.Caldera || place == Place.Kiln;
        }
    }

    internal static bool InAlpine => CurrentPlace == Place.Alpine;

    internal static bool InMesa => CurrentPlace == Place.Mesa;

    internal static bool InRoots => CurrentPlace == Place.Roots;

    /// <summary>
    /// The Nadir: the game's own flag for it, or the place (True Final Ascent's Builder makes its Nadir blocks out of a copy
    /// of the Nadir the game does not count as one).
    /// </summary>
    internal static bool InNadir => Ready && (Singleton<MapHandler>.Instance.inNadir || CurrentPlace == Place.Nadir);

    /// <summary>One of this mod's spawners under a part of the map's ground, which the game runs when it fills that part (once: a part that has one keeps it).</summary>
    internal static void AddSpawner<T>(GameObject part, string name) where T : MonoBehaviour
    {
        if (part == null || part.transform.Find(name) != null)
        {
            return;
        }
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(part.transform, false);
        holder.AddComponent<T>();
    }

    /// <summary>
    /// The way through a part of the map: from where the scouts come into it (the campfire before it, or where they wake for
    /// the first part) to its own campfire. The part is a segment's ground (<paramref name="segment"/> on a regular map),
    /// or with True Final Ascent's Builder the block it belongs to (repeated biomes each have their own; the last one ends
    /// at the peak).
    /// </summary>
    internal static bool WayThrough(Transform part, int segment, out Vector3 start, out Vector3 end)
    {
        start = end = Vector3.zero;
        GameObject from;
        GameObject to;
        bool first;
        Compat.Tfa.Stage stage = Compat.Tfa.StageOf(part);
        if (stage != null)
        {
            from = stage.Before;
            to = stage.Exit != null ? stage.Exit : stage.Campfire;
            first = stage.First;
        }
        else
        {
            MapHandler map = MapHandler.Exists ? Singleton<MapHandler>.Instance : null;
            if (map == null || map.segments == null || segment < 0 || segment >= map.segments.Length)
            {
                return false;
            }
            from = segment > 0 ? map.segments[segment - 1].segmentCampfire : null;
            to = map.segments[segment].segmentCampfire;
            first = segment == 0;
        }
        if (to != null)
        {
            end = to.transform.position;
        }
        else if (stage != null && Singleton<PeakHandler>.Instance != null)
        {
            // The Builder's last block has no campfire at its end: TFA puts the peak there.
            end = Singleton<PeakHandler>.Instance.transform.position;
        }
        else
        {
            return false;
        }
        if (!first)
        {
            if (from == null)
            {
                return false;
            }
            start = from.transform.position;
            return true;
        }
        List<SpawnPoint> points = SpawnPoint.allSpawnPoints.Where(p => p != null).ToList();
        if (points.Count == 0)
        {
            return false;
        }
        start = points.Aggregate(Vector3.zero, (sum, p) => sum + p.transform.position) / points.Count;
        return true;
    }

    /// <summary>The biome a scene object belongs to (its <see cref="Biome"/> parent), if it has one.</summary>
    internal static bool TryGetBiome(Component component, out Biome.BiomeType biome)
    {
        Biome owner = component != null ? component.GetComponentInParent<Biome>(includeInactive: true) : null;
        biome = owner != null ? owner.biomeType : Biome.BiomeType.Shore;
        return owner != null;
    }

    private static readonly Dictionary<int, int> colliderBiomes = new Dictionary<int, int>();

    /// <summary>
    /// Whether ground (a collider) is another biome's than the given one: true for ground under another
    /// <see cref="Biome"/>; false for that biome's own ground and for ground of no biome at all (the map's shared things).
    /// Remembered per collider.
    /// </summary>
    internal static bool IsOtherBiome(Collider ground, Biome.BiomeType own)
    {
        if (ground == null)
        {
            return false;
        }
        int id = ground.GetInstanceID();
        if (!colliderBiomes.TryGetValue(id, out int biome))
        {
            if (colliderBiomes.Count > 8192)
            {
                colliderBiomes.Clear();
            }
            biome = TryGetBiome(ground, out Biome.BiomeType found) ? (int)found : -1;
            colliderBiomes[id] = biome;
        }
        return biome >= 0 && biome != (int)own;
    }

    /// <summary>The biome of the ground below a point (up to 100 m down), if that ground belongs to one.</summary>
    internal static bool TryGetGroundBiome(Vector3 point, out Biome.BiomeType biome)
    {
        biome = Biome.BiomeType.Shore;
        RaycastHit hit = HelperFunctions.LineCheck(point + Vector3.up, point + Vector3.down * 100f, HelperFunctions.LayerType.TerrainMap);
        return hit.transform != null && TryGetBiome(hit.transform, out biome);
    }

    /// <summary>
    /// Day and night only matter below the Caldera and the Gloom; from there on (and in the Kiln, the
    /// Citadel and the Peak) there is no night for this mod's rules. Like the game's own night cold it
    /// goes by the furthest point of the climb reached, counted as the night cold counts it (with True
    /// Final Ascent's FINAL ASCENT: up to its Mesa's campfire, see <see cref="Compat.Tfa.ColdNightReached"/>).
    /// </summary>
    internal static bool DayNightApplies
    {
        get
        {
            MountainProgressHandler progress = Singleton<MountainProgressHandler>.Instance;
            return progress != null && Compat.Tfa.ColdNightReached(progress) < 3;
        }
    }

    /// <summary>Night, where there is night at all (see <see cref="DayNightApplies"/>).</summary>
    internal static bool IsNight => DayNightApplies && DayNightManager.instance != null && DayNightManager.instance.isDay < 0.5f;

    private static readonly HashSet<Collider> fogWalls = new HashSet<Collider>();
    private static MapHandler fogWallsOf;
    private static float fogWallsFound = float.NegativeInfinity;

    /// <summary>
    /// The fog walls between the segments: the game's (each segment's wall before and after it, a picture of fog with
    /// an invisible slab behind it, on the plain layer) and True Final Ascent's Builder copies of them between its blocks.
    /// Nothing there to see but fog, so nothing to stand on, hold or stop at.
    /// </summary>
    internal static bool IsFogWall(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }
        if (Compat.Tfa.BuilderWall(collider))
        {
            return true;
        }
        MapHandler map = MapHandler.Exists ? Singleton<MapHandler>.Instance : null;
        if (map != fogWallsOf || UnityEngine.Time.time - fogWallsFound > 10f)
        {
            fogWallsOf = map;
            fogWallsFound = UnityEngine.Time.time;
            fogWalls.Clear();
            if (map != null && map.segments != null)
            {
                foreach (MapHandler.MapSegment segment in map.segments)
                {
                    foreach (GameObject wall in segment != null ? new[] { segment.wallNext, segment.wallPrevious } : new GameObject[0])
                    {
                        if (wall != null)
                        {
                            fogWalls.UnionWith(wall.GetComponentsInChildren<Collider>(true));
                        }
                    }
                }
            }
        }
        return fogWalls.Contains(collider);
    }

    private static readonly RaycastHit[] fogWallHits = new RaycastHit[16];

    /// <summary>Whether the line between two spots passes into a fog wall (from either end).</summary>
    internal static bool ThroughFogWall(Vector3 from, Vector3 to)
    {
        Vector3 way = to - from;
        float length = way.magnitude;
        if (length < 1e-4f)
        {
            return false;
        }
        foreach ((Vector3 start, Vector3 direction) in new[] { (from, way / length), (to, -way / length) })
        {
            int count = Physics.RaycastNonAlloc(start, direction, fogWallHits, length, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (IsFogWall(fogWallHits[i].collider))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
