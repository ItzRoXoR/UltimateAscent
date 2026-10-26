using BepInEx.Configuration;
using Photon.Pun;
using UnityEngine;

namespace UltimateAscent.Core;

/// <summary>
/// Options that must be identical for everyone: the switches of ascents 8-25 and of the cold nights
/// (config section "Ascents"), the new items (section "Items") and the new mobs (section "Mobs"). The host's config wins and is
/// published as a room property; clients read it.
/// </summary>
internal static class HostOptions
{
    private const string RoomKey = Net.Prefix + "asc";

    /// <summary>
    /// Set when the cold nights are switched off. It follows the ascents' bits and means "off" when
    /// set, so a mask without it (an older host's) keeps the game's cold nights.
    /// </summary>
    private const int ColdNightsOff = 1 << (Lvl.Last - Lvl.First + 1);

    /// <summary>Set when the mod's new items are switched off for regular runs (missing: they spawn).</summary>
    private const int NewItemsOff = ColdNightsOff << 1;

    /// <summary>Set when the game's ascent 8 (win only in the Nadir) is switched off (missing: it applies).</summary>
    private const int Ascent8Off = NewItemsOff << 1;

    /// <summary>Set when the mod's new creatures are switched off (missing: they live).</summary>
    private const int NewMobsOff = Ascent8Off << 1;

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

    /// <summary>The night itself makes you cold, as in the game, unless the host switched it off.</summary>
    internal static bool ColdNightsOn => (Mask & ColdNightsOff) == 0;

    /// <summary>The mod's new items spawn in regular runs, unless the host switched them off.</summary>
    internal static bool NewItemsOn => (Mask & NewItemsOff) == 0;

    /// <summary>The game's ascent 8 applies (a climb from ascent 8 is won only in the Nadir), unless the host switched it off.</summary>
    internal static bool Ascent8On => (Mask & Ascent8Off) == 0;

    /// <summary>The mod's new creatures live in regular runs, unless the host switched them off.</summary>
    internal static bool NewMobsOn => (Mask & NewMobsOff) == 0;

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
            if (Plugin.ColdNights != null && !Plugin.ColdNights.Value)
            {
                mask |= ColdNightsOff;
            }
            if (Plugin.NewItems != null && !Plugin.NewItems.Value)
            {
                mask |= NewItemsOff;
            }
            if (Plugin.Ascent8 != null && !Plugin.Ascent8.Value)
            {
                mask |= Ascent8Off;
            }
            if (Plugin.NewMobs != null && !Plugin.NewMobs.Value)
            {
                mask |= NewMobsOff;
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
