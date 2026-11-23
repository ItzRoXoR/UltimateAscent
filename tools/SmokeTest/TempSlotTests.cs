using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>The numbers Item Stats shows for an item in the temporary (fourth) slot, on the Shore and in the next biome ("tempslot").</summary>
internal sealed partial class Runner
{
    private IEnumerator TempSlotCheck()
    {
        if (!UltimateAscent.Compat.ItemStats.Present)
        {
            Info("the temporary slot check needs Item Stats", "run with -Clean -WithMods boxofbiscuits97-ItemStats");
            yield break;
        }
        // Where the climb is now (the beach, or further on after other steps), then the Tropics or the Roots.
        yield return TempSlotLook($"the {Map.CurrentPlace}");
        // The mod's items Item Stats reads in the hand: the Wonderberry (a sugar rush without a crash, which made Item
        // Stats throw) and the Climbing Chalk (an effect of its own).
        foreach (Item mine in new[] { ItemByName(UltimateAscent.Features.WildFoods.WonderberryPrefab), ItemByName(UltimateAscent.Features.ClimbingChalkItem.PrefabName) })
        {
            if (mine != null)
            {
                yield return TempSlotLook($"the {Map.CurrentPlace}", mine);
            }
        }
        if (Singleton<MapHandler>.Instance.currentSegment != (int)Segment.Tropics)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(10f);
            yield return TempSlotLook($"the {Map.CurrentPlace}");
        }
        Check("no inventory redraw failed with Item Stats on, and the temporary slot's numbers were drawn all along",
            UltimateAscent.Compat.ItemStats.Failures == 0 && UltimateAscent.Compat.ItemStats.Faults == 0,
            $"redraws failed {UltimateAscent.Compat.ItemStats.Failures}, numbers not drawn {UltimateAscent.Compat.ItemStats.Faults}");
        yield return TempSlotHiddenName();
    }

    /// <summary>
    /// The player's bug: Item Stats builds a slot's numbers for the first time while that part of the HUD is hidden (its
    /// copy of the slot's name is made inactive, so TextMeshPro has not set it up: its outline throws, and the copy, off
    /// like the name it was copied from, is never switched on). Item Stats' numbers taken off the temporary slot and its
    /// part of the HUD hidden while it rebuilds: without the mod's repair the numbers stay invisible; with it, the next
    /// rebuild switches them on; and built again with the repair on from the start they show at once.
    /// </summary>
    private IEnumerator TempSlotHiddenName()
    {
        Character local = Local;
        Item food = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values
            .Where(i => i != null && i.UIData != null && i.UIData.canPocket && i.GetComponent<Action_RestoreHunger>() is Action_RestoreHunger r && r.restorationAmount > 0f
                        && i.GetComponent<Action_GiveExtraStamina>() is Action_GiveExtraStamina s && s.amount > 0f)
            .OrderBy(i => i.itemID).FirstOrDefault();
        InventoryItemUI tempUi = GUIManager.instance.temporaryItem;
        if (food == null || tempUi == null || tempUi.fuelBar == null)
        {
            Fail("a food and the temporary slot for the hidden-name check");
            yield break;
        }
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        foreach (byte slot in new byte[] { 0, 1, 2, 250 })
        {
            if (!local.player.GetItemSlot(slot).IsEmpty())
            {
                local.player.EmptySlot(Optionable<byte>.Some(slot), true);
            }
        }
        yield return new WaitForSeconds(0.3f);
        for (int i = 0; i < 4; i++)
        {
            local.player.AddItem(food.itemID, null, out _);
        }
        yield return new WaitForSeconds(0.3f);
        local.refs.items.EquipSlot(Optionable<byte>.Some((byte)250));
        // Some hunger, so the bar's hunger icon (which Item Stats copies its first icon from) is there.
        Unlocked(() => Afflictions.SetStatus(CharacterAfflictions.STATUSTYPE.Hunger, 0.2f));
        yield return new WaitForSeconds(1f);

        bool savedRepair = UltimateAscent.Compat.ItemStats.RepairNumbers;
        int failuresBefore = UltimateAscent.Compat.ItemStats.Failures;
        try
        {
            // 1. Without the repair: built anew with the name hidden.
            UltimateAscent.Compat.ItemStats.RepairNumbers = false;
            yield return RebuildWhileHidden(tempUi);
            var broken = SlotStats(tempUi).Where(s => s.active).ToList();
            int failed = UltimateAscent.Compat.ItemStats.Failures - failuresBefore;
            Check("the bug reproduces: Item Stats building the temporary slot's numbers while that part of the HUD is hidden throws and leaves them invisible (only the icons show)",
                broken.Count > 0 && broken.All(s => !s.drawn) && failed > 0,
                $"{string.Join("; ", broken.Select(s => $"{s.name} '{s.text}' drawn {s.drawn}"))}; redraws that threw {failed}");

            // 2. The repair switched on, nothing rebuilt by hand: the next rebuild switches the numbers on (a game already in this state).
            int repairedBefore = UltimateAscent.Compat.ItemStats.Repaired;
            UltimateAscent.Compat.ItemStats.RepairNumbers = true;
            yield return new WaitForSeconds(1.2f);
            var mended = SlotStats(tempUi).Where(s => s.active).ToList();
            Check("with the repair, the next rebuild switches the invisible numbers on (no restart needed)",
                mended.Count > 0 && mended.All(s => s.drawn && s.text.Length > 0) && UltimateAscent.Compat.ItemStats.Repaired > repairedBefore,
                $"{string.Join("; ", mended.Select(s => $"{s.name} '{s.text}' drawn {s.drawn}"))}; numbers switched on {UltimateAscent.Compat.ItemStats.Repaired - repairedBefore}");

            // 3. Built anew with the name hidden, the repair on from the start: shown at once, the throw caught, the inventory drawn on.
            int caughtBefore = UltimateAscent.Compat.ItemStats.Caught;
            int failuresMid = UltimateAscent.Compat.ItemStats.Failures;
            yield return RebuildWhileHidden(tempUi);
            var fresh = SlotStats(tempUi).Where(s => s.active).ToList();
            Check("built anew while hidden and with the repair on, the temporary slot's numbers show, Item Stats' throw caught and the inventory drawn on",
                fresh.Count > 0 && fresh.All(s => s.drawn && s.text.Length > 0) && UltimateAscent.Compat.ItemStats.Failures == failuresMid,
                $"{string.Join("; ", fresh.Select(s => $"{s.name} '{s.text}' drawn {s.drawn}"))}; throws caught {UltimateAscent.Compat.ItemStats.Caught - caughtBefore}, redraws that failed {UltimateAscent.Compat.ItemStats.Failures - failuresMid}");
            Screenshot("tempslot-hidden-name-repaired");
        }
        finally
        {
            UltimateAscent.Compat.ItemStats.RepairNumbers = savedRepair;
            // The failures of step 1 were made on purpose.
            UltimateAscent.Compat.ItemStats.Failures = failuresBefore;
        }
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        foreach (byte slot in new byte[] { 0, 1, 2, 250 })
        {
            if (!local.player.GetItemSlot(slot).IsEmpty())
            {
                local.player.EmptySlot(Optionable<byte>.Some(slot), true);
            }
        }
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>
    /// Item Stats' numbers taken off the slot and the slot's part of the HUD hidden while it rebuilds them (the game keeps
    /// drawing the slot), then shown again and a moment for the numbers.
    /// </summary>
    private IEnumerator RebuildWhileHidden(InventoryItemUI slot)
    {
        Transform holder = slot.fuelBar.transform.parent;
        foreach (Transform child in holder.Cast<Transform>().Where(c => c.name.EndsWith("Stat")).ToList())
        {
            Object.DestroyImmediate(child.gameObject);
        }
        // The game switches the temporary slot itself on every frame it holds something: hide what it sits in.
        Transform hide = holder == slot.transform || slot.transform.IsChildOf(holder) ? slot.transform.parent : holder;
        string path = string.Join("/", hide.GetComponentsInParent<Transform>(true).Reverse().Select(p => p.name));
        hide.gameObject.SetActive(false);
        try
        {
            yield return new WaitForSecondsRealtime(0.9f);
        }
        finally
        {
            hide.gameObject.SetActive(true);
        }
        Info("the temporary slot's part of the HUD hidden while Item Stats rebuilt it", path);
        yield return new WaitForSeconds(1.2f);
    }

    /// <summary>Item Stats' numbers on a slot: each stat shown, its text and what draws it.</summary>
    private static List<(string name, bool active, string text, string how, bool drawn)> SlotStats(InventoryItemUI slot)
    {
        List<(string, bool, string, string, bool)> stats = new List<(string, bool, string, string, bool)>();
        Transform holder = slot != null && slot.fuelBar != null ? slot.fuelBar.transform.parent : null;
        if (holder == null)
        {
            return stats;
        }
        foreach (Transform child in holder)
        {
            if (!child.name.EndsWith("Stat"))
            {
                continue;
            }
            TextMeshProUGUI text = child.childCount > 0 ? child.GetChild(0).GetComponent<TextMeshProUGUI>() : null;
            if (text == null)
            {
                stats.Add((child.name, child.gameObject.activeInHierarchy, "(no text)", "", false));
                continue;
            }
            text.ForceMeshUpdate();
            bool drawn = text.isActiveAndEnabled && text.textInfo.characterCount > 0 && text.color.a > 0.2f && text.canvasRenderer.GetAlpha() > 0.2f && text.rectTransform.lossyScale.x > 0.01f;
            string how = $"enabled {text.enabled}, active {text.gameObject.activeInHierarchy}, colour {text.color}, renderer alpha {text.canvasRenderer.GetAlpha():0.##}, size {text.fontSize}, "
                + $"characters {text.textInfo.characterCount}, font {(text.font != null ? text.font.name : "none")}, material {(text.fontSharedMaterial != null ? text.fontSharedMaterial.name : "none")}, "
                + $"scale {text.rectTransform.lossyScale.x:0.###}, at {text.rectTransform.localPosition:F0}, rect {text.rectTransform.rect.size:F0}, culled {text.canvasRenderer.cull}, mask {text.maskable}";
            stats.Add((child.name, child.gameObject.activeInHierarchy, text.text, how, drawn));
        }
        return stats;
    }

    private IEnumerator TempSlotLook(string where, Item food = null)
    {
        Character local = Local;
        int failuresBefore = UltimateAscent.Compat.ItemStats.Failures;
        food ??= SingletonAsset<ItemDatabase>.Instance.itemLookup.Values
            .Where(i => i != null && i.UIData != null && i.UIData.canPocket && i.GetComponent<Action_RestoreHunger>() is Action_RestoreHunger r && r.restorationAmount > 0f
                        && i.GetComponent<Action_GiveExtraStamina>() is Action_GiveExtraStamina s && s.amount > 0f)
            .OrderBy(i => i.itemID).FirstOrDefault();
        if (food == null)
        {
            Fail("a food with hunger and extra stamina for the temporary slot");
            yield break;
        }
        // Pockets emptied, then filled with the food; one more goes to the temporary slot, taken in the hand.
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        foreach (byte slot in new byte[] { 0, 1, 2, 250 })
        {
            if (!local.player.GetItemSlot(slot).IsEmpty())
            {
                local.player.EmptySlot(Optionable<byte>.Some(slot), true);
            }
        }
        yield return new WaitForSeconds(0.3f);
        for (int i = 0; i < 3; i++)
        {
            local.player.AddItem(food.itemID, null, out _);
        }
        yield return new WaitForSeconds(0.3f);
        bool added = local.player.AddItem(food.itemID, null, out ItemSlot temp);
        yield return new WaitForSeconds(0.3f);
        local.refs.items.EquipSlot(Optionable<byte>.Some((byte)250));
        yield return new WaitForSeconds(1.5f);
        InventoryItemUI tempUi = GUIManager.instance.temporaryItem;
        Info($"the temporary slot in {where}", $"{food.gameObject.name} added {added} into slot {(temp != null ? temp.itemSlotID.ToString() : "-")}; in the hand {(local.data.currentItem != null ? local.data.currentItem.gameObject.name : "nothing")}; "
            + $"slot shown {tempUi != null && tempUi.gameObject.activeInHierarchy}, its name '{(tempUi != null && tempUi.nameText != null ? tempUi.nameText.text : "")}' (shown {tempUi != null && tempUi.nameText != null && tempUi.nameText.enabled})");
        Screenshot($"tempslot-{where.Replace(' ', '-')}");
        var stats = SlotStats(tempUi);
        foreach (var stat in stats.Where(s => s.active))
        {
            Info($"Item Stats on the temporary slot in {where}: {stat.name}", $"'{stat.text}' drawn {stat.drawn}; {stat.how}");
        }
        Check($"Item Stats shows the numbers of {food.gameObject.name} in the temporary slot in {where}, drawing the slots without failing",
            stats.Any(s => s.active) && stats.Where(s => s.active).All(s => s.drawn && s.text.Length > 0) && UltimateAscent.Compat.ItemStats.Failures == failuresBefore,
            string.Join("; ", stats.Where(s => s.active).Select(s => $"{s.name} '{s.text}' drawn {s.drawn}")) + $"; redraws failed {UltimateAscent.Compat.ItemStats.Failures - failuresBefore}");
        // And on a pocket, for comparison.
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        local.player.EmptySlot(Optionable<byte>.Some((byte)250), true);
        yield return new WaitForSeconds(0.3f);
        local.refs.items.EquipSlot(Optionable<byte>.Some((byte)0));
        yield return new WaitForSeconds(1.5f);
        var pocket = SlotStats(GUIManager.instance.items[0]);
        Info($"Item Stats on the first pocket in {where}", string.Join("; ", pocket.Where(s => s.active).Select(s => $"{s.name} '{s.text}' drawn {s.drawn}; {s.how}")));
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        foreach (byte slot in new byte[] { 0, 1, 2 })
        {
            local.player.EmptySlot(Optionable<byte>.Some(slot), true);
        }
        yield return new WaitForSeconds(0.5f);
    }
}
