using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 24 (More Ascents): going unconscious is instant death (a checkpoint flag still saves you,
/// exactly like any other death). The faints True Final Ascent's Apocalypse events cause on purpose
/// (thunder, sand, burial, drowning, its wisps and Scoutmaster) stay faints.
/// A scout whom a flag will save does not pass out first: the flag saves them at once. (Passing out
/// fades the screen to black and, once that is done, knocks the scout down and drops their items,
/// which would happen after the save, at the flag.)
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.RPCA_PassOut))]
internal static class A24_NoUnconscious
{
    private static bool Applies(Character character)
    {
        return Rules.Enabled(Lvl.NoUnconscious) && character.IsLocal && !character.data.dead && !Compat.Tfa.Causing;
    }

    private static bool Prefix(Character __instance)
    {
        if (Applies(__instance) && __instance.WillDoCheckpoint(out _))
        {
            __instance.DieInstantly();
            return false;
        }
        return true;
    }

    private static void Postfix(Character __instance)
    {
        // Only a scout who really passed out (no flag saved them in the prefix).
        if (Applies(__instance) && __instance.data.passedOut)
        {
            __instance.DieInstantly();
        }
    }
}
