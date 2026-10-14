using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 18 (13DDA): weather is more extreme, including the lava in the Caldera.
/// <list type="bullet">
/// <item>Wind storms blow 2x (Roots: over 3x) harder; rain makes the ground and walls very slippery
/// and, while you are airborne under open sky, pulls you down harder (an extra 40 m/s², 60 in the
/// Tropics: about twice, in the Tropics three times, the pull of a normal jump); snowstorms chill 20%
/// more in the Alpine segment and at night, and scorch you by day elsewhere.</item>
/// <item>Tornadoes spawn every 20-120 s and pull four times as hard (in the Mesa they also make you drowsy).</item>
/// <item>The Caldera's lava river rises and falls like a tide, synchronised for everyone.</item>
/// <item>The fog becomes poisonous and three times as strong (see <see cref="FogSphereRules"/>).</item>
/// <item>Cold nights chill 1.5 times as fast. In the Alpine, the first and the last quarter of the
/// day are night too (see <see cref="A18_AlpineNights"/>).</item>
/// </list>
/// </summary>
internal static class A18_ExtremeWeather
{
    private const float NightColdMultiplier = 1.5f;

    /// <summary>
    /// How much harder rain pulls a scout who is in the air under open sky (m/s² on the hips). The game's
    /// own pull starts as a slight lift when a jump begins and builds up the longer a scout is in the
    /// air (to 500 after about 3 s of falling); over a normal jump it is about 30, so rain roughly
    /// doubles a jump's pull, triples it in the Tropics.
    /// </summary>
    internal const float RainPull = 40f;
    internal const float TropicsRainPull = 60f;

    private static bool On => Rules.Enabled(Lvl.ExtremeWeather);

    /// <summary>
    /// The game applies the night cold from inside the status update; every cold added there is
    /// night cold. (True Final Ascent rewrites when the cold nights end; this only scales the cold.)
    /// </summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateNormalStatuses))]
    private static class NightColdScope
    {
        [ThreadStatic] internal static int Depth;

        // Runs right before the original, after the other prefixes (which add no cold).
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(CharacterAfflictions __instance, out bool __state)
        {
            __state = On && Rules.Owns(__instance.character);
            if (__state)
            {
                Depth++;
            }
        }

        /// <summary>
        /// The Alpine's dark quarters of the day are cold like the night. Added here, inside the
        /// scope, so it is night cold at ascent 18's rate.
        /// </summary>
        private static void Postfix(CharacterAfflictions __instance, bool __state)
        {
            DayNightManager clock = DayNightManager.instance;
            if (__state && Ascents.isNightCold && clock != null && clock.isDay >= 0.5f && A18_AlpineNights.DarkQuarterNow(clock))
            {
                __instance.AddStatus(CharacterAfflictions.STATUSTYPE.Cold, Time.deltaTime * Ascents.nightColdRate);
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                Depth--;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    private static class HarsherNights
    {
        private static void Prefix(CharacterAfflictions.STATUSTYPE statusType, ref float amount)
        {
            if (NightColdScope.Depth > 0 && statusType == CharacterAfflictions.STATUSTYPE.Cold && amount > 0f)
            {
                amount *= NightColdMultiplier;
            }
        }
    }

    internal static bool IsStorm(WindChillZone zone)
    {
        string name = zone.name;
        return name == "WindStorm" || name == "SnowStorm" || name == "RainStorm";
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.ApplyStatus))]
    private static class Storms
    {
        private static bool Prefix(WindChillZone __instance, Character character)
        {
            if (A25_AllWeather.SuppressZone(__instance))
            {
                return false;
            }
            if (!On || character == null || !Map.Ready || DayNightManager.instance == null || !IsStorm(__instance))
            {
                return true;
            }
            Segment segment = Map.CurrentSegment;
            Place place = Map.CurrentPlace;
            string name = __instance.name;

            if (name == "WindStorm")
            {
                float force = 30f;
                if (place == Place.Roots)
                {
                    force = 50f;
                }
                else if (segment == Segment.TheKiln || segment == Segment.Peak)
                {
                    force = 0f;
                }
                Overrides.Set(__instance, nameof(WindChillZone.windForce), force);
            }
            else if (name == "SnowStorm" && Rules.Enabled(Lvl.AllWeather))
            {
                // "At slightly less effect": outside the Alpine the snowstorm is gentler.
                bool alpine = place == Place.Alpine;
                Overrides.Set(__instance, nameof(WindChillZone.windForce), alpine ? 20f : 12f);
                Overrides.Set(__instance, nameof(WindChillZone.statusApplicationPerSecond), alpine ? 0.07f : 0.01f);
            }

            __instance.windPlayerFactor = WindChillZone.GetWindIntensityAtPoint(character.Center, __instance.lightVolumeSampleThreshold_lower, __instance.lightVolumeSampleThreshold_margin);
            float factor = __instance.windPlayerFactor;
            character.refs.climbing.climbingStamMinimumMultiplier = Mathf.Max(__instance.grabStaminaMultiplierDuringWind * factor * __instance.currentForceMult, 1f);
            float ramp = Mathf.Clamp01(__instance.hasBeenActiveFor * 0.2f);
            float amount = factor * __instance.statusApplicationPerSecond * Time.deltaTime * ramp;

            if (name == "SnowStorm")
            {
                if (segment == Segment.Alpine || Map.IsNight)
                {
                    character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Cold, amount * 1.2f);
                }
                else if (segment != Segment.Caldera)
                {
                    character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, amount);
                }
            }
            else if (__instance.statusApplicationPerSecond > 0f)
            {
                character.refs.afflictions.AddStatus(__instance.statusType, amount);
            }

            if (name == "RainStorm")
            {
                if (character.data.groundedFor > 0.5f || character.data.isClimbing)
                {
                    character.data.slippy = Mathf.Clamp01(Mathf.Max(character.data.slippy, factor * 200f));
                }
                else
                {
                    RaycastHit hit = HelperFunctions.LineCheck(character.Center + new Vector3(0f, 500f, 0f), character.Center, HelperFunctions.LayerType.AllPhysical);
                    if (hit.transform == null || hit.transform.root == character.transform.root)
                    {
                        float pull = segment == Segment.Tropics ? TropicsRainPull : RainPull;
                        character.GetBodypart(BodypartType.Hip).rig.linearVelocity -= new Vector3(0f, pull * Time.deltaTime, 0f);
                    }
                }
            }
            else if (__instance.setSlippy)
            {
                character.data.slippy = Mathf.Clamp01(Mathf.Max(character.data.slippy, factor * 10f));
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(TornadoSpawner), nameof(TornadoSpawner.Update))]
    private static class TornadoFrequency
    {
        private static void Prefix(TornadoSpawner __instance)
        {
            if (!On)
            {
                return;
            }
            Overrides.Set(__instance, nameof(TornadoSpawner.minSpawnTime), 20f);
            Overrides.Set(__instance, nameof(TornadoSpawner.maxSpawnTime), 120f);
            if (__instance.untilNext > 120f)
            {
                __instance.untilNext = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(Tornado), nameof(Tornado.CapturedCharacter))]
    private static class TornadoGrip
    {
        [ThreadStatic] internal static int Depth;

        private static void Prefix(out bool __state)
        {
            __state = On;
            if (__state)
            {
                Depth++;
            }
        }

        private static void Postfix(Tornado __instance, bool __state)
        {
            if (!__state || !Map.InMesa)
            {
                return;
            }
            Character local = Character.localCharacter;
            if (local != null && __instance.caughtCharacters.Contains(local) && !__instance.ignoredCharacters.Contains(local))
            {
                local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Drowsy, 0.05f * Time.deltaTime);
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                Depth--;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddForce), typeof(Vector3), typeof(float), typeof(float))]
    private static class TornadoForce
    {
        private static void Prefix(ref float minRandomMultiplier, ref float maxRandomMultiplier)
        {
            if (TornadoGrip.Depth > 0)
            {
                minRandomMultiplier *= 4f;
                maxRandomMultiplier *= 4f;
            }
        }
    }

    /// <summary>The Caldera's lava river follows a shared tide instead of its local animation.</summary>
    [HarmonyPatch(typeof(Lava), nameof(Lava.Update))]
    private static class TideHeat
    {
        private static bool Prefix(Lava __instance) => CalderaTide.Apply(__instance);
    }

    [HarmonyPatch(typeof(Lava), nameof(Lava.FixedUpdate))]
    private static class TideEffects
    {
        private static bool Prefix(Lava __instance) => CalderaTide.Apply(__instance);
    }
}
