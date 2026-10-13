using System;
using System.Collections.Generic;
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
/// 30-90 s between storms (snow pauses 60-120 s in the Gloom).</item>
/// <item>Extra tornadoes roam near the scouts (see <see cref="ExtraTornadoes"/>).</item>
/// <item>The Kiln/Citadel and everything after it keep their normal weather.</item>
/// <item>Weather stays calm for 30 s after waking up (see <see cref="WeatherGrace"/>).</item>
/// <item>With True Final Ascent's Apocalypse mode the vanilla weather is TFA's business: instead,
/// two Apocalypse events run at once (see <see cref="Compat.Tfa"/>), plus the extra tornadoes.</item>
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

    /// <summary>
    /// The Gloom gets snowstorms on top of its sleep fog at ascent 25, so its sleep fog keeps the
    /// strength it has without ascent 15's drowsiness boost (<see cref="GloomFogScale"/>) and its slow
    /// drowsiness recovery is doubled.
    /// </summary>
    internal static bool GloomRelief => On && Map.InGloom;

    /// <summary>What the Gloom's sleep fog is scaled by, so ascent 15's boost leaves it as it was.</summary>
    internal static float GloomFogScale => GloomRelief ? 1f / A15_StrongerAilments.Factor(CharacterAfflictions.STATUSTYPE.Drowsy) : 1f;

    /// <summary>The Kiln/Citadel, the Peak and the Nadir keep their normal weather.</summary>
    internal static bool Excluded => On && Map.Ready && !Map.InLowerMountain;

    /// <summary>True while this mod drives the vanilla storm zones.</summary>
    internal static bool DrivesVanillaWeather => On && !Excluded && !Compat.Tfa.ApocalypseActive;

    internal static bool IsCreated(WindChillZone zone) => zone != null && created.Contains(zone);

    /// <summary>Returns true when the zone must stay calm right now (grace period or excluded segment).</summary>
    internal static bool SuppressZone(WindChillZone zone)
    {
        if (zone == null || !On || Compat.Tfa.ApocalypseActive)
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
            Overrides.Set(zone, nameof(WindChillZone.windTimeRangeOff), new Vector2(30f, 90f));
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

    /// <summary>The game's own storm toggles would fight the shared phases; ignore them while we drive the zone.</summary>
    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.RPCA_ToggleWind))]
    private static class ZoneToggle
    {
        private static bool Prefix(WindChillZone __instance)
        {
            return !SuppressZone(__instance) && !WeatherPhases.Handles(__instance);
        }
    }

    /// <summary>The Gloom's own sleep fog, relieved like the chasing fog (see <see cref="GloomFogScale"/>).</summary>
    [HarmonyPatch(typeof(global::Peak.StatusFieldGloom), nameof(global::Peak.StatusFieldGloom.TickStatus))]
    private static class GloomFogRelief
    {
        private static void Prefix(global::Peak.StatusFieldGloom __instance, out float? __state)
        {
            __state = null;
            if (GloomRelief)
            {
                __state = __instance.statusAmountPerSecond;
                __instance.statusAmountPerSecond *= GloomFogScale;
            }
        }

        private static Exception Finalizer(global::Peak.StatusFieldGloom __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.statusAmountPerSecond = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>In the Gloom the snowstorm pauses longer (60-120 s).</summary>
    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.GetNextWindTime))]
    private static class GloomSnowRest
    {
        private static void Prefix(WindChillZone __instance, bool windActive, out Vector2? __state)
        {
            __state = null;
            if (!windActive && __instance.name == "SnowStorm" && GloomRelief)
            {
                __state = __instance.windTimeRangeOff;
                __instance.windTimeRangeOff = new Vector2(60f, 120f);
            }
        }

        private static Exception Finalizer(WindChillZone __instance, Vector2? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.windTimeRangeOff = __state.Value;
            }
            return __exception;
        }
    }
}
