using System;
using System.Collections.Generic;
using HarmonyLib;
using Peak;

namespace UltimateAscent.Features;

/// <summary>
/// The Warp Compass and the Toxic Drink come only from where the game's mystical items come from (Ancient
/// Luggage and the Ancient Statues), so they count as mystical where the game sorts or prices by that mark:
/// the Custom Run screen lists them among the mystical items (see <see cref="CustomRunItems"/>), and Scout's
/// Generosity clones them at a mystical item's cost (the game: 45 petrification instead of 25; ascent 15:
/// 60 instead of 35). The mark itself is never set on them for real: picking up a marked item also counts
/// toward a badge, which stays the game's.
/// </summary>
internal static class MysticalItems
{
    /// <summary>The item templates that count as mystical.</summary>
    internal static IEnumerable<Item> Templates()
    {
        if (WarpCompass.Template != null)
        {
            yield return WarpCompass.Template;
        }
        if (ToxicDrink.Template != null)
        {
            yield return ToxicDrink.Template;
        }
    }

    internal static bool CountsAsMystical(Item item) => WarpCompass.IsCompass(item) || ToxicDrink.IsToxic(item);

    /// <summary>
    /// Cloning one costs what a mystical item costs: the gem's price for the item aimed at (the game's own
    /// look, the same reach) is the mystical one for that use. After ascent 15's prices are set.
    /// </summary>
    [HarmonyPatch(typeof(Action_CloneSelectedItem), nameof(Action_CloneSelectedItem.RunAction))]
    [HarmonyPriority(Priority.Low)]
    private static class ClonedAtMysticalCost
    {
        private static void Prefix(Action_CloneSelectedItem __instance, out int? __state)
        {
            __state = null;
            Character holder = __instance.item != null ? __instance.item.holderCharacter : null;
            if (holder == null || !holder.IsLocal || Interaction.instance == null)
            {
                return;
            }
            Interaction.instance.DoInteractableRaycasts(out IInteractible aimed, __instance.range);
            if (aimed is Item target && CountsAsMystical(target))
            {
                __state = __instance.petrify;
                __instance.petrify = __instance.petrifyMystical;
            }
        }

        private static Exception Finalizer(Action_CloneSelectedItem __instance, int? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.petrify = __state.Value;
            }
            return __exception;
        }
    }
}
