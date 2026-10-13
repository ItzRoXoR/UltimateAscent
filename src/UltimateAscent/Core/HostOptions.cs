using Photon.Pun;

namespace UltimateAscent.Core;

/// <summary>
/// Gameplay options that must be identical for everyone. The host's config wins and is
/// published as a room property; clients read it.
/// </summary>
internal static class HostOptions
{
    private const string RoomKey = Net.Prefix + "opts";

    private const int NerfedRevivesBit = 1;
    private const int CursePunishmentBit = 2;
    private const int DoubleApocalypseBit = 4;

    internal static bool NerfedRevives => Has(NerfedRevivesBit);

    internal static bool CursePunishment => Has(CursePunishmentBit);

    internal static bool DoubleApocalypse => Has(DoubleApocalypseBit);

    private static int Local
    {
        get
        {
            int mask = 0;
            if (Plugin.NerfedRevives.Value)
            {
                mask |= NerfedRevivesBit;
            }
            if (Plugin.CursePunishment.Value)
            {
                mask |= CursePunishmentBit;
            }
            if (Plugin.TfaDoubleApocalypse.Value)
            {
                mask |= DoubleApocalypseBit;
            }
            return mask;
        }
    }

    private static bool Has(int bit)
    {
        int mask = Local;
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient && Net.GetRoom(RoomKey) is int published)
        {
            mask = published;
        }
        return (mask & bit) != 0;
    }

    /// <summary>Host: publish the current options (cheap when unchanged).</summary>
    internal static void Publish()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && !(Net.GetRoom(RoomKey) is int current && current == Local))
        {
            Net.SetRoom(RoomKey, Local);
        }
    }
}
