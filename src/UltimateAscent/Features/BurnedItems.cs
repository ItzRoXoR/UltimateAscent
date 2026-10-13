using System;
using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// "Burned" items: anything cooked to the game's burnt look (3 cooks or more), e.g. food charred
/// on an ascent-16 campfire, things left in lava, or items roasted by the ascent-23 sun. A burned
/// Bing Bong no longer counts for ascent 20.
/// <para>Ascent 17: a freshly burned item is hot. Holding it, or wearing a burned backpack, burns you
/// exactly like standing in a campfire's flames (each source counts on its own), for the first 10
/// seconds after it got burned; after that it has cooled down and is harmless.</para>
/// </summary>
internal static class BurnedItems
{
    /// <summary>The game tints items burnt from the third cook on (<see cref="ItemCooking.GetCookColor"/>).</summary>
    internal const int BurntCookLevel = 3;

    /// <summary>How long a freshly burned item stays hot enough to burn.</summary>
    internal const float HotSeconds = 10f;

    /// <summary>The campfire flames' heat per second (its "HotRadius" emitter) until one has been found in the scene.</summary>
    private const float FallbackHeatPerSecond = 0.1f;

    /// <summary>Status emitters, the campfire flames included, apply their status every half second.</summary>
    private const float TickSeconds = 0.5f;

    /// <summary>When each item (by its instance id, which it keeps in hands, pockets and on the back) got burned.</summary>
    private static readonly Dictionary<Guid, float> burnedAt = new Dictionary<Guid, float>();

    private static float heatPerSecond = -1f;
    private static float nextLookup;
    private static float sinceTick;

    internal static void Reset()
    {
        heatPerSecond = -1f;
        nextLookup = 0f;
        sinceTick = 0f;
        burnedAt.Clear();
    }

    internal static bool IsBurned(Item item)
    {
        return item != null && IsBurned(item.data);
    }

    internal static bool IsBurned(ItemInstanceData data)
    {
        return CookedLevel(data) >= BurntCookLevel;
    }

    /// <summary>Burned less than <see cref="HotSeconds"/> ago: still hot enough to burn.</summary>
    internal static bool IsHot(ItemInstanceData data)
    {
        return IsBurned(data) && burnedAt.TryGetValue(data.guid, out float at) && Time.time - at < HotSeconds;
    }

    internal static int CookedLevel(Item item)
    {
        return item != null ? CookedLevel(item.data) : 0;
    }

    internal static int CookedLevel(ItemInstanceData data)
    {
        return data != null && data.TryGetDataEntry(DataEntryKey.CookedAmount, out IntItemData cooked) ? cooked.Value : 0;
    }

    /// <summary>Notes the moment an item gets burned, whatever cooked it (fire, sun, lava, explosions).</summary>
    [HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.FinishCookingImpl))]
    private static class NoteBurning
    {
        private static void Prefix(ItemCooking __instance, out int __state)
        {
            __state = __instance.item != null ? CookedLevel(__instance.item.data) : 0;
        }

        private static void Postfix(ItemCooking __instance, int __state)
        {
            ItemInstanceData data = __instance.item != null ? __instance.item.data : null;
            if (data != null && __state < BurntCookLevel && IsBurned(data))
            {
                burnedAt[data.guid] = Time.time;
            }
        }
    }

    /// <summary>Called every frame for the local scout.</summary>
    internal static void Tick()
    {
        Character local = Character.localCharacter;
        if (!Rules.Enabled(Lvl.Hazards) || local == null || local.data == null || local.data.dead || local.inAirport)
        {
            sinceTick = 0f;
            return;
        }
        int sources = 0;
        Item held = local.data.currentItem;
        if (held != null && IsHot(held.data))
        {
            sources++;
        }
        BackpackSlot backpack = local.player != null ? local.player.backpackSlot : null;
        if (backpack != null && !backpack.IsEmpty() && IsHot(backpack.data))
        {
            sources++;
        }
        if (sources == 0)
        {
            sinceTick = 0f;
            return;
        }
        sinceTick += Time.deltaTime;
        if (sinceTick < TickSeconds)
        {
            return;
        }
        sinceTick = 0f;
        FindCampfireFlames();
        float rate = heatPerSecond > 0f ? heatPerSecond : FallbackHeatPerSecond;
        local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, rate * TickSeconds * sources);
    }

    /// <summary>Reads the heat of a campfire's flames from the scene, so "as hot as a campfire" stays true.</summary>
    private static void FindCampfireFlames()
    {
        if (heatPerSecond > 0f || Time.unscaledTime < nextLookup)
        {
            return;
        }
        nextLookup = Time.unscaledTime + 5f;
        foreach (Campfire campfire in Campfire.ALL_CAMPFIRES)
        {
            if (campfire == null)
            {
                continue;
            }
            foreach (StatusEmitter emitter in campfire.GetComponentsInChildren<StatusEmitter>(includeInactive: true))
            {
                if (emitter.statusType == CharacterAfflictions.STATUSTYPE.Hot && emitter.amount > 0f)
                {
                    // Read outside the emitter's own update, so ascent 17's stronger emitters do not count twice.
                    heatPerSecond = Overrides.Original(emitter, "amount", emitter.amount);
                    Plugin.Log.LogInfo($"Burned items burn like campfire flames: {heatPerSecond:0.###} heat per second.");
                    return;
                }
            }
        }
    }
}
