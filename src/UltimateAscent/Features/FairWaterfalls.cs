using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Waterfalls, on every ascent (the game's hitboxes are a common complaint):
/// <list type="bullet">
/// <item>The falling water (the game's WaterfallTrigger: capsules one after another down each stream) knocks a scout down
/// for 0.5 s when it hits them (and again if they are up while still in it) and meanwhile washes them out of it at a
/// steady pace (pushed only as hard as that takes), sideways where there is room, else down the stream, the same anywhere
/// in the water. The game knocked them
/// down again and braked them every physics tick while any part of them was
/// in, and shoved each part on its own, the harder the further it was from the hitbox's anchor: they were held fast, or
/// flung. Only the scout's own game does it (the game did it on every game, to everyone in it).</item>
/// <item>The splash at the foot (the game's WaterfallPusher, about 11 m wide and tall) throws a scout only where the
/// water lands, within 3 m of its middle, and half as hard; the game threw anyone stepping anywhere into it.</item>
/// </list>
/// </summary>
internal static class FairWaterfalls
{
    /// <summary>For the tests: the game's waterfalls again (to compare).</summary>
    internal static bool Off;

    /// <summary>The water hitting a scout knocks them down this long (the game's own).</summary>
    internal const float KnockDownSeconds = 0.5f;
    /// <summary>Out of the water this long, the next touch is a new hit (knocks down again).</summary>
    internal const float NewHitAfter = 0.5f;
    /// <summary>
    /// In the water, a scout is washed out of it sideways at about this pace (m/s), and down it at this pace: pushed only
    /// as hard as it takes to keep them moving so (at most MaxPush, m/s², every part), never harder (nobody is flung).
    /// </summary>
    internal const float WashOutSpeed = 6f;
    internal const float WashDownSpeed = 2f;
    internal const float MaxPush = 120f;
    private const float PushGain = 30f;
    /// <summary>The splash throws a scout only within this far (sideways) of where the water lands, this hard sideways (the game: 500 anywhere in it).</summary>
    internal const float SplashReach = 3f;
    internal const float SplashThrow = 250f;

    /// <summary>For the tests: knock-downs and splash throws given; how often the falling water touched anything, and a scout this game moves.</summary>
    internal static int Hits;
    internal static int Throws;
    internal static int StreamTouches;
    internal static int StreamScoutTouches;

    private sealed class Wet
    {
        internal float lastInWater = float.NegativeInfinity;
        internal float lastPushed = float.NegativeInfinity;
        internal float lastThrown = float.NegativeInfinity;
        /// <summary>The way out of the water picked when it hit (kept while it stays open), and whether that is down the stream.</summary>
        internal Vector3 wayOut;
        internal bool downStream;
    }

    /// <summary>A way out is open when nothing solid lies this near along it (from a little above the scout's middle, this wide).</summary>
    private const float WayReach = 1.5f;
    private const float WayWidth = 0.2f;

    private static bool Blocked(Character scout, Vector3 way)
    {
        return Physics.SphereCast(scout.Center + Vector3.up * 0.25f, WayWidth, way, out _, WayReach, HelperFunctions.terrainMapMask);
    }

    private static readonly ConditionalWeakTable<Character, Wet> wet = new ConditionalWeakTable<Character, Wet>();

    /// <summary>A capsule hitbox as it lies in the world: its middle, the way it runs (downhill) and how far its line reaches each way from the middle.</summary>
    internal static bool Line(Collider collider, out Vector3 middle, out Vector3 down, out float half)
    {
        middle = collider.bounds.center;
        down = Vector3.down;
        half = 0f;
        if (!(collider is CapsuleCollider capsule))
        {
            return false;
        }
        Transform t = capsule.transform;
        Vector3 s = t.lossyScale;
        middle = t.TransformPoint(capsule.center);
        down = (capsule.direction == 0 ? t.right : capsule.direction == 2 ? t.forward : t.up).normalized;
        float along = Mathf.Abs(capsule.direction == 0 ? s.x : capsule.direction == 2 ? s.z : s.y);
        float across = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)) : capsule.direction == 2 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)) : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        half = Mathf.Max(0f, capsule.height * along * 0.5f - capsule.radius * across);
        if (down.y > 0f)
        {
            down = -down;
        }
        return true;
    }

    [HarmonyPatch(typeof(WaterfallTrigger), "OnTriggerStay")]
    private static class Stream
    {
        private static bool Prefix(WaterfallTrigger __instance, Collider other)
        {
            StreamTouches++;
            Character scout = other.GetComponentInParent<Character>();
            bool ours = scout != null && scout.photonView != null && scout.photonView.IsMine && !scout.data.dead;
            if (ours)
            {
                StreamScoutTouches++;
            }
            if (Off)
            {
                return true;
            }
            if (ours)
            {
                InStream(__instance, scout);
            }
            return false;
        }
    }

    /// <summary>Once a physics tick however many of a scout's parts (and hitboxes) are in: knocked down when new to the water, pushed down and out.</summary>
    private static void InStream(WaterfallTrigger stream, Character scout)
    {
        Wet state = wet.GetOrCreateValue(scout);
        float now = Time.fixedTime;
        if (state.lastPushed == now)
        {
            return;
        }
        state.lastPushed = now;
        // Knocked down when the water hits them, and again if they are up while still in it: only a scout who is down can
        // be washed out (standing, the game's own drag soaks up any push), but not every tick, as the game did.
        bool newHit = now - state.lastInWater > NewHitAfter;
        if (newHit || scout.data.fallSeconds <= 0f)
        {
            scout.Fall(KnockDownSeconds);
            Hits++;
        }
        state.lastInWater = now;
        // Out sideways from the nearest point of the stream's line (where they stand on the rock under it too), and down
        // the stream.
        Line(stream.GetComponent<Collider>(), out Vector3 middle, out Vector3 down, out float half);
        Vector3 nearest = middle + down * Mathf.Clamp(Vector3.Dot(scout.Center - middle, down), -half, half);
        Vector3 off = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(scout.Center - nearest, down), Vector3.up);
        Vector3 aside = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(down, Vector3.up));
        Vector3 outward = off.sqrMagnitude > 1e-4f ? off.normalized : aside.sqrMagnitude > 1e-4f ? aside.normalized : Vector3.forward;
        // The way out: sideways (the nearer side first), else the other side, else down the stream (along the channel it
        // runs in), else up: the first that is open (a stream often runs in a groove in the rock, its sides solid).
        if (newHit || state.wayOut == Vector3.zero || Blocked(scout, state.wayOut))
        {
            state.wayOut = Vector3.up;
            state.downStream = false;
            foreach (Vector3 way in new[] { outward, -outward, down })
            {
                if (!Blocked(scout, way))
                {
                    state.wayOut = way;
                    state.downStream = way == down;
                    break;
                }
            }
        }
        Vector3 velocity = scout.data.avarageVelocity;
        Vector3 push = state.wayOut * Mathf.Clamp((WashOutSpeed - Vector3.Dot(velocity, state.wayOut)) * PushGain, 0f, MaxPush);
        if (!state.downStream)
        {
            push += down * Mathf.Clamp((WashDownSpeed - Vector3.Dot(velocity, down)) * PushGain, 0f, MaxPush);
        }
        scout.AddForce(push);
    }

    [HarmonyPatch(typeof(WaterfallPusher), "OnTriggerEnter")]
    private static class Splash
    {
        private static bool Prefix(WaterfallPusher __instance)
        {
            if (Off)
            {
                return true;
            }
            // Where the water lands is checked while a scout is in the splash (from where they stepped in, they may walk into it).
            if (__instance.GetComponent<SplashCore>() == null)
            {
                __instance.gameObject.AddComponent<SplashCore>().pusher = __instance;
            }
            return false;
        }
    }

    /// <summary>The splash's throw, only where the water lands (on the scout's own game, as the game's).</summary>
    internal sealed class SplashCore : MonoBehaviour
    {
        internal WaterfallPusher pusher;

        private void OnTriggerStay(Collider other)
        {
            if (Off || pusher == null)
            {
                return;
            }
            Character scout = other.GetComponentInParent<Character>();
            if (scout == null || !scout.IsLocal || scout.data.dead)
            {
                return;
            }
            Line(pusher.GetComponent<Collider>(), out Vector3 middle, out _, out _);
            Vector3 flat = scout.Center - middle;
            flat.y = 0f;
            if (flat.magnitude > SplashReach)
            {
                return;
            }
            Wet state = wet.GetOrCreateValue(scout);
            if (Time.time < state.lastThrown + pusher.cooldown)
            {
                return;
            }
            state.lastThrown = Time.time;
            if (pusher.fallTime > 0f)
            {
                scout.Fall(pusher.fallTime);
            }
            if (pusher.sfx != null)
            {
                pusher.sfx.Play();
            }
            if (GamefeelHandler.instance != null)
            {
                GamefeelHandler.instance.AddPerlinShake(30f, 0.8f, 20f);
            }
            // Aimed as the game aims it (away from the splash, its height left out), so it is half the game's throw from anywhere.
            Vector3 away = (scout.Center - transform.position).normalized;
            away.y = 0f;
            scout.AddForce(away * SplashThrow + Vector3.down * pusher.downwardKnockback, 0.7f, 1.3f);
            Throws++;
        }
    }
}
