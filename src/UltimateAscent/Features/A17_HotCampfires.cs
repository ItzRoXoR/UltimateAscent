using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17 (More Ascents): campfires are extremely hot, so anything cooked on a campfire is
/// charred to ruin; so is anything cooked over lava or by an explosion (see
/// <see cref="A17_LavaAndBlastCooking"/>).
/// Every client runs the cooking RPC, so the check uses only shared state: the item is held by a
/// scout standing at a lit campfire.
/// </summary>
[HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.FinishCookingImpl))]
internal static class A17_HotCampfires
{
    private const float CampfireReach = 6f;

    private static void Prefix(ItemCooking __instance)
    {
        if (!Rules.Enabled(Lvl.HotCampfires) || __instance.wreckWhenCooked)
        {
            return;
        }
        Item item = __instance.item;
        Character holder = item != null ? item.holderCharacter : null;
        if (holder == null || !NearLitCampfire(holder.Center))
        {
            return;
        }
        __instance.wreckWhenCooked = true;
    }

    private static bool NearLitCampfire(Vector3 position)
    {
        foreach (Campfire campfire in Campfire.ALL_CAMPFIRES)
        {
            if (campfire != null && campfire.Lit && Vector3.Distance(campfire.transform.position, position) <= CampfireReach)
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Ascent 17: lava and explosions char what they cook to ruin on the first cooking cycle, like
/// campfires. Lava (host) and explosions (the item's owner) cook on one client, which first tells
/// everybody, through the item's own cooking message, that this cooking ruins the item.
/// </summary>
internal static class A17_LavaAndBlastCooking
{
    /// <summary>A cooking message the game does not use: "the next cooking chars this item".</summary>
    private const ItemCooking.NetworkedCooking Char = (ItemCooking.NetworkedCooking)85;

    /// <summary>Lava or an explosion is cooking items right now (on this client).</summary>
    [ThreadStatic] private static int scorching;

    private static void Enter()
    {
        scorching++;
    }

    private static Exception Leave(Exception exception)
    {
        scorching--;
        return exception;
    }

    [HarmonyPatch(typeof(Lava), nameof(Lava.TryCookItems))]
    private static class LavaCooking
    {
        private static void Prefix() => Enter();

        private static Exception Finalizer(Exception __exception) => Leave(__exception);
    }

    [HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    private static class BlastCooking
    {
        private static void Prefix() => Enter();

        private static Exception Finalizer(Exception __exception) => Leave(__exception);
    }

    [HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.FinishCooking))]
    private static class Scorch
    {
        private static void Prefix(ItemCooking __instance)
        {
            if (scorching <= 0 || !Rules.Enabled(Lvl.HotCampfires) || __instance.wreckWhenCooked || __instance.photonView == null || !__instance.photonView.AmController)
            {
                return;
            }
            // Sent before the game's own cooking message, so it arrives first everywhere.
            __instance.photonView.RPC("SyncCookingStateRPC", Photon.Pun.RpcTarget.All, Char);
        }
    }

    [HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.SyncCookingStateRPC))]
    private static class Charred
    {
        private static bool Prefix(ItemCooking __instance, ItemCooking.NetworkedCooking newState)
        {
            if (newState != Char)
            {
                return true;
            }
            __instance.wreckWhenCooked = true;
            return false;
        }
    }
}
