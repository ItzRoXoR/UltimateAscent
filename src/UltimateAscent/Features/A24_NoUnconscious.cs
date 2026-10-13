using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 24 (More Ascents): going unconscious is instant death (a checkpoint flag still saves you,
/// exactly like any other death).
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.RPCA_PassOut))]
internal static class A24_NoUnconscious
{
    private static void Postfix(Character __instance)
    {
        if (Rules.Enabled(Lvl.NoUnconscious) && __instance.IsLocal && !__instance.data.dead)
        {
            __instance.DieInstantly();
        }
    }
}
