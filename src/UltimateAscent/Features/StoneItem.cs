using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Stone: the game's cut stone made a throwing stone. Stones lie on the ground in every climb (see
/// <see cref="GroundItems"/>). A stone that hits a scout at 10 m/s or more (a throw charged a third of
/// the way, or a fall from 5 m) knocks them down for 2 s and injures them by 10 (20 from ascent 17);
/// a stone shatters on any impact that hard, whatever it hits.
/// <para>It is built from the Coconut, which already knocks down the scouts it falls on and breaks
/// when it lands hard: the coconut's look, sounds of breaking and halves are replaced by the cut
/// stone's rock and name. The knock-down a stone sends carries a mark (2.0023 s) so that the scout it
/// hit can tell it from the others and take the injury on their own client. A stone that breaks makes
/// the user's rock smash (cut from their clip) and leaves a small gray dust cloud (the Climbing Chalk's
/// dust, gray and smaller), for every player; hitting a scout or a zombie makes only that smash, not the
/// coconut's bonk.</para>
/// </summary>
internal static class StoneItem
{
    internal const string PrefabName = "UA_Stone";
    internal const ushort ItemId = 9003;
    private const string CoconutPrefab = "Item_Coconut";
    private const string CutStonePrefab = "Stone";
    private const string CutRock = "Rock_Round (3)";
    private const string CoconutLook = "Coconut";
    private const string BreakResource = "UltimateAscent.Stone_break.wav";

    /// <summary>
    /// The break's loudness: full. At the coconut break's 0.35 (the clips measure alike, -21.5 LUFS against
    /// the smash's -21.6) the smash came out 6 dB under that break and 15 dB under the Coconut's bonk where
    /// the scout hears it (recorded in the game, 2 m away): barely above the beach's own sound.
    /// </summary>
    internal const float BreakVolume = 1f;

    /// <summary>
    /// The dust cloud: a small one. Fewer, smaller, slower and shorter-lived particles than the chalk's,
    /// from a ball the stone's size, gray, gone after 5 s.
    /// </summary>
    private const int DustParticles = 14;
    private const float DustRadius = 0.2f;
    private const float DustSpeedMin = 0.15f;
    private const float DustSpeedMax = 0.5f;
    private const float DustSize = 0.6f;
    private const float DustLifetime = 0.5f;
    private const float DustOpacity = 0.75f;
    private const float DustSeconds = 5f;
    private static readonly Color DustColour = new Color(0.56f, 0.54f, 0.51f);

    /// <summary>The impact speed that knocks a scout down and shatters the stone.</summary>
    internal const float BreakSpeed = 10f;

    internal const float Knockdown = 2f;
    private const float KnockdownMarked = Knockdown + 0.0023f;
    internal const float Injury = 0.1f;
    internal const float InjuryHazards = 0.2f;

    private static Item template;
    private static SFX_Instance breakSound;
    private static bool breakSoundFailed;
    private static GameObject dustTemplate;
    private static bool dustFailed;

    internal static Item Template => template;

    /// <summary>For the tests: how many stones broke, and how many dust clouds they left, on this client.</summary>
    internal static int Breaks;
    internal static int DustClouds;

    internal static bool IsStone(Item item) => item != null && template != null && item.itemID == template.itemID;

    /// <summary>What a stone takes off the bar when it hits a scout (ascent 17's hazards: 20).</summary>
    internal static float InjuryNow => Rules.Enabled(Lvl.Hazards) ? InjuryHazards : Injury;

    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(CoconutPrefab, out Item coconut) || coconut == null || !ItemDatabase.TryGetItem(CutStonePrefab, out Item cut) || cut == null)
        {
            Plugin.Log.LogWarning("The Coconut or the cut Stone is missing from the item database; the Stone is not added.");
            return;
        }
        Transform rock = cut.transform.Find(CutRock);
        Transform look = coconut.transform.Find(CoconutLook);
        if (rock == null || rock.GetComponent<MeshRenderer>() == null || look == null || coconut.GetComponent<Breakable>() == null || coconut.GetComponent<Bonkable>() == null)
        {
            Plugin.Log.LogWarning("The cut Stone's rock or the Coconut's parts are not where they were; the Stone is not added.");
            return;
        }
        int layer = look.GetComponentInChildren<MeshRenderer>(true)?.gameObject.layer ?? 0;

        Item stone = ModItems.Copy(coconut, PrefabName);
        Object.DestroyImmediate(stone.transform.Find(CoconutLook).gameObject);
        GameObject body = Object.Instantiate(rock.gameObject, stone.transform);
        body.name = "Stone";
        body.transform.localPosition = rock.localPosition;
        body.transform.localRotation = rock.localRotation;
        body.transform.localScale = rock.localScale;
        foreach (Transform part in body.GetComponentsInChildren<Transform>(true))
        {
            part.gameObject.layer = layer;
        }
        foreach (MeshCollider collider in body.GetComponentsInChildren<MeshCollider>(true))
        {
            collider.convex = true;
        }
        // The cut stone's grip.
        foreach (string hand in new[] { "Hand_L", "Hand_R" })
        {
            Transform mine = stone.transform.Find(hand);
            Transform theirs = cut.transform.Find(hand);
            if (mine != null && theirs != null)
            {
                mine.localPosition = theirs.localPosition;
                mine.localRotation = theirs.localRotation;
            }
        }
        stone.colliders = stone.GetComponentsInChildren<Collider>(true);
        stone.mainRenderer = body.GetComponent<MeshRenderer>();
        stone.addtlRenderers = new Renderer[0];

        stone.UIData.itemName = cut.UIData.itemName;
        if (cut.UIData.icon != null)
        {
            stone.UIData.icon = cut.UIData.icon;
        }
        stone.UIData.hasMainInteract = false;
        stone.UIData.iconPositionOffset = cut.UIData.iconPositionOffset;
        stone.UIData.iconRotationOffset = cut.UIData.iconRotationOffset;
        stone.UIData.iconScaleOffset = cut.UIData.iconScaleOffset;
        stone.carryWeight = cut.carryWeight;
        stone.mass = cut.mass;
        stone.usingTimePrimary = 0f;

        Breakable breakable = stone.GetComponent<Breakable>();
        breakable.breakOnCollision = true;
        breakable.minBreakVelocity = BreakSpeed;
        breakable.instantiateOnBreak = new List<GameObject>();
        breakable.instantiatePoints = new List<Transform>();
        breakable.alternateChance = 0f;
        breakable.alternateInstantiateOnBreak = new List<GameObject>();
        // The sound and the dust come on every client (see Shatter); the game plays its own for the stone's owner only.
        breakable.breakSFX = new List<SFX_Instance>();
        breakable.instantiateNonItemOnBreak = new List<GameObject>();
        breakable.ragdollCharacterOnBreak = false;

        Bonkable bonkable = stone.GetComponent<Bonkable>();
        bonkable.minBonkVelocity = BreakSpeed;
        bonkable.decreaseMinVelocityIfRecentlyThrown = false;
        bonkable.minBonkVelocityThrown = BreakSpeed;
        bonkable.ragdollTime = KnockdownMarked;
        bonkable.bonk = new SFX_Instance[0];

        if (stone.TryGetComponent(out LootData loot))
        {
            loot.spawnLocations = SpawnPool.None;
        }
        if (ModItems.Register(stone, ItemId))
        {
            template = stone;
        }
    }

    /// <summary>The user's rock smash, played like the game's own breaking sounds (in the world, a little pitch variation).</summary>
    internal static SFX_Instance BreakSound()
    {
        if (breakSound != null || breakSoundFailed)
        {
            return breakSound;
        }
        try
        {
            AudioClip clip = ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(BreakResource), "Stone_break");
            SFX_Instance sound = ScriptableObject.CreateInstance<SFX_Instance>();
            sound.name = "SFXI Stone Break";
            sound.hideFlags = HideFlags.HideAndDontSave;
            sound.clips = new[] { clip };
            sound.settings = new SFX_Settings
            {
                volume = BreakVolume,
                volume_Variation = 0f,
                pitch = 1f,
                pitch_Variation = 0.1f,
                spatialBlend = 1f,
                dopplerLevel = 0.125f,
                range = 100f,
                cooldown = 0.02f,
            };
            breakSound = sound;
        }
        catch (System.Exception ex)
        {
            breakSoundFailed = true;
            Plugin.Log.LogError("The Stone's break sound could not be loaded: " + ex.Message);
        }
        return breakSound;
    }

    /// <summary>The Climbing Chalk's dust (the Sunscreen's spray cloud) made a small gray cloud.</summary>
    internal static GameObject DustTemplate()
    {
        if (dustTemplate != null || dustFailed)
        {
            return dustTemplate;
        }
        GameObject chalk = ClimbingChalkItem.DustTemplate();
        if (chalk == null)
        {
            dustFailed = true;
            return null;
        }
        GameObject holder = new GameObject("UltimateAscent Stone Dust Template");
        holder.SetActive(false);
        Object.DontDestroyOnLoad(holder);
        GameObject dust = Object.Instantiate(chalk, holder.transform);
        dust.name = "UltimateAscent Stone Dust";
        foreach (ParticleSystem particles in dust.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particles.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(DustSpeedMin, DustSpeedMax);
            main.startColor = Gray(main.startColor);
            main.startSizeMultiplier *= DustSize;
            main.startLifetimeMultiplier *= DustLifetime;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.radius = DustRadius;
            ParticleSystem.EmissionModule emission = particles.emission;
            for (int i = 0; i < emission.burstCount; i++)
            {
                ParticleSystem.Burst burst = emission.GetBurst(i);
                burst.count = new ParticleSystem.MinMaxCurve(DustParticles);
                emission.SetBurst(i, burst);
            }
        }
        foreach (RemoveAfterSeconds timer in dust.GetComponentsInChildren<RemoveAfterSeconds>(true))
        {
            timer.seconds = DustSeconds;
        }
        dustTemplate = dust;
        return dustTemplate;
    }

    /// <summary>The dust's colours made gray, a little more transparent than the chalk's.</summary>
    private static ParticleSystem.MinMaxGradient Gray(ParticleSystem.MinMaxGradient colours)
    {
        Color Shade(Color c) => new Color(DustColour.r, DustColour.g, DustColour.b, c.a * DustOpacity);
        switch (colours.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(Shade(colours.color));
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(Shade(colours.colorMin), Shade(colours.colorMax));
            default:
                return new ParticleSystem.MinMaxGradient(Shade(Color.white));
        }
    }

    /// <summary>A stone that hits a scout hard enough sends the marked knock-down.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.RPCA_Fall))]
    private static class StoneHit
    {
        private static void Prefix(Character __instance, ref float seconds)
        {
            if (Mathf.Abs(seconds - KnockdownMarked) > 0.0001f)
            {
                return;
            }
            // No coconut bonk: the stone shatters on the same hit, and its rock smash is the sound of it (see Shatter).
            seconds = Knockdown;
            if (__instance.IsLocal && !__instance.data.dead)
            {
                ExactStatus.Add(__instance.refs.afflictions, CharacterAfflictions.STATUSTYPE.Injury, InjuryNow, "stone");
            }
        }
    }

    /// <summary>The breaking reaches every client: each one hears it and sees the rubble.</summary>
    [HarmonyPatch(typeof(Breakable), "RPC_BroadcastBreak")]
    private static class Shatter
    {
        private static void Postfix(Breakable __instance)
        {
            if (!IsStone(__instance.GetComponent<Item>()))
            {
                return;
            }
            Vector3 at = __instance.transform.position;
            Breaks++;
            BreakSound()?.Play(at);
            GameObject dust = DustTemplate();
            if (dust != null)
            {
                Object.Instantiate(dust, at, Quaternion.identity);
                DustClouds++;
            }
        }
    }
}
