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
/// </summary>
[HarmonyPatch(typeof(Luggage), nameof(Luggage.Interact_CastFinished))]
internal static class A21_PainfulLuggage
{
    private const float TrickAmount = 0.45f;
    private const int TrickPetrify = 20;
    private const float TrickBlindSeconds = 100f;

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

    /// <summary>Six tricks with weight 2 each, blindness with weight 1.</summary>
    internal static void ClownTrick(CharacterAfflictions afflictions)
    {
        int roll = Random.Range(0, 13);
        switch (roll / 2)
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
                Add(afflictions, CharacterAfflictions.STATUSTYPE.FlyTrap);
                break;
            case 5:
                afflictions.AddPetrify(TrickPetrify);
                break;
            default:
                afflictions.AddAffliction(new Affliction_Blind { totalTime = TrickBlindSeconds });
                break;
        }
        Plugin.Debug($"Ascent 21: the clown chest's trick was #{roll / 2}.");
    }

    private static void Add(CharacterAfflictions afflictions, CharacterAfflictions.STATUSTYPE type)
    {
        afflictions.AddStatus(type, A15_StrongerAilments.Raw(type, TrickAmount));
    }
}
