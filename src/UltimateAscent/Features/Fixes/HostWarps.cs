using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features.Fixes;

/// <summary>
/// PEAK 3.1 only lets the host warp a scout (Character.WarpPlayerRPC refuses anyone else's call), but warps a scout
/// makes for themselves are sent from their own game: the Warp Fungus's warp (the game's WarpOnThrow and this mod's fix
/// of it) and the Warp Compass's. For anyone but the host they did nothing. Such a warp now goes to the host, who makes
/// it (only of the asking player's own scout), on every ascent.
/// </summary>
internal static class HostWarps
{
    private const string WarpMessage = "warp";
    private const string WarpRpc = "WarpPlayerRPC";

    /// <summary>For the tests: warps passed to the host from this game, and warps made for another player.</summary>
    internal static int Passed;
    internal static int MadeForOthers;

    internal static void Listen()
    {
        Net.Handle(WarpMessage, args =>
        {
            if (!PhotonNetwork.IsMasterClient || args.Length < 3 || !(args[0] is int viewId) || !(args[1] is Vector3 to) || !(args[2] is bool poof))
            {
                return;
            }
            PhotonView view = PhotonView.Find(viewId);
            Character scout = view != null ? view.GetComponent<Character>() : null;
            if (scout == null || view.Owner == null || view.Owner.ActorNumber != Net.Sender)
            {
                Plugin.Log.LogWarning($"A warp asked by player {Net.Sender} for view {viewId} was refused: only your own scout can be warped this way.");
                return;
            }
            MadeForOthers++;
            view.RPC(WarpRpc, RpcTarget.All, to, poof);
        });
    }

    /// <summary>A warp sent by anyone but the host goes to the host instead.</summary>
    [HarmonyPatch(typeof(PhotonView), nameof(PhotonView.RPC), typeof(string), typeof(RpcTarget), typeof(object[]))]
    private static class ThroughTheHost
    {
        private static bool Prefix(PhotonView __instance, string methodName, object[] parameters)
        {
            if (methodName != WarpRpc || !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient || PhotonNetwork.MasterClient == null
                || parameters == null || parameters.Length < 2 || !(parameters[0] is Vector3 to) || !(parameters[1] is bool poof))
            {
                return true;
            }
            Passed++;
            AskHost(__instance, to, poof);
            return false;
        }
    }

    /// <summary>Asks the host to warp this scout (their own).</summary>
    internal static void AskHost(PhotonView scout, Vector3 to, bool poof) => Net.SendTo(PhotonNetwork.MasterClient, WarpMessage, scout.ViewID, to, poof);
}
