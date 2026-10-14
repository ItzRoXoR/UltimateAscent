using UnityEngine.SceneManagement;

namespace UltimateAscent.Core;

/// <summary>
/// Answers "is this rule active right now?". The selected ascent is the game's own
/// <see cref="Ascents.currentAscent"/>, which the game already synchronises to every player
/// (departure RPC, late-join RPC and campfire saves), so the rules need no extra networking.
/// </summary>
internal static class Rules
{
    private static string activeScene = "";

    /// <summary>True while a climb is loaded (not the title screen or the airport).</summary>
    internal static bool InRun { get; private set; }

    /// <summary>The ascent of the current climb, or 0 outside a climb / in custom runs.</summary>
    internal static int Level
    {
        get
        {
            if (!InRun || RunSettings.IsCustomRun)
            {
                return 0;
            }
            return Ascents._currentAscent;
        }
    }

    /// <summary>One of this mod's ascents (9-25) is being climbed, whichever of their rules are switched on.</summary>
    internal static bool InExtraAscent => Plugin.Ready && InRun && !RunSettings.IsCustomRun && Ascents._currentAscent >= Lvl.First;

    /// <summary>The rule of this ascent applies: the climb is on it or a higher one, and its switch (the host's) is on.</summary>
    internal static bool Enabled(int level)
    {
        return Plugin.Ready && InRun && !RunSettings.IsCustomRun && Ascents._currentAscent >= level && HostOptions.AscentOn(level);
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
