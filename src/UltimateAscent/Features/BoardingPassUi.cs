using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The game's own boarding pass selects ascents 9-25 (no extra panel):
/// <list type="bullet">
/// <item>The arrows go up to the highest unlocked ascent. With True Final Ascent installed, its
/// FINAL ASCENT entry stays right after that, exactly where TFA puts it.</item>
/// <item>The arrows wrap around: left from Tenderfoot jumps to the end of the list (True Final
/// Ascent's FINAL ASCENT when it is installed, otherwise the highest unlocked ascent), right from the
/// end of the list goes back to Tenderfoot.</item>
/// <item>The boarding pass opens on the ascent chosen in the config ("Default Ascent").</item>
/// <item>Ascent 25 shows the Pain Sash on the reward line while the player has not unlocked it; the other ascents from 9
/// show none. The reward line, the game's own ascents' too,
/// sits under the ascent's text, at its left, in one row, leaving the card's bottom right to the map picker.</item>
/// <item>Ascents 9-25 (a custom run's picked ascent too) can only be started when every player has this mod in the same
/// version; the players missing it are named in a line over the pass, at its top left.</item>
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

    /// <summary>The line over the pass naming the players who miss this version of the mod.</summary>
    private static TextMeshProUGUI notice;

    /// <summary>For the in-game test: the players named as missing the mod (a solo game has nobody else).</summary>
    internal static string MissingForTest;

    /// <summary>The gap between the ascent's text and the reward line under it, and between the pass and the line over it.</summary>
    private const float RewardGap = 5f;
    private const float NoticeGap = 12f;

    /// <summary>The gap between "REWARD:" and the banner beside it, and the room kept from the map picker.</summary>
    private const float RewardWordsGap = 10f;
    private const float RewardMargin = 6f;

    /// <summary>The reward line is never made smaller than this to fit.</summary>
    private const float RewardSmallest = 0.7f;

    /// <summary>The reward line's own size, before it is fitted under the text.</summary>
    private static Vector3? rewardScale;

    /// <summary>The notice's left edge: in line with the plane at the pass's top left.</summary>
    private const float NoticeIndent = 48f;

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
            if (__instance.ascentIndex == FinalAscentIndex)
            {
                // What TFA's own left arrow does when it leaves FINAL ASCENT.
                Compat.Tfa.LeaveFinalAscent();
            }
            __instance.ascentIndex = -1;
            __instance.UpdateAscent();
            return false;
        }
    }

    /// <summary>
    /// After TFA's own left arrow: every prefix runs even once one has skipped the game's method, and
    /// TFA's moves FINAL ASCENT on to the highest ascent, so going there first would land on that.
    /// </summary>
    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.DecrementAscent))]
    private static class WrapBackward
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(BoardingPass __instance)
        {
            if (!Plugin.WrapAround.Value || RunSettings.IsCustomRun || !AscentCatalog.Registered || __instance.ascentIndex != -1)
            {
                return true;
            }
            // TFA offers its FINAL ASCENT right after the highest ascent whenever it is installed (its
            // own arrows go there the same way: the index, then the pass is redrawn).
            __instance.ascentIndex = Compat.Tfa.Present ? FinalAscentIndex : Progress.MaxSelectable;
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
            if (!custom && IsRegular(index) && index >= Lvl.First)
            {
                // Ascents 9-25: the Pain Sash on 25 while it is locked, else no reward line.
                bool shown = CosmeticUnlocks.Reward(index, out string name, out Color banner);
                __instance.reward.gameObject.SetActive(shown);
                if (shown)
                {
                    __instance.rewardText.text = name;
                    __instance.rewardImage.color = banner;
                }
            }
            TightenModifiersLine(__instance);
            RewardUnderText(__instance);
            RefreshStart(__instance);
        }
    }

    /// <summary>The game's empty line before "And all previous modifiers.", and the same line about half as tall.</summary>
    private const string GameGap = "\n\n<alpha=#CC><size=70%>";
    private const string TightGap = "\n<size=45%>\n</size><alpha=#CC><size=70%>";

    /// <summary>
    /// "And all previous modifiers." closer to the ascent's text (the empty line between them about half as tall), on every
    /// ascent that has it: room for the reward line under it, even under ascent 25's three lines.
    /// </summary>
    private static void TightenModifiersLine(BoardingPass pass)
    {
        TMP_Text description = pass.ascentDesc;
        if (description != null && description.text != null && description.text.Contains(GameGap))
        {
            description.text = description.text.Replace(GameGap, TightGap);
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
        int index = CustomRunAscent.OnPass(pass);
        if (index < Lvl.First || index > Lvl.Last)
        {
            return null;
        }
        if (MissingForTest != null)
        {
            return MissingForTest;
        }
        if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode)
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
        }
        else if (startBlocked)
        {
            pass.startGameButton.interactable = startWasInteractable;
            startBlocked = false;
        }
        Notice(pass, missing);
    }

    /// <summary>
    /// The players missing this version of the mod, named in a line over the pass at its top left (in the ascent text's
    /// lettering and the notice's colour), growing upward when it takes more than one line; hidden when nobody is missing.
    /// </summary>
    private static void Notice(BoardingPass pass, string missing)
    {
        if (notice == null)
        {
            if (missing == null)
            {
                return;
            }
            Transform panel = pass.ascentDesc != null && pass.ascentDesc.transform.parent != null ? pass.ascentDesc.transform.parent.parent : null;
            RectTransform card = panel != null ? panel.parent as RectTransform : null;
            if (card == null)
            {
                Plugin.Log.LogWarning("Boarding pass: no place for the version notice; it is put in the ascent's text.");
                pass.ascentDesc.text += $"\n\n<color=#FF9A7A><size=70%>{NoticeText(missing)}</size></color>";
                return;
            }
            GameObject holder = new GameObject("UltimateAscent Version Notice", typeof(RectTransform));
            RectTransform rect = (RectTransform)holder.transform;
            rect.SetParent(card, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(NoticeIndent, NoticeGap);
            rect.sizeDelta = new Vector2(card.rect.width - 2f * NoticeIndent, 0f);
            notice = holder.AddComponent<TextMeshProUGUI>();
            notice.font = pass.ascentDesc.font;
            notice.fontSharedMaterial = pass.ascentDesc.font.material;
            notice.fontSize = pass.ascentDesc.fontSize * 0.7f;
            notice.color = new Color32(0xFF, 0x9A, 0x7A, 0xFF);
            notice.alignment = TextAlignmentOptions.BottomLeft;
            notice.enableWordWrapping = true;
            notice.overflowMode = TextOverflowModes.Overflow;
            notice.raycastTarget = false;
        }
        notice.gameObject.SetActive(missing != null);
        if (missing != null)
        {
            notice.text = NoticeText(missing);
        }
    }

    private static string NoticeText(string missing) => $"Everyone needs Ultimate Ascent {Plugin.Version} for this ascent. Missing: {missing}";

    /// <summary>The line naming the players who miss the mod, while it shows (for the in-game test).</summary>
    internal static TMP_Text NoticeShown => notice != null && notice.gameObject.activeInHierarchy ? notice : null;

    /// <summary>
    /// The game's reward line (on the highest unlocked of its own ascents) right under the ascent's text, "And all previous
    /// modifiers.", at its left, in one row: "REWARD:", then the banner with the scout it gives. The game has the words
    /// over the banner at the card's bottom right, where the map picker is; under the text there is room for one row
    /// only. It is made smaller only as far as it must to fit between the text, the card's bottom and the map picker.
    /// </summary>
    private static void RewardUnderText(BoardingPass pass)
    {
        RectTransform reward = pass.reward != null ? pass.reward.transform as RectTransform : null;
        RectTransform card = reward != null ? reward.parent as RectTransform : null;
        if (card == null || !pass.reward.activeSelf || pass.ascentDesc == null)
        {
            return;
        }
        Rect text = TextArea(pass.ascentDesc, card);
        if (text.width <= 0f)
        {
            return;
        }
        rewardScale ??= reward.localScale;

        // "REWARD:" beside the banner, level with it (in the banner's own units).
        Rect banner = reward.rect;
        float width = banner.width;
        TMP_Text words = pass.reward.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t != pass.rewardText);
        if (words != null)
        {
            Rect letters = TextArea(words, reward);
            words.rectTransform.anchoredPosition += new Vector2(banner.xMin - RewardWordsGap - letters.xMax, banner.center.y - letters.center.y);
            width += letters.width + RewardWordsGap;
        }

        // The room under the text: down to the card's bottom, across to the map picker.
        float right = card.rect.xMax - RewardMargin;
        if (MapPicker.Line != null && MapPicker.Line.activeSelf && MapPicker.Line.transform.parent == card)
        {
            RectTransform picker = (RectTransform)MapPicker.Line.transform;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(picker);
            right = Mathf.Min(right, Area(picker, card).xMin - RewardMargin);
        }
        Rect room = Rect.MinMaxRect(text.xMin, card.rect.yMin, right, text.yMin - RewardGap);
        float fit = Mathf.Min(1f, room.width / (width * rewardScale.Value.x), room.height / (banner.height * rewardScale.Value.y));
        reward.localScale = rewardScale.Value * Mathf.Max(fit, RewardSmallest);

        // All of the line as it shows now: the banner and the words on it and beside it.
        Rect shown = Area(reward, card);
        foreach (TMP_Text part in pass.reward.GetComponentsInChildren<TMP_Text>())
        {
            Rect area = TextArea(part, card);
            shown = area.width > 0f ? Rect.MinMaxRect(Mathf.Min(shown.xMin, area.xMin), Mathf.Min(shown.yMin, area.yMin), Mathf.Max(shown.xMax, area.xMax), Mathf.Max(shown.yMax, area.yMax)) : shown;
        }
        reward.anchoredPosition += new Vector2(room.xMin - shown.xMin, room.yMax - shown.yMax);
    }

    /// <summary>Where a text's letters are, in another part's units.</summary>
    private static Rect TextArea(TMP_Text text, RectTransform within)
    {
        text.ForceMeshUpdate(true);
        Bounds bounds = text.textBounds;
        if (bounds.size.x <= 0f)
        {
            return Rect.zero;
        }
        Vector3 min = within.InverseTransformPoint(text.transform.TransformPoint(bounds.min));
        Vector3 max = within.InverseTransformPoint(text.transform.TransformPoint(bounds.max));
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    /// <summary>Where a part is, in another part's units.</summary>
    private static Rect Area(RectTransform part, RectTransform within)
    {
        Vector3[] corners = new Vector3[4];
        part.GetWorldCorners(corners);
        Vector3 min = within.InverseTransformPoint(corners[0]);
        Vector3 max = within.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }
}
