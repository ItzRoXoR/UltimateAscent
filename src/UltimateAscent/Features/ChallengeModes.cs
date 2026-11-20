using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Blind Mode and Hot Mode: challenges ticked on the boarding pass (<see cref="ChallengeModesUi"/>), kept in the mod's
/// save for next time. They work on every ascent and in custom runs. With others, the host's boxes count for everyone:
/// the host publishes them in the room, and the other players' passes show them without letting them be changed (their
/// own ticks are kept for when they host). Reaching the peak with one of them unlocks one of the mod's cosmetics
/// (<see cref="CosmeticUnlocks"/>).
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

    /// <summary>The host's boxes, in the room: 1 Blind Mode, 2 Hot Mode.</summary>
    private const string RoomKey = Net.Prefix + "modes";
    private const int BlindBit = 1;
    private const int HotBit = 2;

    private static readonly int Strength = Shader.PropertyToID("_Strength");
    private static readonly int Grain = Shader.PropertyToID("_GrainMult");

    private static Material screen;
    private static bool darkened;

    /// <summary>The boarding pass's extras are on (the config's "Boarding Pass Extras"); off, both modes are too.</summary>
    internal static bool Offered => Plugin.BoardingPassExtras == null || Plugin.BoardingPassExtras.Value;

    /// <summary>
    /// Blind Mode is on for this climb: the host's box (this player's own when they host or play alone). Setting it ticks
    /// this player's own box.
    /// </summary>
    internal static bool Blind
    {
        get => (Modes & BlindBit) != 0;
        set => ModSave.SetBool(BlindKey, value);
    }

    /// <summary>Hot Mode is on for this climb: the host's box, as for <see cref="Blind"/>.</summary>
    internal static bool Hot
    {
        get => (Modes & HotBit) != 0;
        set => ModSave.SetBool(HotKey, value);
    }

    /// <summary>In someone else's room: the host's boxes count, and this player's pass only shows them.</summary>
    internal static bool FromHost => PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient;

    /// <summary>This player's own ticks, as their boarding pass extras allow.</summary>
    private static int Own => Offered ? (ModSave.GetBool(BlindKey) ? BlindBit : 0) | (ModSave.GetBool(HotKey) ? HotBit : 0) : 0;

    /// <summary>The boxes that count: the host's, published in the room (none from a host without them), or this player's own.</summary>
    private static int Modes => FromHost ? Net.GetRoom(RoomKey) is int published ? published : 0 : Own;

    /// <summary>Host: publish its boxes (cheap when unchanged).</summary>
    internal static void Publish()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && !(Net.GetRoom(RoomKey) is int current && current == Own))
        {
            Net.SetRoom(RoomKey, Own);
        }
    }

    /// <summary>
    /// On this ascent the sun already incinerates held shields (23 and above: a regular run's, or the one a custom run
    /// picks), so Hot Mode has nothing to add.
    /// </summary>
    internal static bool SunByAscent(int ascent) => ascent >= Lvl.HotSun && ascent != BoardingPassUi.FinalAscentIndex;

    /// <summary>Hot Mode's sun heats this much a second (65% of what ascent 23's sun was, 0.5 a second, before it became 2).</summary>
    internal const float HotModeHeatPerSecond = 0.005f * 0.65f;

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
        Publish();
        ChallengeModesUi.Follow();
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
