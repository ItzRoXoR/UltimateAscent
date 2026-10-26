using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Config "More Wild Food in Multiplayer" (off by default, the host's): in multiplayer, every bush, palm, vine and
/// mushroom patch grows half as much food again as the game gives it (a half left over is a coin toss for one more),
/// on every ascent. The extra food lies beside the game's own, the same kind. Spawners of anything else (the Mesa
/// Tomb's Torches...) are left as they are.
/// </summary>
internal static class MoreWildFood
{
    internal const float Extra = 0.5f;

    /// <summary>How far from one of the game's items an extra one is put.</summary>
    private const float Spread = 0.3f;

    /// <summary>For the tests: count as multiplayer although the game is offline.</summary>
    internal static bool AsMultiplayer;

    /// <summary>For the tests: extra items grown so far.</summary>
    internal static int Grown;

    private static bool On => Plugin.MoreWildFood != null && Plugin.MoreWildFood.Value && PhotonNetwork.IsMasterClient
        && (AsMultiplayer || (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode));

    [HarmonyPatch]
    private static class Grow
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(BerryBush), nameof(BerryBush.SpawnItems));
            yield return AccessTools.Method(typeof(BerryVine), nameof(BerryVine.SpawnItems));
            yield return AccessTools.Method(typeof(GroundPlaceSpawner), nameof(GroundPlaceSpawner.SpawnItems));
        }

        private static void Postfix(Spawner __instance, List<PhotonView> __result)
        {
            if (!On || __result == null || __result.Count == 0)
            {
                return;
            }
            Item first = __result[0] != null ? __result[0].GetComponent<Item>() : null;
            if (first == null || !IsFood(first))
            {
                return;
            }
            float more = __result.Count * Extra;
            int count = Mathf.FloorToInt(more) + (Random.value < more - Mathf.Floor(more) ? 1 : 0);
            bool onGround = __instance is GroundPlaceSpawner;
            string prefab = PrefabName(first);
            int made = __result.Count;
            for (int i = 0; i < count; i++)
            {
                PhotonView beside = __result[Random.Range(0, made)];
                if (beside == null)
                {
                    continue;
                }
                Vector2 flat = Random.insideUnitCircle * Spread;
                Vector3 at = beside.transform.position + new Vector3(flat.x, 0f, flat.y);
                Quaternion turn = beside.transform.rotation;
                if (onGround && Physics.Raycast(at + Vector3.up * 1f, Vector3.down, out RaycastHit ground, 3f, HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)))
                {
                    at = ground.point;
                    turn = HelperFunctions.GetRandomRotationWithUp(ground.normal);
                }
                GameObject spawned = PhotonNetwork.InstantiateItemRoom(prefab, at, turn);
                Item item = spawned != null ? spawned.GetComponent<Item>() : null;
                if (item == null)
                {
                    continue;
                }
                item.SetKinematicNetworked(value: true, item.transform.position, item.transform.rotation);
                __result.Add(item.GetComponent<PhotonView>());
                Grown++;
            }
        }
    }

    /// <summary>Something to eat: it restores hunger, or it is a coconut (broken open, its halves do).</summary>
    internal static bool IsFood(Item item) =>
        item.GetComponent<Action_RestoreHunger>() != null || item.name.IndexOf("Coconut", System.StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>The item's prefab name (the spawned copy's name without "(Clone)").</summary>
    private static string PrefabName(Item item)
    {
        string name = item.gameObject.name;
        int clone = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
        return (clone >= 0 ? name.Substring(0, clone) : name).Trim();
    }
}
