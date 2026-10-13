using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 20 (More Ascents): Bing Bong calls upon an evil force if not given attention.
/// Somebody has to be holding Bing Bong: holding it keeps the timer at zero for as long as it is
/// held (More Ascents only reset it when Bing Bong was picked up or put away, so holding it too long
/// let the evil through anyway). After 10 seconds without, the world starts shaking; after 15
/// seconds every scout starts gaining crab status until Bing Bong is held again. Nothing cures that
/// crab status: not cure-alls, not Pandora's lunchbox (whose reshuffle never touched it). A burned Bing Bong
/// (cooked three times or more; at ascent 16 one campfire cooking does it) never counts, and while
/// freshly burned it burns the hand that holds it (<see cref="BurnedItems"/>). The timer is paused
/// while the scouts are still waking up on the beach.
/// </summary>
internal static class A20_BingBong
{
    private const float WarningAfter = 10f;
    private const float PunishAfter = 15f;
    private const float CrabPerSecond = 0.05f;

    private static float neglectedFor;

    internal static void Reset()
    {
        neglectedFor = 0f;
    }

    internal static void Tick()
    {
        Character local = Character.localCharacter;
        if (!Rules.Enabled(Lvl.BingBong) || local == null || local.data == null || local.data.dead)
        {
            neglectedFor = 0f;
            return;
        }
        if (local.data.passedOutOnTheBeach > 0f || SomeoneHoldsBingBong())
        {
            neglectedFor = 0f;
            return;
        }
        neglectedFor += Time.deltaTime;
        if (neglectedFor > WarningAfter && neglectedFor <= PunishAfter && GamefeelHandler.instance != null)
        {
            GamefeelHandler.instance.AddPerlinShake(0.7f, 0.15f);
        }
        if (neglectedFor >= PunishAfter)
        {
            local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Crab, CrabPerSecond * Time.deltaTime);
        }
    }

    /// <summary>Cure-alls leave the crab status alone (it runs before ascent 14's partial cure-all, which reads the same list).</summary>
    [HarmonyPatch(typeof(Action_ClearAllStatus), nameof(Action_ClearAllStatus.RunAction))]
    private static class CrabStays
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Action_ClearAllStatus __instance, out bool __state)
        {
            __state = Rules.Enabled(Lvl.BingBong) && !__instance.otherExclusions.Contains(CharacterAfflictions.STATUSTYPE.Crab);
            if (__state)
            {
                __instance.otherExclusions.Add(CharacterAfflictions.STATUSTYPE.Crab);
            }
        }

        private static Exception Finalizer(Action_ClearAllStatus __instance, bool __state, Exception __exception)
        {
            if (__state)
            {
                __instance.otherExclusions.Remove(CharacterAfflictions.STATUSTYPE.Crab);
            }
            return __exception;
        }
    }

    private static bool SomeoneHoldsBingBong()
    {
        foreach (Character character in Character.AllCharacters)
        {
            if (character == null || character.isBot || character.data == null || character.data.dead)
            {
                continue;
            }
            Item item = character.data.currentItem;
            if (item != null && (item.itemTags & Item.ItemTags.BingBong) != 0 && !BurnedItems.IsBurned(item))
            {
                return true;
            }
        }
        return false;
    }
}
