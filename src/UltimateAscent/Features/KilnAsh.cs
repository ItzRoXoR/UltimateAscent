using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 18: hot ash fills the Kiln once a minute. From the moment its lava starts to rise, every 60 s the
/// ground rumbles and the ash comes in over 5 s, fills the Kiln for 15 s, burning everyone in it for 3 heat a
/// second (45 in all) (as it lands from ascent 14 on; below 14 without that ascent's boost), and clears again within 2 s.
/// Everyone gets it at the same moment: it keeps the time of the lava's rise, which the game keeps the same
/// for everyone. Scouts still in the Caldera are not in it. At the top of the Kiln (as high as its lava ever
/// rises, or higher) it does not burn. Within 3 m of a petrifying stone it neither shows nor burns: the
/// haze clears within half a second and the smoke and flakes around fade. Each scout sees it around themselves: dark smoke
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

    /// <summary>Heat a second in the ash, as it lands from ascent 14 on.</summary>
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

    /// <summary>Within this distance of a petrifying stone (from any part of the scout) the ash neither shows nor burns.</summary>
    internal const float StoneShelter = 3f;

    /// <summary>How quickly the haze clears and comes back when a scout steps up to a petrifying stone or away from it.</summary>
    private const float ShelterFadeSeconds = 0.5f;

    private static Collider[] stones;
    private static readonly HashSet<Collider> stoneSet = new HashSet<Collider>();
    private static int stoneLayers;
    private static float stonesSearched = float.NegativeInfinity;
    private static readonly Collider[] nearby = new Collider[128];
    private static float shelterFade;
    private static ParticleSystem.Particle[] fading;

    /// <summary>The local scout is within 3 m of a petrifying stone, where the ash neither shows nor burns.</summary>
    internal static bool Sheltered { get; private set; }

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
        stones = null;
        stoneSet.Clear();
        stonesSearched = float.NegativeInfinity;
        shelterFade = 0f;
        Sheltered = false;
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

    /// <summary>At the top of the Kiln: as high as its lava ever rises, or higher. The ash does not burn there.</summary>
    internal static bool AtTop(Vector3 position)
    {
        LavaRising rising = KilnLava();
        return rising != null && rising.topTransform != null && position.y >= rising.topTransform.position.y;
    }

    /// <summary>The petrifying stones' colliders (looked for again, at most every 5 s, while none are known).</summary>
    internal static IReadOnlyList<Collider> Stones
    {
        get
        {
            if ((stones == null || stones.Length == 0 || stones[0] == null) && Time.time - stonesSearched >= 5f)
            {
                stonesSearched = Time.time;
                IEnumerable<Component> parts = UnityEngine.Object.FindObjectsByType<CollisionModifier>(FindObjectsSortMode.None).Where(m => m.applyPetrify).Cast<Component>()
                    .Concat(UnityEngine.Object.FindObjectsByType<ClimbModifierSurface>(FindObjectsSortMode.None).Where(m => m.applyPetrify).Cast<Component>());
                stones = parts.SelectMany(part => part.GetComponentsInChildren<Collider>()).Where(c => !c.isTrigger).Distinct().ToArray();
                stoneSet.Clear();
                stoneSet.UnionWith(stones);
                stoneLayers = stones.Aggregate(0, (mask, c) => mask | 1 << c.gameObject.layer);
            }
            return stones ?? (IReadOnlyList<Collider>)Array.Empty<Collider>();
        }
    }

    /// <summary>Some part of the scout (from the lower foot to the head) is within <paramref name="reach"/> of a petrifying stone.</summary>
    internal static bool NearStone(Character character, float reach = StoneShelter)
    {
        IReadOnlyList<Collider> found = Stones;
        if (found.Count == 0)
        {
            return false;
        }
        Vector3 head = character.Head;
        Vector3 hip = character.Center;
        Rigidbody footR = character.GetBodypartRig(BodypartType.Foot_R);
        Rigidbody footL = character.GetBodypartRig(BodypartType.Foot_L);
        Vector3 feet = footR != null && footL != null ? (footR.position.y < footL.position.y ? footR.position : footL.position) : hip - (head - hip);
        float far = reach + Mathf.Max(Vector3.Distance(hip, head), Vector3.Distance(hip, feet));
        bool close = false;
        foreach (Collider stone in found)
        {
            if (stone != null && stone.enabled && stone.gameObject.activeInHierarchy && stone.bounds.SqrDistance(hip) <= far * far)
            {
                close = true;
                break;
            }
        }
        if (!close)
        {
            return false;
        }
        int count = Physics.OverlapCapsuleNonAlloc(feet, head, reach, nearby, stoneLayers, QueryTriggerInteraction.Ignore);
        Collider[] touching = nearby;
        if (count >= nearby.Length)
        {
            // More of the map around than the buffer holds: all of it.
            touching = Physics.OverlapCapsule(feet, head, reach, stoneLayers, QueryTriggerInteraction.Ignore);
            count = touching.Length;
        }
        for (int i = 0; i < count; i++)
        {
            if (stoneSet.Contains(touching[i]))
            {
                return true;
            }
        }
        return false;
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
        Sheltered = here && NearStone(local);
        shelterFade = Mathf.MoveTowards(shelterFade, Sheltered ? 1f : 0f, Time.deltaTime / ShelterFadeSeconds);
        Drawn = here ? Thickness(phase) * (1f - shelterFade) : 0f;
        if (here && lastPhase >= 0f && lastPhase < Period - Seconds - WarningSeconds && phase >= Period - Seconds - WarningSeconds)
        {
            Rumble(local);
        }
        lastPhase = phase;
        if (here && phase >= Period - Seconds && !Sheltered && !AtTop(local.Center))
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
        float thickness = Sheltered ? 0f : Drawn;
        ParticleSystem.EmissionModule smokeRate = smoke.emission;
        smokeRate.rateOverTimeMultiplier = SmokeRate * thickness;
        ParticleSystem.EmissionModule flakeRate = flakes.emission;
        flakeRate.rateOverTimeMultiplier = FlakeRate * thickness;
        if (Sheltered)
        {
            Fade(smoke);
            Fade(flakes);
        }
        if (Drawn > 0f && !smoke.isPlaying)
        {
            smoke.Play(true);
            flakes.Play(true);
        }
    }

    /// <summary>Beside a petrifying stone the smoke and the flakes already around fade away (within about a second).</summary>
    private static void Fade(ParticleSystem system)
    {
        if (system.particleCount == 0)
        {
            return;
        }
        int most = system.main.maxParticles;
        if (fading == null || fading.Length < most)
        {
            fading = new ParticleSystem.Particle[most];
        }
        int count = system.GetParticles(fading);
        float keep = Mathf.Exp(-Time.deltaTime / 0.2f);
        for (int i = 0; i < count; i++)
        {
            Color32 colour = fading[i].startColor;
            colour.a = (byte)(colour.a * keep);
            fading[i].startColor = colour;
            if (colour.a == 0)
            {
                fading[i].remainingLifetime = 0f;
            }
        }
        system.SetParticles(fading, count);
    }

    /// <summary>The ash's smoke and flakes, darkest and lightest (a burned item crumbling in the sun leaves the same, see A23_HotSun).</summary>
    internal static readonly Color SmokeDark = new Color(0.5f, 0.48f, 0.46f, 0.6f);
    internal static readonly Color SmokeLight = new Color(0.66f, 0.63f, 0.6f, 0.85f);
    internal static readonly Color FlakeDark = new Color(0.82f, 0.8f, 0.77f, 1f);
    internal static readonly Color FlakeLight = new Color(0.96f, 0.94f, 0.9f, 1f);
    internal const float FlakeSizeMin = 0.08f;
    internal const float FlakeSizeMax = 0.2f;

    private static bool MakeCloud()
    {
        ParticleSystem template = A23_HotSun.SmokeTemplate();
        if (template == null)
        {
            return false;
        }
        smoke = Particles(template, "UltimateAscent Kiln Smoke", new Vector3(18f, 9f, 18f), 700, 6f, 9f, 3f, 7f, 0.2f, 0.8f,
            SmokeDark, SmokeLight, -0.01f);
        flakes = Particles(template, "UltimateAscent Kiln Ash", new Vector3(16f, 8f, 16f), 2400, 5f, 8f, FlakeSizeMin, FlakeSizeMax, 0.1f, 0.5f,
            FlakeDark, FlakeLight, 0.03f);
        return true;
    }

    /// <summary>A copy of <paramref name="template"/>'s particles alone, emitting nothing yet from a box, fading in and out over each particle's life.</summary>
    internal static ParticleSystem Particles(ParticleSystem template, string name, Vector3 box, int most, float minLife, float maxLife, float minSize, float maxSize,
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
