using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 13 (More Ascents): butter fingers. Taking fall damage drops every item, including the
/// backpack. Triggered from <see cref="FallDamage"/>.
/// <para>The game makes the dropped pocket items and the backpack right beside the scout's hips, one above
/// the other, at the moment of the landing, while the scout's body is still tumbling from it, and the body
/// crashes into them. The item from the hand passes through the scout for 0.5 s, and after that it collides
/// again even if it is inside the scout by then: the physics engine then shoots it out at its top speed for
/// that (10 m/s), depending on how the scout landed. So at this ascent, items a scout drops all at once
/// pass through that scout for at least 1 s and until the scout is clear of them; on the host, whose game
/// moves dropped items.</para>
/// </summary>
internal static class A13_ButterFingers
{
    /// <summary>Items pass through the scout who dropped them for at least this long...</summary>
    private const float ClearSeconds = 1f;

    /// <summary>...and while they overlap them, up to this long.</summary>
    private const float MaxClearSeconds = 60f;

    /// <summary>Off: the game's way, the items collide with the scout at once (the tests switch it off to compare).</summary>
    internal static bool KeepClear = true;

    private sealed class Clear
    {
        internal Character character;
        internal PhotonView item;
        internal Collider[] itemColliders;
        internal float since;
    }

    private static readonly List<Clear> clears = new List<Clear>();

    /// <summary>Dropped items still passing through their scout (for the tests).</summary>
    internal static int Clearing => clears.Count;

    internal static void DropEverything(Character character)
    {
        if (!Rules.Owns(character) || character.refs?.items == null || character.data.dead)
        {
            return;
        }
        character.refs.items.DropAllItems(includeBackpack: true);
    }

    internal static void Reset()
    {
        clears.Clear();
    }

    internal static void Tick()
    {
        for (int i = clears.Count - 1; i >= 0; i--)
        {
            Clear clear = clears[i];
            List<Collider> body = clear.character != null ? clear.character.refs.ragdoll.colliderList : null;
            if (clear.item == null || body == null)
            {
                clears.RemoveAt(i);
                continue;
            }
            float time = Time.time - clear.since;
            if (time >= MaxClearSeconds || (time >= ClearSeconds && !Overlap(clear.itemColliders, body)))
            {
                Ignore(clear.itemColliders, body, false);
                clears.RemoveAt(i);
            }
        }
    }

    private static void Ignore(Collider[] item, List<Collider> body, bool ignore)
    {
        foreach (Collider part in body)
        {
            if (part == null)
            {
                continue;
            }
            foreach (Collider collider in item)
            {
                if (collider != null)
                {
                    Physics.IgnoreCollision(part, collider, ignore);
                }
            }
        }
    }

    private static bool Overlap(Collider[] item, List<Collider> body)
    {
        foreach (Collider collider in item)
        {
            if (collider == null || !collider.enabled)
            {
                continue;
            }
            Bounds bounds = collider.bounds;
            bounds.Expand(0.1f);
            foreach (Collider part in body)
            {
                if (part != null && part.enabled && bounds.Intersects(part.bounds))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>The host made an item its scout dropped with everything else: it passes through them for now.</summary>
    private static void Track(CharacterItems items, int before)
    {
        if (!KeepClear || !PhotonNetwork.IsMasterClient || !Rules.Enabled(Lvl.ButterFingers) || items == null || items.character == null)
        {
            return;
        }
        for (int i = before; i < items.droppedItems.Count; i++)
        {
            PhotonView view = items.droppedItems[i];
            if (view == null)
            {
                continue;
            }
            Clear clear = new Clear { character = items.character, item = view, itemColliders = view.GetComponentsInChildren<Collider>(true), since = Time.time };
            Ignore(clear.itemColliders, items.character.refs.ragdoll.colliderList, true);
            // The game's own 0.5 s for the item from the hand ends here: this takes it over (it would let it collide
            // again inside the scout).
            if (GameUtils.instance != null)
            {
                GameUtils.instance.ignoredCollidersCache.RemoveAll(entry => Array.IndexOf(clear.itemColliders, entry.colliderA) >= 0 || Array.IndexOf(clear.itemColliders, entry.colliderB) >= 0);
            }
            clears.Add(clear);
        }
    }

    /// <summary>A pocket item or the backpack, dropped with everything else (the game adds it to the scout's dropped items).</summary>
    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.DropItemFromSlotRPC))]
    private static class SlotDrop
    {
        private static void Prefix(CharacterItems __instance, out int __state)
        {
            __state = __instance.droppedItems.Count;
        }

        private static void Postfix(CharacterItems __instance, int __state)
        {
            Track(__instance, __state);
        }
    }

    /// <summary>The item in the hand, dropped with everything else (only then the game adds it to the dropped items).</summary>
    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.DropItemRpc))]
    private static class HandDrop
    {
        private static void Prefix(CharacterItems __instance, out int __state)
        {
            __state = __instance.droppedItems.Count;
        }

        private static void Postfix(CharacterItems __instance, int __state)
        {
            Track(__instance, __state);
        }
    }
}
