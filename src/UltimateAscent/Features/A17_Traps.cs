using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;
using MovingSawBlade = global::Peak.MovingSawBlade;
using SpikeRoller = global::Peak.SpikeRoller;
using SpikeTrap = global::Peak.SpikeTrap;
using TrickLuggage = global::Peak.TrickLuggage;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: the Citadel's traps and chests, and the Alpine's napberries.
/// <list type="bullet">
/// <item>Wall saws hit for 30 on the first touch (the game: 15); the Central Tower's saw for 7.5 a
/// second (the game: nothing, its damage is left empty). Spike rollers hit for 10 (the game: 5),
/// swinging spike balls for up to 60 by their speed (the game: 25). Arrows and spiked fences: see
/// <see cref="A17_Hazards"/>.</item>
/// <item>The spinning traps move 1.5 times as fast: the spike rollers spin, the spike balls swing and their
/// heads turn; so do the saws along their walls. Every scout's game moves them by the run's shared clock, so
/// they stay in step. The pressure plates' spikes come up 1.5 times as soon (0.53 s after the step; the game:
/// 0.8 s).</item>
/// <item>Mimic chests release gas that puts 5 drowsiness, 5 poison and 5 spores on you every 0.5 s
/// (the game: 5 drowsiness). Every second ordinary chest in the Citadel is a mimic too: it releases
/// the same gas when opened, and keeps its loot like the game's own mimics.</item>
/// <item>Napberries pulse once a second (the game: twice every 3 s).</item>
/// </list>
/// </summary>
internal static class A17_Traps
{
    private static bool On => Rules.Enabled(Lvl.Hazards);

    private const float SawFirstHit = 0.3f;
    private const float TowerSawHit = 0.075f;
    private const float RollerInjury = 0.1f;
    private const float SpikeBallInjury = 0.6f;

    /// <summary>How much faster the spike rollers and the swinging spike balls move.</summary>
    internal const float TrapSpeed = 1.5f;

    /// <summary>What the mimic gas puts on the bar of each status every tick (0.5 s).</summary>
    private const float GasTick = 0.05f;

    /// <summary>The napberry animation holds two pulses in a 3 s loop; played 1.5x as fast, that is one a second.</summary>
    private const float NapberryPace = 1.5f;

    /// <summary>How long a mimic's gas object stays around after it is released (its gas lasts 10 s).</summary>
    private const float GasCleanupSeconds = 20f;

    [HarmonyPatch(typeof(MovingSawBlade), nameof(MovingSawBlade.OnTriggerStay))]
    private static class Saws
    {
        private static void Prefix(MovingSawBlade __instance)
        {
            if (!On)
            {
                return;
            }
            float first = Overrides.Original(__instance, "initial", __instance.initialInjury);
            if (first > 0f)
            {
                Overrides.Set(__instance, nameof(MovingSawBlade.initialInjury), SawFirstHit);
            }
            else if (Overrides.Original(__instance, "cooldown", __instance.initialInjuryCooldown) <= 0f)
            {
                // The Central Tower's saw: every hit counts as a first hit, once per its 1 s timeout.
                Overrides.Set(__instance, nameof(MovingSawBlade.initialInjury), TowerSawHit * Overrides.Original(__instance, "timeout", __instance.timeout));
            }
        }
    }

    /// <summary>A spike roller's turn, as the game sets it from the run's clock, 1.5 times as fast.</summary>
    [HarmonyPatch(typeof(SpikeRoller), nameof(SpikeRoller.UpdateSpin))]
    private static class FasterRollers
    {
        private static bool Prefix(SpikeRoller __instance)
        {
            if (!On || RunManager.Instance == null)
            {
                return true;
            }
            float time = RunManager.Instance.TimeSinceRunStarted * TrapSpeed;
            __instance.rb.MoveRotation(Quaternion.Euler(0f, time * __instance.spinnerSpeed % 360f, 0f));
            foreach (Transform roller in __instance.roller)
            {
                roller.localEulerAngles = new Vector3(time * __instance._finalRollerSpeed % 360f, 0f, 0f);
            }
            return false;
        }
    }

    /// <summary>
    /// A swinging spike ball swings and turns its head 1.5 times as fast (also where it works out its swing again
    /// after bumping into rock).
    /// </summary>
    [HarmonyPatch]
    private static class FasterSpikeBalls
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SwingingAxe), nameof(SwingingAxe.SetAxeRotation));
            yield return AccessTools.Method(typeof(SwingingAxe), nameof(SwingingAxe.OnCollisionEnter));
        }

        private static void Prefix(SwingingAxe __instance, out float[] __state)
        {
            __state = null;
            if (On)
            {
                __state = new[] { __instance.swingSpeed, __instance.spikeHeadRotateSpeed };
                __instance.swingSpeed *= TrapSpeed;
                __instance.spikeHeadRotateSpeed *= TrapSpeed;
            }
        }

        private static Exception Finalizer(SwingingAxe __instance, float[] __state, Exception __exception)
        {
            if (__state != null)
            {
                __instance.swingSpeed = __state[0];
                __instance.spikeHeadRotateSpeed = __state[1];
            }
            return __exception;
        }
    }

    /// <summary>A saw runs along its wall 1.5 times as fast (its back-and-forth by the run's clock).</summary>
    [HarmonyPatch(typeof(MovingSawBlade), nameof(MovingSawBlade.Update))]
    private static class FasterSaws
    {
        private static void Prefix(MovingSawBlade __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(MovingSawBlade.pathLength), Overrides.Original(__instance, "pathLength", __instance.pathLength) * TrapSpeed);
            }
        }
    }

    /// <summary>A pressure plate's spikes come up 1.5 times as soon after the step (every scout's game runs the plate's own timing).</summary>
    [HarmonyPatch(typeof(SpikeTrap), nameof(SpikeTrap.SpikeTrapRPC))]
    private static class QuickerPlates
    {
        private static void Prefix(SpikeTrap __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(SpikeTrap.spikeWaitTime), Overrides.Original(__instance, "wait", __instance.spikeWaitTime) / TrapSpeed);
                Overrides.Set(__instance, nameof(SpikeTrap.readyTweenTime), Overrides.Original(__instance, "ready", __instance.readyTweenTime) / TrapSpeed);
            }
        }
    }

    [HarmonyPatch(typeof(SpikeRoller), nameof(SpikeRoller.OnCollisionEnter))]
    private static class Rollers
    {
        private static void Prefix(SpikeRoller __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(SpikeRoller.injury), RollerInjury);
            }
        }
    }

    [HarmonyPatch(typeof(SwingingAxe), nameof(SwingingAxe.OnCollisionEnter))]
    private static class SpikeBalls
    {
        private static void Prefix(SwingingAxe __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(SwingingAxe.maxInjury), SpikeBallInjury);
            }
        }
    }

    private sealed class GasFlag
    {
        internal bool gas;
    }

    private static readonly ConditionalWeakTable<StatusField, GasFlag> gasFields = new ConditionalWeakTable<StatusField, GasFlag>();

    private static bool IsMimicGas(StatusField field)
    {
        if (!gasFields.TryGetValue(field, out GasFlag flag))
        {
            flag = new GasFlag { gas = field.GetComponent<TrickLuggage>() != null };
            gasFields.Add(field, flag);
        }
        return flag.gas;
    }

    /// <summary>Mimic gas: 5 drowsiness, 5 poison and 5 spores every tick, exactly.</summary>
    [HarmonyPatch(typeof(StatusField), nameof(StatusField.TickStatus))]
    private static class MimicGas
    {
        private static bool Prefix(StatusField __instance)
        {
            if (!On || !__instance.tickBased || Character.localCharacter == null || !IsMimicGas(__instance))
            {
                return true;
            }
            __instance.tick += Time.deltaTime;
            if (__instance.tick >= __instance.timeBetweenTicks)
            {
                __instance.tick = 0f;
                CharacterAfflictions afflictions = Character.localCharacter.refs.afflictions;
                afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Drowsy, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Drowsy, GasTick));
                afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Poison, GasTick));
                afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Spores, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Spores, GasTick));
            }
            return false;
        }
    }

    /// <summary>
    /// An ordinary chest in the Citadel (not a statue, a clown chest or one of the game's own mimics).
    /// The Citadel shares the Gloom's biome, so its chests are found by the Citadel's segment.
    /// </summary>
    internal static bool IsCitadelChest(Luggage luggage)
    {
        if (luggage == null || luggage is RespawnChest || luggage.CompareTag("ClownLuggage"))
        {
            return false;
        }
        if (luggage.activateOnOpen != null && luggage.activateOnOpen.GetComponent<TrickLuggage>() != null)
        {
            return false;
        }
        return Map.InCitadelSegment(luggage);
    }

    /// <summary>Every second one, by its network id, which every player agrees on.</summary>
    internal static bool IsSecondChest(Luggage luggage) => luggage.photonView != null && luggage.photonView.ViewID % 2 == 0;

    private static GameObject gasTemplate;

    private static GameObject GasTemplate()
    {
        if (gasTemplate != null)
        {
            return gasTemplate;
        }
        foreach (TrickLuggage trick in Resources.FindObjectsOfTypeAll<TrickLuggage>())
        {
            if (trick != null && trick.statusField != null)
            {
                return gasTemplate = trick.gameObject;
            }
        }
        return null;
    }

    /// <summary>Opening runs on every client, so each one releases the gas for its own scout.</summary>
    [HarmonyPatch(typeof(Luggage), nameof(Luggage.OpenLuggageRPC))]
    private static class CitadelMimics
    {
        private static void Prefix(Luggage __instance, out bool __state)
        {
            __state = __instance.state != Luggage.LuggageState.Open;
        }

        private static void Postfix(Luggage __instance, bool spawnItems, bool __state)
        {
            if (!On || !__state || !spawnItems || !IsCitadelChest(__instance) || !IsSecondChest(__instance))
            {
                return;
            }
            GameObject template = GasTemplate();
            if (template == null)
            {
                Plugin.Log.LogWarning("Ascent 17: no mimic gas found to release from a Citadel chest.");
                return;
            }
            GameObject gas = UnityEngine.Object.Instantiate(template, __instance.transform.position + Vector3.up * 0.5f, __instance.transform.rotation);
            gas.SetActive(true);
            UnityEngine.Object.Destroy(gas, GasCleanupSeconds);
            Plugin.Debug("Ascent 17: a Citadel chest was a mimic.");
        }
    }

    [HarmonyPatch(typeof(OrbThatMakesYouSleepy), nameof(OrbThatMakesYouSleepy.LateUpdate))]
    private static class NapberryPulse
    {
        private static void Prefix(OrbThatMakesYouSleepy __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(OrbThatMakesYouSleepy.animSpeed), NapberryPace);
            }
        }
    }
}
