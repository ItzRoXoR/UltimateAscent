using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The game's own boarding pass selects ascents 9-25 (no extra panel):
/// <list type="bullet">
/// <item>The arrows go up to the highest unlocked ascent. With True Final Ascent installed, its
/// FINAL ASCENT entry stays right after that, exactly where TFA puts it.</item>
/// <item>The arrows wrap around: left from Tenderfoot jumps to the highest unlocked ascent, right
/// from the end of the list goes back to Tenderfoot.</item>
/// <item>The boarding pass opens on the ascent chosen in the config ("Default Ascent").</item>
/// <item>The reward line shows the next extended ascent to beat.</item>
/// <item>Ascents 9-25 can only be started when every player has this mod in the same version.</item>
/// </list>
/// </summary>
internal static class BoardingPassUi
{
    /// <summary>True Final Ascent marks its FINAL ASCENT entry with this ascent index.</summary>
    internal const int FinalAscentIndex = 1337;

    internal const string VersionKey = Net.Prefix + "v";

    private static bool defaultApplied;
    private static bool startBlocked;
    private static bool startWasInteractable;
    private static string lastMissing;
    private static float nextRefresh;

    internal static void OnSceneChanged()
    {
        defaultApplied = false;
        startBlocked = false;
        lastMissing = null;
    }

    private static bool IsRegular(int index) => index >= -1 && index <= Lvl.Last;

    /// <summary>While the boarding pass works, the game (and TFA) see the extended unlock level.</summary>
    private static class Scope
    {
        [ThreadStatic] internal static int Depth;
    }

    [HarmonyPatch]
    private static class BoardingPassScope
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(BoardingPass), nameof(BoardingPass.UpdateAscent));
            yield return AccessTools.Method(typeof(BoardingPass), nameof(BoardingPass.IncrementAscent));
            yield return AccessTools.Method(typeof(BoardingPass), nameof(BoardingPass.DecrementAscent));
            yield return AccessTools.Method(typeof(BoardingPass), nameof(BoardingPass.Initialize));
            yield return AccessTools.Method(typeof(BoardingPass), nameof(BoardingPass.OnOpen));
            yield return AccessTools.Method(typeof(BoardingPass), nameof(BoardingPass.StartGame));
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            Scope.Depth++;
        }

        private static Exception Finalizer(Exception __exception)
        {
            Scope.Depth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.GetMaxAscent))]
    private static class ExtendedMaxAscent
    {
        private static void Postfix(ref int __result)
        {
            if (Scope.Depth > 0 && Plugin.Ready && AscentCatalog.Registered)
            {
                __result = Mathf.Max(Mathf.Min(__result, Lvl.VanillaLast), Progress.MaxSelectable);
            }
        }
    }

    /// <summary>Opens on the configured default ascent, once per visit to the airport.</summary>
    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.OnOpen))]
    private static class DefaultAscent
    {
        private static void Prefix(BoardingPass __instance)
        {
            if (defaultApplied || !AscentCatalog.Registered)
            {
                return;
            }
            defaultApplied = true;
            int wanted = Plugin.DefaultAscent.Value;
            if (wanted == 0 || RunSettings.IsCustomRun)
            {
                return;
            }
            __instance.ascentIndex = Mathf.Clamp(wanted, -1, Progress.MaxSelectable);
        }
    }

    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.IncrementAscent))]
    private static class WrapForward
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BoardingPass __instance)
        {
            if (!Plugin.WrapAround.Value || RunSettings.IsCustomRun || !AscentCatalog.Registered)
            {
                return true;
            }
            bool atEnd = Compat.Tfa.Present ? __instance.ascentIndex == FinalAscentIndex : __instance.ascentIndex >= Progress.MaxSelectable;
            if (!atEnd)
            {
                return true;
            }
            __instance.ascentIndex = -1;
            __instance.UpdateAscent();
            return false;
        }
    }

    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.DecrementAscent))]
    private static class WrapBackward
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BoardingPass __instance)
        {
            if (!Plugin.WrapAround.Value || RunSettings.IsCustomRun || !AscentCatalog.Registered || __instance.ascentIndex != -1)
            {
                return true;
            }
            __instance.ascentIndex = Progress.MaxSelectable;
            __instance.UpdateAscent();
            return false;
        }
    }

    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.UpdateAscent))]
    private static class Refresh
    {
        private static void Postfix(BoardingPass __instance)
        {
            if (!AscentCatalog.Registered)
            {
                return;
            }
            AscentCatalog.Ensure(__instance.ascentData);
            int index = __instance.ascentIndex;
            bool custom = RunSettings.IsCustomRun;
            if (!custom)
            {
                if (index != FinalAscentIndex)
                {
                    __instance.incrementAscentButton.interactable = index < Progress.MaxSelectable || Plugin.WrapAround.Value || Compat.Tfa.Present;
                }
                else if (Plugin.WrapAround.Value)
                {
                    __instance.incrementAscentButton.interactable = true;
                }
                if (Plugin.WrapAround.Value)
                {
                    __instance.decrementAscentButton.interactable = true;
                }
            }
            if (!custom && IsRegular(index) && index >= 0 && __instance.ascentData != null && index + 2 < __instance.ascentData.ascents.Count)
            {
                // The reward line belongs to the next ascent to beat (the game hides it past ascent 8).
                bool nextToBeat = index == Progress.Earned;
                __instance.reward.gameObject.SetActive(nextToBeat);
                if (nextToBeat)
                {
                    AscentData.AscentInstanceData data = __instance.ascentData.ascents[index + 2];
                    __instance.rewardText.text = data.localizedReward;
                    __instance.rewardImage.color = data.color;
                }
            }
            RefreshStart(__instance);
        }
    }

    /// <summary>Every frame while the boarding pass is open: re-check the other players' versions.</summary>
    internal static void Tick()
    {
        if (Time.unscaledTime < nextRefresh || GUIManager.instance == null)
        {
            return;
        }
        nextRefresh = Time.unscaledTime + 0.5f;
        Net.SetLocalPlayer(VersionKey, Plugin.Version);
        BoardingPass pass = GUIManager.instance.boardingPass;
        if (pass == null || !pass.isOpen)
        {
            return;
        }
        string missing = MissingPlayers(pass);
        if (missing != lastMissing)
        {
            pass.UpdateAscent();
        }
    }

    private static string MissingPlayers(BoardingPass pass)
    {
        int index = pass.ascentIndex;
        if (RunSettings.IsCustomRun || index < Lvl.First || index > Lvl.Last || !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode)
        {
            return null;
        }
        List<string> names = null;
        foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerList)
        {
            if (player.IsLocal || player.CustomProperties[VersionKey] as string == Plugin.Version)
            {
                continue;
            }
            names ??= new List<string>();
            names.Add(string.IsNullOrEmpty(player.NickName) ? "#" + player.ActorNumber : player.NickName);
        }
        return names == null ? null : string.Join(", ", names);
    }

    private static void RefreshStart(BoardingPass pass)
    {
        string missing = MissingPlayers(pass);
        lastMissing = missing;
        if (missing != null)
        {
            if (!startBlocked)
            {
                startWasInteractable = pass.startGameButton.interactable;
                startBlocked = true;
            }
            pass.startGameButton.interactable = false;
            pass.ascentDesc.text += $"\n\n<color=#FF9A7A><size=70%>Everyone needs Ultimate Ascent {Plugin.Version} for this ascent. Missing: {missing}</size></color>";
        }
        else if (startBlocked)
        {
            pass.startGameButton.interactable = startWasInteractable;
            startBlocked = false;
        }
    }
}
