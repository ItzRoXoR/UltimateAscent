using System;
using UltimateAscent.Features;
using UltimateAscent.Features.Weather;
using UnityEngine.SceneManagement;

namespace UltimateAscent.Core;

/// <summary>Clears per-climb state whenever a new scene becomes active.</summary>
internal static class RunLifecycle
{
    private static readonly Action[] Resets =
    {
        Overrides.RestoreAll,
        BoardingPassUi.OnSceneChanged,
        FogSphereRules.Reset,
        A11_RuleZero.Reset,
        A19_NoReviveStatues.Reset,
        A20_BingBong.Reset,
        A23_HotSun.Reset,
        BurnedItems.Reset,
        A17_BeeAmbush.Reset,
        A17_Hazards.Reset,
        A17_ClimbingHazards.Reset,
        A17_BananaDarkness.Reset,
        A17_Looker.Reset,
        A15_FoodPoison.Reset,
        ToxicDrink.Reset,
        A13_ButterFingers.Reset,
        A14_Items.Reset,
        ClimbingChalkItem.Reset,
        ClimbingChalkHands.Reset,
        ExactStatus.Reset,
        A18_AlpineNights.Reset,
        CalderaTide.Reset,
        KilnAsh.Reset,
        AlpineSnowfall.Reset,
        ChallengeModes.Reset,
        WeatherPhases.Reset,
        WeatherGrace.Reset,
        A25_AllWeather.Reset,
        ExtraTornadoes.Reset,
        Compat.Tfa.Reset,
        WildFoods.OnMapLoaded,
    };

    internal static void OnSceneChanged(Scene next)
    {
        foreach (Action reset in Resets)
        {
            try
            {
                reset();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Resetting {reset.Method.DeclaringType?.Name} failed: {ex.Message}");
            }
        }
        if (Rules.InRun && Rules.Level >= Lvl.First)
        {
            Plugin.Log.LogInfo($"Climb started on ascent {Rules.Level}" + (Compat.Tfa.Present ? " (True Final Ascent installed)." : "."));
        }
    }
}
