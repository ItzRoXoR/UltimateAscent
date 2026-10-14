using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace UltimateAscent.Features;

/// <summary>
/// The Climbing Chalk's white hands. While a scout has the chalk's effect, a chalk-white copy of just
/// their hands (bare hands and gloves alike, up to the wrists) is drawn over them, for everyone (the
/// effect reaches every player). The scout's own renderers and materials are never changed.
/// <para>The hands of every outfit's body mesh come from the game files: the triangles weighted to the
/// hand and finger bones, with their skinning, extracted offline into Resources/ClimbingChalk_hands.bin
/// (the game's body meshes cannot be read at runtime). The copy is skinned with the body's own bones,
/// so it moves with the hands exactly; it is drawn right after the body with the body's skin material
/// in white, follows the body's visibility, first-person hiding and fades, and dissolves in when the
/// effect starts and out when it ends. An outfit whose body mesh is not in the file shows no white
/// hands.</para>
/// </summary>
internal static class ClimbingChalkHands
{
    private const string HandsResource = "UltimateAscent.ClimbingChalk_hands.bin";

    /// <summary>Chalk white, and how long the hands take to turn white and to lose it again.</summary>
    private static readonly Color ChalkWhite = new Color(0.95f, 0.95f, 0.92f, 1f);
    private const float WhitenSeconds = 0.4f;
    private const float FadeSeconds = 1.5f;

    private static readonly int SkinColorId = Shader.PropertyToID("_SkinColor");
    private static readonly int PaintColor1Id = Shader.PropertyToID("_Color1");
    private static readonly int PaintColor2Id = Shader.PropertyToID("_Color2");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int VertexGhostId = Shader.PropertyToID("_VertexGhost");

    /// <summary>One distinct set of hands, as stored; its mesh is built the first time it is needed.</summary>
    private sealed class Hands
    {
        internal Matrix4x4[] bindposes;
        internal Vector3[] positions;
        internal Vector3[] normals;
        internal Vector2[] uvs;
        internal Color32[] colours;
        internal BoneWeight[] weights;
        internal int[] triangles;
        internal Mesh mesh;
    }

    private static List<Hands> hands;
    private static Dictionary<string, Hands> handsByBody;
    private static bool loadFailed;
    private static readonly HashSet<string> unknownBodies = new HashSet<string>();

    /// <summary>One chalked scout's white hands.</summary>
    private sealed class Chalked
    {
        internal SkinnedMeshRenderer body;
        internal Mesh bodyMesh;
        internal SkinnedMeshRenderer overlay;
        internal Material material;
        internal float white;
    }

    private static readonly Dictionary<Character, Chalked> chalked = new Dictionary<Character, Chalked>();
    private static readonly List<Character> scratch = new List<Character>();

    internal static void Reset()
    {
        foreach (Chalked state in chalked.Values)
        {
            Remove(state);
        }
        chalked.Clear();
    }

    /// <summary>Called every frame.</summary>
    internal static void Tick()
    {
        foreach (Character character in Character.AllCharacters)
        {
            if (character != null && !chalked.ContainsKey(character) && ClimbingChalkItem.Chalked(character))
            {
                chalked[character] = new Chalked();
            }
        }
        if (chalked.Count == 0)
        {
            return;
        }
        scratch.Clear();
        scratch.AddRange(chalked.Keys);
        foreach (Character character in scratch)
        {
            Chalked state = chalked[character];
            if (character == null)
            {
                // Gone (a new scene, a player who left), with the body the white hands hung on.
                Remove(state);
                chalked.Remove(character);
                continue;
            }
            bool on = ClimbingChalkItem.Chalked(character);
            state.white = Mathf.MoveTowards(state.white, on ? 1f : 0f, Time.deltaTime / (on ? WhitenSeconds : FadeSeconds));
            CustomizationRefs refs = character.refs.customization != null ? character.refs.customization.refs : null;
            SkinnedMeshRenderer body = refs != null ? refs.mainRenderer : null;
            if (body == null || state.white <= 0f)
            {
                Remove(state);
                if (!on)
                {
                    chalked.Remove(character);
                }
                continue;
            }
            if (state.overlay == null || state.body != body || state.bodyMesh != body.sharedMesh)
            {
                Remove(state);
                Create(state, body);
                if (state.overlay == null)
                {
                    continue;
                }
            }
            Follow(state);
        }
    }

    private static void Create(Chalked state, SkinnedMeshRenderer body)
    {
        state.body = body;
        state.bodyMesh = body.sharedMesh;
        Mesh handMesh = MeshFor(body.sharedMesh);
        Material skin = body.sharedMaterial;
        if (handMesh == null || skin == null)
        {
            return;
        }
        GameObject holder = new GameObject("UltimateAscent Chalked Hands");
        holder.layer = body.gameObject.layer;
        holder.transform.SetParent(body.transform, false);
        SkinnedMeshRenderer overlay = holder.AddComponent<SkinnedMeshRenderer>();
        overlay.sharedMesh = handMesh;
        overlay.bones = body.bones;
        overlay.rootBone = body.rootBone;
        overlay.localBounds = body.localBounds;
        overlay.updateWhenOffscreen = body.updateWhenOffscreen;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = body.receiveShadows;
        overlay.lightProbeUsage = body.lightProbeUsage;
        // The scout's skin material, in chalk white, drawn right after the body (over its own hands).
        Material material = new Material(skin) { name = "ClimbingChalk White Hands" };
        material.renderQueue = skin.renderQueue + 1;
        if (material.HasProperty(SkinColorId))
        {
            material.SetColor(SkinColorId, ChalkWhite);
        }
        foreach (int paint in new[] { PaintColor1Id, PaintColor2Id })
        {
            if (material.HasProperty(paint))
            {
                Color own = material.GetColor(paint);
                material.SetColor(paint, new Color(ChalkWhite.r, ChalkWhite.g, ChalkWhite.b, own.a));
            }
        }
        overlay.sharedMaterial = material;
        state.overlay = overlay;
        state.material = material;
    }

    /// <summary>The white hands show when the body does, hide its parts where it does, and dissolve in and out.</summary>
    private static void Follow(Chalked state)
    {
        SkinnedMeshRenderer body = state.body;
        state.overlay.enabled = body.enabled;
        Material skin = body.sharedMaterial;
        if (skin == null)
        {
            return;
        }
        if (state.material.HasProperty(OpacityId))
        {
            float own = skin.HasProperty(OpacityId) ? skin.GetFloat(OpacityId) : 1f;
            state.material.SetFloat(OpacityId, own * state.white);
        }
        if (state.material.HasProperty(VertexGhostId) && skin.HasProperty(VertexGhostId))
        {
            state.material.SetFloat(VertexGhostId, skin.GetFloat(VertexGhostId));
        }
    }

    private static void Remove(Chalked state)
    {
        if (state.overlay != null)
        {
            UnityEngine.Object.Destroy(state.overlay.gameObject);
        }
        if (state.material != null)
        {
            UnityEngine.Object.Destroy(state.material);
        }
        state.overlay = null;
        state.material = null;
        state.body = null;
        state.bodyMesh = null;
    }

    /// <summary>The hands of a body mesh (by its name and vertex count), or null for one not in the file.</summary>
    private static Mesh MeshFor(Mesh body)
    {
        if (body == null || !Load())
        {
            return null;
        }
        string key = body.name + "|" + body.vertexCount;
        if (!handsByBody.TryGetValue(key, out Hands set))
        {
            if (unknownBodies.Add(key))
            {
                Plugin.Log.LogWarning($"Climbing Chalk: no hands known for the body mesh {body.name} ({body.vertexCount} vertices); its hands do not turn white.");
            }
            return null;
        }
        if (set.mesh == null)
        {
            Mesh mesh = new Mesh { name = "ClimbingChalk hands" };
            mesh.SetVertices(set.positions);
            mesh.SetNormals(set.normals);
            mesh.SetUVs(0, set.uvs);
            mesh.SetColors(set.colours);
            mesh.boneWeights = set.weights;
            mesh.bindposes = set.bindposes;
            mesh.SetTriangles(set.triangles, 0);
            mesh.RecalculateBounds();
            mesh.hideFlags = HideFlags.HideAndDontSave;
            set.mesh = mesh;
        }
        return set.mesh;
    }

    private static bool Load()
    {
        if (handsByBody != null || loadFailed)
        {
            return handsByBody != null;
        }
        try
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(ClimbingChalkModel.Resource(HandsResource)), Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "UACH" || reader.ReadInt32() != 2)
                {
                    throw new InvalidDataException("unknown format");
                }
                int distinct = reader.ReadInt32();
                List<Hands> sets = new List<Hands>(distinct);
                for (int s = 0; s < distinct; s++)
                {
                    Hands set = new Hands();
                    int poses = reader.ReadInt32();
                    set.bindposes = new Matrix4x4[poses];
                    for (int i = 0; i < poses; i++)
                    {
                        Matrix4x4 m = new Matrix4x4();
                        for (int row = 0; row < 4; row++)
                        {
                            for (int column = 0; column < 4; column++)
                            {
                                m[row, column] = reader.ReadSingle();
                            }
                        }
                        set.bindposes[i] = m;
                    }
                    int vertices = reader.ReadInt32();
                    set.positions = new Vector3[vertices];
                    set.normals = new Vector3[vertices];
                    set.uvs = new Vector2[vertices];
                    set.colours = new Color32[vertices];
                    set.weights = new BoneWeight[vertices];
                    for (int v = 0; v < vertices; v++)
                    {
                        set.positions[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        set.normals[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        set.uvs[v] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                        byte[] c = reader.ReadBytes(4);
                        set.colours[v] = new Color32(c[0], c[1], c[2], c[3]);
                        byte[] b = reader.ReadBytes(4);
                        set.weights[v] = new BoneWeight
                        {
                            boneIndex0 = b[0],
                            boneIndex1 = b[1],
                            boneIndex2 = b[2],
                            boneIndex3 = b[3],
                            weight0 = reader.ReadSingle(),
                            weight1 = reader.ReadSingle(),
                            weight2 = reader.ReadSingle(),
                            weight3 = reader.ReadSingle(),
                        };
                    }
                    int indices = reader.ReadInt32();
                    set.triangles = new int[indices];
                    for (int i = 0; i < indices; i++)
                    {
                        set.triangles[i] = reader.ReadUInt16();
                    }
                    sets.Add(set);
                }
                Dictionary<string, Hands> byBody = new Dictionary<string, Hands>();
                int bodies = reader.ReadInt32();
                for (int i = 0; i < bodies; i++)
                {
                    string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                    int vertexCount = reader.ReadInt32();
                    int index = reader.ReadInt32();
                    byBody[name + "|" + vertexCount] = sets[index];
                }
                hands = sets;
                handsByBody = byBody;
            }
            return true;
        }
        catch (Exception ex)
        {
            loadFailed = true;
            Plugin.Log.LogError("The Climbing Chalk's white hands could not be loaded: " + ex.Message);
            return false;
        }
    }
}
