using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: bee ambushes in the Tropics. Twice per in-game day, as the day begins and as the night
/// begins, every scout has a 50% chance to be ambushed by an angry swarm (no beehive needed) at a moment
/// of their own picked at random within that day or night (at least 30 s from anyone else's, so two
/// scouts are not set upon at once); a scout who is not in the Tropics when their moment comes is left
/// alone. The swarm appears right next to its scout.
/// Escaping works like with a stolen beehive: the swarm is tied to the spot it appeared at (it
/// follows you up to 120 m from there), so you have to outrun it or run far away from it; once it
/// loses you, it disperses. It stays angry for 50 s. Bees only ambush in the Tropics:
/// a scout standing anywhere else (also one lagging behind on the shore) is left alone.
/// </summary>
internal static class A17_BeeAmbush
{
    internal const string Marker = "UltimateAscent.bees";

    private const float ChancePerMoment = 0.5f;
    internal const float AngrySeconds = A17_Hazards.BeeAngerSeconds;

    /// <summary>An ambush swarm follows its scout up to 120 m from where it appeared, like a hive's bees from their hive.</summary>
    private const float Leash = A17_Hazards.BeeChaseDistance;
    private const string FallbackPrefab = "BeeSwarm";
    private const float MinSpawnDistance = 2f;
    private const float MaxSpawnDistance = 3f;

    /// <summary>An ambush swarm chases its scout at least this long, however far they run (then the usual leash applies).</summary>
    internal const float MinChaseSeconds = 20f;

    private sealed class Chase
    {
        internal float until;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BeeSwarm, Chase> chases = new System.Runtime.CompilerServices.ConditionalWeakTable<BeeSwarm, Chase>();

    /// <summary>For the tests: an ambush swarm still in its sure chase.</summary>
    internal static bool StillChasing(BeeSwarm swarm) => swarm != null && chases.TryGetValue(swarm, out Chase chase) && Time.time < chase.until;

    /// <summary>Two scouts' ambush moments are at least this far apart (real seconds of the clock's run).</summary>
    internal const float ApartSeconds = 30f;

    /// <summary>The time of day (hours) seen last frame; -1 before the first look.</summary>
    private static float lastTime = -1f;
    private static float lastCheckAt = -1000f;
    private static string prefabName;
    private static BeeSwarm template;

    /// <summary>The day or the night now running (from the last check): whether it is the day, when it began and when it ends (hours).</summary>
    private static bool stretchIsDay;
    private static float stretchFrom;
    private static float stretchTo;

    /// <summary>
    /// Each scout's ambush moment (the hour) in the day or night now running, by their view: NaN when their roll brought
    /// no ambush, infinity once it came. A scout without one gets their roll (one who joins later, for what is left).
    /// </summary>
    private static readonly Dictionary<int, float> moments = new Dictionary<int, float>();

    /// <summary>For the tests: no ambushes while a check needs the scout's statuses to itself.</summary>
    internal static bool HeldOff;

    /// <summary>For the tests: the chance of a scout's ambush at each check.</summary>
    internal static float Chance = ChancePerMoment;

    /// <summary>For the tests: the checks made, and the moments that came (an ambush or a scout not in the Tropics left alone).</summary>
    internal static int ChecksMade;
    internal static int MomentsCome;

    internal static void Reset()
    {
        lastTime = -1f;
        lastCheckAt = -1000f;
        moments.Clear();
    }

    /// <summary>The checks: as the day begins and as the night begins, in hours.</summary>
    internal static void Checks(DayNightManager clock, out float dayBegins, out float nightBegins)
    {
        dayBegins = clock.dayStart;
        nightBegins = clock.dayEnd;
    }

    /// <summary>For the tests: a scout's ambush moment in the day or night now running (NaN: none; infinity: it came).</summary>
    internal static bool MomentOf(Character scout, out float moment) => moments.TryGetValue(scout.photonView.ViewID, out moment);

    /// <summary>For the tests: the day or night now running.</summary>
    internal static (bool day, float from, float to) Stretch => (stretchIsDay, stretchFrom, stretchTo);

    /// <summary>The clock went past <paramref name="moment"/> between the two times (it wraps at 24).</summary>
    private static bool Passed(float from, float to, float moment)
    {
        if (from <= to)
        {
            return from < moment && moment <= to;
        }
        // A short step back is the clock being set back, not midnight going by.
        if (from - to < 12f)
        {
            return false;
        }
        return moment > from || moment <= to;
    }

    /// <summary>An ambush swarm, or one of the swarms waiting around the map (<see cref="ScatteredBees"/>): no hive, the same leash.</summary>
    internal static bool IsAmbush(BeeSwarm swarm)
    {
        object[] data = swarm.photonView != null ? swarm.photonView.InstantiationData : null;
        return data != null && data.Length > 0 && (data[0] as string == Marker || data[0] as string == ScatteredBees.Marker);
    }

    /// <summary>Host only; called every frame.</summary>
    internal static void Tick()
    {
        if (!Rules.Enabled(Lvl.Hazards) || !Rules.Hazard(RunSettings.SETTINGTYPE.Hazard_Bees) || !Rules.IsHost || !PhotonNetwork.InRoom || DayNightManager.instance == null || LoadingScreenHandler.loading || !MapHandler.ExistsAndInitialized)
        {
            return;
        }
        DayNightManager clock = DayNightManager.instance;
        float now = clock.timeOfDay % 24f;
        float before = lastTime;
        lastTime = now;
        if (HeldOff)
        {
            // Held off for a test: moments going by meanwhile are passed over, not all brought at once afterwards.
            return;
        }
        if (before < 0f)
        {
            // The first look of the climb: the rolls are for what is left of this day or night.
            Check(clock, now, false);
            return;
        }
        Checks(clock, out float dayBegins, out float nightBegins);
        // The host corrects the clock now and then; one check never counts twice.
        if ((Passed(before, now, dayBegins) || Passed(before, now, nightBegins)) && Time.time - lastCheckAt >= 30f)
        {
            lastCheckAt = Time.time;
            Check(clock, now, true);
        }
        foreach (Character scout in Scouts())
        {
            if (!moments.ContainsKey(scout.photonView.ViewID))
            {
                Roll(clock, scout, now);
            }
        }
        foreach (Character scout in Scouts())
        {
            if (moments.TryGetValue(scout.photonView.ViewID, out float moment) && !float.IsNaN(moment) && !float.IsInfinity(moment) && Passed(before, now, moment))
            {
                moments[scout.photonView.ViewID] = float.PositiveInfinity;
                MomentsCome++;
                if (scout.data.fullyConscious && InTropics(scout))
                {
                    Spawn(scout);
                }
                else
                {
                    Plugin.Debug($"Ascent 17: {scout.characterName}'s bee moment came; not in the Tropics (or not up), left alone.");
                }
            }
        }
    }

    /// <summary>A check: the day or the night now begun (or running, at the first look), with a fresh roll for every scout.</summary>
    private static void Check(DayNightManager clock, float now, bool begun)
    {
        stretchIsDay = now >= clock.dayStart && now < clock.dayEnd;
        stretchFrom = stretchIsDay ? clock.dayStart : clock.dayEnd;
        stretchTo = stretchIsDay ? clock.dayEnd : clock.dayStart;
        moments.Clear();
        ChecksMade++;
        foreach (Character scout in Scouts())
        {
            Roll(clock, scout, now);
        }
        int coming = moments.Values.Count(m => !float.IsNaN(m));
        Plugin.Debug($"Ascent 17: the bees' check {(begun ? "as the" : "in the")} {(stretchIsDay ? "day" : "night")} {(begun ? "begins" : "running")}: {coming} of {moments.Count} scout(s) to be ambushed, at "
            + string.Join(", ", moments.Values.Where(m => !float.IsNaN(m)).OrderBy(Since).Select(clock.FloatToTimeString)) + ".");
    }

    /// <summary>A scout's roll: an ambush at a moment of their own in what is left of the day or night, apart from everyone else's.</summary>
    private static void Roll(DayNightManager clock, Character scout, float now)
    {
        if (Random.value >= Chance)
        {
            moments[scout.photonView.ViewID] = float.NaN;
            return;
        }
        float apart = ApartSeconds / (Mathf.Max(clock.dayLengthInMinutes, 0.01f) * 60f) * 24f;
        List<float> others = moments.Values.Where(m => !float.IsNaN(m) && !float.IsInfinity(m)).Select(Since).ToList();
        float at = PickMoment(Since(now) + 0.01f, Mathf.Repeat(stretchTo - stretchFrom, 24f), others, apart);
        moments[scout.photonView.ViewID] = Mathf.Repeat(stretchFrom + at, 24f);
    }

    /// <summary>How long after the day or night now running began an hour is.</summary>
    private static float Since(float hour) => Mathf.Repeat(hour - stretchFrom, 24f);

    /// <summary>
    /// A moment between two times (hours into the day or night) at random among those at least this far from the others;
    /// where none is left that far from them all (more scouts than the day or night has room for), the one furthest from
    /// the nearest of them.
    /// </summary>
    internal static float PickMoment(float from, float to, List<float> others, float apart)
    {
        if (to <= from)
        {
            return from;
        }
        // The times still free: the whole stretch, less a span round each other moment.
        List<(float a, float b)> free = new List<(float, float)> { (from, to) };
        foreach (float other in others)
        {
            List<(float a, float b)> left = new List<(float, float)>();
            foreach ((float a, float b) in free)
            {
                if (other - apart > a)
                {
                    left.Add((a, Mathf.Min(b, other - apart)));
                }
                if (other + apart < b)
                {
                    left.Add((Mathf.Max(a, other + apart), b));
                }
            }
            free = left.Where(f => f.b > f.a).ToList();
        }
        float room = free.Sum(f => f.b - f.a);
        if (room > 1e-5f)
        {
            float pick = Random.Range(0f, room);
            foreach ((float a, float b) in free)
            {
                if (pick <= b - a)
                {
                    return a + pick;
                }
                pick -= b - a;
            }
            return free[free.Count - 1].b;
        }
        // No room: the furthest from the nearest other, among the ends and the middles between them.
        List<float> marks = others.Where(o => o > from && o < to).OrderBy(o => o).Prepend(from).Append(to).ToList();
        List<float> candidates = new List<float> { from, to };
        for (int i = 0; i + 1 < marks.Count; i++)
        {
            candidates.Add((marks[i] + marks[i + 1]) / 2f);
        }
        return candidates.OrderByDescending(c => others.Count == 0 ? float.PositiveInfinity : others.Min(o => Mathf.Abs(o - c))).First();
    }

    /// <summary>The scouts the bees may come for (anywhere: whether they are in the Tropics counts when their moment comes).</summary>
    private static List<Character> Scouts()
    {
        List<Character> scouts = new List<Character>();
        foreach (Character character in Character.AllCharacters)
        {
            if (character != null && !character.isBot && !character.isScoutmaster && character.data != null && !character.data.dead && character.photonView != null)
            {
                scouts.Add(character);
            }
        }
        return scouts;
    }

    /// <summary>The ground below the scout belongs to the Tropics (where it belongs to no biome, the climb's segment decides).</summary>
    internal static bool InTropics(Character character)
    {
        if (Map.TryGetGroundBiome(character.Center, out Biome.BiomeType biome))
        {
            return biome == Biome.BiomeType.Tropics;
        }
        return Map.CurrentPlace == Place.Tropics;
    }

    private static void Spawn(Character target)
    {
        string prefab = PrefabName();
        GameObject spawned = PhotonNetwork.Instantiate(prefab, SpawnPoint(target), Quaternion.identity, 0, new object[] { Marker });
        BeeSwarm swarm = spawned != null ? spawned.GetComponent<BeeSwarm>() : null;
        if (swarm == null)
        {
            return;
        }
        swarm.beehiveID = -1;
        swarm.maxHiveDistance = Leash;
        swarm.currentAggroCharacter = target;
        swarm.GetAngry(AngrySeconds);
        chases.AddOrUpdate(swarm, new Chase { until = Time.time + MinChaseSeconds });
        Plugin.Log.LogInfo($"Ascent 17: a bee swarm ambushes {target.characterName}.");
    }

    /// <summary>
    /// 2-3 m from the scout, in open air; it chases them from there (<see cref="ChasesItsScout"/>).
    /// </summary>
    private static Vector3 SpawnPoint(Character target)
    {
        float distance = Random.Range(MinSpawnDistance, MaxSpawnDistance);
        Vector3 center = target.Center;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 flat = Random.insideUnitCircle.normalized;
            Vector3 candidate = center + new Vector3(flat.x, 0f, flat.y) * distance + Vector3.up;
            if (HelperFunctions.LineCheck(center, candidate, HelperFunctions.LayerType.TerrainMap).transform == null)
            {
                return candidate;
            }
        }
        return center + Vector3.up * 2f;
    }

    /// <summary>The swarm prefab the game's beehives use.</summary>
    internal static string PrefabName()
    {
        if (prefabName != null)
        {
            return prefabName;
        }
        foreach (Beehive hive in Beehive.ALL_BEEHIVES)
        {
            if (hive != null && hive.beeSwarmPrefab != null)
            {
                template = hive.beeSwarmPrefab;
                return prefabName = template.gameObject.name;
            }
        }
        foreach (Item item in SingletonAsset<ItemDatabase>.Instance.itemLookup.Values)
        {
            Beehive hive = item != null ? item.GetComponent<Beehive>() : null;
            if (hive != null && hive.beeSwarmPrefab != null)
            {
                template = hive.beeSwarmPrefab;
                return prefabName = template.gameObject.name;
            }
        }
        return FallbackPrefab;
    }

    /// <summary>An ambush swarm belongs to no beehive (the game would otherwise hand it hive number 0).</summary>
    [HarmonyPatch(typeof(BeeSwarm), nameof(BeeSwarm.TryGetBeehive))]
    private static class NoHive
    {
        private static bool Prefix(BeeSwarm __instance)
        {
            if (!IsAmbush(__instance))
            {
                return true;
            }
            __instance.beehiveID = -1;
            return false;
        }
    }

    /// <summary>
    /// For its first 20 s an ambush swarm keeps after its scout however far they run or however far from where it
    /// appeared (the game drops a target beyond its aggro range or leash); only a scout who passes out loses it.
    /// </summary>
    [HarmonyPatch(typeof(BeeSwarm), nameof(BeeSwarm.TryDeAggro))]
    private static class ChasesItsScout
    {
        private static bool Prefix(BeeSwarm __instance)
        {
            Character target = __instance.currentAggroCharacter;
            return !(StillChasing(__instance) && target != null && target.data.fullyConscious);
        }
    }

    /// <summary>
    /// Like hive bees, an ambush swarm that lost its target near the edge of its range does not pick
    /// a new one there; it drifts back to where it appeared and disperses.
    /// </summary>
    [HarmonyPatch(typeof(BeeSwarm), nameof(BeeSwarm.UpdateAggro))]
    private static class StayLeashed
    {
        private static bool Prefix(BeeSwarm __instance)
        {
            if (!IsAmbush(__instance) || !__instance.photonView.IsMine)
            {
                return true;
            }
            __instance.maxHiveDistance = Leash;
            __instance.TryDeAggro();
            return __instance.currentAggroCharacter != null || __instance.currentLastSawHiveDistance <= __instance.maxHiveDistance - __instance.hiveAggroDistance;
        }
    }
}
