using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;
using Peak;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;

namespace UltimateAscent.Compat;

/// <summary>
/// Effect Preview (OnlyCook) draws what the held item, a campfire, a suitcase or cannibalism would do
/// onto the status bar, from the game's own numbers. It has no API, so everything goes through
/// reflection and is guarded: if it changes, this part switches itself off.
/// <list type="bullet">
/// <item>Items (and the cooked item's next stage): this ascent's numbers (<see cref="ItemFigures"/>),
/// plus what the rules add: a raw egg's or tick's poison, a raw Mandrake's spores, a scorpion's poison
/// (14). Cure-alls take at most 50 of each status (14). Cooking is not previewed from ascent 17, where
/// it ruins the food at once; from 14 it keeps poison and spores.</item>
/// <item>A Remedy Fungus cloud lasts 8 s, so it heals less (14); feeding the Jetpack gives 35% less
/// fuel (14).</item>
/// <item>Lighting a campfire takes 10 petrification off (16); cannibalism costs 25 curse and 50
/// poison (11, 28.6 below 15); suitcases hurt (21; the clown chest's trick is random, so not shown).</item>
/// <item>The Ritual Dagger aimed at a zombie (9+) gives nothing, so nothing is previewed.</item>
/// <item>Scout's Generosity aimed at the Warp Compass or the Toxic Drink costs what a mystical item costs
/// (<see cref="MysticalItems"/>).</item>
/// </list>
/// </summary>
internal static class EffectPreview
{
    internal const string Guid = "OnlyCook.EffectPreview";

    private const string Ns = "EffectPreview.";

    private static bool? present;
    private static MethodInfo addStatus;
    private static FieldInfo clearsCurable;
    private static FieldInfo petrifyReduction;
    private static FieldInfo removalCaps;

    /// <summary>The game's Remedy Fungus cloud lasts this long (ascent 14 cuts it to <see cref="A14_Items.RemedyCloudSeconds"/>).</summary>
    private const float GameRemedyCloudSeconds = 15f;

    /// <summary>Effect Preview's own list of the statuses a cure-all clears.</summary>
    private static readonly HashSet<CharacterAfflictions.STATUSTYPE> CurableStatuses = new HashSet<CharacterAfflictions.STATUSTYPE>
    {
        CharacterAfflictions.STATUSTYPE.Injury,
        CharacterAfflictions.STATUSTYPE.Hunger,
        CharacterAfflictions.STATUSTYPE.Cold,
        CharacterAfflictions.STATUSTYPE.Poison,
        CharacterAfflictions.STATUSTYPE.Drowsy,
        CharacterAfflictions.STATUSTYPE.Hot,
        CharacterAfflictions.STATUSTYPE.Spores,
        CharacterAfflictions.STATUSTYPE.Web,
        CharacterAfflictions.STATUSTYPE.FlyTrap,
    };

    /// <summary>A preview whose cure-all only partly cures (ascent 14), with what that cure-all leaves alone.</summary>
    private sealed class PartialCure
    {
        internal Action_ClearAllStatus action;
    }

    private static readonly ConditionalWeakTable<object, PartialCure> partialCures = new ConditionalWeakTable<object, PartialCure>();

    /// <summary>The cooking preview runs the item preview for the next cooking stage.</summary>
    [ThreadStatic] private static int cookingDepth;

    internal static bool Present => present ??= Chainloader.PluginInfos.ContainsKey(Guid) && Bind();

    private static bool Bind()
    {
        try
        {
            Type preview = AccessTools.TypeByName(Ns + "Preview.ItemPreview") ?? throw new MissingMemberException("ItemPreview");
            addStatus = AccessTools.Method(preview, "AddStatus", new[] { typeof(CharacterAfflictions.STATUSTYPE), typeof(float) }) ?? throw new MissingMemberException("ItemPreview.AddStatus");
            clearsCurable = AccessTools.Field(preview, "ClearsCurableStatusOnUse") ?? throw new MissingMemberException("ItemPreview.ClearsCurableStatusOnUse");
            petrifyReduction = AccessTools.Field(preview, "PetrifyReductionOnUse") ?? throw new MissingMemberException("ItemPreview.PetrifyReductionOnUse");
            removalCaps = AccessTools.Field(preview, "StatusRemovalCaps") ?? throw new MissingMemberException("ItemPreview.StatusRemovalCaps");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Effect Preview changed; its previews show the game's numbers. " + ex.Message);
            return false;
        }
    }

    private static MethodBase Method(string type, string name, params Type[] parameters)
    {
        Type found = AccessTools.TypeByName(Ns + type);
        return found == null ? null : parameters.Length == 0 ? AccessTools.Method(found, name) : AccessTools.Method(found, name, parameters);
    }

    private static void Add(object preview, CharacterAfflictions.STATUSTYPE type, float amount)
    {
        if (preview != null && amount != 0f)
        {
            addStatus.Invoke(preview, new object[] { type, amount });
        }
    }

    [HarmonyPatch]
    private static class ItemNumbers
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.ItemPreviewCalculator", "Compute",
            typeof(Item), typeof(Character), typeof(Func<ItemAction, bool>), typeof(bool), typeof(bool), typeof(bool), typeof(bool));

        private static void Prefix(Item item)
        {
            ItemFigures.Begin(item);
        }

        private static void Postfix(Item item, object __result)
        {
            if (item == null || __result == null || !Rules.Enabled(Lvl.WorseItems))
            {
                return;
            }
            RuleExtras(item, __result);
            if ((bool)clearsCurable.GetValue(__result))
            {
                foreach (Action_ClearAllStatus cureAll in item.GetComponentsInChildren<Action_ClearAllStatus>())
                {
                    if (cureAll.enabled)
                    {
                        clearsCurable.SetValue(__result, false);
                        partialCures.Remove(__result);
                        partialCures.Add(__result, new PartialCure { action = cureAll });
                        break;
                    }
                }
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            ItemFigures.End();
            return __exception;
        }
    }

    /// <summary>What eating it adds from ascent 14 that is not in the item's own actions.</summary>
    private static void RuleExtras(Item item, object preview)
    {
        bool food = false;
        foreach (Action_RestoreHunger hunger in item.GetComponentsInChildren<Action_RestoreHunger>())
        {
            food |= hunger.enabled;
        }
        if (!food)
        {
            return;
        }
        const CharacterAfflictions.STATUSTYPE poison = CharacterAfflictions.STATUSTYPE.Poison;
        const CharacterAfflictions.STATUSTYPE spores = CharacterAfflictions.STATUSTYPE.Spores;
        bool raw = BurnedItems.CookedLevel(item) + (cookingDepth > 0 ? 1 : 0) == 0;
        string name = A14_Items.NameOf(item);
        if (raw && (name == A14_Items.EggName || A14_WorseItems.Ticks.IsTick(item)))
        {
            Add(preview, poison, A15_StrongerAilments.FromAscent15(poison, A14_Items.RawFoodPoison));
        }
        else if (raw && name == A14_Items.MandrakeName)
        {
            Add(preview, spores, A15_StrongerAilments.FromAscent15(spores, A14_Items.MandrakeSpores));
        }
        if (item.GetComponent<Scorpion>() != null)
        {
            Add(preview, poison, A15_StrongerAilments.FromAscent15(poison, A14_WorseItems.ScorpionPoison));
        }
    }

    /// <summary>The partial cure-all, drawn against the live statuses each frame like Effect Preview's own full one.</summary>
    [HarmonyPatch]
    private static class PartialCureAll
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Ui.GhostBarOverlay", "GetStatusPreview");

        private static void Postfix(object preview, CharacterAfflictions.STATUSTYPE type, ref float live, ref float decrease)
        {
            if (preview == null || !CurableStatuses.Contains(type) || !partialCures.TryGetValue(preview, out PartialCure cure))
            {
                return;
            }
            Action_ClearAllStatus action = cure.action;
            if (action == null || action.defaultExclusions.Contains(type) || (action.excludeCurse && type == CharacterAfflictions.STATUSTYPE.Curse) || action.otherExclusions.Contains(type))
            {
                return;
            }
            decrease += Mathf.Min(live, A14_WorseItems.CureAllAmount(type));
        }
    }

    [HarmonyPatch]
    private static class CookingNumbers
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.CookingPreviewCalculator", "Compute", typeof(Item), typeof(Character));

        private static void Prefix(Item item)
        {
            cookingDepth++;
            ItemFigures.Begin(item);
        }

        private static Exception Finalizer(Exception __exception)
        {
            ItemFigures.End();
            cookingDepth--;
            return __exception;
        }
    }

    /// <summary>From ascent 17 one cooking cycle ruins the food (the charring rule), so there is no next stage to show.</summary>
    [HarmonyPatch]
    private static class NoCookingStages
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.CookingPreviewCalculator", "CanPreviewNextStage", typeof(Item));

        private static void Postfix(ref bool __result)
        {
            if (__result && Rules.Enabled(Lvl.HotCampfires))
            {
                __result = false;
            }
        }
    }

    /// <summary>From ascent 14 cooking leaves the food's poison and spores on.</summary>
    [HarmonyPatch]
    private static class CookingKeepsPoison
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.CookingPreviewCalculator", "BuildNextStageActiveOverride", typeof(ItemCooking), typeof(int));

        private static void Postfix(ref Func<ItemAction, bool> __result)
        {
            Func<ItemAction, bool> next = __result;
            if (next == null || !Rules.Enabled(Lvl.WorseItems))
            {
                return;
            }
            __result = action => next(action) || (A14_WorseItems.Poisonous(action) && action.enabled && action.gameObject.activeInHierarchy);
        }
    }

    [HarmonyPatch]
    private static class RemedyCloud
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.TimedUsagePreviewCalculator", "Compute");

        private static void Postfix(Item item, object preview)
        {
            if (item == null || preview == null || !Rules.Enabled(Lvl.WorseItems))
            {
                return;
            }
            ShelfShroom shroom = item.GetComponentInChildren<ShelfShroom>(true);
            if (shroom == null || shroom.instantiateOnBreak == null || !shroom.instantiateOnBreak.name.StartsWith(A14_Items.RemedyCloudParent, StringComparison.Ordinal))
            {
                return;
            }
            if (removalCaps.GetValue(preview) is Dictionary<CharacterAfflictions.STATUSTYPE, float> caps && caps.Count > 0)
            {
                float share = Mathf.Min(1f, A14_Items.RemedyCloudSeconds / GameRemedyCloudSeconds);
                foreach (CharacterAfflictions.STATUSTYPE type in new List<CharacterAfflictions.STATUSTYPE>(caps.Keys))
                {
                    caps[type] *= share;
                }
            }
        }
    }

    [HarmonyPatch]
    private static class JetpackFuel
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.JetpackFuelPreviewCalculator", "ComputeAddedFuel", typeof(Item));

        private static void Postfix(ref int __result)
        {
            if (Rules.Enabled(Lvl.WorseItems))
            {
                __result = Mathf.RoundToInt(__result * A14_Items.JetpackFuelShare);
            }
        }
    }

    [HarmonyPatch]
    private static class CampfireNumbers
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.CampfirePreviewCalculator", "Compute", typeof(global::Campfire));

        private static void Postfix(object __result)
        {
            if (__result != null && Rules.Enabled(Lvl.SlowRecovery))
            {
                float reduction = (float)petrifyReduction.GetValue(__result);
                petrifyReduction.SetValue(__result, Mathf.Min(reduction, A16_CampfirePetrify.PetrifyHealed));
            }
        }
    }

    /// <summary>The Ritual Dagger aimed at a zombie gives nothing (from ascent 9, see <see cref="ZombieDagger"/>): no preview.</summary>
    [HarmonyPatch]
    private static class DaggerOnZombie
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.RitualDaggerPreviewCalculator", "IsUsable", typeof(Item));

        private static void Postfix(ref bool __result)
        {
            if (__result && ZombieDagger.AimedAtZombie)
            {
                __result = false;
            }
        }
    }

    /// <summary>Scout's Generosity aimed at the Warp Compass or the Toxic Drink: a mystical item's cost (<see cref="MysticalItems"/>).</summary>
    [HarmonyPatch]
    private static class MysticalClone
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.DynamicPetrifyPreview", "ComputeClone", typeof(Action_CloneSelectedItem));

        private static void Postfix(Action_CloneSelectedItem action, ref float __result)
        {
            if (__result <= 0f || action == null || Interaction.instance == null)
            {
                return;
            }
            Interaction.instance.DoInteractableRaycasts(out IInteractible aimed, action.range);
            if (aimed is Item target && MysticalItems.CountsAsMystical(target))
            {
                __result = action.petrifyMystical / 100f;
            }
        }
    }

    [HarmonyPatch]
    private static class CannibalismNumbers
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.CannibalismPreviewCalculator", "Compute");

        private static void Postfix(object __result)
        {
            if (Rules.Enabled(Lvl.RuleZero))
            {
                const CharacterAfflictions.STATUSTYPE poison = CharacterAfflictions.STATUSTYPE.Poison;
                Add(__result, CharacterAfflictions.STATUSTYPE.Curse, A11_RuleZero.CannibalCurse - A11_RuleZero.GameCannibalCurse);
                Add(__result, poison, A15_StrongerAilments.FromAscent15(poison, A11_RuleZero.CannibalPoison));
            }
        }
    }

    [HarmonyPatch]
    private static class LuggageNumbers
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => Method("Preview.LuggagePreviewCalculator", "Compute", typeof(global::Luggage));

        private static void Postfix(global::Luggage luggage, object __result)
        {
            if (Rules.Enabled(Lvl.PainfulLuggage) && luggage != null && luggage.state == global::Luggage.LuggageState.Closed &&
                A21_PainfulLuggage.Pain(luggage, out float injury, out float curse))
            {
                Add(__result, CharacterAfflictions.STATUSTYPE.Injury, injury);
                Add(__result, CharacterAfflictions.STATUSTYPE.Curse, curse);
            }
        }
    }
}
