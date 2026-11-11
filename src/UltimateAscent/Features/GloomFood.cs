using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// More of the Gloom's food, on every ascent (and in custom runs):
/// banana vines (the Gloom's BerryVines, which grow Berrynanas) bear fruit 40% of the time (the game: 10%; a vine the
/// game gives a better chance keeps it), as many bananas and of the same kinds as before;
/// and the Gloom has 10 cattail Hot Dogs (the game: one per spawner, 7 on the maps seen). The host adds the missing ones
/// right after the game has put its own out as the Gloom loads: on the swamp floor at the heights the game's stand at
/// (within a metre), on its own ground, level, open to the sky, 20 m or more from any other cattail Hot Dog.
/// </summary>
internal static class GloomFood
{
    internal const float BananaVineChance = 0.4f;
    internal const int HotDogs = 10;
    internal const float HotDogsApart = 20f;
    private const string CattailHotDog = "Glizzy_CattailVariant";

    /// <summary>The Gloom biomes whose Hot Dogs the game is putting out now (host): the missing ones are added right after.</summary>
    private static readonly List<Biome> pending = new List<Biome>();
    private static readonly HashSet<Biome> done = new HashSet<Biome>();

    /// <summary>For the tests: the cattail Hot Dogs the game put out, and the ones added (host).</summary>
    internal static int GameHotDogs;
    internal static readonly List<Item> Added = new List<Item>();

    internal static void Reset()
    {
        pending.Clear();
        done.Clear();
        GameHotDogs = 0;
        Added.Clear();
    }

    internal static bool IsBananaVine(Spawner spawner) =>
        spawner is BerryVine && spawner.spawns != null && spawner.spawns.items != null && spawner.spawns.items.Any(e => e.prefab != null && e.prefab.name.StartsWith("Berrynana"));

    /// <summary>A banana vine bears fruit 40% of the time.</summary>
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.TrySpawnItems))]
    private static class BananaVines
    {
        private static void Prefix(Spawner __instance)
        {
            if (Rules.InRun && IsBananaVine(__instance) && __instance.baseSpawnChance < BananaVineChance)
            {
                __instance.baseSpawnChance = BananaVineChance;
            }
        }
    }

    internal static bool IsCattailHotDog(SingleItemSpawner spawner) => spawner != null && spawner.prefab != null && spawner.prefab.name == CattailHotDog;

    /// <summary>As the game puts out a Gloom's first cattail Hot Dog (host), the missing ones are due (<see cref="Tick"/>).</summary>
    [HarmonyPatch(typeof(SingleItemSpawner), nameof(SingleItemSpawner.TrySpawnItems))]
    private static class HotDogsDue
    {
        private static void Prefix(SingleItemSpawner __instance)
        {
            if (!Rules.InRun || !PhotonNetwork.IsMasterClient || !IsCattailHotDog(__instance))
            {
                return;
            }
            Biome biome = __instance.GetComponentInParent<Biome>(true);
            if (biome != null && done.Add(biome))
            {
                pending.Add(biome);
            }
        }
    }

    /// <summary>Every frame (host): the missing Hot Dogs of a Gloom the game has just put its own out in.</summary>
    internal static void Tick()
    {
        if (pending.Count == 0)
        {
            return;
        }
        List<Biome> due = new List<Biome>(pending);
        pending.Clear();
        if (!PhotonNetwork.IsMasterClient)
        {
            return;
        }
        foreach (Biome biome in due.Where(b => b != null))
        {
            try
            {
                AddHotDogs(biome);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Gloom food: adding Hot Dogs failed: {ex}");
            }
        }
    }

    private static void AddHotDogs(Biome biome)
    {
        List<SingleItemSpawner> spawners = biome.GetComponentsInChildren<SingleItemSpawner>(true).Where(IsCattailHotDog).ToList();
        GameHotDogs += spawners.Count;
        int missing = HotDogs - spawners.Count;
        if (missing <= 0 || spawners.Count == 0 || !MoreNests.GloomArea(biome, out Rect area, out _))
        {
            return;
        }
        float low = spawners.Min(s => s.transform.position.y) - 1f;
        float high = spawners.Max(s => s.transform.position.y) + 1f;
        List<Vector3> taken = spawners.Select(s => s.transform.position).ToList();
        int added = 0;
        for (int attempt = 0; attempt < 4000 && added < missing; attempt++)
        {
            Vector3 start = new Vector3(Random.Range(area.xMin, area.xMax), high + 80f, Random.Range(area.yMin, area.yMax));
            if (!Physics.Raycast(start, Vector3.down, out RaycastHit ground, 200f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
            {
                continue;
            }
            Vector3 p = ground.point;
            if (p.y < low || p.y > high || ground.normal.y < 0.9f || !MoreNests.NaturalGround(ground.collider, biome)
                || taken.Any(t => new Vector2(t.x - p.x, t.z - p.z).magnitude < HotDogsApart)
                || Physics.Raycast(start, Vector3.down, out RaycastHit first, 200f, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore) && first.point.y > p.y + 0.3f
                || Physics.Raycast(p + Vector3.up, Vector3.up, 30f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
            {
                continue;
            }
            // Stood as the game's stand (its spawner's turn), turned at random about the up.
            SingleItemSpawner like = spawners[Random.Range(0, spawners.Count)];
            Quaternion turn = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.FromToRotation(like.transform.up, Vector3.up) * like.transform.rotation;
            GameObject spawned = PhotonNetwork.InstantiateItemRoom(CattailHotDog, p + Vector3.up * 0.1f, turn);
            Item item = spawned != null ? spawned.GetComponent<Item>() : null;
            if (item == null)
            {
                continue;
            }
            if (like.isKinematic)
            {
                item.GetComponent<PhotonView>().RPC("SetKinematicRPC", RpcTarget.AllBuffered, true, item.transform.position, item.transform.rotation);
            }
            taken.Add(p);
            Added.Add(item);
            added++;
        }
        Plugin.Log.LogInfo($"Gloom food: {spawners.Count} cattail Hot Dogs from the game, {added} added ({missing} missing).");
    }
}
