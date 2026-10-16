using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Vanilla Wafers: the game's unused vanilla Scout Cookies (the Scout Cookies with another look), under a
/// name of their own (the game gave them the Scout Cookies' name). Each of 4 bites restores 5 hunger and gives
/// 20 extra stamina (the Scout Cookies: 10 and 10); like the Scout Cookies they can be cooked (the game left
/// that part off them), which makes a bite 10 hunger and 30 extra stamina (the game's cooking: twice the
/// food, 1.5 times the stamina); ascent 14 halves both. They take half of the Scout Cookies' spawns: from
/// ascent 9 (the config's "New Items" switch), and in custom runs, where they have their own switch (see
/// <see cref="CustomRunItems.VariantShare"/>).
/// </summary>
internal static class VanillaWafers
{
    internal const string CookiesPrefab = "ScoutCookies";
    internal const string VanillaPrefab = "ScoutCookies_Vanilla";

    internal const string ItemName = "Vanilla Wafers";
    internal const string NameKey = "NAME_VANILLA WAFERS";

    /// <summary>A bite: hunger restored and extra stamina.</summary>
    internal const float Hunger = 0.05f;
    internal const float ExtraStamina = 0.2f;

    /// <summary>The Scout Cookies' spawns that turn out to be the Vanilla Wafers.</summary>
    internal const float Share = 0.5f;

    private static bool On => RunSettings.IsCustomRun || (Rules.InExtraAscent && HostOptions.NewItemsOn);

    /// <summary>The wafers get their name, and cook like the Scout Cookies: the same cooking settings on them.</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(CookiesPrefab, out Item cookies) || !ItemDatabase.TryGetItem(VanillaPrefab, out Item vanilla))
        {
            Plugin.Log.LogWarning("The Scout Cookies or their unused vanilla look are missing from the item database; the Vanilla Wafers do not spawn.");
            return;
        }
        vanilla.UIData.itemName = ItemName;
        foreach (Action_RestoreHunger food in vanilla.GetComponents<Action_RestoreHunger>())
        {
            food.restorationAmount = Hunger;
        }
        foreach (Action_GiveExtraStamina stamina in vanilla.GetComponents<Action_GiveExtraStamina>())
        {
            stamina.amount = ExtraStamina;
        }
        if (vanilla.GetComponent<ItemCooking>() != null || !cookies.TryGetComponent(out ItemCooking cooking))
        {
            return;
        }
        // Only the cooking settings themselves; what the base classes hold refers to the item they sit on.
        ItemCooking copy = vanilla.gameObject.AddComponent<ItemCooking>();
        foreach (FieldInfo field in typeof(ItemCooking).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            field.SetValue(copy, field.GetValue(cooking));
        }
    }

    /// <summary>Every suitcase and spawner picks what it spawns here: half of the Scout Cookies become Vanilla Wafers.</summary>
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.GetObjectsToSpawn))]
    private static class HalfVanilla
    {
        private static void Postfix(List<GameObject> __result)
        {
            if (__result == null || !On || !ItemDatabase.TryGetItem(VanillaPrefab, out Item vanilla) || vanilla == null)
            {
                return;
            }
            float share = CustomRunItems.VariantShare(CookiesPrefab, VanillaPrefab, Share);
            for (int i = 0; i < __result.Count; i++)
            {
                if (__result[i] != null && __result[i].name == CookiesPrefab && Random.value < share)
                {
                    __result[i] = vanilla.gameObject;
                }
            }
        }
    }
}
