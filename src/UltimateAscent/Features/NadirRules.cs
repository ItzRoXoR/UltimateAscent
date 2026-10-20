using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Nadir.
/// <list type="bullet">
/// <item>From ascent 17 everything bad wears off half as fast there: drowsiness, poison, heat and spores heal at
/// half the rate they would elsewhere on that ascent (see <see cref="StatusRecovery"/>), and blindness, numbness
/// and a pause in poison healing last twice as long.</item>
/// <item>From ascent 18 gravity pulls the scouts twice as hard there: in the air, and while they lie limp.</item>
/// </list>
/// </summary>
internal static class NadirRules
{
    /// <summary>How fast ailments wear off in the Nadir, against elsewhere.</summary>
    internal const float Recovery = 0.5f;

    /// <summary>How hard gravity pulls in the Nadir, against elsewhere.</summary>
    internal const float Gravity = 2f;

    /// <summary>The share of their usual pace at which ailments wear off here and now.</summary>
    internal static float RecoveryShare => Rules.Enabled(Lvl.NadirRecovery) && Map.InNadir ? Recovery : 1f;

    private static bool HeavyHere => Rules.Enabled(Lvl.NadirGravity) && Map.InNadir;

    /// <summary>The effects that are only bad and run out by themselves.</summary>
    internal static bool WearsOff(Affliction affliction)
    {
        switch (affliction.GetAfflictionType())
        {
            case Affliction.AfflictionType.Blind:
            case Affliction.AfflictionType.Numb:
            case Affliction.AfflictionType.PreventPoisonHealing:
                return true;
            default:
                return false;
        }
    }

    /// <summary>On the scout's own game (the one that counts its effects down): half of each frame is taken back.</summary>
    [HarmonyPatch(typeof(Affliction), nameof(Affliction.Tick))]
    private static class SlowerToWearOff
    {
        private static void Prefix(Affliction __instance)
        {
            if (RecoveryShare >= 1f || __instance.character == null || !__instance.character.IsLocal || !WearsOff(__instance))
            {
                return;
            }
            float back = Time.deltaTime * (1f - RecoveryShare);
            if (__instance.bonusTime > 0f)
            {
                __instance.bonusTime += back;
            }
            else
            {
                __instance.timeElapsed -= back;
            }
        }
    }

    /// <summary>A scout in the air: the game's own pull, twice over (what it holds is pulled the same).</summary>
    [HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.GetGravityForce))]
    private static class HeavierFall
    {
        private static void Postfix(CharacterMovement __instance, ref Vector3 __result)
        {
            if (HeavyHere && !__instance.character.isBot)
            {
                __result *= Gravity;
            }
        }
    }

    /// <summary>A limp scout falls by the world's gravity (the game hands the body over to it): the same once more.</summary>
    [HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.FixedUpdate))]
    private static class HeavierWhenLimp
    {
        private static void Postfix(CharacterMovement __instance)
        {
            Character character = __instance.character;
            if (!HeavyHere || character.isBot)
            {
                return;
            }
            Vector3 extra = Physics.gravity * (Gravity - 1f);
            foreach (Bodypart part in character.refs.ragdoll.partList)
            {
                if (part.rig != null && part.rig.useGravity && !part.rig.isKinematic)
                {
                    part.rig.AddForce(extra, ForceMode.Acceleration);
                }
            }
        }
    }
}
