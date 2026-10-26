using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Config "Ascent 8" ([Ascents], the host's): the game's own ascent 8, from which the flares at the peak no longer call
/// the rescue helicopter and a climb is won only in the Nadir. Switched off, the flares call it again on every ascent.
/// </summary>
internal static class Ascent8Switch
{
    [HarmonyPatch(typeof(Ascents), "get_" + nameof(Ascents.shouldRequireNadir))]
    private static class NadirOnlyWhenOn
    {
        private static void Postfix(ref bool __result)
        {
            if (__result && !HostOptions.Ascent8On)
            {
                __result = false;
            }
        }
    }
}
