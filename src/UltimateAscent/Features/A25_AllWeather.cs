using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UltimateAscent.Features.Weather;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 25 (13DDA): all weather is present everywhere, at slightly less effect.
/// <list type="bullet">
/// <item>Every run has rain, wind and snowstorms, whichever biome variants the map rolled. Each daily
/// map only contains the storms of its own variants (Tropics: rain, Roots: wind, Alpine: snow), which
/// is why 13DDA only ever had one of rain or wind. Storms a map has are woken up and moved out of
/// their biome so they run everywhere; storms a map lacks are rebuilt (see <see cref="StormFactory"/>).</item>
/// <item>Every storm covers the whole mountain and makes the ground slippery; wind and snow pause
/// 30-90 s between storms. Outside the Alpine the snowstorm is gentler (see A18_ExtremeWeather's storms,
/// which apply it with or without ascent 18).</item>
/// <item>Extra tornadoes roam near the scouts (see <see cref="ExtraTornadoes"/>).</item>
/// <item>The Kiln/Citadel and everything after it keep their normal weather.</item>
/// <item>Weather stays calm for 30 s after waking up (see <see cref="WeatherGrace"/>).</item>
/// <item>With True Final Ascent's Apocalypse mode, TFA's weather events run as TFA runs them; between
/// them this ascent's rain, wind and snowstorms run as above (TFA would keep the game's storms quiet the
/// whole time, so its hush steps aside then). The extra tornadoes roam throughout.</item>
/// </list>
/// </summary>
internal static class A25_AllWeather
{
    private sealed class Marker
    {
    }

    private sealed class Detached
    {
        internal WindChillZone zone;
        internal Transform parent;
        internal bool wasActive;
    }

    private static readonly StormKind[] Kinds = { StormKind.Rain, StormKind.Wind, StormKind.Snow };

    private static ConditionalWeakTable<WindChillZone, Marker> covered = new ConditionalWeakTable<WindChillZone, Marker>();
    private static readonly List<Detached> detached = new List<Detached>();
    private static readonly List<WindChillZone> created = new List<WindChillZone>();
    private static MapHandler inspectedMap;
    private static float nextEnsure;

    private static bool On => Rules.Enabled(Lvl.AllWeather);

    /// <summary>The Kiln/Citadel, the Peak and the Nadir keep their normal weather.</summary>
    internal static bool Excluded => On && Map.Ready && !Map.InLowerMountain;

    /// <summary>True while this mod drives the vanilla storm zones (with TFA's Apocalypse: between its weather events).</summary>
    internal static bool DrivesVanillaWeather => On && !Excluded && !TfaWeather;

    /// <summary>TFA's Apocalypse is running one of its weather events: the weather is TFA's alone.</summary>
    private static bool TfaWeather => Compat.Tfa.ApocalypseActive && Compat.Tfa.WeatherRunning;

    /// <summary>With TFA's Apocalypse, between its weather events: this ascent's storms run, and TFA's hush of the game's storms stands aside.</summary>
    internal static bool RunsInApocalypseBreak => DrivesVanillaWeather && Compat.Tfa.ApocalypseActive;

    internal static bool IsCreated(WindChillZone zone) => zone != null && created.Contains(zone);

    /// <summary>Returns true when the zone must stay calm right now (grace period or excluded segment).</summary>
    internal static bool SuppressZone(WindChillZone zone)
    {
        if (zone == null || !On || TfaWeather)
        {
            return false;
        }
        if (WeatherGrace.Suppress(zone))
        {
            return true;
        }
        if (!Excluded)
        {
            return false;
        }
        WeatherGrace.Calm(zone);
        return true;
    }

    private static void Cover(WindChillZone zone)
    {
        if (!DrivesVanillaWeather || zone == null || covered.TryGetValue(zone, out _))
        {
            return;
        }
        covered.Add(zone, new Marker());
        Bounds bounds = zone.windZoneBounds;
        // 13DDA grew the zones by 3 km; FINAL ASCENT maps are taller, so never less than 10 km.
        bounds.size = Vector3.Max(bounds.size + Vector3.one * 3000f, Vector3.one * 10000f);
        Overrides.Set(zone, nameof(WindChillZone.windZoneBounds), bounds);
        Overrides.Set(zone, nameof(WindChillZone.setSlippy), true);
        if (zone.name == "WindStorm" || zone.name == "SnowStorm")
        {
            // Pauses of at most 30-90 s; a storm that already paused less (the Alpine's snow: 20-90 s) keeps that.
            Vector2 pause = zone.windTimeRangeOff;
            Overrides.Set(zone, nameof(WindChillZone.windTimeRangeOff), new Vector2(Mathf.Min(pause.x, 30f), Mathf.Min(pause.y, 90f)));
        }
        Plugin.Log.LogInfo("Ascent 25: " + zone.name + " now covers the whole mountain.");
    }

    internal static void Reset()
    {
        foreach (WindChillZone zone in created)
        {
            if (zone != null)
            {
                zone.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(zone.gameObject);
            }
        }
        created.Clear();
        foreach (Detached entry in detached)
        {
            if (entry.zone != null && entry.parent != null)
            {
                entry.zone.transform.SetParent(entry.parent, worldPositionStays: true);
                entry.zone.gameObject.SetActive(entry.wasActive);
            }
        }
        detached.Clear();
        inspectedMap = null;
        nextEnsure = 0f;
        covered = new ConditionalWeakTable<WindChillZone, Marker>();
    }

    /// <summary>Called every frame: makes sure rain, wind and snow all exist and run (checked about once a second).</summary>
    internal static void Tick()
    {
        if (!DrivesVanillaWeather || Time.unscaledTime < nextEnsure || !MapHandler.ExistsAndInitialized)
        {
            return;
        }
        nextEnsure = Time.unscaledTime + 1f;
        MapHandler map = Singleton<MapHandler>.Instance;
        if (inspectedMap == map)
        {
            foreach (Detached entry in detached)
            {
                // A biome switching itself off later must not take its storm along.
                if (entry.zone != null && !entry.zone.gameObject.activeInHierarchy)
                {
                    entry.zone.gameObject.SetActive(true);
                }
            }
            return;
        }
        Reset();
        inspectedMap = map;
        nextEnsure = Time.unscaledTime + 1f;
        WindChillZone[] zones = map.GetComponentsInChildren<WindChillZone>(includeInactive: true);
        foreach (StormKind kind in Kinds)
        {
            string name = StormFactory.ZoneName(kind);
            WindChillZone native = Array.Find(zones, z => z != null && z.name == name);
            if (native != null)
            {
                Wake(map, native);
                continue;
            }
            try
            {
                WindChillZone zone = StormFactory.Create(kind, map.transform);
                if (zone != null)
                {
                    created.Add(zone);
                    zone.gameObject.SetActive(true);
                    Cover(zone);
                    Plugin.Log.LogInfo($"Ascent 25: this map has no {name}, so one was added.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Ascent 25: could not add the {name}: {ex}");
            }
        }
    }

    /// <summary>Storms sleep with their biome until the scouts get there; move them to the map root so they run everywhere.</summary>
    private static void Wake(MapHandler map, WindChillZone zone)
    {
        if (zone.transform.parent != map.transform)
        {
            detached.Add(new Detached { zone = zone, parent = zone.transform.parent, wasActive = zone.gameObject.activeSelf });
            zone.transform.SetParent(map.transform, worldPositionStays: true);
        }
        if (!zone.gameObject.activeSelf)
        {
            zone.gameObject.SetActive(true);
        }
        Cover(zone);
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.Awake))]
    private static class ZoneAwake
    {
        private static void Postfix(WindChillZone __instance)
        {
            Cover(__instance);
        }
    }

    /// <summary>Rebuilt storms have no network view; their timing is handled by <see cref="WeatherPhases"/>.</summary>
    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.Start))]
    private static class ZoneStart
    {
        private static bool Prefix(WindChillZone __instance)
        {
            return !IsCreated(__instance);
        }
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.Update))]
    private static class ZoneUpdate
    {
        private static bool Prefix(WindChillZone __instance)
        {
            Cover(__instance);
            return !SuppressZone(__instance);
        }
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.HandleTime))]
    private static class ZoneTiming
    {
        private static bool Prefix(WindChillZone __instance)
        {
            if (SuppressZone(__instance))
            {
                return false;
            }
            if (WeatherPhases.Handles(__instance))
            {
                WeatherPhases.Apply(__instance);
                return false;
            }
            if (IsCreated(__instance))
            {
                // Without this mod driving it (e.g. Apocalypse switched on), a rebuilt storm stays calm.
                WeatherGrace.Calm(__instance);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.FixedUpdate))]
    private static class ZonePhysics
    {
        private static bool Prefix(WindChillZone __instance)
        {
            if (SuppressZone(__instance))
            {
                return false;
            }
            if (WeatherPhases.Handles(__instance))
            {
                WeatherPhases.Apply(__instance);
            }
            return true;
        }
    }

    /// <summary>
    /// With TFA's Apocalypse, TFA keeps every one of the game's storms quiet ("only one weather"): it
    /// skips their clock, statuses, push and updates while this says so, and switches off any that blows
    /// (<see cref="TfaSweep"/>). Between its weather events this ascent's storms run instead.
    /// </summary>
    [HarmonyPatch]
    private static class TfaHush
    {
        private static MethodBase TargetMethod()
        {
            Type quiet = Compat.Tfa.Present ? AccessTools.TypeByName("TrueFinalAscent.FinalAscentWindStormQuiet") : null;
            return quiet != null ? AccessTools.PropertyGetter(quiet, "Hushed") : null;
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(ref bool __result)
        {
            if (__result && RunsInApocalypseBreak)
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    private static class TfaSweep
    {
        private static MethodBase TargetMethod()
        {
            Type storm = Compat.Tfa.Present ? AccessTools.TypeByName("TrueFinalAscent.FinalAscentWindStorm") : null;
            return storm != null ? AccessTools.Method(storm, "HushVanilla") : null;
        }

        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix()
        {
            return !RunsInApocalypseBreak;
        }
    }

    /// <summary>The game's own storm toggles would fight the shared phases; ignore them while we drive the zone.</summary>
    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.RPCA_ToggleWind))]
    private static class ZoneToggle
    {
        private static bool Prefix(WindChillZone __instance)
        {
            return !SuppressZone(__instance) && !WeatherPhases.Handles(__instance);
        }
    }

}
