using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UltimateAscent.Features.Weather;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>Rain on the walls (18): climbing up a wet wall, on and by letting go and grabbing higher; jumps in the rain.</summary>
internal sealed partial class Runner
{
    /// <summary>How long each climb is timed.</summary>
    private const float ClimbWindow = 3f;

    private static bool climbKeys;
    private static bool holdUp;
    private static bool holdGrab;
    private static bool heldGrab;
    private static bool pressJump;

    /// <summary>While a climbing check runs: W, the grab (the left mouse button) and jump held as the check says.</summary>
    private static void ClimbInput(CharacterInput __instance)
    {
        Character local = Character.localCharacter;
        if (!climbKeys || local == null || local.input != __instance)
        {
            return;
        }
        __instance.movementInput = holdUp ? new Vector2(0f, 1f) : Vector2.zero;
        __instance.sprintIsPressed = false;
        __instance.sprintWasPressed = false;
        __instance.sprintToggleIsPressed = false;
        __instance.sprintToggleWasPressed = false;
        __instance.usePrimaryIsPressed = holdGrab;
        __instance.usePrimaryWasPressed = holdGrab && !heldGrab;
        __instance.usePrimaryWasReleased = !holdGrab && heldGrab;
        heldGrab = holdGrab;
        __instance.jumpWasPressed = pressJump;
        __instance.jumpIsPressed = pressJump;
        pressJump = false;
    }

    private sealed class ClimbTry
    {
        internal float Speed;
        internal float StaminaPerMetre;
        internal int Grabs;
        internal bool Held;
        internal float Pitch;
        internal float Grip;

        /// <summary>The highest a grab held above where the hands had let go just before.</summary>
        internal float Reach = float.NegativeInfinity;

        public override string ToString() => $"{Speed:0.00} m/s, {(StaminaPerMetre < 100f ? $"{StaminaPerMetre * 100f:0.#}%" : "all the")} stamina a metre"
            + (Grip > 0f ? $" ({Grabs} grabs, the highest {Reach:0.00} m above where the hands let go, looking {Pitch:0}° up, letting go {Grip:0.##} s after each grab)" : "")
            + (Held ? "" : ", lost hold");
    }

    /// <summary>
    /// Rain on the walls (18), on a flat wall made for the check on the beach (outside the Tropics: ascent 25's rain) and
    /// another in the Tropics (the rain's own biome, where the map has them: -Biome T), under a rainstorm made for the check
    /// (the game's rain, the way ascent 25 makes one where a map has none; the map's own rain is held off), started and
    /// stopped by the check: climbing on (W and the grab held) and the faster way (W held, the grab let go and taken again
    /// higher, as fast as the game lets a hand grab again; the fastest of nine ways of looking up and letting go). On the
    /// beach each is timed dry, in the game's rain (ascent 18's rule off, in memory) and in ascent 18's rain, with a jump
    /// dry and in the rain; in the Tropics dry and in ascent 18's rain.
    /// </summary>
    private IEnumerator RainClimb()
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather))
        {
            Info("the rain checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        MapHandler.JumpToSegment(Segment.Beach);
        yield return new WaitForSeconds(6f);
        Vector3 home = local.Center;
        GameObject wall = ClimbWall(home, out Vector3 face, out Vector3 normal);
        GameObject tropicsWall = null;
        CharacterClimbing climbing = local.refs.climbing;
        Info("the wall to climb", $"made for the check {Vector3.Distance(Flat(face), Flat(home)):0} m from the spawn, facing {normal:F2}; "
            + $"climbing speed {climbing.climbSpeed}, force {climbing.climbForce}, physics step {Time.fixedDeltaTime:0.###} s");
        WindChillZone rain = StormFactory.Create(StormKind.Rain, Singleton<MapHandler>.Instance.transform);
        if (rain == null)
        {
            Destroy(wall);
            Info("the rain climbing checks are skipped", "the rain's visuals did not load");
            yield break;
        }
        rain.gameObject.SetActive(true);
        rain.RPCA_ToggleWind(false, Vector3.forward, 100000f);
        List<WindChillZone> mapRain = HoldMapRain(rain);
        ConfigEntry<bool> weather = UltimateAscent.Plugin.AscentSwitches[Lvl.ExtremeWeather];
        bool saved = weather.Value;
        Harmony keys = new Harmony("UltimateAscent.SmokeTest.climbkeys");
        keys.Patch(AccessTools.Method(typeof(CharacterInput), "Sample"), postfix: new HarmonyMethod(typeof(Runner), nameof(ClimbInput)));
        ClearStatuses(Afflictions);
        Protect(true);
        local.refs.items.EquipSlot(Optionable<byte>.None);
        climbKeys = true;
        holdUp = holdGrab = heldGrab = pressJump = false;
        // At ascent 25 its weather drives every storm (the test's rain and the map's own too) by phases shared by kind:
        // the rain's phase follows the test's rain, the wind's and the snow's stay calm, and no extra tornado comes.
        bool phased = Rules.Enabled(Lvl.AllWeather) && A25_AllWeather.DrivesVanillaWeather;
        Type phases = typeof(WeatherPhases);
        Type phaseType = AccessTools.Inner(phases, "Phase");
        void Phase(string channel, bool active)
        {
            object phase = AccessTools.CreateInstance(phaseType);
            Traverse t = Traverse.Create(phase);
            t.Field("run").SetValue(Net.RunKey);
            t.Field("clock").SetValue(Net.Clock);
            t.Field("duration").SetValue(100000f);
            t.Field("active").SetValue(active);
            t.Field("direction").SetValue(1);
            Traverse.Create(phases).Field(channel).Field("state").SetValue(phase);
        }
        if (phased)
        {
            Phase("wind", false);
            Phase("snow", false);
            Phase("rain", false);
            ExtraTornadoes.untilNext = 100000f;
        }
        try
        {
            void Rain(bool on, bool rule)
            {
                weather.Value = rule;
                HostOptions.Invalidate();
                rain.RPCA_ToggleWind(on, Vector3.forward, 100000f);
                if (phased)
                {
                    Phase("rain", on);
                    Phase("wind", false);
                    Phase("snow", false);
                }
            }
            string All(List<ClimbTry> tries) => string.Join(" | ", tries.Select(t => $"{t.Pitch:0}°/{t.Grip:0.##} s {t.Speed:0.00}"));
            IEnumerator Fastest(Vector3 at, Vector3 outward, string picture, Action<ClimbTry, List<ClimbTry>> done)
            {
                List<ClimbTry> tries = new List<ClimbTry>();
                foreach (float pitch in new[] { 30f, 45f, 60f })
                {
                    foreach (float grip in new[] { 0.1f, 0.2f, 0.3f })
                    {
                        yield return Climb(at, outward, pitch, grip, t =>
                        {
                            if (t != null)
                            {
                                tries.Add(t);
                            }
                        }, tries.Count == 4 ? picture : null);
                    }
                }
                done(tries.OrderByDescending(t => t.Speed).FirstOrDefault(), tries);
            }

            Rain(false, true);
            yield return new WaitForSeconds(1f);
            ClimbTry dryOn = null, dryFast = null;
            List<ClimbTry> dryTries = null;
            yield return Climb(face, normal, 0f, 0f, t => dryOn = t, null);
            yield return Fastest(face, normal, null, (best, tries) => { dryFast = best; dryTries = tries; });
            Info("dry", $"climbing on {dryOn?.ToString() ?? "did not hold on"}; the faster way {dryFast?.ToString() ?? "did not hold on"} (all: {All(dryTries)})");

            Rain(true, false);
            yield return new WaitForSeconds(1f);
            ClimbTry gameOn = null, gameFast = null;
            List<ClimbTry> gameTries = null;
            yield return Climb(face, normal, 0f, 0f, t => gameOn = t, null);
            yield return Fastest(face, normal, null, (best, tries) => { gameFast = best; gameTries = tries; });
            Info("the game's rain (ascent 18's rule off)", $"climbing on {gameOn?.ToString() ?? "did not hold on"}; the faster way {gameFast?.ToString() ?? "did not hold on"} (all: {All(gameTries)})");

            Rain(true, true);
            yield return new WaitForSeconds(1f);
            ClimbTry wetOn = null, wetFast = null;
            List<ClimbTry> wetTries = null;
            yield return Climb(face, normal, 0f, 0f, t => wetOn = t, "rain-climbing-on");
            yield return Fastest(face, normal, "rain-grabbing-higher", (best, tries) => { wetFast = best; wetTries = tries; });
            Info("ascent 18's rain on the beach", $"climbing on {wetOn?.ToString() ?? "did not hold on"}; the faster way {wetFast?.ToString() ?? "did not hold on"} (all: {All(wetTries)})");

            bool timed = dryOn != null && dryFast != null && gameOn != null && gameFast != null && wetOn != null && wetFast != null && dryOn.Speed > 0.1f;
            // At ascent 25 the rain outside the Tropics is gentler on the walls: climbing on gains 60% of a dry climb (the game's: half).
            bool gentler = Rules.Enabled(Lvl.AllWeather);
            float share = 1f - (gentler ? A18_ExtremeWeather.OutsideTropicsSlide : A18_ExtremeWeather.GameWetSlide);
            Check(gentler
                    ? "at ascent 25, outside the Tropics (here the beach), climbing on in the rain gains about 60% of a dry climb (the game's rain: half), and holds on"
                    : "outside the Tropics (here the beach, as in ascent 25's rain), climbing on in ascent 18's rain is as in the game's rain: about half a dry climb, and holds on",
                timed && wetOn.Held && Mathf.Abs(wetOn.Speed / dryOn.Speed - share) < 0.07f && (gentler ? wetOn.Speed > gameOn.Speed + 0.05f : Mathf.Abs(wetOn.Speed - gameOn.Speed) < 0.06f),
                timed ? $"{wetOn.Speed:0.00} m/s, {wetOn.Speed / dryOn.Speed:P0} of dry ({dryOn.Speed:0.00} m/s); the game's rain {gameOn.Speed:0.00} m/s, {gameOn.Speed / dryOn.Speed:P0}; held on {wetOn.Held}" : "a climb did not hold on");
            float reachDry = timed ? dryTries.Max(t => t.Reach) : 0f, reachWet = timed ? wetTries.Max(t => t.Reach) : 0f;
            Check("outside the Tropics, in ascent 18's rain a grab taken on the wall holds no higher than where the hands let go (dry it reaches higher)",
                timed && reachWet < 0.2f && reachDry > 0.3f, $"the highest grab above where the hands let go: dry {reachDry:0.00} m, in ascent 18's rain {reachWet:0.00} m");
            Check("outside the Tropics, in ascent 18's rain letting go and grabbing higher, however it is done, is slower than climbing on (dry and in the game's rain it is much faster)",
                timed && wetFast.Speed < wetOn.Speed * 0.9f && dryFast.Speed > dryOn.Speed * 2f && gameFast.Speed > gameOn.Speed * 2f,
                timed ? $"the faster way's best: dry {dryFast.Speed:0.00} m/s ({dryFast.Speed / dryOn.Speed:0.0}x climbing on), the game's rain {gameFast.Speed:0.00} m/s ({gameFast.Speed / gameOn.Speed:0.0}x), "
                    + $"ascent 18's rain {wetFast.Speed:0.00} m/s ({wetFast.Speed / Mathf.Max(wetOn.Speed, 0.01f):0.0}x), {wetFast}" : "a climb did not hold on");

            // Jumps, dry and in the rain.
            holdGrab = false;
            climbing.StopAnyClimbing();
            Vector3? flat = null;
            foreach (float distance in new[] { 0f, 4f, 8f, 12f })
            {
                for (int i = 0; i < 12 && flat == null; i++)
                {
                    flat = FlatOpen(home + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * distance);
                }
            }
            if (flat == null)
            {
                Info("the jump check is skipped", $"no flat open ground near {home:F0}");
            }
            else
            {
                float dry = 0f, wet = 0f;
                Rain(false, true);
                yield return Jump(flat.Value, h => dry = h);
                Rain(true, true);
                yield return Jump(flat.Value, h => wet = h);
                Check("rain leaves gravity alone: a jump in ascent 18's rain rises as high as a dry one", dry > 0.3f && Mathf.Abs(wet - dry) < 0.1f,
                    $"{dry:0.00} m dry, {wet:0.00} m in the rain");
            }

            // The Tropics, the rain's own biome: a wall of its own there, under open sky.
            Rain(false, true);
            climbing.StopAnyClimbing();
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(6f);
            Vector3? open = Map.CurrentPlace == Place.Tropics ? OpenSky(local.Center) : null;
            if (open == null)
            {
                Info("the Tropics wall checks are skipped", Map.CurrentPlace == Place.Tropics ? "no open sky near the Tropics' start" : $"this map has no Tropics (the {Map.CurrentPlace} instead)");
            }
            else
            {
                mapRain.AddRange(HoldMapRain(rain).Where(z => !mapRain.Contains(z)));
                tropicsWall = ClimbWall(open.Value + Vector3.up, out Vector3 tropicsFace, out Vector3 tropicsNormal);
                yield return new WaitForSeconds(1f);
                ClimbTry tropicsDry = null, tropicsOn = null, tropicsFast = null;
                List<ClimbTry> tropicsTries = null;
                yield return Climb(tropicsFace, tropicsNormal, 0f, 0f, t => tropicsDry = t, null);
                Rain(true, true);
                yield return new WaitForSeconds(1f);
                yield return Climb(tropicsFace, tropicsNormal, 0f, 0f, t => tropicsOn = t, "rain-climbing-on-tropics");
                yield return Fastest(tropicsFace, tropicsNormal, "rain-grabbing-higher-tropics", (best, tries) => { tropicsFast = best; tropicsTries = tries; });
                Info("ascent 18's rain in the Tropics", $"dry climbing on {tropicsDry?.ToString() ?? "did not hold on"}; in the rain climbing on {tropicsOn?.ToString() ?? "did not hold on"}; "
                    + $"the faster way {tropicsFast?.ToString() ?? "did not hold on"} (all: {(tropicsTries != null ? All(tropicsTries) : "none")})");
                bool there = tropicsDry != null && tropicsOn != null && tropicsFast != null && tropicsDry.Speed > 0.1f;
                Check("in the Tropics, climbing on in ascent 18's rain is as in the game's rain: about half a dry climb, and holds on",
                    there && tropicsOn.Held && Mathf.Abs(tropicsOn.Speed / tropicsDry.Speed - (1f - A18_ExtremeWeather.GameWetSlide)) < 0.07f,
                    there ? $"{tropicsOn.Speed:0.00} m/s, {tropicsOn.Speed / tropicsDry.Speed:P0} of dry ({tropicsDry.Speed:0.00} m/s); held on {tropicsOn.Held}" : "a climb did not hold on");
                Check("in the Tropics, in ascent 18's rain a grab taken on the wall holds no higher than where the hands let go, and letting go and grabbing higher is slower than climbing on",
                    there && tropicsTries.Max(t => t.Reach) < 0.2f && tropicsFast.Speed < tropicsOn.Speed * 0.9f,
                    there ? $"the highest grab {tropicsTries.Max(t => t.Reach):0.00} m above where the hands let go; the faster way's best {tropicsFast.Speed:0.00} m/s against {tropicsOn.Speed:0.00} climbing on" : "a climb did not hold on");
            }
        }
        finally
        {
            climbKeys = false;
            holdUp = holdGrab = heldGrab = pressJump = false;
            keys.UnpatchSelf();
            weather.Value = saved;
            HostOptions.Invalidate();
            Destroy(rain.gameObject);
            Destroy(wall);
            if (tropicsWall != null)
            {
                Destroy(tropicsWall);
            }
            ReleaseMapRain(mapRain);
            if (phased)
            {
                WeatherPhases.Reset();
            }
            local.refs.climbing.StopAnyClimbing();
            Protect(false);
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home, false);
        yield return new WaitForSeconds(1.5f);
    }

    /// <summary>
    /// One climb up the face from its foot, timed: grip 0 climbs on (W and the grab held); otherwise W is held and the
    /// grab let go grip seconds after each grab and taken again at once, looking pitch degrees above level (the game
    /// lets a hand grab again 0.2 s after letting go). Stamina is kept full and what the climb takes of it counted.
    /// </summary>
    private IEnumerator Climb(Vector3 face, Vector3 normal, float pitch, float grip, Action<ClimbTry> done, string picture)
    {
        Character local = Local;
        yield return OnTheFace(face, normal);
        if (!local.data.isClimbing)
        {
            done(null);
            yield break;
        }
        Vector3 look = (-normal * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad)).normalized;
        holdUp = true;
        holdGrab = true;
        float start = local.Center.y;
        float began = Time.time;
        float spent = 0f;
        int grabs = 0;
        bool wasClimbing = true;
        int releasedFrame = -1;
        float letGo = float.NaN;
        float reach = float.NegativeInfinity;
        while (Time.time - began < ClimbWindow)
        {
            local.data.lookValues = HelperFunctions.DirectionToLook(look);
            if (picture != null && Time.time - began >= ClimbWindow * 0.5f)
            {
                Screenshot(picture);
                picture = null;
            }
            if (grip > 0f)
            {
                if (local.data.isClimbing)
                {
                    if (!wasClimbing)
                    {
                        grabs++;
                        if (!float.IsNaN(letGo))
                        {
                            reach = Mathf.Max(reach, local.data.climbPos.y - letGo);
                        }
                    }
                    if (holdGrab && local.data.sinceStartClimb >= grip)
                    {
                        holdGrab = false;
                        releasedFrame = Time.frameCount;
                    }
                    letGo = local.data.climbPos.y;
                }
                else if (!holdGrab && Time.frameCount > releasedFrame)
                {
                    holdGrab = true;
                }
            }
            wasClimbing = local.data.isClimbing;
            spent += Mathf.Max(0f, 1f - local.data.currentStamina);
            local.data.currentStamina = 1f;
            local.data.extraStamina = 0f;
            yield return null;
        }
        float gained = local.Center.y - start;
        ClimbTry result = new ClimbTry
        {
            Speed = gained / (Time.time - began),
            StaminaPerMetre = gained > 0.05f ? spent / gained : float.PositiveInfinity,
            Grabs = grabs,
            Held = local.data.isClimbing || grip > 0f && local.Center.y > start - 1f,
            Pitch = pitch,
            Grip = grip,
            Reach = reach,
        };
        holdUp = false;
        holdGrab = false;
        done(result);
    }

    /// <summary>The scout in front of the face's foot, holding on to it, with full stamina, settled.</summary>
    private IEnumerator OnTheFace(Vector3 face, Vector3 normal)
    {
        Character local = Local;
        holdUp = false;
        holdGrab = false;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            local.refs.climbing.StopAnyClimbing();
            yield return null;
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, face + normal * 0.7f, false);
            yield return new WaitForSeconds(0.1f);
            for (float until = Time.time + 3f; Time.time < until && local.warping;)
            {
                yield return null;
            }
            for (float until = Time.time + 1f; Time.time < until && !local.data.isClimbing;)
            {
                local.data.lookValues = HelperFunctions.DirectionToLook(-normal);
                local.refs.climbing.TryToStartWallClimb(forceAttempt: true, -normal);
                yield return null;
            }
            if (local.data.isClimbing)
            {
                break;
            }
        }
        holdGrab = true;
        for (float until = Time.time + 0.8f; Time.time < until;)
        {
            local.data.lookValues = HelperFunctions.DirectionToLook(-normal);
            local.data.currentStamina = 1f;
            local.data.extraStamina = 0f;
            yield return null;
        }
    }

    /// <summary>A jump from flat ground: how high the scout's middle rises over the 1.5 s after it.</summary>
    private IEnumerator Jump(Vector3 ground, Action<float> done)
    {
        Character local = Local;
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, ground + Vector3.up * 1.2f, false);
        yield return new WaitForSeconds(0.1f);
        for (float until = Time.time + 4f; Time.time < until && (local.warping || local.data.groundedFor < 0.8f);)
        {
            yield return null;
        }
        local.data.currentStamina = 1f;
        float start = local.Center.y;
        float top = start;
        pressJump = true;
        for (float until = Time.time + 1.5f; Time.time < until;)
        {
            top = Mathf.Max(top, local.Center.y);
            yield return null;
        }
        done(top - start);
    }

    /// <summary>
    /// Rain on the vines (18), in the Tropics and the Gloom (where the map has them; a map with both: -Biome TMS or TAS),
    /// under a rainstorm made for the check: in each, the scout grabs a vine the game's way where it is steepest and open
    /// to the rain, then hangs still and climbs up it, 2 s each, dry and in ascent 18's rain. In the rain the vine is
    /// slippery (the scout slides down it while hanging, in the Tropics as fast as on a wet wall where it hangs straight
    /// down, elsewhere half that), climbing up it keeps its pace, and it takes twice the stamina in the Tropics, 1.5 times
    /// elsewhere.
    /// </summary>
    private IEnumerator RainVine()
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather))
        {
            Info("the vine checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Vector3 start = local.Center;
        WindChillZone rain = StormFactory.Create(StormKind.Rain, Singleton<MapHandler>.Instance.transform);
        if (rain == null)
        {
            Info("the vine checks are skipped", "the rain's visuals did not load");
            yield break;
        }
        rain.gameObject.SetActive(true);
        rain.RPCA_ToggleWind(false, Vector3.forward, 100000f);
        Harmony keys = new Harmony("UltimateAscent.SmokeTest.climbkeys");
        keys.Patch(AccessTools.Method(typeof(CharacterInput), "Sample"), postfix: new HarmonyMethod(typeof(Runner), nameof(ClimbInput)));
        ClearStatuses(Afflictions);
        Protect(true);
        local.refs.items.EquipSlot(Optionable<byte>.None);
        climbKeys = true;
        holdUp = holdGrab = heldGrab = pressJump = false;
        try
        {
            foreach (Segment segment in new[] { Segment.Tropics, Segment.Caldera })
            {
                MapHandler.JumpToSegment(segment);
                yield return new WaitForSeconds(6f);
                yield return VinesInTheRain(rain, Map.CurrentPlace);
            }
        }
        finally
        {
            climbKeys = false;
            holdUp = holdGrab = heldGrab = pressJump = false;
            keys.UnpatchSelf();
            Destroy(rain.gameObject);
            if (local.data.isVineClimbing)
            {
                local.refs.vineClimbing.Stop();
            }
            Protect(false);
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, start, false);
        yield return new WaitForSeconds(1.5f);
    }

    /// <summary>The map's own rainstorms (the Tropics' rain) held off while a check times things dry; each stays calm until let go.</summary>
    private static List<WindChillZone> HoldMapRain(WindChillZone ours)
    {
        List<WindChillZone> held = FindObjectsByType<WindChillZone>(FindObjectsSortMode.None).Where(z => z != null && z != ours && z.name == "RainStorm").ToList();
        foreach (WindChillZone zone in held)
        {
            zone.RPCA_ToggleWind(false, zone.currentWindDirection, 100000f);
        }
        return held;
    }

    /// <summary>The map's rainstorms back to the game's own turns (calm for 20 s first).</summary>
    private static void ReleaseMapRain(List<WindChillZone> held)
    {
        foreach (WindChillZone zone in held.Where(z => z != null))
        {
            zone.RPCA_ToggleWind(false, zone.currentWindDirection, 20f);
        }
    }

    /// <summary>The vine checks where the scout is now (one place's vines).</summary>
    private IEnumerator VinesInTheRain(WindChillZone rain, Place place)
    {
        Character local = Local;
        Vector3 home = local.Center;
        float Exposure(Vector3 point) => WindChillZone.GetWindIntensityAtPoint(point, rain.lightVolumeSampleThreshold_lower, rain.lightVolumeSampleThreshold_margin);
        List<JungleVine> vines = FindObjectsByType<JungleVine>(FindObjectsSortMode.None)
            .Where(v => v != null && v.colliderType == JungleVine.ColliderType.Capsule && v.colliderRoot != null && v.colliderRoot.transform.childCount >= 50 && v.LengthFactor() > 0f).ToList();
        // Where the scout would hang (a metre under the vine): how much rain gets there through the leaves, the game's way.
        int open = vines.Count(v => Exposure(v.GetPosition(0.5f) + Vector3.down) > 0.5f);
        JungleVine chosen = null;
        int index = -1;
        float steepest = 0f;
        foreach (JungleVine v in vines.Where(v => Vector3.Distance(v.GetPosition(0.5f), home) < 400f))
        {
            for (int i = 6; i <= 42; i++)
            {
                Vector3 a = v.colliderRoot.transform.GetChild(i).position, b = v.colliderRoot.transform.GetChild(i + 1).position;
                float rise = Mathf.Abs(b.y - a.y) / Mathf.Max(Vector3.Distance(a, b), 0.001f);
                if (rise > steepest && Exposure(a + Vector3.down) > 0.9f)
                {
                    chosen = v;
                    index = i;
                    steepest = rise;
                }
            }
        }
        Info($"vines in the {place}", $"{vines.Count}, {open} with rain reaching a scout on them at their middle; the steepest stretch open to the rain within 400 m rises {steepest:0.00} m a metre"
            + (chosen != null ? $", {Vector3.Distance(chosen.GetPosition(0.5f), home):0} m away" : ""));
        if (chosen == null || steepest < 0.3f)
        {
            Info($"the vine checks in the {place} are skipped", "no vine open to the rain with a steep stretch");
            yield break;
        }
        CharacterVineClimbing vineClimbing = local.refs.vineClimbing;
        Vector3 grabAt = chosen.colliderRoot.transform.GetChild(index).position;
        float percent = chosen.GetPercentFromSegmentIndex(index);
        Vector3 lower = chosen.GetPosition(percent - 0.02f), upper = chosen.GetPosition(percent + 0.02f);
        // The way along the vine that rises (towards its far end or its near one), and a look that climbs that way.
        float toFar = Mathf.Sign(upper.y - lower.y);
        Vector3 flat = Vector3.ProjectOnPlane((upper - lower) * toFar, Vector3.up).normalized;
        Vector3 look = chosen.GetSign(flat, percent) == toFar ? flat : -flat;

        IEnumerator Grab()
        {
            if (local.data.isVineClimbing)
            {
                vineClimbing.Stop();
            }
            yield return new WaitForSeconds(0.3f);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, grabAt + Vector3.down, false);
            yield return new WaitForSeconds(0.1f);
            for (float until = Time.time + 3f; Time.time < until && local.warping;)
            {
                yield return null;
            }
            for (float until = Time.time + 2f; Time.time < until && !local.data.isVineClimbing;)
            {
                if (vineClimbing.canInteractVine)
                {
                    chosen.Interact(local);
                }
                yield return null;
            }
            for (float until = Time.time + 0.6f; Time.time < until;)
            {
                local.data.lookValues = HelperFunctions.DirectionToLook(look);
                local.data.currentStamina = 1f;
                local.data.extraStamina = 0f;
                yield return null;
            }
        }
        // How hard the last 2 s on the vine slid the scout down it, against a full-speed climb on its slopes there.
        float slid = 0f;
        // 2 s on the vine, hanging still or climbing up it: the height it takes the scout a second, the stamina a second.
        IEnumerator On(bool climb, string picture, Action<float, float, bool> done)
        {
            yield return Grab();
            bool held = local.data.isVineClimbing && local.data.heldVine == chosen;
            float startHeight = chosen.GetPosition(local.data.vinePercent).y;
            float startPercent = local.data.vinePercent;
            float began = Time.time;
            float spent = 0f;
            float fullSpeed = 0f;
            holdUp = climb;
            while (Time.time - began < 2f)
            {
                local.data.lookValues = HelperFunctions.DirectionToLook(look);
                if (picture != null && Time.time - began > 1f)
                {
                    Screenshot(picture);
                    picture = null;
                }
                held &= local.data.isVineClimbing && local.data.heldVine == chosen;
                Vector3 there = chosen.GetPosition(local.data.vinePercent + 0.02f) - chosen.GetPosition(local.data.vinePercent - 0.02f);
                fullSpeed += there.y / Mathf.Max(there.magnitude, 0.001f) * chosen.LengthFactor() * vineClimbing.climbSpeed * Time.deltaTime;
                spent += Mathf.Max(0f, 1f - local.data.currentStamina);
                local.data.currentStamina = 1f;
                local.data.extraStamina = 0f;
                yield return null;
            }
            holdUp = false;
            float seconds = Time.time - began;
            slid = Mathf.Abs(fullSpeed) > 0.0001f ? (startPercent - local.data.vinePercent) / fullSpeed : 0f;
            done((chosen.GetPosition(local.data.vinePercent).y - startHeight) / seconds, spent / seconds, held);
        }

        string name = place.ToString().ToLowerInvariant();
        float dryHang = 0f, dryHangStamina = 0f, dryClimb = 0f, dryClimbStamina = 0f, wetHang = 0f, wetHangStamina = 0f, wetClimb = 0f, wetClimbStamina = 0f, wet = 0f;
        bool stayed = true;
        List<WindChillZone> mapRain = HoldMapRain(rain);
        rain.RPCA_ToggleWind(false, Vector3.forward, 100000f);
        yield return On(false, null, (h, s, ok) => { dryHang = h; dryHangStamina = s; stayed &= ok; });
        yield return On(true, null, (h, s, ok) => { dryClimb = h; dryClimbStamina = s; stayed &= ok; });
        rain.RPCA_ToggleWind(true, Vector3.forward, 100000f);
        yield return new WaitForSeconds(0.5f);
        yield return On(false, $"rain-vine-hanging-{name}", (h, s, ok) => { wetHang = h; wetHangStamina = s; stayed &= ok; wet = rain.windPlayerFactor; });
        float slide = slid;
        yield return On(true, $"rain-vine-climbing-{name}", (h, s, ok) => { wetClimb = h; wetClimbStamina = s; stayed &= ok; });
        rain.RPCA_ToggleWind(false, Vector3.forward, 100000f);
        ReleaseMapRain(mapRain);
        if (local.data.isVineClimbing)
        {
            vineClimbing.Stop();
        }
        string seen = $"dry: hanging {dryHang:+0.00;-0.00} m/s, {dryHangStamina * 100f:0.##}% stamina a second, climbing {dryClimb:+0.00;-0.00} m/s, {dryClimbStamina * 100f:0.#}% a second; "
            + $"ascent 18's rain: hanging {wetHang:+0.00;-0.00} m/s, {wetHangStamina * 100f:0.##}% a second, climbing {wetClimb:+0.00;-0.00} m/s, {wetClimbStamina * 100f:0.#}% a second; "
            + $"the stretch rises {steepest:0.00} m a metre, rain on the scout {wet:0.00}; on the vine all along {stayed}; the vine's climbing speed {vineClimbing.climbSpeed}, {chosen.totalLength:0.0} m long";
        bool tropics = place == Place.Tropics;
        float expectedSlide = tropics ? A18_ExtremeWeather.VineSlide : A18_ExtremeWeather.VineSlideElsewhere;
        float expectedStamina = tropics ? A18_ExtremeWeather.WetVineStamina : A18_ExtremeWeather.WetVineStaminaElsewhere;
        Check($"in ascent 18's rain a vine in the {place} is slippery: hanging still, the scout slides down it (dry they stay put) at {expectedSlide:0.##} of a full-speed climb on its slope; climbing up it keeps its pace",
            stayed && dryClimb > 0.2f && Mathf.Abs(dryHang) < 0.05f && wetHang < -0.1f && Mathf.Abs(slide - expectedSlide) < expectedSlide * 0.2f && Mathf.Abs(wetClimb / dryClimb - 1f) < 0.1f,
            $"the slide {slide:0.00} of a full-speed climb; " + seen);
        Check($"in ascent 18's rain a vine in the {place} takes {expectedStamina:0.#} times the stamina, climbing and hanging on",
            stayed && dryClimbStamina > 0f && dryHangStamina > 0f && Mathf.Abs(wetClimbStamina / dryClimbStamina - expectedStamina) < 0.15f
            && Mathf.Abs(wetHangStamina / dryHangStamina - expectedStamina) < 0.15f,
            $"climbing x{(dryClimbStamina > 0f ? wetClimbStamina / dryClimbStamina : 0f):0.00}, hanging x{(dryHangStamina > 0f ? wetHangStamina / dryHangStamina : 0f):0.00}; " + seen);
    }

    /// <summary>
    /// A flat wall for the climbing checks, 30 m tall and 6 m wide, standing on the ground 4 m from this point the way
    /// there is room, on the map's layer (the game climbs it like rock): a point on its face 8 m up, and its outward
    /// direction.
    /// </summary>
    private static GameObject ClimbWall(Vector3 near, out Vector3 face, out Vector3 normal)
    {
        Vector3 way = Vector3.forward;
        for (int angle = 0; angle < 360; angle += 30)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            if (HelperFunctions.LineCheck(near + Vector3.up, near + Vector3.up + direction * 10f, HelperFunctions.LayerType.TerrainMap).transform == null)
            {
                way = direction;
                break;
            }
        }
        Vector3 ground = Ground(near + way * 4f, out RaycastHit floor) ? floor.point : near + way * 4f;
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "UltimateAscentTestWall";
        wall.layer = LayerMask.NameToLayer("Map");
        wall.transform.SetPositionAndRotation(ground + way * 0.5f + Vector3.up * 14f, Quaternion.LookRotation(way));
        wall.transform.localScale = new Vector3(6f, 32f, 1f);
        Grey(wall);
        Physics.SyncTransforms();
        face = ground + Vector3.up * 8f;
        normal = -way;
        return wall;
    }
}
