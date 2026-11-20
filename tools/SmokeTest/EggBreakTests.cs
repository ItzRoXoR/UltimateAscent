using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>Eggs breaking: in the map's own nests and in the mod's new ones ("eggbreak").</summary>
internal sealed partial class Runner
{
    /// <summary>The breaks the game tells every client of: what each one left as pieces (its non-item objects).</summary>
    [HarmonyPatch(typeof(Breakable), "RPC_BroadcastBreak")]
    internal static class BreakRecorder
    {
        internal static readonly List<(string item, Vector3 at, int pieces)> Breaks = new List<(string, Vector3, int)>();

        private static void Postfix(Breakable __instance)
        {
            Breaks.Add((__instance.gameObject.name, __instance.transform.position, __instance.instantiateNonItemOnBreak?.Count ?? 0));
        }
    }

    private static string Names(IEnumerable<GameObject> list) => list == null ? "-" : "[" + string.Join(", ", list.Select(g => g != null ? g.name : "null")) + "]";

    /// <summary>
    /// The egg items' break as the game sets it up (what they leave, items and pieces); then, in the Caldera or the Gloom,
    /// an egg of one of the mod's new nests and one of the map's own nests, each dropped onto the ground from 3 m: each
    /// breaks, leaving the game's yolk and its shell pieces, pictured.
    /// </summary>
    private IEnumerator EggBreakCheck()
    {
        foreach (Item egg in SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.Where(i => i != null && i.GetComponent<Breakable>() != null && i.gameObject.name.ToLowerInvariant().Contains("egg")))
        {
            Breakable b = egg.GetComponent<Breakable>();
            Info($"the break of {egg.gameObject.name} ({b.GetType().Name})",
                $"on collision {b.breakOnCollision} above {b.minBreakVelocity} m/s; items {Names(b.instantiateOnBreak)} at {b.instantiatePoints?.Count ?? 0} points (kinematic {b.spawnsItemsKinematic}); "
                + $"alternate {b.alternateChance:0.##}: {Names(b.alternateInstantiateOnBreak)}; pieces (not items) {Names(b.instantiateNonItemOnBreak)}; sounds {b.breakSFX?.Count ?? 0}; "
                + $"animation {(b.playAnimationOnInstantiatedObject ? b.animString : "none")}");
        }
        if (!(Map.InVolcanoCaldera || Map.InGloom))
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(10f);
        }
        List<Item> eggs = Object.FindObjectsByType<Item>(FindObjectsSortMode.None).Where(e => e.gameObject.name.StartsWith("NestEgg") && e.itemState == ItemState.Ground).ToList();
        HashSet<BerryBush> added = new HashSet<BerryBush>(MoreNests.Reports.SelectMany(r => r.added));
        List<BerryBush> nests = Object.FindObjectsByType<BerryBush>(FindObjectsSortMode.None).Where(MoreNests.IsNest).ToList();
        BerryBush NestOf(Item egg) => nests.OrderBy(n => (n.transform.position - egg.transform.position).sqrMagnitude).FirstOrDefault();
        Item newEgg = eggs.FirstOrDefault(e => NestOf(e) is BerryBush n && added.Contains(n) && (n.transform.position - e.transform.position).magnitude < 1.5f);
        Item oldEgg = eggs.FirstOrDefault(e => NestOf(e) is BerryBush n && !added.Contains(n) && (n.transform.position - e.transform.position).magnitude < 1.5f);
        Info("eggs in the nests", $"{eggs.Count} eggs; one in a new nest {newEgg != null}, one in a map's own nest {oldEgg != null}");
        Character local = Local;
        Protect(true);
        foreach ((Item egg, string which) in new[] { (newEgg, "new"), (oldEgg, "own") })
        {
            if (egg == null)
            {
                Fail($"an egg in one of the {(which == "new" ? "mod's new" : "map's own")} nests, to break");
                continue;
            }
            Vector3 at = egg.transform.position;
            Breakable breakable = egg.GetComponent<Breakable>();
            Info($"the egg in a {which} nest", $"{egg.gameObject.name} at {at:F1}; kinematic {egg.rig.isKinematic}, state {egg.itemState}, mine {egg.photonView.IsMine}, pieces {Names(breakable.instantiateNonItemOnBreak)}");
            // The scout a few metres off, looking at where it will land.
            Vector3 drop = at + Vector3.up * 3f;
            Vector3 stand = at + new Vector3(3f, 0f, 0f);
            if (Physics.Raycast(stand + Vector3.up * 6f, Vector3.down, out RaycastHit ground, 15f, HelperFunctions.terrainMapMask))
            {
                stand = ground.point;
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand + Vector3.up * 1.2f, false);
            for (float until = Time.time + 2f; Time.time < until;)
            {
                LookAtPoint(local, at);
                yield return null;
            }
            int breaks = BreakRecorder.Breaks.Count;
            HashSet<int> before = new HashSet<int>(Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Select(g => g.GetInstanceID()));
            egg.SetKinematic(false);
            egg.transform.position = drop;
            egg.rig.position = drop;
            egg.rig.linearVelocity = Vector3.down * 12f;
            float deadline = Time.time + 5f;
            while (egg != null && Time.time < deadline)
            {
                LookAtPoint(local, at);
                yield return null;
            }
            for (float until = Time.time + 0.4f; Time.time < until;)
            {
                LookAtPoint(local, at);
                yield return null;
            }
            Screenshot($"egg-broken-{which}");
            List<GameObject> fresh = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                .Where(g => !before.Contains(g.GetInstanceID()) && g.transform.parent == null && (g.transform.position - at).magnitude < 6f).ToList();
            bool broke = egg == null && BreakRecorder.Breaks.Count > breaks;
            List<string> freshItems = fresh.Where(g => g.GetComponent<Item>() != null).Select(g => g.name).ToList();
            List<string> freshPieces = fresh.Where(g => g.GetComponent<Item>() == null).Select(g => g.name).ToList();
            Check($"an egg of a {which} nest dropped from 3 m breaks, leaving the game's items and its shell pieces",
                broke && freshPieces.Count > 0,
                $"broke {broke}; items left {string.Join(", ", freshItems)}; pieces left {string.Join(", ", freshPieces)}");
            yield return new WaitForSeconds(1f);
        }
    }
}
