using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features.Fixes;

/// <summary>
/// Game bugs of items, fixed on every ascent (the user's list of 2026-11-04), and the balloons' new pops.
/// <list type="bullet">
/// <item>A dropped or thrown item could end up inside the rock: the host makes it appear right where the hand held it,
/// and a held item pressed into a corner sits in the rock (a held item is pulled to the hand by a spring); what drops
/// out of the other slots (by the item-dropping rules, a scout passing out, the backpack among them, stacked half a
/// metre apart over the hip) and the backpack a scout drops to carry another appear where the game puts them, in
/// rock or not. Now every such item appears there only if nothing solid lies between the scout's middle and there;
/// otherwise just short of what does (several at once spaced along the way). The Warp Fungus's warps through
/// corners start here too (see <see cref="WarpFixes"/>).</item>
/// <item>A stick of dynamite could explode twice: its fuse and cooking set it off by separate ways, and a cooked stick
/// taken out of a backpack again exploded anew. Each stick now explodes once.</item>
/// <item>A checkpoint flag brought a scout back with none of the statuses it kept if they died invincible (milk, a
/// Shroomberry, a gem, Bing Bong's shield): the flag puts them back as any status is added, which invincibility keeps
/// off. The flag's statuses and thorns now come back; the invincibility goes on after.</item>
/// <item>Tied balloons now pop when they touch lava (a lava river, a lava pillar, or under the lava's surface) and in
/// an explosion that reaches them (the user's).</item>
/// </list>
/// </summary>
internal static class ItemFixes
{
    /// <summary>How wide an item's way from the scout's middle to the hand is looked along for something solid.</summary>
    private const float DropRadius = 0.12f;

    /// <summary>How far short of what is in the way an item then appears, and how far apart several moved at once are kept.</summary>
    private const float DropShort = 0.05f;
    private const float DropApart = 0.25f;

    /// <summary>Where items were put short this frame (several dropped at once are spaced along their way).</summary>
    private static readonly List<Vector3> movedThisFrame = new List<Vector3>();
    private static int movedFrame = -1;

    /// <summary>For the tests: drops put short of something solid, second explosions held back, flags that brought statuses back past invincibility, balloons popped by lava or blasts.</summary>
    internal static int DropsMoved;
    internal static int RepeatBlasts;
    internal static int FlagsPastInvincibility;
    internal static int BalloonsPopped;
    internal static string LastPop = "";

    private static readonly HashSet<string> exploded = new HashSet<string>();

    internal static void Reset()
    {
        exploded.Clear();
        lavas.Clear();
        lavasFound = float.NegativeInfinity;
    }

    private static readonly RaycastHit[] dropHits = new RaycastHit[16];

    private static int DropMask => HelperFunctions.AllPhysicalExceptCharacter | LayerMask.GetMask("InvisWall");

    /// <summary>Where an item dropped at this spot appears: there, or just short of the first thing solid between the dropper's middle and there.</summary>
    internal static Vector3 ClearDrop(Character dropper, Vector3 spot)
    {
        if (dropper == null)
        {
            return spot;
        }
        Vector3 from = dropper.Center;
        Vector3 way = spot - from;
        float length = way.magnitude;
        if (length < DropShort)
        {
            return spot;
        }
        Vector3 direction = way / length;
        Item held = dropper.data.currentItem;
        float nearest = float.PositiveInfinity;
        int count = Physics.SphereCastNonAlloc(from, DropRadius, direction, dropHits, length, DropMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = dropHits[i];
            // What the sphere is already in at the scout's middle comes back at 0 with no point: their own body, what they hold.
            if (hit.collider == null || (hit.distance <= 0f && hit.point == Vector3.zero) || hit.collider.GetComponentInParent<Character>() != null
                || (held != null && hit.collider.transform.IsChildOf(held.transform)))
            {
                continue;
            }
            nearest = Mathf.Min(nearest, hit.distance);
        }
        if (float.IsPositiveInfinity(nearest))
        {
            return spot;
        }
        DropsMoved++;
        if (movedFrame != Time.frameCount)
        {
            movedFrame = Time.frameCount;
            movedThisFrame.Clear();
        }
        float reach = Mathf.Max(0f, nearest - DropShort);
        Vector3 put = from + direction * reach;
        while (reach > 0f && movedThisFrame.Any(p => Vector3.Distance(p, put) < DropApart))
        {
            reach = Mathf.Max(0f, reach - DropApart);
            put = from + direction * reach;
        }
        movedThisFrame.Add(put);
        return put;
    }

    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.DropItemRpc))]
    private static class ClearDropFromHand
    {
        private static void Prefix(CharacterItems __instance, ref Vector3 spawnPos)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                spawnPos = ClearDrop(__instance.character, spawnPos);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.DropItemFromSlotRPC))]
    private static class ClearDropFromSlot
    {
        private static void Prefix(CharacterItems __instance, ref Vector3 spawnPosition)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                spawnPosition = ClearDrop(__instance.character, spawnPosition);
            }
        }
    }

    /// <summary>The scout whose backpack the game drops as they pick up another scout (the host makes it appear at the carried scout's middle).</summary>
    [ThreadStatic] private static Character carrying;

    [HarmonyPatch(typeof(CharacterCarrying), nameof(CharacterCarrying.RPCA_StartCarry))]
    private static class BackpackDroppedToCarry
    {
        private static void Prefix(Character ___character, out Character __state)
        {
            __state = carrying;
            carrying = ___character;
        }

        private static Exception Finalizer(Character __state, Exception __exception)
        {
            carrying = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PhotonNetwork), nameof(PhotonNetwork.InstantiateItemRoom))]
    private static class ClearCarryDrop
    {
        private static void Prefix(ref Vector3 position)
        {
            if (carrying != null)
            {
                position = ClearDrop(carrying, position);
            }
        }
    }

    /// <summary>Whether this is a stick's first explosion on this game (a stick goes by its instance data, the same in and out of a backpack).</summary>
    private static bool FirstBlast(Item item)
    {
        if (item == null)
        {
            return true;
        }
        string stick = item.data != null && item.data.guid != Guid.Empty ? item.data.guid.ToString() : "view:" + item.GetInstanceID();
        return exploded.Add(stick);
    }

    [HarmonyPatch(typeof(Dynamite), "RPC_Explode")]
    private static class DynamiteOnce
    {
        private static bool Prefix(Dynamite __instance)
        {
            if (FirstBlast(__instance.item))
            {
                return true;
            }
            RepeatBlasts++;
            __instance.gameObject.SetActive(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemCooking), "CookingExplodeImpl")]
    private static class CookedDynamiteOnce
    {
        private static void Prefix(ItemCooking __instance, out GameObject __state)
        {
            __state = null;
            if (__instance.GetComponent<Dynamite>() == null || FirstBlast(__instance.item))
            {
                return;
            }
            // A stick that has exploded already: only the clearing up (out of the hand, off the map), no second blast.
            RepeatBlasts++;
            __state = __instance.explosionPrefab;
            __instance.explosionPrefab = null;
        }

        private static Exception Finalizer(ItemCooking __instance, GameObject __state, Exception __exception)
        {
            if (__state != null)
            {
                __instance.explosionPrefab = __state;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.TryCheckpoint))]
    private static class FlagPastInvincibility
    {
        private static void Prefix(Character __instance, out bool __state)
        {
            __state = __instance.data.isInvincible && __instance.WillDoCheckpoint(out _);
            if (__state)
            {
                __instance.data.isInvincible = false;
                __instance.data.isInvincibleMilk = false;
                FlagsPastInvincibility++;
            }
        }

        private static Exception Finalizer(Character __instance, bool __state, Exception __exception)
        {
            if (__state)
            {
                __instance.data.RecalculateInvincibility();
            }
            return __exception;
        }
    }

    private static void Pop(TiedBalloon balloon, string why)
    {
        if (balloon == null || !balloon.photonView.IsMine)
        {
            return;
        }
        BalloonsPopped++;
        LastPop = why;
        balloon.Pop();
    }

    [HarmonyPatch(typeof(TiedBalloonCollision), "OnCollisionEnter")]
    private static class BalloonPopsOnLava
    {
        private static void Postfix(TiedBalloonCollision __instance, Collision collision)
        {
            if (collision != null && collision.collider != null && ClimbFixes.IsLava(collision.collider.GetComponentInParent<CollisionModifier>()))
            {
                Pop(__instance.tiedBalloon, "lava");
            }
        }
    }

    private static readonly List<Lava> lavas = new List<Lava>();
    private static float lavasFound = float.NegativeInfinity;
    private static readonly AccessTools.FieldRef<Lava, Bounds> LavaBounds = AccessTools.FieldRefAccess<Lava, Bounds>("bounds");

    /// <summary>Whether a spot is under a lava's surface, inside its area (as the game takes a scout to be in it).</summary>
    internal static bool InLava(Vector3 spot)
    {
        if (Time.time - lavasFound > 2f)
        {
            lavasFound = Time.time;
            lavas.Clear();
            lavas.AddRange(UnityEngine.Object.FindObjectsByType<Lava>(FindObjectsSortMode.None));
        }
        foreach (Lava lava in lavas)
        {
            if (lava == null || !lava.isActiveAndEnabled)
            {
                continue;
            }
            Bounds area = LavaBounds(lava);
            if (area.size != Vector3.zero && spot.y < lava.transform.position.y && spot.x >= area.min.x && spot.x <= area.max.x && spot.z >= area.min.z && spot.z <= area.max.z)
            {
                return true;
            }
        }
        return false;
    }

    [HarmonyPatch(typeof(TiedBalloon), "FixedUpdate")]
    private static class BalloonPopsInLava
    {
        private static void Postfix(TiedBalloon __instance)
        {
            if (__instance != null && __instance.photonView.IsMine && __instance.rb != null && InLava(__instance.rb.position))
            {
                Pop(__instance, "lava");
            }
        }
    }

    /// <summary>A blast: an area effect that throws what it reaches (not a Shroomberry's spores, a cooked Toxic Drink's cloud, a Mandrake's scream, nor what True Final Ascent's events set off).</summary>
    private static bool IsBlast(AOE aoe) => aoe != null && aoe.range > 0f && aoe.knockback > 0f && !A14_Shroomberries.IsSporeBurst(aoe) && !ToxicDrink.IsBurst(aoe)
        && aoe.GetComponentInParent<Mandrake>() == null && !Compat.Tfa.Spawned(aoe) && !Compat.Tfa.Causing;

    [HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    private static class BalloonPopsInBlast
    {
        private static void Postfix(AOE __instance)
        {
            if (!IsBlast(__instance))
            {
                return;
            }
            Vector3 at = __instance.transform.position;
            foreach (Character scout in Character.AllCharacters.Where(c => c != null && c.refs != null && c.refs.balloons != null))
            {
                foreach (TiedBalloon balloon in scout.refs.balloons.tiedBalloons.ToList())
                {
                    if (balloon != null && balloon.rb != null && Vector3.Distance(balloon.rb.position, at) <= __instance.range)
                    {
                        Pop(balloon, "blast");
                    }
                }
            }
        }
    }
}
