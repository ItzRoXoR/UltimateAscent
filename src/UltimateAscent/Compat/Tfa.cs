using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Compat;

/// <summary>
/// True Final Ascent (CptPICHU) integration. TFA has no public API, so everything goes through
/// reflection and is guarded: if TFA changes, the integration switches itself off and the rest of
/// the mod keeps working.
/// <list type="bullet">
/// <item>FINAL ASCENT stacks every biome into the vanilla segments; TFA's stage tracking tells
/// where the scouts really are, and its biome offsets tell where the Caldera (and its lava tide)
/// was moved to.</item>
/// <item>Apocalypse mode: TFA decides how hard its own events are, so this mod's rules leave them
/// alone. What TFA's code does to a scout (<see cref="Causing"/>), what its events spawn
/// (<see cref="Spawned"/>) and, for what cannot be traced, the weather TFA is running
/// (<see cref="Weather"/>) are kept out of the rules that would change them.</item>
/// </list>
/// </summary>
internal static class Tfa
{
    internal const string Guid = "com.CptPICHU.peak.truefinalascent";

    private const string Ns = "TrueFinalAscent.";

    /// <summary>What TFA puts in the network data of the things its events spawn ("FA_BloodMoon", "FA_CactusRain", "FA_SUPPLY").</summary>
    private const string SpawnTag = "FA_";

    /// <summary>
    /// TFA components that TFA adds to the game's own objects, not to what its events spawn: the
    /// voice lines it gives every Mandrake. They do not make an object TFA's.
    /// </summary>
    private static readonly string[] OnGameObjects = { Ns + "FinalAscentMandrakeLine" };

    private static bool? present;
    private static bool broken;

    private static Assembly assembly;
    private static PropertyInfo finalAscentActive;
    private static MethodInfo placeCurrent;
    private static MethodInfo placeInRun;
    private static PropertyInfo apocalypseActive;
    private static FieldInfo builderOffsets;
    private static PropertyInfo weatherApplied;
    private static PropertyInfo iceFogDrawing;
    private static MethodInfo terrainComeBack;
    private static bool terrainFailed;

    internal static bool Present => present ??= Chainloader.PluginInfos.ContainsKey(Guid) && Bind();

    private static bool Bind()
    {
        try
        {
            Type finalAscent = AccessTools.TypeByName(Ns + "FinalAscent");
            Type place = AccessTools.TypeByName(Ns + "FinalAscentPlace");
            Type apocalypse = AccessTools.TypeByName(Ns + "FinalAscentApocalypse");
            Type builder = AccessTools.TypeByName(Ns + "FinalAscentBuilder");
            Type weather = AccessTools.TypeByName(Ns + "FinalAscentWeather");

            finalAscentActive = AccessTools.Property(finalAscent, "Active");
            placeCurrent = AccessTools.Method(place, "Current");
            placeInRun = AccessTools.Method(place, "InRun");
            apocalypseActive = AccessTools.Property(apocalypse, "Active");
            builderOffsets = AccessTools.Field(builder, "Offsets");
            weatherApplied = AccessTools.Property(weather, "Applied");
            // Optional: without it the fog colour simply does not check for TFA's Ice Fog.
            iceFogDrawing = AccessTools.Property(AccessTools.TypeByName(Ns + "FinalAscentIceFog"), "Drawing");
            // Optional: without it, leaving FINAL ASCENT by wrapping around leaves the TerrainRandomiser mod as TFA set it.
            Type terrain = AccessTools.TypeByName(Ns + "FinalAscentTerrainRandomiser");
            terrainComeBack = terrain != null ? AccessTools.Method(terrain, "ComeBack") : null;

            if (finalAscent == null || finalAscentActive == null || placeCurrent == null || placeInRun == null || apocalypseActive == null || builderOffsets == null || weatherApplied == null)
            {
                Plugin.Log.LogWarning("True Final Ascent was found, but not the parts Ultimate Ascent works with; TFA integration is off.");
                return false;
            }
            assembly = finalAscent.Assembly;
            Plugin.Log.LogInfo("True Final Ascent integration ready.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("True Final Ascent integration is off: " + ex.Message);
            return false;
        }
    }

    private static T Guard<T>(Func<T> call, T fallback)
    {
        if (broken || !Present)
        {
            return fallback;
        }
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            broken = true;
            Plugin.Log.LogWarning("True Final Ascent integration stopped after an error: " + ex.GetBaseException().Message);
            return fallback;
        }
    }

    private static int flagsFrame = -1;
    private static bool finalAscentCached;
    private static bool apocalypseCached;
    private static string weatherCached = "";

    private static void RefreshFlags()
    {
        int frame = Time.frameCount;
        if (frame == flagsFrame)
        {
            return;
        }
        flagsFrame = frame;
        finalAscentCached = Rules.InRun && Guard(() => (bool)finalAscentActive.GetValue(null), false);
        apocalypseCached = Rules.InRun && Guard(() => (bool)apocalypseActive.GetValue(null) && (bool)placeInRun.Invoke(null, null), false);
        weatherCached = apocalypseCached ? Guard(() => weatherApplied.GetValue(null) as string ?? "", "") : "";
    }

    /// <summary>A FINAL ASCENT climb is loaded.</summary>
    internal static bool FinalAscentActive
    {
        get
        {
            if (!Present || broken)
            {
                return false;
            }
            RefreshFlags();
            return finalAscentCached;
        }
    }

    /// <summary>TFA's Apocalypse weather replaces the vanilla weather in this climb.</summary>
    internal static bool ApocalypseActive
    {
        get
        {
            if (!Present || broken)
            {
                return false;
            }
            RefreshFlags();
            return apocalypseCached;
        }
    }

    // ---- Apocalypse mode: TFA's events are TFA's own ----

    /// <summary>The Apocalypse weather TFA is running right now ("" for none), e.g. "dark", "watch", "sleepfog", "antigrav".</summary>
    internal static string Weather
    {
        get
        {
            if (!Present || broken)
            {
                return "";
            }
            RefreshFlags();
            return weatherCached;
        }
    }

    /// <summary>An Apocalypse weather is running.</summary>
    internal static bool WeatherRunning => Weather.Length > 0;

    /// <summary>TFA's Blackout or Watch is driving the Scoutmaster.</summary>
    internal static bool ScoutmasterEvent
    {
        get
        {
            string weather = Weather;
            return weather == "dark" || weather == "watch";
        }
    }

    /// <summary>
    /// TFA's own code is doing this right now, in Apocalypse mode: one of its methods is on the call
    /// stack. That covers what its events do to the local scout on this game, including the knock-downs,
    /// pushes and faints it sends (the game runs a scout's own messages at once). Only asked where a rule
    /// would change something, since it reads the stack.
    /// </summary>
    internal static bool Causing
    {
        get
        {
            if (assembly == null || !ApocalypseActive)
            {
                return false;
            }
            StackFrame[] frames = new StackTrace(1, false).GetFrames();
            if (frames == null)
            {
                return false;
            }
            foreach (StackFrame frame in frames)
            {
                Type type = frame.GetMethod()?.DeclaringType;
                if (type != null && type.Assembly == assembly)
                {
                    return true;
                }
            }
            return false;
        }
    }

    private static ConditionalWeakTable<GameObject, StrongBox<bool>> spawned = new ConditionalWeakTable<GameObject, StrongBox<bool>>();

    /// <summary>
    /// Spawned by one of TFA's events, on every player's game: TFA tags what its events spawn over the
    /// network ("FA_..."), or builds it with components of its own (other than those it adds to the
    /// game's own objects, <see cref="OnGameObjects"/>). Looked up the object and its parents, never
    /// past a scout (TFA equips the scouts too); remembered per object.
    /// </summary>
    internal static bool Spawned(Component component)
    {
        if (assembly == null || broken || component == null)
        {
            return false;
        }
        GameObject self = component.gameObject;
        if (spawned.TryGetValue(self, out StrongBox<bool> known))
        {
            return known.Value;
        }
        bool result = false;
        for (Transform level = self.transform; level != null && !result; level = level.parent)
        {
            if (level.GetComponent<Character>() != null)
            {
                break;
            }
            foreach (MonoBehaviour behaviour in level.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null)
                {
                    continue;
                }
                if (behaviour is PhotonView view && view.InstantiationData is object[] data && data.Length > 0 && data[0] is string tag && tag.StartsWith(SpawnTag, StringComparison.Ordinal))
                {
                    result = true;
                    break;
                }
                if (behaviour.GetType().Assembly == assembly && Array.IndexOf(OnGameObjects, behaviour.GetType().FullName) < 0)
                {
                    result = true;
                    break;
                }
            }
        }
        spawned.Add(self, new StrongBox<bool>(result));
        return result;
    }

    /// <summary>
    /// TFA's Ice Fog is drawing its fog with the game's fog sphere: while it runs it takes the rising
    /// fog's sphere over, in its own colour and with its own cold.
    /// </summary>
    /// <summary>
    /// The boarding pass leaves FINAL ASCENT: TFA switches the TerrainRandomiser mod back on if it switched
    /// it off for FINAL ASCENT (its own left arrow does this; the wrap-around to Tenderfoot is this mod's).
    /// </summary>
    internal static void LeaveFinalAscent()
    {
        if (terrainFailed || terrainComeBack == null || !Present)
        {
            return;
        }
        try
        {
            terrainComeBack.Invoke(null, null);
        }
        catch (Exception ex)
        {
            terrainFailed = true;
            Plugin.Log.LogWarning("Could not hand the TerrainRandomiser mod back to True Final Ascent: " + ex.GetBaseException().Message);
        }
    }

    internal static bool IceFogDrawing => iceFogDrawing != null && Guard(() => (bool)iceFogDrawing.GetValue(null), false);

    // ---- FINAL ASCENT ----

    internal static bool TryGetFinalAscentPlace(out Place place)
    {
        place = Place.Unknown;
        if (!FinalAscentActive)
        {
            return false;
        }
        string name = Guard(() => placeCurrent.Invoke(null, null)?.ToString(), null);
        switch (name)
        {
            case "Kueste": place = Place.Shore; break;
            case "Tropen": place = Place.Tropics; break;
            case "Wurzeln": place = Place.Roots; break;
            case "Schnee": place = Place.Alpine; break;
            case "Mesa": place = Place.Mesa; break;
            case "Caldera": place = Place.Caldera; break;
            case "Kiln": place = Place.Kiln; break;
            case "Gloom": place = Place.Gloom; break;
            case "Citadel": place = Place.Citadel; break;
            case "Peak": place = Place.Peak; break;
            case "Nadir": place = Place.Nadir; break;
            default: return false;
        }
        return true;
    }

    private static bool TryGetVolcanoOffset(out Vector3 offset)
    {
        offset = Vector3.zero;
        if (!FinalAscentActive)
        {
            return false;
        }
        Vector3? found = Guard<Vector3?>(() => builderOffsets.GetValue(null) is IDictionary offsets && offsets.Contains(Biome.BiomeType.Volcano) ? (Vector3)offsets[Biome.BiomeType.Volcano] : (Vector3?)null, null);
        if (!found.HasValue)
        {
            return false;
        }
        offset = found.Value;
        return true;
    }

    /// <summary>The middle of the Caldera lava tide on a FINAL ASCENT map.</summary>
    internal static bool TryGetTideCenter(float regularCenter, out float center)
    {
        center = regularCenter;
        if (!TryGetVolcanoOffset(out Vector3 offset))
        {
            return false;
        }
        center = regularCenter + offset.y;
        return true;
    }

    internal static void Reset()
    {
        spawned = new ConditionalWeakTable<GameObject, StrongBox<bool>>();
    }
}
