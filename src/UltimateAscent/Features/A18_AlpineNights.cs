using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 18: in the Alpine the nights are longer. The first and the last quarter of the day are
/// dark, and the night cold (with ascent 18's harsher rate) applies then too.
/// <para>Only the sky and the cold change. The sky squeezes the whole daylight into the bright
/// middle half of the day and stretches the night over the two dark quarters, so dawn and dusk stay
/// smooth; it fades between the normal and the Alpine sky over a few seconds when the climb enters
/// or leaves the Alpine. The clock itself, the day count and everything else that asks for the time
/// keep the real time.</para>
/// </summary>
internal static class A18_AlpineNights
{
    private const float BlendSeconds = 3f;

    private static float blend;

    internal static void Reset()
    {
        blend = 0f;
    }

    private static bool Wanted => Rules.Enabled(Lvl.ExtremeWeather) && Map.InAlpine;

    /// <summary>It is one of the day's dark quarters and the climb is in the Alpine.</summary>
    internal static bool DarkQuarterNow(DayNightManager clock)
    {
        return clock != null && Wanted && InDarkQuarter(clock.timeOfDay, clock.dayStart, clock.dayEnd);
    }

    /// <summary>The first or the last quarter of the day (the day runs from dayStart to dayEnd, in hours).</summary>
    internal static bool InDarkQuarter(float time, float dayStart, float dayEnd)
    {
        float quarter = (dayEnd - dayStart) / 4f;
        if (quarter <= 0f)
        {
            return false;
        }
        time = Mathf.Repeat(time, 24f);
        return (time >= dayStart && time < dayStart + quarter) || (time >= dayEnd - quarter && time < dayEnd);
    }

    /// <summary>
    /// The time of day the Alpine sky shows: daylight is squeezed into the middle half of the day,
    /// the night is stretched over the two dark quarters around the real night. Continuous everywhere.
    /// </summary>
    internal static float SkyTime(float time, float dayStart, float dayEnd)
    {
        float day = dayEnd - dayStart;
        float night = 24f - day;
        if (day <= 0f || night <= 0f)
        {
            return time;
        }
        float quarter = day / 4f;
        float brightStart = dayStart + quarter;
        float brightEnd = dayEnd - quarter;
        time = Mathf.Repeat(time, 24f);
        if (time >= brightStart && time < brightEnd)
        {
            return dayStart + (time - brightStart) * day / (brightEnd - brightStart);
        }
        float sinceDusk = Mathf.Repeat(time - brightEnd, 24f);
        return Mathf.Repeat(dayEnd + sinceDusk * night / (night + 2f * quarter), 24f);
    }

    /// <summary>The sky (sun, moon, colours, the shaders' day flag) follows <see cref="SkyTime"/> in the Alpine.</summary>
    [HarmonyPatch(typeof(DayNightManager), nameof(DayNightManager.UpdateCycle))]
    private static class Sky
    {
        private static void Prefix(DayNightManager __instance, out float? __state)
        {
            __state = null;
            if (!Application.isPlaying)
            {
                return;
            }
            blend = Mathf.MoveTowards(blend, Wanted ? 1f : 0f, Time.deltaTime / BlendSeconds);
            if (blend <= 0f)
            {
                return;
            }
            float real = __instance.timeOfDay;
            float sky = SkyTime(real, __instance.dayStart, __instance.dayEnd);
            // The shorter way round the clock (15 degrees per hour).
            float hours = Mathf.DeltaAngle(real * 15f, sky * 15f) / 15f;
            __state = real;
            __instance.timeOfDay = Mathf.Repeat(real + hours * blend, 24f);
        }

        private static Exception Finalizer(DayNightManager __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.timeOfDay = __state.Value;
            }
            return __exception;
        }
    }
}
