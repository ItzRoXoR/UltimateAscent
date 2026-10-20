using System.Collections.Generic;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The config's "New Items" switch (the host's): on, the mod's new items turn up in regular runs on every
/// ascent (<see cref="Available"/>); off, they stay out of regular runs,
/// as when their switches are off on the Custom Run screen (custom runs keep those switches): the
/// Climbing Chalk does not spawn, and wherever the game would spawn the Roots parasol (the
/// Lightweight Parasol) the regular Parasol spawns instead, with the same odds. The
/// newer items check the switch themselves (<see cref="AutoParachute"/>, <see cref="WarpCompass"/>, <see cref="VanillaWafers"/>,
/// <see cref="WildFoods"/>, <see cref="GroundItems"/>). The wild ones never spawn in suitcases, so the
/// Custom Run screen has no switch for them: in custom runs too this switch is theirs (<see cref="WildOn"/>).
/// </summary>
internal static class NewItems
{
    private const string ParasolPrefab = "Parasol";

    /// <summary>A climb that is not a custom run, on any ascent.</summary>
    internal static bool RegularRun => Plugin.Ready && Rules.InRun && !RunSettings.IsCustomRun;

    /// <summary>The new items may spawn: in custom runs (their own switches on the Custom Run screen decide), and in regular runs on every ascent with the switch on.</summary>
    internal static bool Available => RunSettings.IsCustomRun || (RegularRun && HostOptions.NewItemsOn);

    /// <summary>Switched off, in a regular run.</summary>
    private static bool Off => RegularRun && !HostOptions.NewItemsOn;

    /// <summary>The wild new items (the wild foods, the Stone and the Stick) are out: in every climb, unless the switch is off.</summary>
    internal static bool WildOn => HostOptions.NewItemsOn && Plugin.Ready && Rules.InRun;

    /// <summary>Spawning checks every candidate here first (luggage and ground spawns alike).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.IsValidToSpawn))]
    private static class NoChalk
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result && Off && ClimbingChalkItem.IsChalk(__instance))
            {
                __result = false;
            }
        }
    }

    /// <summary>Every suitcase and spawner picks what it spawns here (from its pool, or its one item).</summary>
    [HarmonyPatch(typeof(Spawner), nameof(Spawner.GetObjectsToSpawn))]
    private static class ParasolInstead
    {
        private static void Postfix(List<GameObject> __result)
        {
            if (__result == null || !Off)
            {
                return;
            }
            for (int i = 0; i < __result.Count; i++)
            {
                GameObject chosen = __result[i];
                if (chosen != null && A14_Items.IsRootsParasol(chosen.GetComponent<Item>()) && ItemDatabase.TryGetItem(ParasolPrefab, out Item parasol) && parasol != null)
                {
                    __result[i] = parasol.gameObject;
                }
            }
        }
    }
}
