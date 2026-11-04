using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Spruce needles stuck in a scout: the game's cactus thorns in all but their look. A needle the wind drives into a
/// scout takes the cactus thorn nearest where it hit (the thorns a scout carries, ready on their body), with all a
/// thorn does (on the bar, coming out as thorns do, pulled out by hand), only shown as a spruce needle on every game:
/// the cactus spine hidden, a needle in its place stuck in by its tip, the rest of it standing out from the body. Once
/// it is out, the thorn looks like a cactus thorn again. On a scout True Final Ascent has made bigger (its Mega size)
/// the needle is as much bigger, as its cactus spines are; its Mini scouts take no cactus thorns at all (TFA's rule).
/// A needle that hits an item a scout holds sticks in it instead, by its tip along the way it flew, on every game; it
/// stays in the item (dropped too) until there are more than <see cref="MostInItem"/> in it, the oldest going first.
/// </summary>
internal static class SpruceNeedles
{
    private const string Message = "spruceneedle";
    private const string ItemMessage = "spruceneedleitem";

    /// <summary>At most this many needles stuck in one item; the oldest goes when another comes.</summary>
    internal const int MostInItem = 40;
    /// <summary>The game's cactus thorns, and the look a thorn of them shows (its spine).</summary>
    private const int CactusThorn = 0;
    private const string CactusSpine = "Cactus spine";
    /// <summary>
    /// A needle, stuck in or flying: this long and this thick at its base, far thicker than a real one so it shows as a
    /// cactus spine does (the user's); and how deep a stuck one's tip is in.
    /// </summary>
    internal const float Length = 0.24f;
    internal const float Thickness = 0.015f;
    private const float TipIn = 0.015f;

    /// <summary>The needle's mesh (a metre long and 2.2 cm across, see <see cref="SpruceLook.Needle"/>) scaled to a needle's size.</summary>
    internal static Vector3 Scale => new Vector3(Thickness / (2f * SpruceLook.NeedleRadius), Thickness / (2f * SpruceLook.NeedleRadius), Length);

    private struct Spine
    {
        internal MeshRenderer renderer;
        internal GameObject needle;
    }

    private static readonly Dictionary<ThornOnMe, Spine> needles = new Dictionary<ThornOnMe, Spine>();
    private static readonly Dictionary<Item, Queue<GameObject>> inItems = new Dictionary<Item, Queue<GameObject>>();

    /// <summary>For the tests: needles stuck in on this game (in any scout), and whether a thorn shows one.</summary>
    internal static int Stuck { get; private set; }
    internal static bool Shows(ThornOnMe thorn) => thorn != null && needles.ContainsKey(thorn);

    /// <summary>For the tests: the needles stuck in this item now.</summary>
    internal static int InItem(Item item) => item != null && inItems.TryGetValue(item, out Queue<GameObject> stuck) ? stuck.Count(n => n != null) : 0;

    internal static void Reset()
    {
        foreach (ThornOnMe thorn in needles.Keys.ToList())
        {
            Restore(thorn);
        }
        needles.Clear();
        inItems.Clear();
    }

    internal static void Listen()
    {
        Net.Handle(Message, args =>
        {
            if (args.Length > 1 && args[0] is int view && args[1] is int index)
            {
                PhotonView found = PhotonView.Find(view);
                Character scout = found != null ? found.GetComponent<Character>() : null;
                List<ThornOnMe> thorns = scout != null ? scout.refs.afflictions.physicalThorns : null;
                if (thorns != null && index >= 0 && index < thorns.Count && thorns[index] != null && thorns[index].stuckIn)
                {
                    Show(thorns[index]);
                }
            }
        });
        Net.Handle(ItemMessage, args =>
        {
            if (args.Length > 2 && args[0] is int view && args[1] is Vector3 at && args[2] is Vector3 way)
            {
                PhotonView found = PhotonView.Find(view);
                Item item = found != null ? found.GetComponent<Item>() : null;
                if (item != null)
                {
                    ShowInItem(item, at, way);
                }
            }
        });
    }

    /// <summary>A needle driven into an item a scout holds, where it hit, along the way it flew: shown so on every game.</summary>
    internal static void StickInItem(Item item, Vector3 point, Vector3 way)
    {
        PhotonView view = item != null ? item.GetComponent<PhotonView>() : null;
        if (view == null || way.sqrMagnitude < 1e-6f)
        {
            return;
        }
        Net.SendAll(ItemMessage, view.ViewID, item.transform.InverseTransformPoint(point), item.transform.InverseTransformDirection(way.normalized));
    }

    /// <summary>The needle in the item: its tip (its +z end) just inside where it hit, the rest standing out the way it came from.</summary>
    private static void ShowInItem(Item item, Vector3 localAt, Vector3 localWay)
    {
        Vector3 way = item.transform.TransformDirection(localWay).normalized;
        Vector3 at = item.transform.TransformPoint(localAt);
        GameObject needle = new GameObject("UltimateAscent Spruce Needle");
        needle.transform.SetParent(item.transform, false);
        needle.transform.SetPositionAndRotation(at - way * (Length * 0.5f - TipIn), Quaternion.LookRotation(way));
        Vector3 parentScale = item.transform.lossyScale;
        needle.transform.localScale = new Vector3(Scale.x / Mathf.Max(parentScale.x, 1e-4f), Scale.y / Mathf.Max(parentScale.y, 1e-4f), Scale.z / Mathf.Max(parentScale.z, 1e-4f));
        needle.AddComponent<MeshFilter>().sharedMesh = SpruceLook.Needle;
        needle.AddComponent<MeshRenderer>().sharedMaterial = SpruceLook.Materials[SpruceLook.Green];
        foreach (Item gone in inItems.Keys.Where(i => i == null).ToList())
        {
            inItems.Remove(gone);
        }
        if (!inItems.TryGetValue(item, out Queue<GameObject> stuck))
        {
            inItems[item] = stuck = new Queue<GameObject>();
        }
        stuck.Enqueue(needle);
        while (stuck.Count > MostInItem)
        {
            GameObject oldest = stuck.Dequeue();
            if (oldest != null)
            {
                Object.Destroy(oldest);
            }
        }
    }

    /// <summary>On the scout's own game: a needle driven in where it hit (the nearest free cactus thorn), shown as a needle everywhere. False if none was free.</summary>
    internal static bool StickIn(Character scout, Vector3 at)
    {
        CharacterAfflictions afflictions = scout.refs.afflictions;
        HashSet<ThornOnMe> before = new HashSet<ThornOnMe>(afflictions.physicalThorns.Where(t => t != null && t.type == CactusThorn && t.stuckIn));
        afflictions.AddThorn(at, CactusThorn);
        int index = afflictions.physicalThorns.FindIndex(t => t != null && t.type == CactusThorn && t.stuckIn && !before.Contains(t));
        if (index < 0)
        {
            return false;
        }
        Stuck++;
        Net.SendAll(Message, scout.photonView.ViewID, index);
        return true;
    }

    /// <summary>
    /// A thorn shown as a needle: its cactus spine hidden and a needle in its place, its tip in the body where the thorn
    /// sits and the rest standing out along the spine's line, away from the bone the thorn is on.
    /// </summary>
    private static void Show(ThornOnMe thorn)
    {
        if (needles.ContainsKey(thorn))
        {
            return;
        }
        MeshFilter filter = thorn.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null && f.sharedMesh.name.StartsWith(CactusSpine, System.StringComparison.Ordinal));
        if (filter == null || !filter.TryGetComponent(out MeshRenderer renderer))
        {
            return;
        }
        float size = Compat.Tfa.SizeOf(thorn.character);
        Vector3 at = thorn.transform.position;
        Vector3 line = filter.transform.forward;
        Transform bone = thorn.transform.parent;
        Vector3 fromBone = bone != null ? at - bone.position : line;
        Vector3 outward = Vector3.Dot(line, fromBone) >= 0f ? line : -line;
        // Where the body's surface is along that line, from outside (the thorn's own spot may lie off it or in it).
        Vector3 surface = at;
        float nearest = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(at + outward * (0.6f * size), -outward, 1.2f * size, LayerMask.GetMask("Character"), QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<Character>() == thorn.character && hit.distance < nearest)
            {
                nearest = hit.distance;
                surface = hit.point;
            }
        }
        GameObject needle = new GameObject("UltimateAscent Spruce Needle");
        needle.transform.SetParent(thorn.transform, false);
        // The needle's tip (its +z end) just under the surface, the rest standing out.
        needle.transform.SetPositionAndRotation(surface + outward * ((Length * 0.5f - TipIn) * size), Quaternion.LookRotation(-outward));
        Vector3 parentScale = thorn.transform.lossyScale;
        needle.transform.localScale = new Vector3(Scale.x * size / Mathf.Max(parentScale.x, 1e-4f), Scale.y * size / Mathf.Max(parentScale.y, 1e-4f), Scale.z * size / Mathf.Max(parentScale.z, 1e-4f));
        needle.AddComponent<MeshFilter>().sharedMesh = SpruceLook.Needle;
        needle.AddComponent<MeshRenderer>().sharedMaterial = SpruceLook.Materials[SpruceLook.Green];
        renderer.enabled = false;
        needles[thorn] = new Spine { renderer = renderer, needle = needle };
    }

    /// <summary>For the tests: the needle a thorn shows, if it shows one.</summary>
    internal static GameObject NeedleOf(ThornOnMe thorn) => thorn != null && needles.TryGetValue(thorn, out Spine spine) ? spine.needle : null;

    private static void Restore(ThornOnMe thorn)
    {
        if (thorn == null || !needles.TryGetValue(thorn, out Spine spine))
        {
            return;
        }
        needles.Remove(thorn);
        if (spine.needle != null)
        {
            Object.Destroy(spine.needle);
        }
        if (spine.renderer != null)
        {
            spine.renderer.enabled = true;
        }
    }

    /// <summary>A thorn that comes out (by hand, by itself, a cure) looks like a cactus thorn again.</summary>
    [HarmonyPatch(typeof(ThornOnMe), nameof(ThornOnMe.DisableThorn))]
    private static class BackToCactus
    {
        private static void Postfix(ThornOnMe __instance)
        {
            Restore(__instance);
        }
    }
}
