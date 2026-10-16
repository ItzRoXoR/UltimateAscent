using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Core;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace UltimateAscent.Features;

/// <summary>
/// Three of the game's cut foods grow in the wild from ascent 9 and in custom runs, unless the config's
/// "New Items" switch is off (the Custom Run screen lists only what spawns in suitcases, so in custom
/// runs too that switch is the only one they follow; see <see cref="NewItems.WildOn"/>):
/// <list type="bullet">
/// <item>the Wonderberry (20 hunger and 10 s of infinite stamina, no crash afterwards; from ascent 14
/// 10 hunger and 6 s) on 0.2% of the berry bushes that grow Crispberries;</item>
/// <item>the Green Clusterberry (10 hunger, removes 30 poison; from ascent 14 5 hunger and 15 poison) on
/// thorny vines, as often as each of the other Clusterberries;</item>
/// <item>the Weird Shroom (5 hunger, the game's 10; glows for 60 s) in mushroom patches, as often as the (safe) Cluster
/// Shroom. Its glow works again: the game copies every effect before it applies it, and the copy lost
/// the light the glow needs.</item>
/// </list>
/// A berry bush and a thorny vine take the kind of berry they grow from the game's spawn pools without
/// asking whether an item may spawn, and mushroom patches pick from fixed lists of their own: each wild
/// food takes its share of those picks instead of sitting in a pool.
/// <para>The Wonderberry is not in the game's item database: it is built from the Red Crispberry and
/// registered under an id of its own (its old one, 81, is the Medicinal Root's now). Once a map is loaded
/// (the game's own Wonderberry comes with it) it takes that berry's look and icon.</para>
/// </summary>
internal static class WildFoods
{
    internal const string WonderberryPrefab = "Wonderberry";
    internal const ushort WonderberryId = 9002;
    internal const string WonderberryNameKey = "NAME_WONDERBERRY";
    internal const string WonderberryName = "Wonderberry";
    internal const string GreenClusterberryPrefab = "Clusterberry_UNUSED";
    internal const string WeirdShroomPrefab = "Mushroom Glow";
    private const string CrispberryPrefab = "Apple Berry Red";
    private const string LollipopPrefab = "Lollipop";

    /// <summary>The safe Cluster Shroom, whose share of a mushroom patch's picks the Weird Shroom gets.</summary>
    internal const string ClusterShroomPrefab = "Mushroom Cluster";

    private const string BerryMesh = "Apple Berry";
    private const float WonderberryHunger = 0.2f;
    internal const float WonderberryRush = 10f;

    /// <summary>From ascent 14 (half the hunger comes from the rule that halves all food).</summary>
    internal const float WonderberryRushWorse = 6f;

    /// <summary>The share of Crispberry bushes that grow Wonderberries.</summary>
    internal const float WonderberryShare = 0.002f;

    /// <summary>The Green Clusterberry's weight among the thorny vines' berries: Common, like the others.</summary>
    internal static readonly int GreenClusterberryWeight = LootData.RarityWeights[Rarity.Common];

    /// <summary>The poison a Green Clusterberry removes from ascent 14 (the game: 30).</summary>
    internal const float GreenClusterberryCureWorse = 0.15f;

    private static Item wonderberry;
    private static Item greenClusterberry;
    private static Item weirdShroom;

    /// <summary>The Weird Shroom's hunger (the game: 10).</summary>
    internal const float WeirdShroomHunger = 0.05f;
    private static bool wonderberryLook;
    private static GameObject glowLight;

    internal static bool IsWonderberry(Item item) => item != null && wonderberry != null && item.itemID == wonderberry.itemID;

    internal static bool IsGreenClusterberry(Item item) => item != null && greenClusterberry != null && item.itemID == greenClusterberry.itemID;

    internal static void Build()
    {
        BuildWonderberry();
        if (!ItemDatabase.TryGetItem(GreenClusterberryPrefab, out greenClusterberry) || greenClusterberry == null)
        {
            Plugin.Log.LogWarning($"{GreenClusterberryPrefab} is missing from the item database; it does not grow in the wild.");
        }
        if (ItemDatabase.TryGetItem(WeirdShroomPrefab, out weirdShroom) && weirdShroom != null)
        {
            foreach (Action_RestoreHunger food in weirdShroom.GetComponents<Action_RestoreHunger>())
            {
                food.restorationAmount = WeirdShroomHunger;
            }
            Action_ApplyAffliction glow = weirdShroom.GetComponents<Action_ApplyAffliction>().FirstOrDefault(a => a.affliction is Affliction_Glowing);
            glowLight = glow != null ? ((Affliction_Glowing)glow.affliction).pointLightPref : null;
        }
        else
        {
            Plugin.Log.LogWarning($"{WeirdShroomPrefab} is missing from the item database; it does not grow in the wild.");
        }
    }

    /// <summary>The Wonderberry: a Red Crispberry that restores 20 hunger and gives 10 s of infinite stamina.</summary>
    private static void BuildWonderberry()
    {
        if (ItemDatabase.TryGetItem(WonderberryPrefab, out Item existing) && existing != null)
        {
            // The game has it again: use the game's own.
            wonderberry = existing;
            wonderberryLook = true;
            return;
        }
        if (!ItemDatabase.TryGetItem(CrispberryPrefab, out Item crispberry) || crispberry == null)
        {
            Plugin.Log.LogWarning("The Red Crispberry is missing from the item database; the Wonderberry is not added.");
            return;
        }
        Item berry = ModItems.Copy(crispberry, WonderberryPrefab);
        berry.UIData.itemName = WonderberryName;
        // The Crispberry's colour-blind look would swap the rainbow skin back.
        foreach (ColorblindVariant variant in berry.GetComponentsInChildren<ColorblindVariant>(true))
        {
            Object.DestroyImmediate(variant);
        }
        berry.UIData.hasColorBlindIcon = false;
        Material rainbow = RainbowSkin();
        if (rainbow != null)
        {
            foreach (MeshFilter filter in berry.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && f.sharedMesh.name == BerryMesh))
            {
                filter.GetComponent<MeshRenderer>().sharedMaterial = rainbow;
            }
        }
        foreach (Action_RestoreHunger hunger in berry.GetComponents<Action_RestoreHunger>())
        {
            hunger.restorationAmount = WonderberryHunger;
        }
        Action_ApplyAffliction rush = berry.gameObject.AddComponent<Action_ApplyAffliction>();
        rush.affliction = new Affliction_InfiniteStamina(WonderberryRush);
        rush.extraAfflictions = new Affliction[0];
        rush.OnCastFinished = true;
        if (berry.TryGetComponent(out LootData loot))
        {
            // It grows only where the berry bushes' picks hand it out (the Crispberry's pools are not its own).
            loot.spawnLocations = SpawnPool.None;
            loot.Rarity = Rarity.Common;
        }
        if (ModItems.Register(berry, WonderberryId))
        {
            wonderberry = berry;
        }
    }

    /// <summary>
    /// The Wonderberry's rainbow skin: the game made it from the Big Lollipop's material (its rainbow texture
    /// five times around the berry, whiter and smoother); the same here, until the game's own is loaded.
    /// </summary>
    private static Material RainbowSkin()
    {
        Renderer lollipop = ItemDatabase.TryGetItem(LollipopPrefab, out Item item) && item != null ? item.mainRenderer : null;
        if (lollipop == null || lollipop.sharedMaterial == null)
        {
            return null;
        }
        Material skin = new Material(lollipop.sharedMaterial) { name = "M_RainbowBerry (Ultimate Ascent)" };
        skin.SetColor("_BaseColor", Color.white);
        skin.SetColor("_Color1", Color.white);
        skin.SetFloat("_BaseSmooth", 0.563f);
        skin.SetTextureScale("_BaseTexture", new Vector2(5f, 1f));
        Color tint = skin.GetColor("_Tint");
        skin.SetColor("_Tint", new Color(0.6f, 0.6f, 0.6f, tint.a));
        return skin;
    }

    /// <summary>
    /// The game's own Wonderberry comes with every map (an old spawn list still names it): once a map is
    /// loaded, the built one takes its materials and icon.
    /// </summary>
    internal static void OnMapLoaded()
    {
        if (!Rules.InRun || wonderberry == null || wonderberryLook)
        {
            return;
        }
        Item original = Resources.FindObjectsOfTypeAll<Item>().FirstOrDefault(i => i != null && i != wonderberry && i.gameObject.name == WonderberryPrefab && !i.gameObject.scene.IsValid());
        if (original == null)
        {
            Plugin.Debug("The game's own Wonderberry is not loaded with this map; the built one keeps the Crispberry's look.");
            return;
        }
        wonderberryLook = true;
        if (original.UIData.icon != null)
        {
            wonderberry.UIData.icon = original.UIData.icon;
        }
        Dictionary<string, Material[]> looks = original.GetComponentsInChildren<Renderer>(true)
            .Where(r => r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null)
            .GroupBy(r => r.GetComponent<MeshFilter>().sharedMesh.name)
            .ToDictionary(g => g.Key, g => g.First().sharedMaterials);
        foreach (Renderer renderer in wonderberry.GetComponentsInChildren<Renderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && looks.TryGetValue(filter.sharedMesh.name, out Material[] materials))
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    /// <summary>The share an item of this weight would get of a spawn pool's picks if it were in the pool.</summary>
    private static float ShareOf(SpawnPool pool, int weight)
    {
        if (LootData.AllSpawnWeightData == null)
        {
            LootData.PopulateLootData();
        }
        int total = LootData.AllSpawnWeightData.TryGetValue(pool, out Dictionary<ushort, int> weights) ? weights.Values.Sum() : 0;
        return weight / (float)(total + weight);
    }

    /// <summary>
    /// A berry bush (or a thorny vine) picks the one kind of berry it grows here: 0.2% of the Crispberry
    /// bushes grow Wonderberries, and the thorny vines grow Green Clusterberries as often as each of the
    /// other Clusterberries.
    /// </summary>
    [HarmonyPatch(typeof(LootData), nameof(LootData.GetRandomItem))]
    private static class BerryPicks
    {
        private static void Postfix(SpawnPool spawnPool, ref GameObject __result)
        {
            if (__result == null || !NewItems.WildOn)
            {
                return;
            }
            if ((spawnPool == SpawnPool.BerryBushBeach || spawnPool == SpawnPool.BerryBushJungle) && wonderberry != null)
            {
                if (Random.value < WonderberryShare)
                {
                    __result = wonderberry.gameObject;
                }
            }
            else if (spawnPool == SpawnPool.SpikyVine && greenClusterberry != null && Random.value < ShareOf(spawnPool, GreenClusterberryWeight))
            {
                __result = greenClusterberry.gameObject;
            }
        }
    }

    /// <summary>
    /// A mushroom patch picks what grows on it from its own list: the Weird Shroom gets the share it would
    /// have with the safe Cluster Shroom's weight in that list.
    /// </summary>
    [HarmonyPatch(typeof(SpawnList), nameof(SpawnList.GetSpawns))]
    private static class ShroomPicks
    {
        private static void Postfix(SpawnList __instance, List<GameObject> __result)
        {
            if (__result == null || weirdShroom == null || __instance.items == null || !NewItems.WildOn)
            {
                return;
            }
            float share = WeirdShroomShare(__instance);
            if (share <= 0f)
            {
                return;
            }
            for (int i = 0; i < __result.Count; i++)
            {
                if (__result[i] != null && Random.value < share)
                {
                    __result[i] = weirdShroom.gameObject;
                }
            }
        }
    }

    /// <summary>The Weird Shroom's share of a mushroom patch's picks (0 for lists without the Cluster Shroom).</summary>
    internal static float WeirdShroomShare(SpawnList list)
    {
        int total = 0;
        int cluster = 0;
        foreach (SpawnEntry entry in list.items)
        {
            if (entry == null || entry.prefab == null)
            {
                continue;
            }
            total += entry.weight;
            if (entry.prefab.name == ClusterShroomPrefab)
            {
                cluster = entry.weight;
            }
        }
        return cluster > 0 ? cluster / (float)(total + cluster) : 0f;
    }

    /// <summary>The Wonderberry's sugar rush lasts 6 s from ascent 14 (the game copies it as it applies it).</summary>
    [HarmonyPatch(typeof(Action_ApplyAffliction), nameof(Action_ApplyAffliction.RunAction))]
    private static class ShorterRush
    {
        private static void Prefix(Action_ApplyAffliction __instance, out float? __state)
        {
            __state = null;
            if (Rules.Enabled(Lvl.WorseItems) && IsWonderberry(__instance.item) && __instance.affliction is Affliction_InfiniteStamina rush)
            {
                __state = rush.totalTime;
                rush.totalTime = WonderberryRushWorse;
            }
        }

        private static Exception Finalizer(Action_ApplyAffliction __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue && __instance.affliction != null)
            {
                __instance.affliction.totalTime = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>The glow's light, which the copy of the effect the game applies has lost.</summary>
    [HarmonyPatch(typeof(Affliction_Glowing), nameof(Affliction_Glowing.OnApplied))]
    private static class GlowLight
    {
        private static void Prefix(Affliction_Glowing __instance)
        {
            if (__instance.pointLightPref == null)
            {
                __instance.pointLightPref = glowLight;
            }
        }
    }
}
