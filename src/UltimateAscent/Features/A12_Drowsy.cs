using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 12 (13DDA + More Ascents): falling and helping people make you drowsy.
/// Taking fall damage makes you 60 drowsy (46.2 below ascent 15, without its boost). Pulling someone up makes you
/// 10 drowsy a second while you help (7.7 below ascent 15). Falls can no longer be
/// cancelled by snapping the ragdoll to its animation (e.g. by switching items mid-air).
/// </summary>
internal static class A12_Drowsy
{
    /// <summary>
    /// Drowsiness from fall damage and per second of helping, as they land from ascent 15 on (below 15
    /// without that ascent's boost: 46.2 and 7.7).
    /// </summary>
    internal const float FallDrowsiness = 0.6f;
    internal const float HelpDrowsyPerSecond = 0.1f;

    private static float lastHelpTime = -10f;

    internal static void FallAsleep(Character character)
    {
        if (!Rules.Owns(character))
        {
            return;
        }
        CharacterAfflictions.STATUSTYPE drowsy = CharacterAfflictions.STATUSTYPE.Drowsy;
        ExactStatus.Add(character.refs.afflictions, drowsy, A15_StrongerAilments.FromAscent15(drowsy, FallDrowsiness), "fall");
    }

    internal static float AilmentSum(CharacterAfflictions afflictions)
    {
        return afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Poison)
               + afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Drowsy)
               + afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury)
               + afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hunger)
               + afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Cold)
               + afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hot);
    }

    /// <summary>Helping somebody up (reaching out to a scout who can be pulled) makes the helper drowsy.</summary>
    [HarmonyPatch(typeof(CharacterGrabbing), nameof(CharacterGrabbing.Reach))]
    private static class HelpingIsTiring
    {
        private static void Postfix(CharacterGrabbing __instance)
        {
            Character helper = __instance.character;
            if (!Rules.Enabled(Lvl.Drowsy) || !Rules.Owns(helper) || Time.time - lastHelpTime < 1f)
            {
                return;
            }
            foreach (Character other in Character.AllCharacters)
            {
                if (other == null || other == helper)
                {
                    continue;
                }
                if (Vector3.Distance(helper.Center, other.Center) > 4f || Vector3.Angle(helper.data.lookDirection, other.Center - helper.Center) > 60f)
                {
                    continue;
                }
                if (CharacterGrabbing.TargetCanBeHelped(other, helper))
                {
                    lastHelpTime = Time.time;
                    CharacterAfflictions.STATUSTYPE drowsy = CharacterAfflictions.STATUSTYPE.Drowsy;
                    ExactStatus.Add(helper.refs.afflictions, drowsy, A15_StrongerAilments.FromAscent15(drowsy, HelpDrowsyPerSecond), "help");
                    return;
                }
            }
        }
    }

    /// <summary>Snapping the ragdoll to its animation keeps the fall's momentum (no fall cancelling).</summary>
    [HarmonyPatch(typeof(Bodypart), nameof(Bodypart.SnapToAnim))]
    private static class NoFallCancel
    {
        internal struct Motion
        {
            internal Rigidbody rig;
            internal Vector3 linear;
            internal Vector3 angular;
        }

        private static void Prefix(Bodypart __instance, out Motion __state)
        {
            __state = default;
            if (!Rules.Enabled(Lvl.Drowsy))
            {
                return;
            }
            Character owner = __instance.character != null ? __instance.character : __instance.GetComponentInParent<Character>();
            Rigidbody rig = __instance.rig;
            if (!Rules.Owns(owner) || rig == null || rig.isKinematic)
            {
                return;
            }
            __state = new Motion { rig = rig, linear = rig.linearVelocity, angular = rig.angularVelocity };
        }

        private static void Postfix(Motion __state)
        {
            if (__state.rig != null && !__state.rig.isKinematic)
            {
                __state.rig.linearVelocity = __state.linear;
                __state.rig.angularVelocity = __state.angular;
            }
        }
    }
}
