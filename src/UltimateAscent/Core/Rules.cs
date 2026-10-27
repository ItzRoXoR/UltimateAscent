using UnityEngine.SceneManagement;

namespace UltimateAscent.Core;

/// <summary>
/// Answers "is this rule active right now?". The selected ascent is the game's own
/// <see cref="Ascents.currentAscent"/>, which the game already synchronises to every player
/// (departure RPC, late-join RPC and campfire saves), so the rules need no extra networking.
/// A custom run plays by the ascents only when the Custom Run screen picks one (<see cref="Features.CustomRunAscent"/>).
/// </summary>
internal static class Rules
{
    private static string activeScene = "";

    /// <summary>True while a climb is loaded (not the title screen or the airport).</summary>
    internal static bool InRun { get; private set; }

    /// <summary>The climb plays by its ascent: a regular run, or a custom run with an ascent picked on the Custom Run screen.</summary>
    internal static bool ByAscent => !RunSettings.IsCustomRun || Features.CustomRunAscent.On;

    /// <summary>The ascent of the current climb, or 0 outside a climb / in custom runs without a picked ascent.</summary>
    internal static int Level
    {
        get
        {
            if (!InRun || !ByAscent)
            {
                return 0;
            }
            return Ascents._currentAscent;
        }
    }

    /// <summary>One of this mod's ascents (9-25) is being climbed, whichever of their rules are switched on.</summary>
    internal static bool InExtraAscent => Plugin.Ready && InRun && ByAscent && Ascents._currentAscent >= Lvl.First;

    /// <summary>The rule of this ascent applies: the climb is on it or a higher one, and its switch (the host's) is on.</summary>
    internal static bool Enabled(int level)
    {
        return Plugin.Ready && InRun && ByAscent && Ascents._currentAscent >= level && HostOptions.AscentOn(level);
    }

    /// <summary>
    /// A threat the Custom Run screen can switch off is there: always in regular runs, in custom runs (whatever ascent they
    /// pick) unless its switch is off.
    /// </summary>
    internal static bool Hazard(RunSettings.SETTINGTYPE threat)
    {
        return !RunSettings.IsCustomRun || RunSettings.GetValue(threat) != 0;
    }

    /// <summary>The local client is responsible for this character's statuses and movement.</summary>
    internal static bool Owns(Character character)
    {
        return character != null && character.IsLocal;
    }

    internal static bool IsHost => !Photon.Pun.PhotonNetwork.InRoom || Photon.Pun.PhotonNetwork.IsMasterClient;

    internal static void OnActiveSceneChanged(Scene scene)
    {
        activeScene = scene.name ?? "";
        InRun = activeScene.Length > 0 && activeScene != "Airport" && activeScene != "Title" && activeScene != "MainMenu";
    }

    internal static string ActiveScene => activeScene;
}
