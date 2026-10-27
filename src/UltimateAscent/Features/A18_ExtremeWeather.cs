using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 18 (13DDA): weather is more extreme, including the lava in the Caldera.
/// <list type="bullet">
/// <item>Wind storms blow 1.5x (Roots: 2.5x) harder; rain makes the ground and walls very slippery
/// and, while you are airborne under open sky, pulls you down harder (an extra 40 m/s², 60 in the
/// Tropics: about twice, in the Tropics three times, the pull of a normal jump); snowstorms chill 20%
/// more in the Alpine segment and at night, and scorch you by day elsewhere, a heat only sunscreen keeps
/// off (from ascent 15 all but a quarter of it). While a snowstorm blows where you are, no sun burns
/// through it: the scorching sun (the Mesa's, and ascent 23's everywhere) neither heats you nor roasts
/// what shields you.</item>
/// <item>Tornadoes spawn every 20-120 s; one that catches you spins, drags and lifts you 2.5 times as
/// hard (in the Mesa it also makes you drowsy).</item>
/// <item>The Mesa's midday sun burns 6 heat a second; the Gloom's fog makes you 2.5 drowsy a second.</item>
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

    /// <summary>How much harder a tornado spins, drags and lifts the scouts it has caught.</summary>
    internal const float TornadoPull = 2.5f;

    /// <summary>The Mesa's midday sun, a second (the game: about 1.6, 2.4 with ascent 14's boost).</summary>
    internal const float MesaSunHeat = 0.06f;

    /// <summary>The Gloom's fog, a second (the game: 0.9, 1.17 with ascent 14's boost).</summary>
    internal const float GloomFogDrowsiness = 0.025f;

    private static float mesaSunTime;

    /// <summary>The last frame a snowstorm blew on the local scout (the game applies a storm only while it blows where the scout is).</summary>
    private static int snowstormFrame = -10;

    private static bool On => Rules.Enabled(Lvl.ExtremeWeather);

    /// <summary>
    /// A snowstorm blows where the local scout is (this frame or the last), so the scorching sun does not burn
    /// through it (see <see cref="MesaSun"/> and A23_HotSun); the storm's own daytime heat takes its place. The
    /// two meet where ascent 25's storms reach the sun.
    /// </summary>
    internal static bool SnowstormHidesSun => On && Time.frameCount - snowstormFrame <= 1;

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

    private static bool Temperature(CharacterAfflictions.STATUSTYPE status) => status == CharacterAfflictions.STATUSTYPE.Cold || status == CharacterAfflictions.STATUSTYPE.Hot;

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
            if (character == null || !Map.Ready || DayNightManager.instance == null || !IsStorm(__instance))
            {
                return true;
            }
            Segment segment = Map.CurrentSegment;
            Place place = Map.CurrentPlace;
            string name = __instance.name;
            if (name == "SnowStorm")
            {
                snowstormFrame = Time.frameCount;
            }

            if (name == "SnowStorm" && Rules.Enabled(Lvl.AllWeather))
            {
                // Ascent 25, "at slightly less effect": outside the Alpine the snowstorm is gentler (with
                // ascent 18's rule or without it).
                bool alpine = place == Place.Alpine;
                Overrides.Set(__instance, nameof(WindChillZone.windForce), alpine ? 20f : 12f);
                Overrides.Set(__instance, nameof(WindChillZone.statusApplicationPerSecond), alpine ? 0.07f : 0.01f);
            }
            // Ascent 25: from the Caldera and the Gloom on (no day or night there) a storm neither chills nor heats.
            bool noTemperature = Rules.Enabled(Lvl.AllWeather) && (place == Place.Caldera || place == Place.Gloom);
            if (!On)
            {
                if (!noTemperature)
                {
                    return true;
                }
                // The game's own storm, without its cold or heat.
                __instance.windPlayerFactor = WindChillZone.GetWindIntensityAtPoint(character.Center, __instance.lightVolumeSampleThreshold_lower, __instance.lightVolumeSampleThreshold_margin);
                character.refs.climbing.climbingStamMinimumMultiplier = Mathf.Max(__instance.grabStaminaMultiplierDuringWind * __instance.windPlayerFactor * __instance.currentForceMult, 1f);
                if (__instance.statusApplicationPerSecond > 0f && !Temperature(__instance.statusType))
                {
                    character.refs.afflictions.AddStatus(__instance.statusType, __instance.windPlayerFactor * __instance.statusApplicationPerSecond * Time.deltaTime * Mathf.Clamp01(__instance.hasBeenActiveFor * 0.2f));
                }
                if (__instance.setSlippy)
                {
                    character.data.slippy = Mathf.Clamp01(Mathf.Max(character.data.slippy, __instance.windPlayerFactor * 10f));
                }
                return false;
            }

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

            __instance.windPlayerFactor = WindChillZone.GetWindIntensityAtPoint(character.Center, __instance.lightVolumeSampleThreshold_lower, __instance.lightVolumeSampleThreshold_margin);
            float factor = __instance.windPlayerFactor;
            character.refs.climbing.climbingStamMinimumMultiplier = Mathf.Max(__instance.grabStaminaMultiplierDuringWind * factor * __instance.currentForceMult, 1f);
            float ramp = Mathf.Clamp01(__instance.hasBeenActiveFor * 0.2f);
            float amount = factor * __instance.statusApplicationPerSecond * Time.deltaTime * ramp;

            if (name == "SnowStorm")
            {
                if (noTemperature)
                {
                    // Neither cold nor heat (see above).
                }
                else if (segment == Segment.Alpine || Map.IsNight)
                {
                    character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Cold, amount * 1.2f);
                }
                else if (segment != Segment.Caldera)
                {
                    // Only sunscreen keeps this heat off (from ascent 15 all but a quarter); a parasol or a glider does not.
                    if (!character.data.wearingSunscreen)
                    {
                        character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, amount);
                    }
                    else if (A14_Items.SunscreenLeaks)
                    {
                        character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, amount * A14_Items.SunscreenLeak);
                    }
                }
            }
            else if (__instance.statusApplicationPerSecond > 0f && !(noTemperature && Temperature(__instance.statusType)))
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

    /// <summary>
    /// A tornado that has caught a scout spins, drags and lifts them 2.5 times as hard. The force itself is
    /// scaled: the game ignores the random multipliers when both are the same.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.AddForce), typeof(Vector3), typeof(float), typeof(float))]
    private static class TornadoForce
    {
        private static void Prefix(ref Vector3 move)
        {
            if (TornadoGrip.Depth > 0)
            {
                move *= TornadoPull;
            }
        }
    }

    /// <summary>
    /// The Mesa's midday sun (the game's own, in its box over the Mesa, while the sun is at full strength and
    /// nothing shades the scout from it) burns 6 heat a second, exactly as it lands on the bar. An open
    /// parasol or a glider held overhead still keeps it off, and sunscreen too (but for the quarter ascent 15
    /// lets through); a snowstorm blowing on the scout hides it. Not while True Final Ascent runs a weather;
    /// in its Apocalypse mode TFA switches the game's sun off and burns with its own.
    /// </summary>
    [HarmonyPatch(typeof(HotSun), nameof(HotSun.Update))]
    private static class MesaSun
    {
        private static bool Prefix(HotSun __instance)
        {
            if (!On || !Application.isPlaying || Compat.Tfa.WeatherRunning || !Rules.Hazard(RunSettings.SETTINGTYPE.Hazard_Heat))
            {
                return true;
            }
            if (SnowstormHidesSun)
            {
                return false;
            }
            __instance.bounds.center = __instance.transform.position;
            __instance.bounds.size = __instance.transform.localScale;
            Character local = Character.localCharacter;
            DayNightManager clock = DayNightManager.instance;
            if (local == null || clock == null || clock.sun == null || !__instance.bounds.Contains(local.Center) || clock.sun.intensity < 5f)
            {
                return false;
            }
            RaycastHit shade = HelperFunctions.LineCheck(local.Center + clock.sun.transform.forward * -1000f, local.Center, HelperFunctions.LayerType.AllPhysical);
            if (shade.transform != null && shade.transform.root != local.transform.root)
            {
                return false;
            }
            mesaSunTime += Time.deltaTime;
            if (mesaSunTime <= __instance.rate)
            {
                return false;
            }
            float seconds = mesaSunTime;
            mesaSunTime = 0f;
            Item held = local.data.currentItem;
            if (held != null && ((held.TryGetComponent(out Parasol parasol) && parasol.isOpen) || held.gliderHold))
            {
                return false;
            }
            float heat = MesaSunHeat * seconds;
            if (local.data.wearingSunscreen)
            {
                if (!A14_Items.SunscreenLeaks)
                {
                    return false;
                }
                heat *= A14_Items.SunscreenLeak;
            }
            ExactStatus.Add(local.refs.afflictions, CharacterAfflictions.STATUSTYPE.Hot, heat, "mesa sun");
            return false;
        }
    }

    /// <summary>
    /// The Gloom's fog (the game's own sleepy fog over the Gloom, not the Citadel's rising gloom) makes the
    /// scouts whose heads are in it 2.5 drowsy a second, exactly as it lands on the bar; the game's safe
    /// zones (lit bell towers, campfires, stoves, candles, Faerie Lanterns) still keep it off.
    /// </summary>
    [HarmonyPatch(typeof(Peak.StatusFieldGloom), nameof(Peak.StatusFieldGloom.TickStatus))]
    private static class GloomFog
    {
        private static bool Prefix(Peak.StatusFieldGloom __instance)
        {
            Character local = Character.localCharacter;
            if (!On || local == null || __instance.GetComponentInParent<LavaRising>() != null)
            {
                return true;
            }
            if (RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_SleepyGloom) == 0)
            {
                return false;
            }
            ExactStatus.Add(local.refs.afflictions, CharacterAfflictions.STATUSTYPE.Drowsy, GloomFogDrowsiness * Time.deltaTime, "gloom fog");
            if (__instance.additionalStatuses != null)
            {
                foreach (Peak.StatusFieldBase.StatusFieldStatus other in __instance.additionalStatuses)
                {
                    local.refs.afflictions.AdjustStatus(other.statusType, __instance.statusAmountPerSecond * Time.deltaTime);
                }
            }
            return false;
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
