using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the new mobs below ascent 17 and of the piton placed from ascent 15.</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The new mobs below ascent 17 (its rule switched off in memory, on the beach): they still live, and all they do is
    /// half as strong: a crab's pinch (a crab's own), a ground and a tree snake's bite and a goat's ram (the code a bite or
    /// a ram that lands runs on the scout); snakes chase a third slower. With the config's New Mobs off none spawns; with
    /// Effect Icons off the paralysis drop does not show.
    /// </summary>
    private IEnumerator NewMobsBelow17()
    {
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;
        ConfigEntry<bool> hazards = UltimateAscent.Plugin.AscentSwitches[Lvl.Hazards];
        ConfigEntry<bool> newMobs = UltimateAscent.Plugin.NewMobs;
        ConfigEntry<bool> icons = UltimateAscent.Plugin.EffectIcons;
        bool hazardsWere = hazards.Value, newMobsWere = newMobs.Value, iconsWere = icons.Value;
        Protect(false);
        Action calm = CalmForMeasuring();
        try
        {
            // With the config's New Mobs off: none spawns (the crabs' own spawn asked again here gives none).
            newMobs.Value = false;
            HostOptions.Invalidate();
            yield return null;
            Transform beach = MapHandler.Exists && Singleton<MapHandler>.Instance.segments.Length > 0 ? Singleton<MapHandler>.Instance.segments[0].segmentParent.transform : null;
            bool crabsOff = Crabs.On, snakesOff = Snakes.On, goatsOff = Goats.On;
            bool shut = !NewMobs.On && !crabsOff && !snakesOff && !goatsOff && Crabs.Spawn(beach).Count == 0;
            newMobs.Value = newMobsWere;
            HostOptions.Invalidate();
            yield return null;
            Check("with the config's New Mobs off, no crabs, snakes or goats spawn", shut && NewMobs.On,
                $"switched off, they spawn: crabs {crabsOff}, snakes {snakesOff}, goats {goatsOff}; a crab spawn now {(shut ? "gives none" : "is not shut")}; on again: {NewMobs.On}");

            hazards.Value = false;
            HostOptions.Invalidate();
            yield return null;
            List<BeachCrab> crabs = BeachCrabs();
            Check("below ascent 17 the new mobs live, half as strong, the snakes chasing a third slower",
                NewMobs.On && Crabs.On && Snakes.On && Goats.On && crabs.Count > 0 && Mathf.Approximately(NewMobs.Strength, 0.5f) && Mathf.Abs(Snakes.ChaseSpeed(false) - Snakes.GroundChaseSpeed * 2f / 3f) < 0.001f,
                $"crabs {Crabs.On} ({crabs.Count} on the beach), snakes {Snakes.On}, goats {Goats.On}; strength {NewMobs.Strength:0.00}; "
                + $"snakes chase at {Snakes.ChaseSpeed(false):0.0} m/s on the ground, {Snakes.ChaseSpeed(true):0.0} m/s from the trees (from 17: {Snakes.GroundChaseSpeed:0.0}, {Snakes.TreeChaseSpeed:0.0})");

            // A crab's pinch: the scout standing by one, as in the crab checks.
            foreach (BeachCrab crab in crabs)
            {
                crab.Unaware = true;
            }
            BeachCrab pincher = crabs.OrderBy(c => Vector3.Distance(c.transform.position, local.Center)).FirstOrDefault();
            float pinchInjury = -1f;
            if (pincher != null)
            {
                Vector3? close = StandNear(pincher.transform.position, 1.1f, 1.5f) ?? StandNear(pincher.transform.position, 1.1f, 1.8f, firm: false);
                pincher.Still = true;
                if (close.HasValue)
                {
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, close.Value + Vector3.up * 1.2f, false);
                }
                yield return new WaitForSeconds(0.5f);
                pincher.Still = false;
                ClearStatuses(afflictions);
                float injuryAtPinch = afflictions.GetCurrentStatus(Status.Injury);
                int pinches = Crabs.PinchesTaken;
                pincher.Unaware = false;
                for (float until = Time.time + 9f; Time.time < until && Crabs.PinchesTaken == pinches;)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(0.3f);
                pincher.Unaware = true;
                if (Crabs.PinchesTaken > pinches)
                {
                    pinchInjury = afflictions.GetCurrentStatus(Status.Injury) - injuryAtPinch;
                }
            }
            Check("below ascent 17 a crab's pinch is half as strong: 5 injury (17: 10)", Mathf.Abs(pinchInjury - Crabs.PinchInjury * 0.5f) < 0.006f,
                pinchInjury >= 0f ? $"+{pinchInjury * 100f:0.0} injury" : "no pinch landed in 9 s");
            ClearStatuses(afflictions);

            // A ground snake's bite: its injury at once, then its poison over 10 s.
            float injuryBefore = afflictions.GetCurrentStatus(Status.Injury);
            float poisonBefore = afflictions.GetCurrentStatus(Status.Poison);
            int bite = Snakes.PoisonByBite.Count;
            Snakes.Bitten(local, Snakes.Kind.Ground);
            float groundInjury = afflictions.GetCurrentStatus(Status.Injury) - injuryBefore;
            yield return new WaitForSeconds(Snakes.GroundPoisonSeconds + 0.6f);
            float poisonGiven = bite < Snakes.PoisonByBite.Count ? Snakes.PoisonByBite[bite] : 0f;
            float poisonOnBar = afflictions.GetCurrentStatus(Status.Poison) - poisonBefore;
            Check("below ascent 17 a ground snake's bite is half as strong: 5 injury, then 20 poison over 10 s (17: 10 and 40)",
                Mathf.Abs(groundInjury - Snakes.GroundBiteInjury * 0.5f) < 0.006f && Mathf.Abs(poisonGiven - Snakes.GroundPoison * 0.5f) < 0.006f && Mathf.Abs(poisonOnBar - Snakes.GroundPoison * 0.5f) < 0.03f,
                $"+{groundInjury * 100f:0.0} injury; poison {poisonGiven * 100f:0.0} given, {poisonOnBar * 100f:0.0} on the bar");
            ClearStatuses(afflictions);

            // A tree snake's bite: its injury, then the paralysis (its drop over the stamina bar, gone with Effect Icons off)
            // and its first collapse.
            FallRecorder.Clear();
            FallRecorder.Of = local;
            injuryBefore = afflictions.GetCurrentStatus(Status.Injury);
            Snakes.Bitten(local, Snakes.Kind.Tree);
            float treeInjury = afflictions.GetCurrentStatus(Status.Injury) - injuryBefore;
            float paralysis = SnakeParalysis.Left(local);
            yield return null;
            yield return null;
            bool dropShown = SnakeParalysis.IconShowing;
            icons.Value = false;
            yield return null;
            yield return null;
            bool dropHidden = !SnakeParalysis.IconShowing;
            icons.Value = iconsWere;
            for (float until = Time.time + Snakes.FirstCollapseMax + 1f; Time.time < until && FallRecorder.Seconds.Count == 0;)
            {
                yield return null;
            }
            float collapse = FallRecorder.Seconds.Count > 0 ? FallRecorder.Seconds[0] : 0f;
            Snakes.Venom.EndAll();
            FallRecorder.Of = null;
            Check("below ascent 17 a tree snake's bite is half as strong: 15 injury, then a minute of collapses 2 s long (17: 30, two minutes, 4 s)",
                Mathf.Abs(treeInjury - Snakes.TreeBiteInjury * 0.5f) < 0.006f && Mathf.Abs(paralysis - Snakes.ParalysisSeconds * 0.5f) < 1f && Mathf.Abs(collapse - Snakes.CollapseSeconds * 0.5f) < 0.01f,
                $"+{treeInjury * 100f:0.0} injury; paralysed for {paralysis:0.0} s; the first collapse {collapse:0.0} s");
            Check("with the config's Effect Icons off, the paralysis drop over the stamina bar does not show", dropShown && dropHidden,
                $"shown with it on {dropShown}; hidden with it off {dropHidden}");
            ClearStatuses(afflictions);

            // A goat's ram: knocked down, hurt and thrown along open ground.
            Vector3 away = OpenWay(local.Center);
            FallRecorder.Clear();
            FallRecorder.Of = local;
            injuryBefore = afflictions.GetCurrentStatus(Status.Injury);
            Vector3 from = local.Center;
            Goats.Rammed(local, away);
            float ramInjury = afflictions.GetCurrentStatus(Status.Injury) - injuryBefore;
            yield return new WaitForSeconds(3f);
            float thrown = Goats.Flat(local.Center, from);
            FallRecorder.Of = null;
            Check("below ascent 17 a goat's ram is half as strong: 10 injury, knocked down 1 s, thrown about half as far (17: 20, 2 s, 6-8 m)",
                Mathf.Abs(ramInjury - Goats.RamInjury * 0.5f) < 0.006f && FallRecorder.Seconds.Any(s => Mathf.Abs(s - Goats.RamKnockDown * 0.5f) < 0.01f) && thrown >= 2f && thrown <= 5f,
                $"+{ramInjury * 100f:0.0} injury; knock-downs {string.Join(", ", FallRecorder.Seconds.Select(s => $"{s:0.0} s"))}; thrown {thrown:0.0} m");
        }
        finally
        {
            hazards.Value = hazardsWere;
            newMobs.Value = newMobsWere;
            icons.Value = iconsWere;
            HostOptions.Invalidate();
            Snakes.Venom.EndAll();
            FallRecorder.Of = null;
            foreach (BeachCrab crab in UnityEngine.Object.FindObjectsByType<BeachCrab>(FindObjectsSortMode.None))
            {
                crab.Unaware = false;
            }
            ClearStatuses(afflictions);
            calm();
            Protect(true);
        }
    }

    /// <summary>
    /// The boarding pass on a custom run and the Custom Run screen, opened as a player does (the pass's custom-run button,
    /// then its Custom Options button): the map picker beside that button, not on it; the threats with the new mobs each
    /// in its biome's group (crabs after the urchins, snakes after the rain, goats after the blizzards), each its own
    /// switch, on by default, its click turning it off and on again, and every threat inside the threats' panel. Both
    /// pictured; the scorpions' switch and every switch's name listed; the pass left as it was.
    /// </summary>
    private IEnumerator CustomRunThreats()
    {
        BoardingPass pass = GUIManager.instance != null ? GUIManager.instance.boardingPass : null;
        AirportCheckInKiosk kiosk = UnityEngine.Object.FindAnyObjectByType<AirportCheckInKiosk>();
        CustomOptionsWindow window = pass != null ? pass.customOptionsWindow : null;
        if (kiosk == null || window == null)
        {
            Fail("the Custom Run screen can be opened", $"kiosk {kiosk != null}, screen {window != null}");
            yield break;
        }
        bool wasCustom = RunSettings.IsCustomRun;
        Step("the boarding pass on a custom run", () =>
        {
            pass.kiosk = kiosk;
            if (!pass.isOpen)
            {
                pass.Open();
            }
            if (!RunSettings.IsCustomRun)
            {
                pass.customRunButton.onClick.Invoke();
            }
        });
        yield return new WaitForSecondsRealtime(2f);
        Transform picker = pass.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "UltimateAscentMapPicker");
        RectTransform options = pass.customOptionsButton != null ? (RectTransform)pass.customOptionsButton.transform : null;
        bool pickerShown = picker != null && picker.gameObject.activeInHierarchy;
        bool optionsShown = options != null && options.gameObject.activeInHierarchy;
        bool overlapping = pickerShown && optionsShown && Overlap((RectTransform)picker, options);
        Screenshot("boarding-pass-custom");
        Check("on a custom run the boarding pass's map picker sits beside the Custom Options button, not on it",
            RunSettings.IsCustomRun && optionsShown && !overlapping,
            $"custom run {RunSettings.IsCustomRun}; the pass's title '{(pass.ascentTitle != null ? pass.ascentTitle.text : "")}'; the picker {(picker == null ? "missing" : pickerShown ? $"shown, anchored at {((RectTransform)picker).anchorMin}" : "hidden")}, "
            + $"the Custom Options button {(optionsShown ? "shown" : "hidden")}, overlapping {overlapping}");
        // Nothing changes on screen in the frame the picture is taken.
        yield return new WaitForSecondsRealtime(0.5f);

        Step("open the Custom Run screen", () => pass.customOptionsButton.onClick.Invoke());
        yield return new WaitForSecondsRealtime(1.5f);
        List<CustomOptionToggle> toggles = window.GetComponentsInChildren<CustomOptionToggle>(true).ToList();
        CustomOptionToggle scorpions = toggles.FirstOrDefault(t => t.settingType == RunSettings.SETTINGTYPE.Hazard_Scorpions);
        string Label(CustomOptionToggle toggle) => string.Join("/", toggle.GetComponentsInChildren<TMPro.TMP_Text>(true).Select(text => text.text));
        Info("the Custom Run screen's switches", string.Join("; ", toggles.Select(t => $"{t.settingType} '{Label(t)}' #{t.transform.GetSiblingIndex()}")));
        List<string> outside = new List<string>();
        if (scorpions != null)
        {
            RectTransform list = (RectTransform)scorpions.transform.parent;
            UnityEngine.UI.GridLayoutGroup grid = list.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            Info("the threats' layout", grid != null ? $"a grid, {grid.startAxis} first, {grid.constraint} {grid.constraintCount}, cells {grid.cellSize}, spacing {grid.spacing}, panel {list.rect.size}"
                : string.Join(", ", list.GetComponents<Component>().Select(c => c.GetType().Name)));
            foreach (CustomOptionToggle toggle in toggles.Where(t => t.transform.parent == list && t.gameObject.activeInHierarchy))
            {
                RectTransform cell = (RectTransform)toggle.transform;
                if (!Inside(list, cell.TransformPoint(cell.rect.center)))
                {
                    outside.Add(Label(toggle));
                }
            }
        }
        yield return new WaitForSecondsRealtime(0.3f);
        Screenshot("custom-run-threats");
        List<string> results = new List<string>();
        bool right = scorpions != null && window.isOpen;
        foreach ((RunSettings.SETTINGTYPE setting, _, string english, string russian, RunSettings.SETTINGTYPE after) in NewMobs.Threats)
        {
            CustomOptionToggle toggle = toggles.FirstOrDefault(t => t.settingType == setting);
            if (toggle == null)
            {
                right = false;
                results.Add($"{english}: missing");
                continue;
            }
            string label = Label(toggle);
            bool named = label.IndexOf(english, StringComparison.OrdinalIgnoreCase) >= 0 || label.IndexOf(russian, StringComparison.OrdinalIgnoreCase) >= 0;
            int index = toggle.transform.GetSiblingIndex();
            CustomOptionToggle neighbour = toggles.FirstOrDefault(t => t.settingType == after);
            int neighbourAt = neighbour != null ? neighbour.transform.GetSiblingIndex() : -9;
            int was = RunSettings.GetValue(setting, forceGetCustomValue: true);
            bool onByDefault = RunSettings.GetDefaultValue(setting) == 1;
            toggle.OnClick();
            int clicked = RunSettings.GetValue(setting, forceGetCustomValue: true);
            bool tickOff = !toggle.toggle.isOn;
            toggle.OnClick();
            int again = RunSettings.GetValue(setting, forceGetCustomValue: true);
            bool tickOn = toggle.toggle.isOn;
            right &= toggle.gameObject.activeInHierarchy && named && index == neighbourAt + 1 && onByDefault && was == 1 && clicked == 0 && tickOff && again == 1 && tickOn;
            results.Add($"{english}: shown {toggle.gameObject.activeInHierarchy}, named '{label}', at {index} (after {after} at {neighbourAt}); on by default {onByDefault} (was {was}); clicked: {clicked} (ticked {!tickOff}), again: {again} (ticked {tickOn})");
        }
        Check("the Custom Run screen's threats have the new mobs each in its biome's group (crabs after the urchins, snakes after the rain, goats after the blizzards), each its own switch, on by default, and every threat fits its panel",
            right && outside.Count == 0,
            $"screen open {window.isOpen}; " + string.Join("; ", results) + $"; outside the threats' panel: {(outside.Count == 0 ? "none" : string.Join(", ", outside))}");

        Step("close the Custom Run screen", () =>
        {
            // Back on the boarding pass, which the screen reopens, as it was.
            window.closeButtonCustom.onClick.Invoke();
            if (RunSettings.IsCustomRun != wasCustom)
            {
                pass.customRunButton.onClick.Invoke();
            }
        });
        yield return new WaitForSecondsRealtime(1f);
    }

    /// <summary>A point of the screen inside a rectangle of it.</summary>
    private static bool Inside(RectTransform rect, Vector3 world)
    {
        Vector3 local = rect.InverseTransformPoint(world);
        return rect.rect.Contains(new Vector2(local.x, local.y));
    }

    /// <summary>Two rectangles of the screen overlap (by their corners in the world).</summary>
    private static bool Overlap(RectTransform a, RectTransform b)
    {
        Vector3[] ca = new Vector3[4], cb = new Vector3[4];
        a.GetWorldCorners(ca);
        b.GetWorldCorners(cb);
        return ca[0].x < cb[2].x && cb[0].x < ca[2].x && ca[0].y < cb[2].y && cb[0].y < ca[2].y;
    }

    /// <summary>A piece of the screen and everything under it: names, and what each shows (images' sprites, texts, text keys).</summary>
    private static string Layout(Transform root)
    {
        List<string> lines = new List<string>();
        void Walk(Transform part, int depth)
        {
            string shows = string.Join(", ", part.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c =>
                c is UnityEngine.UI.Image image ? $"Image({(image.sprite != null ? image.sprite.name : "none")})"
                : c is TMPro.TMP_Text text ? $"Text('{text.text}')"
                : c is LocalizedText localized ? $"LocalizedText({localized.index})"
                : c.GetType().Name));
            lines.Add(new string('.', depth * 2) + part.name + (part.gameObject.activeSelf ? "" : " (off)") + ": " + shows);
            foreach (Transform child in part)
            {
                Walk(child, depth + 1);
            }
        }
        Walk(root, 0);
        return string.Join(" | ", lines);
    }

    /// <summary>A way along open ground from here: nothing in the way for 8 m, ground under its end.</summary>
    private static Vector3 OpenWay(Vector3 from)
    {
        for (int angle = 0; angle < 360; angle += 20)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            if (HelperFunctions.LineCheck(from, from + direction * 8f, HelperFunctions.LayerType.TerrainMap).transform == null
                && Physics.Raycast(from + direction * 5f + Vector3.up, Vector3.down, 4f, HelperFunctions.terrainMapMask))
            {
                return direction;
            }
        }
        return Vector3.forward;
    }

    /// <summary>
    /// A piton hammered in from ascent 15 (the game's own hammering, as holding a piton to a wall ends in): it is the Alps'
    /// cracking piton, made sturdier (5 cracks to break), and keeps the yellow look of the piton that was in the hand.
    /// </summary>
    private IEnumerator PitonChecks()
    {
        Character local = Local;
        Item pitonPrefab = ItemByName("ClimbingSpike");
        if (pitonPrefab == null)
        {
            Fail("the piton for the piton checks", "it is not in the item database");
            yield break;
        }
        if (!FindPitonWall(local.Center, out RaycastHit wall))
        {
            Info("the piton checks are skipped", "no steep face within 40 m of the scout");
            yield break;
        }
        Vector3 stand = wall.point + new Vector3(wall.normal.x, 0f, wall.normal.z).normalized * 1.3f;
        if (Physics.Raycast(stand + Vector3.up * 1.5f, Vector3.down, out RaycastHit floor, 6f, HelperFunctions.terrainMapMask))
        {
            stand = floor.point;
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand + Vector3.up * 1.2f, false);
        yield return new WaitForSeconds(1f);
        yield return SpawnIntoHand(pitonPrefab);
        Item inHand = held;
        if (inHand == null)
        {
            yield break;
        }
        LookAtPoint(local, wall.point);
        yield return new WaitForSeconds(0.6f);
        Transform heldRingPart = inHand.transform.Find("ClimbingSpike/Ring");
        string heldRing = heldRingPart != null && heldRingPart.TryGetComponent(out Renderer heldRenderer) ? MaterialName(heldRenderer) : "none";
        CharacterItems items = local.refs.items;
        items.UpdateClimbingSpikeCount(local.player.itemSlots);
        Transform eye = MainCamera.instance.transform;
        // Aimed at the spot on the wall itself (the look turned to it can fall a little short of a thin trunk).
        if (!Physics.Raycast(eye.position, (wall.point - eye.position).normalized, out RaycastHit hit, 4f, HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)))
        {
            Fail("aim the piton at the wall", $"nothing within 4 m of the camera (the wall {Vector3.Distance(eye.position, wall.point):0.0} m off)");
            held = null;
            yield break;
        }
        HashSet<ShittyPiton> before = new HashSet<ShittyPiton>(UnityEngine.Object.FindObjectsByType<ShittyPiton>(FindObjectsSortMode.None));
        Step("hammer the piton into the wall", () => items.HammerClimbingSpike(hit));
        yield return new WaitForSeconds(1f);
        ShittyPiton placed = UnityEngine.Object.FindObjectsByType<ShittyPiton>(FindObjectsSortMode.None)
            .Where(p => p != null && !before.Contains(p)).OrderBy(p => Vector3.Distance(p.transform.position, hit.point)).FirstOrDefault();
        Transform ringPart = placed != null ? placed.transform.Find("ClimbingSpike/Ring") : null;
        string placedRing = ringPart != null && ringPart.TryGetComponent(out Renderer ringRenderer) ? MaterialName(ringRenderer) : "none";
        LookAtPoint(local, hit.point);
        yield return new WaitForSeconds(0.4f);
        Screenshot("piton-placed");
        Check("a piton hammered in from ascent 15 is the Alps' cracking piton, made sturdier, with the yellow look of the piton in the hand (15)",
            placed != null && placed.name.StartsWith("UA_PitonCracking", StringComparison.Ordinal) && placedRing == heldRing && placed.cracksToBreak == 5,
            $"placed {(placed != null ? placed.name : "none")} {(placed != null ? Vector3.Distance(placed.transform.position, hit.point) : 0f):0.00} m from where it was hammered; "
            + $"its ring '{placedRing}', the hand's piton's '{heldRing}'; cracks to break {(placed != null ? placed.cracksToBreak : 0)}");
        held = null;

        // Hung on (the game's own grab): it holds for its time, then cracks every 1.5 s of hanging and breaks at its fifth
        // crack, dropping the scout.
        ClimbHandle handle = placed != null ? placed.GetComponent<ClimbHandle>() : null;
        if (handle == null)
        {
            yield break;
        }
        float holds = placed.totalSecondsOfHang;
        int cracksAtStart = placed.cracksToBreak;
        Step("hang on the placed piton", () => handle.Interact(local));
        float hungAt = Time.time, brokeAt = -1f;
        List<float> cracks = new List<float>();
        int left = cracksAtStart;
        bool hanging = false, shot = false;
        for (float until = Time.time + holds + 1.5f * cracksAtStart + 6f; Time.time < until;)
        {
            yield return null;
            local.data.currentStamina = 1f;
            hanging |= handle.hanger == local;
            if (placed.cracksToBreak < left)
            {
                left = placed.cracksToBreak;
                cracks.Add(Time.time - hungAt);
                if (!shot && cracks.Count == 3)
                {
                    shot = true;
                    LookAtPoint(local, placed.transform.position);
                    Screenshot("piton-cracking");
                }
            }
            if (placed.disabled)
            {
                brokeAt = Time.time - hungAt;
                break;
            }
        }
        yield return new WaitForSeconds(0.5f);
        bool dropped = handle.hanger != local;
        bool gone = placed.transform.childCount > 0 && Enumerable.Range(0, placed.transform.childCount).All(i => !placed.transform.GetChild(i).gameObject.activeSelf);
        Check("hung on, the placed piton holds a while, then cracks five times, every 1.5 s of hanging, and breaks, dropping the scout (15)",
            hanging && cracks.Count == cracksAtStart && cracks.Count == 5 && brokeAt > 0f && dropped && gone && cracks.Zip(cracks.Skip(1), (a, b) => b - a).All(gap => Mathf.Abs(gap - 1.5f) < 0.25f),
            $"hung on {hanging}; it was to hold {holds:0.0} s; cracks at {string.Join(", ", cracks.Select(c => $"{c:0.0}"))} s; broke at {(brokeAt > 0f ? $"{brokeAt:0.0} s" : "never")}; "
            + $"the scout let go {dropped}; the piton gone from the wall {gone}");
    }

    private static string MaterialName(Renderer renderer)
    {
        return renderer.sharedMaterial != null ? renderer.sharedMaterial.name.Replace(" (Instance)", "") : "none";
    }

    /// <summary>The nearest steep face around the scout with ground 1.3 m out from it, the face 0.3-2.2 m above that ground.</summary>
    private static bool FindPitonWall(Vector3 around, out RaycastHit wall)
    {
        wall = default;
        float nearest = float.MaxValue;
        for (int angle = 0; angle < 360; angle += 10)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            RaycastHit hit = HelperFunctions.LineCheck(around, around + direction * 40f, HelperFunctions.LayerType.TerrainMap);
            if (hit.transform == null || Mathf.Abs(hit.normal.y) > 0.3f || hit.distance >= nearest)
            {
                continue;
            }
            Vector3 front = hit.point + new Vector3(hit.normal.x, 0f, hit.normal.z).normalized * 1.3f;
            if (!Physics.Raycast(front + Vector3.up * 1.5f, Vector3.down, out RaycastHit floor, 6f, HelperFunctions.terrainMapMask)
                || hit.point.y - floor.point.y < 0.3f || hit.point.y - floor.point.y > 2.2f)
            {
                continue;
            }
            wall = hit;
            nearest = hit.distance;
        }
        return nearest < float.MaxValue;
    }
}
