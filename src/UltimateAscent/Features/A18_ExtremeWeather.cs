using System;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 18 (13DDA): weather is more extreme, including the lava in the Caldera.
/// <list type="bullet">
/// <item>Wind storms blow 1.5x (Roots: 2.5x) harder; in the rain (see <see cref="WetGrab"/>) a grab taken while
/// already on the wall holds no higher than where the hands let go, so letting go and grabbing higher is slower
/// than climbing on (climbing on is as in the game's rain; at ascent 25 outside the Tropics a little easier, see
/// <see cref="WetWalls"/>); vines get slippery and take more stamina,
/// most in the Tropics (see <see cref="WetVines"/>); gravity and jumps are left as they are. Snowstorms chill 20%
/// more in the Alpine segment and at night, and scorch you by day elsewhere, a heat only sunscreen keeps
/// off (from ascent 15 all but a quarter of it). While a snowstorm blows where you are, no sun burns
/// through it: the scorching sun (the Mesa's, and ascent 23's everywhere) neither heats you nor roasts
/// what shields you.</item>
/// <item>Tornadoes spawn every 20-120 s; one that catches you spins, drags and lifts you 2.5 times as
/// hard (in the Mesa it also makes you drowsy).</item>
/// <item>The Mesa's midday sun burns 6 heat a second; the Gloom's fog makes you 2.5 drowsy a second, the
/// Citadel's rising gloom 5 a second.</item>
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
    /// How much of a full-speed climb the game's rain takes off a scout climbing on: half. Ascent 18 leaves it so, in the
    /// Tropics and elsewhere.
    /// </summary>
    internal const float GameWetSlide = 0.5f;

    /// <summary>
    /// Ascent 25's rain outside the Tropics (the rain's own biome) takes only this much of a full-speed climb off a scout
    /// climbing on, so it gains 60% of a dry climb there, against half in the Tropics (the user's).
    /// </summary>
    internal const float OutsideTropicsSlide = 0.4f;

    /// <summary>
    /// How hard a scout on a vine slides down it in the rain where it hangs straight down, against a full-speed climb:
    /// in the Tropics, and elsewhere.
    /// </summary>
    internal const float VineSlide = 0.6f;
    internal const float VineSlideElsewhere = 0.3f;

    /// <summary>How many times the stamina a vine takes (climbing along it, or hanging on) in the rain: in the Tropics, and elsewhere.</summary>
    internal const float WetVineStamina = 2f;
    internal const float WetVineStaminaElsewhere = 1.5f;

    /// <summary>How much harder a tornado spins, drags and lifts the scouts it has caught.</summary>
    internal const float TornadoPull = 2.5f;

    /// <summary>The Mesa's midday sun, a second (the game: about 1.6, 2.4 with ascent 14's boost).</summary>
    internal const float MesaSunHeat = 0.06f;

    /// <summary>The Gloom's fog, a second (the game: 0.9, 1.17 with ascent 14's boost).</summary>
    internal const float GloomFogDrowsiness = 0.025f;

    /// <summary>The Citadel's rising gloom, a second (the game: 0.9, 1.17 with ascent 14's boost).</summary>
    internal const float CitadelGloomDrowsiness = 0.05f;

    private static float mesaSunTime;

    /// <summary>The last frame a snowstorm blew on the local scout (the game applies a storm only while it blows where the scout is).</summary>
    private static int snowstormFrame = -10;

    /// <summary>
    /// The last frame the game's rain fell on the local scout, how wet it made the walls (the game's own slipperiness), and
    /// whether that was in the Tropics, the rain's own biome (elsewhere it is weaker).
    /// </summary>
    private static int rainFrame = -10;
    private static float rainWetness;
    private static bool rainInTropics;

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
            // True Final Ascent's Builder: its rest stops spare the resting scouts (TFA's own guard would come after this).
            if (Compat.Tfa.RestSpares(character))
            {
                __instance.windPlayerFactor = 0f;
                return false;
            }
            Place place = Map.CurrentPlace;
            // The game's segments, as a regular map has them: the Alpine's holds the Mesa, the Caldera's the Gloom.
            bool alpineSegment = place == Place.Alpine || place == Place.Mesa;
            bool calderaSegment = place == Place.Caldera || place == Place.Gloom;
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
            // Ascent 25: from the Caldera and the Gloom on, up to the Peak (no day or night there), a storm neither chills nor heats.
            bool noTemperature = Rules.Enabled(Lvl.AllWeather) && A25_AllWeather.UpperMountain(place);
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
                float force = place == Place.Roots ? 50f : 30f;
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
                else if (alpineSegment || Map.IsNight)
                {
                    character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Cold, amount * 1.2f);
                }
                else if (!calderaSegment)
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

            if (__instance.setSlippy)
            {
                character.data.slippy = Mathf.Clamp01(Mathf.Max(character.data.slippy, factor * 10f));
            }
            if (name == "RainStorm")
            {
                rainFrame = Time.frameCount;
                rainWetness = Mathf.Clamp01(factor * 10f);
                rainInTropics = place == Place.Tropics;
            }
            return false;
        }
    }

    /// <summary>How wet the game's rain makes the walls for this scout now (0 out of the rain; only the local scout's own).</summary>
    internal static float Wetness(Character character)
    {
        return On && Rules.Owns(character) && Time.frameCount - rainFrame <= 1 ? rainWetness : 0f;
    }

    /// <summary>
    /// Ascent 25: in the rain outside the Tropics the game's rain slides a scout on a wall back less, <see
    /// cref="OutsideTropicsSlide"/> of a full-speed climb instead of half, so climbing on gains 60% of a dry climb there
    /// (the Tropics: half, the game's). Only the rain's own share of the slipperiness (not a jellyfish's); not while
    /// hanging from a piton.
    /// </summary>
    [HarmonyPatch(typeof(CharacterClimbing), nameof(CharacterClimbing.GetRequestedPostition))]
    private static class WetWalls
    {
        private static void Postfix(CharacterClimbing __instance, ref Vector3 __result)
        {
            Character character = __instance.character;
            float wet = Wetness(character);
            if (wet <= 0f || rainInTropics || !Rules.Enabled(Lvl.AllWeather) || character.data.currentClimbHandle != null)
            {
                return;
            }
            float rain = Mathf.Min(Mathf.Clamp01(character.data.slippy), wet);
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, character.data.climbNormal).normalized;
            __result += up * ((GameWetSlide - OutsideTropicsSlide) * rain * __instance.climbSpeed * Time.fixedDeltaTime * __instance.climbSpeedMod);
        }
    }

    /// <summary>
    /// In the rain, a grab taken while the scout is already on the wall (they let go and grab again) holds no higher than
    /// where the hands let go: a hold reached for higher up on a wet wall slips down to there. So letting go and grabbing
    /// higher only gains what climbing on gains in between, less the drop while the hands are off the wall: slower than
    /// climbing on. A grab from the ground, or one below where the hands were, holds as in the game.
    /// </summary>
    [HarmonyPatch(typeof(CharacterClimbing), nameof(CharacterClimbing.StartClimbRpc))]
    private static class WetGrab
    {
        private static void Prefix(CharacterClimbing __instance, ref Vector3 climbPos, Vector3 climbNormal)
        {
            Character character = __instance.character;
            float above = climbPos.y - character.data.climbPos.y;
            if (above <= 0f || !character.data.hasClimbedSinceGrounded || Wetness(character) <= 0f)
            {
                return;
            }
            // Down along the wall to the height the hands let go at (the climb settles onto the face from there).
            Vector3 down = -Vector3.ProjectOnPlane(Vector3.up, climbNormal).normalized;
            climbPos += down * (above / Mathf.Max(-down.y, 0.1f));
        }
    }

    /// <summary>
    /// A wet vine is slippery: on it in the rain, unless climbing up it, the scout slides down along it toward its lowest
    /// point, where the vine hangs straight down at <see cref="VineSlide"/> of a full-speed climb in the Tropics
    /// (elsewhere <see cref="VineSlideElsewhere"/>), slower where it slopes gently, not at all where it is
    /// level. Climbing up it keeps the game's pace (the rain costs stamina there, <see cref="WetVineScope"/>). Not while
    /// sliding along it the game's way (after grabbing it at speed).
    /// </summary>
    [HarmonyPatch(typeof(CharacterVineClimbing), nameof(CharacterVineClimbing.FixedUpdate))]
    private static class WetVines
    {
        private static void Postfix(CharacterVineClimbing __instance)
        {
            Character character = __instance.character;
            JungleVine vine = character != null ? character.data.heldVine : null;
            float wet = Wetness(character);
            if (vine == null || wet <= 0f || !character.data.isVineClimbing || __instance.Sliding())
            {
                return;
            }
            float at = character.data.vinePercent;
            Vector3 along = vine.GetPosition(at + 0.02f) - vine.GetPosition(at - 0.02f);
            if (along.sqrMagnitude < 0.0001f)
            {
                return;
            }
            // How steeply the vine rises toward its far end here: the slide goes the other way, unless the scout climbs up.
            float rise = along.y / along.magnitude;
            if (__instance._currentClimbInput * rise > 0.01f)
            {
                return;
            }
            float slide = (rainInTropics ? VineSlide : VineSlideElsewhere) * wet * rise * vine.LengthFactor() * __instance.climbSpeed * __instance.climbSpeedMod * Time.fixedDeltaTime;
            character.data.vinePercent = Mathf.Clamp(at - slide, 0.01f, 0.99f);
        }
    }

    /// <summary>
    /// While the scout is on a vine in the rain, the stamina it takes (climbing along it, or hanging on) is doubled in the
    /// Tropics, 1.5 times elsewhere.
    /// </summary>
    [HarmonyPatch(typeof(CharacterVineClimbing), nameof(CharacterVineClimbing.Update))]
    private static class WetVineScope
    {
        [ThreadStatic] internal static int Depth;

        private static void Prefix(CharacterVineClimbing __instance, out bool __state)
        {
            __state = Wetness(__instance.character) > 0f;
            if (__state)
            {
                Depth++;
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

    [HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    private static class WetVineStaminaUse
    {
        private static void Prefix(ref float usage)
        {
            if (WetVineScope.Depth > 0)
            {
                usage *= rainInTropics ? WetVineStamina : WetVineStaminaElsewhere;
            }
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
    /// in its Apocalypse mode TFA switches the game's sun off and burns with its own. TFA's Builder: not at its
    /// rest stops while the scouts rest, and its invisible walls between blocks do not shade it.
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
            if (SnowstormHidesSun || Compat.Tfa.RestSpares(Character.localCharacter))
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
            RaycastHit shade = Compat.Tfa.SunLineCheck(local.Center + clock.sun.transform.forward * -1000f, local.Center, HelperFunctions.LayerType.AllPhysical);
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
    /// The Gloom's fog (the game's own sleepy fog over the Gloom) makes the scouts whose heads are in it 2.5 drowsy a
    /// second, and the Citadel's rising gloom 5 a second, exactly as it lands on the bar; the game's safe zones (lit bell
    /// towers, campfires, stoves, candles, Faerie Lanterns) still keep them off.
    /// </summary>
    [HarmonyPatch(typeof(Peak.StatusFieldGloom), nameof(Peak.StatusFieldGloom.TickStatus))]
    private static class GloomFog
    {
        private static bool Prefix(Peak.StatusFieldGloom __instance)
        {
            Character local = Character.localCharacter;
            if (!On || local == null)
            {
                return true;
            }
            LavaRising rising = __instance.GetComponentInParent<LavaRising>();
            bool citadel = rising != null && rising.risingFieldType == LavaRising.RisingFieldType.Gloom;
            if (rising != null && !citadel)
            {
                return true;
            }
            if (RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_SleepyGloom) == 0)
            {
                return false;
            }
            ExactStatus.Add(local.refs.afflictions, CharacterAfflictions.STATUSTYPE.Drowsy,
                (citadel ? CitadelGloomDrowsiness : GloomFogDrowsiness) * Time.deltaTime, citadel ? "citadel gloom" : "gloom fog");
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

    /// <summary>
    /// True Final Ascent's rest island in a Caldera it made larger stands as high above the game's tide as the lava's heat
    /// reaches; the higher tide here does not heat the scouts on it.
    /// </summary>
    [HarmonyPatch(typeof(Lava), "Heat")]
    private static class TideSparesRestIsland
    {
        private static bool Prefix(Lava __instance) => !CalderaTide.OnRestIsland(__instance);
    }
}
