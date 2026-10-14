using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Game bugs, fixed from ascent 9 on.
/// <list type="bullet">
/// <item>A fire that is lit from the start (the Portable Stove) gave the scouts present when it was
/// placed no Gloom protection while it burned, and a permanent 7 m safe zone once it burned out: its
/// <see cref="Campfire.Awake"/> sets the fire lit before it finds its safe zone, and
/// <see cref="Campfire.Extinguish_Rpc"/> updates the zone while the fire still counts as lit. Late
/// joiners already got it right. Both now bring the safe zone in line with the fire.</item>
/// <item>A sugar rush without a crash (the Looker's, a Shroomberry's, the Bugle of Friendship's)
/// arriving during a lollipop's threw an error: the game adds the newcomer's crash time to the running
/// rush's without checking that it has one. It now adds nothing.</item>
/// </list>
/// </summary>
internal static class GameFixes
{
    private static bool On => Rules.Enabled(Lvl.First);

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Awake))]
    private static class StoveLitFromTheStart
    {
        private static void Postfix(Campfire __instance)
        {
            if (On)
            {
                __instance.UpdateSafeZone();
            }
        }
    }

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Extinguish_Rpc))]
    private static class StoveBurnedOut
    {
        private static void Postfix(Campfire __instance)
        {
            if (On)
            {
                __instance.UpdateSafeZone();
            }
        }
    }

    /// <summary>The newcomer is a copy made for this call, so giving it an empty crash changes nothing else.</summary>
    [HarmonyPatch(typeof(Affliction_InfiniteStamina), nameof(Affliction_InfiniteStamina.Stack))]
    private static class RushWithoutCrash
    {
        private static void Prefix(Affliction_InfiniteStamina __instance, Affliction incomingAffliction)
        {
            if (On && __instance.drowsyAffliction != null && incomingAffliction is Affliction_InfiniteStamina incoming && incoming.drowsyAffliction == null)
            {
                incoming.drowsyAffliction = new Affliction_AdjustDrowsyOverTime(0f, 0f);
            }
        }
    }
}
