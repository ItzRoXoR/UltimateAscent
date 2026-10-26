using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Scouts joining a climb under way, and the starting curse.
/// <list type="bullet">
/// <item>From ascent 9 a scout who joins after the others have left the shore arrives alive at the current base camp
/// (when the game counts it safe: no fog or lava closing in), waking up there with the starting curse and nothing else:
/// no revive curse or hunger. The game brings a joiner back only at a base camp whose end-of-location statue has
/// revived scouts, and ascent 19 takes those statues away, so joiners arrived dead.</item>
/// <item>A scout's arrival (the game spawns joiners and kills them at once before any revive) is no death for ascent
/// 11's curse on the survivors: <see cref="JustArrived"/>.</item>
/// <item>On every ascent: the game gives the starting curse (from ascent 7) only to scouts who wake up on the beach;
/// a seat by the plane that does not start its scout passed out gave none, so one scout started without it. Those
/// scouts get it as they appear (True Final Ascent does the same for its FINAL ASCENT).</item>
/// </list>
/// </summary>
internal static class LateJoiners
{
    /// <summary>A scout this young (seconds since it appeared) who dies has only just arrived.</summary>
    private const float ArrivalSeconds = 10f;

    private sealed class Born
    {
        internal float at;
    }

    private static readonly ConditionalWeakTable<Character, Born> born = new ConditionalWeakTable<Character, Born>();

    /// <summary>The local scout is being brought in at the base camp: its revive costs nothing.</summary>
    private static bool arriving;

    [HarmonyPatch(typeof(Character), "Awake")]
    private static class Appeared
    {
        private static void Postfix(Character __instance)
        {
            born.GetOrCreateValue(__instance).at = Time.time;
        }
    }

    /// <summary>The scout appeared only moments ago: a death now is the game's dead-on-arrival, not a real one.</summary>
    internal static bool JustArrived(Character character) =>
        character != null && born.TryGetValue(character, out Born b) && Time.time - b.at < ArrivalSeconds;

    [HarmonyPatch(typeof(CharacterSpawner), nameof(CharacterSpawner.RPC_NewPlayerSpawn))]
    private static class ArriveAlive
    {
        private static void Prefix(ref bool canRevive)
        {
            if (!Rules.InExtraAscent || !GameHandler.IsOnIsland || !GameHandler.PlayersHaveLeftShore || canRevive)
            {
                return;
            }
            if (MapHandler.LastSeenCampfireIsSafe)
            {
                canRevive = true;
                arriving = true;
                Plugin.Log.LogInfo("Late joiner: arriving alive at the base camp.");
            }
        }
    }

    /// <summary>The arrival's revive gives no revive curse or hunger.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyPostReviveStatus))]
    private static class FreeArrival
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CharacterAfflictions afflictions)
        {
            return !(arriving && afflictions != null && afflictions.character != null && afflictions.character.IsLocal);
        }
    }

    /// <summary>Waking up at the base camp ends the arrival (the game gives the starting curse there).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.StartPassedOutOnTheBeach))]
    private static class Woke
    {
        private static void Postfix(Character __instance)
        {
            if (__instance.IsLocal)
            {
                arriving = false;
            }
        }
    }

    /// <summary>A scout starting the climb without waking up on the beach gets the starting curse as it appears.</summary>
    [HarmonyPatch(typeof(CharacterSpawner), nameof(CharacterSpawner.SpawnMyPlayerCharacter))]
    private static class StartingCurse
    {
        private static void Postfix(CharacterSpawner.SpawnFlavor spawnFlavor, bool initializeNewScout, Character __result)
        {
            if (__result == null || !initializeNewScout || (spawnFlavor == CharacterSpawner.SpawnFlavor.PassedOut && !CharacterSpawner.BlockPassOutSpawns)
                || !GameHandler.IsOnIsland || GameHandler.PlayersHaveLeftShore || Peak.Quicksave.ShouldUseSaveData || RunSettings.isMiniRun)
            {
                return;
            }
            float curse = Ascents.startingCurse;
            CharacterAfflictions afflictions = __result.refs != null ? __result.refs.afflictions : null;
            if (curse <= 0f || afflictions == null)
            {
                return;
            }
            float has = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Curse);
            if (has < curse)
            {
                afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, curse - has);
                Plugin.Log.LogInfo($"Starting curse given to a scout who did not wake up on the beach ({spawnFlavor}): {curse * 100f:0}.");
            }
        }
    }
}
