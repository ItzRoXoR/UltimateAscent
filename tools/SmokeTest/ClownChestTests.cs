using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the clown chests' tricks (21).</summary>
internal sealed partial class Runner
{
    /// <summary>While the tricks are checked the mod's eruptions wait (the Kiln's throw the scout about).</summary>
    [HarmonyPatch(typeof(A17_Hazards), nameof(A17_Hazards.Tick))]
    internal static class NoEruptions
    {
        internal static bool On;

        private static bool Prefix() => !On;
    }

    private static readonly string[] ClownTricks =
    {
        "45 drowsiness", "45 poison", "45 spores", "a web of 45", "a fly trap (a grip of 45, or a paralysis where none grows)", "20 petrification", "100 s of blindness",
    };

    /// <summary>
    /// The clown chests' tricks (21). The odds first: 13,000 picks, each of six tricks about 2 in 13 and blindness 1 in
    /// 13. Then each trick once, on the map's own clown chests (in the part of the map the scout is in, or else the part
    /// with the most; when it has fewer than seven, the last one is shut again, in memory, and opened once more; the
    /// loot each opening throws out is cleared away at once, so the scout cannot slip on a banana peel among it): the
    /// scout stands beside the chest and opens it the game's way (the clasps, then the lid), the chest told which trick to
    /// play. What lands: 45 drowsiness, poison or spores on the bar, a web of 45, 20 petrification, or 100 s of
    /// blindness (the blindness and the darkened screen); and no injury, which other suitcases deal at 21. Caught in the
    /// web, the scout jumps free. The fly trap: on a map with the Gloom, a copy of its plant springs up under the scout and
    /// holds them in its jaws with a grip of 45, never poisoning, until they jump free; then it spits them out and sinks
    /// away. On a map without it, the scout is knocked down once for 12 s instead.
    /// Meanwhile no eruptions, and in the Kiln its lava held at its start (both would throw the scout about).
    /// </summary>
    private IEnumerator ClownChestChecks()
    {
        if (!Rules.Enabled(Lvl.PainfulLuggage))
        {
            Info("the clown chests' tricks are skipped", $"testing ascent {target}; they come from ascent 21");
            yield break;
        }
        int[] picks = new int[ClownTricks.Length];
        for (int i = 0; i < 13000; i++)
        {
            picks[A21_PainfulLuggage.PickTrick()]++;
        }
        Check("a clown chest's trick: drowsiness, poison, spores, a web, a fly trap's grip and petrification 2 in 13 each, blindness 1 in 13 (21)",
            picks.Take(6).All(c => Mathf.Abs(c - 2000) <= 200) && Mathf.Abs(picks[6] - 1000) <= 150,
            $"of 13,000 picks: {string.Join(", ", picks.Select((c, i) => $"{ClownTricks[i]} {c}"))} (2 in 13 comes to 2,000, 1 in 13 to 1,000)");

        MapHandler map = Singleton<MapHandler>.Instance;
        Luggage[] clowns = FindObjectsByType<Luggage>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(l => l != null && l.gameObject.scene.IsValid() && l.CompareTag("ClownLuggage") && l.state == Luggage.LuggageState.Closed && !Compat.Tfa.Spawned(l))
            .ToArray();
        int SegmentOf(Luggage l) => Array.FindIndex(map.segments, s => s.segmentParent != null && l.transform.IsChildOf(s.segmentParent.transform));
        // On in its part of the map (not under the biome the map left out there).
        bool Present(Luggage l)
        {
            int segment = SegmentOf(l);
            if (segment < 0)
            {
                return false;
            }
            for (Transform t = l.transform; t != null && t.gameObject != map.segments[segment].segmentParent; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    return false;
                }
            }
            return true;
        }
        List<IGrouping<int, Luggage>> bySegment = clowns.Where(Present).GroupBy(SegmentOf).OrderByDescending(g => g.Count()).ToList();
        Info("the map's closed clown chests", $"{clowns.Length} in all; in the map's parts: {string.Join(", ", bySegment.Select(g => $"{(Segment)g.Key} {g.Count()}"))}");
        VenusFlyTrap[] flyTraps = FindObjectsByType<VenusFlyTrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t != null && t.gameObject.scene.IsValid()).ToArray();
        Info("the map's own fly traps", $"{flyTraps.Length}: {string.Join(", ", flyTraps.GroupBy(t => BiomeOf(t)).Select(g => $"{g.Key} {g.Count()}"))}");
        IGrouping<int, Luggage> chosen = bySegment.FirstOrDefault(g => g.Key == map.currentSegment) ?? bySegment.FirstOrDefault();
        if (chosen == null)
        {
            Fail("closed clown chests on the map", $"{clowns.Length} clown chests, none in a part of the map in use");
            yield break;
        }
        if (chosen.Key != map.currentSegment)
        {
            MapHandler.JumpToSegment((Segment)chosen.Key);
            for (float until = Time.time + 20f; Time.time < until && !chosen.Any(l => l != null && l.gameObject.activeInHierarchy);)
            {
                yield return null;
            }
            yield return new WaitForSeconds(3f);
        }
        List<Luggage> chests = chosen.Where(l => l != null && l.gameObject.activeInHierarchy).ToList();
        if (chests.Count == 0)
        {
            Fail("the clown chests of " + (Segment)chosen.Key, "none came on there");
            yield break;
        }
        Character local = Local;
        int petrifyBefore = local.data.petrifyAmount;
        NoEruptions.On = true;
        LavaRising heldLava = Map.InVolcanoKiln ? KilnAsh.Lava : null;
        Vector3 lavaWas = Vector3.zero;
        if (heldLava != null && heldLava.lava != null)
        {
            lavaWas = heldLava.lava.position;
            heldLava.enabled = false;
            heldLava.lava.position = heldLava.lava.transform.position = new Vector3(lavaWas.x, Mathf.Min(lavaWas.y, heldLava.startHeight), lavaWas.z);
            Info("the Kiln's lava held at its start for the tricks", $"it was at {lavaWas.y:0.0}, its start {heldLava.startHeight:0.0}");
        }
        Protect(false);
        try
        {
            Luggage last = null;
            for (int trick = 0; trick < ClownTricks.Length; trick++)
            {
                Luggage chest = chests.Where(l => l != null && l.state == Luggage.LuggageState.Closed && l.gameObject.activeInHierarchy)
                    .OrderBy(l => Vector3.Distance(l.transform.position, local.Center)).FirstOrDefault();
                bool again = chest == null;
                if (again)
                {
                    chest = last;
                }
                if (chest == null)
                {
                    Fail("a clown chest for " + ClownTricks[trick]);
                    yield break;
                }
                yield return ClownTrickOn(chest, trick, again);
                last = chest;
            }
        }
        finally
        {
            NoEruptions.On = false;
            if (heldLava != null && heldLava.lava != null)
            {
                heldLava.lava.position = heldLava.lava.transform.position = lavaWas;
                heldLava.enabled = true;
            }
            A21_PainfulLuggage.NextTrick = null;
            Unblind(local);
            ClearStatuses(Afflictions);
            foreach (Status held in new[] { Status.Web, Status.FlyTrap })
            {
                if (Afflictions.GetCurrentStatus(held) > 0f)
                {
                    Afflictions.SetStatus(held, 0f);
                }
            }
            local.data.SetPetrify(petrifyBefore);
            Protect(true);
        }
    }

    /// <summary>One clown chest opened the game's way with the scout beside it, told to play this trick, and what it did.</summary>
    private IEnumerator ClownTrickOn(Luggage chest, int trick, bool again)
    {
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;
        string where = $"{chest.name} in {Map.CurrentPlace}" + (again ? " (shut again in memory for this trick)" : "");
        Vector3 at = chest.Center();
        // Clear of the lid, which swings up as the chest opens.
        Vector3? stand = BesideChest(at);
        if (stand == null)
        {
            stand = StandNear(at, 2.5f, 4f, seeIt: true) ?? StandNear(at, 2.5f, 6f, firm: false);
            where += ", no flat ground beside it 5 m clear of lava";
        }
        if (stand.HasValue)
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand.Value + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1.5f);
        }
        else
        {
            Info("no ground beside " + where, $"opened from where the scout stands, {Vector3.Distance(at, local.Center):0} m away");
        }
        LookAtPoint(local, at);
        Unblind(local);
        ClearStatuses(afflictions);
        yield return new WaitForSeconds(0.5f);
        int petrifyBefore = local.data.petrifyAmount;
        if (again)
        {
            chest.state = Luggage.LuggageState.Closed;
            if (!Luggage.ALL_LUGGAGE.Contains(chest))
            {
                Luggage.ALL_LUGGAGE.Add(chest);
            }
        }
        HashSet<Item> itemsBefore = new HashSet<Item>(FindObjectsByType<Item>(FindObjectsSortMode.None));
        // The game's opening: the clasps as the scout takes hold, then, held as long as the chest takes, the lid.
        chest.Interact(local);
        yield return new WaitForSeconds(Mathf.Clamp(chest.timeToOpen, 0.2f, 3f));
        Dictionary<Status, float> landed = new Dictionary<Status, float>();
        Action<Status, float> listener = (type, amount) => landed[type] = (landed.TryGetValue(type, out float had) ? had : 0f) + amount;
        afflictions.OnAddedIncrementalStatus += listener;
        FallRecorder.Clear();
        FallRecorder.Of = local;
        int plantsBefore = ClownFlyTrap.Shown;
        A21_PainfulLuggage.NextTrick = trick;
        try
        {
            chest.Interact_CastFinished(local);
        }
        finally
        {
            afflictions.OnAddedIncrementalStatus -= listener;
            A21_PainfulLuggage.NextTrick = null;
            if (trick != 4)
            {
                FallRecorder.Of = null;
            }
        }
        float Landed(Status type) => landed.TryGetValue(type, out float amount) ? amount : 0f;
        bool opened = chest.state == Luggage.LuggageState.Open;
        bool unhurt = Landed(Status.Injury) < 0.0001f;
        string all = landed.Count > 0 ? string.Join(", ", landed.Select(p => $"{p.Key} {p.Value * 100f:0.#}")) : "nothing";
        yield return new WaitForSeconds(0.6f);
        // The loot the chest threw out goes at once (each opening throws it again): the scout, held or knocked down
        // beside it, would slip on a banana peel among it.
        int cleared = 0;
        foreach (Item item in FindObjectsByType<Item>(FindObjectsSortMode.None)
            .Where(i => i != null && !itemsBefore.Contains(i) && i.holderCharacter == null && Vector3.Distance(i.transform.position, chest.transform.position) < 8f
                && i.photonView != null && i.photonView.IsMine))
        {
            PhotonNetwork.Destroy(item.gameObject);
            cleared++;
        }
        where += $", its loot ({cleared}) cleared away";
        string name = "a clown chest's trick: " + ClownTricks[trick];
        string shot = "clown-" + new[] { "drowsy", "poison", "spores", "web", "flytrap", "petrify", "blind" }[trick];
        if (trick <= 2)
        {
            Status type = trick == 0 ? Status.Drowsy : trick == 1 ? Status.Poison : Status.Spores;
            float bar = afflictions.GetCurrentStatus(type);
            Screenshot(shot);
            Check(name + " on the bar, and no injury (21)", opened && Mathf.Abs(Landed(type) - 0.45f) < 0.002f && bar >= 0.425f && unhurt,
                $"{where}, opened {opened}: {Landed(type) * 100f:0.0} landed (all that landed: {all}); the bar shows {bar * 100f:0.#} 0.6 s later");
        }
        else if (trick == 4)
        {
            yield return ClownFlyTrapCheck(name, where, opened, Landed(Status.FlyTrap), unhurt, all, plantsBefore);
        }
        else if (trick == 3)
        {
            Status type = Status.Web;
            float bar = afflictions.GetCurrentStatus(type);
            bool caught = afflictions.isStruggling;
            bool prompt = GUIManager.instance != null && GUIManager.instance.strugglePrompt != null && GUIManager.instance.strugglePrompt.gameObject.activeInHierarchy;
            Screenshot(shot);
            // Jumping free: a press every 0.45 s, as a player mashes the button.
            int jumpsBefore = Struggling.Jumps;
            float ground = local.Center.y;
            float lifted = 0f;
            float start = Time.time;
            float nextPress = start;
            while (afflictions.GetCurrentStatus(type) > 0.001f && Time.time < start + 20f)
            {
                if (Time.time >= nextPress)
                {
                    nextPress += 0.45f;
                    local.refs.movement.TryToJump();
                }
                lifted = Mathf.Max(lifted, local.Center.y - ground);
                yield return null;
            }
            yield return null;
            bool free = afflictions.GetCurrentStatus(type) <= 0.001f && !afflictions.isStruggling;
            Check(name + ", and no injury; caught, the scout jumps free (21)",
                opened && Mathf.Abs(Landed(type) - 0.45f) < 0.002f && bar >= 0.425f && caught && unhurt && free,
                $"{where}, opened {opened}: {Landed(type) * 100f:0.0} landed (all that landed: {all}), the bar {bar * 100f:0.#}; caught {caught}, the struggle prompt shown {prompt}; "
                + $"free {free} after {Struggling.Jumps - jumpsBefore} jumps in {Time.time - start:0.0} s; the game's held pose lifted them up to {lifted:0.00} m");
        }
        else if (trick == 5)
        {
            int added = local.data.petrifyAmount - petrifyBefore;
            Screenshot(shot);
            Check(name + ", and no injury (21)", opened && added == A21_PainfulLuggage.TrickPetrify && unhurt,
                $"{where}, opened {opened}: petrification {petrifyBefore} -> {local.data.petrifyAmount} (all that landed on the bar: {all})");
            local.data.SetPetrify(petrifyBefore);
        }
        else
        {
            IllegalScreenEffect screen = FindObjectsByType<IllegalScreenEffect>(FindObjectsSortMode.None)
                .FirstOrDefault(s => s != null && string.Equals(s.statusName, "BLIND", StringComparison.OrdinalIgnoreCase));
            Affliction Blindness() => afflictions.HasAfflictionType(Affliction.AfflictionType.Blind, out Affliction found) ? found : null;
            Affliction blind = Blindness();
            float total = blind != null ? blind.totalTime : 0f;
            float since = Time.time;
            bool darkAtOnce = local.data.isBlind && screen != null && screen.rend != null && screen.rend.enabled;
            float screenLeft = screen != null ? screen.activeForSeconds : 0f;
            Screenshot(shot);
            yield return new WaitForSeconds(5f);
            blind = Blindness();
            float effectLeft = blind != null ? blind.totalTime - blind.timeElapsed : 0f;
            float screenLater = screen != null ? screen.activeForSeconds : 0f;
            bool darkLater = local.data.isBlind && screen != null && screen.rend != null && screen.rend.enabled;
            float expectedLater = A21_PainfulLuggage.TrickBlindSeconds - (Time.time - since) - 0.6f;
            Check(name + ": the blindness and the darkened screen, still on 5 s later, and no injury (21)",
                opened && Mathf.Abs(total - A21_PainfulLuggage.TrickBlindSeconds) < 0.01f && darkAtOnce && darkLater && blind != null
                && Mathf.Abs(effectLeft - expectedLater) < 1.5f && Mathf.Abs(screenLater - expectedLater) < 1.5f && unhurt,
                $"{where}, opened {opened}: blindness for {total:0.#} s; the screen dark {darkAtOnce} with {screenLeft:0.#} s left; 5 s later dark {darkLater}, "
                + $"{effectLeft:0.#} s of the blindness left and {screenLater:0.#} s of the dark screen (expected about {expectedLater:0.#}); all that landed on the bar: {all}");
            Unblind(local);
        }
        ClearStatuses(afflictions);
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>
    /// The fly trap's trick, after the chest opened: on a map with the Gloom the plant sprung up round the scout, its jaws
    /// shut on them, a grip of 45 and no poison while it holds; jumping frees them, and it lets go and is gone within its
    /// spit and sink. On a map without it, a knock-down of 12 s, and no grip or plant.
    /// </summary>
    private IEnumerator ClownFlyTrapCheck(string name, string where, bool opened, float grip, bool unhurt, string all, int plantsBefore)
    {
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;
        FallRecorder.Of = null;
        if (!ClownFlyTrap.Grows)
        {
            float down = local.data.fallSeconds;
            List<float> falls = FallRecorder.Seconds.ToList();
            Vector3 fellAt = local.Center;
            Screenshot("clown-paralysis");
            // Knocked down, the scout may slide down the ground they stood on (into the Kiln's lava): statuses locked meanwhile.
            bool wasLocked = local.statusesLocked;
            local.statusesLocked = true;
            float downLater;
            bool upAgain;
            float slid = 0f;
            try
            {
                for (float since = 0f; since < 5f; since += Time.deltaTime)
                {
                    slid = Mathf.Max(slid, Vector3.Distance(local.Center, fellAt));
                    yield return null;
                }
                downLater = local.data.fallSeconds;
                for (float since = 0f; since < ClownFlyTrap.ParalysisSeconds - 5f + 1.5f; since += Time.deltaTime)
                {
                    slid = Mathf.Max(slid, Vector3.Distance(local.Center, fellAt));
                    yield return null;
                }
                upAgain = local.data.fallSeconds <= 0f && !local.data.dead;
            }
            finally
            {
                local.statusesLocked = wasLocked;
            }
            where += $", slid up to {slid:0.0} m while down";
            Check("a clown chest's trick on a map without the Gloom (no fly trap grows there): the scout paralysed once, 12 s, no grip, no plant, no injury (21)",
                opened && falls.Count == 1 && Mathf.Abs(falls[0] - ClownFlyTrap.ParalysisSeconds) < 0.01f && down > ClownFlyTrap.ParalysisSeconds - 1f && downLater > 0f && upAgain
                && grip < 0.0001f && ClownFlyTrap.Shown == plantsBefore && unhurt,
                $"{where}, opened {opened}: knock-downs {string.Join(", ", falls.Select(f => $"{f:0.#} s"))}; down for {down:0.0} s more at once, {downLater:0.0} s 5 s later, up again {upAgain}; "
                + $"grip {grip * 100f:0.#}, plants {ClownFlyTrap.Shown - plantsBefore}; all that landed: {all}");
            yield break;
        }
        ClownFlyTrapPlant plant = ClownFlyTrap.Plants.FirstOrDefault(p => p != null && p.Scout == local);
        bool caught = afflictions.isStruggling;
        float bar = afflictions.GetCurrentStatus(Status.FlyTrap);
        float poison = 0f;
        Action<Status, float> poisoned = (type, amount) =>
        {
            if (type == Status.Poison)
            {
                poison += amount;
            }
        };
        afflictions.OnAddedIncrementalStatus += poisoned;
        try
        {
            if (plant == null)
            {
                Check(name + ": the plant springs up", false, $"{where}: no plant (shown {ClownFlyTrap.Shown - plantsBefore})");
                yield break;
            }
            float under = Goats.Flat(plant.transform.position, local.Center);
            float held = local.Center.y - plant.transform.position.y;
            float jaws = plant.HoldPoint.y - plant.transform.position.y;
            bool biting = plant.Biting;
            float size = plant.transform.localScale.x;
            yield return ClownPicture(local.Center, "clown-flytrap-holding");
            // Jumping free: a press every 0.45 s.
            int jumpsBefore = Struggling.Jumps;
            float start = Time.time;
            float nextPress = start;
            while (afflictions.GetCurrentStatus(Status.FlyTrap) > 0.001f && Time.time < start + 20f)
            {
                if (Time.time >= nextPress)
                {
                    nextPress += 0.45f;
                    local.refs.movement.TryToJump();
                }
                yield return null;
            }
            float freedIn = Time.time - start;
            yield return new WaitForSeconds(0.2f);
            bool free = afflictions.GetCurrentStatus(Status.FlyTrap) <= 0.001f && !afflictions.isStruggling;
            bool lettingGo = plant != null && !plant.Holding && !plant.Biting;
            yield return ClownPicture(local.Center, "clown-flytrap-spit");
            float goneBy = Time.time + ClownFlyTrap.SpitSeconds + ClownFlyTrap.SinkSeconds + 1f;
            while (plant != null && Time.time < goneBy)
            {
                yield return null;
            }
            bool gone = plant == null;
            Check(name + ": on a map with the Gloom its fly trap springs up under the scout and holds them in its jaws (a grip of 45), never poisoning; "
                + "they jump free, it spits them out and sinks away, and no injury (21)",
                opened && Mathf.Abs(grip - 0.45f) < 0.002f && bar >= 0.425f && caught && biting && under < 0.8f && Mathf.Abs(size - ClownFlyTrap.Size) < 0.01f
                && poison < 0.0001f && free && lettingGo && gone && unhurt,
                $"{where}, opened {opened}: grip {grip * 100f:0.0} (the bar {bar * 100f:0.#}), caught {caught}; the plant {size:0.00} of its size, jaws shut {biting}, rooted {under:0.00} m (flat) from the scout, "
                + $"the scout's middle {held:0.00} m above its root and its jaws' hold {jaws:0.00} m; poison while held {poison * 100f:0.#}; free {free} after {Struggling.Jumps - jumpsBefore} jumps in {freedIn:0.0} s; "
                + $"letting go {lettingGo}, gone {gone}; all that landed at the opening: {all}");
        }
        finally
        {
            afflictions.OnAddedIncrementalStatus -= poisoned;
        }
    }

    /// <summary>
    /// Flat ground 2.5-6 m from a chest to stand on, in sight of it and 5 m clear of lava (the Kiln's lava streams run
    /// beside its chests: a scout knocked down by a trick would fall into one).
    /// </summary>
    private static Vector3? BesideChest(Vector3 at)
    {
        for (int ring = 0; ring <= 6; ring++)
        {
            float reach = Mathf.Lerp(2.5f, 6f, ring / 6f);
            for (int angle = 0; angle < 360; angle += 20)
            {
                Vector3 above = at + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * reach + Vector3.up * 4f;
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit floor, 8f, HelperFunctions.terrainMapMask) || floor.normal.y < 0.9f
                    || InsideSomething(floor.point + Vector3.up) || !Foothold(floor.point) || Hazard(floor.point + Vector3.up, 1.5f)
                    || Physics.OverlapSphere(floor.point, 5f, ~0, QueryTriggerInteraction.Collide).Any(c => c.GetComponentInParent<Lava>() != null)
                    || HelperFunctions.LineCheck(floor.point + Vector3.up * 1.2f, at, HelperFunctions.LayerType.TerrainMap).transform != null)
                {
                    continue;
                }
                return floor.point;
            }
        }
        return null;
    }

    /// <summary>A picture of a point from 3.5 m to its side (a side with a clear view), a little above it.</summary>
    private IEnumerator ClownPicture(Vector3 at, string picture)
    {
        Vector3 from = at + new Vector3(3.5f, 1f, 0f);
        for (int angle = 0; angle < 360; angle += 45)
        {
            Vector3 probe = at + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 3.5f + Vector3.up * 1f;
            if (HelperFunctions.LineCheck(at + Vector3.up * 0.3f, probe, HelperFunctions.LayerType.TerrainMap).transform == null)
            {
                from = probe;
                break;
            }
        }
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        try
        {
            Camera camera = holder.AddComponent<Camera>();
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.05f;
            holder.transform.position = from;
            holder.transform.LookAt(at);
            yield return new WaitForSeconds(0.2f);
            Screenshot(picture);
            yield return new WaitForSeconds(0.3f);
        }
        finally
        {
            Destroy(holder);
        }
    }

    /// <summary>The blindness and the darkened screen taken away.</summary>
    private static void Unblind(Character local)
    {
        CharacterAfflictions afflictions = local.refs.afflictions;
        while (afflictions.HasAfflictionType(Affliction.AfflictionType.Blind, out Affliction blind))
        {
            afflictions.RemoveAffliction(blind);
        }
        local.AddIllegalStatus("BLIND", 0f);
    }
}
