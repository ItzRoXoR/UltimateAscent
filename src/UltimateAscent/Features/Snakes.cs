using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Snakes in the Tropics, as many as the map has scorpions, a third of them in the giant trees; on every ascent, below 17
/// half as strong and a third slower to chase (see <see cref="NewMobs"/>).
/// <list type="bullet">
/// <item>Ground snakes (about 1.4 m long, 11 cm thick) never sleep: they slither around, and once they see a scout (as near as a
/// scorpion notices one) they chase them. A bite: 10 injury, then 40 poison over 10 s.</item>
/// <item>Snakes never leave the Tropics: they stop where the Tropics' ground ends, and give up a scout who goes beyond it.</item>
/// <item>Tree snakes (about 3.6 m long, 28 cm thick) sleep curled up in a ball on a branch of the giant trees (the Tropics' 90-110 m tall ones; where there are none, they live on the ground); when a scout comes within 5 m they uncoil, raising the head towards them, jump at them and
/// chase; once the scout is 20 m away they crawl back to the foot of their tree and climb it as snakes do, winding up the
/// trunk and along the branches, back to their spot to sleep. A bite: 30 injury, then for 2 minutes the scout
/// collapses (the game's knock-down, as from the Roots' mushrooms) for 4 s every 15-30 s (another bite meanwhile starts the
/// 2 minutes over, it does not stack); a poison drop over the stamina bar drains as that time runs out, and the game's
/// poison cures end it (see <see cref="SnakeParalysis"/>).</item>
/// <item>Both rear up and jump at the scout to bite, mouth open and fangs out (the scorpion's glint on the fangs after the
/// strike, its sparks as the bite lands); for 3 s after it they do not strike, but slither round the scout 3.5 m out
/// (or after them, if further off), then go for another scout within their reach for aggression if there is one, or
/// for the same scout again. They cannot be picked up; a Stick's swing (4 s), a Torch's (2 s), a Stone thrown hard (2 s)
/// or a kick (1 s) knocks one back 1.5 m, and it curls up for that long; then it goes for the scout who stunned it (the
/// first, if more did), when they are within its reach for aggression.</item>
/// </list>
/// The host runs the snakes (a room object each); everyone else sees them through the network. Their bodies are
/// built in code: one skinned mesh, head and body, on a skeleton of 26 bones (and the lower jaw's) that lie along the head's track (so a crawling snake
/// lies in S-curves and never folds), a broad flat head with eyes and a flicking forked tongue, with the game's own
/// item shading.
/// </summary>
internal static class Snakes
{
    internal const string PrefabName = "UA_Snake";
    private const string SpawnerName = "UltimateAscent Snake Spawner";

    internal enum Kind : byte
    {
        Ground = 0,
        Tree = 1,
    }

    // Bites, as they land (injury is not scaled by any ascent; the poison lands exactly).
    internal const float GroundBiteInjury = 0.1f;
    internal const float GroundPoison = 0.4f;
    internal const float GroundPoisonSeconds = 10f;
    internal const float TreeBiteInjury = 0.3f;
    internal const float ParalysisSeconds = 120f;
    internal const float CollapseSeconds = 4f;
    internal const float CollapseEveryMin = 15f;
    internal const float CollapseEveryMax = 30f;
    internal const float FirstCollapseMin = 5f;
    internal const float FirstCollapseMax = 15f;

    // Hunting.
    internal const float TreeWakeDistance = 5f;
    /// <summary>A tree snake takes this long to uncoil, raising its head towards the scout, before it strikes.</summary>
    internal const float UncoilSeconds = 0.8f;
    /// <summary>Before each strike on the ground a snake takes this long to rear up into an S.</summary>
    internal const float RearSeconds = 0.3f;
    internal const float TreeGiveUpDistance = 20f;
    internal const float GroundGiveUpDistance = 20f;
    /// <summary>Once it goes for a scout, a snake keeps after them at least this long, however far they run (then it gives up beyond the distances above).</summary>
    internal const float MinChaseSeconds = 10f;
    internal const float LungeFrom = 2.5f;
    internal const float BiteReach = 1.5f;
    internal const float LungeSeconds = 0.35f;
    internal const float RestAfterBite = 3f;
    /// <summary>Calm, a snake goes slowly (wandering; going home a little quicker); hunting, fast (chasing; keeping off after a bite or a hit).</summary>
    internal const float WanderSpeed = 0.5f;
    /// <summary>Hit, a snake slithers round the scout this far out (a metre beyond where it strikes from), at this speed, until it may strike again.</summary>
    internal const float CircleRadius = 3.5f;
    internal const float CircleSpeed = 2f;

    /// <summary>
    /// The sounds (the user's recordings, styled after PEAK's creatures, see the repository's notes): a slither through
    /// the grass while it crawls, louder the faster it goes, and a rattle while it hunts a scout; their loudness set
    /// against the game's bee swarms (the slither below an idle swarm, the rattle a little below an angry one).
    /// </summary>
    internal const string SlitherResource = "UltimateAscent.Snake_slither.wav";
    internal const string RattleResource = "UltimateAscent.Snake_rattle.wav";
    internal const float SlitherVolume = 0.5f;
    internal const float RattleVolume = 0.7f;
    internal const float SlitherReach = 18f;
    internal const float RattleReach = 30f;
    internal const float GroundChaseSpeed = 3.6f;
    internal const float TreeChaseSpeed = 3.2f;

    /// <summary>How fast a snake chases now (a third slower below ascent 17).</summary>
    internal static float ChaseSpeed(bool tree) => (tree ? TreeChaseSpeed : GroundChaseSpeed) * NewMobs.SnakeChase;
    internal const float WanderRange = 20f;
    internal const float FallbackSight = 8f;

    /// <summary>Snakes when the map's scorpions cannot be counted.</summary>
    internal const int FallbackCount = 9;

    /// <summary>A stone hits a snake when it flies at least this fast (the game's own bonk speed).</summary>
    internal const float StoneHitSpeed = 10f;

    internal static bool On => NewMobs.Allowed(NewMobs.SnakesThreat);

    internal static readonly List<Snake> All = new List<Snake>();

    /// <summary>For the tests: snakes spawned, and bites landed on the local scout.</summary>
    internal static int Spawned;
    internal static int BitesTaken;
    /// <summary>For the tests: the bites heard (the game's hit sound at the bitten scout) on this game.</summary>
    internal static int BiteSounds;
    /// <summary>For the tests: hits on snakes heard (the game's impact sound of a kick or a Stick's blow) on this game.</summary>
    internal static int HitSounds;

    private static AudioClip slitherClip;
    private static AudioClip rattleClip;
    private static bool soundsLoaded;

    internal static AudioClip SlitherClip
    {
        get
        {
            LoadSounds();
            return slitherClip;
        }
    }

    internal static AudioClip RattleClip
    {
        get
        {
            LoadSounds();
            return rattleClip;
        }
    }

    private static void LoadSounds()
    {
        if (soundsLoaded)
        {
            return;
        }
        soundsLoaded = true;
        try
        {
            slitherClip = ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(SlitherResource), "Snake_slither");
            rattleClip = ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(RattleResource), "Snake_rattle");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogError("Snakes: their sounds could not be loaded: " + ex.Message);
        }
    }

    /// <summary>The scorpion's sting effects (its prefab's AnimationVFX): the star glint before the sting, the sparks of it.</summary>
    internal const string ScorpionGlintName = "VFX_Star";
    internal const string ScorpionSparksName = "VFX_Dart";

    /// <summary>For the tests: the fangs' glints and sparks shown on this game.</summary>
    internal static int FangGlints;
    internal static int FangSparks;

    private static Item scorpionPrefab;
    private static bool scorpionLooked;

    /// <summary>A copy of one of the scorpion's sting effects on a snake's head, sized for it (none if the scorpion is not found).</summary>
    internal static ParticleSystem ScorpionEffect(string name, Transform parent, Vector3 at, float radius)
    {
        if (!scorpionLooked)
        {
            scorpionLooked = true;
            scorpionPrefab = SingletonAsset<ItemDatabase>.Instance.Objects.FirstOrDefault(i => i != null && i.GetComponentInChildren<Scorpion>(true) != null);
        }
        Transform source = scorpionPrefab != null ? scorpionPrefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) : null;
        ParticleSystem template = source != null ? source.GetComponent<ParticleSystem>() : null;
        if (template == null)
        {
            return null;
        }
        ParticleSystem made = Object.Instantiate(template, parent, false);
        made.name = "UltimateAscent Snake " + name;
        made.transform.localPosition = at;
        made.transform.localRotation = Quaternion.identity;
        made.transform.localScale = Vector3.one;
        foreach (Transform child in made.GetComponentsInChildren<Transform>(true))
        {
            if (child != made.transform)
            {
                child.localPosition = Vector3.zero;
            }
        }
        made.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        // Sized to the snake: a ground snake's a little smaller than the scorpion's, a tree snake's a little bigger.
        float size = Mathf.Clamp(radius / 0.09f, 0.6f, 1.6f);
        foreach (ParticleSystem part in made.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = part.main;
            main.startSizeMultiplier *= size;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.ShapeModule shape = part.shape;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
        }
        return made;
    }

    /// <summary>A bite lands: the game's sound of a scout taking a hit, where the bitten scout is (at their own ears for them).</summary>
    internal static void PlayBite(Character bitten)
    {
        SFX_Instance[] hits = ZombieDagger.HitSfx();
        if (bitten == null || hits.Length == 0)
        {
            return;
        }
        Vector3 at = bitten.IsLocal && MainCamera.instance != null ? MainCamera.instance.transform.position : bitten.Center;
        hits[Random.Range(0, hits.Length)].Play(at);
        BiteSounds++;
    }

    /// <summary>For the tests: the poison the ground snakes' venom has added (in status units), all of it and bite by bite.</summary>
    internal static float PoisonGiven;
    internal static readonly List<float> PoisonByBite = new List<float>();

    private static GameObject template;
    private static Material skinMaterial;

    /// <summary>The second part of each map gets a spawner; it spawns only where that part is the Tropics.</summary>
    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.InitializeMap))]
    private static class EveryMap
    {
        private static void Postfix()
        {
            MapHandler map = MapHandler.Exists ? Singleton<MapHandler>.Instance : null;
            if (map == null || map.segments == null || map.segments.Length < 2)
            {
                return;
            }
            MapHandler.MapSegment second = map.segments[1];
            GameObject parent = second.segmentParent;
            if (parent == null || second.biome != Biome.BiomeType.Tropics || parent.transform.Find(SpawnerName) != null)
            {
                return;
            }
            GameObject holder = new GameObject(SpawnerName);
            holder.transform.SetParent(parent.transform, false);
            holder.AddComponent<SnakeSpawner>();
        }
    }

    /// <summary>The host's spawn of the Tropics' snakes (none below ascent 17).</summary>
    internal static List<PhotonView> Spawn(Transform segment)
    {
        List<PhotonView> spawned = new List<PhotonView>();
        if (segment == null || !PhotonNetwork.IsMasterClient || !On || !Register())
        {
            return spawned;
        }
        Physics.SyncTransforms();
        int total = ScorpionCount();
        int inTrees = total / 3;
        List<TreeHome> branches = TreeHomes(segment, inTrees);
        if (branches.Count < inTrees)
        {
            Plugin.Debug($"Snakes: {branches.Count} of {inTrees} tree snakes found a way up the giant trees; the rest live on the ground.");
        }
        int grounded = total - branches.Count;
        foreach (TreeHome tree in branches)
        {
            spawned.Add(Make(Kind.Tree, tree.spot, tree.way, tree.facesOut));
        }
        ClimbingWays = branches.Count(b => b.way != null);
        List<Vector3> anchors = GroundItems.Anchors(segment);
        WaterZone[] water = Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
        List<Vector3> taken = new List<Vector3>();
        for (int attempt = 0; attempt < grounded * 20 && taken.Count < grounded && anchors.Count > 0; attempt++)
        {
            if (GroundItems.TryFindSpot(anchors, taken, water, out RaycastHit ground) && !Map.IsOtherBiome(ground.collider, Biome.BiomeType.Tropics))
            {
                taken.Add(ground.point);
                spawned.Add(Make(Kind.Ground, ground.point));
            }
        }
        spawned.RemoveAll(v => v == null);
        Spawned += spawned.Count;
        Plugin.Log.LogInfo($"New mobs: {spawned.Count} snakes in {segment.name} ({branches.Count} in trees, {taken.Count} on the ground; the map's scorpions: {total}).");
        return spawned;
    }

    private static PhotonView Make(Kind kind, Vector3 at, Vector3[] way = null, Vector3[] facesOut = null)
    {
        // The way goes as plain numbers (x, y, z after each other), which the network carries as they are.
        object[] data = way != null
            ? new object[] { (byte)kind, at, way.SelectMany(p => new[] { p.x, p.y, p.z }).ToArray(), facesOut.SelectMany(p => new[] { p.x, p.y, p.z }).ToArray() }
            : new object[] { (byte)kind, at };
        GameObject made = PhotonNetwork.InstantiateRoomObject(PrefabName, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 0, data);
        return made != null ? made.GetComponent<PhotonView>() : null;
    }

    /// <summary>The map's scorpions: every spot of every spawner that makes them (all parts of the map).</summary>
    internal static int ScorpionCount()
    {
        int count = 0;
        foreach (Spawner spawner in Object.FindObjectsByType<Spawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (spawner != null && spawner.spawnedObjectPrefab != null && spawner.spawnedObjectPrefab.GetComponent<Scorpion>() != null)
            {
                count += spawner.spawnSpots != null && spawner.spawnSpots.Count > 0 ? spawner.spawnSpots.Count : 1;
            }
        }
        return count > 0 ? count : FallbackCount;
    }

    /// <summary>The Tropics' giant trees are the game's "Jungle_GiantTree" objects (90-110 m tall, crowns of leaf clumps).</summary>
    private const string GiantTreeName = "GiantTree";

    /// <summary>The giant trees of this part of the map, each as its solid parts (trunk, branches, the clumps of its crown).</summary>
    private static List<Collider[]> GiantTrees(Transform segment)
    {
        List<Collider[]> trees = new List<Collider[]>();
        foreach (Transform t in segment.GetComponentsInChildren<Transform>(false))
        {
            bool tree = t.name.IndexOf(GiantTreeName, System.StringComparison.OrdinalIgnoreCase) >= 0
                && t.name.IndexOf("Crown", System.StringComparison.OrdinalIgnoreCase) < 0;
            bool inTree = t.parent != null && t.parent.name.IndexOf(GiantTreeName, System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!tree || inTree)
            {
                continue;
            }
            Collider[] parts = t.GetComponentsInChildren<Collider>(false).Where(c => c != null && !c.isTrigger).ToArray();
            if (parts.Length > 0)
            {
                trees.Add(parts);
            }
        }
        return trees;
    }

    /// <summary>A tree snake's spot (the top of a branch, where it sleeps curled up), its way up there, and which way the bark faces at each point of it.</summary>
    internal struct TreeHome
    {
        internal Vector3 spot;
        internal Vector3[] way;
        internal Vector3[] facesOut;
    }

    /// <summary>How steeply a climbing snake winds up a trunk or a steep branch (degrees above level): about two turns up a giant trunk.</summary>
    internal const float ClimbPitch = 32f;
    /// <summary>A surface steeper than this (its normal's height below this) is climbed; flatter ones are crawled on.</summary>
    internal const float SteepNormal = 0.6f;
    private const float TreeSnakeRadius = 0.14f;
    private const float ClimbStep = 0.15f;
    private const float WayPointApart = 0.25f;

    /// <summary>
    /// Spots in the giant trees, one tree after another, 8 m apart or more. Each is where a snake's climb ends: from the
    /// ground at the foot of the tree it winds up the trunk and the branches (the way it climbs home later, see
    /// <see cref="Snake"/>) to the top of a branch 6 m up or higher, with room to lie curled up. Fewer than wanted if the
    /// trees have no more room (or the map has no giant trees).
    /// </summary>
    private static List<TreeHome> TreeHomes(Transform segment, int wanted)
    {
        List<TreeHome> homes = new List<TreeHome>();
        List<Collider[]> trees = GiantTrees(segment).OrderBy(_ => Random.value).ToList();
        ClimbsTried = ClimbNoBark = ClimbNoFoot = ClimbLost = ClimbNoTop = 0;
        ClimbBestRise = 0f;
        for (int n = 0; n < wanted && trees.Count > 0; n++)
        {
            Collider[] tree = trees[n % trees.Count];
            Bounds bounds = tree[0].bounds;
            foreach (Collider part in tree)
            {
                bounds.Encapsulate(part.bounds);
            }
            HashSet<Collider> parts = new HashSet<Collider>(tree);
            float reach = Mathf.Max(bounds.extents.x, bounds.extents.z) + 5f;
            // Where a climb can start: bark with walkable ground right below it. The giant trees stand in rock and on cliffs,
            // so their lowest parts are buried: the lower half of the tree is searched, and the lowest starts tried first.
            List<(RaycastHit bark, Vector3 foot)> starts = new List<(RaycastHit, Vector3)>();
            for (int attempt = 0; attempt < 120 && starts.Count < 12; attempt++)
            {
                ClimbsTried++;
                Vector3 inward = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                Vector3 from = new Vector3(bounds.center.x, Random.Range(bounds.min.y + 1f, bounds.min.y + bounds.size.y * 0.6f), bounds.center.z) - inward * reach;
                if (!Physics.Raycast(from, inward, out RaycastHit bark, reach * 2f, HelperFunctions.terrainMapMask) || !parts.Contains(bark.collider) || bark.normal.y > SteepNormal)
                {
                    ClimbNoBark++;
                    continue;
                }
                if (!Physics.Raycast(bark.point + bark.normal * 0.6f, Vector3.down, out RaycastHit foot, 5f, HelperFunctions.terrainMapMask) || foot.normal.y < SteepNormal)
                {
                    ClimbNoFoot++;
                    continue;
                }
                starts.Add((bark, foot.point));
            }
            foreach ((RaycastHit bark, Vector3 foot) in starts.OrderBy(s => s.Item2.y).Take(10))
            {
                if (Climb(bark, foot, Random.value < 0.5f ? -1f : 1f, out TreeHome home)
                    && homes.All(h => (h.spot - home.spot).sqrMagnitude > 64f))
                {
                    homes.Add(home);
                    break;
                }
            }
        }
        Plugin.Log.LogInfo($"Snakes: climbs up the giant trees ({trees.Count} trees): {homes.Count} found a spot in {ClimbsTried} tries; no bark {ClimbNoBark}, no ground at the foot {ClimbNoFoot}, "
            + $"lost hold {ClimbLost}, no branch top {ClimbNoTop}; highest climb {ClimbBestRise:0.0} m.");
        if (homes.Count < wanted)
        {
            // Where no climb works, the old spots: a branch's top found from above, with a straight climb home.
            foreach (Vector3 spot in BranchTops(trees, wanted - homes.Count, homes.Select(h => h.spot).ToList()))
            {
                homes.Add(new TreeHome { spot = spot });
            }
        }
        return homes;
    }

    /// <summary>For the tests: the tree snakes spawned with a way up their tree (the rest climb straight home).</summary>
    internal static int ClimbingWays;

    /// <summary>For the log: how the climbs went (tries, why they failed, the highest any got).</summary>
    internal static int ClimbsTried, ClimbNoBark, ClimbNoFoot, ClimbLost, ClimbNoTop;
    internal static float ClimbBestRise;

    /// <summary>Branch tops found from above (6 m up or higher, room to lie curled up), 8 m apart, the trees in turn.</summary>
    private static List<Vector3> BranchTops(List<Collider[]> trees, int wanted, List<Vector3> taken)
    {
        List<Vector3> spots = new List<Vector3>();
        for (int n = 0; n < wanted && trees.Count > 0; n++)
        {
            Collider[] tree = trees[n % trees.Count];
            Bounds bounds = tree[0].bounds;
            foreach (Collider part in tree)
            {
                bounds.Encapsulate(part.bounds);
            }
            HashSet<Collider> parts = new HashSet<Collider>(tree);
            for (int attempt = 0; attempt < 300; attempt++)
            {
                Vector3 from = new Vector3(Random.Range(bounds.min.x, bounds.max.x), Random.Range(bounds.min.y + 8f, bounds.max.y + 1f), Random.Range(bounds.min.z, bounds.max.z));
                if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 12f, HelperFunctions.terrainMapMask)
                    || !parts.Contains(hit.collider) || hit.normal.y < 0.7f || hit.point.y < bounds.min.y + 6f
                    || Physics.CheckSphere(hit.point + Vector3.up * 0.8f, 0.5f, HelperFunctions.terrainMapMask)
                    || !RoomToCoil(hit.point + Vector3.up * TreeSnakeRadius)
                    || spots.Concat(taken).Any(s => (s - hit.point).sqrMagnitude < 64f))
                {
                    continue;
                }
                spots.Add(hit.point);
                break;
            }
        }
        return spots;
    }

    /// <summary>
    /// A snake's climb, worked out at once: from the foot of the tree, up the bark (winding round and up), over and round
    /// whatever it meets, until a branch's top (6 m up or higher, room above). False if it would lose its hold first, or
    /// finds no such top.
    /// </summary>
    private static bool Climb(RaycastHit bark, Vector3 foot, float side, out TreeHome home)
    {
        home = default;
        List<Vector3> way = new List<Vector3> { foot + Vector3.up * TreeSnakeRadius };
        List<Vector3> facesOut = new List<Vector3> { Vector3.up };
        Vector3 at = bark.point + bark.normal * TreeSnakeRadius;
        Vector3 normal = bark.normal;
        float start = foot.y;
        float sinceKept = WayPointApart;
        for (int step = 0; step < 900; step++)
        {
            Vector3 dir = normal.y > SteepNormal ? UpSlope(normal) : SpiralUp(normal, side);
            Vector3 was = at;
            if (!StepOnSurface(ref at, ref normal, dir, ClimbStep, TreeSnakeRadius))
            {
                ClimbLost++;
                ClimbBestRise = Mathf.Max(ClimbBestRise, at.y - start);
                return false;
            }
            sinceKept += Vector3.Distance(was, at);
            if (sinceKept >= WayPointApart)
            {
                way.Add(at);
                facesOut.Add(normal);
                sinceKept = 0f;
            }
            float rise = at.y - start;
            if (normal.y > 0.75f && rise >= 6f && !Physics.CheckSphere(at + Vector3.up * 0.8f, 0.5f, HelperFunctions.terrainMapMask) && RoomToCoil(at))
            {
                way.Add(at);
                facesOut.Add(normal);
                home = new TreeHome { spot = at, way = way.ToArray(), facesOut = facesOut.ToArray() };
                ClimbBestRise = Mathf.Max(ClimbBestRise, rise);
                return true;
            }
        }
        ClimbNoTop++;
        ClimbBestRise = Mathf.Max(ClimbBestRise, at.y - start);
        return false;
    }

    /// <summary>Room to lie curled up: the surface round the spot (0.75 m out, all round) about level with it, not falling away.</summary>
    private static bool RoomToCoil(Vector3 at)
    {
        for (int angle = 0; angle < 360; angle += 45)
        {
            Vector3 round = at + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 0.75f;
            if (!Physics.Raycast(round + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, 1.2f, HelperFunctions.terrainMapMask)
                || Mathf.Abs(floor.point.y - (at.y - TreeSnakeRadius)) > 0.3f)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// One step of a snake's head along the surface it clings to: onto whatever is in the way (an inside corner, where a
    /// branch leaves the trunk), along the surface, or round an outside edge (over a branch's top); false if it would
    /// lose its hold.
    /// </summary>
    internal static bool StepOnSurface(ref Vector3 at, ref Vector3 normal, Vector3 dir, float step, float radius)
    {
        if (Physics.Raycast(at, dir, out RaycastHit inWay, step + radius, HelperFunctions.terrainMapMask))
        {
            normal = inWay.normal;
            at = inWay.point + inWay.normal * radius;
            return true;
        }
        Vector3 next = at + dir * step;
        if (Physics.Raycast(next + normal * radius * 2f, -normal, out RaycastHit hold, radius * 4f + 0.25f, HelperFunctions.terrainMapMask))
        {
            normal = hold.normal;
            at = hold.point + hold.normal * radius;
            return true;
        }
        if (Physics.Raycast(next - normal * (radius * 2f + 0.05f), -dir, out RaycastHit edge, step + radius * 3f + 0.25f, HelperFunctions.terrainMapMask))
        {
            normal = edge.normal;
            at = edge.point + edge.normal * radius;
            return true;
        }
        return false;
    }

    /// <summary>Up a steep surface the way snakes climb trees: winding round it as it rises.</summary>
    internal static Vector3 SpiralUp(Vector3 normal, float side)
    {
        Vector3 up = UpSlope(normal);
        Vector3 around = Vector3.Cross(normal, up).normalized * side;
        float pitch = ClimbPitch * Mathf.Deg2Rad;
        return (up * Mathf.Sin(pitch) + around * Mathf.Cos(pitch)).normalized;
    }

    /// <summary>Straight up the surface (on a gentle one, up its slope).</summary>
    internal static Vector3 UpSlope(Vector3 normal)
    {
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
        return up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
    }

    /// <summary>The network template, made once: a PhotonView watching a Snake, kept inactive beside the mod's items.</summary>
    internal static bool Register()
    {
        if (template != null)
        {
            return true;
        }
        DefaultPool pool = ModItems.GamePool();
        if (pool == null)
        {
            Plugin.Log.LogError($"Snakes: the game's prefab pool was not found ({PhotonNetwork.PrefabPool?.GetType().Name}); no snakes.");
            return false;
        }
        GameObject root = new GameObject("UltimateAscentSnakes");
        root.SetActive(false);
        Object.DontDestroyOnLoad(root);
        template = new GameObject(PrefabName);
        template.transform.SetParent(root.transform, false);
        PhotonView view = template.AddComponent<PhotonView>();
        Snake snake = template.AddComponent<Snake>();
        view.ObservedComponents = new List<Component> { snake };
        view.Synchronization = ViewSynchronization.UnreliableOnChange;
        SphereCollider hitBox = template.AddComponent<SphereCollider>();
        hitBox.isTrigger = true;
        hitBox.radius = 0.35f;
        Rigidbody body = template.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        pool.ResourceCache[PrefabName] = template;
        return true;
    }

    /// <summary>A copy of the game's item shading (the Backpack's), for the snakes' skin.</summary>
    internal static Material Skin()
    {
        if (skinMaterial != null)
        {
            return skinMaterial;
        }
        GameObject backpack = Resources.Load<GameObject>("0_Items/Backpack");
        Material source = backpack != null ? backpack.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == ClimbingChalkModel.ItemShader) : null;
        skinMaterial = source != null ? new Material(source) { name = "UA_SnakeSkin" } : new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "UA_SnakeSkin" };
        skinMaterial.SetColor("_Tint", Color.white);
        skinMaterial.SetColor("_BaseColor", Color.white);
        skinMaterial.SetFloat("_BaseSmooth", 0.35f);
        return skinMaterial;
    }

    /// <summary>
    /// The skin: u along the body (a tile every nine radii), v once around it from the top of the back (0 and 1) to the
    /// middle of the belly (0.5). Ground snakes are olive with dark saddles across the back and spots along the sides;
    /// tree snakes are green with a broken pale line along each side. Both have a paler belly and a faint scale texture.
    /// </summary>
    internal static Texture2D Pattern(Kind kind)
    {
        const int Wide = 64;
        const int High = 32;
        bool tree = kind == Kind.Tree;
        Color back = tree ? new Color(0.16f, 0.45f, 0.14f) : new Color(0.5f, 0.45f, 0.24f);
        Color mark = tree ? new Color(0.75f, 0.8f, 0.35f) : new Color(0.2f, 0.17f, 0.09f);
        Color belly = tree ? new Color(0.72f, 0.78f, 0.38f) : new Color(0.86f, 0.8f, 0.58f);
        Texture2D texture = new Texture2D(Wide, High, TextureFormat.RGBA32, false, false) { name = "UA_SnakePattern_" + kind, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        for (int x = 0; x < Wide; x++)
        {
            for (int y = 0; y < High; y++)
            {
                float v = (y + 0.5f) / High;
                // 1 on top of the back, 0 in the middle of the belly.
                float up = Mathf.Abs(v - 0.5f) * 2f;
                float u = (x % (Wide / 2)) / (Wide / 2f);
                Color colour = back;
                if (up < 0.3f)
                {
                    colour = belly;
                }
                else if (up < 0.38f)
                {
                    colour = Color.Lerp(belly, back, (up - 0.3f) / 0.08f);
                }
                else if (tree)
                {
                    if (up > 0.5f && up < 0.56f && u > 0.1f && u < 0.75f)
                    {
                        colour = mark;
                    }
                }
                else
                {
                    float saddle = Mathf.Abs(u - 0.5f) * 2f + (1f - up) * 1.4f;
                    float spot = new Vector2((u + 0.5f) % 1f - 0.5f, (up - 0.5f) * 2.2f).magnitude;
                    if (saddle < 0.6f || spot < 0.17f)
                    {
                        colour = mark;
                    }
                }
                // Scales: a faint checker of lighter and darker rows.
                colour *= ((x + (y % 2)) % 2 == 0) ? 1.04f : 0.95f;
                colour.a = 1f;
                texture.SetPixel(x, y, colour);
            }
        }
        texture.Apply(false, true);
        return texture;
    }

    /// <summary>Asks the host to stun a snake (a hit from any scout's game), saying who hit it.</summary>
    internal static void Hit(Snake snake, float seconds, Vector3 push, Character by)
    {
        if (snake != null && snake.photonView != null)
        {
            snake.photonView.RPC(nameof(Snake.RPC_Hit), RpcTarget.MasterClient, seconds, push, by != null && by.photonView != null ? by.photonView.ViewID : -1);
        }
    }

    /// <summary>The snakes a swing or a kick from here reaches (the kick's reach and aim).</summary>
    internal static IEnumerable<Snake> InReach(Vector3 from, Vector3 look, float reach, float aim)
    {
        foreach (Snake snake in All.ToList())
        {
            if (snake == null)
            {
                continue;
            }
            // Any part of it: the head, or a point along its body.
            foreach (Vector3 part in snake.Parts())
            {
                Vector3 to = part - from;
                if (to.magnitude <= reach + 0.5f && Vector3.Angle(look, to) <= aim)
                {
                    yield return snake;
                    break;
                }
            }
        }
    }

    /// <summary>A bite on the local scout: its injury, then the poison or the paralysis on this game.</summary>
    internal static void Bitten(Character scout, Kind kind)
    {
        if (scout == null || !scout.IsLocal || scout.data.dead)
        {
            return;
        }
        BitesTaken++;
        CharacterAfflictions afflictions = scout.refs.afflictions;
        float strength = NewMobs.Strength;
        afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, (kind == Kind.Tree ? TreeBiteInjury : GroundBiteInjury) * strength);
        if (kind == Kind.Tree)
        {
            SnakeParalysis.Bite(scout);
        }
        else
        {
            PoisonByBite.Add(0f);
            Venom.Runner.StartCoroutine(Venom.Poison(scout, PoisonByBite.Count - 1, strength));
        }
    }

    /// <summary>The bites' after-effects, run on a holder that outlives the snake.</summary>
    internal sealed class Venom : MonoBehaviour
    {
        private static Venom runner;

        internal static Venom Runner
        {
            get
            {
                if (runner == null)
                {
                    GameObject holder = new GameObject("UltimateAscent Snake Venom");
                    Object.DontDestroyOnLoad(holder);
                    runner = holder.AddComponent<Venom>();
                }
                return runner;
            }
        }

        /// <summary>For the tests: every bite's after-effects on this game end now (no more collapses).</summary>
        internal static void EndAll()
        {
            SnakeParalysis.Stop();
            if (runner != null)
            {
                runner.StopAllCoroutines();
            }
        }

        /// <summary>40 poison over 10 s (20 below ascent 17), landing exactly (4 every second, or 2).</summary>
        internal static IEnumerator Poison(Character scout, int bite, float strength)
        {
            float perSecond = GroundPoison * strength / GroundPoisonSeconds;
            for (int second = 0; second < GroundPoisonSeconds; second++)
            {
                yield return new WaitForSeconds(1f);
                if (scout == null || scout.data.dead)
                {
                    yield break;
                }
                ExactStatus.Add(scout.refs.afflictions, CharacterAfflictions.STATUSTYPE.Poison, perSecond, "snake bite");
                PoisonGiven += perSecond;
                PoisonByBite[bite] += perSecond;
            }
        }
    }

    /// <summary>A Stick's or lit Torch's swing reaches snakes too (its knock-down time as their stun).</summary>
    internal static void SwingHits(Character holder, float seconds, float reach, float aim, Vector3 look)
    {
        foreach (Snake snake in InReach(holder.Center, look, reach, aim))
        {
            Hit(snake, seconds, look, holder);
        }
    }

    /// <summary>A kick (Kick Mode) reaches snakes in front too, when it lands.</summary>
    [HarmonyPatch(typeof(CharacterGrabbing), nameof(CharacterGrabbing.KickCast))]
    private static class KickedSnakes
    {
        private static void Postfix(CharacterGrabbing __instance)
        {
            Character kicker = __instance.character;
            if (All.Count > 0 && kicker != null && kicker.IsLocal)
            {
                __instance.StartCoroutine(Kick(__instance, kicker));
            }
        }

        private static IEnumerator Kick(CharacterGrabbing kick, Character kicker)
        {
            yield return new WaitForSeconds(kick.kickDelay);
            foreach (Snake snake in InReach(kicker.Center, kicker.data.lookDirection, kick.kickDistance, kick.kickAngle))
            {
                Hit(snake, kick.kickRagdollTime, kicker.data.lookDirection, kicker);
            }
        }
    }
}

/// <summary>One snake: its hunt (on the host), its body (on every game) and the network between.</summary>
internal sealed class Snake : MonoBehaviourPun, IPunObservable
{
    private enum State : byte
    {
        Sleeping,
        Wandering,
        Chasing,
        Lunging,
        Resting,
        Stunned,
        Returning,
        /// <summary>A tree snake that has noticed a scout: it uncoils, raising its head towards them, then strikes.</summary>
        Waking,
        /// <summary>About to strike: it draws the front of its body up into an S, facing the scout, mouth half open.</summary>
        Rearing,
    }

    /// <summary>The skeleton: a bone at the snout, one at the back of the head, the rest spread evenly to the tail tip.</summary>
    private const int Bones = 26;
    /// <summary>The skinned body: rings along it (closer at the head) and points around each.</summary>
    private const int Rings = 80;
    private const int Sides = 16;
    /// <summary>The head's length, in body radii.</summary>
    private const float HeadRadii = 3.6f;
    /// <summary>The eyes, as a share of the head's length from the snout.</summary>
    private const float EyeAt = 0.38f;

    /// <summary>The lower jaw's hinge, as a share of the head's length from the snout: the corner of the mouth, just behind the eye.</summary>
    private const float HingeAt = 0.65f;
    /// <summary>
    /// Crawling, the head swings up to this far to either side of its way, a full swing every 1/1.6 of the body's length;
    /// the body, lying along the head's track, takes the S-curves of a crawling snake.
    /// </summary>
    private const float SerpentDegrees = 40f;
    private const float WavesPerLength = 1.6f;
    /// <summary>The swings make the way longer: the head crawls this much faster along its track to keep its pace.</summary>
    private const float SerpentPace = 1.16f;
    private const float TrailStep = 0.02f;
    /// <summary>The head moved further than this in one frame (shoved, or snapped to the host's): the body is laid out again.</summary>
    private const float JumpDistance = 1f;
    /// <summary>How fast the body's track sinks to the ground under it where it was left in the air (m/s).</summary>
    private const float TrailFall = 2.5f;
    /// <summary>The head falls no faster than this (m/s).</summary>
    private const float MaxFallSpeed = 20f;
    /// <summary>Crawling with the ground more than this below, the head is in the air: it falls rather than snapping down.</summary>
    private const float AirborneHeight = 0.3f;
    /// <summary>A jump at a scout covers this much a second at least (a tree snake dropping at a scout 5 m off).</summary>
    private const float LungeSpeed = 7f;
    /// <summary>A tree snake going home climbs once it is this close to below its spot, at this speed.</summary>
    private const float ClimbReach = 1.5f;
    private const float ClimbSpeed = 1f;
    /// <summary>Going home for this long without getting there, it is back at its spot once no scout is near enough to see it.</summary>
    private const float ReturnGiveUp = 120f;
    private const float UnseenDistance = 30f;
    private const float FlickSeconds = 0.4f;
    /// <summary>How high the strike's arc rises above the straight line from the S to the scout.</summary>
    private const float StrikeArc = 0.15f;

    private Snakes.Kind kind;
    private Vector3 home;
    private State state;
    private float length;
    private float radius;
    private Vector3 head;
    private Vector3 facing = Vector3.forward;
    private float[] boneAt;
    private Transform[] bones;
    private Vector3[] spine;
    /// <summary>Which way each bone's back is turned: away from the surface it lies on (the bark while it climbs, the ground's slope).</summary>
    private Vector3[] spineUp;
    /// <summary>How high what each bone lies on is (the ground, the bark, its own coil): a bone well above it is raised off it.</summary>
    private float[] spineFloor;
    /// <summary>The bones' frames as worked out for each pose (see Frames): which way each faces, how firmly it lies on something, which way its back is turned.</summary>
    private Vector3[] aheads;
    private float[] holds;
    private Vector3[] backs;
    /// <summary>How much of the head's turn each piece of the body (bone to bone) takes where it is raised (see Frames).</summary>
    private float[] twists;
    /// <summary>Raised up (rearing, uncoiling, striking): the turn spread along the raised part to keep its head upright, as last posed.</summary>
    private float neckRoll;
    private bool neckRolling;
    private readonly List<TrailPoint> trail = new List<TrailPoint>();
    private float fallSpeed;
    /// <summary>A shove from a hit: how fast, and until when (on every game).</summary>
    private Vector3 shove;
    private float shoveUntil = float.NegativeInfinity;
    private bool shoving;
    private const float ShoveDistance = 1.5f;
    private const float ShoveSeconds = 0.25f;
    /// <summary>A tree snake's way up its tree (from the ground at its foot to its spot), the distance along it at each point, and whether its spot is on the trunk.</summary>
    private Vector3[] way;
    private float[] wayAt;
    /// <summary>Which way the bark faces at each point of its way (its belly goes against it while it climbs).</summary>
    private Vector3[] wayFacesOut;
    private int nearestOnWay;
    /// <summary>On its way up (how far along it is), and clinging to a steep surface (no falling; on every game, from the host).</summary>
    private bool onWay;
    private float alongWay;
    private bool clinging;
    private float returningSince;
    private State lastState;
    /// <summary>Which way it slithers round the scout after a hit: 1 or -1.</summary>
    private float circleSide = 1f;
    private float lostFor;
    private float stuckFor;
    private float lungeTime = Snakes.LungeSeconds;

    /// <summary>A point of the head's track and how low it can sink: the ground under it (where it was laid, pinned while climbing).</summary>
    private struct TrailPoint
    {
        internal Vector3 at;
        internal float floor;
        /// <summary>Which way the surface under it faces (its back is turned that way, its belly against the surface).</summary>
        internal Vector3 up;
    }
    private Vector3 lastHead;
    private bool coiled;
    /// <summary>Reared up into an S (drawn so), and since when.</summary>
    private bool reared;
    private float rearStart;
    /// <summary>Curling up: how it lay when it began, and when; when it began to uncoil; the state it was posed in last.</summary>
    private Vector3[] curledFrom;
    private float curlStart = float.NegativeInfinity;
    private float wakeStart;
    private State lastPosed;
    private const float CurlSeconds = 1.2f;
    /// <summary>Hit, it curls up quicker.</summary>
    private const float HitCurlSeconds = 0.5f;
    private float curlSeconds = CurlSeconds;
    /// <summary>
    /// Striking out of its uncoiling or the S it reared up in: how it was drawn before it was rounded off (the track is
    /// laid from that), how much nearer the head each bone lay along the body then than its place on the body (bone to
    /// bone straight across a bend is shorter), and since when. That and the sinking (see Sag) come in over a moment, so
    /// the strike goes on from just how it looked (at once, the body jumped).
    /// </summary>
    private Vector3[] drawnRaw;
    private float[] handOverShift;
    private float handOverStart = float.NegativeInfinity;
    private const float HandOverSeconds = 0.15f;

    /// <summary>How much of the hand-over from its uncoiling or its S to the strike is still to come (1 as it begins, 0 once it is over).</summary>
    private float HandOverLeft => 1f - Mathf.SmoothStep(0f, 1f, (Time.time - handOverStart) / HandOverSeconds);

    /// <summary>For the tests: curled up (asleep, or hit).</summary>
    internal bool Curled => coiled;
    private Transform tongue;
    private float flickFrom = float.NegativeInfinity;
    /// <summary>The lower jaw (a bone hinged at the back of the head), the fangs, and how far the mouth is open (0-1).</summary>
    private Transform jaw;
    private readonly List<Transform> fangs = new List<Transform>();
    private float mouth;
    private State lastGape;
    private const float JawDegrees = 40f;
    private const float HeadLiftDegrees = 8f;
    private static readonly Quaternion FangFolded = Quaternion.FromToRotation(Vector3.down, new Vector3(0f, -0.12f, -1f));
    private static readonly Quaternion FangBared = Quaternion.FromToRotation(Vector3.down, new Vector3(0f, -1f, 0.35f));

    /// <summary>For the tests: how far its mouth is open (0 shut, 1 wide open, fangs bared).</summary>
    internal float MouthOpen => mouth;
    private float nextFlick;
    private float stateUntil;
    private Character target;
    /// <summary>The scout its last strike bit (none if it missed), and the first scout to stun it while it is stunned.</summary>
    private Character lastBitten;
    private Character stunnedBy;
    private Vector3 lungeFrom;
    private Vector3 lungeTo;
    private Vector3 wanderTo;
    private float nextWander;
    private float nextLook;
    private float wave;
    private float sight = Snakes.FallbackSight;

    // Network: where the head should be on the others' games.
    private Vector3 netHead;
    private Vector3 netFacing = Vector3.forward;

    private Mesh mesh;
    private SkinnedMeshRenderer body;
    private AudioSource slither;
    private AudioSource rattle;
    private Vector3 heardAt;
    private float pace;

    /// <summary>For the tests: how loud its slither and its rattle are now (0 when silent).</summary>
    internal float SlitherLoudness => slither != null && slither.isPlaying ? slither.volume : 0f;
    internal float RattleLoudness => rattle != null && rattle.isPlaying ? rattle.volume : 0f;

    internal Vector3 Head => head;

    /// <summary>
    /// For the tests: of the parts of it raised off what it lies on that reach out (an arch, a strike; not those rising
    /// steeply), the furthest a back is turned from the sky, in degrees (0 back up, 90 on its side, 180 belly up).
    /// </summary>
    internal float RaisedRoll
    {
        get
        {
            float worst = 0f;
            if (backs == null)
            {
                return worst;
            }
            for (int i = 0; i < Bones; i++)
            {
                Vector3 sky = Vector3.ProjectOnPlane(Vector3.up, aheads[i]);
                if (holds[i] < 0.3f && Mathf.Abs(aheads[i].y) < 0.8f && sky.sqrMagnitude > 1e-4f)
                {
                    worst = Mathf.Max(worst, Vector3.Angle(backs[i], sky));
                }
            }
            return worst;
        }
    }

    /// <summary>
    /// For the tests: how its frames lie now, along the front of the body: for each bone how far its back is turned from
    /// the sky (* where RaisedRoll counts it), its pitch, how firmly it lies on something and the share of the head's
    /// turn it takes; what it is doing and for how long.
    /// </summary>
    internal string RollReport
    {
        get
        {
            if (backs == null || twists == null)
            {
                return "";
            }
            float total = 0f;
            for (int i = 0; i < Bones - 1; i++)
            {
                total += twists[i];
            }
            float rest = total;
            List<string> parts = new List<string>();
            for (int i = 0; i < Bones; i++)
            {
                Vector3 sky = Vector3.ProjectOnPlane(Vector3.up, aheads[i]);
                bool counted = holds[i] < 0.3f && Mathf.Abs(aheads[i].y) < 0.8f && sky.sqrMagnitude > 1e-4f;
                string off = sky.sqrMagnitude > 1e-4f ? Vector3.Angle(backs[i], sky).ToString("0") : "-";
                parts.Add($"{i}:{off}{(counted ? "*" : "")} p{Mathf.Asin(Mathf.Clamp(aheads[i].y, -1f, 1f)) * Mathf.Rad2Deg:0} h{holds[i]:0.0} s{(total > 1e-8f ? rest / total : 0f):0.00}");
                if (i < Bones - 1)
                {
                    rest -= twists[i];
                }
            }
            float since = state == State.Lunging ? lungeTime - (stateUntil - Time.time) : state == State.Rearing ? Time.time - rearStart : state == State.Waking ? Time.time - wakeStart : 0f;
            return $"{state} {since:0.00} s; the head's turn {neckRoll:0} (rolling {neckRolling}); " + string.Join(" ", parts);
        }
    }

    /// <summary>The head and points along the body (every fourth bone), for what can hit it.</summary>
    internal IEnumerable<Vector3> Parts()
    {
        yield return head;
        if (spine == null)
        {
            yield break;
        }
        for (int i = 4; i < spine.Length; i += 4)
        {
            yield return spine[i];
        }
    }

    internal bool IsTree => kind == Snakes.Kind.Tree;

    /// <summary>For the tests: what it is doing (Sleeping, Wandering, Chasing, Lunging, Resting, Stunned, Returning).</summary>
    internal string Doing => state.ToString();

    internal Vector3 Home => home;

    /// <summary>For the tests: the scout it goes for (none if it hunts nobody), and how far its last hit knocked it back (cut short by what was behind it).</summary>
    internal Character Target => target;
    internal float LastShove { get; private set; }
    internal string LastShoveStop { get; private set; } = "";

    private void Awake()
    {
        object[] data = photonView != null ? photonView.InstantiationData : null;
        kind = data != null && data.Length > 0 && data[0] is byte k ? (Snakes.Kind)k : Snakes.Kind.Ground;
        home = data != null && data.Length > 1 && data[1] is Vector3 h ? h : transform.position;
        float[] numbers = data != null && data.Length > 2 ? data[2] as float[] : null;
        way = numbers != null ? Enumerable.Range(0, numbers.Length / 3).Select(i => new Vector3(numbers[3 * i], numbers[3 * i + 1], numbers[3 * i + 2])).ToArray() : null;
        float[] facing3 = data != null && data.Length > 3 ? data[3] as float[] : null;
        wayFacesOut = facing3 != null ? Enumerable.Range(0, facing3.Length / 3).Select(i => new Vector3(facing3[3 * i], facing3[3 * i + 1], facing3[3 * i + 2])).ToArray() : null;
        if (wayFacesOut != null && way != null && wayFacesOut.Length != way.Length)
        {
            wayFacesOut = null;
        }
        if (way != null && way.Length > 1)
        {
            wayAt = new float[way.Length];
            for (int i = 1; i < way.Length; i++)
            {
                wayAt[i] = wayAt[i - 1] + Vector3.Distance(way[i - 1], way[i]);
            }
        }
        else
        {
            way = null;
        }
        length = IsTree ? 3.6f : 1.4f;
        radius = IsTree ? 0.14f : 0.055f;
        state = IsTree ? State.Sleeping : State.Wandering;
        head = transform.position;
        netHead = head;
        facing = transform.forward;
        sight = ScorpionSight();
        BuildBody();
        Reseed();
        // A tree snake starts asleep, curled up.
        coiled = IsTree;
        slither = Loop(Snakes.SlitherClip, Snakes.SlitherReach);
        rattle = Loop(Snakes.RattleClip, Snakes.RattleReach);
        heardAt = head;
        Snakes.All.Add(this);
    }

    private void OnDestroy()
    {
        Snakes.All.Remove(this);
        if (mesh != null)
        {
            Destroy(mesh);
        }
    }

    /// <summary>How near a scorpion notices a scout (its own data), for the ground snakes.</summary>
    private static float ScorpionSight()
    {
        foreach (Item item in SingletonAsset<ItemDatabase>.Instance.Objects)
        {
            Scorpion scorpion = item != null ? item.GetComponent<Scorpion>() : null;
            if (scorpion != null)
            {
                return Mathf.Max(scorpion.aggroDistance, 3f);
            }
        }
        return Snakes.FallbackSight;
    }

    /// <summary>
    /// The skinned body, made once: rings along the snake laid straight from the snout (at the origin) down -z, closer
    /// together at the head, each vertex bound to the two bones around it (the head between the first two, so it stays
    /// rigid); its eyes on the head bone, and its forked tongue at the snout.
    /// </summary>
    private void BuildBody()
    {
        float headLength = radius * HeadRadii;
        boneAt = new float[Bones];
        for (int i = 1; i < Bones; i++)
        {
            boneAt[i] = headLength + (length - headLength) * (i - 1) / (Bones - 2);
        }
        spine = new Vector3[Bones];
        spineUp = Enumerable.Repeat(Vector3.up, Bones).ToArray();
        spineFloor = new float[Bones];
        aheads = new Vector3[Bones];
        holds = new float[Bones];
        backs = new Vector3[Bones];
        twists = new float[Bones];
        curledFrom = new Vector3[Bones];
        handOverShift = new float[Bones];
        drawnRaw = new Vector3[Bones];
        bones = new Transform[Bones + 1];
        Matrix4x4[] bindPoses = new Matrix4x4[Bones + 1];
        Transform skeleton = new GameObject("Skeleton").transform;
        skeleton.SetParent(transform, false);
        for (int i = 0; i < Bones; i++)
        {
            spine[i] = head - facing * boneAt[i];
            bones[i] = new GameObject("Bone " + i).transform;
            bones[i].SetParent(skeleton, false);
            bindPoses[i] = Matrix4x4.Translate(new Vector3(0f, 0f, boneAt[i]));
        }
        // The lower jaw: the last bone, hinged near the back of the head (on the head bone, so it goes where the head goes).
        Vector3 hinge = new Vector3(0f, -radius * 0.05f, -headLength * HingeAt);
        jaw = new GameObject("Jaw").transform;
        jaw.SetParent(bones[0], false);
        jaw.localPosition = hinge;
        bones[Bones] = jaw;
        bindPoses[Bones] = Matrix4x4.Translate(-hinge);
        lastHead = head;
        mesh = BodyMesh(headLength);
        mesh.bindposes = bindPoses;
        GameObject skin = new GameObject("Body");
        skin.transform.SetParent(transform, false);
        body = skin.AddComponent<SkinnedMeshRenderer>();
        body.bones = bones;
        body.rootBone = bones[0];
        body.quality = SkinQuality.Bone2;
        body.updateWhenOffscreen = true;
        body.sharedMesh = mesh;
        Material material = new Material(Snakes.Skin()) { name = "UA_SnakeSkin_" + kind };
        material.SetTexture("_BaseTexture", Snakes.Pattern(kind));
        body.sharedMaterials = new[] { material, Plain("UA_SnakeMouth", new Color(0.62f, 0.2f, 0.26f), 0.55f) };

        Material black = Plain("UA_SnakeEye", new Color(0.03f, 0.03f, 0.03f), 0.9f);
        Material iris = Plain("UA_SnakeIris", IsTree ? new Color(0.95f, 0.86f, 0.2f) : new Color(0.95f, 0.58f, 0.12f), 0.9f);
        foreach (float side in new[] { -1f, 1f })
        {
            HeadShape(EyeAt, headLength, out float eyeWidth, out float eyeHeight, out float _);
            Vector3 eyeAt = new Vector3(side * Mathf.Cos(35f * Mathf.Deg2Rad) * eyeWidth * 0.92f, Mathf.Sin(35f * Mathf.Deg2Rad) * eyeHeight * 0.92f, 0f) * radius
                + new Vector3(0f, 0f, -headLength * EyeAt);
            Vector3 outward = new Vector3(side * Mathf.Cos(35f * Mathf.Deg2Rad), Mathf.Sin(35f * Mathf.Deg2Rad), 0.35f).normalized;
            Part(PrimitiveType.Sphere, bones[0], eyeAt, Quaternion.identity, Vector3.one * radius * 0.58f, iris);
            Part(PrimitiveType.Sphere, bones[0], eyeAt + outward * radius * 0.25f, Quaternion.identity, new Vector3(radius * 0.09f, radius * 0.4f, radius * 0.09f), black);
        }
        Material red = Plain("UA_SnakeTongue", new Color(0.55f, 0.06f, 0.1f), 0.5f);
        tongue = new GameObject("Tongue").transform;
        tongue.SetParent(bones[0], false);
        tongue.localPosition = new Vector3(0f, -radius * 0.12f, -radius * 0.25f);
        float reach = headLength * 0.85f;
        float thin = radius * 0.09f;
        Part(PrimitiveType.Cube, tongue, new Vector3(0f, 0f, reach * 0.35f), Quaternion.identity, new Vector3(thin, thin * 0.6f, reach * 0.7f), red);
        foreach (float side in new[] { -1f, 1f })
        {
            Quaternion fork = Quaternion.Euler(0f, side * 24f, 0f);
            Part(PrimitiveType.Cube, tongue, new Vector3(0f, 0f, reach * 0.7f) + fork * new Vector3(0f, 0f, reach * 0.15f), fork, new Vector3(thin * 0.8f, thin * 0.5f, reach * 0.3f), red);
        }
        tongue.gameObject.SetActive(false);
        nextFlick = Time.time + Random.Range(1f, 3f);
        BuildMouth(headLength, hinge);
    }

    /// <summary>
    /// Two fangs at the front of the roof of the mouth, folded back flat against it until the mouth opens, then swung down
    /// and forward.
    /// </summary>
    private void BuildMouth(float headLength, Vector3 hinge)
    {
        Material ivory = Plain("UA_SnakeFang", new Color(0.98f, 0.97f, 0.92f), 0.7f);
        Mesh cone = Cone(radius * 1.0f, radius * 0.11f);
        Profile(headLength * 0.22f, headLength, out float front, out float _);
        foreach (float side in new[] { -1f, 1f })
        {
            Transform pivot = new GameObject("Fang").transform;
            pivot.SetParent(bones[0], false);
            pivot.localPosition = new Vector3(side * front * radius * 0.4f, -radius * 0.04f, -headLength * 0.22f);
            pivot.localRotation = FangFolded;
            GameObject fang = new GameObject("Fang Mesh");
            fang.transform.SetParent(pivot, false);
            fang.AddComponent<MeshFilter>().sharedMesh = cone;
            fang.AddComponent<MeshRenderer>().sharedMaterial = ivory;
            fangs.Add(pivot);
        }
        glint = Snakes.ScorpionEffect(Snakes.ScorpionGlintName, bones[0], new Vector3(0f, -radius * 0.25f, -headLength * 0.2f), radius);
        sparks = Snakes.ScorpionEffect(Snakes.ScorpionSparksName, bones[0], new Vector3(0f, -radius * 0.25f, -headLength * 0.1f), radius);
    }

    private ParticleSystem glint;
    private ParticleSystem sparks;

    /// <summary>The bite lands, on every game: the scorpion's sparks burst from the fangs.</summary>
    internal void VenomSparks()
    {
        if (sparks != null)
        {
            sparks.Play(true);
            Snakes.FangSparks++;
        }
    }

    /// <summary>A small cone pointing down from its base (a fang).</summary>
    private static Mesh Cone(float height, float baseRadius)
    {
        const int sides = 6;
        Vector3[] points = new Vector3[sides + 2];
        List<int> triangles = new List<int>();
        for (int s = 0; s < sides; s++)
        {
            float a = s * Mathf.PI * 2f / sides;
            points[s] = new Vector3(Mathf.Cos(a) * baseRadius, 0f, Mathf.Sin(a) * baseRadius);
        }
        int tip = sides, top = sides + 1;
        points[tip] = new Vector3(0f, -height, 0f);
        points[top] = Vector3.zero;
        for (int s = 0; s < sides; s++)
        {
            int n = (s + 1) % sides;
            triangles.AddRange(new[] { s, n, tip });
            triangles.AddRange(new[] { top, n, s });
        }
        Mesh made = new Mesh { name = "UA_SnakeFang", vertices = points, triangles = triangles.ToArray() };
        made.RecalculateNormals();
        return made;
    }

    /// <summary>
    /// The whole snake as one skinned mesh, laid straight from the snout (at the origin) down -z, rings closer together at
    /// the head. The head is part of it: in front of the jaw's hinge each ring is split along the line of the lips (the
    /// lips' points twice, one for each jaw), the skull's half on the head bone and the lower jaw's on the jaw bone, so the
    /// mouth opens cleanly; behind the hinge the rings are whole and the head runs on into the neck and the body, bound to
    /// the bones around each ring. The inside of the mouth (roof and floor, inset from the lips) is its second part.
    /// </summary>
    private Mesh BodyMesh(float headLength)
    {
        int around = Sides + 1;
        int lipLeft = Sides / 4;
        int lipRight = Sides * 3 / 4;
        float hingeAt = headLength * HingeAt;
        List<Vector3> points = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<BoneWeight> weights = new List<BoneWeight>();
        List<int> skin = new List<int>();
        List<int> inside = new List<int>();
        int[,] upper = new int[Rings, around];
        int[,] lower = new int[Rings, around];
        float[] ringAt = new float[Rings];
        BoneWeight jawBone = new BoneWeight { boneIndex0 = Bones, weight0 = 1f };
        int Add(Vector3 p, Vector2 t, BoneWeight w)
        {
            points.Add(p);
            uv.Add(t);
            weights.Add(w);
            return points.Count - 1;
        }
        for (int r = 0; r < Rings; r++)
        {
            float d = length * Mathf.Pow(r / (float)(Rings - 1), 1.25f);
            ringAt[r] = d;
            float width, up, down;
            if (d < headLength)
            {
                HeadShape(d / headLength, headLength, out width, out up, out down);
            }
            else
            {
                Profile(d, headLength, out width, out float height);
                up = height;
                down = height * 0.75f;
            }
            bool split = d < hingeAt;
            BoneWeight skull = SkullWeight(d, headLength);
            for (int s = 0; s < around; s++)
            {
                // From the top of the back, around the side, under the belly and back up.
                float a = Mathf.PI * 0.5f + s / (float)Sides * Mathf.PI * 2f;
                float sin = Mathf.Sin(a);
                Vector3 p = new Vector3(Mathf.Cos(a) * width, sin >= 0f ? sin * up : sin * down, 0f) * radius + new Vector3(0f, 0f, -d);
                Vector2 t = new Vector2(d / (radius * 9f), s / (float)Sides);
                bool onLip = s == lipLeft || s == lipRight;
                bool below = s > lipLeft && s < lipRight;
                upper[r, s] = Add(p, t, split && below ? jawBone : skull);
                lower[r, s] = split && onLip ? Add(p, t, jawBone) : upper[r, s];
            }
        }
        for (int r = 0; r < Rings - 1; r++)
        {
            for (int s = 0; s < Sides; s++)
            {
                // The lower jaw's quads (round under the belly between the lips) take its own copies of the lips' points.
                bool jawSide = s >= lipLeft && s < lipRight;
                int[,] ring = jawSide ? lower : upper;
                int i = ring[r, s], b = ring[r, s + 1], c = ring[r + 1, s], e = ring[r + 1, s + 1];
                skin.AddRange(new[] { i, c, b, b, c, e });
            }
        }
        // The snout's tip, the skull's and the jaw's, and the tail's tip.
        int tipUp = Add(new Vector3(0f, radius * 0.02f, radius * 0.03f), new Vector2(0f, 0f), SkullWeight(0f, headLength));
        int tipDown = Add(new Vector3(0f, -radius * 0.02f, radius * 0.03f), new Vector2(0f, 0.5f), jawBone);
        int tail = Add(new Vector3(0f, 0f, -length - radius * 0.1f), new Vector2(length / (radius * 9f), 0f), new BoneWeight { boneIndex0 = Bones - 1, weight0 = 1f });
        for (int s = 0; s < Sides; s++)
        {
            bool jawSide = s >= lipLeft && s < lipRight;
            int[,] ring = jawSide ? lower : upper;
            skin.AddRange(new[] { jawSide ? tipDown : tipUp, ring[0, s], ring[0, s + 1] });
            skin.AddRange(new[] { tail, upper[Rings - 1, s + 1], upper[Rings - 1, s] });
        }
        // Inside the mouth: the roof (seen from below, on the skull) and the floor (seen from above, on the jaw), out to the
        // lips; and at each side, over the back half of the mouth, the skin of its corner from the upper lip down to the lower
        // (nothing while the mouth is shut, stretched across the gap when it opens; seen from both sides). Up to the ring
        // where the lips meet again.
        for (int r = 0; r < Rings - 1 && ringAt[r] < hingeAt; r++)
        {
            int[] roof = new int[4];
            int[] floor = new int[4];
            for (int k = 0; k < 4; k++)
            {
                int rr = r + k / 2;
                Vector3 edge = points[upper[rr, k % 2 == 0 ? lipRight : lipLeft]];
                Vector3 across = new Vector3(edge.x, 0f, edge.z);
                BoneWeight below = ringAt[rr] < hingeAt ? jawBone : SkullWeight(ringAt[rr], headLength);
                roof[k] = Add(across + Vector3.down * radius * 0.01f, Vector2.zero, SkullWeight(ringAt[rr], headLength));
                floor[k] = Add(across + Vector3.down * radius * 0.03f, Vector2.zero, below);
            }
            // roof/floor: [0] right of ring r, [1] left of ring r, [2] right of ring r + 1, [3] left of ring r + 1.
            inside.AddRange(new[] { roof[0], roof[1], roof[2], roof[1], roof[3], roof[2] });
            inside.AddRange(new[] { floor[0], floor[2], floor[1], floor[1], floor[2], floor[3] });
            foreach (int lip in new[] { lipLeft, lipRight })
            {
                if (ringAt[r + 1] < hingeAt * 0.5f)
                {
                    // The front of the mouth stays open at the sides (the fangs show there).
                    continue;
                }
                for (int face = 0; face < 2; face++)
                {
                    int[] corner = new int[4];
                    for (int k = 0; k < 4; k++)
                    {
                        int rr = r + k / 2;
                        Vector3 at = points[upper[rr, lip]];
                        bool bottom = k % 2 == 1;
                        BoneWeight w = bottom && ringAt[rr] < hingeAt ? jawBone : SkullWeight(ringAt[rr], headLength);
                        corner[k] = Add(at, Vector2.zero, w);
                    }
                    // corner: [0] upper lip, ring r; [1] lower lip, ring r; [2] upper lip, ring r + 1; [3] lower lip, ring r + 1.
                    inside.AddRange(face == 0
                        ? new[] { corner[0], corner[2], corner[1], corner[1], corner[2], corner[3] }
                        : new[] { corner[0], corner[1], corner[2], corner[1], corner[3], corner[2] });
                }
            }
        }
        Mesh made = new Mesh { name = "UA_SnakeBody" };
        made.SetVertices(points);
        made.SetUVs(0, uv);
        made.boneWeights = weights.ToArray();
        made.subMeshCount = 2;
        made.SetTriangles(skin, 0);
        made.SetTriangles(inside, 1);
        made.RecalculateNormals();
        // The seam down the back: both copies of each point share their normal, so no edge shows.
        Vector3[] normals = made.normals;
        for (int r = 0; r < Rings; r++)
        {
            Vector3 shared = (normals[upper[r, 0]] + normals[upper[r, Sides]]).normalized;
            normals[upper[r, 0]] = shared;
            normals[upper[r, Sides]] = shared;
        }
        made.normals = normals;
        made.RecalculateBounds();
        return made;
    }

    /// <summary>
    /// How the skull and the back of the head go: the front of the skull with the head bone alone (it does not bend), then
    /// over to the bone at the back of the head, and from there as the body's bones carry it.
    /// </summary>
    private BoneWeight SkullWeight(float d, float headLength)
    {
        if (d >= headLength)
        {
            return WeightAt(d);
        }
        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(headLength * 0.6f, headLength, d));
        return new BoneWeight { boneIndex0 = 0, weight0 = 1f - w, boneIndex1 = 1, weight1 = w };
    }

    /// <summary>
    /// The head's half width and the heights of its upper and lower halves at a point along it (0 snout, 1 back), in
    /// radii: flat and broad at the temples, easing at the back into the neck's own shape.
    /// </summary>
    private void HeadShape(float h, float headLength, out float width, out float upper, out float lower)
    {
        Profile(h * headLength, headLength, out width, out float _);
        float back = h * h * h;
        upper = width * Mathf.Lerp(0.42f, 0.9f, back);
        lower = width * Mathf.Lerp(0.26f, 0.675f, back);
    }

    /// <summary>
    /// Half the width and height at a distance from the snout, in body radii: a rounded snout, a broad flat head with
    /// its temples behind the eyes, a narrower neck, the body, and a long tail tapering to a point.
    /// </summary>
    private const float Neck = 0.8f;
    private const float SnoutLength = 0.2f;

    private void Profile(float d, float headLength, out float width, out float height)
    {
        if (d < headLength)
        {
            float h = d / headLength;
            width = h < 0.65f ? Mathf.Lerp(0.55f, 1.3f, Mathf.Sin(h / 0.65f * Mathf.PI * 0.5f)) : Mathf.Lerp(1.3f, Neck, (h - 0.65f) / 0.35f);
            height = width * Mathf.Lerp(0.55f, 0.9f, h * h * h);
            // The snout rounded off: the front of the head closes in a dome to its tip.
            if (h < SnoutLength)
            {
                float u = 1f - h / SnoutLength;
                float dome = Mathf.Max(0.12f, Mathf.Sqrt(1f - u * u));
                width *= dome;
                height *= dome;
            }
            return;
        }
        float b = (d - headLength) / (length - headLength);
        float girth = b < 0.15f ? Mathf.Lerp(Neck, 1f, Mathf.SmoothStep(0f, 1f, b / 0.15f)) : b < 0.55f ? 1f : Mathf.Lerp(1f, 0.05f, Mathf.Pow((b - 0.55f) / 0.45f, 1.3f));
        width = girth;
        height = girth * 0.9f;
    }

    private BoneWeight WeightAt(float d)
    {
        for (int i = 0; i < Bones - 1; i++)
        {
            if (d <= boneAt[i + 1])
            {
                float w = Mathf.InverseLerp(boneAt[i], boneAt[i + 1], d);
                return new BoneWeight { boneIndex0 = i, weight0 = 1f - w, boneIndex1 = i + 1, weight1 = w };
            }
        }
        return new BoneWeight { boneIndex0 = Bones - 1, weight0 = 1f };
    }

    private static Material Plain(string name, Color colour, float smooth)
    {
        Material made = new Material(Snakes.Skin()) { name = name };
        made.SetColor("_Tint", colour);
        made.SetColor("_BaseColor", colour);
        made.SetFloat("_BaseSmooth", smooth);
        return made;
    }

    private static void Part(PrimitiveType shape, Transform parent, Vector3 at, Quaternion turn, Vector3 size, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(shape);
        DestroyImmediate(part.GetComponent<Collider>());
        part.name = shape == PrimitiveType.Sphere ? "Eye" : "Tongue Part";
        part.transform.SetParent(parent, false);
        part.transform.localPosition = at;
        part.transform.localRotation = turn;
        part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    private void Update()
    {
        if (photonView.IsMine)
        {
            Think();
        }
        else
        {
            head = (netHead - head).sqrMagnitude > JumpDistance * JumpDistance ? netHead : Vector3.Lerp(head, netHead, Time.deltaTime * 12f);
            facing = Vector3.Slerp(facing, netFacing, Time.deltaTime * 12f).normalized;
        }
        transform.position = head;
        Pose();
        Voice();
    }

    /// <summary>A looping sound on the snake, silent until it is needed, through the game's sound-effects mixer (its volume setting).</summary>
    private AudioSource Loop(AudioClip clip, float reach)
    {
        if (clip == null)
        {
            return null;
        }
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.minDistance = 1f;
        source.maxDistance = reach;
        // Falls away about as the game's sounds do, and is gone at its reach.
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.1f, 0.72f), new Keyframe(0.3f, 0.36f), new Keyframe(0.6f, 0.12f), new Keyframe(1f, 0f)));
        source.outputAudioMixerGroup = SFX_Player.instance != null ? SFX_Player.instance.defaultMixerGroup : null;
        source.time = Random.Range(0f, clip.length * 0.95f);
        return source;
    }

    /// <summary>
    /// Its sounds, on every game: the slither while it crawls (louder and a little higher the faster it goes), the rattle
    /// while it hunts a scout (chasing, jumping, waiting to strike again); both fade in and out as the bees' do. Stunned
    /// by a hit, it falls silent at once (the hit itself is heard, see RPC_Struck).
    /// </summary>
    private void Voice()
    {
        float moved = Time.deltaTime > 0f ? Vector3.Distance(head, heardAt) / Time.deltaTime : 0f;
        heardAt = head;
        if (moved > 15f)
        {
            moved = 0f;
        }
        pace = Mathf.Lerp(pace, moved, Time.deltaTime * 6f);
        float quick = Mathf.Clamp01(pace / Snakes.GroundChaseSpeed);
        bool stunned = state == State.Stunned;
        bool crawling = state != State.Sleeping && state != State.Lunging && state != State.Waking && state != State.Rearing && !stunned;
        bool hunting = state == State.Chasing || state == State.Lunging || state == State.Resting || state == State.Waking || state == State.Rearing;
        Fade(slither, crawling && pace > 0.05f ? Snakes.SlitherVolume * Mathf.Clamp01(0.6f + 0.4f * quick) : 0f, stunned ? StunnedHush : 1.5f, (IsTree ? 0.85f : 1f) * (0.9f + 0.25f * quick));
        Fade(rattle, hunting ? Snakes.RattleVolume : 0f, stunned ? StunnedHush : 2f, IsTree ? 0.85f : 1.05f);
    }

    /// <summary>Stunned, its sounds die away this fast (volume a second): in about a tenth of a second.</summary>
    private const float StunnedHush = 8f;

    private static void Fade(AudioSource source, float to, float speed, float pitch)
    {
        if (source == null)
        {
            return;
        }
        source.volume = Mathf.MoveTowards(source.volume, to, speed * Time.deltaTime);
        source.pitch = pitch;
        if (source.volume > 0.001f)
        {
            if (!source.isPlaying)
            {
                source.Play();
            }
        }
        else if (source.isPlaying && to <= 0f)
        {
            source.Pause();
        }
    }

    /// <summary>The host's hunt.</summary>
    private void Think()
    {
        float now = Time.time;
        if (Puppet && state != State.Lunging && state != State.Waking && state != State.Rearing)
        {
            // Told what to do (the tests' slow look at its moves): between moves it lies where it is.
            Settle();
            return;
        }
        Character nearest = Nearest(out float distance);
        if (state != State.Lunging && !SomeoneWithin(120f))
        {
            return;
        }
        switch (state)
        {
            case State.Sleeping:
                if (nearest != null && distance <= Snakes.TreeWakeDistance)
                {
                    GoFor(nearest);
                    state = State.Waking;
                    stateUntil = now + Snakes.UncoilSeconds;
                }
                break;
            case State.Rearing:
                if (Puppet)
                {
                    if (now >= stateUntil)
                    {
                        StartLunge(puppetAim);
                    }
                    break;
                }
                if (target == null || !target.data.fullyConscious || OutOfTropics(target))
                {
                    target = null;
                    state = IsTree ? State.Returning : State.Wandering;
                    break;
                }
                Vector3 facingScout = Vector3.ProjectOnPlane(target.Center - head, Vector3.up);
                if (facingScout.sqrMagnitude > 0.01f)
                {
                    facing = Vector3.Slerp(facing, facingScout.normalized, Time.deltaTime * 10f).normalized;
                }
                if (now >= stateUntil && !target.warping)
                {
                    StartLunge(Strike(target));
                }
                break;
            case State.Waking:
                if (Puppet)
                {
                    if (now >= stateUntil)
                    {
                        StartLunge(puppetAim);
                    }
                    break;
                }
                if (target == null || !target.data.fullyConscious || Vector3.Distance(head, target.Center) > Snakes.TreeWakeDistance * 2f || OutOfTropics(target))
                {
                    // The scout is gone: back to sleep.
                    target = null;
                    state = State.Sleeping;
                    break;
                }
                Vector3 toScout = Vector3.ProjectOnPlane(target.Center - head, Vector3.up);
                if (toScout.sqrMagnitude > 0.01f)
                {
                    facing = Vector3.Slerp(facing, toScout.normalized, Time.deltaTime * 6f).normalized;
                }
                if (now >= stateUntil)
                {
                    StartLunge(Strike(target));
                }
                break;
            case State.Wandering:
                if (nearest != null && distance <= sight && now >= nextLook && Sees(nearest) && !OutOfTropics(nearest))
                {
                    GoFor(nearest);
                    state = State.Chasing;
                    break;
                }
                if (now >= nextWander || Flat(head, wanderTo) < 0.5f)
                {
                    wanderTo = WanderSpot();
                    nextWander = now + Random.Range(3f, 6f);
                }
                Crawl(wanderTo, Snakes.WanderSpeed);
                break;
            case State.Chasing:
                if (target == null || !target.data.fullyConscious || OutOfTropics(target))
                {
                    // Gone, or out of the Tropics (it does not follow them there).
                    target = null;
                    state = IsTree ? State.Returning : State.Wandering;
                    break;
                }
                float away = Vector3.Distance(head, target.Center);
                if (away > (IsTree ? Snakes.TreeGiveUpDistance : Snakes.GroundGiveUpDistance) && now - chaseStart >= Snakes.MinChaseSeconds)
                {
                    target = null;
                    state = IsTree ? State.Returning : State.Wandering;
                    break;
                }
                if (away <= Snakes.LungeFrom && !target.warping)
                {
                    state = State.Rearing;
                    stateUntil = now + Snakes.RearSeconds;
                    break;
                }
                Crawl(target.Center, Snakes.ChaseSpeed(IsTree));
                break;
            case State.Lunging:
                float t = Mathf.Clamp01(1f - (stateUntil - now) / lungeTime);
                float reach = 1f - (1f - t) * (1f - t);
                Vector3 along = Vector3.Lerp(lungeFrom, lungeTo, reach);
                head = along + Vector3.up * Mathf.Sin(t * Mathf.PI) * StrikeArc;
                Vector3 dir = lungeTo - lungeFrom;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    facing = dir.normalized;
                }
                if (t >= 1f)
                {
                    lastBitten = null;
                    if (target != null && target.data.fullyConscious && !target.warping && Vector3.Distance(head, target.Center) <= Snakes.BiteReach)
                    {
                        photonView.RPC(nameof(RPC_Bite), RpcTarget.All, target.photonView.ViewID);
                        lastBitten = target;
                    }
                    state = State.Resting;
                    stateUntil = now + Snakes.RestAfterBite;
                    TurnOntoCircle();
                }
                break;
            case State.Resting:
                if (now >= stateUntil)
                {
                    if (lastBitten != null)
                    {
                        // It may strike again: another scout within its reach for aggression is the next it goes for.
                        Character other = Nearest(out float otherDistance, lastBitten);
                        if (other != null && otherDistance <= AggroRange)
                        {
                            GoFor(other);
                        }
                        lastBitten = null;
                    }
                    state = target != null ? State.Chasing : IsTree ? State.Returning : State.Wandering;
                    break;
                }
                KeepOff();
                break;
            case State.Stunned:
                if (now >= stateUntil)
                {
                    // The stun over: it goes for the scout who stunned it (the first, if more did), when they are within its reach for aggression.
                    if (stunnedBy != null && Hunts(stunnedBy) && Vector3.Distance(stunnedBy.Center, head) <= AggroRange)
                    {
                        GoFor(stunnedBy);
                    }
                    stunnedBy = null;
                    state = target != null ? State.Chasing : IsTree ? State.Returning : State.Wandering;
                    break;
                }
                if (now < shoveUntil)
                {
                    Shoved();
                }
                // Then it lies curled up where it was knocked to (see Pose) until it may strike again.
                break;
            case State.Returning:
                if (nearest != null && distance <= Snakes.TreeWakeDistance)
                {
                    GoFor(nearest);
                    state = State.Chasing;
                    onWay = false;
                    clinging = false;
                    break;
                }
                if (now - returningSince > ReturnGiveUp && (nearest == null || distance > UnseenDistance))
                {
                    // It could not find its way: back at its spot, where nobody sees it.
                    AtHome();
                    break;
                }
                if (way != null)
                {
                    ClimbHome();
                    break;
                }
                if (Flat(head, home) > ClimbReach)
                {
                    Crawl(home, Snakes.WanderSpeed * 1.5f);
                }
                else
                {
                    head = Vector3.MoveTowards(head, home, Time.deltaTime * ClimbSpeed);
                    if (Vector3.Distance(head, home) < 0.1f)
                    {
                        state = State.Sleeping;
                    }
                }
                break;
        }
        if (state == State.Returning && lastState != State.Returning)
        {
            returningSince = now;
        }
        if (state != State.Returning)
        {
            onWay = false;
        }
        if (state != State.Returning)
        {
            clinging = false;
        }
        lastState = state;
        if (state != State.Lunging && state != State.Sleeping && state != State.Waking && !clinging)
        {
            Settle();
        }
    }

    /// <summary>Home along its way: it crawls to the foot of its tree, then climbs the way it climbed before, winding up the trunk and the branches.</summary>
    private void ClimbHome()
    {
        if (!onWay)
        {
            // The nearest point of its way (the foot of the tree if it is down there, or on up the tree if it stayed up).
            int nearest = 0;
            for (int w = 1; w < way.Length; w++)
            {
                if ((way[w] - head).sqrMagnitude < (way[nearest] - head).sqrMagnitude)
                {
                    nearest = w;
                }
            }
            if (Flat(head, way[nearest]) > 0.8f || Mathf.Abs(head.y - way[nearest].y) > 1.5f)
            {
                stuckFor = Crawl(way[nearest], Snakes.WanderSpeed * 1.5f) ? 0f : stuckFor + Time.deltaTime;
                if (stuckFor < 2f || Vector3.Distance(head, way[nearest]) > 6f)
                {
                    return;
                }
                // Stuck close by (an edge between it and its way): it slithers straight across to it.
                head = Vector3.MoveTowards(head, way[nearest], ClimbSpeed * Time.deltaTime);
                clinging = true;
                if (Vector3.Distance(head, way[nearest]) > 0.1f)
                {
                    return;
                }
            }
            stuckFor = 0f;
            onWay = true;
            alongWay = wayAt[nearest];
        }
        alongWay += ClimbSpeed * Time.deltaTime;
        float end = wayAt[wayAt.Length - 1];
        if (alongWay >= end)
        {
            AtHome();
            return;
        }
        Vector3 next = OnWay(alongWay);
        Vector3 flat = Vector3.ProjectOnPlane(next - head, Vector3.up);
        if (flat.sqrMagnitude > 1e-6f)
        {
            facing = flat.normalized;
        }
        head = next;
        clinging = true;
    }

    /// <summary>The point of its way this far along it from the foot.</summary>
    private Vector3 OnWay(float along)
    {
        int i = 1;
        while (i < wayAt.Length - 1 && wayAt[i] < along)
        {
            i++;
        }
        return Vector3.Lerp(way[i - 1], way[i], Mathf.InverseLerp(wayAt[i - 1], wayAt[i], along));
    }

    /// <summary>For the tests: on its way up its tree, this far from the foot, climbing on home from there.</summary>
    internal void ClimbFrom(float along)
    {
        if (way == null)
        {
            return;
        }
        Puppet = false;
        target = null;
        alongWay = Mathf.Clamp(along, 0f, wayAt[wayAt.Length - 1] - 0.1f);
        head = OnWay(alongWay);
        onWay = true;
        clinging = true;
        state = State.Returning;
        lastState = State.Returning;
        returningSince = Time.time;
    }

    /// <summary>For the tests: back at its spot at once, asleep.</summary>
    internal void GoHomeNow() => AtHome();

    private void AtHome()
    {
        head = home;
        target = null;
        onWay = false;
        clinging = false;
        fallSpeed = 0f;
        state = State.Sleeping;
    }

    /// <summary>For the tests (a calm snake, close up): it notices no scout.</summary>
    internal bool Unaware;

    /// <summary>For the tests: it wanders (calm) towards a point, and keeps on there for a while.</summary>
    internal void WanderTowards(Vector3 point)
    {
        target = null;
        state = State.Wandering;
        wanderTo = point;
        nextWander = Time.time + 60f;
    }

    /// <summary>For the tests (another snake under test): it notices no scout from now on and leaves the one it is after.</summary>
    internal void Ignore()
    {
        Unaware = true;
        target = null;
        if (state == State.Chasing || state == State.Rearing || state == State.Resting || state == State.Waking)
        {
            state = IsTree ? State.Returning : State.Wandering;
        }
    }

    /// <summary>For the tests: a ground snake notices no scout and wanders about by itself, picking its own way, as with nobody near.</summary>
    internal void WanderAlone()
    {
        Unaware = true;
        Puppet = false;
        target = null;
        state = State.Wandering;
        nextWander = 0f;
    }

    /// <summary>For the tests (a slow look at its moves, on the host): it does nothing by itself, only what it is told below.</summary>
    internal bool Puppet;
    private Vector3 puppetAim;

    /// <summary>For the tests: laid out at a spot, facing a way, calm (a tree snake asleep, curled up, if the spot is its own).</summary>
    internal void PlaceAt(Vector3 at, Vector3 towards)
    {
        target = null;
        head = at;
        Vector3 flat = Vector3.ProjectOnPlane(towards - at, Vector3.up);
        facing = flat.sqrMagnitude > 1e-4f ? flat.normalized : facing;
        state = IsTree && Vector3.Distance(at, home) < 0.3f ? State.Sleeping : State.Wandering;
        Reseed();
    }

    /// <summary>For the tests: it rears up and strikes at a point, as it would at a scout, with nobody to bite.</summary>
    internal void StrikeAt(Vector3 point)
    {
        target = null;
        puppetAim = point;
        Vector3 flat = Vector3.ProjectOnPlane(point - head, Vector3.up);
        if (flat.sqrMagnitude > 1e-4f)
        {
            facing = flat.normalized;
        }
        state = State.Rearing;
        stateUntil = Time.time + Snakes.RearSeconds;
    }

    /// <summary>For the tests: it uncoils towards a point (a tree snake, curled up), then strikes at it.</summary>
    internal void UncoilAt(Vector3 point)
    {
        target = null;
        puppetAim = point;
        Vector3 flat = Vector3.ProjectOnPlane(point - head, Vector3.up);
        if (flat.sqrMagnitude > 1e-4f)
        {
            facing = flat.normalized;
        }
        state = State.Waking;
        stateUntil = Time.time + Snakes.UncoilSeconds;
    }

    /// <summary>For the tests: climbing (clinging to a steep surface), and the length of its way up.</summary>
    internal bool Clinging => clinging;
    internal float AlongWay => onWay ? alongWay : -1f;
    internal float WayLength => wayAt != null ? wayAt[wayAt.Length - 1] : 0f;

    /// <summary>In the air (after a jump, shoved onto or off something, crawled off a ledge), the head drops to the ground.</summary>
    private void Settle()
    {
        if (!Physics.Raycast(head + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, 200f, HelperFunctions.terrainMapMask))
        {
            // Nothing under it: somehow inside the map's rock or under it. Lost for 2 s, it is back at its spot.
            fallSpeed = 0f;
            lostFor += Time.deltaTime;
            if (lostFor > 2f)
            {
                lostFor = 0f;
                target = null;
                head = home;
                state = IsTree ? State.Sleeping : State.Wandering;
                Plugin.Debug("Snake: lost under the map; back at its spot.");
            }
            return;
        }
        lostFor = 0f;
        float floor = ground.point.y + radius;
        if (head.y <= floor + 0.01f)
        {
            fallSpeed = 0f;
            return;
        }
        fallSpeed = Mathf.Min(fallSpeed + Physics.gravity.magnitude * Time.deltaTime, MaxFallSpeed);
        head.y = Mathf.Max(floor, head.y - fallSpeed * Time.deltaTime);
    }

    /// <summary>Where a jump at a scout ends: low on them (a snake strikes at the legs), a little short of them.</summary>
    private Vector3 Strike(Character scout)
    {
        Vector3 centre = scout.Center;
        Vector3 feet = Physics.Raycast(centre, Vector3.down, out RaycastHit ground, 3f, HelperFunctions.terrainMapMask) ? ground.point : centre + Vector3.down * 0.9f;
        Vector3 aim = Vector3.Lerp(feet, centre, 0.45f);
        Vector3 short_ = aim - head;
        return short_.sqrMagnitude > 0.09f ? aim - short_.normalized * 0.25f : aim;
    }

    private void StartLunge(Vector3 to)
    {
        // From the S it reared up in (or uncoiled into), where its head is now drawn.
        bool reared = state == State.Rearing || state == State.Waking;
        state = State.Lunging;
        lungeFrom = reared && spine != null ? spine[0] : head;
        head = lungeFrom;
        lungeTo = to;
        lungeTime = Mathf.Max(Snakes.LungeSeconds, Vector3.Distance(lungeFrom, lungeTo) / LungeSpeed);
        stateUntil = Time.time + lungeTime;
    }

    /// <summary>
    /// Waiting to strike again (after a strike, bitten or missed): it keeps moving and keeps off, slithering round the
    /// scout (turning the other way when something blocks it), or after them at a distance if they are further away; with
    /// no scout to go for, a ground snake wanders and a tree snake stays where it is (it heads home once the wait is over).
    /// </summary>
    private void KeepOff()
    {
        if (target == null || !target.data.fullyConscious)
        {
            if (!IsTree)
            {
                Crawl(wanderTo, Snakes.WanderSpeed);
            }
            return;
        }
        if (Flat(head, target.Center) > Snakes.CircleRadius * 2.5f)
        {
            Crawl(target.Center, Snakes.ChaseSpeed(IsTree) * 0.7f);
            return;
        }
        Circle();
    }

    /// <summary>Turned onto its circle round the scout (and a little away from them), not towards them.</summary>
    private void TurnOntoCircle()
    {
        circleSide = Random.value < 0.5f ? -1f : 1f;
        if (target == null)
        {
            return;
        }
        Vector3 outward = Vector3.ProjectOnPlane(head - target.Center, Vector3.up);
        if (outward.sqrMagnitude > 1e-4f)
        {
            outward.Normalize();
            facing = (Vector3.Cross(Vector3.up, outward) * circleSide + outward * 1.2f).normalized;
        }
    }

    /// <summary>Knocked back by a hit: the head slides with the shove, over the ground (off an edge it falls).</summary>
    private void Shoved()
    {
        Vector3 next = head + shove * Time.deltaTime;
        if (GroundAt(next, out Vector3 ground, out bool drop, out Collider under) && !LeavesTropics(under, next))
        {
            head = drop || head.y - ground.y > AirborneHeight ? new Vector3(next.x, head.y, next.z) : ground;
        }
    }

    private void Circle()
    {
        if (target == null || !target.data.fullyConscious)
        {
            return;
        }
        Vector3 outward = Vector3.ProjectOnPlane(head - target.Center, Vector3.up);
        float away = outward.magnitude;
        outward = away > 0.01f ? outward / away : -facing;
        Vector3 round = Vector3.Cross(Vector3.up, outward) * circleSide;
        // Along the circle, drawn back onto it from inside or outside.
        Vector3 way = (round + outward * Mathf.Clamp((Snakes.CircleRadius - away) * 1.5f, -1f, 3f)).normalized;
        if (!Crawl(head + way * 2f, Snakes.CircleSpeed))
        {
            circleSide = -circleSide;
        }
    }

    /// <summary>A step of crawling towards a point (in S-curves); false if a wall or the void is in the way.</summary>
    private bool Crawl(Vector3 towards, float speed)
    {
        Vector3 dir = towards - head;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
        {
            return true;
        }
        dir.Normalize();
        facing = Vector3.Slerp(facing, dir, Time.deltaTime * 6f).normalized;
        wave += Time.deltaTime * speed * SerpentPace * WavesPerLength / length * Mathf.PI * 2f;
        Vector3 heading = Quaternion.AngleAxis(Mathf.Sin(wave) * SerpentDegrees, Vector3.up) * facing;
        Vector3 next = head + heading * speed * SerpentPace * Time.deltaTime;
        if (!GroundAt(next, out Vector3 ground, out bool drop, out Collider under) || (drop && !Airborne()) || LeavesTropics(under, next))
        {
            // A wall, nothing at all below, a high edge (it does not crawl off one) or another biome's ground (it keeps to
            // the Tropics): it does not go there (wandering, it picks somewhere else to go).
            nextWander = 0f;
            return false;
        }
        // Over the ground it keeps to it; over a drop, or in the air, it goes on and falls (see Settle).
        head = drop || head.y - ground.y > AirborneHeight ? new Vector3(next.x, head.y, next.z) : ground;
        return true;
    }

    /// <summary>In the air now (falling, or thrown off something): more than a little above the ground under it.</summary>
    private bool Airborne()
    {
        return !Physics.Raycast(head + Vector3.up * 0.3f, Vector3.down, out RaycastHit under, 0.3f + radius + AirborneHeight, HelperFunctions.terrainMapMask);
    }

    /// <summary>
    /// The ground at a point (and what it is): within 1.5 m above it to 4.5 m below (true), further down (true, a drop), or
    /// nothing below at all, the point being inside a wall or over the void (false).
    /// </summary>
    private bool GroundAt(Vector3 at, out Vector3 ground, out bool drop, out Collider under)
    {
        drop = false;
        if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out RaycastHit near, 6f, HelperFunctions.terrainMapMask))
        {
            ground = near.point + Vector3.up * radius;
            under = near.collider;
            return true;
        }
        if (Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, out RaycastHit far, 200f, HelperFunctions.terrainMapMask))
        {
            ground = far.point + Vector3.up * radius;
            under = far.collider;
            drop = true;
            return true;
        }
        ground = at;
        under = null;
        return false;
    }

    /// <summary>Another biome's ground, where a snake does not go (the Tropics' own, or ground of no biome, is its to crawl).</summary>
    private static bool Foreign(Collider ground) => Map.IsOtherBiome(ground, Biome.BiomeType.Tropics);

    /// <summary>
    /// Whether going onto this ground would take it out of the Tropics: onto another biome's ground from the Tropics' (out
    /// of them, knocked or fallen, it may go wherever takes it nearer its spot, back into them).
    /// </summary>
    private bool LeavesTropics(Collider ground, Vector3 to)
    {
        return Foreign(ground) && (!OutOfTropics() || Flat(to, home) >= Flat(head, home));
    }

    /// <summary>Out of the Tropics now: on another biome's ground.</summary>
    private bool OutOfTropics()
    {
        return Physics.Raycast(head + Vector3.up * 0.5f, Vector3.down, out RaycastHit under, 2f, HelperFunctions.terrainMapMask) && Foreign(under.collider);
    }

    /// <summary>A scout out of the Tropics (on another biome's ground): a snake does not go after them there.</summary>
    private static bool OutOfTropics(Character scout)
    {
        return Physics.Raycast(scout.Center, Vector3.down, out RaycastHit under, 4f, HelperFunctions.terrainMapMask) && Foreign(under.collider);
    }

    /// <summary>Somewhere to wander to, within its range of its spot, on the Tropics' ground (out of them, its spot itself).</summary>
    private Vector3 WanderSpot()
    {
        if (!OutOfTropics())
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 r = Random.insideUnitCircle * Snakes.WanderRange;
                Vector3 spot = home + new Vector3(r.x, 0f, r.y);
                if (GroundAt(spot, out _, out _, out Collider under) && !Foreign(under))
                {
                    return spot;
                }
            }
        }
        return home;
    }

    private bool Sees(Character scout)
    {
        nextLook = Time.time + 0.5f;
        return HelperFunctions.LineCheck(head + Vector3.up * 0.3f, scout.Center, HelperFunctions.LayerType.TerrainMap).transform == null;
    }

    private Character Nearest(out float distance, Character skip = null)
    {
        distance = float.MaxValue;
        Character best = null;
        foreach (Character scout in Character.AllCharacters)
        {
            if (scout == skip || !Hunts(scout))
            {
                continue;
            }
            float d = Vector3.Distance(scout.Center, head);
            if (d < distance)
            {
                distance = d;
                best = scout;
            }
        }
        return best;
    }

    /// <summary>Goes for a scout: its hunt of them starts now (it keeps after them at least Snakes.MinChaseSeconds).</summary>
    private void GoFor(Character scout)
    {
        target = scout;
        chaseStart = Time.time;
    }

    private float chaseStart;

    /// <summary>A scout it can go for: awake and about (as the game's mobs choose), and not in the middle of a warp (nothing reaches them then).</summary>
    private bool Hunts(Character scout)
    {
        return scout != null && !scout.isBot && scout.data.fullyConscious && !scout.warping && !Unaware;
    }

    /// <summary>How near a scout has to be for it to go for them: a ground snake's sight (the scorpions'), a tree snake's waking distance.</summary>
    private float AggroRange => IsTree ? Snakes.TreeWakeDistance : sight;

    /// <summary>Anyone about within a distance (out cold or not): it goes on as usual where it can be seen; with nobody near, it lies still.</summary>
    private bool SomeoneWithin(float range)
    {
        foreach (Character scout in Character.AllCharacters)
        {
            if (scout != null && !scout.isBot && !scout.data.dead && Vector3.Distance(scout.Center, head) <= range)
            {
                return true;
            }
        }
        return false;
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    /// <summary>
    /// The skeleton: the bones lie along the track the head has crawled (so the body bends where the head turned and
    /// never folds), or coiled up while it sleeps at home; each bone faces the bone ahead of it (see Frames).
    /// </summary>
    private void Pose()
    {
        if ((head - lastHead).sqrMagnitude > JumpDistance * JumpDistance)
        {
            // Pushed or moved at once: the body lies straight behind the head again, and drops to the ground under it.
            Reseed();
        }
        lastHead = head;
        bool curledByHit = state == State.Stunned && Time.time >= shoveUntil;
        if ((state == State.Sleeping && Vector3.Distance(head, home) < 0.3f) || state == State.Waking || curledByHit)
        {
            for (int i = 0; i < Bones; i++)
            {
                spineUp[i] = Vector3.up;
            }
            if (!coiled)
            {
                // Settling down (or hit): it curls up from how it lies now, over a moment.
                System.Array.Copy(spine, curledFrom, Bones);
                curlStart = Time.time;
                curlSeconds = curledByHit ? HitCurlSeconds : CurlSeconds;
                coiled = true;
            }
            if (state == State.Waking && lastPosed != State.Waking)
            {
                wakeStart = Time.time;
                uncoilTurning = false;
            }
            Coil();
            for (int i = 0; i < Bones; i++)
            {
                // It lies on its coil (uncoiling, its neck rises off it).
                spineFloor[i] = coilPose[i].y;
            }
            if (state == State.Waking)
            {
                Uncoil(Mathf.Clamp01((Time.time - wakeStart) / Snakes.UncoilSeconds));
            }
            float curl = Mathf.Clamp01((Time.time - curlStart) / curlSeconds);
            if (curl < 1f)
            {
                float k = Mathf.SmoothStep(0f, 1f, curl);
                for (int i = 0; i < Bones; i++)
                {
                    spine[i] = Vector3.Lerp(curledFrom[i], spine[i], k);
                }
            }
            if (state == State.Waking)
            {
                // Rounded off more and more as it rises, by its end as much as the strike rounds it off (see Smooth).
                System.Array.Copy(spine, drawnRaw, Bones);
                Smooth(Mathf.SmoothStep(0f, 1f, (Time.time - wakeStart) / (Snakes.UncoilSeconds * 0.9f)));
            }
        }
        else
        {
            if (coiled || (reared && state != State.Rearing))
            {
                // Waking, or striking out of the S it reared up in: the body goes on from how it was drawn.
                coiled = false;
                reared = false;
                trail.Clear();
                // Out of its uncoiling or its S, the track is laid from how it was drawn before it was rounded off, each
                // bone at first where it lay along it (see HandOverLeft): rounded off again as before, the strike goes on
                // from just how it looked.
                bool fromDrawn = lastPosed == State.Waking || lastPosed == State.Rearing;
                Vector3[] laidFrom = fromDrawn ? drawnRaw : spine;
                float drawn = 0f;
                for (int i = 1; i < Bones; i++)
                {
                    trail.Add(Point(laidFrom[i]));
                    drawn += Vector3.Distance(laidFrom[i - 1], laidFrom[i]);
                    handOverShift[i] = fromDrawn ? drawn - boneAt[i] : 0f;
                }
                if (fromDrawn)
                {
                    handOverStart = Time.time;
                }
            }
            if (trail.Count == 0 || (head - trail[0].at).sqrMagnitude >= TrailStep * TrailStep)
            {
                trail.Insert(0, Point(head));
            }
            ShoveBody();
            Sag();
            Follow();
            Smooth();
            if (state == State.Rearing)
            {
                if (!reared)
                {
                    reared = true;
                    rearStart = Time.time;
                }
                RearUp(Mathf.Clamp01((Time.time - rearStart) / Snakes.RearSeconds));
                // Rounded off more and more as it rears up, by the end as much as the strike rounds it off (see Smooth).
                System.Array.Copy(spine, drawnRaw, Bones);
                Smooth(Mathf.SmoothStep(0f, 1f, (Time.time - rearStart) / (Snakes.RearSeconds * 0.9f)));
            }
        }
        Frames();
        lastPosed = state;
        Gape();
        Flick();
    }

    /// <summary>The share of the head's turn a raised piece lying level takes, against one rising straight up (1).</summary>
    private const float TwistLevel = 0.002f;

    /// <summary>
    /// Each bone faces the bone ahead of it, its back turned away from what it lies on (the ground, the bark, its own coil),
    /// carried on from bone to bone from the tail. Where the body is raised off what it lies on (a neck reared up or
    /// uncoiling, a strike), it is carried on up the raised part as that bends, and the turn the head needs (upright in
    /// the air, its back away from what it lies on once down) is spread along the raised part where it rises steeply,
    /// going on from how it was spread the frame before (carried from the head instead, a bone's back pulled round from
    /// the other way flipped this way and that where a raised neck met the coil); a raised part reaching out is then
    /// turned the rest of the way to its own back.
    /// </summary>
    private void Frames()
    {
        for (int i = 0; i < Bones; i++)
        {
            Vector3 ahead = i == 0 ? spine[0] - spine[1] : spine[i - 1] - spine[Mathf.Min(i + 1, Bones - 1)];
            aheads[i] = ahead.sqrMagnitude > 1e-8f ? ahead.normalized : i > 0 ? aheads[i - 1] : facing;
            holds[i] = Mathf.Clamp01(1f - (spine[i].y - spineFloor[i]) / (radius * 2f));
        }
        // From the tail: carried on as the body bends, turned away from what it lies on (as far as it lies along it, and
        // never pulled round from the other way: a part arching up off the ground has its back to the sky no longer).
        Vector3 back = spineUp[Bones - 1];
        for (int i = Bones - 1; i >= 0; i--)
        {
            if (i < Bones - 1)
            {
                back = Quaternion.FromToRotation(aheads[i + 1], aheads[i]) * back;
            }
            back = Vector3.ProjectOnPlane(back, aheads[i]);
            if (back.sqrMagnitude < 1e-6f)
            {
                back = Vector3.ProjectOnPlane(Vector3.up, aheads[i]);
            }
            if (back.sqrMagnitude < 1e-6f)
            {
                back = Vector3.ProjectOnPlane(Vector3.forward, aheads[i]);
            }
            back.Normalize();
            Vector3 surface = Vector3.ProjectOnPlane(spineUp[i], aheads[i]);
            if (surface.sqrMagnitude > 1e-6f)
            {
                float pull = 0.45f * holds[i] * surface.sqrMagnitude * Mathf.Clamp01(Vector3.Dot(back, surface.normalized) * 2f);
                if (pull > 1e-4f)
                {
                    back = Vector3.Slerp(back, surface.normalized, pull);
                }
            }
            backs[i] = back;
        }
        // Wherever a part of the body is raised (a neck reared up or uncoiling, a strike's arch): the head's back as it
        // should be (upright while it is in the air, turned away from what it lies on once it is down), and the turn
        // that takes spread back along the raised part, nearly all of it where that rises steeply (each bone turned by
        // the share from it to the tail): turned there, a neck going up only faces another way; turned where it
        // reaches out, an arch or a strike lay on its side, its belly to the side or upwards.
        float raised = 0f;
        float spread = 0f;
        for (int i = 0; i < Bones - 1; i++)
        {
            Vector3 piece = spine[i] - spine[i + 1];
            float lifted = piece.magnitude * (1f - holds[i]);
            float rising = piece.sqrMagnitude > 1e-8f ? Mathf.Max(0f, piece.normalized.y) : 0f;
            rising *= rising;
            twists[i] = lifted * (TwistLevel + rising * rising);
            raised += lifted;
            spread += twists[i];
        }
        if (raised > 1e-4f)
        {
            float roll = neckRolling ? neckRoll : 0f;
            Vector3 lying = Vector3.ProjectOnPlane(spineUp[0], aheads[0]);
            Vector3 upright = Vector3.ProjectOnPlane(Vector3.up, aheads[0]);
            Vector3 wanted = lying.sqrMagnitude > 0.01f && upright.sqrMagnitude > 0.01f
                ? Vector3.Slerp(lying.normalized, upright.normalized, Mathf.SmoothStep(0f, 1f, 1f - holds[0]))
                : upright;
            if (wanted.sqrMagnitude > 0.01f)
            {
                float turn = Vector3.SignedAngle(backs[0], wanted.normalized, aheads[0]);
                roll = neckRolling ? neckRoll + Mathf.DeltaAngle(neckRoll, turn) : turn;
                if (Mathf.Abs(roll) > 270f)
                {
                    roll -= 360f * Mathf.Sign(roll);
                }
            }
            neckRoll = roll;
            neckRolling = true;
            float rest = spread;
            for (int i = 0; i < Bones && rest > 1e-8f; i++)
            {
                backs[i] = Quaternion.AngleAxis(roll * rest / spread, aheads[i]) * backs[i];
                if (i < Bones - 1)
                {
                    rest -= twists[i];
                }
            }
            // What that leaves over along a raised stretch reaching out (not every part of an arch, or of a strike sagging
            // as it goes, needs the head's turn): each such bone turned the rest of the way to its own back, as far as it
            // is raised and reaches out (rising steeply, it is left to the turn spread from the head).
            for (int i = 1; i < Bones; i++)
            {
                float lifted = Mathf.SmoothStep(0f, 1f, 1f - holds[i]);
                Vector3 sky = Vector3.ProjectOnPlane(Vector3.up, aheads[i]);
                float reaches = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, sky.magnitude));
                if (lifted * reaches < 1e-3f)
                {
                    continue;
                }
                Vector3 lies = Vector3.ProjectOnPlane(spineUp[i], aheads[i]);
                Vector3 own = lies.sqrMagnitude > 0.01f ? Vector3.Slerp(lies.normalized, sky.normalized, lifted) : sky.normalized;
                backs[i] = Quaternion.AngleAxis(Vector3.SignedAngle(backs[i], own, aheads[i]) * lifted * reaches, aheads[i]) * backs[i];
            }
        }
        else
        {
            neckRolling = false;
        }
        for (int i = 0; i < Bones; i++)
        {
            bones[i].SetPositionAndRotation(spine[i], Quaternion.LookRotation(aheads[i], backs[i]));
        }
    }

    /// <summary>
    /// The mouth opens wide as it strikes (the jaw drops, the head tips up a little, the fangs swing down and forward)
    /// and shuts again after the bite; on every game, from the state the host sends.
    /// </summary>
    private void Gape()
    {
        float wanted = state == State.Lunging ? 1f : state == State.Rearing ? 0.5f : state == State.Waking ? 0.3f : 0f;
        if (lastGape == State.Lunging && state != State.Lunging && glint != null)
        {
            // The strike is over: the scorpion's star glints on the fangs, as after its sting.
            glint.Play(true);
            Snakes.FangGlints++;
        }
        lastGape = state;
        mouth = Mathf.MoveTowards(mouth, wanted, Time.deltaTime * (wanted > mouth ? 12f : 5f));
        float open = Mathf.SmoothStep(0f, 1f, mouth);
        jaw.localRotation = Quaternion.Euler(open * JawDegrees, 0f, 0f);
        Vector3 hingeAt = bones[0].TransformPoint(jaw.localPosition);
        bones[0].rotation *= Quaternion.Euler(-open * HeadLiftDegrees, 0f, 0f);
        bones[0].position += hingeAt - bones[0].TransformPoint(jaw.localPosition);
        Quaternion fang = Quaternion.Slerp(FangFolded, FangBared, Mathf.Clamp01(open * 1.4f - 0.2f));
        foreach (Transform pivot in fangs)
        {
            pivot.localRotation = fang;
        }
    }

    /// <summary>The bones at their distances from the snout along the head's track (straight on behind its end).</summary>
    private void Follow()
    {
        spine[0] = head;
        Vector3 fromUp = trail.Count > 0 ? trail[0].up : Vector3.up;
        float fromFloor = trail.Count > 0 ? Mathf.Min(head.y, trail[0].floor) : head.y;
        spineUp[0] = fromUp;
        spineFloor[0] = fromFloor;
        int b = 1;
        float walked = 0f;
        Vector3 from = head;
        Vector3 back = -facing;
        int used = 0;
        float left = HandOverLeft;
        for (; used < trail.Count && b < Bones; used++)
        {
            Vector3 to = trail[used].at;
            Vector3 toUp = trail[used].up;
            float toFloor = trail[used].floor;
            float step = Vector3.Distance(from, to);
            if (step < 1e-5f)
            {
                continue;
            }
            back = (to - from) / step;
            while (b < Bones && walked + step >= AlongBody(b, left))
            {
                float along = AlongBody(b, left);
                float k = (along - walked) / step;
                spine[b] = from + back * (along - walked);
                spineUp[b] = Vector3.Slerp(fromUp, toUp, k);
                spineFloor[b] = Mathf.Lerp(fromFloor, toFloor, k);
                b++;
            }
            walked += step;
            from = to;
            fromUp = toUp;
            fromFloor = toFloor;
        }
        for (; b < Bones; b++)
        {
            spine[b] = from + back * (AlongBody(b, left) - walked);
            spineUp[b] = fromUp;
            spineFloor[b] = fromFloor;
        }
        // The track behind the tail is no longer needed.
        if (used + 2 < trail.Count)
        {
            trail.RemoveRange(used + 2, trail.Count - used - 2);
        }
    }

    /// <summary>How far along the track from the head a bone lies: its place on the body (as drawn, just after a hand-over).</summary>
    private float AlongBody(int b, float left) => boneAt[b] + handOverShift[b] * left;

    /// <summary>A point of the track where the head is now: it can sink to the ground under it (not while climbing).</summary>
    private TrailPoint Point(Vector3 at)
    {
        float floor = at.y;
        Vector3 up = Vector3.up;
        if (clinging)
        {
            up = BarkFacing(at);
        }
        else if (Physics.Raycast(at + Vector3.up * 0.6f, Vector3.down, out RaycastHit ground, 30f, HelperFunctions.terrainMapMask))
        {
            float onGround = ground.point.y + radius;
            if (at.y < onGround)
            {
                // Below the ground (laid out on a slope): on it.
                at.y = onGround;
            }
            floor = Mathf.Min(at.y, onGround);
            if (at.y - ground.point.y < radius * 3f)
            {
                up = ground.normal;
            }
        }
        return new TrailPoint { at = at, floor = floor, up = up };
    }

    /// <summary>Which way the bark faces at the point of its way nearest to a spot (searched near the last one found).</summary>
    private Vector3 BarkFacing(Vector3 at)
    {
        if (way == null || wayFacesOut == null)
        {
            return Vector3.up;
        }
        int best = Mathf.Clamp(nearestOnWay, 0, way.Length - 1);
        float bestDistance = (way[best] - at).sqrMagnitude;
        int from = Mathf.Max(0, best - 40), to = Mathf.Min(way.Length - 1, best + 40);
        if (bestDistance > 4f)
        {
            from = 0;
            to = way.Length - 1;
        }
        for (int i = from; i <= to; i++)
        {
            float distance = (way[i] - at).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        nearestOnWay = best;
        return wayFacesOut[best];
    }

    /// <summary>The body laid straight behind the head (where the head is now), every 10 cm, each point to sink to the ground under it.</summary>
    private void Reseed()
    {
        trail.Clear();
        Vector3 back = -new Vector3(facing.x, 0f, facing.z);
        back = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.back;
        for (float d = 0.1f; d <= length + 0.15f; d += 0.1f)
        {
            trail.Add(Point(head + back * d));
        }
    }

    /// <summary>
    /// While shoved, the whole track slides with the head (the body is knocked back with it); after, each point finds the
    /// ground under it again (so it settles there, and does not sink into a slope).
    /// </summary>
    private void ShoveBody()
    {
        if (Time.time < shoveUntil)
        {
            shoving = true;
            Vector3 step = shove * Time.deltaTime;
            for (int i = 1; i < trail.Count; i++)
            {
                // Slid with the head, kept on the ground under it (pushed up a slope it would sink into it).
                trail[i] = Point(trail[i].at + step);
            }
            return;
        }
        if (!shoving)
        {
            return;
        }
        shoving = false;
        for (int i = 0; i < trail.Count; i++)
        {
            TrailPoint point = Point(trail[i].at);
            if (point.at.y < point.floor)
            {
                point.at.y = point.floor;
            }
            trail[i] = point;
        }
    }

    /// <summary>The track sinks where it lies above the ground under it: a body left in the air (after a jump, off a ledge) falls.</summary>
    private void Sag()
    {
        float drop = TrailFall * Time.deltaTime * (1f - HandOverLeft);
        for (int i = 0; i < trail.Count; i++)
        {
            TrailPoint point = trail[i];
            if (point.at.y > point.floor)
            {
                point.at.y = Mathf.Max(point.floor, point.at.y - drop);
                trail[i] = point;
            }
        }
    }

    /// <summary>
    /// Asleep: curled up where it lies, its loops side by side (never tighter than its body is thick), each lying on what
    /// is under it, from the middle out to the tail; the head and neck rest on top of the coil. Worked out once for a spot.
    /// </summary>
    private void Coil()
    {
        // Uncoiling, the coil stays as it lay (only the rising neck turns towards the scout, see Uncoil): rebuilt for each
        // turn of its head, the loops would twist this way and that.
        if (coilFor == head && (coilFacing == facing || state == State.Waking) && coilPose != null)
        {
            System.Array.Copy(coilPose, spine, Bones);
            return;
        }
        Vector3 forward = new Vector3(facing.x, 0f, facing.z);
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float inner = radius * 1.7f;
        float apart = radius * 2.05f;
        float headLength = radius * HeadRadii;
        float angle = 0f;
        float walked = 0f;
        float under = head.y - radius;
        for (int b = 0; b < Bones; b++)
        {
            while (walked < boneAt[b])
            {
                float ringNow = inner + apart * angle / (Mathf.PI * 2f);
                angle += 0.05f;
                walked += ringNow * 0.05f;
            }
            float ring = inner + apart * angle / (Mathf.PI * 2f);
            Vector3 at = head + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * ring;
            float floor = under;
            if (Physics.Raycast(at + Vector3.up * 0.8f, Vector3.down, out RaycastHit ground, 1.6f, HelperFunctions.terrainMapMask))
            {
                floor = ground.point.y;
            }
            else if (Physics.Raycast(at + Vector3.up * 2.5f, Vector3.down, out ground, 4f, HelperFunctions.terrainMapMask) && Mathf.Abs(ground.point.y - under) < 1.5f)
            {
                // The ground rising past where that ray started (curled up on a slope).
                floor = ground.point.y;
            }
            float lift = radius * (1f + 1.6f * Mathf.Max(0f, 1f - boneAt[b] / (headLength * 2.2f)));
            spine[b] = new Vector3(at.x, floor + lift, at.z);
        }
        coilPose ??= new Vector3[Bones];
        System.Array.Copy(spine, coilPose, Bones);
        coilFor = head;
        coilFacing = facing;
    }

    private Vector3[] coilPose;
    private Vector3 coilFor;
    private Vector3 coilFacing;

    /// <summary>
    /// The body without sharp bends: each point drawn a little towards the middle of its neighbours (twice), the head
    /// and the tail tip where they are; as much as asked (uncoiling or rearing up, more and more).
    /// </summary>
    private void Smooth(float weight = 1f)
    {
        // Rearing up, striking and just after, its body bends most; it is smoothed harder then.
        bool striking = state == State.Rearing || state == State.Lunging || state == State.Waking || Time.time - struckAt < 1f;
        int passes = striking ? 6 : 2;
        float pull = (striking ? 0.5f : 0.35f) * weight;
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 1; i < Bones - 1; i++)
            {
                spine[i] = Vector3.Lerp(spine[i], (spine[i - 1] + spine[i + 1]) * 0.5f, pull);
            }
        }
        if (state == State.Lunging)
        {
            struckAt = Time.time;
        }
    }

    private float struckAt = float.NegativeInfinity;


    /// <summary>
    /// Rearing up to strike (0 to 1): the front third of the body rises in an S from where it lies, the head raised and
    /// drawn back, facing the scout.
    /// </summary>
    private void RearUp(float t)
    {
        float k = Mathf.SmoothStep(0f, 1f, t);
        int neck = 1;
        while (neck < Bones - 1 && boneAt[neck] < length * 0.38f)
        {
            neck++;
        }
        Vector3 forward = new Vector3(facing.x, 0f, facing.z);
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        Vector3 root = spine[neck];
        Vector3 raised = root + Vector3.up * length * 0.16f + forward * length * 0.12f;
        Vector3 bend = root + Vector3.up * length * 0.14f - forward * length * 0.12f;
        for (int i = 0; i < neck; i++)
        {
            float u = 1f - (float)i / neck;
            Vector3 s = (1 - u) * (1 - u) * root + 2 * (1 - u) * u * bend + u * u * raised;
            spine[i] = Vector3.Lerp(spine[i], s, k);
        }
    }

    /// <summary>
    /// Uncoiling (0 to 1): the front half of the body rises out of the coil into a question mark, its head tipped down
    /// towards the scout: it lifts its neck up (the head held about level, turned a little ahead of it the way the neck
    /// turns), unwinds it while upright, and arches over.
    /// Each segment keeps its length and turns there from how it lay (its angles change, not its place, so the neck
    /// never folds through itself on the way up).
    /// </summary>
    private void Uncoil(float t)
    {
        // Overlapping moves: its head turns first, the way the top of its neck turns after it (never the other way round:
        // the neck swung round to meet it, the head folded back over it), and is held about level; the neck lifts up
        // into a column under it and turns while upright (turning only twists it there); then it arches over into the
        // question mark, the head tipping down.
        float lift = Mathf.SmoothStep(0f, 1f, t / 0.45f);
        float turning = Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.5f);
        float arch = Mathf.SmoothStep(0f, 1f, (t - 0.5f) / 0.5f);
        int neck = 1;
        while (neck < Bones - 1 && boneAt[neck] < length * UncoilShare)
        {
            neck++;
        }
        // How the neck lies in the coil: each segment's length, heading (followed on from the root as the coil turns,
        // never jumping a whole turn) and pitch, from the root towards the snout.
        float[] lengths = new float[neck];
        float[] headings = new float[neck];
        float[] pitches = new float[neck];
        for (int i = neck - 1; i >= 0; i--)
        {
            Vector3 segment = coilPose[i] - coilPose[i + 1];
            lengths[i] = segment.magnitude;
            float heading = Mathf.Atan2(segment.x, segment.z) * Mathf.Rad2Deg;
            headings[i] = i == neck - 1 ? heading : headings[i + 1] + Mathf.DeltaAngle(headings[i + 1], heading);
            pitches[i] = lengths[i] > 1e-5f ? Mathf.Asin(Mathf.Clamp(segment.y / lengths[i], -1f, 1f)) * Mathf.Rad2Deg : 0f;
        }
        // The heading it turns to (towards the scout), on the side nearest how its head and the top of its neck lay, so
        // both turn the short way and the coiled neck under them twists into it; going on from the frame before (it
        // never swaps one way round for the other).
        float aim = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        if (!uncoilTurning)
        {
            float lay = neck > 1 ? (headings[0] + headings[1]) * 0.5f : headings[0];
            uncoilHeading = lay + Mathf.DeltaAngle(lay, aim);
            uncoilHead = headings[0];
        }
        else
        {
            uncoilHeading += Mathf.DeltaAngle(uncoilHeading, aim);
        }
        uncoilTurning = true;
        // Each segment turns from how it lay (its length kept), so the neck bends up out of the coil rather than sliding
        // through it.
        float neckLength = boneAt[neck] - boneAt[1];
        Vector3 at = coilPose[neck];
        float topHeading = headings[neck - 1];
        for (int i = neck - 1; i >= 0; i--)
        {
            float s = i == 0 ? 1f : Mathf.Clamp01(1f - ((boneAt[i] + boneAt[i + 1]) * 0.5f - boneAt[1]) / neckLength);
            float p = Mathf.Lerp(pitches[i], i == 0 ? HeadUp : ColumnPitch(s), lift);
            p = Mathf.Lerp(p, i == 0 ? HeadPitch : NeckPitch(s), arch);
            float h;
            if (i > 0 || neck < 2)
            {
                // From how it lay towards that heading: the top of the neck all the way, the column under it the more the
                // higher (upright there, it only twists), the bend out of the coil not at all.
                float heading = Mathf.Lerp(headings[i], uncoilHeading, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, s)));
                h = Mathf.Lerp(headings[i], heading, turning);
                topHeading = h;
            }
            else
            {
                // The head, early: from how it lay against the top of the neck to a little ahead of it, towards that
                // heading; never back the other way (it waits for the neck instead).
                float lay = headings[0] - headings[1];
                float ahead = Mathf.Clamp(uncoilHeading - topHeading, -HeadLead, HeadLead);
                h = topHeading + Mathf.Lerp(lay, ahead, Mathf.SmoothStep(0f, 1f, t / 0.35f));
                if ((h - uncoilHead) * (uncoilHeading - headings[0]) < 0f)
                {
                    h = uncoilHead;
                }
                uncoilHead = h;
            }
            at += Quaternion.Euler(-p, h, 0f) * Vector3.forward * lengths[i];
            spine[i] = at;
        }
    }

    /// <summary>Uncoiled, this much of the body (from the snout) rises out of the coil.</summary>
    private const float UncoilShare = 0.5f;
    /// <summary>The head's pitch (degrees): about level while the neck goes up; then tipped down towards the scout, the question mark's hook.</summary>
    private const float HeadUp = 8f;
    private const float HeadPitch = -30f;
    /// <summary>Uncoiling, the head turns this far (degrees) ahead of the top of its neck at most, the way that turns.</summary>
    private const float HeadLead = 45f;
    /// <summary>
    /// Uncoiling: the heading it turns to (towards the scout), as it went last frame (it carries on from there), and the
    /// head's heading last frame (it never turns back).
    /// </summary>
    private float uncoilHeading;
    private float uncoilHead;
    private bool uncoilTurning;

    /// <summary>
    /// The uncoiled neck's pitch along it (0 at the coil, 1 at the back of the head), a question mark: it bends up out
    /// of the coil, rises leaning a little back, and arches over towards the head.
    /// </summary>
    private static float NeckPitch(float s)
    {
        if (s < 0.25f)
        {
            return 80f * Mathf.SmoothStep(0f, 1f, s / 0.25f);
        }
        if (s < 0.45f)
        {
            return Mathf.Lerp(80f, 100f, (s - 0.25f) / 0.2f);
        }
        return Mathf.Lerp(100f, -10f, Mathf.SmoothStep(0f, 1f, (s - 0.45f) / 0.55f));
    }

    /// <summary>On its way up: the same bend out of the coil, straight up, and curving forward at the top into the level head.</summary>
    private static float ColumnPitch(float s)
    {
        if (s < 0.25f)
        {
            return 80f * Mathf.SmoothStep(0f, 1f, s / 0.25f);
        }
        if (s < 0.7f)
        {
            return Mathf.Lerp(80f, 88f, (s - 0.25f) / 0.45f);
        }
        return Mathf.Lerp(88f, 40f, Mathf.SmoothStep(0f, 1f, (s - 0.7f) / 0.3f));
    }

    /// <summary>Awake, the forked tongue flicks out now and then (more often on the hunt).</summary>
    private void Flick()
    {
        bool awake = state != State.Sleeping && state != State.Stunned;
        float now = Time.time;
        if (awake && now >= nextFlick)
        {
            flickFrom = now;
            nextFlick = now + (state == State.Chasing ? Random.Range(0.6f, 1.5f) : Random.Range(1.5f, 4f));
        }
        float t = (now - flickFrom) / FlickSeconds;
        bool showing = t >= 0f && t < 1f;
        tongue.gameObject.SetActive(showing);
        if (showing)
        {
            tongue.localScale = new Vector3(1f, 1f, Mathf.Max(0.05f, Mathf.Sin(t * Mathf.PI)));
        }
    }

    internal Vector3 EyeSpot(float side) => bones != null ? bones[0].TransformPoint(new Vector3(side * radius * 0.85f, radius * 0.35f, -radius * HeadRadii * 0.4f)) : head;

    /// <summary>A stone thrown hard hits the snake (the host decides).</summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!photonView.IsMine)
        {
            return;
        }
        Item item = other.GetComponentInParent<Item>();
        if (item != null && StoneItem.IsStone(item) && item.rig != null && item.rig.linearVelocity.magnitude >= Snakes.StoneHitSpeed)
        {
            Stun(StoneItem.Knockdown, item.rig.linearVelocity, item.lastThrownCharacter);
        }
    }

    [PunRPC]
    internal void RPC_Hit(float seconds, Vector3 push, int byView)
    {
        if (photonView.IsMine)
        {
            PhotonView view = byView >= 0 ? PhotonView.Find(byView) : null;
            Stun(seconds, push, view != null ? view.GetComponent<Character>() : null);
        }
    }

    private void Stun(float seconds, Vector3 push, Character by)
    {
        photonView.RPC(nameof(RPC_Struck), RpcTarget.All);
        push.y = 0f;
        if (push.sqrMagnitude > 0.01f)
        {
            // Shoved back, still facing whoever hit it.
            facing = -push.normalized;
        }
        // Knocked back 1.5 m, all of it at once, as a quick slide along the ground (a wall or a steep rise behind it cuts it short).
        Vector3 away = push.sqrMagnitude > 0.01f ? push.normalized : -new Vector3(facing.x, 0f, facing.z).normalized;
        float room = ShoveRoom(away, out string stop);
        LastShove = room;
        LastShoveStop = stop;
        if (room > 0.1f)
        {
            photonView.RPC(nameof(RPC_Shove), RpcTarget.All, away * room, ShoveSeconds * room / ShoveDistance);
        }
        if (state != State.Stunned || stunnedBy == null)
        {
            // The first scout to stun it (hit again while stunned, it still goes for them after).
            stunnedBy = by;
        }
        state = State.Stunned;
        stateUntil = Time.time + seconds;
        TurnOntoCircle();
        Plugin.Debug($"Snake hit: it curls up for {seconds:0.#} s.");
    }

    /// <summary>
    /// How far a hit can knock it back that way, up to the whole shove: along the ground under it (up and down slopes,
    /// over its small bumps, and on over an edge, to fall), cut short by a wall, a rise too steep to slide up, or anything
    /// solid in the way (what, for the tests).
    /// </summary>
    private float ShoveRoom(Vector3 away, out string stop)
    {
        const float step = 0.1f;
        Vector3 from = head;
        Vector3 clear = Vector3.up * (radius + 0.1f);
        float room = 0f;
        stop = "";
        for (float d = step; d <= ShoveDistance + 1e-3f; d += step)
        {
            if (!GroundAt(head + away * d, out Vector3 ground, out bool drop, out Collider under))
            {
                stop = "nothing to slide on";
                break;
            }
            if (LeavesTropics(under, ground))
            {
                stop = "the edge of the Tropics";
                break;
            }
            Vector3 to = drop ? new Vector3(ground.x, from.y, ground.z) : ground;
            if (to.y - from.y > step * 1.5f)
            {
                stop = $"a rise of {to.y - from.y:0.00} m in {step:0.0} m";
                break;
            }
            if (Physics.Linecast(from + clear, to + clear, out RaycastHit wall, HelperFunctions.terrainMapMask))
            {
                stop = wall.collider != null ? wall.collider.name : "something solid";
                break;
            }
            from = to;
            room = d;
        }
        return room;
    }

    /// <summary>A hit lands on it, heard on every game: the game's own sound of a kick or a Stick's blow landing on a scout.</summary>
    [PunRPC]
    internal void RPC_Struck()
    {
        CharacterGrabbing kick = Character.localCharacter != null ? Character.localCharacter.refs.grabbing : null;
        if (kick != null && kick.kickImpactSFX != null)
        {
            kick.kickImpactSFX.Play(head);
            Snakes.HitSounds++;
        }
    }

    /// <summary>The shove, on every game: the whole snake slides back together (its head moved by the host, its body here).</summary>
    [PunRPC]
    internal void RPC_Shove(Vector3 offset, float seconds)
    {
        shove = offset / Mathf.Max(seconds, 0.01f);
        shoveUntil = Time.time + seconds;
    }

    [PunRPC]
    internal void RPC_Bite(int targetView)
    {
        PhotonView view = PhotonView.Find(targetView);
        Character bitten = view != null ? view.GetComponent<Character>() : null;
        Snakes.PlayBite(bitten);
        VenomSparks();
        if (bitten != null && bitten.IsLocal)
        {
            Snakes.Bitten(bitten, kind);
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(head);
            stream.SendNext(facing);
            stream.SendNext((byte)state);
            stream.SendNext(wave);
            stream.SendNext(clinging);
        }
        else
        {
            netHead = (Vector3)stream.ReceiveNext();
            netFacing = (Vector3)stream.ReceiveNext();
            state = (State)(byte)stream.ReceiveNext();
            wave = (float)stream.ReceiveNext();
            clinging = (bool)stream.ReceiveNext();
        }
    }
}

/// <summary>The Tropics' snake spawner: the host spawns once, when the game spawns that part of the map.</summary>
internal sealed class SnakeSpawner : MonoBehaviour, ISpawner
{
    private bool done;

    public List<PhotonView> TrySpawnItems()
    {
        if (done)
        {
            return new List<PhotonView>();
        }
        done = PhotonNetwork.IsMasterClient;
        return Snakes.Spawn(transform.parent);
    }
}
