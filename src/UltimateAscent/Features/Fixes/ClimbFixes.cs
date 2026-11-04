using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features.Fixes;

/// <summary>
/// Game bugs of climbing and stamina, fixed on every ascent (the user's list of 2026-11-04).
/// <list type="bullet">
/// <item>The fog walls between the segments (a picture of fog with an invisible slab behind it, see
/// <see cref="Map.IsFogWall"/>) could be climbed and stood on: the hands' look for rock goes through the slab, so a
/// scout pressed against the fog held the rock behind it, and two slabs (the Gloom's back wall among them) lie flat
/// enough for the game to take as ground, to stand on and get stamina back. Now nothing is held through a fog wall
/// and it is no ground.</item>
/// <item>Lava gave stamina: a lava pillar's lava has 10 stamina back for a scout it bounces off, like a mushroom (the
/// user's report: climbing up a lava stream gave stamina). No lava gives stamina now (it stays climbable; the user's
/// choice).</item>
/// <item>A scout a condor carries could grab a rope or a vine (the game only stops the rope they hold as it picks
/// them up, and a grab of the wall meanwhile) and was pulled back to it when the flight ended. Now they cannot grab
/// one while carried, and let go of one they still hold.</item>
/// <item>Stuck on a cactus, a scout got stamina back the whole time (20 a second, whatever they were doing), so
/// sticking to a cactus beside a wall and breaking free again climbed the wall forever. Stuck, stamina now comes back
/// only as it would unstuck: on the ground, after the usual pause.</item>
/// </list>
/// </summary>
internal static class ClimbFixes
{
    /// <summary>For the tests: grabs refused through a fog wall, and touches of a fog wall not taken as ground.</summary>
    internal static int GrabsRefused;
    internal static int GroundRefused;

    [HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.OnCollision))]
    private static class FogWallNoGround
    {
        private static bool Prefix(Collision collision)
        {
            if (collision == null || !Map.IsFogWall(collision.collider))
            {
                return true;
            }
            GroundRefused++;
            return false;
        }
    }

    /// <summary>Whether a scout holding this spot would hold it through a fog wall (the line from their middle to it passes into one).</summary>
    private static bool HeldThroughFog(Character scout, Vector3 spot) => scout != null && Map.ThroughFogWall(scout.Center, spot);

    [HarmonyPatch(typeof(CharacterClimbing), "StartClimbRpc")]
    private static class NoGrabThroughFog
    {
        private static bool Prefix(Character ___character, Vector3 climbPos)
        {
            if (!HeldThroughFog(___character, climbPos))
            {
                return true;
            }
            GrabsRefused++;
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterClimbing), "SampleWall")]
    private static class NoClimbThroughFog
    {
        private static void Postfix(Character ___character, ref RaycastHit __result)
        {
            if (__result.transform != null && HeldThroughFog(___character, __result.point))
            {
                // No wall there: the climb ends as on any wall that runs out.
                GrabsRefused++;
                __result = default;
            }
        }
    }

    private static readonly ConditionalWeakTable<CollisionModifier, StrongBox<bool>> lava = new ConditionalWeakTable<CollisionModifier, StrongBox<bool>>();

    /// <summary>
    /// Lava's touch: a lava river's, or lava by its name or the name of what it is part of (a lava pillar's lava, which
    /// does no harm of its own, is told only by that).
    /// </summary>
    internal static bool IsLava(CollisionModifier touch)
    {
        if (touch == null)
        {
            return false;
        }
        if (!lava.TryGetValue(touch, out StrongBox<bool> known))
        {
            bool named = false;
            for (Transform part = touch.transform; part != null && !named; part = part.parent)
            {
                named = part.name.IndexOf("Lava", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            known = new StrongBox<bool>(named || touch.GetComponentInParent<LavaRiver>() != null);
            lava.Add(touch, known);
        }
        return known.Value;
    }

    [HarmonyPatch(typeof(CollisionModifier), nameof(CollisionModifier.Collide))]
    private static class NoStaminaFromLava
    {
        private static void Prefix(CollisionModifier __instance)
        {
            if (IsLava(__instance) && __instance.staminaBackOnBounce != 0f)
            {
                Overrides.Set(__instance, nameof(CollisionModifier.staminaBackOnBounce), 0f);
            }
        }
    }

    [HarmonyPatch(typeof(RopeSegment), nameof(RopeSegment.Interact))]
    private static class NoRopeInACondor
    {
        private static bool Prefix(Character interactor) => interactor == null || interactor.data.carrierCondor == null;
    }

    [HarmonyPatch(typeof(JungleVine), nameof(JungleVine.Interact))]
    private static class NoVineInACondor
    {
        private static bool Prefix(Character interactor) => interactor == null || interactor.data.carrierCondor == null;
    }

    /// <summary>For the tests: ropes and vines let go of because a condor carried the scout.</summary>
    internal static int CondorLetGo;

    [HarmonyPatch(typeof(CharacterRopeHandling), "Update")]
    private static class LetGoOfRopeInACondor
    {
        private static void Prefix(CharacterRopeHandling __instance, Character ___character)
        {
            if (___character != null && ___character.photonView.IsMine && ___character.data.isRopeClimbing && ___character.data.carrierCondor != null)
            {
                CondorLetGo++;
                __instance.Stop();
            }
        }
    }

    [HarmonyPatch(typeof(CharacterVineClimbing), "Update")]
    private static class LetGoOfVineInACondor
    {
        private static void Prefix(CharacterVineClimbing __instance, Character ___character)
        {
            if (___character != null && ___character.photonView.IsMine && ___character.data.isVineClimbing && ___character.data.carrierCondor != null)
            {
                CondorLetGo++;
                __instance.Stop();
            }
        }
    }

    [HarmonyPatch(typeof(Character), "CanRegenStamina")]
    private static class NoStaminaStuck
    {
        private static void Postfix(Character __instance, ref bool __result)
        {
            CharacterData data = __instance.data;
            if (!__result || data == null || data.currentClimbHandle != null || !__instance.IsStuck())
            {
                return;
            }
            // As unstuck: not while off the ground (the game holds a stuck scout's time off it at 0.5 s), nor before the pause after spending stamina.
            float pause = data.currentStamina > 0f ? 1f : 2f;
            __result = !((data.sinceGrounded > 0.2f && !__instance.refs.afflictions.isWebbed) || data.sinceUseStamina < pause);
        }
    }
}
