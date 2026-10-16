using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 9 (13DDA): the fog moves twice as fast and starts almost at once (20 s instead of
/// waiting for the scouts to move on), from the beach until the Caldera or the Gloom; it changes
/// no fog from there on (with True Final Ascent's FINAL ASCENT, whose Caldera comes before its
/// Gloom, that means until the Caldera).
/// The Kiln's lava and the Citadel's gloom start after 30 seconds and reach the top in 700 seconds;
/// the Nadir's ghosts rise 2.5 times as fast.
/// </summary>
internal static class A09_FogAndKiln
{
    private const float FogWaitSeconds = 20f;

    /// <summary>The fog origins below the Caldera/Gloom are the beach, Tropics/Roots and Alpine/Mesa ones.</summary>
    private const int CalderaFogId = (int)Segment.Caldera;

    /// <summary>The scouts have not reached the Caldera or the Gloom yet (nor anything after them).</summary>
    internal static bool BeforeTheVolcano
    {
        get
        {
            switch (Map.CurrentPlace)
            {
                case Place.Shore:
                case Place.Tropics:
                case Place.Roots:
                case Place.Alpine:
                case Place.Mesa:
                    return true;
                default:
                    return false;
            }
        }
    }

    internal static bool Active(OrbFogHandler handler)
    {
        if (!Rules.Enabled(Lvl.FogAndKiln) || !Ascents.fogEnabled || handler == null || handler.sphere == null || handler.origins == null)
        {
            return false;
        }
        int id = handler.currentID;
        return id >= 0 && id < CalderaFogId && id < handler.origins.Length && handler.origins[id] != null && !handler.origins[id].disableFog && BeforeTheVolcano;
    }

    /// <summary>Fog speed x2 (only the normal fog speeds; never compounds).</summary>
    [HarmonyPatch(typeof(OrbFogHandler), nameof(OrbFogHandler.Move))]
    private static class FogSpeed
    {
        internal struct State
        {
            internal bool changed;
            internal float original;
            internal float applied;
        }

        private static void Prefix(OrbFogHandler __instance, out State __state)
        {
            __state = default;
            if (__instance.speed > 0f && __instance.speed <= 0.4f && Active(__instance))
            {
                __state = new State { changed = true, original = __instance.speed, applied = __instance.speed * 2f };
                __instance.speed = __state.applied;
            }
        }

        private static Exception Finalizer(OrbFogHandler __instance, State __state, Exception __exception)
        {
            if (__state.changed && __instance.speed == __state.applied)
            {
                __instance.speed = __state.original;
            }
            return __exception;
        }
    }

    /// <summary>The fog starts moving 20 seconds after it is set up, on the beach too.</summary>
    [HarmonyPatch(typeof(OrbFogHandler), nameof(OrbFogHandler.TimeToMove))]
    private static class FogWait
    {
        private static void Postfix(OrbFogHandler __instance, ref bool __result)
        {
            if (Active(__instance))
            {
                __result = __instance.currentWaitTime > FogWaitSeconds;
            }
        }
    }

    /// <summary>Kiln lava and Citadel gloom: 30 s wait, 700 s rise. The Nadir ghosts rise 2.5x as fast.</summary>
    [HarmonyPatch(typeof(LavaRising), nameof(LavaRising.Update))]
    private static class FasterRisingHazards
    {
        private static void Prefix(LavaRising __instance)
        {
            if (!Rules.Enabled(Lvl.FogAndKiln) || !Map.Ready || Map.CurrentSegment != __instance.requiredSegment)
            {
                return;
            }
            bool kilnLava = __instance.risingFieldType == LavaRising.RisingFieldType.Lava && __instance.requiredSegment == Segment.TheKiln && Map.InVolcanoKiln;
            bool citadelGloom = __instance.risingFieldType == LavaRising.RisingFieldType.Gloom && __instance.requiredSegment == Segment.TheKiln;
            bool nadirGhosts = __instance.risingFieldType == LavaRising.RisingFieldType.VoidGhosts && __instance.requiredSegment == Segment.Void;
            if (!kilnLava && !citadelGloom && !nadirGhosts)
            {
                return;
            }
            float original = Overrides.Original(__instance, "travelTime", __instance.travelTime);
            if (original <= 0f || float.IsNaN(original) || float.IsInfinity(original))
            {
                return;
            }
            float travel = nadirGhosts ? original / 2.5f : Mathf.Min(700f, original);
            if (!nadirGhosts && __instance.initialWaitTime != 30f)
            {
                Overrides.Set(__instance, nameof(LavaRising.initialWaitTime), Mathf.Min(30f, Overrides.Original(__instance, "initialWaitTime", __instance.initialWaitTime)));
            }
            if (__instance.travelTime != travel)
            {
                Overrides.Set(__instance, nameof(LavaRising.travelTime), travel);
                Plugin.Debug($"Ascent 9: {__instance.risingFieldType} in {__instance.requiredSegment} now rises in {travel:F0} s.");
            }
        }
    }
}
