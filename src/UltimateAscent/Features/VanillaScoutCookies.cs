using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The game's unused Vanilla Scout Cookies (the Scout Cookies with another look: the same name, the
/// same 10 hunger and 10 extra stamina for each of 4 bites) take half of the Scout Cookies' spawns:
/// from ascent 9 (the config's "New Items" switch), and in custom runs, where they have their own switch
/// (see <see cref="CustomRunItems.VariantShare"/>). Like the Scout Cookies they can be cooked (the game left that part off them).
/// </summary>
internal static class VanillaScoutCookies
{
    internal const string CookiesPrefab = "ScoutCookies";
    internal const string VanillaPrefab = "ScoutCookies_Vanilla";

    /// <summary>What its Custom Run switch is called (it shares the Scout Cookies' name).</summary>
    internal const string SwitchKey = "NAME_VANILLA SCOUT COOKIES";

    /// <summary>The Scout Cookies' spawns that turn out to be the vanilla ones.</summary>
    internal const float Share = 0.5f;

    private static bool On => RunSettings.IsCustomRun || (Rules.InExtraAscent && HostOptions.NewItemsOn);

    /// <summary>The vanilla cookies cook like the Scout Cookies: the same cooking settings on them.</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(CookiesPrefab, out Item cookies) || !ItemDatabase.TryGetItem(VanillaPrefab, out Item vanilla))
        {
            Plugin.Log.LogWarning("The Scout Cookies or the Vanilla Scout Cookies are missing from the item database; the vanilla ones do not spawn.");
            return;
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

    /// <summary>Every suitcase and spawner picks what it spawns here: half of the Scout Cookies become the vanilla ones.</summary>
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
