using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The per-frame status update of the local scout, shared by two ascents:
/// <list type="bullet">
/// <item>Ascent 16: heat heals three times slower in the volcanic Caldera and Kiln; in their
/// variants (the Gloom and the Citadel) drowsiness heals at that same slow rate instead.</item>
/// <item>Ascent 22: unique effects. Poison and spores make you stumble, injuries slowly worsen,
/// weight pulls you down faster, drowsiness does not heal at night (there is no night from the
/// Caldera and the Gloom on, see <see cref="Map.IsNight"/>), heat drains stamina while sprinting or
/// climbing (cold slows you down: see <see cref="A22_UniqueAilments"/>).</item>
/// <item>Ascent 17: poison does not heal for 10 s after a tick bite.</item>
/// </list>
/// Recovery rates are changed only for the duration of the call and then restored.
/// </summary>
[HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateNormalStatuses))]
internal static class StatusRecovery
{
    private const float SlowHeatRecovery = 0.00333333f;
    private const float DayDrowsyRecovery = 0.02f;
    private const float InjuryWorsening = 0.00033333f;

    internal struct Rates
    {
        internal bool changed;
        internal float drowsy;
        internal float hot;
        internal float poison;
    }

    private static void Prefix(CharacterAfflictions __instance, out Rates __state)
    {
        __state = default;
        Character character = __instance.character;
        bool slow = Rules.Enabled(Lvl.SlowHeatHotCampfires);
        bool unique = Rules.Enabled(Lvl.UniqueAilments);
        bool bitten = A17_Hazards.PoisonHealingBlocked;
        if ((!slow && !unique && !bitten) || !Rules.Owns(character))
        {
            return;
        }
        __state = new Rates { changed = true, drowsy = __instance.drowsyReductionPerSecond, hot = __instance.hotReductionPerSecond, poison = __instance.poisonReductionPerSecond };
        if (bitten)
        {
            __instance.poisonReductionPerSecond = 0f;
        }

        bool slowDrowsyRegion = false;
        float slowDrowsyRate = SlowHeatRecovery;
        if (slow)
        {
            if (Map.InVolcano)
            {
                __instance.hotReductionPerSecond = SlowHeatRecovery;
            }
            slowDrowsyRegion = Map.InGloom || Map.InCitadel;
            if (slowDrowsyRegion)
            {
                if (A25_AllWeather.GloomRelief)
                {
                    // Ascent 25 adds snowstorms to the Gloom, so its sleep heals twice as fast as on 16-24.
                    slowDrowsyRate *= 2f;
                }
                __instance.drowsyReductionPerSecond = slowDrowsyRate;
            }
        }

        if (!unique)
        {
            return;
        }
        float poison = __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Poison);
        float spores = __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Spores);
        if ((poison > 0.02f || spores > 0.02f) && UnityEngine.Random.Range((poison + spores) * 2000f * Time.deltaTime, 2000f * Time.deltaTime) > 1999f * Time.deltaTime)
        {
            character.Fall(0.1f);
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
        if (DayNightManager.instance != null)
        {
            if (Map.IsNight)
            {
                __instance.drowsyReductionPerSecond = 0f;
            }
            else
            {
                __instance.drowsyReductionPerSecond = slowDrowsyRegion ? slowDrowsyRate : DayDrowsyRecovery;
            }
        }
        float heat = __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hot);
        if (heat > 0f && (character.data.isClimbing || character.data.isSprinting))
        {
            character.AddStamina(-heat * 0.15f * Time.deltaTime);
        }
    }

    private static Exception Finalizer(CharacterAfflictions __instance, Rates __state, Exception __exception)
    {
        if (__state.changed)
        {
            __instance.drowsyReductionPerSecond = __state.drowsy;
            __instance.hotReductionPerSecond = __state.hot;
            __instance.poisonReductionPerSecond = __state.poison;
        }
        return __exception;
    }
}
