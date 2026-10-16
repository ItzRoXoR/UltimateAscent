using System;
using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: lighting a campfire takes only 10 petrification off the scouts beside it (the game: 20).
/// (This ascent's extremely hot campfires live in <see cref="A17_HotCampfires"/>.)
/// </summary>
internal static class A17_CampfirePetrify
{
    internal const float PetrifyHealed = 0.1f;

    [ThreadStatic] private static bool lighting;

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Update))]
    private static class Lighting
    {
        private static void Prefix()
        {
            lighting = Rules.Enabled(Lvl.HotCampfires);
        }

        private static Exception Finalizer(Exception __exception)
        {
            lighting = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AdjustStatus))]
    private static class LessHealing
    {
        private static void Prefix(CharacterAfflictions.STATUSTYPE statusType, ref float amount)
        {
            if (lighting && statusType == CharacterAfflictions.STATUSTYPE.Petrify && amount < 0f)
            {
                amount = -PetrifyHealed;
            }
        }
    }
}
