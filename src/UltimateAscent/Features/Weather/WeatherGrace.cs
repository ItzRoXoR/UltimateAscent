using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features.Weather;

/// <summary>
/// Ascent 25: storms stay calm for the first 30 seconds after the scouts wake up (new run or a
/// campfire save), so nobody freezes while still lying on the beach. The host starts the shared
/// timer; tornadoes are not affected.
/// </summary>
internal static class WeatherGrace
{
    private const string RoomKey = Net.Prefix + "grace";
    private const double Seconds = 30.0;

    private static string run;
    private static double start;
    private static bool known;

    internal static void Reset()
    {
        run = null;
        known = false;
    }

    internal static bool Blocking
    {
        get
        {
            if (!Rules.Enabled(Lvl.AllWeather))
            {
                return false;
            }
            string current = Net.RunKey;
            if (!Rules.IsHost)
            {
                Receive();
            }
            if (!known || current == null || run != current)
            {
                return true;
            }
            return Net.Elapsed(Net.Clock, start) < Seconds;
        }
    }

    internal static void Tick()
    {
        if (!Rules.Enabled(Lvl.AllWeather) || !Rules.IsHost)
        {
            return;
        }
        string current = Net.RunKey;
        if (current == null || (known && run == current) || !ScoutsAreAwake())
        {
            return;
        }
        run = current;
        start = Net.Clock;
        known = true;
        Net.SetRoom(RoomKey, new object[] { run, start });
        Plugin.Log.LogInfo("Ascent 25: the 30-second weather grace has started.");
    }

    private static void Receive()
    {
        if (Net.GetRoom(RoomKey) is object[] d && d.Length == 2 && d[0] is string r && d[1] is double s && Net.Finite(s))
        {
            run = r;
            start = s;
            known = true;
        }
    }

    private static bool ScoutsAreAwake()
    {
        if (LoadingScreenHandler.loading || RunManager.Instance == null || !RunManager.Instance.runStarted || !MapHandler.ExistsAndInitialized)
        {
            return false;
        }
        Character local = Character.localCharacter;
        if (local != null && local.data != null && !local.inAirport && !local.warping && local.data.passedOutOnTheBeach <= 0f)
        {
            return true;
        }
        // Never hold the weather back forever (e.g. the host is dead or spectating).
        return RunManager.Instance.TimeSinceRunStarted > 90f;
    }

    /// <summary>Silences a zone during the grace period. Returns true when the zone must stay calm.</summary>
    internal static bool Suppress(WindChillZone zone)
    {
        if (zone == null || !Blocking)
        {
            return false;
        }
        Calm(zone);
        return true;
    }

    internal static void Calm(WindChillZone zone)
    {
        zone.windActive = false;
        zone.localCharacterInsideBounds = zone.observedCharacterInsideBounds = false;
        zone.windPlayerFactor = zone.windIntensity = zone.hasBeenActiveFor = zone.currentForceMult = 0f;
        zone.StormProgress = zone.timeUntilStorm = 0f;
        zone.untilSwitch = 0f;
        zone.timeUntilNextWind = 1f;
        if (Time.frameCount % 600 == 0)
        {
            Plugin.Debug("Ascent 25: keeping " + zone.name + " calm.");
        }
    }
}
