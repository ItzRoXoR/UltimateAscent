using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 19 (More Ascents, extended): no more revive statues, and revives come at a price.
/// <list type="bullet">
/// <item>The statues at the end of each part of the climb (they revive the dead, or hand out an
/// artifact if nobody died) are gone: they stay in the scene (the game still looks them up for
/// campfire bookkeeping) but are invisible, cannot be touched, never revive and never hand out
/// items.</item>
/// <item>Every other way back to life still works (scout effigies, skeletons, the Nadir...), but
/// every revive adds 15 curse, also the ones that give the game's no status at all, and no revive
/// takes petrification away any more (the game removes 75).</item>
/// <item>Scouts start with 20 curse (the game: 10 from ascent 7).</item>
/// </list>
/// </summary>
internal static class A19_NoReviveStatues
{
    private sealed class Hidden
    {
    }

    internal const float StartingCurse = 0.2f;
    internal const float ReviveCurse = 0.15f;

    private static ConditionalWeakTable<RespawnChest, Hidden> hidden = new ConditionalWeakTable<RespawnChest, Hidden>();
    private static float nextScan;

    private static bool On => Rules.Enabled(Lvl.NoReviveStatues);

    internal static void Reset()
    {
        hidden = new ConditionalWeakTable<RespawnChest, Hidden>();
        nextScan = 0f;
    }

    /// <summary>Called every frame; hides newly loaded statues about once a second.</summary>
    internal static void Tick()
    {
        if (!On || Time.unscaledTime < nextScan)
        {
            return;
        }
        nextScan = Time.unscaledTime + 1f;
        List<Luggage> all = Luggage.ALL_LUGGAGE;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is RespawnChest statue && statue != null && !hidden.TryGetValue(statue, out _))
            {
                Hide(statue);
            }
        }
    }

    private static void Hide(RespawnChest statue)
    {
        hidden.Add(statue, new Hidden());
        foreach (Renderer renderer in statue.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            renderer.enabled = false;
        }
        foreach (Collider collider in statue.GetComponentsInChildren<Collider>(includeInactive: true))
        {
            collider.enabled = false;
        }
        Plugin.Debug("Ascent 19: hid a revive statue at " + statue.transform.position);
    }

    [HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.IsInteractible))]
    private static class NotInteractible
    {
        private static bool Prefix(ref bool __result)
        {
            if (!On)
            {
                return true;
            }
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Luggage), nameof(Luggage.IsConstantlyInteractable))]
    private static class NotConstantlyInteractible
    {
        private static bool Prefix(Luggage __instance, ref bool __result)
        {
            if (!On || !(__instance is RespawnChest))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.Interact_CastFinished))]
    private static class NoTouch
    {
        private static bool Prefix()
        {
            return !On;
        }
    }

    [HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.SpawnItems))]
    private static class NoReviveNoItems
    {
        private static bool Prefix(ref List<PhotonView> __result)
        {
            if (!On)
            {
                return true;
            }
            __result = new List<PhotonView>();
            return false;
        }
    }

    [HarmonyPatch(typeof(Ascents), "get_" + nameof(Ascents.startingCurse))]
    private static class Start
    {
        private static void Postfix(ref float __result)
        {
            if (On)
            {
                __result = StartingCurse;
            }
        }
    }

    [HarmonyPatch(typeof(Ascents), "get_" + nameof(Ascents.revivalCurse))]
    private static class Revival
    {
        private static void Postfix(ref float __result)
        {
            if (On)
            {
                __result = ReviveCurse;
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ReviveCharacter))]
    private static class CursedRevives
    {
        private static void Prefix(Character __instance, out int __state)
        {
            __state = __instance.data.petrifyAmount;
        }

        private static void Postfix(Character __instance, bool applyStatus, int __state)
        {
            if (!On || !__instance.IsLocal)
            {
                return;
            }
            if (__instance.data.petrifyAmount < __state)
            {
                __instance.data.SetPetrify(__state);
            }
            if (!applyStatus)
            {
                // Revives that give no status (scout effigies, the Nadir) still curse.
                __instance.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, ReviveCurse);
            }
        }
    }
}
