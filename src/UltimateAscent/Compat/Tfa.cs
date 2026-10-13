using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
/// <item>Ascent 25 with Apocalypse mode: whenever TFA runs an Apocalypse weather, a second one
/// (picked the way TFA picks, never the same kind) runs at the same time.</item>
/// </list>
/// </summary>
internal static class Tfa
{
    internal const string Guid = "com.CptPICHU.peak.truefinalascent";

    private const string Ns = "TrueFinalAscent.";
    private const string SecondaryKey = Net.Prefix + "apo2";
    private const string SecondaryOwnerKey = Net.Prefix + "apo2s";

    /// <summary>Weather keys never used as the second weather (they manage the primary slot themselves).</summary>
    private static readonly HashSet<string> NeverSecondary = new HashSet<string> { "wisps", "foodfrenzy", "sleepfog", "flood" };

    /// <summary>These share TFA's storm shader state; two of them at once would fight visually.</summary>
    private static readonly HashSet<string> StormFamily = new HashSet<string> { "storm", "rain", "snow", "sand" };

    private static bool? present;
    private static bool broken;

    private static PropertyInfo finalAscentActive;
    private static MethodInfo placeCurrent;
    private static MethodInfo placeIsDay;
    private static MethodInfo placeInRun;
    private static PropertyInfo apocalypseActive;
    private static FieldInfo builderOffsets;
    private static MethodInfo weatherFind;
    private static FieldInfo effectSet;
    private static MethodInfo weatherAnnounce;
    private static MethodInfo bookOf;
    private static MethodInfo cycleRoll;
    private static FieldInfo rollEntry;
    private static FieldInfo entryKey;
    private static Type placeType;

    private static string appliedSecondary = "";
    private static int hostPrimarySeq = -1;
    private static string hostSecondary = "";
    private static float nextTick;

    internal static bool Present => present ??= Chainloader.PluginInfos.ContainsKey(Guid) && Bind();

    internal static string CurrentSecondary => appliedSecondary;

    private static bool Bind()
    {
        try
        {
            Type finalAscent = AccessTools.TypeByName(Ns + "FinalAscent");
            Type place = AccessTools.TypeByName(Ns + "FinalAscentPlace");
            Type apocalypse = AccessTools.TypeByName(Ns + "FinalAscentApocalypse");
            Type builder = AccessTools.TypeByName(Ns + "FinalAscentBuilder");
            Type weather = AccessTools.TypeByName(Ns + "FinalAscentWeather");
            Type effect = AccessTools.TypeByName(Ns + "WeatherEffect");
            Type entry = AccessTools.TypeByName(Ns + "WeatherEntry");
            Type book = AccessTools.TypeByName(Ns + "FinalAscentWeatherBook");
            Type cycle = AccessTools.TypeByName(Ns + "FinalAscentWeatherCycle");
            Type roll = AccessTools.TypeByName(Ns + "WeatherRoll");
            placeType = AccessTools.TypeByName(Ns + "Place");

            finalAscentActive = AccessTools.Property(finalAscent, "Active");
            placeCurrent = AccessTools.Method(place, "Current");
            placeIsDay = AccessTools.Method(place, "IsDay");
            placeInRun = AccessTools.Method(place, "InRun");
            apocalypseActive = AccessTools.Property(apocalypse, "Active");
            builderOffsets = AccessTools.Field(builder, "Offsets");
            weatherFind = AccessTools.Method(weather, "Find");
            weatherAnnounce = AccessTools.Method(weather, "Announce");
            effectSet = AccessTools.Field(effect, "Set");
            entryKey = AccessTools.Field(entry, "Key");
            bookOf = AccessTools.Method(book, "Of");
            cycleRoll = AccessTools.Method(cycle, "Roll");
            rollEntry = AccessTools.Field(roll, "Entry");

            if (finalAscentActive == null || placeCurrent == null || apocalypseActive == null || builderOffsets == null || weatherFind == null || effectSet == null ||
                entryKey == null || bookOf == null || cycleRoll == null || rollEntry == null || placeType == null || placeIsDay == null || placeInRun == null)
            {
                Plugin.Log.LogWarning("True Final Ascent was found, but not the parts Ultimate Ascent works with; TFA integration is off.");
                return false;
            }
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

    // ---- Ascent 25: a second Apocalypse weather ----

    private static bool SecondaryWanted =>
        Rules.Enabled(Lvl.AllWeather) && HostOptions.DoubleApocalypse && ApocalypseActive && !Features.A25_AllWeather.Excluded;

    private static string RoomString(string key) => Net.GetRoom(key) as string ?? "";

    private static int RoomInt(string key) => Net.GetRoom(key) is int value ? value : 0;

    private static bool PrimaryRunning(out string primary, out int sequence)
    {
        primary = RoomString("APOW");
        sequence = RoomInt("APOWS");
        // TFA's cycle phase 4 is "Running".
        return primary.Length > 0 && RoomInt("APOCP") == 4;
    }

    internal static void Tick()
    {
        if (!Present || broken || Time.unscaledTime < nextTick)
        {
            return;
        }
        nextTick = Time.unscaledTime + 1f;
        Guard(() =>
        {
            TickSecondary();
            return 0;
        }, 0);
    }

    private static void TickSecondary()
    {
        bool wanted = SecondaryWanted;
        bool running = PrimaryRunning(out string primary, out int sequence);
        if (Rules.IsHost && PhotonNetwork.InRoom)
        {
            string next = hostSecondary;
            if (!wanted || !running)
            {
                next = "";
                hostPrimarySeq = -1;
            }
            else if (sequence != hostPrimarySeq)
            {
                hostPrimarySeq = sequence;
                next = PickSecondary(primary);
                if (next.Length > 0)
                {
                    object entry = bookOf.Invoke(null, new object[] { next });
                    if (entry != null && weatherAnnounce != null)
                    {
                        weatherAnnounce.Invoke(null, new[] { entry });
                    }
                    Plugin.Log.LogInfo($"Ascent 25: second Apocalypse weather \"{next}\" joins \"{primary}\".");
                }
            }
            if (next != hostSecondary || RoomString(SecondaryKey) != next)
            {
                hostSecondary = next;
                Net.SetRoom(SecondaryKey, next);
                Net.SetRoom(SecondaryOwnerKey, sequence);
            }
        }
        string want = wanted && running ? RoomString(SecondaryKey) : "";
        if (want.Length > 0 && want == primary)
        {
            // TFA itself now runs this weather in its own slot; it owns it from here on.
            appliedSecondary = "";
            return;
        }
        Apply(want);
    }

    private static void Apply(string want)
    {
        if (want == appliedSecondary)
        {
            return;
        }
        if (appliedSecondary.Length > 0)
        {
            SetEffect(appliedSecondary, false);
        }
        appliedSecondary = want;
        if (want.Length > 0)
        {
            SetEffect(want, true);
        }
    }

    private static void SetEffect(string key, bool on)
    {
        object effect = weatherFind.Invoke(null, new object[] { key });
        if (effect != null && effectSet.GetValue(effect) is Action<bool> set)
        {
            set(on);
        }
    }

    /// <summary>Rolls like TFA does (clusters, place weights, enabled list, day/night) until a fitting second weather comes up.</summary>
    private static string PickSecondary(string primary)
    {
        object place = placeCurrent.Invoke(null, null);
        bool day = (bool)placeIsDay.Invoke(null, null);
        Func<int, int, int> dice = UnityEngine.Random.Range;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            object roll = cycleRoll.Invoke(null, new[] { place, day, dice });
            object entry = rollEntry.GetValue(roll);
            string key = entry != null ? entryKey.GetValue(entry) as string : null;
            if (string.IsNullOrEmpty(key) || key == primary || NeverSecondary.Contains(key) || (StormFamily.Contains(key) && StormFamily.Contains(primary)))
            {
                continue;
            }
            if (weatherFind.Invoke(null, new object[] { key }) == null)
            {
                continue;
            }
            return key;
        }
        return "";
    }

    internal static void Reset()
    {
        if (appliedSecondary.Length > 0 && Present && !broken)
        {
            string key = appliedSecondary;
            Guard(() =>
            {
                SetEffect(key, false);
                return 0;
            }, 0);
        }
        appliedSecondary = "";
        hostSecondary = "";
        hostPrimarySeq = -1;
    }

    /// <summary>TFA's own roller must not pick the weather that is already running as the second one.</summary>
    [HarmonyPatch]
    private static class KeepSecondaryOutOfPrimary
    {
        private static bool Prepare() => Present;

        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName(Ns + "FinalAscentWeatherBook"), "ConditionMet");

        private static void Postfix(object entry, ref bool __result)
        {
            if (__result && appliedSecondary.Length > 0 && entry != null && entryKey != null && entryKey.GetValue(entry) as string == appliedSecondary)
            {
                __result = false;
            }
        }
    }
}
