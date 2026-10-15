using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Stones and sticks lying around (<see cref="StoneItem"/>, <see cref="StickItem"/>), from ascent 9 and in
/// custom runs unless the config's "New Items" switch is off: up to 20 of each on the beach and up to 20
/// of each in the Tropics or the Roots. They lie near the spots the game chose for its mushroom patches
/// and berry bushes: a few metres away, on ground at the same level and open to the sky, not steeper than
/// 30 degrees, out of the water and 4 m apart. The host lays them out when the game fills that part of the
/// map with its own items, and the game clears them with the rest when the climb moves on.
/// </summary>
internal static class GroundItems
{
    internal const int PerBiome = 20;
    internal const float Spacing = 4f;
    internal const float MaxSlope = 30f;
    private const float Scatter = 6f;
    private const float SameLevel = 1.5f;
    private const float OpenAbove = 6f;
    private const int AttemptsPerItem = 60;
    private const float StickDrop = 0.5f;
    internal const string SpawnerName = "UltimateAscent Stones And Sticks";

    /// <summary>For the tests: what the host laid out in each part of the map, by the part's name.</summary>
    internal static readonly Dictionary<string, List<Item>> Placed = new Dictionary<string, List<Item>>();

    private static readonly int WaterMask = LayerMask.GetMask("Water");

    /// <summary>The beach and the Tropics/Roots get a spawner each when the map is set up, before the game fills the beach.</summary>
    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.InitializeMap))]
    private static class EveryMap
    {
        private static void Postfix()
        {
            MapHandler map = MapHandler.Exists ? Singleton<MapHandler>.Instance : null;
            if (map == null || map.segments == null || map.segments.Length < 2)
            {
                return;
            }
            Placed.Clear();
            for (int i = 0; i < 2; i++)
            {
                GameObject segment = map.segments[i].segmentParent;
                if (segment == null || segment.transform.Find(SpawnerName) != null)
                {
                    continue;
                }
                GameObject holder = new GameObject(SpawnerName);
                holder.transform.SetParent(segment.transform, false);
                holder.AddComponent<GroundItemSpawner>();
            }
        }
    }

    /// <summary>The host lays out this part of the map's stones and sticks (none if they are switched off).</summary>
    internal static List<PhotonView> Spawn(Transform segment)
    {
        List<PhotonView> spawned = new List<PhotonView>();
        if (segment == null || !PhotonNetwork.IsMasterClient || !NewItems.WildOn)
        {
            return spawned;
        }
        Physics.SyncTransforms();
        List<Vector3> anchors = Anchors(segment);
        if (anchors.Count == 0)
        {
            Plugin.Log.LogWarning($"No mushroom patches or berry bushes in {segment.name}; no stones or sticks there.");
            return spawned;
        }
        WaterZone[] water = Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
        List<Vector3> taken = new List<Vector3>();
        List<Item> placed = new List<Item>();
        foreach (Item template in new[] { StoneItem.Template, StickItem.Template })
        {
            if (template == null)
            {
                continue;
            }
            int count = 0;
            for (int attempt = 0; attempt < PerBiome * AttemptsPerItem && count < PerBiome; attempt++)
            {
                if (!TryFindSpot(anchors, taken, water, out RaycastHit ground))
                {
                    continue;
                }
                Item item = Lay(template, ground);
                if (item == null)
                {
                    continue;
                }
                taken.Add(ground.point);
                placed.Add(item);
                spawned.Add(item.photonView);
                count++;
            }
            if (count < PerBiome)
            {
                Plugin.Debug($"Only {count} spots for {template.UIData.itemName} in {segment.name}.");
            }
        }
        Placed[segment.name] = placed;
        Plugin.Debug($"Laid out {placed.Count} stones and sticks in {segment.name}.");
        return spawned;
    }

    /// <summary>The ground under the game's mushroom patches and berry bushes of this part of the map.</summary>
    private static List<Vector3> Anchors(Transform segment)
    {
        List<Vector3> anchors = new List<Vector3>();
        const SpawnPool bushes = SpawnPool.BerryBushBeach | SpawnPool.BerryBushJungle;
        foreach (Spawner spawner in segment.GetComponentsInChildren<Spawner>(true))
        {
            bool onGround = spawner is GroundPlaceSpawner || (spawner is BerryBush bush && (bush.spawnPool & bushes) != 0);
            if (onGround && Physics.Raycast(spawner.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 20f, HelperFunctions.terrainMapMask))
            {
                anchors.Add(hit.point);
            }
        }
        return anchors;
    }

    private static bool TryFindSpot(List<Vector3> anchors, List<Vector3> taken, WaterZone[] water, out RaycastHit ground)
    {
        Vector3 anchor = anchors[Random.Range(0, anchors.Count)];
        Vector2 offset = Random.insideUnitCircle * Scatter;
        Vector3 above = new Vector3(anchor.x + offset.x, anchor.y + OpenAbove, anchor.z + offset.y);
        // The first thing below must be the ground at the patch's level: nothing over it, no cliff below.
        if (!Physics.Raycast(above, Vector3.down, out ground, OpenAbove + SameLevel, HelperFunctions.terrainMapMask))
        {
            return false;
        }
        Vector3 spot = ground.point;
        if (Mathf.Abs(spot.y - anchor.y) > SameLevel || Vector3.Angle(ground.normal, Vector3.up) > MaxSlope)
        {
            return false;
        }
        return !InWater(spot, water) && !taken.Any(t => (t - spot).sqrMagnitude < Spacing * Spacing);
    }

    /// <summary>Under water: inside one of the map's water zones, or below a water surface.</summary>
    internal static bool InWater(Vector3 spot, WaterZone[] water = null)
    {
        water ??= Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
        if (Physics.Raycast(spot + Vector3.up * OpenAbove, Vector3.down, out RaycastHit wet, OpenAbove + 0.3f, WaterMask, QueryTriggerInteraction.Collide) && wet.point.y > spot.y - 0.05f)
        {
            return true;
        }
        return water.Any(zone => zone != null && zone.isActiveAndEnabled && zone.zoneBounds.Contains(spot));
    }

    /// <summary>A stone rests where it lies (it would roll otherwise); a stick drops from half a metre and settles.</summary>
    private static Item Lay(Item template, RaycastHit ground)
    {
        bool stone = StoneItem.IsStone(template);
        Quaternion turn = stone ? HelperFunctions.GetRandomRotationWithUp(ground.normal) : Random.rotation;
        Vector3 at = ground.point + (stone ? ground.normal * RestHeight(template) : Vector3.up * StickDrop);
        GameObject made = PhotonNetwork.InstantiateItemRoom(template.gameObject.name, at, turn);
        Item item = made != null ? made.GetComponent<Item>() : null;
        if (item != null && stone)
        {
            item.SetKinematicNetworked(true, item.transform.position, item.transform.rotation);
        }
        return item;
    }

    /// <summary>How far the item's lowest point is below its centre, along its own up.</summary>
    private static float RestHeight(Item template)
    {
        float lowest = 0f;
        bool any = false;
        foreach (MeshCollider collider in template.GetComponentsInChildren<MeshCollider>(true))
        {
            if (collider.sharedMesh == null)
            {
                continue;
            }
            Bounds bounds = collider.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                float y = template.transform.InverseTransformPoint(collider.transform.TransformPoint(corner)).y;
                if (!any || y < lowest)
                {
                    lowest = y;
                    any = true;
                }
            }
        }
        return any ? -lowest : 0.2f;
    }
}

/// <summary>The game fills a part of the map by asking every spawner in it; this one lays out the stones and sticks.</summary>
internal sealed class GroundItemSpawner : MonoBehaviour, ISpawner
{
    private bool done;

    public List<PhotonView> TrySpawnItems()
    {
        if (done)
        {
            return new List<PhotonView>();
        }
        done = PhotonNetwork.IsMasterClient;
        return GroundItems.Spawn(transform.parent);
    }
}
