using BepInEx.Configuration;
using Photon.Pun;
using UnityEngine;

namespace UltimateAscent.Core;

/// <summary>
/// Options that must be identical for everyone: the switches of ascents 9-25 (config section
/// "Ascents"). The host's config wins and is published as a room property; clients read it.
/// </summary>
internal static class HostOptions
{
    private const string RoomKey = Net.Prefix + "asc";

    private static int cachedMask;
    private static int cachedFrame = -1;

    /// <summary>The rule of this ascent is switched on (always for levels outside 9-25).</summary>
    internal static bool AscentOn(int level)
    {
        if (level < Lvl.First || level > Lvl.Last)
        {
            return true;
        }
        return (Mask & Bit(level)) != 0;
    }

    /// <summary>A switch changed: read them again.</summary>
    internal static void Invalidate()
    {
        cachedFrame = -1;
    }

    private static int Bit(int level) => 1 << (level - Lvl.First);

    /// <summary>Read once a frame: the rules ask many times a frame.</summary>
    private static int Mask
    {
        get
        {
            int frame = Time.frameCount;
            if (frame != cachedFrame)
            {
                int mask = Local;
                if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient && Net.GetRoom(RoomKey) is int published)
                {
                    mask = published;
                }
                cachedMask = mask;
                cachedFrame = frame;
            }
            return cachedMask;
        }
    }

    private static int Local
    {
        get
        {
            int mask = 0;
            for (int level = Lvl.First; level <= Lvl.Last; level++)
            {
                ConfigEntry<bool> entry = Plugin.AscentSwitches?[level];
                if (entry == null || entry.Value)
                {
                    mask |= Bit(level);
                }
            }
            return mask;
        }
    }

    /// <summary>Host: publish the current switches (cheap when unchanged).</summary>
    internal static void Publish()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && !(Net.GetRoom(RoomKey) is int current && current == Local))
        {
            Net.SetRoom(RoomKey, Local);
            Invalidate();
        }
    }
}
