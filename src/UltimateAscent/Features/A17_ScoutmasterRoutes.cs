using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: the Scoutmaster finds a way up to a scout he cannot reach straight (the user's list of 2026-11-04). In the
/// game he only walks and climbs straight at his target, so under a scout on a ledge he could not climb to (an
/// overhang, a rock with no wall up to it) he stood turning round until, out of sight, he teleported. Now, stuck below
/// the scout (no nearer for 6 s, or standing right under them for 1.5 s), he looks for a way up:
/// <list type="bullet">
/// <item>a rope tied to an anchor (the map's or a scout's) or a vine or a chain (the jungle's, a Chain Launcher's)
/// whose low end he can reach from the ground and whose other end comes near the scout: he walks to it, climbs it and
/// gets off at the scout;</item>
/// <item>or higher ground near the scout with an open way down to them: he goes up there and drops down to them.</item>
/// </list>
/// Landed on the scout's head, he steps off them (on them, with no ground under his feet, he never grabbed).
/// He takes the shortest way first; one that gets him no nearer for 6 s is given up for 45 s and the next tried. With
/// none left he goes on as in the game (out of sight for 30 s, he teleports near the scout).
/// </summary>
internal static class A17_ScoutmasterRoutes
{
    private static bool On => Rules.Enabled(Lvl.Hazards);

    /// <summary>No nearer to the scout by this for <see cref="StuckSeconds"/> (or right under them for <see cref="UnderSeconds"/>): stuck.</summary>
    private const float Progress = 1f;
    private const float StuckSeconds = 6f;
    private const float UnderSeconds = 1.5f;
    /// <summary>Right under the scout: this near across, and they this much higher.</summary>
    private const float UnderAcross = 2.5f;
    private const float UnderBelow = 3f;
    /// <summary>The game's grab: within 3 m of the scout. A way is not looked for nearer than this.</summary>
    private const float GrabRange = 3.2f;
    /// <summary>A way up is looked for only to a scout at least this much higher than him (level with them, his own walk does).</summary>
    private const float AboveHim = 1.5f;
    private const float PlanEvery = 2f;
    /// <summary>A way that got him no nearer for <see cref="LegPatience"/> s is given up for this long.</summary>
    private const float GiveUpSeconds = 45f;
    private const float LegPatience = 6f;
    private const float HoldSeconds = 25f;
    /// <summary>Off a hold's end (or the higher ground), this long to get onto the scout's ledge before the game's own chase goes on.</summary>
    private const float OffSeconds = 8f;
    /// <summary>A hold's low end: at most this high over the ground (he takes hold from the ground), and hanging (at least this high).</summary>
    internal const float ReachUp = 2.4f;
    private const float HangsOver = 0.6f;
    /// <summary>He takes a hold from beside it, this near across (a rope taken from farther is yanked aside, past what holds him).</summary>
    private const float TakeHoldAcross = 0.8f;
    /// <summary>His own walk stops 2.5 m short of where it goes; within this he walks the last steps himself.</summary>
    private const float LastSteps = 3.5f;
    /// <summary>Off a hold's top, he keeps reaching up for the wall this long (the game lets nobody take a wall within 0.2 s of a hold).</summary>
    private const float ReachSeconds = 1f;
    /// <summary>On a hold, no further along it for this long: at its end.</summary>
    private const float StallSeconds = 0.75f;
    /// <summary>A hold's high end: within this of the scout, and at most this far under their feet (he reaches up for their ledge from it).</summary>
    private const float NearScout = 5f;
    private const float EndUnderFeet = 1.5f;
    /// <summary>He goes at most this far to a way.</summary>
    private const float WayFar = 80f;
    /// <summary>Higher ground for a drop: this much over the scout's feet, this far across from them.</summary>
    private const float DropLeast = 1f;
    private const float DropMost = 12f;
    private static readonly float[] DropAcross = { 3f, 5f, 8f, 12f };
    /// <summary>Higher ground counts this much farther than a hold (a hold is the surer way).</summary>
    private const float GroundExtra = 5f;

    internal enum Way { Rope, Vine, HighGround }

    private enum Leg { ToIt, OnIt, Off }

    private sealed class Route
    {
        internal Way Way;
        internal Leg Leg = Leg.ToIt;
        internal Rope Rope;
        internal JungleVine Vine;
        /// <summary>Where he takes hold (the hold's low end), or the higher ground.</summary>
        internal Vector3 Start;
        /// <summary>The hold's end at the scout, and how far along the hold it is.</summary>
        internal Vector3 End;
        internal float EndPercent;
        internal float LegBegan;
        internal float Best = float.MaxValue;
        internal float BetterAt;
        /// <summary>On the hold: where along it he last moved on, and when.</summary>
        internal float Along = -1f;
        internal float MovedAt;
        /// <summary>Off a hold's top: until when he reaches up for the wall.</summary>
        internal float ReachUntil;
    }

    private sealed class Pursuit
    {
        internal Character Target;
        internal float Best = float.MaxValue;
        internal float BetterAt;
        /// <summary>Where the scout was when his nearest so far was taken (they move on: a new start).</summary>
        internal Vector3 TargetAt;
        internal float NextPlan;
        internal float Steered;
        internal Route Route;
        internal readonly List<(Vector3 at, float until)> GivenUp = new List<(Vector3, float)>();
    }

    private static readonly Dictionary<Scoutmaster, Pursuit> pursuits = new Dictionary<Scoutmaster, Pursuit>();

    /// <summary>Which way along the vine he climbs (+1 up its length, -1 down, 0 still), for the bot's vine climbing.</summary>
    private static readonly Dictionary<Character, float> vineInput = new Dictionary<Character, float>();

    /// <summary>For the tests: ways taken (by kind), given up, and followed to the scout.</summary>
    internal static readonly int[] Taken = new int[3];
    internal static int GivenUp;
    internal static int Reached;
    internal static Way? LastWay;

    /// <summary>For the tests: what he did on his ways, latest last (at most 40), with the time.</summary>
    internal static readonly List<(float time, string what)> Trail = new List<(float, string)>();

    private static void Note(string what)
    {
        Trail.Add((Time.time, what));
        if (Trail.Count > 40)
        {
            Trail.RemoveAt(0);
        }
        Plugin.Debug("Ascent 17: the Scoutmaster " + what);
    }

    internal static void Reset()
    {
        foreach (KeyValuePair<Scoutmaster, Pursuit> each in pursuits.ToList())
        {
            if (each.Key != null && each.Key.character != null)
            {
                LetGo(each.Key.character);
            }
        }
        pursuits.Clear();
        vineInput.Clear();
    }

    private static Pursuit For(Scoutmaster scoutmaster, Character target)
    {
        if (!pursuits.TryGetValue(scoutmaster, out Pursuit pursuit))
        {
            pursuit = new Pursuit();
            pursuits[scoutmaster] = pursuit;
        }
        if (pursuit.Target != target)
        {
            Drop(scoutmaster, pursuit);
            pursuit.Target = target;
            pursuit.Best = float.MaxValue;
            pursuit.BetterAt = Time.time;
        }
        return pursuit;
    }

    [HarmonyPatch(typeof(Scoutmaster), nameof(Scoutmaster.Chase))]
    private static class FindsAWay
    {
        private static bool Prefix(Scoutmaster __instance)
        {
            Character him = __instance.character;
            Character target = __instance.currentTarget;
            if (!On || him == null || target == null || __instance.view == null || !__instance.view.IsMine)
            {
                return true;
            }
            Pursuit pursuit = For(__instance, target);
            pursuit.Steered = Time.time;
            if (him.data.lastStoodOnPlayer == target && him.data.sinceStandOnPlayer < 0.25f)
            {
                // Dropped onto the scout's head: off them first (his grab wants his feet on the ground; on them he stood for good).
                __instance.LookAt(target.Head);
                him.input.movementInput = new Vector2(0f, -1f);
                return false;
            }
            // Out of sight 30 s: the game's teleport near the scout, as in the game.
            if (__instance.sinceSeenTarget > 30f)
            {
                Drop(__instance, pursuit);
                return true;
            }
            if (pursuit.Route != null && Steer(__instance, him, target, pursuit))
            {
                return false;
            }

            float distance = Vector3.Distance(him.Center, target.Center);
            if (distance < pursuit.Best - Progress || Vector3.Distance(target.Center, pursuit.TargetAt) > 3f)
            {
                // Nearer (or the scout moved on: a new start).
                pursuit.Best = distance;
                pursuit.BetterAt = Time.time;
                pursuit.TargetAt = target.Center;
            }
            bool under = him.data.isGrounded && !him.data.isClimbing && HelperFunctions.FlatDistance(him.Center, target.Center) < UnderAcross && target.Center.y - him.Center.y > UnderBelow;
            float waited = Time.time - pursuit.BetterAt;
            if (distance < GrabRange || target.Center.y - him.Center.y < AboveHim || Time.time < pursuit.NextPlan || !(waited > StuckSeconds || (under && waited > UnderSeconds)))
            {
                return true;
            }
            pursuit.NextPlan = Time.time + PlanEvery;
            pursuit.Route = Plan(him, target, pursuit, out string looked);
            if (pursuit.Route == null)
            {
                Note($"cannot reach {target.characterName} straight ({distance:0.0} m off{(under ? ", right under them" : "")}): no way ({looked})");
                return true;
            }
            Taken[(int)pursuit.Route.Way]++;
            LastWay = pursuit.Route.Way;
            Note($"cannot reach {target.characterName} straight ({distance:0.0} m off{(under ? ", right under them" : "")}): takes the {pursuit.Route.Way} from {pursuit.Route.Start:F1} to {pursuit.Route.End:F1}");
            return !Steer(__instance, him, target, pursuit);
        }
    }

    /// <summary>A Scoutmaster on a way who is no longer chasing (his target gone, a breather, a teleport) lets go of what he holds.</summary>
    [HarmonyPatch(typeof(Scoutmaster), "Update")]
    private static class LeavesAWay
    {
        private static void Postfix(Scoutmaster __instance)
        {
            if (pursuits.TryGetValue(__instance, out Pursuit pursuit) && pursuit.Route != null && Time.time - pursuit.Steered > 0.5f)
            {
                Drop(__instance, pursuit);
            }
        }
    }

    /// <summary>A bot on a vine: the game moves along it only by the local player's input; the Scoutmaster's comes from his way.</summary>
    [HarmonyPatch(typeof(CharacterVineClimbing), "Update")]
    private static class BotClimbsVines
    {
        private static void Postfix(Character ___character, PhotonView ___view, ref float ____currentClimbInput, ref float ___syncC, float ___attachVel)
        {
            if (___character == null || ___character.IsLocal || !___character.isBot || !___character.data.isVineClimbing || ___view == null || !___view.IsMine)
            {
                return;
            }
            ____currentClimbInput = vineInput.TryGetValue(___character, out float input) ? input : 0f;
            ___syncC += Time.deltaTime;
            if (___syncC > 0.25f)
            {
                ___syncC = 0f;
                ___view.RPC("RPCA_SyncVineClimb", RpcTarget.Others, ___character.data.vinePercent, ___attachVel);
            }
        }
    }

    private static void Drop(Scoutmaster scoutmaster, Pursuit pursuit)
    {
        if (pursuit.Route != null && scoutmaster != null && scoutmaster.character != null)
        {
            LetGo(scoutmaster.character);
        }
        pursuit.Route = null;
    }

    private static void LetGo(Character him)
    {
        vineInput.Remove(him);
        if (him.data.isRopeClimbing)
        {
            him.refs.ropeHandling.Stop();
        }
        if (him.data.isVineClimbing)
        {
            him.refs.vineClimbing.Stop();
        }
    }

    /// <summary>The way given up: not tried again for a while, and he goes on as before (this frame the game's own chase).</summary>
    private static bool GiveUp(Scoutmaster scoutmaster, Pursuit pursuit, string why)
    {
        pursuit.GivenUp.Add((pursuit.Route.Start, Time.time + GiveUpSeconds));
        GivenUp++;
        Note($"gives up the {pursuit.Route.Way} from {pursuit.Route.Start:F1} ({why}; at {scoutmaster.character.Center:F1})");
        Drop(scoutmaster, pursuit);
        pursuit.Best = float.MaxValue;
        pursuit.BetterAt = Time.time;
        return false;
    }

    private static void NextLeg(Route route, Leg leg)
    {
        route.Leg = leg;
        route.LegBegan = Time.time;
        route.Best = float.MaxValue;
        route.BetterAt = Time.time;
        route.Along = -1f;
        route.MovedAt = Time.time;
        route.ReachUntil = 0f;
    }

    /// <summary>Takes him along his way this frame; false once it is over (or given up), the game's chase then going on.</summary>
    private static bool Steer(Scoutmaster scoutmaster, Character him, Character target, Pursuit pursuit)
    {
        Route route = pursuit.Route;
        float now = Time.time;
        if ((route.Way == Way.Rope && route.Rope == null) || (route.Way == Way.Vine && route.Vine == null))
        {
            return GiveUp(scoutmaster, pursuit, "it is gone");
        }
        switch (route.Leg)
        {
            case Leg.ToIt:
            {
                Vector3 ground = route.Way == Way.HighGround ? route.Start : GroundUnder(route.Start);
                float near = Vector3.Distance(him.Center, ground);
                if (near < route.Best - Progress)
                {
                    route.Best = near;
                    route.BetterAt = now;
                }
                if (now - route.BetterAt > LegPatience)
                {
                    return GiveUp(scoutmaster, pursuit, $"no nearer to it for {LegPatience:0} s, {near:0.0} m off");
                }
                bool across = HelperFunctions.FlatDistance(him.Center, route.Start) < 2.6f;
                if (route.Way == Way.HighGround)
                {
                    if (across && Mathf.Abs(Feet(him).y - route.Start.y) < 1.5f)
                    {
                        Note($"is up on the higher ground at {him.Center:F1}");
                        NextLeg(route, Leg.Off);
                        return true;
                    }
                }
                else
                {
                    float flat = HelperFunctions.FlatDistance(him.Center, route.Start);
                    if (flat < TakeHoldAcross && Vector3.Distance(him.Center, route.Start) < 3f)
                    {
                        scoutmaster.LookAt(route.Start);
                        TakeHold(him, route);
                        Note($"takes hold of the {route.Way} at {him.Center:F1}");
                        NextLeg(route, Leg.OnIt);
                        return true;
                    }
                    if (flat < LastSteps && !him.data.isClimbing)
                    {
                        // The last steps right up to it.
                        scoutmaster.LookAt(route.Start);
                        him.input.movementInput = new Vector2(0f, 1f);
                        return true;
                    }
                }
                Go(scoutmaster, him, route.Way == Way.HighGround ? route.Start : ground + Vector3.up);
                return true;
            }
            case Leg.OnIt:
            {
                if (now - route.LegBegan > 0.5f && !him.data.isRopeClimbing && !him.data.isVineClimbing)
                {
                    return GiveUp(scoutmaster, pursuit, "not on it");
                }
                if (now - route.LegBegan > HoldSeconds)
                {
                    LetGo(him);
                    return GiveUp(scoutmaster, pursuit, $"not at its end in {HoldSeconds:0} s");
                }
                scoutmaster.LookAt(route.End);
                him.data.currentStamina = 1f;
                float along = him.data.isRopeClimbing ? him.data.ropePercent : him.data.vinePercent;
                if (Mathf.Abs(along - route.Along) > 0.002f)
                {
                    route.Along = along;
                    route.MovedAt = now;
                }
                bool stalled = now - route.LegBegan > 1f && now - route.MovedAt > StallSeconds;
                if (stalled && Vector3.Distance(him.Center, route.End) > EndNear)
                {
                    LetGo(him);
                    return GiveUp(scoutmaster, pursuit, $"stuck on it {Vector3.Distance(him.Center, route.End):0.0} m from its end");
                }
                bool there = stalled;
                if (him.data.isRopeClimbing)
                {
                    float percent = him.data.ropePercent;
                    float toGo = route.EndPercent - percent;
                    if (there || Mathf.Abs(toGo) < 0.02f)
                    {
                        Note($"is at the rope's end at {him.Center:F1}");
                        OffTheTop(him, target, route);
                        return true;
                    }
                    // Up or down the rope (the game's input: up is along the rope as it hangs).
                    him.input.movementInput = new Vector2(0f, -Mathf.Sign(toGo) * route.Rope.climbingAPI.UpMult(percent));
                }
                else if (him.data.isVineClimbing)
                {
                    float toGo = route.EndPercent - him.data.vinePercent;
                    if (there || Mathf.Abs(toGo) < 0.02f)
                    {
                        Note($"is at the vine's end at {him.Center:F1}");
                        OffTheTop(him, target, route);
                        return true;
                    }
                    vineInput[him] = Mathf.Sign(toGo);
                }
                return true;
            }
            default:
            {
                // Off the way: on to the scout (off the higher ground, onto the ledge from the hold's end).
                if (Vector3.Distance(him.Center, target.Center) < GrabRange + 1f)
                {
                    Note($"is at {target.characterName} ({him.Center:F1})");
                    Reached++;
                    pursuit.Route = null;
                    pursuit.Best = float.MaxValue;
                    pursuit.BetterAt = now;
                    return false;
                }
                if (route.ReachUntil > 0f && !him.data.isClimbing)
                {
                    if (now < route.ReachUntil)
                    {
                        if (ReachForTheWall(him, target))
                        {
                            Note($"takes hold of the wall above at {him.Center:F1}");
                            route.ReachUntil = 0f;
                        }
                        return true;
                    }
                    Note($"finds no wall above in reach (at {him.Center:F1})");
                    route.ReachUntil = 0f;
                }
                if (now - route.LegBegan > OffSeconds)
                {
                    Note($"did not get to {target.characterName} off the {route.Way} in {OffSeconds:0} s (at {him.Center:F1}, climbing {him.data.isClimbing})");
                    pursuit.Route = null;
                    pursuit.Best = float.MaxValue;
                    pursuit.BetterAt = now;
                    return false;
                }
                Go(scoutmaster, him, target.Head);
                return true;
            }
        }
    }

    /// <summary>The farthest he reaches up for the wall from a hold's top (a scout reaches up from a rope's top the same way), and only from this near under the scout's feet.</summary>
    private const float ReachForWall = 2.6f;
    /// <summary>A hold he stops moving along this near its end is as far as it goes; farther off, it is given up.</summary>
    private const float EndNear = 2.5f;

    /// <summary>
    /// Off a hold's top: he lets go and takes hold of the wall above him toward the scout's feet (the face of their ledge),
    /// if there is one within reach; then on to the scout.
    /// </summary>
    private static void OffTheTop(Character him, Character target, Route route)
    {
        LetGo(him);
        NextLeg(route, Leg.Off);
        route.ReachUntil = Time.time + ReachSeconds;
    }

    /// <summary>Reaching up from a hold's top for the wall toward the scout's feet (their ledge's face), then toward their head: whether he holds a wall now.</summary>
    private static bool ReachForTheWall(Character him, Character target)
    {
        if (Feet(target).y - him.Center.y > ReachForWall)
        {
            return false;
        }
        foreach (Vector3 to in new[] { Feet(target), target.Head })
        {
            Vector3 way = to - him.Center;
            if (way.y > 0.5f && !him.data.isClimbing)
            {
                him.refs.climbing.TryToStartWallClimb(forceAttempt: true, way.normalized, botGrab: true, Mathf.Min(way.magnitude, ReachForWall));
            }
        }
        return him.data.isClimbing;
    }

    /// <summary>
    /// On his ways he keeps hold of a rope however it tilts: the game lets a climber go of a rope tilted past their limit
    /// (his own 65°; meant for a scout's own steering), and a rope bends where it lies on the ground, as where he takes
    /// it. The game's own default, 90°, never lets go.
    /// </summary>
    private const float KeepsHoldAngle = 90f;

    /// <summary>His own way of going somewhere: climbing toward it on a wall (off it if it is below in the open), else walking (and climbing what is in his way).</summary>
    private static void Go(Scoutmaster scoutmaster, Character him, Vector3 to)
    {
        if (him.data.isClimbing)
        {
            scoutmaster.ClimbTowards(to, 1f);
            if (to.y < him.Center.y - 0.5f && HelperFunctions.LineCheck(him.Center, to, HelperFunctions.LayerType.TerrainMap).transform == null)
            {
                him.refs.climbing.StopClimbing();
            }
            return;
        }
        scoutmaster.WalkTowards(to, 1f);
        him.input.sprintIsPressed = Vector3.Distance(him.Center, to) > 4f;
    }

    private static void TakeHold(Character him, Route route)
    {
        if (route.Way == Way.Rope)
        {
            // The nearest hanging segment, by the game's own count (as RopeSegment.Interact grabs).
            Transform nearest = route.Rope.GetRopeSegments().Where(s => s != null && Hangs(s) && OverGround(s.position, route.Rope.transform) > HangsOver)
                .OrderBy(s => Vector3.Distance(s.position, him.Center)).FirstOrDefault();
            if (nearest == null)
            {
                return;
            }
            him.refs.ropeHandling.maxRopeAngle = Mathf.Max(him.refs.ropeHandling.maxRopeAngle, KeepsHoldAngle);
            PhotonView rope = route.Rope.GetComponentInParent<PhotonView>();
            him.photonView.RPC("GrabRopeRpc", RpcTarget.All, rope, Mathf.Clamp(SegmentIndex(nearest), 0, route.Rope.SegmentCount - 1));
        }
        else
        {
            him.photonView.RPC("GrabVineRpc", RpcTarget.All, route.Vine.GetComponent<PhotonView>(), route.Vine.GetClosestChild(him.Center));
            vineInput[him] = 0f;
        }
    }

    /// <summary>How far along a rope (as its climbing goes) the point of it nearest a spot is.</summary>
    private static float NearestPercent(Rope rope, Vector3 spot)
    {
        float best = 0f;
        float nearest = float.MaxValue;
        for (float p = 0f; p <= 1.0001f; p += 0.01f)
        {
            float off = Vector3.Distance(rope.climbingAPI.GetPosition(p), spot);
            if (off < nearest)
            {
                nearest = off;
                best = p;
            }
        }
        return best;
    }

    /// <summary>A rope segment's place along the rope, as the game counts it (RopeSegment.Interact).</summary>
    private static int SegmentIndex(Transform segment) => segment.GetSiblingIndex() - 2;

    private static readonly RaycastHit[] below = new RaycastHit[16];

    /// <summary>How high over the ground a point of a hold is (its own links under it not counted; more than 6 m: as if 6 m).</summary>
    private static float OverGround(Vector3 at, Transform hold)
    {
        int count = Physics.RaycastNonAlloc(at, Vector3.down, below, 6f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore);
        float nearest = 6f;
        for (int i = 0; i < count; i++)
        {
            if (below[i].distance < nearest && (hold == null || !below[i].transform.IsChildOf(hold)))
            {
                nearest = below[i].distance;
            }
        }
        return nearest;
    }

    /// <summary>A rope segment that hangs (within 50° of straight down or up), not one lying on the ground or bent over it: where he takes a rope and climbs it.</summary>
    private const float HangsWithin = 50f;

    private static bool Hangs(Transform segment)
    {
        RopeSegment part = segment.GetComponent<RopeSegment>();
        if (part == null)
        {
            return false;
        }
        float angle = part.GetAngle();
        return angle <= HangsWithin || 180f - angle <= HangsWithin;
    }

    private static Vector3 GroundUnder(Vector3 at)
    {
        return Physics.Raycast(at, Vector3.down, out RaycastHit ground, 6f, HelperFunctions.terrainMapMask) ? ground.point : at + Vector3.down * 6f;
    }

    private static Vector3 Feet(Character scout)
    {
        return Physics.Raycast(scout.Center, Vector3.down, out RaycastHit ground, 2.5f, HelperFunctions.terrainMapMask) ? ground.point : scout.Center + Vector3.down;
    }

    private static bool Tried(Pursuit pursuit, Vector3 at) => pursuit.GivenUp.Any(g => Vector3.Distance(g.at, at) < 2f);

    /// <summary>The shortest way up to the scout he has not given up, or none.</summary>
    private static Route Plan(Character him, Character target, Pursuit pursuit, out string looked)
    {
        List<string> turnedDown = new List<string>();
        int spots = 0;
        float now = Time.time;
        pursuit.GivenUp.RemoveAll(g => g.until < now);
        Vector3 feet = Feet(target);
        Vector3 aim = feet + Vector3.up;
        Route best = null;
        float cheapest = float.MaxValue;

        void Consider(Way way, Rope rope, JungleVine vine, IReadOnlyList<Vector3> points, IReadOnlyList<float> percents)
        {
            Transform hold = rope != null ? rope.transform : vine != null ? vine.transform : null;
            int low = -1;
            int high = -1;
            float lowCost = float.MaxValue;
            float highCost = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                float toHim = Vector3.Distance(him.Center, p);
                float over = OverGround(p, hold);
                if (toHim < WayFar && toHim < lowCost && over > HangsOver && over < ReachUp)
                {
                    low = i;
                    lowCost = toHim;
                }
                // The highest point near the scout: off there he is as high on their ledge's face as the hold goes.
                float toScout = Vector3.Distance(p, aim);
                if (p.y > feet.y - EndUnderFeet && toScout < NearScout && (high < 0 || p.y > points[high].y))
                {
                    high = i;
                    highCost = toScout;
                }
            }
            if (low < 0 || high < 0 || points[high].y < points[low].y + 1.5f || Tried(pursuit, points[low]))
            {
                turnedDown.Add($"{way}: {(low < 0 ? "no low end in his reach" : high < 0 ? "no end near them" : points[high].y < points[low].y + 1.5f ? "its end no higher" : "given up lately")}");
                return;
            }
            float along = 0f;
            for (int i = Mathf.Min(low, high); i < Mathf.Max(low, high); i++)
            {
                along += Vector3.Distance(points[i], points[i + 1]);
            }
            float cost = lowCost + along + highCost;
            if (cost < cheapest)
            {
                cheapest = cost;
                float endPercent = rope != null ? NearestPercent(rope, points[high]) : percents[high];
                best = new Route { Way = way, Rope = rope, Vine = vine, Start = points[low], End = points[high], EndPercent = endPercent };
            }
        }

        foreach (Rope rope in Object.FindObjectsByType<Rope>(FindObjectsSortMode.None))
        {
            if (rope == null || rope.attachmenState != Rope.ATTACHMENT.anchored || !rope.isClimbable || rope.climbingAPI == null)
            {
                continue;
            }
            List<Transform> segments = rope.GetRopeSegments();
            if (segments == null || segments.Count < 3 || Vector3.Distance(segments[0].position, target.Center) > WayFar)
            {
                continue;
            }
            // Along the rope by the game's own count (a segment's place among the rope's children), as a scout's grab counts.
            List<Transform> parts = segments.Where(s => s != null && Hangs(s)).OrderBy(SegmentIndex).ToList();
            if (parts.Count < 3)
            {
                continue;
            }
            Consider(Way.Rope, rope, null, parts.Select(s => s.position).ToList(), parts.Select(s => rope.climbingAPI.GetPercentFromSegmentIndex(Mathf.Clamp(SegmentIndex(s), 0, rope.SegmentCount - 1))).ToList());
        }
        foreach (JungleVine vine in Object.FindObjectsByType<JungleVine>(FindObjectsSortMode.None))
        {
            if (vine == null || vine.GetComponent<PhotonView>() == null || Vector3.Distance(vine.Center(), target.Center) > WayFar)
            {
                continue;
            }
            List<float> percents = new List<float>();
            for (float p = 0.02f; p <= 0.981f; p += 0.02f)
            {
                percents.Add(p);
            }
            Consider(Way.Vine, null, vine, percents.Select(p => vine.GetPosition(p)).ToList(), percents);
        }

        // Higher ground near the scout, with room for him and an open way down to them.
        foreach (float across in DropAcross)
        {
            for (int k = 0; k < 16; k++)
            {
                Vector3 side = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                Vector3 from = target.Center + side * across + Vector3.up * (DropMost + 2f);
                if (!Physics.Raycast(from, Vector3.down, out RaycastHit top, DropMost + 6f, HelperFunctions.terrainMapMask) || top.normal.y < 0.6f)
                {
                    continue;
                }
                float above = top.point.y - feet.y;
                if (above < DropLeast || above > DropMost || Tried(pursuit, top.point)
                    || Physics.CheckSphere(top.point + Vector3.up, 0.4f, HelperFunctions.terrainMapMask)
                    || HelperFunctions.LineCheck(top.point + Vector3.up * 1.2f, target.Center, HelperFunctions.LayerType.TerrainMap).transform != null)
                {
                    continue;
                }
                spots++;
                float cost = Vector3.Distance(him.Center, top.point) + above + GroundExtra;
                if (cost < cheapest)
                {
                    cheapest = cost;
                    best = new Route { Way = Way.HighGround, Start = top.point, End = top.point };
                }
            }
        }
        if (best != null)
        {
            best.LegBegan = now;
            best.BetterAt = now;
        }
        looked = $"{(turnedDown.Count > 0 ? string.Join(", ", turnedDown) : "no rope or vine near")}; higher ground: {spots} spot(s)";
        return best;
    }
}
