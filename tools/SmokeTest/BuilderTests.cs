using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;
using Status = CharacterAfflictions.STATUSTYPE;
using Tfa = UltimateAscent.Compat.Tfa;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// True Final Ascent's Builder ("--ua-smoketest-chain=S,A,K*200,..."): the climb is a chain of TFA's Builder, set in memory
/// before departure. Each block is visited in turn (the first of each of the game's segments by the game's own jump, the
/// others by lighting the campfire before them, as climbing does) and the rules that go by the block are checked there.
/// </summary>
internal sealed partial class Runner
{
    /// <summary>The Builder's chain for this climb (null: a regular climb).</summary>
    internal string Chain;

    private static Type TfaType(string name) => AccessTools.TypeByName("TrueFinalAscent." + name);

    /// <summary>The boarding pass starts TFA's FINAL ASCENT on the test's ascent, a Builder climb of the chain (in memory only).</summary>
    private void SetBuilderChain(BoardingPass pass)
    {
        Type block = TfaType("ChainBlock");
        MethodInfo parse = block != null ? AccessTools.Method(TfaType("FinalAscentChain"), "TryParse", new[] { typeof(string), typeof(List<>).MakeGenericType(block).MakeByRefType() }) : null;
        object[] args = { Chain, null };
        if (parse == null || !(bool)parse.Invoke(null, args))
        {
            throw new InvalidOperationException($"TFA cannot read the chain '{Chain}'");
        }
        IList chosen = (IList)AccessTools.Field(TfaType("FinalAscentSetup"), "Chain").GetValue(null);
        chosen.Clear();
        foreach (object one in (IList)args[1])
        {
            chosen.Add(one);
        }
        AccessTools.Property(TfaType("FinalAscentSetup"), "ChainOn").SetValue(null, true);
        AccessTools.Property(TfaType("FinalAscentDifficulty"), "Chosen").SetValue(null, target);
        BuilderConfirmed(true);
        attemptsBefore = RunCounts.Started(target);
        pass.ascentIndex = BoardingPassUi.FinalAscentIndex;
        Info("TFA's Builder", $"FINAL ASCENT on ascent {target}, chain {Chain}; attempts on the ticket before: {attemptsBefore}");
    }

    /// <summary>TFA's setup window has been confirmed (only then does FINAL ASCENT start); put back after departure.</summary>
    private static void BuilderConfirmed(bool on) => AccessTools.Property(TfaType("FinalAscentSetupWindow"), "Confirmed").SetValue(null, on);

    private static Place PlaceOf(string kind)
    {
        switch (kind)
        {
            case "Shore": return Place.Shore;
            case "Tropics": return Place.Tropics;
            case "Roots": return Place.Roots;
            case "Alpine": return Place.Alpine;
            case "Mesa": return Place.Mesa;
            case "Caldera": return Place.Caldera;
            case "Kiln": return Place.Kiln;
            case "Gloom": return Place.Gloom;
            case "Citadel": return Place.Citadel;
            case "Nadir": return Place.Nadir;
            default: return Place.Unknown;
        }
    }

    /// <summary>Each block of the Builder's climb in turn, and what the mod's rules make of it.</summary>
    private IEnumerator BuilderChecks()
    {
        if (string.IsNullOrEmpty(Chain) || !Tfa.BuilderActive)
        {
            Info("the Builder's checks are skipped", string.IsNullOrEmpty(Chain) ? "run with -Chain" : $"no Builder climb (TFA {Tfa.Present}, FINAL ASCENT {Tfa.FinalAscentActive})");
            yield break;
        }
        MapHandler map = Singleton<MapHandler>.Instance;
        List<Tfa.Stage> stages = Tfa.Stages().ToList();
        int GroupOf(Tfa.Stage stage) => Array.FindIndex(map.segments, segment => segment != null && segment.segmentParent == stage.Terrain);
        Info("the Builder's blocks", string.Join(", ", stages.Select(s => s.Kind + (GroupOf(s) >= 0 ? $" (first of segment {GroupOf(s)})" : ""))));

        bool blocked = RunSettings.blockingAchievements;
        Check(blocked ? "TFA keeps this chain from earning achievements: the climb is no attempt on its ascent's ticket"
                : "TFA lets this chain earn achievements: the climb counts as an attempt on its ascent's ticket",
            attemptsBefore >= 0 && RunCounts.Started(target) == attemptsBefore + (blocked ? 0 : 1),
            $"ascent {Ascents.currentAscent}; attempts {attemptsBefore} -> {RunCounts.Started(target)}");
        WallsAndSun();
        MildNights();

        // Keep the walk on the ground: no extra tornadoes (25).
        ExtraTornadoes.untilNext = 100000f;
        Protect(true);
        Character local = Local;
        for (int i = 0; i < stages.Count; i++)
        {
            Tfa.Stage stage = stages[i];
            string name = $"block {i + 1}, {stage.Kind}";
            HashSet<int> mobsBefore = new HashSet<int>(Mobs().Select(m => m.GetInstanceID()));
            if (i > 0)
            {
                // As the climb goes on: at the campfire before the block, lit (it moves the climb on to the block, and TFA
                // wakes what the block has).
                Campfire fire = stage.Before != null ? stage.Before.GetComponentInChildren<Campfire>(true) : null;
                if (fire == null)
                {
                    Fail($"{name}: reached", "no campfire before it");
                    continue;
                }
                Vector3 away = local.Center - fire.transform.position;
                away.y = 0f;
                Vector3 stand = fire.transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back) * 1.6f + Vector3.up * 1.2f;
                yield return WarpOnto(stand);
                if (sleepingGhosts != null)
                {
                    // At the Nadir block's campfire, past its soul: TFA releases its ghosts.
                    yield return GhostsWoken();
                }
                Step($"light the campfire before {name}", () => fire.Interact_CastFinished(local));
                for (float until = Time.time + 25f; Time.time < until && (Tfa.CurrentStage == null || Tfa.CurrentStage.Terrain != stage.Terrain);)
                {
                    yield return new WaitForSeconds(0.5f);
                }
                int group = GroupOf(stage);
                if ((Tfa.CurrentStage == null || Tfa.CurrentStage.Terrain != stage.Terrain) && group >= 0)
                {
                    Info($"{name}: the campfire did not move the climb on", "the game's jump to its segment instead");
                    MapHandler.JumpToSegment((Segment)group);
                    for (float until = Time.time + 25f; Time.time < until && (Tfa.CurrentStage == null || Tfa.CurrentStage.Terrain != stage.Terrain);)
                    {
                        yield return new WaitForSeconds(0.5f);
                    }
                }
                // The block's spawners run as it begins.
                yield return new WaitForSeconds(5f);
            }
            Tfa.Stage here = Tfa.CurrentStage;
            Place expected = PlaceOf(stage.Kind);
            Check($"{name}: reached, and the mod takes it for the {expected}", here != null && here.Terrain == stage.Terrain && Map.CurrentPlace == expected
                    && (expected != Place.Nadir || Map.InNadir),
                $"TFA's block now {here?.Kind} ({(here?.Terrain != null ? here.Terrain.name : "none")}); the mod's place {Map.CurrentPlace}, Nadir {Map.InNadir}, the game's segment {Map.CurrentSegment}");
            Screenshot($"builder-{i + 1}-{stage.Kind.ToLowerInvariant()}");

            switch (stage.Kind)
            {
                case "Shore":
                    yield return BlockMobs<BeachCrab, CrabSpawner>(stage, name, "crabs", 0, mobsBefore);
                    yield return RestStopSun(i, name);
                    break;
                case "Tropics":
                    yield return BlockMobs<Snake, SnakeSpawner>(stage, name, "snakes", (int)Segment.Tropics, mobsBefore);
                    break;
                case "Alpine":
                    yield return BlockMobs<Goat, GoatSpawner>(stage, name, "mountain goats", (int)Segment.Alpine, mobsBefore);
                    // Past ascent 25's calm after waking up.
                    for (float until = Time.time + 40f; Time.time < until && Features.Weather.WeatherGrace.Blocking;)
                    {
                        yield return new WaitForSeconds(1f);
                    }
                    OneStormEach(name);
                    yield return SnowstormHere(name);
                    break;
                case "Kiln":
                    yield return BlockKiln(stage, name);
                    FastFog(name);
                    break;
                case "Nadir":
                    BlockNadir(stage, name);
                    FastFog(name);
                    break;
                case "Caldera":
                    yield return BlockCaldera(stage, name);
                    FastFog(name);
                    break;
            }
        }
        yield return PeakAfterTheChain();
    }

    /// <summary>A Nadir block's ghosts, asleep until TFA releases them (checked once it has: see <see cref="GhostsWoken"/>).</summary>
    private LavaRising sleepingGhosts;
    private string sleepingGhostsOf;

    /// <summary>Once TFA has released a Nadir block's ghosts (the scout past its soul, at its campfire): they rise 2.5 times as fast (9).</summary>
    private IEnumerator GhostsWoken()
    {
        LavaRising ghosts = sleepingGhosts;
        string name = sleepingGhostsOf;
        sleepingGhosts = null;
        for (float until = Time.time + 20f; Time.time < until && (ghosts == null || !ghosts.isActiveAndEnabled || !ghosts.started);)
        {
            yield return new WaitForSeconds(0.5f);
        }
        if (ghosts == null || !ghosts.isActiveAndEnabled)
        {
            Info($"{name}: its ghosts did not wake within 20 s at its campfire", "the 2.5 times faster rise is not seen");
            yield break;
        }
        yield return new WaitForSeconds(1f);
        float original = Overrides.Original(ghosts, "travelTime", ghosts.travelTime);
        Check($"{name}: once TFA releases its ghosts, they rise 2.5 times as fast (9)", Mathf.Abs(ghosts.travelTime - original / 2.5f) < 1f,
            $"rise in {ghosts.travelTime:F0} s (TFA's {original:F0} s); started {ghosts.started}, in the Nadir {Map.InNadir}, segment {ghosts.requiredSegment} (the scouts' {Map.CurrentSegment})");
    }

    /// <summary>
    /// Where another mod's biome has mild nights (TFA's cold nights stay away there), there is no night for the mod's rules
    /// either. No such biome is installed: TFA is told for one frame that this block's biome has them.
    /// </summary>
    private void MildNights()
    {
        Type rules = TfaType("ModStageRules");
        Type parts = AccessTools.TypeByName("TrueFinalAscent.Api.TfaFrameParts");
        FieldInfo here = AccessTools.Field(TfaType("FinalAscentModBiomes"), "_rulesHere");
        if (rules == null || parts == null || here == null)
        {
            Info("the mild nights check is skipped", "this TFA has no rules for other mods' biomes");
            return;
        }
        object mild = Activator.CreateInstance(rules, true);
        AccessTools.Field(rules, "Drop").SetValue(mild, Enum.Parse(parts, "ColdNight"));
        bool before = Map.DayNightApplies;
        object was = here.GetValue(null);
        bool during;
        try
        {
            here.SetValue(null, mild);
            during = Map.DayNightApplies;
        }
        finally
        {
            here.SetValue(null, was);
        }
        Check("where another mod's biome has mild nights, the mod's night rules rest there too", before && !during,
            $"day and night count: {before} here, {during} with the biome's mild nights, {Map.DayNightApplies} after");
    }

    /// <summary>A snowstorm blowing on the scout in an Alpine block: its cold (by day too), wherever the block stands in the chain (18).</summary>
    private IEnumerator SnowstormHere(string name)
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather))
        {
            yield break;
        }
        WindChillZone[] snows = FindObjectsByType<WindChillZone>(FindObjectsSortMode.None).Where(z => z.name == "SnowStorm").ToArray();
        WindChillZone snow = snows.FirstOrDefault(z => !A25_AllWeather.SuppressZone(z)) ?? snows.FirstOrDefault();
        if (snow == null)
        {
            Info($"{name}: the snowstorm check is skipped", "no snowstorm here");
            yield break;
        }
        MethodInfo apply = AccessTools.Method(typeof(WindChillZone), "ApplyStatus");
        Character local = Local;
        bool night = Map.IsNight;
        float exposure = 0f;
        HeldWeather.Hold(false);
        Protect(false);
        ClearStatuses(Afflictions);
        float cold0 = Afflictions.GetCurrentStatus(Status.Cold);
        float hot0 = Afflictions.GetCurrentStatus(Status.Hot);
        float cold;
        float hot;
        try
        {
            for (float until = Time.time + 3f; Time.time < until;)
            {
                snow.hasBeenActiveFor = 10f;
                snow.currentForceMult = 1f;
                apply.Invoke(snow, new object[] { local });
                exposure = Mathf.Max(exposure, snow.windPlayerFactor);
                yield return null;
            }
        }
        finally
        {
            cold = Afflictions.GetCurrentStatus(Status.Cold) - cold0;
            hot = Afflictions.GetCurrentStatus(Status.Hot) - hot0;
            ClearStatuses(Afflictions);
            Protect(true);
            HeldWeather.Hold(true);
        }
        string detail = $"3 s in the snowstorm by {(night ? "night" : "day")}, exposed up to {exposure:0.00}: cold +{cold * 100f:0.##}, heat +{hot * 100f:0.##}; the game's segment {Map.CurrentSegment}, the mod's place {Map.CurrentPlace}";
        if (exposure < 0.05f)
        {
            Info($"{name}: the snowstorm check is inconclusive (the scout stands sheltered)", detail);
            yield break;
        }
        Check($"{name}: a snowstorm chills here, wherever the block stands in the chain (18)", cold > 0.001f && hot < 0.001f, detail);
    }

    /// <summary>The rest area TFA made for the chain's block at this place (a block at 400% or more), if any.</summary>
    private static Component RestAreaOf(int block)
    {
        Type type = TfaType("FinalAscentRestArea");
        FieldInfo place = type != null ? AccessTools.Field(type, "Place") : null;
        return place == null ? null : Resources.FindObjectsOfTypeAll(type).OfType<Component>()
            .FirstOrDefault(c => c != null && c.gameObject.scene.IsValid() && (int)place.GetValue(c) == block);
    }

    /// <summary>The heat the mod's own sun (ascent 23) gives the scout in so long (counted as it comes; the scout's bar locked).</summary>
    private IEnumerator OwnSun(float seconds, Action<float> heat)
    {
        HeatSources.Clear();
        HeatSources.On = true;
        yield return new WaitForSeconds(seconds);
        HeatSources.On = false;
        heat(HeatSources.Totals.Where(kv => kv.Key.StartsWith("A15_StrongerAilments.AddUnboosted", StringComparison.Ordinal)).Sum(kv => kv.Value));
    }

    /// <summary>At a block's rest stop (a Shore at 400%) the mod's sun (ascent 23) spares the resting scout; outside it, it burns.</summary>
    private IEnumerator RestStopSun(int block, string name)
    {
        Component area = Rules.Enabled(Lvl.HotSun) ? RestAreaOf(block) : null;
        if (area == null)
        {
            yield break;
        }
        float radius = (float)AccessTools.Field(area.GetType(), "Radius").GetValue(area);
        Character local = Local;
        Vector3 middle = area.transform.position;
        RaycastHit ground = HelperFunctions.LineCheck(middle + Vector3.up * 20f, middle + Vector3.down * 40f, HelperFunctions.LayerType.TerrainMap);
        yield return WarpOnto((ground.transform != null ? ground.point : middle) + Vector3.up * 1.5f);
        yield return new WaitForSeconds(2f);
        bool spared = Tfa.RestSpares(local);
        bool sunIn = InSunlight(local);
        float inside = 0f;
        yield return OwnSun(4f, h => inside = h);
        // Out on the ground past the rest stop's edge, in sunlight (a few spots tried).
        float outside = -1f;
        int tries = 0;
        for (float distance = radius + 15f; distance <= radius + 75f && outside < 0f && tries < 8; distance += 20f)
        {
            for (int angle = 0; angle < 360 && outside < 0f && tries < 8; angle += 60)
            {
                Vector3 probe = middle + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                RaycastHit below = HelperFunctions.LineCheck(probe + Vector3.up * 80f, probe + Vector3.down * 120f, HelperFunctions.LayerType.TerrainMap);
                if (below.transform == null || below.normal.y < 0.75f)
                {
                    continue;
                }
                tries++;
                yield return WarpOnto(below.point + Vector3.up * 1.2f);
                yield return new WaitForSeconds(2.5f);
                if (Tfa.RestSpares(local) || !InSunlight(local))
                {
                    continue;
                }
                yield return OwnSun(4f, h => outside = h);
            }
        }
        string detail = $"at its rest stop (r {radius:0}): spared {spared}, in sunlight {sunIn}, the mod's sun {inside * 100f:0.##} in 4 s; outside it, in sunlight and not spared: "
            + $"{(outside >= 0f ? (outside * 100f).ToString("0.##") : "no such spot in " + tries + " tried")}; {(DayNightManager.instance != null && DayNightManager.instance.isDay >= 0.5f ? "day" : "night")}";
        if (!sunIn || outside < 0f)
        {
            Info($"{name}: the rest stop's sun check is inconclusive (no sunlight in or out)", detail);
            yield break;
        }
        Check($"{name}: its rest stop spares the resting scout from the mod's sun (23), which burns outside it", spared && inside <= 0f && outside > 0f, detail);
    }

    /// <summary>
    /// At a rest stop (the Caldera's island at 400%) while the scouts rest: the storms spare them, no extra tornado comes, and
    /// a calm lasts (25); away from it, all come back.
    /// </summary>
    private IEnumerator RestStopWeather(Tfa.Stage stage, string name)
    {
        if (!Rules.Enabled(Lvl.AllWeather) || !A25_AllWeather.DrivesVanillaWeather)
        {
            yield break;
        }
        for (float until = Time.time + 5f; Time.time < until && !Tfa.RestPaused;)
        {
            yield return new WaitForSeconds(0.25f);
        }
        Character local = Local;
        WindChillZone snow = FindObjectsByType<WindChillZone>(FindObjectsSortMode.None).FirstOrDefault(z => z.name == "SnowStorm" && !A25_AllWeather.SuppressZone(z));
        MethodInfo apply = AccessTools.Method(typeof(WindChillZone), "ApplyStatus");
        Type phases = typeof(Features.Weather.WeatherPhases);
        Type phaseType = AccessTools.Inner(phases, "Phase");
        object Calm()
        {
            object phase = AccessTools.CreateInstance(phaseType);
            Traverse t = Traverse.Create(phase);
            t.Field("run").SetValue(Net.RunKey);
            t.Field("clock").SetValue(Net.Clock - 100.0);
            t.Field("duration").SetValue(10f);
            t.Field("active").SetValue(false);
            t.Field("direction").SetValue(1);
            return phase;
        }
        Traverse snowState = Traverse.Create(phases).Field("snow").Field("state");
        // Storms, tornadoes and the weather clock as they are; the scout's bar locked.
        HeldWeather.Hold(false);
        ExtraTornadoes.untilNext = 0f;
        int Tornadoes() => FindObjectsByType<Tornado>(FindObjectsSortMode.None).Count(ExtraTornadoes.IsCustom);
        int tornadoesBefore = Tornadoes();
        float factor = -1f;
        bool slipped = false;
        bool clockHeld = false;
        bool paused = Tfa.RestPaused;
        try
        {
            for (float until = Time.time + 3f; Time.time < until;)
            {
                if (snow != null)
                {
                    local.data.slippy = 0f;
                    snow.hasBeenActiveFor = 10f;
                    snow.currentForceMult = 1f;
                    apply.Invoke(snow, new object[] { local });
                    factor = Mathf.Max(factor, snow.windPlayerFactor);
                    slipped |= local.data.slippy > 0f;
                }
                yield return null;
            }
            if (snow != null)
            {
                object calm = Calm();
                snowState.SetValue(calm);
                Features.Weather.WeatherPhases.Apply(snow);
                clockHeld = ReferenceEquals(snowState.GetValue(), calm);
            }
        }
        finally
        {
            ExtraTornadoes.untilNext = 100000f;
        }
        int tornadoesResting = Tornadoes() - tornadoesBefore;
        Check($"{name}: while the scouts rest at its rest stop, no storm reaches them, no extra tornado comes and a calm lasts (25)",
            paused && snow != null && factor <= 0f && !slipped && tornadoesResting == 0 && clockHeld,
            $"resting {paused}; the snowstorm's reach {factor:0.00}, slippery {slipped}; extra tornadoes come {tornadoesResting}; the calm lasts {clockHeld}");

        // Away from the rest stop: at the campfire the block began at.
        Campfire start = stage.Before != null ? stage.Before.GetComponentInChildren<Campfire>(true) : null;
        if (start != null)
        {
            yield return WarpOnto(start.transform.position + Vector3.back * 1.6f + Vector3.up * 1.2f);
            for (float until = Time.time + 5f; Time.time < until && Tfa.RestPaused;)
            {
                yield return new WaitForSeconds(0.25f);
            }
            ExtraTornadoes.untilNext = 0f;
            int before = Tornadoes();
            for (float until = Time.time + 8f; Time.time < until && Tornadoes() == before;)
            {
                yield return new WaitForSeconds(0.25f);
            }
            int came = Tornadoes() - before;
            bool clockRuns = false;
            if (snow != null)
            {
                object calm = Calm();
                snowState.SetValue(calm);
                Features.Weather.WeatherPhases.Apply(snow);
                clockRuns = !ReferenceEquals(snowState.GetValue(), calm);
            }
            Check($"{name}: away from the rest stop, the extra tornadoes come and the weather moves on again (25)", !Tfa.RestPaused && came > 0 && clockRuns,
                $"resting {Tfa.RestPaused}; extra tornadoes came {came}; the weather moved on {clockRuns}");
        }
        foreach (Tornado tornado in FindObjectsByType<Tornado>(FindObjectsSortMode.None).Where(ExtraTornadoes.IsCustom).ToList())
        {
            Photon.Pun.PhotonNetwork.Destroy(tornado.gameObject);
        }
        ExtraTornadoes.untilNext = 100000f;
        Features.Weather.WeatherPhases.Reset();
        HeldWeather.Hold(true);
    }

    /// <summary>The peak after the chain's last block (TFA puts it there): its hill's zombie places round its summit, and the zombies (17).</summary>
    private IEnumerator PeakAfterTheChain()
    {
        if (!Rules.Enabled(Lvl.Hazards))
        {
            yield break;
        }
        Transform summit = PeakHill.Summit();
        if (summit == null)
        {
            Fail("the peak after the chain", "no summit found");
            yield break;
        }
        RaycastHit ground = HelperFunctions.LineCheck(summit.position + Vector3.up * 10f, summit.position + Vector3.down * 30f, HelperFunctions.LayerType.TerrainMap);
        yield return WarpOnto((ground.transform != null ? ground.point : summit.position) + Vector3.up * 1.5f);
        for (float until = Time.time + 30f; Time.time < until && PeakZombies.Places.Count < PeakZombies.Count;)
        {
            yield return new WaitForSeconds(0.5f);
        }
        yield return new WaitForSeconds(4f);
        int zombies = FindObjectsByType<MushroomZombie>(FindObjectsSortMode.None).Count(z => PeakZombies.IsPeakZombie(z, out _));
        float farthest = PeakZombies.Places.Count > 0 ? PeakZombies.Places.Max(p => Vector3.Distance(p, summit.position)) : -1f;
        Screenshot("builder-peak");
        Check("the peak after the chain's last block: its hill has the zombies' fifteen places round its summit, and they come (17)",
            PeakZombies.Places.Count == PeakZombies.Count && farthest >= 0f && farthest < 150f && zombies > 0,
            $"{PeakZombies.Places.Count} places, the farthest {farthest:0} m from the summit at {summit.position:F0}; {zombies} zombies about; place {Map.CurrentPlace}");
    }

    /// <summary>
    /// The scout warped to a spot and kept there until there is ground under it: TFA switches far parts of its mountain off
    /// and back on as the scouts come near, so a warp can arrive before the ground does (and the scout would fall).
    /// </summary>
    private IEnumerator WarpOnto(Vector3 spot)
    {
        Character local = Local;
        for (float until = Time.time + 15f; Time.time < until;)
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot, false);
            yield return new WaitForSeconds(1f);
            bool ground = HelperFunctions.LineCheck(spot + Vector3.up, spot + Vector3.down * 4f, HelperFunctions.LayerType.TerrainMap).transform != null;
            if (ground && Vector3.Distance(local.Center, spot) < 3f)
            {
                yield return new WaitForSeconds(1f);
                if (Vector3.Distance(local.Center, spot) < 3f)
                {
                    yield break;
                }
            }
        }
        Info("warped to a spot without ground under it within 15 s", $"{spot:F1}; the scout at {local.Center:F1}");
    }

    private static IEnumerable<Component> Mobs() =>
        FindObjectsByType<BeachCrab>(FindObjectsSortMode.None).Cast<Component>()
            .Concat(FindObjectsByType<Snake>(FindObjectsSortMode.None))
            .Concat(FindObjectsByType<Goat>(FindObjectsSortMode.None));

    /// <summary>A Shore, Tropics or Alpine block has its own spawner of the new mobs, and its mobs live along its own way.</summary>
    private IEnumerator BlockMobs<TMob, TSpawner>(Tfa.Stage stage, string name, string mobs, int segment, HashSet<int> before) where TMob : Component where TSpawner : Component
    {
        if (!NewMobs.On)
        {
            Info($"{name}: the {mobs} checks are skipped", "the new mobs are switched off");
            yield break;
        }
        List<TMob> fresh = new List<TMob>();
        for (float until = Time.time + 10f; Time.time < until; )
        {
            fresh = FindObjectsByType<TMob>(FindObjectsSortMode.None).Where(m => !before.Contains(m.GetInstanceID())).ToList();
            if (fresh.Count > 0)
            {
                break;
            }
            yield return new WaitForSeconds(0.5f);
        }
        bool spawner = stage.Terrain != null && stage.Terrain.GetComponentInChildren<TSpawner>(true) != null;
        if (stage.First)
        {
            // The first block's mobs came with the climb's start.
            fresh = FindObjectsByType<TMob>(FindObjectsSortMode.None).ToList();
        }
        bool way = Map.WayThrough(stage.Terrain != null ? stage.Terrain.transform : null, segment, out Vector3 a, out Vector3 b);
        int along = fresh.Count(m => way && Crabs.Share(m.transform.position, a, b) > -0.3f && Crabs.Share(m.transform.position, a, b) < 1.3f && Across(m.transform.position, a, b) < 150f);
        Check($"{name}: its own {mobs} (a spawner of its own, the {mobs} along its own way)", spawner && fresh.Count > 0 && along >= fresh.Count * 0.8f,
            $"spawner {spawner}; {fresh.Count} new {mobs}, {along} along this block's way ({(way ? $"{a:F0} to {b:F0}" : "way not found")})");
    }

    /// <summary>How far a point is from the line between two others, across it (level).</summary>
    private static float Across(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 along = b - a;
        along.y = 0f;
        Vector3 from = point - a;
        from.y = 0f;
        return along.sqrMagnitude < 1f ? from.magnitude : Vector3.Cross(along.normalized, from).magnitude;
    }

    /// <summary>At ascent 25: one storm of each kind blows, however many copies of a biome the chain has (each copy has its own).</summary>
    private void OneStormEach(string name)
    {
        if (!A25_AllWeather.DrivesVanillaWeather)
        {
            return;
        }
        foreach (string storm in new[] { "SnowStorm", "RainStorm", "WindStorm" })
        {
            WindChillZone[] zones = FindObjectsByType<WindChillZone>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(z => z.name == storm && z.gameObject.activeInHierarchy).ToArray();
            int running = zones.Count(z => !A25_AllWeather.SuppressZone(z));
            Check($"{name}: one {storm} blows, not one per copy of its biome (25)", running == 1, $"{zones.Length} here, {running} not held calm");
        }
    }

    /// <summary>Ascent 9's fast fog goes wherever TFA's fog goes, the Kiln, the Nadir and the Caldera too.</summary>
    private void FastFog(string name)
    {
        if (!Rules.Enabled(Lvl.FogAndKiln))
        {
            return;
        }
        OrbFogHandler fog = FindFirstObjectByType<OrbFogHandler>();
        int id = fog != null ? fog.currentID : -1;
        bool on = fog != null && fog.origins != null && id >= 0 && id < fog.origins.Length && fog.origins[id] != null && !fog.origins[id].disableFog && Ascents.fogEnabled;
        Check($"{name}: ascent 9's fast fog goes with TFA's fog here (9)", fog != null && A09_FogAndKiln.Active(fog) == on, $"fog origin {id}, its fog on {on}, ascent 9's rule on it {fog != null && A09_FogAndKiln.Active(fog)}");
    }

    /// <summary>A Kiln (anywhere in the chain): its lava's timing (ascent 9), the ash keeping its lava (18), and its eruptions (17).</summary>
    /// <summary>The rising hazards of a kind in a block (its own ground's first), and how each one stands.</summary>
    private List<LavaRising> Risings(Tfa.Stage stage, LavaRising.RisingFieldType kind, string name)
    {
        List<LavaRising> all = Resources.FindObjectsOfTypeAll<LavaRising>().Where(l => l != null && l.gameObject.scene.IsValid() && l.risingFieldType == kind).ToList();
        Info($"{name}: the map's rising {kind}", string.Join("; ", all.Select(l => $"{HierarchyPath(l.transform)}: enabled {l.enabled}, active {l.gameObject.activeInHierarchy}, segment {l.requiredSegment}"
            + $", started {l.started}, rises in {l.travelTime:F0} s, under this block's ground {stage.Terrain != null && l.transform.IsChildOf(stage.Terrain.transform)}")) + $"; the scouts' segment {Map.CurrentSegment}");
        return all.Where(l => stage.Terrain != null && l.transform.IsChildOf(stage.Terrain.transform))
            .Concat(all.Where(l => stage.Root != null && l.transform.IsChildOf(stage.Root.transform) && l.enabled)).Distinct().ToList();
    }

    private IEnumerator BlockKiln(Tfa.Stage stage, string name)
    {
        LavaRising lava = null;
        for (float until = Time.time + 20f; Time.time < until && (lava == null || !lava.isActiveAndEnabled);)
        {
            lava = Risings(stage, LavaRising.RisingFieldType.Lava, name).FirstOrDefault();
            yield return new WaitForSeconds(lava != null && lava.isActiveAndEnabled ? 1f : 5f);
        }
        if (lava == null)
        {
            Fail($"{name}: its rising lava", "none found");
            yield break;
        }
        float original = Overrides.Original(lava, "travelTime", lava.travelTime);
        if (Rules.Enabled(Lvl.FogAndKiln))
        {
            float wanted = original * A09_FogAndKiln.RiseSeconds / A09_FogAndKiln.GameRiseSeconds;
            Check($"{name}: its lava starts within 30 s and rises at ascent 9's pace, 700 s where the game takes 1,200 (9)",
                Mathf.Abs(lava.travelTime - wanted) < 1f && lava.initialWaitTime <= 30f,
                $"rises in {lava.travelTime:F0} s (TFA's {original:F0} s; {wanted:F0} wanted), starts after {lava.initialWaitTime:F0} s; running {lava.isActiveAndEnabled}");
        }
        if (Rules.Enabled(Lvl.ExtremeWeather))
        {
            Check($"{name}: the Kiln's ash goes by this Kiln's lava (18)", KilnAsh.Lava == lava,
                $"the ash's lava {(KilnAsh.Lava != null ? HierarchyPath(KilnAsh.Lava.transform) : "none")}; this Kiln's {HierarchyPath(lava.transform)}");
        }
        if (Rules.Enabled(Lvl.Hazards) && PhotonNetwork.IsMasterClient)
        {
            Character local = Local;
            Vector3 spot = local.Center + Vector3.ProjectOnPlane(local.transform.forward, Vector3.up).normalized * 30f;
            RaycastHit ground = HelperFunctions.LineCheck(spot + Vector3.up * 30f, spot + Vector3.down * 60f, HelperFunctions.LayerType.TerrainMap);
            spot = ground.transform != null ? ground.point : spot;
            bool running = FindObjectsByType<EruptionSpawner>(FindObjectsSortMode.None).Any(s => s.isActiveAndEnabled);
            A17_Hazards.Erupt(spot);
            yield return new WaitForSeconds(1f);
            bool erupted = FindObjectsByType<RemoveAfterSeconds>(FindObjectsSortMode.None).Any(r => Vector3.Distance(r.transform.position, spot) < 0.5f);
            Check($"{name}: the Kiln can erupt here (17)", erupted, $"{(running ? "an eruption spawner runs here" : "no eruption spawner runs here (TFA leaves it out of a Kiln that does not follow its Caldera): the mod's own message")}");
        }
    }

    /// <summary>A Nadir block: the Nadir's rules (17, 18) and its ghosts rising 2.5 times as fast (9).</summary>
    private void BlockNadir(Tfa.Stage stage, string name)
    {
        Check($"{name}: the Nadir's rules hold here (17)", !Rules.Enabled(Lvl.NadirRecovery) || NadirRules.RecoveryShare < 1f,
            $"recovery share {NadirRules.RecoveryShare:0.##}");
        LavaRising ghosts = Risings(stage, LavaRising.RisingFieldType.VoidGhosts, name).FirstOrDefault();
        if (ghosts == null || !Rules.Enabled(Lvl.FogAndKiln))
        {
            Info($"{name}: the ghosts' check is skipped", ghosts == null ? "no rising ghosts in this block" : $"ascent {target}");
            return;
        }
        if (!ghosts.isActiveAndEnabled)
        {
            sleepingGhosts = ghosts;
            sleepingGhostsOf = name;
            // Asleep until TFA releases the block's ghosts (as the game does when the soul is freed): what ascent 9 goes by.
            Check($"{name}: its ghosts are this block's, the scouts' own, for ascent 9's 2.5 times faster rise once they wake (9)",
                ghosts.requiredSegment == Map.CurrentSegment && Map.InNadir, $"their segment {ghosts.requiredSegment}, the scouts' {Map.CurrentSegment}, in the Nadir {Map.InNadir}");
            return;
        }
        float original = Overrides.Original(ghosts, "travelTime", ghosts.travelTime);
        Check($"{name}: its ghosts rise 2.5 times as fast (9)", Mathf.Abs(ghosts.travelTime - original / 2.5f) < 1f,
            $"rise in {ghosts.travelTime:F0} s (TFA's {original:F0} s)");
    }

    /// <summary>A Caldera block: its own lava tide (18), its own last stretch ablaze (17), and TFA's rest island clear of the tide's heat.</summary>
    private IEnumerator BlockCaldera(Tfa.Stage stage, string name)
    {
        Lava river = stage.Terrain != null ? stage.Terrain.GetComponentsInChildren<Lava>(true).FirstOrDefault(l => l.name == "River") : null;
        if (river == null)
        {
            Fail($"{name}: its lava river", "none found");
            yield break;
        }
        if (Rules.Enabled(Lvl.ExtremeWeather))
        {
            Vector3 local = river.transform.localPosition;
            float low = river.transform.parent != null ? river.transform.parent.TransformPoint(new Vector3(local.x, 779f, local.z)).y : 779f;
            float lowest = float.MaxValue, highest = float.MinValue;
            for (float until = Time.time + 6f; Time.time < until;)
            {
                lowest = Mathf.Min(lowest, river.transform.position.y);
                highest = Mathf.Max(highest, river.transform.position.y);
                yield return null;
            }
            Check($"{name}: its lava river keeps the tide in its own basin, between {low:0.0} and {low + 14f:0.0} (18)",
                lowest >= low - 0.05f && highest <= low + 14.05f && highest - lowest > 0.001f,
                $"watched 6 s: from {lowest:0.00} to {highest:0.00}; the river's own heat {(river.enabled ? "on" : "off (TFA's lava tiles follow it)")}");
        }
        if (Rules.Enabled(Lvl.Hazards))
        {
            bool built = CalderaPathEruptions.Build();
            Campfire end = stage.Exit != null ? stage.Exit.GetComponentInChildren<Campfire>(true) : null;
            float off = end != null ? Vector3.Distance(CalderaPathEruptions.Campfire, end.transform.position) : -1f;
            Check($"{name}: its own last stretch is ablaze, up to its own campfire (17)", built && CalderaPathEruptions.Ground.Count > 0 && off >= 0f && off < 15f,
                $"built {built}, {CalderaPathEruptions.Ground.Count} spots; the stretch's campfire {off:0.0} m from this block's");
        }
        Transform island = river.transform.parent != null ? river.transform.parent.Find("TFA_CalderaRest") : null;
        Collider[] parts = island != null ? island.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
        if (parts.Length == 0 || !Rules.Enabled(Lvl.ExtremeWeather))
        {
            yield break;
        }
        Bounds top = parts[0].bounds;
        foreach (Collider part in parts)
        {
            top.Encapsulate(part.bounds);
        }
        RaycastHit onIt = HelperFunctions.LineCheck(new Vector3(top.center.x, top.max.y + 20f, top.center.z), new Vector3(top.center.x, top.min.y, top.center.z), HelperFunctions.LayerType.AllPhysical);
        Character scout = Local;
        yield return WarpOnto((onIt.transform != null ? onIt.point : new Vector3(top.center.x, top.max.y, top.center.z)) + Vector3.up * 1.5f);
        yield return new WaitForSeconds(1f);
        Protect(false);
        Afflictions.SetStatus(Status.Hot, 0f);
        yield return new WaitForSeconds(3f);
        float heat = Afflictions.GetCurrentStatus(Status.Hot);
        bool spared = CalderaTide.OnRestIsland(river);
        bool rest = Tfa.RestSpares(scout);
        Afflictions.SetStatus(Status.Hot, 0f);
        Protect(true);
        yield return RestStopWeather(stage, name);
        Check($"{name}: on TFA's rest island the lava's heat does not reach the scout (18), and TFA's rest stop spares them from the weather and the sun",
            spared && rest && heat < 0.005f,
            $"on the island (as the tide sees it) {spared}, spared by the rest stop {rest}, heat in 3 s {heat * 100f:0.##}; the river at {river.transform.position.y:0.0}, the island's top {top.max.y:0.0}");
    }

    /// <summary>The sun's look at the scouts passes through the Builder's invisible walls between its blocks (18, 23).</summary>
    private void WallsAndSun()
    {
        GameObject walls = GameObject.Find("FA_Walls");
        Collider wall = walls != null ? walls.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled && !c.isTrigger) : null;
        if (wall == null)
        {
            Info("the Builder's walls and the sun: skipped", "no wall up between the blocks right now");
            return;
        }
        Bounds bounds = wall.bounds;
        Vector3 across = bounds.size.x < bounds.size.z ? Vector3.right : Vector3.forward;
        float half = Vector3.Scale(bounds.extents, across).magnitude;
        if (wall is BoxCollider box)
        {
            // Across the box's own thinnest side (it may stand turned).
            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
            Vector3 axis = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
            across = box.transform.TransformDirection(axis).normalized;
            half = Mathf.Abs(Vector3.Dot(Vector3.Scale(size, axis), Vector3.one)) * 0.5f;
        }
        // Up the wall's face from its middle, both ways through it, until a plain look meets it (and not the ground first).
        RaycastHit plain = default;
        RaycastHit sun = default;
        bool found = false;
        for (float up = 0f; up <= bounds.extents.y && !found; up += 25f)
        {
            Vector3 middle = wall.ClosestPoint(bounds.center + Vector3.up * up);
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 from = middle + across * side * (half + 40f);
                Vector3 to = middle - across * side * (half + 2f);
                plain = HelperFunctions.LineCheck(from, to, HelperFunctions.LayerType.AllPhysical);
                if (plain.collider != null && plain.collider.transform.IsChildOf(walls.transform))
                {
                    sun = Tfa.SunLineCheck(from, to, HelperFunctions.LayerType.AllPhysical);
                    found = true;
                    break;
                }
            }
        }
        bool plainWall = plain.collider != null && plain.collider.transform.IsChildOf(walls.transform);
        bool sunWall = sun.collider != null && sun.collider.transform.IsChildOf(walls.transform);
        Check("the sun shines through the Builder's invisible walls between its blocks (18, 23)", plainWall && !sunWall,
            $"a plain look meets {(plain.collider != null ? HierarchyPath(plain.collider.transform) : "nothing")}; the sun's look meets {(sun.collider != null ? HierarchyPath(sun.collider.transform) : "nothing")}; "
            + $"the wall {wall.GetType().Name} on layer {LayerMask.LayerToName(wall.gameObject.layer)}, {bounds.size:F0} across, at {bounds.center:F0}");
    }
}
