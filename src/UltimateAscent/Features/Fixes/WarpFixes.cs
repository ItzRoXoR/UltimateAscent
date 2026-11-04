using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Peak;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features.Fixes;

/// <summary>
/// Game bugs of the Warp Fungus, fixed on every ascent (the user's list of 2026-11-04).
/// <list type="bullet">
/// <item>Warped onto a jumping mushroom in the middle of a fall, a scout bounced as hard as if they had fallen all the
/// way onto it (up to 7.5 times): the warp sets them down still, but their time in the air ran on until it had
/// finished. Now it starts again where the warp sets them down, and they bounce as from a standstill.</item>
/// <item>A warp could put a scout inside the rock, or past it out of the map: it goes to where the fungus hit, a set
/// distance off the surface, without a look whether there is room for the scout there (in a corner the other face is
/// right there). Now a warp goes only where there is room for the scout's body, moving off the surface (or up) to find
/// it, and fizzles if there is none. A fungus thrown out of the rock it was pressed into is the other half (see
/// <see cref="ItemFixes.ClearDrop"/>).</item>
/// <item>A Warp Fungus an explosion reached (the Tropics' bombs) flew off far too hard: an explosion throws an item as
/// much harder as it is lighter, once for each of its colliders. It is now thrown once, as hard as a scout there.</item>
/// <item>A warp could pass the black wall the Nadir has until the Scoutmaster's soul is freed, past the freeing that
/// starts the ghosts' rise. No warp now goes through that wall while it stands.</item>
/// </list>
/// </summary>
internal static class WarpFixes
{
    /// <summary>The room a warped scout needs: their middle and head, this wide, free of the land.</summary>
    private const float BodyRadius = 0.3f;
    private const float HeadAbove = 0.45f;
    private const float HeadRadius = 0.22f;

    /// <summary>How far off the surface (or up) a warp may move to find room, in steps of this.</summary>
    private const float RoomReach = 1.5f;
    private const float RoomStep = 0.25f;

    /// <summary>For the tests: warps moved to find room, warps that fizzled for want of it or at the Nadir's wall, fungi an explosion threw as a scout.</summary>
    internal static int WarpsMoved;
    internal static int WarpsFizzled;
    internal static int BlastsEased;
    internal static Vector3 LastWarp;

    internal static void Reset()
    {
        walls.Clear();
        wallsFound = float.NegativeInfinity;
    }

    [HarmonyPatch(typeof(CharacterRagdoll), nameof(CharacterRagdoll.ToggleCollision))]
    private static class ArriveStill
    {
        private static void Prefix(bool enableCollision, Character ___character)
        {
            if (enableCollision && ___character != null && ___character.warping && ___character.data != null)
            {
                // Set down still: no time in the air to bounce or fall by.
                ___character.data.sinceGrounded = 0f;
            }
        }
    }

    /// <summary>Whether the scout's body has room at this spot (their middle there): the land neither at their middle nor at their head.</summary>
    private static bool Room(Vector3 middle) =>
        !Physics.CheckSphere(middle, BodyRadius, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)
        && !Physics.CheckSphere(middle + Vector3.up * HeadAbove, HeadRadius, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore);

    /// <summary>Room for a warped scout at or near a spot: off the surfaces the fungus touched, or up; false if there is none within reach.</summary>
    internal static bool FindRoom(ref Vector3 spot, IEnumerable<Vector3> normals)
    {
        if (Room(spot))
        {
            return true;
        }
        Vector3 away = normals.Aggregate(Vector3.zero, (sum, n) => sum + n);
        List<Vector3> ways = new List<Vector3>();
        if (away.sqrMagnitude > 1e-4f)
        {
            ways.Add(away.normalized);
        }
        ways.AddRange(normals.Where(n => n.sqrMagnitude > 1e-4f).Select(n => n.normalized));
        ways.Add(Vector3.up);
        for (float out_ = RoomStep; out_ <= RoomReach + 1e-3f; out_ += RoomStep)
        {
            foreach (Vector3 way in ways)
            {
                Vector3 there = spot + way * out_;
                if (Room(there))
                {
                    spot = there;
                    return true;
                }
            }
        }
        return false;
    }

    private static readonly List<VoidInvisWall> walls = new List<VoidInvisWall>();
    private static float wallsFound = float.NegativeInfinity;

    /// <summary>Whether the line between two spots passes the Nadir's black wall while it stands (before the soul is freed).</summary>
    internal static bool ThroughSoulWall(Vector3 from, Vector3 to)
    {
        if (Time.time - wallsFound > 2f)
        {
            wallsFound = Time.time;
            walls.Clear();
            walls.AddRange(UnityEngine.Object.FindObjectsByType<VoidInvisWall>(FindObjectsSortMode.None));
        }
        Vector3 way = to - from;
        float length = way.magnitude;
        if (length < 1e-4f)
        {
            return false;
        }
        foreach (VoidInvisWall wall in walls)
        {
            if (wall == null || wall.wall == null || !wall.wall.activeInHierarchy)
            {
                continue;
            }
            foreach (Collider part in wall.wall.GetComponentsInChildren<Collider>())
            {
                if (part.enabled && (part.Raycast(new Ray(from, way / length), out _, length) || part.Raycast(new Ray(to, -way / length), out _, length)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    [HarmonyPatch(typeof(WarpOnThrow), "OnCollisionEnter")]
    private static class RoomToLand
    {
        private static bool Prefix(WarpOnThrow __instance, Collision collision, ref bool ___warped)
        {
            Item item = __instance.item;
            // Where the game warps (as it does, on the fungus's own game): the rest it leaves to the game.
            if (___warped || item == null || collision == null || item.itemState != ItemState.Ground || !__instance.photonView.IsMine || !item.lastThrownCharacter
                || (HelperFunctions.terrainMapMask & (1 << collision.gameObject.layer)) == 0 || collision.relativeVelocity.magnitude <= __instance.minVelocity)
            {
                return true;
            }
            float flown = Time.time - item.lastThrownTime;
            if (!(flown > __instance.minTime && flown < __instance.maxTime) || collision.contactCount == 0)
            {
                return true;
            }
            Character thrower = item.lastThrownCharacter;
            ContactPoint first = collision.GetContact(0);
            Vector3 target = first.point + first.normal * __instance.moveAwayFromWallDistance;
            Vector3 wanted = target;
            List<Vector3> normals = Enumerable.Range(0, collision.contactCount).Select(i => collision.GetContact(i).normal).ToList();
            if (ThroughSoulWall(thrower.Center, target) || !FindRoom(ref target, normals))
            {
                // No warp: the fungus lies where it fell, spent.
                WarpsFizzled++;
                ___warped = true;
                return false;
            }
            if (target != wanted)
            {
                WarpsMoved++;
            }
            LastWarp = target;
            thrower.photonView.RPC("WarpPlayerRPC", RpcTarget.All, target, true);
            PhotonNetwork.Destroy(__instance.gameObject);
            ___warped = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    private static class BlastThrowsFungusAsAScout
    {
        [ThreadStatic] private static AOE blast;
        [ThreadStatic] private static HashSet<Rigidbody> thrown;

        private static void Prefix(AOE __instance, out (AOE blast, HashSet<Rigidbody> thrown) __state)
        {
            __state = (blast, thrown);
            blast = __instance;
            thrown = new HashSet<Rigidbody>();
        }

        private static Exception Finalizer((AOE blast, HashSet<Rigidbody> thrown) __state, Exception __exception)
        {
            (blast, thrown) = __state;
            return __exception;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo add = AccessTools.Method(typeof(Rigidbody), nameof(Rigidbody.AddForce), new[] { typeof(Vector3), typeof(ForceMode) });
            MethodInfo push = AccessTools.Method(typeof(BlastThrowsFungusAsAScout), nameof(Push));
            foreach (CodeInstruction instruction in code)
            {
                yield return instruction.Calls(add) ? new CodeInstruction(OpCodes.Call, push).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction) : instruction;
            }
        }

        /// <summary>
        /// The explosion's throw of an item. A Warp Fungus is thrown once, and as hard as a scout there would be (an
        /// explosion pushes a scout's body as hard for one physics step); everything else as in the game.
        /// </summary>
        private static void Push(Rigidbody rig, Vector3 force, ForceMode mode)
        {
            if (rig == null)
            {
                return;
            }
            if (rig.GetComponent<WarpOnThrow>() == null)
            {
                rig.AddForce(force, mode);
                return;
            }
            if (thrown != null && !thrown.Add(rig))
            {
                return;
            }
            BlastsEased++;
            float items = blast != null ? Mathf.Max(blast.itemKnockbackMultiplier, 1e-3f) : 1f;
            rig.AddForce(force / items * Time.fixedDeltaTime, ForceMode.VelocityChange);
        }
    }
}
