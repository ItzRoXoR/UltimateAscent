using System;
using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 14: poisonous food poisons by exact totals, as they land on the bar with this ascent's
/// boost included, over the game's own delay and duration: Button Shroom 150, Cluster Shroom 75,
/// Bugle Shroom 120, Green Crispberry 40, Yellow Kingberry 150, Pink Berrynana 100 (the game: 30,
/// 40, 20, 10, 25, 15). Green Kingberry poisons 35 at once (5), every Shroomberry gives 15 spores (5),
/// a Napberry 200 drowsiness (100) and a Scorchberry 80 heat (25).
/// <para>Each meal's poison runs on its own timer instead of the game's single "poison over time"
/// effect (where a second meal only adds time), so meals add up and every total lands. A cure that
/// clears poison effects (an antidote, the healing Shroomberry, a small poison or spore cure) or
/// death stops it; natural recovery, which clears the game's effects too, does not.</para>
/// </summary>
internal static class A15_FoodPoison
{
    private static bool On => Rules.Enabled(Lvl.StrongerAilments);

    private const CharacterAfflictions.STATUSTYPE Poison = CharacterAfflictions.STATUSTYPE.Poison;
    private const CharacterAfflictions.STATUSTYPE Spores = CharacterAfflictions.STATUSTYPE.Spores;

    /// <summary>Total poison of each poisonous food (by the game's English item name).</summary>
    private static readonly Dictionary<string, float> Totals = new Dictionary<string, float>
    {
        ["Button Shroom"] = 1.5f,
        ["Cluster Shroom"] = 0.75f,
        ["Bugle Shroom"] = 1.2f,
        ["Green Crispberry"] = 0.4f,
        ["Yellow Kingberry"] = 1.5f,
        ["Pink Berrynana"] = 1f,
    };

    private const string GreenKingberry = "Green Kingberry";
    private const float GreenKingberryPoison = 0.35f;
    private const string Shroomberry = "Shroomberry";
    private const float ShroomberrySpores = 0.15f;
    private const string Napberry = "Napberry";
    private const float NapberryDrowsiness = 2f;
    private const string Scorchberry = "Scorchberry";
    private const float ScorchberryHeat = 0.8f;

    private sealed class Meal
    {
        internal string source;
        internal float eatenAt;
        internal float delay;
        internal float duration;
        internal float total;
        internal float given;
    }

    private static readonly List<Meal> meals = new List<Meal>();
    private static int mealCount;

    /// <summary>The game's natural recovery is running (on this thread).</summary>
    [ThreadStatic] private static int recovering;

    /// <summary>The game's natural recovery is running (it clears poison effects too, which is no cure).</summary>
    internal static bool Recovering => recovering > 0;

    internal static void Reset()
    {
        meals.Clear();
    }

    private static string NameOf(Item item)
    {
        return item == null ? "" : item.UIData != null && !string.IsNullOrEmpty(item.UIData.itemName) ? item.UIData.itemName : item.name;
    }

    /// <summary>Called every frame.</summary>
    internal static void Tick()
    {
        if (meals.Count == 0)
        {
            return;
        }
        Character local = Character.localCharacter;
        if (!On || local == null || local.data.dead)
        {
            meals.Clear();
            return;
        }
        float now = Time.time;
        for (int i = meals.Count - 1; i >= 0; i--)
        {
            Meal meal = meals[i];
            float elapsed = now - meal.eatenAt - meal.delay;
            if (elapsed <= 0f)
            {
                continue;
            }
            float due = meal.total * Mathf.Clamp01(elapsed / meal.duration);
            if (due > meal.given)
            {
                ExactStatus.Add(local.refs.afflictions, Poison, due - meal.given, meal.source);
                meal.given = due;
            }
            if (elapsed >= meal.duration)
            {
                meals.RemoveAt(i);
            }
        }
    }

    [HarmonyPatch(typeof(Action_InflictPoison), nameof(Action_InflictPoison.RunAction))]
    private static class PoisonousFood
    {
        private static bool Prefix(Action_InflictPoison __instance)
        {
            Character character = __instance.character;
            if (!On || character == null || !character.IsLocal || !Totals.TryGetValue(NameOf(__instance.item), out float total))
            {
                return true;
            }
            mealCount++;
            meals.Add(new Meal
            {
                source = "meal " + mealCount,
                eatenAt = Time.time,
                delay = __instance.delay,
                duration = Mathf.Max(0.1f, __instance.inflictionTime),
                total = total,
            });
            return false;
        }
    }

    /// <summary>The whole poison a poisonous food gives from ascent 14, as it lands.</summary>
    internal static bool TryTotal(Item item, out float total)
    {
        total = 0f;
        return On && Totals.TryGetValue(NameOf(item), out total);
    }

    /// <summary>The exact amount for a food's own status change (Green Kingberry, Shroomberries); also tells ascent 15 to leave it alone.</summary>
    internal static bool TryExact(Action_ModifyStatus action, out float amount)
    {
        amount = 0f;
        if (!On || action == null || action.changeAmount <= 0f)
        {
            return false;
        }
        string name = NameOf(action.item);
        if (action.statusType == Poison && name == GreenKingberry)
        {
            amount = A15_StrongerAilments.Raw(Poison, GreenKingberryPoison);
            return true;
        }
        if (action.statusType == Spores && name.EndsWith(Shroomberry, StringComparison.Ordinal))
        {
            amount = A15_StrongerAilments.Raw(Spores, ShroomberrySpores);
            return true;
        }
        if (action.statusType == CharacterAfflictions.STATUSTYPE.Drowsy && name == Napberry)
        {
            amount = A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Drowsy, NapberryDrowsiness);
            return true;
        }
        if (action.statusType == CharacterAfflictions.STATUSTYPE.Hot && name == Scorchberry)
        {
            amount = A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Hot, ScorchberryHeat);
            return true;
        }
        return false;
    }

    [HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static class ExactFood
    {
        private static void Prefix(Action_ModifyStatus __instance, out float? __state)
        {
            __state = null;
            if (TryExact(__instance, out float amount))
            {
                __state = __instance.changeAmount;
                __instance.changeAmount = amount;
            }
        }

        private static Exception Finalizer(Action_ModifyStatus __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.changeAmount = __state.Value;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateNormalStatuses))]
    private static class NaturalRecovery
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            recovering++;
        }

        private static Exception Finalizer(Exception __exception)
        {
            recovering--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.ClearPoisonAfflictions))]
    private static class Cured
    {
        private static void Postfix(CharacterAfflictions __instance)
        {
            if (recovering == 0 && __instance.character != null && __instance.character.IsLocal)
            {
                meals.Clear();
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
                meals.Clear();
            }
        }
    }
}
