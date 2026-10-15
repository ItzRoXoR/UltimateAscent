using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Pink Lollipop: the game's Big Lollipop in pink and white stripes (the player's own texture), with
/// the Big Lollipop's effects and cooking (and the ascents' rules for it) under a name of its own. It
/// takes half of the Big Lollipop's spawns: from ascent 9 (the config's "New Items" switch), and in custom
/// runs, where it has its own switch (see <see cref="CustomRunItems.VariantShare"/>).
/// </summary>
internal static class PinkLollipop
{
    internal const string LollipopPrefab = "Lollipop";
    internal const string PrefabName = "UA_PinkLollipop";

    internal const string ItemName = "Pink Lollipop";
    internal const string NameKey = "NAME_PINK LOLLIPOP";
    internal const ushort ItemId = 9004;

    /// <summary>The Big Lollipop's spawns that turn out to be the pink one.</summary>
    internal const float Share = 0.5f;

    private const string TextureResource = "UltimateAscent.PinkLollipop.png";
    private const string IconResource = "UltimateAscent.PinkLollipop_icon.png";
    private const string LollipopMaterial = "M_Lollipop";

    private static Item template;

    internal static Item Template => template;

    private static bool On => RunSettings.IsCustomRun || (Rules.InExtraAscent && HostOptions.NewItemsOn);

    internal static bool IsPink(Item item) => item != null && template != null && item.itemID == template.itemID;

    /// <summary>A copy of the Big Lollipop with the pink texture on its candy and its own icon.</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(LollipopPrefab, out Item lollipop) || lollipop == null)
        {
            Plugin.Log.LogWarning("The Big Lollipop is missing from the item database; the pink one is not added.");
            return;
        }
        Texture2D stripes = ModItems.LoadTexture(TextureResource, "PinkLollipop", TextureWrapMode.Repeat);
        if (stripes == null)
        {
            return;
        }
        Item pink = ModItems.Copy(lollipop, PrefabName);
        pink.UIData.itemName = ItemName;
        int painted = 0;
        foreach (Renderer renderer in pink.GetComponentsInChildren<Renderer>(true))
        {
            Material candy = renderer.sharedMaterial;
            if (candy != null && candy.name.StartsWith(LollipopMaterial, System.StringComparison.Ordinal))
            {
                Material copy = new Material(candy) { name = "M_PinkLollipop" };
                copy.SetTexture("_BaseTexture", stripes);
                renderer.sharedMaterial = copy;
                painted++;
            }
        }
        if (painted == 0)
        {
            Plugin.Log.LogWarning("The Big Lollipop's candy material was not found; the pink one is not added.");
            Object.Destroy(pink.gameObject);
            return;
        }
        Texture2D icon = ModItems.HasResource(IconResource) ? ModItems.LoadTexture(IconResource, "PinkLollipop_icon", TextureWrapMode.Clamp) : null;
        if (icon != null)
        {
            pink.UIData.icon = icon;
        }
        if (pink.TryGetComponent(out LootData loot))
        {
            // It turns up only in place of a Big Lollipop (see HalfPink).
            loot.spawnLocations = SpawnPool.None;
        }
        if (ModItems.Register(pink, ItemId))
        {
            template = pink;
        }
    }

    /// <summary>Every suitcase and spawner picks what it spawns here: half of the Big Lollipops become pink ones.</summary>
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.GetObjectsToSpawn))]
    private static class HalfPink
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
