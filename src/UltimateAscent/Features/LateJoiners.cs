using System.Collections;
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
/// <item>On every ascent: the starting curse (from ascent 7) reaches every scout. The game gives it once, as a scout
/// wakes up on the beach (or at the base camp), on that scout's own game, and it could miss: a seat by the plane that
/// does not start its scout passed out gave none, and a joining player's game could lose it while its scout was being
/// set up (statuses are refused while a scout is being moved, or while the game still counts it in the airport). So once
/// the scout is up it is checked, once: what is missing of it is added as soon as its statuses are taken again (True
/// Final Ascent does the same for its FINAL ASCENT).</item>
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

    /// <summary>How long after a scout appears its starting curse is still checked (it must be up by then).</summary>
    private const float CurseCheckSeconds = 60f;

    /// <summary>A scout starting the climb (on the beach, or arriving at the base camp) has its starting curse checked once it is up.</summary>
    [HarmonyPatch(typeof(CharacterSpawner), nameof(CharacterSpawner.SpawnMyPlayerCharacter))]
    private static class StartingCurse
    {
        private static void Postfix(CharacterSpawner.SpawnFlavor spawnFlavor, bool initializeNewScout, Character __result)
        {
            if (__result == null || !initializeNewScout || !GameHandler.IsOnIsland || Peak.Quicksave.ShouldUseSaveData || RunSettings.isMiniRun)
            {
                return;
            }
            __result.StartCoroutine(MakeUpStartingCurse(__result, spawnFlavor.ToString()));
        }
    }

    /// <summary>
    /// Once the scout is alive and awake (within a minute of appearing): a scout who has all of the starting curse is left
    /// as is; what is missing is added, once, as soon as its statuses are taken.
    /// </summary>
    internal static IEnumerator MakeUpStartingCurse(Character scout, string how)
    {
        for (float until = Time.time + CurseCheckSeconds; Time.time < until; )
        {
            yield return null;
            if (scout == null || !scout.IsLocal)
            {
                yield break;
            }
            CharacterAfflictions afflictions = scout.refs != null ? scout.refs.afflictions : null;
            if (afflictions == null || !Rules.InRun || scout.data.dead || scout.data.passedOut || scout.data.fullyPassedOut || scout.data.passedOutOnTheBeach > 0f
                || scout.data.fallSeconds > 0f)
            {
                continue;
            }
            // Up: a scout who has it is done; one who lacks it waits until statuses are taken again.
            float curse = Ascents.startingCurse;
            float has = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Curse);
            if (curse <= 0f || has >= curse - 0.001f)
            {
                yield break;
            }
            if (scout.statusesLocked || scout.warping || afflictions.m_inAirport)
            {
                continue;
            }
            afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, curse - has);
            Plugin.Log.LogInfo($"Starting curse made up ({how}): had {has * 100f:0.#}, now {curse * 100f:0}.");
            yield break;
        }
    }
}
