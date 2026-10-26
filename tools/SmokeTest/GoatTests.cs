using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the Alps' mountain goats (17).</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The mountain goats (17, on a map with the Alps): where they live (up to 12, alone or in pairs, along the whole way
    /// up the Alps); left alone for 45 s they graze and roam the mountainside, on the ground, their hooves heard; one goes up
    /// a ledge too steep for a beetle by leaping onto it, and back down; with a scout 12 m off one decides every 20 s
    /// (told not to charge, it grazes on); told to charge, it lowers its head and paws the ground for 1 s, charges and
    /// rams the scout (knocked down, thrown, hurt); a scout who steps aside at the last moment makes it miss.
    /// </summary>
    /// <summary>The test's sheer step or platform, and its slope (blocks on the map's layer), while they stand.</summary>
    private static GameObject ledgeBlock;
    private static GameObject plank;

    /// <summary>A test block shown in plain grey (the primitive's own material does not draw in the game's pipeline).</summary>
    private static void Grey(GameObject block)
    {
        Renderer look = block.GetComponent<Renderer>();
        if (look != null)
        {
            Material grey = new Material(Snakes.Skin()) { name = "UltimateAscentTestGrey" };
            grey.SetTexture("_BaseTexture", Texture2D.whiteTexture);
            grey.SetColor("_Tint", new Color(0.45f, 0.45f, 0.47f));
            grey.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.47f));
            look.sharedMaterial = grey;
        }
    }

    /// <summary>How far a point is from a segment.</summary>
    private static float ToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector3.Distance(point, a + ab * t);
    }

    private IEnumerator GoatChecks()
    {
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;
        if (Map.CurrentPlace != Place.Alpine)
        {
            MapHandler.JumpToSegment(Segment.Alpine);
            yield return new WaitForSeconds(8f);
        }
        if (Map.CurrentPlace != Place.Alpine)
        {
            Info("the goat checks are skipped", $"no Alps here ({Map.CurrentPlace}); run with -Biome and a map number that has the Alps (80)");
            yield break;
        }
        for (float until = Time.time + 10f; Time.time < until && Goats.All.Count == 0;)
        {
            yield return null;
        }
        List<Goat> goats = Goats.All.Where(g => g != null).ToList();
        MapHandler map = Singleton<MapHandler>.Instance;
        Vector3 first = map.segments[(int)Segment.Alpine - 1].segmentCampfire.transform.position;
        Vector3 last = map.segments[(int)Segment.Alpine].segmentCampfire.transform.position;
        float Share(Vector3 p)
        {
            Vector3 along = last - first;
            along.y = 0f;
            Vector3 from = p - first;
            from.y = 0f;
            return Vector3.Dot(from, along.normalized) / along.magnitude;
        }
        List<IGrouping<int, Goat>> pairs = goats.Where(g => g.PairKey > 0).GroupBy(g => g.PairKey).ToList();
        int alone = goats.Count(g => g.PairKey == 0);
        bool together = pairs.All(p => p.Count() == 2 && Goats.Flat(p.First().Home, p.Last().Home) <= 4f);
        bool inAlps = goats.All(g => Map.TryGetGroundBiome(g.Home + Vector3.up * 0.5f, out Biome.BiomeType biome) && biome == Biome.BiomeType.Alpine);
        float[] shares = goats.Select(g => Share(g.Home)).OrderBy(s => s).ToArray();
        Check("mountain goats live in the Alps, at most 12, alone or in pairs, from where the Alps begin up their climb (17)",
            goats.Count > 0 && goats.Count <= Goats.MaxGoats && together && inAlps && shares.First() < 0.35f && shares.Last() > 0.65f,
            $"{goats.Count} goats: {alone} alone, {pairs.Count} pairs (each pair's spots within 4 m: {together}); all on the Alps' ground: {inAlps}; "
            + $"along the way from the Alps' first campfire to their last (0 to 1): {string.Join(", ", shares.Select(s => s.ToString("0.00")))}; spots found {Goats.SpotsFound} of {Goats.SpotsTried} looked at");
        Info("the goats", string.Join("; ", goats.Select(g => $"{g.Doing} at {g.Feet:F0}, {Share(g.Home):0.00} of the way, {g.Home.y - first.y:0} m above the first campfire{(g.PairKey > 0 ? $", pair {g.PairKey}" : "")}"
            + (NewMobs.OnTheClimb(g.Home) ? ", on the climb" : ", on the way to it"))));
        if (goats.Count == 0)
        {
            yield break;
        }

        Protect(true);
        Action calm = CalmForMeasuring();
        Harmony keys = null;
        try
        {
            foreach (Goat goat in goats)
            {
                goat.Unaware = true;
            }
            // Left alone (noticing nobody), the goats near the scout go about as they do: 45 s of it by one of them.
            Goat watched = null;
            Vector3? watchFrom = null;
            foreach (Goat goat in goats.OrderBy(g => Vector3.Distance(g.Feet, local.Center)))
            {
                watchFrom = StandNear(goat.Feet + Vector3.up * 0.6f, 6f, 12f, seeIt: true) ?? StandNear(goat.Feet + Vector3.up * 0.6f, 6f, 14f, seeIt: true, firm: false);
                if (watchFrom.HasValue)
                {
                    watched = goat;
                    break;
                }
            }
            if (watched == null)
            {
                Fail("a place to watch a goat from", "none 6-14 m from any goat");
                yield break;
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, watchFrom.Value + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
            Dictionary<Goat, Vector3> lastSeen = goats.ToDictionary(g => g, g => g.Feet);
            Dictionary<Goat, float> walked = goats.ToDictionary(g => g, g => 0f);
            Dictionary<Goat, int> leapsBefore = goats.ToDictionary(g => g, g => g.Leaps);
            HashSet<string> doings = new HashSet<string>();
            Dictionary<Goat, int> bleatsBefore = goats.ToDictionary(g => g, g => g.Bleats);
            int heardBefore = Goats.BleatsHeard;
            float loudestHooves = 0f;
            // Every goat near enough to be heard bleats once now; after that each bleats as it likes (every 25-70 s).
            List<Goat> told = goats.Where(g => Vector3.Distance(g.Feet, local.Center) <= Goats.BleatHeardFrom).ToList();
            foreach (Goat goat in told)
            {
                goat.BleatSoon();
            }
            float floated = 0f, sunk = 0f;
            string floatedAt = "", sunkAt = "";
            // How far the knees and the hooves (at their fetlocks) of the goats walking about bend.
            float kneeMost = 0f, flickMost = 0f;
            foreach (Goat goat in goats.Where(g => g != null))
            {
                goat.ResetBends();
            }
            int shot = 0;
            float nextShot = 0f;
            for (float since = 0f; since < 45f; since += Time.deltaTime)
            {
                yield return null;
                foreach (Goat goat in goats.Where(g => g != null && Vector3.Distance(g.Feet, local.Center) <= 120f))
                {
                    walked[goat] += Vector3.Distance(lastSeen[goat], goat.Feet);
                    lastSeen[goat] = goat.Feet;
                    doings.Add(goat.Doing);
                    if (goat.Speed > 0.3f && goat.Doing != "Leaping")
                    {
                        loudestHooves = Mathf.Max(loudestHooves, goat.HoofLoudness);
                    }
                    if (goat.Doing == "Roaming" && goat.Speed > 0.6f)
                    {
                        kneeMost = Mathf.Max(kneeMost, goat.KneeBend);
                        flickMost = Mathf.Max(flickMost, goat.HoofFlick);
                    }
                    if (goat.Doing != "Leaping" && goat.Doing != "Falling")
                    {
                        float above = AboveGround(goat.Feet);
                        float under = UnderGround(goat.Feet + Vector3.up * 0.05f);
                        if (above > floated)
                        {
                            floated = above;
                            floatedAt = $"{goat.Doing} at {goat.Feet:F1}";
                        }
                        if (under > sunk)
                        {
                            sunk = under;
                            sunkAt = $"{goat.Doing} at {goat.Feet:F1}";
                        }
                    }
                }
                foreach (Goat goat in goats.Where(g => g != null))
                {
                    goat.ResetBends();
                }
                if (since >= nextShot)
                {
                    nextShot += 9f;
                    LookAtPoint(local, watched.Feet + Vector3.up * 0.6f);
                    yield return new WaitForSeconds(0.3f);
                    Screenshot($"goat-roam-{++shot}");
                }
            }
            List<Goat> awake = goats.Where(g => g != null && Vector3.Distance(g.Feet, local.Center) <= 120f).ToList();
            int leaps = awake.Sum(g => g.Leaps - leapsBefore[g]);
            Check("left alone, the goats near a scout graze, look about and roam the mountainside (17)",
                awake.Count > 0 && awake.Sum(g => walked[g]) >= 8f && doings.Contains("Grazing") && doings.Contains("Roaming"),
                $"{awake.Count} goats within 120 m went {string.Join(", ", awake.Select(g => $"{walked[g]:0.0} m"))} in 45 s; seen {string.Join(", ", doings)}; "
                + $"leaps {leaps}, the highest {awake.Select(g => g.HighestLeap).DefaultIfEmpty(0f).Max():0.0} m up; the steepest slope walked {awake.Select(g => g.SteepestWalked).DefaultIfEmpty(0f).Max():0} degrees");
            Check("a goat keeps to the ground: never in the air (but in a leap), never sunk into it (17)", floated < 0.35f && sunk < 0.2f,
                $"at most {floated:0.00} m above the ground ({floatedAt}), {sunk:0.00} m under it ({sunkAt})");
            Check("their hooves are heard while they move (the user's horse recording, laid out into a long loop) (17)", loudestHooves > 0.2f,
                $"the hooves of a moving goat up to {loudestHooves:0.00} (of the sound's loudness)");
            Check("walking, a goat's legs bend: each hoof lifted with the knee folding and the hoof flicking up at its joint (17)",
                kneeMost >= 40f && flickMost >= 20f,
                $"while the goats walked, their knees bent up to {kneeMost:0} degrees and their hooves up to {flickMost:0}");
            int heard = Goats.BleatsHeard - heardBefore;
            int most = goats.Max(g => g.Bleats - bleatsBefore[g]);
            Check("goats bleat now and then (the user's recordings), never constantly: each near enough told to once, then at most every 25 s (17)",
                heard >= told.Count && most <= 2,
                $"{heard} bleats heard in 45 s from {told.Count} goats within {Goats.BleatHeardFrom:0} m (told once each), the most from one goat {most}");

            // A stage: open, nearly flat ground (16 m), for the ledge and the charges.
            if (!GoatStage(local.Center, out Vector3 stage, out Vector3 way))
            {
                Fail("open, nearly flat ground for a goat's leap and its charge (16 m)", $"none near {local.Center:F0}");
                yield break;
            }
            // A sheer 2.2 m step on it (a test block, the map's own ground for a goat): too steep to walk, or for a beetle to
            // crawl. A goat 2 m in front of it goes up onto it, leaping, then back down.
            Goat climber = watched;
            climber.Puppet = true;
            Vector3 face = GroundUnder(stage + way * 5f);
            Vector3 foot = GroundUnder(stage + way * 3f);
            ledgeBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ledgeBlock.name = "UltimateAscentTestLedge";
            ledgeBlock.layer = LayerMask.NameToLayer("Map");
            ledgeBlock.transform.SetPositionAndRotation(face + way * 1.25f + Vector3.up * 0.95f, Quaternion.LookRotation(way));
            ledgeBlock.transform.localScale = new Vector3(3f, 2.5f, 2.5f);
            Grey(ledgeBlock);
            Physics.SyncTransforms();
            Vector3 top = GroundUnder(face + way * 1.25f + Vector3.up * 3f);
            {
                climber.PlaceAt(foot, top);
                Vector3 middle = (foot + top) * 0.5f;
                Vector3? see = StandNear(middle, 5f, 9f, seeIt: true) ?? StandNear(middle, 5f, 12f, seeIt: true, firm: false);
                if (see.HasValue)
                {
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, see.Value + Vector3.up * 1.2f, false);
                }
                yield return new WaitForSeconds(1.5f);
                LookAtPoint(local, middle + Vector3.up * 0.5f);
                yield return new WaitForSeconds(0.4f);
                Screenshot("goat-ledge-foot");
                int leapsUp = climber.Leaps;
                climber.RoamTo(top);
                bool up = false;
                int leapShot = 0;
                float stepClear = float.MaxValue;
                string stepWorst = "";
                List<string> trace = new List<string>();
                float nextTrace = 0f;
                for (float until = Time.time + 10f; Time.time < until && !up;)
                {
                    yield return null;
                    if (Time.time >= nextTrace)
                    {
                        nextTrace = Time.time + 0.5f;
                        trace.Add($"{climber.Doing} {climber.Feet:F1} ({climber.LastStop})");
                    }
                    NoteClearance(climber, ref stepClear, ref stepWorst, ledgeBlock);
                    if (climber.Doing == "Leaping" && leapShot < 3)
                    {
                        Screenshot($"goat-ledge-leap-{++leapShot}");
                        yield return new WaitForSeconds(0.12f);
                    }
                    up = Goats.Flat(climber.Feet, top) < 0.8f && Mathf.Abs(climber.Feet.y - top.y) < 0.6f && climber.Doing != "Leaping";
                }
                yield return new WaitForSeconds(0.4f);
                Screenshot("goat-ledge-top");
                int leapsDown = climber.Leaps;
                climber.RoamTo(foot);
                bool down = false;
                for (float until = Time.time + 10f; Time.time < until && !down;)
                {
                    yield return null;
                    NoteClearance(climber, ref stepClear, ref stepWorst, ledgeBlock);
                    down = Goats.Flat(climber.Feet, foot) < 0.8f && Mathf.Abs(climber.Feet.y - foot.y) < 0.6f && climber.Doing != "Leaping";
                }
                Check("a goat goes up a sheer step too steep to walk or for a beetle to crawl, leaping onto it, and leaps back down (17)",
                    up && down && climber.Leaps > leapsUp && climber.Leaps > leapsDown,
                    $"a {top.y - foot.y:0.0} m step (a test block), its top {Goats.Flat(foot, top):0.0} m off; "
                    + $"up {up} with {leapsDown - leapsUp} leaps, down {down} with {climber.Leaps - leapsDown}; it is {climber.Doing}, last stopped by {climber.LastStop}");
                Check("coming to the step, leaping onto it and back down, the goat's head and body never go into it (17)",
                    stepClear >= 0.05f,
                    $"its body, neck, head, brow and muzzle at least {stepClear:0.00} m from the block (the nearest: {stepWorst})");
                Info("the goat on its way up the step, every half second", string.Join("; ", trace));
                Info("its last look for a foothold", climber.LeapReport);
            }
            Destroy(ledgeBlock);
            Physics.SyncTransforms();

            // A slope of 75 degrees (a test plank up to a platform 2.6 m high): steeper than a beetle crawls (70); a goat walks
            // up it, no leap.
            Vector3 across = Vector3.Cross(Vector3.up, way);
            Vector3 bottom = GroundUnder(stage + way * 4.9f);
            Vector3 edge = GroundUnder(stage + way * 5.6f);
            edge.y = bottom.y + 2.6f;
            Vector3 deck = GroundUnder(stage + way * 6.6f);
            ledgeBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ledgeBlock.name = "UltimateAscentTestPlatform";
            ledgeBlock.layer = LayerMask.NameToLayer("Map");
            ledgeBlock.transform.SetPositionAndRotation(new Vector3(deck.x, bottom.y + 1.15f, deck.z), Quaternion.LookRotation(way));
            ledgeBlock.transform.localScale = new Vector3(2.5f, 2.9f, 2f);
            plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "UltimateAscentTestSlope";
            plank.layer = LayerMask.NameToLayer("Map");
            Vector3 slope = (edge - bottom).normalized;
            Vector3 slopeFace = Vector3.Cross(across, slope);
            if (slopeFace.y < 0f)
            {
                slopeFace = -slopeFace;
            }
            float slopeLength = Vector3.Distance(bottom, edge);
            plank.transform.SetPositionAndRotation((bottom + edge) * 0.5f - slope * 0.2f - slopeFace * 0.15f, Quaternion.LookRotation(slope, slopeFace));
            plank.transform.localScale = new Vector3(2.5f, 0.3f, slopeLength + 0.4f);
            Grey(ledgeBlock);
            Grey(plank);
            Physics.SyncTransforms();
            Vector3 deckTop = GroundUnder(new Vector3(deck.x, bottom.y + 4f, deck.z));
            {
                climber.PlaceAt(GroundUnder(stage + way * 3f), edge);
                Vector3 middle = (bottom + edge) * 0.5f;
                Vector3? see = StandNear(middle, 5f, 9f, seeIt: true) ?? StandNear(middle, 5f, 12f, seeIt: true, firm: false);
                if (see.HasValue)
                {
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, see.Value + Vector3.up * 1.2f, false);
                }
                yield return new WaitForSeconds(1.5f);
                LookAtPoint(local, middle);
                yield return new WaitForSeconds(0.4f);
                Screenshot("goat-slope-foot");
                int leapsBefore75 = climber.Leaps;
                climber.RoamTo(deckTop);
                bool upSlope = false;
                bool slopeShot = false;
                // On the slope's face: how near any part of its body and head came to it (below 0 inside it), and how often
                // two or more of its hooves were on it.
                float nearest = float.MaxValue;
                int onFace = 0, onSlope = 0;
                float slopeClear = float.MaxValue;
                string slopeWorst = "";
                List<string> trace = new List<string>();
                float nextTrace = 0f;
                for (float until = Time.time + 12f; Time.time < until && !upSlope;)
                {
                    yield return null;
                    if (Time.time >= nextTrace)
                    {
                        nextTrace = Time.time + 0.5f;
                        trace.Add($"{climber.Doing} {climber.Feet:F1} ({climber.LastStop})");
                    }
                    if (!slopeShot && climber.Feet.y > bottom.y + 1.2f)
                    {
                        slopeShot = true;
                        Screenshot("goat-slope-climbing");
                    }
                    NoteClearance(climber, ref slopeClear, ref slopeWorst, plank, ledgeBlock);
                    if (climber.Feet.y > bottom.y + 0.4f && climber.Feet.y < edge.y - 0.3f && climber.Doing != "Leaping")
                    {
                        onSlope++;
                        foreach (Vector3 part in climber.BodyPoints())
                        {
                            nearest = Mathf.Min(nearest, Vector3.Dot(part - bottom, slopeFace));
                        }
                        if (climber.Soles().Count(sole => Mathf.Abs(Vector3.Dot(sole - bottom, slopeFace)) < 0.15f) >= 2)
                        {
                            onFace++;
                        }
                    }
                    upSlope = Goats.Flat(climber.Feet, deckTop) < 0.8f && Mathf.Abs(climber.Feet.y - deckTop.y) < 0.5f;
                }
                Check("a goat walks up a slope of 75 degrees (steeper than a beetle crawls, 70) without leaping (17)",
                    upSlope && climber.Leaps == leapsBefore75 && climber.SteepestWalked >= 70f,
                    $"up {upSlope}, leaps {climber.Leaps - leapsBefore75}, the steepest it has walked {climber.SteepestWalked:0} degrees; it is {climber.Doing}, last stopped by {climber.LastStop}");
                Check("climbing it, the goat's body lies along the slope, its hooves on it and its head and body never in it (17)",
                    onSlope > 10 && nearest >= 0.1f && onFace >= onSlope * 0.8f,
                    $"on the face for {onSlope} frames: its body and head at least {nearest:0.00} m out from it; two or more hooves on it in {onFace} of them");
                Check("coming to the slope, the goat rears up onto it: its head and body never go into it, there or climbing it (17)",
                    slopeClear >= 0.05f,
                    $"its body, neck, head, brow and muzzle at least {slopeClear:0.00} m from the slope and the platform (the nearest: {slopeWorst})");
                Info("the goat on its way up the slope, every half second", string.Join("; ", trace));
            }
            Destroy(plank);
            Destroy(ledgeBlock);
            Physics.SyncTransforms();

            // Solid: the scout walks at a goat standing side-on 3 m in front of them for 3 s; it stops them.
            {
                Vector3 spot = GroundUnder(stage + way * 6f);
                climber.PlaceAt(spot, spot + across);
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, GroundUnder(stage + way * 3f) + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(1.5f);
                LookAtPoint(local, spot + Vector3.up * 0.7f);
                yield return new WaitForSeconds(0.3f);
                keys = new Harmony("UltimateAscent.SmokeTest.goatkeys");
                keys.Patch(AccessTools.Method(typeof(CharacterInput), "Sample"), postfix: new HarmonyMethod(typeof(Runner), nameof(ForceInput)));
                forcedMove = new Vector2(0f, 1f);
                forcedSprint = false;
                forcingInput = true;
                float closest = float.MaxValue;
                bool againstShot = false;
                for (float since = 0f; since < 3f; since += Time.deltaTime)
                {
                    yield return null;
                    Vector3 axis = climber.Feet + Vector3.up * 0.76f;
                    closest = Mathf.Min(closest, ToSegment(local.Center, axis - across * 0.34f, axis + across * 0.34f));
                    if (!againstShot && since > 2f)
                    {
                        againstShot = true;
                        Screenshot("goat-solid");
                    }
                }
                forcingInput = false;
                Check("a goat is solid: a scout walking into it stops against it, never through it (17)", closest >= 0.3f,
                    $"walking at it for 3 s, the scout came within {closest:0.00} m of the middle of its body (its body is 0.24 m round)");
            }

            // A goat at one end of the open ground grazing, the scout 12 m along it.
            Goat subject = climber;
            subject.Unaware = true;
            subject.PlaceAt(stage, stage + way);
            subject.GrazeNow(999f);
            Vector3 scoutSpot = GroundUnder(stage + way * 12f);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, scoutSpot + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
            LookAtPoint(local, subject.Feet + Vector3.up * 0.6f);
            int decisionsBefore = Goats.DecidedAt.Count;
            bool wound = false;
            Goats.NextRoll = 0.99f;
            subject.Unaware = false;
            subject.DecideSoon();
            float began = Time.time;
            float drifted = 0f;
            for (float since = 0f; since < 45f; since += Time.deltaTime)
            {
                if (Goats.NextRoll == null)
                {
                    // Told each time: not this time.
                    Goats.NextRoll = 0.99f;
                }
                yield return null;
                wound |= subject.Doing == "WindingUp" || subject.Doing == "Charging";
                float off = Goats.Flat(local.Center, scoutSpot);
                drifted = Mathf.Max(drifted, off);
                bool between = (since > 8f && since < 12f) || (since > 28f && since < 32f);
                if (between && off > 1f && !local.warping)
                {
                    // Pushed off their spot (the Alps' storms): back to it, between the goat's decisions.
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, scoutSpot + Vector3.up * 1.2f, false);
                }
                if (since < 1f && since + Time.deltaTime >= 1f)
                {
                    Screenshot("goat-grazing");
                }
            }
            Goats.NextRoll = null;
            List<float> times = Goats.DecidedAt.Skip(decisionsBefore).Select(t => t - began).ToList();
            List<float> gaps = times.Zip(times.Skip(1), (x, y) => y - x).ToList();
            bool seen = HelperFunctions.LineCheck(subject.Feet + Vector3.up * 1.1f, local.Center, HelperFunctions.LayerType.TerrainMap).transform == null;
            Check("with a scout 12 m off a goat decides every 20 s whether to charge; deciding not to, it grazes on (17)",
                times.Count == 3 && gaps.All(g => Mathf.Abs(g - Goats.DecideEvery) < 0.6f) && !wound && subject.Doing == "Grazing",
                $"decisions {string.Join(", ", times.Select(t => $"{t:0.0} s"))} after the scout came (every {string.Join(", ", gaps.Select(g => $"{g:0.0} s"))}); it wound up to charge: {wound}; it is {subject.Doing}; "
                + $"the scout moved up to {drifted:0.0} m off their spot, at the end {Vector3.Distance(subject.Feet, local.Center):0.0} m from it, in its sight {seen}, fully awake {local.data.fullyConscious}");

            // Told to charge: head down, a hoof pawing the ground for 1 s, then at the scout (9 m off), and the ram.
            Protect(false);
            ClearStatuses(afflictions);
            subject.Unaware = true;
            subject.PlaceAt(stage, stage + way);
            subject.GrazeNow(999f);
            scoutSpot = GroundUnder(stage + way * 9f);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, scoutSpot + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
            LookAtPoint(local, subject.Feet + Vector3.up * 0.6f);
            yield return new WaitForSeconds(0.3f);
            FallRecorder.Clear();
            FallRecorder.Of = local;
            float injuryBefore = afflictions.GetCurrentStatus(Status.Injury);
            int ramsBefore = Goats.RamsTaken, soundsBefore = Goats.RamSounds, chargesBefore = Goats.Charges;
            Goats.NextRoll = 0.05f;
            subject.Unaware = false;
            subject.DecideSoon();
            float decided = -1f, charging = -1f, topSpeed = 0f, headDown = 0f;
            int chargeBleatsBefore = Goats.ChargeBleats;
            bool windShot = false, runShot = false;
            Vector3 hitAt = local.Center;
            for (float until = Time.time + 8f; Time.time < until && Goats.RamsTaken == ramsBefore;)
            {
                hitAt = local.Center;
                yield return null;
                if (subject.Doing == "WindingUp")
                {
                    if (decided < 0f)
                    {
                        decided = Time.time;
                    }
                    headDown = Mathf.Max(headDown, subject.HeadDown);
                    if (!windShot && Time.time - decided > 0.6f)
                    {
                        windShot = true;
                        Screenshot("goat-windup");
                    }
                }
                if (subject.Doing == "Charging")
                {
                    if (charging < 0f)
                    {
                        charging = Time.time;
                    }
                    topSpeed = Mathf.Max(topSpeed, subject.Speed);
                    if (!runShot && Time.time - charging > 0.45f)
                    {
                        runShot = true;
                        Screenshot("goat-charge");
                    }
                }
            }
            bool rammed = Goats.RamsTaken > ramsBefore;
            Screenshot("goat-ram");
            yield return new WaitForSeconds(0.5f);
            Screenshot("goat-ram-after");
            yield return new WaitForSeconds(2f);
            float thrown = Goats.Flat(hitAt, local.Center);
            float injury = afflictions.GetCurrentStatus(Status.Injury) - injuryBefore;
            FallRecorder.Of = null;
            float bleatGap = decided > 0f ? subject.BleatedAt - decided : -1f;
            bool bleatedFirst = Goats.ChargeBleats > chargeBleatsBefore && decided > 0f && Mathf.Abs(bleatGap) < 0.2f;
            // A second ram (once the scout is up), to see the throw carries as far again.
            ClearStatuses(afflictions);
            for (float until = Time.time + 6f; Time.time < until && (local.data.fallSeconds > 0f || !local.data.fullyConscious);)
            {
                yield return null;
            }
            yield return new WaitForSeconds(1f);
            subject.Unaware = true;
            subject.PlaceAt(stage, stage + way);
            subject.GrazeNow(999f);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, GroundUnder(stage + way * 9f) + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
            LookAtPoint(local, subject.Feet + Vector3.up * 0.6f);
            int ramsSecond = Goats.RamsTaken;
            Goats.NextRoll = 0.05f;
            subject.Unaware = false;
            subject.DecideSoon();
            Vector3 secondAt = local.Center;
            for (float until = Time.time + 8f; Time.time < until && Goats.RamsTaken == ramsSecond;)
            {
                secondAt = local.Center;
                yield return null;
            }
            yield return new WaitForSeconds(2.5f);
            float thrownAgain = Goats.RamsTaken > ramsSecond ? Goats.Flat(secondAt, local.Center) : -1f;
            Check("deciding to charge, a goat bleats, lowers its head and paws the ground for 1 s, then charges at about 7 m/s (17)",
                decided > 0f && charging > 0f && Mathf.Abs(charging - decided - Goats.WindUpSeconds) < 0.2f && headDown > 65f && topSpeed > Goats.ChargeSpeed * 0.8f && Goats.Charges > chargesBefore && bleatedFirst,
                $"bleated {bleatGap:0.00} s after deciding; wound up for {(charging > 0f && decided > 0f ? charging - decided : -1f):0.00} s, its head down to {headDown:0} degrees below the level (about 40 at rest); top speed {topSpeed:0.0} m/s");
            Check("its ram knocks the scout down for 2 s with 20 injury, its blow heard (the beetles' hit, heavier), and throws them 6-8 m (17)",
                rammed && FallRecorder.Seconds.Any(s => Mathf.Abs(s - Goats.RamKnockDown) < 0.01f) && Mathf.Abs(injury - Goats.RamInjury) < 0.013f && Goats.RamSounds > soundsBefore
                && thrown >= 6f && thrown <= 8f && thrownAgain >= 6f && thrownAgain <= 8f,
                $"rammed {rammed}; knock-downs {string.Join(", ", FallRecorder.Seconds.Select(s => $"{s:0.0} s"))}; +{injury * 100f:0.#} injury; blows heard {Goats.RamSounds - soundsBefore}; thrown {thrown:0.0} m, again {thrownAgain:0.0} m (the goat {subject.Doing}, last stopped by {subject.LastStop})");
            ClearStatuses(afflictions);
            for (float until = Time.time + 6f; Time.time < until && (local.data.fallSeconds > 0f || !local.data.fullyConscious);)
            {
                yield return null;
            }
            yield return new WaitForSeconds(1f);

            // A scout who steps aside (sprints sideways) as the goat comes 5.5 m off (it runs straight on from 5 m): it runs past,
            // skids, shakes its head, grazes again.
            subject.Unaware = true;
            subject.PlaceAt(stage, stage + way);
            subject.GrazeNow(999f);
            scoutSpot = GroundUnder(stage + way * 10f);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, scoutSpot + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
            LookAtPoint(local, subject.Feet + Vector3.up * 0.6f);
            yield return new WaitForSeconds(0.3f);
            ramsBefore = Goats.RamsTaken;
            int missesBefore = Goats.Misses;
            Goats.NextRoll = 0.05f;
            subject.Unaware = false;
            subject.DecideSoon();
            bool dodged = false;
            Vector3 passedAt = Vector3.zero;
            float skidded = 0f;
            for (float until = Time.time + 8f; Time.time < until;)
            {
                yield return null;
                if (!dodged && subject.Doing == "Charging" && Vector3.Distance(subject.Nose, local.Center) < Goats.CommitDistance + 0.5f)
                {
                    dodged = true;
                    local.data.currentStamina = 1f;
                    forcedMove = new Vector2(1f, 0f);
                    forcedSprint = true;
                    forcingInput = true;
                }
                if (subject.Doing == "Recovering")
                {
                    if (passedAt == Vector3.zero)
                    {
                        passedAt = subject.Feet;
                        Screenshot("goat-miss");
                    }
                    skidded = Mathf.Max(skidded, Goats.Flat(passedAt, subject.Feet));
                    forcingInput = false;
                }
                if (passedAt != Vector3.zero && subject.Doing == "Grazing")
                {
                    break;
                }
            }
            forcingInput = false;
            Check("a scout who steps aside as the goat comes (it runs straight on in its last 5 m) makes it miss: it skids on, stops, then grazes again (17)",
                dodged && Goats.RamsTaken == ramsBefore && Goats.Misses > missesBefore && subject.LastStop == "ran past" && skidded >= 0.5f && subject.Doing == "Grazing",
                $"stepped aside {dodged}; rams {Goats.RamsTaken - ramsBefore}, misses {Goats.Misses - missesBefore}; it stopped for: {subject.LastStop}; skidded {skidded:0.0} m; it is {subject.Doing}");
        }
        finally
        {
            if (ledgeBlock != null)
            {
                Destroy(ledgeBlock);
            }
            if (plank != null)
            {
                Destroy(plank);
            }
            forcingInput = false;
            keys?.UnpatchSelf();
            Goats.NextRoll = null;
            FallRecorder.Of = null;
            foreach (Goat goat in Goats.All.Where(g => g != null))
            {
                goat.Unaware = false;
                goat.Puppet = false;
            }
            calm();
            Protect(false);
        }
    }

    /// <summary>
    /// The goats' roaming (17, on a map with the Alps): a goat 100 m from its spot still picks where to go next round
    /// itself (its range is 150 m), and one 200 m off heads back towards its spot; on top of a real cliff of the Alps it
    /// picks ground below the cliff to go to as well as ground above it, and sent to the cliff's foot it comes down; and it
    /// leaps forward across a gap 6.5 m wide between two test pillars.
    /// </summary>
    private IEnumerator GoatCliffChecks()
    {
        Character local = Local;
        if (Map.CurrentPlace != Place.Alpine)
        {
            MapHandler.JumpToSegment(Segment.Alpine);
            yield return new WaitForSeconds(8f);
        }
        if (Map.CurrentPlace != Place.Alpine)
        {
            Info("the goats' roaming checks are skipped", $"no Alps here ({Map.CurrentPlace}); run with -Biome and a map number that has the Alps (80)");
            yield break;
        }
        for (float until = Time.time + 10f; Time.time < until && Goats.All.Count == 0;)
        {
            yield return null;
        }
        List<Goat> goats = Goats.All.Where(g => g != null).ToList();
        Goat goat = goats.Where(g => !g.Follower).OrderBy(g => Vector3.Distance(g.Feet, local.Center)).FirstOrDefault();
        if (goat == null)
        {
            Fail("a goat for the roaming checks", $"{goats.Count} goats, none leading its own way");
            yield break;
        }
        Protect(true);
        Action calm = CalmForMeasuring();
        try
        {
            foreach (Goat other in goats)
            {
                other.Unaware = true;
            }
            goat.Puppet = true;

            // Its range: picks round itself 100 m from its spot; drawn back within 150 m of it from 200 m off.
            {
                Vector3 spot = goat.Feet;
                goat.PlaceAt(spot, spot + Vector3.forward);
                Vector3 away = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
                goat.Home = spot + away * 100f;
                List<float> nearFar = new List<float>();
                for (int i = 0; i < 60; i++)
                {
                    if (goat.TryPickRoam(out Vector3 to))
                    {
                        nearFar.Add(Goats.Flat(to, spot));
                    }
                }
                goat.Home = spot + away * 200f;
                List<float> nearer = new List<float>();
                for (int i = 0; i < 60; i++)
                {
                    if (goat.TryPickRoam(out Vector3 to))
                    {
                        nearer.Add(200f - Goats.Flat(to, goat.Home));
                    }
                }
                goat.Home = spot;
                Check("a goat roams up to 150 m from its spot: 100 m off it still picks where to go round itself, 200 m off it heads back towards its spot (17)",
                    nearFar.Count >= 20 && nearFar.Max() <= Goats.RoamStepMax + 1f && nearer.Count >= 20 && nearer.Min() > 0f,
                    $"100 m off: {nearFar.Count} picks, {(nearFar.Count > 0 ? nearFar.Min() : 0f):0}-{(nearFar.Count > 0 ? nearFar.Max() : 0f):0} m from it; "
                    + $"200 m off: {nearer.Count} picks, each {(nearer.Count > 0 ? nearer.Min() : 0f):0.0}-{(nearer.Count > 0 ? nearer.Max() : 0f):0.0} m nearer its spot");
            }

            // A gap: two pillars 8 m tall (test blocks), 6.5 m apart, wider than the old 4.5 m leap, the ground between
            // them too far down to leap to. A goat on one, sent to the other, leaps across.
            if (GoatStage(local.Center, out Vector3 stage, out Vector3 way))
            {
                const float gap = 6.5f;
                Vector3 nearSpot = stage + way * 2f;
                Vector3 farSpot = stage + way * (5f + gap);
                float topY = Mathf.Max(GroundUnder(nearSpot).y, GroundUnder(farSpot).y) + 8f;
                List<GameObject> pillars = new List<GameObject>();
                foreach (Vector3 spot in new[] { nearSpot, farSpot })
                {
                    GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pillar.name = "UltimateAscentTestPillar";
                    pillar.layer = LayerMask.NameToLayer("Map");
                    pillar.transform.SetPositionAndRotation(new Vector3(spot.x, topY - 6f, spot.z), Quaternion.LookRotation(way));
                    pillar.transform.localScale = new Vector3(3f, 12f, 3f);
                    Grey(pillar);
                    pillars.Add(pillar);
                }
                Physics.SyncTransforms();
                Vector3 from = new Vector3(nearSpot.x, topY, nearSpot.z);
                Vector3 to = new Vector3(farSpot.x, topY, farSpot.z);
                try
                {
                    goat.PlaceAt(from, to);
                    Vector3 middle = (from + to) * 0.5f;
                    Vector3? see = StandNear(middle - Vector3.up * 6f, 8f, 14f, seeIt: true) ?? StandNear(middle - Vector3.up * 6f, 8f, 18f, seeIt: true, firm: false);
                    if (see.HasValue)
                    {
                        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, see.Value + Vector3.up * 1.2f, false);
                    }
                    yield return new WaitForSeconds(1.5f);
                    LookAtPoint(local, middle);
                    yield return new WaitForSeconds(0.4f);
                    Screenshot("goat-gap-before");
                    int leapsBefore = goat.Leaps;
                    float longestBefore = goat.LongestLeap;
                    goat.RoamTo(to);
                    bool across = false, shot = false;
                    List<string> trace = new List<string>();
                    float nextTrace = 0f;
                    for (float until = Time.time + 15f; Time.time < until && !across;)
                    {
                        yield return null;
                        if (Time.time >= nextTrace)
                        {
                            nextTrace = Time.time + 0.5f;
                            trace.Add($"{goat.Doing} {goat.Feet:F1} ({goat.LastStop})");
                        }
                        if (!shot && goat.Doing == "Leaping" && goat.Feet.y > topY + 0.3f)
                        {
                            shot = true;
                            Screenshot("goat-gap-leap");
                        }
                        if (goat.Doing == "Standing" && Time.time > until - 14f && Goats.Flat(goat.Feet, to) > 1f)
                        {
                            goat.RoamTo(to);
                        }
                        across = Goats.Flat(goat.Feet, to) < 1.6f && Mathf.Abs(goat.Feet.y - topY) < 0.6f && goat.Doing != "Leaping";
                    }
                    Check("a goat leaps forward across a gap 6.5 m wide (it leaps up to 8 m forward) (17)",
                        across && goat.Leaps > leapsBefore && goat.LongestLeap >= gap,
                        $"across {across} with {goat.Leaps - leapsBefore} leaps, the longest {goat.LongestLeap:0.0} m (before {longestBefore:0.0}); it is {goat.Doing}, last stopped by {goat.LastStop}");
                    if (!across)
                    {
                        Info("the goat at the gap, every half second", string.Join("; ", trace) + "; " + goat.LeapReport);
                    }
                    yield return new WaitForSeconds(0.4f);
                    Screenshot("goat-gap-after");
                }
                finally
                {
                    foreach (GameObject pillar in pillars)
                    {
                        Destroy(pillar);
                    }
                    Physics.SyncTransforms();
                }
            }
            else
            {
                Info("the gap check is skipped", "no open, flat ground near the scout for the test pillars");
            }

            // Real cliffs of the Alps near the goats: a goat put on top of one, then sent to its foot. Cliffs differ (some
            // too sheer, with no ledge on the way down); up to six are tried until one is come down.
            List<(Vector3 top, Vector3 bottom)> cliffs = Cliffs(goats.Select(g => g.Home).Prepend(goat.Feet).ToList(), 6);
            Info("cliffs found near the goats", string.Join("; ", cliffs.Select(c => $"{c.top:F0} over {c.bottom:F0} ({c.top.y - c.bottom.y:0.0} m drop, {Goats.Flat(c.top, c.bottom):0.0} m out)")));
            bool down = false, picked = false;
            int tried = 0, fellOff = 0, backedUp = 0;
            List<string> how = new List<string>();
            foreach ((Vector3 top, Vector3 bottom) in cliffs)
            {
                tried++;
                goat.PlaceAt(top, bottom);
                Vector3 middle = (top + bottom) * 0.5f;
                Vector3? see = StandNear(middle, 7f, 14f, seeIt: true) ?? StandNear(middle, 7f, 18f, seeIt: true, firm: false);
                if (see.HasValue)
                {
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, see.Value + Vector3.up * 1.2f, false);
                }
                yield return new WaitForSeconds(1.5f);
                LookAtPoint(local, top + Vector3.up * 0.6f);
                yield return new WaitForSeconds(0.4f);
                if (!picked)
                {
                    // On top: where it would roam to next, ground below the cliff among it.
                    picked = true;
                    Screenshot("goat-cliff-top");
                    int picks = 0, below = 0, above = 0;
                    for (int i = 0; i < 100; i++)
                    {
                        if (goat.TryPickRoam(out Vector3 to))
                        {
                            picks++;
                            below += to.y < goat.Feet.y - 4f ? 1 : 0;
                            above += to.y > goat.Feet.y + 4f ? 1 : 0;
                        }
                    }
                    Check("on top of a cliff a goat picks ground below it to roam to, as well as ground above (17)", picks >= 30 && below >= picks / 10,
                        $"{picks} picks of 100: {below} more than 4 m lower, {above} more than 4 m higher, the rest about level");
                }

                int leapsBefore = goat.Leaps;
                int fallsBefore = goat.Falls;
                int backsBefore = goat.BackedUp;
                bool fell = false;
                down = false;
                goat.RoamTo(bottom);
                List<string> downTrace = new List<string>();
                int downShots = 0;
                float nextTrace = 0f;
                for (float until = Time.time + 30f; Time.time < until && !down;)
                {
                    yield return null;
                    if (Time.time >= nextTrace)
                    {
                        nextTrace = Time.time + 0.5f;
                        downTrace.Add($"{goat.Doing} {goat.Feet:F1} ({goat.LastStop})");
                        LookAtPoint(local, goat.Feet + Vector3.up * 0.6f);
                    }
                    if (downShots < 2 && goat.Feet.y < top.y - (downShots + 1) * (top.y - bottom.y) / 3f)
                    {
                        Screenshot($"goat-cliff-down-{++downShots}");
                    }
                    if (goat.Doing == "Standing" && Time.time > until - 29f && Goats.Flat(goat.Feet, bottom) > 1f)
                    {
                        goat.RoamTo(bottom);
                    }
                    fell |= goat.Doing == "Falling" || goat.Falls > fallsBefore;
                    down = Goats.Flat(goat.Feet, bottom) < 1f && Mathf.Abs(goat.Feet.y - bottom.y) < 1f && goat.Doing != "Leaping";
                }
                fellOff += fell ? 1 : 0;
                backedUp += goat.BackedUp - backsBefore;
                how.Add($"cliff {tried} ({top.y - bottom.y:0.0} m drop): down {down} with {goat.Leaps - leapsBefore} leaps{(fell ? ", slipping and falling part of the way" : "")}, "
                    + $"back up to firm ground {goat.BackedUp - backsBefore} times, the steepest it walked {goat.SteepestWalked:0} degrees, {goat.Doing}, last stopped by {goat.LastStop}");
                if (down && !fell)
                {
                    Info($"the goat on its way down cliff {tried}, every half second", string.Join("; ", downTrace));
                    yield return new WaitForSeconds(0.5f);
                    Screenshot("goat-cliff-bottom");
                    break;
                }
                Info($"cliff {tried}: the goat did not come down it", string.Join("; ", downTrace.TakeLast(12)) + "; " + goat.LeapReport);
            }
            Check("a goat never slips off a cliff by itself: part way down a face it cannot get down, it goes back up to firm ground (17)", tried > 0 && fellOff == 0,
                $"{tried} cliffs tried, falls on {fellOff} of them, back up to firm ground {backedUp} times");
            Check("a goat on top of a cliff of the Alps comes down it, walking and leaping, without falling (17)", down && fellOff == 0,
                $"{tried} of {cliffs.Count} cliffs tried: " + string.Join("; ", how));
        }
        finally
        {
            foreach (Goat other in Goats.All.Where(g => g != null))
            {
                other.Unaware = false;
                other.Puppet = false;
            }
            calm();
            Protect(false);
        }
    }

    /// <summary>
    /// Real cliffs of the Alps near some points: a top it can rest on with ground it can rest on 6-20 m below it, 2-5 m
    /// out (as steep as two in one at least), both the Alps', room for a goat on both, neither under the map; at most so
    /// many, apart.
    /// </summary>
    private static List<(Vector3 top, Vector3 bottom)> Cliffs(List<Vector3> near, int most)
    {
        List<(Vector3 top, Vector3 bottom)> cliffs = new List<(Vector3, Vector3)>();
        float rest = Mathf.Cos(Goats.RestSlope * Mathf.Deg2Rad);
        bool Alps(Collider ground) => Map.TryGetBiome(ground, out Biome.BiomeType biome) && biome == Biome.BiomeType.Alpine;
        for (int attempt = 0; attempt < 3000 && cliffs.Count < most; attempt++)
        {
            Vector3 centre = near[attempt % near.Count];
            Vector2 r = UnityEngine.Random.insideUnitCircle * 60f;
            Vector3 probe = new Vector3(centre.x + r.x, centre.y + 60f, centre.z + r.y);
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit top, 120f, HelperFunctions.terrainMapMask) || top.normal.y < rest || !Alps(top.collider) || !Goat.RoomAt(top.point)
                || cliffs.Any(c => Goats.Flat(c.top, top.point) < 15f) || NewMobs.UnderTheMap(top.point))
            {
                continue;
            }
            Vector3 out0 = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
            for (int turn = 0; turn < 8; turn++)
            {
                Vector3 outward = Quaternion.Euler(0f, turn * 45f, 0f) * out0;
                bool found = false;
                for (float d = 2f; d <= 5f; d += 1f)
                {
                    if (!Physics.Raycast(top.point + Vector3.up + outward * d, Vector3.down, out RaycastHit foot, 25f, HelperFunctions.terrainMapMask))
                    {
                        continue;
                    }
                    float drop = top.point.y - foot.point.y;
                    if (drop >= 6f && drop <= 20f && drop >= d * 2f && foot.normal.y >= rest && Alps(foot.collider) && Goat.RoomAt(foot.point) && !NewMobs.UnderTheMap(foot.point))
                    {
                        cliffs.Add((top.point, foot.point));
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    break;
                }
            }
        }
        return cliffs;
    }

    /// <summary>
    /// Goats keep to the Alps and snakes to the Tropics (17, on a map with both, as 80): where the two biomes' ground
    /// meets (looked for round the goats nearest the Tropics), a goat told to roam to a spot just over the edge (on the
    /// Tropics' ground) stops where the Alps' ground ends, and a ground snake told to wander to a spot just over it the
    /// other way (on the Alps' ground) stops where the Tropics' ground ends.
    /// </summary>
    private IEnumerator BorderChecks()
    {
        Character local = Local;
        // The snakes come out once the Tropics are reached (and the goats the Alps): there first, if they have not yet.
        if (Snakes.All.Count == 0)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(8f);
        }
        if (Map.CurrentPlace != Place.Alpine)
        {
            MapHandler.JumpToSegment(Segment.Alpine);
            yield return new WaitForSeconds(8f);
        }
        MapHandler map = Singleton<MapHandler>.Instance;
        if (Map.CurrentPlace != Place.Alpine || map.segments[(int)Segment.Tropics].biome != Biome.BiomeType.Tropics)
        {
            Info("the border checks are skipped", $"they need the Tropics and the Alps (here {Map.CurrentPlace}); run with -Biome 80");
            yield break;
        }
        for (float until = Time.time + 10f; Time.time < until && (Goats.All.Count == 0 || Snakes.All.Count == 0);)
        {
            yield return null;
        }
        // Where the two meet: round the campfire between them, then round each goat's spot, the nearest to it first.
        Vector3 campfire = map.segments[(int)Segment.Tropics].segmentCampfire.transform.position;
        List<Vector3> centres = new List<Vector3> { campfire };
        centres.AddRange(Goats.All.Where(g => g != null).Select(g => g.Home).OrderBy(h => Goats.Flat(h, campfire)));
        Vector3 border = campfire, inAlps = campfire, inTropics = campfire;
        BorderSearch search = new BorderSearch();
        int looked = 0;
        bool found = false;
        foreach (Vector3 centre in centres)
        {
            looked++;
            if (FindBorder(centre, search, out border, out inAlps, out inTropics))
            {
                found = true;
                Info("the ground within 70 m of where the edge was found", BiomeShares(centre, 70f));
                break;
            }
        }
        Info("the edges looked at", search.ToString());
        if (!found)
        {
            Info("the ground within 70 m of the campfire between the Tropics and the Alps", BiomeShares(campfire, 70f));
            Fail("a walkable edge between the Tropics' ground and the Alps'", $"none within 70 m of the campfire or any goat's spot ({looked} places looked round)");
            yield break;
        }
        Vector3 intoAlps = Vector3.ProjectOnPlane(inAlps - inTropics, Vector3.up).normalized;
        Info("the edge under test", $"at {border:F1}, {Goats.Flat(border, campfire):0} m from the campfire (looked round {looked} places); the Alps that way: {intoAlps:F2}");
        Goat goat = Goats.All.Where(g => g != null).OrderBy(g => Vector3.Distance(g.Feet, border)).FirstOrDefault();
        Snake snake = Snakes.All.Where(s => s != null && !s.IsTree).OrderBy(s => Vector3.Distance(s.Head, border)).FirstOrDefault();
        Protect(true);
        Action calm = CalmForMeasuring();
        try
        {
            foreach (Goat other in Goats.All.Where(g => g != null))
            {
                other.Unaware = true;
            }
            foreach (Snake other in Snakes.All.Where(s => s != null))
            {
                other.Ignore();
            }
            Vector3 across = Vector3.Cross(Vector3.up, intoAlps);
            Vector3? watch = StandNear(border, 5f, 9f, seeIt: true) ?? StandNear(border, 5f, 12f, seeIt: true, firm: false);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, (watch ?? GroundUnder(border + across * 6f)) + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);

            // A goat 2.25 m into the Alps, told to roam to a spot 2.25 m over their edge.
            if (goat == null)
            {
                Fail("a goat for the border check", "no goats");
            }
            else
            {
                goat.Puppet = true;
                Vector3 from = inAlps;
                Vector3 to = inTropics;
                goat.PlaceAt(from, to);
                yield return new WaitForSeconds(0.8f);
                LookAtPoint(local, border + Vector3.up * 0.5f);
                goat.RoamTo(to);
                int frames = 0, outside = 0;
                float furthest = float.MinValue;
                for (float until = Time.time + 10f; Time.time < until;)
                {
                    yield return null;
                    frames++;
                    furthest = Mathf.Max(furthest, Vector3.Dot(goat.Feet - border, -intoAlps));
                    if (Map.TryGetGroundBiome(goat.Feet + Vector3.up * 0.3f, out Biome.BiomeType under) && under != Biome.BiomeType.Alpine)
                    {
                        outside++;
                    }
                }
                Screenshot("border-goat");
                Check("a goat keeps to the Alps: told to roam to a spot just over their edge, it stops where the Alps' ground ends (17)",
                    frames > 0 && outside == 0 && furthest > -1.25f,
                    $"from 2,25 m inside the edge its feet came to {furthest:0.00} m past it (below 0: still short of it); on another biome's ground in {outside} of {frames} frames; "
                    + $"it is {goat.Doing}, last stopped by {goat.LastStop}");
                goat.Puppet = false;
            }

            // A ground snake 2.25 m into the Tropics, told to wander to a spot 2.25 m over their edge.
            if (snake == null)
            {
                Fail("a ground snake for the border check", "no ground snakes");
            }
            else
            {
                snake.Puppet = false;
                Vector3 from = inTropics;
                Vector3 to = inAlps;
                snake.PlaceAt(from, to);
                yield return new WaitForSeconds(0.5f);
                LookAtPoint(local, border + Vector3.up * 0.2f);
                snake.WanderTowards(to);
                int frames = 0, outside = 0;
                float furthest = float.MinValue;
                for (float until = Time.time + 10f; Time.time < until;)
                {
                    yield return null;
                    frames++;
                    furthest = Mathf.Max(furthest, Vector3.Dot(snake.Head - border, intoAlps));
                    if (Map.TryGetGroundBiome(snake.Head + Vector3.up * 0.2f, out Biome.BiomeType under) && under != Biome.BiomeType.Tropics)
                    {
                        outside++;
                    }
                }
                Screenshot("border-snake");
                Check("a snake keeps to the Tropics: told to wander to a spot just over their edge, it stops where the Tropics' ground ends (17)",
                    frames > 0 && outside == 0 && furthest > -1.25f,
                    $"from 2,25 m inside the edge its head came to {furthest:0.00} m past it (below 0: still short of it); on another biome's ground in {outside} of {frames} frames; it is {snake.Doing}");
            }
        }
        finally
        {
            foreach (Goat other in Goats.All.Where(g => g != null))
            {
                other.Unaware = false;
                other.Puppet = false;
            }
            foreach (Snake other in Snakes.All.Where(s => s != null))
            {
                other.Unaware = false;
            }
            calm();
            Protect(false);
        }
    }

    /// <summary>Whose the ground is round a point, every 5 m out to a distance: each biome's share, and ground of none.</summary>
    private static string BiomeShares(Vector3 centre, float reach)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        int total = 0;
        for (float x = -reach; x <= reach; x += 5f)
        {
            for (float z = -reach; z <= reach; z += 5f)
            {
                if (x * x + z * z > reach * reach
                    || !Physics.Raycast(centre + new Vector3(x, 60f, z), Vector3.down, out RaycastHit hit, 160f, HelperFunctions.terrainMapMask))
                {
                    continue;
                }
                string whose = Map.TryGetBiome(hit.collider, out Biome.BiomeType biome) ? biome.ToString() : "no biome";
                counts[whose] = counts.TryGetValue(whose, out int n) ? n + 1 : 1;
                total++;
            }
        }
        return total == 0 ? "no ground found" : string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value * 100 / total}%")) + $" (of {total} points)";
    }

    /// <summary>For the report: the places looked at where the Tropics' ground meets the Alps', and why those that did not do did not.</summary>
    private sealed class BorderSearch
    {
        internal int seen, steep, uneven, mixed, blocked;

        public override string ToString() => $"{seen} places where the ground changes from the one to the other; of them too steep {steep}, "
            + $"uneven {uneven}, other ground beside them {mixed}, something in the way {blocked}";
    }

    /// <summary>
    /// Where the Tropics' ground meets the Alps' near a point: along lines out from it (every 10 degrees, up to 70 m), the
    /// nearest change from the one to the other with 3 m of ground of one biome on each side that a goat walks (no steeper
    /// than 60 degrees, no step of more than half a metre) and nothing in the way across it; and the ground 2.25 m into each.
    /// </summary>
    private static bool FindBorder(Vector3 near, BorderSearch search, out Vector3 border, out Vector3 inAlps, out Vector3 inTropics)
    {
        const float spacing = 0.5f;
        const int side = 6;
        int tropics = (int)Biome.BiomeType.Tropics, alps = (int)Biome.BiomeType.Alpine;
        border = inAlps = inTropics = near;
        float best = float.MaxValue;
        for (int angle = 0; angle < 360; angle += 10)
        {
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            List<(Vector3 at, int biome, bool walkable)> line = new List<(Vector3 at, int biome, bool walkable)>();
            for (float r = 0f; r <= 70f; r += spacing)
            {
                Vector3 p = near + dir * r;
                if (Physics.Raycast(new Vector3(p.x, near.y + 60f, p.z), Vector3.down, out RaycastHit hit, 160f, HelperFunctions.terrainMapMask))
                {
                    line.Add((hit.point, Map.TryGetBiome(hit.collider, out Biome.BiomeType biome) ? (int)biome : -1, hit.normal.y >= 0.5f));
                }
                else
                {
                    line.Add((p, -2, false));
                }
            }
            for (int i = side; i + side < line.Count; i++)
            {
                int before = line[i - 1].biome, after = line[i].biome;
                if (!(before == tropics && after == alps) && !(before == alps && after == tropics))
                {
                    continue;
                }
                search.seen++;
                int fault = 0;
                for (int k = i - side; k < i + side && fault == 0; k++)
                {
                    if (line[k].biome != (k < i ? before : after))
                    {
                        fault = 1;
                        search.mixed++;
                    }
                    else if (!line[k].walkable)
                    {
                        fault = 2;
                        search.steep++;
                    }
                    else if (k + 1 < i + side && Mathf.Abs(line[k + 1].at.y - line[k].at.y) > 0.5f)
                    {
                        fault = 3;
                        search.uneven++;
                    }
                }
                if (fault == 0 && Physics.Linecast(line[i - side].at + Vector3.up * 0.8f, line[i + side - 1].at + Vector3.up * 0.8f, HelperFunctions.terrainMapMask))
                {
                    fault = 4;
                    search.blocked++;
                }
                if (fault != 0)
                {
                    continue;
                }
                float distance = Vector3.Distance(near, line[i].at);
                if (distance < best)
                {
                    best = distance;
                    border = (line[i - 1].at + line[i].at) * 0.5f;
                    inAlps = after == alps ? line[i + 4].at : line[i - 5].at;
                    inTropics = after == alps ? line[i - 5].at : line[i + 4].at;
                }
                break;
            }
        }
        return best < float.MaxValue;
    }

    /// <summary>
    /// A close look at a goat (only when asked): its model from the side, the front, three-quarters and behind, 3.2 m off on
    /// open ground (the scout's own view); grazing; walking past, a leap up a ledge and its charge and ram, slowed down,
    /// seen by a camera off to the side; the charge also as the scout charged at sees it.
    /// </summary>
    private IEnumerator GoatPreview()
    {
        Character local = Local;
        if (Map.CurrentPlace != Place.Alpine)
        {
            MapHandler.JumpToSegment(Segment.Alpine);
            yield return new WaitForSeconds(8f);
        }
        for (float until = Time.time + 10f; Time.time < until && Goats.All.Count == 0;)
        {
            yield return null;
        }
        Goat goat = Goats.All.Where(g => g != null).OrderBy(g => Vector3.Distance(g.Feet, local.Center)).FirstOrDefault();
        if (goat == null || !GoatStage(local.Center, out Vector3 stage, out Vector3 way))
        {
            Info("the goat preview is skipped", goat == null ? "no goats here (run with -Biome 80)" : "no open, flat ground for it");
            yield break;
        }
        float timeScale = Time.timeScale;
        Protect(true);
        Action calm = CalmForMeasuring();
        GameObject side = null;
        try
        {
            foreach (Goat other in Goats.All.Where(g => g != null))
            {
                other.Unaware = true;
                other.Puppet = true;
            }
            Vector3 right = Vector3.Cross(Vector3.up, way);
            // The scout at the start of the open ground, the goat 3.2 m along it, turned to show each side.
            Vector3 eye = GroundUnder(stage + way * 0.5f);
            Vector3 spot = GroundUnder(stage + way * 3.7f);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, eye + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(2f);
            foreach ((string view, Vector3 facing) in new[]
            {
                ("side", right),
                ("front", -way),
                ("three-quarters", (right - way).normalized),
                ("back", way),
            })
            {
                goat.PlaceAt(spot, spot + facing);
                yield return new WaitForSeconds(0.8f);
                LookAtPoint(local, spot + Vector3.up * 0.55f);
                yield return new WaitForSeconds(0.5f);
                Screenshot($"goat-model-{view}");
                yield return new WaitForSeconds(0.8f);
            }
            goat.PlaceAt(spot, spot + right);
            goat.GrazeNow(999f);
            yield return new WaitForSeconds(1.2f);
            LookAtPoint(local, spot + Vector3.up * 0.4f);
            yield return new WaitForSeconds(0.3f);
            Screenshot("goat-graze-1");
            yield return new WaitForSeconds(0.2f);
            Screenshot("goat-graze-2");
            yield return new WaitForSeconds(0.8f);

            // Walking along the open ground, seen from 3.5 m off its side at a quarter of the speed, a picture every twentieth
            // of a second of its walk (a little over a stride).
            side = ViewCamera(GroundUnder(stage + way * 6f + right * 3.5f) + Vector3.up * 0.9f, GroundUnder(stage + way * 6f) + Vector3.up * 0.45f);
            goat.PlaceAt(GroundUnder(stage + way * 2.5f), stage + way * 12f);
            yield return new WaitForSeconds(0.5f);
            goat.RoamTo(GroundUnder(stage + way * 11f));
            for (float until = Time.time + 6f; Time.time < until && Vector3.Dot(goat.Feet - stage, way) < 5.4f;)
            {
                yield return null;
            }
            Time.timeScale = 0.25f;
            for (int frame = 1; frame <= 12; frame++)
            {
                RenderView(side, $"goat-walk-{frame}");
                yield return new WaitForSeconds(0.05f);
            }
            Time.timeScale = timeScale;

            // A leap onto a sheer 2.2 m step (a test block) at a quarter of the speed, seen from the side of the leap.
            Vector3 face = GroundUnder(stage + way * 5f);
            Vector3 foot = GroundUnder(stage + way * 3f);
            ledgeBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ledgeBlock.name = "UltimateAscentTestLedge";
            ledgeBlock.layer = LayerMask.NameToLayer("Map");
            ledgeBlock.transform.SetPositionAndRotation(face + way * 1.25f + Vector3.up * 0.95f, Quaternion.LookRotation(way));
            ledgeBlock.transform.localScale = new Vector3(3f, 2.5f, 2.5f);
            Grey(ledgeBlock);
            Physics.SyncTransforms();
            Vector3 top = GroundUnder(face + way * 1.25f + Vector3.up * 3f);
            {
                Vector3 mid = (foot + top) * 0.5f;
                Vector3 across = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(top - foot, Vector3.up).normalized);
                Vector3 camAt = mid + across * 4.5f + Vector3.up * 0.8f;
                if (Physics.Linecast(mid + Vector3.up * 0.6f, camAt, HelperFunctions.terrainMapMask))
                {
                    camAt = mid - across * 4.5f + Vector3.up * 0.8f;
                }
                Destroy(side);
                side = ViewCamera(camAt, mid + Vector3.up * 0.6f);
                goat.PlaceAt(foot, top);
                yield return new WaitForSeconds(1f);
                Info("goat preview", $"a leap onto a {top.y - foot.y:0.0} m step (a test block), at a quarter of the speed from the moment it crouches to leap, seen from its side");
                goat.RoamTo(top);
                RenderView(side, "goat-leap-0");
                for (float until = Time.time + 8f; Time.time < until && goat.Doing != "Leaping";)
                {
                    yield return null;
                }
                Time.timeScale = 0.25f;
                for (int frame = 1; frame <= 12; frame++)
                {
                    RenderView(side, $"goat-leap-{frame}");
                    yield return new WaitForSeconds(0.06f);
                }
                Time.timeScale = timeScale;
                yield return new WaitForSeconds(1f);
                Info("goat preview", $"the leap: {(goat.Leaps > 0 ? "leapt" : "no leap")}; {goat.LeapReport}");
                RenderView(side, "goat-leap-top");
            }
            Destroy(ledgeBlock);
            Physics.SyncTransforms();

            // Up a slope of 75 degrees (a test plank to a platform 2.6 m high), seen from beside it with the scout's own eyes:
            // its body along the face, its hooves on it, its head up along it.
            {
                Vector3 bottom = GroundUnder(stage + way * 4.9f);
                Vector3 edge = GroundUnder(stage + way * 5.6f);
                edge.y = bottom.y + 2.6f;
                Vector3 deck = GroundUnder(stage + way * 6.6f);
                ledgeBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ledgeBlock.name = "UltimateAscentTestPlatform";
                ledgeBlock.layer = LayerMask.NameToLayer("Map");
                ledgeBlock.transform.SetPositionAndRotation(new Vector3(deck.x, bottom.y + 1.15f, deck.z), Quaternion.LookRotation(way));
                ledgeBlock.transform.localScale = new Vector3(2.5f, 2.9f, 2f);
                plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plank.name = "UltimateAscentTestSlope";
                plank.layer = LayerMask.NameToLayer("Map");
                Vector3 slope = (edge - bottom).normalized;
                Vector3 slopeFace = Vector3.Cross(right, slope);
                if (slopeFace.y < 0f)
                {
                    slopeFace = -slopeFace;
                }
                plank.transform.SetPositionAndRotation((bottom + edge) * 0.5f - slope * 0.2f - slopeFace * 0.15f, Quaternion.LookRotation(slope, slopeFace));
                plank.transform.localScale = new Vector3(2.5f, 0.3f, Vector3.Distance(bottom, edge) + 0.4f);
                Grey(ledgeBlock);
                Grey(plank);
                Physics.SyncTransforms();
                Vector3 deckTop = GroundUnder(new Vector3(deck.x, bottom.y + 4f, deck.z));
                goat.PlaceAt(GroundUnder(stage + way * 3.2f), edge);
                Vector3 beside = GroundUnder(stage + way * 4.6f + right * 4.5f);
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, beside + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(1.5f);
                LookAtPoint(local, (bottom + edge) * 0.5f + slopeFace * 0.6f);
                yield return new WaitForSeconds(0.3f);
                goat.RoamTo(deckTop);
                for (int frame = 1; frame <= 8; frame++)
                {
                    yield return new WaitForSeconds(0.55f);
                    Screenshot($"goat-climb-{frame}");
                }
                yield return new WaitForSeconds(1f);
                Destroy(plank);
                Destroy(ledgeBlock);
                Physics.SyncTransforms();
            }

            // The charge and the ram at a quarter of the speed: seen from the side (a camera 5 m off), then as the scout
            // charged at sees it.
            goat.Unaware = false;
            foreach (string view in new[] { "side", "head-on" })
            {
                goat.PlaceAt(GroundUnder(stage + way * 0.5f), stage + way * 10f);
                goat.GrazeNow(999f);
                Vector3 victim = GroundUnder(stage + way * 9f);
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, victim + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(2f);
                LookAtPoint(local, goat.Feet + Vector3.up * 0.6f);
                if (side != null)
                {
                    Destroy(side);
                }
                side = ViewCamera(GroundUnder(stage + way * 6f + right * 5f) + Vector3.up * 1.2f, GroundUnder(stage + way * 6f) + Vector3.up * 0.6f);
                yield return new WaitForSeconds(0.3f);
                Time.timeScale = 0.25f;
                Goats.NextRoll = 0.05f;
                goat.DecideSoon();
                int rams = Goats.RamsTaken;
                for (int frame = 1; frame <= 24; frame++)
                {
                    yield return new WaitForSeconds(0.1f);
                    if (view == "side")
                    {
                        RenderView(side, $"goat-charge-side-{frame}");
                    }
                    else
                    {
                        Screenshot($"goat-charge-head-on-{frame}");
                    }
                    if (Goats.RamsTaken > rams && frame < 20)
                    {
                        // After the ram: four more pictures.
                        frame = 20;
                    }
                }
                Time.timeScale = timeScale;
                Goats.NextRoll = null;
                for (float until = Time.time + 6f; Time.time < until && (local.data.fallSeconds > 0f || !local.data.fullyConscious);)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(2f);
            }
        }
        finally
        {
            if (ledgeBlock != null)
            {
                Destroy(ledgeBlock);
            }
            if (plank != null)
            {
                Destroy(plank);
            }
            Time.timeScale = timeScale;
            Goats.NextRoll = null;
            if (side != null)
            {
                Destroy(side);
            }
            foreach (Goat other in Goats.All.Where(g => g != null))
            {
                other.Unaware = false;
                other.Puppet = false;
            }
            calm();
            Protect(false);
        }
    }

    /// <summary>
    /// A test camera at a point looking at another, rendering only when asked (RenderView), never to the screen: set up
    /// as the scout's camera is, its post-processing (the game's colour grading) on.
    /// </summary>
    /// <summary>Notes how near the goat's body and head (its middle, neck, head, brow and muzzle) come to test blocks (0 in one), and which part.</summary>
    private static void NoteClearance(Goat goat, ref float nearest, ref string worst, params GameObject[] blocks)
    {
        string[] parts = { "body", "neck", "head", "brow", "muzzle" };
        int part = 0;
        foreach (Vector3 point in goat.BodyPoints())
        {
            foreach (GameObject block in blocks)
            {
                Collider solid = block != null ? block.GetComponent<Collider>() : null;
                if (solid == null)
                {
                    continue;
                }
                float distance = Vector3.Distance(solid.ClosestPoint(point), point);
                if (distance < nearest)
                {
                    nearest = distance;
                    worst = $"its {parts[Mathf.Min(part, parts.Length - 1)]}, {goat.Doing}, its feet at {goat.Feet:F1}";
                }
            }
            part++;
        }
    }

    private static GameObject ViewCamera(Vector3 at, Vector3 lookAt)
    {
        GameObject holder = new GameObject("UltimateAscentGoatCamera");
        Camera camera = holder.AddComponent<Camera>();
        if (Camera.main != null)
        {
            camera.CopyFrom(Camera.main);
        }
        camera.enabled = false;
        camera.targetTexture = null;
        camera.fieldOfView = 50f;
        camera.nearClipPlane = 0.05f;
        Type extra = AccessTools.TypeByName("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
        if (extra != null)
        {
            Component data = holder.GetComponent(extra) ?? holder.AddComponent(extra);
            AccessTools.Property(extra, "renderPostProcessing")?.SetValue(data, true);
        }
        holder.transform.position = at;
        holder.transform.LookAt(lookAt);
        return holder;
    }

    /// <summary>
    /// What sounds the Roots' beetles make (only when asked, on a map with the Roots): every sound effect and clip their
    /// parts and animations hold, and the clips themselves written next to the results (where they can be read).
    /// </summary>
    private IEnumerator BeetleSoundProbe()
    {
        MapHandler.JumpToSegment(Segment.Tropics);
        yield return new WaitForSeconds(8f);
        Beetle[] beetles = UnityEngine.Object.FindObjectsByType<Beetle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Info("beetle probe", $"{beetles.Length} beetles here ({Map.CurrentPlace})");
        Beetle beetle = beetles.FirstOrDefault();
        if (beetle == null)
        {
            yield break;
        }
        HashSet<AudioClip> clips = new HashSet<AudioClip>();
        List<string> found = new List<string>();
        void Note(string where, object value)
        {
            if (value is SFX_Instance sfx && sfx != null)
            {
                found.Add($"{where}: SFX {sfx.name} [{string.Join(", ", (sfx.clips ?? new AudioClip[0]).Where(c => c != null).Select(c => c.name))}] volume {sfx.settings.volume:0.00} range {sfx.settings.range:0}");
                foreach (AudioClip clip in sfx.clips ?? new AudioClip[0])
                {
                    if (clip != null)
                    {
                        clips.Add(clip);
                    }
                }
            }
            else if (value is AudioClip clip && clip != null)
            {
                found.Add($"{where}: clip {clip.name}");
                clips.Add(clip);
            }
            else if (value is AudioSource source && source != null && source.clip != null)
            {
                found.Add($"{where}: source playing {source.clip.name}");
                clips.Add(source.clip);
            }
        }
        foreach (Component part in beetle.GetComponentsInChildren<Component>(true))
        {
            if (part == null)
            {
                continue;
            }
            string where = $"{PathOf(part.transform, beetle.transform)} {part.GetType().Name}";
            foreach (System.Reflection.FieldInfo field in part.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                object value = field.GetValue(part);
                if (value is System.Collections.IEnumerable many && !(value is string) && !(value is Transform))
                {
                    foreach (object one in many)
                    {
                        Note(where + "." + field.Name, one);
                    }
                }
                else
                {
                    Note(where + "." + field.Name, value);
                }
            }
            if (part is Animator animator && animator.runtimeAnimatorController != null)
            {
                foreach (AnimationClip motion in animator.runtimeAnimatorController.animationClips)
                {
                    foreach (AnimationEvent ev in motion.events)
                    {
                        found.Add($"{where}: animation {motion.name} at {ev.time:0.00} s calls {ev.functionName} ({(ev.objectReferenceParameter != null ? ev.objectReferenceParameter.GetType().Name + " " + ev.objectReferenceParameter.name : "-")}, '{ev.stringParameter}', {ev.floatParameter:0.##}, {ev.intParameter})");
                        Note($"{where} event {motion.name}", ev.objectReferenceParameter);
                    }
                }
            }
        }
        Info("beetle probe: its sounds", found.Count == 0 ? "none found on it" : string.Join(" | ", found.Distinct()));
        foreach (AudioClip clip in clips)
        {
            float[] samples = new float[clip.samples * clip.channels];
            bool read = clip.GetData(samples, 0);
            string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "UltimateAscent.SmokeTest.clip-" + clip.name.Replace(' ', '_') + ".wav");
            if (read)
            {
                WriteFloatWav(path, samples, clip.channels, clip.frequency);
            }
            Info("beetle probe: clip", $"{clip.name}: {clip.length:0.00} s, {clip.frequency} Hz, {clip.channels} ch, load {clip.loadType}; written {read}");
        }
    }

    private static void WriteFloatWav(string path, float[] samples, int channels, int rate)
    {
        using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(System.IO.File.Create(path)))
        {
            int bytes = samples.Length * 4;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + bytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)3);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * 4);
            writer.Write((short)(channels * 4));
            writer.Write((short)32);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(bytes);
            foreach (float sample in samples)
            {
                writer.Write(sample);
            }
        }
    }

    /// <summary>The scout's view turned to a point.</summary>
    private static void LookAtPoint(Character local, Vector3 point)
    {
        Vector3 from = MainCamera.instance != null ? MainCamera.instance.transform.position : local.Center;
        Vector3 to = point - from;
        local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y,
            Mathf.Clamp(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -70f, 70f));
    }

    /// <summary>The top of the ground under a point (from a little above it), or the point itself.</summary>
    private static Vector3 GroundUnder(Vector3 at)
    {
        return Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out RaycastHit floor, 8f, HelperFunctions.terrainMapMask) ? floor.point : at;
    }

    /// <summary>
    /// Open, nearly flat ground 16 m long (a goat at one end, a scout 8-12 m along it and room behind them to be
    /// thrown), near a point: every metre of it no steeper than 30 degrees, within 1.2 m of the start's height, with room
    /// for a scout, and not in water.
    /// </summary>
    private static bool GoatStage(Vector3 near, out Vector3 start, out Vector3 way)
    {
        WaterZone[] water = UnityEngine.Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
        for (int ring = 0; ring <= 6; ring++)
        {
            for (int place = 0; place < (ring == 0 ? 1 : 8); place++)
            {
                Vector3 origin = near + Quaternion.Euler(0f, place * 45f, 0f) * Vector3.forward * (ring * 12f);
                if (!Physics.Raycast(origin + Vector3.up * 30f, Vector3.down, out RaycastHit there, 80f, HelperFunctions.terrainMapMask) || InsideSomething(there.point + Vector3.up))
                {
                    continue;
                }
                for (int angle = 0; angle < 360; angle += 15)
                {
                    Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    bool open = true;
                    for (float d = 0f; d <= 16f && open; d += 1f)
                    {
                        Vector3 p = there.point + dir * d;
                        open = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit floor, 8f, HelperFunctions.terrainMapMask) && floor.normal.y >= 0.86f
                            && Mathf.Abs(floor.point.y - there.point.y) < 1.2f
                            && !Physics.CheckCapsule(floor.point + Vector3.up * 0.5f, floor.point + Vector3.up * 1.6f, 0.35f, HelperFunctions.terrainMapMask)
                            && !GroundItems.InWater(floor.point, water) && !Hazard(floor.point + Vector3.up, 1.5f);
                    }
                    if (open)
                    {
                        start = there.point;
                        way = dir;
                        return true;
                    }
                }
            }
        }
        start = near;
        way = Vector3.forward;
        return false;
    }
}
