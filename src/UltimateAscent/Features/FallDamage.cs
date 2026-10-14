using System;
using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Detects "this character just took fall damage" for ascents 12 (drowsy from falling) and
/// 13 (butter fingers). The game's two fall-damage checks (landing, and sliding down a wall or a
/// tree into a ledge) are wrapped in a scope, and an injury added inside the scope counts as fall
/// damage, however the scout came to fall: jumping, a jetpack, a glider, balloons, something they
/// grabbed, or True Final Ascent's Apocalypse events (its antigravity, its storms, its knock-downs).
/// Other damage (suitcases, traps, mimics) never counts.
/// </summary>
internal static class FallDamage
{
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
        if (error != null || !damaged || character == null)
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
