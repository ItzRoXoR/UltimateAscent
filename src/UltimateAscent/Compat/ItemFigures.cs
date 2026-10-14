using System;
using System.Collections.Generic;
using Peak.Afflictions;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;

namespace UltimateAscent.Compat;

/// <summary>
/// What an item does on the ascent being climbed, written into the item's own numbers for as long as
/// a mod that shows item effects reads them (Item Stats, Effect Preview), then put back exactly. The
/// rules change most of these numbers only while the item is being used, so such mods would show the
/// game's. Numbers are shown as they land on the bar: the rules' exact amounts, ascent 15's boost
/// included.
/// <list type="bullet">
/// <item>Food: half the hunger (14), none from ticks.</item>
/// <item>Status changes: cures at 60% (the Early Worm 50%; 14), what they add with ascent 15's boost,
/// the exact Napberry, Scorchberry, Green Kingberry and Shroomberry amounts (15).</item>
/// <item>Extra stamina: half (14). Poisonous food: its exact total (15) or the boosted poison.</item>
/// <item>Effects: the Energy Drink's 50 crash and the boost on other crashes (15), the Big Lollipop's
/// crash (14), every drowsiness over time (14), the Heat Pack's 20 s (14), the gems' numbers (14).</item>
/// <item>Weight (for Item Stats, which reads the raw field): the rules' weight.</item>
/// </list>
/// </summary>
internal static class ItemFigures
{
    private static readonly List<Action> undo = new List<Action>();
    private static int depth;

    /// <summary>Starts showing <paramref name="item"/>'s numbers for this ascent; every Begin needs an <see cref="End"/>.</summary>
    internal static void Begin(Item item, bool weight = false)
    {
        depth++;
        if (depth > 1 || item == null || !Rules.Enabled(Lvl.First))
        {
            return;
        }
        try
        {
            Apply(item, weight);
        }
        catch (Exception ex)
        {
            Restore();
            Plugin.Log.LogWarning($"Could not show {item.gameObject.name}'s numbers for this ascent: {ex.Message}");
        }
    }

    internal static void End()
    {
        if (depth == 0)
        {
            return;
        }
        depth--;
        if (depth == 0)
        {
            Restore();
        }
    }

    private static void Restore()
    {
        for (int i = undo.Count - 1; i >= 0; i--)
        {
            try
            {
                undo[i]();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not put an item's own number back: " + ex.Message);
            }
        }
        undo.Clear();
    }

    private static void Set(float current, float value, Action<float> set)
    {
        if (current == value)
        {
            return;
        }
        undo.Add(() => set(current));
        set(value);
    }

    private static void Apply(Item item, bool weight)
    {
        bool worse = Rules.Enabled(Lvl.WorseItems);
        bool stronger = Rules.Enabled(Lvl.StrongerAilments);
        string name = A14_Items.NameOf(item);
        if (weight)
        {
            int carry = item.carryWeight;
            int shown = item.CarryWeight - Ascents.itemWeightModifier;
            if (carry != shown)
            {
                undo.Add(() => item.carryWeight = carry);
                item.carryWeight = shown;
            }
        }
        A14_Gems.Prepare(item);
        foreach (ItemAction action in item.GetComponentsInChildren<ItemAction>(true))
        {
            switch (action)
            {
                case Action_RestoreHunger food when worse:
                    Set(food.restorationAmount, A14_WorseItems.Ticks.IsTick(item) ? 0f : food.restorationAmount * A14_WorseItems.FoodShare,
                        v => food.restorationAmount = v);
                    break;
                case Action_ModifyStatus status:
                    Set(status.changeAmount, Landed(status, item), v => status.changeAmount = v);
                    break;
                case Action_GiveExtraStamina stamina when worse:
                    Set(stamina.amount, stamina.amount * A14_WorseItems.ExtraStaminaShare, v => stamina.amount = v);
                    break;
                case Action_InflictPoison poison when stronger && poison.inflictionTime > 0f:
                    float total = A15_FoodPoison.TryTotal(item, out float exact)
                        ? exact
                        : poison.poisonPerSecond * poison.inflictionTime * A15_StrongerAilments.Factor(CharacterAfflictions.STATUSTYPE.Poison);
                    Set(poison.poisonPerSecond, total / poison.inflictionTime, v => poison.poisonPerSecond = v);
                    break;
                case Action_ApplyAffliction apply:
                    Affliction(apply.affliction, name, worse, stronger);
                    if (apply.extraAfflictions != null)
                    {
                        foreach (Affliction extra in apply.extraAfflictions)
                        {
                            Affliction(extra, name, worse, stronger);
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>What an item's status change puts on (or takes off) the bar.</summary>
    private static float Landed(Action_ModifyStatus status, Item item)
    {
        CharacterAfflictions.STATUSTYPE type = status.statusType;
        float amount = status.changeAmount;
        if (amount > 0f)
        {
            if (A15_FoodPoison.TryExact(status, out float raw))
            {
                amount = raw;
            }
            return amount * A15_StrongerAilments.Factor(type);
        }
        if (amount < 0f && type == CharacterAfflictions.STATUSTYPE.Hunger && Rules.Enabled(Lvl.WorseItems) && A14_WorseItems.Ticks.IsTick(item))
        {
            return 0f;
        }
        return amount * A14_WorseItems.CureScale(status);
    }

    private static void Affliction(Affliction affliction, string name, bool worse, bool stronger)
    {
        const CharacterAfflictions.STATUSTYPE drowsy = CharacterAfflictions.STATUSTYPE.Drowsy;
        switch (affliction)
        {
            case Affliction_FasterBoi rush when stronger:
                float crash = name == A14_Items.EnergyDrinkName ? A14_Items.EnergyDrinkCrash : rush.drowsyOnEnd * A15_StrongerAilments.Factor(drowsy);
                Set(rush.drowsyOnEnd, crash, v => rush.drowsyOnEnd = v);
                break;
            case Affliction_InfiniteStamina sugar when worse && sugar.drowsyAffliction is Affliction_AdjustDrowsyOverTime sugarCrash && sugarCrash.statusPerSecond > 0f:
                if (name == A14_Items.BigLollipopName)
                {
                    Set(sugarCrash.totalTime, A14_Items.LollipopDrowsySeconds, v => sugarCrash.totalTime = v);
                    Set(sugarCrash.statusPerSecond, A15_StrongerAilments.FromAscent15(drowsy, A14_Items.LollipopDrowsiness) / A14_Items.LollipopDrowsySeconds,
                        v => sugarCrash.statusPerSecond = v);
                }
                else
                {
                    Affliction(sugarCrash, name, worse, stronger);
                }
                break;
            case Affliction_AdjustDrowsyOverTime sleepy when worse && sleepy.statusPerSecond > 0f:
                Set(sleepy.statusPerSecond, A15_StrongerAilments.FromAscent15(drowsy, sleepy.statusPerSecond), v => sleepy.statusPerSecond = v);
                break;
            case Affliction_AdjustColdOverTime warmth when worse && name == A14_Items.HeatPackName:
                Set(warmth.totalTime, A14_Items.HeatPackSeconds, v => warmth.totalTime = v);
                break;
        }
    }
}
