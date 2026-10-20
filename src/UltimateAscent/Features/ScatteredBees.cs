using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: twenty bee swarms without hives wait on the peak's grassy hill (<see cref="PeakHill"/>), 2.5 m above
/// its ground, 8 m apart and away from the zombies' places; the host puts them out when it finds the hill (as a scout
/// gets near it, see <see cref="PeakZombies"/>). A swarm hovers where it is until a scout comes near. Then it is a
/// tropical ambush swarm (<see cref="A17_BeeAmbush"/>): angry for 50 s, it follows up to 120 m from its place, and
/// once it has lost the scout it disperses. The game's beehives and the Tropics' ambushes are as before.
/// </summary>
internal static class ScatteredBees
{
    internal const string Marker = "UltimateAscent.bees.waiting";
    internal const int Count = 20;
    private const float Apart = 8f;
    private const float AboveGround = 2.5f;

    /// <summary>The swarms that have gone for a scout already (on their owner's game): those disperse like ambush swarms.</summary>
    private static readonly HashSet<BeeSwarm> roused = new HashSet<BeeSwarm>();

    /// <summary>The places the swarms were put out at on this game (host); for the tests.</summary>
    internal static readonly List<Vector3> Placed = new List<Vector3>();

    internal static bool IsWaiting(BeeSwarm swarm)
    {
        object[] data = swarm != null && swarm.photonView != null ? swarm.photonView.InstantiationData : null;
        return data != null && data.Length > 0 && data[0] as string == Marker;
    }

    internal static void Reset()
    {
        roused.Clear();
        Placed.Clear();
    }

    /// <summary>The host puts the hill's swarms out (the hill's ground, without the zombies' places).</summary>
    internal static void SpawnOnHill(List<Vector3> ground, IList<Vector3> zombies)
    {
        if (!PhotonNetwork.IsMasterClient || Placed.Count > 0)
        {
            return;
        }
        foreach (Vector3 spot in PeakHill.Spread(ground, Count, Apart, zombies))
        {
            PhotonNetwork.Instantiate(A17_BeeAmbush.PrefabName(), spot + Vector3.up * AboveGround, Quaternion.identity, 0, new object[] { Marker });
            Placed.Add(spot);
        }
        Plugin.Log.LogInfo($"Ascent 17: {Placed.Count} waiting bee swarms on the peak's hill.");
    }

    /// <summary>A waiting swarm stays where it is (without a hive the game's bees would disperse after a few seconds).</summary>
    [HarmonyPatch(typeof(BeeSwarm), "UpdateDisperse")]
    private static class Waits
    {
        private static bool Prefix(BeeSwarm __instance) => !IsWaiting(__instance) || roused.Contains(__instance);
    }

    /// <summary>The first scout it goes for rouses it: angry as an ambush swarm, and it disperses once it has lost them.</summary>
    [HarmonyPatch(typeof(BeeSwarm), "UpdateAggro")]
    private static class Roused
    {
        private static void Postfix(BeeSwarm __instance)
        {
            if (__instance.photonView == null || !__instance.photonView.IsMine || __instance.currentAggroCharacter == null || !IsWaiting(__instance) || roused.Contains(__instance))
            {
                return;
            }
            roused.Add(__instance);
            __instance.GetAngry(A17_BeeAmbush.AngrySeconds);
            Plugin.Debug($"Ascent 17: a waiting bee swarm goes for {__instance.currentAggroCharacter.characterName}.");
        }
    }
}
