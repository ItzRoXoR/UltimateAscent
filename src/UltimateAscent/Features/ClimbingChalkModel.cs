using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// The Climbing Chalk is in the game files but has only Unity's placeholder cube for a model (and
/// is never handed out). It gets a chalk bag instead: a low-poly mesh coloured by a 4 x 3 palette,
/// both shipped inside this DLL. The prefab's "Cube" child keeps its place, its 0.28 scale and its
/// renderer; its mesh, material and box collider change, and the hands' grip points (Hand_L, Hand_R,
/// set for the cube) move in to the bag's sides. The bag is drawn with the game's own item shader
/// (a copy of the Backpack's fabric material), so it is lit like the other items: Unity's Lit, the
/// cube's material, left it a flat cut-out in the shade. Cooking darkens the bag like any item. In a
/// backpack it is shown larger than the game's half size, since the bag is smaller than the cube.
/// Cosmetic only, so it is not tied to an ascent.
/// </summary>
internal static class ClimbingChalkModel
{
    private const string ModelChild = "Cube";
    private const string MeshResource = "UltimateAscent.ClimbingChalk.obj";
    private const string PaletteResource = "UltimateAscent.ClimbingChalk_palette.png";

    /// <summary>The item whose material the bag's is copied from, and the game's item shader it uses.</summary>
    private const string TemplateItem = "Backpack";
    internal const string ItemShader = "W/Peak_Standard";

    /// <summary>
    /// The bag's own tint: the palette's colours as they are (the Backpack's 0.53 is for its own,
    /// brighter texture). Cooking darkens the bag from it, as the game does with every item's tint.
    /// </summary>
    internal static readonly Color BagTint = Color.white;

    /// <summary>Matte, like the model's own material (roughness 0.88).</summary>
    private const float Smoothness = 0.12f;

    /// <summary>
    /// How far a grip point sits outside the surface it holds: a scout's hand reaches about this far
    /// inward from its grip point (measured in game on the cube's grips, whose hands hovered 7.5 cm
    /// off the bag).
    /// </summary>
    private const float HandReach = 0.07f;

    /// <summary>
    /// In a backpack or fanny pack the game shows every item at half its size; the bag is smaller than
    /// the cube it replaces, so there it is shown at this size instead.
    /// </summary>
    private const float BackpackScale = 0.8f;

    private static Mesh mesh;
    private static Material material;
    private static bool failed;

    /// <summary>Every chalk the game creates (on the ground, in a hand) gets the bag.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.Awake))]
    private static class ChalkBag
    {
        private static void Postfix(Item __instance)
        {
            if (failed || !ClimbingChalkItem.IsChalk(__instance))
            {
                return;
            }
            Transform child = __instance.transform.Find(ModelChild);
            MeshFilter filter = child != null ? child.GetComponent<MeshFilter>() : null;
            MeshRenderer renderer = child != null ? child.GetComponent<MeshRenderer>() : null;
            if (filter == null || renderer == null || !Load())
            {
                return;
            }
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            if (child.TryGetComponent(out BoxCollider box))
            {
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }
            // The hands hold the bag's sides (left and right of it along the item's x).
            Bounds bag = mesh.bounds;
            float scale = child.localScale.x;
            Grip(__instance.transform.Find("Hand_L"), child.localPosition.x + bag.min.x * scale - HandReach);
            Grip(__instance.transform.Find("Hand_R"), child.localPosition.x + bag.max.x * scale + HandReach);
        }

        private static void Grip(Transform hand, float x)
        {
            if (hand != null)
            {
                Vector3 position = hand.localPosition;
                hand.localPosition = new Vector3(x, position.y, position.z);
            }
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.SetState))]
    private static class BagInBackpack
    {
        private static void Postfix(Item __instance, ItemState setState)
        {
            if (setState == ItemState.InBackpack && __instance.forceScale && !failed && ClimbingChalkItem.IsChalk(__instance))
            {
                __instance.transform.localScale = Vector3.one * BackpackScale;
            }
        }
    }

    private static bool Load()
    {
        if (mesh != null && material != null)
        {
            return true;
        }
        try
        {
            Material template = TemplateMaterial();
            if (template == null)
            {
                throw new InvalidDataException("the " + TemplateItem + " has no " + ItemShader + " material to copy");
            }
            mesh = ReadObj(Resource(MeshResource));
            // Read as linear: the game's item shader shows a texture's colours much darker than Unity's
            // Lit did (the game paints its own textures brighter for it); read this way, the palette
            // keeps the colours the bag had.
            Texture2D palette = new Texture2D(2, 2, TextureFormat.RGBA32, true, true)
            {
                name = "ClimbingChalk_palette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            if (!palette.LoadImage(Resource(PaletteResource), true))
            {
                throw new InvalidDataException("the palette is not a readable image");
            }
            // The Backpack's material (its paint layers and shading), coloured by the palette.
            material = new Material(template) { name = "ClimbingChalk" };
            material.SetTexture("_BaseTexture", palette);
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Tint", BagTint);
            material.SetFloat("_BaseSmooth", Smoothness);
            Plugin.Log.LogInfo($"Climbing Chalk model loaded: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles.");
            return true;
        }
        catch (Exception ex)
        {
            failed = true;
            mesh = null;
            material = null;
            Plugin.Log.LogError("The Climbing Chalk model could not be loaded; it keeps the game's cube. " + ex.Message);
            return false;
        }
    }

    /// <summary>The Backpack's material in the game's item shader (its ground and worn meshes share it).</summary>
    private static Material TemplateMaterial()
    {
        Item backpack = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i != null && i.gameObject.name == TemplateItem);
        return backpack == null ? null : backpack.GetComponentsInChildren<Renderer>(true)
            .Select(r => r.sharedMaterial)
            .FirstOrDefault(m => m != null && m.shader != null && m.shader.name == ItemShader);
    }

    internal static byte[] Resource(string name)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (stream == null)
            {
                throw new FileNotFoundException("missing resource", name);
            }
            using (MemoryStream copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }
    }

    /// <summary>
    /// Reads an OBJ the way Unity's own importer does: X negated into Unity's left-handed space and
    /// every face's corners turned around to match, so the model is not mirrored.
    /// </summary>
    private static Mesh ReadObj(byte[] data)
    {
        List<Vector3> positions = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> vertexUvs = new List<Vector2>();
        List<Vector3> vertexNormals = new List<Vector3>();
        List<int> triangles = new List<int>();
        bool hasNormals = true;
        using (StreamReader reader = new StreamReader(new MemoryStream(data)))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }
                switch (parts[0])
                {
                    case "v":
                        positions.Add(new Vector3(-Number(parts[1]), Number(parts[2]), Number(parts[3])));
                        break;
                    case "vt":
                        uvs.Add(new Vector2(Number(parts[1]), Number(parts[2])));
                        break;
                    case "vn":
                        normals.Add(new Vector3(-Number(parts[1]), Number(parts[2]), Number(parts[3])));
                        break;
                    case "f":
                        int first = vertices.Count;
                        for (int i = 1; i < parts.Length; i++)
                        {
                            string[] index = parts[i].Split('/');
                            vertices.Add(positions[Index(index[0], positions.Count)]);
                            vertexUvs.Add(index.Length > 1 && index[1].Length > 0 ? uvs[Index(index[1], uvs.Count)] : Vector2.zero);
                            if (index.Length > 2 && index[2].Length > 0)
                            {
                                vertexNormals.Add(normals[Index(index[2], normals.Count)]);
                            }
                            else
                            {
                                hasNormals = false;
                                vertexNormals.Add(Vector3.zero);
                            }
                        }
                        // A fan over the face's corners, turned around for the negated X.
                        for (int i = 1; i < parts.Length - 2; i++)
                        {
                            triangles.Add(first);
                            triangles.Add(first + i + 1);
                            triangles.Add(first + i);
                        }
                        break;
                }
            }
        }
        if (triangles.Count == 0)
        {
            throw new InvalidDataException("the model has no faces");
        }
        Mesh result = new Mesh { name = "ClimbingChalk" };
        if (vertices.Count > 65535)
        {
            result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        result.SetVertices(vertices);
        result.SetUVs(0, vertexUvs);
        result.SetTriangles(triangles, 0);
        if (hasNormals)
        {
            result.SetNormals(vertexNormals);
        }
        else
        {
            result.RecalculateNormals();
        }
        result.RecalculateBounds();
        return result;
    }

    /// <summary>An OBJ index: 1-based, or negative counting back from the end.</summary>
    private static int Index(string text, int count)
    {
        int value = int.Parse(text, CultureInfo.InvariantCulture);
        return value > 0 ? value - 1 : count + value;
    }

    private static float Number(string text)
    {
        return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
