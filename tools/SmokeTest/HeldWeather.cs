using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Features;
using UltimateAscent.Features.Weather;
using UnityEngine;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// The weather held calm for the checks that are not about it: storms (their cold, heat and wind), the game's tornadoes
/// and ascent 25's extra ones push the test scout off ledges and cliffs and kill it. While <see cref="On"/>, every storm
/// stays calm and no tornado comes; the tornadoes about are taken away when it begins.
/// </summary>
internal static class HeldWeather
{
    internal static bool On { get; private set; }

    internal static void Hold(bool on)
    {
        On = on;
        if (!on || !PhotonNetwork.IsMasterClient)
        {
            return;
        }
        foreach (Tornado tornado in Object.FindObjectsByType<Tornado>(FindObjectsSortMode.None))
        {
            PhotonView view = tornado != null ? tornado.GetComponent<PhotonView>() : null;
            if (view != null && view.ViewID > 0)
            {
                PhotonNetwork.Destroy(tornado.gameObject);
            }
        }
    }

    internal static readonly System.Type[] Patches = { typeof(ZoneClock), typeof(ZoneUpdate), typeof(ZonePush), typeof(ZoneStatus), typeof(ZoneShove), typeof(TornadoSpawns), typeof(ExtraTornadoSpawns) };

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.HandleTime))]
    private static class ZoneClock
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WindChillZone __instance)
        {
            if (!On)
            {
                return true;
            }
            WeatherGrace.Calm(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.Update))]
    private static class ZoneUpdate
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WindChillZone __instance)
        {
            if (!On)
            {
                return true;
            }
            WeatherGrace.Calm(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.FixedUpdate))]
    private static class ZonePush
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !On;
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.ApplyStatus))]
    private static class ZoneStatus
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !On;
    }

    [HarmonyPatch(typeof(WindChillZone), nameof(WindChillZone.AddWindForceToCharacter))]
    private static class ZoneShove
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !On;
    }

    [HarmonyPatch(typeof(TornadoSpawner), nameof(TornadoSpawner.Update))]
    private static class TornadoSpawns
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !On;
    }

    [HarmonyPatch(typeof(ExtraTornadoes), nameof(ExtraTornadoes.Tick))]
    private static class ExtraTornadoSpawns
    {
        private static bool Prefix() => !On;
    }
}
