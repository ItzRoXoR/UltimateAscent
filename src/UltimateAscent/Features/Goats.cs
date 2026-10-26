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
/// Mountain goats in the Alps, at most 12, alone or in pairs, spread from where the Alps begin to the top of their climb;
/// on every ascent, their ram half as strong below 17 (see <see cref="NewMobs"/>).
/// <list type="bullet">
/// <item>They graze, look about and roam the mountainside around their spot, surer on it than the Roots' beetles: they
/// walk slopes up to 80 degrees and leap from foothold to foothold, up to 3.5 m up, 6 m down and 4.5 m across. They never
/// leave the Alps: they stop where the Alps' ground ends, and do not go for a scout beyond it.</item>
/// <item>Once a scout is within 20 m (and in sight) a goat decides whether to charge them, and again every 20 s while one
/// is: three times in four it goes on grazing; the fourth it bleats, lowers its head, paws the ground for a second and
/// charges (7 m/s; a sprinting scout makes 5.2), turning after the scout, but running straight on in its last 5 m. Its
/// ram knocks the scout down for 2 s and throws them 6-8 m, with 20 injury. A goat that misses (the scout stepped aside)
/// skids on, stops and shakes its head.</item>
/// </list>
/// The host runs the goats (a room object each); everyone else sees them through the network. Their bodies are built in
/// code in the rounded shapes of PEAK's creatures, fluffy, with long thick horns (see <see cref="GoatBody"/>), solid to
/// the scouts; climbing, a goat's whole body lies along the cliff, its hooves on the rock. Their sounds (in the
/// mod's resources): bleats from the user's recordings, now and then and always right before a charge; hooves from the
/// user's horse recording, laid out into long walking and galloping loops that play while a goat moves; and a ram that
/// is the Roots' beetles' hit, heavier and louder.
/// </summary>
internal static class Goats
{
    internal const string PrefabName = "UA_Goat";
    private const string SpawnerName = "UltimateAscent Goat Spawner";

    /// <summary>At most this many goats in the Alps, alone or in pairs.</summary>
    internal const int MaxGoats = 12;

    // The charge.
    /// <summary>A goat notices a scout within this distance (and in sight), and then decides whether to charge them, at most once in this long.</summary>
    internal const float NoticeDistance = 20f;
    internal const float DecideEvery = 20f;
    internal const float ChargeChance = 0.25f;
    /// <summary>Before it charges it bleats, lowers its head and paws the ground this long (the scout's warning).</summary>
    internal const float WindUpSeconds = 1f;
    internal const float ChargeSpeed = 7f;
    /// <summary>A charge ends after this long or this far, whatever happens.</summary>
    internal const float MaxChargeSeconds = 4f;
    internal const float MaxChargeRun = 30f;
    /// <summary>The ram lands when its forehead is this near the middle of a scout (or 0.4 m from any part of them).</summary>
    internal const float RamReach = 0.85f;
    /// <summary>This near the scout it stops turning after them and runs straight on: a scout who steps aside now makes it miss.</summary>
    internal const float CommitDistance = 5f;
    internal const float RamKnockDown = 2f;
    internal const float RamInjury = 0.2f;
    /// <summary>
    /// The ram's throw: a hop (the scout's whole body sped up this much, as the game's pushes go, a second's worth of it),
    /// then their body driven along the charge at a speed falling from 12 m/s to nothing over 1.15 s (pushed or held back
    /// as hard as that takes, at most MaxThrowPush): about 7 m, whatever they land on (a single push threw 7-8.5 m, as the
    /// snow let them slide).
    /// </summary>
    internal const float RamLift = 300f;
    internal const float ThrowSpeed = 12f;
    internal const float ThrowSeconds = 1.15f;
    private const float ThrowGain = 60f;
    private const float MaxThrowPush = 700f;

    // Roaming.
    internal const float WalkSpeed = 1.3f;
    /// <summary>A goat roams within this distance of its spot (one of a pair keeps near the other).</summary>
    internal const float RoamRange = 25f;
    /// <summary>
    /// It walks on slopes up to this steep (the Roots' beetles crawl on surfaces up to 70 degrees, but never leap), rests
    /// and grazes on ground up to this steep, lands from a leap on ground up to this steep, and leaps up to these heights
    /// and distances.
    /// </summary>
    internal const float MaxSlope = 80f;
    internal const float RestSlope = 45f;
    internal const float LandSlope = 60f;
    internal const float MaxLeapUp = 3.5f;
    internal const float MaxLeapDown = 6f;
    internal const float MaxLeapAcross = 4.5f;

    // Where they live.
    /// <summary>Spots for the goats: ground in the Alps no steeper than this, this far from the campfires, groups this far apart.</summary>
    private const float SpotSlope = 30f;
    private const float AwayFromCampfires = 25f;
    private const float GroupsApart = 25f;
    private const int SpotTries = 2500;
    /// <summary>How far to either side of the line from the Alps' first campfire to their last spots are looked for.</summary>
    private const float SpotSpread = 90f;

    // Sounds: their resources, loudness (of the sound source; the clips are levelled as the game's creature sounds are)
    // and how far each is heard.
    internal static readonly string[] BleatResources = { "UltimateAscent.Goat_bleat_1.wav", "UltimateAscent.Goat_bleat_2.wav" };
    internal const string HoovesResource = "UltimateAscent.Goat_hooves.wav";
    internal const string GallopResource = "UltimateAscent.Goat_gallop.wav";
    internal const string LandResource = "UltimateAscent.Goat_land.wav";
    internal const string RamResource = "UltimateAscent.Goat_ram.wav";
    internal const float BleatVolume = 0.7f;
    internal const float HoovesVolume = 0.8f;
    internal const float GallopVolume = 0.9f;
    internal const float LandVolume = 0.7f;
    internal const float RamVolume = 1f;
    internal const float BleatReach = 40f;
    internal const float HoovesReach = 20f;
    internal const float GallopReach = 30f;
    internal const float ThudReach = 30f;
    /// <summary>A goat bleats every 25-70 s (while a scout is within 60 m to hear it), and always right before it charges.</summary>
    internal const float BleatEveryMin = 25f;
    internal const float BleatEveryMax = 70f;
    internal const float BleatHeardFrom = 60f;

    internal static bool On => NewMobs.Allowed(NewMobs.GoatsThreat);

    internal static readonly List<Goat> All = new List<Goat>();

    /// <summary>
    /// For the tests: when decisions were taken; charges begun; rams taken by the local scout, rams heard, misses;
    /// bleats heard (and when the last was), bleats sent right before a charge, landings heard.
    /// </summary>
    internal static readonly List<float> DecidedAt = new List<float>();
    internal static int Charges;
    internal static int RamsTaken;
    internal static int RamSounds;
    internal static int Misses;
    internal static int BleatsHeard;
    internal static float LastBleat = float.NegativeInfinity;
    internal static int ChargeBleats;
    internal static int Landings;
    /// <summary>For the tests: the next decision's dice (below ChargeChance charges), once.</summary>
    internal static float? NextRoll;
    /// <summary>For the tests: the spots found for the goats when they spawned, and how many places were looked at.</summary>
    internal static int SpotsFound;
    internal static int SpotsTried;

    private static GameObject template;
    private static AudioClip[] bleats;
    private static AudioClip hooves;
    private static AudioClip gallop;
    private static AudioClip land;
    private static AudioClip ram;
    private static bool soundsLoaded;

    /// <summary>The third part of each map gets a spawner; it spawns only where that part is the Alps (not the Mesa).</summary>
    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.InitializeMap))]
    private static class EveryMap
    {
        private static void Postfix()
        {
            MapHandler map = MapHandler.Exists ? Singleton<MapHandler>.Instance : null;
            if (map == null || map.segments == null || map.segments.Length <= (int)Segment.Alpine)
            {
                return;
            }
            MapHandler.MapSegment alps = map.segments[(int)Segment.Alpine];
            GameObject parent = alps.segmentParent;
            if (parent == null || alps.biome != Biome.BiomeType.Alpine || parent.transform.Find(SpawnerName) != null)
            {
                return;
            }
            GameObject holder = new GameObject(SpawnerName);
            holder.transform.SetParent(parent.transform, false);
            holder.AddComponent<GoatSpawner>();
        }
    }

    /// <summary>The host's spawn of the Alps' goats (none below ascent 17): groups of one or two up to 12, spread along the Alps.</summary>
    internal static List<PhotonView> Spawn(Transform segment)
    {
        List<PhotonView> spawned = new List<PhotonView>();
        if (segment == null || !PhotonNetwork.IsMasterClient || !On || !Register())
        {
            return spawned;
        }
        Physics.SyncTransforms();
        List<int> groups = new List<int>();
        for (int count = 0; count < MaxGoats;)
        {
            int size = Random.value < 0.5f && count + 2 <= MaxGoats ? 2 : 1;
            groups.Add(size);
            count += size;
        }
        List<Vector3[]> places = Places(groups);
        int pair = 0;
        foreach (Vector3[] place in places)
        {
            pair++;
            for (int i = 0; i < place.Length; i++)
            {
                spawned.Add(Make(place[i], place.Length > 1 ? pair : 0, i > 0));
            }
        }
        spawned.RemoveAll(v => v == null);
        Plugin.Log.LogInfo($"New mobs: {spawned.Count} mountain goats in {segment.name} ({places.Count(p => p.Length == 1)} alone, {places.Count(p => p.Length == 2)} pairs; "
            + $"{SpotsFound} spots found in {SpotsTried} places looked at).");
        return spawned;
    }

    private static PhotonView Make(Vector3 at, int pair, bool follower)
    {
        object[] data = { at, pair, follower };
        GameObject made = PhotonNetwork.InstantiateRoomObject(PrefabName, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 0, data);
        return made != null ? made.GetComponent<PhotonView>() : null;
    }

    /// <summary>
    /// Where the groups live: open ground in the Alps (no steeper than 30 degrees, room to stand, not in water), away from
    /// the campfires, found along the way from the Alps' first campfire to their last; each group is put at its share
    /// of that way (the first near the start, the last near the top), 25 m or more from the others, the second of a pair
    /// a couple of metres from the first.
    /// </summary>
    private static List<Vector3[]> Places(List<int> groups)
    {
        List<Vector3[]> places = new List<Vector3[]>();
        MapHandler map = Singleton<MapHandler>.Instance;
        GameObject first = map.segments[(int)Segment.Alpine - 1].segmentCampfire;
        GameObject last = map.segments[(int)Segment.Alpine].segmentCampfire;
        if (first == null || last == null)
        {
            Plugin.Log.LogWarning("Goats: the Alps' campfires were not found; no goats.");
            return places;
        }
        Vector3 a = first.transform.position;
        Vector3 b = last.transform.position;
        Vector3 along = b - a;
        along.y = 0f;
        float length = along.magnitude;
        if (length < 1f)
        {
            return places;
        }
        along /= length;
        Vector3 across = Vector3.Cross(Vector3.up, along);
        float top = Mathf.Max(a.y, b.y) + 250f;
        float depth = top - Mathf.Min(a.y, b.y) + 150f;
        WaterZone[] water = Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
        List<(Vector3 spot, float share)> found = new List<(Vector3, float)>();
        SpotsTried = SpotTries;
        for (int i = 0; i < SpotTries; i++)
        {
            float share = Random.value;
            Vector3 probe = a + along * (share * length) + across * Random.Range(-SpotSpread, SpotSpread);
            if (!Physics.Raycast(new Vector3(probe.x, top, probe.z), Vector3.down, out RaycastHit hit, depth, HelperFunctions.terrainMapMask)
                || Vector3.Angle(hit.normal, Vector3.up) > SpotSlope
                || !Map.TryGetBiome(hit.collider, out Biome.BiomeType biome) || biome != Biome.BiomeType.Alpine
                || Flat(hit.point, a) < AwayFromCampfires || Flat(hit.point, b) < AwayFromCampfires
                || !Goat.RoomAt(hit.point) || !LevelAround(hit.point) || GroundItems.InWater(hit.point, water))
            {
                continue;
            }
            found.Add((hit.point, share));
        }
        SpotsFound = found.Count;
        for (int g = 0; g < groups.Count; g++)
        {
            float wanted = (g + 0.5f) / groups.Count;
            float band = 0.5f / groups.Count;
            List<Vector3> taken = places.Select(p => p[0]).ToList();
            List<(Vector3 spot, float share)> open = found.Where(f => taken.All(t => Flat(t, f.spot) >= GroupsApart)).ToList();
            List<(Vector3 spot, float share)> inBand = open.Where(f => Mathf.Abs(f.share - wanted) <= band).ToList();
            List<(Vector3 spot, float share)> pick = inBand.Count > 0 ? inBand : open.OrderBy(f => Mathf.Abs(f.share - wanted)).Take(5).ToList();
            if (pick.Count == 0)
            {
                continue;
            }
            Vector3 spot = pick[Random.Range(0, pick.Count)].spot;
            if (groups[g] == 2 && Beside(spot, out Vector3 second))
            {
                places.Add(new[] { spot, second });
            }
            else
            {
                places.Add(new[] { spot });
            }
        }
        return places;
    }

    /// <summary>Ground about level a little way round a spot (not a pinnacle to fall from).</summary>
    private static bool LevelAround(Vector3 at)
    {
        for (int angle = 0; angle < 360; angle += 90)
        {
            Vector3 round = at + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 1f;
            if (!Physics.Raycast(round + Vector3.up * 1.5f, Vector3.down, out RaycastHit floor, 3f, HelperFunctions.terrainMapMask) || Mathf.Abs(floor.point.y - at.y) > 0.7f)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>A spot for the second of a pair, 1.5-3 m from the first, about as level.</summary>
    private static bool Beside(Vector3 at, out Vector3 second)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector3 probe = at + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(1.5f, 3f);
            if (Physics.Raycast(probe + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 4f, HelperFunctions.terrainMapMask)
                && Vector3.Angle(hit.normal, Vector3.up) <= SpotSlope + 10f && Mathf.Abs(hit.point.y - at.y) <= 1f && Goat.RoomAt(hit.point))
            {
                second = hit.point;
                return true;
            }
        }
        second = at;
        return false;
    }

    internal static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    /// <summary>
    /// The network template, made once: a PhotonView watching a Goat, kept inactive beside the mod's items; solid (a
    /// kinematic body, its capsule round the goat's body, on the layer scouts bump into but the ground checks pass by).
    /// </summary>
    internal static bool Register()
    {
        if (template != null)
        {
            return true;
        }
        DefaultPool pool = ModItems.GamePool();
        if (pool == null)
        {
            Plugin.Log.LogError($"Goats: the game's prefab pool was not found ({PhotonNetwork.PrefabPool?.GetType().Name}); no goats.");
            return false;
        }
        GameObject root = new GameObject("UltimateAscentGoats");
        root.SetActive(false);
        Object.DontDestroyOnLoad(root);
        template = new GameObject(PrefabName);
        template.transform.SetParent(root.transform, false);
        PhotonView view = template.AddComponent<PhotonView>();
        Goat goat = template.AddComponent<Goat>();
        view.ObservedComponents = new List<Component> { goat };
        view.Synchronization = ViewSynchronization.UnreliableOnChange;
        Rigidbody solid = template.AddComponent<Rigidbody>();
        solid.isKinematic = true;
        solid.useGravity = false;
        CapsuleCollider capsule = template.AddComponent<CapsuleCollider>();
        capsule.direction = 2;
        capsule.center = new Vector3(0f, 0.76f, 0f);
        capsule.radius = 0.24f;
        capsule.height = 1.16f;
        pool.ResourceCache[PrefabName] = template;
        return true;
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
            bleats = BleatResources.Select((r, i) => ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(r), "Goat_bleat_" + (i + 1))).ToArray();
            hooves = ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(HoovesResource), "Goat_hooves");
            gallop = ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(GallopResource), "Goat_gallop");
            land = ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(LandResource), "Goat_land");
            ram = ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(RamResource), "Goat_ram");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogError("Goats: their sounds could not be loaded: " + ex.Message);
        }
    }

    internal static AudioClip Bleat(int which)
    {
        LoadSounds();
        return bleats != null && bleats.Length > 0 ? bleats[Mathf.Abs(which) % bleats.Length] : null;
    }

    internal static int BleatCount
    {
        get
        {
            LoadSounds();
            return bleats != null ? bleats.Length : 0;
        }
    }

    internal static AudioClip HoovesClip
    {
        get
        {
            LoadSounds();
            return hooves;
        }
    }

    internal static AudioClip GallopClip
    {
        get
        {
            LoadSounds();
            return gallop;
        }
    }

    internal static AudioClip LandClip
    {
        get
        {
            LoadSounds();
            return land;
        }
    }

    internal static AudioClip RamClip
    {
        get
        {
            LoadSounds();
            return ram;
        }
    }

    /// <summary>A ram on the local scout: knocked down, hurt, thrown along the charge (all of it half below ascent 17).</summary>
    internal static void Rammed(Character scout, Vector3 away)
    {
        if (scout == null || !scout.IsLocal || scout.data.dead)
        {
            return;
        }
        RamsTaken++;
        float strength = NewMobs.Strength;
        scout.Fall(RamKnockDown * strength);
        scout.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, RamInjury * strength);
        away.y = 0f;
        if (away.sqrMagnitude > 1e-4f)
        {
            scout.StartCoroutine(Thrown(scout, away.normalized, strength));
        }
    }

    /// <summary>The throw (see ThrowSpeed), on the thrown scout's own game.</summary>
    private static IEnumerator Thrown(Character scout, Vector3 away, float strength)
    {
        scout.AddForce(Vector3.up * RamLift * strength);
        for (float since = 0f; since < ThrowSeconds; since += Time.fixedDeltaTime)
        {
            if (scout == null || scout.data.dead)
            {
                yield break;
            }
            float wanted = ThrowSpeed * strength * (1f - since / ThrowSeconds);
            float along = Vector3.Dot(scout.data.avarageVelocity, away);
            scout.AddForce(away * Mathf.Clamp((wanted - along) * ThrowGain, -MaxThrowPush, MaxThrowPush));
            yield return new WaitForFixedUpdate();
        }
    }
}

/// <summary>A ring of a rounded tube: where its middle is, how wide and high it is (half of each), and how square (2 round, more squarer).</summary>
internal struct GoatRing
{
    internal Vector3 at;
    internal float width;
    internal float height;
    internal float round;

    internal GoatRing(Vector3 at, float width, float height, float round = 2.4f)
    {
        this.at = at;
        this.width = width;
        this.height = height;
        this.round = round;
    }

    internal GoatRing(float x, float y, float z, float width, float height, float round = 2.4f) : this(new Vector3(x, y, z), width, height, round)
    {
    }
}

/// <summary>
/// The goat's body, built in code in the shapes of PEAK's creatures (rounded, chunky, smoothly shaded, in the game's own
/// item shading), coloured as in the user's picture: a fluffy off-white coat, locks of hair over almost all of it (the
/// back and sides, the rump, all round the neck, the back of the head and the cheeks, the legs down to above the
/// hooves, the tail), longer grey hair hanging from its belly, chest and throat, a beard, long thick grey banded horns
/// curving back, dark hooves and nose, black eyes. Parts (metres; +x its right, +y up, +z the way it faces; the root the
/// ground between its feet): the body, the tail, the neck and the head on pivots, and four legs, each an upper leg, a
/// shin bending at the knee (the hock, on a hind leg) and a hoof bending at its fetlock.
/// </summary>
internal static class GoatBody
{
    internal const int Coat = 0, Shade = 1, Horn = 2, Hoof = 3, Eye = 4, Nose = 5, Face = 6, Kinds = 7;

    internal static readonly Vector3 BodyPivot = new Vector3(0f, 0.70f, 0f);
    internal static readonly Vector3 NeckPivot = new Vector3(0f, 0.08f, 0.38f);
    internal const float NeckLength = 0.36f;
    /// <summary>The neck rises this far above the level (a pitch up) and the head hangs this far under the neck, 40 degrees below the level, at rest.</summary>
    internal const float NeckRest = -55f;
    internal const float HeadRest = 95f;
    internal static readonly Vector3 TailPivot = new Vector3(0f, 0.10f, -0.53f);
    /// <summary>The legs' tops (front right, front left, back right, back left) in the body's space, and the knees in the legs'.</summary>
    internal static readonly Vector3[] Hips =
    {
        new Vector3(0.12f, -0.08f, 0.30f), new Vector3(-0.12f, -0.08f, 0.30f), new Vector3(0.12f, -0.02f, -0.36f), new Vector3(-0.12f, -0.02f, -0.36f),
    };
    internal static readonly Vector3 FrontKnee = new Vector3(0f, -0.24f, 0f);
    internal static readonly Vector3 BackKnee = new Vector3(0f, -0.257f, -0.039f);
    /// <summary>How long the shins are (front, back) from the knees to the fetlocks (the joints above the hooves), and the hooves below those to their soles.</summary>
    internal const float FrontShin = 0.28f;
    internal const float BackShin = 0.34f;
    internal const float HoofHeight = 0.09f;
    /// <summary>The forehead between the horns, in the head's space: where a ram lands from.</summary>
    internal static readonly Vector3 Forehead = new Vector3(0f, 0.09f, 0.06f);
    /// <summary>The tip of its muzzle, in the head's space.</summary>
    internal static readonly Vector3 Muzzle = new Vector3(0f, -0.02f, 0.3f);

    private static Quaternion NeckWorldRest => Quaternion.AngleAxis(NeckRest, Vector3.right);
    private static Quaternion HeadWorldRest => NeckWorldRest * Quaternion.AngleAxis(HeadRest, Vector3.right);

    private static Material[] materials;
    private static Mesh body;
    private static Mesh tail;
    private static Mesh neck;
    private static Mesh head;
    private static Mesh[] uppers;
    private static Mesh[] lowers;
    private static Mesh hoof;

    internal static Material[] Materials => materials ??= MakeMaterials();
    internal static Mesh BodyMesh => body ??= MakeBody();
    internal static Mesh TailMesh => tail ??= MakeTail();
    internal static Mesh NeckMesh => neck ??= MakeNeck();
    internal static Mesh HeadMesh => head ??= MakeHead();

    internal static Mesh Upper(int leg)
    {
        uppers ??= Enumerable.Range(0, 4).Select(i => MakeUpper(i < 2, i % 2 == 0 ? 1f : -1f)).ToArray();
        return uppers[leg];
    }

    internal static Mesh Lower(int leg)
    {
        lowers ??= new[] { MakeLower(true), MakeLower(false) };
        return lowers[leg < 2 ? 0 : 1];
    }

    internal static Mesh HoofMesh => hoof ??= MakeHoof();

    /// <summary>Rings smoothed in between (Catmull-Rom), `between` new ones between each two.</summary>
    internal static List<GoatRing> Smooth(IList<GoatRing> rings, int between)
    {
        List<GoatRing> path = new List<GoatRing>();
        for (int i = 0; i < rings.Count - 1; i++)
        {
            GoatRing p0 = rings[Mathf.Max(i - 1, 0)], p1 = rings[i], p2 = rings[i + 1], p3 = rings[Mathf.Min(i + 2, rings.Count - 1)];
            for (int s = 0; s <= between; s++)
            {
                float t = s / (float)(between + 1);
                path.Add(new GoatRing(
                    new Vector3(Spline(p0.at.x, p1.at.x, p2.at.x, p3.at.x, t), Spline(p0.at.y, p1.at.y, p2.at.y, p3.at.y, t), Spline(p0.at.z, p1.at.z, p2.at.z, p3.at.z, t)),
                    Mathf.Max(0.002f, Spline(p0.width, p1.width, p2.width, p3.width, t)),
                    Mathf.Max(0.002f, Spline(p0.height, p1.height, p2.height, p3.height, t)),
                    Mathf.Max(1.5f, Spline(p0.round, p1.round, p2.round, p3.round, t))));
            }
        }
        path.Add(rings[rings.Count - 1]);
        return path;
    }

    private static float Spline(float a, float b, float c, float d, float t)
    {
        return 0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t * t + (3f * b - a - 3f * c + d) * t * t * t);
    }

    /// <summary>A point on a ring at an angle round it (0 its top, a quarter turn its right), and which way the skin there faces (the ring's own frame: x right, y top).</summary>
    private static Vector3 OnRing(GoatRing ring, float angle, out Vector3 skin)
    {
        float s = Mathf.Sin(angle), c = Mathf.Cos(angle), e = 2f / ring.round;
        Vector3 local = new Vector3(ring.width * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e), ring.height * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e), 0f);
        skin = new Vector3(local.x / (ring.width * ring.width), local.y / (ring.height * ring.height), 0f).normalized;
        return local;
    }

    private static float Dice(System.Random dice) => (float)dice.NextDouble();

    /// <summary>
    /// Locks of hair all over a tube whose rings run along z (the body, the neck, the head, in their own spaces): row after
    /// row, each a little offset from the last, where `grows` lets hair grow (by the angle round and the ring); each lock
    /// falls the way `fall` gives for its skin (down and back, mostly), sticking out a little from it.
    /// </summary>
    private static void Hair(GoatMesh mesh, IList<GoatRing> rings, int rows, int around, System.Random dice,
        System.Func<int, float, bool> grows, System.Func<Vector3, Vector3, Vector3> fall, System.Func<int, float, int> kind,
        float shortest, float longest, float width)
    {
        List<GoatRing> path = Smooth(rings, 2);
        for (int row = 0; row < rows; row++)
        {
            float along = Mathf.Lerp(1.5f, path.Count - 2.5f, row / (float)Mathf.Max(1, rows - 1));
            int i = Mathf.Clamp((int)along, 0, path.Count - 2);
            float t = along - i;
            GoatRing ring = new GoatRing(Vector3.Lerp(path[i].at, path[i + 1].at, t), Mathf.Lerp(path[i].width, path[i + 1].width, t),
                Mathf.Lerp(path[i].height, path[i + 1].height, t), Mathf.Lerp(path[i].round, path[i + 1].round, t));
            for (int k = 0; k < around; k++)
            {
                float angle = 2f * Mathf.PI * (k + (row % 2) * 0.5f + (Dice(dice) - 0.5f) * 0.3f) / around;
                if (!grows(row, angle))
                {
                    continue;
                }
                Vector3 local = OnRing(ring, angle, out Vector3 skin);
                Vector3 root = ring.at + local - skin * 0.012f;
                Vector3 way = fall(root, skin);
                way = Vector3.ProjectOnPlane(way, skin);
                if (way.sqrMagnitude < 1e-6f)
                {
                    way = Vector3.back;
                }
                way.Normalize();
                float length = Mathf.Lerp(shortest, longest, Dice(dice));
                Vector3 tip = root + way * length + skin * length * 0.3f;
                mesh.Lock(kind(row, angle), root, tip, skin, width * (0.8f + 0.4f * Dice(dice)), width * 0.35f);
            }
        }
    }

    private static readonly GoatRing[] Torso =
    {
        new GoatRing(0f, 0.07f, -0.58f, 0.04f, 0.05f, 2f),
        new GoatRing(0f, 0.075f, -0.53f, 0.12f, 0.14f, 2.4f),
        new GoatRing(0f, 0.075f, -0.44f, 0.185f, 0.205f, 2.6f),
        new GoatRing(0f, 0.065f, -0.28f, 0.21f, 0.23f, 2.7f),
        new GoatRing(0f, 0.055f, -0.08f, 0.215f, 0.24f, 2.7f),
        new GoatRing(0f, 0.07f, 0.12f, 0.21f, 0.255f, 2.7f),
        new GoatRing(0f, 0.09f, 0.28f, 0.2f, 0.27f, 2.6f),
        new GoatRing(0f, 0.085f, 0.4f, 0.175f, 0.245f, 2.5f),
        new GoatRing(0f, 0.07f, 0.49f, 0.12f, 0.17f, 2.3f),
        new GoatRing(0f, 0.06f, 0.55f, 0.04f, 0.06f, 2f),
    };

    /// <summary>The body: deep at the chest, a hump over the shoulders, covered in locks of hair falling down its sides and back over the rump, longer grey hair hanging from its belly and chest.</summary>
    private static Mesh MakeBody()
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        mesh.Tube(Coat, Torso, 20, Vector3.up, 0.5f, false, 2);
        System.Random dice = new System.Random(11);
        // The coat: down the sides, parting along the spine, back over the rump; the belly's underside left to the hanging hair.
        Hair(mesh, Torso, 15, 16, dice,
            (row, angle) => Mathf.Cos(angle) > -0.6f,
            (root, skin) => Vector3.down * 0.9f + Vector3.back * (root.z < -0.4f ? 0.9f : 0.4f) + Vector3.right * Mathf.Sign(root.x) * 0.3f * Mathf.Clamp01(skin.y),
            (row, angle) => Face, 0.1f, 0.16f, 0.085f);
        // The hanging hair: long and grey along both sides of the belly and across the chest.
        Hair(mesh, Torso, 13, 16, dice,
            (row, angle) => Mathf.Cos(angle) <= -0.55f,
            (root, skin) => Vector3.down * 1f + Vector3.back * 0.15f,
            (row, angle) => Shade, 0.14f, 0.21f, 0.08f);
        return mesh.Build("UA_Goat Body");
    }

    internal static readonly Quaternion TailRest = Quaternion.identity;

    /// <summary>The tail: a short fluffy tuft, up and back from the rump.</summary>
    private static Mesh MakeTail()
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        System.Random dice = new System.Random(23);
        for (int i = 0; i < 6; i++)
        {
            float spread = (i - 2.5f) * 0.018f;
            Vector3 root = new Vector3(spread, 0f, 0f);
            Vector3 tip = new Vector3(spread * 1.8f, 0.05f + 0.03f * Dice(dice), -0.09f - 0.03f * Dice(dice));
            mesh.Lock(i % 3 == 0 ? Shade : Face, root, tip, Vector3.up, 0.05f, 0.025f);
        }
        return mesh.Build("UA_Goat Tail");
    }

    /// <summary>The neck: thick at the shoulders, its hair all round it falling (as it stands) down to the shoulders and in a long grey ruff under the throat.</summary>
    private static Mesh MakeNeck()
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        GoatRing[] rings =
        {
            new GoatRing(0f, 0f, -0.08f, 0.12f, 0.14f, 2.3f),
            new GoatRing(0f, 0f, -0.02f, 0.135f, 0.155f, 2.4f),
            new GoatRing(0f, 0.005f, 0.1f, 0.125f, 0.14f, 2.4f),
            new GoatRing(0f, 0.01f, 0.22f, 0.105f, 0.115f, 2.3f),
            new GoatRing(0f, 0.01f, 0.32f, 0.092f, 0.098f, 2.2f),
            new GoatRing(0f, 0.01f, 0.39f, 0.06f, 0.065f, 2f),
        };
        mesh.Tube(Face, rings, 16, Vector3.up, 0.5f, true, 2);
        Vector3 down = Quaternion.Inverse(NeckWorldRest) * Vector3.down;
        System.Random dice = new System.Random(13);
        Hair(mesh, rings, 7, 12, dice,
            (row, angle) => true,
            (root, skin) => down + Vector3.back * 0.3f,
            (row, angle) => Mathf.Cos(angle) < -0.3f ? Shade : Face, 0.09f, 0.15f, 0.08f);
        return mesh.Build("UA_Goat Neck");
    }

    /// <summary>
    /// The head: long, rounded, narrowing to the muzzle; hair on the back of the head and the cheeks, falling back; long
    /// thick horns curving back, ears, eyes, a dark nose, the beard.
    /// </summary>
    private static Mesh MakeHead()
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        GoatRing[] skull =
        {
            new GoatRing(0f, 0.01f, -0.07f, 0.05f, 0.06f, 2f),
            new GoatRing(0f, 0.012f, -0.03f, 0.095f, 0.105f, 2.4f),
            new GoatRing(0f, 0.01f, 0.05f, 0.115f, 0.12f, 2.5f),
            new GoatRing(0f, 0f, 0.14f, 0.095f, 0.105f, 2.5f),
            new GoatRing(0f, -0.012f, 0.22f, 0.075f, 0.085f, 2.4f),
            new GoatRing(0f, -0.018f, 0.28f, 0.058f, 0.068f, 2.3f),
            new GoatRing(0f, -0.02f, 0.315f, 0.035f, 0.042f, 2f),
        };
        mesh.Tube(Face, skull, 16, Vector3.up, 0.4f, true, 2);
        Quaternion toHead = Quaternion.Inverse(HeadWorldRest);
        Vector3 down = toHead * Vector3.down;
        System.Random dice = new System.Random(29);
        // Hair on the back half of the head and the cheeks (not the face in front of the eyes, the forehead or the jaw).
        Hair(mesh, skull.Take(4).ToArray(), 4, 12, dice,
            (row, angle) => Mathf.Abs(Mathf.Sin(angle)) > 0.45f || (row == 0 && Mathf.Cos(angle) < 0.5f),
            (root, skin) => Vector3.back + down * 0.6f,
            (row, angle) => Face, 0.05f, 0.08f, 0.06f);
        foreach (float side in new[] { -1f, 1f })
        {
            // Eyes high on the sides, a little behind the widest of the head.
            mesh.Ball(Eye, new Vector3(side * 0.104f, 0.038f, 0.06f), new Vector3(0.02f, 0.024f, 0.022f));
            // Ears: small flat leaves out to the sides, drooping back.
            Vector3 earRoot = new Vector3(side * 0.075f, 0.065f, 0f);
            Vector3 earWay = new Vector3(side * 0.85f, 0.05f, -0.52f).normalized;
            GoatRing[] ear =
            {
                new GoatRing(earRoot - earWay * 0.01f, 0.018f, 0.009f, 2.2f),
                new GoatRing(earRoot + earWay * 0.04f, 0.03f, 0.009f, 2.2f),
                new GoatRing(earRoot + earWay * 0.08f, 0.026f, 0.008f, 2.2f),
                new GoatRing(earRoot + earWay * 0.115f, 0.007f, 0.004f, 2f),
            };
            mesh.Tube(Face, ear, 10, Vector3.up, 0.3f, false, 1);
            // Horns: long and thick, from the top of the head up and sweeping back, as it stands.
            Vector3 hornRoot = new Vector3(side * 0.05f, 0.085f, 0.04f);
            Vector3[] rise =
            {
                Vector3.zero, new Vector3(side * 0.01f, 0.07f, -0.015f), new Vector3(side * 0.02f, 0.13f, -0.045f),
                new Vector3(side * 0.028f, 0.18f, -0.095f), new Vector3(side * 0.034f, 0.205f, -0.155f),
                new Vector3(side * 0.038f, 0.205f, -0.215f), new Vector3(side * 0.04f, 0.19f, -0.26f),
            };
            float[] radius = { 0.04f, 0.036f, 0.031f, 0.025f, 0.018f, 0.011f, 0.004f };
            GoatRing[] horn = rise.Select((r, i) => new GoatRing(hornRoot + toHead * r, radius[i], radius[i], 2f)).ToArray();
            mesh.Tube(Horn, horn, 12, Vector3.right, 0.38f, true, 2);
        }
        // The beard: locks hanging from under the chin, straight down as it stands.
        foreach ((Vector3 root, float length) in new[]
        {
            (new Vector3(0f, -0.075f, 0.19f), 0.17f), (new Vector3(0.03f, -0.07f, 0.16f), 0.13f), (new Vector3(-0.03f, -0.07f, 0.16f), 0.13f),
            (new Vector3(0f, -0.08f, 0.13f), 0.12f), (new Vector3(0.045f, -0.065f, 0.11f), 0.1f), (new Vector3(-0.045f, -0.065f, 0.11f), 0.1f),
        })
        {
            mesh.Lock(Shade, root, root + down * length + toHead * Vector3.back * 0.02f, toHead * Vector3.forward, 0.05f, 0.025f);
        }
        mesh.Ball(Nose, new Vector3(0f, -0.014f, 0.318f), new Vector3(0.038f, 0.028f, 0.024f));
        return mesh.Build("UA_Goat Head");
    }

    /// <summary>An upper leg: thick, hidden under long hair hanging round it (its "trousers"), the lowest of it grey.</summary>
    private static Mesh MakeUpper(bool front, float side)
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        System.Random dice = new System.Random(front ? 17 : 19);
        Vector3 down = front ? Vector3.down : new Vector3(0f, -1f, -0.15f).normalized;
        GoatRing[] leg = front
            ? new[]
            {
                new GoatRing(0f, 0.04f, 0f, 0.08f, 0.077f, 2.2f),
                new GoatRing(0f, -0.08f, 0f, 0.076f, 0.072f, 2.2f),
                new GoatRing(0f, -0.18f, 0f, 0.062f, 0.058f, 2.2f),
                new GoatRing(0f, -0.25f, 0f, 0.053f, 0.05f, 2.1f),
            }
            : new[]
            {
                new GoatRing(down * -0.04f, 0.097f, 0.102f, 2.2f),
                new GoatRing(down * 0.09f, 0.092f, 0.097f, 2.2f),
                new GoatRing(down * 0.19f, 0.07f, 0.074f, 2.2f),
                new GoatRing(down * 0.262f, 0.055f, 0.057f, 2.1f),
            };
        mesh.Tube(Face, leg, 12, Vector3.forward, 0.4f, true, 1);
        // Rows of hair round the leg (in its own space: the leg runs down, round it is x and z), falling down it, flaring a little.
        for (int row = 0; row < 3; row++)
        {
            float at = 0.02f + row * 0.075f;
            float radius = Mathf.Lerp(0.08f, 0.06f, row / 2f) + (front ? 0f : 0.015f);
            for (int k = 0; k < 9; k++)
            {
                float angle = 2f * Mathf.PI * (k + (row % 2) * 0.5f) / 9f;
                Vector3 skin = new Vector3(Mathf.Sin(angle) * side, 0f, Mathf.Cos(angle));
                Vector3 root = down * at + skin * (radius - 0.012f);
                float length = 0.1f + 0.05f * Dice(dice);
                Vector3 tip = root + down * length + skin * length * 0.3f;
                mesh.Lock(row == 2 ? Shade : Face, root, tip, skin, 0.07f, 0.024f);
            }
        }
        return mesh.Build("UA_Goat Upper Leg");
    }

    /// <summary>A shin down to the fetlock, short hair round it.</summary>
    private static Mesh MakeLower(bool front)
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        float length = front ? FrontShin : BackShin;
        GoatRing[] shin =
        {
            new GoatRing(0f, 0.02f, 0f, 0.054f, 0.052f, 2.2f),
            new GoatRing(0f, -length * 0.4f, 0f, 0.046f, 0.044f, 2.2f),
            new GoatRing(0f, -length * 0.85f, 0f, 0.043f, 0.042f, 2.2f),
            new GoatRing(0f, -length - 0.015f, 0f, 0.044f, 0.043f, 2.2f),
        };
        mesh.Tube(Face, shin, 12, Vector3.forward, 0.4f, true, 1);
        System.Random dice = new System.Random(front ? 31 : 37);
        for (int row = 0; row < 3; row++)
        {
            float at = 0.02f + row * length * 0.27f;
            for (int k = 0; k < 7; k++)
            {
                float angle = 2f * Mathf.PI * (k + (row % 2) * 0.5f) / 7f;
                Vector3 skin = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Vector3 root = new Vector3(0f, -at, 0f) + skin * 0.038f;
                float hair = 0.06f + 0.03f * Dice(dice);
                mesh.Lock(Face, root, root + Vector3.down * hair + skin * hair * 0.25f, skin, 0.05f, 0.018f);
            }
        }
        return mesh.Build(front ? "UA_Goat Front Shin" : "UA_Goat Back Shin");
    }

    /// <summary>A hoof below its fetlock (the joint it bends at, at the hoof's own origin): a short white pastern, then the dark hoof.</summary>
    private static Mesh MakeHoof()
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        GoatRing[] pastern =
        {
            new GoatRing(0f, 0.02f, 0f, 0.045f, 0.044f, 2.2f),
            new GoatRing(0f, -0.015f, 0.003f, 0.043f, 0.042f, 2.2f),
            new GoatRing(0f, -0.03f, 0.005f, 0.046f, 0.045f, 2.2f),
        };
        mesh.Tube(Face, pastern, 12, Vector3.forward, 0.2f, true, 1);
        GoatRing[] hoofRings =
        {
            new GoatRing(0f, -0.025f, 0.005f, 0.048f, 0.047f, 2.2f),
            new GoatRing(0f, -0.055f, 0.008f, 0.053f, 0.052f, 2.4f),
            new GoatRing(0f, -HoofHeight + 0.004f, 0.01f, 0.055f, 0.053f, 2.6f),
            new GoatRing(0f, -HoofHeight, 0.01f, 0.035f, 0.033f, 2f),
        };
        mesh.Tube(Hoof, hoofRings, 12, Vector3.forward, 0.2f, true, 1);
        return mesh.Build("UA_Goat Hoof");
    }

    /// <summary>The goat's materials: copies of the game's item shading (the snakes'), coloured or textured.</summary>
    private static Material[] MakeMaterials()
    {
        Material[] made = new Material[Kinds];
        made[Coat] = Make("UA_GoatCoat", Fur("UA_GoatCoat", true, 0.85f), Color.white, 0.1f);
        made[Shade] = Make("UA_GoatHair", Fur("UA_GoatHair", false, 0.8f), new Color(0.9f, 0.89f, 0.87f), 0.1f);
        made[Face] = Make("UA_GoatFace", Fur("UA_GoatFace", false, 0.45f), Color.white, 0.1f);
        made[Horn] = Make("UA_GoatHorn", HornBands(), Color.white, 0.35f);
        made[Hoof] = Make("UA_GoatHoof", null, new Color(0.2f, 0.19f, 0.2f), 0.3f);
        made[Eye] = Make("UA_GoatEye", null, new Color(0.03f, 0.03f, 0.03f), 0.9f);
        made[Nose] = Make("UA_GoatNose", null, new Color(0.17f, 0.15f, 0.16f), 0.5f);
        return made;
    }

    private static Material Make(string name, Texture2D texture, Color tint, float smooth)
    {
        Material made = new Material(Snakes.Skin()) { name = name };
        made.SetTexture("_BaseTexture", texture != null ? texture : Texture2D.whiteTexture);
        made.SetColor("_Tint", tint);
        made.SetColor("_BaseColor", tint);
        made.SetFloat("_BaseSmooth", smooth);
        // No paint layers over it.
        foreach (string layer in new[] { "_Texture1", "_Texture2" })
        {
            if (made.HasProperty(layer))
            {
                made.SetTexture(layer, Texture2D.whiteTexture);
            }
        }
        return made;
    }

    /// <summary>
    /// The coat: off-white with strands of grey hair here and there running across u (down the body's sides, along the
    /// head, neck and legs), each a pixel or a few wide, some fainter; on the body's coat the grey stronger towards the
    /// belly (v 0.5, round the body from the top of the back at 0); `strength` how grey the strands get.
    /// </summary>
    private static Texture2D Fur(string name, bool belly, float strength)
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
        System.Random dice = new System.Random(belly ? 5 : 6);
        float[] strands = new float[size];
        for (int x = 0; x < size;)
        {
            // A strand (or a gap) a few pixels wide.
            int wide = 1 + (int)(Dice(dice) * 3f);
            float strandGrey = Dice(dice) < 0.35f ? 0.4f + 0.6f * Dice(dice) : 0f;
            for (int i = 0; i < wide && x < size; i++, x++)
            {
                strands[x] = strandGrey * (i == 0 || i == wide - 1 ? 0.7f : 1f);
            }
        }
        Color white = new Color(0.95f, 0.94f, 0.9f);
        Color grey = new Color(0.74f, 0.73f, 0.7f);
        Color under = new Color(0.82f, 0.81f, 0.78f);
        for (int x = 0; x < size; x++)
        {
            float streak = strands[x] * strength;
            for (int y = 0; y < size; y++)
            {
                float low = belly ? 1f - Mathf.Abs((y + 0.5f) / size * 2f - 1f) : 0.6f;
                Color colour = Color.Lerp(white, grey, streak * (0.2f + 0.8f * low));
                if (belly && low > 0.8f)
                {
                    colour = Color.Lerp(colour, under, (low - 0.8f) / 0.2f * 0.6f);
                }
                float grain = (Dice(dice) - 0.5f) * 0.03f;
                colour.r += grain;
                colour.g += grain;
                colour.b += grain;
                colour.a = 1f;
                texture.SetPixel(x, y, colour);
            }
        }
        texture.Apply(true, true);
        return texture;
    }

    /// <summary>The horns: light grey, banded darker every so often along them (v), darkening to the tip.</summary>
    private static Texture2D HornBands()
    {
        Texture2D texture = new Texture2D(8, 64, TextureFormat.RGBA32, true, false) { name = "UA_GoatHorn", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
        Color light = new Color(0.64f, 0.63f, 0.62f);
        Color band = new Color(0.47f, 0.46f, 0.46f);
        Color tip = new Color(0.36f, 0.35f, 0.36f);
        for (int y = 0; y < 64; y++)
        {
            float v = (y + 0.5f) / 64f;
            Color colour = Color.Lerp(y % 9 < 3 ? band : light, tip, v * v * 0.8f);
            for (int x = 0; x < 8; x++)
            {
                texture.SetPixel(x, y, colour);
            }
        }
        texture.Apply(true, true);
        return texture;
    }
}

/// <summary>A mesh made of the goat's shapes (rounded tubes, tufts of hair, balls), one submesh for each of its materials.</summary>
internal sealed class GoatMesh
{
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int>[] triangles;

    internal GoatMesh(int kinds)
    {
        triangles = Enumerable.Range(0, kinds).Select(_ => new List<int>()).ToArray();
    }

    private int Add(Vector3 point, Vector3 normal, Vector2 uv)
    {
        points.Add(point);
        normals.Add(normal);
        uvs.Add(uv);
        return points.Count - 1;
    }

    /// <summary>A triangle facing outward (the side Unity draws).</summary>
    private void Face(int kind, int a, int b, int c, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(points[b] - points[a], points[c] - points[a]), outward) < 0f)
        {
            (b, c) = (c, b);
        }
        triangles[kind].Add(a);
        triangles[kind].Add(b);
        triangles[kind].Add(c);
    }

    /// <summary>
    /// A rounded tube through rings (smoothed in between), each turned along the way it goes with its top towards `up`,
    /// closed at both ends; u along it (a tile every `tile` metres) and v once round it from the top, or the other way
    /// round (`roundU`).
    /// </summary>
    internal void Tube(int kind, IList<GoatRing> rings, int sides, Vector3 up, float tile, bool roundU, int between)
    {
        List<GoatRing> path = GoatBody.Smooth(rings, between);
        int n = path.Count;
        Vector3[] tangent = new Vector3[n];
        Vector3[] right = new Vector3[n];
        Vector3[] top = new Vector3[n];
        float[] along = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 t = path[Mathf.Min(i + 1, n - 1)].at - path[Mathf.Max(i - 1, 0)].at;
            tangent[i] = t.sqrMagnitude > 1e-10f ? t.normalized : i > 0 ? tangent[i - 1] : Vector3.forward;
            Vector3 r = Vector3.Cross(up, tangent[i]);
            right[i] = r.sqrMagnitude > 1e-8f ? r.normalized : i > 0 ? right[i - 1] : Vector3.right;
            top[i] = Vector3.Cross(tangent[i], right[i]);
            if (i > 0)
            {
                along[i] = along[i - 1] + Vector3.Distance(path[i - 1].at, path[i].at);
            }
        }
        Vector3[,] grid = new Vector3[n, sides];
        for (int i = 0; i < n; i++)
        {
            float e = 2f / path[i].round;
            for (int k = 0; k < sides; k++)
            {
                float angle = 2f * Mathf.PI * k / sides;
                float s = Mathf.Sin(angle), c = Mathf.Cos(angle);
                grid[i, k] = path[i].at + right[i] * (path[i].width * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), e))
                    + top[i] * (path[i].height * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e));
            }
        }
        int[,] index = new int[n, sides + 1];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k <= sides; k++)
            {
                int kk = k % sides;
                Vector3 round = grid[i, (kk + 1) % sides] - grid[i, (kk + sides - 1) % sides];
                Vector3 ahead = grid[Mathf.Min(i + 1, n - 1), kk] - grid[Mathf.Max(i - 1, 0), kk];
                Vector3 outward = grid[i, kk] - path[i].at;
                Vector3 normal = Vector3.Cross(round, ahead);
                if (normal.sqrMagnitude < 1e-12f)
                {
                    normal = outward;
                }
                normal.Normalize();
                if (Vector3.Dot(normal, outward) < 0f)
                {
                    normal = -normal;
                }
                float u = along[i] / tile, v = k / (float)sides;
                index[i, k] = Add(grid[i, kk], normal, roundU ? new Vector2(v, u) : new Vector2(u, v));
            }
        }
        for (int i = 0; i < n - 1; i++)
        {
            for (int k = 0; k < sides; k++)
            {
                Vector3 outward = (grid[i, k] + grid[i + 1, (k + 1) % sides]) * 0.5f - (path[i].at + path[i + 1].at) * 0.5f;
                Face(kind, index[i, k], index[i, k + 1], index[i + 1, k + 1], outward);
                Face(kind, index[i, k], index[i + 1, k + 1], index[i + 1, k], outward);
            }
        }
        Cap(kind, path[0], -tangent[0], index, 0, sides, along[0] / tile, roundU);
        Cap(kind, path[n - 1], tangent[n - 1], index, n - 1, sides, along[n - 1] / tile, roundU);
    }

    private void Cap(int kind, GoatRing ring, Vector3 outward, int[,] index, int i, int sides, float u, bool roundU)
    {
        int tip = Add(ring.at + outward * Mathf.Min(ring.width, ring.height) * 0.4f, outward, roundU ? new Vector2(0.5f, u) : new Vector2(u, 0.5f));
        for (int k = 0; k < sides; k++)
        {
            Face(kind, tip, index[i, k], index[i, k + 1], outward);
        }
    }

    /// <summary>A tuft of hair: a thin five-sided cone from its root to its tip.</summary>
    internal void Tuft(int kind, Vector3 root, Vector3 tip, float radius)
    {
        Vector3 axis = tip - root;
        float length = axis.magnitude;
        if (length < 1e-4f)
        {
            return;
        }
        axis /= length;
        Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 other = Vector3.Cross(axis, side);
        const int corners = 5;
        int[] ring = new int[corners];
        Vector3[] ways = new Vector3[corners];
        for (int k = 0; k < corners; k++)
        {
            float angle = 2f * Mathf.PI * k / corners;
            ways[k] = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
            ring[k] = Add(root + ways[k] * radius, (ways[k] + axis * 0.35f).normalized, new Vector2(k / (float)corners, 0.1f));
        }
        int point = Add(tip, axis, new Vector2(0.5f, 0.9f));
        for (int k = 0; k < corners; k++)
        {
            Face(kind, ring[k], ring[(k + 1) % corners], point, ways[k] + ways[(k + 1) % corners]);
        }
    }

    /// <summary>
    /// A lock of hair lying along the skin: a flattened cone from its root (in the skin) to its tip, wider across than it
    /// is thick, shaded like the skin it grows from (its normals leaning to the skin's), so a coat of them looks soft.
    /// </summary>
    internal void Lock(int kind, Vector3 root, Vector3 tip, Vector3 skin, float width, float thickness)
    {
        Vector3 axis = tip - root;
        float length = axis.magnitude;
        if (length < 1e-4f)
        {
            return;
        }
        axis /= length;
        Vector3 across = Vector3.Cross(skin, axis);
        if (across.sqrMagnitude < 1e-6f)
        {
            across = Vector3.Cross(Vector3.up, axis);
        }
        across.Normalize();
        Vector3 up = Vector3.Cross(axis, across).normalized;
        if (Vector3.Dot(up, skin) < 0f)
        {
            up = -up;
        }
        const int corners = 6;
        int[] ring = new int[corners];
        Vector3[] ways = new Vector3[corners];
        for (int k = 0; k < corners; k++)
        {
            float angle = 2f * Mathf.PI * k / corners;
            ways[k] = across * (Mathf.Cos(angle) * width * 0.5f) + up * (Mathf.Sin(angle) * thickness * 0.5f);
            ring[k] = Add(root + ways[k], (skin * 0.7f + ways[k].normalized * 0.3f).normalized, new Vector2(k / (float)corners, 0.1f));
        }
        int point = Add(tip, (skin * 0.7f + axis * 0.3f).normalized, new Vector2(0.5f, 0.9f));
        for (int k = 0; k < corners; k++)
        {
            Face(kind, ring[k], ring[(k + 1) % corners], point, ways[k] + ways[(k + 1) % corners]);
        }
    }

    /// <summary>A ball (an eye, the nose), its radii along x, y and z.</summary>
    internal void Ball(int kind, Vector3 centre, Vector3 radii, int bands = 8, int sides = 12)
    {
        int start = points.Count;
        for (int b = 0; b <= bands; b++)
        {
            float lat = Mathf.PI * b / bands;
            for (int s = 0; s <= sides; s++)
            {
                float lon = 2f * Mathf.PI * s / sides;
                Vector3 unit = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
                Vector3 normal = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z).normalized;
                Add(centre + Vector3.Scale(unit, radii), normal, new Vector2(s / (float)sides, b / (float)bands));
            }
        }
        for (int b = 0; b < bands; b++)
        {
            for (int s = 0; s < sides; s++)
            {
                int a = start + b * (sides + 1) + s;
                Vector3 outward = points[a + sides + 2] - centre + (points[a] - centre);
                Face(kind, a, a + 1, a + sides + 2, outward);
                Face(kind, a, a + sides + 2, a + sides + 1, outward);
            }
        }
    }

    internal Mesh Build(string name)
    {
        Mesh mesh = new Mesh { name = name };
        mesh.SetVertices(points);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = triangles.Length;
        for (int i = 0; i < triangles.Length; i++)
        {
            mesh.SetTriangles(triangles[i], i);
        }
        mesh.RecalculateBounds();
        return mesh;
    }
}

/// <summary>One goat: its life on the mountain (on the host), its body and voice (on every game) and the network between.</summary>
internal sealed class Goat : MonoBehaviourPun, IPunObservable
{
    private enum State : byte
    {
        /// <summary>Head down, eating.</summary>
        Grazing,
        /// <summary>Head up, looking about.</summary>
        Standing,
        /// <summary>Walking to a spot nearby, leaping where it cannot walk.</summary>
        Roaming,
        /// <summary>In the air, from one foothold to another.</summary>
        Leaping,
        /// <summary>Facing the scout it will charge, head down, pawing the ground (having bleated).</summary>
        WindingUp,
        Charging,
        /// <summary>After a charge: stopped dead by its ram, or skidding to a stop after a miss, shaking its head.</summary>
        Recovering,
        /// <summary>Lost its footing: dropping to the ground below.</summary>
        Falling,
    }

    /// <summary>Its feet keep to ground this many metres up or down a step; more is a ledge or a drop (it leaps those).</summary>
    private const float StepHeight = 0.45f;
    /// <summary>Its body's middle above its feet (along the ground's up), and how wide it is there (for walls and overhangs).</summary>
    private const float BodyLift = 0.7f;
    private const float BodyRadius = 0.22f;
    private const float EyeHeight = 1.1f;
    /// <summary>How fast it turns (degrees a second): about, while charging (so a scout can step aside), facing a scout before it.</summary>
    private const float RoamTurn = 220f;
    private const float ChargeTurn = 100f;
    private const float FaceTurn = 400f;
    /// <summary>A leap: the crouch before it, the fall's pull (a little more than the world's, for a springy hop), the landing.</summary>
    private const float Crouch = 0.12f;
    private const float Landing = 0.15f;
    private const float LeapGravity = 22f;
    private static readonly float[] LeapAngles = { 0f, -25f, 25f, -50f, 50f, -80f, 80f };
    private static readonly float[] LeapDistances = { 0.6f, 1.2f, 2f, 3f, 4f, Goats.MaxLeapAcross };
    /// <summary>
    /// Where each leg (front right, front left, back right, back left) is in its stride (as a share of it) as the hind left
    /// one comes down: walking, they come down one after another (hind left, front left, hind right, front right), as
    /// goats walk; galloping, the hind legs one just after the other, then the front ones.
    /// </summary>
    private static readonly float[] WalkTimes = { 0.25f, 0.75f, 0.5f, 0f };
    private static readonly float[] GallopTimes = { 0.4f, 0.5f, 0.9f, 0f };
    /// <summary>After a miss it skids to a stop this fast (m/s each second).</summary>
    private const float SkidBrake = 12f;
    /// <summary>With no living scout this near, it stands still (nobody would see it).</summary>
    private const float AwakeDistance = 120f;

    private static float WalkNormal => Mathf.Cos(Goats.MaxSlope * Mathf.Deg2Rad);
    private static float RestNormal => Mathf.Cos(Goats.RestSlope * Mathf.Deg2Rad);
    private static float LandNormal => Mathf.Cos(Goats.LandSlope * Mathf.Deg2Rad);

    private Vector3 home;
    private int pair;
    private bool follower;
    private Goat partner;

    private State state = State.Grazing;
    private float stateSince;
    private float stateUntil;
    private Vector3 feet;
    /// <summary>Which way the ground under it faces (it walks along it, up slopes as steep as it can).</summary>
    private Vector3 groundUp = Vector3.up;
    private float yaw;
    private float speedNow;
    private Character target;
    private float nextDecision;
    private float nextBleat;
    private Vector3 roamTo;
    private float bestLeft;
    private float bestSince;
    private float chargeStart;
    private float chargeRun;
    /// <summary>In its last few metres it runs straight on, at a point far ahead along its way.</summary>
    private bool committed;
    private Vector3 chargeLine;
    private float skid;
    private float fallSpeed;

    // A leap (on every game, from the host's word).
    private bool leaping;
    private Vector3 leapFrom;
    private Vector3 leapTo;
    private float leapPeak;
    private float leapSeconds;
    private float leapStart = float.NegativeInfinity;
    private State afterLeap = State.Roaming;

    // Network.
    private Vector3 netFeet;
    private float netYaw;
    private int netTarget = -1;

    // The body.
    private Transform body;
    private Transform tail;
    private Transform neck;
    private Transform head;
    private readonly Transform[] uppers = new Transform[4];
    private readonly Transform[] lowers = new Transform[4];
    private readonly Transform[] pasterns = new Transform[4];
    private readonly float[] flicks = new float[4];
    /// <summary>Its body shown this far from its feet (rearing up onto a slope it comes to, its hind hooves stay on their ground), and where that is going.</summary>
    private Vector3 rootShift;
    private Vector3 shownShift;
    /// <summary>How far its neck is raised above the pose it wants (degrees), so its head is never in the ground or the rock.</summary>
    private float headLift;
    private const float MaxHeadLift = 60f;
    private readonly float[] swings = new float[4];
    private readonly float[] bends = new float[4];
    private Vector3 posedAt;
    private float pace;
    private float phase;
    private float bodyPitch;
    /// <summary>Which way the ground round its feet faces (its whole body lies along it), and its body's up as shown (eased towards that; straight up in a leap).</summary>
    private Vector3 surfaceUp = Vector3.up;
    private Vector3 bodyUp = Vector3.up;
    private float nextTilt;
    private float neckPitch;
    private float neckYaw;
    private float headPitch;
    private float seed;
    private float rammedAt = float.NegativeInfinity;
    private float bleatedAt = float.NegativeInfinity;
    private float tailFlick = float.NegativeInfinity;
    private float landedAt = float.NegativeInfinity;

    // Its voice and hooves.
    private AudioSource voice;
    private AudioSource walkLoop;
    private AudioSource gallopLoop;
    private AudioSource thud;

    /// <summary>For the tests: told what to do (it chooses nothing to do itself and stays as it is between orders, though it still decides on a charge), and noticing no scout (no charge then either).</summary>
    internal bool Puppet;
    internal bool Unaware;

    /// <summary>For the tests: what it is doing, where it stands, its spot, its pair (0 alone), its leaps and walking, why it last stopped, how fast it goes.</summary>
    internal string Doing => state.ToString();
    internal Vector3 Feet => feet;
    internal Vector3 Home => home;
    internal int PairKey => pair;
    internal int Leaps { get; private set; }
    internal float HighestLeap { get; private set; }
    internal float SteepestWalked { get; private set; }
    internal string LastStop { get; private set; } = "";
    internal float Speed => pace;
    /// <summary>For the tests: its forehead (where a ram lands from), and how far its head points below the level (degrees; about 40 at rest, 80 charging, 90 grazing).</summary>
    internal Vector3 Nose => head != null ? head.TransformPoint(GoatBody.Forehead) : feet + Forward * 0.8f + Vector3.up * 0.65f;
    internal float HeadDown => head != null ? Mathf.Asin(Mathf.Clamp(-head.forward.y, -1f, 1f)) * Mathf.Rad2Deg : 40f;
    /// <summary>For the tests: its bleats heard here, and when the last was.</summary>
    internal int Bleats { get; private set; }
    internal float BleatedAt => bleatedAt;
    /// <summary>For the tests: how loud its hooves are now (walking and galloping together; 0 when silent).</summary>
    internal float HoofLoudness => (walkLoop != null && walkLoop.isPlaying ? walkLoop.volume : 0f) + (gallopLoop != null && gallopLoop.isPlaying ? gallopLoop.volume : 0f);

    /// <summary>For the tests: the soles of its hooves, and points along it (its body's middle, the base of its neck, its head, forehead and snout).</summary>
    internal IEnumerable<Vector3> Soles()
    {
        for (int i = 0; i < 4; i++)
        {
            if (pasterns[i] != null)
            {
                yield return pasterns[i].TransformPoint(new Vector3(0f, -GoatBody.HoofHeight, 0f));
            }
        }
    }

    /// <summary>For the tests: the most its knees and its hooves (at their fetlocks) have bent since asked last (degrees), read and reset.</summary>
    internal float KneeBend { get; private set; }
    internal float HoofFlick { get; private set; }

    internal void ResetBends()
    {
        KneeBend = 0f;
        HoofFlick = 0f;
    }

    internal IEnumerable<Vector3> BodyPoints()
    {
        if (body == null)
        {
            yield break;
        }
        yield return body.position;
        yield return neck.position;
        yield return head.position;
        yield return head.TransformPoint(GoatBody.Forehead);
        yield return head.TransformPoint(GoatBody.Muzzle);
    }

    private Vector3 Forward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

    private bool Calm => state == State.Grazing || state == State.Standing || state == State.Roaming;

    private void Awake()
    {
        object[] data = photonView != null ? photonView.InstantiationData : null;
        home = data != null && data.Length > 0 && data[0] is Vector3 h ? h : transform.position;
        pair = data != null && data.Length > 1 && data[1] is int p ? p : 0;
        follower = data != null && data.Length > 2 && data[2] is bool f && f;
        feet = transform.position;
        netFeet = feet;
        yaw = transform.eulerAngles.y;
        netYaw = yaw;
        posedAt = feet;
        seed = Random.Range(0f, 100f);
        nextBleat = Time.time + Random.Range(5f, Goats.BleatEveryMax);
        Begin(State.Grazing, Random.Range(3f, 12f));
        BuildBody();
        voice = Source(null, Goats.BleatReach, false);
        walkLoop = Source(Goats.HoovesClip, Goats.HoovesReach, true);
        gallopLoop = Source(Goats.GallopClip, Goats.GallopReach, true);
        thud = Source(null, Goats.ThudReach, false);
        Goats.All.Add(this);
    }

    private void OnDestroy()
    {
        Goats.All.Remove(this);
    }

    /// <summary>Its parts on their pivots (see <see cref="GoatBody"/>), and its head solid like its body.</summary>
    private void BuildBody()
    {
        Material[] skins = GoatBody.Materials;
        body = Part("Body", transform, GoatBody.BodyPivot, Quaternion.identity, GoatBody.BodyMesh, skins);
        tail = Part("Tail", body, GoatBody.TailPivot, GoatBody.TailRest, GoatBody.TailMesh, skins);
        neck = Part("Neck", body, GoatBody.NeckPivot, Quaternion.AngleAxis(GoatBody.NeckRest, Vector3.right), GoatBody.NeckMesh, skins);
        head = Part("Head", neck, new Vector3(0f, 0f, GoatBody.NeckLength), Quaternion.AngleAxis(GoatBody.HeadRest, Vector3.right), GoatBody.HeadMesh, skins);
        for (int i = 0; i < 4; i++)
        {
            uppers[i] = Part("Leg " + i, body, GoatBody.Hips[i], Quaternion.identity, GoatBody.Upper(i), skins);
            lowers[i] = Part("Shin " + i, uppers[i], i < 2 ? GoatBody.FrontKnee : GoatBody.BackKnee, Quaternion.identity, GoatBody.Lower(i), skins);
            pasterns[i] = Part("Hoof " + i, lowers[i], new Vector3(0f, -(i < 2 ? GoatBody.FrontShin : GoatBody.BackShin), 0f), Quaternion.identity, GoatBody.HoofMesh, skins);
        }
        SphereCollider skull = head.gameObject.AddComponent<SphereCollider>();
        skull.center = new Vector3(0f, 0.01f, 0.1f);
        skull.radius = 0.13f;
    }

    private static Transform Part(string name, Transform parent, Vector3 at, Quaternion turn, Mesh mesh, Material[] skins)
    {
        GameObject made = new GameObject(name);
        made.transform.SetParent(parent, false);
        made.transform.localPosition = at;
        made.transform.localRotation = turn;
        made.AddComponent<MeshFilter>().sharedMesh = mesh;
        made.AddComponent<MeshRenderer>().sharedMaterials = skins;
        return made.transform;
    }

    /// <summary>A sound on the goat, through the game's sound-effects mixer, falling away about as the game's sounds do and gone at its reach; a loop starts silent.</summary>
    private AudioSource Source(AudioClip clip, float reach, bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = loop;
        source.playOnAwake = false;
        source.volume = loop ? 0f : 1f;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.minDistance = 1f;
        source.maxDistance = reach;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.1f, 0.72f), new Keyframe(0.3f, 0.36f), new Keyframe(0.6f, 0.12f), new Keyframe(1f, 0f)));
        source.outputAudioMixerGroup = SFX_Player.instance != null ? SFX_Player.instance.defaultMixerGroup : null;
        if (loop && clip != null)
        {
            source.time = Random.Range(0f, clip.length * 0.95f);
        }
        return source;
    }

    private void Begin(State next, float seconds)
    {
        state = next;
        stateSince = Time.time;
        stateUntil = Time.time + seconds;
    }

    private void Update()
    {
        if (partner == null && pair > 0)
        {
            partner = Goats.All.FirstOrDefault(g => g != null && g != this && g.pair == pair);
        }
        if (photonView.IsMine)
        {
            Think();
        }
        else
        {
            Follow();
        }
        Pose();
    }

    /// <summary>The others' games: it goes where the host says (a leap along the arc the host sent).</summary>
    private void Follow()
    {
        if (leaping)
        {
            float since = Time.time - leapStart;
            feet = LeapNow(since);
            if (since >= Crouch + leapSeconds + Landing)
            {
                leaping = false;
            }
        }
        else
        {
            feet = (netFeet - feet).sqrMagnitude > 4f ? netFeet : Vector3.Lerp(feet, netFeet, Time.deltaTime * 10f);
        }
        yaw = Mathf.LerpAngle(yaw, netYaw, Time.deltaTime * 10f);
        if (netTarget < 0)
        {
            target = null;
        }
        else if (target == null || target.photonView == null || target.photonView.ViewID != netTarget)
        {
            PhotonView view = PhotonView.Find(netTarget);
            target = view != null ? view.GetComponent<Character>() : null;
        }
    }

    /// <summary>The host's goat.</summary>
    private void Think()
    {
        float now = Time.time;
        if (state == State.Leaping)
        {
            Fly(now);
            return;
        }
        if (state == State.Falling)
        {
            Drop(now);
            return;
        }
        if (!SomeoneWithin(AwakeDistance) && !Puppet)
        {
            speedNow = 0f;
            return;
        }
        if (now >= nextBleat)
        {
            // Now and then a bleat, while someone is near enough to hear it.
            nextBleat = now + Random.Range(Goats.BleatEveryMin, Goats.BleatEveryMax);
            if (Calm && SomeoneWithin(Goats.BleatHeardFrom))
            {
                Bleat(Random.Range(0.94f, 1.06f));
            }
        }
        if (Calm && !Unaware && now >= nextDecision)
        {
            Decide(now);
        }
        switch (state)
        {
            case State.Grazing:
            case State.Standing:
                speedNow = 0f;
                if (!Puppet && (now >= stateUntil || (state == State.Grazing && follower && partner != null && Goats.Flat(feet, partner.feet) > 10f)))
                {
                    NextActivity();
                }
                StayOnGround();
                break;
            case State.Roaming:
                Roam(now);
                break;
            case State.WindingUp:
                WindUp(now);
                break;
            case State.Charging:
                Charge(now);
                break;
            case State.Recovering:
                Recover(now);
                break;
        }
    }

    private void Bleat(float pitch)
    {
        photonView.RPC(nameof(RPC_Bleat), RpcTarget.All, Random.Range(0, Mathf.Max(1, Goats.BleatCount)), pitch);
    }

    /// <summary>A bleat, on every game (its head comes up a little as it calls).</summary>
    [PunRPC]
    internal void RPC_Bleat(int which, float pitch)
    {
        AudioClip clip = Goats.Bleat(which);
        bleatedAt = Time.time;
        if (voice == null || clip == null)
        {
            return;
        }
        voice.clip = clip;
        voice.pitch = pitch;
        voice.volume = Goats.BleatVolume;
        voice.Play();
        Bleats++;
        Goats.BleatsHeard++;
        Goats.LastBleat = Time.time;
    }

    /// <summary>
    /// Once a scout is within reach and in sight, at most every 20 s: charge them (one time in four: it bleats first) or
    /// go on as it was.
    /// </summary>
    private void Decide(float now)
    {
        Character scout = Noticed();
        if (scout == null)
        {
            return;
        }
        nextDecision = now + Goats.DecideEvery;
        float roll = Goats.NextRoll ?? Random.value;
        Goats.NextRoll = null;
        bool charge = roll < Goats.ChargeChance;
        Goats.DecidedAt.Add(now);
        Plugin.Debug($"Goat at {feet:F0}: a scout {Vector3.Distance(scout.Center, feet):0} m away; {(charge ? "it charges" : "it goes on grazing")}.");
        if (charge)
        {
            target = scout;
            Begin(State.WindingUp, Goats.WindUpSeconds);
            Goats.ChargeBleats++;
            nextBleat = now + Random.Range(Goats.BleatEveryMin, Goats.BleatEveryMax);
            Bleat(Random.Range(1.06f, 1.14f));
        }
    }

    /// <summary>The nearest scout within its notice, in sight, in the Alps.</summary>
    private Character Noticed()
    {
        Character best = null;
        float nearest = Goats.NoticeDistance;
        Vector3 eye = feet + Vector3.up * EyeHeight;
        foreach (Character scout in Character.AllCharacters)
        {
            if (!Fit(scout) || OutOfAlps(scout))
            {
                continue;
            }
            float distance = Vector3.Distance(scout.Center, eye);
            if (distance <= nearest && HelperFunctions.LineCheck(eye, scout.Center, HelperFunctions.LayerType.TerrainMap).transform == null)
            {
                nearest = distance;
                best = scout;
            }
        }
        return best;
    }

    /// <summary>A scout it can go for: awake and about, not in the middle of a warp.</summary>
    private static bool Fit(Character scout)
    {
        return scout != null && !scout.isBot && !scout.data.dead && scout.data.fullyConscious && !scout.warping;
    }

    private bool SomeoneWithin(float range)
    {
        foreach (Character scout in Character.AllCharacters)
        {
            if (scout != null && !scout.isBot && !scout.data.dead && Vector3.Distance(scout.Center, feet) <= range)
            {
                return true;
            }
        }
        return false;
    }

    private bool ScoutNear(Vector3 at, float range)
    {
        foreach (Character scout in Character.AllCharacters)
        {
            if (scout != null && !scout.isBot && !scout.data.dead && Goats.Flat(scout.Center, at) <= range && Mathf.Abs(scout.Center.y - at.y) < 2.5f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Grazing done it looks up; looking about done it roams somewhere (most often) or grazes again; one of a pair goes back to the other first.</summary>
    private void NextActivity()
    {
        if (groundUp.y < RestNormal)
        {
            // On ground too steep to rest on (a cliff it climbs), it goes on to where it can.
            if (PickRoam(out Vector3 onward))
            {
                StartRoam(onward);
                return;
            }
            Begin(State.Standing, Random.Range(1f, 2.5f));
            return;
        }
        if (state == State.Grazing)
        {
            Begin(State.Standing, Random.Range(1.5f, 4f));
            return;
        }
        if (follower && partner != null && Goats.Flat(feet, partner.feet) > 6f && PickNear(partner.feet, 1.5f, 4f, out Vector3 near))
        {
            StartRoam(near);
            return;
        }
        if (Random.value < 0.7f && PickRoam(out Vector3 to))
        {
            StartRoam(to);
            return;
        }
        Begin(State.Grazing, Random.Range(6f, 15f));
    }

    private void StartRoam(Vector3 to)
    {
        roamTo = to;
        bestLeft = Distance3(feet, roamTo);
        bestSince = Time.time;
        Begin(State.Roaming, 25f);
    }

    /// <summary>Somewhere to roam: 4-15 m off on ground it can rest on (up a slope or a ledge as well), within its range of its spot; one of a pair keeps near the other.</summary>
    private bool PickRoam(out Vector3 to)
    {
        if (follower && partner != null)
        {
            return PickNear(partner.feet, 2f, 5f, out to);
        }
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3 probe = feet + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(4f, 15f);
            Vector3 fromHome = probe - home;
            fromHome.y = 0f;
            if (fromHome.magnitude > Goats.RoamRange)
            {
                probe = new Vector3(home.x, probe.y, home.z) + fromHome.normalized * Random.Range(0f, Goats.RoamRange);
            }
            if (RestingAt(probe, 12f, out to) && !ScoutNear(to, 2.5f))
            {
                return true;
            }
        }
        to = feet;
        return false;
    }

    private bool PickNear(Vector3 centre, float min, float max, out Vector3 to)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector3 probe = centre + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(min, max);
            if (RestingAt(probe, 3f, out to) && !ScoutNear(to, 2f))
            {
                return true;
            }
        }
        to = feet;
        return false;
    }

    /// <summary>Ground it could rest on below a point (looked for from a little above it): the Alps', not too steep, room for its body.</summary>
    private static bool RestingAt(Vector3 probe, float above, out Vector3 at)
    {
        at = probe;
        if (!Physics.Raycast(probe + Vector3.up * above, Vector3.down, out RaycastHit hit, above + 15f, HelperFunctions.terrainMapMask) || hit.normal.y < RestNormal
            || Foreign(hit.collider) || !RoomAt(hit.point))
        {
            return false;
        }
        at = hit.point;
        return true;
    }

    /// <summary>Room for a goat standing here: nothing solid where its body and head go.</summary>
    internal static bool RoomAt(Vector3 at)
    {
        return !Physics.CheckSphere(at + Vector3.up * BodyLift, BodyRadius, HelperFunctions.terrainMapMask)
            && !Physics.CheckSphere(at + Vector3.up * 1.15f, 0.2f, HelperFunctions.terrainMapMask);
    }

    /// <summary>
    /// On its way: walking where it can, leaping where it cannot (a ledge, a drop, ground too steep), giving up when it
    /// gets no nearer for 3 s or a scout is in its way.
    /// </summary>
    private void Roam(float now)
    {
        float left = Distance3(feet, roamTo);
        if (Goats.Flat(feet, roamTo) < 0.4f && Mathf.Abs(feet.y - roamTo.y) < 1f || now >= stateUntil)
        {
            Begin(State.Grazing, Random.Range(6f, 15f));
            return;
        }
        if (left < bestLeft - 0.3f)
        {
            bestLeft = left;
            bestSince = now;
        }
        else if (now - bestSince > 3f)
        {
            LastStop = "no way on";
            Begin(State.Standing, Random.Range(1f, 2.5f));
            return;
        }
        if (ScoutNear(feet + Forward * 0.9f, 0.9f))
        {
            LastStop = "a scout in the way";
            Begin(State.Standing, Random.Range(1f, 2.5f));
            return;
        }
        if (Walk(roamTo, Goats.WalkSpeed, RoamTurn, false))
        {
            return;
        }
        if (FindLeap(roamTo, out Vector3 landing))
        {
            StartLeap(landing, State.Roaming);
            return;
        }
        Begin(State.Standing, Random.Range(1f, 2.5f));
    }

    /// <summary>Distance with height counting a little less (a goat on the ledge right above is nearly there).</summary>
    private static float Distance3(Vector3 a, Vector3 b)
    {
        float flat = Goats.Flat(a, b);
        float up = (a.y - b.y) * 0.7f;
        return Mathf.Sqrt(flat * flat + up * up);
    }

    /// <summary>A step of walking towards a point, turning as it goes (on the spot first if it faces away); false where it cannot walk on.</summary>
    private bool Walk(Vector3 towards, float speed, float turn, bool charging)
    {
        Vector3 flat = towards - feet;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f)
        {
            speedNow = 0f;
            return true;
        }
        if (!charging && flat.sqrMagnitude < 0.16f && Mathf.Abs(towards.y - feet.y) > StepHeight)
        {
            LastStop = towards.y > feet.y ? "right under it" : "right over it";
            speedNow = 0f;
            return false;
        }
        float wanted = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        yaw = Mathf.MoveTowardsAngle(yaw, wanted, turn * Time.deltaTime);
        float off = Mathf.Abs(Mathf.DeltaAngle(yaw, wanted));
        float pace = charging ? speed : speed * Mathf.Clamp01(1f - (off - 30f) / 60f);
        speedNow = pace;
        if (pace <= 0f)
        {
            return true;
        }
        if (!Step(Forward, pace * Time.deltaTime, out RaycastHit ground, out string stop))
        {
            speedNow = 0f;
            if (!charging && (stop == "a wall ahead" || stop == "the edge of the Alps") && off > 5f)
            {
                // Its head right by something it cannot climb, or at the edge of the Alps: it turns on the spot first.
                return true;
            }
            LastStop = stop;
            return false;
        }
        if (Mathf.Abs(ground.point.y - feet.y) > 0.005f)
        {
            SteepestWalked = Mathf.Max(SteepestWalked, Vector3.Angle(ground.normal, Vector3.up));
        }
        feet = ground.point;
        groundUp = ground.normal;
        return true;
    }

    /// <summary>
    /// One step its way along the ground: up onto whatever rises in front of it (as steep as it walks), along the slope it
    /// is on (up or down it, however steep), or over a crest onto the ground just below; false where it cannot walk on
    /// (ground too steep, a drop, a wall, no room for it).
    /// </summary>
    private bool Step(Vector3 heading, float step, out RaycastHit ground, out string stop)
    {
        stop = "";
        Vector3 along = Vector3.ProjectOnPlane(heading, groundUp);
        along = along.sqrMagnitude > 1e-6f ? along.normalized : heading;
        // A slope it can climb a little way ahead: it rears up onto it as it comes (see MeasureTilt), its head over it.
        bool slopeAhead = SlopeAhead(feet, groundUp, along, out _);
        if (Physics.Raycast(feet + groundUp * 0.25f, along, out RaycastHit ahead, step + 0.2f, HelperFunctions.terrainMapMask) && Vector3.Angle(ahead.normal, groundUp) > 5f)
        {
            ground = ahead;
            if (ahead.normal.y < WalkNormal)
            {
                stop = "too steep";
                return false;
            }
            if (LeavesAlps(ahead.collider, ahead.point))
            {
                stop = "the edge of the Alps";
                return false;
            }
            return Room(ahead.point, ahead.normal, out stop);
        }
        Vector3 probe = feet + along * step;
        if (Physics.Raycast(probe + groundUp * 0.35f, -groundUp, out ground, 0.7f, HelperFunctions.terrainMapMask)
            || Physics.Raycast(probe + Vector3.up * 0.3f, Vector3.down, out ground, StepHeight + 0.3f, HelperFunctions.terrainMapMask))
        {
            if (ground.normal.y < WalkNormal)
            {
                stop = "too steep";
                return false;
            }
            if (LeavesAlps(ground.collider, ground.point))
            {
                // Another biome's ground: it keeps to the Alps.
                stop = "the edge of the Alps";
                return false;
            }
            if (!slopeAhead && !HeadRoom(ground.point, ground.normal, along))
            {
                // Something it cannot climb right in front of its head: it stops short of it (and leaps, if it can).
                stop = "a wall ahead";
                return false;
            }
            return Room(ground.point, ground.normal, out stop);
        }
        stop = "a drop";
        return false;
    }

    /// <summary>Another biome's ground, where a goat does not go (the Alps' own, or ground of no biome, is its to walk).</summary>
    private static bool Foreign(Collider ground) => Map.IsOtherBiome(ground, Biome.BiomeType.Alpine);

    /// <summary>
    /// Whether going onto this ground would take it out of the Alps: onto another biome's ground from the Alps' (out of
    /// them, fallen, it may go wherever takes it nearer its spot, back into them).
    /// </summary>
    private bool LeavesAlps(Collider ground, Vector3 to)
    {
        return Foreign(ground) && (!OutOfAlps() || Goats.Flat(to, home) >= Goats.Flat(feet, home));
    }

    /// <summary>Out of the Alps now (fallen out of them): on another biome's ground.</summary>
    private bool OutOfAlps()
    {
        return Physics.Raycast(feet + groundUp * 0.4f, -groundUp, out RaycastHit under, 0.9f, HelperFunctions.terrainMapMask) && Foreign(under.collider);
    }

    /// <summary>A scout out of the Alps (on another biome's ground): a goat does not go for them there.</summary>
    private static bool OutOfAlps(Character scout)
    {
        return Physics.Raycast(scout.Center, Vector3.down, out RaycastHit under, 4f, HelperFunctions.terrainMapMask) && Foreign(under.collider);
    }

    /// <summary>A slope it can climb ahead of it, in reach of its front legs (at their height), steeper than where it stands, in the Alps.</summary>
    private bool SlopeAhead(Vector3 at, Vector3 up, Vector3 along, out RaycastHit face)
    {
        return Physics.Raycast(at + up * 0.35f, along, out face, 0.9f, HelperFunctions.terrainMapMask)
            && face.normal.y >= WalkNormal && Vector3.Angle(face.normal, up) > 20f && !LeavesAlps(face.collider, face.point);
    }

    /// <summary>Room for its chest and head ahead of it, standing there (from its chest to its snout, a hand's breadth round).</summary>
    private static bool HeadRoom(Vector3 at, Vector3 up, Vector3 along)
    {
        Vector3 ahead = Vector3.ProjectOnPlane(along, up).normalized;
        return !Physics.CheckCapsule(at + up * 0.75f + ahead * 0.45f, at + up * 0.95f + ahead * 0.85f, 0.12f, HelperFunctions.terrainMapMask);
    }

    /// <summary>Room for its body over the ground there (out from the slope and up a little).</summary>
    private static bool Room(Vector3 at, Vector3 up, out string stop)
    {
        stop = "";
        if (Physics.CheckSphere(at + up * 0.45f + Vector3.up * 0.2f, 0.2f, HelperFunctions.terrainMapMask))
        {
            stop = "a wall";
            return false;
        }
        return true;
    }

    /// <summary>Kept on the ground under it, ground it can walk (with none, or only a face too steep for it, it falls).</summary>
    private void StayOnGround()
    {
        if ((Physics.Raycast(feet + groundUp * 0.4f, -groundUp, out RaycastHit hit, 0.9f, HelperFunctions.terrainMapMask) && hit.normal.y >= WalkNormal)
            || (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out hit, 1.2f, HelperFunctions.terrainMapMask) && hit.normal.y >= WalkNormal))
        {
            feet = hit.point;
            groundUp = hit.normal;
            return;
        }
        fallSpeed = 0f;
        Begin(State.Falling, 0f);
    }

    /// <summary>
    /// A foothold to leap to, nearer where it is going: there itself, ahead and to the sides, 0.6-4.5 m off, up to 3.5 m
    /// higher or 6 m lower, ground it can stand on with room for it, and nothing in the way of the leap; the nearest its
    /// goal (smaller leaps first among equals).
    /// </summary>
    private bool FindLeap(Vector3 goal, out Vector3 landing)
    {
        landing = feet;
        Vector3 toGoal = goal - feet;
        toGoal.y = 0f;
        float now = Distance3(feet, goal);
        Vector3 ahead = toGoal.sqrMagnitude > 0.01f ? toGoal.normalized : Forward;
        float best = float.MaxValue;
        int tried = 0, noGround = 0, outOfReach = 0, tooSteep = 0, otherBiome = 0, noRoom = 0, scout = 0, noNearer = 0, inTheWay = 0;
        List<Vector3> probes = new List<Vector3>();
        if (toGoal.magnitude <= Goats.MaxLeapAcross)
        {
            probes.Add(goal);
        }
        foreach (float angle in LeapAngles)
        {
            foreach (float across in LeapDistances)
            {
                probes.Add(feet + Quaternion.Euler(0f, angle, 0f) * ahead * across);
            }
        }
        foreach (Vector3 probe in probes)
        {
            tried++;
            if (!Physics.Raycast(new Vector3(probe.x, feet.y + Goats.MaxLeapUp + 0.5f, probe.z), Vector3.down, out RaycastHit hit, Goats.MaxLeapUp + 0.5f + Goats.MaxLeapDown, HelperFunctions.terrainMapMask))
            {
                noGround++;
                continue;
            }
            float rise = hit.point.y - feet.y;
            float across = Goats.Flat(feet, hit.point);
            if (rise > Goats.MaxLeapUp || rise < -Goats.MaxLeapDown || across < 0.3f)
            {
                outOfReach++;
                continue;
            }
            if (hit.normal.y < LandNormal)
            {
                tooSteep++;
                continue;
            }
            if (LeavesAlps(hit.collider, hit.point))
            {
                otherBiome++;
                continue;
            }
            if (!RoomAt(hit.point))
            {
                noRoom++;
                continue;
            }
            if (ScoutNear(hit.point, 1.2f))
            {
                scout++;
                continue;
            }
            float left = Distance3(hit.point, goal);
            if (left >= now - 0.5f)
            {
                noNearer++;
                continue;
            }
            float turned = Vector3.Angle(ahead, Vector3.ProjectOnPlane(hit.point - feet, Vector3.up));
            float score = left + Mathf.Abs(rise) * 0.15f + across * 0.1f + turned * 0.01f;
            if (score >= best)
            {
                continue;
            }
            if (!ArcClear(feet, hit.point))
            {
                inTheWay++;
                continue;
            }
            best = score;
            landing = hit.point;
        }
        LeapReport = $"from {feet:F1} towards {goal:F1} ({now:0.0} m): {tried} footholds tried; no ground {noGround}, out of reach {outOfReach}, too steep {tooSteep}, "
            + $"another biome's {otherBiome}, no room {noRoom}, "
            + $"a scout there {scout}, no nearer {noNearer}, something in the way {inTheWay}; {(best < float.MaxValue ? $"leaps to {landing:F1}" : "no leap")}";
        return best < float.MaxValue;
    }

    /// <summary>For the tests: how its last look for a foothold to leap to went.</summary>
    internal string LeapReport { get; private set; } = "";

    /// <summary>Nothing solid where its body or its head goes along the leap.</summary>
    private static bool ArcClear(Vector3 from, Vector3 to)
    {
        Arc(from, to, out float peak, out float seconds);
        Vector3 way = to - from;
        way.y = 0f;
        way = way.sqrMagnitude > 1e-4f ? way.normalized : Vector3.forward;
        Vector3 lastBody = from + Vector3.up * BodyLift;
        Vector3 lastHead = from + Vector3.up * 0.95f + way * 0.8f;
        for (int i = 1; i <= 8; i++)
        {
            Vector3 point = ArcPoint(from, to, peak, seconds, seconds * i / 8f);
            // Its head too: rising, its nose is up (the head higher and nearer); coming down, level again.
            float rising = i <= 4 ? 1f : 0f;
            Vector3 body = point + Vector3.up * BodyLift;
            Vector3 headAt = point + Vector3.up * (0.95f + 0.15f * rising) + way * (0.8f - 0.15f * rising);
            if (Physics.Linecast(lastBody, body, HelperFunctions.terrainMapMask) || Physics.Linecast(lastHead, headAt, HelperFunctions.terrainMapMask))
            {
                return false;
            }
            lastBody = body;
            lastHead = headAt;
        }
        return true;
    }

    /// <summary>A leap's arc: as high as half a metre over its higher end (a little more the further it goes), and how long it takes.</summary>
    private static void Arc(Vector3 from, Vector3 to, out float peak, out float seconds)
    {
        peak = Mathf.Max(from.y, to.y) + 0.5f + Goats.Flat(from, to) * 0.12f;
        seconds = Mathf.Sqrt(2f * (peak - from.y) / LeapGravity) + Mathf.Sqrt(2f * (peak - to.y) / LeapGravity);
    }

    /// <summary>
    /// Where a leap is after t seconds: falling as anything thrown does, and along its way the faster the further it has
    /// come up a steep step (it springs up first, so it clears the edge it leaps from right below) or, down one, the
    /// sooner (out over the edge first, then down).
    /// </summary>
    private static Vector3 ArcPoint(Vector3 from, Vector3 to, float peak, float seconds, float t)
    {
        float up = Mathf.Sqrt(2f * (peak - from.y) / LeapGravity);
        float s = Mathf.Clamp01(t / seconds);
        float steep = (to.y - from.y) / Mathf.Max(Goats.Flat(from, to), 0.5f);
        float along = steep > 0f ? Mathf.Pow(s, 1f + Mathf.Clamp01(steep)) : 1f - Mathf.Pow(1f - s, 1f + Mathf.Clamp01(-steep));
        Vector3 flat = Vector3.Lerp(from, to, along);
        float y = t < up ? from.y + LeapGravity * up * t - 0.5f * LeapGravity * t * t : peak - 0.5f * LeapGravity * (t - up) * (t - up);
        return new Vector3(flat.x, y, flat.z);
    }

    private void StartLeap(Vector3 to, State then)
    {
        afterLeap = then;
        Leaps++;
        HighestLeap = Mathf.Max(HighestLeap, to.y - feet.y);
        Arc(feet, to, out float peak, out float seconds);
        Begin(State.Leaping, Crouch + seconds + Landing);
        photonView.RPC(nameof(RPC_Leap), RpcTarget.All, feet, to, peak, seconds);
    }

    /// <summary>A leap, on every game: from where it stands, along the arc, to its foothold.</summary>
    [PunRPC]
    internal void RPC_Leap(Vector3 from, Vector3 to, float peak, float seconds)
    {
        leapFrom = from;
        leapTo = to;
        leapPeak = peak;
        leapSeconds = Mathf.Max(seconds, 0.05f);
        leapStart = Time.time;
        leaping = true;
        Vector3 flat = to - from;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f)
        {
            yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            netYaw = yaw;
        }
    }

    private Vector3 LeapNow(float since)
    {
        if (since < Crouch)
        {
            return leapFrom;
        }
        float t = since - Crouch;
        return t >= leapSeconds ? leapTo : ArcPoint(leapFrom, leapTo, leapPeak, leapSeconds, t);
    }

    private void Fly(float now)
    {
        float since = now - leapStart;
        feet = LeapNow(since);
        if (since >= Crouch + leapSeconds + Landing)
        {
            leaping = false;
            feet = leapTo;
            groundUp = Vector3.up;
            Begin(afterLeap, afterLeap == State.Roaming ? 25f : Random.Range(1f, 2.5f));
            if (afterLeap == State.Roaming)
            {
                bestLeft = Distance3(feet, roamTo);
                bestSince = now;
            }
        }
    }

    /// <summary>Falling: down to the first ground it can stand on; a face too steep to stand on it slides down, off and out.</summary>
    private void Drop(float now)
    {
        fallSpeed = Mathf.Min(fallSpeed + 25f * Time.deltaTime, 30f);
        float step = fallSpeed * Time.deltaTime;
        if (Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, step + 0.3f, HelperFunctions.terrainMapMask))
        {
            if (hit.normal.y >= WalkNormal)
            {
                feet = hit.point;
                groundUp = hit.normal;
                fallSpeed = 0f;
                Begin(State.Standing, 1.5f);
                return;
            }
            Vector3 off = Vector3.ProjectOnPlane(Vector3.down, hit.normal).normalized + hit.normal * 0.5f;
            feet = hit.point + off * step;
            return;
        }
        feet += Vector3.down * step;
        if (now - stateSince > 5f)
        {
            // Fell out of the world: back at its spot.
            feet = home;
            groundUp = Vector3.up;
            Begin(State.Standing, 1.5f);
        }
    }

    private void FaceTowards(Vector3 point, float turn)
    {
        Vector3 flat = point - feet;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f)
        {
            yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, turn * Time.deltaTime);
        }
    }

    private void WindUp(float now)
    {
        speedNow = 0f;
        if (!Fit(target))
        {
            target = null;
            Begin(State.Standing, 1.5f);
            return;
        }
        FaceTowards(target.Center, FaceTurn);
        StayOnGround();
        if (state == State.WindingUp && now >= stateUntil)
        {
            chargeStart = now;
            chargeRun = 0f;
            committed = false;
            Goats.Charges++;
            Begin(State.Charging, Goats.MaxChargeSeconds);
        }
    }

    /// <summary>
    /// At the scout, turning after them only so fast, and in its last 5 m not at all (it runs straight on): the ram
    /// lands once its forehead reaches them (or any scout it runs into); it has missed once they are behind it, it runs
    /// out of breath (4 s, 30 m) or it cannot run on (a wall, a drop, ground too steep: it never runs off an edge).
    /// </summary>
    private void Charge(float now)
    {
        if (!Fit(target))
        {
            Miss("the scout is gone");
            return;
        }
        Character struck = Hits(target) ? target : Character.AllCharacters.FirstOrDefault(c => c != target && Fit(c) && Hits(c));
        if (struck != null)
        {
            Ram(struck);
            return;
        }
        Vector3 toScout = target.Center - feet;
        toScout.y = 0f;
        if (Vector3.Dot(toScout, Forward) < (committed ? 0.3f : -0.3f))
        {
            Miss("ran past");
            return;
        }
        if (now >= stateUntil || chargeRun >= Goats.MaxChargeRun)
        {
            Miss("out of breath");
            return;
        }
        float speed = Goats.ChargeSpeed * Mathf.Clamp01(0.3f + (now - chargeStart) / 0.35f);
        if (!committed && toScout.magnitude <= Goats.CommitDistance)
        {
            committed = true;
            chargeLine = feet + Forward * 100f;
        }
        Vector3 before = feet;
        if (!Walk(committed ? chargeLine : target.Center, speed, committed ? 0f : ChargeTurn, true))
        {
            Miss(LastStop);
            return;
        }
        chargeRun += Goats.Flat(before, feet);
    }

    private bool Hits(Character scout)
    {
        Vector3 forehead = Nose;
        if (Vector3.Distance(forehead, scout.Center) <= Goats.RamReach)
        {
            return true;
        }
        foreach (Bodypart part in scout.refs.ragdoll.partList)
        {
            if (part != null && Vector3.Distance(part.transform.position, forehead) <= 0.4f)
            {
                return true;
            }
        }
        return false;
    }

    private void Ram(Character scout)
    {
        photonView.RPC(nameof(RPC_Ram), RpcTarget.All, scout.photonView.ViewID, Forward);
        LastStop = "rammed";
        target = null;
        skid = 0f;
        speedNow = 0f;
        Begin(State.Recovering, 1.2f);
    }

    private void Miss(string why)
    {
        LastStop = why;
        target = null;
        Goats.Misses++;
        // Run past, it skids on; stopped by a wall or an edge, it stops there.
        skid = why == "ran past" || why == "out of breath" ? speedNow : 0f;
        speedNow = 0f;
        Begin(State.Recovering, 1.6f);
    }

    private void Recover(float now)
    {
        if (skid > 0.05f)
        {
            skid = Mathf.MoveTowards(skid, 0f, SkidBrake * Time.deltaTime);
            if (Step(Forward, skid * Time.deltaTime, out RaycastHit ground, out _))
            {
                feet = ground.point;
                groundUp = ground.normal;
            }
            else
            {
                skid = 0f;
            }
            speedNow = skid;
        }
        else
        {
            speedNow = 0f;
            StayOnGround();
        }
        if (state == State.Recovering && now >= stateUntil)
        {
            Begin(State.Grazing, Random.Range(4f, 10f));
        }
    }

    /// <summary>The ram, on every game: the blow heard (the beetles' hit, heavier), the scout hit on their own game.</summary>
    [PunRPC]
    internal void RPC_Ram(int targetView, Vector3 away)
    {
        PhotonView view = PhotonView.Find(targetView);
        Character scout = view != null ? view.GetComponent<Character>() : null;
        rammedAt = Time.time;
        if (thud != null && Goats.RamClip != null)
        {
            thud.PlayOneShot(Goats.RamClip, Goats.RamVolume);
            Goats.RamSounds++;
        }
        if (scout != null && scout.IsLocal)
        {
            Goats.Rammed(scout, away);
        }
    }

    // --- Orders, for the tests ---

    /// <summary>For the tests: here, facing that way, this its spot now.</summary>
    internal void PlaceAt(Vector3 at, Vector3 towards)
    {
        feet = at;
        home = at;
        netFeet = at;
        groundUp = Vector3.up;
        Vector3 flat = towards - at;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f)
        {
            yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }
        leaping = false;
        target = null;
        Begin(State.Standing, 2f);
        transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
    }

    internal void RoamTo(Vector3 to) => StartRoam(to);

    internal void GrazeNow(float seconds) => Begin(State.Grazing, seconds);

    internal void ChargeAt(Character scout)
    {
        target = scout;
        Begin(State.WindingUp, Goats.WindUpSeconds);
    }

    /// <summary>For the tests: its next decision may come at once (as if it had not decided for 20 s).</summary>
    internal void DecideSoon() => nextDecision = 0f;

    /// <summary>For the tests: its next bleat at once.</summary>
    internal void BleatSoon() => nextBleat = 0f;

    // --- The body, on every game ---

    /// <summary>
    /// Its pose: its whole body along the ground it is on (up a cliff, its hooves on the rock and its head up along it,
    /// never into it), its legs swinging and bending at the knee (walking in diagonal pairs, reaching further and quicker
    /// as it climbs, galloping in a charge, stretched out in a leap), the head down at the grass, up looking about or
    /// lowered with the horns forward to charge, lifted a little as it bleats; its tail flicks now and then. Its hooves
    /// are heard while it moves.
    /// </summary>
    private void Pose()
    {
        float dt = Time.deltaTime;
        float now = Time.time;
        float sinceLeap = now - leapStart;
        bool inLeap = leaping || state == State.Leaping;
        bool flying = inLeap && sinceLeap >= Crouch && sinceLeap < Crouch + leapSeconds;
        float flight = flying ? (sinceLeap - Crouch) / leapSeconds : 0f;
        // How far its feet went since the last frame: along the ground as it walks (up a cliff as well as on the level), or
        // at once, set up a step or onto a face it comes to (its body shown where it was, then going over smoothly). Its
        // leaps are not counted.
        Vector3 went = inLeap ? Vector3.zero : feet - posedAt;
        posedAt = feet;
        float wentFar = went.magnitude;
        bool stepped = wentFar > Goats.ChargeSpeed * dt * 1.5f + 0.02f && wentFar < 1f;
        if (stepped)
        {
            shownShift -= went;
            nextTilt = 0f;
        }
        float moved = stepped ? pace : dt > 0f && wentFar < 1f ? wentFar / dt : 0f;
        pace = Mathf.Lerp(pace, moved, Mathf.Clamp01(dt * 8f));

        if (now >= nextTilt)
        {
            nextTilt = now + 0.1f;
            MeasureTilt();
        }
        bodyUp = Vector3.Slerp(bodyUp, flying ? Vector3.up : surfaceUp, Mathf.Clamp01(dt * 6f)).normalized;
        Vector3 ahead = Vector3.ProjectOnPlane(Forward, bodyUp);
        if (ahead.sqrMagnitude < 1e-4f)
        {
            ahead = Vector3.ProjectOnPlane(transform.forward, bodyUp);
        }
        shownShift = Vector3.Lerp(shownShift, flying ? Vector3.zero : rootShift, Mathf.Clamp01(dt * 8f));
        transform.SetPositionAndRotation(feet + shownShift, Quaternion.LookRotation(ahead.normalized, bodyUp));
        float steep = Vector3.Angle(bodyUp, Vector3.up);
        float wantPitch = 0f;
        if (flying)
        {
            // Nose along its flight: up as it rises, down as it comes down.
            float rise = leapTo.y - leapFrom.y;
            wantPitch = Mathf.Lerp(Mathf.Clamp(rise * 8f + 15f, -10f, 40f), Mathf.Clamp(rise * 8f - 15f, -40f, 10f), flight);
        }
        else if (state == State.WindingUp)
        {
            wantPitch += 6f;
        }
        else if (state == State.Grazing)
        {
            wantPitch -= 6f;
        }
        bodyPitch = Mathf.LerpAngle(bodyPitch, wantPitch, Mathf.Clamp01(dt * 8f));

        // Legs (front right, front left, back right, back left): each stride, its hoof down and still while the body goes
        // over it, then lifted (the knee folding, the hoof flicking up at its fetlock) and brought forward, reaching out to
        // land; climbing in shorter, higher strides; galloping, longer and quicker.
        bool galloping = state == State.Charging || (state == State.Recovering && pace > 2f);
        bool climbing = !galloping && steep > 45f;
        float going = Mathf.Clamp01(pace / (galloping ? 3f : 0.8f));
        // Each stride as long as the way its body goes while a hoof is down is to the hoof's sweep back under it (so a hoof
        // stays where it came down), shorter the slower it goes.
        float stride = (galloping ? 1.6f : climbing ? 0.6f : 0.75f) * Mathf.Max(going, 0.3f);
        phase += pace / stride * Mathf.PI * 2f * dt;
        float[] offsets = galloping ? GallopTimes : WalkTimes;
        float duty = galloping ? 0.4f : 0.65f;
        float reach = going * (galloping ? 32f : climbing ? 18f : 23f);
        for (int i = 0; i < 4; i++)
        {
            float fold = going * (i < 2 ? (galloping || climbing ? 80f : 50f) : (galloping || climbing ? 60f : 35f));
            Stride(Mathf.Repeat(phase / (Mathf.PI * 2f) + offsets[i], 1f), duty, reach, fold, out swings[i], out bends[i], out flicks[i]);
        }
        float crouch = 0f;
        if (inLeap)
        {
            if (sinceLeap < Crouch)
            {
                crouch = Mathf.Sin(sinceLeap / Crouch * Mathf.PI * 0.5f) * 0.09f;
            }
            else if (flying)
            {
                // Front legs tucked up, hind legs pushed back, folding in again for the landing.
                float stretch = Mathf.Sin(Mathf.Clamp01(flight * 1.2f) * Mathf.PI);
                for (int i = 0; i < 4; i++)
                {
                    swings[i] = i < 2 ? -50f * stretch : 40f * stretch;
                    bends[i] = i < 2 ? 75f * stretch : 15f * stretch;
                    flicks[i] = i < 2 ? 35f * stretch : -10f * stretch;
                }
            }
            else
            {
                crouch = Mathf.Sin(Mathf.Clamp01((sinceLeap - Crouch - leapSeconds) / Landing) * Mathf.PI) * 0.09f;
            }
            if (!flying)
            {
                for (int i = 0; i < 4; i++)
                {
                    bends[i] = Mathf.Max(bends[i], crouch * 300f);
                    flicks[i] = Mathf.Min(flicks[i], -crouch * 120f);
                }
            }
        }
        if (state == State.WindingUp)
        {
            // A front hoof paws the ground.
            float paw = Mathf.Max(0f, Mathf.Sin((now - stateSince) * Mathf.PI * 2f * 2.2f));
            swings[0] = -20f + 45f * paw;
            bends[0] = 55f * paw;
            flicks[0] = 35f * paw;
        }
        for (int i = 0; i < 4; i++)
        {
            uppers[i].localRotation = Quaternion.AngleAxis(swings[i], Vector3.right);
            // A hind leg's joint there is its hock, folding the other way (the hoof forward under it).
            lowers[i].localRotation = Quaternion.AngleAxis(i < 2 ? bends[i] : -bends[i], Vector3.right);
            pasterns[i].localRotation = Quaternion.AngleAxis(flicks[i], Vector3.right);
            KneeBend = Mathf.Max(KneeBend, bends[i]);
            HoofFlick = Mathf.Max(HoofFlick, flicks[i]);
        }

        // Body: a bob with each stride (a push with each step up a cliff), low in a crouch.
        float bob = pace > 0.1f && !flying ? Mathf.Abs(Mathf.Sin(phase)) * (galloping ? 0.05f : climbing ? 0.04f : 0.02f) : 0f;
        body.localPosition = GoatBody.BodyPivot + new Vector3(0f, bob - crouch, 0f);
        body.localRotation = Quaternion.AngleAxis(-bodyPitch, Vector3.right);

        // Neck and head (against their rest: the neck raised 55 degrees, the head 40 below the level).
        float wantNeck = 0f, wantYaw = 0f, wantHead = 0f;
        switch (state)
        {
            case State.Grazing:
                wantNeck = 125f;
                wantHead = -70f + Mathf.Sin(now * Mathf.PI * 2f * 2.5f) * 4f;
                break;
            case State.Standing:
                Character watched = WatchedScout();
                wantYaw = watched != null ? LookYaw(watched.Center) * 0.6f : Mathf.Sin(now * 0.7f + seed) * 25f;
                if (steep > 50f)
                {
                    // On a cliff: its head up along it.
                    wantNeck = 30f;
                    wantHead = -15f;
                }
                break;
            case State.Roaming:
                // Climbing a cliff, its neck low along its body and its head up along the rock.
                wantNeck = steep > 50f ? 35f : 12f + Mathf.Sin(phase * 2f) * 2f;
                wantHead = steep > 50f ? -20f : -5f;
                break;
            case State.Leaping:
                wantNeck = -8f;
                wantHead = -10f;
                break;
            case State.WindingUp:
                wantNeck = 65f;
                wantHead = -30f + Mathf.Sin(now * 18f) * 3f;
                break;
            case State.Charging:
                wantNeck = 70f;
                wantHead = -30f;
                break;
            case State.Recovering:
                float since = now - stateSince;
                if (now - rammedAt < 1.5f)
                {
                    // A toss of the head as the ram lands.
                    wantNeck = since < 0.25f ? -15f : 0f;
                    wantHead = since < 0.25f ? -20f : 0f;
                }
                else
                {
                    // Missed: it shakes its head.
                    wantNeck = 20f;
                    wantYaw = since > 0.3f && since < 1.1f ? Mathf.Sin(since * 26f) * 25f : 0f;
                }
                break;
            case State.Falling:
                wantNeck = -10f;
                break;
        }
        if (inLeap && state != State.Leaping)
        {
            wantNeck = -8f;
            wantHead = -10f;
        }
        if (now - bleatedAt < 0.7f && state != State.WindingUp)
        {
            // It lifts its head to bleat.
            wantNeck -= 15f;
            wantHead -= 10f;
        }
        float quick = state == State.Recovering ? 14f : 6f;
        neckPitch = Mathf.Lerp(neckPitch, wantNeck, Mathf.Clamp01(dt * quick));
        neckYaw = Mathf.Lerp(neckYaw, wantYaw, Mathf.Clamp01(dt * quick));
        headPitch = Mathf.Lerp(headPitch, wantHead, Mathf.Clamp01(dt * quick));
        // Its head kept out of the ground and the rock (coming to a slope, down one onto level ground, under an overhang):
        // its neck raised smoothly as rock comes within a hand's breadth of its head, never less than keeps its head out of
        // it, and eased back down after. Grazing, its nose is in the grass.
        float mustLift = 0f, nearLift = 0f;
        if (state != State.Grazing)
        {
            mustLift = LiftFor(0f, 0f);
            nearLift = LiftFor(0.1f, mustLift);
        }
        headLift = Mathf.Max(mustLift, Mathf.Lerp(headLift, nearLift, Mathf.Clamp01(dt * (nearLift > headLift ? 10f : 3f))));
        PoseHead(headLift);

        // The tail flicks now and then (more often while it grazes).
        if (now > tailFlick + (state == State.Grazing ? Random.Range(1.5f, 4f) : Random.Range(4f, 9f)))
        {
            tailFlick = now;
        }
        float wag = now - tailFlick < 0.5f ? Mathf.Sin((now - tailFlick) * 30f) * 25f : 0f;
        tail.localRotation = Quaternion.AngleAxis(wag, Vector3.up) * GoatBody.TailRest;

        Hooves(galloping, flying, inLeap, sinceLeap);
    }

    /// <summary>Its neck and head as it wants them, the neck raised `lift` degrees more (its head kept nearly as level).</summary>
    private void PoseHead(float lift)
    {
        neck.localRotation = Quaternion.AngleAxis(neckYaw, Vector3.up) * Quaternion.AngleAxis(GoatBody.NeckRest + neckPitch - lift, Vector3.right);
        head.localRotation = Quaternion.AngleAxis(GoatBody.HeadRest + headPitch + lift * 0.6f, Vector3.right);
    }

    /// <summary>How far (from `from`, in steps of 15 degrees, at most 60) its neck must be raised for its head to be `margin` clear of the rock.</summary>
    private float LiftFor(float margin, float from)
    {
        float lift = from;
        while (lift < MaxHeadLift)
        {
            PoseHead(lift);
            if (HeadClear(margin))
            {
                break;
            }
            lift += 15f;
        }
        return lift;
    }

    /// <summary>Nothing solid within `margin` of its head: its muzzle, its brow, or on the way from its neck to its muzzle.</summary>
    private bool HeadClear(float margin)
    {
        Vector3 muzzle = head.TransformPoint(GoatBody.Muzzle);
        return !Physics.CheckSphere(muzzle, 0.07f + margin, HelperFunctions.terrainMapMask)
            && !Physics.CheckSphere(head.TransformPoint(GoatBody.Forehead), 0.1f + margin, HelperFunctions.terrainMapMask)
            && !Physics.Linecast(neck.position, muzzle, HelperFunctions.terrainMapMask);
    }

    /// <summary>
    /// One leg's stride, `p` its share of the way through it (0 the hoof just down in front, `duty` lifting off behind, 1
    /// down again): on the ground the leg sweeps back as the body goes over the hoof, giving a little at the knee and the
    /// fetlock under the weight; in the air the knee folds and the hoof flicks up early, the leg swinging forward and
    /// reaching out a little past where it lands. Swing (degrees, the hoof back positive), the knee's bend and the
    /// fetlock's.
    /// </summary>
    private static void Stride(float p, float duty, float reach, float fold, out float swing, out float knee, out float fetlock)
    {
        if (p < duty)
        {
            float s = p / duty;
            float weight = Mathf.Sin(s * Mathf.PI);
            swing = Mathf.Lerp(-reach, reach, s);
            knee = fold * 0.1f * weight;
            fetlock = -fold * 0.15f * weight;
            return;
        }
        float t = (p - duty) / (1f - duty);
        float eased = t * t * (3f - 2f * t);
        swing = Mathf.Lerp(reach, -reach * 1.05f, eased);
        knee = fold * Mathf.Sin(t * Mathf.PI);
        fetlock = fold * 0.5f * Mathf.Sin(Mathf.Clamp01(t * 1.3f) * Mathf.PI);
    }

    /// <summary>
    /// Its hooves, on every game: the walking loop while it walks (louder and quicker the faster it goes), the galloping
    /// loop while it charges, a landing as it comes down from a leap.
    /// </summary>
    private void Hooves(bool galloping, bool flying, bool inLeap, float sinceLeap)
    {
        bool moving = pace > 0.15f && !flying;
        Fade(walkLoop, moving && !galloping ? Goats.HoovesVolume * Mathf.Clamp01(pace / 0.9f) : 0f, 4f, Mathf.Clamp(0.8f + 0.2f * pace / Goats.WalkSpeed, 0.85f, 1.35f));
        Fade(gallopLoop, moving && galloping ? Goats.GallopVolume * Mathf.Clamp01(pace / 3f) : 0f, 6f, Mathf.Clamp(0.85f + 0.15f * pace / Goats.ChargeSpeed, 0.9f, 1.1f));
        if (inLeap && sinceLeap >= Crouch + leapSeconds && landedAt < leapStart)
        {
            landedAt = Time.time;
            if (thud != null && Goats.LandClip != null)
            {
                thud.PlayOneShot(Goats.LandClip, Goats.LandVolume);
                Goats.Landings++;
            }
        }
    }

    private static void Fade(AudioSource source, float to, float speed, float pitch)
    {
        if (source == null || source.clip == null)
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

    /// <summary>
    /// Which way the ground round its feet faces: the plane through the ground under its front and hind legs and its
    /// sides (felt for along the ground's own up, so on a cliff as well), else the ground under its feet. Coming to a
    /// slope it can climb, its front legs reach onto it first while its hind legs stay down: it rears up onto it (its body
    /// shown where its hind hooves stay on their ground), so its head goes up over the slope, never into it.
    /// </summary>
    private void MeasureTilt()
    {
        Vector3 up = photonView.IsMine ? groundUp : surfaceUp;
        Vector3 ahead = Vector3.ProjectOnPlane(Forward, up);
        ahead = ahead.sqrMagnitude > 1e-4f ? ahead.normalized : Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        Vector3 side = Vector3.Cross(up, ahead);
        Vector3 front;
        bool frontFound;
        if (SlopeAhead(feet, up, ahead, out RaycastHit face))
        {
            front = face.point;
            frontFound = true;
        }
        else
        {
            frontFound = Feel(feet + ahead * 0.34f, up, out front);
        }
        if (frontFound && Feel(feet - ahead * 0.36f, up, out Vector3 back)
            && Feel(feet - side * 0.2f, up, out Vector3 left) && Feel(feet + side * 0.2f, up, out Vector3 right))
        {
            Vector3 axis = (front - back).normalized;
            Vector3 plane = Vector3.Cross(right - left, axis);
            if (plane.sqrMagnitude > 1e-6f)
            {
                plane.Normalize();
                surfaceUp = Vector3.Dot(plane, up) < 0f ? -plane : plane;
                rootShift = back + axis * 0.36f - feet;
                return;
            }
        }
        rootShift = Vector3.zero;
        if (Physics.Raycast(feet + up * 0.5f, -up, out RaycastHit under, 1.2f, HelperFunctions.terrainMapMask))
        {
            surfaceUp = under.normal;
        }
    }

    private static bool Feel(Vector3 at, Vector3 up, out Vector3 ground)
    {
        bool hit = Physics.Raycast(at + up * 0.6f, -up, out RaycastHit floor, 1.4f, HelperFunctions.terrainMapMask);
        ground = hit ? floor.point : at;
        return hit;
    }

    /// <summary>Looking about, it watches a scout near it (within 12 m, in front of it or to the side).</summary>
    private Character WatchedScout()
    {
        Character best = null;
        float nearest = 12f;
        foreach (Character scout in Character.AllCharacters)
        {
            if (scout == null || scout.isBot || scout.data.dead)
            {
                continue;
            }
            float distance = Vector3.Distance(scout.Center, feet);
            if (distance < nearest && Mathf.Abs(LookYaw(scout.Center)) < 70f)
            {
                nearest = distance;
                best = scout;
            }
        }
        return best;
    }

    private float LookYaw(Vector3 point)
    {
        Vector3 flat = point - feet;
        flat.y = 0f;
        return flat.sqrMagnitude < 0.01f ? 0f : Mathf.Clamp(Mathf.DeltaAngle(yaw, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg), -70f, 70f);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(feet);
            stream.SendNext(yaw);
            stream.SendNext((byte)state);
            stream.SendNext(target != null && target.photonView != null ? target.photonView.ViewID : -1);
        }
        else
        {
            netFeet = (Vector3)stream.ReceiveNext();
            netYaw = (float)stream.ReceiveNext();
            State received = (State)(byte)stream.ReceiveNext();
            if (received != state)
            {
                stateSince = Time.time;
            }
            state = received;
            netTarget = (int)stream.ReceiveNext();
        }
    }
}

/// <summary>The Alps' goat spawner: the host spawns once, when the game spawns that part of the map.</summary>
internal sealed class GoatSpawner : MonoBehaviour, ISpawner
{
    private bool done;

    public List<PhotonView> TrySpawnItems()
    {
        if (done)
        {
            return new List<PhotonView>();
        }
        done = PhotonNetwork.IsMasterClient;
        return Goats.Spawn(transform.parent);
    }
}
