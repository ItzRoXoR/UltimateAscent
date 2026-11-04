using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the new creatures on every player's game, and of the ground snakes' yellow skin.</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// In the airport, before any climb (as on a guest's game, which never spawns them itself): the snakes', the goats' and
    /// the crabs' network templates are in the game's prefab pool, and a copy of each is made from it by its name, the
    /// way the network makes the host's creatures on the other games.
    /// </summary>
    private IEnumerator MobTemplatesCheck()
    {
        DefaultPool pool = ModItems.GamePool();
        List<string> results = new List<string>();
        bool all = pool != null;
        foreach ((string name, System.Type part) in new[]
                 {
                     (Snakes.PrefabName, typeof(Snake)),
                     (Goats.PrefabName, typeof(Goat)),
                     ("0_Items/" + Crabs.PrefabName, typeof(BeachCrab)),
                 })
        {
            bool cached = pool != null && pool.ResourceCache.ContainsKey(name);
            GameObject made = null;
            try
            {
                // As the network does: from the pool by its name, left inactive until it has been set up.
                made = cached ? PhotonNetwork.PrefabPool.Instantiate(name, new Vector3(0f, -500f, 0f), Quaternion.identity) : null;
                bool right = made != null && !made.activeSelf && made.GetComponent(part) != null && made.GetComponent<PhotonView>() != null;
                all &= right;
                results.Add($"{name}: in the pool {cached}, a copy made {made != null} (with its {part.Name} and PhotonView {right})");
            }
            finally
            {
                if (made != null)
                {
                    Destroy(made);
                }
            }
        }
        Check("before any climb (as on a guest's game, which never spawns them), the snakes', goats' and crabs' network templates are there, and a copy is made from each by its name",
            all && Rules.ActiveScene == "Airport", $"in {Rules.ActiveScene}; " + string.Join("; ", results));
        yield break;
    }

    /// <summary>
    /// A ground snake up close in the Tropics: its skin is yellow (the back's colour), the tree snakes' still green;
    /// pictured from beside it, held still where it is.
    /// </summary>
    private IEnumerator SnakeColourCheck()
    {
        if (!Snakes.On)
        {
            Info("the snakes' colour check is skipped", "no snakes in this climb");
            yield break;
        }
        Character local = Local;
        if (Map.CurrentPlace != Place.Tropics)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(8f);
        }
        for (float until = Time.time + 10f; Time.time < until && !Snakes.All.Any(s => s != null && !s.IsTree);)
        {
            yield return null;
        }
        List<Snake> ground = Snakes.All.Where(s => s != null && !s.IsTree).ToList();
        if (Map.CurrentPlace != Place.Tropics || ground.Count == 0)
        {
            Info("the snakes' colour check is skipped", $"no ground snakes out ({Map.CurrentPlace}); a map with the Tropics, as 80");
            yield break;
        }
        Color groundBack = BackColour(Snakes.Kind.Ground);
        Color treeBack = BackColour(Snakes.Kind.Tree);
        bool yellow = groundBack.r > 0.7f && groundBack.g > 0.7f && groundBack.b < 0.3f && Mathf.Abs(groundBack.r - groundBack.g) < 0.12f;
        bool green = treeBack.g > treeBack.r + 0.15f && treeBack.g > treeBack.b + 0.15f;
        Check("the ground snakes' skin is yellow (the colour of their backs), the tree snakes' still green", yellow && green,
            $"ground {Rgb(groundBack)}, tree {Rgb(treeBack)}");

        Protect(true);
        A17_BeeAmbush.HeldOff = true;
        Snake snake = ground.OrderBy(s => Vector3.Distance(s.Head, local.Center)).First();
        foreach (Snake other in ground.Where(s => s != snake))
        {
            other.Ignore();
        }
        try
        {
            Vector3? stand = StandNear(snake.Head, 2f, 3f, seeIt: true, firm: true);
            if (stand == null)
            {
                Info("the ground snake's picture is skipped", $"no firm ground 2-3 m from the snake at {snake.Head:F1}");
                yield break;
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand.Value + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
            // Held still, side on to the scout.
            Vector3 toScout = Vector3.ProjectOnPlane(local.Center - snake.Head, Vector3.up);
            snake.Unaware = true;
            snake.PlaceAt(snake.Head, snake.Head + Vector3.Cross(Vector3.up, toScout.normalized));
            snake.Puppet = true;
            yield return new WaitForSeconds(0.5f);
            local.data.lookValues = HelperFunctions.DirectionToLook(snake.Head - MainCamera.instance.transform.position);
            yield return new WaitForSeconds(1f);
            Screenshot("snake-yellow");
            yield return new WaitForSeconds(1f);
        }
        finally
        {
            snake.Puppet = false;
            snake.Unaware = false;
            foreach (Snake other in ground.Where(s => s != snake))
            {
                other.Unaware = false;
            }
            A17_BeeAmbush.HeldOff = false;
        }
    }

    /// <summary>
    /// The peak's hill, for choosing where its zombies go ("-Only peakprobe"): its level ground by distance from the summit
    /// (the flare boxes) and height against it, how much level room each spot has around it, this climb's zombie places, and
    /// a picture from high above.
    /// </summary>
    private IEnumerator PeakHillProbe()
    {
        Character local = Local;
        Transform summit = PeakHill.Summit();
        if (summit == null)
        {
            Fail("the summit (the game's flare boxes at the peak)");
            yield break;
        }
        if (Singleton<MapHandler>.Instance.GetCurrentSegment() < Segment.TheKiln)
        {
            MapHandler.JumpToSegment(Segment.TheKiln);
            yield return new WaitForSeconds(8f);
        }
        Vector3 onTop = summit.position + Vector3.up * 3f;
        if (Physics.Raycast(summit.position + Vector3.up * 80f, Vector3.down, out RaycastHit top, 200f, HelperFunctions.terrainMapMask))
        {
            onTop = top.point + Vector3.up * 1.5f;
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, onTop, false);
        for (float until = Time.time + 8f; Time.time < until && PeakZombies.Places.Count == 0;)
        {
            yield return null;
        }
        List<RaycastHit> ground = PeakHill.Ground(out string counts);
        Vector3 s = summit.position;
        float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        List<(Vector3 p, float slope)> all = ground.Select(h => (h.point, Vector3.Angle(h.normal, Vector3.up))).ToList();
        List<Vector3> level = all.Where(g => g.slope < 30f).Select(g => g.p).ToList();
        int Room(Vector3 at) => level.Count(o => o != at && Flat(o, at) <= 3.6f && Mathf.Abs(o.y - at.y) < 1f);
        Info("the peak's hill", $"{counts}; summit (flare boxes) at {s:F1}; ground {all.Count} spots, level {level.Count}; hill height {(all.Count > 0 ? all.Min(g => g.p.y) - s.y : 0f):0.0} to {(all.Count > 0 ? all.Max(g => g.p.y) - s.y : 0f):0.0} m against the summit");
        foreach ((float from, float to) in new[] { (0f, 10f), (10f, 20f), (20f, 30f), (30f, 40f), (40f, 60f), (60f, 90f), (90f, 200f) })
        {
            List<Vector3> band = level.Where(p => Flat(p, s) >= from && Flat(p, s) < to).ToList();
            List<(Vector3 p, float slope)> bandAll = all.Where(g => Flat(g.p, s) >= from && Flat(g.p, s) < to).ToList();
            Info($"the hill {from:0}-{to:0} m from the summit", band.Count == 0 ? $"no level ground ({bandAll.Count} steeper spots)"
                : $"{band.Count} level spots of {bandAll.Count}; height {band.Min(p => p.y) - s.y:0.0} to {band.Max(p => p.y) - s.y:0.0} m; with room all round (8 level neighbours) {band.Count(p => Room(p) >= 8)}, with little (4 or fewer) {band.Count(p => Room(p) <= 4)}");
        }
        Info("this climb's zombie places", string.Join("; ", PeakZombies.Places.Select(p => $"{Flat(p, s):0} m out, {p.y - s.y:+0.0;-0.0} m, room {Room(p)}")));
        // The statue the gems go in (the way to the Nadir): where it stands, and the hill's level ground around it.
        List<Peak.ScoutStatue> statues = Resources.FindObjectsOfTypeAll<Peak.ScoutStatue>().Where(st => st != null && st.gameObject.scene.IsValid()).ToList();
        foreach (Peak.ScoutStatue statue in statues)
        {
            Vector3 at = statue.transform.position;
            List<Vector3> near = level.Where(p => Flat(p, at) <= 15f).ToList();
            Info($"the gem statue '{statue.name}' (under {statue.transform.parent?.name})",
                $"{Flat(at, s):0} m from the summit, {at.y - s.y:+0.0;-0.0} m; active {statue.gameObject.activeInHierarchy}; the hill's level ground within 15 m: {near.Count} spots"
                + (near.Count > 0 ? $", {near.Min(p => Flat(p, at)):0.0}-{near.Max(p => Flat(p, at)):0.0} m from it, height {near.Min(p => p.y) - at.y:+0.0;-0.0} to {near.Max(p => p.y) - at.y:+0.0;-0.0} m against it, with room all round {near.Count(p => Room(p) >= 8)}" : ""));
        }
        if (statues.Count == 0)
        {
            Info("the gem statue", "none in the map");
        }
        Info("the hill's highest level ground", string.Join("; ", level.OrderByDescending(p => p.y).Take(12).Select(p => $"{Flat(p, s):0} m out, {p.y - s.y:+0.0;-0.0} m, room {Room(p)}")));

        // From high above: the hill, the summit and the zombie places marked.
        List<GameObject> marks = new List<GameObject>();
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        try
        {
            foreach (Peak.ScoutStatue statue in statues)
            {
                GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(mark.GetComponent<Collider>());
                mark.transform.position = statue.transform.position + Vector3.up * 4f;
                mark.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);
                mark.GetComponent<Renderer>().material.color = Color.blue;
                marks.Add(mark);
            }
            foreach (Vector3 place in PeakZombies.Places)
            {
                GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(mark.GetComponent<Collider>());
                mark.transform.position = place + Vector3.up * 2f;
                mark.transform.localScale = new Vector3(1.5f, 2f, 1.5f);
                mark.GetComponent<Renderer>().material.color = Color.red;
                marks.Add(mark);
            }
            Camera camera = holder.AddComponent<Camera>();
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            camera.fieldOfView = 60f;
            camera.farClipPlane = 2000f;
            holder.transform.position = s + Vector3.up * 120f;
            holder.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            yield return new WaitForSeconds(1f);
            Screenshot("peak-hill-above");
            yield return new WaitForSeconds(1f);
            holder.transform.position = s + new Vector3(0f, 40f, -90f);
            holder.transform.LookAt(s);
            yield return new WaitForSeconds(1f);
            Screenshot("peak-hill-side");
            yield return new WaitForSeconds(1f);
        }
        finally
        {
            Destroy(holder);
            marks.ForEach(Destroy);
        }
    }

    /// <summary>The colour of a snake's back on its skin (a spot clear of its markings), read back from the texture.</summary>
    private static Color BackColour(Snakes.Kind kind)
    {
        Texture2D skin = Snakes.Pattern(kind);
        RenderTexture target = RenderTexture.GetTemporary(skin.width, skin.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture before = RenderTexture.active;
        Texture2D copy = new Texture2D(skin.width, skin.height, TextureFormat.RGBA32, false, false);
        try
        {
            Graphics.Blit(skin, target);
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, skin.width, skin.height), 0, 0);
            copy.Apply();
            // Near the top of the back (v just above 0), at the start of a tile: between a saddle and a side spot.
            return copy.GetPixel(0, 1);
        }
        finally
        {
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(target);
            Destroy(copy);
            Destroy(skin);
        }
    }

    private static string Rgb(Color c) => $"({c.r:0.00}, {c.g:0.00}, {c.b:0.00})";
}
