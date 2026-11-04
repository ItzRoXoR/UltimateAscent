using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UltimateAscent.Features.Fixes;
using UnityEngine;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// The Scoutmaster on a test stage (smroutes): a flat floor with a ledge 7 m over it (a block 2.5 m thick with nothing under it).
/// He pulls a scout off a rope, a Chain Launcher's chain and a piton (every ascent), and from ascent 17, stuck under a
/// scout up on the ledge, he goes up a rope hanging from it, a chain up to it, or a pillar beside it to drop from.
/// </summary>
internal sealed partial class Runner
{
    /// <summary>Why a bot left a rope during a check (who called the game's stop), latest last.</summary>
    private static readonly List<string> botRopeStops = new List<string>();

    private static void BotLeavesRope(Character ___character, bool jumped)
    {
        if (___character == null || !___character.isBot || botRopeStops.Count > 12)
        {
            return;
        }
        System.Diagnostics.StackTrace trace = new System.Diagnostics.StackTrace(1, false);
        string callers = string.Join(" < ", trace.GetFrames().Take(5).Select(f => f.GetMethod()).Where(m => m != null).Select(m => $"{m.DeclaringType?.Name}.{m.Name}"));
        CharacterData data = ___character.data;
        Rope rope = data.heldRope;
        float angle = rope != null && rope.climbingAPI != null ? rope.climbingAPI.GetAngleAtPercent(data.ropePercent) : -1f;
        botRopeStops.Add($"{Time.time:0.0}: at {data.ropePercent:0.00} of the rope (angle {angle:0}, his limit {___character.refs.ropeHandling.maxRopeAngle:0}), jumped {jumped}, by {callers}");
    }

    private const float LedgeHigh = 7f;
    private const float PillarOver = 3f;
    /// <summary>The ledge's face: from 4.5 m up (out of his reach from the floor) to its top, as a cliff's under a rope.</summary>
    private const float LedgeThick = 2.5f;

    /// <summary>
    /// A floor for the stage near a spot (the ledge, the pillar beside it, a chain's foot and room to walk round):
    /// 24 × 22 m, its top just over the highest ground under it (where that is at most 8 m over the lowest), not under
    /// water and with nothing over it for 15 m; the evenest such spot, the nearer the better. Its top under the ledge's
    /// middle comes back as <paramref name="middle"/>.
    /// </summary>
    private static GameObject RouteFloor(Vector3 near, out Vector3 middle)
    {
        WaterZone[] water = UnityEngine.Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
        float bestScore = float.MaxValue;
        Vector3 bestTop = near;
        float bestLow = 0f;
        for (int ring = 0; ring <= 10; ring++)
        {
            for (int place = 0; place < (ring == 0 ? 1 : 12); place++)
            {
                Vector3 origin = near + Quaternion.Euler(0f, place * 30f, 0f) * Vector3.forward * (ring * 12f);
                float lo = float.MaxValue;
                float hi = float.MinValue;
                int hits = 0;
                int tries = 0;
                for (float x = -10f; x <= 14f; x += 2f)
                {
                    for (float z = -10f; z <= 12f; z += 2f)
                    {
                        tries++;
                        if (Physics.Raycast(origin + new Vector3(x, 60f, z), Vector3.down, out RaycastHit ground, 160f, HelperFunctions.terrainMapMask))
                        {
                            hits++;
                            lo = Mathf.Min(lo, ground.point.y);
                            hi = Mathf.Max(hi, ground.point.y);
                        }
                    }
                }
                if (hits < tries * 0.8f || hi - lo > 8f)
                {
                    continue;
                }
                Vector3 top = new Vector3(origin.x, hi + 0.2f, origin.z);
                float score = hi - lo + ring * 0.3f;
                if (score >= bestScore || GroundItems.InWater(top + Vector3.up * 0.5f, water)
                    || Physics.CheckBox(top + new Vector3(2f, 8f, 1f), new Vector3(12.5f, 7.5f, 11.5f), Quaternion.identity, HelperFunctions.terrainMapMask))
                {
                    continue;
                }
                bestScore = score;
                bestTop = top;
                bestLow = lo;
            }
        }
        middle = bestTop;
        if (bestScore == float.MaxValue)
        {
            return null;
        }
        float depth = bestTop.y - bestLow + 1f;
        return RouteBlock("Floor", bestTop + new Vector3(2f, -depth / 2f, 1f), new Vector3(24f, depth, 22f));
    }

    private static GameObject RouteBlock(string name, Vector3 centre, Vector3 size)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "UltimateAscentTest" + name;
        block.layer = LayerMask.NameToLayer("Map");
        block.transform.SetPositionAndRotation(centre, Quaternion.identity);
        block.transform.localScale = size;
        Grey(block);
        Physics.SyncTransforms();
        return block;
    }

    /// <summary>A Chain Launcher's chain from here to there, built as the launcher builds one (its sag tried from the most down).</summary>
    private static JungleVine LaunchedChain(Vector3 from, Vector3 to)
    {
        VineShooter shooter = ItemWith<VineShooter>()?.GetComponent<VineShooter>();
        JungleVine prefab = shooter != null && shooter.vinePrefab != null ? shooter.vinePrefab.GetComponent<JungleVine>() : null;
        if (prefab == null)
        {
            return null;
        }
        for (int i = 0; i < 10; i++)
        {
            Vector2 sag = Vector2.Lerp(new Vector2(prefab.minDown, prefab.maxDown), Vector2.zero, i / 9f);
            float hang = -UnityEngine.Random.Range(sag.x, sag.y);
            if (JungleVine.CheckVinePath(from, to, hang, out Vector3 mid))
            {
                JungleVine chain = PhotonNetwork.Instantiate(shooter.vinePrefab.name, from, Quaternion.identity, 0).GetComponent<JungleVine>();
                chain.GetComponent<PhotonView>().RPC("ForceBuildVine_RPC", RpcTarget.All, from, to, hang, mid);
                return chain;
            }
        }
        return null;
    }

    /// <summary>A piton hammered into a wall here (facing out along the normal), as a scout's piton is put in.</summary>
    private static ClimbHandle HammeredPiton(Vector3 at, Vector3 normal)
    {
        ClimbingSpikeComponent spike = ItemWith<ClimbingSpikeComponent>()?.GetComponent<ClimbingSpikeComponent>();
        if (spike == null || spike.hammeredVersionPrefab == null)
        {
            return null;
        }
        GameObject piton = PhotonNetwork.InstantiateItemRoom(spike.hammeredVersionPrefab.gameObject.name, at, Quaternion.LookRotation(-normal, Vector3.up), warnIfNotHost: false);
        return piton != null ? piton.GetComponentInChildren<ClimbHandle>() : null;
    }

    private IEnumerator ScoutmasterRouteChecks()
    {
        Character local = Local;
        Scoutmaster scoutmaster = UnityEngine.Object.FindObjectsByType<Scoutmaster>(FindObjectsSortMode.None).FirstOrDefault();
        if (scoutmaster == null)
        {
            Info("the Scoutmaster's checks are skipped", "no Scoutmaster here");
            yield break;
        }
        Protect(true);
        yield return UntilUp(local);
        Vector3 home = GroundUnder(local.Center);
        GameObject floor = RouteFloor(local.Center, out Vector3 ground);
        if (floor == null)
        {
            Fail("room for the Scoutmaster's test stage (24 × 22 m with nothing over it for 15 m, the ground under it at most 8 m uneven)");
            yield break;
        }
        bool routes = Rules.Enabled(Lvl.Hazards);
        Info("the Scoutmaster's test stage", $"a floor at {ground:F0} ({Map.CurrentPlace}), a ledge {LedgeHigh} m over it; his ways up (17) {(routes ? "on" : "off")}");
        GameObject ledge = RouteBlock("Ledge", ground + Vector3.up * (LedgeHigh - LedgeThick / 2f), new Vector3(5f, LedgeThick, 5f));
        Vector3 top = ground + Vector3.up * LedgeHigh;
        GameObject anchor = null;
        Rope rope = null;
        JungleVine chain = null;
        GameObject pillar = null;
        ClimbHandle piton = null;
        HarmonyLib.Harmony keep = KeepHisTarget();
        try
        {
            // A rope hanging from the ledge's edge (a Rope Shooter's).
            rope = ShotRope(ground + new Vector3(0f, 0f, 2.75f), LedgeHigh, out anchor);
            yield return new WaitForSeconds(5f);
            RopeSegment low = (rope != null ? rope.GetRopeSegments() : new List<Transform>())
                .Select(s => (segment: s.GetComponent<RopeSegment>(), high: OverGround(s.position)))
                .Where(s => s.segment != null && s.high > 0.9f && s.high < 2f)
                .OrderBy(s => Mathf.Abs(s.high - 1.4f)).Select(s => s.segment).FirstOrDefault();
            if (low == null)
            {
                Fail("a rope hanging from the test ledge, low enough to hold within the Scoutmaster's reach");
            }
            else
            {
                Vector3 at = low.transform.position;
                yield return PullsOff(scoutmaster, "rope", at, at + Vector3.left * 2.2f, () => low.Interact(local), () => local.data.isRopeClimbing, () => { },
                    () => $"its rope {local.data.heldRope == rope}");
                if (routes)
                {
                    yield return WayUp(scoutmaster, top, ground, A17_ScoutmasterRoutes.Way.Rope, "a rope hanging from the ledge (a Rope Shooter's)");
                }
            }
            if (anchor != null)
            {
                PhotonNetwork.Destroy(anchor);
            }
            if (rope != null && rope.photonView != null)
            {
                PhotonNetwork.Destroy(rope.photonView);
            }

            // A Chain Launcher's chain from the floor up to the ledge's edge.
            chain = LaunchedChain(ground + new Vector3(0f, 1.5f, 8f), top + new Vector3(0f, -0.2f, 2.5f));
            if (chain == null)
            {
                Fail("a Chain Launcher's chain up to the test ledge");
            }
            else
            {
                yield return new WaitForSeconds(1f);
                // Held where its point is 2.3 m up (the scout hangs a metre under the chain).
                float along = -1f;
                float best = float.MaxValue;
                for (float p = 0.02f; p <= 0.981f; p += 0.01f)
                {
                    float off = Mathf.Abs(OverGround(chain.GetPosition(p)) - 2.3f);
                    if (off < best)
                    {
                        best = off;
                        along = p;
                    }
                }
                if (best > 0.6f)
                {
                    Fail("a point of the chain 2.3 m over the floor, to hold within the Scoutmaster's reach");
                }
                else
                {
                    Vector3 at = chain.GetPosition(along);
                    JungleVine held = chain;
                    yield return PullsOff(scoutmaster, "chain", at + Vector3.down, at + Vector3.down + Vector3.left * 2.2f, () => held.Interact(local), () => local.data.isVineClimbing, () =>
                    {
                        // Held where it is low (no sliding along the chain from the grab).
                        if (local.data.isVineClimbing)
                        {
                            local.refs.vineClimbing.attachVel = 0f;
                            local.data.vinePercent = along;
                        }
                    }, () => $"its chain {local.data.heldVine == held}, at {local.data.vinePercent:0.00} of it (held at {along:0.00}), can take a vine {local.refs.vineClimbing.canInteractVine}");
                }
                if (routes)
                {
                    yield return WayUp(scoutmaster, top, ground, A17_ScoutmasterRoutes.Way.Vine, "a Chain Launcher's chain up to the ledge");
                }
                PhotonNetwork.Destroy(chain.GetComponent<PhotonView>());
            }

            // A pillar beside the ledge, 3 m higher: a piton in its side, and its top to drop down from.
            float pillarTop = LedgeHigh + PillarOver;
            pillar = RouteBlock("Pillar", ground + new Vector3(6f, (pillarTop - 1.5f) / 2f, 0f), new Vector3(4f, pillarTop + 1.5f, 4f));
            Vector3 face = ground + new Vector3(6f, 1.3f, -2f);
            piton = HammeredPiton(face, Vector3.back);
            if (piton == null)
            {
                Fail("a piton hammered into the test pillar");
            }
            else
            {
                yield return new WaitForSeconds(0.5f);
                ClimbHandle handle = piton;
                Vector3 hang = face + Vector3.back * 0.5f + Vector3.down * 0.3f;
                yield return PullsOff(scoutmaster, "piton", hang, hang + Vector3.back * 2.2f, () => handle.Interact(local), () => local.data.currentClimbHandle != null, () => { },
                    () => $"its network view {handle.view != null}, hanging on it {(handle.hanger != null ? handle.hanger.characterName : "nobody")}, can take a piton {local.refs.climbing.canInteractWithPiton}");
            }
            if (routes)
            {
                yield return WayUp(scoutmaster, top, ground, A17_ScoutmasterRoutes.Way.HighGround, "a pillar beside the ledge, to drop down from");
            }
        }
        finally
        {
            keep.UnpatchSelf();
            scoutmaster.SetCurrentTarget(null);
            if (piton != null && piton.GetComponentInParent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(piton.GetComponentInParent<PhotonView>());
            }
            UnityEngine.Object.Destroy(ledge);
            UnityEngine.Object.Destroy(floor);
            if (pillar != null)
            {
                UnityEngine.Object.Destroy(pillar);
            }
        }
        // Everything as it was for the next checks: he lets go of anyone and goes back to his hiding spot, the scout off any hold.
        Character him = scoutmaster.character;
        if (him.data.grabbedPlayer != null)
        {
            him.photonView.RPC("RPCA_GrabUnattach", RpcTarget.All);
        }
        scoutmaster.tpCounter = 10f;
        scoutmaster.TeleportFarAway();
        yield return new WaitForSeconds(1f);
        yield return UntilUp(local);
        local.refs.climbing.StopAnyClimbing();
        local.data.carrierCondor = null;
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
        yield return new WaitForSeconds(1f);
        yield return UntilUp(local);
        ClearStatuses(Afflictions);
        Protect(true);
    }

    /// <summary>
    /// The Scoutmaster pulls a scout off what they hold (every ascent): the scout holding it low (within his reach from
    /// the floor), he put beside them first and then sent after them; his own grab takes them off it, they cannot take
    /// hold again while he holds them, and his throw lands (thrown, hurt).
    /// </summary>
    private IEnumerator PullsOff(Scoutmaster scoutmaster, string hold, Vector3 at, Vector3 beside, Action take, Func<bool> holding, Action stay, Func<string> note)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            pullsOffMeasured = true;
            yield return PullsOffOnce(scoutmaster, hold, at, beside, take, holding, stay, note, attempt == 2);
            if (pullsOffMeasured)
            {
                yield break;
            }
        }
    }

    /// <summary>Whether the last attempt measured anything (the scout still held on when he grabbed them).</summary>
    private bool pullsOffMeasured;

    private IEnumerator PullsOffOnce(Scoutmaster scoutmaster, string hold, Vector3 at, Vector3 beside, Action take, Func<bool> holding, Action stay, Func<string> note, bool last)
    {
        Character local = Local;
        Character him = scoutmaster.character;
        scoutmaster.SetCurrentTarget(null);
        yield return UntilUp(local);
        // He first: sent after a scout far off, he would teleport to 50-70 m from them.
        him.photonView.RPC("WarpPlayerRPC", RpcTarget.All, GroundUnder(beside) + Vector3.up, false);
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, at, false);
        for (float until = Time.time + 3f; Time.time < until && (local.warping || him.warping);)
        {
            scoutmaster.tpCounter = 0f;
            scoutmaster.chillForSeconds = 0.5f;
            yield return null;
        }
        local.refs.ropeHandling.lastInteractedRopeTime = 0f;
        local.refs.vineClimbing.lastInteractedVineTime = 0f;
        local.refs.climbing.lastInteractedPitonTime = 0f;
        local.AddStamina(1f);
        Character heldBy = local.data.grabbingPlayer;
        bool condor = local.data.carrierCondor != null;
        int refused = ScoutmasterFixes.HoldsRefused;
        // Held by him already, before reaching for the hold: how (his grip then), and let go of as a jump lets a scout go.
        string before = "";
        if (heldBy != null)
        {
            CharacterData d = heldBy.data;
            before = $"; before reaching for it held by {heldBy.characterName}: his grip on {(d.grabbedPlayer != null ? d.grabbedPlayer.characterName : "nobody")}, joint {d.grabJoint != null}, "
                + $"reaching {d.isReaching}, since pressing reach {d.sincePressReach:0.0} s, his target {(scoutmaster.currentTarget != null ? scoutmaster.currentTarget.characterName : "none")}, throwing {scoutmaster.isThrowing}; let go first";
            heldBy.photonView.RPC("RPCA_GrabUnattach", RpcTarget.All);
            yield return new WaitForSeconds(0.5f);
            refused = ScoutmasterFixes.HoldsRefused;
        }
        take();
        // How the hold went in its first moments (taken at once? kept?).
        float tookAt = -1f;
        float lostAt = -1f;
        float began = Time.time;
        string first = null;
        for (float until = Time.time + 1.2f; Time.time < until;)
        {
            stay();
            scoutmaster.tpCounter = 0f;
            scoutmaster.chillForSeconds = 0.5f;
            yield return null;
            first ??= note();
            bool now = holding();
            if (now && tookAt < 0f)
            {
                tookAt = Time.time - began;
            }
            if (!now && tookAt >= 0f && lostAt < 0f)
            {
                lostAt = Time.time - began;
            }
        }
        bool tookHold = holding();
        string start = $"took hold {(tookAt >= 0f ? $"after {tookAt:0.00} s" : "never")}{(lostAt >= 0f ? $", lost after {lostAt:0.00} s" : "")}; first frame: {first}; at 1.2 s: {note()}, stamina {local.data.currentStamina:0.00}; "
            + $"a condor {condor}, holds refused while he held them {ScoutmasterFixes.HoldsRefused - refused}{before}";
        float high = OverGround(local.Center);
        Protect(false);
        ClearStatuses(Afflictions);
        int pulled = ScoutmasterFixes.PulledOff;
        float hurtBefore = Afflictions.GetCurrentStatus(Status.Injury);
        scoutmaster.chillForSeconds = 0f;
        scoutmaster.SetCurrentTarget(local, 30f);
        // His own grab (as on a scout who has had him in sight a while): left to him, up to 12 s.
        bool grabbed = false;
        int frames = 0;
        int targeted = 0;
        float nearest = float.MaxValue;
        bool grounded = false;
        bool heldOn = false;
        for (float until = Time.time + 12f; Time.time < until && !grabbed;)
        {
            scoutmaster.targetHasSeenMeCounter = Mathf.Max(scoutmaster.targetHasSeenMeCounter, 2f);
            scoutmaster.tpCounter = 0f;
            stay();
            heldOn = holding();
            yield return null;
            frames++;
            targeted += scoutmaster.currentTarget == local ? 1 : 0;
            nearest = Mathf.Min(nearest, Vector3.Distance(him.Center, local.Center));
            grounded |= him.data.isGrounded;
            grabbed = local.data.grabbingPlayer == him;
        }
        string waiting = $"his target the scout {targeted * 100 / Mathf.Max(frames, 1)}% of the wait, {nearest:0.0} m at the nearest, on the ground {grounded}";
        bool stillHolding = false;
        bool tookAgain = false;
        if (grabbed)
        {
            yield return null;
            stillHolding = holding();
            local.refs.ropeHandling.lastInteractedRopeTime = 0f;
            local.refs.vineClimbing.lastInteractedVineTime = 0f;
            local.refs.climbing.lastInteractedPitonTime = 0f;
            take();
            yield return new WaitForSeconds(0.3f);
            tookAgain = holding();
        }
        for (float until = Time.time + 7f; Time.time < until && grabbed && (local.data.grabbingPlayer != null || Afflictions.GetCurrentStatus(Status.Injury) <= hurtBefore);)
        {
            yield return null;
        }
        float hurt = Afflictions.GetCurrentStatus(Status.Injury) - hurtBefore;
        if (grabbed && !heldOn && !last)
        {
            // The scout was off it before he grabbed them (the hold swung them off): nothing measured, once more.
            Info($"the Scoutmaster's {hold} check goes again", "the scout was off it before he grabbed them");
            pullsOffMeasured = false;
            scoutmaster.SetCurrentTarget(null);
            yield return new WaitForSeconds(1f);
            yield return UntilUp(local);
            ClearStatuses(Afflictions);
            Protect(true);
            yield break;
        }
        Check($"the Scoutmaster pulls a scout off a {hold}: they let go when he grabs them, cannot take hold again while he holds them, and his throw lands",
            tookHold && grabbed && !stillHolding && !tookAgain && ScoutmasterFixes.PulledOff > pulled && local.data.grabbingPlayer == null && hurt > 0.1f,
            $"on the {hold} {tookHold} ({high:0.0} m over the floor; {start}); grabbed by him {grabbed} ({waiting}; on it just before {heldOn}); still holding it {stillHolding}; took hold again {tookAgain}; "
            + $"pulled off {ScoutmasterFixes.PulledOff - pulled}; let go by him {local.data.grabbingPlayer == null}, injury +{hurt * 100f:0.#} (his throw: 30)");
        scoutmaster.SetCurrentTarget(null);
        yield return new WaitForSeconds(1f);
        yield return UntilUp(local);
        ClearStatuses(Afflictions);
        Protect(true);
    }

    /// <summary>The scout up on the ledge, the Scoutmaster put beside it and then sent after them, and what way he took to them (up to 75 s).</summary>
    private IEnumerator WayUp(Scoutmaster scoutmaster, Vector3 top, Vector3 ground, A17_ScoutmasterRoutes.Way way, string what)
    {
        Character local = Local;
        Character him = scoutmaster.character;
        scoutmaster.SetCurrentTarget(null);
        yield return UntilUp(local);
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, top + Vector3.up * 1.2f, false);
        him.photonView.RPC("WarpPlayerRPC", RpcTarget.All, ground + new Vector3(-3.5f, 1f, 0f), false);
        for (float until = Time.time + 4.5f; Time.time < until;)
        {
            scoutmaster.tpCounter = 0f;
            scoutmaster.chillForSeconds = 0.5f;
            yield return null;
        }
        scoutmaster.chillForSeconds = 0f;
        HarmonyLib.Harmony watch = new HarmonyLib.Harmony("UltimateAscent.SmokeTest.ropestop");
        watch.Patch(HarmonyLib.AccessTools.Method(typeof(CharacterRopeHandling), "StopRopeClimbingRpc"), prefix: new HarmonyLib.HarmonyMethod(typeof(Runner), nameof(BotLeavesRope)));
        botRopeStops.Clear();
        List<string> states = new List<string>();
        float nextState = 0f;
        int taken = A17_ScoutmasterRoutes.Taken[(int)way];
        int givenUp = A17_ScoutmasterRoutes.GivenUp;
        int reached = A17_ScoutmasterRoutes.Reached;
        bool onIt = false;
        bool grabbed = false;
        float highest = float.MinValue;
        // The highest he got while on the hold (under the ledge's top): up it, not round it.
        float highestOnHold = float.MinValue;
        float began = Time.time;
        scoutmaster.SetCurrentTarget(local, 120f);
        for (float until = Time.time + 75f; Time.time < until && !grabbed;)
        {
            // He has been seen (as when the scout watches him come): he goes on walking at them near, and grabs.
            // No teleport of his own (out of sight 30 s, the game's would take him off the stage).
            scoutmaster.targetHasSeenMeCounter = Mathf.Max(scoutmaster.targetHasSeenMeCounter, 2f);
            scoutmaster.tpCounter = 0f;
            yield return null;
            onIt |= way == A17_ScoutmasterRoutes.Way.Rope ? him.data.isRopeClimbing
                : way == A17_ScoutmasterRoutes.Way.Vine ? him.data.isVineClimbing
                : him.Center.y > top.y + PillarOver;
            highest = Mathf.Max(highest, him.Center.y - top.y);
            if (him.data.isRopeClimbing || him.data.isVineClimbing)
            {
                highestOnHold = Mathf.Max(highestOnHold, him.Center.y - top.y);
            }
            grabbed = local.data.grabbingPlayer == him;
            if (Time.time >= nextState)
            {
                nextState = Time.time + 5f;
                CharacterData d = him.data;
                states.Add($"{Time.time - began:0}s {him.Center.y - top.y:0.0} m to the ledge, {Vector3.Distance(him.Center, local.Center):0.0} m off"
                    + $"{(d.isGrounded ? ", on the ground" : "")}{(d.isClimbing ? ", climbing" : "")}{(d.isRopeClimbing ? ", on a rope" : "")}{(d.isVineClimbing ? ", on a vine" : "")}");
            }
        }
        watch.UnpatchSelf();
        float took = Time.time - began;
        A17_ScoutmasterRoutes.Way? last = A17_ScoutmasterRoutes.LastWay;
        string trail = string.Join(" | ", A17_ScoutmasterRoutes.Trail.Where(e => e.time >= began).Select(e => $"{e.time - began:0}s {e.what}"));
        Check($"stuck under a scout on a ledge he cannot climb to, the Scoutmaster takes {what} and reaches them (17)",
            A17_ScoutmasterRoutes.Taken[(int)way] > taken && onIt && grabbed && (way == A17_ScoutmasterRoutes.Way.HighGround || highestOnHold > -3.5f),
            $"took the {way} {A17_ScoutmasterRoutes.Taken[(int)way] - taken} time(s) (last way taken: {(last.HasValue ? last.Value.ToString() : "none")}); on it {onIt}"
            + (way == A17_ScoutmasterRoutes.Way.HighGround ? "; " : $" (up it to {-highestOnHold:0.0} m under the ledge's top; wanted within 3.5); ")
            + $"at most {highest:0.0} m over the ledge; grabbed the scout {grabbed} after {took:0} s; ways given up {A17_ScoutmasterRoutes.GivenUp - givenUp}, followed to the scout {A17_ScoutmasterRoutes.Reached - reached}"
            + $"; his trail: {(trail.Length > 0 ? trail : "nothing")}; he was: {string.Join(" | ", states)}"
            + (botRopeStops.Count > 0 ? $"; off the rope: {string.Join(" | ", botRopeStops)}" : ""));
        scoutmaster.SetCurrentTarget(null);
        for (float until = Time.time + 6f; Time.time < until && local.data.grabbingPlayer != null;)
        {
            yield return null;
        }
        yield return new WaitForSeconds(1f);
        yield return UntilUp(local);
        ClearStatuses(Afflictions);
    }
}
