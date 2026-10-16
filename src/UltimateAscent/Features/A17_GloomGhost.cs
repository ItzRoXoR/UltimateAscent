using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;
using GhostBall = global::Peak.GhostBall;
using GhostBallSpawner = global::Peak.GhostBallSpawner;
using GloomSafeZone = global::Peak.GloomSafeZone;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: the Gloom's big ghost. It comes every 30 s (the game: 60-180 s) for the scout who
/// leads, alone or in a group (the game: only someone alone), lives 50 s (the game: 30 s) and
/// explodes 1.5 s after it arms (the game: 2.1 s). Its burst chills for 60 and blinds for 80 s
/// (see <see cref="A17_Hazards"/>). Lit bell towers push it back out of blast range of the scouts
/// they protect, and no longer burn it.
/// </summary>
internal static class A17_GloomGhost
{
    private static bool On => Rules.Enabled(Lvl.Hazards);

    private const float SpawnSeconds = 30f;
    private const float Lifetime = 50f;
    private const float ArmSeconds = 1.5f;

    /// <summary>The game's ghost: armed for 2.1 s, past the point of no return after 1.5 s.</summary>
    private const float GameArmSeconds = 2.1f;
    private const float GameNoGoingBackSeconds = 1.5f;

    /// <summary>The reach of the ghost's burst (the game's VFX_ExplosionGhost).</summary>
    private const float BlastRange = 10f;

    /// <summary>Anyone fully awake below the ghost's ceiling can be its target, whoever is around.</summary>
    [HarmonyPatch(typeof(GhostBallSpawner), nameof(GhostBallSpawner.IsValidTarget))]
    private static class NotOnlyLoners
    {
        private static bool Prefix(GhostBallSpawner __instance, Character c, ref bool __result)
        {
            if (!On)
            {
                return true;
            }
            __result = c != null && c.data.fullyConscious && c.Center.y <= __instance.maxHeightTransform.position.y &&
                       c.Center.z <= __instance.spawnMaxDepthTransform.position.z - __instance.spawnOffset - __instance.spawnOffsetRandomization;
            return false;
        }
    }

    [HarmonyPatch(typeof(GhostBallSpawner), nameof(GhostBallSpawner.UpdateSpawning))]
    private static class EveryFiftySeconds
    {
        private static void Prefix(GhostBallSpawner __instance)
        {
            if (!On)
            {
                return;
            }
            Overrides.Set(__instance, nameof(GhostBallSpawner.timeBetweenSpawns), new Vector2(SpawnSeconds, SpawnSeconds));
            if (__instance.currentSpawnTime > SpawnSeconds)
            {
                __instance.currentSpawnTime = SpawnSeconds;
            }
        }
    }

    [HarmonyPatch(typeof(GhostBall), nameof(GhostBall.Update))]
    private static class Timing
    {
        private static void Prefix(GhostBall __instance)
        {
            if (!On)
            {
                return;
            }
            Overrides.Set(__instance, nameof(GhostBall.lifetime), Lifetime);
            Overrides.Set(__instance, nameof(GhostBall.timeBeforeExplosion), ArmSeconds);
            Overrides.Set(__instance, nameof(GhostBall.timeBeforeNoGoingBackImGonnaExplode), ArmSeconds * GameNoGoingBackSeconds / GameArmSeconds);
        }
    }

    /// <summary>
    /// A ghost hunting a scout under a lit bell tower is pushed back until it is out of blast range
    /// of everyone the tower protects. While it is pushed it cannot arm (the game's own rule while a
    /// tower burns it), but it takes no damage.
    /// </summary>
    [HarmonyPatch(typeof(GhostBall), nameof(GhostBall.UpdateMovement))]
    private static class RepelOutOfReach
    {
        private static void Postfix(GhostBall __instance)
        {
            if (!On || __instance.risingFromSpawn || __instance.target == null ||
                !GloomSafeZone.CharacterProtectedFromStatus(__instance.target, out GloomSafeZone zone, out _) || zone == null)
            {
                return;
            }
            Vector3 away = __instance.transform.position - zone.transform.position;
            float distance = away.magnitude;
            // The game itself repels inside 1.6x the tower's radius.
            if (distance <= zone.statusProtectionRadius * 1.6f || distance >= zone.statusProtectionRadius + BlastRange)
            {
                return;
            }
            __instance.burnTick = 0.5f;
            __instance.rig.AddForce(away.normalized * __instance.repelForce * Time.deltaTime);
        }
    }

    [HarmonyPatch(typeof(GhostBall), nameof(GhostBall.UpdateLifetime))]
    private static class NoBurning
    {
        private static void Prefix(GhostBall __instance, out float? __state)
        {
            __state = On ? __instance.burnHealth : (float?)null;
        }

        private static void Postfix(GhostBall __instance, float? __state)
        {
            if (!__state.HasValue)
            {
                return;
            }
            __instance.burnHealth = __state.Value;
            if (__instance.flame != null && __instance.flame.isPlaying)
            {
                __instance.flame.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    [HarmonyPatch(typeof(GhostBall), nameof(GhostBall.UpdateAnimations))]
    private static class NoBurningLook
    {
        private static void Postfix(GhostBall __instance)
        {
            if (On && __instance.anim != null)
            {
                __instance.anim.SetBool(__instance.AN_BURNING, __instance._playedBurnDeath);
            }
        }
    }
}
