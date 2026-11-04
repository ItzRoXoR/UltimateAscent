using System.Collections;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>The Citadel's mimic gas (17).</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The mimic gas (17): the scout stands by one of the Citadel's own mimics as its gas comes out; every tick (0.5 s)
    /// puts 5 drowsiness, 2.5 poison and 2.5 spores on the bar.
    /// </summary>
    private IEnumerator MimicGasCheck()
    {
        if (!Rules.Enabled(Lvl.Hazards))
        {
            Info("the mimic gas check is skipped", $"testing ascent {target}");
            yield break;
        }
        yield return ToTheCitadel(_ => { });
        Character local = Local;
        Peak.TrickLuggage gas = Resources.FindObjectsOfTypeAll<Peak.TrickLuggage>()
            .Where(g => g != null && g.gameObject.scene.IsValid() && !g.gameObject.activeSelf && g.statusField != null)
            .OrderBy(g => Vector3.Distance(g.transform.position, local.Center)).FirstOrDefault();
        Vector3? floor = gas != null ? StandNear(gas.transform.position, 1f, 3f, seeIt: false, firm: true) : null;
        if (gas == null || floor == null)
        {
            Info("the mimic gas check is skipped", gas == null ? $"no mimic here ({Map.CurrentPlace}); run with -Biome 81" : "no open floor by the mimic");
            yield break;
        }
        Vector3 home = local.Center;
        CharacterAfflictions afflictions = local.refs.afflictions;
        Protect(true);
        try
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, floor.Value + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
            Protect(false);
            ClearStatuses(afflictions);
            yield return null;
            gas.gameObject.SetActive(true);
            yield return new WaitForSeconds(1.7f);
            float drowsy = afflictions.GetCurrentStatus(Status.Drowsy);
            float poison = afflictions.GetCurrentStatus(Status.Poison);
            float spores = afflictions.GetCurrentStatus(Status.Spores);
            float ticks = drowsy / A17_Traps.GasDrowsy;
            Check("mimic gas puts 5 drowsiness, 2.5 poison and 2.5 spores on the bar every tick (17)",
                drowsy > 0f && Mathf.Abs(ticks - Mathf.Round(ticks)) < 0.05f && Mathf.Abs(poison - Mathf.Round(ticks) * A17_Traps.GasPoisonSpores) < 0.005f && Mathf.Abs(spores - poison) < 0.005f,
                $"{Mathf.Round(ticks):0} ticks in 1.7 s: {drowsy * 100f:0.#} drowsiness, {poison * 100f:0.#} poison, {spores * 100f:0.#} spores; the scout {Vector3.Distance(local.Center, gas.transform.position):0.0} m from the gas");
        }
        finally
        {
            if (gas != null)
            {
                gas.gameObject.SetActive(false);
            }
            ClearStatuses(afflictions);
            Protect(true);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
        }
        yield return new WaitForSeconds(1.5f);
    }
}
