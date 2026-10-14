using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Lightweight Parasol has a look of its own (the user's recolour of the Parasol): its canopy's
/// rainbow becomes dark blue, turquoise and pale mint stripes, and it has its own inventory icon. The
/// frame and the handle stay the game's.
/// <list type="bullet">
/// <item>The game paints the canopy with vertex colours, and its meshes cannot be read at runtime, so
/// the two canopy meshes (open and closed) ship as copies made from the game files, every vertex
/// marked by the stripe it belongs to (Resources/LightweightParasol_canopy.bin); each stripe gets its
/// colour here.</item>
/// <item>The canopy gets its own copy of the parasol material with a neutral tint: the game's warm
/// tint holds blues and greens back. Cooking darkens it the game's way, from that tint.</item>
/// <item>The icon is the user's recolour inside the game icon's outline
/// (Resources/LightweightParasol_icon.png).</item>
/// <item>Only where the Roots parasol is the Lightweight Parasol (from ascent 9 and in custom runs);
/// otherwise it keeps the Parasol's look.</item>
/// </list>
/// </summary>
internal static class LightweightParasolLook
{
    private const string IconResource = "UltimateAscent.LightweightParasol_icon.png";
    private const string CanopyResource = "UltimateAscent.LightweightParasol_canopy.bin";
    private const string TintProperty = "_Tint";

    /// <summary>
    /// The stripes, in the order of the game's stripes they replace (blue, red, yellow, green): picked
    /// so that, under <see cref="CanopyTint"/>, they render like the user's image (dark blue, turquoise,
    /// pale mint, turquoise; fitted on the game's own render of the Parasol's icon).
    /// </summary>
    private static readonly Color32[] StripeColours =
    {
        new Color32(0, 60, 255, 255),
        new Color32(16, 170, 189, 255),
        new Color32(215, 255, 250, 255),
        new Color32(8, 187, 193, 255),
    };

    /// <summary>The canopy material's tint (the game's: 0.53, 0.42, 0.40).</summary>
    private static readonly Color CanopyTint = new Color(0.485f, 0.507f, 0.543f, 1f);

    private static readonly object Marker = new object();

    private static Dictionary<string, Mesh> canopies;
    private static Material canopyMaterial;
    private static Texture2D icon;
    private static bool canopyFailed;
    private static bool iconFailed;
    private static Item.ItemUIData prefabUIData;

    /// <summary>The UI data of every Roots parasol in the world (each item has its own copy).</summary>
    private static readonly ConditionalWeakTable<Item.ItemUIData, object> rootsUIData = new ConditionalWeakTable<Item.ItemUIData, object>();

    /// <summary>A Roots parasol made while it is the Lightweight Parasol gets its canopy.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.Awake))]
    private static class OwnCanopy
    {
        private static void Postfix(Item __instance)
        {
            if (!A14_Items.IsRootsParasol(__instance))
            {
                return;
            }
            rootsUIData.GetValue(__instance.UIData, _ => Marker);
            if (!A14_Items.IsLightweightParasol(__instance) || !LoadCanopies())
            {
                return;
            }
            foreach (MeshFilter filter in __instance.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh game = filter.sharedMesh;
                if (game == null || !canopies.TryGetValue(game.name, out Mesh own) || game.vertexCount != own.vertexCount)
                {
                    continue;
                }
                filter.sharedMesh = own;
                if (filter.TryGetComponent(out MeshRenderer renderer) && renderer.sharedMaterial != null)
                {
                    renderer.sharedMaterial = CanopyMaterial(renderer.sharedMaterial);
                }
            }
        }
    }

    /// <summary>Cooking tints the canopy from its own tint, the way the game tints every item.</summary>
    [HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.CookVisually))]
    private static class CookedCanopy
    {
        private static void Postfix(ItemCooking __instance, int cookedAmount)
        {
            if (cookedAmount <= 0 || canopies == null || __instance.renderers == null)
            {
                return;
            }
            foreach (Renderer renderer in __instance.renderers)
            {
                if (renderer == null || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !canopies.ContainsValue(filter.sharedMesh))
                {
                    continue;
                }
                foreach (Material material in renderer.materials)
                {
                    material.SetColor(TintProperty, CanopyTint * ItemCooking.GetCookColor(cookedAmount));
                }
            }
        }
    }

    /// <summary>Every icon the game shows (inventory, backpack wheel, a friend's use) comes from here.</summary>
    [HarmonyPatch(typeof(Item.ItemUIData), nameof(Item.ItemUIData.GetIcon))]
    private static class OwnIcon
    {
        private static void Postfix(Item.ItemUIData __instance, ref Texture2D __result)
        {
            if (!A14_Items.LightweightParasolExists || !IsRoots(__instance))
            {
                return;
            }
            Texture2D own = Icon();
            if (own != null)
            {
                __result = own;
            }
        }
    }

    private static bool IsRoots(Item.ItemUIData data)
    {
        if (prefabUIData == null && ItemDatabase.TryGetItem(A14_Items.RootsParasolPrefab, out Item prefab) && prefab != null)
        {
            prefabUIData = prefab.UIData;
        }
        return ReferenceEquals(data, prefabUIData) || rootsUIData.TryGetValue(data, out _);
    }

    private static Material CanopyMaterial(Material game)
    {
        if (canopyMaterial == null)
        {
            canopyMaterial = new Material(game)
            {
                name = game.name + " (Lightweight Parasol)",
                hideFlags = HideFlags.HideAndDontSave
            };
            canopyMaterial.SetColor(TintProperty, CanopyTint);
        }
        return canopyMaterial;
    }

    internal static Texture2D Icon()
    {
        if (icon != null || iconFailed)
        {
            return icon;
        }
        try
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "LightweightParasol_icon",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!texture.LoadImage(ClimbingChalkModel.Resource(IconResource), true))
            {
                throw new InvalidDataException("the icon is not a readable image");
            }
            icon = texture;
        }
        catch (Exception ex)
        {
            iconFailed = true;
            Plugin.Log.LogError("The Lightweight Parasol's icon could not be loaded: " + ex.Message);
        }
        return icon;
    }

    private static bool LoadCanopies()
    {
        if (canopies != null || canopyFailed)
        {
            return canopies != null;
        }
        try
        {
            canopies = ReadCanopies(ClimbingChalkModel.Resource(CanopyResource));
        }
        catch (Exception ex)
        {
            canopyFailed = true;
            Plugin.Log.LogError("The Lightweight Parasol's canopy could not be loaded: " + ex.Message);
        }
        return canopies != null;
    }

    /// <summary>
    /// The file: "ULPC", version 1, the number of meshes; per mesh its name in the game, its vertex count,
    /// positions, normals, tangents and UVs (floats), each vertex's stripe (a byte), then its submeshes'
    /// triangle indices.
    /// </summary>
    private static Dictionary<string, Mesh> ReadCanopies(byte[] data)
    {
        Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        using (BinaryReader reader = new BinaryReader(new MemoryStream(data)))
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "ULPC" || reader.ReadInt32() != 1)
            {
                throw new InvalidDataException("not a canopy file");
            }
            int count = reader.ReadInt32();
            for (int m = 0; m < count; m++)
            {
                string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                int vertices = reader.ReadInt32();
                Vector3[] positions = new Vector3[vertices];
                Vector3[] normals = new Vector3[vertices];
                Vector4[] tangents = new Vector4[vertices];
                Vector2[] uvs = new Vector2[vertices];
                Color32[] colours = new Color32[vertices];
                for (int i = 0; i < vertices; i++)
                {
                    positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                for (int i = 0; i < vertices; i++)
                {
                    normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                for (int i = 0; i < vertices; i++)
                {
                    tangents[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                for (int i = 0; i < vertices; i++)
                {
                    uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
                byte[] stripes = reader.ReadBytes(vertices);
                for (int i = 0; i < vertices; i++)
                {
                    if (stripes[i] >= StripeColours.Length)
                    {
                        throw new InvalidDataException($"vertex {i} of {name} has no stripe");
                    }
                    colours[i] = StripeColours[stripes[i]];
                }
                Mesh mesh = new Mesh
                {
                    name = name + " (Lightweight Parasol)",
                    hideFlags = HideFlags.HideAndDontSave,
                    vertices = positions,
                    normals = normals,
                    tangents = tangents,
                    uv = uvs,
                    colors32 = colours
                };
                int subMeshes = reader.ReadInt32();
                mesh.subMeshCount = subMeshes;
                for (int s = 0; s < subMeshes; s++)
                {
                    int[] indices = new int[reader.ReadInt32()];
                    for (int i = 0; i < indices.Length; i++)
                    {
                        indices[i] = reader.ReadInt32();
                    }
                    mesh.SetTriangles(indices, s);
                }
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                meshes[name] = mesh;
            }
        }
        return meshes;
    }
}
