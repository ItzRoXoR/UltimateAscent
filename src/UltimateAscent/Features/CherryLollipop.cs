using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Cherry Lollipop: the game's Big Lollipop in pink and white stripes (the player's own texture), under
/// a name of its own. Eaten, it gives the Big Lollipop's infinite stamina and the Energy Drink's speed boost
/// at once, for 6 s (4 s with ascent 14's rule), from the first lick (neither holds its clock while you don't
/// climb, as the game's do for 3 s). Then the Big Lollipop's drowsiness follows (its ascents' rules too:
/// from 14 up to 85 over 12 s); the Energy Drink's crash does not. Cooked, like the Big Lollipop, no
/// drowsiness follows. It takes half of the Big Lollipop's spawns: from ascent 9 (the config's "New Items"
/// switch), and in custom runs, where it has its own switch (see <see cref="CustomRunItems.VariantShare"/>).
/// </summary>
internal static class CherryLollipop
{
    internal const string LollipopPrefab = "Lollipop";
    /// <summary>Its prefab keeps the name it was first made under: the Custom Run screen saves its switch under it.</summary>
    internal const string PrefabName = "UA_PinkLollipop";

    internal const string ItemName = "Cherry Lollipop";
    internal const string NameKey = "NAME_CHERRY LOLLIPOP";
    internal const ushort ItemId = 9004;

    /// <summary>The Big Lollipop's spawns that turn out to be the Cherry Lollipop.</summary>
    internal const float Share = 0.5f;

    /// <summary>How long its infinite stamina and speed boost last, and with ascent 14's rule.</summary>
    internal const float Seconds = 6f;
    internal const float SecondsWorse = 4f;

    private const string EnergyDrinkPrefab = "Energy Drink";

    private const string TextureResource = "UltimateAscent.PinkLollipop.png";
    private const string IconResource = "UltimateAscent.PinkLollipop_icon.png";
    private const string LollipopMaterial = "M_Lollipop";

    private static Item template;

    internal static Item Template => template;

    private static bool On => RunSettings.IsCustomRun || (Rules.InExtraAscent && HostOptions.NewItemsOn);

    internal static bool IsCherry(Item item) => item != null && template != null && item.itemID == template.itemID;

    internal static float SecondsNow => Rules.Enabled(Lvl.WorseItems) ? SecondsWorse : Seconds;

    /// <summary>A copy of the Big Lollipop with the pink and white texture on its candy and its own icon.</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(LollipopPrefab, out Item lollipop) || lollipop == null)
        {
            Plugin.Log.LogWarning("The Big Lollipop is missing from the item database; the Cherry Lollipop is not added.");
            return;
        }
        Texture2D stripes = ModItems.LoadTexture(TextureResource, "PinkLollipop", TextureWrapMode.Repeat);
        if (stripes == null)
        {
            return;
        }
        Item cherry = ModItems.Copy(lollipop, PrefabName);
        cherry.UIData.itemName = ItemName;
        int painted = 0;
        foreach (Renderer renderer in cherry.GetComponentsInChildren<Renderer>(true))
        {
            Material candy = renderer.sharedMaterial;
            if (candy != null && candy.name.StartsWith(LollipopMaterial, System.StringComparison.Ordinal))
            {
                Material copy = new Material(candy) { name = "M_CherryLollipop" };
                copy.SetTexture("_BaseTexture", stripes);
                renderer.sharedMaterial = copy;
                painted++;
            }
        }
        if (painted == 0)
        {
            Plugin.Log.LogWarning("The Big Lollipop's candy material was not found; the Cherry Lollipop is not added.");
            Object.Destroy(cherry.gameObject);
            return;
        }
        Texture2D icon = ModItems.HasResource(IconResource) ? ModItems.LoadTexture(IconResource, "PinkLollipop_icon", TextureWrapMode.Clamp) : null;
        if (icon != null)
        {
            cherry.UIData.icon = icon;
        }
        // Its rush (raw: with the Big Lollipop's drowsiness after it; cooked: without) comes with the Energy Drink's
        // speed boost, both for 6 s and without the 3 s hold. New effects: the Big Lollipop keeps its own.
        Affliction_FasterBoi energy = ItemDatabase.TryGetItem(EnergyDrinkPrefab, out Item drink) && drink != null
            ? drink.GetComponents<Action_ApplyAffliction>().Select(a => a.affliction).OfType<Affliction_FasterBoi>().FirstOrDefault()
            : null;
        foreach (Action_ApplyAffliction apply in cherry.GetComponents<Action_ApplyAffliction>())
        {
            if (!(apply.affliction is Affliction_InfiniteStamina rush))
            {
                continue;
            }
            Affliction_AdjustDrowsyOverTime crash = rush.drowsyAffliction as Affliction_AdjustDrowsyOverTime;
            apply.affliction = new Affliction_InfiniteStamina
            {
                totalTime = Seconds,
                climbDelay = 0f,
                drowsyAffliction = crash != null ? new Affliction_AdjustDrowsyOverTime { statusPerSecond = crash.statusPerSecond, totalTime = crash.totalTime } : null,
            };
            apply.extraAfflictions = new Affliction[]
            {
                new Affliction_FasterBoi
                {
                    moveSpeedMod = energy != null ? energy.moveSpeedMod : 0.5f,
                    climbSpeedMod = energy != null ? energy.climbSpeedMod : 1.5f,
                    totalTime = Seconds,
                    drowsyOnEnd = 0f,
                    climbDelay = 0f,
                },
            };
        }
        if (cherry.TryGetComponent(out LootData loot))
        {
            // It turns up only in place of a Big Lollipop (see HalfCherry).
            loot.spawnLocations = SpawnPool.None;
        }
        if (ModItems.Register(cherry, ItemId))
        {
            template = cherry;
        }
    }

    /// <summary>With ascent 14's rule its rush and boost last 4 s.</summary>
    [HarmonyPatch(typeof(Action_ApplyAffliction), nameof(Action_ApplyAffliction.RunAction))]
    private static class ShorterWithAscent14
    {
        private static void Prefix(Action_ApplyAffliction __instance, out float __state)
        {
            __state = -1f;
            if (!IsCherry(__instance.item) || !(__instance.affliction is Affliction_InfiniteStamina rush))
            {
                return;
            }
            __state = rush.totalTime;
            SetTime(__instance, SecondsNow);
        }

        private static System.Exception Finalizer(Action_ApplyAffliction __instance, float __state, System.Exception __exception)
        {
            if (__state >= 0f)
            {
                SetTime(__instance, __state);
            }
            return __exception;
        }

        private static void SetTime(Action_ApplyAffliction apply, float seconds)
        {
            apply.affliction.totalTime = seconds;
            foreach (Affliction extra in apply.extraAfflictions ?? new Affliction[0])
            {
                if (extra is Affliction_FasterBoi boost)
                {
                    boost.totalTime = seconds;
                }
            }
        }
    }

    /// <summary>Every suitcase and spawner picks what it spawns here: half of the Big Lollipops become Cherry Lollipops.</summary>
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.GetObjectsToSpawn))]
    private static class HalfCherry
    {
        private static void Postfix(List<GameObject> __result)
        {
            if (__result == null || template == null || !On)
            {
                return;
            }
            float share = CustomRunItems.VariantShare(LollipopPrefab, PrefabName, Share);
            for (int i = 0; i < __result.Count; i++)
            {
                if (__result[i] != null && __result[i].name == LollipopPrefab && Random.value < share)
                {
                    __result[i] = template.gameObject;
                }
            }
        }
    }
}
