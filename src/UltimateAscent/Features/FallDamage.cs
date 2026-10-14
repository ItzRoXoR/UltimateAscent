using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Detects "this character just took fall damage" for ascents 12 (drowsy from falling) and
/// 13 (butter fingers). The game's two fall-damage checks are wrapped in a scope, and an
/// injury added inside the scope counts as fall damage.
/// <para>A fall True Final Ascent's Apocalypse events caused is TFA's: its knock-downs and pushes, its
/// rain making you let go, the Scoutmaster its Blackout or Watch sent throwing you, its antigravity
/// letting you drop. Its landing hurts as TFA meant, without ascents 12 and 13.</para>
/// </summary>
internal static class FallDamage
{
    /// <summary>How long after TFA's knock-down, push or throw a landing still counts as its fall (on top of the knock-down itself).</summary>
    private const float TfaFallSeconds = 5f;

    private static Character tfaFallen;
    private static float tfaFallUntil;

    private static void TfaFall(Character character, float seconds)
    {
        if (tfaFallen != character)
        {
            tfaFallen = character;
            tfaFallUntil = 0f;
        }
        tfaFallUntil = Mathf.Max(tfaFallUntil, Time.time + seconds + TfaFallSeconds);
    }

    /// <summary>The landing ends the fall TFA started, whatever it does.</summary>
    private static bool LandsTfaFall(Character character)
    {
        bool tfa = (character == tfaFallen && Time.time < tfaFallUntil) || Compat.Tfa.AntigravFalls;
        if (character == tfaFallen)
        {
            tfaFallen = null;
        }
        return tfa;
    }

    internal struct Scope
    {
        internal Character previous;
        internal bool previousDamage;
        internal bool entered;
    }

    [ThreadStatic] private static Character current;
    [ThreadStatic] private static bool tookDamage;

    private static Scope Enter(Character character)
    {
        Scope scope = new Scope
        {
            previous = current,
            previousDamage = tookDamage,
            entered = (Rules.Enabled(Lvl.Drowsy) || Rules.Enabled(Lvl.ButterFingers)) && Rules.Owns(character)
        };
        if (scope.entered)
        {
            current = character;
            tookDamage = false;
        }
        return scope;
    }

    private static Exception Exit(Scope scope, Exception error)
    {
        if (!scope.entered)
        {
            return error;
        }
        Character character = current;
        bool damaged = tookDamage;
        current = scope.previous;
        tookDamage = scope.previousDamage;
        bool tfaFall = character != null && LandsTfaFall(character);
        if (error != null || !damaged || character == null || tfaFall)
        {
            return error;
        }
        if (Rules.Enabled(Lvl.Drowsy))
        {
            A12_Drowsy.FallAsleep(character);
        }
        if (Rules.Enabled(Lvl.ButterFingers))
        {
            A13_ButterFingers.DropEverything(character);
        }
        return error;
    }

    [HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.CheckFallDamage))]
    private static class MovementFall
    {
        private static void Prefix(CharacterMovement __instance, out Scope __state)
        {
            __state = Enter(__instance.character);
        }

        private static Exception Finalizer(Exception __exception, Scope __state)
        {
            return Exit(__state, __exception);
        }
    }

    [HarmonyPatch(typeof(CharacterClimbing), nameof(CharacterClimbing.CheckFallDamage))]
    private static class ClimbingFall
    {
        private static void Prefix(CharacterClimbing __instance, out Scope __state)
        {
            __state = Enter(__instance.character);
        }

        private static Exception Finalizer(Exception __exception, Scope __state)
        {
            return Exit(__state, __exception);
        }
    }

    private static bool Watching(Character character) =>
        (Rules.Enabled(Lvl.Drowsy) || Rules.Enabled(Lvl.ButterFingers)) && Rules.Owns(character) && Compat.Tfa.ApocalypseActive;

    /// <summary>A knock-down sent by TFA's code (thunder, quakes, slips, avalanches, tsunamis, volcano rocks...).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.RPCA_Fall))]
    private static class TfaKnockDown
    {
        private static void Prefix(Character __instance, float seconds)
        {
            if (Watching(__instance) && Compat.Tfa.Causing)
            {
                TfaFall(__instance, seconds);
            }
        }
    }

    /// <summary>A push sent by TFA's code (its storms).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.RPCA_AddForceAtPosition))]
    private static class TfaPush
    {
        private static void Prefix(Character __instance)
        {
            if (Watching(__instance) && Compat.Tfa.Causing)
            {
                TfaFall(__instance, 0f);
            }
        }
    }

    /// <summary>Letting go of a wall because TFA's code said so (its rain).</summary>
    [HarmonyPatch(typeof(CharacterClimbing), nameof(CharacterClimbing.StopClimbing))]
    private static class TfaLetGo
    {
        private static void Prefix(CharacterClimbing __instance)
        {
            if (Watching(__instance.character) && Compat.Tfa.Causing)
            {
                TfaFall(__instance.character, 0f);
            }
        }
    }

    /// <summary>A throw by the Scoutmaster TFA's Blackout or Watch sent.</summary>
    [HarmonyPatch(typeof(Scoutmaster), nameof(Scoutmaster.IThrow))]
    private static class TfaThrow
    {
        private static void Prefix(Scoutmaster __instance)
        {
            Character target = __instance.currentTarget;
            if (Watching(target) && Compat.Tfa.ScoutmasterEvent)
            {
                TfaFall(target, 0f);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    private static class ObserveInjury
    {
        private static void Postfix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, float amount, bool __result)
        {
            if (__result && amount > 0f && statusType == CharacterAfflictions.STATUSTYPE.Injury && current != null && __instance.character == current)
            {
                tookDamage = true;
            }
        }
    }
}
