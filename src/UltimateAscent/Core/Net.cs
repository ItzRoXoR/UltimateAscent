using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

namespace UltimateAscent.Core;

/// <summary>Small helpers around Photon room properties and the shared network clock.</summary>
internal static class Net
{
    internal const string Prefix = "UA.";

    /// <summary>Seconds on a clock shared by every player in the room.</summary>
    internal static double Clock => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;

    /// <summary>Elapsed seconds between two <see cref="Clock"/> readings, tolerating the Photon clock wrap.</summary>
    internal static double Elapsed(double now, double then)
    {
        double elapsed = now - then;
        if (elapsed < -2147483.648)
        {
            elapsed += 4294967.296;
        }
        return Math.Max(0.0, elapsed);
    }

    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>Identifies the current climb; changes on every new run and survives campfire saves.</summary>
    internal static string RunKey
    {
        get
        {
            if (RunManager.Instance == null)
            {
                return null;
            }
            Guid id = RunManager.Instance.RunId;
            return id == Guid.Empty ? null : id.ToString("N");
        }
    }

    internal static object GetRoom(string key)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            return null;
        }
        return PhotonNetwork.CurrentRoom.CustomProperties[key];
    }

    /// <summary>Writes a room property. Only the host writes; everybody reads.</summary>
    internal static void SetRoom(string key, object value)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null || !PhotonNetwork.IsMasterClient)
        {
            return;
        }
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [key] = value });
    }

    internal static void SetLocalPlayer(string key, object value)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
        {
            return;
        }
        if (!Equals(PhotonNetwork.LocalPlayer.CustomProperties[key], value))
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { [key] = value });
        }
    }
}
