using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The per-frame status update of the scouts, shared by several ascents:
/// <list type="bullet">
/// <item>Ascent 16: heat heals at 0.5 a second in the volcanic Caldera and Kiln (the game: 1.3); in
/// their variants (the Gloom and the Citadel) drowsiness heals at 1 a second (the game: 2.5).</item>
/// <item>Ascent 22: unique effects. Injuries slowly worsen, weight pulls you down faster, drowsiness does not
/// heal at night (there is no night from the Caldera and the Gloom on, see <see cref="Map.IsNight"/>) and heals at 2
/// a second by day (cold, heat, poison and spores: see <see cref="A22_UniqueAilments"/> and <see cref="A22_Episodes"/>).</item>
/// <item>Ascent 17: poison does not heal for 10 s after a tick bite. In the Nadir drowsiness, poison, heat and
/// spores heal half as fast as they would elsewhere on the ascent (see <see cref="NadirRules"/>).</item>
/// </list>
/// The drowsiness, poison, heat and spores recovery rates stay on every scout's own fields (the game's are
/// kept aside and come back wherever the rules leave them alone), so mods that work out from those fields
/// when a status wears off (PlayersInfo's countdowns, Effect Preview) show this ascent's times. For
/// other players' scouts they serve only those displays: each player's own game heals their scout.
/// The tick bite's pause is set only for the duration of the update.
/// </summary>
[HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateNormalStatuses))]
internal static class StatusRecovery
{
    private const float SlowHeatRecovery = 0.005f;
    private const float SlowDrowsyRecovery = 0.01f;
    private const float DayDrowsyRecovery = 0.02f;
    private const float InjuryWorsening = 0.00033333f;

    /// <summary>A scout's recovery rates as the game set them, kept aside the first time it is updated.</summary>
    private sealed class GameRates
    {
        internal readonly float drowsy;
        internal readonly float hot;
        internal readonly float poison;
        internal readonly float spores;

        internal GameRates(CharacterAfflictions afflictions)
        {
            drowsy = afflictions.drowsyReductionPerSecond;
            hot = afflictions.hotReductionPerSecond;
            poison = afflictions.poisonReductionPerSecond;
            spores = afflictions.sporesReductionPerSecond;
        }
    }

    private static readonly ConditionalWeakTable<CharacterAfflictions, GameRates> gameRates = new ConditionalWeakTable<CharacterAfflictions, GameRates>();

    /// <summary>How fast drowsiness heals here and now on this ascent, given the game's own rate.</summary>
    internal static float DrowsyRate(float game)
    {
        bool slowRegion = Rules.Enabled(Lvl.SlowRecovery) && (Map.InGloom || Map.InCitadel);
        float rate = slowRegion ? SlowDrowsyRecovery : game;
        if (Rules.Enabled(Lvl.UniqueAilments) && DayNightManager.instance != null)
        {
            rate = Map.IsNight ? 0f : slowRegion ? SlowDrowsyRecovery : DayDrowsyRecovery;
        }
        return rate * NadirRules.RecoveryShare;
    }

    /// <summary>How fast heat heals here and now on this ascent, given the game's own rate.</summary>
    internal static float HeatRate(float game)
    {
        return (Rules.Enabled(Lvl.SlowRecovery) && Map.InVolcano ? SlowHeatRecovery : game) * NadirRules.RecoveryShare;
    }

    private static void Prefix(CharacterAfflictions __instance, out float? __state)
    {
        __state = null;
        GameRates game = gameRates.GetValue(__instance, afflictions => new GameRates(afflictions));
        __instance.drowsyReductionPerSecond = DrowsyRate(game.drowsy);
        __instance.hotReductionPerSecond = HeatRate(game.hot);
        __instance.poisonReductionPerSecond = game.poison * NadirRules.RecoveryShare;
        __instance.sporesReductionPerSecond = game.spores * NadirRules.RecoveryShare;

        Character character = __instance.character;
        if (!Rules.Owns(character))
        {
            return;
        }
        if (A17_Hazards.PoisonHealingBlocked)
        {
            __state = __instance.poisonReductionPerSecond;
            __instance.poisonReductionPerSecond = 0f;
        }
        if (!Rules.Enabled(Lvl.UniqueAilments))
        {
            return;
        }
        if (__instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury) > 0.02f)
        {
            __instance.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, InjuryWorsening * Time.deltaTime);
        }
        float weight = __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Weight);
        if (weight > 0f && !character.data.isClimbing && character.data.groundedFor < 0.5f)
        {
            character.GetBodypart(BodypartType.Hip).rig.linearVelocity -= Vector3.up * weight * 100f * Time.deltaTime;
        }
    }

    private static Exception Finalizer(CharacterAfflictions __instance, float? __state, Exception __exception)
    {
        if (__state.HasValue)
        {
            __instance.poisonReductionPerSecond = __state.Value;
        }
        return __exception;
    }
}
