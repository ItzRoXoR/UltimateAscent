using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;

namespace UltimateAscent.SmokeTest;

/// <summary>The Gloom's food: banana vines 40%, ten cattail Hot Dogs (every ascent).</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The climb goes on to the Gloom (a map with it: -Biome MS), as the game loads it. Its banana vines had a 40% chance
    /// (the game: 10%); there are 10 cattail Hot Dogs lying about (the game's spawners plus the added ones), every added
    /// one on the swamp floor within a metre of the game's heights, 20 m or more from any other. The scout is shown one.
    /// </summary>
    private IEnumerator GloomFoodCheck()
    {
        if (!(Map.InVolcanoCaldera || Map.InGloom))
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(10f);
        }
        if (!Map.InGloom)
        {
            Info("the Gloom's food checks need the Gloom", "run with -Biome MS");
            yield break;
        }
        yield return new WaitForSeconds(1f);
        List<BerryVine> vines = Object.FindObjectsByType<BerryVine>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(v => GloomFood.IsBananaVine(v) && Map.TryGetBiome(v, out Biome.BiomeType b) && b == Biome.BiomeType.Swamp).ToList();
        List<Item> bananas = Object.FindObjectsByType<Item>(FindObjectsSortMode.None).Where(i => i.itemState == ItemState.Ground && i.gameObject.name.StartsWith("Berrynana")).ToList();
        int bearing = vines.Count(v => bananas.Any(b => Vector3.Distance(b.transform.position, v.transform.position) < 12f));
        Check("the Gloom's banana vines bear fruit 40% of the time (the game: 10%)", vines.Count > 0 && vines.All(v => v.baseSpawnChance >= GloomFood.BananaVineChance - 0.001f),
            $"{vines.Count} vines, chances {string.Join(", ", vines.Select(v => v.baseSpawnChance.ToString("0.##")).Distinct())}; {bearing} bore fruit this time, {bananas.Count} bananas ({string.Join(", ", bananas.GroupBy(b => b.gameObject.name.Replace("(Clone)", "")).Select(g => $"{g.Count()} {g.Key}"))})");

        List<Item> hotDogs = Object.FindObjectsByType<Item>(FindObjectsSortMode.None).Where(i => i.gameObject.name.StartsWith("Glizzy_CattailVariant")).ToList();
        Check("the Gloom has 10 cattail Hot Dogs", hotDogs.Count == GloomFood.HotDogs || GloomFood.GameHotDogs >= GloomFood.HotDogs && GloomFood.Added.Count == 0,
            $"{hotDogs.Count} lying about: {GloomFood.GameHotDogs} from the game's spawners, {GloomFood.Added.Count} added");
        List<SingleItemSpawner> spawners = Object.FindObjectsByType<SingleItemSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(GloomFood.IsCattailHotDog).ToList();
        if (spawners.Count > 0 && GloomFood.Added.Count > 0)
        {
            float low = spawners.Min(s => s.transform.position.y) - 1f;
            float high = spawners.Max(s => s.transform.position.y) + 1.2f;
            float closest = GloomFood.Added.Where(a => a != null)
                .Select(a => hotDogs.Where(o => o != a).Select(o => new Vector2(o.transform.position.x - a.transform.position.x, o.transform.position.z - a.transform.position.z).magnitude).DefaultIfEmpty(float.MaxValue).Min())
                .DefaultIfEmpty(float.MaxValue).Min();
            bool heights = GloomFood.Added.All(a => a != null && a.transform.position.y >= low && a.transform.position.y <= high);
            Check("the added Hot Dogs stand on the swamp floor at the game's heights, 20 m or more from any other", heights && closest >= GloomFood.HotDogsApart - 0.5f,
                $"heights {GloomFood.Added.Min(a => a.transform.position.y):F1}-{GloomFood.Added.Max(a => a.transform.position.y):F1} m (the game's {low + 1f:F1}-{high - 1.2f:F1}), the closest {closest:F1} m from another");

            Item shown = GloomFood.Added.First(a => a != null);
            Character local = Local;
            Vector3 home = local.Center;
            Vector3 at = shown.transform.position;
            Vector3 away = at + new Vector3(home.x - at.x, 0f, home.z - at.z).normalized * 3f;
            if (Physics.Raycast(away + Vector3.up * 6f, Vector3.down, out RaycastHit ground, 15f, HelperFunctions.terrainMapMask))
            {
                away = ground.point;
            }
            Protect(true);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, away + Vector3.up * 1.2f, false);
            for (float until = Time.time + 3f; Time.time < until;)
            {
                LookAtPoint(local, at + Vector3.up * 0.4f);
                yield return null;
            }
            Screenshot("gloom-hotdog");
            yield return new WaitForSeconds(1f);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
            yield return new WaitForSeconds(1f);
        }
    }
}
