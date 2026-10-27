using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Blind Mode and Hot Mode: challenges each player ticks for themselves on the boarding pass
/// (<see cref="ChallengeModesUi"/>), kept in the mod's save for next time. They work on every ascent and in
/// custom runs, alone or with others (each player's own boxes count only for them). Reaching the peak with
/// one of them unlocks one of the mod's cosmetics (<see cref="CosmeticUnlocks"/>).
/// <list type="bullet">
/// <item>Blind Mode: all climb long the screen stays as dark as the Scoutmaster makes it at his closest (the
/// game's own screen effect at full strength), with no sound of its own. When the Scoutmaster really comes
/// it gets no darker; everything else he does stays his.</item>
/// <item>Hot Mode: ascent 23's sun on any ascent below 23, where that ascent's sun would scorch (not in the
/// Alpine, not from the Caldera and the Gloom on): it incinerates whatever you hold up against it just the
/// same, and heats you at 65% of its strength (half, raised by 30%). From ascent 23 on that ascent's own sun does it all, so Hot Mode
/// does nothing there.</item>
/// </list>
/// </summary>
internal static class ChallengeModes
{
    private const string BlindKey = "blindmode";
    private const string HotKey = "hotmode";

    private static readonly int Strength = Shader.PropertyToID("_Strength");
    private static readonly int Grain = Shader.PropertyToID("_GrainMult");

    private static Material screen;
    private static bool darkened;

    /// <summary>The boarding pass's extras are on (the config's "Boarding Pass Extras"); off, both modes are too.</summary>
    internal static bool Offered => Plugin.BoardingPassExtras == null || Plugin.BoardingPassExtras.Value;

    internal static bool Blind
    {
        get => Offered && ModSave.GetBool(BlindKey);
        set => ModSave.SetBool(BlindKey, value);
    }

    internal static bool Hot
    {
        get => Offered && ModSave.GetBool(HotKey);
        set => ModSave.SetBool(HotKey, value);
    }

    /// <summary>
    /// On this ascent the sun already incinerates held shields (23 and above: a regular run's, or the one a custom run
    /// picks), so Hot Mode has nothing to add.
    /// </summary>
    internal static bool SunByAscent(int ascent) => ascent >= Lvl.HotSun && ascent != BoardingPassUi.FinalAscentIndex;

    /// <summary>Hot Mode's sun heats at this share of ascent 23's.</summary>
    internal const float HotModeHeat = 0.5f * 1.3f;

    /// <summary>Hot Mode's sun shines in this climb: it is ticked, and the climb is below ascent 23 (or a custom run without a picked ascent).</summary>
    internal static bool HotRoasts => Hot && Rules.InRun && (!Rules.ByAscent || Ascents._currentAscent < Lvl.HotSun);

    /// <summary>
    /// Blind Mode darkens the screen right now: it is ticked and a climb is on, its map loaded (the scenes before the
    /// title screen, the developers' logos among them, are no climb even though they are neither title nor airport).
    /// </summary>
    internal static bool Blinding => Blind && Rules.InRun && Map.Ready;

    internal static void Reset()
    {
        screen = null;
    }

    internal static void Tick()
    {
        if (Blinding)
        {
            Darken(Screen());
            darkened = true;
        }
        else if (darkened)
        {
            // The effect's material outlives the climb: back to clear.
            Material material = Screen();
            if (material != null)
            {
                material.SetFloat(Strength, 0f);
            }
            darkened = false;
        }
    }

    /// <summary>The Scoutmaster's screen effect (the one in the level, or his prefab's when none is there yet).</summary>
    private static Material Screen()
    {
        if (screen == null)
        {
            Scoutmaster live = Object.FindAnyObjectByType<Scoutmaster>();
            screen = live != null ? live.mat : null;
            if (screen == null)
            {
                GameObject prefab = Resources.Load<GameObject>("Character_Scoutmaster");
                Scoutmaster template = prefab != null ? prefab.GetComponentInChildren<Scoutmaster>(true) : null;
                screen = template != null ? template.mat : null;
            }
        }
        return screen;
    }

    private static void Darken(Material material)
    {
        if (material == null)
        {
            return;
        }
        material.SetFloat(Grain, GUIManager.instance != null && GUIManager.instance.photosensitivity ? 0f : 1f);
        material.SetFloat(Strength, 1f);
    }

    /// <summary>The Scoutmaster sets his screen effect every frame; in Blind Mode it stays at its strongest.</summary>
    [HarmonyPatch(typeof(Scoutmaster), nameof(Scoutmaster.DoVisuals))]
    private static class StayDark
    {
        internal static void Postfix(Scoutmaster __instance)
        {
            if (Blinding)
            {
                screen = __instance.mat;
                Darken(__instance.mat);
                // Cleared again as soon as the climb is over (a Scoutmaster also goes when the level unloads).
                darkened = true;
            }
        }
    }

    /// <summary>A Scoutmaster switched off or removed clears his screen effect; in Blind Mode it stays dark.</summary>
    [HarmonyPatch(typeof(Scoutmaster), nameof(Scoutmaster.OnDisable))]
    private static class StayDarkWhenHeLeaves
    {
        private static void Postfix(Scoutmaster __instance) => StayDark.Postfix(__instance);
    }

    [HarmonyPatch(typeof(Scoutmaster), nameof(Scoutmaster.OnDestroy))]
    private static class StayDarkWhenHeIsGone
    {
        private static void Postfix(Scoutmaster __instance) => StayDark.Postfix(__instance);
    }
}
