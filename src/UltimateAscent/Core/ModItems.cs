using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Core;

/// <summary>
/// Items this mod adds to the game. Each is built from a copy of one of the game's items (a template
/// kept inactive, outside every scene) and registered like the game's own: under its item id in the
/// item database (inventories, pickups and the network refer to items by id) and under its prefab
/// name ("0_Items/" + name) in the network's prefab pool, which spawns items by name. Everyone in a
/// lobby runs the same mod version, so every client registers the same items under the same ids.
/// <para>The items are registered right before the game sets up its run settings, which gives every
/// item its custom-run switch and loot tables are built from the database later on.</para>
/// </summary>
internal static class ModItems
{
    private const string ItemFolder = "0_Items/";

    private static GameObject root;
    private static bool registered;

    /// <summary>The items this mod builds and registers (each builds and registers its own template), and the game's items it changes.</summary>
    private static readonly Action[] Builders =
    {
        Features.A17_Hazards.OwnEnergyDrinkBurst,
        Features.AutoParachute.Build,
        Features.WildFoods.Build,
        Features.VanillaWafers.Build,
        Features.StoneItem.Build,
        Features.StickItem.Build,
        Features.WarpCompass.Build,
        Features.CherryLollipop.Build,
        Features.ToxicDrink.Build,
        Features.TorchItem.Build,
        Features.SixSlotBackpack.Build,
        Features.Crabs.Build,
        Features.A14_Items.BuildPlacedPiton,
    };

    /// <summary>The templates' parent: inactive and kept across scenes, so no template ever runs.</summary>
    private static Transform Root
    {
        get
        {
            if (root == null)
            {
                root = new GameObject("UltimateAscentItems");
                root.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(root);
                root.hideFlags = HideFlags.HideAndDontSave;
            }
            return root.transform;
        }
    }

    /// <summary>A copy of one of the game's items under a new name, to build a new item from.</summary>
    internal static Item Copy(Item source, string name)
    {
        GameObject copy = UnityEngine.Object.Instantiate(source.gameObject, Root);
        copy.name = name;
        copy.SetActive(false);
        return copy.GetComponent<Item>();
    }

    /// <summary>A copy of one of the game's objects kept beside the templates (inactive), for an item of this mod to spawn.</summary>
    internal static GameObject Keep(GameObject source, string name)
    {
        GameObject copy = UnityEngine.Object.Instantiate(source, Root);
        copy.name = name;
        return copy;
    }

    /// <summary>A file shipped inside this DLL.</summary>
    internal static bool HasResource(string resource) => typeof(ModItems).Assembly.GetManifestResourceInfo(resource) != null;

    /// <summary>An image shipped inside this DLL as a texture (null, with a warning, if it cannot be read).</summary>
    internal static Texture2D LoadTexture(string resource, string name, TextureWrapMode wrap)
    {
        try
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!texture.LoadImage(Features.ClimbingChalkModel.Resource(resource), true))
            {
                throw new System.IO.InvalidDataException("not a readable image");
            }
            return texture;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"The image {resource} could not be loaded: {ex.Message}");
            return null;
        }
    }

    /// <summary>Registers a template under its id and prefab name; false if the id belongs to another item.</summary>
    internal static bool Register(Item template, ushort id)
    {
        ItemDatabase database = SingletonAsset<ItemDatabase>.Instance;
        if (database.itemLookup.TryGetValue(id, out Item taken) && taken != null && taken != template)
        {
            Plugin.Log.LogError($"Item id {id} already belongs to {taken.gameObject.name}; {template.gameObject.name} is not added.");
            return false;
        }
        if (!(PhotonNetwork.PrefabPool is DefaultPool pool))
        {
            Plugin.Log.LogError($"The network's prefab pool is not the game's own ({PhotonNetwork.PrefabPool?.GetType().Name}); {template.gameObject.name} is not added.");
            return false;
        }
        template.itemID = id;
        database.itemLookup[id] = template;
        if (!database.Objects.Contains(template))
        {
            database.Objects.Add(template);
        }
        pool.ResourceCache[ItemFolder + template.gameObject.name] = template.gameObject;
        if (RunSettings.initialized)
        {
            // Registered late (the run settings were set up first): its custom-run switch, as the game gives every item.
            RunSettings.AddDefaultSetting("Item_" + template.gameObject.name, 1, 1);
        }
        Plugin.Debug($"Registered item {template.gameObject.name} (id {id}).");
        return true;
    }

    /// <summary>
    /// The game's own prefab pool: the network's, or the one PEAKLib's pool keeps inside it (its own pool takes over
    /// once PEAKLib starts, and hands every prefab it does not know to the game's). Null if there is none.
    /// </summary>
    internal static DefaultPool GamePool()
    {
        IPunPrefabPool pool = PhotonNetwork.PrefabPool;
        if (pool is DefaultPool own)
        {
            return own;
        }
        return pool?.GetType().GetProperty("DefaultPool", BindingFlags.Public | BindingFlags.Instance)?.GetValue(pool) as DefaultPool;
    }

    /// <summary>Builds and registers every item of this mod, once.</summary>
    internal static void EnsureRegistered()
    {
        if (registered)
        {
            return;
        }
        registered = true;
        foreach (Action build in Builders)
        {
            try
            {
                build();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{build.Method.DeclaringType?.Name} could not add its items: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(RunSettings), nameof(RunSettings.Init))]
    private static class BeforeRunSettings
    {
        private static void Prefix()
        {
            EnsureRegistered();
        }
    }
}
