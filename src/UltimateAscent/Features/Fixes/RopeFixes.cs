using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltimateAscent.Features.Fixes;

/// <summary>
/// Ropes' bugs, fixed on every ascent (the user's list of 2026-11-04).
/// <list type="bullet">
/// <item>When the host left, every rope vanished on the screen of the scout who put it up, and for anyone joining after:
/// the game takes any change of host for the rope's maker leaving, and stops drawing and sending the rope. Now only its
/// maker's leaving does that.</item>
/// <item>A scout who left took everything they had put up with them, for everyone (the host, as any scout): Photon
/// takes a leaving player's things off every game. What they put up for good now stays for those still there: ropes
/// tied to an anchor (a Rope Shooter's, or a rope spool's tied off), their anchors and Chain Launcher chains. A rope whose maker has left hangs as it last was
/// (the game's own way: it stops swinging), still to climb. Who joins after does not see it.</item>
/// <item>The airport's training rope came back no more after a run: it is put up only as the room is first joined, and
/// the scouts come back to the airport in the room they are in (and a host who leaves takes it with them). The host now
/// puts it up again whenever it is missing.</item>
/// </list>
/// </summary>
internal static class RopeFixes
{
    private const string Airport = "Airport";

    /// <summary>For the tests: ropes kept at a change of host, things kept after their maker left, and airport ropes put up again.</summary>
    internal static int KeptAtHostChange;
    internal static int KeptAfterLeaving;
    internal static int PutUpAgain;

    /// <summary>The player whose things Photon is taking off this game (they left), while it does.</summary>
    [ThreadStatic] internal static int Leaving;

    private static float nextLook;

    [HarmonyPatch(typeof(Rope), nameof(Rope.OnMasterClientSwitched))]
    private static class MakerStillHere
    {
        private static bool Prefix(Rope __instance)
        {
            PhotonView view = __instance.photonView;
            Room room = PhotonNetwork.CurrentRoom;
            if (view == null || room == null || !room.Players.ContainsKey(view.CreatorActorNr))
            {
                return true;
            }
            KeptAtHostChange++;
            return false;
        }
    }

    [HarmonyPatch(typeof(PhotonNetwork), nameof(PhotonNetwork.DestroyPlayerObjects), typeof(int), typeof(bool))]
    private static class PlayerLeaving
    {
        private static void Prefix(int playerId, out int __state)
        {
            __state = Leaving;
            Leaving = playerId;
        }

        private static Exception Finalizer(int __state, Exception __exception)
        {
            Leaving = __state;
            return __exception;
        }
    }

    /// <summary>Something a scout put up for good: a rope tied to an anchor, an anchor, a Chain Launcher's chain (pitons are the room's, never a player's).</summary>
    internal static bool PutUpForGood(GameObject thing)
    {
        if (thing == null)
        {
            return false;
        }
        Rope rope = thing.GetComponentInChildren<Rope>(true);
        if (rope != null)
        {
            return rope.attachmenState == Rope.ATTACHMENT.anchored;
        }
        return thing.GetComponentInChildren<RopeAnchor>(true) != null || thing.GetComponentInChildren<JungleVine>(true) != null;
    }

    [HarmonyPatch(typeof(PhotonNetwork), "RemoveInstantiatedGO")]
    private static class KeepWhatWasPutUp
    {
        private static bool Prefix(GameObject go)
        {
            if (Leaving <= 0 || !PutUpForGood(go))
            {
                return true;
            }
            foreach (Rope rope in go.GetComponentsInChildren<Rope>(true))
            {
                rope.creatorLeft = true;
            }
            KeptAfterLeaving++;
            return false;
        }
    }

    /// <summary>
    /// A rope whose maker has left, on the game that has it in hand now (the host): its segments are the ones it last had
    /// from its maker, as on everyone else's game (this game never moved it itself).
    /// </summary>
    [HarmonyPatch(typeof(Rope), nameof(Rope.GetRopeSegments))]
    private static class FrozenRopeSegments
    {
        private static bool Prefix(Rope __instance, List<Transform> ___remoteColliderSegments, ref List<Transform> __result)
        {
            if (!__instance.creatorLeft || __instance.photonView == null || !__instance.photonView.IsMine || ___remoteColliderSegments.Count == 0)
            {
                return true;
            }
            __result = ___remoteColliderSegments;
            return false;
        }
    }

    [HarmonyPatch(typeof(Rope), "get_" + nameof(Rope.SegmentCount))]
    private static class FrozenRopeLength
    {
        private static bool Prefix(Rope __instance, List<Transform> ___remoteColliderSegments, ref int __result)
        {
            if (!__instance.creatorLeft || __instance.photonView == null || !__instance.photonView.IsMine || ___remoteColliderSegments.Count == 0)
            {
                return true;
            }
            __result = ___remoteColliderSegments.Count;
            return false;
        }
    }

    [HarmonyPatch(typeof(RopeAnchorWithRope), nameof(RopeAnchorWithRope.SpawnRope))]
    private static class OneRopeEach
    {
        private static bool Prefix(RopeAnchorWithRope __instance, ref Rope __result)
        {
            if (__instance.rope == null)
            {
                return true;
            }
            __result = __instance.rope;
            return false;
        }
    }

    /// <summary>Every 2 s in the airport, on the host: a rope anchor of the airport's own whose rope is gone puts it up again.</summary>
    internal static void Tick()
    {
        if (Time.time < nextLook)
        {
            return;
        }
        nextLook = Time.time + 2f;
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || SceneManager.GetActiveScene().name != Airport)
        {
            return;
        }
        Rope[] ropes = null;
        foreach (RopeAnchorWithRope anchor in UnityEngine.Object.FindObjectsByType<RopeAnchorWithRope>(FindObjectsSortMode.None))
        {
            if (anchor == null || anchor.photonView == null || anchor.photonView.isRuntimeInstantiated || anchor.anchor == null || anchor.rope != null)
            {
                continue;
            }
            ropes ??= UnityEngine.Object.FindObjectsByType<Rope>(FindObjectsSortMode.None);
            if (ropes.Any(r => r != null && r.attachedToAnchor == anchor.anchor))
            {
                continue;
            }
            anchor.SpawnRope();
            PutUpAgain++;
        }
    }
}
