using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>The boarding pass's own parts against the mod's: the game's reward line, the version notice, the map picker.</summary>
internal sealed partial class Runner
{
    /// <summary>The highest ascent unlocked, as the game reads it, while a check fakes it (in memory; -1: the real one).</summary>
    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.GetMaxAscent))]
    internal static class FakeMaxAscent
    {
        internal static int To = -1;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref int __result)
        {
            if (To >= 0)
            {
                __result = To;
            }
        }
    }

    /// <summary>
    /// The boarding pass's parts (airport): the game's reward line on each of its own ascents (shown on the highest
    /// unlocked, here faked in memory) sits under the ascent's text in one row ("REWARD:", then the banner), on the card
    /// and clear of the map picker; the version notice is a line over the pass at its top left, seen on the screen; with
    /// "Map Biomes" on (in memory) every map but today's shows its biomes' letters; the ticket's columns are the ascent's
    /// GATE (Tenderfoot 0), its ATTEMPTS and its COMPLETIONS, evenly apart, on the ascent's box and clear of the
    /// passenger's name, even a long one.
    /// </summary>
    private IEnumerator PassLayoutCheck()
    {
        BoardingPass pass = GUIManager.instance.boardingPass;
        int before = pass.ascentIndex;
        int pickedBefore = MapPicker.Picked;
        RectTransform card = pass.ascentDesc.transform.parent as RectTransform;
        // Once the pass has come in (it slides in as it opens).
        yield return new WaitForSecondsRealtime(1.5f);
        try
        {
            // The reward line, on each of the game's ascents.
            List<string> wrong = new List<string>();
            List<string> seen = new List<string>();
            for (int ascent = 0; ascent <= 8; ascent++)
            {
                FakeMaxAscent.To = ascent;
                pass.ascentIndex = ascent;
                pass.UpdateAscent();
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                Rect text = TextRect(pass.ascentDesc);
                Rect reward = RewardRect(pass);
                Rect banner = Screen((RectTransform)pass.reward.transform);
                TMP_Text words = pass.reward.GetComponentsInChildren<TMP_Text>().FirstOrDefault(w => w != pass.rewardText);
                Rect wordsRect = words != null ? TextRect(words) : Rect.zero;
                Rect picker = MapPicker.Line != null && MapPicker.Line.activeInHierarchy ? Screen((RectTransform)MapPicker.Line.transform) : Rect.zero;
                Rect panel = Screen(card);
                bool shown = pass.reward.activeInHierarchy;
                bool under = reward.yMax <= text.yMin - 4f && Mathf.Abs(reward.xMin - text.xMin) <= 4f;
                bool row = words != null && wordsRect.xMax <= banner.xMin && Mathf.Abs(wordsRect.center.y - banner.center.y) < 4f;
                bool clear = picker.width <= 0f || !reward.Overlaps(picker);
                bool inside = reward.yMin >= panel.yMin && reward.xMax <= panel.xMax;
                float size = pass.reward.transform.localScale.x;
                seen.Add($"{ascent} '{Plain(pass.rewardText.text)}' x{size:0.00}");
                if (!shown || !under || !row || !clear || !inside || size < 0.6f)
                {
                    wrong.Add($"{ascent}: shown {shown}, under the text {under} ({Describe(reward)} under {Describe(text)}), in a row {row}, clear of the picker {clear} ({Describe(picker)}), "
                        + $"on the card {inside} ({Describe(panel)}), size x{size:0.00}");
                }
                if (ascent == 5 || ascent == 8)
                {
                    Screenshot("pass-reward-" + ascent);
                    yield return new WaitForSecondsRealtime(0.3f);
                }
            }
            FakeMaxAscent.To = -1;
            Check("the game's reward line sits under the ascent's text, at its left, in one row, on the card and clear of the map picker (ascents 0-8, the highest unlocked faked)",
                wrong.Count == 0, wrong.Count > 0 ? string.Join("; ", wrong) : string.Join("; ", seen));

            // The version notice, on an ascent of the mod's.
            pass.ascentIndex = 20;
            BoardingPassUi.MissingForTest = "Autosar";
            pass.UpdateAscent();
            yield return null;
            Canvas.ForceUpdateCanvases();
            TMP_Text notice = BoardingPassUi.NoticeShown;
            Rect noticeText = notice != null ? TextRect(notice) : Rect.zero;
            Rect pane = Screen((RectTransform)card.parent);
            bool blocked = !pass.startGameButton.interactable;
            List<float> seenColour = new List<float>();
            yield return ColourShown(noticeText, new Color32(0xFF, 0x9A, 0x7A, 0xFF), seenColour);
            float coloured = seenColour.DefaultIfEmpty(0f).First();
            Check("the version notice is a line over the pass, at its top left, seen on the screen, and the start stays blocked (20)",
                notice != null && notice.text.Contains("Missing: Autosar") && noticeText.yMin >= pane.yMax && noticeText.yMin - pane.yMax < 40f
                && noticeText.xMin >= pane.xMin && noticeText.xMin - pane.xMin < 80f && coloured > 0.03f && !Plain(pass.ascentDesc.text).Contains("Everyone needs") && blocked,
                notice == null ? "no notice" : $"'{Plain(notice.text)}' at {Describe(noticeText)}, the pass {Describe(pane)}, {coloured:P0} of its place in its colour on the screen; "
                    + $"in the ascent's text too {Plain(pass.ascentDesc.text).Contains("Everyone needs")}, start blocked {blocked}");
            Screenshot("pass-version-notice");
            yield return new WaitForSecondsRealtime(0.3f);
            BoardingPassUi.MissingForTest = null;
            pass.UpdateAscent();
            yield return null;
            Check("with nobody missing the mod, the notice is gone and the start can be used", BoardingPassUi.NoticeShown == null && pass.startGameButton.interactable,
                $"notice {(BoardingPassUi.NoticeShown != null ? "shown" : "gone")}, start {pass.startGameButton.interactable}");

            // The maps' biomes.
            pass.ascentIndex = before;
            MapPicker.Picked = MapPicker.Today;
            UltimateAscent.Plugin.MapBiomes.Value = true;
            yield return null;
            MapBaker baker = SingletonAsset<MapBaker>.Instance;
            Dictionary<(int, char), char> english = new Dictionary<(int, char), char>
            {
                [(0, 'S')] = 'S', [(1, 'T')] = 'T', [(1, 'R')] = 'R', [(2, 'A')] = 'A', [(2, 'M')] = 'M', [(3, 'V')] = 'C', [(3, 'S')] = 'G',
            };
            string Expected(int map) => new string(baker.BiomeIDs[map].Select((c, part) => english.TryGetValue((part, c), out char letter) ? letter : '?').ToArray());
            List<string> mismatched = new List<string>();
            foreach (int map in MapPicker.Order())
            {
                string name = MapPicker.Name(map);
                string wanted = map == MapPicker.Today ? $"TODAY'S MAP ({MapPicker.Number(map)})" : $"MAP {MapPicker.Number(map)} ({Expected(map)})";
                if (name != wanted)
                {
                    mismatched.Add($"'{name}' (expected '{wanted}')");
                }
            }
            bool englishShown = LocalizedText.CURRENT_LANGUAGE == LocalizedText.Language.English;
            MapPicker.Step(1);
            yield return null;
            Canvas.ForceUpdateCanvases();
            string shownName = Plain(MapPicker.Label.text);
            bool fits = MapPicker.Label.preferredWidth <= MapPicker.Label.rectTransform.rect.width + 1f;
            Rect pickerNow = Screen((RectTransform)MapPicker.Line.transform);
            bool offText = !LineRects(pass.ascentDesc).Any(pickerNow.Overlaps);
            Check("Map Biomes on: every map but today's shows its biomes' first letters after its number, from the game's map list (STMC: Shore, Tropics, Mesa, Caldera)",
                (!englishShown || mismatched.Count == 0) && shownName.EndsWith(")") && fits && offText,
                $"{MapPicker.Count} maps, {mismatched.Count} named otherwise{(mismatched.Count > 0 ? ": " + string.Join(", ", mismatched.Take(5)) : "")}; shown '{shownName}', fits its outline {fits}, clear of the ascent's text {offText}"
                + (englishShown ? "" : $"; the game is in {LocalizedText.CURRENT_LANGUAGE}, the letters are its names'"));
            Screenshot("map-picker-biomes");
            yield return new WaitForSecondsRealtime(0.3f);
            LocalizedText.Language language = LocalizedText.CURRENT_LANGUAGE;
            LocalizedText.CURRENT_LANGUAGE = LocalizedText.Language.Russian;
            string russian = MapPicker.Name(MapPicker.Picked);
            LocalizedText.CURRENT_LANGUAGE = language;
            Info("a map's name in Russian", $"{russian} ({string.Join(" ", russian.Select(c => c < 128 ? c.ToString() : $"U+{(int)c:X4}"))})");
            UltimateAscent.Plugin.MapBiomes.Value = false;
            yield return null;
            Check("Map Biomes off: the maps are named by their number only", Plain(MapPicker.Label.text) == $"MAP {MapPicker.Number(MapPicker.Picked)}", Plain(MapPicker.Label.text));

            // The ticket's columns.
            Transform ticket = card.parent;
            TMP_Text Part(string name) => ticket.Find(name)?.GetComponent<TMP_Text>();
            RectTransform line = ticket.Find("DottedLine") as RectTransform;
            List<string> ticketWrong = new List<string>();
            List<string> ticketSeen = new List<string>();
            foreach (int ascent in new[] { -1, 0, 5, 24 })
            {
                pass.ascentIndex = ascent;
                pass.UpdateAscent();
                yield return null;
                Canvas.ForceUpdateCanvases();
                string[] names = { "Gate", "Gate (1)", "From", "From (1)", "To", "To (1)" };
                TMP_Text[] parts = names.Select(Part).ToArray();
                if (parts.Any(p => p == null))
                {
                    ticketWrong.Add($"{ascent}: parts missing: {string.Join(", ", names.Where((n, i) => parts[i] == null))}");
                    continue;
                }
                Rect[] rects = parts.Select(TextRect).ToArray();
                string[] shown = parts.Select(p => Plain(p.text)).ToArray();
                bool words = shown[2] == "ATTEMPTS:" && shown[4] == "COMPLETIONS:" && shown[1] == Mathf.Max(0, ascent).ToString()
                    && shown[3] == RunCounts.Started(ascent).ToString() && shown[5] == RunCounts.Won(ascent).ToString();
                bool apart = rects[0].xMax < rects[2].xMin && rects[1].xMax < rects[3].xMin && rects[2].xMax < rects[4].xMin && rects[3].xMax < rects[5].xMin;
                bool beforeLine = line == null || Mathf.Max(rects[4].xMax, rects[5].xMax) < Screen(line).xMin;
                bool aligned = Mathf.Abs(rects[2].xMin - rects[3].xMin) < 6f && Mathf.Abs(rects[4].xMin - rects[5].xMin) < 6f;
                float firstGap = rects[2].xMin - Mathf.Max(rects[0].xMax, rects[1].xMax);
                float secondGap = rects[4].xMin - Mathf.Max(rects[2].xMax, rects[3].xMax);
                bool even = Mathf.Abs(firstGap - secondGap) < 3f;
                bool onBox = Mathf.Max(rects[4].xMax, rects[5].xMax) <= Screen(card).xMax + 1f;
                Rect nameShown = TextRect(pass.playerName);
                bool clearOfName = nameShown.xMax < Mathf.Min(rects[0].xMin, rects[1].xMin) - 10f;
                ticketSeen.Add($"{ascent}: {shown[0]} {shown[1]} / {shown[2]} {shown[3]} / {shown[4]} {shown[5]}, from x {rects[0].xMin:0} to {Mathf.Max(rects[4].xMax, rects[5].xMax):0}, gaps {firstGap:0} and {secondGap:0}");
                if (!words || !apart || !beforeLine || !aligned || !even || !onBox || !clearOfName)
                {
                    ticketWrong.Add($"{ascent}: '{string.Join(" ", shown)}' words {words}, apart {apart}, before the dotted line {beforeLine} ({(line != null ? Describe(Screen(line)) : "none")}), "
                        + $"aligned {aligned}, evenly apart {even} ({firstGap:0}, {secondGap:0}), on the box {onBox} ({Describe(Screen(card))}), clear of the name {clearOfName} ({Describe(nameShown)}); "
                        + string.Join(" ", rects.Select(Describe)));
                }
                if (ascent == 24)
                {
                    Screenshot("pass-ticket");
                    yield return new WaitForSecondsRealtime(0.3f);
                }
            }
            Check("the ticket shows the ascent's GATE (Tenderfoot 0), its ATTEMPTS and its COMPLETIONS from the mod's save, evenly apart, on the ascent's box and clear of the passenger's name",
                ticketWrong.Count == 0, ticketWrong.Count > 0 ? string.Join("; ", ticketWrong) : string.Join("; ", ticketSeen));

            // A long name (Steam's longest) is made smaller, then cut short, before the columns.
            pass.playerName.text = new string('W', 32);
            pass.UpdateAscent();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Rect longName = TextRect(pass.playerName);
            Rect gateShown = TextRect(Part("Gate"));
            Screenshot("pass-ticket-long-name");
            yield return new WaitForSecondsRealtime(0.3f);
            Check("a long passenger's name ends before the ticket's columns",
                longName.width > 0f && longName.xMax < gateShown.xMin - 10f,
                $"'{Plain(pass.playerName.text)}' at size {pass.playerName.fontSize:0.#} {Describe(longName)}, the gate {Describe(gateShown)}");
            pass.playerName.text = Character.localCharacter.characterName;
            pass.UpdateAscent();
            yield return null;
            Info("the passenger's own name", $"'{Plain(pass.playerName.text)}' at size {pass.playerName.fontSize:0.#} {Describe(TextRect(pass.playerName))}");
            attemptsBefore = RunCounts.Started(target);
        }
        finally
        {
            FakeMaxAscent.To = -1;
            BoardingPassUi.MissingForTest = null;
            UltimateAscent.Plugin.MapBiomes.Value = false;
            MapPicker.Picked = pickedBefore;
            pass.ascentIndex = before;
            pass.playerName.text = Character.localCharacter.characterName;
            pass.UpdateAscent();
        }
    }

    /// <summary>The attempts on the tested ascent before its climb (-1: not looked at).</summary>
    private int attemptsBefore = -1;

    /// <summary>
    /// The climb counts as an attempt on its ascent, once (its run id is kept); a win counts once too, however often the
    /// game tells it (the mod's own count, called here: the game's win would also write the Steam stats).
    /// </summary>
    private IEnumerator RunCountsCheck()
    {
        if (attemptsBefore < 0)
        {
            Info("the attempts check is skipped", "the boarding pass check did not run");
            yield break;
        }
        yield return new WaitForSeconds(1f);
        int attempts = RunCounts.Started(target);
        int won = RunCounts.Won(target);
        RunCounts.CountWin();
        RunCounts.CountWin();
        Check("the climb counts as one attempt on its ascent, and a win as one completion however often it is told",
            attempts == attemptsBefore + 1 && RunCounts.Won(target) == won + 1,
            $"ascent {target}: attempts {attemptsBefore} -> {attempts} (run {RunManager.Instance?.RunId}); completions {won} -> {RunCounts.Won(target)} after the win told twice");
    }

    /// <summary>The reward line as it shows: the banner and the words on it and over it.</summary>
    private static Rect RewardRect(BoardingPass pass)
    {
        Rect all = Screen((RectTransform)pass.reward.transform);
        foreach (Transform part in pass.reward.transform)
        {
            Rect area = part.TryGetComponent(out TMP_Text words) ? TextRect(words) : Screen((RectTransform)part);
            if (part.gameObject.activeSelf && area.width > 0f)
            {
                all = Rect.MinMaxRect(Mathf.Min(all.xMin, area.xMin), Mathf.Min(all.yMin, area.yMin), Mathf.Max(all.xMax, area.xMax), Mathf.Max(all.yMax, area.yMax));
            }
        }
        return all;
    }

    /// <summary>Where each line of a text is on the screen, in pixels.</summary>
    private static IEnumerable<Rect> LineRects(TMP_Text text)
    {
        text.ForceMeshUpdate(true);
        TMP_TextInfo info = text.textInfo;
        for (int i = 0; i < info.lineCount; i++)
        {
            TMP_LineInfo line = info.lineInfo[i];
            if (line.characterCount > 0 && line.lineExtents.max.x > line.lineExtents.min.x)
            {
                yield return ScreenRectOf(text.transform, new Vector3[] { line.lineExtents.min, line.lineExtents.max });
            }
        }
    }

    /// <summary>How much of a place on the screen shows a colour (as the frame ends: a share of its pixels close to it).</summary>
    private static IEnumerator ColourShown(Rect place, Color32 colour, List<float> result)
    {
        yield return new WaitForEndOfFrame();
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            int near = 0, all = 0;
            for (int y = Mathf.Max(0, (int)place.yMin); y < Mathf.Min(shot.height, (int)place.yMax); y += 2)
            {
                for (int x = Mathf.Max(0, (int)place.xMin); x < Mathf.Min(shot.width, (int)place.xMax); x += 2)
                {
                    Color32 pixel = shot.GetPixel(x, y);
                    all++;
                    near += Mathf.Abs(pixel.r - colour.r) < 40 && Mathf.Abs(pixel.g - colour.g) < 40 && Mathf.Abs(pixel.b - colour.b) < 40 ? 1 : 0;
                }
            }
            result.Add(all > 0 ? (float)near / all : 0f);
        }
        finally
        {
            Destroy(shot);
        }
    }

    /// <summary>Where a text's letters are on the screen, in pixels.</summary>
    private static Rect TextRect(TMP_Text text)
    {
        text.ForceMeshUpdate(true);
        Bounds bounds = text.textBounds;
        if (bounds.size.x <= 0f)
        {
            return Rect.zero;
        }
        return ScreenRectOf(text.transform, new[] { bounds.min, bounds.max });
    }

    /// <summary>Where a part is on the screen, in pixels.</summary>
    private static Rect Screen(RectTransform part)
    {
        Vector3[] corners = new Vector3[4];
        part.GetWorldCorners(corners);
        return ScreenRectOf(null, corners);
    }

    private static Rect ScreenRectOf(Transform local, Vector3[] points)
    {
        Canvas canvas = (local != null ? local : null)?.GetComponentInParent<Canvas>() ?? GUIManager.instance.boardingPass.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        IEnumerable<Vector2> onScreen = points.Select(p => RectTransformUtility.WorldToScreenPoint(camera, local != null ? local.TransformPoint(p) : p));
        return Rect.MinMaxRect(onScreen.Min(p => p.x), onScreen.Min(p => p.y), onScreen.Max(p => p.x), onScreen.Max(p => p.y));
    }

    private static string Describe(Rect rect) => rect.width <= 0f ? "nowhere" : $"[{rect.xMin:0},{rect.yMin:0} - {rect.xMax:0},{rect.yMax:0}]";
}
