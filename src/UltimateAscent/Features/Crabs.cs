using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Small red crabs on the beach, about a scorpion's size, 25 of them spread along it from where the scouts wake to its
/// campfire, seven in ten on the climb itself (none come back once taken); on every ascent, their pinch half as strong
/// below 17 (see <see cref="NewMobs"/>).
/// <list type="bullet">
/// <item>They crawl around the beach (never off it), scuttling sideways. A scout who comes within 2 m is chased (one of
/// them at random, if more are that near) for at least 6 s, and after that for as long as they stay within 2 m. A
/// crab goes 1 m/s, crawling or chasing (a walking scout makes 2.5); crawling about, it rests a moment at each spot it
/// gets to.</item>
/// <item>Near the scout it turns to face them and pinches: its big claw rises and opens, then snaps shut, with the
/// scorpion's glint and sparks. A pinch that lands deals 10 injury and knocks whatever the scout holds out of their
/// hands.</item>
/// <item>Scouts can pick them up (they are an item, as scorpions are), into their hands only: put away, a crab is
/// dropped. A crab held pinches its holder as a scorpion stings, and then falls out of their hands. Cooked, it turns
/// into a cooked crab leg (its model and its icon), food like any other, carried in a pocket or a backpack: 10 hunger
/// and 10 extra stamina (5 and 5 from ascent 15, as all food). Burnt (this ascent's campfires burn it), it can still be
/// eaten, as burnt food can, and only poisons.</item>
/// </list>
/// A crab is the game's scorpion item underneath (its pick-up, cooking, network and physics, the Bing Bong plush for
/// players with bug phobia) with the crab's own body, built in code in PEAK's style, its own choice of whom it goes for
/// and its own sounds: its pinch and claw clicks from the game's own scorpion clicks, its scuttle from the user's claw
/// recording, made natural.
/// </summary>
internal static class Crabs
{
    internal const string PrefabName = "UA_Crab";
    internal const ushort ItemId = 9007;
    internal const string NameKey = "NAME_CRAB";
    internal const string LegNameKey = "NAME_CRAB LEG";
    private const string SpawnerName = "UltimateAscent Crab Spawner";

    internal const int MaxCrabs = 25;
    /// <summary>A scout this near (on the level) is noticed, and after the first 6 s kept after while this near.</summary>
    internal const float NoticeDistance = 2f;
    internal const float MinChaseSeconds = 6f;
    /// <summary>How fast a crab goes, crawling about or chasing.</summary>
    internal const float Speed = 1f;
    /// <summary>Crawling about, a crab this near the spot it crawls to has got there: it rests a moment, then crawls on to another.</summary>
    internal const float ArriveDistance = 0.4f;
    internal const float MinRest = 0.4f;
    internal const float MaxRest = 1.6f;
    internal const float PinchInjury = 0.1f;
    /// <summary>The pinch's push on the scout (the scorpion's sting pushes 500).</summary>
    internal const float PinchPush = 250f;
    /// <summary>The leg's hunger before cooking (the game's cooking doubles it, to 10); cooking also gives it 10 extra stamina.</summary>
    internal const float RawHunger = 0.05f;

    // Where they live: the beach's own ground (no steeper than 35 degrees, not in water), along the way from where the
    // scouts wake to the beach's campfire, seven in ten on the climb itself, 5 m or more apart.
    private const int SpotTries = 3000;
    private const float SpotSpread = 50f;
    private const float SpotSlope = 35f;
    private const float CrabsApart = 5f;

    // Sounds (the mod's resources).
    private static readonly string[] PinchResources = { "UltimateAscent.Crab_pinch_1.wav", "UltimateAscent.Crab_pinch_2.wav" };
    private static readonly string[] ClickResources = { "UltimateAscent.Crab_click_1.wav", "UltimateAscent.Crab_click_2.wav", "UltimateAscent.Crab_click_3.wav", "UltimateAscent.Crab_click_4.wav" };
    private const string ScuttleResource = "UltimateAscent.Crab_scuttle.wav";
    private const string IconResource = "UltimateAscent.Crab_icon.png";
    private const string LegIconResource = "UltimateAscent.CrabLeg_icon.png";
    internal const float PinchVolume = 1f;
    /// <summary>
    /// The pinch lands (its injury, push and hit sound) this long after the claw's snap is heard: at the same moment, the
    /// game's own sound for the injury and the hit drowned the snap.
    /// </summary>
    internal const float DamageAfterSnap = 0.15f;
    internal const float ClickVolume = 0.4f;
    internal const float ScuttleVolume = 0.2f;
    internal const float PinchReach = 22f;
    internal const float ClickReach = 16f;
    internal const float ScuttleReach = 12f;

    internal static bool On => NewMobs.Allowed(NewMobs.CrabsThreat);

    /// <summary>The crab item (the template the network spawns), null until built.</summary>
    internal static Item Template { get; private set; }

    internal static Texture2D LegIcon { get; private set; }

    // For the tests.
    internal static int Spawned;
    internal static int PinchesTaken;
    internal static int PinchSounds;
    internal static int ItemsKnockedOut;
    internal static int Flashes;
    internal static int SpotsFound;
    internal static int SpotsTried;
    /// <summary>For the tests: how many of the crabs were put on the climb itself (the beach's wall).</summary>
    internal static int OnClimb;

    private static AudioClip[] pinches;
    private static AudioClip[] clicks;
    private static AudioClip scuttle;
    private static bool soundsLoaded;

    /// <summary>Builds the crab from the game's scorpion item and registers it.</summary>
    internal static void Build()
    {
        Item scorpion = SingletonAsset<ItemDatabase>.Instance.Objects.FirstOrDefault(i => i != null && i.GetComponent<Scorpion>() != null);
        if (scorpion == null)
        {
            Plugin.Log.LogWarning("Crabs: the game's scorpion item was not found; no crabs.");
            return;
        }
        Item crab = ModItems.Copy(scorpion, PrefabName);
        GameObject root = crab.gameObject;

        // The scorpion's brain swapped for the crab's, every setting carried over.
        Scorpion old = root.GetComponent<Scorpion>();
        BeachCrab brain = root.AddComponent<BeachCrab>();
        foreach (FieldInfo field in AccessTools.GetDeclaredFields(typeof(Mob)).Where(f => !f.IsStatic))
        {
            field.SetValue(brain, field.GetValue(old));
        }
        PhotonView view = root.GetComponent<PhotonView>();
        if (view.ObservedComponents != null)
        {
            view.ObservedComponents = view.ObservedComponents.Select(c => c == old ? (Component)brain : c).ToList();
        }
        Object.DestroyImmediate(old);
        brain.aggroDistance = NoticeDistance;
        brain.movementSpeed = Speed;
        brain.turnRate = 500f;

        // Not a scorpion any more: its own run setting and its sounds gone, its body hidden (the Bing Bong for bug
        // phobia stays), the crab's own body in its place.
        foreach (DisableBasedOnRunSettings setting in root.GetComponentsInChildren<DisableBasedOnRunSettings>(true))
        {
            Object.DestroyImmediate(setting);
        }
        Transform model = brain.mesh;
        Transform sounds = model.Find("SFX");
        if (sounds != null)
        {
            Object.DestroyImmediate(sounds.gameObject);
        }
        Transform scorpionBody = model.Find("Body");
        if (scorpionBody != null)
        {
            scorpionBody.gameObject.SetActive(false);
        }
        CrabLook look = root.AddComponent<CrabLook>();
        look.Build(brain, model);
        BugPhobia phobia = root.GetComponent<BugPhobia>();
        if (phobia != null)
        {
            phobia.defaultGameObjects = new[] { look.crabBody.transform.parent.gameObject };
        }

        // A crab's size: a box round its shell and the near part of its legs; the scout's hands on its sides.
        Transform box = root.transform.Find("Collider");
        if (box != null)
        {
            box.localPosition = new Vector3(0f, 0.08f, 0f);
            box.localScale = new Vector3(0.16f, 0.075f, 0.125f);
        }
        foreach ((string hand, float side) in new[] { ("Hand_L", -1f), ("Hand_R", 1f) })
        {
            Transform holding = root.transform.Find(hand);
            if (holding != null)
            {
                holding.localPosition = new Vector3(side * 0.17f, 0.06f, -0.03f);
            }
        }

        crab.UIData.itemName = "Crab";
        Texture2D icon = LoadIcon(IconResource, "UA_Crab");
        if (icon != null)
        {
            crab.UIData.icon = icon;
        }
        LegIcon = LoadIcon(LegIconResource, "UA_CrabLeg") ?? crab.UIData.icon;
        foreach (Action_RestoreHunger hunger in root.GetComponents<Action_RestoreHunger>())
        {
            hunger.restorationAmount = RawHunger;
        }
        // Cooked, a crab leg is food like any other, carried in a pocket or a backpack (a live crab only in the hands,
        // as a scorpion is): the game's own cooking switch for that.
        if (!root.TryGetComponent(out ItemCooking cooking))
        {
            cooking = root.AddComponent<ItemCooking>();
        }
        cooking.additionalCookingBehaviors = cooking.additionalCookingBehaviors
            .Concat(new AdditionalCookingBehavior[] { new CookingBehavior_ModifyEtcStats { canPocket = true, canBackpack = true, cookedAmountToTrigger = 1 } }).ToArray();
        if (ModItems.Register(crab, ItemId))
        {
            Template = crab;
        }
    }

    private static Texture2D LoadIcon(string resource, string name)
    {
        return ModItems.HasResource(resource) ? ModItems.LoadTexture(resource, name, TextureWrapMode.Clamp) : null;
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
            pinches = PinchResources.Select((r, i) => ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(r), "Crab_pinch_" + (i + 1))).ToArray();
            clicks = ClickResources.Select((r, i) => ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(r), "Crab_click_" + (i + 1))).ToArray();
            scuttle = ClimbingChalkItem.ReadWav(ClimbingChalkModel.Resource(ScuttleResource), "Crab_scuttle");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogError("Crabs: their sounds could not be loaded: " + ex.Message);
        }
    }

    internal static AudioClip PinchClip()
    {
        LoadSounds();
        return pinches != null && pinches.Length > 0 ? pinches[Random.Range(0, pinches.Length)] : null;
    }

    internal static AudioClip ClickClip()
    {
        LoadSounds();
        return clicks != null && clicks.Length > 0 ? clicks[Random.Range(0, clicks.Length)] : null;
    }

    internal static AudioClip ScuttleClip
    {
        get
        {
            LoadSounds();
            return scuttle;
        }
    }

    internal static bool IsCrab(Item item) => item != null && item.itemID == ItemId;

    /// <summary>How often an item has been cooked (from its data, or its own if none is given).</summary>
    internal static int Cooked(Item item, ItemInstanceData data = null)
    {
        if (data != null)
        {
            return data.TryGetDataEntry(DataEntryKey.CookedAmount, out IntItemData amount) ? amount.Value : 0;
        }
        return item != null && item.cooking != null ? item.cooking.timesCookedLocal : 0;
    }

    /// <summary>Ground of the beach (or of no biome at all): not another biome's.</summary>
    internal static bool BeachGround(Collider ground) => !Map.IsOtherBiome(ground, Biome.BiomeType.Shore);

    /// <summary>The ground under a point is the beach's.</summary>
    internal static bool OnBeach(Vector3 at)
    {
        return !Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit under, 4f, HelperFunctions.terrainMapMask) || BeachGround(under.collider);
    }

    /// <summary>
    /// The pinch landing on the local scout, a moment after its snap: 10 injury and a little push (half below ascent 17),
    /// and what they hold out of their hands (the crab itself, if it is the one held).
    /// </summary>
    internal static IEnumerator PinchLands(Character scout, Vector3 forward, Vector3 from)
    {
        yield return new WaitForSeconds(DamageAfterSnap);
        if (scout == null || scout.data.dead)
        {
            yield break;
        }
        PinchesTaken++;
        scout.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, PinchInjury * NewMobs.Strength);
        scout.AddForceAtPosition(PinchPush * NewMobs.Strength * forward, from, 2f);
        KnockOutOfHands(scout);
    }

    /// <summary>A pinch landed on the local scout: what they hold falls out of their hands (the crab itself, if it is the one held).</summary>
    internal static void KnockOutOfHands(Character scout)
    {
        CharacterItems items = scout != null ? scout.refs.items : null;
        Item held = scout != null ? scout.data.currentItem : null;
        if (items == null || held == null || !items.currentSelectedSlot.IsSome)
        {
            return;
        }
        byte slotId = items.currentSelectedSlot.Value;
        ItemSlot slot = scout.player.GetItemSlot(slotId);
        if (slot == null || (slot.prefab != null && !slot.prefab.UIData.canDrop))
        {
            return;
        }
        Vector3 away = held.transform.position - scout.Center;
        away.y = 0f;
        Vector3 jolt = (away.sqrMagnitude > 1e-4f ? away.normalized : scout.transform.forward) * 1.5f + Vector3.up * 1.2f;
        items.photonView.RPC(nameof(CharacterItems.DropItemRpc), RpcTarget.All, 0f, slotId, held.transform.position, jolt, held.transform.rotation, slot.data, false);
        items.EquipSlot(Optionable<byte>.None);
        ItemsKnockedOut++;
    }

    /// <summary>The first part of each map (the beach) gets a spawner.</summary>
    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.InitializeMap))]
    private static class EveryMap
    {
        private static void Postfix()
        {
            MapHandler map = MapHandler.Exists ? Singleton<MapHandler>.Instance : null;
            if (map == null || map.segments == null || map.segments.Length == 0)
            {
                return;
            }
            MapHandler.MapSegment beach = map.segments[0];
            GameObject parent = beach.segmentParent;
            if (parent == null || beach.biome != Biome.BiomeType.Shore || parent.transform.Find(SpawnerName) != null)
            {
                return;
            }
            GameObject holder = new GameObject(SpawnerName);
            holder.transform.SetParent(parent.transform, false);
            holder.AddComponent<CrabSpawner>();
        }
    }

    /// <summary>The host's spawn of the beach's crabs (when they live in this climb), spread along the beach and its climb.</summary>
    internal static List<PhotonView> Spawn(Transform segment)
    {
        List<PhotonView> spawned = new List<PhotonView>();
        if (segment == null || !PhotonNetwork.IsMasterClient || !On || Template == null)
        {
            return spawned;
        }
        Physics.SyncTransforms();
        foreach (Vector3 spot in Places())
        {
            GameObject made = PhotonNetwork.InstantiateItemRoom(PrefabName, spot + Vector3.up * 0.1f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            PhotonView madeView = made != null ? made.GetComponent<PhotonView>() : null;
            if (madeView != null)
            {
                spawned.Add(madeView);
            }
        }
        Spawned += spawned.Count;
        Plugin.Log.LogInfo($"New mobs: {spawned.Count} crabs on the beach ({OnClimb} on the climb itself, {spawned.Count - OnClimb} on the way to it; {SpotsFound} spots found in {SpotsTried} places looked at).");
        return spawned;
    }

    /// <summary>Where the scouts wake on the beach (between the spawn points), and the beach's campfire at the top of it.</summary>
    internal static bool Way(out Vector3 start, out Vector3 end)
    {
        start = end = Vector3.zero;
        MapHandler map = MapHandler.Exists ? Singleton<MapHandler>.Instance : null;
        GameObject campfire = map != null && map.segments != null && map.segments.Length > 0 ? map.segments[0].segmentCampfire : null;
        List<SpawnPoint> points = SpawnPoint.allSpawnPoints.Where(p => p != null).ToList();
        if (campfire == null || points.Count == 0)
        {
            return false;
        }
        start = points.Aggregate(Vector3.zero, (sum, p) => sum + p.transform.position) / points.Count;
        end = campfire.transform.position;
        return true;
    }

    /// <summary>How far along the beach's way a point is (0 where the scouts wake, 1 at the campfire).</summary>
    internal static float Share(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 along = end - start;
        along.y = 0f;
        Vector3 from = point - start;
        from.y = 0f;
        return along.sqrMagnitude > 1f ? Vector3.Dot(from, along) / along.sqrMagnitude : 0f;
    }

    /// <summary>
    /// The crabs' spots: the beach's own ground (no steeper than 35 degrees, about level round it, not in water) found
    /// along its way from where the scouts wake to its campfire; seven in ten of the crabs on the climb itself (the
    /// beach's wall up to the campfire), the rest on the sand before it, each spread along its stretch, 5 m or more apart
    /// (see <see cref="NewMobs.Place"/>).
    /// </summary>
    private static List<Vector3> Places()
    {
        List<Vector3> places = new List<Vector3>();
        if (!Way(out Vector3 a, out Vector3 b))
        {
            Plugin.Log.LogWarning("Crabs: the beach's spawn points or campfire were not found; no crabs.");
            return places;
        }
        Vector3 along = b - a;
        along.y = 0f;
        float length = along.magnitude;
        if (length < 1f)
        {
            return places;
        }
        along /= length;
        Vector3 across = Vector3.Cross(Vector3.up, along);
        float top = Mathf.Max(a.y, b.y) + 200f;
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
                || !Map.TryGetBiome(hit.collider, out Biome.BiomeType biome) || biome != Biome.BiomeType.Shore
                || !LevelAround(hit.point) || GroundItems.InWater(hit.point, water))
            {
                continue;
            }
            found.Add((hit.point, Share(hit.point, a, b)));
        }
        SpotsFound = found.Count;
        return NewMobs.Place(found, MaxCrabs, CrabsApart, out OnClimb);
    }

    /// <summary>Ground about level a little way round a spot (not a ledge's edge).</summary>
    private static bool LevelAround(Vector3 at)
    {
        for (int angle = 0; angle < 360; angle += 90)
        {
            Vector3 round = at + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 0.6f;
            if (!Physics.Raycast(round + Vector3.up * 1f, Vector3.down, out RaycastHit floor, 2f, HelperFunctions.terrainMapMask) || Mathf.Abs(floor.point.y - at.y) > 0.4f)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>A crab chooses whom it goes for itself (see <see cref="BeachCrab.ChooseTarget"/>), on the game that runs it.</summary>
    [HarmonyPatch(typeof(Mob), nameof(Mob.Targeting))]
    private static class Targeting
    {
        private static bool Prefix(Mob __instance)
        {
            if (__instance is BeachCrab crab)
            {
                crab.ChooseTarget();
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// A crab crawls around the beach only: a spot to crawl to off it is not taken, nor turned to for a moment (it keeps
    /// the spot it had and picks again).
    /// </summary>
    [HarmonyPatch(typeof(Mob), nameof(Mob.GetNewPatrolPos))]
    private static class BeachPatrol
    {
        private static void Prefix(Mob __instance, out Vector3 __state)
        {
            __state = __instance.patrolPos;
        }

        private static void Postfix(Mob __instance, Vector3 __state)
        {
            if (__instance is BeachCrab && __instance.untilNextPatrolPos > 0f && !OnBeach(__instance.patrolPos))
            {
                __instance.patrolPos = __state;
                __instance.untilNextPatrolPos = 0f;
            }
        }
    }

    /// <summary>The scorpion's glint and sparks, played on a crab (for the tests).</summary>
    [HarmonyPatch(typeof(AnimationVFX), nameof(AnimationVFX.PlayVFX))]
    private static class FlashSeen
    {
        private static void Postfix(AnimationVFX __instance)
        {
            if (__instance.GetComponentInParent<BeachCrab>() != null)
            {
                Flashes++;
            }
        }
    }

    /// <summary>
    /// Burnt, a crab leg is still food, as burnt food is: what ruins anything it cooks (this ascent's campfires, lava and
    /// explosions) burns a crab leg just as far (the game's "incinerated") without ruining it, so it can be eaten and,
    /// by the game's own cooking, gives no hunger or extra stamina, only poison.
    /// </summary>
    [HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.ChangeStatsCooked))]
    private static class BurntStillFood
    {
        private static void Prefix(ItemCooking __instance)
        {
            if (__instance.wreckWhenCooked && IsCrab(__instance.item))
            {
                __instance.wreckWhenCooked = false;
            }
        }
    }

    /// <summary>A cooked crab shows as a crab leg in the inventory.</summary>
    [HarmonyPatch(typeof(InventoryItemUI), nameof(InventoryItemUI.SetItem))]
    private static class LegIconInSlot
    {
        private static void Postfix(InventoryItemUI __instance, ItemSlot slot)
        {
            if (slot != null && IsCrab(slot.prefab) && Cooked(slot.prefab, slot.data) > 0 && LegIcon != null && __instance.icon != null)
            {
                __instance.icon.texture = LegIcon;
            }
        }
    }

    /// <summary>A cooked crab is named a crab leg ("Cooked Crab Leg"; the game's words round it, as for all food).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.GetItemName))]
    private static class LegName
    {
        private static void Postfix(Item __instance, ItemInstanceData data, ref string __result)
        {
            if (IsCrab(__instance) && Cooked(__instance, data) > 0)
            {
                string crab = __instance.GetName();
                if (!string.IsNullOrEmpty(crab))
                {
                    __result = __result.Replace(crab, LocalizedText.GetText(LegNameKey));
                }
            }
        }
    }
}

/// <summary>The beach's crab spawner: the game runs it for the beach when the climb starts.</summary>
internal sealed class CrabSpawner : MonoBehaviour, ISpawner
{
    private bool done;

    public List<PhotonView> TrySpawnItems()
    {
        if (done)
        {
            return new List<PhotonView>();
        }
        done = PhotonNetwork.IsMasterClient;
        return Crabs.Spawn(transform.parent);
    }
}

/// <summary>
/// A crab's brain: the game's scorpion's (crawling about, chasing, its attack, being held), with its own choice of whom
/// it goes for, its own speed, and its pinch.
/// </summary>
internal sealed class BeachCrab : Mob
{
    /// <summary>When it began going for the scout it is after.</summary>
    private float chaseSince;

    /// <summary>For the tests: who it is after (null: nobody) and how long it has been.</summary>
    internal Character Target => targetChar;
    internal float ChasingFor => targetChar != null ? Time.time - chaseSince : 0f;
    internal bool Striking => attacking;

    /// <summary>For the tests: it goes for nobody (not even the scout holding it).</summary>
    internal bool Unaware;

    /// <summary>For the tests' pictures: it stays where it is.</summary>
    internal bool Still;

    /// <summary>Crawling about: the spot it last got to, and until when it rests there.</summary>
    private Vector3 reached;
    private bool hasReached;
    private float restUntil;

    /// <summary>Resting at a spot it crawled to (for the tests too).</summary>
    internal bool Resting { get; private set; }

    /// <summary>When the game that runs it next looks whether it has got under the map.</summary>
    private float nextMapCheck;

    /// <summary>Under the map (fallen through a gap between the map's pieces), on the game that runs it: back at its spot.</summary>
    private void PutBackIfUnderMap()
    {
        if (!photonView.IsMine || (_mobItem != null && _mobItem.itemState != ItemState.Ground)
            || !NewMobs.UnderTheMap(transform.position) || NewMobs.UnderTheMap(startPos))
        {
            return;
        }
        Plugin.Log.LogWarning($"New mobs: a crab was under the map at {transform.position:F1}; put back at its spot.");
        NewMobs.PutBack++;
        rig.linearVelocity = Vector3.zero;
        rig.position = startPos + Vector3.up * 0.1f;
        transform.position = rig.position;
    }

    public override void Update()
    {
        if (Time.time >= nextMapCheck)
        {
            nextMapCheck = Time.time + 2f;
            PutBackIfUnderMap();
        }
        // Crawling about, a crab that has got to its spot rests there a moment, then crawls on to another (the scorpion's
        // walk keeps its full speed there and circles the spot until it is time for a new one).
        bool wandering = targetChar == null && !forceNoMovement && !sleeping && mobState == MobState.Walking;
        bool atSpot = hasReached && patrolPos == reached;
        if (wandering && !atSpot && Goats.Flat(transform.position, patrolPos) < Crabs.ArriveDistance)
        {
            reached = patrolPos;
            hasReached = atSpot = true;
            restUntil = Time.time + Random.Range(Crabs.MinRest, Crabs.MaxRest);
        }
        Resting = wandering && atSpot;
        if (Resting)
        {
            // Its next spot only once rested: then at once (or as soon as one is found).
            untilNextPatrolPos = Time.time < restUntil ? Mathf.Max(untilNextPatrolPos, restUntil - Time.time + 0.1f) : 0f;
        }
        movementSpeed = Still || Resting ? 0f : Crabs.Speed;
        base.Update();
    }

    /// <summary>
    /// Whom it goes for (on the game that runs it): held, its holder; else the scout it is after, for at least 6 s and
    /// then while they stay within 2 m, so long as they are on the beach; else one of the scouts within 2 m, at random,
    /// awake and in sight.
    /// </summary>
    internal void ChooseTarget()
    {
        if (attacking && targetChar != null)
        {
            return;
        }
        if (Unaware)
        {
            targetChar = null;
            return;
        }
        Character forced = forcedCharacterTarget;
        if (forced != null)
        {
            targetChar = Fit(forced) ? forced : null;
            return;
        }
        Character after = targetChar;
        if (after != null && Fit(after) && Crabs.OnBeach(after.Center))
        {
            if (Time.time - chaseSince < Crabs.MinChaseSeconds || Near(after))
            {
                return;
            }
        }
        List<Character> near = Character.AllCharacters.Where(c => Fit(c) && Near(c) && Crabs.OnBeach(c.Center) && Sees(c)).ToList();
        Character picked = near.Count > 0 ? near[Random.Range(0, near.Count)] : null;
        if (picked != targetChar)
        {
            targetChar = picked;
            chaseSince = Time.time;
        }
    }

    private static bool Fit(Character scout)
    {
        return scout != null && !scout.data.dead && scout.data.fullyConscious;
    }

    /// <summary>Within 2 m of it, on the level (its feet, not the middle of its body, against the crab on the ground).</summary>
    private bool Near(Character scout)
    {
        Vector3 feet = scout.Center + Vector3.down * 0.9f;
        return Goats.Flat(feet, transform.position) <= Crabs.NoticeDistance && Mathf.Abs(feet.y - transform.position.y) <= 1.5f;
    }

    private bool Sees(Character scout)
    {
        return HelperFunctions.LineCheck(transform.position + Vector3.up * 0.2f, scout.Center, HelperFunctions.LayerType.TerrainMap).transform == null;
    }

    /// <summary>
    /// The pinch (on every game): the claw's snap heard at once, then the pinch landing (see <see cref="Crabs.PinchLands"/>),
    /// with the scout's hit sound, on the pinched scout's own game.
    /// </summary>
    public override void InflictAttack(Character character)
    {
        CrabLook look = GetComponent<CrabLook>();
        if (look != null)
        {
            look.Pinched(character);
        }
        if (character == null || !character.IsLocal || character.data.dead)
        {
            return;
        }
        character.StartCoroutine(Crabs.PinchLands(character, mesh.forward, transform.position));
    }
}

/// <summary>
/// A crab's body (built in code, see <see cref="CrabBody"/>) and its movements and sounds, on every game: scuttling
/// sideways on eight legs, turning to face the scout it goes for with its claws up, the big claw rising, opening and
/// snapping shut as it pinches; held, flailing and snapping; cooked, a crab leg.
/// </summary>
internal sealed class CrabLook : MonoBehaviour
{
    public BeachCrab brain;
    public MobItem item;
    /// <summary>The crab's root (turned to scuttle sideways or to face its scout), its body and limbs, and the cooked leg.</summary>
    public Transform crabRoot;
    public GameObject crabBody;
    public GameObject leg;
    public Transform[] hips = new Transform[8];
    public Transform[] knees = new Transform[8];
    public Transform[] arms = new Transform[2];
    public Transform[] hands = new Transform[2];
    public Transform[] fingers = new Transform[2];
    /// <summary>The scorpion's glint and sparks, kept where they are (so players with bug phobia see them too) and moved to the big claw's tip.</summary>
    public Transform[] flashes = new Transform[0];
    public Transform clawTip;

    private Quaternion[] hipRest;
    private Quaternion[] kneeRest;
    private Quaternion[] armRest;
    private Quaternion[] handRest;
    private float yaw;
    private float sideways = -90f;
    private float phase;
    private float speed;
    private Vector3 lastPosition;
    private bool wasAttacking;
    private float snapAt = -10f;
    private float nextClick;
    private float openIdle;
    private float nextIdleClaw;
    private int cookedShown;
    private AudioSource scuttleSource;
    private AudioSource voice;

    /// <summary>For the tests: how loud its scuttle is now (of its full loudness), and its clicks heard.</summary>
    internal float ScuttleLoudness => scuttleSource != null && scuttleSource.isPlaying ? scuttleSource.volume / Crabs.ScuttleVolume : 0f;
    internal int ClicksHeard { get; private set; }
    internal bool Facing => Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) < 20f;
    internal bool Sideways => Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) > 70f;
    internal bool ShowsLeg => leg != null && leg.activeSelf && crabBody != null && !crabBody.activeSelf;
    internal float ClawOpen { get; private set; }

    /// <summary>Builds the crab's body under the scorpion's model (whose turning and hiding it follows), on the template.</summary>
    internal void Build(BeachCrab mob, Transform model)
    {
        brain = mob;
        item = GetComponent<MobItem>();
        // Under the scorpion's model (scaled 0.07, a little up and forward): the crab's root at the item's own origin, at its scale.
        Transform holder = new GameObject("UA_CrabLook").transform;
        holder.SetParent(model, false);
        float scale = model.localScale.x > 1e-4f ? model.localScale.x : 1f;
        holder.localPosition = -model.localPosition / scale;
        holder.localRotation = Quaternion.identity;
        holder.localScale = Vector3.one / scale;
        crabRoot = new GameObject("Crab").transform;
        crabRoot.SetParent(holder, false);
        Material[] skins = CrabBody.Materials;
        crabBody = Part("Body", crabRoot, Vector3.zero, Quaternion.identity, CrabBody.BodyMesh, skins).gameObject;
        for (int i = 0; i < 8; i++)
        {
            float side = i < 4 ? 1f : -1f;
            int j = i % 4;
            hips[i] = Part("Leg " + i, crabBody.transform, CrabBody.Hip(j, side), CrabBody.HipTurn(j, side), CrabBody.LegUpper(side), skins);
            knees[i] = Part("Shin " + i, hips[i], CrabBody.Knee(side), Quaternion.identity, CrabBody.LegLower(side), skins);
        }
        for (int c = 0; c < 2; c++)
        {
            float side = c == 0 ? 1f : -1f;
            float size = c == 0 ? CrabBody.BigClaw : CrabBody.SmallClaw;
            arms[c] = Part("Arm " + c, crabBody.transform, CrabBody.Shoulder(side), Quaternion.identity, CrabBody.ClawArm(side, size), skins);
            hands[c] = Part("Claw " + c, arms[c], CrabBody.Wrist(side, size), Quaternion.identity, CrabBody.ClawHand(side, size), skins);
            fingers[c] = Part("Finger " + c, hands[c], CrabBody.FingerHinge(side, size), Quaternion.identity, CrabBody.ClawFinger(side, size), skins);
        }
        clawTip = new GameObject("Tip").transform;
        clawTip.SetParent(hands[0], false);
        clawTip.localPosition = CrabBody.ClawTip(1f, CrabBody.BigClaw);
        leg = Part("Leg", crabRoot, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), CrabBody.LegMesh, skins).gameObject;
        leg.transform.localScale = Vector3.one * CrabBody.LegScale;
        leg.SetActive(false);
        AnimationVFX vfx = model.GetComponent<AnimationVFX>();
        flashes = vfx != null && vfx.vfx != null ? vfx.vfx.Where(p => p != null).Select(p => p.transform).ToArray() : new Transform[0];
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

    private void Awake()
    {
        hipRest = hips.Select(t => t != null ? t.localRotation : Quaternion.identity).ToArray();
        kneeRest = knees.Select(t => t != null ? t.localRotation : Quaternion.identity).ToArray();
        armRest = arms.Select(t => t != null ? t.localRotation : Quaternion.identity).ToArray();
        handRest = hands.Select(t => t != null ? t.localRotation : Quaternion.identity).ToArray();
        lastPosition = transform.position;
        yaw = 0f;
        nextIdleClaw = Time.time + Random.Range(1f, 5f);
        nextClick = Time.time + Random.Range(2f, 8f);
        scuttleSource = Source(Crabs.ScuttleClip, Crabs.ScuttleReach, true);
        voice = Source(null, Crabs.ClickReach, false);
    }

    /// <summary>A sound on the crab, through the game's sound-effects mixer, falling away about as the game's sounds do; a loop starts silent.</summary>
    private AudioSource Source(AudioClip clip, float reach, bool loop)
    {
        AudioSource source = Configure(gameObject.AddComponent<AudioSource>(), reach);
        source.clip = clip;
        source.loop = loop;
        source.volume = loop ? 0f : 1f;
        if (loop && clip != null)
        {
            source.time = Random.Range(0f, clip.length * 0.95f);
        }
        return source;
    }

    /// <summary>A crab's sound: in the world, through the game's sound-effects mixer, falling away about as the game's sounds do.</summary>
    private static AudioSource Configure(AudioSource source, float reach)
    {
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.minDistance = 0.6f;
        source.maxDistance = reach;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.1f, 0.72f), new Keyframe(0.3f, 0.36f), new Keyframe(0.6f, 0.12f), new Keyframe(1f, 0f)));
        source.outputAudioMixerGroup = SFX_Player.instance != null ? SFX_Player.instance.defaultMixerGroup : null;
        return source;
    }

    /// <summary>The pinch (on every game): the snap heard at once, the scout's hit sound as the pinch lands, a moment later.</summary>
    internal void Pinched(Character scout)
    {
        Snap();
        SFX_Instance[] hits = ZombieDagger.HitSfx();
        if (scout != null && hits.Length > 0)
        {
            scout.StartCoroutine(HitAfterSnap(scout, hits[Random.Range(0, hits.Length)]));
        }
    }

    private static IEnumerator HitAfterSnap(Character scout, SFX_Instance hit)
    {
        yield return new WaitForSeconds(Crabs.DamageAfterSnap);
        if (scout != null)
        {
            hit.Play(scout.IsLocal && MainCamera.instance != null ? MainCamera.instance.transform.position : scout.Center);
        }
    }

    /// <summary>For the tests: the last snap's sound.</summary>
    internal static AudioSource LastSnap { get; private set; }

    /// <summary>
    /// The snap, from a sound of its own where the claw is: it plays on when the crab is gone (a crab held falls out of the
    /// hands as it pinches, a new one appearing on the ground).
    /// </summary>
    private void Snap()
    {
        if (Time.time - snapAt < 0.3f)
        {
            return;
        }
        snapAt = Time.time;
        AudioClip clip = Crabs.PinchClip();
        if (clip == null)
        {
            return;
        }
        GameObject sound = new GameObject("UltimateAscent Crab Snap");
        sound.transform.position = clawTip != null ? clawTip.position : transform.position;
        AudioSource source = Configure(sound.AddComponent<AudioSource>(), Crabs.PinchReach);
        source.pitch = Random.Range(0.95f, 1.08f);
        source.PlayOneShot(clip, Crabs.PinchVolume);
        Object.Destroy(sound, clip.length / source.pitch + 0.2f);
        LastSnap = source;
        Crabs.PinchSounds++;
    }

    private void Click(float volume)
    {
        AudioClip clip = Crabs.ClickClip();
        if (voice != null && clip != null)
        {
            voice.pitch = Random.Range(0.9f, 1.15f);
            voice.PlayOneShot(clip, volume);
            ClicksHeard++;
        }
    }

    private void LateUpdate()
    {
        if (brain == null || item == null || crabRoot == null)
        {
            return;
        }
        float dt = Time.deltaTime;
        float now = Time.time;
        int cooked = Crabs.Cooked(item);
        if (cooked > 0)
        {
            ShowCooked(cooked);
            return;
        }
        bool held = item.itemState == ItemState.Held;
        bool asleep = brain.sleeping;
        float moved = dt > 0f ? Vector3.Distance(transform.position, lastPosition) / dt : 0f;
        lastPosition = transform.position;
        if (moved > 15f || held)
        {
            moved = 0f;
        }
        speed = Mathf.Lerp(speed, moved, Mathf.Clamp01(dt * 10f));
        Character target = brain.Target;
        bool attacking = brain.Striking;
        float windUp = attacking ? Mathf.Clamp01(brain.inRangeForAttackTime / Mathf.Max(brain.attackTime, 0.01f)) : 0f;
        if (wasAttacking && !attacking)
        {
            // The attack is over, landed or not: the claw snaps shut (heard at once if the pinch missed).
            Snap();
        }
        wasAttacking = attacking;
        float sinceSnap = now - snapAt;

        // Facing: sideways while it scuttles (one side or the other leading), turned to its scout near them or held.
        bool near = target != null && Vector3.Distance(transform.position, target.Center) < 2.6f;
        float wantYaw = held || attacking || near ? 0f : speed > 0.08f ? sideways : yaw;
        if (speed <= 0.08f && Mathf.Abs(Mathf.DeltaAngle(yaw, sideways)) < 1f && Random.value < dt * 0.3f)
        {
            sideways = -sideways;
        }
        yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, 600f * dt);
        crabRoot.localRotation = Quaternion.Euler(0f, yaw, 0f);

        // Legs: an alternating gait (a stride every 0.12 m), flailing while held.
        phase += speed / 0.12f * Mathf.PI * 2f * dt;
        float going = Mathf.Clamp01(speed / 0.6f);
        for (int i = 0; i < 8; i++)
        {
            float side = i < 4 ? 1f : -1f;
            int j = i % 4;
            float legPhase = phase + ((j + (i < 4 ? 0 : 1)) % 2) * Mathf.PI;
            float lift, sweep, bend;
            if (held)
            {
                lift = 18f + 22f * Mathf.Sin(now * 19f + i * 1.7f);
                sweep = 14f * Mathf.Sin(now * 13f + i * 2.3f);
                bend = 20f + 18f * Mathf.Sin(now * 16f + i * 0.9f);
            }
            else
            {
                lift = going * 26f * Mathf.Max(0f, Mathf.Sin(legPhase)) + (asleep ? 0f : 2f * Mathf.Sin(now * 1.3f + i));
                sweep = going * 12f * Mathf.Cos(legPhase);
                bend = going * 14f * Mathf.Cos(legPhase);
            }
            hips[i].localRotation = hipRest[i] * Quaternion.Euler(0f, sweep, side * lift);
            knees[i].localRotation = kneeRest[i] * Quaternion.Euler(0f, 0f, -side * bend);
        }
        crabBody.transform.localPosition = new Vector3(0f, going * 0.005f * Mathf.Abs(Mathf.Sin(phase)), 0f);

        // Claws: now and then opening a little; raised and open going for a scout; the big one rising and opening as it
        // winds up to pinch (over the scorpion's 1 s), snapping shut as it pinches; snapping away while held.
        if (now >= nextIdleClaw)
        {
            nextIdleClaw = now + Random.Range(2f, 6f);
            openIdle = Random.value < 0.6f ? Random.Range(10f, 25f) : 0f;
        }
        for (int c = 0; c < 2; c++)
        {
            float raise, open;
            if (held)
            {
                raise = 25f + 10f * Mathf.Sin(now * 7f + c);
                open = Mathf.Max(0f, Mathf.Sin(now * 9f + c * 2f)) * 40f;
            }
            else if (target != null)
            {
                raise = 22f;
                open = 28f;
                if (c == 0 && attacking)
                {
                    raise = Mathf.Lerp(22f, 45f, Mathf.Clamp01(windUp / 0.8f));
                    open = Mathf.Lerp(28f, 45f, Mathf.Clamp01(windUp / 0.8f));
                }
            }
            else
            {
                raise = 6f;
                open = openIdle * (0.5f + 0.5f * Mathf.Sin(now * 1.7f + c));
            }
            float jab = 0f;
            if (sinceSnap < 0.35f && c == 0)
            {
                // The snap: shut at once, the claw jabbing forward and back.
                open = 0f;
                jab = Mathf.Sin(Mathf.Clamp01(sinceSnap / 0.35f) * Mathf.PI) * 25f;
                raise = Mathf.Lerp(raise, 0f, Mathf.Clamp01(sinceSnap / 0.12f));
            }
            arms[c].localRotation = armRest[c] * Quaternion.Euler(-raise - jab * 0.3f, 0f, 0f);
            hands[c].localRotation = handRest[c] * Quaternion.Euler(-jab * 0.5f, 0f, 0f);
            fingers[c].localRotation = Quaternion.Euler(-open, 0f, 0f);
            if (c == 0)
            {
                ClawOpen = open;
            }
        }
        foreach (Transform flash in flashes)
        {
            if (flash != null && clawTip != null)
            {
                flash.position = clawTip.position;
            }
        }

        // Sounds: the scuttle while it moves; a claw click now and then while somebody is about, none while it goes for a
        // scout or is held (its pinch is then the one snap heard).
        if (scuttleSource != null && scuttleSource.clip != null)
        {
            float loud = asleep || held ? 0f : Crabs.ScuttleVolume * Mathf.Clamp01(speed / 1.2f);
            scuttleSource.volume = Mathf.MoveTowards(scuttleSource.volume, loud, dt * 4f);
            scuttleSource.pitch = 0.9f + 0.3f * Mathf.Clamp01(speed / 3f);
            if (scuttleSource.volume > 0.001f && !scuttleSource.isPlaying)
            {
                scuttleSource.Play();
            }
            else if (scuttleSource.volume <= 0.001f && scuttleSource.isPlaying)
            {
                scuttleSource.Pause();
            }
        }
        if (asleep)
        {
            return;
        }
        if (held || target != null)
        {
            nextClick = now + Random.Range(2f, 5f);
        }
        else if (now >= nextClick)
        {
            if (SomeoneWithin(12f))
            {
                Click(Crabs.ClickVolume);
            }
            nextClick = now + Random.Range(5f, 12f);
        }
    }

    private bool SomeoneWithin(float range)
    {
        foreach (Character scout in Character.AllCharacters)
        {
            if (scout != null && Vector3.Distance(scout.Center, transform.position) <= range)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Cooked: a crab leg (cooked, or burnt by this ascent's campfires), its icon a leg's too; quiet.</summary>
    private void ShowCooked(int cooked)
    {
        if (cookedShown == cooked)
        {
            return;
        }
        cookedShown = cooked;
        crabBody.SetActive(false);
        leg.SetActive(true);
        crabRoot.localRotation = Quaternion.identity;
        MeshRenderer legRenderer = leg.GetComponent<MeshRenderer>();
        Material[] tinted = legRenderer.materials;
        for (int i = 0; i < tinted.Length; i++)
        {
            tinted[i].SetColor("_Tint", CrabBody.CookedTint(i, cooked));
        }
        legRenderer.materials = tinted;
        if (Crabs.LegIcon != null)
        {
            item.UIData.icon = Crabs.LegIcon;
        }
        if (scuttleSource != null)
        {
            scuttleSource.Stop();
        }
    }
}

/// <summary>
/// The crab's body, built in code in the rounded shapes of PEAK's creatures, with the game's item shading: a wide,
/// smooth red shell with a pale belly, eyes on stalks, eight jointed legs and two pincer claws (the right one bigger),
/// each finger dark-tipped; and the cooked crab leg it turns into, its broken end showing white meat. Model space: +x
/// its right, +y up, +z the way it faces; the ground at y = 0.
/// </summary>
internal static class CrabBody
{
    internal const int Shell = 0, Belly = 1, Joint = 2, Tip = 3, Eye = 4, Meat = 5, Kinds = 6;
    internal const float BigClaw = 1.4f;
    internal const float SmallClaw = 1.15f;
    /// <summary>The cooked leg's size, against the crab's own legs (food, it reads at a glance).</summary>
    internal const float LegScale = 1.5f;
    /// <summary>The cooked leg's shell against the live crab's, on top of the game's cooking tint (its meat as the game cooks it).</summary>
    internal const float CookedShade = 0.5f;
    /// <summary>The materials' colours, a deep red against the sand (the cooked leg's tint is cooked from these).</summary>
    internal static readonly Color[] Tints =
    {
        new Color(0.74f, 0.1f, 0.06f), new Color(0.95f, 0.75f, 0.55f), new Color(0.9f, 0.4f, 0.24f),
        new Color(0.18f, 0.06f, 0.05f), new Color(0.03f, 0.03f, 0.03f), new Color(0.98f, 0.95f, 0.9f),
    };

    /// <summary>A cooked leg's colour of each of its materials: the crab's own, cooked as the game cooks food, its shell darker still.</summary>
    internal static Color CookedTint(int kind, int cooked)
    {
        Color tint = Tints[kind] * ItemCooking.GetCookColor(cooked) * (kind == Meat ? 1f : CookedShade);
        tint.a = 1f;
        return tint;
    }

    private static Material[] materials;
    private static Mesh body;
    private static Mesh legMesh;
    private static readonly Dictionary<string, Mesh> parts = new Dictionary<string, Mesh>();

    internal static Material[] Materials => materials ??= MakeMaterials();
    internal static Mesh BodyMesh => body ??= MakeBody();
    internal static Mesh LegMesh => legMesh ??= MakeLeg();

    private static Mesh Cached(string key, System.Func<Mesh> make)
    {
        if (!parts.TryGetValue(key, out Mesh mesh) || mesh == null)
        {
            mesh = make();
            parts[key] = mesh;
        }
        return mesh;
    }

    // The legs: four a side along the shell's edge, the front ones turned forward, the back ones back.
    private static readonly float[] HipZ = { 0.046f, 0.014f, -0.02f, -0.054f };
    private static readonly float[] HipFan = { 28f, 9f, -10f, -30f };

    internal static Vector3 Hip(int j, float side) => new Vector3(side * 0.112f, 0.105f, HipZ[j]);
    internal static Quaternion HipTurn(int j, float side) => Quaternion.Euler(0f, -side * HipFan[j], 0f);
    internal static Vector3 Knee(float side) => new Vector3(side * 0.078f, 0.036f, 0f);
    internal static Vector3 Shoulder(float side) => new Vector3(side * 0.082f, 0.11f, 0.082f);
    internal static Vector3 Wrist(float side, float size) => new Vector3(side * 0.034f, 0.012f, 0.062f) * size;
    internal static Vector3 FingerHinge(float side, float size) => new Vector3(side * 0.002f, 0.013f, 0.05f) * size;
    internal static Vector3 ClawTip(float side, float size) => new Vector3(-side * 0.005f, -0.004f, 0.104f) * size;

    internal static Mesh LegUpper(float side) => Cached("upper" + side, () =>
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        mesh.Tube(Shell, new[]
        {
            new GoatRing(0f, 0f, 0f, 0.018f, 0.015f, 2.2f),
            new GoatRing(side * 0.04f, 0.022f, 0f, 0.017f, 0.015f, 2.2f),
            new GoatRing(side * 0.078f, 0.036f, 0f, 0.015f, 0.014f, 2.2f),
        }, 8, Vector3.up, 0.2f, true, 1);
        mesh.Ball(Joint, new Vector3(side * 0.078f, 0.036f, 0f), new Vector3(0.0155f, 0.0155f, 0.0155f), 6, 8);
        return mesh.Build("UA_Crab Leg");
    });

    internal static Mesh LegLower(float side) => Cached("lower" + side, () =>
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        mesh.Tube(Shell, new[]
        {
            new GoatRing(0f, 0f, 0f, 0.0145f, 0.013f, 2.2f),
            new GoatRing(side * 0.032f, -0.05f, 0f, 0.0115f, 0.0105f, 2.2f),
            new GoatRing(side * 0.052f, -0.098f, 0f, 0.008f, 0.0072f, 2.2f),
        }, 8, Vector3.forward, 0.2f, true, 1);
        mesh.Tube(Tip, new[]
        {
            new GoatRing(side * 0.05f, -0.094f, 0f, 0.008f, 0.0072f, 2.2f),
            new GoatRing(side * 0.058f, -0.112f, 0f, 0.0052f, 0.0047f, 2.2f),
            new GoatRing(side * 0.062f, -0.124f, 0f, 0.0014f, 0.0014f, 2.2f),
        }, 8, Vector3.forward, 0.2f, true, 1);
        return mesh.Build("UA_Crab Shin");
    });

    internal static Mesh ClawArm(float side, float size) => Cached("arm" + side + size, () =>
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        mesh.Tube(Shell, new[]
        {
            new GoatRing(Vector3.zero, 0.015f * size, 0.013f * size, 2.2f),
            new GoatRing(new Vector3(side * 0.024f, 0.007f, 0.036f) * size, 0.016f * size, 0.014f * size, 2.2f),
            new GoatRing(new Vector3(side * 0.034f, 0.012f, 0.062f) * size, 0.017f * size, 0.015f * size, 2.2f),
        }, 10, Vector3.up, 0.2f, true, 1);
        mesh.Ball(Joint, new Vector3(side * 0.034f, 0.012f, 0.062f) * size, Vector3.one * 0.016f * size, 6, 10);
        return mesh.Build("UA_Crab Arm");
    });

    /// <summary>The claw's palm and its fixed finger (below; the movable one hinges on top).</summary>
    internal static Mesh ClawHand(float side, float size) => Cached("hand" + side + size, () =>
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        mesh.Tube(Shell, new[]
        {
            new GoatRing(new Vector3(0f, 0f, -0.006f) * size, 0.019f * size, 0.016f * size, 2.3f),
            new GoatRing(new Vector3(0f, 0.004f, 0.026f) * size, 0.029f * size, 0.023f * size, 2.3f),
            new GoatRing(new Vector3(0f, 0.003f, 0.056f) * size, 0.024f * size, 0.019f * size, 2.3f),
        }, 12, Vector3.up, 0.2f, true, 1);
        mesh.Tube(Shell, new[]
        {
            new GoatRing(new Vector3(-side * 0.004f, -0.006f, 0.052f) * size, 0.013f * size, 0.01f * size, 2.2f),
            new GoatRing(new Vector3(-side * 0.006f, -0.007f, 0.074f) * size, 0.01f * size, 0.008f * size, 2.2f),
        }, 8, Vector3.up, 0.2f, true, 1);
        mesh.Tube(Tip, new[]
        {
            new GoatRing(new Vector3(-side * 0.006f, -0.007f, 0.072f) * size, 0.0095f * size, 0.0078f * size, 2.2f),
            new GoatRing(new Vector3(-side * 0.006f, -0.006f, 0.092f) * size, 0.006f * size, 0.005f * size, 2.2f),
            new GoatRing(new Vector3(-side * 0.005f, -0.004f, 0.104f) * size, 0.0015f * size, 0.0015f * size, 2.2f),
        }, 8, Vector3.up, 0.2f, true, 1);
        return mesh.Build("UA_Crab Claw");
    });

    /// <summary>The claw's movable finger, from its hinge on top of the palm.</summary>
    internal static Mesh ClawFinger(float side, float size) => Cached("finger" + side + size, () =>
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        mesh.Tube(Shell, new[]
        {
            new GoatRing(Vector3.zero, 0.012f * size, 0.0095f * size, 2.2f),
            new GoatRing(new Vector3(side * 0.002f, -0.002f, 0.024f) * size, 0.0095f * size, 0.0078f * size, 2.2f),
        }, 8, Vector3.up, 0.2f, true, 1);
        mesh.Tube(Tip, new[]
        {
            new GoatRing(new Vector3(side * 0.002f, -0.002f, 0.022f) * size, 0.009f * size, 0.0074f * size, 2.2f),
            new GoatRing(new Vector3(side * 0.002f, -0.006f, 0.042f) * size, 0.0055f * size, 0.0045f * size, 2.2f),
            new GoatRing(new Vector3(side * 0.001f, -0.011f, 0.054f) * size, 0.0014f * size, 0.0014f * size, 2.2f),
        }, 8, Vector3.up, 0.2f, true, 1);
        return mesh.Build("UA_Crab Finger");
    });

    /// <summary>The shell (wide, rounded, a little higher at the middle), the pale belly under it, the mouth, eyes on stalks.</summary>
    private static Mesh MakeBody()
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        mesh.Tube(Shell, new[]
        {
            new GoatRing(0f, 0.108f, -0.1f, 0.035f, 0.017f, 2.2f),
            new GoatRing(0f, 0.112f, -0.085f, 0.085f, 0.031f, 2.4f),
            new GoatRing(0f, 0.118f, -0.05f, 0.124f, 0.042f, 2.6f),
            new GoatRing(0f, 0.121f, 0f, 0.137f, 0.046f, 2.6f),
            new GoatRing(0f, 0.12f, 0.045f, 0.13f, 0.043f, 2.6f),
            new GoatRing(0f, 0.116f, 0.08f, 0.1f, 0.034f, 2.4f),
            new GoatRing(0f, 0.112f, 0.098f, 0.055f, 0.02f, 2.2f),
        }, 22, Vector3.up, 0.3f, false, 2);
        mesh.Ball(Belly, new Vector3(0f, 0.083f, -0.004f), new Vector3(0.112f, 0.022f, 0.08f), 8, 16);
        mesh.Ball(Belly, new Vector3(0f, 0.102f, 0.097f), new Vector3(0.03f, 0.012f, 0.009f), 6, 10);
        foreach (float side in new[] { 1f, -1f })
        {
            mesh.Tube(Joint, new[]
            {
                new GoatRing(side * 0.031f, 0.142f, 0.083f, 0.0085f, 0.0085f, 2f),
                new GoatRing(side * 0.037f, 0.162f, 0.089f, 0.0078f, 0.0078f, 2f),
                new GoatRing(side * 0.04f, 0.178f, 0.093f, 0.0072f, 0.0072f, 2f),
            }, 8, Vector3.forward, 0.2f, true, 1);
            mesh.Ball(Eye, new Vector3(side * 0.041f, 0.186f, 0.095f), new Vector3(0.0135f, 0.0135f, 0.0135f), 8, 12);
        }
        return mesh.Build("UA_Crab Body");
    }

    /// <summary>
    /// The cooked crab leg: a thick upper segment, a pale knuckle, a narrower lower one curving to a point; at the top,
    /// where it broke off, white meat. Lying along z on the ground.
    /// </summary>
    private static Mesh MakeLeg()
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        mesh.Tube(Shell, new[]
        {
            new GoatRing(0f, 0.03f, -0.13f, 0.027f, 0.024f, 2.3f),
            new GoatRing(0f, 0.032f, -0.07f, 0.029f, 0.026f, 2.3f),
            new GoatRing(0f, 0.031f, -0.012f, 0.025f, 0.022f, 2.3f),
        }, 14, Vector3.up, 0.2f, false, 2);
        mesh.Ball(Joint, new Vector3(0f, 0.031f, 0f), new Vector3(0.0225f, 0.021f, 0.02f), 8, 12);
        mesh.Tube(Shell, new[]
        {
            new GoatRing(0f, 0.031f, 0.012f, 0.021f, 0.019f, 2.3f),
            new GoatRing(0f, 0.033f, 0.07f, 0.019f, 0.017f, 2.3f),
            new GoatRing(0f, 0.029f, 0.115f, 0.012f, 0.011f, 2.2f),
        }, 12, Vector3.up, 0.2f, false, 2);
        mesh.Tube(Tip, new[]
        {
            new GoatRing(0f, 0.029f, 0.112f, 0.0125f, 0.0115f, 2.2f),
            new GoatRing(0f, 0.024f, 0.135f, 0.007f, 0.0065f, 2.2f),
            new GoatRing(0f, 0.019f, 0.15f, 0.0015f, 0.0015f, 2.2f),
        }, 10, Vector3.up, 0.2f, false, 1);
        mesh.Ball(Meat, new Vector3(0f, 0.03f, -0.133f), new Vector3(0.024f, 0.021f, 0.009f), 8, 12);
        return mesh.Build("UA_Crab Cooked Leg");
    }

    private static Material[] MakeMaterials()
    {
        string[] names = { "UA_CrabShell", "UA_CrabBelly", "UA_CrabJoint", "UA_CrabTip", "UA_CrabEye", "UA_CrabMeat" };
        float[] smooth = { 0.45f, 0.25f, 0.35f, 0.55f, 0.9f, 0.2f };
        Material[] made = new Material[Kinds];
        for (int i = 0; i < Kinds; i++)
        {
            made[i] = new Material(Snakes.Skin()) { name = names[i] };
            made[i].SetTexture("_BaseTexture", Texture2D.whiteTexture);
            made[i].SetColor("_Tint", Tints[i]);
            made[i].SetColor("_BaseColor", Tints[i]);
            made[i].SetFloat("_BaseSmooth", smooth[i]);
            foreach (string layer in new[] { "_Texture1", "_Texture2" })
            {
                if (made[i].HasProperty(layer))
                {
                    made[i].SetTexture(layer, Texture2D.whiteTexture);
                }
            }
        }
        return made;
    }
}
