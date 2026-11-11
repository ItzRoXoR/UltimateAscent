using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;

namespace UltimateAscent.SmokeTest;

/// <summary>More egg nests in the Caldera and the Gloom (every ascent).</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The climb goes on to the Caldera or the Gloom (whichever the map has), as the game loads it. Then: 2-3 times as many
    /// nests as the map has there (as many as there are places for); every new one near a hill's top at the allowed
    /// heights (the Caldera: 18-56 m above the lava; the Gloom: 2-46 m above its sleepy fog), on rock or the biome's own
    /// ground, apart from every other nest, not within 30 m of a condor's nest nor 25 m of a campfire; a copy of one of the
    /// biome's own nests, filled by the game like its template (its eggs lie in it); the map's own nests all still there;
    /// the places in the room for the other players. The scout is shown a new nest with eggs in it.
    /// </summary>
    private IEnumerator MoreNestsCheck()
    {
        // Loaded once only: loading a segment again would have the game fill its nests again.
        if (!(Map.InVolcanoCaldera || Map.InGloom))
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(10f);
        }
        if (MoreNests.Reports.Count == 0)
        {
            Check("the Caldera's or the Gloom's new nests are placed as it loads", false, "no report: no nest was filled");
            yield break;
        }
        foreach (MoreNests.Report report in MoreNests.Reports)
        {
            string biome = report.biome == Biome.BiomeType.Volcano ? "Caldera" : "Gloom";
            int added = report.added.Count;
            if (report.biome == Biome.BiomeType.Volcano)
            {
                Check("the Caldera wants 2-3 times as many nests as the map has there", report.wanted >= report.before * 2 && report.wanted <= report.before * 3,
                    $"{report.before} nests on the map, {report.wanted} wanted");
            }
            else
            {
                Check("the Gloom wants 14-17 more nests", report.wanted - report.before >= MoreNests.GloomLeast && report.wanted - report.before <= MoreNests.GloomMost,
                    $"{report.before} nests on the map, {report.wanted} wanted");
            }
            Info($"the {biome}'s nests the game filled (the templates)", $"{report.filled} of {report.before}");
            Check($"the {biome}'s new nests copy only nests holding eggs", report.templates.All(t => Object.FindObjectsByType<Item>(FindObjectsSortMode.None).Any(e => e.gameObject.name.StartsWith("NestEgg") && (e.transform.position - t.transform.position).magnitude < 1.5f)),
                $"{report.templates.Distinct().Count()} different templates");
            Check($"the {biome} gets the new nests wanted, or as many as there are places for", report.before + added == report.wanted || added > 0 && added < report.wanted - report.before,
                $"{added} new nests ({report.wanted - report.before} wanted) on {report.spots} spots found");
            Info($"the {biome}'s nests", $"{report.before} -> {report.before + added} ({(report.before + added) / (float)report.before:0.00}x)");
            List<Vector3> all = Object.FindObjectsByType<BerryBush>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(MoreNests.IsNest).Select(n => n.transform.position).ToList();
            List<string> bad = new List<string>();
            float closest = float.MaxValue;
            Vector3[] condorNests = Object.FindObjectsByType<Condor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.nest != null).Select(c => c.nest.transform.position).ToArray();
            Vector3[] campfires = Object.FindObjectsByType<Campfire>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(c => c.transform.position).ToArray();
            float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
            foreach (BerryBush nest in report.added)
            {
                Vector3 p = nest.transform.position;
                float near = all.Where(o => (o - p).sqrMagnitude > 0.01f).Select(o => Flat(o, p)).DefaultIfEmpty(float.MaxValue).Min();
                closest = Mathf.Min(closest, near);
                if (p.y < report.low - 0.01f || p.y > report.high + 0.01f)
                {
                    bad.Add($"{p:F0} at {p.y:F1} m");
                }
                if (condorNests.Any(c => Flat(c, p) < MoreNests.CondorNestClearance - 0.01f) || campfires.Any(c => Flat(c, p) < MoreNests.CampfireClearance - 0.01f))
                {
                    bad.Add($"{p:F0} by a condor's nest or a campfire");
                }
            }
            Check($"the {biome}'s new nests sit at {report.low:F0}-{report.high:F0} m, {report.apart:F0} m or more from any other nest, away from condor nests and campfires",
                added > 0 && bad.Count == 0 && closest >= report.apart - 0.01f,
                bad.Count > 0 ? string.Join("; ", bad.Take(6)) : $"heights {report.added.Min(n => n.transform.position.y):F1}-{report.added.Max(n => n.transform.position.y):F1} m, the closest {closest:F1} m from another nest");
            int wallLayer = LayerMask.NameToLayer("InvisWall");
            List<Bounds> walls = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.gameObject.layer == wallLayer).Select(c => c.bounds).ToList();
            float left = walls.Count > 0 ? walls.Min(b => b.min.x) : float.MinValue;
            float right = walls.Count > 0 ? walls.Max(b => b.max.x) : float.MaxValue;
            List<BerryBush> own = Object.FindObjectsByType<BerryBush>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(n => MoreNests.IsNest(n) && !MoreNests.IsAdded(n)).ToList();
            Check($"the {biome}'s new nests are on the climb, within its edge walls", report.added.All(n => n.transform.position.x >= left && n.transform.position.x <= right),
                $"new nests x {report.added.Min(n => n.transform.position.x):F0} to {report.added.Max(n => n.transform.position.x):F0}, the map's x {own.Min(n => n.transform.position.x):F0} to {own.Max(n => n.transform.position.x):F0}, the walls' x {left:F0} to {right:F0}");
            Check($"the {biome}'s map nests are all still there", MoreNests.Reports.Count > 0 && all.Count == report.before + added + MoreNests.Reports.Where(r => r != report).Sum(r => r.before + r.added.Count),
                $"{all.Count} nests in all");
            // The eggs: each new nest is filled like its template (how many it may hold, how often).
            List<Item> eggs = Object.FindObjectsByType<Item>(FindObjectsSortMode.None).Where(i => i.gameObject.name.StartsWith("NestEgg")).ToList();
            int Inside(Vector3 at) => eggs.Count(e => (e.transform.position - at).magnitude < 1.5f);
            List<int> counts = report.added.Select(n => Inside(n.transform.position)).ToList();
            bool kinds = true;
            for (int i = 0; i < report.added.Count; i++)
            {
                BerryBush nest = report.added[i];
                BerryBush template = report.templates[i];
                kinds &= nest.spawnedObjectPrefab == template.spawnedObjectPrefab && nest.possibleBerries.y == Mathf.Max(1f, template.possibleBerries.y) && nest.possibleBerries.x >= 1f && Mathf.Approximately(nest.baseSpawnChance, 1f)
                    && nest.spawnSpots.Count == template.spawnSpots.Count && counts[i] <= nest.spawnSpots.Count;
            }
            Check($"each new {biome} nest is a copy of one of the map's, always filled (at least one egg, as many as its template may hold)", kinds && counts.All(c => c >= 1),
                $"eggs per new nest: {string.Join(" ", counts)} (templates hold {string.Join(", ", report.templates.Select(t => $"{t.possibleBerries.x:0}-{t.possibleBerries.y:0} at {t.baseSpawnChance:0.##}").Distinct())})");
            for (int i = 0; i < report.added.Count; i++)
            {
                if (counts[i] == 0)
                {
                    Vector3 at = report.added[i].transform.position;
                    Item nearest = eggs.OrderBy(e => (e.transform.position - at).sqrMagnitude).FirstOrDefault();
                    Info($"  an empty new {biome} nest", $"at {at:F1}, chance {report.added[i].baseSpawnChance:0.##}, tried {report.added[i]._hasTriedToSpawn}, spawned {report.added[i]._hasSuccessfullySpawned}; the nearest egg {(nearest != null ? $"{(nearest.transform.position - at).magnitude:F1} m away at {nearest.transform.position:F1}, kinematic {nearest.rig.isKinematic}" : "none")}");
                }
            }
            List<string> sizes = new List<string>();
            for (int i = 0; i < report.added.Count; i++)
            {
                float mine = report.added[i].transform.lossyScale.x;
                float theirs = report.templates[i].transform.lossyScale.x;
                if (Mathf.Abs(mine - theirs) > 0.01f * Mathf.Max(1f, theirs))
                {
                    sizes.Add($"{mine:0.###} for {theirs:0.###}");
                }
            }
            Check($"the new {biome} nests are as big as the ones they copy", sizes.Count == 0,
                sizes.Count > 0 ? string.Join(", ", sizes) : $"sizes {string.Join(", ", report.templates.Select(t => t.transform.lossyScale.x.ToString("0.###")).Distinct())}");
            Check($"the new {biome} nests look like nests", report.added.All(n => n.GetComponentInChildren<MeshRenderer>(true) != null && n.GetComponentInChildren<Collider>(true) != null),
                string.Join(", ", report.added.Take(3).Select(n => string.Join("/", n.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.name)))));
        }
        string room = Net.GetRoom(MoreNests.RoomKey) as string;
        int inRoom = room == null ? 0 : room.Split(';').Length - 1;
        Check("the new nests' places are in the room for the other players", inRoom == MoreNests.Reports.Sum(r => r.added.Count), $"{inRoom} places in the room");

        // A look at one.
        BerryBush shown = MoreNests.Reports.SelectMany(r => r.added).OrderByDescending(n => Object.FindObjectsByType<Item>(FindObjectsSortMode.None).Count(e => e.gameObject.name.StartsWith("NestEgg") && (e.transform.position - n.transform.position).magnitude < 1.5f)).FirstOrDefault();
        if (shown != null)
        {
            Character local = Local;
            Vector3 home = local.Center;
            Vector3 at = shown.transform.position;
            Vector3 away = at + (new Vector3(home.x - at.x, 0f, home.z - at.z).normalized + Vector3.zero) * 3f;
            if (Physics.Raycast(away + Vector3.up * 6f, Vector3.down, out RaycastHit ground, 15f, HelperFunctions.terrainMapMask))
            {
                away = ground.point;
            }
            Protect(true);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, away + Vector3.up * 1.2f, false);
            for (float until = Time.time + 3f; Time.time < until;)
            {
                LookAtPoint(local, at);
                yield return null;
            }
            Screenshot("new-nest");
            yield return new WaitForSeconds(1f);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
            yield return new WaitForSeconds(1f);
        }
    }
}
