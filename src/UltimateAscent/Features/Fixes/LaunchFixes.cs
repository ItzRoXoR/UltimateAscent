using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Peak;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features.Fixes;

/// <summary>
/// Game bugs that launched a scout absurdly far, fixed on every ascent (the user's list of 2026-11-04).
/// <list type="bullet">
/// <item>The Jetpack in an explosion or a Rescue Claw's pull shot a scout sky high: a scout knocked limp has no air
/// drag, and the Jetpack pushed them at full strength all the same. It now gives nothing while they are knocked down,
/// and as much as they have their body back after; its push was also added once a drawn frame and spent once a
/// physics step, so it pushed as much harder as the frame rate is over 60 (2.4 times at 144 fps). It now pushes as at
/// 60 fps at any frame rate (the user's choice).</item>
/// <item>A cooked Anti-Zooka's bubble with another under the scout lifted them enormously high: every bubble lifted
/// them and ran their anti-gravity clock on, and every limb out at a bubble's edge started another push up out of it.
/// Now one bubble lifts a scout at a time, and a scout leaves a bubble only once their middle is out of it.</item>
/// <item>A jumping mushroom bounce whose scout goes limp on it (the Chain Launcher's chain run out of stamina, then
/// playing dead) kept the full strength of a bounce after a long fall with no drag to slow it: now a limp scout gets
/// the game's own limp bounce, as if they had been limp when they landed (the speed the bounce gave them before they
/// went limp brought down to the same share). Going limp later in the bounce's flight took the air's drag off them as
/// well (a limp scout has none), and they flew on far higher than the bounce: a scout a mushroom bounced keeps their
/// drag while they rise from it.</item>
/// <item>A backpack fired from the Scout Cannon (at 5,000 m/s) went through the scout in the tube and threw them across
/// the whole mountain: it now flies off without them.</item>
/// </list>
/// (From ascent 15 the Scout Cannon is also thrown with half the force: <see cref="A14_WorseItems"/>.)
/// </summary>
internal static class LaunchFixes
{
    /// <summary>A scout this little back in control of their body counts as limp for a mushroom's bounce (the game: knocked down or out).</summary>
    internal const float LimpBelow = 0.9f;

    /// <summary>For the tests: jetpack pushes the knock-down held back, limp bounces eased, backpacks fired alone.</summary>
    internal static int JetpackHeld;
    internal static int LimpBouncesEased;
    internal static int BackpacksAlone;

    internal static void Reset()
    {
        lifted.Clear();
    }

    [HarmonyPatch(typeof(CharacterMovement), "TryJetpack")]
    private static class NoJetpackKnockedDown
    {
        private static bool Prefix(Character ___character, ref bool __result)
        {
            if (___character == null || ___character.data.fallSeconds <= 0f)
            {
                return true;
            }
            JetpackHeld++;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.JetpackRPC))]
    private static class SteadyJetpack
    {
        private static void Prefix(CharacterMovement __instance, Character ___character)
        {
            float original = Overrides.Original(__instance, nameof(CharacterMovement.jetpackForce), __instance.jetpackForce);
            float control = ___character != null ? Mathf.Clamp01(___character.data.currentRagdollControll) : 1f;
            // A push each drawn frame, spent once a physics step: on the scout's own game, as at 60 fps (others' copies follow theirs).
            float pace = ___character != null && ___character.IsLocal ? Mathf.Clamp(Time.deltaTime / Time.fixedDeltaTime, 0f, 3f) : 1f;
            Overrides.Set(__instance, nameof(CharacterMovement.jetpackForce), original * control * pace);
        }
    }

    [HarmonyPatch(typeof(AntiSphere), "OnTriggerExit")]
    private static class LeaveBubbleWhenOut
    {
        private static bool Prefix(AntiSphere __instance, Collider other, List<Character> ___ACTIVE_CHARACTERS)
        {
            if (!CharacterRagdoll.TryGetCharacterFromCollider(other, out Character scout) || scout == null || !___ACTIVE_CHARACTERS.Contains(scout))
            {
                return true;
            }
            // A limb out at the edge with the scout still in the bubble is no leaving (nor a push out of it).
            return Vector3.Distance(__instance.transform.position, scout.Center) >= __instance.coll.radius * __instance.transform.lossyScale.x;
        }
    }

    /// <summary>Which bubble lifted each scout last, and in which physics step.</summary>
    private static readonly Dictionary<Character, (float at, AntiSphere by)> lifted = new Dictionary<Character, (float, AntiSphere)>();

    [HarmonyPatch(typeof(AntiSphere), "FixedUpdate")]
    private static class OneBubbleAtATime
    {
        private static void Prefix(AntiSphere __instance, List<Character> ___ACTIVE_CHARACTERS, out List<Character> __state)
        {
            __state = null;
            for (int i = ___ACTIVE_CHARACTERS.Count - 1; i >= 0; i--)
            {
                Character scout = ___ACTIVE_CHARACTERS[i];
                if (scout == null)
                {
                    continue;
                }
                if (lifted.TryGetValue(scout, out (float at, AntiSphere by) last) && last.at == Time.fixedTime && last.by != null && last.by != __instance)
                {
                    // Another bubble has lifted them this step: this one leaves them be.
                    (__state ??= new List<Character>()).Add(scout);
                    ___ACTIVE_CHARACTERS.RemoveAt(i);
                }
                else
                {
                    lifted[scout] = (Time.fixedTime, __instance);
                }
            }
        }

        private static Exception Finalizer(List<Character> ___ACTIVE_CHARACTERS, List<Character> __state, Exception __exception)
        {
            if (__state != null)
            {
                foreach (Character scout in __state.Where(s => !___ACTIVE_CHARACTERS.Contains(s)))
                {
                    ___ACTIVE_CHARACTERS.Add(scout);
                }
            }
            return __exception;
        }
    }

    /// <summary>Whether a scout is limp for a mushroom's bounce: knocked down or out, or not yet back in their body after it.</summary>
    private static bool Limp(Character scout) => scout.data.fallSeconds > 0f || scout.refs.afflictions.shouldPassOut || scout.data.currentRagdollControll < LimpBelow;

    [HarmonyPatch(typeof(CollisionModifier), nameof(CollisionModifier.Collide))]
    private static class LimpLanding
    {
        private static void Prefix(CollisionModifier __instance, Character character)
        {
            // The game gives a knocked-down scout its plain bounce; one not yet back in their body after it gets the same.
            if (character != null && __instance.knockback > 0f && Limp(character) && !(character.data.fallSeconds > 0f || character.refs.afflictions.shouldPassOut))
            {
                character.data.sinceGrounded = Mathf.Min(character.data.sinceGrounded, 1f - __instance.sinceGroundedOffset);
            }
        }
    }

    [HarmonyPatch]
    private static class LimpMidBounce
    {
        private static FieldInfo push;
        private static FieldInfo closure;
        private static FieldInfo bounced;

        private static bool Prepare() => TargetMethod() != null;

        private static MethodBase TargetMethod()
        {
            Type outer = AccessTools.FirstInner(typeof(CollisionModifier), t => t.GetNestedTypes(AccessTools.all).Any(n => n.Name.Contains("BounceRoutine")));
            Type routine = outer?.GetNestedTypes(AccessTools.all).FirstOrDefault(n => n.Name.Contains("BounceRoutine"));
            push = routine != null ? AccessTools.Field(routine, "kb") : null;
            closure = routine != null ? AccessTools.Field(routine, "<>4__this") : null;
            bounced = outer != null ? AccessTools.Field(outer, "character") : null;
            return push != null && closure != null && bounced != null ? AccessTools.Method(routine, "MoveNext") : null;
        }

        private static void Prefix(object __instance)
        {
            if (!(bounced.GetValue(closure.GetValue(__instance)) is Character scout) || scout == null || !Limp(scout))
            {
                return;
            }
            // The bounce's strength was taken as the scout landed; gone limp since, they get the plain bounce.
            Vector3 kb = (Vector3)push.GetValue(__instance);
            if (kb.sqrMagnitude > 1.02f)
            {
                TakeBack(scout, kb.normalized, 1f / kb.magnitude);
                push.SetValue(__instance, kb.normalized);
                LimpBouncesEased++;
            }
        }

        /// <summary>
        /// What the bounce gave so far, brought down to the plain bounce's share: the speed along it, and its push not
        /// yet spent. (Limp, nothing slows that speed any more: kept, the steps before the scout went limp alone would
        /// throw them high.)
        /// </summary>
        private static void TakeBack(Character scout, Vector3 along, float share)
        {
            foreach (Bodypart part in scout.refs.ragdoll.partList)
            {
                Rigidbody rig = part != null ? part.Rig : null;
                if (rig == null || rig.isKinematic)
                {
                    continue;
                }
                float speed = Vector3.Dot(rig.linearVelocity, along);
                if (speed > 0f)
                {
                    rig.linearVelocity -= along * (speed * (1f - share));
                }
                float pending = Vector3.Dot(part.forcesToAdd, along);
                if (pending > 0f)
                {
                    part.forcesToAdd -= along * (pending * (1f - share));
                }
            }
        }
    }

    /// <summary>A bounce's flight: this long after a mushroom bounced a scout, while they rise.</summary>
    private const float BounceFlightSeconds = 2.5f;

    /// <summary>For the tests: physics steps a limp scout rising from a bounce kept their drag.</summary>
    internal static int LimpFlightDragKept;

    [HarmonyPatch(typeof(Bodypart), nameof(Bodypart.Drag))]
    private static class LimpFlightKeepsDrag
    {
        private static void Prefix(Bodypart __instance, ref bool ignoreRagdoll, Character ___character)
        {
            if (ignoreRagdoll || ___character == null)
            {
                return;
            }
            CharacterData data = ___character.data;
            if (data.currentRagdollControll < 1f && Time.time - data.lastBouncedTime < BounceFlightSeconds && __instance.Rig != null && __instance.Rig.linearVelocity.y > 0f)
            {
                // As in control: the drag the game fades out as a scout goes limp.
                ignoreRagdoll = true;
                LimpFlightDragKept++;
            }
        }
    }

    [HarmonyPatch(typeof(ScoutCannon), nameof(ScoutCannon.RPCA_LaunchItem))]
    private static class BackpackFliesAlone
    {
        private static void Prefix(int targetID, Transform ___tube)
        {
            PhotonView view = PhotonNetwork.GetPhotonView(targetID);
            Item item = view != null ? view.GetComponent<Item>() : null;
            if (!(item is Backpack) || ___tube == null || GameUtils.instance == null)
            {
                return;
            }
            foreach (Character scout in Character.AllCharacters.Where(c => c != null && Vector3.Distance(c.Center, ___tube.position) < 3f))
            {
                GameUtils.instance.IgnoreCollisions(scout, item, 2f);
            }
            BackpacksAlone++;
        }
    }
}
