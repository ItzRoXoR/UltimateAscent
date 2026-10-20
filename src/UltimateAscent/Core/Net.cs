using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace UltimateAscent.Core;

/// <summary>Small helpers around Photon room properties and the shared network clock.</summary>
internal static class Net
{
    internal const string Prefix = "UA.";

    /// <summary>The Photon event this mod's messages travel in (the game uses 18, True Final Ascent 180-184).</summary>
    internal const byte EventCode = 171;

    private static readonly Dictionary<string, Action<object[]>> handlers = new Dictionary<string, Action<object[]>>();

    /// <summary>What to do when a message of this kind arrives (on the game it was sent to).</summary>
    internal static void Handle(string kind, Action<object[]> handler)
    {
        handlers[kind] = handler;
    }

    /// <summary>A message to one player's game; for this player's own game it is handled at once.</summary>
    internal static void SendTo(Photon.Realtime.Player player, string kind, params object[] args)
    {
        if (player == null || !PhotonNetwork.InRoom || player.IsLocal)
        {
            Receive(kind, args);
            return;
        }
        object[] content = new object[args.Length + 1];
        content[0] = kind;
        Array.Copy(args, 0, content, 1, args.Length);
        PhotonNetwork.RaiseEvent(EventCode, content, new RaiseEventOptions { TargetActors = new[] { player.ActorNumber } }, SendOptions.SendReliable);
    }

    /// <summary>A message to every player's game, this one's included (handled here at once).</summary>
    internal static void SendAll(string kind, params object[] args)
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            object[] content = new object[args.Length + 1];
            content[0] = kind;
            Array.Copy(args, 0, content, 1, args.Length);
            PhotonNetwork.RaiseEvent(EventCode, content, new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
        }
        Receive(kind, args);
    }

    /// <summary>A Photon event (from <see cref="Runtime"/>): this mod's messages go to their handlers.</summary>
    internal static void Receive(EventData data)
    {
        if (data.Code != EventCode || !(data.CustomData is object[] content) || content.Length == 0 || !(content[0] is string kind))
        {
            return;
        }
        object[] args = new object[content.Length - 1];
        Array.Copy(content, 1, args, 0, args.Length);
        Receive(kind, args);
    }

    private static void Receive(string kind, object[] args)
    {
        if (!handlers.TryGetValue(kind, out Action<object[]> handler))
        {
            return;
        }
        try
        {
            handler(args);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"The message '{kind}' could not be handled: {ex}");
        }
    }

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
