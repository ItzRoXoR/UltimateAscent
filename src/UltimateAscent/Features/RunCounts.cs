using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The boarding pass's ticket details tell the ascent's own story: GATE is the ascent's number (Tenderfoot and Peak are
/// 0), FROM becomes ATTEMPTS, the runs started on that ascent (on any map), and TO becomes COMPLETIONS, the runs on it
/// that someone won. Each player counts their own, in the mod's save: a run counts once, when its run id comes (a run
/// resumed from a campfire save keeps its id, so it is no new attempt), and a win when the game says someone has won
/// it. Runs that can earn the game's achievements, Tenderfoot to ascent 25: custom runs count nothing and show dashes;
/// True Final Ascent's FINAL ASCENT counts on its ascent's ticket, but not a chain of its Builder that TFA keeps from
/// earning achievements (lower than the regular mountain, or of too few biomes).
/// </summary>
internal static class RunCounts
{
    private const string StartedKey = "runs.started.";
    private const string WonKey = "runs.won.";
    private const string StartedRunsKey = "runs.started.ids";
    private const string WonRunsKey = "runs.won.ids";

    /// <summary>How many runs' ids are kept, so a resumed or twice-told run counts once.</summary>
    private const int RunsRemembered = 30;

    /// <summary>The room between the ticket's columns (between their letters), and kept before the pass's dotted line.</summary>
    private const float ColumnGap = 28f;
    private const float LineMargin = 14f;

    /// <summary>The columns begin this far left of the game's gate, with this room kept between them and the passenger's name.</summary>
    private const float MovedLeft = 40f;
    private const float NameGap = 24f;

    /// <summary>The columns' headings are never made smaller than this to fit, nor a long passenger's name before it is cut short.</summary>
    private const float SmallestHeading = 0.7f;
    private const float SmallestName = 0.7f;

    /// <summary>The run whose start has been looked at in this scene.</summary>
    private static Guid lookedAt;

    /// <summary>The pass whose columns were set out: its headings' and name's own size, and where its gate's letters began.</summary>
    private static BoardingPass setOut;
    private static float headingSize;
    private static float nameSize;
    private static float gateLeft;

    /// <summary>The passenger's whole name, and what was shown of it when it was cut short.</summary>
    private static string fullName;
    private static string shortName;

    private static bool Russian => LocalizedText.CURRENT_LANGUAGE == LocalizedText.Language.Russian;

    internal static int Started(int ascent) => int.TryParse(ModSave.Get(StartedKey + ascent), out int count) ? count : 0;

    internal static int Won(int ascent) => int.TryParse(ModSave.Get(WonKey + ascent), out int count) ? count : 0;

    /// <summary>Counted: a regular run on the game's ascents or the mod's.</summary>
    private static bool Counts(int ascent) => ascent >= -1 && ascent <= Lvl.Last && !RunSettings.IsCustomRun;

    /// <summary>The climb can earn the game's achievements (in the climb; True Final Ascent says so for its Builder's chains).</summary>
    private static bool EarnsAchievements => !RunSettings.blockingAchievements;

    internal static void Reset() => lookedAt = Guid.Empty;

    /// <summary>A run's id has come (the host makes it as the run starts, the others are sent it): its start counts once.</summary>
    internal static void Tick()
    {
        RunManager run = RunManager.Instance;
        if (run == null || run.RunId == Guid.Empty || run.RunId == lookedAt || Character.localCharacter == null || Character.localCharacter.inAirport)
        {
            return;
        }
        lookedAt = run.RunId;
        int ascent = Ascents.currentAscent;
        if (Counts(ascent) && EarnsAchievements && FirstTime(StartedRunsKey, run.RunId))
        {
            ModSave.Set(StartedKey + ascent, (Started(ascent) + 1).ToString());
            Plugin.Log.LogInfo($"Run started on ascent {ascent}: attempt {Started(ascent)} there.");
        }
    }

    /// <summary>Someone has won the run (on every player's game).</summary>
    [HarmonyPatch(typeof(GlobalEvents), nameof(GlobalEvents.TriggerSomeoneWonRun))]
    private static class RunWon
    {
        private static void Postfix() => CountWin();
    }

    internal static void CountWin()
    {
        int ascent = Ascents.currentAscent;
        Guid run = RunManager.Instance != null ? RunManager.Instance.RunId : Guid.Empty;
        if (Counts(ascent) && EarnsAchievements && (run == Guid.Empty || FirstTime(WonRunsKey, run)))
        {
            ModSave.Set(WonKey + ascent, (Won(ascent) + 1).ToString());
            Plugin.Log.LogInfo($"Run won on ascent {ascent}: completion {Won(ascent)} there.");
        }
    }

    /// <summary>The run has not been counted under this key yet; it is now (the latest runs' ids are kept).</summary>
    private static bool FirstTime(string key, Guid run)
    {
        List<string> ids = ModSave.Get(key).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        string id = run.ToString("N");
        if (ids.Contains(id))
        {
            return false;
        }
        ids.Add(id);
        ModSave.Set(key, string.Join(",", ids.Skip(Math.Max(0, ids.Count - RunsRemembered))));
        return true;
    }

    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.UpdateAscent))]
    private static class OnThePass
    {
        private static void Postfix(BoardingPass __instance) => Show(__instance);
    }

    /// <summary>The ticket's three columns for the ascent on the pass.</summary>
    private static void Show(BoardingPass pass)
    {
        Transform panel = pass.ascentDesc != null && pass.ascentDesc.transform.parent != null ? pass.ascentDesc.transform.parent.parent : null;
        TMP_Text gate = Text(panel, "Gate (1)");
        TMP_Text attemptsHeading = Text(panel, "From");
        TMP_Text attempts = Text(panel, "From (1)");
        TMP_Text completionsHeading = Text(panel, "To");
        TMP_Text completions = Text(panel, "To (1)");
        TMP_Text gateHeading = Text(panel, "Gate");
        if (gate == null || attemptsHeading == null || attempts == null || completionsHeading == null || completions == null || gateHeading == null)
        {
            return;
        }
        int ascent = pass.ascentIndex;
        bool counted = Counts(ascent);
        // The headings keep what is set here: the game's own translation of them is switched off.
        foreach (TMP_Text heading in new[] { attemptsHeading, completionsHeading })
        {
            foreach (LocalizedText localized in heading.GetComponents<LocalizedText>())
            {
                localized.enabled = false;
            }
        }
        attemptsHeading.text = Russian ? "ПОПЫТКИ:" : "ATTEMPTS:";
        completionsHeading.text = Russian ? "ПРОЙДЕНО:" : "COMPLETIONS:";
        gate.text = counted ? Mathf.Max(0, ascent).ToString() : "-";
        attempts.text = counted ? Started(ascent).ToString() : "-";
        completions.text = counted ? Won(ascent).ToString() : "-";
        SetOut(pass, (RectTransform)panel, new[] { (gateHeading, gate), (attemptsHeading, attempts), (completionsHeading, completions) });
    }

    private static TMP_Text Text(Transform panel, string name) => panel != null ? panel.Find(name)?.GetComponent<TMP_Text>() : null;

    /// <summary>
    /// The columns side by side a little left of the game's gate, each as wide as its heading or number and as far apart
    /// by their letters, up to the end of the ascent's box and the pass's dotted line; the headings made smaller only as
    /// far as they must to fit, and the passenger's name kept clear of them.
    /// </summary>
    private static void SetOut(BoardingPass pass, RectTransform panel, (TMP_Text heading, TMP_Text value)[] columns)
    {
        TMP_Text name = pass.playerName;
        if (setOut != pass)
        {
            setOut = pass;
            headingSize = columns[0].heading.fontSize;
            nameSize = name == null ? 0f : name.enableAutoSizing ? name.fontSizeMax : name.fontSize;
            gateLeft = Letters(columns[0].heading, panel).xMin;
        }
        float left = gateLeft - MovedLeft;
        float right = Area((RectTransform)pass.ascentDesc.transform.parent, panel).xMax;
        RectTransform line = panel.Find("DottedLine") as RectTransform;
        if (line != null)
        {
            right = Mathf.Min(right, Area(line, panel).xMin - LineMargin);
        }
        foreach ((TMP_Text heading, TMP_Text value) in columns)
        {
            foreach (TMP_Text text in new[] { heading, value })
            {
                text.enableAutoSizing = false;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
            }
            heading.fontSize = headingSize;
        }
        float Width((TMP_Text heading, TMP_Text value) column) => Mathf.Max(Letters(column.heading, panel).width, Letters(column.value, panel).width);
        // The headings grow smaller, the room between the columns does not.
        float scale = Mathf.Clamp((right - left - ColumnGap * (columns.Length - 1)) / columns.Sum(Width), SmallestHeading, 1f);
        float x = left;
        foreach ((TMP_Text heading, TMP_Text value) column in columns)
        {
            column.heading.fontSize = headingSize * scale;
            float width = Width(column);
            foreach (TMP_Text text in new[] { column.heading, column.value })
            {
                text.rectTransform.anchoredPosition += new Vector2(x - Letters(text, panel).xMin, 0f);
            }
            x += width + ColumnGap;
        }
        FitName(name, panel, left - NameGap);
    }

    /// <summary>The passenger's name ends before the columns: a long one is made smaller, then cut short.</summary>
    private static void FitName(TMP_Text name, RectTransform panel, float until)
    {
        if (name == null || nameSize <= 0f)
        {
            return;
        }
        // The game puts the whole name again as the pass opens.
        if (name.text != shortName)
        {
            fullName = name.text;
        }
        name.text = fullName;
        name.enableAutoSizing = false;
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Overflow;
        name.fontSize = nameSize;
        Rect letters = Letters(name, panel);
        if (letters.xMax <= until)
        {
            return;
        }
        name.fontSize = nameSize * Mathf.Max(SmallestName, (until - letters.xMin) / letters.width * 0.99f);
        for (int keep = fullName.Length - 1; keep > 0 && Letters(name, panel).xMax > until; keep--)
        {
            name.text = fullName.Substring(0, keep).TrimEnd() + "...";
        }
        shortName = name.text;
    }

    /// <summary>Where a text's letters are, in another part's units.</summary>
    private static Rect Letters(TMP_Text text, RectTransform within)
    {
        text.ForceMeshUpdate(true);
        Bounds bounds = text.textBounds;
        if (bounds.size.x <= 0f)
        {
            Rect area = Area(text.rectTransform, within);
            return new Rect(area.xMin, area.yMin, 0f, area.height);
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
