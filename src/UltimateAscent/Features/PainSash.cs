using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace UltimateAscent.Features;

/// <summary>
/// The Pain Sash (one of the mod's cosmetics, <see cref="ModCosmetics"/>; reaching the peak on ascent 25 unlocks it):
/// a sash soaked in blood, made of the game's own sash layers (the spotted ascent 8 sash's paint and splatter, the
/// galaxy sash's smoke): dark maroon cloth with washed-out patches, dried stains and red splatter, the stains and the
/// brightest spots slowly drifting as the galaxy sash's layers do.
/// <para>A scout wearing it leaves bloody boot prints: one at each step on the ground (not while climbing, not in
/// water), where the planted foot is, on the game of every player with the mod (nothing is sent: each game sees the
/// scout's sash and steps). A print is the sash's own material cut out in a boot's shape; it erodes away over its last
/// seconds and is gone after 10 s.</para>
/// </summary>
internal static class PainSash
{
    internal const string MaterialName = "UA_M_Sash_Pain";
    private const string SpottedSash = "M_Sash_Ascent8";
    private const string GalaxySash = "M_Sash_Ascent9";

    /// <summary>A print stays this long, eroding away over its last seconds.</summary>
    internal const float PrintSeconds = 10f;
    private const float ErodeSeconds = 3f;

    /// <summary>The print's alpha cut-off while it is fresh and as it is gone (its shape's alpha runs 0.52-1).</summary>
    private const float FreshCutoff = 0.3f;
    private const float GoneCutoff = 1f;

    /// <summary>A boot print's size (m) and how far over the ground it lies.</summary>
    private const float PrintLength = 0.3f;
    private const float PrintWidth = 0.15f;
    private const float OverGround = 0.015f;

    /// <summary>The print's middle is this far ahead of the foot's joint (towards the toes).</summary>
    private const float FootAhead = 0.05f;

    /// <summary>A scout's prints are at least this far apart in time (a landing plays several steps at once).</summary>
    private const float StepGap = 0.2f;

    /// <summary>Steps closer than this are one walk, the feet taking turns.</summary>
    private const float WalkGap = 1f;

    /// <summary>The most prints shown for one scout (10 s of running) and in all.</summary>
    private const int PrintsPerScout = 40;
    private const int PrintsInAll = 160;

    private sealed class Print
    {
        internal GameObject Body;
        internal Renderer Renderer;
        internal Character Scout;
        internal float Born;
    }

    private static readonly List<Print> prints = new List<Print>();
    private static readonly Dictionary<Character, (float at, bool left)> lastPrint = new Dictionary<Character, (float, bool)>();
    private static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");
    private static MaterialPropertyBlock block;
    private static Material material;
    private static Material printMaterial;
    private static Mesh quad;
    private static bool failed;
    private static bool stepFailed;

    /// <summary>The prints on the ground now (for the tests).</summary>
    internal static int PrintsShown
    {
        get
        {
            int shown = 0;
            foreach (Print print in prints)
            {
                shown += print.Body != null && print.Body.activeSelf ? 1 : 0;
            }
            return shown;
        }
    }

    /// <summary>Where the prints on the ground are (for the tests).</summary>
    internal static IEnumerable<Transform> PrintsOnGround()
    {
        foreach (Print print in prints)
        {
            if (print.Body != null && print.Body.activeSelf)
            {
                yield return print.Body.transform;
            }
        }
    }

    /// <summary>The Pain Sash's material, made once from the game's sash materials (null if they are not the game's).</summary>
    internal static Material MaterialFrom(Material[] sashes)
    {
        if (material != null || failed)
        {
            return material;
        }
        Material spotted = Find(sashes, SpottedSash);
        Material galaxy = Find(sashes, GalaxySash);
        Texture paint = spotted != null ? spotted.GetTexture("_Texture1") : null;
        Texture splatter = spotted != null ? spotted.GetTexture("_Texture3") : null;
        Texture smoke = galaxy != null ? galaxy.GetTexture("_Texture1") : null;
        if (paint == null || splatter == null || smoke == null)
        {
            failed = true;
            Plugin.Log.LogWarning($"Pain Sash: the game's sash layers are missing (spotted sash {spotted != null}, galaxy sash {galaxy != null}).");
            return null;
        }
        Material made = new Material(spotted) { name = MaterialName, hideFlags = HideFlags.DontUnloadUnusedAsset };
        made.SetVector("_HueShifts", Vector4.zero);
        made.SetColor("_Tint", Color.white);
        made.SetColor("_BaseColor", new Color(0.05f, 0.004f, 0.006f, 1f));
        // Layer windows are linear values (the layers' textures are read as colour): a texture's grey 0.5 reads 0.22.
        // Washed-out patches.
        Layer(made, 1, paint, new Color(0.2f, 0.055f, 0.055f, 0.8f), 0.12f, 0.2f, 0.3f);
        // Dried stains, drifting.
        Layer(made, 2, smoke, new Color(0.02f, 0.001f, 0.002f, 0.9f), 0.3f, 0.45f, 0.5f, new Vector2(0.004f, -0.008f));
        // Red splatter: the spotted sash's own splatter layer (its height blend kept).
        made.SetColor("_Color3", new Color(0.16f, 0.006f, 0.008f, 1f));
        made.SetVector("_Remap3", new Vector4(0.4f, 0.6f, 0f, 0f));
        made.SetFloat("_UV3", 0.35f);
        // The brightest spots, drifting the other way.
        Layer(made, 4, splatter, new Color(0.4f, 0.02f, 0.02f, 1f), 0.7f, 0.8f, 0.6f, new Vector2(-0.003f, 0.006f));
        material = made;
        return material;
    }

    private static Material Find(Material[] sashes, string name)
    {
        if (sashes == null)
        {
            return null;
        }
        foreach (Material sash in sashes)
        {
            if (sash != null && sash.name.StartsWith(name, StringComparison.Ordinal))
            {
                return sash;
            }
        }
        return null;
    }

    private static void Layer(Material m, int n, Texture texture, Color color, float from, float to, float scale, Vector2 drift = default)
    {
        m.SetTexture($"_Texture{n}", texture);
        m.SetColor($"_Color{n}", color);
        m.SetVector($"_Remap{n}", new Vector4(from, to, 0f, 0f));
        m.SetFloat($"_UV{n}", scale);
        m.SetVector($"_Tex{n}Movement", new Vector4(drift.x, drift.y, 0f, 0f));
        m.SetFloat($"_Height{n}", 0f);
        m.SetFloat($"_Triplanar{n}On", 0f);
        m.SetFloat($"_UseHeightMask{n}", 0f);
    }

    /// <summary>The scout wears the Pain Sash, and it shows (a mushroom zombie's is hidden).</summary>
    internal static bool Wears(Character scout)
    {
        if (scout == null || scout.refs == null || scout.refs.customization == null || scout.refs.customization.refs == null)
        {
            return false;
        }
        Renderer sash = scout.refs.customization.refs.sashRenderer;
        if (sash == null || !sash.enabled || !sash.gameObject.activeInHierarchy)
        {
            return false;
        }
        // By name: the game gives each scout's sash copies of its materials.
        Material[] materials = sash.sharedMaterials;
        return materials.Length > 1 && materials[1] != null && materials[1].name.StartsWith(MaterialName, StringComparison.Ordinal);
    }

    [HarmonyPatch(typeof(FootStepPlayer), "PlayStep")]
    private static class OnStep
    {
        private static void Postfix(FootStepPlayer __instance)
        {
            try
            {
                Step(__instance.character, __instance.floorLayer);
            }
            catch (Exception ex)
            {
                if (!stepFailed)
                {
                    stepFailed = true;
                    Plugin.Log.LogWarning("Pain Sash: a footprint could not be left (" + ex.Message + ").");
                }
            }
        }
    }

    /// <summary>A step: a print where the planted foot is, if the scout wears the sash and walks on the ground.</summary>
    private static void Step(Character scout, LayerMask floor)
    {
        if (material == null || !Wears(scout))
        {
            return;
        }
        CharacterData data = scout.data;
        if (data.dead || data.passedOut || data.fullyPassedOut || data.isClimbingAnything || !data.isGrounded || data.isInWater)
        {
            return;
        }
        bool walking = lastPrint.TryGetValue(scout, out (float at, bool left) last) && Time.time - last.at < WalkGap;
        if (walking && Time.time - last.at < StepGap)
        {
            return;
        }
        Vector3 left = scout.GetBodypart(BodypartType.Foot_L).transform.position;
        Vector3 right = scout.GetBodypart(BodypartType.Foot_R).transform.position;
        // Walking, the feet take turns; from standing, the lower foot steps first. The toes point the way the scout
        // goes (or looks, standing).
        bool isLeft = walking ? !last.left : left.y < right.y;
        Vector3 going = Vector3.ProjectOnPlane(data.avarageVelocity, Vector3.up);
        Vector3 way = going.sqrMagnitude > 0.09f ? going.normalized : data.lookDirection_Flat;
        Vector3 under = (isLeft ? left : right) + way * FootAhead;
        if (way.sqrMagnitude < 1e-4f || !Physics.Raycast(under + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.5f, floor, QueryTriggerInteraction.Ignore))
        {
            return;
        }
        Vector3 forward = Vector3.ProjectOnPlane(way, hit.normal);
        if (forward.sqrMagnitude < 1e-4f)
        {
            return;
        }
        Print print = Take(scout);
        if (print == null)
        {
            return;
        }
        lastPrint[scout] = (Time.time, isLeft);
        print.Body.transform.SetPositionAndRotation(hit.point + hit.normal * OverGround, Quaternion.LookRotation(forward, hit.normal));
        // The print is a right boot's: the left one is its mirror.
        print.Body.transform.localScale = new Vector3(isLeft ? -PrintWidth : PrintWidth, 1f, PrintLength);
        print.Scout = scout;
        print.Born = Time.time;
        Cut(print.Renderer, FreshCutoff);
        print.Body.SetActive(true);
    }

    /// <summary>A print to leave: a free one, else the scout's oldest (at its most), else the oldest of all (at the most in all).</summary>
    private static Print Take(Character scout)
    {
        Print free = null, oldest = null, oldestOfScout = null;
        int shown = 0, ofScout = 0;
        prints.RemoveAll(p => p.Body == null);
        foreach (Print print in prints)
        {
            if (!print.Body.activeSelf)
            {
                free ??= print;
                continue;
            }
            shown++;
            if (oldest == null || print.Born < oldest.Born)
            {
                oldest = print;
            }
            if (print.Scout == scout)
            {
                ofScout++;
                if (oldestOfScout == null || print.Born < oldestOfScout.Born)
                {
                    oldestOfScout = print;
                }
            }
        }
        if (ofScout >= PrintsPerScout)
        {
            return oldestOfScout;
        }
        if (free != null)
        {
            return free;
        }
        if (shown >= PrintsInAll)
        {
            return oldest;
        }
        Material look = PrintMaterial();
        if (look == null)
        {
            return null;
        }
        GameObject body = new GameObject("UltimateAscent Bloody Print");
        body.SetActive(false);
        body.AddComponent<MeshFilter>().sharedMesh = Quad();
        MeshRenderer renderer = body.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = look;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        Print made = new Print { Body = body, Renderer = renderer };
        prints.Add(made);
        return made;
    }

    /// <summary>The sash's material, still and without its washed-out patches (blood on the ground), cut out in the boot print's shape.</summary>
    private static Material PrintMaterial()
    {
        if (printMaterial != null)
        {
            return printMaterial;
        }
        Texture2D shape = ModCosmetics.Icon("PainSash_footprint.png");
        if (material == null || shape == null)
        {
            return null;
        }
        shape.wrapMode = TextureWrapMode.Clamp;
        printMaterial = new Material(material) { name = MaterialName + " Print", hideFlags = HideFlags.DontUnloadUnusedAsset };
        for (int n = 1; n <= 4; n++)
        {
            printMaterial.SetVector($"_Tex{n}Movement", Vector4.zero);
        }
        printMaterial.SetColor("_Color1", Color.clear);
        printMaterial.SetTexture("_MainTex", shape);
        printMaterial.SetFloat("_UseTextureAlpha", 1f);
        printMaterial.SetFloat("_AlphaClip", FreshCutoff);
        printMaterial.SetFloat("_Cull", 0f);
        return printMaterial;
    }

    /// <summary>A flat square on the ground, a metre a side (the print's size is its scale), the toes towards +z.</summary>
    private static Mesh Quad()
    {
        if (quad != null)
        {
            return quad;
        }
        quad = new Mesh { name = "UltimateAscent Print Quad" };
        quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
        quad.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
        quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        quad.RecalculateNormals();
        quad.RecalculateTangents();
        quad.RecalculateBounds();
        return quad;
    }

    private static void Cut(Renderer renderer, float cutoff)
    {
        block ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetFloat(AlphaClip, cutoff);
        renderer.SetPropertyBlock(block);
    }

    /// <summary>Every frame: the prints erode over their last seconds and are gone after 10 s.</summary>
    internal static void Tick()
    {
        if (prints.Count == 0)
        {
            return;
        }
        float now = Time.time;
        foreach (Print print in prints)
        {
            if (print.Body == null || !print.Body.activeSelf)
            {
                continue;
            }
            float age = now - print.Born;
            if (age >= PrintSeconds)
            {
                print.Body.SetActive(false);
                continue;
            }
            if (age > PrintSeconds - ErodeSeconds)
            {
                Cut(print.Renderer, Mathf.Lerp(FreshCutoff, GoneCutoff, (age - (PrintSeconds - ErodeSeconds)) / ErodeSeconds));
            }
        }
    }

    /// <summary>A new scene: the prints went with the old one.</summary>
    internal static void Reset()
    {
        foreach (Print print in prints)
        {
            if (print.Body != null)
            {
                UnityEngine.Object.Destroy(print.Body);
            }
        }
        prints.Clear();
        lastPrint.Clear();
    }
}
