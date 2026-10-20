using HarmonyLib;
using Peak.Afflictions;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Blindness (a ghost's burst, a shroomberry, a clown chest's trick) is cured by the First Aid Kit and the Cure-All
/// (in Russian the Medkit and the Panacea): using one takes the blindness away along with what it heals, on every
/// ascent. The game's own effects of the two items are unchanged. The game blinds in two parts: the blindness effect,
/// and the darkened screen ("BLIND"), which runs on its own timer; both go.
/// </summary>
internal static class BlindnessCures
{
    internal const string FirstAidKitPrefab = "FirstAidKit";
    internal const string CureAllPrefab = "Cure-All";

    internal static bool Cures(Item item)
    {
        if (item == null)
        {
            return false;
        }
        string name = item.gameObject.name;
        return name.StartsWith(FirstAidKitPrefab, System.StringComparison.Ordinal) || name.StartsWith(CureAllPrefab, System.StringComparison.Ordinal);
    }

    /// <summary>The darkened screen's name; the game sets its time left to what it is given.</summary>
    private const string BlindScreen = "BLIND";

    /// <summary>Both items heal through status changes (each runs several); the first one takes the blindness away.</summary>
    [HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static class CuresBlindness
    {
        private static void Postfix(Action_ModifyStatus __instance)
        {
            Character character = __instance.item != null ? __instance.item.holderCharacter : null;
            if (character == null || !character.IsLocal || !Cures(__instance.item))
            {
                return;
            }
            CharacterAfflictions afflictions = character.refs.afflictions;
            while (afflictions.HasAfflictionType(Affliction.AfflictionType.Blind, out Affliction blind))
            {
                afflictions.RemoveAffliction(blind);
            }
            character.AddIllegalStatus(BlindScreen, 0f);
        }
    }
}
