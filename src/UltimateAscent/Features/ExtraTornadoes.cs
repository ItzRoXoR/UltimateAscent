using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 25: the host spawns extra tornadoes near a random living scout (first after 5 s, then
/// every 40-140 s) below the Kiln. They wander around the scouts instead of following the
/// Mesa's tornado paths, and stay inside the map's invisible walls.
/// </summary>
internal static class ExtraTornadoes
{
    internal const string Marker = "UltimateAscent.tornado";

    private static float untilNext = 5f;

    internal static void Reset()
    {
        untilNext = 5f;
    }

    internal static bool IsCustom(Tornado tornado)
    {
        PhotonView view = tornado.view != null ? tornado.view : tornado.GetComponent<PhotonView>();
        object[] data = view != null ? view.InstantiationData : null;
        return data != null && data.Length > 0 && data[0] as string == Marker;
    }

    /// <summary>Host only; called every frame.</summary>
    internal static void Tick()
    {
        if (!Rules.Enabled(Lvl.AllWeather) || !Rules.Hazard(RunSettings.SETTINGTYPE.Hazard_Tornado) || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || A25_AllWeather.Excluded || LoadingScreenHandler.loading || !MapHandler.ExistsAndInitialized)
        {
            return;
        }
        untilNext -= Time.deltaTime;
        if (untilNext > 0f)
        {
            return;
        }
        untilNext = 5f;
        List<Character> candidates = new List<Character>();
        foreach (Character character in Character.AllCharacters)
        {
            if (character != null && !character.isBot && !character.isScoutmaster && character.data != null && !character.data.dead)
            {
                candidates.Add(character);
            }
        }
        while (candidates.Count > 0)
        {
            int index = Random.Range(0, candidates.Count);
            Character anchor = candidates[index];
            candidates.RemoveAt(index);
            if (!Boundary.TrySpawn(anchor.Center, out Vector3 position))
            {
                continue;
            }
            GameObject tornado = PhotonNetwork.Instantiate("Tornado", position, Quaternion.identity, 0, new object[] { Marker });
            PhotonView view = tornado.GetComponent<PhotonView>();
            view.RPC("RPCA_InitTornado", RpcTarget.All, view.ViewID);
            untilNext = Random.Range(40f, 140f);
            Plugin.Debug($"Ascent 25: spawned an extra tornado near {anchor.characterName}.");
            return;
        }
    }

    /// <summary>Extra tornadoes do not pick targets from the Mesa's tornado paths.</summary>
    [HarmonyPatch(typeof(Tornado), nameof(Tornado.TargetSelection))]
    private static class NoPathTargets
    {
        private static bool Prefix(Tornado __instance) => !IsCustom(__instance);
    }

    /// <summary>Extra tornadoes wander between random nearby points, clipped to the map walls.</summary>
    [HarmonyPatch(typeof(Tornado), nameof(Tornado.Movement))]
    private static class Wander
    {
        private sealed class State
        {
            internal Vector3 target;
            internal float remaining;
        }

        private static readonly ConditionalWeakTable<Tornado, State> states = new ConditionalWeakTable<Tornado, State>();

        private static bool Prefix(Tornado __instance)
        {
            if (!IsCustom(__instance))
            {
                return true;
            }
            __instance.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            State state = states.GetOrCreateValue(__instance);
            if (__instance.view != null && __instance.view.IsMine)
            {
                state.remaining -= Time.deltaTime;
                if (state.remaining <= 0f)
                {
                    Boundary.TryTarget(__instance.tornadoPos, out state.target);
                    state.remaining = 5f;
                }
                __instance.vel = FRILerp.Lerp(__instance.vel, (state.target - __instance.tornadoPos).Flat().normalized * 15f, 0.15f);
            }
            __instance.tornadoPos = Boundary.Step(__instance.tornadoPos, __instance.tornadoPos + __instance.vel * Time.deltaTime * 0.5f, Time.deltaTime, out bool blocked);
            if (blocked)
            {
                state.remaining = 0f;
                __instance.vel = Vector3.zero;
            }
            __instance.transform.position = __instance.tornadoPos;
            return false;
        }
    }

    /// <summary>Extra tornadoes vanish once the scouts reach the Kiln.</summary>
    [HarmonyPatch(typeof(Tornado), nameof(Tornado.Update))]
    private static class LeaveAtTheKiln
    {
        private static bool Prefix(Tornado __instance)
        {
            if (!A25_AllWeather.Excluded || !IsCustom(__instance))
            {
                return true;
            }
            PhotonView view = __instance.view != null ? __instance.view : __instance.GetComponent<PhotonView>();
            if (view != null && view.IsMine)
            {
                PhotonNetwork.Destroy(__instance.gameObject);
            }
            else
            {
                __instance.gameObject.SetActive(false);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Tornado), nameof(Tornado.FixedUpdate))]
    private static class NoPullAtTheKiln
    {
        private static bool Prefix(Tornado __instance) => !(A25_AllWeather.Excluded && IsCustom(__instance));
    }

    /// <summary>Keeps extra tornadoes on the ground and inside the invisible map walls.</summary>
    internal static class Boundary
    {
        private const float WallClearance = 12f;
        private static readonly int WallMask = LayerMask.GetMask("InvisWall");
        private static readonly int GroundMask = LayerMask.GetMask("Terrain", "Map");

        private static Vector3 ClipToWalls(Vector3 origin, Vector3 desired)
        {
            Vector3 delta = desired - origin;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.001f)
            {
                return origin;
            }
            Vector3 direction = delta / distance;
            Vector3 start = origin + Vector3.up * 2f;
            float allowed = distance;
            if (Physics.Raycast(start, direction, out RaycastHit hit, distance + WallClearance, WallMask, QueryTriggerInteraction.Collide))
            {
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - WallClearance));
            }
            if (Physics.SphereCast(start, WallClearance, direction, out RaycastHit sphereHit, distance, WallMask, QueryTriggerInteraction.Collide))
            {
                allowed = Mathf.Min(allowed, Mathf.Max(0f, sphereHit.distance - 0.1f));
            }
            return origin + direction * allowed;
        }

        private static bool TryGround(Vector3 point, out Vector3 ground)
        {
            if (Physics.Raycast(point + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 300f, GroundMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.point.y - point.y) <= 100f)
            {
                ground = hit.point;
                return true;
            }
            ground = point;
            return false;
        }

        private static bool TryRandom(Vector3 origin, float spread, float minDistance, int attempts, out Vector3 position)
        {
            for (int i = 0; i < attempts; i++)
            {
                Vector3 desired = origin + new Vector3(Random.Range(-spread, spread), 0f, Random.Range(-spread, spread));
                if (TryGround(ClipToWalls(origin, desired), out position))
                {
                    Vector3 offset = position - origin;
                    offset.y = 0f;
                    if (offset.sqrMagnitude >= minDistance * minDistance)
                    {
                        return true;
                    }
                }
            }
            position = origin;
            return false;
        }

        internal static bool TrySpawn(Vector3 anchor, out Vector3 position) => TryRandom(anchor, 200f, 30f, 12, out position);

        internal static bool TryTarget(Vector3 current, out Vector3 position) => TryRandom(current, 150f, 2f, 8, out position);

        internal static Vector3 Step(Vector3 current, Vector3 desired, float deltaTime, out bool blocked)
        {
            Vector3 next = ClipToWalls(current, desired);
            blocked = (next - desired).sqrMagnitude > 1E-06f;
            if (!TryGround(next, out Vector3 ground))
            {
                blocked = true;
                return current;
            }
            next.y = Mathf.Lerp(current.y, ground.y, 0.5f * deltaTime);
            return next;
        }
    }
}
