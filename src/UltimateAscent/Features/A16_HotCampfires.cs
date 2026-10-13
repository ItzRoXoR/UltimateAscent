using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 16 (More Ascents): campfires are extremely hot, so anything cooked on a campfire is
/// charred to ruin. (The slower heat recovery of this ascent lives in <see cref="StatusRecovery"/>.)
/// Every client runs the cooking RPC, so the check uses only shared state: the item is held by a
/// scout standing at a lit campfire.
/// </summary>
[HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.FinishCookingImpl))]
internal static class A16_HotCampfires
{
    private const float CampfireReach = 6f;

    private static void Prefix(ItemCooking __instance)
    {
        if (!Rules.Enabled(Lvl.SlowHeatHotCampfires) || __instance.wreckWhenCooked)
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
/// Ascent 16: lighting a campfire takes only 10 petrification off the scouts beside it (the game: 20).
/// </summary>
internal static class A16_CampfirePetrify
{
    private const float PetrifyHealed = 0.1f;

    [ThreadStatic] private static bool lighting;

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Update))]
    private static class Lighting
    {
        private static void Prefix()
        {
            lighting = Rules.Enabled(Lvl.SlowHeatHotCampfires);
        }

        private static Exception Finalizer(Exception __exception)
        {
            lighting = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AdjustStatus))]
    private static class LessHealing
    {
        private static void Prefix(CharacterAfflictions.STATUSTYPE statusType, ref float amount)
        {
            if (lighting && statusType == CharacterAfflictions.STATUSTYPE.Petrify && amount < 0f)
            {
                amount = -PetrifyHealed;
            }
        }
    }
}
