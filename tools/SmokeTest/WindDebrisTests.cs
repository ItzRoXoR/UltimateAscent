using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;
using Status = CharacterAfflictions.STATUSTYPE;
using Kind = UltimateAscent.Features.WindDebris.Kind;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the Roots wind's debris (18) and the Spruce Branch.</summary>
internal sealed partial class Runner
{
    /// <summary>The sounds the game's sound player plays, with when and where, while a check listens.</summary>
    [HarmonyLib.HarmonyPatch(typeof(SFX_Player), nameof(SFX_Player.PlaySFX))]
    internal static class SoundsTimed
    {
        internal static bool Listening;
        internal static readonly List<(float at, Vector3 where, string name)> Heard = new List<(float, Vector3, string)>();

        private static void Prefix(SFX_Instance SFX, Vector3 position)
        {
            if (Listening && SFX != null)
            {
                Heard.Add((Time.time, position, SFX.name));
            }
        }

        /// <summary>The sounds within this long of a moment and this near a spot (not the rest of the world's: a Scoutmaster walking, a spider).</summary>
        internal static List<string> Around(float at, float within, Vector3 near, float reach) =>
            Heard.Where(h => Mathf.Abs(h.at - at) <= within && Vector3.Distance(h.where, near) <= reach).Select(h => h.name).OrderBy(n => n).ToList();
    }

    /// <summary>
    /// The Roots wind's debris (18, on a map with the Roots): what a gust brings (200-250 needles, 7-10 sticks, 13-17
    /// branches, 4-6 rocks, none in its first 1.5 s, then each spread evenly over the rest; outside the Roots the needles
    /// only); the Roots' Spruce Branches on
    /// the ground in the sticks' place; then, in the Roots' own wind storm (started and stopped by the test), with the
    /// scout on open ground (a test wall right behind them, downwind, so the wind does not blow them away from where the
    /// debris comes): one of each piece sent straight at them (a needle sticks in, shown as a spruce needle; a stick or a
    /// branch knocks them down for 4 s; a rock for 2 s with 20 injury); a stick sent at them as they step aside misses;
    /// behind a wall upwind everything stops at the wall; an item held into the wind (a Lantern: a backpack spawned for a
    /// scout goes on their back) shields them (the needles stick in it, the sticks, branches and rocks bounce off it and
    /// fall); on sheltered ground nothing that flies at them hits, and what
    /// has the land in its way stops at it (nothing through a rock or a hill); a whole gust of the wind's own debris round
    /// them (what it brings, nothing in its first 1.5 s, the far needles neither, then evenly through it, 65% of the
    /// needles and half the rest past them and the rest at them,
    /// pieces only near the scout, the far needles flying as pictures, all of it gone soon after); and with ascent 18's
    /// rule off (in memory) no debris.
    /// </summary>
    private IEnumerator WindDebrisChecks()
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather))
        {
            Info("the wind's debris checks are skipped", $"testing ascent {target}; the debris comes from ascent 18");
            yield break;
        }
        {
            // 300 gusts' plans in the Roots, 100 outside, 3-40 s long: the counts, and each kind spread evenly over the
            // gust after its first 1.5 s (its k-th of n in the k-th n-th of the rest); a gust no longer than 1.5 s brings nothing.
            int[] least = { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue }, most = new int[4];
            bool even = true, needlesOnly = true;
            float earliest = float.MaxValue;
            bool shortEmpty = WindDebris.PlanGust(true, WindDebris.StartSeconds).Count == 0 && WindDebris.PlanGust(true, 1f).Count == 0;
            for (int gustNo = 0; gustNo < 400; gustNo++)
            {
                bool roots = gustNo < 300;
                float seconds = UnityEngine.Random.Range(3f, 40f);
                float from = WindDebris.StartSeconds / seconds;
                List<(float at, Kind kind)> made = WindDebris.PlanGust(roots, seconds);
                earliest = Mathf.Min(earliest, made.Count > 0 ? made[0].at * seconds : float.MaxValue);
                for (int kind = 0; kind < 4; kind++)
                {
                    List<float> times = made.Where(p => (int)p.kind == kind).Select(p => p.at).ToList();
                    if (!roots)
                    {
                        needlesOnly &= kind == 0 ? times.Count >= WindDebris.PerGust[0].least && times.Count <= WindDebris.PerGust[0].most : times.Count == 0;
                        continue;
                    }
                    least[kind] = Mathf.Min(least[kind], times.Count);
                    most[kind] = Mathf.Max(most[kind], times.Count);
                    for (int k = 0; k < times.Count; k++)
                    {
                        even &= times[k] >= from + (1f - from) * k / times.Count && times[k] <= from + (1f - from) * (k + 1f) / times.Count;
                    }
                }
            }
            bool counts = Enumerable.Range(0, 4).All(k => least[k] >= WindDebris.PerGust[k].least && most[k] <= WindDebris.PerGust[k].most);
            Check("a gust of the wind brings in the Roots 200-250 needles, 7-10 sticks, 13-17 branches and 4-6 rocks, none in its first 1.5 s, then each spread evenly over the rest of it; outside the Roots the needles only (18, 25)",
                counts && even && needlesOnly && earliest >= WindDebris.StartSeconds && shortEmpty,
                $"in 300 gusts' plans in the Roots (3-40 s gusts): needles {least[0]}-{most[0]}, sticks {least[1]}-{most[1]}, branches {least[2]}-{most[2]}, rocks {least[3]}-{most[3]}; each kind evenly spread after the first {WindDebris.StartSeconds} s {even}; "
                + $"the earliest piece {earliest:0.00} s into its gust; a gust of 1-1.5 s brings nothing {shortEmpty}; outside the Roots (100 plans) the needles only {needlesOnly}");
        }

        Character local = Local;
        if (Map.CurrentPlace != Place.Roots)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(8f);
        }
        if (Map.CurrentPlace != Place.Roots)
        {
            Info("the Roots wind's checks are skipped", $"no Roots here ({Map.CurrentPlace}); run with -Biome and a map number that has the Roots (81)");
            yield break;
        }
        SpruceBranchChecks();
        WindChillZone zone = RootsWind.instance != null ? RootsWind.instance.windZone : null;
        if (zone == null)
        {
            Fail("the Roots' wind storm", "RootsWind has no wind zone");
            yield break;
        }
        if (UltimateAscent.Compat.Tfa.BuilderActive)
        {
            // True Final Ascent's Builder parks its far blocks (a jump can land before the ground is there): onto the Roots
            // block's own ground first, at the campfire it begins from.
            UltimateAscent.Compat.Tfa.Stage roots = UltimateAscent.Compat.Tfa.Stages().FirstOrDefault(s => s.Kind == "Roots");
            Campfire start = roots != null && roots.Before != null ? roots.Before.GetComponentInChildren<Campfire>(true) : null;
            if (start != null)
            {
                yield return WarpOnto(start.transform.position + Vector3.back * 1.6f + Vector3.up * 1.2f);
            }
            Info("the Builder's Roots block", roots == null ? "none in the chain" : $"its campfire before {(start != null ? start.transform.position.ToString("F0") : "none")}; the scout at {local.Center:F0}; the storm {zone.windZoneBounds.center:F0} size {zone.windZoneBounds.size:F0}");
        }
        Vector3 wind = Vector3.Lerp(Vector3.right, Vector3.forward, 0.2f).normalized;
        // The Roots' storm covers the upper Roots: spots are looked for inside it, from its edge nearest the scout inwards.
        Bounds storm = zone.windZoneBounds;
        Vector3 edge = storm.ClosestPoint(local.Center);
        Vector3 inside = edge + Vector3.ProjectOnPlane(storm.center - edge, Vector3.up).normalized * 25f;
        inside.y = local.Center.y;
        Vector3? open = DebrisSpot(zone, inside, wind, true, out float openness, out string ground);
        Vector3? sheltered = DebrisSpot(zone, inside, wind, false, out float shelter, out _);
        Info("the Roots' ground in the storm, how open to the wind", ground);
        if (open == null)
        {
            Fail("open ground in the Roots for the wind's debris", $"none near {inside:F0} (the most open {openness:0.00})");
            yield break;
        }
        ConfigEntryHolder rule = new ConfigEntryHolder(UltimateAscent.Plugin.AscentSwitches[Lvl.ExtremeWeather]);
        Protect(false);
        WindDebris.OnlySent = true;
        // A test wall right behind the open spot, downwind: the gust would blow the scout away from where the debris
        // comes (a wall downwind gives no shelter: the game and the mod look upwind for that).
        GameObject wall = WindWall(open.Value, wind, shelters: false);
        try
        {
            // The wind on for good, at the start of a gust (it barely pushes then): pieces sent by the test only.
            zone.RPCA_ToggleWind(true, wind, 100000f);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, open.Value + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(2f);
            Info("the scout in the open in the Roots' wind", $"at {local.Center:F0} (looked {openness:0.00} open), exposed {zone.windPlayerFactor:0.00} (0 sheltered, 1 open), the wind blowing {zone.windActive}, inside it {zone.localCharacterInsideBounds}; "
                + $"sheltered ground {(sheltered.HasValue ? $"{sheltered.Value:F0} ({shelter:0.00} open)" : "none")}");
            foreach (Kind kind in new[] { Kind.Needle, Kind.Stick, Kind.Branch, Kind.Rock })
            {
                yield return DebrisHit(zone, kind, open.Value);
            }

            // Stepping aside: a stick sent at the scout, who moves 2.5 m across the wind before it arrives.
            yield return UntilUp(local);
            {
                int hitsBefore = WindDebris.Hits.Sum();
                FallRecorder.Clear();
                FallRecorder.Of = local;
                Vector3 stood = local.Center;
                bool sent = WindDebris.Send(local, zone, Kind.Stick, stood);
                yield return new WaitForSeconds(0.4f);
                Vector3 aside = GroundUnder(stood + Vector3.Cross(Vector3.up, wind) * 2.5f);
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, aside + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(4f);
                FallRecorder.Of = null;
                Check("a stick the wind sends at a scout misses them once they step aside (they can dodge what comes) (18)",
                    sent && WindDebris.Hits.Sum() == hitsBefore && FallRecorder.Seconds.Count == 0,
                    $"sent {sent}; hits {WindDebris.Hits.Sum() - hitsBefore}, knock-downs {FallRecorder.Seconds.Count}; the scout {Vector3.Distance(local.Center, stood):0.0} m from where it stood");
            }

            // Behind a wall upwind (the test's, 3 m off): everything sent straight at the scout stops at the wall.
            yield return UntilUp(local);
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, open.Value + Vector3.up * 1.2f, false);
                GameObject upwind = WindWall(open.Value, -wind, 3f);
                yield return new WaitForSeconds(1.5f);
                int wallHitsBefore = WindDebris.Hits.Sum(), wallStoppedBefore = WindDebris.Stopped, wallNearBefore = WindDebris.CameNear;
                int sent = 0;
                foreach (Kind kind in new[] { Kind.Needle, Kind.Stick, Kind.Branch, Kind.Rock, Kind.Needle, Kind.Needle, Kind.Stick, Kind.Needle })
                {
                    sent += WindDebris.Send(local, zone, kind, local.Center) ? 1 : 0;
                    yield return new WaitForSeconds(0.25f);
                }
                yield return ClownPicture(local.Center, "wind-wall-upwind");
                yield return new WaitForSeconds(3.5f);
                Destroy(upwind);
                Physics.SyncTransforms();
                Check("a wall upwind stops all the wind's debris sent at the scout behind it: none goes through it (18)",
                    sent >= 6 && WindDebris.Stopped - wallStoppedBefore == sent && WindDebris.CameNear == wallNearBefore && WindDebris.Hits.Sum() == wallHitsBefore,
                    $"{sent} pieces sent straight at them; stopped {WindDebris.Stopped - wallStoppedBefore}, came within 2.5 m of them {WindDebris.CameNear - wallNearBefore}, hits {WindDebris.Hits.Sum() - wallHitsBefore}");
            }

            // An item held into the wind: what comes straight at it stops at it, the needles stuck in it, the rest bounced
            // off it to the ground; nothing reaches the scout behind it.
            yield return UntilUp(local);
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, open.Value + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(1.5f);
                List<Item> all = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.Where(i => i != null && !(i is Backpack)).ToList();
                Item pack = new[] { "Lantern", "PortableStovetopItem", "Bugle" }.Select(n => all.FirstOrDefault(i => i.gameObject.name == n)).FirstOrDefault(i => i != null);
                if (pack == null)
                {
                    Fail("an item held into the wind", "no Lantern, Portable Stove or Bugle in the item database");
                }
                else
                {
                    yield return SpawnIntoHand(pack);
                    LookAtPoint(local, local.Center - wind * 10f);
                    yield return new WaitForSeconds(0.6f);
                    Item shield = held;
                    Collider part = shield != null ? shield.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).OrderByDescending(c => c.bounds.size.sqrMagnitude).FirstOrDefault() : null;
                    if (part == null)
                    {
                        Fail("an item held into the wind", shield == null ? "it did not come into the hands" : "it has nothing solid");
                    }
                    else
                    {
                        int hitsBefore = WindDebris.Hits.Sum(), inItemsBefore = WindDebris.NeedlesInItems, bouncedBefore = WindDebris.BouncedOffItems;
                        int thornsBefore = Afflictions.physicalThorns.Count(t => t.stuckIn);
                        int stuckBefore = SpruceNeedles.InItem(shield);
                        FallRecorder.Clear();
                        FallRecorder.Of = local;
                        int needles = 0, pieces = 0;
                        foreach (Kind kind in new[] { Kind.Needle, Kind.Stick, Kind.Needle, Kind.Branch, Kind.Needle, Kind.Rock, Kind.Needle })
                        {
                            LookAtPoint(local, local.Center - wind * 10f);
                            if (WindDebris.Send(local, zone, kind, part.bounds.center))
                            {
                                needles += kind == Kind.Needle ? 1 : 0;
                                pieces += kind == Kind.Needle ? 0 : 1;
                            }
                            yield return new WaitForSeconds(0.3f);
                        }
                        yield return new WaitForSeconds(2.2f);
                        Vector3 side = Vector3.Cross(Vector3.up, wind).normalized;
                        GameObject look = new GameObject("UltimateAscentTestCamera");
                        Camera lens = look.AddComponent<Camera>();
                        lens.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
                        lens.fieldOfView = 40f;
                        lens.nearClipPlane = 0.05f;
                        look.transform.position = part.bounds.center + side * 1.6f - wind * 0.8f + Vector3.up * 0.3f;
                        look.transform.LookAt(part.bounds.center);
                        yield return null;
                        Screenshot("wind-held-item");
                        yield return new WaitForSeconds(0.3f);
                        Destroy(look);
                        FallRecorder.Of = null;
                        int stuck = SpruceNeedles.InItem(shield) - stuckBefore;
                        Check("an item held into the wind shields the scout: the needles that hit it stick in it, the sticks, branches and rocks bounce off it, nothing reaches them (18)",
                            needles > 0 && pieces > 0 && WindDebris.NeedlesInItems - inItemsBefore == needles && stuck == needles && WindDebris.BouncedOffItems - bouncedBefore >= pieces
                                && WindDebris.Hits.Sum() == hitsBefore && FallRecorder.Seconds.Count == 0 && Afflictions.physicalThorns.Count(t => t.stuckIn) == thornsBefore,
                            $"sent at the {shield.name} in the hands ('{part.name}'): {needles} needles, {pieces} sticks, branches and rocks; needles stuck in it {WindDebris.NeedlesInItems - inItemsBefore} (shown in it {stuck}), bounced off it {WindDebris.BouncedOffItems - bouncedBefore}; "
                            + $"hits on the scout {WindDebris.Hits.Sum() - hitsBefore}, knock-downs {FallRecorder.Seconds.Count}, thorns {Afflictions.physicalThorns.Count(t => t.stuckIn) - thornsBefore}");
                    }
                    local.refs.items.EquipSlot(Optionable<byte>.None);
                }
            }

            // Sheltered: everything sent at the scout passes them by, and what has the land in its way stops at it.
            if (sheltered.HasValue)
            {
                yield return UntilUp(local);
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, sheltered.Value + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(2f);
                float exposed = zone.windPlayerFactor;
                int hitsBefore = WindDebris.Hits.Sum();
                int thornsBefore = Afflictions.physicalThorns.Count(t => t.stuckIn);
                int shelterStoppedBefore = WindDebris.Stopped, shelterNearBefore = WindDebris.CameNear, shelterInsideBefore = WindDebris.StartsInside;
                FallRecorder.Clear();
                FallRecorder.Of = local;
                int sent = 0, blocked = 0;
                foreach (Kind kind in new[] { Kind.Needle, Kind.Stick, Kind.Branch, Kind.Rock, Kind.Needle, Kind.Needle, Kind.Needle, Kind.Stick, Kind.Branch, Kind.Needle, Kind.Rock, Kind.Needle })
                {
                    if (WindDebris.Send(local, zone, kind, local.Center))
                    {
                        sent++;
                        blocked += LandBetween(WindDebris.LastStart, local.Center) ? 1 : 0;
                    }
                    yield return new WaitForSeconds(0.25f);
                }
                yield return new WaitForSeconds(4f);
                FallRecorder.Of = null;
                int stopped = WindDebris.Stopped - shelterStoppedBefore, near = WindDebris.CameNear - shelterNearBefore;
                Check("in shelter from the wind nothing it carries hits the scout, and what has the land in its way stops at it: nothing through a rock or a hill (18)",
                    exposed < WindDebris.ExposedFrom && sent > 0 && WindDebris.Hits.Sum() == hitsBefore && FallRecorder.Seconds.Count == 0 && Afflictions.physicalThorns.Count(t => t.stuckIn) == thornsBefore
                        && stopped >= blocked && near <= sent - blocked,
                    $"at {local.Center:F0}, exposed {exposed:0.00}; {sent} pieces sent straight at them, {blocked} with the land between where they set off and the scout; stopped by the land {stopped}, came within 2.5 m of them {near}; "
                    + $"places to set off from turned down as inside the land {WindDebris.StartsInside - shelterInsideBefore}; hits {WindDebris.Hits.Sum() - hitsBefore}, knock-downs {FallRecorder.Seconds.Count}, thorns {Afflictions.physicalThorns.Count(t => t.stuckIn) - thornsBefore}");
            }
            else
            {
                Info("the shelter check is skipped", "no sheltered ground found near the scout");
            }

            // With True Final Ascent: its rest stops, its Builder's invisible walls, its Mega size.
            if (UltimateAscent.Compat.Tfa.Present)
            {
                yield return UntilUp(local);
                yield return TfaWindChecks(zone, wind, open.Value);
            }

            // A whole gust of the wind's own debris, the scout in the open (locked statuses: what it does is not measured here).
            yield return UntilUp(local);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, open.Value + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
            Protect(true);
            WindDebris.OnlySent = false;
            int[] sentBefore = WindDebris.Sent.ToArray();
            int[] hitsBeforeGust = WindDebris.Hits.ToArray();
            int stoppedBefore = WindDebris.Stopped, nearBefore = WindDebris.CameNear;
            int[] passedBefore = WindDebris.PassedBy.ToArray();
            int droppedBefore = WindDebris.Dropped, insideBefore = WindDebris.StartsInside;
            WindDebris.NeedlesOn.Clear();
            int mostNeedles = 0, mostOthers = 0, mostFar = 0;
            float farthest = 0f, strongest = 0f;
            // What comes in the gust's first 1.5 s (nothing, far needles neither), then in each third of the rest: needles,
            // and the other pieces.
            int[] needlesByThird = new int[3], othersByThird = new int[3];
            int needlesSeen = WindDebris.Sent[0], othersSeen = WindDebris.Sent.Skip(1).Sum();
            int earlyPieces = 0, earlyFar = 0, farSeen = WindDebris.FarSent;
            float firstPieceAt = -1f, firstFarAt = -1f;
            const float GustSeconds = 14f;
            float third3 = (GustSeconds - WindDebris.StartSeconds) / 3f;
            zone.RPCA_ToggleWind(true, wind, GustSeconds);
            int shot = 0, thornsTaken = 0;
            bool volleyShot = false, farShot = false;
            float gustFrom = Time.time, thornsAt = Time.time;
            while (Time.time - gustFrom < GustSeconds)
            {
                yield return null;
                float since = Time.time - gustFrom;
                int newNeedles = WindDebris.Sent[0] - needlesSeen, newOthers = WindDebris.Sent.Skip(1).Sum() - othersSeen, newFar = WindDebris.FarSent - farSeen;
                if (newNeedles + newOthers > 0 && firstPieceAt < 0f)
                {
                    firstPieceAt = since;
                }
                if (newFar > 0 && firstFarAt < 0f)
                {
                    firstFarAt = since;
                }
                // The game's clock and this one may be a frame apart at the 1.5 s mark.
                if (since < WindDebris.StartSeconds - 0.1f)
                {
                    earlyPieces += newNeedles + newOthers;
                    earlyFar += newFar;
                }
                int third = Mathf.Clamp((int)((since - WindDebris.StartSeconds) / third3), 0, 2);
                needlesByThird[third] += newNeedles;
                othersByThird[third] += newOthers;
                needlesSeen = WindDebris.Sent[0];
                othersSeen = WindDebris.Sent.Skip(1).Sum();
                farSeen = WindDebris.FarSent;
                if (!volleyShot && since > 10f)
                {
                    // The needles streaming past: from 4 m to the scout's side, looking at them across the wind; then from
                    // 2 m upwind of them and 1.5 m up, looking into the wind as it comes.
                    volleyShot = true;
                    GameObject beside = new GameObject("UltimateAscentTestCamera");
                    Camera lens = beside.AddComponent<Camera>();
                    lens.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
                    lens.fieldOfView = 60f;
                    lens.nearClipPlane = 0.05f;
                    Vector3 alongWind = Vector3.ProjectOnPlane(wind, Vector3.up).normalized;
                    Vector3 acrossWind = Vector3.Cross(Vector3.up, alongWind);
                    beside.transform.position = local.Center + acrossWind * 4f + Vector3.up * 1f;
                    beside.transform.LookAt(local.Center);
                    yield return null;
                    Screenshot("wind-gust-side");
                    yield return new WaitForSeconds(0.2f);
                    beside.transform.position = local.Center - alongWind * 2f + Vector3.up * 1.5f + acrossWind * 0.5f;
                    beside.transform.rotation = Quaternion.LookRotation(-alongWind + Vector3.up * 0.05f);
                    yield return null;
                    Screenshot("wind-gust-coming");
                    yield return new WaitForSeconds(0.2f);
                    Destroy(beside);
                }
                if (Time.time - thornsAt > 1.5f)
                {
                    // Out with the thorns every 1.5 s: 20 of them knock a scout out (at 24 that is death, and the gust ends for them).
                    thornsAt = Time.time;
                    thornsTaken += Afflictions.physicalThorns.Count(t => t.stuckIn);
                    TidyThorns();
                }
                mostNeedles = Mathf.Max(mostNeedles, WindDebris.Needles);
                mostOthers = Mathf.Max(mostOthers, WindDebris.Others);
                mostFar = Mathf.Max(mostFar, WindDebris.FarCount);
                farthest = Mathf.Max(farthest, WindDebris.FarthestFrom(local.Center));
                strongest = Mathf.Max(strongest, WindDebris.Strength);
                if (!farShot && since > 8f)
                {
                    farShot = true;
                    GameObject zoom = new GameObject("UltimateAscentTestCamera");
                    Camera lens = zoom.AddComponent<Camera>();
                    lens.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
                    lens.fieldOfView = 18f;
                    zoom.transform.position = local.Head + Vector3.up * 0.3f;
                    zoom.transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(25f, Vector3.up) * -wind + Vector3.up * 0.08f);
                    yield return null;
                    Screenshot("wind-far-needles");
                    yield return new WaitForSeconds(0.3f);
                    Destroy(zoom);
                }
                if (shot < 2 && since > 6f + shot * 3f)
                {
                    shot++;
                    LookAtPoint(local, local.Center - wind * 10f);
                    yield return new WaitForSeconds(0.2f);
                    Screenshot($"wind-debris-{shot}");
                    yield return null;
                    yield return null;
                    yield return ClownPicture(local.Center, $"wind-debris-side-{shot}");
                }
            }
            int[] gust = WindDebris.Sent.Select((n, i) => n - sentBefore[i]).ToArray();
            int[] gustHits = WindDebris.Hits.Select((n, i) => n - hitsBeforeGust[i]).ToArray();
            zone.RPCA_ToggleWind(false, wind, 100000f);
            yield return new WaitForSeconds(WindDebris.LiesSeconds + WindDebris.FadeSeconds + 4f);
            int left = WindDebris.Needles + WindDebris.Others;
            int[] planned = WindDebris.Planned.ToArray();
            int[] passed = WindDebris.PassedBy.Select((n, i) => n - passedBefore[i]).ToArray();
            int passingNeedles = passed[0], others = gust.Skip(1).Sum(), passingOthers = passed.Skip(1).Sum();
            Check("a gust of the Roots wind brings what it should: 200-250 needles, 7-10 sticks, 13-17 branches, 4-6 rocks (18)",
                Enumerable.Range(0, 4).All(k => planned[k] >= WindDebris.PerGust[k].least && planned[k] <= WindDebris.PerGust[k].most && gust[k] <= planned[k] && gust[k] >= planned[k] * 0.9f - 1f),
                $"in a 14 s gust, planned needles {planned[0]}, sticks {planned[1]}, branches {planned[2]}, rocks {planned[3]}; sent {gust[0]}, {gust[1]}, {gust[2]}, {gust[3]}; "
                + $"dropped (nowhere in the open to set off from in time) {WindDebris.Dropped - droppedBefore}; places to set off from turned down as inside the land {WindDebris.StartsInside - insideBefore}");
            Check("no debris sets off in a gust's first 1.5 s, the far needles neither (18)",
                earlyPieces == 0 && earlyFar == 0 && firstPieceAt >= WindDebris.StartSeconds - 0.1f && firstFarAt >= WindDebris.StartSeconds - 0.1f,
                $"in the first {WindDebris.StartSeconds} s of a 14 s gust: pieces sent {earlyPieces}, far needles {earlyFar}; the first piece set off {firstPieceAt:0.00} s in, the first far needle {firstFarAt:0.00} s in");
            Check("the Roots wind's debris comes evenly through the rest of the gust, not all at once: needles and the other pieces alike in each third of it (18)",
                needlesByThird.Min() >= 0.75f * needlesByThird.Max() && othersByThird.Min() > 0 && othersByThird.Min() >= 0.5f * othersByThird.Max(),
                $"in the thirds of a 14 s gust after its first {WindDebris.StartSeconds} s: needles {string.Join(", ", needlesByThird)}; sticks, branches and rocks {string.Join(", ", othersByThird)}");
            Check("in a gust 65% of the needles fly past the scout and 35% at them, spread over the whole of them (not at one spot); half the sticks, branches and rocks fly past, half at them (18)",
                gust[0] > 0 && Mathf.Abs(passingNeedles / (float)gust[0] - WindDebris.PassingShare) <= 0.1f && others > 0 && Mathf.Abs(passingOthers / (float)others - WindDebris.PiecePassingShare) <= 0.25f
                    && gustHits[0] >= 8 && WindDebris.NeedlesOn.Count >= 5 && WindDebris.NeedlesOn.Values.Max() <= gustHits[0] * 0.5f && gustHits.Skip(1).Sum() >= 1,
                $"of {gust[0]} needles {passingNeedles} sent past them ({passingNeedles / (float)Mathf.Max(gust[0], 1):P0}), {gust[0] - passingNeedles} at them; "
                + $"of {others} sticks, branches and rocks {passingOthers} past them ({passingOthers / (float)Mathf.Max(others, 1):P0}), {others - passingOthers} at them; "
                + $"hits on the scout standing in the open: needles {gustHits[0]} (thorns stuck in {thornsTaken + Afflictions.physicalThorns.Count(t => t.stuckIn)}, pulled out every 1.5 s so the scout stays up), sticks {gustHits[1]}, branches {gustHits[2]}, rocks {gustHits[3]}; "
                + $"the needles hit {string.Join(", ", WindDebris.NeedlesOn.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}"))}");
            Check("a gust of the Roots wind carries debris round the scout: pieces only near them and never more than allowed, the far needles as pictures, all of it gone soon after (18)",
                mostNeedles <= WindDebris.MostNeedles && mostOthers <= WindDebris.MostPieces && farthest <= WindDebris.NearRadius * 2f && mostFar > 50 && left == 0,
                $"in a 14 s gust (strongest {strongest:0.00}): at most {mostNeedles} needles and {mostOthers} other pieces at a time, "
                + $"the farthest {farthest:0} m from the scout; at most {mostFar} far needles ({WindDebris.FarShading}); {left} left 7 s after the gust; "
                + $"{WindDebris.Stopped - stoppedBefore} stopped by the land, {WindDebris.CameNear - nearBefore} came within 2.5 m of them");
            TidyThorns();
            Protect(false);

            // With ascent 18's rule off (in memory): no debris.
            rule.Set(false);
            int quietBefore = WindDebris.Sent.Sum();
            zone.RPCA_ToggleWind(true, wind, 10f);
            yield return new WaitForSeconds(10f);
            zone.RPCA_ToggleWind(false, wind, 100000f);
            rule.Restore();
            Check("with ascent 18's rule off the wind carries no debris (18)", WindDebris.Sent.Sum() == quietBefore, $"{WindDebris.Sent.Sum() - quietBefore} pieces sent in a 10 s gust");
        }
        finally
        {
            if (wall != null)
            {
                Destroy(wall);
                Physics.SyncTransforms();
            }
            rule.Restore();
            WindDebris.OnlySent = false;
            FallRecorder.Of = null;
            zone.RPCA_ToggleWind(false, wind, 100000f);
            ClearStatuses(Afflictions);
            Protect(true);
        }
    }

    /// <summary>
    /// A wall for the test, 6 m wide and 2.6 m high, this far downwind of a spot on the ground, across the wind: on the
    /// map's layer (a shelter to whoever stands behind it), or on the plain layer (solid to scouts and to what flies, but
    /// no shelter: the game, the mod and True Final Ascent, whose look at the sky goes all round, take only the land's
    /// layers for that).
    /// </summary>
    private static GameObject WindWall(Vector3 spot, Vector3 wind, float off = 0.75f, bool shelters = true)
    {
        Vector3 along = Vector3.ProjectOnPlane(wind, Vector3.up).normalized;
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "UltimateAscent Test Wind Wall";
        wall.layer = LayerMask.NameToLayer(shelters ? "Map" : "Default");
        Grey(wall);
        wall.transform.SetPositionAndRotation(spot + along * off + Vector3.up * 1.1f, Quaternion.LookRotation(along));
        wall.transform.localScale = new Vector3(6f, 2.6f, 0.4f);
        Physics.SyncTransforms();
        return wall;
    }

    /// <summary>A switch set in memory for a while, and put back as it was.</summary>
    private sealed class ConfigEntryHolder
    {
        private readonly BepInEx.Configuration.ConfigEntry<bool> entry;
        private readonly bool was;
        private bool changed;

        internal ConfigEntryHolder(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            this.entry = entry;
            was = entry != null && entry.Value;
        }

        internal void Set(bool value)
        {
            if (entry == null)
            {
                return;
            }
            entry.Value = value;
            changed = true;
            HostOptions.Invalidate();
        }

        internal void Restore()
        {
            if (entry == null || !changed)
            {
                return;
            }
            entry.Value = was;
            changed = false;
            HostOptions.Invalidate();
        }
    }

    private static IEnumerator UntilUp(Character scout)
    {
        for (float until = Time.time + 8f; Time.time < until && scout.data.fallSeconds > 0f;)
        {
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>One piece sent straight at the scout standing in the open (put back there first: a hit throws them), and what it did; a picture of it on its way.</summary>
    private IEnumerator DebrisHit(WindChillZone zone, Kind kind, Vector3 open)
    {
        Character local = Local;
        yield return UntilUp(local);
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, open + Vector3.up * 1.2f, false);
        yield return new WaitForSeconds(1.5f);
        ClearStatuses(Afflictions);
        TidyThorns();
        yield return new WaitForSeconds(0.3f);
        HashSet<ThornOnMe> stuckBefore = new HashSet<ThornOnMe>(Afflictions.physicalThorns.Where(t => t.stuckIn));
        float injuryBefore = Afflictions.GetCurrentStatus(Status.Injury);
        int hitsBefore = WindDebris.Hits[(int)kind];
        FallRecorder.Clear();
        FallRecorder.Of = local;
        float exposed = zone.windPlayerFactor;
        SoundsTimed.Heard.Clear();
        SoundsTimed.Listening = true;
        // For a needle, statuses locked while it flies and goes in (a thorn still sticks and counts on the bar): no other
        // status's sound (the sun's burn) among its own.
        bool wasLocked = local.statusesLocked;
        if (kind == Kind.Needle)
        {
            local.statusesLocked = true;
        }
        bool sent = false;
        float sentAt = Time.time;
        GameObject watcher = null;
        int tries = 0;
        for (; tries < 6 && WindDebris.Hits[(int)kind] == hitsBefore; tries++)
        {
            // Again if the land stops it on its way (a branch, a root in the line upwind).
            int stoppedBefore = WindDebris.Stopped;
            sent = WindDebris.Send(local, zone, kind, local.Center);
            sentAt = Time.time;
            while (Time.time < sentAt + 5f && WindDebris.Hits[(int)kind] == hitsBefore && WindDebris.Stopped == stoppedBefore)
            {
            // On its way, 5 m out: a picture of it from the side.
            Vector3? flying = WindDebris.Newest(kind);
            if (watcher == null && flying.HasValue && Vector3.Distance(flying.Value, local.Center) < 5f)
            {
                Vector3 across = Vector3.Cross(Vector3.up, (local.Center - flying.Value).normalized);
                watcher = new GameObject("UltimateAscentTestCamera");
                Camera camera = watcher.AddComponent<Camera>();
                camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
                camera.fieldOfView = 50f;
                camera.nearClipPlane = 0.05f;
                watcher.transform.position = flying.Value + across * 2.2f + Vector3.up * 0.4f;
                watcher.transform.LookAt(flying.Value);
                Screenshot($"wind-{kind.ToString().ToLowerInvariant()}-flying");
            }
            yield return null;
            }
            if (WindDebris.Hits[(int)kind] == hitsBefore)
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
        if (watcher != null)
        {
            Destroy(watcher);
        }
        float arrived = Time.time - sentAt;
        float hitAt = Time.time;
        bool hit = WindDebris.Hits[(int)kind] > hitsBefore;
        yield return new WaitForSeconds(0.6f);
        List<string> hitSounds = SoundsTimed.Around(hitAt, 0.4f, local.Center, 1.5f);
        SoundsTimed.Listening = false;
        local.statusesLocked = wasLocked;
        FallRecorder.Of = null;
        List<float> falls = FallRecorder.Seconds.ToList();
        float injury = Afflictions.GetCurrentStatus(Status.Injury) - injuryBefore;
        string seen = $"sent {sent} (the scout exposed {exposed:0.00}); it hit {hit} after {arrived:0.0} s ({WindDebris.LastHit}){(tries > 1 ? $", sent {tries} times (the land stopped the first)" : "")}; knock-downs {string.Join(", ", falls.Select(f => $"{f:0.###} s"))}; injury +{injury * 100f:0.#}";
        switch (kind)
        {
            case Kind.Needle:
                ThornOnMe thorn = Afflictions.physicalThorns.FirstOrDefault(t => t.stuckIn && !stuckBefore.Contains(t));
                GameObject shown = SpruceNeedles.NeedleOf(thorn);
                bool looks = thorn != null && SpruceNeedles.Shows(thorn) && shown != null && shown.GetComponent<MeshFilter>()?.sharedMesh == SpruceLook.Needle
                    && !thorn.GetComponentsInChildren<MeshRenderer>(true).Any(r => r.enabled && r.GetComponent<MeshFilter>()?.sharedMesh != null && r.GetComponent<MeshFilter>().sharedMesh.name.StartsWith("Cactus spine", StringComparison.Ordinal));
                if (thorn != null)
                {
                    // From a metre out along the needle, looking back at it.
                    Vector3 at = shown != null ? shown.transform.position : thorn.transform.position;
                    // The needle points its tip into the body: out is against its forward.
                    Vector3 outward = shown != null ? -shown.transform.forward : Vector3.up;
                    GameObject holder = new GameObject("UltimateAscentTestCamera");
                    Camera camera = holder.AddComponent<Camera>();
                    camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
                    camera.fieldOfView = 40f;
                    camera.nearClipPlane = 0.02f;
                    Vector3 side = Vector3.Cross(outward, Vector3.up).sqrMagnitude > 0.01f ? Vector3.Cross(outward, Vector3.up).normalized : Vector3.right;
                    holder.transform.position = at + outward * 1.3f + side * 0.6f + Vector3.up * 0.3f;
                    holder.transform.LookAt(at);
                    yield return new WaitForSeconds(0.2f);
                    Screenshot("wind-needle-stuck");
                    yield return new WaitForSeconds(0.3f);
                    Destroy(holder);
                }
                Check("a spruce needle the wind drives into a scout sticks in like a cactus thorn, and shows as a spruce needle (18)",
                    sent && hit && thorn != null && thorn.type == 0 && looks && Afflictions.GetCurrentStatus(Status.Thorns) > 0f && falls.Count == 0,
                    seen + $"; the thorn stuck in: {(thorn != null ? $"type {thorn.type} on '{thorn.transform.parent?.name}', a needle {looks}" : "none")}; thorns on the bar {Afflictions.GetCurrentStatus(Status.Thorns) * 100f:0.#}");
                if (thorn != null)
                {
                    // A cactus needle into the same part, as a cactus pricks (the game's way): the sounds of the two alike.
                    Vector3 prickAt = thorn.transform.position;
                    HashSet<ThornOnMe> beforeCactus = new HashSet<ThornOnMe>(Afflictions.physicalThorns.Where(t => t.stuckIn));
                    SoundsTimed.Heard.Clear();
                    SoundsTimed.Listening = true;
                    bool lockedBefore = local.statusesLocked;
                    local.statusesLocked = true;
                    float prickedAt = Time.time;
                    Afflictions.AddThorn(prickAt, 0);
                    yield return new WaitForSeconds(0.6f);
                    List<string> cactusSounds = SoundsTimed.Around(prickedAt, 0.4f, local.Center, 1.5f);
                    SoundsTimed.Listening = false;
                    local.statusesLocked = lockedBefore;
                    ThornOnMe cactus = Afflictions.physicalThorns.FirstOrDefault(t => t.stuckIn && !beforeCactus.Contains(t));
                    SFX_PlayOneShot slotSound = thorn.GetComponent<SFX_PlayOneShot>();
                    Check("a spruce needle going in sounds as a cactus needle does (the same sounds, the thorn's own) (18)",
                        cactus != null && cactusSounds.Count > 0 && hitSounds.SequenceEqual(cactusSounds),
                        $"the needle: [{string.Join(", ", hitSounds)}]; a cactus needle: [{string.Join(", ", cactusSounds)}]; the thorn's own sound '{slotSound?.sfx?.name}' (when shown {slotSound?.playOnEnable}, shown to its scout {thorn.visibleLocally})");
                    if (cactus != null)
                    {
                        Afflictions.RemoveThorn(cactus, removedByPlayer: false);
                    }
                    Afflictions.RemoveThorn(thorn, removedByPlayer: false);
                    yield return new WaitForSeconds(0.3f);
                    Check("pulled out, the needle's thorn looks like a cactus thorn again", !SpruceNeedles.Shows(thorn) && SpruceNeedles.NeedleOf(thorn) == null,
                        $"shows a needle {SpruceNeedles.Shows(thorn)}");
                }
                break;
            case Kind.Stick:
            case Kind.Branch:
                Check($"a {(kind == Kind.Stick ? "stick" : "Spruce Branch")} the wind throws at a scout knocks them down for 4 s, as a stick's swing does, without hurting them (18)",
                    sent && hit && falls.Count == 1 && Mathf.Abs(falls[0] - StickItem.Knockdown) < 0.001f && injury < 0.001f, seen);
                break;
            case Kind.Rock:
                Check("a rock the wind throws at a scout hits as a thrown stone that shatters on them: down for 2 s and 20 injury (18)",
                    sent && hit && falls.Count == 1 && Mathf.Abs(falls[0] - StoneItem.KnockdownMarked) < 0.001f && Mathf.Abs(injury - StoneItem.InjuryNow) < 0.013f, seen);
                break;
        }
        SoundsTimed.Listening = false;
        ClearStatuses(Afflictions);
    }

    /// <summary>
    /// Flat, firm ground near a point, inside the storm, where a scout stands open to a wind from this way (the game's
    /// sky and the land both: the most open found, if it is open enough, with nothing in the way 30 m upwind at chest
    /// height), or sheltered from it (the most sheltered);
    /// and how open the ground looked at was (a count by how open it is).
    /// </summary>
    /// <summary>For the TFA checks: TFA's rest stops spare the local scout, and TFA has made them this big (0: as TFA says).</summary>
    private static bool tfaSparing;
    private static float tfaSize;

    private static bool TfaSpares(Character who, ref bool __result)
    {
        if (tfaSparing && who != null && who.IsLocal)
        {
            __result = true;
            return false;
        }
        return true;
    }

    private static bool TfaSizeOf(Character who, ref float __result)
    {
        if (tfaSize > 0f && who != null && who.IsLocal)
        {
            __result = tfaSize;
            return false;
        }
        return true;
    }

    /// <summary>
    /// The wind's debris with True Final Ascent (tfa, with -WithTfa): a gust over a scout TFA's rest stops spare brings
    /// nothing (TFA's own answer bent to "spared" for the test, so its own wind gates spare them too); in the Builder,
    /// what flies passes its invisible walls between blocks (a test wall put among them, upwind) and stops at none of them;
    /// on a scout TFA says is 2.5 times as big (its answer bent) a needle hits from 2.5 times as far and sticks in as
    /// much bigger.
    /// </summary>
    private IEnumerator TfaWindChecks(WindChillZone zone, Vector3 wind, Vector3 open)
    {
        Character local = Local;
        Type restPause = TfaType("FinalAscentRestPause");
        Type sizeRange = TfaType("FinalAscentSizeRange");
        HarmonyLib.Harmony bends = new HarmonyLib.Harmony("UltimateAscent.SmokeTest.tfawind");
        Vector3 across = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(wind, Vector3.up).normalized);
        GameObject wall = null;
        try
        {
            if (restPause != null)
            {
                bends.Patch(HarmonyLib.AccessTools.Method(restPause, "Spares", new[] { typeof(Character) }), prefix: new HarmonyLib.HarmonyMethod(typeof(Runner), nameof(TfaSpares)));
            }
            if (sizeRange != null)
            {
                bends.Patch(HarmonyLib.AccessTools.Method(sizeRange, "Of", new[] { typeof(Character) }), prefix: new HarmonyLib.HarmonyMethod(typeof(Runner), nameof(TfaSizeOf)));
            }

            // A rest stop: a gust in the open while TFA spares the scout.
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, open + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
            if (restPause != null && UltimateAscent.Compat.Tfa.FinalAscentActive)
            {
                tfaSparing = true;
                WindDebris.OnlySent = false;
                int sentBefore = WindDebris.Sent.Sum();
                zone.RPCA_ToggleWind(true, wind, 6f);
                float exposedMost = 0f;
                bool seenSpared = false;
                for (float since = 0f; since < 5.5f; since += Time.deltaTime)
                {
                    yield return null;
                    exposedMost = Mathf.Max(exposedMost, zone.windPlayerFactor);
                    seenSpared |= UltimateAscent.Compat.Tfa.RestSpares(local);
                }
                int sent = WindDebris.Sent.Sum() - sentBefore;
                // The wind on again for good (at the start of a gust): pieces sent by the test only.
                WindDebris.OnlySent = true;
                zone.RPCA_ToggleWind(true, wind, 100000f);
                tfaSparing = false;
                Check("with True Final Ascent: a gust over a scout its rest stops spare brings no debris, as the wind there spares them (18)",
                    sent == 0 && seenSpared && exposedMost < WindDebris.ExposedFrom,
                    $"in a 6 s gust while TFA spared the scout (the mod saw it: {seenSpared}): {sent} pieces sent; the wind's own exposure for them at most {exposedMost:0.00} (TFA's rest gate)");
                yield return new WaitForSeconds(3f);
            }
            else
            {
                Info("the rest stop check is skipped", restPause == null ? "TFA has no rest stops (FinalAscentRestPause)" : "not in FINAL ASCENT");
            }

            // The Builder's invisible walls: where TFA's own stand, and a test wall among them upwind of the scout.
            if (UltimateAscent.Compat.Tfa.BuilderActive)
            {
                Transform holder = Singleton<MapHandler>.Instance != null ? Singleton<MapHandler>.Instance.transform.Find("FA_Walls") : null;
                Bounds storm = zone.windZoneBounds;
                List<Collider> walls = holder != null ? holder.GetComponentsInChildren<Collider>(false).Where(c => c.enabled).ToList() : new List<Collider>();
                Info("the Builder's invisible walls", holder == null ? "none (no FA_Walls)"
                    : $"{walls.Count} on now: " + string.Join("; ", walls.Select(c => $"{c.transform.parent?.name ?? c.name} on {LayerMask.LayerToName(c.gameObject.layer)} at {c.bounds.center:F0} size {c.bounds.size:F0}, in the Roots' storm {c.bounds.Intersects(storm)}")));
                if (holder != null)
                {
                    wall = WindWall(open, -wind, 3f);
                    wall.transform.SetParent(holder, true);
                    Physics.SyncTransforms();
                    bool counts = UltimateAscent.Compat.Tfa.BuilderWall(wall.GetComponent<Collider>());
                    int nearBefore = WindDebris.CameNear, stoppedBefore = WindDebris.Stopped;
                    WindDebris.StoppedBy.Clear();
                    int sent = 0;
                    for (int i = 0; i < 6; i++)
                    {
                        // Past the scout, a metre to one side: through the wall, near them.
                        sent += WindDebris.Send(local, zone, Kind.Needle, local.Center + across * (i % 2 == 0 ? 1f : -1f)) ? 1 : 0;
                        yield return new WaitForSeconds(0.25f);
                    }
                    yield return new WaitForSeconds(3.5f);
                    Collider wallBody = wall.GetComponent<Collider>();
                    int atWall = WindDebris.StoppedBy.Count(c => c == wallBody);
                    Check("with True Final Ascent's Builder: what the wind carries passes its invisible walls between blocks (nothing there to see) (18)",
                        counts && sent >= 4 && atWall == 0 && WindDebris.CameNear - nearBefore >= 2,
                        $"a test wall among TFA's (TFA takes it for one of its own: {counts}), 3 m upwind; {sent} needles sent through it past the scout; came within 2.5 m of them {WindDebris.CameNear - nearBefore}; "
                        + $"stopped {WindDebris.Stopped - stoppedBefore}, at the wall {atWall}, by {string.Join(", ", WindDebris.StoppedBy.Select(c => c != null ? c.name : "?").Distinct())}");
                    Destroy(wall);
                    wall = null;
                    Physics.SyncTransforms();
                }
            }
            else
            {
                Info("the Builder's wall check is skipped", "not a Builder climb");
            }

            // TFA's Mega size: its answer for the scout bent to 2.5 times (TFA grows a scout only in its Apocalypse or a mod's
            // weather run; scaling the body here by hand throws it about).
            if (sizeRange != null)
            {
                const float Big = 2.5f;
                tfaSize = Big;
                yield return UntilUp(local);
                HashSet<ThornOnMe> stuckBefore = new HashSet<ThornOnMe>(Afflictions.physicalThorns.Where(t => t.stuckIn));
                SpruceNeedles.StickIn(local, local.Center + across * 0.3f + Vector3.up * 0.2f);
                yield return new WaitForSeconds(0.3f);
                ThornOnMe thorn = Afflictions.physicalThorns.FirstOrDefault(t => t.stuckIn && !stuckBefore.Contains(t));
                GameObject shown = SpruceNeedles.NeedleOf(thorn);
                float length = shown != null ? shown.transform.lossyScale.z : 0f;
                float reach = WindDebris.ReachOf(local, Kind.Needle);
                float said = UltimateAscent.Compat.Tfa.SizeOf(local);
                tfaSize = 0f;
                TidyThorns();
                Check("with True Final Ascent's Mega size: how near a needle hits a scout and how big it sticks in go by their size (18)",
                    Mathf.Abs(said - Big) < 0.001f && Mathf.Abs(length - SpruceNeedles.Length * Big) < 0.02f && Mathf.Abs(reach - WindDebris.NeedleReach * Big) < 0.001f
                        && Mathf.Abs(WindDebris.ReachOf(local, Kind.Needle) - WindDebris.NeedleReach) < 0.001f,
                    $"TFA says the scout is {said:0.0}x as big: a needle stuck in is {length:0.00} m long (a normal scout's {SpruceNeedles.Length} m), it hits them from {reach:0.00} m (a normal scout's {WindDebris.NeedleReach} m); "
                    + $"back to TFA's own answer, {WindDebris.ReachOf(local, Kind.Needle):0.00} m");
            }
        }
        finally
        {
            tfaSparing = false;
            tfaSize = 0f;
            if (wall != null)
            {
                Destroy(wall);
                Physics.SyncTransforms();
            }
            WindDebris.OnlySent = true;
            zone.RPCA_ToggleWind(true, wind, 100000f);
            bends.UnpatchSelf();
        }
        yield return new WaitForSeconds(1.5f);
    }

    /// <summary>Whether anything solid lies on the line between two spots (meeting either face of it: a rock's or a hill's back too).</summary>
    private static bool LandBetween(Vector3 from, Vector3 to)
    {
        bool backs = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            return Physics.Linecast(from, to, out RaycastHit there, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore) && Vector3.Distance(there.point, to) > 0.6f;
        }
        finally
        {
            Physics.queriesHitBackfaces = backs;
        }
    }

    private static Vector3? DebrisSpot(WindChillZone zone, Vector3 around, Vector3 wind, bool open, out float found, out string ground)
    {
        Vector3? best = null;
        found = open ? -1f : 2f;
        float bestScore = float.NegativeInfinity;
        int[] counts = new int[5];
        for (int ring = 0; ring <= 12; ring++)
        {
            float reach = 3f + ring * 10f;
            for (int angle = 0; angle < 360; angle += 15)
            {
                Vector3 above = around + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * reach + Vector3.up * 40f;
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit floor, 100f, HelperFunctions.terrainMapMask) || floor.normal.y < 0.85f
                    || !zone.windZoneBounds.Contains(floor.point + Vector3.up)
                    || floor.collider.GetComponentsInParent<Transform>().Any(t => t.name.IndexOf("Redwood", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0)
                    || InsideSomething(floor.point + Vector3.up) || !Foothold(floor.point) || Hazard(floor.point + Vector3.up, 1.5f))
                {
                    continue;
                }
                float sky = WindChillZone.GetWindIntensityAtPoint(floor.point + Vector3.up, zone.lightVolumeSampleThreshold_lower, zone.lightVolumeSampleThreshold_margin);
                float land = SnowstormShelter.Openness(floor.point + Vector3.up * 1.6f, wind);
                float exposure = Mathf.Min(sky, land);
                counts[Mathf.Clamp((int)(exposure * 5f), 0, 4)]++;
                if (open && (exposure < 0.5f || Physics.SphereCast(floor.point + Vector3.up * 1.1f, 0.35f, -wind, out _, 30f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)))
                {
                    // Not open enough, or something low in the way upwind (it would stop what flies at the scout's chest).
                    continue;
                }
                // Open ground: the most of it clear a little to either side of the wind too (what flies comes up to 8°
                // off it), then the most open; sheltered ground: the least open.
                int clear = open ? new[] { -8f, -4f, 4f, 8f }.Count(yaw => !Physics.SphereCast(floor.point + Vector3.up * 1.1f, 0.35f, Quaternion.AngleAxis(yaw, Vector3.up) * -wind, out _, 36f, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore)) : 0;
                float score = open ? clear + exposure : -exposure;
                if (score > bestScore)
                {
                    bestScore = score;
                    found = exposure;
                    best = floor.point;
                }
            }
        }
        ground = $"{counts.Sum()} spots inside the storm: open 0-0.2 {counts[0]}, 0.2-0.4 {counts[1]}, 0.4-0.6 {counts[2]}, 0.6-0.8 {counts[3]}, 0.8-1 {counts[4]}";
        return open ? (found >= 0.5f ? best : null) : (found <= 0.15f ? best : null);
    }

    /// <summary>
    /// The Spruce Branch's look for its icon (branchicon, only when asked): its mesh written out (points, normals, the
    /// colour's place along each part, the parts by material, in the space of the stick it is built on) for the icon to be
    /// drawn from outside the game; drawing it off-screen here draws nothing in this game's pipeline.
    /// </summary>
    private IEnumerator BranchIcon()
    {
        yield return null;
        Item branch = SpruceBranch.Template;
        MeshFilter look = branch != null ? branch.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "SpruceBranch") : null;
        Mesh mesh = look != null ? look.sharedMesh : null;
        if (mesh == null)
        {
            Fail("the Spruce Branch's look for its icon");
            yield break;
        }
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        System.Globalization.CultureInfo plain = System.Globalization.CultureInfo.InvariantCulture;
        foreach (Vector3 v in mesh.vertices)
        {
            text.Append("v ").Append(v.x.ToString("R", plain)).Append(' ').Append(v.y.ToString("R", plain)).Append(' ').Append(v.z.ToString("R", plain)).Append('\n');
        }
        foreach (Vector3 n in mesh.normals)
        {
            text.Append("vn ").Append(n.x.ToString("R", plain)).Append(' ').Append(n.y.ToString("R", plain)).Append(' ').Append(n.z.ToString("R", plain)).Append('\n');
        }
        foreach (Vector2 uv in mesh.uv)
        {
            text.Append("vt ").Append(uv.x.ToString("R", plain)).Append(' ').Append(uv.y.ToString("R", plain)).Append('\n');
        }
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            text.Append("g ").Append(sub).Append('\n');
            int[] tris = mesh.GetTriangles(sub);
            for (int i = 0; i < tris.Length; i += 3)
            {
                text.Append("f ").Append(tris[i] + 1).Append(' ').Append(tris[i + 1] + 1).Append(' ').Append(tris[i + 2] + 1).Append('\n');
            }
        }
        string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "UltimateAscent.SmokeTest.sprucebranch.obj");
        System.IO.File.WriteAllText(path, text.ToString());
        Info("the Spruce Branch's mesh for its icon", $"{System.IO.Path.GetFileName(path)}: {mesh.vertexCount} points, {mesh.subMeshCount} parts, {mesh.triangles.Length / 3} triangles, the stick's scale {look.transform.lossyScale.x:0.###}");
    }

    /// <summary>
    /// Close looks at the spruce (sprucelook, only when asked, in the Roots): three needles stuck in the front of the scout,
    /// seen from 1.4 m; a flying needle at its size half a metre from the camera; a Spruce Branch lying on the ground.
    /// </summary>
    private IEnumerator SpruceLookPictures()
    {
        Character local = Local;
        if (Map.CurrentPlace != Place.Roots)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(8f);
        }
        // Onto the Roots' own ground (the segment starts at the campfire below it, on the beach): over the nearest Spruce Branch lying there.
        Item lyingBranch = GroundItems.Placed.Values.SelectMany(v => v).Where(i => i != null && SpruceBranch.IsBranch(i)).OrderBy(i => Vector3.Distance(i.transform.position, local.Center)).FirstOrDefault();
        if (lyingBranch != null)
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, lyingBranch.transform.position + Vector3.up * 1.5f, false);
            yield return new WaitForSeconds(3f);
        }
        Vector3 look = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, look);
        Protect(false);
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        try
        {
            Camera camera = holder.AddComponent<Camera>();
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.02f;
            // Needles in the scout's front and sides.
            foreach (Vector3 at in new[] { local.Center + look * 0.35f, local.Center + right * 0.35f + Vector3.up * 0.15f, local.Center - right * 0.35f - Vector3.up * 0.1f })
            {
                SpruceNeedles.StickIn(local, at);
            }
            yield return new WaitForSeconds(0.5f);
            // The whole scout, from 2.2 m in front.
            holder.transform.position = local.Center + look * 2.2f + Vector3.up * 0.4f;
            holder.transform.LookAt(local.Center + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.3f);
            Screenshot("spruce-needles-in-scout");
            yield return new WaitForSeconds(0.3f);
            // Each stuck needle from its side, about 60 cm off and away from the body: its tip in the body, the rest out.
            int side = 0;
            foreach (ThornOnMe thorn in Afflictions.physicalThorns.Where(t => t.stuckIn && SpruceNeedles.Shows(t)).Take(3))
            {
                GameObject shown = SpruceNeedles.NeedleOf(thorn);
                if (shown == null)
                {
                    continue;
                }
                Vector3 axis = shown.transform.forward;
                Vector3 outward = -axis;
                Vector3 across = Vector3.Cross(axis, Vector3.up).sqrMagnitude > 0.01f ? Vector3.Cross(axis, Vector3.up).normalized : Vector3.right;
                if (Vector3.Dot(across, shown.transform.position - local.Center) < 0f)
                {
                    across = -across;
                }
                holder.transform.position = shown.transform.position + across * 0.55f + outward * 0.3f;
                holder.transform.LookAt(shown.transform.position);
                Info($"stuck needle {++side}", $"on '{thorn.transform.parent?.name}', its middle {Vector3.Distance(shown.transform.position, thorn.transform.position):0.000} m from the thorn's spot, pointing {axis:F2}, "
                    + $"{shown.transform.lossyScale.z:0.000} m long and {shown.transform.lossyScale.x * 2f * SpruceLook.NeedleRadius * 100f:0.0} cm thick at its base");
                yield return new WaitForSeconds(0.3f);
                Screenshot($"spruce-needle-stuck-{side}");
                yield return new WaitForSeconds(0.3f);
            }
            TidyThorns();
            // A flying needle at its size, half a metre in front of the camera, against the sky.
            GameObject needle = new GameObject("UltimateAscent Test Needle");
            needle.AddComponent<MeshFilter>().sharedMesh = SpruceLook.Needle;
            needle.AddComponent<MeshRenderer>().sharedMaterial = SpruceLook.Materials[SpruceLook.Green];
            needle.transform.localScale = SpruceNeedles.Scale;
            holder.transform.position = local.Head + Vector3.up * 1.5f;
            holder.transform.rotation = Quaternion.LookRotation(look + Vector3.up * 0.4f);
            needle.transform.SetPositionAndRotation(holder.transform.position + holder.transform.forward * 0.5f, Quaternion.LookRotation(holder.transform.right));
            yield return new WaitForSeconds(0.3f);
            Screenshot("spruce-needle-flying-size");
            yield return new WaitForSeconds(0.3f);
            Destroy(needle);
            // A Spruce Branch lying on the Roots' ground.
            Item branch = GroundItems.Placed.Values.SelectMany(v => v).Where(i => i != null && SpruceBranch.IsBranch(i)).OrderBy(i => Vector3.Distance(i.transform.position, local.Center)).FirstOrDefault();
            if (branch != null)
            {
                Vector3 at = branch.transform.position;
                holder.transform.position = at + new Vector3(0.8f, 0.9f, 0.8f);
                holder.transform.LookAt(at);
                yield return new WaitForSeconds(0.3f);
                Screenshot("spruce-branch-on-ground");
                yield return new WaitForSeconds(0.3f);
            }
            else
            {
                Info("no Spruce Branch lying near for its picture");
            }
        }
        finally
        {
            Destroy(holder);
            TidyThorns();
            Protect(true);
        }
    }

    /// <summary>The Spruce Branch: its own name, the Stick's swing, and on the Roots' ground in the sticks' place.</summary>
    private void SpruceBranchChecks()
    {
        Item branch = SpruceBranch.Template;
        if (branch == null)
        {
            Fail("the Spruce Branch is built");
            return;
        }
        List<Item> lying = GroundItems.Placed.Values.SelectMany(v => v).Where(i => i != null && Map.TryGetGroundBiome(i.transform.position + Vector3.up * 0.5f, out Biome.BiomeType b) && b == Biome.BiomeType.Roots).ToList();
        int branches = lying.Count(SpruceBranch.IsBranch);
        int sticks = lying.Count(i => StickItem.IsStick(i) && !SpruceBranch.IsBranch(i));
        Check("the Spruce Branch is an item of its own (its name), swung like the Stick, and lies on the Roots' ground in the sticks' place (18)",
            string.Equals(Plain(branch.GetItemName()), SpruceBranch.ItemName, StringComparison.OrdinalIgnoreCase) && branch.UIData.icon != null && branch.UIData.icon.name == "SpruceBranch_icon" && StickItem.IsStick(branch) && branch.UIData.mainInteractPrompt == StickItem.SwingPrompt && branches > 0 && sticks == 0,
            $"named '{Plain(branch.GetItemName())}', icon '{branch.UIData.icon?.name}', swung like a stick {StickItem.IsStick(branch)} ('{branch.UIData.mainInteractPrompt}'); on the Roots' ground {branches} branches and {sticks} sticks");
    }
}
