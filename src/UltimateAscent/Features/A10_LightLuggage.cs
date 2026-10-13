using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>Ascent 10 (More Ascents): big and Explorer's luggage have a 50% chance to contain a single item.</summary>
[HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnItems))]
internal static class A10_LightLuggage
{
    private static void Prefix(Spawner __instance, ref List<Transform> spawnSpots)
    {
        if (!Rules.Enabled(Lvl.LightLuggage) || spawnSpots == null || spawnSpots.Count < 2)
        {
            return;
        }
        string name = __instance.gameObject.name;
        if (__instance is RespawnChest || !(__instance is Luggage) || !(name.StartsWith("LuggageBig") || name.StartsWith("LuggageEpic")))
        {
            return;
        }
        if (Random.Range(0, 2) == 0)
        {
            spawnSpots = new List<Transform> { spawnSpots[0] };
        }
    }
}
