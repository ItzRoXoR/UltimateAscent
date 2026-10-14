using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 24 (More Ascents): going unconscious is instant death (a checkpoint flag still saves you,
/// exactly like any other death). The faints True Final Ascent's Apocalypse events cause on purpose
/// (thunder, sand, burial, drowning, its wisps and Scoutmaster) stay faints.
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.RPCA_PassOut))]
internal static class A24_NoUnconscious
{
    private static void Postfix(Character __instance)
    {
        if (Rules.Enabled(Lvl.NoUnconscious) && __instance.IsLocal && !__instance.data.dead && !Compat.Tfa.Causing)
        {
            __instance.DieInstantly();
        }
    }
}
