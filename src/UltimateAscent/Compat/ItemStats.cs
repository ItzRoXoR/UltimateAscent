using System;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace UltimateAscent.Compat;

/// <summary>
/// Item Stats (boxofbiscuits97) lists the held item's numbers next to the inventory when a slot is
/// drawn, reading the item's own fields in its prefix on <see cref="InventoryItemUI.SetItem"/>. This
/// prefix runs before it and puts this ascent's numbers in place (<see cref="ItemFigures"/>), the
/// weight included; the finalizer puts the item's own back after it.
/// </summary>
internal static class ItemStats
{
    internal const string Guid = "com.github.boxofbiscuits97.ItemStats";

    private static bool? present;

    internal static bool Present => present ??= Chainloader.PluginInfos.ContainsKey(Guid);

    [HarmonyPatch(typeof(InventoryItemUI), nameof(InventoryItemUI.SetItem))]
    private static class HeldItemNumbers
    {
        private static bool Prepare() => Present;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(out bool __state)
        {
            Character local = Character.localCharacter;
            ItemFigures.Begin(local != null ? local.data.currentItem : null, weight: true);
            __state = true;
        }

        private static Exception Finalizer(bool __state, Exception __exception)
        {
            if (__state)
            {
                ItemFigures.End();
            }
            return __exception;
        }
    }
}
