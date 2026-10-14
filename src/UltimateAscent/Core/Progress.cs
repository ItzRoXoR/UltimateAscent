using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Core;

/// <summary>
/// Unlock progression for ascents 9-25.
/// The game's Steam stat "MaxAscent" stops at 9 (all vanilla ascents beaten) and is never written
/// beyond that; progress past it is kept in this mod's config. Beating ascent N unlocks N + 1.
/// </summary>
internal static class Progress
{
    /// <summary>The raw vanilla stat: 0-9, 9 meaning ascent 8 has been beaten.</summary>
    internal static int VanillaStat
    {
        get
        {
            AchievementManager achievements = Singleton<AchievementManager>.Instance;
            if (achievements == null || !achievements.GetSteamStatInt(STEAMSTATTYPE.MaxAscent, out int value))
            {
                return 0;
            }
            return Mathf.Clamp(value, 0, Lvl.VanillaCompletedStat);
        }
    }

    /// <summary>Earned progress, without the "unlock all" switch: the next ascent to beat.</summary>
    internal static int Earned
    {
        get
        {
            int vanilla = VanillaStat;
            if (vanilla < Lvl.VanillaCompletedStat)
            {
                return vanilla;
            }
            return Mathf.Clamp(Mathf.Max(Lvl.First, Plugin.ExtendedProgress.Value), Lvl.First, Lvl.Last + 1);
        }
    }

    /// <summary>The highest ascent that can be picked on the boarding pass.</summary>
    internal static int MaxSelectable
    {
        get
        {
            if (Plugin.UnlockAll.Value)
            {
                return Lvl.Last;
            }
            return Mathf.Clamp(Earned, 0, Lvl.Last);
        }
    }

    /// <summary>
    /// Beating an extended ascent: record the progress in the config. These ascents give no reward,
    /// so the end screen shows no promotion for them. Vanilla ascents keep the game's own handling.
    /// </summary>
    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.TryCompleteAscent))]
    private static class CompleteExtendedAscent
    {
        private static bool Prefix(AchievementManager __instance)
        {
            int ascent = Ascents.currentAscent;
            if (ascent < Lvl.First || !AscentCatalog.Registered)
            {
                return true;
            }
            if (RunSettings.blockingAchievements)
            {
                return false;
            }
            int next = Earned;
            if (ascent >= next)
            {
                Plugin.ExtendedProgress.Value = Mathf.Min(ascent + 1, Lvl.Last + 1);
                Plugin.Instance.Config.Save();
                Plugin.Log.LogInfo($"Ascent {ascent} beaten; ascent {Mathf.Min(ascent + 1, Lvl.Last)} is unlocked.");
            }
            // Every vanilla ascent badge is earned along the way: ascent N contains ascents 1-8.
            __instance.TestAscentAchievements(Lvl.VanillaCompletedStat);
            return false;
        }
    }

    /// <summary>Never write an out-of-range value to the game's Steam stat.</summary>
    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.SetSteamStat), typeof(STEAMSTATTYPE), typeof(int))]
    private static class ClampSteamStat
    {
        private static void Prefix(STEAMSTATTYPE steamStatType, ref int value)
        {
            if (steamStatType == STEAMSTATTYPE.MaxAscent && value > Lvl.VanillaCompletedStat)
            {
                value = Lvl.VanillaCompletedStat;
            }
        }
    }
}
