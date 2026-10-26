using System;
using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// On every ascent: a hill, rock or wall between a scout and an oncoming snowstorm or wind storm shelters them, in the
/// lowlands too. The game (and True Final Ascent on its own terrain) only asks how open the sky above a
/// scout is, so on open low ground a hill on the storm's side gave no shelter. Now ground that rises
/// steeply enough on the side the storm comes from (seen from the head at least 12° up, fully from 25° up,
/// within 60 m, across 20° to either side of the wind) shelters as well. It only ever adds shelter:
/// standing in the open, with nothing on the storm's side, a scout is exactly as exposed as the game (or
/// TFA) says. The shelter takes off the storm's cold and its push alike (the wind's push and its hold on a climber's
/// stamina). Items lying in a wind storm (a backpack put down, food, anything on the ground) are sheltered as a scout
/// would be where they lie: the game pushed every one at full strength, under a roof or behind a rock as much as in the
/// open. Only snowstorms and wind storms (the game's, and ascent 25's), not True Final Ascent's own weather.
/// </summary>
internal static class SnowstormShelter
{
    private const float Reach = 60f;

    /// <summary>Ground seen this high (degrees above level, from the head) on the storm's side gives no shelter yet...</summary>
    private const float ShelterFrom = 12f;

    /// <summary>...and from this high full shelter.</summary>
    private const float FullShelterFrom = 25f;

    /// <summary>The heights looked at, from the lowest up (a slope that only rises gently does not count).</summary>
    private static readonly float[] Heights = { 6f, 12f, 18f, 25f, 35f };

    /// <summary>The directions looked at, around the one the storm comes from.</summary>
    private static readonly float[] Sides = { -20f, -10f, 0f, 10f, 20f };

    /// <summary>How long one look holds while the scout stays within a metre and the wind keeps its direction.</summary>
    private const float LookSeconds = 0.25f;

    private static WindChillZone storm;
    private static Character exposed;

    private static float lookedAt = float.NegativeInfinity;
    private static Vector3 lookedFrom;
    private static Vector3 lookedUpwind;
    private static float lookedOpen = 1f;

    private static bool On => Plugin.Ready && Rules.InRun;

    /// <summary>How open the land on the storm's side was at the last look, 0 (sheltered) to 1 (open); for the tests.</summary>
    internal static float LastOpen { get; private set; } = 1f;

    /// <summary>The exposure the game (or TFA) gave at the last look, before this rule; for the tests.</summary>
    internal static float LastGame { get; private set; } = 1f;

    /// <summary>
    /// How open the land is on the side a wind blowing along <paramref name="wind"/> comes from, as seen from
    /// <paramref name="eye"/>: 1 with nothing there, 0 behind a hill, rock or wall that rises at least 25° up.
    /// </summary>
    internal static float Openness(Vector3 eye, Vector3 wind)
    {
        Vector3 upwind = Vector3.ProjectOnPlane(-wind, Vector3.up);
        if (upwind.sqrMagnitude < 0.0001f)
        {
            return 1f;
        }
        upwind.Normalize();
        float shelter = 0f;
        foreach (float side in Sides)
        {
            Vector3 toward = Quaternion.AngleAxis(side, Vector3.up) * upwind;
            // The ground's height on this side: the highest look that is still blocked, every lower one blocked too.
            float seen = 0f;
            foreach (float height in Heights)
            {
                float angle = height * Mathf.Deg2Rad;
                Vector3 ray = toward * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
                if (!Physics.Raycast(eye, ray, Reach, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
                {
                    break;
                }
                seen = height;
            }
            shelter += Mathf.InverseLerp(ShelterFrom, FullShelterFrom, seen);
        }
        return 1f - shelter / Sides.Length;
    }

    private static float Look(Character character, Vector3 wind)
    {
        Vector3 eye = character.Head;
        Vector3 upwind = Vector3.ProjectOnPlane(-wind, Vector3.up).normalized;
        if (Time.time - lookedAt >= LookSeconds || (eye - lookedFrom).sqrMagnitude > 1f || Vector3.Dot(upwind, lookedUpwind) < 0.999f)
        {
            lookedAt = Time.time;
            lookedFrom = eye;
            lookedUpwind = upwind;
            lookedOpen = Openness(eye, wind);
        }
        return lookedOpen;
    }

    /// <summary>A storm whose cold and push the land shelters from: a snowstorm or a wind storm (the game's, or ascent 25's).</summary>
    private static bool Shelters(WindChillZone zone) => zone != null && (zone.name == "SnowStorm" || zone.name == "WindStorm");

    /// <summary>How long an item's look holds while it stays within a metre and the wind keeps its direction (items mostly lie still).</summary>
    private const float ItemLookSeconds = 1f;

    /// <summary>The look from just above an item's middle (out of the ground it lies on).</summary>
    private const float ItemEyeLift = 0.3f;

    private struct ItemLook
    {
        internal float at;
        internal Vector3 from;
        internal Vector3 upwind;
        internal float open;
    }

    private static readonly Dictionary<int, ItemLook> itemLooks = new Dictionary<int, ItemLook>();

    /// <summary>How open the land on the storm's side of an item is (see <see cref="Openness"/>), looked at again only now and then.</summary>
    private static float ItemOpenness(Item item, Vector3 eye, Vector3 wind)
    {
        int id = item.GetInstanceID();
        Vector3 upwind = Vector3.ProjectOnPlane(-wind, Vector3.up).normalized;
        if (itemLooks.TryGetValue(id, out ItemLook look) && Time.time - look.at < ItemLookSeconds && (eye - look.from).sqrMagnitude <= 1f && Vector3.Dot(upwind, look.upwind) >= 0.999f)
        {
            return look.open;
        }
        if (itemLooks.Count > 2048)
        {
            itemLooks.Clear();
        }
        look = new ItemLook { at = Time.time, from = eye, upwind = upwind, open = Openness(eye, wind) };
        itemLooks[id] = look;
        return look.open;
    }

    /// <summary>
    /// How exposed an item lying in a storm is, 0 (sheltered) to 1, as a scout would be there: the game's own measure of
    /// how open the sky is where it lies, the land on the storm's side, and a wall right upwind of it (the game's push on
    /// a scout weakens there too).
    /// </summary>
    internal static float ItemExposure(WindChillZone zone, Item item)
    {
        Vector3 at = item.Center();
        float open = WindChillZone.GetWindIntensityAtPoint(at, zone.lightVolumeSampleThreshold_lower, zone.lightVolumeSampleThreshold_margin);
        if (open <= 0f)
        {
            return 0f;
        }
        open = Mathf.Min(open, ItemOpenness(item, at + Vector3.up * ItemEyeLift, zone.currentWindDirection));
        if (zone.useRaycast && Physics.Raycast(at, -zone.currentWindDirection, out RaycastHit wall, zone.maxRaycastDistance, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            float shielded = Mathf.InverseLerp(zone.minRaycastDistance, zone.maxRaycastDistance, wall.distance) + 1f - Mathf.Clamp01(Vector3.Dot(wall.normal, zone.currentWindDirection));
            open *= Mathf.Clamp01(shielded);
        }
        return open;
    }

    /// <summary>For the tests: how many item pushes were taken off or weakened by shelter.</summary>
    internal static int ItemsSheltered;

    /// <summary>For the tests: an item to follow, how often it was pushed and how exposed it was at the last push.</summary>
    internal static Item Watched;
    internal static int WatchedPushes;
    internal static float WatchedOpen = -1f;

    /// <summary>An item lying in a wind storm is pushed only as hard as it is exposed (the game: always at full strength).</summary>
    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.AddWindForceToItem))]
    private static class ItemsInShelter
    {
        private static bool Prefix(WindChillZone __instance, Item item)
        {
            if (!On || !Shelters(__instance) || item == null || item.rig == null)
            {
                return true;
            }
            float open = ItemExposure(__instance, item);
            if (open < 0.999f)
            {
                ItemsSheltered++;
            }
            if (item == Watched)
            {
                WatchedPushes++;
                WatchedOpen = open;
            }
            if (open > 0f)
            {
                float intensity = __instance.useIntensityCurve ? Mathf.Clamp01(__instance.windIntensity - 0.5f) * 2f : 1f;
                item.rig.AddForce(__instance.currentWindDirection * (__instance.windForce * __instance.windItemFactor * intensity * open), ForceMode.Acceleration);
            }
            return false;
        }
    }

    /// <summary>Which snowstorm or wind storm is asking how exposed which scout is (the game, or ascent 18's own storm rule, asks within).</summary>
    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.ApplyStatus))]
    private static class Asking
    {
        // Before ascent 18's storm rule, which asks itself and skips the game's method.
        [HarmonyPriority(Priority.First)]
        private static void Prefix(WindChillZone __instance, Character character)
        {
            bool shelters = Shelters(__instance);
            storm = shelters ? __instance : null;
            exposed = shelters ? character : null;
        }

        private static Exception Finalizer(Exception __exception)
        {
            storm = null;
            exposed = null;
            return __exception;
        }
    }

    /// <summary>After the game's answer (or TFA's, which replaces it on its own terrain): the land on the storm's side shelters too.</summary>
    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.GetWindIntensityAtPoint))]
    private static class LandShelters
    {
        private static void Postfix(ref float __result)
        {
            if (storm == null || exposed == null || __result <= 0f || !On)
            {
                return;
            }
            LastGame = __result;
            LastOpen = Look(exposed, storm.currentWindDirection);
            __result = Mathf.Min(__result, LastOpen);
        }
    }
}
