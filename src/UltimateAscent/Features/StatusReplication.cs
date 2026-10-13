using System;
using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// When the game copies a character's absolute statuses over the network it replays them through
/// AddStatus. Status multipliers must not run again on those copies, and the received values
/// are written back exactly.
/// </summary>
[HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.SetAll))]
internal static class StatusReplication
{
    [ThreadStatic] private static int depth;

    internal static bool Active => depth > 0;

    private static bool Relevant => Rules.Enabled(Lvl.WorseItems) || Rules.Enabled(Lvl.StrongerAilments) || Rules.Enabled(Lvl.Hazards);

    private static void Prefix(out bool __state)
    {
        __state = Relevant;
        if (__state)
        {
            depth++;
        }
    }

    private static void Postfix(CharacterAfflictions __instance, float[] statuses, bool __state)
    {
        if (!__state || statuses == null || statuses.Length != __instance.currentStatuses.Length)
        {
            return;
        }
        for (int i = 0; i < Math.Min(12, statuses.Length); i++)
        {
            float value = statuses[i];
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                continue;
            }
            __instance.currentStatuses[i] = value;
            __instance.currentIncrementalStatuses[i] = 0f;
            __instance.currentDecrementalStatuses[i] = 0f;
        }
    }

    private static Exception Finalizer(Exception __exception, bool __state)
    {
        if (__state)
        {
            depth--;
        }
        return __exception;
    }
}
