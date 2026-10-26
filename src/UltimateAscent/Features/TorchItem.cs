using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Torch, the game's (three hang in the Mesa's Tomb), is a weapon as well as a light in every climb, custom runs
/// included.
/// <list type="bullet">
/// <item>The use button, held, lights it or puts it out, as in the game. Lit, it warms like the Lantern: 2.5
/// cold a second off every scout within 4.5 m, 2 from ascent 15 (A14_Items' weaker lanterns). It burns its 40 s
/// of fuel only while lit.</item>
/// <item>The second use button (the right mouse button) swings it, only while it burns: the Stick's swing (33%
/// stamina, the kick's reach, aim and push, see <see cref="StickItem"/>), knocking every scout and zombie it
/// reaches down for 2 s (the Stick: 3 s). The scouts it hits also take 10 heat, 20 with ascent 15's rule (as it
/// lands from ascent 14 on); zombies take no heat. A swing burns 4 s of the fuel: a full Torch gives 10 swings,
/// 40 s of light, or a mix of both.</item>
/// <item>Lit in a scout's hands, it keeps the Gloom's ghosts away like a lit bell tower: a ghost hunting the holder
/// or anyone within 4.5 m of the Torch is pushed back until it is out of blast range of them, and cannot arm while
/// it is pushed. Below ascent 17 the push also burns it, as the bell towers do in the game (from 17 they no longer
/// burn it, see <see cref="A17_GloomGhost"/>).</item>
/// <item>It turns up in suitcases wherever the Lantern does, at half the Lantern's weight (10 to its 20): from
/// ascent 9 with the config's "New Items" on; in custom runs it has its own switch, after the Lantern's.</item>
/// </list>
/// </summary>
internal static class TorchItem
{
    internal const string PrefabName = "Torch";
    internal const string ItemName = "Torch";

    /// <summary>The knock-down of a Torch's hit (the Stick's: 3 s).</summary>
    internal const float Knockdown = 2f;

    /// <summary>
    /// The knock-down a burning Torch's hit sends to a scout: 2 s plus a mark, so the scout it hit can tell it from
    /// any other knock-down (the game sends only the seconds) and take the heat on their own client.
    /// </summary>
    internal const float KnockdownMarked = Knockdown + 0.0031f;

    /// <summary>The heat of a hit, as it lands on the bar; with ascent 15's rule as it lands from ascent 14 on.</summary>
    internal const float Heat = 0.1f;
    internal const float HeatWorse = 0.2f;

    /// <summary>A lit Torch keeps ghosts away from the scouts within this reach of it (its warmth's)...</summary>
    internal const float GhostReach = 4.5f;

    /// <summary>...pushing them back until they are this far beyond it (the reach of the ghost's burst).</summary>
    internal const float GhostBlastRange = 10f;

    /// <summary>The fuel a swing burns: a full Torch (40 s) gives 10 swings.</summary>
    internal const float SwingFuel = 4f;

    /// <summary>What the heat lands as (one name for every hit: what is left of a 2.5-point step carries over to the next).</summary>
    private const string HeatSource = "torch";

    private static Item template;

    internal static Item Template => template;

    internal static bool IsTorch(Item item) => item != null && template != null && item.itemID == template.itemID;

    /// <summary>A Torch swings in every climb.</summary>
    internal static bool Works => NewItems.RegularRun || CustomRunItems.InCustomRun;

    /// <summary>Suitcases may hold it in custom runs (by its own switch), and on every ascent with the config's "New Items" on.</summary>
    private static bool Allowed => NewItems.Available;

    /// <summary>The heat of a hit, as it lands on the bar now.</summary>
    internal static float HeatNow => Rules.Enabled(Lvl.WorseItems) ? A15_StrongerAilments.FromAscent15(CharacterAfflictions.STATUSTYPE.Hot, HeatWorse) : Heat;

    /// <summary>For the tests: the Torch's swings on this client, and the hits that burned this client's scout.</summary>
    internal static int Swings;
    internal static int Burns;

    /// <summary>The game's Torch, given its second use (the swing, and the game's message that plays an item's animation for everyone) and the Lantern's suitcases.</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(PrefabName, out Item torch) || torch == null || !torch.TryGetComponent(out Lantern _))
        {
            Plugin.Log.LogWarning("The Torch is missing from the item database, or no longer burns like a lantern; it stays the game's.");
            return;
        }
        torch.UIData.hasSecondInteract = true;
        torch.UIData.secondaryInteractPrompt = StickItem.SwingPrompt;
        if (torch.GetComponent<Action_PlayItemAnimation>() == null)
        {
            Action_PlayItemAnimation message = torch.gameObject.AddComponent<Action_PlayItemAnimation>();
            message.useRPC = true;
        }
        // In the Lantern's suitcases (set again for each climb, see MoreLanterns), which also lists it on the Custom Run screen.
        if (torch.TryGetComponent(out LootData loot) && ItemDatabase.TryGetItem(MoreLanterns.LanternPrefab, out Item lantern) && lantern != null && lantern.TryGetComponent(out LootData lanternLoot))
        {
            loot.spawnLocations = lanternLoot.spawnLocations;
            loot.Rarity = lanternLoot.Rarity;
        }
        template = torch;
    }

    /// <summary>In every suitcase pool that holds the Torch: half the Lantern's weight there (10 to its 20).</summary>
    [HarmonyPatch(typeof(LootData), nameof(LootData.PopulateLootData))]
    private static class HalfTheLantern
    {
        private static void Postfix()
        {
            if (template == null || !ItemDatabase.TryGetItem(MoreLanterns.LanternPrefab, out Item lantern) || lantern == null || !lantern.TryGetComponent(out LootData lanternLoot))
            {
                return;
            }
            foreach (Dictionary<ushort, int> weights in LootData.AllSpawnWeightData.Values)
            {
                if (weights.ContainsKey(template.itemID))
                {
                    int lanternWeight = weights.TryGetValue(lantern.itemID, out int weight) ? weight : LootData.RarityWeights[lanternLoot.Rarity];
                    weights[template.itemID] = Mathf.Max(1, lanternWeight / 2);
                }
            }
        }
    }

    /// <summary>Regular runs: suitcases hold it only with "New Items" on (on every ascent) (the Tomb's three are the game's own and always there).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.IsValidToSpawn))]
    private static class SuitcasesFromAscent9
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result && !Allowed && IsTorch(__instance))
            {
                __result = false;
            }
        }
    }

    /// <summary>The second use (and its prompt) only while the Torch burns, and only where it swings.</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.CanUseSecondary))]
    private static class SwingOnlyLit
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result && IsTorch(__instance) && (!Works || !__instance.TryGetComponent(out Lantern lantern) || !lantern.lit))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// The second use button swings a burning Torch, once per press: when a second use starts (the game keeps it going
    /// while the button is held, and asks again every frame). The swing burns its fuel; scouts it hits are knocked down
    /// with the mark that brings the heat, zombies plainly.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.StartUseSecondary))]
    private static class SwingOnSecondUse
    {
        private static void Prefix(Item __instance, out bool __state)
        {
            __state = __instance.isUsingSecondary;
        }

        private static void Postfix(Item __instance, bool __state)
        {
            if (__state || !__instance.isUsingSecondary || !IsTorch(__instance) || !Works || !__instance.TryGetComponent(out Lantern lantern) || !lantern.lit)
            {
                return;
            }
            Character holder = __instance.holderCharacter;
            if (holder == null || !holder.IsLocal)
            {
                return;
            }
            if (StickItem.TrySwing(__instance, (target, zombie) => zombie ? Knockdown : KnockdownMarked))
            {
                Swings++;
                lantern.fuel = Mathf.Max(0f, lantern.fuel - SwingFuel);
            }
        }
    }

    /// <summary>A burning Torch's hit: a 2 s knock-down, and the scout it hit takes the heat on their own client.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.RPCA_Fall))]
    private static class BurningHit
    {
        private static void Prefix(Character __instance, ref float seconds)
        {
            if (Mathf.Abs(seconds - KnockdownMarked) > 0.0001f)
            {
                return;
            }
            seconds = Knockdown;
            if (__instance.IsLocal && !__instance.data.dead && !__instance.isBot && !__instance.isZombie)
            {
                Burns++;
                ExactStatus.Add(__instance.refs.afflictions, CharacterAfflictions.STATUSTYPE.Hot, HeatNow, HeatSource);
            }
        }
    }

    /// <summary>A burning Torch in a scout's hands, the nearest to this point within its reach, or null.</summary>
    internal static Item LitTorchNear(Vector3 point)
    {
        Item nearest = null;
        float best = GhostReach;
        foreach (Character character in Character.AllCharacters)
        {
            Item item = character != null && !character.data.dead ? character.data.currentItem : null;
            if (!IsTorch(item) || !item.TryGetComponent(out Lantern lantern) || !lantern.lit)
            {
                continue;
            }
            float distance = Vector3.Distance(item.transform.position, point);
            if (distance <= best)
            {
                best = distance;
                nearest = item;
            }
        }
        return nearest;
    }

    /// <summary>
    /// A ghost hunting a scout by a burning Torch is pushed back, the way a lit bell tower pushes it (on the ghost's
    /// own game, the host's): until it is out of blast range of everyone the Torch keeps safe, burning (which keeps it
    /// from arming; from ascent 17 burning does it no harm).
    /// </summary>
    [HarmonyPatch(typeof(global::Peak.GhostBall), nameof(global::Peak.GhostBall.UpdateMovement))]
    private static class KeepsGhostsAway
    {
        private static void Postfix(global::Peak.GhostBall __instance)
        {
            if (!Works || template == null || __instance.risingFromSpawn || __instance.target == null)
            {
                return;
            }
            Item torch = LitTorchNear(__instance.target.Center);
            if (torch == null)
            {
                return;
            }
            Vector3 away = __instance.transform.position - torch.transform.position;
            if (away.magnitude >= GhostReach + GhostBlastRange)
            {
                return;
            }
            __instance.burnTick = 0.5f;
            __instance.rig.AddForce(away.normalized * __instance.repelForce * Time.deltaTime);
        }
    }
}
