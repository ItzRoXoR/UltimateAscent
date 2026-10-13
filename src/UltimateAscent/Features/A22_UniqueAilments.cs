using System;
using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 22 (13DDA): all status ailments have unique effects. Cold slows walking and climbing
/// (up to 35% at a full cold bar). The other effects are in <see cref="StatusRecovery"/>.
/// </summary>
internal static class A22_UniqueAilments
{
    private static float ColdFactor(Character character)
    {
        return 1f - character.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold) * 0.35f;
    }

    [HarmonyPatch(typeof(CharacterClimbing), nameof(CharacterClimbing.GetRequestedPostition))]
    private static class ColdClimbing
    {
        private static void Prefix(CharacterClimbing __instance, out float? __state)
        {
            __state = null;
            if (Rules.Enabled(Lvl.UniqueAilments) && Rules.Owns(__instance.character))
            {
                __state = __instance.climbSpeedMod;
                __instance.climbSpeedMod *= ColdFactor(__instance.character);
            }
        }

        private static Exception Finalizer(CharacterClimbing __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.climbSpeedMod = __state.Value;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.GetMovementForce))]
    private static class ColdWalking
    {
        private static void Postfix(CharacterMovement __instance, ref float __result)
        {
            if (Rules.Enabled(Lvl.UniqueAilments) && Rules.Owns(__instance.character))
            {
                __result *= ColdFactor(__instance.character);
            }
        }
    }
}
