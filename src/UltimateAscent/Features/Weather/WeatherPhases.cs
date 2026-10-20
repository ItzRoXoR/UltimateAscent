using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features.Weather;

/// <summary>
/// Ascent 25: the storm/calm cycle of each weather zone is decided by the host and shared as a
/// room property (run, start time, duration, active, direction). Everybody derives the current
/// state from the shared clock, so late joiners and players who missed a message see the same
/// weather, and rebuilt storms (which have no network view of their own) work in multiplayer.
/// In the Caldera and the Gloom the wind and the snowstorm never blow together: whichever began later stays
/// calm until the other is over (each game goes by where its own scout is).
/// </summary>
internal static class WeatherPhases
{
    private sealed class Phase
    {
        internal string run;
        internal double clock;
        internal float duration;
        internal bool active;
        internal int direction;

        internal double Elapsed(double now) => Net.Elapsed(now, clock);

        internal double Remaining(double now) => System.Math.Max(0.0, duration - Elapsed(now));
    }

    private sealed class Channel
    {
        internal readonly string key;
        internal Phase state;
        internal object received;

        internal Channel(string key)
        {
            this.key = key;
        }

        internal void Reset()
        {
            state = null;
            received = null;
        }

        internal void Receive()
        {
            object value = Net.GetRoom(key);
            if (ReferenceEquals(value, received))
            {
                return;
            }
            received = value;
            if (value is object[] d && d.Length == 5 && d[0] is string run && d[1] is double clock && d[2] is float duration && d[3] is bool active && d[4] is int direction &&
                Net.Finite(clock) && duration > 0f && (direction == 1 || direction == -1))
            {
                state = new Phase { run = run, clock = clock, duration = duration, active = active, direction = direction };
            }
        }

        internal void Publish()
        {
            object[] value = { state.run, state.clock, state.duration, state.active, state.direction };
            received = value;
            Net.SetRoom(key, value);
        }
    }

    private static readonly Channel wind = new Channel(Net.Prefix + "wx.wind");
    private static readonly Channel snow = new Channel(Net.Prefix + "wx.snow");
    private static readonly Channel rain = new Channel(Net.Prefix + "wx.rain");

    private static Channel For(WindChillZone zone)
    {
        switch (zone.name)
        {
            case "WindStorm":
                return wind;
            case "SnowStorm":
                return snow;
            case "RainStorm":
                return rain;
            default:
                return null;
        }
    }

    /// <summary>Zones whose timing this mod drives at ascent 25.</summary>
    internal static bool Handles(WindChillZone zone)
    {
        return zone != null && A25_AllWeather.DrivesVanillaWeather && For(zone) != null;
    }

    internal static void Reset()
    {
        wind.Reset();
        snow.Reset();
        rain.Reset();
    }

    internal static void Apply(WindChillZone zone)
    {
        Channel channel = For(zone);
        if (channel == null)
        {
            return;
        }
        if (!Rules.IsHost)
        {
            channel.Receive();
        }
        string run = Net.RunKey;
        double now = Net.Clock;
        Phase state = channel.state;
        if (Rules.IsHost && run != null && (state == null || state.run != run || state.Remaining(now) <= 0.0))
        {
            bool first = state == null || state.run != run;
            bool active = !first && !state.active;
            float duration = zone.GetNextWindTime(active);
            if (first)
            {
                // Start every storm with a calm of random length, so rain, wind and snow arrive one by one.
                duration *= Random.Range(0.1f, 1f);
            }
            if (!Net.Finite(duration) || duration <= 0f)
            {
                duration = 0.01f;
            }
            state = channel.state = new Phase { run = run, clock = now, duration = duration, active = active, direction = Random.value > 0.5f ? 1 : -1 };
            channel.Publish();
            Plugin.Debug($"Ascent 25 weather: {zone.name} {(active ? "storm" : "calm")} for {duration:F0} s.");
        }
        Phase shown = state != null && state.run == run ? state : null;
        if (shown != null && WaitsForTheOther(channel, shown, run, now))
        {
            shown = null;
        }
        ApplyState(zone, shown, now);
    }

    /// <summary>In the Caldera and the Gloom, a wind or snowstorm that began while the other was blowing waits for it to end.</summary>
    private static bool WaitsForTheOther(Channel channel, Phase phase, string run, double now)
    {
        if (!phase.active || (channel != wind && channel != snow) || !(Map.CurrentPlace == Place.Caldera || Map.CurrentPlace == Place.Gloom))
        {
            return false;
        }
        Channel other = channel == wind ? snow : wind;
        if (!Rules.IsHost)
        {
            other.Receive();
        }
        Phase blowing = other.state;
        if (blowing == null || blowing.run != run || !blowing.active || blowing.Remaining(now) <= 0.0)
        {
            return false;
        }
        double mine = phase.Elapsed(now);
        double theirs = blowing.Elapsed(now);
        return theirs > mine || (theirs == mine && other == snow);
    }

    private static void ApplyState(WindChillZone zone, Phase state, double now)
    {
        if (state == null)
        {
            zone.windActive = false;
            zone.untilSwitch = zone.timeUntilNextWind = 1f;
            zone.hasBeenActiveFor = 0f;
            zone.currentForceMult = 0f;
            zone.windIntensity = zone.windPlayerFactor = 0f;
            return;
        }
        zone.windActive = state.active && state.Remaining(now) > 0.0;
        zone.untilSwitch = (float)state.Remaining(now);
        zone.timeUntilNextWind = state.duration;
        zone.currentWindDirection = Vector3.Lerp(Vector3.right * state.direction, Vector3.forward, 0.2f).normalized;
        zone.hasBeenActiveFor = zone.windActive ? Mathf.Max(0f, (float)state.Elapsed(now) - Time.deltaTime) : 0f;
        zone.currentForceMult = Mathf.Clamp01(zone.windActive ? (float)state.Elapsed(now) : 1f - (float)state.Elapsed(now));
        zone.StormProgress = zone.windActive ? Mathf.Clamp01(1f - zone.untilSwitch / state.duration) : 0f;
        zone.timeUntilStorm = zone.windActive ? 0f : Mathf.Clamp01(1f - zone.untilSwitch / state.duration);
        zone.windIntensity = zone.windActive && zone.windIntensityCurve != null ? zone.windIntensityCurve.Evaluate(zone.StormProgress) : 0f;
    }
}
