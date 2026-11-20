using System.Collections;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>No bee ambushes once the Tropics' campfire is lit (17).</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// By the Tropics' campfire, on the ground the scouts sit on there: a night's check gives the scout a moment and a
    /// swarm is out on them; the campfire is lit as a player lights it. The swarm disperses, the moment is dropped, and
    /// afterwards no check rolls and the old moment's time brings no swarm.
    /// </summary>
    private IEnumerator BeesAfterCampfire()
    {
        DayNightManager clock = DayNightManager.instance;
        if (clock == null || !Rules.Enabled(Lvl.Hazards) || !Rules.Hazard(RunSettings.SETTINGTYPE.Hazard_Bees))
        {
            Info("the bees-after-the-campfire check is skipped", clock == null ? "no day-night clock" : $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        if (Singleton<MapHandler>.Instance.currentSegment < (int)Segment.Tropics)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(10f);
        }
        Campfire campfire = Campfire.ALL_CAMPFIRES.FirstOrDefault(c => c != null && !c.Lit && c.advanceToSegment == Segment.Alpine);
        if (campfire == null || Map.CurrentPlace != Place.Tropics)
        {
            Info("the bees-after-the-campfire check is skipped", campfire == null ? "no unlit campfire at the end of the Tropics" : $"the climb is at {Map.CurrentPlace}, not the Tropics");
            yield break;
        }
        // Beside the fire, where the scouts sit by it.
        Vector3 away = local.Center - campfire.transform.position;
        away.y = 0f;
        Vector3 standAt = campfire.transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 1.6f + Vector3.up;
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standAt, false);
        yield return new WaitForSeconds(1.5f);
        bool tropicsGround = A17_BeeAmbush.InTropics(local);
        Info("by the Tropics' campfire", $"ground {GroundBiome(local)}, in the Tropics for the bees {tropicsGround}");

        float original = clock.timeOfDay;
        float chance = A17_BeeAmbush.Chance;
        A17_BeeAmbush.Chance = 1f;
        A17_BeeAmbush.Checks(clock, out float dayBegins, out float nightBegins);
        Protect(true);
        try
        {
            // The night's check gives the scout a moment (not set upon yet today); a swarm is out on them. The clock is put
            // just before the night with the moments it goes by passed over (the climb's own day roll would bring a swarm
            // and leave the night without a roll).
            A17_BeeAmbush.HeldOff = true;
            clock.timeOfDay = Mathf.Repeat(nightBegins - 0.03f, 24f);
            yield return null;
            yield return null;
            A17_BeeAmbush.ambushedOn.Clear();
            A17_BeeAmbush.HeldOff = false;
            int nightChecks = A17_BeeAmbush.ChecksMade;
            A17_BeeAmbush.lastCheckAt = -1000f;
            for (float deadline = Time.time + 5f; A17_BeeAmbush.ChecksMade == nightChecks && Time.time < deadline;)
            {
                yield return null;
            }
            bool due = A17_BeeAmbush.MomentOf(local, out float moment) && !float.IsNaN(moment) && !float.IsInfinity(moment);
            // Held off until the fire is lit, so the moment does not come first (the fire calls the bees off all the same).
            A17_BeeAmbush.HeldOff = true;
            A17_BeeAmbush.Spawn(local);
            yield return new WaitForSeconds(0.5f);
            BeeSwarm swarm = FindObjectsByType<BeeSwarm>(FindObjectsSortMode.None).FirstOrDefault(b => A17_BeeAmbush.IsAmbush(b) && !ScatteredBees.IsWaiting(b) && b.currentAggroCharacter == local);
            int calledOff = A17_BeeAmbush.CalledOff;
            int dropped = A17_BeeAmbush.MomentsAfterFire;
            Step("light the Tropics' campfire", () => campfire.Interact_CastFinished(local));
            A17_BeeAmbush.HeldOff = false;
            for (float end = Time.realtimeSinceStartup + 30f; !A17_BeeAmbush.TropicsFireLit && Time.realtimeSinceStartup < end;)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            bool gone = swarm == null || swarm.dispersing;
            bool kept = A17_BeeAmbush.MomentOf(local, out float left) && !float.IsNaN(left);
            Check("once the Tropics' campfire is lit, the swarm out on a scout by it disperses and their moment to come is dropped (17)",
                due && swarm != null && campfire.Lit && A17_BeeAmbush.TropicsFireLit && gone && A17_BeeAmbush.CalledOff > calledOff && !kept && A17_BeeAmbush.MomentsAfterFire > dropped,
                $"moment before {(due ? clock.FloatToTimeString(moment) : "none")}, swarm {(swarm == null ? "none out" : swarm.dispersing ? "dispersing" : "still out")}, called off {A17_BeeAmbush.CalledOff - calledOff}; "
                + $"fire lit {campfire.Lit}, the climb at {Map.CurrentPlace}; moment after {(kept ? clock.FloatToTimeString(left) : "none")}");

            // From then on no checks and no ambushes, though the scout still stands on the Tropics' ground by the fire.
            yield return new WaitForSeconds(4.5f);
            int checks = A17_BeeAmbush.ChecksMade;
            yield return BeeCheckAt(clock, dayBegins);
            yield return BeeCheckAt(clock, nightBegins);
            if (due)
            {
                clock.timeOfDay = Mathf.Repeat(moment - 0.03f, 24f);
                yield return new WaitForSeconds(1.5f);
            }
            int swarms = FindObjectsByType<BeeSwarm>(FindObjectsSortMode.None).Count(b => A17_BeeAmbush.IsAmbush(b) && !ScatteredBees.IsWaiting(b) && !b.dispersing);
            Check("after the Tropics' campfire no bee checks roll and no ambush comes, also by the fire on the Tropics' ground (17)",
                A17_BeeAmbush.ChecksMade == checks && swarms == 0 && !(A17_BeeAmbush.MomentOf(local, out float later) && !float.IsNaN(later)),
                $"checks {A17_BeeAmbush.ChecksMade - checks} as the day and the night began, ambush swarms {swarms} at the old moment's time; the scout on {GroundBiome(local)} ground (in the Tropics for the bees {A17_BeeAmbush.InTropics(local)})");
        }
        finally
        {
            A17_BeeAmbush.HeldOff = false;
            clock.timeOfDay = original;
            A17_BeeAmbush.Chance = chance;
        }
    }
}
