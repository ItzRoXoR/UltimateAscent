using System;
using System.Collections.Generic;
using System.Linq;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 18: the Caldera's lava river rises and falls on 13DDA's tide curve, from the game's lowest
/// level up 14 m (the game's own animation, which this replaces, rises 6.3 m). The host publishes
/// a phase anchor (run, phase, network time, running) as a room property and every client
/// computes the same lava height from the shared network clock, so the lava is where everybody
/// sees it, including late joiners. Each river rises from its own lowest level, so a Caldera True
/// Final Ascent moved (FINAL ASCENT, every Caldera of its Builder) keeps its tide in its basin; a
/// river whose heat TFA switched off (it lays lava tiles that follow the river instead) still rises
/// and falls. TFA's rest island in a Caldera it made larger stands clear of the river's heat, as TFA
/// built it for the game's lower tide.
/// </summary>
internal static class CalderaTide
{
    private const string RoomKey = Net.Prefix + "tide";

    private const double MinPhase = -0.35;
    private static readonly double MaxPhase = Math.Sqrt(Math.PI * 2.0);
    private const double SecondsPerPhase = 17.8;
    private const double SlowStart = 1.75;
    private const double SlowEnd = 1.85;
    private const double SlowSpeed = 0.15;

    /// <summary>Middle of the tide on the regular map and how far it swings: 779 to 793, the game's lowest
    /// river level and 14 m above it (13DDA: 779 to 795).</summary>
    internal const float RegularCenter = 786f;
    private const float Amplitude = 7f;

    /// <summary>The river's lowest level in its own part of the map (where the game's animation starts it).</summary>
    private const float LowLevel = RegularCenter - Amplitude;

    /// <summary>True Final Ascent's rest island in a Caldera it made larger.</summary>
    private const string RestIsland = "TFA_CalderaRest";

    /// <summary>Rivers whose own updates are off (True Final Ascent switched their heat off), driven from here instead.</summary>
    private static readonly List<Lava> sleeping = new List<Lava>();
    private static float sleepingLooked = float.NegativeInfinity;

    private sealed class Anchor
    {
        internal string run;
        internal double phase;
        internal double clock;
        internal bool running;

        internal double At(double now) => running ? Advance(phase, Net.Elapsed(now, clock)) : phase;
    }

    private static Anchor anchor;
    private static object received;

    private static double Period => PhaseTime(MaxPhase);

    private static double PhaseTime(double phase)
    {
        return SecondsPerPhase * (phase - MinPhase + Math.Max(0.0, Math.Min(phase, SlowEnd) - SlowStart) * (1.0 / SlowSpeed - 1.0));
    }

    private static bool ValidPhase(double phase) => Net.Finite(phase) && phase >= MinPhase && phase <= MaxPhase;

    private static double Advance(double phase, double elapsed)
    {
        double t = (PhaseTime(phase) + elapsed) % Period;
        double slowStartTime = PhaseTime(SlowStart);
        double slowEndTime = PhaseTime(SlowEnd);
        if (t < slowStartTime)
        {
            return MinPhase + t / SecondsPerPhase;
        }
        if (t < slowEndTime)
        {
            return SlowStart + (t - slowStartTime) * SlowSpeed / SecondsPerPhase;
        }
        return SlowEnd + (t - slowEndTime) / SecondsPerPhase;
    }

    private static float Height(double phase, float center) => center + (float)(-Amplitude * Math.Cos(phase * phase));

    internal static void Reset()
    {
        anchor = null;
        received = null;
        sleeping.Clear();
        sleepingLooked = float.NegativeInfinity;
        islands.Clear();
    }

    /// <summary>Every game, every frame: a river in the scouts' Caldera whose own update is off still follows the tide.</summary>
    internal static void Follow()
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather) || !Map.InVolcano)
        {
            return;
        }
        if (Time.time - sleepingLooked >= 2f)
        {
            sleepingLooked = Time.time;
            sleeping.Clear();
            sleeping.AddRange(UnityEngine.Object.FindObjectsByType<Lava>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(l => l.name == "River" && !l.enabled));
        }
        foreach (Lava river in sleeping)
        {
            if (river != null && !river.enabled && river.gameObject.activeInHierarchy)
            {
                Apply(river);
            }
        }
    }

    /// <summary>Host: create the anchor for a new run and re-anchor whenever the tide pauses or resumes.</summary>
    internal static void Tick()
    {
        Follow();
        if (!Rules.Enabled(Lvl.ExtremeWeather) || !Rules.IsHost)
        {
            return;
        }
        string run = Net.RunKey;
        if (run == null)
        {
            return;
        }
        bool running = !LoadingScreenHandler.loading && Time.timeScale > 0f && Map.InVolcano;
        double now = Net.Clock;
        if (anchor == null || anchor.run != run)
        {
            anchor = new Anchor { run = run, phase = 0.0, clock = now, running = running };
            Publish();
        }
        else if (anchor.running != running)
        {
            anchor = new Anchor { run = run, phase = anchor.At(now), clock = now, running = running };
            Publish();
        }
    }

    private static void Publish()
    {
        object[] value = { anchor.run, anchor.phase, anchor.clock, anchor.running };
        received = value;
        Net.SetRoom(RoomKey, value);
    }

    private static void Receive()
    {
        object value = Net.GetRoom(RoomKey);
        if (ReferenceEquals(value, received))
        {
            return;
        }
        received = value;
        if (value is object[] data && data.Length == 4 && data[0] is string run && data[1] is double phase && data[2] is double clock && data[3] is bool running &&
            ValidPhase(phase) && Net.Finite(clock))
        {
            anchor = new Anchor { run = run, phase = phase, clock = clock, running = running };
        }
    }

    private static bool TryPhase(out double phase)
    {
        phase = 0.0;
        if (!Rules.IsHost)
        {
            Receive();
        }
        string run = Net.RunKey;
        if (anchor == null || run == null || anchor.run != run)
        {
            return false;
        }
        phase = anchor.At(Net.Clock);
        return true;
    }

    /// <summary>Returns false to skip the lava's own update until the shared tide is known.</summary>
    internal static bool Apply(Lava lava)
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather) || lava.name != "River" || !Map.InVolcano)
        {
            return true;
        }
        Animator animator = lava.GetComponent<Animator>();
        if (animator == null)
        {
            return true;
        }
        if (animator.enabled)
        {
            Overrides.Set(animator, nameof(Animator.enabled), false);
        }
        if (!TryPhase(out double phase))
        {
            return false;
        }
        Vector3 position = lava.transform.position;
        position.y = Height(phase, Low(lava.transform) + Amplitude);
        lava.transform.position = position;
        return true;
    }

    /// <summary>A river's lowest level: where the game's animation starts it in its own part of the map (as True Final Ascent reads it).</summary>
    private static float Low(Transform river)
    {
        Transform parent = river.parent;
        Vector3 local = river.localPosition;
        return parent != null ? parent.TransformPoint(new Vector3(local.x, LowLevel, local.z)).y : LowLevel;
    }

    private static readonly Dictionary<Lava, Collider[]> islands = new Dictionary<Lava, Collider[]>();

    /// <summary>
    /// The local scout is on True Final Ascent's rest island in this river's Caldera (on it or just above it): the river's
    /// heat does not reach them there.
    /// </summary>
    internal static bool OnRestIsland(Lava lava)
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather) || lava == null || !Compat.Tfa.BuilderActive || Character.localCharacter == null)
        {
            return false;
        }
        if (!islands.TryGetValue(lava, out Collider[] island))
        {
            Transform rest = lava.transform.parent != null ? lava.transform.parent.Find(RestIsland) : null;
            island = rest != null ? rest.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
            islands[lava] = island;
        }
        if (island.Length == 0)
        {
            return false;
        }
        Vector3 at = Character.localCharacter.Center;
        foreach (Collider part in island)
        {
            if (part == null)
            {
                continue;
            }
            Bounds bounds = part.bounds;
            if (at.x >= bounds.min.x && at.x <= bounds.max.x && at.z >= bounds.min.z && at.z <= bounds.max.z && at.y >= bounds.max.y - 2f)
            {
                return true;
            }
        }
        return false;
    }
}
