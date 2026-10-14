using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// The config's "Cold Nights" switch (the host's): off, the night itself no longer makes anyone cold,
/// on every ascent. The game asks <see cref="Ascents.isNightCold"/> before adding its night cold, and
/// so do ascent 18's longer Alpine nights (<see cref="A18_ExtremeWeather"/>), whose harsher rate only
/// scales that cold; cold from anything else is untouched. Custom runs keep their own Cold Night
/// setting.
/// </summary>
[HarmonyPatch(typeof(Ascents), "get_" + nameof(Ascents.isNightCold))]
internal static class ColdNights
{
    private static void Postfix(ref bool __result)
    {
        if (__result && !RunSettings.IsCustomRun && !HostOptions.ColdNightsOn)
        {
            __result = false;
        }
    }
}
