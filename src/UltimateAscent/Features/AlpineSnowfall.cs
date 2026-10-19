using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// A light snowfall in the Alps, on every run: small flakes drift slowly down around the view while the
/// ground below the view is the Alpine's (with no ground below, while the climb is in the Alpine; so not
/// yet at the campfire below the Alps, where the climb already counts as the Alpine). It is only for the look (no
/// cold, no wind) and thin enough to keep the view clear. Under a roof or an overhang it stops, and
/// flakes melt away where they meet the ground; it comes and goes over a few seconds. Each scout sees it
/// around themselves (the game's cooking smoke, made small and white, as the Kiln's ash flakes are).
/// </summary>
internal static class AlpineSnowfall
{
    /// <summary>Flakes a second around the view.</summary>
    internal const float Rate = 110f * 4f / 3f;

    /// <summary>How long the snowfall takes to start or stop.</summary>
    private const float FadeSeconds = 3f;

    /// <summary>Anything this close above the view (rock, ice, a roof) keeps the snow off.</summary>
    private const float Cover = 30f;

    private static ParticleSystem snow;
    private static float amount;
    private static float lastLook = float.NegativeInfinity;
    private static bool overAlps;
    private static bool overGround;

    /// <summary>The view is under a roof or an overhang (looked at twice a second).</summary>
    internal static bool Covered { get; private set; }

    /// <summary>How thick the snowfall is right now, 0 to 1.</summary>
    internal static float Amount => amount;

    /// <summary>Flakes in the air around the view (for the tests).</summary>
    internal static int Flakes => snow != null ? snow.particleCount : 0;

    internal static void Reset()
    {
        snow = null;
        amount = 0f;
        lastLook = float.NegativeInfinity;
        overAlps = false;
        overGround = false;
        Covered = false;
    }

    internal static void Tick()
    {
        Transform view = MainCamera.instance != null ? MainCamera.instance.transform : null;
        bool wanted = false;
        if (view != null && Map.Ready)
        {
            if (Time.time - lastLook >= 0.5f)
            {
                lastLook = Time.time;
                overGround = Map.TryGetGroundBiome(view.position, out Biome.BiomeType biome);
                overAlps = overGround && biome == Biome.BiomeType.Alpine;
                Covered = Physics.Raycast(view.position, Vector3.up, Cover, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore);
            }
            wanted = (overGround ? overAlps : Map.InAlpine) && !Covered;
        }
        amount = Mathf.MoveTowards(amount, wanted ? 1f : 0f, Time.deltaTime / FadeSeconds);
        if (amount <= 0f && snow == null)
        {
            return;
        }
        if (snow == null && !Make())
        {
            return;
        }
        if (view != null)
        {
            snow.transform.position = view.position + Vector3.up * 6f;
        }
        ParticleSystem.EmissionModule emission = snow.emission;
        emission.rateOverTimeMultiplier = Rate * amount;
        if (amount > 0f && !snow.isPlaying)
        {
            snow.Play(true);
        }
    }

    private static bool Make()
    {
        ParticleSystem template = A23_HotSun.SmokeTemplate();
        if (template == null)
        {
            return false;
        }
        // Flakes come 3 to 9 m above the view, across 30 m, and fall for 12 to 16 s.
        snow = KilnAsh.Particles(template, "UltimateAscent Alpine Snow", new Vector3(30f, 6f, 30f), 3200, 12f, 16f, 0.04f, 0.08f, 0f, 0f,
            new Color(0.9f, 0.93f, 0.97f, 0.9f), Color.white, 0f);
        // The smoke's shader colours its particles from its own three greys (dark smoke): snow-white ones
        // instead, on a copy of its material, and lightly shaded.
        ParticleSystemRenderer look = snow.GetComponent<ParticleSystemRenderer>();
        Material white = new Material(look.sharedMaterial) { name = "UltimateAscent Alpine Snow" };
        white.SetColor("_Color", new Color(0.84f, 0.88f, 0.93f, 1f));
        white.SetColor("_Color2", new Color(0.94f, 0.96f, 0.98f, 0f));
        white.SetColor("_Color3", new Color(1f, 1f, 1f, 0f));
        white.SetFloat("_ShadowStr", 0.2f);
        look.sharedMaterial = white;
        // Falling slowly, drifting a little.
        ParticleSystem.VelocityOverLifetimeModule fall = snow.velocityOverLifetime;
        fall.enabled = true;
        fall.space = ParticleSystemSimulationSpace.World;
        fall.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        fall.y = new ParticleSystem.MinMaxCurve(-1f, -0.7f);
        fall.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        ParticleSystem.NoiseModule sway = snow.noise;
        sway.enabled = true;
        sway.strength = 0.35f;
        sway.frequency = 0.4f;
        sway.scrollSpeed = 0.3f;
        sway.damping = true;
        // Nothing of the smoke's own growing, slowing or pushing.
        ParticleSystem.SizeOverLifetimeModule size = snow.sizeOverLifetime;
        size.enabled = false;
        ParticleSystem.LimitVelocityOverLifetimeModule limit = snow.limitVelocityOverLifetime;
        limit.enabled = false;
        ParticleSystem.ForceOverLifetimeModule force = snow.forceOverLifetime;
        force.enabled = false;
        ParticleSystem.InheritVelocityModule inherit = snow.inheritVelocity;
        inherit.enabled = false;
        // Flakes melt where they meet the ground, so none fall through rock into caves.
        ParticleSystem.CollisionModule ground = snow.collision;
        ground.enabled = true;
        ground.type = ParticleSystemCollisionType.World;
        ground.mode = ParticleSystemCollisionMode.Collision3D;
        ground.collidesWith = HelperFunctions.terrainMapMask;
        ground.quality = ParticleSystemCollisionQuality.Medium;
        ground.lifetimeLoss = 1f;
        ground.sendCollisionMessages = false;
        return true;
    }
}
