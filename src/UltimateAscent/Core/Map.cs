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
/// segment and its biome variant. During True Final Ascent's FINAL ASCENT all biomes are stacked
/// into the vanilla segments (Roots reports Tropics, Mesa reports Alpine, Gloom and Citadel report
/// the Kiln), so there TFA's own stage tracking is used instead.
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
                return biome == Biome.BiomeType.Temple ? Place.Citadel : biome == Biome.BiomeType.Swamp ? Place.Gloom : Place.Kiln;
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

    /// <summary>The Gloom of a regular map, which takes the Caldera's place below the Kiln.</summary>
    internal static bool InCalderaGloom => InGloom && CurrentSegment == Segment.Caldera;

    /// <summary>The volcanic Kiln (not the Citadel).</summary>
    internal static bool InVolcanoKiln => CurrentPlace == Place.Kiln;

    internal static bool InCitadel => CurrentPlace == Place.Citadel;

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

    /// <summary>The biome a scene object belongs to (its <see cref="Biome"/> parent), if it has one.</summary>
    internal static bool TryGetBiome(Component component, out Biome.BiomeType biome)
    {
        Biome owner = component != null ? component.GetComponentInParent<Biome>(includeInactive: true) : null;
        biome = owner != null ? owner.biomeType : Biome.BiomeType.Shore;
        return owner != null;
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
    /// goes by the furthest point of the climb reached.
    /// </summary>
    internal static bool DayNightApplies
    {
        get
        {
            MountainProgressHandler progress = Singleton<MountainProgressHandler>.Instance;
            return progress != null && progress.maxProgressPointReached < 3;
        }
    }

    /// <summary>Night, where there is night at all (see <see cref="DayNightApplies"/>).</summary>
    internal static bool IsNight => DayNightApplies && DayNightManager.instance != null && DayNightManager.instance.isDay < 0.5f;

    /// <summary>Every segment below the Kiln: the part of the mountain with weather and the chasing fog.</summary>
    internal static bool InLowerMountain
    {
        get
        {
            if (!Ready)
            {
                return false;
            }
            int segment = (int)Singleton<MapHandler>.Instance.GetCurrentSegment();
            return segment >= 0 && segment < (int)Segment.TheKiln;
        }
    }
}
