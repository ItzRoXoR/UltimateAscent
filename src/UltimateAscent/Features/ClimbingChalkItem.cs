using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Climbing Chalk becomes a real item. The game has it unfinished: no icon, the prompt "read",
/// never handed out, nothing to use it up, its effect switched off (none of its action's triggers is
/// set) and nothing that reads the climbing multiplier of that effect.
/// <list type="bullet">
/// <item>From ascent 9 it turns up wherever the Energy Drink does, as rarely as the Big Lollipop (its
/// rarity, Legendary). Below ascent 9 it never spawns. A custom run lists it on the Custom Run screen
/// with the other items (<see cref="CustomRunItems"/>), and its switch there decides.</item>
/// <item>It has three uses (a use bar like the game's other multi-use items); from ascent 14 two, as
/// that ascent takes two uses per use of every item. Each use takes 2 s. For 16 s, climbing (walls,
/// ropes and vines alike) costs 60% of its stamina; from ascent 14, 75% for 12 s. Another use restarts
/// the time.</item>
/// <item>Cooking darkens it but gives it none of the game's food changes (extra stamina, poison once
/// incinerated); a burned chalk (cooked three times or more) can no longer be used.</item>
/// <item>While it is being used, its user hears hands rubbing (only its user, like the game's own
/// item-use sounds). After each use, a puff of dust bursts around the scout for everyone: the
/// Sunscreen's spray cloud, without its sound and without its sun protection.</item>
/// <item>While the effect lasts, a chalked hand shows above the right end of the stamina bar (for the
/// scout the HUD follows, so also when spectating); it goes when the effect ends.</item>
/// <item>While the effect lasts, the scout's hands are chalk white for everyone, gloves included
/// (<see cref="ClimbingChalkHands"/>).</item>
/// <item>It gets an icon (rendered from its model), its name, the prompt "use", and loses the game's
/// "mystical" tag: picking up a mystical item earns a Steam achievement.</item>
/// </list>
/// </summary>
internal static class ClimbingChalkItem
{
    internal const string PrefabName = "ClimbingChalk";
    internal const string NameKey = "NAME_CLIMBING CHALK";

    internal const string EnergyDrinkPrefab = "Energy Drink";

    /// <summary>The Big Lollipop, whose rarity the chalk has.</summary>
    internal const string BigLollipopPrefab = "Lollipop";
    private const string SunscreenPrefab = "Sunscreen";
    private const string IconResource = "UltimateAscent.ClimbingChalk_icon.png";
    private const string StatusIconResource = "UltimateAscent.ClimbingChalk_status.png";
    private const string RubResource = "UltimateAscent.ClimbingChalk_rub.wav";
    private const string UsePrompt = "use";

    /// <summary>The share of climbing stamina it costs while chalked, and for how long.</summary>
    internal const float Stamina = 0.6f;
    internal const float Seconds = 16f;

    /// <summary>How many times one chalk can be used (ascent 14's two uses per use leave two).</summary>
    internal const int Uses = 3;

    /// <summary>From ascent 14.</summary>
    internal const float WorseStamina = 0.75f;
    internal const float WorseSeconds = 12f;

    /// <summary>
    /// The rub (the user's clip without its silent first second, its rubbing played twice so it fills
    /// the 2 s use, peak -1 dBFS), as loud as the game's own item sounds where the scout hears it.
    /// </summary>
    private const float RubVolume = 0.25f;

    /// <summary>
    /// The dust bursts out from the scout's hands, from a shell this wide around them, slower than the
    /// spray (5-10 m/s) so it stays around the scout; denser than the spray, and it lasts twice as long.
    /// </summary>
    private const float DustRadius = 0.35f;
    private const float DustSpeedMin = 0.5f;
    private const float DustSpeedMax = 1.5f;
    private const int DustParticles = 50;
    private const float DustOpacity = 2f;
    private const float DustLifetime = 2f;
    private const float DustSeconds = 9f;

    /// <summary>
    /// The chalked hand above the stamina bar, in the row where the game shows its status icons over
    /// the bar, just past the bar's right end: right above the campfire icon's place (the bar's left
    /// end is where stamina-number mods put their labels). Measured from the bar's top-right corner.
    /// </summary>
    private const float StatusIconSize = 40f;
    private static readonly Vector2 StatusIconPosition = new Vector2(32f, 22.5f);

    private static Texture2D icon;
    private static bool iconFailed;
    private static Sprite statusSprite;
    private static bool statusFailed;
    private static GameObject statusIcon;
    private static SFX_Instance rub;
    private static bool rubFailed;
    private static SFX_Player.SoundEffectHandle rubbing;
    private static GameObject dustTemplate;
    private static bool dustFailed;

    internal static bool IsChalk(Item item)
    {
        return item != null && item.gameObject.name.StartsWith(PrefabName, StringComparison.Ordinal);
    }

    internal static void Reset()
    {
        rubbing = null;
    }

    /// <summary>The scout has the chalk's effect (on every player's game: the effect is sent to everyone).</summary>
    internal static bool Chalked(Character character)
    {
        return character.refs.afflictions != null && character.refs.afflictions.HasAfflictionType(Affliction.AfflictionType.ClimbingChalk, out _);
    }

    /// <summary>The effect of one chalk, for the ascent being climbed.</summary>
    internal static Affliction_ClimbingChalk Effect()
    {
        bool worse = Rules.Enabled(Lvl.WorseItems);
        return new Affliction_ClimbingChalk
        {
            totalTime = worse ? WorseSeconds : Seconds,
            climbStaminaMultiplier = worse ? WorseStamina : Stamina,
        };
    }

    /// <summary>
    /// The item's own fields, on the prefab (the inventory shows the prefab's icon) and on every copy.
    /// The effect action fires once the 2 s use is done. Its triggers are read each time it subscribes
    /// (OnEnable, then Start, which subscribes it afresh), so setting them in its Item's Awake is enough.
    /// The item's Start then gives a new chalk its uses (with the use bar).
    /// </summary>
    internal static void Prepare(Item item)
    {
        Texture2D texture = Icon();
        if (texture != null)
        {
            item.UIData.icon = texture;
        }
        item.UIData.mainInteractPrompt = UsePrompt;
        item.itemTags &= ~Item.ItemTags.Mystical;
        item.totalUses = Uses;
        foreach (Action_ApplyAffliction effect in item.GetComponents<Action_ApplyAffliction>())
        {
            effect.OnCastFinished = true;
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.Awake))]
    private static class ChalkItem
    {
        private static void Postfix(Item __instance)
        {
            if (!IsChalk(__instance))
            {
                return;
            }
            if (ItemDatabase.TryGetItem(__instance.itemID, out Item prefab) && prefab != null)
            {
                Prepare(prefab);
            }
            Prepare(__instance);
            if (!__instance.TryGetComponent(out Action_ReduceUses _))
            {
                // Each finished use takes one off; the last one uses the chalk up. Its own Start
                // subscribes it to the finished use (the flag is still off in OnEnable).
                Action_ReduceUses uses = __instance.gameObject.AddComponent<Action_ReduceUses>();
                uses.consumeOnFullyUsed = true;
                uses.OnCastFinished = true;
            }
        }
    }

    /// <summary>
    /// The game gives an item its uses when it first starts, unless something has read its uses before
    /// (the inventory does, for an item made straight into a hand or a slot), and a chalk carried over
    /// from a version where it had a single use has none either. A chalk without uses gets its three.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.Start))]
    private static class ChalkUses
    {
        private static void Postfix(Item __instance)
        {
            if (!IsChalk(__instance))
            {
                return;
            }
            OptionableIntItemData uses = __instance.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (!uses.HasData)
            {
                uses.HasData = true;
                uses.Value = Uses;
                __instance.SetUseRemainingPercentage(1f);
            }
        }
    }

    /// <summary>The prefab's effect is the game's draft (80% for 20 s); it gets this ascent's instead.</summary>
    [HarmonyPatch(typeof(Action_ApplyAffliction), nameof(Action_ApplyAffliction.RunAction))]
    private static class ChalkEffect
    {
        private static void Prefix(Action_ApplyAffliction __instance)
        {
            if (IsChalk(__instance.item))
            {
                __instance.affliction = Effect();
                __instance.extraAfflictions = null;
            }
        }
    }

    /// <summary>While one of these runs, the stamina it spends is climbing stamina.</summary>
    private static class Climbing
    {
        [ThreadStatic] internal static int Depth;
    }

    [HarmonyPatch]
    private static class ClimbingScope
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Walls: holding on, the climbing lunge, grabbing on.
            yield return AccessTools.Method(typeof(CharacterClimbing), nameof(CharacterClimbing.Update));
            yield return AccessTools.Method(typeof(CharacterClimbing), nameof(CharacterClimbing.RPCA_ClimbJump));
            yield return AccessTools.Method(typeof(CharacterClimbing), nameof(CharacterClimbing.StartClimbRpc));
            yield return AccessTools.Method(typeof(CharacterRopeHandling), nameof(CharacterRopeHandling.Update));
            yield return AccessTools.Method(typeof(CharacterVineClimbing), nameof(CharacterVineClimbing.Update));
            yield return AccessTools.Method(typeof(CharacterVineClimbing), nameof(CharacterVineClimbing.GrabVineRpc));
        }

        private static void Prefix()
        {
            Climbing.Depth++;
        }

        private static Exception Finalizer(Exception __exception)
        {
            Climbing.Depth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    private static class ChalkedClimbing
    {
        private static void Prefix(Character __instance, ref float usage)
        {
            if (Climbing.Depth > 0
                && __instance.refs.afflictions.HasAfflictionType(Affliction.AfflictionType.ClimbingChalk, out Affliction affliction)
                && affliction is Affliction_ClimbingChalk chalk)
            {
                usage *= chalk.climbStaminaMultiplier;
            }
        }
    }

    /// <summary>The weight of the Big Lollipop's rarity in the loot tables (the chalk's own, if that item is missing).</summary>
    internal static int SpawnWeight()
    {
        LootData lollipop = ItemDatabase.TryGetItem(BigLollipopPrefab, out Item item) && item != null ? item.GetComponent<LootData>() : null;
        Rarity rarity = lollipop != null ? lollipop.Rarity : Rarity.Legendary;
        return LootData.RarityWeights.TryGetValue(rarity, out int weight) ? weight : 15;
    }

    /// <summary>
    /// The chalk joins every pool the Energy Drink is in, as rare as the Big Lollipop. The table is
    /// built once and kept, so the chalk is always in it; <see cref="OnlyFromAscent9"/> keeps it out
    /// of what actually spawns below ascent 9.
    /// </summary>
    [HarmonyPatch(typeof(LootData), nameof(LootData.PopulateLootData))]
    private static class SpawnLikeEnergyDrink
    {
        private static void Postfix()
        {
            if (LootData.AllSpawnWeightData == null
                || !ItemDatabase.TryGetItem(PrefabName, out Item chalk)
                || !ItemDatabase.TryGetItem(EnergyDrinkPrefab, out Item drink))
            {
                Plugin.Log.LogWarning("The Climbing Chalk or the Energy Drink is missing from the item database; the chalk does not spawn.");
                return;
            }
            Prepare(chalk);
            int weight = SpawnWeight();
            foreach (Dictionary<ushort, int> pool in LootData.AllSpawnWeightData.Values)
            {
                if (pool.ContainsKey(drink.itemID))
                {
                    pool[chalk.itemID] = weight;
                }
            }
        }
    }

    /// <summary>A burned chalk (cooked three times or more) cannot be used.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.CanUsePrimary))]
    private static class BurnedChalkPrimary
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result && IsChalk(__instance) && BurnedItems.IsBurned(__instance))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.CanUseSecondary))]
    private static class BurnedChalkSecondary
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result && IsChalk(__instance) && BurnedItems.IsBurned(__instance))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Cooking gives food extra stamina and, once incinerated, poison, both handed out when the item is
    /// used up; the chalk is no food and gets neither. Ruining it (a scorching campfire, lava or an
    /// explosion from ascent 17) still ruins it.
    /// </summary>
    [HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.ChangeStatsCooked))]
    private static class NoFoodChanges
    {
        private static bool Prefix(ItemCooking __instance, int totalCooked)
        {
            return !IsChalk(__instance.item) || (__instance.wreckWhenCooked && totalCooked > 0);
        }
    }

    /// <summary>
    /// Spawning checks every candidate here first (luggage and ground spawns alike). In a custom run
    /// the game's own check has already read the chalk's switch on the Custom Run screen.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.IsValidToSpawn))]
    private static class OnlyFromAscent9
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result && IsChalk(__instance) && !RunSettings.IsCustomRun && !Rules.InExtraAscent)
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// The rub starts with the use, which only its user's game runs (the press is not sent to others),
    /// and follows their hands like the game's item-use sounds.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.StartUsePrimary))]
    private static class RubWhileUsing
    {
        private static void Postfix(Item __instance)
        {
            Character holder = __instance.holderCharacter;
            if (!IsChalk(__instance) || holder == null || !holder.IsLocal || SFX_Player.instance == null)
            {
                return;
            }
            SFX_Instance sound = RubSound();
            if (sound == null)
            {
                return;
            }
            StopRubbing(0f);
            rubbing = SFX_Player.instance.PlaySFX(sound, __instance.transform.position, holder.refs.carryPosRef);
        }
    }

    /// <summary>Letting go early stops it; a finished use lets it end by itself.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.CancelUsePrimary))]
    private static class RubStopsWhenCancelled
    {
        private static void Prefix(Item __instance)
        {
            if (IsChalk(__instance) && __instance.isUsingPrimary && !__instance.finishedCast)
            {
                StopRubbing(0.15f);
            }
        }
    }

    private static void StopRubbing(float fadeSeconds)
    {
        if (rubbing != null)
        {
            SFX_Player.StopPlaying(rubbing, fadeSeconds);
            rubbing = null;
        }
    }

    /// <summary>
    /// Every use's message (one use off) reaches every player, so everyone sees the dust, the last
    /// use's too (it arrives before the chalk is used up and switched off).
    /// </summary>
    [HarmonyPatch(typeof(Action_ReduceUses), nameof(Action_ReduceUses.ReduceUsesRPC))]
    private static class ChalkDust
    {
        private static void Postfix(Action_ReduceUses __instance)
        {
            Item item = __instance.item;
            if (!IsChalk(item))
            {
                return;
            }
            GameObject template = DustTemplate();
            if (template == null)
            {
                return;
            }
            // From the hands, where the chalk was rubbed in (and in front of the scout's own eyes).
            UnityEngine.Object.Instantiate(template, item.transform.position, Quaternion.identity);
        }
    }

    /// <summary>
    /// A copy of the Sunscreen's spray cloud, made under a switched-off holder so nothing of it runs:
    /// its sound (SFX_PlayOneShot) and its "AOE" child, which hands out the sun protection, are
    /// removed; it keeps its particles, its look and its 4 s self-removal. It bursts from a small ball
    /// around the scout instead of the bottle's cone.
    /// </summary>
    internal static GameObject DustTemplate()
    {
        if (dustTemplate != null || dustFailed)
        {
            return dustTemplate;
        }
        try
        {
            if (!ItemDatabase.TryGetItem(SunscreenPrefab, out Item sunscreen) || !sunscreen.TryGetComponent(out Action_Spawn spray) || spray.objectToSpawn == null)
            {
                throw new InvalidOperationException("the Sunscreen's spray cloud was not found");
            }
            GameObject holder = new GameObject("UltimateAscent Chalk Dust Template");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            GameObject dust = UnityEngine.Object.Instantiate(spray.objectToSpawn, holder.transform);
            dust.name = "UltimateAscent Chalk Dust";
            foreach (SFX_PlayOneShot sound in dust.GetComponentsInChildren<SFX_PlayOneShot>(true))
            {
                UnityEngine.Object.DestroyImmediate(sound);
            }
            foreach (AOE aoe in dust.GetComponentsInChildren<AOE>(true))
            {
                if (aoe.gameObject == dust)
                {
                    UnityEngine.Object.DestroyImmediate(aoe);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(aoe.gameObject);
                }
            }
            foreach (TimeEvent timer in dust.GetComponentsInChildren<TimeEvent>(true))
            {
                UnityEngine.Object.DestroyImmediate(timer);
            }
            ParticleSystem particles = dust.GetComponent<ParticleSystem>();
            if (particles == null)
            {
                throw new InvalidOperationException("the spray cloud has no particles");
            }
            ParticleSystem.MainModule main = particles.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(DustSpeedMin, DustSpeedMax);
            ParticleSystem.MinMaxCurve life = main.startLifetime;
            if (life.mode == ParticleSystemCurveMode.TwoConstants)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(life.constantMin * DustLifetime, life.constantMax * DustLifetime);
            }
            else if (life.mode == ParticleSystemCurveMode.Constant)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(life.constant * DustLifetime);
            }
            else
            {
                main.startLifetimeMultiplier *= DustLifetime;
            }
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = DustRadius;
            shape.radiusThickness = 0f;
            ParticleSystem.EmissionModule emission = particles.emission;
            for (int i = 0; i < emission.burstCount; i++)
            {
                ParticleSystem.Burst burst = emission.GetBurst(i);
                burst.count = new ParticleSystem.MinMaxCurve(DustParticles);
                emission.SetBurst(i, burst);
            }
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            if (colour.enabled)
            {
                ParticleSystem.MinMaxGradient gradient = colour.color;
                gradient.gradient = Denser(gradient.gradient);
                gradient.gradientMin = Denser(gradient.gradientMin);
                gradient.gradientMax = Denser(gradient.gradientMax);
                colour.color = gradient;
            }
            foreach (RemoveAfterSeconds timer in dust.GetComponentsInChildren<RemoveAfterSeconds>(true))
            {
                timer.seconds = DustSeconds;
            }
            dustTemplate = dust;
        }
        catch (Exception ex)
        {
            dustFailed = true;
            Plugin.Log.LogError("The Climbing Chalk's dust could not be made; it is used up without it. " + ex.Message);
        }
        return dustTemplate;
    }

    /// <summary>
    /// The chalked hand shows while the scout the HUD follows has the chalk's effect (a teammate's
    /// reaches every player, and its end too). It hangs off the bar itself: the game's own bar icons
    /// (campfire, shield) sit inside the bar's outline, whose mask would cut off anything above the bar.
    /// </summary>
    [HarmonyPatch(typeof(StaminaBar), nameof(StaminaBar.Update))]
    private static class ChalkedHandsIcon
    {
        private static void Postfix(StaminaBar __instance)
        {
            Character observed = Character.observedCharacter;
            bool chalked = observed != null && observed.refs.afflictions.HasAfflictionType(Affliction.AfflictionType.ClimbingChalk, out _);
            if (statusIcon == null || statusIcon.transform.parent != __instance.transform)
            {
                if (!chalked)
                {
                    return;
                }
                statusIcon = StatusIcon(__instance);
                if (statusIcon == null)
                {
                    return;
                }
            }
            if (statusIcon.activeSelf != chalked)
            {
                statusIcon.SetActive(chalked);
            }
        }
    }

    private static GameObject StatusIcon(StaminaBar bar)
    {
        Sprite sprite = StatusSprite();
        if (sprite == null)
        {
            return null;
        }
        GameObject hand = new GameObject("UltimateAscent Chalk Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        hand.layer = bar.gameObject.layer;
        RectTransform rect = (RectTransform)hand.transform;
        rect.SetParent(bar.transform, false);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(StatusIconSize, StatusIconSize);
        rect.anchoredPosition = StatusIconPosition;
        UnityEngine.UI.Image image = hand.GetComponent<UnityEngine.UI.Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        hand.SetActive(false);
        return hand;
    }

    private static Sprite StatusSprite()
    {
        if (statusSprite != null || statusFailed)
        {
            return statusSprite;
        }
        try
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "ClimbingChalk_status",
                wrapMode = TextureWrapMode.Clamp,
                // Shown at about a fifth of its size: blend the mipmaps.
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!texture.LoadImage(ClimbingChalkModel.Resource(StatusIconResource), true))
            {
                throw new InvalidDataException("the icon is not a readable image");
            }
            statusSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            statusSprite.name = "ClimbingChalk_status";
            statusSprite.hideFlags = HideFlags.HideAndDontSave;
        }
        catch (Exception ex)
        {
            statusFailed = true;
            Plugin.Log.LogError("The Climbing Chalk's status icon could not be loaded: " + ex.Message);
        }
        return statusSprite;
    }

    /// <summary>The same gradient, more opaque all along.</summary>
    private static Gradient Denser(Gradient gradient)
    {
        if (gradient == null)
        {
            return null;
        }
        GradientAlphaKey[] alphas = gradient.alphaKeys;
        for (int i = 0; i < alphas.Length; i++)
        {
            alphas[i].alpha = Mathf.Min(1f, alphas[i].alpha * DustOpacity);
        }
        Gradient denser = new Gradient { mode = gradient.mode };
        denser.SetKeys(gradient.colorKeys, alphas);
        return denser;
    }

    private static SFX_Instance RubSound()
    {
        if (rub != null || rubFailed)
        {
            return rub;
        }
        try
        {
            AudioClip clip = ReadWav(ClimbingChalkModel.Resource(RubResource), "ClimbingChalk_rub");
            SFX_Instance sound = ScriptableObject.CreateInstance<SFX_Instance>();
            sound.name = "SFXI Climbing Chalk Rub";
            sound.hideFlags = HideFlags.HideAndDontSave;
            sound.clips = new[] { clip };
            // The game's item-use sound settings, at the rub's own volume.
            sound.settings = new SFX_Settings
            {
                volume = RubVolume,
                volume_Variation = 0f,
                pitch = 1f,
                pitch_Variation = 0.1f,
                spatialBlend = 1f,
                dopplerLevel = 0.125f,
                range = 100f,
                cooldown = 0.02f,
            };
            rub = sound;
        }
        catch (Exception ex)
        {
            rubFailed = true;
            Plugin.Log.LogError("The Climbing Chalk's sound could not be loaded: " + ex.Message);
        }
        return rub;
    }

    /// <summary>A 16-bit PCM WAV, as shipped in this DLL, as an AudioClip.</summary>
    internal static AudioClip ReadWav(byte[] wav, string name)
    {
        if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
        {
            throw new InvalidDataException("the sound is not a WAV file");
        }
        int format = 0, channels = 0, rate = 0, bits = 0, dataStart = -1, dataLength = 0;
        int position = 12;
        while (position + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, position, 4);
            int size = BitConverter.ToInt32(wav, position + 4);
            if (id == "fmt " && size >= 16)
            {
                format = BitConverter.ToInt16(wav, position + 8);
                channels = BitConverter.ToInt16(wav, position + 10);
                rate = BitConverter.ToInt32(wav, position + 12);
                bits = BitConverter.ToInt16(wav, position + 22);
            }
            else if (id == "data")
            {
                dataStart = position + 8;
                dataLength = Math.Min(size, wav.Length - dataStart);
                break;
            }
            position += 8 + size + (size & 1);
        }
        if (format != 1 || bits != 16 || channels < 1 || rate <= 0 || dataStart < 0)
        {
            throw new InvalidDataException("the sound is not 16-bit PCM");
        }
        int count = dataLength / 2 / channels * channels;
        float[] samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = BitConverter.ToInt16(wav, dataStart + 2 * i) / 32768f;
        }
        AudioClip clip = AudioClip.Create(name, count / channels, channels, rate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    private static Texture2D Icon()
    {
        if (icon != null || iconFailed)
        {
            return icon;
        }
        try
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "ClimbingChalk_icon",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!texture.LoadImage(ClimbingChalkModel.Resource(IconResource), true))
            {
                throw new InvalidDataException("the icon is not a readable image");
            }
            icon = texture;
        }
        catch (Exception ex)
        {
            iconFailed = true;
            Plugin.Log.LogError("The Climbing Chalk's icon could not be loaded: " + ex.Message);
        }
        return icon;
    }
}
