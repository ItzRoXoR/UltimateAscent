using System.Collections.Generic;
using System.Linq;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The peak's grassy hill, where the flowers grow: the ground of the game's peak object (the hill after the Kiln or
/// the Citadel), without the bridge onto it, the climbing holds on its rocks, and the summit around the flare boxes.
/// The hill is part of the map only once the climb has got near it, so it is looked for then
/// (<see cref="ScoutNear"/>). With True Final Ascent's Builder it is the peak TFA puts after the chain's last block. Used for the bees (<see cref="ScatteredBees"/>); the zombies stand on its top, by the
/// statue the gems go in (<see cref="TopGround"/>) and on its rocks' ledges (<see cref="Ledges"/>, <see cref="PeakZombies"/>).
/// </summary>
internal static class PeakHill
{
    /// <summary>How near the summit a scout must be for the hill to be looked for.</summary>
    internal const float LookNear = 400f;

    /// <summary>Nothing goes within this distance of the flare boxes (the summit).</summary>
    internal const float AwayFromSummit = 20f;

    private const float Grid = 2.5f;

    /// <summary>The top of the hill: within this distance of the summit (across) and this far below it.</summary>
    internal const float TopReach = 30f;
    internal const float TopDepth = 30f;

    /// <summary>By the statue the gems go in (the way to the Nadir): within this distance of it, about its level.</summary>
    internal const float StatueReach = 15f;
    private const float StatueLevel = 3f;

    /// <summary>The top's ground is looked at this finely; the flare boxes keep this much room.</summary>
    private const float TopGrid = 1.5f;
    internal const float FlareRoom = 3f;

    /// <summary>A place has level ground round it: this many level spots within 2.2 m of it, about its height.</summary>
    private const int RoomAround = 4;

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
        if (sequence == null || Moved(sequence))
        {
            sequence = Of<PeakSequence>();
        }
        return sequence;
    }

    /// <summary>The summit: the flare boxes at the very top.</summary>
    internal static Transform Summit()
    {
        if (flares == null || Moved(flares))
        {
            flares = Of<Peak.EndgameFlareSpawner>();
        }
        return flares != null ? flares : Sequence();
    }

    /// <summary>
    /// The peak's part of this kind. With True Final Ascent's Builder, the peak's TFA put after the chain's last block (the
    /// map keeps the game's other peaks where they were).
    /// </summary>
    private static bool Moved(Transform found) => Compat.Tfa.BuilderSummit is Transform peak && !found.IsChildOf(peak);

    private static Transform Of<T>() where T : Component
    {
        Transform builderPeak = Compat.Tfa.BuilderSummit;
        IEnumerable<T> parts = Resources.FindObjectsOfTypeAll<T>().Where(p => p != null && p.gameObject.scene.IsValid());
        if (builderPeak != null)
        {
            parts = parts.Where(p => p.transform.IsChildOf(builderPeak));
        }
        return parts.Select(p => p.transform).FirstOrDefault();
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

    /// <summary>
    /// Level ground at the top of the peak (within 30 m of the summit, up to 30 m below it), highest first, and by the
    /// statue the gems go in (<paramref name="byStatue"/>: within 15 m of it, about its level), nearest it first: open to
    /// stand on (room for a scout), with level ground round it (so nothing standing there walks straight off an edge), out
    /// of water, off the statue and clear of the flare boxes. Any of the map's ground counts here: the very top is not the
    /// hill's rock.
    /// </summary>
    internal static List<Vector3> TopGround(out List<Vector3> byStatue, out string counts)
    {
        List<Vector3> spots = new List<Vector3>();
        byStatue = new List<Vector3>();
        Transform summit = Summit();
        if (summit == null)
        {
            counts = "no summit";
            return spots;
        }
        Vector3 s = summit.position;
        Peak.ScoutStatue statue = GemStatue(s);
        Physics.SyncTransforms();
        WaterZone[] water = Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
        List<Vector3> found = new List<Vector3>();
        void Look(Vector3 centre, float reach, float from, float lowest, float highest)
        {
            for (float x = -reach; x <= reach; x += TopGrid)
            {
                for (float z = -reach; z <= reach; z += TopGrid)
                {
                    if (x * x + z * z > reach * reach)
                    {
                        continue;
                    }
                    Vector3 top = new Vector3(centre.x + x, from, centre.z + z);
                    if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, from - lowest + 1f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                    if (hit.point.y < lowest || hit.point.y > highest || Vector3.Angle(hit.normal, Vector3.up) >= 30f
                        || hit.collider.GetComponentInParent<Peak.ScoutStatue>() != null
                        || new Vector2(hit.point.x - s.x, hit.point.z - s.z).magnitude < FlareRoom
                        || !Snakes.RoomForScout(hit.point) || GroundItems.InWater(hit.point, water))
                    {
                        continue;
                    }
                    found.Add(hit.point);
                }
            }
        }
        Look(s, TopReach, s.y + 20f, s.y - TopDepth, s.y + 10f);
        int onTop = found.Count;
        if (statue != null)
        {
            Vector3 at = statue.transform.position;
            Look(at, StatueReach, at.y + 8f, at.y - StatueLevel, at.y + StatueLevel);
        }
        for (int i = 0; i < found.Count; i++)
        {
            Vector3 spot = found[i];
            int around = found.Count(o => o != spot && new Vector2(o.x - spot.x, o.z - spot.z).magnitude <= 2.2f && Mathf.Abs(o.y - spot.y) < 0.6f);
            List<Vector3> into = i < onTop ? spots : byStatue;
            if (around >= RoomAround && !into.Any(p => (p - spot).sqrMagnitude < 0.01f))
            {
                into.Add(spot);
            }
        }
        spots = spots.OrderByDescending(p => p.y).ToList();
        if (statue != null)
        {
            Vector3 at = statue.transform.position;
            byStatue = byStatue.OrderBy(p => Vector3.Distance(p, at)).ToList();
        }
        counts = $"top: {onTop} open level spots, {spots.Count} with room round them; by the statue{(statue != null ? $" ({Vector3.Distance(statue.transform.position, s):0} m from the summit)" : " (none)")}: {found.Count - onTop}, {byStatue.Count} with room round them";
        return spots;
    }

    /// <summary>
    /// Level ledges on the hill's rocks around the peak (<paramref name="ground"/>: <see cref="Ground"/>), off its top and
    /// away from the statue the gems go in: open to stand on (room for a scout), with level ground round it.
    /// </summary>
    internal static List<Vector3> Ledges(List<RaycastHit> ground, out string counts)
    {
        Transform summit = Summit();
        if (summit == null)
        {
            counts = "no summit";
            return new List<Vector3>();
        }
        Vector3 s = summit.position;
        Peak.ScoutStatue statue = GemStatue(s);
        List<Vector3> level = ground.Where(h => Vector3.Angle(h.normal, Vector3.up) < 30f).Select(h => h.point)
            .Where(p => !(new Vector2(p.x - s.x, p.z - s.z).magnitude <= TopReach && p.y >= s.y - TopDepth))
            .Where(p => statue == null || Vector3.Distance(p, statue.transform.position) > StatueReach + 5f)
            .ToList();
        List<Vector3> ledges = level.Where(p => Snakes.RoomForScout(p) && RoomRound(p)).ToList();
        counts = $"{level.Count} level spots on its rocks off the top, {ledges.Count} with room round them";
        return ledges;
    }

    /// <summary>Level ground round a spot, about its height (the top's grid around it: at least 4 of its 8 neighbours).</summary>
    private static bool RoomRound(Vector3 spot)
    {
        int level = 0;
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                if ((x != 0 || z != 0) && Physics.Raycast(spot + new Vector3(x * TopGrid, 2f, z * TopGrid), Vector3.down, out RaycastHit hit, 4f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)
                    && Vector3.Angle(hit.normal, Vector3.up) < 30f && Mathf.Abs(hit.point.y - spot.y) < 0.6f)
                {
                    level++;
                }
            }
        }
        return level >= RoomAround;
    }

    /// <summary>The statue the gems go in (the way to the Nadir): the nearest one to the summit, within 200 m of it.</summary>
    private static Peak.ScoutStatue GemStatue(Vector3 summit)
    {
        Peak.ScoutStatue statue = Object.FindObjectsByType<Peak.ScoutStatue>(FindObjectsSortMode.None)
            .Where(st => st != null).OrderBy(st => Vector3.Distance(st.transform.position, summit)).FirstOrDefault();
        return statue != null && Vector3.Distance(statue.transform.position, summit) <= 200f ? statue : null;
    }

    /// <summary>
    /// Spots in the order given, each at least <paramref name="apart"/> from those before (closer together where there is
    /// less room: down to 2.5 m).
    /// </summary>
    internal static List<Vector3> InTurn(List<Vector3> spots, int count, float apart)
    {
        List<Vector3> picked = new List<Vector3>();
        foreach (float gap in new[] { apart, 3f, 2.5f })
        {
            foreach (Vector3 spot in spots)
            {
                if (picked.Count >= count)
                {
                    return picked;
                }
                if (picked.All(p => (p - spot).sqrMagnitude >= gap * gap))
                {
                    picked.Add(spot);
                }
            }
        }
        return picked;
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
