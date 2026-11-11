using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>The Caldera's and the Gloom's egg nests, read from the map, and their ground sampled (for new nests).</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// Every spawner that fills from the Nest pool (where it is, its settings, its spots, what it is part of), the Nest pool's
    /// items, the condors and their nests, the eggs lying about, every object named like a nest, the lava and the Gloom's fog;
    /// then the ground of the Caldera's or the Gloom's biome sampled every 2 m from above (the terrain's height and what
    /// the first thing from above is), written to UltimateAscent.SmokeTest.nests-(biome).tsv.
    /// </summary>
    private IEnumerator NestProbe()
    {
        // Loaded once only: loading a segment again would have the game fill its nests again.
        if (!(Map.InVolcanoCaldera || Map.InGloom))
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(10f);
        }
        if (LootData.AllSpawnWeightData != null && LootData.AllSpawnWeightData.TryGetValue(SpawnPool.Nest, out Dictionary<ushort, int> pool))
        {
            Info("the Nest pool", string.Join(", ", pool.Select(p => $"{(ItemDatabase.TryGetItem(p.Key, out Item item) ? item.gameObject.name : p.Key.ToString())} {p.Value}")));
        }
        foreach (Item item in LootData.GetAllItemsInPool(SpawnPool.Nest).Where(i => i != null))
        {
            LootData loot = item.GetComponent<LootData>();
            Info($"in the Nest pool: {item.gameObject.name}", $"id {item.itemID}, pools {(loot != null ? loot.spawnLocations.ToString() : "-")}, rarity {(loot != null ? loot.Rarity.ToString() : "-")}");
        }

        List<Spawner> spawners = Resources.FindObjectsOfTypeAll<Spawner>().Where(s => s != null && s.gameObject.scene.IsValid()).ToList();
        Info("spawners on the map", string.Join(", ", spawners.GroupBy(s => s.GetSpawnPool()).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        List<Spawner> nests = spawners.Where(s => s.GetSpawnPool().HasFlag(SpawnPool.Nest) || (s.heightBasedSpawnPools != null && s.heightBasedSpawnPools.Any(h => h.spawnPool.HasFlag(SpawnPool.Nest)))).ToList();
        Info("nest spawners", $"{nests.Count} on this map");
        foreach (Spawner nest in nests)
        {
            string biome = Map.TryGetBiome(nest, out Biome.BiomeType b) ? b.ToString() : "no biome";
            Transform root = nest.transform.parent != null ? nest.transform.parent : nest.transform;
            Info($"nest: {HierarchyPath(nest.transform)}", $"{nest.GetType().Name} at {nest.transform.position:F1}, {biome}, active {nest.gameObject.activeInHierarchy}; {Settings(nest)}; "
                + $"spots {string.Join(" ", (nest.spawnSpots ?? new List<Transform>()).Where(t => t != null).Select(t => t.position.ToString("F1")))}; "
                + $"part of {root.name} ({string.Join(", ", root.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct().Take(25))}); "
                + $"meshes {string.Join(", ", root.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null).Select(m => m.name + ":" + m.sharedMesh.name).Distinct().Take(12))}");
            Info($"  ground under it", Ground(nest.transform.position));
        }

        foreach (Condor condor in Resources.FindObjectsOfTypeAll<Condor>().Where(c => c != null && c.gameObject.scene.IsValid()))
        {
            Info($"condor: {HierarchyPath(condor.transform)}", $"at {condor.transform.position:F1}, active {condor.gameObject.activeInHierarchy}, nest {(condor.nest != null ? HierarchyPath(condor.nest.transform) + " at " + condor.nest.transform.position.ToString("F1") : "none")}");
        }
        foreach (Item item in Resources.FindObjectsOfTypeAll<Item>().Where(i => i != null && i.gameObject.scene.IsValid() && i.gameObject.name.ToLowerInvariant().Contains("egg")))
        {
            Info($"egg: {item.gameObject.name}", $"at {item.transform.position:F1}, state {item.itemState}, active {item.gameObject.activeInHierarchy}");
        }
        List<Transform> named = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t != null && t.gameObject.scene.IsValid() && t.name.ToLowerInvariant().Contains("nest")).ToList();
        Info("objects named like a nest", $"{named.Count}");
        foreach (Transform t in named.Take(80))
        {
            Info($"  {HierarchyPath(t)}", $"at {t.position:F1}, active {t.gameObject.activeInHierarchy}, scripts {string.Join(", ", t.GetComponents<Component>().Select(c => c.GetType().Name))}");
        }
        foreach (Lava lava in Resources.FindObjectsOfTypeAll<Lava>().Where(l => l != null && l.gameObject.scene.IsValid()))
        {
            Renderer plane = lava.GetComponentInChildren<MeshRenderer>(true);
            Info($"lava: {HierarchyPath(lava.transform)}", $"at {lava.transform.position:F1}, active {lava.gameObject.activeInHierarchy}, covers {(plane != null ? plane.bounds.min.ToString("F0") + " to " + plane.bounds.max.ToString("F0") : "nothing")}");
        }
        foreach (global::Peak.StatusFieldGloom field in Resources.FindObjectsOfTypeAll<global::Peak.StatusFieldGloom>().Where(f => f != null && f.gameObject.scene.IsValid()))
        {
            Collider c = field.GetComponent<Collider>();
            Info($"gloom field: {HierarchyPath(field.transform)}", $"at {field.transform.position:F1}, active {field.gameObject.activeInHierarchy}, bounds {(c != null ? c.bounds.min.ToString("F0") + " to " + c.bounds.max.ToString("F0") : "none")}, rising {field.GetComponentInParent<LavaRising>(true) != null}");
        }

        // The biome's ground, from above.
        foreach (Biome biome in Resources.FindObjectsOfTypeAll<Biome>().Where(x => x != null && x.gameObject.scene.IsValid() && (x.biomeType == Biome.BiomeType.Volcano || x.biomeType == Biome.BiomeType.Swamp)))
        {
            Collider[] colliders = biome.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
            if (colliders.Length == 0)
            {
                continue;
            }
            Bounds bounds = colliders[0].bounds;
            foreach (Collider c in colliders)
            {
                bounds.Encapsulate(c.bounds);
            }
            Info($"biome {biome.biomeType}: {HierarchyPath(biome.transform)}", $"active {biome.gameObject.activeInHierarchy}, ground from {bounds.min:F0} to {bounds.max:F0}");
            const float Step = 2f;
            StringBuilder text = new StringBuilder("x\tz\tterrain\tterrainHit\tterrainLayer\ttop\ttopHit\ttopLayer\ttopLava\tnormalY\n");
            CultureInfo plain = CultureInfo.InvariantCulture;
            int rows = 0;
            float from = bounds.max.y + 30f;
            float length = bounds.size.y + 80f;
            for (float x = bounds.min.x; x <= bounds.max.x; x += Step)
            {
                for (float z = bounds.min.z; z <= bounds.max.z; z += Step)
                {
                    Vector3 start = new Vector3(x, from, z);
                    bool terrain = Physics.Raycast(start, Vector3.down, out RaycastHit t, length, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore);
                    bool top = Physics.Raycast(start, Vector3.down, out RaycastHit a, length, ~0, QueryTriggerInteraction.Collide);
                    if (!terrain && !top)
                    {
                        continue;
                    }
                    bool lava = top && a.collider.GetComponentInParent<Lava>() != null;
                    text.Append(x.ToString("F1", plain)).Append('\t').Append(z.ToString("F1", plain)).Append('\t')
                        .Append(terrain ? t.point.y.ToString("F2", plain) : "").Append('\t').Append(terrain ? t.collider.name : "").Append('\t').Append(terrain ? LayerMask.LayerToName(t.collider.gameObject.layer) : "").Append('\t')
                        .Append(top ? a.point.y.ToString("F2", plain) : "").Append('\t').Append(top ? a.collider.name : "").Append('\t').Append(top ? LayerMask.LayerToName(a.collider.gameObject.layer) : "").Append('\t')
                        .Append(lava ? "1" : "0").Append('\t').Append(terrain ? t.normal.y.ToString("F3", plain) : "").Append('\n');
                    rows++;
                }
                if (rows % 4000 == 0)
                {
                    yield return null;
                }
            }
            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, $"UltimateAscent.SmokeTest.nests-{biome.biomeType}-{biome.name}.tsv");
            File.WriteAllText(path, text.ToString());
            Info($"the {biome.biomeType} ground sampled", $"{rows} points every {Step} m, written to {Path.GetFileName(path)}");
        }
    }

    /// <summary>What lies under a point: the terrain's height and what the first thing from above is.</summary>
    private static string Ground(Vector3 at)
    {
        Vector3 start = at + Vector3.up * 30f;
        string terrain = Physics.Raycast(start, Vector3.down, out RaycastHit t, 200f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)
            ? $"terrain {t.point.y:F1} ({t.collider.name}, {LayerMask.LayerToName(t.collider.gameObject.layer)}, slope {Vector3.Angle(t.normal, Vector3.up):F0})" : "no terrain";
        string top = Physics.Raycast(start, Vector3.down, out RaycastHit a, 200f, ~0, QueryTriggerInteraction.Ignore)
            ? $"first from above {a.point.y:F1} ({a.collider.name}, {LayerMask.LayerToName(a.collider.gameObject.layer)})" : "nothing";
        return terrain + "; " + top;
    }
}
