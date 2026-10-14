using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: bee ambushes in the Tropics. Twice per in-game day, at the middle of the day and at the
/// middle of the night, every scout in the Tropics has a 30% chance to be attacked by an angry swarm
/// (no beehive needed), so several scouts can be hit at the same time. The swarm appears right next
/// to its scout.
/// Escaping works like with a stolen beehive: the swarm is tied to the spot it appeared at (it
/// follows you up to 120 m from there), so you have to outrun it or run far away from it; once it
/// loses you, it disperses. It stays angry for 50 s. Bees only ambush in the Tropics:
/// a scout standing anywhere else (also one lagging behind on the shore) is left alone.
/// </summary>
internal static class A17_BeeAmbush
{
    internal const string Marker = "UltimateAscent.bees";

    private const float ChancePerMoment = 0.3f;
    private const float AngrySeconds = A17_Hazards.BeeAngerSeconds;

    /// <summary>An ambush swarm follows its scout up to 120 m from where it appeared, like a hive's bees from their hive.</summary>
    private const float Leash = A17_Hazards.BeeChaseDistance;
    private const string FallbackPrefab = "BeeSwarm";
    private const float FallbackSpawnDistance = 3f;

    /// <summary>The time of day (hours) seen last frame; -1 before the first look.</summary>
    private static float lastTime = -1f;
    private static float lastMomentAt = -1000f;
    private static string prefabName;
    private static BeeSwarm template;

    internal static void Reset()
    {
        lastTime = -1f;
        lastMomentAt = -1000f;
    }

    /// <summary>The middle of the day and the middle of the night, in hours.</summary>
    internal static void Moments(DayNightManager clock, out float midday, out float midnight)
    {
        midday = (clock.dayStart + clock.dayEnd) / 2f;
        midnight = Mathf.Repeat(clock.dayEnd + (24f - clock.dayEnd + clock.dayStart) / 2f, 24f);
    }

    /// <summary>The clock went past <paramref name="moment"/> between the two times (it wraps at 24).</summary>
    private static bool Passed(float from, float to, float moment)
    {
        if (from <= to)
        {
            return from < moment && moment <= to;
        }
        // A short step back is the clock being set back, not midnight going by.
        if (from - to < 12f)
        {
            return false;
        }
        return moment > from || moment <= to;
    }

    internal static bool IsAmbush(BeeSwarm swarm)
    {
        object[] data = swarm.photonView != null ? swarm.photonView.InstantiationData : null;
        return data != null && data.Length > 0 && data[0] as string == Marker;
    }

    /// <summary>Host only; called every frame.</summary>
    internal static void Tick()
    {
        if (!Rules.Enabled(Lvl.Hazards) || !Rules.IsHost || !PhotonNetwork.InRoom || DayNightManager.instance == null || LoadingScreenHandler.loading || !MapHandler.ExistsAndInitialized)
        {
            return;
        }
        DayNightManager clock = DayNightManager.instance;
        float now = clock.timeOfDay % 24f;
        float before = lastTime;
        lastTime = now;
        if (before < 0f)
        {
            return;
        }
        Moments(clock, out float midday, out float midnight);
        bool day = Passed(before, now, midday);
        // The host corrects the clock now and then; one moment never counts twice.
        if ((!day && !Passed(before, now, midnight)) || Time.time - lastMomentAt < 30f)
        {
            return;
        }
        lastMomentAt = Time.time;
        if (Map.CurrentPlace != Place.Tropics)
        {
            return;
        }
        int swarms = 0;
        foreach (Character character in Targets())
        {
            if (Random.value < ChancePerMoment)
            {
                Spawn(character);
                swarms++;
            }
        }
        Plugin.Debug($"Ascent 17: the bee moment of the {(day ? "day" : "night")}: {swarms} swarm(s).");
    }

    private static List<Character> Targets()
    {
        List<Character> targets = new List<Character>();
        foreach (Character character in Character.AllCharacters)
        {
            if (character != null && !character.isBot && !character.isScoutmaster && character.data != null && character.data.fullyConscious && !character.data.dead &&
                InTropics(character))
            {
                targets.Add(character);
            }
        }
        return targets;
    }

    /// <summary>The ground below the scout belongs to the Tropics (where it belongs to no biome, the climb's segment decides).</summary>
    internal static bool InTropics(Character character)
    {
        if (Map.TryGetGroundBiome(character.Center, out Biome.BiomeType biome))
        {
            return biome == Biome.BiomeType.Tropics;
        }
        return Map.CurrentPlace == Place.Tropics;
    }

    private static void Spawn(Character target)
    {
        string prefab = PrefabName();
        GameObject spawned = PhotonNetwork.Instantiate(prefab, SpawnPoint(target), Quaternion.identity, 0, new object[] { Marker });
        BeeSwarm swarm = spawned != null ? spawned.GetComponent<BeeSwarm>() : null;
        if (swarm == null)
        {
            return;
        }
        swarm.beehiveID = -1;
        swarm.maxHiveDistance = Leash;
        swarm.currentAggroCharacter = target;
        swarm.GetAngry(AngrySeconds);
        Plugin.Log.LogInfo($"Ascent 17: a bee swarm ambushes {target.characterName}.");
    }

    /// <summary>
    /// Right next to the scout, in open air. The game's bees drop any target outside their aggro
    /// range, so a swarm appearing farther away would only hover there and disperse.
    /// </summary>
    private static Vector3 SpawnPoint(Character target)
    {
        float distance = FallbackSpawnDistance;
        if (template != null)
        {
            float range = Mathf.Min(template.hiveAggroDistance, Mathf.Min(template.deAggroDistance, template.defaultAggroDistance));
            distance = Mathf.Clamp(range * 0.5f, 1.5f, 5f);
        }
        Vector3 center = target.Center;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 flat = Random.insideUnitCircle.normalized;
            Vector3 candidate = center + new Vector3(flat.x, 0f, flat.y) * distance + Vector3.up;
            if (HelperFunctions.LineCheck(center, candidate, HelperFunctions.LayerType.TerrainMap).transform == null)
            {
                return candidate;
            }
        }
        return center + Vector3.up * 2f;
    }

    /// <summary>The swarm prefab the game's beehives use.</summary>
    private static string PrefabName()
    {
        if (prefabName != null)
        {
            return prefabName;
        }
        foreach (Beehive hive in Beehive.ALL_BEEHIVES)
        {
            if (hive != null && hive.beeSwarmPrefab != null)
            {
                template = hive.beeSwarmPrefab;
                return prefabName = template.gameObject.name;
            }
        }
        foreach (Item item in SingletonAsset<ItemDatabase>.Instance.itemLookup.Values)
        {
            Beehive hive = item != null ? item.GetComponent<Beehive>() : null;
            if (hive != null && hive.beeSwarmPrefab != null)
            {
                template = hive.beeSwarmPrefab;
                return prefabName = template.gameObject.name;
            }
        }
        return FallbackPrefab;
    }

    /// <summary>An ambush swarm belongs to no beehive (the game would otherwise hand it hive number 0).</summary>
    [HarmonyPatch(typeof(BeeSwarm), nameof(BeeSwarm.TryGetBeehive))]
    private static class NoHive
    {
        private static bool Prefix(BeeSwarm __instance)
        {
            if (!IsAmbush(__instance))
            {
                return true;
            }
            __instance.beehiveID = -1;
            return false;
        }
    }

    /// <summary>
    /// Like hive bees, an ambush swarm that lost its target near the edge of its range does not pick
    /// a new one there; it drifts back to where it appeared and disperses.
    /// </summary>
    [HarmonyPatch(typeof(BeeSwarm), nameof(BeeSwarm.UpdateAggro))]
    private static class StayLeashed
    {
        private static bool Prefix(BeeSwarm __instance)
        {
            if (!IsAmbush(__instance) || !__instance.photonView.IsMine)
            {
                return true;
            }
            __instance.maxHiveDistance = Leash;
            __instance.TryDeAggro();
            return __instance.currentAggroCharacter != null || __instance.currentLastSawHiveDistance <= __instance.maxHiveDistance - __instance.hiveAggroDistance;
        }
    }
}
