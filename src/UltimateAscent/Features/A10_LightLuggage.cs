using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 10 (More Ascents): big and Explorer's luggage have a 50% chance to contain a single item
/// (not True Final Ascent's supply drops, which are its own).
/// </summary>
[HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnItems))]
internal static class A10_LightLuggage
{
    /// <summary>A big or Explorer's suitcase this rule lightens (on the ascents it holds).</summary>
    internal static bool Lightens(Spawner spawner)
    {
        if (!Rules.Enabled(Lvl.LightLuggage) || spawner == null)
        {
            return false;
        }
        string name = spawner.gameObject.name;
        return !(spawner is RespawnChest) && spawner is Luggage && (name.StartsWith("LuggageBig") || name.StartsWith("LuggageEpic")) && !Compat.Tfa.Spawned(spawner);
    }

    private static void Prefix(Spawner __instance, ref List<Transform> spawnSpots)
    {
        if (spawnSpots == null || spawnSpots.Count < 2 || !Lightens(__instance))
        {
            return;
        }
        if (Random.Range(0, 2) == 0)
        {
            spawnSpots = new List<Transform> { spawnSpots[0] };
        }
    }
}
