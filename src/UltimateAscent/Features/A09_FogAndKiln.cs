using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 9 (13DDA): the fog moves twice as fast and starts almost at once (20 s instead of
/// waiting for the scouts to move on), from the beach until the Caldera or the Gloom; it changes
/// no fog from there on. With True Final Ascent's FINAL ASCENT it goes wherever TFA's fog goes
/// (its Builder sends the fog through every block).
/// The Kiln's lava and the Citadel's gloom start after 30 seconds and reach the top in 700 seconds
/// (a taller one, built by TFA's Builder, rises as fast, so it takes longer);
/// the Nadir's ghosts rise 2.5 times as fast.
/// </summary>
internal static class A09_FogAndKiln
{
    private const float FogWaitSeconds = 20f;

    /// <summary>The Kiln's lava and the Citadel's gloom reach the top in this long (the game: 1,200 s).</summary>
    internal const float RiseSeconds = 700f;
    internal const float GameRiseSeconds = 1200f;

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
        if (id < 0 || id >= handler.origins.Length || handler.origins[id] == null || handler.origins[id].disableFog)
        {
            return false;
        }
        // Wherever True Final Ascent's FINAL ASCENT sends the fog (its Builder: through every block, the Caldera, the Gloom
        // and the Nadir too), this rule goes along with it. The fog's number there is TFA's segment, not the biome's.
        return Compat.Tfa.FinalAscentActive || (id < CalderaFogId && BeforeTheVolcano);
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
            // Only the scouts' own part of the map rises (True Final Ascent's Builder: its segment, which may hold several
            // blocks, so the place tells which hazard is theirs).
            if (!Rules.Enabled(Lvl.FogAndKiln) || !Map.Ready || Map.CurrentSegment != __instance.requiredSegment)
            {
                return;
            }
            bool kilnLava = __instance.risingFieldType == LavaRising.RisingFieldType.Lava && Map.InVolcanoKiln;
            bool citadelGloom = __instance.risingFieldType == LavaRising.RisingFieldType.Gloom && Map.InCitadel;
            bool nadirGhosts = __instance.risingFieldType == LavaRising.RisingFieldType.VoidGhosts && Map.InNadir;
            if (!kilnLava && !citadelGloom && !nadirGhosts)
            {
                return;
            }
            float original = Overrides.Original(__instance, "travelTime", __instance.travelTime);
            if (original <= 0f || float.IsNaN(original) || float.IsInfinity(original))
            {
                return;
            }
            // 700 s where the game takes 1,200 s; a Kiln or Citadel the Builder made taller rises longer, at the same pace.
            float travel = nadirGhosts ? original / 2.5f : original * RiseSeconds / GameRiseSeconds;
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
