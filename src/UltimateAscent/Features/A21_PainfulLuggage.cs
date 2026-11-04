using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 21 (13DDA): suitcases are painful. Every suitcase hurts like an ancient luggage when
/// opened, more the rarer it is: small 6, big 10, epic 16, scout statues 20 plus 6 curse.
/// Clown chests play a trick instead of hurting: 45 drowsiness, poison, spores, web or fly-trap
/// grip, or 20 petrification (all equally likely), or, half as often, 100 s of blindness. Web and
/// fly-trap grip come off the way they do in the game: by struggling (jumping) or a friend's help.
/// The grip is a fly trap's that springs up and holds the scout (<see cref="ClownFlyTrap"/>); on a
/// map without the Gloom, where no fly trap grows, the scout is paralysed once instead.
/// </summary>
[HarmonyPatch(typeof(Luggage), nameof(Luggage.Interact_CastFinished))]
internal static class A21_PainfulLuggage
{
    internal const float TrickAmount = 0.45f;
    internal const int TrickPetrify = 20;
    internal const float TrickBlindSeconds = 100f;

    /// <summary>For the tests: the next clown chest's trick (see <see cref="PickTrick"/>), once.</summary>
    internal static int? NextTrick;

    private static void Prefix(Luggage __instance, Character interactor)
    {
        if (!Rules.Enabled(Lvl.PainfulLuggage) || !Rules.Owns(interactor) || __instance.state != Luggage.LuggageState.Closed || Compat.Tfa.Spawned(__instance))
        {
            return;
        }
        CharacterAfflictions afflictions = interactor.refs.afflictions;
        if (__instance.CompareTag("ClownLuggage"))
        {
            ClownTrick(afflictions);
        }
        else if (Pain(__instance, out float injury, out float curse))
        {
            afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, injury);
            if (curse > 0f)
            {
                afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, curse);
            }
        }
    }

    /// <summary>What opening this luggage costs (not the clown chest, whose trick is random).</summary>
    internal static bool Pain(Luggage luggage, out float injury, out float curse)
    {
        injury = 0f;
        curse = 0f;
        if (luggage == null || luggage.CompareTag("ClownLuggage"))
        {
            return false;
        }
        string name = luggage.gameObject.name;
        if (name.StartsWith("LuggageSmall"))
        {
            injury = 0.06f;
        }
        else if (name.StartsWith("LuggageBig"))
        {
            injury = 0.1f;
        }
        else if (name.StartsWith("LuggageEpic"))
        {
            injury = 0.16f;
        }
        else if (luggage is RespawnChest || name.StartsWith("scout statue"))
        {
            injury = 0.2f;
            curse = 0.06f;
        }
        else
        {
            injury = 0.1f;
        }
        return true;
    }

    /// <summary>
    /// A clown chest's trick: drowsiness, poison, spores, web, fly-trap grip or petrification (0-5) with weight 2 each,
    /// blindness (6) with weight 1.
    /// </summary>
    internal static int PickTrick()
    {
        if (NextTrick is int told)
        {
            NextTrick = null;
            return told;
        }
        return Random.Range(0, 13) / 2;
    }

    internal static void ClownTrick(CharacterAfflictions afflictions)
    {
        int trick = PickTrick();
        switch (trick)
        {
            case 0:
                Add(afflictions, CharacterAfflictions.STATUSTYPE.Drowsy);
                break;
            case 1:
                Add(afflictions, CharacterAfflictions.STATUSTYPE.Poison);
                break;
            case 2:
                Add(afflictions, CharacterAfflictions.STATUSTYPE.Spores);
                break;
            case 3:
                Add(afflictions, CharacterAfflictions.STATUSTYPE.Web);
                break;
            case 4:
                ClownFlyTrap.Play(afflictions.character, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.FlyTrap, TrickAmount));
                break;
            case 5:
                afflictions.AddPetrify(TrickPetrify);
                break;
            default:
                afflictions.AddAffliction(new Affliction_Blind { totalTime = TrickBlindSeconds });
                break;
        }
        Plugin.Debug($"Ascent 21: the clown chest's trick was #{trick}.");
    }

    private static void Add(CharacterAfflictions afflictions, CharacterAfflictions.STATUSTYPE type)
    {
        afflictions.AddStatus(type, A15_StrongerAilments.Raw(type, TrickAmount));
    }
}
