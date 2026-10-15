using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Toxic Drink: the Energy Drink's can in the player's own purple label. Drunk, it makes you
/// walk and climb 5 times as fast for 10 s (4 times for 8 s with ascent 14's rule); when the boost is
/// over it poisons you for 4 over 5 s (8 with ascent 14's rule, as it lands from ascent 15 on). Cooked,
/// it works like a cooked Energy Drink: it bursts when drunk (a knock-down and a push, never knocking you
/// out or making you drowsy, as ascent 17's explosions would), and the poison is gone. Its drinking sound
/// is the Energy Drink's, 1.5 times as fast. Everything else is the Energy Drink's: it cools you by 30
/// heat and wakes you up.
/// <para>It turns up only in Ancient Luggage and in the Ancient Statues (the pools the Ritual Dagger
/// comes from), as rarely as the Faerie Lantern (Legendary): from ascent 9 with the config's "New Items"
/// on; in custom runs it has its own switch.</para>
/// </summary>
internal static class ToxicDrink
{
    internal const string EnergyDrinkPrefab = "Energy Drink";
    internal const string PrefabName = "UA_ToxicDrink";
    internal const string ItemName = "Toxic Drink";
    internal const string NameKey = "NAME_TOXIC DRINK";
    internal const ushort ItemId = 9005;

    /// <summary>Ancient Luggage's pool and the Ancient Statues', at the Faerie Lantern's rarity.</summary>
    internal const SpawnPool Pools = SpawnPool.LuggageCursed | SpawnPool.RespawnCoffin;
    internal const Rarity SpawnRarity = Rarity.Legendary;

    /// <summary>How fast you walk and climb (times the normal speed), and for how long...</summary>
    internal const float Speed = 5f;
    internal const float Seconds = 10f;

    /// <summary>...and with ascent 14's rule.</summary>
    internal const float SpeedWorse = 4f;
    internal const float SecondsWorse = 8f;

    /// <summary>The poison once the boost is over, given over 5 s (with ascent 14's rule: as it lands from ascent 15 on).</summary>
    internal const float Poison = 0.04f;
    internal const float PoisonWorse = 0.08f;
    internal const float PoisonSeconds = 5f;

    /// <summary>Its drinking sound: the Energy Drink's, played this much faster (and higher).</summary>
    internal const float SoundSpeed = 1.5f;

    /// <summary>The burst when it is drunk cooked: the cooked Energy Drink's, under its own name (no explosion for ascent 17).</summary>
    internal const string BurstName = "VFX_ToxicDrinkBurst";

    private const string TextureResource = "UltimateAscent.ToxicDrink.png";
    private const string IconResource = "UltimateAscent.ToxicDrink_icon.png";
    private const string CanMaterial = "M_EnergyDrink";

    /// <summary>The item shader's first paint layer: on the Energy Drink's can a coarse checker "print" pattern.</summary>
    internal const string CheckerLayer = "_Texture1";

    /// <summary>What the poison lands as (one name for every drink: what is left of a 2.5-point step carries over to the next).</summary>
    private const string PoisonSource = "toxic drink";

    private sealed class Dose
    {
        internal float startsAt;
        internal float total;
        internal float given;
    }

    private static readonly List<Dose> doses = new List<Dose>();
    private static Item template;

    internal static Item Template => template;

    private static bool Worse => Rules.Enabled(Lvl.WorseItems);

    internal static float SpeedNow => Worse ? SpeedWorse : Speed;
    internal static float SecondsNow => Worse ? SecondsWorse : Seconds;

    /// <summary>The poison after the boost, as it lands on the bar.</summary>
    internal static float PoisonNow => Worse ? A15_StrongerAilments.FromAscent15(CharacterAfflictions.STATUSTYPE.Poison, PoisonWorse) : Poison;

    /// <summary>Poison doses still to come or running, on this game (for the tests).</summary>
    internal static int DosesPending => doses.Count;

    private static bool Allowed => RunSettings.IsCustomRun || (Rules.InExtraAscent && HostOptions.NewItemsOn);

    internal static bool IsToxic(Item item) => item != null && template != null && item.itemID == template.itemID;

    /// <summary>The cooked drink's burst (its knock-down and push are the cooked Energy Drink's; ascent 17's explosions leave it alone).</summary>
    internal static bool IsBurst(Component component) => component != null && component.gameObject.name.StartsWith(BurstName, StringComparison.Ordinal);

    /// <summary>The boost is this drink's: no other speed boost comes near it (the copy the game applies keeps only its numbers).</summary>
    internal static bool IsBoost(Affliction_FasterBoi boost) => boost != null && boost.moveSpeedMod >= 2.5f;

    internal static void Reset()
    {
        doses.Clear();
    }

    /// <summary>A copy of the Energy Drink with the new label, its own name and icon, its boosts and its spawns.</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(EnergyDrinkPrefab, out Item energy) || energy == null)
        {
            Plugin.Log.LogWarning("The Energy Drink is missing from the item database; the Toxic Drink is not added.");
            return;
        }
        Texture2D label = ModItems.LoadTexture(TextureResource, "ToxicDrink", TextureWrapMode.Repeat);
        if (label == null)
        {
            return;
        }
        Item drink = ModItems.Copy(energy, PrefabName);
        Action_ApplyAffliction[] boosts = drink.GetComponents<Action_ApplyAffliction>();
        ItemCooking cooking = drink.GetComponent<ItemCooking>();
        Renderer can = drink.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.name.StartsWith(CanMaterial, StringComparison.Ordinal));
        if (boosts.Length != 2 || boosts.Any(b => !(b.affliction is Affliction_FasterBoi)) || cooking == null || can == null)
        {
            Plugin.Log.LogWarning("The Energy Drink is not built as expected (two boosts, cooking, its can); the Toxic Drink is not added.");
            UnityEngine.Object.Destroy(drink.gameObject);
            return;
        }
        Material look = new Material(can.sharedMaterial) { name = "M_ToxicDrink" };
        look.SetTexture("_BaseTexture", label);
        // The item shader lays a coarse checker "print" pattern over the label (its first paint layer); on this
        // detailed label it clashes with the label's own halftone and looks grainy, so that layer is plain. The
        // shader's two other paint layers stay.
        look.SetTexture(CheckerLayer, Texture2D.whiteTexture);
        can.sharedMaterial = look;

        // Its own boosts (as drunk, and the cooked one cooking switches to); neither ends in drowsiness. They last
        // just their time: the Energy Drink's holds its clock for its first 3 s unless you climb (its climb delay).
        foreach (Action_ApplyAffliction boost in boosts)
        {
            boost.affliction = new Affliction_FasterBoi
            {
                moveSpeedMod = Speed - 1f,
                climbSpeedMod = Speed - 1f,
                totalTime = Seconds,
                drowsyOnEnd = 0f,
                climbDelay = 0f,
            };
            boost.extraAfflictions = new Affliction[0];
        }
        // Cooking switches the boosts and lets the burst out, on this can's own parts.
        cooking.additionalCookingBehaviors = cooking.additionalCookingBehaviors.Select(behavior => OwnParts(drink, behavior)).ToArray();
        // Its burst under its own name: ascent 17's explosions leave it alone (the cooked Energy Drink's pushes and knocks down as one).
        Action_Spawn burst = drink.GetComponent<Action_Spawn>();
        if (burst != null && burst.objectToSpawn != null)
        {
            burst.objectToSpawn = ModItems.Keep(burst.objectToSpawn, BurstName);
        }

        // The sound when it is drunk: the Energy Drink's, 1.5 times as fast.
        if (drink.TryGetComponent(out ItemUseFeedback feedback) && feedback.sfxUsed != null)
        {
            SFX_Instance sound = UnityEngine.Object.Instantiate(feedback.sfxUsed);
            sound.name = feedback.sfxUsed.name + " (Toxic Drink)";
            sound.hideFlags = HideFlags.HideAndDontSave;
            sound.settings.pitch *= SoundSpeed;
            feedback.sfxUsed = sound;
        }

        drink.UIData.itemName = ItemName;
        Texture2D icon = ModItems.HasResource(IconResource) ? ModItems.LoadTexture(IconResource, "ToxicDrink_icon", TextureWrapMode.Clamp) : null;
        if (icon != null)
        {
            drink.UIData.icon = icon;
        }
        if (drink.TryGetComponent(out LootData loot))
        {
            loot.spawnLocations = Pools;
            loot.Rarity = SpawnRarity;
        }
        if (ModItems.Register(drink, ItemId))
        {
            template = drink;
        }
    }

    /// <summary>A cooking behaviour of its own (never the Energy Drink's), switching this can's parts.</summary>
    private static AdditionalCookingBehavior OwnParts(Item drink, AdditionalCookingBehavior behavior)
    {
        switch (behavior)
        {
            case CookingBehavior_EnableScripts enable:
                return new CookingBehavior_EnableScripts { scriptsToEnable = Own(drink, enable.scriptsToEnable), cookedAmountToTrigger = enable.cookedAmountToTrigger, onlyOnce = enable.onlyOnce };
            case CookingBehavior_DisableScripts disable:
                return new CookingBehavior_DisableScripts { scriptsToDisable = Own(drink, disable.scriptsToDisable), cookedAmountToTrigger = disable.cookedAmountToTrigger, onlyOnce = disable.onlyOnce };
            default:
                return behavior;
        }
    }

    private static MonoBehaviour[] Own(Item drink, MonoBehaviour[] scripts)
    {
        return (scripts ?? new MonoBehaviour[0]).Select(script =>
        {
            if (script == null || script.gameObject == drink.gameObject)
            {
                return script;
            }
            int index = Array.IndexOf(script.GetComponents(script.GetType()), script);
            Component[] mine = drink.GetComponents(script.GetType());
            return index >= 0 && index < mine.Length ? (MonoBehaviour)mine[index] : null;
        }).ToArray();
    }

    /// <summary>Called every frame: the poison after each boost, exactly.</summary>
    internal static void Tick()
    {
        if (doses.Count == 0)
        {
            return;
        }
        Character local = Character.localCharacter;
        if (local == null || local.data.dead)
        {
            doses.Clear();
            return;
        }
        float now = Time.time;
        for (int i = doses.Count - 1; i >= 0; i--)
        {
            Dose dose = doses[i];
            float elapsed = now - dose.startsAt;
            if (elapsed <= 0f)
            {
                continue;
            }
            float due = dose.total * Mathf.Clamp01(elapsed / PoisonSeconds);
            if (due > dose.given)
            {
                ExactStatus.Add(local.refs.afflictions, CharacterAfflictions.STATUSTYPE.Poison, due - dose.given, PoisonSource);
                dose.given = due;
            }
            if (elapsed >= PoisonSeconds)
            {
                doses.RemoveAt(i);
            }
        }
    }

    /// <summary>Regular runs: only from ascent 9 and with the config's "New Items" on.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.IsValidToSpawn))]
    private static class OnlyFromAscent9
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result && !Allowed && IsToxic(__instance))
            {
                __result = false;
            }
        }
    }

    /// <summary>The boost for this ascent; drunk uncooked, the poison follows when the boost is over.</summary>
    [HarmonyPatch(typeof(Action_ApplyAffliction), nameof(Action_ApplyAffliction.RunAction))]
    private static class Boost
    {
        private static void Prefix(Action_ApplyAffliction __instance, out float[] __state)
        {
            __state = null;
            if (!IsToxic(__instance.item) || !(__instance.affliction is Affliction_FasterBoi boost))
            {
                return;
            }
            __state = new[] { boost.totalTime, boost.moveSpeedMod, boost.climbSpeedMod };
            boost.totalTime = SecondsNow;
            boost.moveSpeedMod = SpeedNow - 1f;
            boost.climbSpeedMod = SpeedNow - 1f;
            Character drinker = __instance.character;
            bool cooked = __instance.item.GetData<IntItemData>(DataEntryKey.CookedAmount).Value > 0;
            if (drinker != null && drinker.IsLocal && !cooked)
            {
                doses.Add(new Dose { startsAt = Time.time + SecondsNow, total = PoisonNow });
            }
        }

        private static Exception Finalizer(Action_ApplyAffliction __instance, float[] __state, Exception __exception)
        {
            if (__state != null && __instance.affliction is Affliction_FasterBoi boost)
            {
                boost.totalTime = __state[0];
                boost.moveSpeedMod = __state[1];
                boost.climbSpeedMod = __state[2];
            }
            return __exception;
        }
    }

    /// <summary>A cure that clears poison effects (an antidote and the like), or death, stops the poison to come; natural recovery does not.</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.ClearPoisonAfflictions))]
    private static class Cured
    {
        private static void Postfix(CharacterAfflictions __instance)
        {
            if (!A15_FoodPoison.Recovering && __instance.character != null && __instance.character.IsLocal)
            {
                doses.Clear();
            }
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.ClearAllAfflictions))]
    private static class AllCleared
    {
        private static void Postfix(CharacterAfflictions __instance)
        {
            if (__instance.character != null && __instance.character.IsLocal)
            {
                doses.Clear();
            }
        }
    }
}
