using System;
using System.IO;
using UnityEngine;

namespace UltimateAscent.Features.Weather;

internal enum StormKind
{
    Rain,
    Wind,
    Snow
}

/// <summary>
/// Builds a storm the current map does not have. Each daily map only contains the storms of the
/// biome variants it rolled (Tropics maps have rain, Roots maps wind, Alpine maps snow), so for
/// "all weather everywhere" the missing ones are recreated here with the vanilla zone settings.
/// <para>Visuals: the game's snow particle system, its "Storm" particle shader and its storm fog
/// texture come from an embedded asset bundle (prepared by 13DDA Continued). Rain and wind are
/// derived from it with the vanilla rain/wind particle and material settings.</para>
/// </summary>
internal static class StormFactory
{
    private const string ResourceName = "UltimateAscent.NativeAlpineSnow.bundle";

    private static AssetBundle bundle;
    private static GameObject particlesPrefab;
    private static Texture2D fogTexture;
    private static Material rainMaterial;
    private static Material windMaterial;
    private static bool failed;

    internal static string ZoneName(StormKind kind)
    {
        switch (kind)
        {
            case StormKind.Rain:
                return "RainStorm";
            case StormKind.Wind:
                return "WindStorm";
            default:
                return "SnowStorm";
        }
    }

    /// <summary>Creates the storm inactive under <paramref name="parent"/>; returns null if the visuals are unavailable.</summary>
    internal static WindChillZone Create(StormKind kind, Transform parent)
    {
        if (!EnsureLoaded())
        {
            return null;
        }
        GameObject root = new GameObject(ZoneName(kind));
        root.SetActive(false);
        try
        {
            root.transform.SetParent(parent, worldPositionStays: false);
            WindChillZone zone = root.AddComponent<WindChillZone>();
            switch (kind)
            {
                case StormKind.Rain:
                    BuildRain(root, zone);
                    break;
                case StormKind.Wind:
                    BuildWind(root, zone);
                    break;
                default:
                    BuildSnow(root, zone);
                    break;
            }
            zone.windZoneBounds = new Bounds(root.transform.position, Vector3.one * 10000f);
            return zone;
        }
        catch
        {
            UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    private static void CommonZone(WindChillZone zone)
    {
        zone.forceRadius = 2f;
        zone.delayBeforeForce = 2f;
        zone.windItemFactor = 1f;
    }

    private static void BuildSnow(GameObject root, WindChillZone zone)
    {
        root.transform.position = new Vector3(0f, 580f, 841f);
        CommonZone(zone);
        zone.grabStaminaMultiplierDuringWind = 1f;
        zone.windTimeRangeOn = new Vector2(15f, 25f);
        zone.windTimeRangeOff = new Vector2(30f, 90f);
        zone.lightVolumeSampleThreshold_lower = 0.15f;
        zone.lightVolumeSampleThreshold_margin = 0.15f;
        zone.statusType = CharacterAfflictions.STATUSTYPE.Cold;
        zone.statusApplicationPerSecond = 0.07f;
        zone.windForce = 4f;
        zone.ragdolledWindForceMult = 0.5f;
        zone.windMovesItems = false;
        zone.windIntensityCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        zone.disableIfSettingDisabled = RunSettings.SETTINGTYPE.Hazard_Storm;

        WindHeightEffect height = root.AddComponent<WindHeightEffect>();
        height.from = 0.13f;
        height.to = 0.08f;
        height.fromHeight = 530f;
        height.toHeight = 570f;

        GameObject sphere = NewChild(root, "StormSphere", "Storm");
        ParticleSystem particles = Particles(sphere, new Vector3(0f, 0f, -14.38f), Quaternion.identity, 1f);
        Fog(sphere, zone, skyBrightness: 0.5f, textureInfluence: 0.3f, tint: new Color(0.75f, 0.75f, 0.75f, 1f), sphereScale: 0.25f,
            speed: new Vector2(0f, 1.5f), density: 50f, textureDensity: 15f, mix: 0f, maxVal: 1f, straightDown: false, useIntensity: false);
        Visual(sphere, StormVisual.StormType.Snow, particles, photosensitive: true, useIntensity: false, forceField: null);
    }

    private static void BuildRain(GameObject root, WindChillZone zone)
    {
        root.transform.position = new Vector3(0f, 333.9f, 308.9f);
        CommonZone(zone);
        zone.grabStaminaMultiplierDuringWind = 1f;
        zone.windTimeRangeOn = new Vector2(20f, 40f);
        zone.windTimeRangeOff = new Vector2(40f, 80f);
        zone.lightVolumeSampleThreshold_lower = 0.18f;
        zone.lightVolumeSampleThreshold_margin = 0.05f;
        zone.statusType = CharacterAfflictions.STATUSTYPE.Injury;
        zone.statusApplicationPerSecond = 0f;
        zone.windForce = 0f;
        zone.setSlippy = true;
        zone.ragdolledWindForceMult = 0.5f;
        zone.windMovesItems = false;
        zone.windIntensityCurve = new AnimationCurve();
        zone.disableIfSettingDisabled = RunSettings.SETTINGTYPE.Hazard_Rain;

        GameObject sphere = NewChild(root, "WindSphere", "Rain");
        ParticleSystem particles = Particles(sphere, new Vector3(0f, -52f, 13.9f), new Quaternion(0.60876f, 0f, 0f, 0.79335f), 3f);
        ParticleSystem.MainModule main = particles.main;
        main.simulationSpeed = 30f;
        main.startColor = new Color(0.69811f, 0.69811f, 0.69811f, 1f);
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 100f;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 25f;
        renderer.sharedMaterial = rainMaterial;

        Fog(sphere, zone, skyBrightness: 0.5f, textureInfluence: 0.2f, tint: new Color(0.46818f, 0.52225f, 0.59434f, 1f), sphereScale: 0.25f,
            speed: new Vector2(0f, 0.4f), density: 50f, textureDensity: 15f, mix: 0.8f, maxVal: 0.96f, straightDown: true, useIntensity: false);
        Visual(sphere, StormVisual.StormType.Rain, particles, photosensitive: false, useIntensity: false, forceField: null);
    }

    private static void BuildWind(GameObject root, WindChillZone zone)
    {
        root.transform.position = new Vector3(-1.1f, 375.9f, 241.9f);
        CommonZone(zone);
        zone.grabStaminaMultiplierDuringWind = 3f;
        zone.windTimeRangeOn = new Vector2(7.5f, 15f);
        zone.windTimeRangeOff = new Vector2(60f, 120f);
        zone.lightVolumeSampleThreshold_lower = 0.01f;
        zone.lightVolumeSampleThreshold_margin = 0.05f;
        zone.statusType = CharacterAfflictions.STATUSTYPE.Cold;
        zone.statusApplicationPerSecond = 0f;
        zone.windForce = 20f;
        zone.ragdolledWindForceMult = 0.25f;
        zone.windMovesItems = true;
        zone.useIntensityCurve = true;
        zone.windIntensityCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 3.84555f, 3.84555f),
            new Keyframe(0.03161f, 0.10223f, 0f, 0f),
            new Keyframe(0.3963f, 1.0018f, 6.38313f, 0f),
            new Keyframe(0.9f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, 0f, 0f));
        zone.useRaycast = true;
        zone.maxRaycastDistance = 5f;
        zone.minRaycastDistance = 4f;
        zone.disableIfSettingDisabled = RunSettings.SETTINGTYPE.Hazard_Wind;

        ParticleSystemForceField forceField = root.AddComponent<ParticleSystemForceField>();
        forceField.shape = ParticleSystemForceFieldShape.Box;
        forceField.startRange = 0f;
        forceField.endRange = 170f;
        forceField.length = 1f;

        GameObject sphere = NewChild(root, "WindSphere-Forest", null);
        ParticleSystem particles = Particles(sphere, Vector3.zero, Quaternion.identity, 3f);
        particles.gameObject.name = "Broad Wind";
        ParticleSystem.MainModule main = particles.main;
        main.simulationSpeed = 20f;
        main.maxParticles = 100;
        main.startColor = new Color(0.69811f, 0.69811f, 0.69811f, 1f);
        main.startLifetime = 40f;
        main.startSize = new ParticleSystem.MinMaxCurve(3f, 10f);
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 2f;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.radius = 25f;
        ParticleSystem.LimitVelocityOverLifetimeModule clamp = particles.limitVelocityOverLifetime;
        clamp.enabled = false;
        ParticleSystem.ForceOverLifetimeModule force = particles.forceOverLifetime;
        force.enabled = false;
        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.frequency = 0.1f;
        noise.scrollSpeed = 0.05f;
        noise.strength = 0.2f;
        ParticleSystem.TrailModule trails = particles.trails;
        trails.enabled = true;
        trails.widthOverTrail = 0.2f;
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.z = new ParticleSystem.MinMaxCurve(4f, 2f);
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 25f;
        renderer.sharedMaterial = windMaterial;
        renderer.trailMaterial = windMaterial;

        Fog(sphere, zone, skyBrightness: 0f, textureInfluence: 0.5f, tint: new Color(0.66981f, 0.47708f, 0.50473f, 1f), sphereScale: 0.5f,
            speed: new Vector2(0f, 1.5f), density: 37.79f, textureDensity: 151.4f, mix: 0.8f, maxVal: 0.96f, straightDown: false, useIntensity: true);
        Visual(sphere, StormVisual.StormType.Wind, particles, photosensitive: false, useIntensity: true, forceField: forceField);
    }

    private static GameObject NewChild(GameObject parent, string name, string tag)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform, worldPositionStays: false);
        if (tag != null)
        {
            try
            {
                child.tag = tag;
            }
            catch (UnityException)
            {
                Plugin.Debug("Storm tag " + tag + " is not defined in this build.");
            }
        }
        return child;
    }

    private static ParticleSystem Particles(GameObject sphere, Vector3 localPosition, Quaternion localRotation, float scale)
    {
        GameObject instance = UnityEngine.Object.Instantiate(particlesPrefab, sphere.transform, worldPositionStays: false);
        instance.name = "Particle System";
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = localRotation;
        instance.transform.localScale = Vector3.one * scale;
        return instance.GetComponent<ParticleSystem>();
    }

    private static void Fog(GameObject sphere, WindChillZone zone, float skyBrightness, float textureInfluence, Color tint, float sphereScale, Vector2 speed, float density,
        float textureDensity, float mix, float maxVal, bool straightDown, bool useIntensity)
    {
        FogConfig fog = sphere.AddComponent<FogConfig>();
        fog.windSkyBrightnessValue = skyBrightness;
        fog.windTextureInfluence = textureInfluence;
        fog.windTint = tint;
        fog.windTexture = fogTexture;
        fog.windSphereScale = sphereScale;
        fog.windSpeed = speed;
        fog.windFogDensity = density;
        fog.WindFogTextureDensity = textureDensity;
        fog.windMixInfluence = mix;
        fog.maxVal = maxVal;
        fog.straightDown = straightDown;
        fog.zone = zone;
        fog.useWindChillZoneIntensity = useIntensity;
    }

    private static void Visual(GameObject sphere, StormVisual.StormType type, ParticleSystem particles, bool photosensitive, bool useIntensity, ParticleSystemForceField forceField)
    {
        StormVisual visual = sphere.AddComponent<StormVisual>();
        visual.part = new[] { particles };
        visual.stormType = type;
        visual.usePhotosensitiveMode = photosensitive;
        visual.useWindChillZoneIntensity = useIntensity;
        visual.windchillZoneMult = 1f;
        visual.windParticleMult = 50f;
        visual.particleForceField = forceField;
    }

    private static bool EnsureLoaded()
    {
        if (bundle != null)
        {
            return true;
        }
        if (failed)
        {
            return false;
        }
        try
        {
            using (Stream stream = typeof(StormFactory).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("the storm resource is missing from the DLL");
                }
                byte[] bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0)
                    {
                        break;
                    }
                    read += n;
                }
                bundle = AssetBundle.LoadFromMemory(bytes);
            }
            if (bundle == null)
            {
                throw new InvalidOperationException("the storm bundle could not be loaded");
            }
            particlesPrefab = bundle.LoadAsset<GameObject>("assets/continued/native-alpine-snow.prefab");
            fogTexture = bundle.LoadAsset<Texture2D>("assets/continued/native-alpine-fog.texture");
            Material snow = particlesPrefab != null ? particlesPrefab.GetComponent<ParticleSystemRenderer>()?.sharedMaterial : null;
            if (particlesPrefab == null || particlesPrefab.GetComponent<ParticleSystem>() == null || fogTexture == null || snow == null)
            {
                throw new InvalidOperationException("the storm assets are incomplete");
            }
            rainMaterial = new Material(snow) { name = "UA_VFX_Rain" };
            rainMaterial.SetTextureScale("_Texture", new Vector2(16f, 16f));
            rainMaterial.SetFloat("_Cuttof", 0.4f);
            rainMaterial.SetFloat("_MixToGray", 0f);
            rainMaterial.SetFloat("_SunPower", 8f);
            rainMaterial.SetFloat("_Value", 2f);

            windMaterial = new Material(snow) { name = "UA_VFX_Wind" };
            windMaterial.SetTexture("_Texture", CloudNoise());
            windMaterial.SetTextureScale("_Texture", Vector2.one);
            windMaterial.SetColor("_Tint", new Color(1f, 1f, 1f, 0.22745f));
            windMaterial.SetFloat("_Cuttof", 0.085f);
            windMaterial.SetFloat("_MixToGray", 0.144f);
            windMaterial.SetFloat("_SunPower", 8f);
            windMaterial.SetFloat("_Value", 5f);
            windMaterial.SetFloat("_UseCavityMap", 0.728f);
            return true;
        }
        catch (Exception ex)
        {
            failed = true;
            Plugin.Log.LogWarning("Ascent 25: no storm visuals available (" + ex.Message + "); missing storms will not be added.");
            Release();
            return false;
        }
    }

    /// <summary>A soft "cloud" noise like the game's wind texture: overlapping soft discs, tileable.</summary>
    private static Texture2D CloudNoise()
    {
        const int size = 256;
        float[] value = new float[size * size];
        System.Random random = new System.Random(1337);
        for (int blob = 0; blob < 140; blob++)
        {
            float cx = (float)random.NextDouble() * size;
            float cy = (float)random.NextDouble() * size;
            float radius = 6f + (float)random.NextDouble() * 34f;
            float brightness = 0.25f + (float)random.NextDouble() * 0.75f;
            int r = Mathf.CeilToInt(radius);
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    if (distance >= 1f)
                    {
                        continue;
                    }
                    int x = ((int)cx + dx + size) % size;
                    int y = ((int)cy + dy + size) % size;
                    float softness = Mathf.SmoothStep(1f, 0f, distance);
                    int index = y * size + x;
                    value[index] = Mathf.Max(value[index], brightness * softness);
                }
            }
        }
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true) { name = "UA_CloudNoise", wrapMode = TextureWrapMode.Repeat };
        Color32[] pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = (byte)Mathf.Clamp(16f + value[i] * 231f, 0f, 255f);
            pixels[i] = new Color32(v, v, v, 255);
        }
        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        return texture;
    }

    internal static void Release()
    {
        particlesPrefab = null;
        fogTexture = null;
        rainMaterial = null;
        windMaterial = null;
        if (bundle != null)
        {
            bundle.Unload(unloadAllLoadedObjects: true);
        }
        bundle = null;
    }
}
