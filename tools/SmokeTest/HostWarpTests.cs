using System.Collections;
using UltimateAscent.Features.Fixes;
using UnityEngine;

namespace UltimateAscent.SmokeTest;

/// <summary>PEAK 3.1 lets only the host warp scouts: a scout's own warps go through the host.</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The host's side, played here (a solo game is its own host): the scout's game asks the host for a warp of its own
    /// scout 6 m away, and the host makes it with the game's own warp (which PEAK 3.1 accepts only from the host). The
    /// other side, a player's game sending its warp to the host instead of making it, needs a second player.
    /// </summary>
    private IEnumerator HostWarpCheck()
    {
        Character local = Local;
        Vector3 home = local.Center;
        Vector3 to = home + local.data.lookDirection_Flat.normalized * 6f;
        if (Physics.Raycast(to + Vector3.up * 10f, Vector3.down, out RaycastHit ground, 30f, HelperFunctions.terrainMapMask))
        {
            to = ground.point + Vector3.up * 1.2f;
        }
        int made = HostWarps.MadeForOthers;
        HostWarps.AskHost(local.refs.view, to, true);
        yield return new WaitForSeconds(2f);
        float off = Vector3.Distance(local.Center, to);
        Check("a scout's own warp asked of the host is made by the host (PEAK 3.1 refuses warps from anyone else)", HostWarps.MadeForOthers == made + 1 && off < 2f,
            $"warps made by the host {HostWarps.MadeForOthers - made}, the scout {off:F1} m from where it was warped to ({Vector3.Distance(home, to):F1} m from where it stood)");
        local.photonView.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, home, false);
        yield return new WaitForSeconds(1f);
    }
}
