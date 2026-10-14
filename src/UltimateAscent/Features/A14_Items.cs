using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Peak;
using Peak.Afflictions;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 14: more items are worse.
/// <list type="bullet">
/// <item>Raw food: an egg (the Caldera's and the Gloom's) or a tick eaten raw poisons for 5 at once.</item>
/// <item>Sunscreen keeps off only 75% of the sun's heat (the game: all of it), in the Mesa and under
/// ascent 23's sun alike.</item>
/// <item>A heat pack warms for 20 s (the game: 60 s).</item>
/// <item>The Scoutmaster's Bugle sends him after his target for 180 s (the game: 120 s).</item>
/// <item>A Bounce Fungus pad gives back 2.5 stamina per bounce (the game: 10).</item>
/// <item>The Roots' copy of the parasol becomes the Lightweight Parasol: it weighs nothing below
/// ascent 14 and in custom runs, 5% of the bar from 14 (as much as the Parasol), and is the one
/// parasol that still slows your fall (every other one pulls you down, see A14_WorseItems). It is the
/// Lightweight Parasol from ascent 9 and in custom runs (see <see cref="IsLightweightParasol"/>).</item>
/// <item>The Energy Drink speeds your walking up by 30% (see A14_WorseItems' energy-drink rule); from
/// ascent 15 its crash brings 50 drowsiness. The Big Lollipop's drowsiness comes over 12 s and adds up
/// to 85 (the game: 33 over 10 s).</item>
/// <item>Rope spools hold half the rope: 25 segments, at most 20 in one rope (the game: 50 and 40).</item>
/// <item>Thrown Shelf and Cloud Fungus fly as if 1.5 times as heavy, a Warp Fungus as if 1.35 times
/// (the throw speed divided by that).</item>
/// <item>A Magic Bean's vine grows at most 15 m (the game: 20 m); standing on it sticks 2.5 thorns a
/// second into you, and each 2.5 works its way out again 30 s later.</item>
/// <item>Placed pitons are the Alps' cracking pitons, 30% sturdier: they hold for 1.3-6.5 s of hanging
/// (the Alps': 1-5 s), then crack five times (the Alps': four) and break.</item>
/// <item>A snowball that bonks a scout gives 30 cold and knocks them down for 2 s (the game: no cold,
/// 1 s); holding one chills 2.5 a second.</item>
/// <item>A Remedy Fungus's cloud lasts 8 s (the game: 15 s). The Anti-Zooka's bubble is 8 m across and
/// lasts 40 s (the game: 10 m, 60 s). A Portable Stove burns for 30 s (the game: 60 s).</item>
/// <item>The Book of Bones: becoming a skeleton costs 50 curse, 35 after its cure (its curse is not
/// weakened to 60%); a teammate revived with it gets 20 curse (the game: 15).</item>
/// <item>The Ancient Idol weighs half the bar. A raw Mandrake gives 30 spores.</item>
/// <item>Whatever you feed the Jetpack gives 35% less fuel.</item>
/// <item>A checkpoint flag that saves you curses you for 10.</item>
/// <item>The Cursed Skull is the only cure for the crab status (Bing Bong's, see A20_BingBong): cure-alls
/// such as the Book of Bones leave it alone, the skull takes it with everything else, and its clear
/// keeps 10% of every status (crab included) instead of the 25% revives keep.</item>
/// <item>The Ritual Dagger turns the teammate it is used on into a zombie instead of killing them.</item>
/// <item>Pandora's Lunchbox can hand out the crab status too.</item>
/// <item>A lit Lantern takes 1.5 cold a second off the scouts within 4.5 m of it (the game: 2.5); a lit
/// Faerie Lantern 2.5 a second of each of cold, injury, poison, drowsiness and heat (the game: 5).</item>
/// </list>
/// </summary>
internal static class A14_Items
{
    private static bool On => Rules.Enabled(Lvl.WorseItems);

    private const CharacterAfflictions.STATUSTYPE Poison = CharacterAfflictions.STATUSTYPE.Poison;
    private const CharacterAfflictions.STATUSTYPE Crab = CharacterAfflictions.STATUSTYPE.Crab;
    private const CharacterAfflictions.STATUSTYPE Cold = CharacterAfflictions.STATUSTYPE.Cold;
    private const CharacterAfflictions.STATUSTYPE Curse = CharacterAfflictions.STATUSTYPE.Curse;
    private const CharacterAfflictions.STATUSTYPE Thorns = CharacterAfflictions.STATUSTYPE.Thorns;

    /// <summary>A raw egg or tick: 5 poison at once.</summary>
    internal const float RawFoodPoison = 0.05f;

    internal const string EggName = "Egg";

    /// <summary>The share of the sun's heat that gets through sunscreen.</summary>
    internal const float SunscreenLeak = 0.25f;

    internal const float HeatPackSeconds = 20f;
    internal const string HeatPackName = "Heat Pack";

    private const float BugleChaseSeconds = 180f;

    private const float BouncePadStamina = 0.025f;

    /// <summary>The game's second parasol (it drops from Roots suitcases); the same item otherwise.</summary>
    internal const string RootsParasolPrefab = "Parasol_Roots Variant";

    internal const string LightweightParasolKey = "NAME_LIGHTWEIGHT PARASOL";

    internal static bool IsRootsParasol(Item item)
    {
        return item != null && item.gameObject.name.StartsWith(RootsParasolPrefab, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The Lightweight Parasol (name, no weight) exists from ascent 9; from 14 it weighs as much as
    /// the Parasol and is the one parasol that still slows your fall. Custom runs have it too,
    /// weightless, with its own switch on the Custom Run screen (<see cref="CustomRunItems"/>).
    /// </summary>
    internal static bool IsLightweightParasol(Item item)
    {
        return LightweightParasolExists && IsRootsParasol(item);
    }

    /// <summary>The Roots parasol is the Lightweight Parasol in this run (from ascent 9, and in custom runs).</summary>
    internal static bool LightweightParasolExists => Rules.InExtraAscent || CustomRunItems.InCustomRun;

    internal const string EnergyDrinkName = "Energy Drink";
    internal const string BigLollipopName = "Big Lollipop";

    /// <summary>The Energy Drink's walking bonus (+30%; the game: +50%).</summary>
    internal const float EnergyDrinkWalking = 0.3f;

    /// <summary>From ascent 15: the Energy Drink's crash (the game: 25 drowsiness).</summary>
    internal const float EnergyDrinkCrash = 0.5f;

    /// <summary>The Big Lollipop's drowsiness, in all and over how long (the game: 33 over 10 s).</summary>
    internal const float LollipopDrowsiness = 0.85f;
    internal const float LollipopDrowsySeconds = 12f;

    /// <summary>Rope spools: total rope and the longest single rope, in segments.</summary>
    private const float SpoolRope = 25f;
    private const float LongestRope = 20f;

    /// <summary>How many times as heavy each fungus flies when thrown: its throw speed is divided by this.</summary>
    private static readonly (string prefab, float heavier)[] HeavyFungus = { ("ShelfShroom", 1.5f), ("CloudFungus", 1.5f), ("WarpFungus", 1.35f) };

    /// <summary>A Magic Bean's vine: share of the game's longest vine, thorns per second while standing on it, and how long each 2.5 of them stays in.</summary>
    private const float BeanVineLength = 0.75f;
    private const float BeanVineThorns = 0.025f;
    private const float BeanThornSeconds = 30f;

    private const float SnowballCold = 0.3f;
    private const float SnowballKnockdown = 2f;

    /// <summary>
    /// The knock-down a snowball's bonk sends: 2 s plus a mark, so the scout it hit can tell it from
    /// any other knock-down (the game sends only the seconds) and add the cold on their own client.
    /// </summary>
    private const float SnowballKnockdownMarked = SnowballKnockdown + 0.0004f;

    private const float HeldSnowballCold = 0.025f;

    internal const string MandrakeName = "Mandrake";
    internal const float MandrakeSpores = 0.3f;

    /// <summary>The Remedy Fungus's cloud is this child of its spawn; it removes itself after its seconds.</summary>
    internal const string RemedyCloudParent = "HealingPuffShroomSpawn";
    internal const float RemedyCloudSeconds = 8f;

    /// <summary>The Anti-Zooka's bubble: its collider's radius is 0.5, so its full-grown scale is its diameter.</summary>
    private const string AntiZookaBubble = "AntiSphere_Projectile";
    private const float AntiZookaBubbleDiameter = 8f;
    private const float AntiZookaBubbleSeconds = 40f;

    private const string StoveName = "NAME_PORTABLE STOVE";
    private const float StoveSeconds = 30f;

    private const float BookReviveCurse = 0.2f;

    private const string AncientIdolName = "Ancient Idol";

    private const string LanternName = "Lantern";
    private const string FaerieLanternName = "Faerie Lantern";

    /// <summary>What a lit Lantern takes off a second: cold (the game: 2.5).</summary>
    internal const float LanternWarmth = 0.015f;

    /// <summary>What a lit Faerie Lantern takes off a second, of each of its statuses (the game: 5).</summary>
    internal const float FaerieLanternHealing = 0.025f;

    /// <summary>The Ancient Idol's weight: half the bar (each point is 2.5).</summary>
    private const int AncientIdolWeight = 20;

    /// <summary>The Lightweight Parasol's weight from ascent 14: 5% of the bar, as much as the Parasol.</summary>
    internal const int WorseLightweightParasolWeight = 2;

    /// <summary>A carried Balloon takes 2% of the bar off your weight, a Balloon Bunch 8% (the game, from ascent 3: 2.5% and 12.5%).</summary>
    private const string BalloonName = "Balloon";
    private const string BalloonBunchName = "Balloon Bunch";
    private const float BalloonLift = 0.02f;
    private const float BalloonBunchLift = 0.08f;

    private const string CrackingPitonPrefab = "0_Items/ClimbingSpikeHammered_Shitty";
    private const float PitonSturdiness = 1.3f;
    private const int PitonCracks = 5;

    private const float CheckpointCurse = 0.1f;

    private const string CursedSkullName = "Cursed Skull";

    /// <summary>The share of every status the Cursed Skull's clear keeps (revives keep 25%).</summary>
    internal const float CursedSkullResidue = 0.1f;

    [ThreadStatic] private static int energyDrinkScope;
    [ThreadStatic] private static int cursedSkullScope;

    /// <summary>An Energy Drink's effect is being applied right now (on this client).</summary>
    internal static bool InEnergyDrink => energyDrinkScope > 0;

    /// <summary>The Cursed Skull's clear is being applied right now (on this client).</summary>
    internal static bool InCursedSkull => cursedSkullScope > 0;

    /// <summary>Whether sunscreen still lets some of the sun's heat through.</summary>
    internal static bool SunscreenLeaks => On;

    [HarmonyPatch(typeof(Action_RestoreHunger), nameof(Action_RestoreHunger.RunAction))]
    private static class RawFood
    {
        private static void Postfix(Action_RestoreHunger __instance)
        {
            Character character = __instance.character;
            Item item = __instance.item;
            if (!On || character == null || !character.IsLocal || item == null || BurnedItems.CookedLevel(item) != 0)
            {
                return;
            }
            bool egg = item.UIData != null && item.UIData.itemName == EggName;
            if (egg || A14_WorseItems.Ticks.IsTick(item))
            {
                ExactStatus.Add(character.refs.afflictions, Poison, A15_StrongerAilments.FromAscent15(Poison, RawFoodPoison), "raw food");
            }
            else if (NameOf(item) == MandrakeName)
            {
                CharacterAfflictions.STATUSTYPE spores = CharacterAfflictions.STATUSTYPE.Spores;
                ExactStatus.Add(character.refs.afflictions, spores, A15_StrongerAilments.FromAscent15(spores, MandrakeSpores), "raw mandrake");
            }
        }
    }

    /// <summary>
    /// The Mesa's sun: sunscreen lets a quarter of the heat through. Not while True Final Ascent runs
    /// an Apocalypse weather (TFA rests More Ascents' sun the same way).
    /// </summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddSunHeat))]
    private static class LeakySunscreen
    {
        private static bool Prefix(CharacterAfflictions __instance, float amount)
        {
            Character character = __instance.character;
            if (!On || character == null || !character.data.wearingSunscreen || Compat.Tfa.WeatherRunning)
            {
                return true;
            }
            Item held = character.data.currentItem;
            bool shaded = held != null && ((held.TryGetComponent(out Parasol parasol) && parasol.isOpen) || held.gliderHold);
            bool heatOff = RunSettings.IsCustomRun && RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_Heat) == 0;
            if (!shaded && !heatOff)
            {
                __instance.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, amount * SunscreenLeak);
            }
            return false;
        }
    }

    internal static string NameOf(Item item)
    {
        return item != null && item.UIData != null ? item.UIData.itemName : null;
    }

    [HarmonyPatch(typeof(Action_ApplyAffliction), nameof(Action_ApplyAffliction.RunAction))]
    private static class ItemAfflictions
    {
        private static void Prefix(Action_ApplyAffliction __instance, out bool __state)
        {
            __state = false;
            if (!On)
            {
                return;
            }
            string name = NameOf(__instance.item);
            if (name == HeatPackName && __instance.affliction is Affliction_AdjustColdOverTime warmth)
            {
                Overrides.Set(warmth, nameof(Affliction.totalTime), HeatPackSeconds);
            }
            else if (name == EnergyDrinkName)
            {
                energyDrinkScope++;
                __state = true;
            }
            else if (name == BigLollipopName && __instance.affliction is Affliction_InfiniteStamina rush &&
                     rush.drowsyAffliction is Affliction_AdjustDrowsyOverTime crash && crash.statusPerSecond > 0f)
            {
                Overrides.Set(crash, nameof(Affliction.totalTime), LollipopDrowsySeconds);
                Overrides.Set(crash, nameof(Affliction_AdjustDrowsyOverTime.statusPerSecond), LollipopDrowsiness / LollipopDrowsySeconds);
            }
        }

        private static Exception Finalizer(bool __state, Exception __exception)
        {
            if (__state)
            {
                energyDrinkScope--;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(RopeSpool), nameof(RopeSpool.Awake))]
    private static class ShortSpool
    {
        private static void Postfix(RopeSpool __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(RopeSpool.ropeStartFuel), SpoolRope);
                // Its own copy of the fuel, used until the item's data is read.
                __instance.ropeFuel = Mathf.Min(__instance.ropeFuel, SpoolRope);
            }
        }
    }

    [HarmonyPatch(typeof(RopeSpool), nameof(RopeSpool.FixedUpdate))]
    private static class ShortRopes
    {
        private static void Postfix(RopeSpool __instance)
        {
            if (!On || __instance.rope == null || !__instance.photonView.IsMine)
            {
                return;
            }
            float longest = Mathf.Max(__instance.minSegments, Mathf.Min(LongestRope, __instance.ropeFuel));
            if (__instance.Segments > longest)
            {
                __instance.Segments = longest;
                __instance.rope.Segments = longest;
            }
        }
    }

    /// <summary>The host launches thrown items; a heavy fungus leaves the hand slower, by how much heavier it flies.</summary>
    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.DropItemRpc))]
    private static class HeavyFungi
    {
        private static void Prefix(CharacterItems __instance, out Vector2? __state)
        {
            __state = null;
            Item held = __instance.character != null ? __instance.character.data.currentItem : null;
            int fungus = held == null ? -1 : Array.FindIndex(HeavyFungus, entry => held.gameObject.name.StartsWith(entry.prefab, StringComparison.Ordinal));
            if (!On || fungus < 0)
            {
                return;
            }
            __state = new Vector2(__instance.minThrowForce, __instance.maxThrowForce);
            __instance.minThrowForce /= HeavyFungus[fungus].heavier;
            __instance.maxThrowForce /= HeavyFungus[fungus].heavier;
        }

        private static Exception Finalizer(CharacterItems __instance, Vector2? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.minThrowForce = __state.Value.x;
                __instance.maxThrowForce = __state.Value.y;
            }
            return __exception;
        }
    }

    /// <summary>When each 2.5 of the Magic Bean's thorns went into the local scout, oldest first.</summary>
    private static readonly Queue<float> beanThorns = new Queue<float>();
    private static float beanThornsOwed;

    /// <summary>
    /// Called every frame: a held snowball chills; a Magic Bean's vine sticks thorns into whoever stands
    /// on it, and they work their way out again.
    /// </summary>
    internal static void Tick()
    {
        Character local = Character.localCharacter;
        if (!On || local == null || local.data.dead)
        {
            ClearBeanThorns(null);
            return;
        }
        CharacterAfflictions afflictions = local.refs.afflictions;
        Item held = local.data.currentItem;
        if (held != null && held.TryGetComponent(out Snowball _))
        {
            ExactStatus.Add(afflictions, Cold, A15_StrongerAilments.FromAscent15(Cold, HeldSnowballCold * Time.deltaTime), "held snowball");
        }
        bool changed = false;
        int added = 0;
        float now = Time.time;
        while (beanThorns.Count > 0 && now - beanThorns.Peek() >= BeanThornSeconds)
        {
            beanThorns.Dequeue();
            changed = true;
        }
        // Like the game's thorns, none for an invincible scout or a skeleton.
        if (!local.data.isInvincible && !local.data.isSkeleton && StandsOnBeanVine(local))
        {
            beanThornsOwed += BeanVineThorns * Time.deltaTime;
            while (beanThornsOwed >= A15_StrongerAilments.Step)
            {
                beanThornsOwed -= A15_StrongerAilments.Step;
                beanThorns.Enqueue(now);
                added++;
            }
        }
        if (changed || added > 0)
        {
            afflictions.UpdateWeight();
        }
        if (added > 0)
        {
            // The sound, screen effect and particles the game plays for new thorns.
            float amount = added * A15_StrongerAilments.Step;
            afflictions.StatusSFX(Thorns, amount);
            if (local == Character.observedCharacter)
            {
                GUIManager.instance.AddStatusFX(Thorns, amount);
            }
            afflictions.PlayParticle(Thorns);
        }
    }

    private static bool StandsOnBeanVine(Character character)
    {
        if (!character.data.isGrounded)
        {
            return false;
        }
        RaycastHit[] hits = Physics.RaycastAll(character.Center, Vector3.down, 2f, ~HelperFunctions.CharacterMask.value, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider != null && hit.collider.GetComponentInParent<MagicBeanVine>() != null)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>All of the bean's thorns come out at once (death, a checkpoint flag, the end of the climb).</summary>
    private static void ClearBeanThorns(CharacterAfflictions afflictions)
    {
        beanThornsOwed = 0f;
        if (beanThorns.Count == 0)
        {
            return;
        }
        beanThorns.Clear();
        if (afflictions != null)
        {
            afflictions.UpdateWeight();
        }
    }

    /// <summary>The game recounts the Thorns status from the thorns stuck in you; this adds the bean's.</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateWeight))]
    private static class BeanThornStatus
    {
        private static void Postfix(CharacterAfflictions __instance)
        {
            Character character = __instance.character;
            if (beanThorns.Count > 0 && character != null && character.IsLocal && !character.data.isSkeleton)
            {
                __instance.SetStatus(Thorns, __instance.GetCurrentStatus(Thorns) + A15_StrongerAilments.Step * beanThorns.Count);
            }
        }
    }

    /// <summary>The host decides how long a planted bean's vine grows: at most 75% of the game's longest.</summary>
    [HarmonyPatch(typeof(MagicBean), nameof(MagicBean.GetVineDistance))]
    private static class ShorterBeanVine
    {
        private static void Postfix(MagicBean __instance, ref float __result)
        {
            if (On && __instance.plantPrefab != null)
            {
                __result = Mathf.Min(__result, __instance.plantPrefab.maxLength * BeanVineLength);
            }
        }
    }

    /// <summary>Where this client just hammered a piton in (the spike appears a frame later).</summary>
    private static readonly List<(Vector3 point, float time)> placedPitons = new List<(Vector3, float)>();
    private static GameObject crackingPiton;
    private static bool lookedForCrackingPiton;

    internal static void Reset()
    {
        placedPitons.Clear();
        ClearBeanThorns(null);
    }

    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.HammerClimbingSpike))]
    private static class CrackingPitons
    {
        private static void Prefix(CharacterItems __instance, RaycastHit hit, out GameObject __state)
        {
            __state = null;
            ClimbingSpikeComponent spike = __instance.currentClimbingSpikeComponent;
            if (!On || spike == null)
            {
                return;
            }
            if (!lookedForCrackingPiton)
            {
                lookedForCrackingPiton = true;
                crackingPiton = Resources.Load<GameObject>(CrackingPitonPrefab);
                if (crackingPiton == null)
                {
                    Plugin.Log.LogWarning("Ascent 14: the Alps' cracking piton was not found; placed pitons stay as they are.");
                }
            }
            if (crackingPiton == null)
            {
                return;
            }
            __state = spike.hammeredVersionPrefab;
            spike.hammeredVersionPrefab = crackingPiton;
            placedPitons.RemoveAll(p => Time.time - p.time > 10f);
            placedPitons.Add((hit.point, Time.time));
        }

        private static Exception Finalizer(CharacterItems __instance, GameObject __state, Exception __exception)
        {
            if (__state != null && __instance.currentClimbingSpikeComponent != null)
            {
                __instance.currentClimbingSpikeComponent.hammeredVersionPrefab = __state;
            }
            return __exception;
        }
    }

    /// <summary>A piton this client placed is 30% sturdier than the Alps' (its placer counts the hanging).</summary>
    [HarmonyPatch(typeof(ShittyPiton), nameof(ShittyPiton.Start))]
    private static class SturdierPitons
    {
        private static void Postfix(ShittyPiton __instance)
        {
            int index = placedPitons.FindIndex(p => (p.point - __instance.transform.position).sqrMagnitude < 0.01f);
            if (index < 0)
            {
                return;
            }
            placedPitons.RemoveAt(index);
            __instance.totalSecondsOfHang *= PitonSturdiness;
            __instance.cracksToBreak = PitonCracks;
        }
    }

    [HarmonyPatch(typeof(Action_CallScoutmaster), nameof(Action_CallScoutmaster.RunAction))]
    private static class LongerBugleHunt
    {
        private static void Prefix(Action_CallScoutmaster __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(Action_CallScoutmaster.forcedChaseTime), BugleChaseSeconds);
            }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.GetName))]
    private static class LightweightParasolName
    {
        private static void Postfix(Item __instance, ref string __result)
        {
            if (IsLightweightParasol(__instance))
            {
                __result = LocalizedText.GetText(LightweightParasolKey);
            }
        }
    }

    /// <summary>
    /// The weight counts the item prefabs in your slots, so they are told apart by name. Balloons
    /// count as nothing here; <see cref="BalloonWeight"/> takes their share off the total instead.
    /// </summary>
    [HarmonyPatch(typeof(Item), "get_" + nameof(Item.CarryWeight))]
    private static class ItemWeights
    {
        private static void Postfix(Item __instance, ref int __result)
        {
            if (IsLightweightParasol(__instance))
            {
                __result = On ? WorseLightweightParasolWeight : 0;
                return;
            }
            if (!On)
            {
                return;
            }
            string name = NameOf(__instance);
            if (name == AncientIdolName)
            {
                __result = AncientIdolWeight;
            }
            else if (name == BalloonName || name == BalloonBunchName)
            {
                __result = 0;
            }
        }
    }

    /// <summary>
    /// A lit lantern's warmth is a status field on its light, switched on with it: the Lantern's takes
    /// cold off, the Faerie Lantern's the same amount of each of its statuses (the game uses the field's
    /// one amount for all of them). Each item gets its own copy of the field, so this changes nothing else.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.Awake))]
    private static class WeakerLanterns
    {
        private static void Postfix(Item __instance)
        {
            if (!On)
            {
                return;
            }
            string name = NameOf(__instance);
            float perSecond = name == LanternName ? LanternWarmth : name == FaerieLanternName ? FaerieLanternHealing : 0f;
            if (perSecond <= 0f)
            {
                return;
            }
            foreach (StatusField field in __instance.GetComponentsInChildren<StatusField>(true))
            {
                if (field.statusAmountPerSecond < 0f)
                {
                    field.statusAmountPerSecond = -perSecond;
                }
            }
        }
    }

    /// <summary>
    /// Takes the carried balloons' 2% and 8% off the weight the game just counted. Written straight
    /// into the bar: the game rounds a status set the usual way to its 2.5% steps.
    /// </summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateWeight))]
    private static class BalloonWeight
    {
        private static void Postfix(CharacterAfflictions __instance)
        {
            Character character = __instance.character;
            if (!On || character == null || !character.IsLocal || character.player == null)
            {
                return;
            }
            float lift = 0f;
            foreach (ItemSlot slot in CountedSlots(character.player))
            {
                string name = slot != null && !slot.IsEmpty() && slot.prefab != null ? NameOf(slot.prefab) : null;
                if (name == BalloonName)
                {
                    lift += BalloonLift;
                }
                else if (name == BalloonBunchName)
                {
                    lift += BalloonBunchLift;
                }
            }
            if (lift <= 0f)
            {
                return;
            }
            int weight = (int)CharacterAfflictions.STATUSTYPE.Weight;
            __instance.currentStatuses[weight] = Mathf.Max(0f, __instance.currentStatuses[weight] - lift);
            character.ClampStamina();
            GUIManager.instance.bar.ChangeBar();
            __instance.PushStatuses();
        }

        /// <summary>The slots the game counts for weight: your own, your backpack's and the one for a carried item.</summary>
        private static IEnumerable<ItemSlot> CountedSlots(Player player)
        {
            foreach (ItemSlot slot in player.itemSlots)
            {
                yield return slot;
            }
            BackpackSlot backpack = player.backpackSlot;
            if (!backpack.IsEmpty() && backpack.data.TryGetDataEntry(DataEntryKey.BackpackData, out BackpackData contents))
            {
                foreach (ItemSlot slot in contents.itemSlots)
                {
                    yield return slot;
                }
            }
            yield return player.GetItemSlot(250);
        }
    }

    /// <summary>Whatever you feed the Jetpack gives 35% less fuel (the game: an item's own amount on the ground, 50 into a worn Jetpack).</summary>
    internal const float JetpackFuelShare = 0.65f;

    /// <summary>A Jetpack on the ground: runs on the refueller's client, before the fuel goes out to everyone.</summary>
    [HarmonyPatch(typeof(JetpackItem), nameof(JetpackItem.AddFuel))]
    private static class LessJetpackFuel
    {
        private static void Prefix(ref float addedFuel)
        {
            if (On)
            {
                addedFuel *= JetpackFuelShare;
            }
        }
    }

    /// <summary>A Jetpack on someone's back, filled through the backpack wheel.</summary>
    [HarmonyPatch(typeof(CharacterBackpackHandler), nameof(CharacterBackpackHandler.AddFuelToJetpack))]
    private static class LessWornJetpackFuel
    {
        private static void Prefix(ref float addedFuel)
        {
            if (On)
            {
                addedFuel *= JetpackFuelShare;
            }
        }
    }

    /// <summary>The pad a thrown Bounce Fungus grows (its bouncy part is called "Bounce").</summary>
    [HarmonyPatch(typeof(CollisionModifier), nameof(CollisionModifier.Collide))]
    private static class TiringBouncePad
    {
        private static void Prefix(CollisionModifier __instance)
        {
            if (On && __instance.useBounceCoroutine && __instance.name == "Bounce")
            {
                Overrides.Set(__instance, nameof(CollisionModifier.staminaBackOnBounce), BouncePadStamina);
            }
        }
    }

    /// <summary>
    /// Runs on the saved scout's client, right after the flag has put back its saved statuses: the game
    /// then moves the scout to the flag, and statuses cannot change while it does.
    /// </summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.ApplyStatusesFromFloatArray))]
    private static class CursedCheckpoint
    {
        private static void Postfix(CharacterAfflictions __instance)
        {
            if (A14_WorseItems.CheckpointScope.Depth > 0 && On && Rules.Owns(__instance.character))
            {
                __instance.AddStatus(Curse, A15_StrongerAilments.Raw(Curse, CheckpointCurse));
            }
        }
    }

    /// <summary>The bean's thorns come out when a flag saves the scout.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.TryCheckpoint))]
    private static class CheckpointPullsBeanThorns
    {
        private static void Postfix(Character __instance, bool __result)
        {
            if (__result && On && Rules.Owns(__instance))
            {
                ClearBeanThorns(__instance.refs.afflictions);
            }
        }
    }

    /// <summary>A snowball's bonk (run by whoever simulates the snowball) sends the marked knock-down.</summary>
    [HarmonyPatch(typeof(Bonkable), nameof(Bonkable.Bonk))]
    private static class SnowballBonk
    {
        private static void Prefix(Bonkable __instance, out float? __state)
        {
            __state = null;
            if (On && __instance.TryGetComponent(out Snowball _))
            {
                __state = __instance.ragdollTime;
                __instance.ragdollTime = SnowballKnockdownMarked;
            }
        }

        private static Exception Finalizer(Bonkable __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.ragdollTime = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>The knock-down reaches every client; the scout it hit gets the cold on their own.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.RPCA_Fall))]
    private static class SnowballHit
    {
        private static void Prefix(Character __instance, ref float seconds)
        {
            if (Mathf.Abs(seconds - SnowballKnockdownMarked) > 0.0001f)
            {
                return;
            }
            seconds = SnowballKnockdown;
            if (On && __instance.IsLocal && !__instance.data.dead)
            {
                ExactStatus.Add(__instance.refs.afflictions, Cold, A15_StrongerAilments.FromAscent15(Cold, SnowballCold), "snowball");
            }
        }
    }

    /// <summary>The cloud removes itself on every client; each one shortens its own.</summary>
    [HarmonyPatch(typeof(RemoveAfterSeconds), nameof(RemoveAfterSeconds.Start))]
    private static class ShortRemedyCloud
    {
        private static void Postfix(RemoveAfterSeconds __instance)
        {
            Transform parent = __instance.transform.parent;
            if (On && parent != null && parent.name.StartsWith(RemedyCloudParent, StringComparison.Ordinal))
            {
                __instance.seconds = Mathf.Min(__instance.seconds, RemedyCloudSeconds);
            }
        }
    }

    /// <summary>Every client grows the bubble and counts its lifetime itself.</summary>
    [HarmonyPatch(typeof(GrowOverTime), nameof(GrowOverTime.Awake))]
    private static class SmallerAntiZookaBubble
    {
        private static void Postfix(GrowOverTime __instance)
        {
            if (On && __instance.name.StartsWith(AntiZookaBubble, StringComparison.Ordinal) && __instance.TryGetComponent(out AntiSphere bubble))
            {
                __instance.endScale = AntiZookaBubbleDiameter;
                bubble.lifetime = AntiZookaBubbleSeconds;
            }
        }
    }

    /// <summary>A placed stove is a campfire named after the stove; its placer puts it out when the time is up.</summary>
    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Awake))]
    private static class ShortStove
    {
        private static void Postfix(Campfire __instance)
        {
            if (On && __instance.nameOverride == StoveName)
            {
                __instance.burnsFor = Mathf.Min(__instance.burnsFor, StoveSeconds);
            }
        }
    }

    /// <summary>
    /// A teammate revived with the Book of Bones gets 20 curse (the game: 15, the revive curse every
    /// revive gives). It is the only revive that brings a scout back as a skeleton: the reader turns the
    /// body into one just before the revive reaches the clients.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyPostReviveStatus))]
    private static class BookRevive
    {
        private static void Postfix(CharacterAfflictions afflictions)
        {
            Character revived = afflictions != null ? afflictions.character : null;
            float extra = BookReviveCurse - Ascents.revivalCurse;
            if (On && revived != null && revived.data.isSkeleton && extra > 0f)
            {
                // On every client, like the game's own revive curse.
                afflictions.AddStatus(Curse, extra + 0.0001f, fromRPC: true);
            }
        }
    }

    /// <summary>
    /// The skull's clear reaches every other scout as an RPC on the skull; its clear runs inside this
    /// call on each scout's own client.
    /// </summary>
    [HarmonyPatch(typeof(Action_ApplyMassAffliction), nameof(Action_ApplyMassAffliction.TryAddAfflictionToLocalCharacter))]
    private static class CursedSkullScope
    {
        private static void Prefix(Action_ApplyMassAffliction __instance, out bool __state)
        {
            __state = On && __instance.affliction is Affliction_ClearAllStatus && NameOf(__instance.item) == CursedSkullName;
            if (__state)
            {
                cursedSkullScope++;
            }
        }

        private static Exception Finalizer(bool __state, Exception __exception)
        {
            if (__state)
            {
                cursedSkullScope--;
            }
            return __exception;
        }
    }

    /// <summary>The skull's clear takes the crab status too (the game's clear never touches it).</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.ClearAllStatus))]
    private static class SkullCuresCrab
    {
        private static void Postfix(CharacterAfflictions __instance)
        {
            if (!InCursedSkull || !Rules.Owns(__instance.character))
            {
                return;
            }
            float crab = __instance.GetCurrentStatus(Crab);
            if (crab > 0f)
            {
                __instance.SetStatus(Crab, crab * CursedSkullResidue);
            }
        }
    }

    /// <summary>
    /// Cure-alls leave the crab status alone. Runs before A14_WorseItems' partial cure-all, which
    /// reads the same list.
    /// </summary>
    [HarmonyPatch(typeof(Action_ClearAllStatus), nameof(Action_ClearAllStatus.RunAction))]
    private static class CrabStays
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Action_ClearAllStatus __instance, out bool __state)
        {
            __state = On && !__instance.otherExclusions.Contains(Crab);
            if (__state)
            {
                __instance.otherExclusions.Add(Crab);
            }
        }

        private static Exception Finalizer(Action_ClearAllStatus __instance, bool __state, Exception __exception)
        {
            if (__state)
            {
                __instance.otherExclusions.Remove(Crab);
            }
            return __exception;
        }
    }

    /// <summary>
    /// Runs on the victim's client. The master raises the zombie, as when the spores get a scout; a
    /// checkpoint flag still saves the victim as in the game.
    /// </summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.DieToRitualDagger))]
    private static class ZombifyingDagger
    {
        private static bool Prefix(CharacterAfflictions __instance)
        {
            Character victim = __instance.character;
            if (!On || victim == null || !victim.IsLocal || victim.data.zombified || victim.WillDoCheckpoint(out _))
            {
                return true;
            }
            victim.photonView.RPC(nameof(Character.RPCA_Zombify), RpcTarget.MasterClient);
            return false;
        }
    }

    /// <summary>Crab joins the statuses Pandora's Lunchbox picks from, sharing the same budget.</summary>
    [HarmonyPatch(typeof(Affliction_Chaos), nameof(Affliction_Chaos.OnApplied))]
    private static class PandoraCrab
    {
        private static readonly OpCode[] Stores = { OpCodes.Stloc, OpCodes.Stloc_S, OpCodes.Stloc_0, OpCodes.Stloc_1, OpCodes.Stloc_2, OpCodes.Stloc_3 };

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int created = code.FindIndex(instruction => instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo constructor &&
                                                        constructor.DeclaringType == typeof(List<CharacterAfflictions.STATUSTYPE>));
            int stored = created < 0 ? -1 : code.FindIndex(created, instruction => Array.IndexOf(Stores, instruction.opcode) >= 0);
            if (stored < 0)
            {
                throw new InvalidOperationException("Pandora's list of statuses was not found; Pandora's Lunchbox gives no crab.");
            }
            code.InsertRange(stored, new[]
            {
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(A14_Items), nameof(AddCrab))),
            });
            return code;
        }
    }

    private static void AddCrab(List<CharacterAfflictions.STATUSTYPE> statuses)
    {
        if (On)
        {
            statuses.Add(Crab);
        }
    }
}
