using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Warp Compass, cut from the game (it is in the item database, in no pool), from ascent 9. It turns
/// up only in Ancient Luggage and in the Ancient Statues at the end of each part of the climb (the two
/// pools the Ritual Dagger comes from), at its own rarity (Legendary, like the dagger), and, as in the
/// game, never in solo, where there is no one to warp to. The config's "New Items" switch keeps it out;
/// in custom runs it has its own switch.
/// <list type="bullet">
/// <item>One use (the game: 3).</item>
/// <item>It warps you only to a teammate more than 12 m away who has stood on solid ground for a second
/// (not climbing, hanging on a rope or vine, falling, flying or carried), onto free ground beside them
/// that they can see. The game warped you into the teammate's body: into the wall they climbed, or into
/// the air with them. With no such teammate, or no free ground beside one, nothing happens and the use is
/// kept.</item>
/// </list>
/// </summary>
internal static class WarpCompass
{
    internal const string PrefabName = "Warp Compass";

    /// <summary>The Ritual Dagger's pools: the one Ancient Luggage spawns from, and the Ancient Statues'.</summary>
    internal const SpawnPool Pools = SpawnPool.LuggageCursed | SpawnPool.RespawnCoffin;

    internal const int Uses = 1;

    /// <summary>How long a teammate must have stood on solid ground.</summary>
    internal const float GroundedSeconds = 1f;

    /// <summary>Where you may land: this far from the teammate (tried nearest first, all round)...</summary>
    internal static readonly float[] Rings = { 1.5f, 2.2f, 3f };

    private const int Directions = 12;

    /// <summary>...on ground this level with theirs...</summary>
    internal const float SameLevel = 1f;

    /// <summary>...no steeper than this...</summary>
    internal const float MaxSlope = 40f;

    /// <summary>...with room for a scout (this tall, this wide)...</summary>
    private const float Height = 1.7f;
    private const float Radius = 0.35f;

    /// <summary>...not in another scout...</summary>
    private const float Personal = 0.9f;

    /// <summary>...and well above any lava below.</summary>
    private const float AboveLava = 1.5f;

    private static Item template;
    private static bool spending;

    /// <summary>Warps made and uses kept (nobody to warp to), on this game; for the tests.</summary>
    internal static int Warps { get; private set; }
    internal static int Kept { get; private set; }

    internal static Item Template => template;

    /// <summary>Spawns in this run: from ascent 9 with the config's "New Items" on (custom runs: its own switch).</summary>
    private static bool Allowed => RunSettings.IsCustomRun || (Rules.InExtraAscent && HostOptions.NewItemsOn);

    internal static bool IsCompass(Item item) => item != null && template != null && item.itemID == template.itemID;

    /// <summary>The game's cut compass, changed in place before the game builds its loot tables and custom-run switches.</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(PrefabName, out Item compass) || compass == null || compass.GetComponent<Action_WarpToRandomPlayer>() == null)
        {
            Plugin.Log.LogWarning("The cut Warp Compass is missing from the item database; it does not spawn.");
            return;
        }
        LootData loot = compass.GetComponent<LootData>();
        if (loot == null)
        {
            Plugin.Log.LogWarning("The cut Warp Compass has no loot data; it does not spawn.");
            return;
        }
        loot.spawnLocations = Pools;
        compass.totalUses = Uses;
        template = compass;
        Plugin.Debug($"Warp Compass: spawns in {loot.spawnLocations}, {loot.Rarity}, banned in solo {loot.banInSolo}, {compass.totalUses} use.");
    }

    /// <summary>A teammate to warp to: alive, more than <paramref name="minimumDistance"/> away, on solid ground for a second.</summary>
    internal static bool CanWarpTo(Character user, Character mate, float minimumDistance)
    {
        return mate != null && mate != user && !mate.data.dead && Vector3.Distance(user.Center, mate.Center) > minimumDistance && Grounded(mate);
    }

    /// <summary>Standing (or lying) on solid ground for a second: not climbing, hanging, falling, flying or carried.</summary>
    internal static bool Grounded(Character mate)
    {
        CharacterData data = mate.data;
        return data.isGrounded && data.groundedFor >= GroundedSeconds && !data.isClimbingAnything && data.carrier == null;
    }

    /// <summary>
    /// Where to land beside a teammate (the point the warp brings the scout's centre to): free, level
    /// ground they can see, nearest first, starting on the side the warping scout comes from.
    /// </summary>
    internal static bool TryFindLanding(Character mate, Vector3 from, out Vector3 landing)
    {
        landing = default;
        int mask = HelperFunctions.terrainMapMask;
        Vector3 centre = mate.Center;
        if (!Physics.Raycast(centre + Vector3.up * 0.5f, Vector3.down, out RaycastHit under, 4f, mask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }
        float lift = Mathf.Clamp(centre.y - under.point.y, 0.8f, 1.4f);
        Vector3 toward = Vector3.ProjectOnPlane(from - centre, Vector3.up);
        toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
        WaterZone[] water = Object.FindObjectsByType<WaterZone>(FindObjectsSortMode.None);
        Lava[] lava = Object.FindObjectsByType<Lava>(FindObjectsSortMode.None);
        foreach (float ring in Rings)
        {
            for (int i = 0; i < Directions; i++)
            {
                // Alternate sides: straight toward the warping scout first, then further round each way.
                int step = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
                Vector3 direction = Quaternion.AngleAxis(step * 360f / Directions, Vector3.up) * toward;
                Vector3 probe = new Vector3(centre.x, under.point.y + Height, centre.z) + direction * ring;
                if (Physics.Linecast(centre, probe, mask, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                if (!Physics.Raycast(probe, Vector3.down, out RaycastHit ground, Height + SameLevel + 0.5f, mask, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(ground.point.y - under.point.y) > SameLevel || Vector3.Angle(ground.normal, Vector3.up) > MaxSlope)
                {
                    continue;
                }
                // Room to stand, from a little above the ground (a slope's own rise is not in the way).
                if (Physics.CheckCapsule(ground.point + Vector3.up * (Radius + 0.3f), ground.point + Vector3.up * (Height - Radius), Radius, mask, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }
                Vector3 spot = ground.point + Vector3.up * lift;
                if (GroundItems.InWater(ground.point, water) || lava.Any(l => l != null && l.isActiveAndEnabled && !l.OutsideBounds(spot) && ground.point.y < l.transform.position.y + AboveLava)
                    || Character.AllCharacters.Any(c => c != null && HelperFunctions.FlatDistance(c.Center, spot) < Personal && Mathf.Abs(c.Center.y - spot.y) < Height))
                {
                    continue;
                }
                landing = spot;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The game gives an item its uses when it first starts, unless something has read its uses before
    /// (the inventory does, for an item made straight into a hand or a slot); a compass without uses
    /// would never be used up. It gets its one.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.Start))]
    private static class CompassUses
    {
        private static void Postfix(Item __instance)
        {
            if (!IsCompass(__instance))
            {
                return;
            }
            OptionableIntItemData uses = __instance.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (!uses.HasData)
            {
                uses.HasData = true;
                uses.Value = Uses;
                __instance.SetUseRemainingPercentage(1f);
            }
        }
    }

    /// <summary>Regular runs: only from ascent 9 and with the config's "New Items" on.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.IsValidToSpawn))]
    private static class OnlyFromAscent9
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result && !Allowed && IsCompass(__instance))
            {
                __result = false;
            }
        }
    }

    /// <summary>The compass spends its use only when it warps you (<see cref="SafeWarp"/>).</summary>
    [HarmonyPatch(typeof(Action_ReduceUses), nameof(Action_ReduceUses.RunAction))]
    private static class UseOnlyOnWarp
    {
        private static bool Prefix(Action_ReduceUses __instance)
        {
            return spending || !IsCompass(__instance.item);
        }
    }

    /// <summary>On the game of the scout using it: a teammate on solid ground, and free ground beside them, or nothing.</summary>
    [HarmonyPatch(typeof(Action_WarpToRandomPlayer), nameof(Action_WarpToRandomPlayer.RunAction))]
    private static class SafeWarp
    {
        private static bool Prefix(Action_WarpToRandomPlayer __instance)
        {
            Item item = __instance.item;
            Character user = __instance.character;
            if (!IsCompass(item) || user == null)
            {
                return true;
            }
            foreach (SFX_Instance sound in __instance.warpSFX)
            {
                sound.Play();
            }
            List<Character> mates = Character.AllCharacters.Where(c => CanWarpTo(user, c, __instance.minimumDistance)).OrderBy(_ => Random.value).ToList();
            foreach (Character mate in mates)
            {
                if (TryFindLanding(mate, user.Center, out Vector3 landing))
                {
                    user.photonView.RPC("WarpPlayerRPC", RpcTarget.All, landing, true);
                    Spend(item);
                    Warps++;
                    Plugin.Debug($"Warp Compass: warped beside {mate.characterName}, to {landing}.");
                    return false;
                }
            }
            Kept++;
            Plugin.Debug($"Warp Compass: no teammate on solid ground with free ground beside them ({mates.Count} on the ground); the use is kept.");
            return false;
        }

        /// <summary>The game's own use of a use (and the compass is gone once it has none left).</summary>
        private static void Spend(Item item)
        {
            Action_ReduceUses reduce = item.GetComponent<Action_ReduceUses>();
            if (reduce == null)
            {
                return;
            }
            spending = true;
            try
            {
                reduce.RunAction();
            }
            finally
            {
                spending = false;
            }
        }
    }
}
