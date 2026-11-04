using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Peak.Afflictions;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>The Lookers (17).</summary>
internal sealed partial class Runner
{
    /// <summary>What a scout saw while looking at a Looker.</summary>
    private sealed class LookerTally
    {
        internal float Peeking;
        internal int Peeks;
        internal float Took;
    }

    /// <summary>
    /// The Lookers (17): about 70% of the map's 20 spots show one. The scout stands in sight of the nearest (its rock in
    /// plain view, 8-30 m off): looking at its rock while it peeks out costs nothing; looking into its eyes curses 2.5 a
    /// second and brings the sugar rush. Halfway to the 12.5 curse that sends the Scoutmaster the scout gets curse from
    /// elsewhere (which does not count) and goes on to the next Looker: once the two have given 12.5 together, that one
    /// leaves and the Scoutmaster comes (sent away again at once). True Final Ascent switches every Looker off, so this
    /// needs a profile without it (-Clean).
    /// </summary>
    private IEnumerator LookerCheck()
    {
        if (!Rules.Enabled(Lvl.Hazards))
        {
            Info("the Looker check is skipped", $"testing ascent {target}");
            yield break;
        }
        List<Looker> all = Resources.FindObjectsOfTypeAll<Looker>().Where(l => l != null && l.gameObject.scene.IsValid()).ToList();
        // The host picks the spots in the Lookers' first update.
        for (float until = Time.time + 5f; Time.time < until && !all.All(l => !l.gameObject.activeSelf || l.hasChecked);)
        {
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        List<Looker> shown = all.Where(l => l != null && l.gameObject.activeSelf).ToList();
        if (UltimateAscent.Compat.Tfa.Present)
        {
            Info("the Looker check is skipped", $"True Final Ascent switches every Looker off ({shown.Count} of {all.Count} left); run it with -Clean");
            yield break;
        }
        // 70% of 20: below 8 or all 20 happens about once in 500 climbs.
        Check("about 70% of the Lookers' hiding spots show one (17)", all.Count == 20 && shown.Count >= 8 && shown.Count <= 19,
            $"{shown.Count} of {all.Count} spots");
        Character local = Local;
        Vector3 home = local.Center;
        Vector2 lookBefore = local.data.lookValues;
        // The two nearest Lookers with open ground in sight of their rocks (the map round them loaded).
        List<(Looker looker, Vector3 stand)> near = new List<(Looker, Vector3)>();
        foreach (Looker candidate in shown.Where(l => l.guy != null && l.guy.activeInHierarchy && l.pivot != null).OrderBy(l => Vector3.Distance(l.pivot.position, home)))
        {
            Vector3? spot = InSightOf(candidate);
            if (spot != null)
            {
                near.Add((candidate, spot.Value));
                if (near.Count == 2)
                {
                    break;
                }
            }
        }
        if (near.Count == 0)
        {
            Fail("a Looker with open ground in sight of it", $"{shown.Count} shown, none with level ground 8-30 m off and its rock in view; the Looker setting {GameHandler.Instance.SettingsHandler.GetSetting<LookerSetting>().Value}");
            yield break;
        }
        Looker first = near[0].looker;
        Looker second = near.Count > 1 ? near[1].looker : first;
        Protect(true);
        A17_BeeAmbush.HeldOff = true;
        A17_Looker.Reset();
        CharacterAfflictions afflictions = local.refs.afflictions;
        try
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, near[0].stand + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(2f);
            Protect(false);
            ClearStatuses(afflictions);
            yield return new WaitForSeconds(0.3f);

            // Its rock in the middle of the view while it peeks out: nothing.
            float curseBefore = afflictions.GetCurrentStatus(Status.Curse);
            float firstBefore = first.hasLookedAtMeFor;
            LookerTally rock = new LookerTally();
            yield return LookAtLooker(first, false, 25f, () => rock.Peeking >= 2f, rock, "looker-rock");
            float rockCurse = afflictions.GetCurrentStatus(Status.Curse) - curseBefore;
            Check("looking at a Looker's rock while it peeks out, not into its eyes, costs nothing (17)",
                rock.Peeking >= 1f && rockCurse <= 0f && A17_Looker.cursed <= 0f && first.hasLookedAtMeFor <= firstBefore,
                $"{rock.Peeks} peeks ({rock.Peeking:0.0} s out) in {rock.Took:0} s: {rockCurse * 100f:0.0} curse, {A17_Looker.cursed * 100f:0.0} counted from the Lookers; "
                + $"{Vector3.Distance(near[0].stand + Vector3.up * 1.6f, first.pivot.position):0} m off");

            // Into its eyes, up to half the summoning curse.
            bool rushBefore = afflictions.afflictionList.Any(a => a is Affliction_InfiniteStamina);
            bool rush = false;
            float curseAtRush = -1f;
            curseBefore = afflictions.GetCurrentStatus(Status.Curse);
            float half = A17_Looker.SummoningCurse / 2f;
            LookerTally eyesFirst = new LookerTally();
            yield return LookAtLooker(first, true, 45f, () =>
            {
                if (!rush && afflictions.afflictionList.Any(a => a is Affliction_InfiniteStamina))
                {
                    rush = true;
                    curseAtRush = afflictions.GetCurrentStatus(Status.Curse) - curseBefore;
                }
                return A17_Looker.cursed >= (second != first ? half : A17_Looker.SummoningCurse) || first.neverReturn;
            }, eyesFirst, "looker-eyes");
            // Eyes off it (up at the sky) before anything is measured.
            local.data.lookValues = new Vector2(local.data.lookValues.x, 80f);
            yield return new WaitForSeconds(0.3f);
            float firstWatched = first.hasLookedAtMeFor - firstBefore;
            float firstCurse = afflictions.GetCurrentStatus(Status.Curse) - curseBefore;
            float countedFirst = A17_Looker.cursed;

            // Curse from elsewhere does not count toward the Scoutmaster.
            float otherBefore = afflictions.GetCurrentStatus(Status.Curse);
            afflictions.AddStatus(Status.Curse, 0.1f);
            yield return new WaitForSeconds(0.3f);
            float other = afflictions.GetCurrentStatus(Status.Curse) - otherBefore;
            bool otherCounted = Mathf.Abs(A17_Looker.cursed - countedFirst) > 0.0001f;

            // The next Looker, until the two have given the summoning curse together.
            float secondBefore = second.hasLookedAtMeFor;
            float secondCurse = 0f;
            LookerTally eyesSecond = new LookerTally();
            if (second != first)
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, near[1].stand + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(2f);
                curseBefore = afflictions.GetCurrentStatus(Status.Curse);
                yield return LookAtLooker(second, true, 60f, () => second.neverReturn, eyesSecond, null);
                secondCurse = afflictions.GetCurrentStatus(Status.Curse) - curseBefore;
            }
            float secondWatched = second != first ? second.hasLookedAtMeFor - secondBefore : 0f;
            bool left = second.neverReturn;
            bool sent = A17_Looker.summoned != null && A17_Looker.summonedFor == local;
            // Sent away again at once: no hunt in the test.
            if (A17_Looker.summoned != null)
            {
                A17_Looker.summoned.SetCurrentTarget(null);
            }
            float watched = firstWatched + secondWatched;
            float cursed = firstCurse + secondCurse;
            float wanted = A17_Looker.CursePerSecond * watched;
            Check("looking into a Looker's eyes while it peeks out curses you 2.5 a second (17)",
                watched >= 1f && Mathf.Abs(cursed - wanted) <= 0.026f,
                $"{watched:0.00} s of eye contact: {cursed * 100f:0.0} curse (wanted {wanted * 100f:0.0}); the first {firstWatched:0.00} s in {eyesFirst.Peeks} peeks ({eyesFirst.Took:0} s), "
                + $"the second {secondWatched:0.00} s in {eyesSecond.Peeks} peeks ({eyesSecond.Took:0} s)");
            Check("looking into a Looker's eyes gives the sugar rush once its curse lands on the bar (17)", rush && !rushBefore && curseAtRush >= 0.024f,
                $"sugar rush seen {rush} (on before {rushBefore}), with {curseAtRush * 100f:0.0} curse from the Looker on the bar then");
            Check("curse from elsewhere does not count toward the Scoutmaster (17)", other > 0f && !otherCounted,
                $"{other * 100f:0.0} curse from elsewhere; counted from the Lookers {countedFirst * 100f:0.0} before, {(otherCounted ? "changed" : "the same")} after");
            Check(second != first
                    ? "the Lookers' curse adds up across them: once two have given 12.5 together, the one being watched leaves and sends the Scoutmaster (17)"
                    : "once a Looker has given 12.5 curse, it leaves and sends the Scoutmaster (17)",
                left && sent && watched >= 4.95f && watched <= 5.1f && (second == first || (!first.neverReturn && secondWatched < 4.9f)) && A17_Looker.cursed <= 0f,
                $"{watched:0.00} s of eye contact in all ({firstWatched:0.00} s with the first, {secondWatched:0.00} s with the {(second != first ? "second" : "same")}); "
                + $"the one watched left {left}, the first still there {!first.neverReturn}; the Scoutmaster sent {sent}; the count after {A17_Looker.cursed * 100f:0.0}"
                + (second == first ? "; only one Looker in reach" : ""));
        }
        finally
        {
            A17_Looker.Reset();
            A17_BeeAmbush.HeldOff = false;
            Protect(true);
            ClearStatuses(afflictions);
            local.data.lookValues = lookBefore;
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
        }
        yield return new WaitForSeconds(1.5f);
    }

    /// <summary>
    /// The scout keeps looking at a Looker, into its eyes or at the middle of its rock, until <paramref name="done"/> or
    /// <paramref name="most"/> seconds; pictured once while it peeks out (and, into its eyes, is watched).
    /// </summary>
    private IEnumerator LookAtLooker(Looker looker, bool intoEyes, float most, Func<bool> done, LookerTally tally, string picture)
    {
        Character local = Local;
        float started = Time.time;
        bool wasPeeking = false, pictured = picture == null;
        float peekStarted = 0f, watchedBefore = looker.hasLookedAtMeFor;
        while (Time.time < started + most && looker != null && !looker.neverReturn && !done())
        {
            // Only the curse is measured: the sun (2 heat a second at 23) and the rest are kept off.
            foreach (Status type in CareStatuses.Where(t => t != Status.Curse && local.refs.afflictions.GetCurrentStatus(t) > 0f))
            {
                local.refs.afflictions.SetStatus(type, 0f);
            }
            Transform eye = MainCamera.instance.transform;
            Vector3 at = looker.pivot.position;
            if (intoEyes && A17_Looker.Eyes(looker, out Vector3 eyes))
            {
                at = eyes;
            }
            local.data.lookValues = HelperFunctions.DirectionToLook(at - eye.position);
            bool peeking = looker.anim.GetBool("IsActive");
            if (peeking)
            {
                tally.Peeking += Time.deltaTime;
                if (!wasPeeking)
                {
                    tally.Peeks++;
                    peekStarted = Time.time;
                }
                if (!pictured && Time.time > peekStarted + 0.5f && (!intoEyes || looker.hasLookedAtMeFor > watchedBefore))
                {
                    pictured = true;
                    Screenshot(picture);
                }
            }
            wasPeeking = peeking;
            yield return null;
        }
        tally.Took = Time.time - started;
    }

    /// <summary>Open, level ground 8-30 m from a Looker with its rock in plain view (nothing else in between).</summary>
    private static Vector3? InSightOf(Looker looker)
    {
        Vector3 pivot = looker.pivot.position;
        foreach (float reach in new[] { 12f, 16f, 20f, 9f, 25f })
        {
            for (int angle = 0; angle < 360; angle += 20)
            {
                Vector3 above = pivot + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * reach + Vector3.up * 20f;
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit floor, 45f, HelperFunctions.terrainMapMask) || floor.normal.y < 0.8f
                    || floor.transform.IsChildOf(looker.transform) || InsideSomething(floor.point + Vector3.up) || Hazard(floor.point + Vector3.up, 2f)
                    || Physics.CheckCapsule(floor.point + Vector3.up * 0.5f, floor.point + Vector3.up * 1.6f, 0.3f, HelperFunctions.terrainMapMask))
                {
                    continue;
                }
                Vector3 eye = floor.point + Vector3.up * 1.6f;
                float off = Vector3.Distance(eye, pivot);
                RaycastHit line = HelperFunctions.LineCheck(eye, pivot, HelperFunctions.LayerType.TerrainMap);
                if (off >= 8f && off <= 30f && line.transform != null && line.transform.IsChildOf(looker.transform))
                {
                    return floor.point;
                }
            }
        }
        return null;
    }
}
