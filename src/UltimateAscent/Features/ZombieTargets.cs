using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: the awake zombies (the game's, and the grassy ones on the Peak) share out the scouts between them instead
/// of each going for the nearest. The host shares them out every second:
/// <list type="bullet">
/// <item>First every scout gets a zombie of their own, the closest pairs first (with fewer zombies than scouts, each
/// zombie a different scout).</item>
/// <item>Each zombie left over goes for the scout with the fewest zombies after them, the nearest of those; the zombies
/// nearest to a scout choose first.</item>
/// </list>
/// Once a zombie has a scout it chases them for at least 10 s (unless they die or pass out), and these count as
/// chased when the rest are shared out. A zombie only hunts scouts within 100 m (the game puts zombies to sleep with
/// nobody that near), and after those 10 s the scout it is after counts as a fifth nearer, so it does not keep
/// switching between two about as near.
/// </summary>
internal static class ZombieTargets
{
    private const float Every = 1f;
    private const float Urgent = 0.25f;
    internal const float Reach = 100f;
    internal const float Keep = 0.8f;

    /// <summary>A zombie chases the scout it was given for at least this long.</summary>
    internal const float AtLeast = 10f;

    /// <summary>When each zombie was given the scout it is after.</summary>
    private static readonly Dictionary<MushroomZombie, float> givenAt = new Dictionary<MushroomZombie, float>();

    private static float nextAt;
    private static float lastAt = -100f;

    /// <summary>For the tests.</summary>
    internal static int Shares;

    private static bool On => Rules.Enabled(Lvl.Hazards);

    internal static void Reset()
    {
        nextAt = 0f;
        lastAt = -100f;
        givenAt.Clear();
    }

    /// <summary>
    /// Who each zombie goes for (an index into <paramref name="scouts"/>, or -1): those still within their 10 s
    /// (<paramref name="held"/>) keep their scout; then every scout not yet chased gets one zombie, the closest pairs
    /// first; then each zombie left over takes the scout with the fewest zombies after them, the nearest of those.
    /// </summary>
    internal static int[] Share(IList<Vector3> zombies, IList<int> chasing, IList<Vector3> scouts, IList<bool> held = null)
    {
        int[] pick = Enumerable.Repeat(-1, zombies.Count).ToArray();
        int[] chasers = new int[scouts.Count];
        for (int z = 0; z < zombies.Count; z++)
        {
            if (held != null && z < held.Count && held[z] && chasing != null && z < chasing.Count && chasing[z] >= 0 && chasing[z] < scouts.Count)
            {
                pick[z] = chasing[z];
                chasers[chasing[z]]++;
            }
        }
        float Cost(int z, int s)
        {
            float distance = Vector3.Distance(zombies[z], scouts[s]);
            return chasing != null && z < chasing.Count && chasing[z] == s ? distance * Keep : distance;
        }
        bool InReach(int z, int s) => Vector3.Distance(zombies[z], scouts[s]) <= Reach;

        var pairs = Enumerable.Range(0, zombies.Count)
            .SelectMany(z => Enumerable.Range(0, scouts.Count).Where(s => InReach(z, s)).Select(s => (z, s, cost: Cost(z, s))))
            .OrderBy(p => p.cost);
        foreach ((int z, int s, float _) in pairs)
        {
            if (pick[z] < 0 && chasers[s] == 0)
            {
                pick[z] = s;
                chasers[s] = 1;
            }
        }
        var rest = Enumerable.Range(0, zombies.Count)
            .Where(z => pick[z] < 0 && Enumerable.Range(0, scouts.Count).Any(s => InReach(z, s)))
            .OrderBy(z => Enumerable.Range(0, scouts.Count).Where(s => InReach(z, s)).Min(s => Cost(z, s)))
            .ToList();
        foreach (int z in rest)
        {
            int best = Enumerable.Range(0, scouts.Count).Where(s => InReach(z, s)).OrderBy(s => chasers[s]).ThenBy(s => Cost(z, s)).First();
            pick[z] = best;
            chasers[best]++;
        }
        return pick;
    }

    private static bool Hunting(MushroomZombie zombie) => zombie != null && zombie.view != null && zombie.view.IsMine && zombie.character != null && !zombie.character.data.dead
        && zombie.currentState != MushroomZombie.State.Sleeping && zombie.currentState != MushroomZombie.State.Dead;

    /// <summary>The game's own test of a scout a zombie may go for.</summary>
    private static bool Prey(Character scout) => scout != null && !scout.isBot && !scout.data.dead && !scout.data.fullyPassedOut;

    private static void ShareOut()
    {
        lastAt = Time.time;
        nextAt = Time.time + Every;
        ZombieManager manager = ZombieManager.Instance;
        if (manager == null)
        {
            return;
        }
        List<MushroomZombie> zombies = manager.zombies.Where(Hunting).ToList();
        List<Character> scouts = Character.AllCharacters.Where(Prey).ToList();
        if (zombies.Count == 0)
        {
            return;
        }
        List<int> chasing = zombies.Select(z => scouts.IndexOf(z.currentTarget)).ToList();
        List<bool> held = zombies.Select((z, i) => chasing[i] >= 0 && givenAt.TryGetValue(z, out float at) && Time.time - at < AtLeast).ToList();
        int[] pick = Share(zombies.Select(z => z.character.Center).ToList(), chasing, scouts.Select(s => s.Center).ToList(), held);
        Shares++;
        for (int i = 0; i < zombies.Count; i++)
        {
            Character target = pick[i] >= 0 ? scouts[pick[i]] : null;
            if (target != zombies[i].currentTarget)
            {
                zombies[i].SetCurrentTarget(target);
                if (target != null)
                {
                    givenAt[zombies[i]] = Time.time;
                }
            }
        }
        foreach (MushroomZombie gone in givenAt.Keys.Where(z => z == null).ToList())
        {
            givenAt.Remove(gone);
        }
    }

    /// <summary>Instead of each awake zombie going for the nearest scout every 10 s, the host shares the scouts out (see above).</summary>
    [HarmonyPatch(typeof(MushroomZombie), "TryLookForTarget")]
    private static class SharedOut
    {
        private static bool Prefix(MushroomZombie __instance)
        {
            if (!On)
            {
                return true;
            }
            if (Time.time >= nextAt || (__instance.currentTarget == null && Time.time >= lastAt + Urgent))
            {
                ShareOut();
            }
            return false;
        }
    }
}
