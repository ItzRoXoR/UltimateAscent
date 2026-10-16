using System;
using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 18: hot ash fills the Kiln once a minute. From the moment its lava starts to rise, every 60 s the
/// ground rumbles and the ash comes in over 5 s, fills the Kiln for 15 s, burning everyone in it for 3 heat a
/// second (45 in all) (as it lands from ascent 15 on; below 15 without that ascent's boost), and clears again within 2 s.
/// Everyone gets it at the same moment: it keeps the time of the lava's rise, which the game keeps the same
/// for everyone. Scouts still in the Caldera are not in it. Each scout sees it around themselves: dark smoke
/// billowing and ash flakes drifting down (the game's cooking smoke, made big and dark), and the air grown
/// hazy from 3 m on (the game's fog sphere, the rising fog's, ash grey; while True Final Ascent's Ice Fog has
/// the sphere, the smoke and the flakes still show).
/// </summary>
internal static class KilnAsh
{
    internal const float Period = 60f;
    internal const float Seconds = 15f;
    internal const float WarningSeconds = 5f;
    internal const float ClearingSeconds = 2f;

    /// <summary>Heat a second in the ash, as it lands from ascent 15 on.</summary>
    internal const float Heat = 0.03f;

    /// <summary>The ash leaves this much clear air around you (the fog sphere's radius around the camera).</summary>
    private const float ClearAir = 3f;

    /// <summary>The smoke and the flakes around the view, as many a second at full thickness.</summary>
    private const float SmokeRate = 90f;
    private const float FlakeRate = 300f;

    private static ParticleSystem smoke;
    private static ParticleSystem flakes;

    internal static readonly Color AshColour = new Color(0.36f, 0.33f, 0.31f, 0f);

    private const string HeatSource = "kiln ash";

    private static LavaRising lava;
    private static Bounds kiln;
    private static bool kilnKnown;
    private static FogSphere sphere;
    private static bool weActivated;
    private static float lastPhase = -1f;

    private sealed class Saved
    {
        internal float enable;
        internal float size;
        internal float reveal;
        internal Vector3 point;
    }

    private static Saved saved;

    /// <summary>How thick the ash is drawn on this game right now, 0 to 1.</summary>
    internal static float Drawn { get; private set; }

    /// <summary>The ash is drawn with the fog sphere right now (its colour is the ash's, see <see cref="FogSphereRules"/>).</summary>
    internal static bool Drawing => Drawn > 0f && sphere != null && !Compat.Tfa.IceFogDrawing;

    private static bool On => Rules.Enabled(Lvl.ExtremeWeather) && Map.InVolcanoKiln;

    /// <summary>Smoke particles alive around the view (for the tests).</summary>
    internal static int SmokeParticles => (smoke != null ? smoke.particleCount : 0) + (flakes != null ? flakes.particleCount : 0);

    internal static void Reset()
    {
        smoke = null;
        flakes = null;
        lava = null;
        kilnKnown = false;
        sphere = null;
        weActivated = false;
        lastPhase = -1f;
        saved = null;
        Drawn = 0f;
    }

    /// <summary>The Kiln's rising lava (on a volcano map), whose plane covers the Kiln.</summary>
    private static LavaRising KilnLava()
    {
        if (lava == null)
        {
            lava = LavaRising.ALL_LAVA.FirstOrDefault(l => l != null && l.requiredSegment == Segment.TheKiln && l.risingFieldType == LavaRising.RisingFieldType.Lava);
            kilnKnown = false;
        }
        if (lava != null && !kilnKnown && lava.lava != null)
        {
            Renderer plane = lava.lava.GetComponentsInChildren<Renderer>(true).FirstOrDefault();
            if (plane != null)
            {
                kiln = plane.bounds;
                kilnKnown = true;
            }
        }
        return lava;
    }

    /// <summary>Inside the Kiln: over its lava's plane, and not below the lava.</summary>
    internal static bool InKiln(Vector3 position)
    {
        LavaRising rising = KilnLava();
        if (rising == null || !kilnKnown || rising.lava == null)
        {
            return false;
        }
        return position.x >= kiln.min.x && position.x <= kiln.max.x && position.z >= kiln.min.z && position.z <= kiln.max.z
            && position.y >= rising.lava.position.y - 5f;
    }

    /// <summary>The Kiln's lava rising now (on a volcano map, in the Kiln), whose time the ash and the eruptions keep.</summary>
    private static LavaRising Rising()
    {
        LavaRising rising = Map.InVolcanoKiln ? KilnLava() : null;
        return rising != null && rising.started && !rising.ended ? rising : null;
    }

    /// <summary>Seconds into the Kiln's minute (counted from when the lava began to rise), or -1 while the lava does not rise.</summary>
    internal static float ClockPhase => Rising() is LavaRising rising ? rising.timeTraveled % Period : -1f;

    /// <summary>Which minute of the Kiln's it is, or -1 while the lava does not rise.</summary>
    internal static int ClockMinute => Rising() is LavaRising rising ? (int)(rising.timeTraveled / Period) : -1;

    /// <summary>Seconds into the ash's minute, or -1 while it does not run (ascent 18's rule off, or the lava not rising).</summary>
    internal static float Phase => On ? ClockPhase : -1f;

    /// <summary>The ash burns right now (its 15 s at the end of each minute).</summary>
    internal static bool Burning => Phase >= Period - Seconds;

    private static float Thickness(float phase)
    {
        if (phase < 0f)
        {
            return 0f;
        }
        float comes = Period - Seconds - WarningSeconds;
        if (phase >= Period - Seconds)
        {
            return 1f;
        }
        if (phase >= comes)
        {
            return (phase - comes) / WarningSeconds;
        }
        return phase < ClearingSeconds ? 1f - phase / ClearingSeconds : 0f;
    }

    internal static void Tick()
    {
        Character local = Character.localCharacter;
        float phase = Phase;
        bool here = local != null && !local.data.dead && phase >= 0f && InKiln(local.Center);
        Drawn = here ? Thickness(phase) : 0f;
        if (here && lastPhase >= 0f && lastPhase < Period - Seconds - WarningSeconds && phase >= Period - Seconds - WarningSeconds)
        {
            Rumble(local);
        }
        lastPhase = phase;
        if (here && phase >= Period - Seconds)
        {
            CharacterAfflictions.STATUSTYPE hot = CharacterAfflictions.STATUSTYPE.Hot;
            ExactStatus.Add(local.refs.afflictions, hot, A15_StrongerAilments.FromAscent15(hot, Heat) * Time.deltaTime, HeatSource);
        }
        Sphere();
        Cloud();
    }

    /// <summary>The smoke and the flakes follow the view and thicken and thin with the ash.</summary>
    private static void Cloud()
    {
        if (Drawn <= 0f && smoke == null)
        {
            return;
        }
        if (smoke == null && !MakeCloud())
        {
            return;
        }
        Transform view = MainCamera.instance != null ? MainCamera.instance.transform : null;
        if (view != null)
        {
            smoke.transform.position = view.position;
            flakes.transform.position = view.position + Vector3.up * 3f;
        }
        ParticleSystem.EmissionModule smokeRate = smoke.emission;
        smokeRate.rateOverTimeMultiplier = SmokeRate * Drawn;
        ParticleSystem.EmissionModule flakeRate = flakes.emission;
        flakeRate.rateOverTimeMultiplier = FlakeRate * Drawn;
        if (Drawn > 0f && !smoke.isPlaying)
        {
            smoke.Play(true);
            flakes.Play(true);
        }
    }

    private static bool MakeCloud()
    {
        ParticleSystem template = A23_HotSun.SmokeTemplate();
        if (template == null)
        {
            return false;
        }
        smoke = Particles(template, "UltimateAscent Kiln Smoke", new Vector3(18f, 9f, 18f), 700, 6f, 9f, 3f, 7f, 0.2f, 0.8f,
            new Color(0.5f, 0.48f, 0.46f, 0.6f), new Color(0.66f, 0.63f, 0.6f, 0.85f), -0.01f);
        flakes = Particles(template, "UltimateAscent Kiln Ash", new Vector3(16f, 8f, 16f), 2400, 5f, 8f, 0.08f, 0.2f, 0.1f, 0.5f,
            new Color(0.82f, 0.8f, 0.77f, 1f), new Color(0.96f, 0.94f, 0.9f, 1f), 0.03f);
        return true;
    }

    private static ParticleSystem Particles(ParticleSystem template, string name, Vector3 box, int most, float minLife, float maxLife, float minSize, float maxSize,
        float minSpeed, float maxSpeed, Color dark, Color light, float gravity)
    {
        ParticleSystem made = UnityEngine.Object.Instantiate(template);
        made.name = name;
        made.transform.SetParent(null, false);
        made.transform.rotation = Quaternion.identity;
        made.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        // Only the particles: the cooking smoke comes with what else the item's smoke carries (a heat emitter
        // among it), which would follow the view and burn.
        foreach (Transform child in made.transform.Cast<Transform>().ToArray())
        {
            UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
        foreach (Component part in made.GetComponents<Component>())
        {
            if (!(part is Transform) && !(part is ParticleSystem) && !(part is ParticleSystemRenderer))
            {
                UnityEngine.Object.DestroyImmediate(part);
            }
        }
        ParticleSystem.MainModule main = made.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = true;
        main.playOnAwake = false;
        main.maxParticles = most;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startColor = new ParticleSystem.MinMaxGradient(dark, light);
        main.gravityModifier = gravity;
        ParticleSystem.ShapeModule shape = made.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;
        ParticleSystem.EmissionModule emission = made.emission;
        emission.enabled = true;
        emission.rateOverTimeMultiplier = 0f;
        emission.burstCount = 0;
        ParticleSystem.ColorOverLifetimeModule fade = made.colorOverLifetime;
        fade.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = new ParticleSystem.MinMaxGradient(gradient);
        return made;
    }

    /// <summary>The ash is coming: the ground shakes and rumbles (the eruptions' own sound).</summary>
    private static void Rumble(Character local)
    {
        if (GamefeelHandler.instance != null)
        {
            GamefeelHandler.instance.AddPerlinShake(4f, WarningSeconds, 10f);
        }
        EruptionSpawner spawner = UnityEngine.Object.FindAnyObjectByType<EruptionSpawner>(FindObjectsInactive.Include);
        SFX_PlayOneShot sound = spawner != null && spawner.eruption != null ? spawner.eruption.GetComponentInChildren<SFX_PlayOneShot>(true) : null;
        SFX_Instance rumble = sound != null && sound.sfxs != null && sound.sfxs.Length > 0 ? sound.sfxs[UnityEngine.Random.Range(0, sound.sfxs.Length)] : null;
        if (rumble != null)
        {
            rumble.Play(local.Head);
        }
    }

    /// <summary>The fog sphere is switched on while the ash is drawn (it is off in the Kiln), and back off after.</summary>
    private static void Sphere()
    {
        if (Drawn > 0f && !Compat.Tfa.IceFogDrawing)
        {
            if (sphere == null)
            {
                sphere = UnityEngine.Object.FindAnyObjectByType<FogSphere>(FindObjectsInactive.Include);
            }
            if (sphere != null && saved == null)
            {
                saved = new Saved { enable = sphere.ENABLE, size = sphere.currentSize, reveal = sphere.REVEAL_AMOUNT, point = sphere.fogPoint };
                if (!sphere.gameObject.activeSelf)
                {
                    sphere.gameObject.SetActive(true);
                    weActivated = true;
                }
            }
            return;
        }
        if (sphere != null && saved != null)
        {
            sphere.ENABLE = saved.enable;
            sphere.currentSize = saved.size;
            sphere.REVEAL_AMOUNT = saved.reveal;
            sphere.fogPoint = saved.point;
            if (weActivated)
            {
                sphere.gameObject.SetActive(false);
            }
        }
        saved = null;
        weActivated = false;
    }

    /// <summary>Drawn around the camera: clear air close by, the ash beyond, as thick as it is right now.</summary>
    [HarmonyPatch(typeof(FogSphere), nameof(FogSphere.SetSharderVars))]
    private static class AroundTheView
    {
        private static void Prefix(FogSphere __instance)
        {
            if (Drawing && __instance == sphere && MainCamera.instance != null)
            {
                __instance.fogPoint = MainCamera.instance.transform.position;
                __instance.currentSize = ClearAir;
                // Never quite 1: the game's own fog chills only at full strength.
                __instance.ENABLE = 0.999f * Drawn;
                __instance.REVEAL_AMOUNT = 0f;
            }
        }
    }
}
