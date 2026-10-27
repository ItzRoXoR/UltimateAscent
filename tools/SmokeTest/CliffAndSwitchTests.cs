using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the snakes on the Tropics' cliffs, the bees' checks, the Hot Campfires switch and the modifiers line.</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The config's Hot Campfires (first in its [Ascents] list): with it on, one cooking cycle at a lit campfire chars the
    /// held food to ruin (17); with it off (in memory), the same cycle only cooks it, on this ascent too.
    /// </summary>
    private IEnumerator HotCampfiresSwitch()
    {
        ConfigEntry<bool> entry = UltimateAscent.Plugin.HotCampfires;
        string file = UltimateAscent.Plugin.Instance.Config.ConfigFilePath;
        string[] lines = File.Exists(file) ? File.ReadAllLines(file) : new string[0];
        int section = Array.FindIndex(lines, l => l.Trim() == "[Ascents]");
        string first = section < 0 ? "" : lines.Skip(section + 1).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("#")) ?? "";
        Check("the config has the Hot Campfires switch, first in its [Ascents] list", entry != null && first.StartsWith("Hot Campfires = "),
            $"bound {entry != null}; the list's first entry '{first}'");
        if (entry == null || !Rules.Enabled(Lvl.HotCampfires))
        {
            Info("the Hot Campfires cooking check is skipped", $"testing ascent {target} (the hot campfires are ascent 17's)");
            yield break;
        }
        Character local = Local;
        if (!Campfire.ALL_CAMPFIRES.Any(c => c != null && c.Lit))
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(6f);
        }
        Campfire campfire = Campfire.ALL_CAMPFIRES.Where(c => c != null && c.Lit).OrderBy(c => Vector3.Distance(c.transform.position, local.Center)).FirstOrDefault();
        Item food = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values
            .Where(i => i != null && i.UIData != null && i.UIData.canPocket && i.GetComponent<ItemCooking>() is ItemCooking cooking && cooking.canBeCooked && !cooking.wreckWhenCooked && i.GetComponent<Action_RestoreHunger>() != null)
            .OrderBy(i => i.itemID)
            .FirstOrDefault();
        if (campfire == null || food == null)
        {
            Info("the Hot Campfires cooking check is skipped", $"lit campfire {campfire != null}, cookable food {food != null}");
            yield break;
        }
        bool saved = entry.Value;
        (bool burned, int cooked, bool rule)[] results = new (bool, int, bool)[2];
        try
        {
            for (int pass = 0; pass < 2; pass++)
            {
                entry.Value = pass == 0;
                HostOptions.Invalidate();
                yield return SpawnIntoHand(food);
                Item item = held;
                if (item == null)
                {
                    yield break;
                }
                Vector3 toCamp = MapHandler.CurrentBaseCampSpawnPoint != null ? MapHandler.CurrentBaseCampSpawnPoint.position - campfire.transform.position : local.Center - campfire.transform.position;
                toCamp.y = 0f;
                Vector3 standAt = campfire.transform.position + (toCamp.sqrMagnitude > 0.01f ? toCamp.normalized : Vector3.forward) * 1.6f + Vector3.up;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standAt, false);
                for (float until = Time.time + 3f; Time.time < until && Vector3.Distance(local.Center, campfire.transform.position) > 3f;)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(0.3f);
                Info($"cooking at the campfire with Hot Campfires {(pass == 0 ? "on" : "off")}", $"{Vector3.Distance(local.Center, campfire.transform.position):0.0} m from {campfire.name} (lit {campfire.Lit}), holding {item.GetName()} (holder {(item.holderCharacter != null ? item.holderCharacter.characterName : "none")})");
                // One cooking cycle at the campfire, as in the second segment's check.
                campfire.Interact(local);
                item.cooking.FinishCooking();
                campfire.CancelCast(local);
                yield return new WaitForSeconds(0.5f);
                results[pass] = (item != null && BurnedItems.IsBurned(item), item != null ? item.GetData<IntItemData>(DataEntryKey.CookedAmount).Value : -1, A17_HotCampfires.On);
                Screenshot(pass == 0 ? "hot-campfires-on" : "hot-campfires-off");
                yield return new WaitForSeconds(1f);
            }
        }
        finally
        {
            entry.Value = saved;
            HostOptions.Invalidate();
        }
        Check("with Hot Campfires on, one cooking cycle at a campfire chars the food to ruin (17)", results[0].rule && results[0].burned,
            $"rule {results[0].rule}: cooked {results[0].cooked}, burned {results[0].burned}");
        Check("with Hot Campfires off, the same cycle only cooks it, on ascent 17 too", !results[1].rule && !results[1].burned && results[1].cooked == 1,
            $"rule {results[1].rule}: cooked {results[1].cooked}, burned {results[1].burned}");
    }

    /// <summary>
    /// Under the ascent at the top right of the climb, the modifiers line: "ALL MODIFIERS ON" with every switch of this
    /// ascent on; with ascent 14's switch, the cold nights and the hot campfires off (in memory) it names them; just under
    /// the ascent's letters, lined up with them; back as it was afterwards.
    /// </summary>
    private IEnumerator ModifiersLineCheck()
    {
        ModifiersLine line = FindObjectsByType<ModifiersLine>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (line == null || line.main == null)
        {
            Fail("the modifiers line is under the ascent at the top right of the climb", "the game's ascent label has no line under it");
            yield break;
        }
        List<ConfigEntry<bool>> switches = new List<ConfigEntry<bool>> { UltimateAscent.Plugin.ColdNights, UltimateAscent.Plugin.Ascent8, UltimateAscent.Plugin.HotCampfires };
        switches.AddRange(Enumerable.Range(Lvl.First, Lvl.Count).Select(l => UltimateAscent.Plugin.AscentSwitches[l]));
        Dictionary<ConfigEntry<bool>, bool> saved = switches.ToDictionary(s => s, s => s.Value);
        string allOn = "", someOff = "", wanted = "";
        bool under = false;
        string where = "";
        try
        {
            foreach (ConfigEntry<bool> entry in switches)
            {
                entry.Value = true;
            }
            HostOptions.Invalidate();
            yield return new WaitForSeconds(0.6f);
            allOn = line.Shown;
            Screenshot("modifiers-all-on");
            yield return new WaitForSeconds(1f);
            UltimateAscent.Plugin.AscentSwitches[Lvl.StrongerAilments].Value = false;
            UltimateAscent.Plugin.ColdNights.Value = false;
            UltimateAscent.Plugin.HotCampfires.Value = false;
            HostOptions.Invalidate();
            yield return new WaitForSeconds(0.6f);
            someOff = line.Shown;
            wanted = "MODIFIERS OFF: " + string.Join(", ", new[] { Ascents._currentAscent >= Lvl.StrongerAilments ? "14" : null, "COLD NIGHTS", Ascents._currentAscent >= Lvl.HotCampfires ? "HOT CAMPFIRES" : null }.Where(w => w != null));
            // Just under the ascent's letters (on the screen, its top below their bottom, at most a line's height lower), lined up with their right end.
            TMPro.TMP_Text main = line.main;
            TMPro.TMP_Text own = line.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t => t != main);
            if (own != null)
            {
                main.ForceMeshUpdate();
                own.ForceMeshUpdate();
                Vector3 lettersBottom = main.transform.TransformPoint(new Vector3(main.textBounds.max.x, main.textBounds.min.y, 0f));
                Vector3 lineTop = own.transform.TransformPoint(new Vector3(own.textBounds.max.x, own.textBounds.max.y, 0f));
                float gap = lettersBottom.y - lineTop.y;
                float height = (main.transform.TransformPoint(new Vector3(0f, main.textBounds.max.y, 0f)) - main.transform.TransformPoint(new Vector3(0f, main.textBounds.min.y, 0f))).magnitude;
                under = gap >= -1f && gap <= height && Mathf.Abs(lettersBottom.x - lineTop.x) <= height * 0.5f;
                where = $"{gap:0.#} under the letters (their height {height:0.#}), right ends {lettersBottom.x:0} and {lineTop.x:0}";
            }
            Screenshot("modifiers-some-off");
            yield return new WaitForSeconds(1f);
        }
        finally
        {
            foreach (KeyValuePair<ConfigEntry<bool>, bool> s in saved)
            {
                s.Key.Value = s.Value;
            }
            HostOptions.Invalidate();
        }
        yield return new WaitForSeconds(0.6f);
        Check("under the ascent at the top right, the line says all modifiers are on when every switch of this ascent is", allOn == "ALL MODIFIERS ON", $"'{allOn}' at ascent {Ascents._currentAscent}");
        Check("with switches off it names them (ascent 14, the cold nights, the hot campfires)", someOff == wanted, $"'{someOff}' (wanted '{wanted}')");
        Check("the line sits just under the ascent's letters, lined up with them", under, where);
        Info("the modifiers line with the config's own switches", $"'{line.Shown}'");
    }

    /// <summary>
    /// The bees' checks (17): one as the night begins and one as the day begins (the clock put just before each), every
    /// scout rolled for a moment of their own within that night or day; scouts' moments at least 30 s of the clock apart
    /// (drawn for four); a scout's moment coming as the clock goes past it (outside the Tropics, with no swarm); a clock set
    /// back before the night begins brings no check.
    /// </summary>
    private IEnumerator BeeMoments()
    {
        DayNightManager clock = DayNightManager.instance;
        if (clock == null || Map.CurrentPlace == Place.Tropics || !Rules.Enabled(Lvl.Hazards))
        {
            Info("the bee checks are skipped", clock == null ? "no day-night clock" : Map.CurrentPlace == Place.Tropics ? "the scout is in the Tropics" : $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        float original = clock.timeOfDay;
        float chance = A17_BeeAmbush.Chance;
        A17_BeeAmbush.Chance = 1f;
        A17_BeeAmbush.Checks(clock, out float dayBegins, out float nightBegins);
        try
        {
            foreach ((string name, float begins, bool day) in new[] { ("night", nightBegins, false), ("day", dayBegins, true) })
            {
                clock.timeOfDay = Mathf.Repeat(begins - 0.03f, 24f);
                // Put forward past the night's start (from the evening to just before dawn), the clock went by a check
                // there: that one is not waited for.
                yield return null;
                yield return null;
                int checks = A17_BeeAmbush.ChecksMade;
                A17_BeeAmbush.lastCheckAt = -1000f;
                float deadline = Time.time + 5f;
                while (A17_BeeAmbush.ChecksMade == checks && Time.time < deadline)
                {
                    yield return null;
                }
                float at = clock.timeOfDay;
                (bool isDay, float from, float to) stretch = A17_BeeAmbush.Stretch;
                bool rolled = A17_BeeAmbush.MomentOf(local, out float moment);
                float into = Mathf.Repeat(moment - stretch.from, 24f);
                float length = Mathf.Repeat(stretch.to - stretch.from, 24f);
                Check($"the bees' check comes as the {name} begins, and gives the scout a moment of their own within the {name} (17)",
                    A17_BeeAmbush.ChecksMade > checks && stretch.isDay == day && Mathf.Abs(Mathf.DeltaAngle(at * 15f, begins * 15f)) / 15f < 0.05f
                    && rolled && !float.IsNaN(moment) && into > 0f && into <= length,
                    $"check at {clock.FloatToTimeString(at)} (the {name} begins {clock.FloatToTimeString(begins)}); the {(stretch.isDay ? "day" : "night")} {clock.FloatToTimeString(stretch.from)}-{clock.FloatToTimeString(stretch.to)}; "
                    + $"the scout's moment {(rolled && !float.IsNaN(moment) ? clock.FloatToTimeString(moment) : "none")}");
                if (!day)
                {
                    // The scout's moment comes as the clock goes past it (here, out of the Tropics, it brings no swarm).
                    int come = A17_BeeAmbush.MomentsCome;
                    int swarms = FindObjectsByType<BeeSwarm>(FindObjectsSortMode.None).Count(A17_BeeAmbush.IsAmbush);
                    clock.timeOfDay = Mathf.Repeat(moment - 0.03f, 24f);
                    deadline = Time.time + 5f;
                    while (A17_BeeAmbush.MomentsCome == come && Time.time < deadline)
                    {
                        yield return null;
                    }
                    float when = clock.timeOfDay;
                    A17_BeeAmbush.MomentOf(local, out float after);
                    int swarmsAfter = FindObjectsByType<BeeSwarm>(FindObjectsSortMode.None).Count(A17_BeeAmbush.IsAmbush);
                    Check("the scout's moment comes as the clock goes past it; out of the Tropics it brings no swarm (17)",
                        A17_BeeAmbush.MomentsCome > come && Mathf.Abs(Mathf.DeltaAngle(when * 15f, moment * 15f)) / 15f < 0.05f && float.IsPositiveInfinity(after) && swarmsAfter == swarms,
                        $"at {clock.FloatToTimeString(when)} (the moment {clock.FloatToTimeString(moment)}); spent {float.IsPositiveInfinity(after)}; ambush swarms {swarms} -> {swarmsAfter}");
                    // A clock set back before the night begins is not the night beginning again.
                    checks = A17_BeeAmbush.ChecksMade;
                    clock.timeOfDay = Mathf.Repeat(nightBegins - 0.2f, 24f);
                    yield return null;
                    yield return null;
                    Check("a clock set back to before the night begins brings no check (17)", A17_BeeAmbush.ChecksMade == checks,
                        $"{clock.FloatToTimeString(when)} -> {clock.FloatToTimeString(clock.timeOfDay)}: {A17_BeeAmbush.ChecksMade - checks} check(s)");
                }
            }
        }
        finally
        {
            clock.timeOfDay = original;
            A17_BeeAmbush.Chance = chance;
        }
        // Four scouts' moments in one night, drawn as the checks draw them: at least 30 s of the clock apart.
        float apart = A17_BeeAmbush.ApartSeconds / (clock.dayLengthInMinutes * 60f) * 24f;
        float night = Mathf.Repeat(dayBegins - nightBegins, 24f);
        float closest = float.PositiveInfinity;
        for (int trial = 0; trial < 200; trial++)
        {
            List<float> drawn = new List<float>();
            for (int scout = 0; scout < 4; scout++)
            {
                drawn.Add(A17_BeeAmbush.PickMoment(0.01f, night, drawn, apart));
            }
            for (int i = 0; i < drawn.Count; i++)
            {
                for (int j = i + 1; j < drawn.Count; j++)
                {
                    closest = Mathf.Min(closest, Mathf.Abs(drawn[i] - drawn[j]));
                }
            }
        }
        Check("four scouts' moments in a night are always at least 30 s apart, so no two are set upon at once (17)", closest >= apart - 1e-4f,
            $"the closest two of 200 nights' draws {closest * clock.dayLengthInMinutes * 60f / 24f:0.#} s apart (the night {night * clock.dayLengthInMinutes * 60f / 24f:0} s long)");
    }

    /// <summary>
    /// The ground snakes on the Tropics' cliffs (17): a cliff found near their spots (a steep rock face 4-12 m high, ground
    /// at its foot and on its top); a snake at its foot told to wander to its top gets onto the face and climbs it (as a
    /// snake does, winding), and from the top told to wander to its foot, crawls over the edge and down it; pictured on
    /// the way.
    /// </summary>
    private IEnumerator SnakeClimbChecks()
    {
        Character local = Local;
        if (Map.CurrentPlace != Place.Tropics)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(8f);
        }
        for (float until = Time.time + 10f; Time.time < until && Snakes.All.Count == 0;)
        {
            yield return null;
        }
        List<Snake> grounded = Snakes.All.Where(s => s != null && !s.IsTree).ToList();
        if (Map.CurrentPlace != Place.Tropics || grounded.Count == 0)
        {
            Info("the snakes' cliff checks are skipped", $"no ground snakes out ({Map.CurrentPlace}); a map with the Tropics, as 80");
            yield break;
        }
        List<(Vector3 top, Vector3 foot)> cliffs = TropicsCliffs(grounded.Select(s => s.Home).ToList(), 4);
        Info("cliffs found near the ground snakes", string.Join("; ", cliffs.Select(c => $"{c.top:F0} over {c.foot:F0} ({c.top.y - c.foot.y:0.0} m)")));
        if (cliffs.Count == 0)
        {
            Fail("a cliff of the Tropics near the ground snakes, to climb", "none found");
            yield break;
        }
        Protect(true);
        A17_BeeAmbush.HeldOff = true;
        Snake snake = grounded.OrderBy(s => Vector3.Distance(s.Head, cliffs[0].foot)).First();
        foreach (Snake other in grounded.Where(s => s != snake))
        {
            other.Ignore();
        }
        int up = 0, down = 0, tried = 0;
        string how = "";
        try
        {
            for (int c = 0; c < cliffs.Count && tried < 3; c++, tried++)
            {
                (Vector3 top, Vector3 foot) = cliffs[c];
                // The scout watches from close by (a snake with nobody within 120 m rests): where the cliff is in sight, or
                // else a few metres out from its foot.
                Vector3 outFrom = Vector3.ProjectOnPlane(foot - top, Vector3.up).normalized;
                Vector3 watch = StandNear(Vector3.Lerp(foot, top, 0.5f), 5f, 9f, seeIt: true, firm: false)
                    ?? (Physics.Raycast(foot + outFrom * 4f + Vector3.up * 3f, Vector3.down, out RaycastHit outThere, 8f, HelperFunctions.terrainMapMask) ? outThere.point : foot + outFrom * 4f);
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, watch + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(1.5f);
                snake.WanderAlone();
                snake.PlaceAt(foot, top);
                yield return new WaitForSeconds(0.3f);
                ClimbResult upward = new ClimbResult();
                yield return ClimbTo(local, snake, top, $"snake-climbing-up-{c + 1}", upward);
                up += upward.there && upward.clung ? 1 : 0;
                ClimbResult downward = new ClimbResult();
                if (upward.there)
                {
                    yield return ClimbTo(local, snake, foot, $"snake-climbing-down-{c + 1}", downward);
                    down += downward.there && downward.clung ? 1 : 0;
                }
                how += $"cliff {c + 1} ({top.y - foot.y:0.0} m): up {upward.there} in {upward.seconds:0} s ({upward.steps} steps on the rock, holds lost {upward.falls}); "
                    + $"down {downward.there} in {downward.seconds:0} s ({downward.steps} steps, holds lost {downward.falls}); now {snake.Doing} at {snake.Head:F1}. ";
                Info($"the snake on cliff {c + 1}, every half second", $"up: {upward.trace}" + (upward.there ? $" | down: {downward.trace}" : ""));
            }
        }
        finally
        {
            A17_BeeAmbush.HeldOff = false;
            snake.Unaware = false;
            snake.PlaceAt(snake.Home, snake.Home + Vector3.forward);
            foreach (Snake other in grounded.Where(s => s != snake))
            {
                other.Unaware = false;
            }
        }
        Check("a ground snake told to wander up a cliff of the Tropics gets onto the rock and climbs it to the top, on two of three cliffs at least (17)", up >= Mathf.Min(2, tried), $"up {up} of {tried}: " + how);
        Check("from the top told to wander down, it crawls over the edge and down the rock to the foot, on two of three cliffs at least (17)", down >= Mathf.Min(2, tried), $"down {down} of {tried}: " + how);
    }

    private sealed class ClimbResult
    {
        internal bool there;
        internal bool clung;
        internal float seconds;
        internal int steps;
        internal int falls;
        internal string trace = "";
    }

    /// <summary>A ground snake told to wander to a point (45 s at most), pictured once it is on the rock, its way traced every half second.</summary>
    private IEnumerator ClimbTo(Character local, Snake snake, Vector3 to, string picture, ClimbResult result)
    {
        int steps = snake.WallSteps, falls = snake.WallFalls;
        bool pictured = false;
        float start = Time.time, nextTrace = 0f;
        List<string> trace = new List<string>();
        snake.WanderTowards(to + Vector3.up * 0.06f);
        while (Time.time - start < 45f)
        {
            result.clung |= snake.OnWall;
            if (Time.time >= nextTrace)
            {
                nextTrace = Time.time + 0.5f;
                trace.Add($"{snake.Head:F1}{(snake.OnWall ? $" on the rock (facing {snake.WallNormal.y:0.00} up)" : "")}{(snake.WallStop.Length > 0 ? $" [{snake.WallStop}]" : "")}"
                    + $"{(snake.CrawlStop.Length > 0 ? $" [{snake.CrawlStop}]" : "")} {snake.Doing}");
                snake.WallStop = "";
                snake.CrawlStop = "";
            }
            if (!pictured && snake.OnWall && Mathf.Abs(snake.Head.y - to.y) > 1.5f)
            {
                pictured = true;
                LookAtPoint(local, snake.Head);
                yield return new WaitForSeconds(0.3f);
                Screenshot(picture);
            }
            if (Goats.Flat(snake.Head, to) < 3f && Mathf.Abs(snake.Head.y - to.y) < 1f)
            {
                break;
            }
            yield return null;
        }
        result.there = Goats.Flat(snake.Head, to) < 3f && Mathf.Abs(snake.Head.y - to.y) < 1f;
        result.seconds = Time.time - start;
        result.steps = snake.WallSteps - steps;
        result.falls = snake.WallFalls - falls;
        result.trace = string.Join("; ", trace);
    }

    /// <summary>Cliffs of the Tropics near some points: a steep rock face (a level ray at its foot meets it) 4-12 m high, the Tropics' walkable ground at its top and its foot.</summary>
    private static List<(Vector3 top, Vector3 foot)> TropicsCliffs(List<Vector3> near, int most)
    {
        List<(Vector3, Vector3)> cliffs = new List<(Vector3, Vector3)>();
        bool Tropics(Collider ground) => Map.TryGetBiome(ground, out Biome.BiomeType biome) && biome == Biome.BiomeType.Tropics && !Snakes.InGiantTree(ground);
        for (int attempt = 0; attempt < 4000 && cliffs.Count < most; attempt++)
        {
            Vector3 centre = near[attempt % near.Count];
            Vector2 r = UnityEngine.Random.insideUnitCircle * 40f;
            Vector3 probe = new Vector3(centre.x + r.x, centre.y + 30f, centre.z + r.y);
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit top, 60f, HelperFunctions.terrainMapMask) || top.normal.y < 0.85f || !Tropics(top.collider)
                || cliffs.Any(c => Goats.Flat(c.Item1, top.point) < 12f) || NewMobs.UnderTheMap(top.point))
            {
                continue;
            }
            Vector3 out0 = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
            for (int turn = 0; turn < 8; turn++)
            {
                Vector3 outward = Quaternion.Euler(0f, turn * 45f, 0f) * out0;
                bool found = false;
                for (float d = 1.5f; d <= 4f; d += 0.5f)
                {
                    if (!Physics.Raycast(top.point + Vector3.up + outward * d, Vector3.down, out RaycastHit foot, 20f, HelperFunctions.terrainMapMask))
                    {
                        continue;
                    }
                    float drop = top.point.y - foot.point.y;
                    if (drop < 4f || drop > 12f || foot.normal.y < 0.85f || !Tropics(foot.collider) || NewMobs.UnderTheMap(foot.point)
                        || Physics.Raycast(foot.point + Vector3.up * 0.3f, Vector3.up, drop + 2f, HelperFunctions.terrainMapMask)
                        || Physics.Raycast(new Vector3(top.point.x, foot.point.y + 0.3f, top.point.z), Vector3.up, drop - 0.5f, HelperFunctions.terrainMapMask))
                    {
                        continue;
                    }
                    // The face between: a level ray from just above the foot back towards the top meets steep rock.
                    if (Physics.Raycast(foot.point + Vector3.up * 0.15f, -outward, out RaycastHit face, d + 1f, HelperFunctions.terrainMapMask) && face.normal.y < 0.6f && face.normal.y > -0.2f && Tropics(face.collider) && Snake.Climbable(face.collider)
                        && Snake.Climbable(top.collider) && Snake.Climbable(foot.collider))
                    {
                        cliffs.Add((top.point, foot.point));
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    break;
                }
            }
        }
        return cliffs;
    }
}
