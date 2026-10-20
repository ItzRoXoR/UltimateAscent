using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: the Caldera's last stretch is ablaze. From the Caldera's statue (on the lava temple, with its luggage)
/// up to the campfire that opens the Kiln, the volcano erupts every 2-6 s (the game's eruption, a column of fire
/// burning 35 s) while any scout is on that stretch or coming up to it. The eruptions land all over it, each one away from the last few, so the
/// fire reaches about everywhere; only 20 m around the campfire and around the statue stay clear (the Caldera's own
/// eruptions keep off them too). The host chooses where and sends the game's own eruption message.
/// </summary>
internal static class CalderaPathEruptions
{
    private const float EveryFrom = 2f;
    internal const float EveryTo = 6f;

    /// <summary>No eruption this close to the campfire or the statue (along the ground); an eruption's heat reaches about 10 m.</summary>
    internal const float KeepClear = 20f;

    /// <summary>The stretch is sampled every 3 m, as far as 60 m to either side of the line from the statue to the campfire.</summary>
    private const float Step = 3f;
    private const float Wide = 60f;

    /// <summary>Ground more than this far below the statue (or the campfire, if lower) is the lava's floor, not the stretch.</summary>
    private const float Below = 25f;

    /// <summary>Each eruption lands as far as it can from the last few (picked among 10 tries).</summary>
    private const int Remembered = 8;
    private const int Tries = 10;

    private static MapHandler builtFor;
    private static readonly List<Vector3> ground = new List<Vector3>();
    private static Vector3 campfire;
    private static Vector3 statue;
    private static bool hasStatue;
    private static float fromZ;
    private static float toZ;
    private static float nextAt;
    private static readonly List<Vector3> recent = new List<Vector3>();

    /// <summary>For the tests.</summary>
    internal static int Spawned;
    internal static float LastAt = -100f;
    internal static float LongestWait;

    /// <summary>For the tests: the stretch's open ground left clear around the campfire and the statue.</summary>
    internal static int Cleared;

    /// <summary>For the tests: no eruptions in the Caldera (the stretch's or the Caldera's own) while one is measured.</summary>
    internal static bool Held;
    internal static IReadOnlyList<Vector3> Ground => ground;
    internal static Vector3 Campfire => campfire;
    internal static Vector3 Statue => statue;
    internal static bool HasStatue => hasStatue;
    internal static float FromZ => fromZ;
    internal static float ToZ => toZ;

    private static bool On => Rules.Enabled(Lvl.Hazards);

    internal static void Reset()
    {
        builtFor = null;
        ground.Clear();
        recent.Clear();
        hasStatue = false;
        nextAt = 0f;
        LastAt = -100f;
        LongestWait = 0f;
    }

    /// <summary>Within 20 m (along the ground) of the Kiln's campfire or the Caldera's statue.</summary>
    internal static bool TooClose(Vector3 position)
    {
        if (builtFor == null)
        {
            return false;
        }
        return Flat(position, campfire) < KeepClear || (hasStatue && Flat(position, statue) < KeepClear);
    }

    /// <summary>A scout is on the stretch or coming up to it (within 30 m of the statue's end), short of the campfire.</summary>
    internal static bool OnTheStretch(Vector3 position) => builtFor != null && position.z >= fromZ - 30f && position.z <= toZ + 5f && Mathf.Abs(position.x - LineX(position.z)) <= Wide + 20f;

    /// <summary>Where the line from the statue to the campfire crosses this z.</summary>
    private static float LineX(float z)
    {
        Vector3 start = hasStatue ? statue : campfire;
        float t = Mathf.Approximately(campfire.z, start.z) ? 1f : Mathf.Clamp01((z - start.z) / (campfire.z - start.z));
        return Mathf.Lerp(start.x, campfire.x, t);
    }

    /// <summary>The host, every frame while the Caldera's eruption spawner runs: the next eruption on the stretch when it is due.</summary>
    internal static void Tick(EruptionSpawner spawner)
    {
        if (!On || Held || !PhotonNetwork.IsMasterClient || !Map.InVolcanoCaldera || !Build() || ground.Count == 0)
        {
            return;
        }
        bool anyone = Character.AllCharacters.Any(c => c != null && !c.data.dead && OnTheStretch(c.Center));
        if (!anyone)
        {
            nextAt = 0f;
            return;
        }
        if (nextAt == 0f)
        {
            nextAt = Time.time + Random.Range(0f, EveryFrom);
        }
        if (Time.time < nextAt)
        {
            return;
        }
        nextAt = Time.time + Random.Range(EveryFrom, EveryTo);
        Vector3 spot = Pick();
        if (LastAt > 0f)
        {
            LongestWait = Mathf.Max(LongestWait, Time.time - LastAt);
        }
        LastAt = Time.time;
        Spawned++;
        spawner.photonView.RPC("RPCA_SpawnEruption", RpcTarget.All, spot);
    }

    private static Vector3 Pick()
    {
        Vector3 best = ground[Random.Range(0, ground.Count)];
        float bestDistance = -1f;
        for (int i = 0; i < Tries; i++)
        {
            Vector3 candidate = ground[Random.Range(0, ground.Count)];
            float nearest = recent.Count == 0 ? float.MaxValue : recent.Min(r => Flat(r, candidate));
            if (nearest > bestDistance)
            {
                bestDistance = nearest;
                best = candidate;
            }
        }
        recent.Add(best);
        if (recent.Count > Remembered)
        {
            recent.RemoveAt(0);
        }
        return best;
    }

    /// <summary>
    /// The stretch's ground, found once per map: from the statue to the campfire, every 3 m, the open ground (not
    /// steep, above the lava, not in it, not far below the statue), within 60 m of the line between them, minus 20 m
    /// around the campfire and the statue.
    /// </summary>
    internal static bool Build()
    {
        MapHandler map = Singleton<MapHandler>.Instance;
        if (map == null)
        {
            return false;
        }
        if (builtFor == map)
        {
            return true;
        }
        GameObject campfireRoot = MapHandler.GetCampfireRoot((int)Segment.Caldera);
        Campfire fire = campfireRoot != null ? campfireRoot.GetComponentInChildren<Campfire>(true) : null;
        GameObject segment = map.segments.Length > (int)Segment.Caldera ? map.segments[(int)Segment.Caldera].segmentParent : null;
        if (fire == null || segment == null)
        {
            return false;
        }
        builtFor = map;
        ground.Clear();
        recent.Clear();
        Cleared = 0;
        campfire = fire.transform.position;
        RespawnChest chest = segment.GetComponentsInChildren<RespawnChest>(true).OrderBy(c => Flat(c.transform.position, campfire)).FirstOrDefault();
        hasStatue = chest != null;
        statue = hasStatue ? chest.transform.position : Vector3.zero;
        Lava river = segment.GetComponentsInChildren<Lava>(true).FirstOrDefault(l => l.name == "River");
        fromZ = hasStatue ? statue.z : campfire.z - 250f;
        toZ = campfire.z;
        float lowest = Mathf.Min(campfire.y, hasStatue ? statue.y : campfire.y) - Below;
        if (river != null)
        {
            lowest = Mathf.Max(lowest, river.transform.position.y + 2f);
        }
        float top = campfire.y + 40f;
        float bottom = Mathf.Min(campfire.y, hasStatue ? statue.y : campfire.y) - 120f;
        for (float z = fromZ; z <= toZ; z += Step)
        {
            float middle = LineX(z);
            for (float x = middle - Wide; x <= middle + Wide; x += Step)
            {
                RaycastHit hit = HelperFunctions.LineCheck(new Vector3(x, top, z), new Vector3(x, bottom, z), HelperFunctions.LayerType.TerrainMap);
                if (hit.transform == null || hit.normal.y < 0.55f || hit.point.y < lowest || hit.point.y > campfire.y + 15f
                    || hit.collider.GetComponentInParent<Lava>() != null)
                {
                    continue;
                }
                if (TooClose(hit.point))
                {
                    Cleared++;
                    continue;
                }
                ground.Add(hit.point);
            }
        }
        Plugin.Debug($"Ascent 17: the Caldera's last stretch (z {fromZ:0} to {toZ:0}) has {ground.Count} spots to erupt on.");
        return true;
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }
}
