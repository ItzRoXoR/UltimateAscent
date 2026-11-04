using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Status = CharacterAfflictions.STATUSTYPE;
using Tfa = UltimateAscent.Compat.Tfa;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// A weather another mod starts through True Final Ascent (TFA 3.0's weather API): the test plays that mod, starts TFA's
/// sun with it and stops it again. Meanwhile the mod's own sun rests, ascent 25's storms keep calm, and what TFA's weather
/// does to the scout lands as TFA gives it (no ascent 14 boost).
/// </summary>
internal sealed partial class Runner
{
    private const string ModWeatherOwner = "uatest";
    private const string ModWeatherKey = "sun";

    /// <summary>The statuses TFA's own code adds to the local scout while recorded: as asked, and as they land after every rule.</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    internal static class TfaAmounts
    {
        internal static bool On;
        internal static readonly List<(Status status, float asked, float landed)> Seen = new List<(Status, float, float)>();
        private static Assembly tfa;
        private static float asked = float.NaN;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(CharacterAfflictions __instance, Status statusType, float amount)
        {
            asked = float.NaN;
            if (!On || amount <= 0f || __instance.character != Character.localCharacter)
            {
                return;
            }
            tfa = tfa ?? AccessTools.TypeByName("TrueFinalAscent.FinalAscent")?.Assembly;
            if (tfa != null && (new StackTrace(1, false).GetFrames() ?? new StackFrame[0]).Any(f => f.GetMethod()?.DeclaringType?.Assembly == tfa))
            {
                asked = amount;
            }
        }

        internal static void Landed(Status statusType, float amount)
        {
            if (!float.IsNaN(asked))
            {
                Seen.Add((statusType, asked, amount));
                asked = float.NaN;
            }
        }
    }

    /// <summary>The second half of <see cref="TfaAmounts"/>: the amount after every other prefix.</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    internal static class TfaAmountsLanded
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Status statusType, float amount) => TfaAmounts.Landed(statusType, amount);
    }

    private IEnumerator ModWeatherChecks()
    {
        Type api = Tfa.Present ? AccessTools.TypeByName("TrueFinalAscent.Api.TfaWeather") : null;
        Type values = Tfa.Present ? AccessTools.TypeByName("TrueFinalAscent.Api.TfaWeatherParams") : null;
        MethodInfo start = api != null ? AccessTools.Method(api, "Start") : null;
        MethodInfo stop = api != null ? AccessTools.Method(api, "Stop") : null;
        if (start == null || stop == null || values == null)
        {
            Info("the check of another mod's weather is skipped", Tfa.Present ? "this TFA has no weather API" : "True Final Ascent is not here");
            yield break;
        }
        Character local = Local;
        bool drivesBefore = A25_AllWeather.DrivesVanillaWeather;
        Protect(false);
        ClearStatuses(Afflictions);
        HeatSources.Clear();
        HeatSources.On = true;
        TfaAmounts.Seen.Clear();
        TfaAmounts.On = true;
        try
        {
            // Before: the mod's own sun (ascent 23, on the beach by day).
            yield return new WaitForSeconds(3f);
            float ownSunBefore = HeatSources.Totals.Where(kv => kv.Key.StartsWith("A15_StrongerAilments.AddUnboosted", StringComparison.Ordinal)).Sum(kv => kv.Value);
            bool sunlit = InSunlight(local);

            object settings = Activator.CreateInstance(values);
            AccessTools.Property(values, "Seconds").SetValue(settings, (float?)60f);
            AccessTools.Property(values, "Warn").SetValue(settings, (bool?)false);
            object result = start.Invoke(null, new object[] { ModWeatherOwner, ModWeatherKey, settings });
            for (float until = Time.time + 20f; Time.time < until && Tfa.Weather != ModWeatherKey;)
            {
                yield return new WaitForSeconds(0.5f);
            }
            Check("a weather another mod starts through TFA is seen as TFA's weather (any climb, no Apocalypse)", Tfa.Weather == ModWeatherKey && Tfa.ModWeatherRunning,
                $"TFA answered {result}; TFA's weather now '{Tfa.Weather}', another mod's {Tfa.ModWeatherRunning}, Apocalypse {Tfa.ApocalypseActive}, FINAL ASCENT {Tfa.FinalAscentActive}");
            ClearStatuses(Afflictions);
            HeatSources.Clear();
            TfaAmounts.Seen.Clear();
            yield return new WaitForSeconds(6f);
            float ownSun = HeatSources.Totals.Where(kv => kv.Key.StartsWith("A15_StrongerAilments.AddUnboosted", StringComparison.Ordinal)).Sum(kv => kv.Value);
            string sources = string.Join(", ", HeatSources.Totals.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value * 100f:0.#}"));
            if (Rules.Enabled(Lvl.HotSun))
            {
                Check("meanwhile the mod's own sun rests (23)", ownSun <= 0f,
                    $"its heat in 6 s {ownSun * 100f:0.##} (before the weather, in 3 s: {ownSunBefore * 100f:0.##}, in sunlight {sunlit}); heat by its source: {sources}");
            }
            if (Rules.Enabled(Lvl.AllWeather))
            {
                WindChillZone[] storms = FindObjectsByType<WindChillZone>(FindObjectsSortMode.None).Where(A18_ExtremeWeather.IsStorm).ToArray();
                bool calm = storms.All(z => A25_AllWeather.SuppressZone(z) && !z.windActive);
                Check("meanwhile ascent 25's storms keep calm (25)", !A25_AllWeather.DrivesVanillaWeather && calm,
                    $"driven {A25_AllWeather.DrivesVanillaWeather}; {storms.Length} storms, calm {calm}");
            }
            List<(Status status, float asked, float landed)> seen = TfaAmounts.Seen.ToList();
            if (seen.Count == 0)
            {
                Info("what TFA's weather does to the scout: nothing came in 6 s", $"in sunlight {InSunlight(local)}; heat by its source: {sources}");
            }
            else
            {
                Check("what TFA's weather does to the scout lands as TFA gives it: no ascent 14 boost (14)", seen.All(s => Mathf.Abs(s.landed - s.asked) < 1e-5f),
                    $"{seen.Count} from TFA's code: " + string.Join(", ", seen.Take(6).Select(s => $"{s.status} {s.asked * 100f:0.###} -> {s.landed * 100f:0.###}")));
            }
        }
        finally
        {
            TfaAmounts.On = false;
            HeatSources.On = false;
            stop.Invoke(null, new object[] { ModWeatherOwner });
            ClearStatuses(Afflictions);
            Protect(true);
        }
        for (float until = Time.time + 20f; Time.time < until && Tfa.ModWeatherRunning;)
        {
            yield return new WaitForSeconds(0.5f);
        }
        Check("once it has stopped, the climb's weather is the mod's again", !Tfa.ModWeatherRunning && Tfa.Weather.Length == 0 && A25_AllWeather.DrivesVanillaWeather == drivesBefore,
            $"TFA's weather '{Tfa.Weather}', ascent 25 drives the storms {A25_AllWeather.DrivesVanillaWeather} (before {drivesBefore})");
    }
}
