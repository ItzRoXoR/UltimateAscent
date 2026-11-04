using System.Linq;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Spruce Branch: the Stick in the Roots. A copy of the Stick (its grip, weight and swing, which knocks a scout or a
/// zombie down for 4 s) that looks like a spruce branch (see <see cref="SpruceLook.Branch"/>). It lies on the ground in
/// the Roots in the sticks' place (see <see cref="GroundItems"/>), and the Roots wind carries branches like it
/// (see <see cref="WindDebris"/>).
/// </summary>
internal static class SpruceBranch
{
    internal const string PrefabName = "UA_SpruceBranch";
    internal const string ItemName = "Spruce Branch";
    internal const string NameKey = "NAME_SPRUCE BRANCH";
    internal const ushort ItemId = 9008;
    private const string IconResource = "UltimateAscent.SpruceBranch_icon.png";

    /// <summary>The Stick's look in it, the game's driftwood stick.</summary>
    internal const string StickLook = "ShitStick";

    private static Item template;

    internal static Item Template => template;

    internal static bool IsBranch(Item item) => item != null && template != null && item.itemID == template.itemID;

    /// <summary>Built after the Stick, from it: the Stick's look swapped for the branch's (its collider, the stick's shape, stays).</summary>
    internal static void Build()
    {
        Item stick = StickItem.Template;
        if (stick == null)
        {
            Plugin.Log.LogWarning("The Stick is missing; the Spruce Branch is not added.");
            return;
        }
        Item branch = ModItems.Copy(stick, PrefabName);
        branch.UIData.itemName = ItemName;
        MeshFilter wood = branch.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == StickLook && f.sharedMesh != null);
        if (wood == null || !wood.TryGetComponent(out MeshRenderer woodLook))
        {
            Plugin.Log.LogWarning("The Stick's look was not found; the Spruce Branch is not added.");
            Object.Destroy(branch.gameObject);
            return;
        }
        woodLook.enabled = false;
        GameObject look = new GameObject("SpruceBranch");
        look.transform.SetParent(wood.transform, false);
        look.AddComponent<MeshFilter>().sharedMesh = SpruceLook.Branch(wood.sharedMesh.bounds);
        look.AddComponent<MeshRenderer>().sharedMaterials = SpruceLook.Materials;
        Texture2D icon = ModItems.HasResource(IconResource) ? ModItems.LoadTexture(IconResource, "SpruceBranch_icon", TextureWrapMode.Clamp) : null;
        if (icon != null)
        {
            branch.UIData.icon = icon;
        }
        if (branch.TryGetComponent(out LootData loot))
        {
            loot.spawnLocations = SpawnPool.None;
        }
        if (ModItems.Register(branch, ItemId))
        {
            template = branch;
        }
    }

    /// <summary>The branch's look (the meshes and materials of it), for the wind's flying branches.</summary>
    internal static GameObject Look()
    {
        if (template == null)
        {
            return null;
        }
        Transform look = template.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SpruceBranch");
        return look != null ? look.gameObject : null;
    }
}
