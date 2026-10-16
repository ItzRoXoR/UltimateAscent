using System;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 18: the Caldera's lava river rises and falls on 13DDA's tide curve, from the game's lowest
/// level up 14.5 m (the game's own animation, which this replaces, rises 6.3 m). The host publishes
/// a phase anchor (run, phase, network time, running) as a room property and every client
/// computes the same lava height from the shared network clock, so the lava is where everybody
/// sees it, including late joiners.
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

    /// <summary>Middle of the tide on the regular map and how far it swings: 779 to 793.5, the game's lowest
    /// river level and 14.5 m above it (13DDA: 779 to 795).</summary>
    internal const float RegularCenter = 786.25f;
    private const float Amplitude = 7.25f;

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
    }

    /// <summary>Host: create the anchor for a new run and re-anchor whenever the tide pauses or resumes.</summary>
    internal static void Tick()
    {
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
        Compat.Tfa.TryGetTideCenter(RegularCenter, out float center);
        Vector3 position = lava.transform.position;
        position.y = Height(phase, center);
        lava.transform.position = position;
        return true;
    }
}
