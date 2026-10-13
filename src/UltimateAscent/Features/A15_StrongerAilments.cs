using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 15 (13DDA): cold and drowsiness apply 30% more, heat 50% more, spores 65% more and
/// poison 75% more. No exceptions: rules that need an exact amount on the bar ask
/// <see cref="Raw"/> for the amount to add (see <see cref="ExactStatus"/> for streams).
/// </summary>
[HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
internal static class A15_StrongerAilments
{
    internal const float ColdFactor = 1.3f;
    internal const float DrowsyFactor = 1.3f;
    internal const float HeatFactor = 1.5f;
    internal const float SporesFactor = 1.65f;
    internal const float PoisonFactor = 1.75f;

    /// <summary>The game puts statuses on the bar in whole steps of this size and drops the rest.</summary>
    internal const float Step = CharacterAfflictions.STATUS_INCREMENT;

    /// <summary>How much this ascent scales a status right now (1 below ascent 15).</summary>
    internal static float Factor(CharacterAfflictions.STATUSTYPE type)
    {
        if (!Rules.Enabled(Lvl.StrongerAilments))
        {
            return 1f;
        }
        switch (type)
        {
            case CharacterAfflictions.STATUSTYPE.Cold:
                return ColdFactor;
            case CharacterAfflictions.STATUSTYPE.Drowsy:
                return DrowsyFactor;
            case CharacterAfflictions.STATUSTYPE.Hot:
                return HeatFactor;
            case CharacterAfflictions.STATUSTYPE.Poison:
                return PoisonFactor;
            case CharacterAfflictions.STATUSTYPE.Spores:
                return SporesFactor;
            default:
                return 1f;
        }
    }

    /// <summary>
    /// The amount to add so that exactly <paramref name="target"/> lands on the bar once this ascent
    /// has scaled it. The target should be a whole number of steps; the small nudge keeps float
    /// rounding from dropping the last step.
    /// </summary>
    internal static float Raw(CharacterAfflictions.STATUSTYPE type, float target)
    {
        return target / Factor(type) + 0.0001f;
    }

    /// <summary>The same for an amount that is not a whole number of steps itself (rates, falloff).</summary>
    internal static float RawRate(CharacterAfflictions.STATUSTYPE type, float target)
    {
        return target / Factor(type);
    }

    private static void Prefix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount)
    {
        if (amount <= 0f || StatusReplication.Active || !Rules.Owns(__instance.character) || !Rules.Enabled(Lvl.StrongerAilments))
        {
            return;
        }
        amount *= Factor(statusType);
    }
}

/// <summary>
/// Adds a status so that exactly the requested amount lands on the bar over time. The game puts
/// statuses on the bar in whole 2.5-point steps and drops whatever is left over; this carries that
/// remainder to the next call instead, so a stream (bee stings) or an amount that is not a whole
/// number of steps (a spider's 12 poison) averages out exactly.
/// </summary>
internal static class ExactStatus
{
    private static readonly Dictionary<string, float> owed = new Dictionary<string, float>();

    internal static void Reset()
    {
        owed.Clear();
    }

    internal static void Add(CharacterAfflictions afflictions, CharacterAfflictions.STATUSTYPE type, float target, string source)
    {
        if (afflictions == null || target <= 0f)
        {
            return;
        }
        string key = source + "/" + (int)type;
        owed.TryGetValue(key, out float carried);
        float total = carried + target;
        int steps = Mathf.FloorToInt(total / A15_StrongerAilments.Step + 0.0001f);
        if (steps > 0)
        {
            afflictions.AddStatus(type, A15_StrongerAilments.Raw(type, steps * A15_StrongerAilments.Step));
            total -= steps * A15_StrongerAilments.Step;
        }
        owed[key] = Mathf.Max(0f, total);
    }
}
