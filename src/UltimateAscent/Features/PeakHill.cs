using System.Collections.Generic;
using System.Linq;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The peak's grassy hill, where the flowers grow: the ground of the game's peak object (the hill after the Kiln or
/// the Citadel), without the bridge onto it, the climbing holds on its rocks, and the summit around the flare boxes.
/// The hill is part of the map only once the climb has got near it, so it is looked for then
/// (<see cref="ScoutNear"/>). Used for the zombies (<see cref="PeakZombies"/>) and the bees (<see cref="ScatteredBees"/>).
/// </summary>
internal static class PeakHill
{
    /// <summary>How near the summit a scout must be for the hill to be looked for.</summary>
    internal const float LookNear = 400f;

    /// <summary>Nothing goes within this distance of the flare boxes (the summit).</summary>
    internal const float AwayFromSummit = 20f;

    private const float Grid = 2.5f;

    private static Transform sequence;
    private static Transform flares;

    internal static void Reset()
    {
        sequence = null;
        flares = null;
    }

    /// <summary>Where the game's ending plays (its peak sequence); its parent is the peak object.</summary>
    internal static Transform Sequence()
    {
        if (sequence == null)
        {
            sequence = Resources.FindObjectsOfTypeAll<PeakSequence>().Where(p => p != null && p.gameObject.scene.IsValid()).Select(p => p.transform).FirstOrDefault();
        }
        return sequence;
    }

    /// <summary>The summit: the flare boxes at the very top.</summary>
    internal static Transform Summit()
    {
        if (flares == null)
        {
            flares = Resources.FindObjectsOfTypeAll<Peak.EndgameFlareSpawner>().Where(f => f != null && f.gameObject.scene.IsValid()).Select(f => f.transform).FirstOrDefault();
        }
        return flares != null ? flares : Sequence();
    }

    /// <summary>A scout is near enough the summit for the hill to be in the map.</summary>
    internal static bool ScoutNear()
    {
        Transform summit = Summit();
        return summit != null && Character.AllCharacters.Any(c => c != null && !c.isBot && Vector3.Distance(c.Center, summit.position) <= LookNear);
    }

    /// <summary>The hill's ground: points on it, each with its slope, away from the summit and out of water.</summary>
    internal static List<RaycastHit> Ground(out string counts)
    {
        List<RaycastHit> ground = new List<RaycastHit>();
        counts = "no peak";
        Transform peak = Sequence() != null ? Sequence().parent : null;
        Transform summit = Summit();
        if (peak == null || summit == null || !peak.gameObject.activeInHierarchy)
        {
            return ground;
        }
        Collider[] hill = peak.GetComponentsInChildren<Collider>().Where(IsHill).ToArray();
        if (hill.Length == 0)
        {
            counts = "no hill ground";
            return ground;
        }
        Physics.SyncTransforms();
        // The map's water, looked up once (looking it up for every point froze the game for seconds).
        WaterZone[] water = Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
        Bounds bounds = hill[0].bounds;
        foreach (Collider c in hill)
        {
            bounds.Encapsulate(c.bounds);
        }
        int hits = 0, own = 0;
        for (float x = bounds.min.x; x <= bounds.max.x; x += Grid)
        {
            for (float z = bounds.min.z; z <= bounds.max.z; z += Grid)
            {
                Vector3 top = new Vector3(x, bounds.max.y + 5f, z);
                if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, bounds.size.y + 10f, HelperFunctions.terrainMapMask))
                {
                    continue;
                }
                hits++;
                if (!hit.collider.transform.IsChildOf(peak) || !IsHill(hit.collider))
                {
                    continue;
                }
                own++;
                if (Vector3.Distance(hit.point, summit.position) >= AwayFromSummit && !GroundItems.InWater(hit.point, water))
                {
                    ground.Add(hit);
                }
            }
        }
        counts = $"{hill.Length} hill colliders, {hits} ground hits, {own} on the hill, {ground.Count} away from the summit";
        return ground;
    }

    /// <summary>The hill's own rock and grass (not the bridge onto it, not the climbing holds).</summary>
    private static bool IsHill(Collider c)
    {
        string name = c.name;
        return c != null && !c.isTrigger && name.StartsWith("Rock_", System.StringComparison.Ordinal)
            && name.IndexOf("Hold", System.StringComparison.OrdinalIgnoreCase) < 0;
    }

    /// <summary>Spots from <paramref name="ground"/>, spread at least <paramref name="apart"/> from each other and from <paramref name="taken"/>.</summary>
    internal static List<Vector3> Spread(IEnumerable<Vector3> ground, int count, float apart, IList<Vector3> taken = null)
    {
        List<Vector3> picked = new List<Vector3>();
        foreach (Vector3 spot in ground.OrderBy(_ => Random.value))
        {
            if (picked.Count >= count)
            {
                break;
            }
            if (picked.All(p => (p - spot).sqrMagnitude >= apart * apart) && (taken == null || taken.All(p => (p - spot).sqrMagnitude >= apart * apart)))
            {
                picked.Add(spot);
            }
        }
        return picked;
    }
}
