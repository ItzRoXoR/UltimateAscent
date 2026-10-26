using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// With "Verbose Logging" on: every time the local scout takes an item out or puts it away, the log gets the stamina,
/// the most stamina the bar allows, every status on it, and what the weight is made of (each slot, the backpack and
/// its contents, the temporary hand slot, scouts carried, items stuck to them), a frame after the change. For a
/// stamina change seen only while an item is held (reported 2026-10-25 after a Warp Fungus throw and a jellyfish slip).
/// </summary>
internal static class HoldDiagnostics
{
    private static bool On => Plugin.VerboseLogging != null && Plugin.VerboseLogging.Value;

    [HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.EquipSlotRpc))]
    private static class Held
    {
        private static void Prefix(CharacterItems __instance, int slotID, out string __state)
        {
            __state = null;
            Character character = __instance.character;
            if (On && character != null && character.IsLocal)
            {
                __state = Describe(character);
            }
        }

        private static void Postfix(CharacterItems __instance, int slotID, string __state)
        {
            if (__state != null)
            {
                __instance.StartCoroutine(After(__instance.character, slotID, __state));
            }
        }
    }

    private static IEnumerator After(Character character, int slotID, string before)
    {
        yield return null;
        if (character == null)
        {
            yield break;
        }
        Plugin.Log.LogInfo($"[hold] {(slotID < 0 ? "put away" : $"took out slot {slotID}")}\n  before: {before}\n  after:  {Describe(character)}");
    }

    /// <summary>One line: stamina, the bar's statuses and what the weight is made of.</summary>
    internal static string Describe(Character character)
    {
        CharacterAfflictions afflictions = character.refs.afflictions;
        Player player = character.player;
        string statuses = string.Join(", ", System.Enum.GetValues(typeof(CharacterAfflictions.STATUSTYPE)).Cast<CharacterAfflictions.STATUSTYPE>()
            .Where(t => (int)t < afflictions.currentStatuses.Length && afflictions.currentStatuses[(int)t] > 0.0001f)
            .Select(t => $"{t} {afflictions.currentStatuses[(int)t] * 100f:0.#}"));
        List<string> weight = new List<string>();
        int steps = 0;
        void Count(string where, Item prefab)
        {
            if (prefab == null)
            {
                return;
            }
            weight.Add($"{where} {prefab.name} {prefab.CarryWeight}");
            steps += prefab.CarryWeight;
        }
        if (player != null)
        {
            foreach (ItemSlot slot in player.itemSlots)
            {
                Count($"slot {slot.itemSlotID}", slot.prefab);
            }
            BackpackSlot backpack = player.backpackSlot;
            if (!backpack.IsEmpty() && backpack.data.TryGetDataEntry(DataEntryKey.BackpackData, out BackpackData contents))
            {
                foreach (ItemSlot slot in contents.itemSlots)
                {
                    if (!slot.IsEmpty())
                    {
                        Count("in the backpack", slot.prefab);
                    }
                }
                if (ItemDatabase.TryGetItem(backpack.GetPrefabName(), out Item pack))
                {
                    Count("backpack", pack);
                }
            }
            ItemSlot temporary = player.GetItemSlot(250);
            if (temporary != null && !temporary.IsEmpty())
            {
                Count("hand slot 250", temporary.prefab);
            }
        }
        if (character.data.carriedPlayer != null)
        {
            weight.Add("carrying a scout 8");
            steps += 8;
        }
        foreach (StickyItemComponent stuck in StickyItemComponent.ALL_STUCK_ITEMS)
        {
            if (stuck != null && stuck.stuckToCharacter == character)
            {
                weight.Add($"stuck {stuck.name} {stuck.addWeightToStuckPlayer}");
                steps += stuck.addWeightToStuckPlayer;
            }
        }
        Item held = character.data.currentItem;
        return $"stamina {character.data.currentStamina * 100f:0.#} (+{character.data.extraStamina * 100f:0.#} extra), most {character.GetMaxStamina() * 100f:0.#}; "
            + $"statuses [{statuses}] sum {afflictions.statusSum * 100f:0.#}; "
            + $"weight {steps * 2.5f:0.#} from [{string.Join(", ", weight)}]{(Ascents.itemWeightModifier != 0 ? $" (ascent item weight {Ascents.itemWeightModifier:+0;-0} each)" : "")}; "
            + $"holding {(held != null ? held.name : "nothing")} (slot {character.refs.items.currentSelectedSlot.ValueOr(byte.MaxValue)}), sticky {(character.data.currentStickyItem != null ? character.data.currentStickyItem.name : "none")}, "
            + $"throw charge {character.refs.items.throwChargeLevel:0.##}";
    }
}
