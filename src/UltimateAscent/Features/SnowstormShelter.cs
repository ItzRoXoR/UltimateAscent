using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// From ascent 9: a hill, rock or wall between a scout and an oncoming snowstorm shelters them, in the
/// lowlands too. The game (and True Final Ascent on its own terrain) only asks how open the sky above a
/// scout is, so on open low ground a hill on the storm's side gave no shelter. Now ground that rises
/// steeply enough on the side the storm comes from (seen from the head at least 12° up, fully from 25° up,
/// within 60 m, across 20° to either side of the wind) shelters as well. It only ever adds shelter:
/// standing in the open, with nothing on the storm's side, a scout is exactly as exposed as the game (or
/// TFA) says. Only snowstorms (the game's, and ascent 25's), not True Final Ascent's own weather.
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

    private static bool On => Rules.InExtraAscent;

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

    /// <summary>Which snowstorm is asking how exposed which scout is (the game, or ascent 18's own storm rule, asks within).</summary>
    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.ApplyStatus))]
    private static class Asking
    {
        // Before ascent 18's storm rule, which asks itself and skips the game's method.
        [HarmonyPriority(Priority.First)]
        private static void Prefix(WindChillZone __instance, Character character)
        {
            bool snow = __instance != null && __instance.name == "SnowStorm";
            storm = snow ? __instance : null;
            exposed = snow ? character : null;
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
