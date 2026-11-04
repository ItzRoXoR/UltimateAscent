using System;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace UltimateAscent.Features.Fixes;

/// <summary>
/// The Scoutmaster's bugs, fixed on every ascent (the user's list of 2026-11-04).
/// <list type="bullet">
/// <item>A scout on a piton, a rope or a vine could not be pulled off: his grab holds them, but the piton, rope or vine
/// pulls them back every physics step (the shaking), and nothing in his grab made them let go. Now a scout he grabs
/// lets go of it (as the game does when a frog's tongue or a Rescue Claw takes them) and cannot take hold of one while
/// he holds them (while his grip on them is there); his throw then goes as on a wall.</item>
/// <item>His throw could break off for good, and every grab after it held a scout with no throw and no harm: the throw
/// looks to his target all through it, and a target let go of meanwhile (or a scout let go of) broke it with him still
/// "throwing". It now ends cleanly, letting go of whoever he holds.</item>
/// <item>He turned round and round: looking nearly straight up (a scout above him), his facing went any way, and the
/// facing other players see went the long way round each time it passed north. His facing is now kept on the turn it
/// is on, and kept as it was while the scout is right above him.</item>
/// </list>
/// </summary>
internal static class ScoutmasterFixes
{
    /// <summary>A scout this near across (whatever the height) leaves his facing as it was.</summary>
    private const float AboveHim = 1f;

    /// <summary>For the tests: scouts pulled off a hold, throws that ended cleanly, holds refused while he holds the scout.</summary>
    internal static int PulledOff;
    internal static int ThrowsEnded;
    internal static int HoldsRefused;

    /// <summary>
    /// He holds the scout: his grip on them is there. (When he stops reaching, as on a wall, the game takes his grip away
    /// but leaves the scout marked as held by him, a bot whom no jump gets free of; a scout so marked is free.)
    /// </summary>
    private static bool HeldByHim(Character scout)
    {
        Character him = scout != null ? scout.data.grabbingPlayer : null;
        return him != null && him.data.isScoutmaster && him.data.grabbedPlayer == scout && him.data.grabJoint != null;
    }

    private static bool MayHold(Character scout)
    {
        if (!HeldByHim(scout))
        {
            return true;
        }
        HoldsRefused++;
        return false;
    }

    [HarmonyPatch(typeof(CharacterGrabbing), nameof(CharacterGrabbing.RPCA_GrabAttach))]
    private static class LetGoWhenGrabbed
    {
        private static void Postfix(Character ___character, PhotonView view)
        {
            Character scout = view != null ? view.GetComponent<Character>() : null;
            if (___character == null || !___character.data.isScoutmaster || scout == null || !scout.photonView.IsMine)
            {
                return;
            }
            bool held = false;
            if (scout.data.currentClimbHandle != null)
            {
                scout.refs.climbing.CancelHandle(grabWall: false);
                held = true;
            }
            if (scout.data.isRopeClimbing)
            {
                scout.refs.ropeHandling.Stop();
                held = true;
            }
            if (scout.data.isVineClimbing)
            {
                scout.refs.vineClimbing.Stop();
                held = true;
            }
            PulledOff += held ? 1 : 0;
        }
    }

    [HarmonyPatch(typeof(ClimbHandle), nameof(ClimbHandle.Interact))]
    private static class NoPitonInHisHand
    {
        private static bool Prefix(Character interactor) => MayHold(interactor);
    }

    [HarmonyPatch(typeof(RopeSegment), nameof(RopeSegment.Interact))]
    private static class NoRopeInHisHand
    {
        private static bool Prefix(Character interactor) => MayHold(interactor);
    }

    [HarmonyPatch(typeof(JungleVine), nameof(JungleVine.Interact))]
    private static class NoVineInHisHand
    {
        private static bool Prefix(Character interactor) => MayHold(interactor);
    }

    [HarmonyPatch]
    private static class ThrowEndsCleanly
    {
        private static FieldInfo owner;

        private static MethodBase TargetMethod()
        {
            MethodInfo moveNext = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Scoutmaster), "IThrow"));
            owner = moveNext != null ? AccessTools.Field(moveNext.DeclaringType, "<>4__this") : null;
            return owner != null ? moveNext : null;
        }

        private static bool Prepare() => TargetMethod() != null;

        /// <summary>His throw over: not throwing, a breather, and whoever he holds let go (as his throw's end does).</summary>
        private static void End(Scoutmaster scoutmaster)
        {
            scoutmaster.isThrowing = false;
            scoutmaster.chillForSeconds = 2f;
            Character him = scoutmaster.character;
            if (him != null && him.data.grabbedPlayer != null)
            {
                him.refs.grabbing.RPCA_GrabUnattach();
            }
            ThrowsEnded++;
        }

        private static bool Prefix(object __instance, ref bool __result)
        {
            if (!(owner.GetValue(__instance) is Scoutmaster scoutmaster) || scoutmaster == null || (scoutmaster.currentTarget != null && scoutmaster.character.data.grabbedPlayer != null))
            {
                return true;
            }
            // Nobody to throw (his target let go of, or the scout he held): the throw ends here.
            End(scoutmaster);
            __result = false;
            return false;
        }

        private static Exception Finalizer(object __instance, Exception __exception)
        {
            if (__exception != null && owner.GetValue(__instance) is Scoutmaster scoutmaster && scoutmaster != null)
            {
                End(scoutmaster);
                return null;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Scoutmaster), "LookAt")]
    private static class SteadyFacing
    {
        private static void Prefix(Scoutmaster __instance, out float __state) => __state = __instance.character.data.lookValues.x;

        private static void Postfix(Scoutmaster __instance, Vector3 lookAtPos, float __state)
        {
            CharacterData data = __instance.character.data;
            Vector2 look = data.lookValues;
            bool rightAbove = HelperFunctions.FlatDistance(__instance.character.Head, lookAtPos) < AboveHim;
            // On the turn it is on (no long way round for others), and as it was with the scout right above him.
            look.x = rightAbove ? __state : __state + Mathf.DeltaAngle(__state, look.x);
            data.lookValues = look;
        }
    }
}
