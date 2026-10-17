using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using UltimateAscent.Features;
using UltimateAscent.Features.Weather;

namespace UltimateAscent.Core;

/// <summary>Drives the per-frame logic of every rule and receives Photon room callbacks.</summary>
internal sealed class Runtime : MonoBehaviourPunCallbacks
{
    private static readonly Action[] Ticks =
    {
        HostOptions.Publish,
        BoardingPassUi.Tick,
        A11_RuleZero.Tick,
        A19_NoReviveStatues.Tick,
        A20_BingBong.Tick,
        A23_HotSun.Tick,
        BurnedItems.Tick,
        A17_BeeAmbush.Tick,
        A17_Looker.Tick,
        A17_BananaDarkness.Tick,
        A15_FoodPoison.Tick,
        ToxicDrink.Tick,
        A13_ButterFingers.Tick,
        A14_Items.Tick,
        A17_Hazards.Tick,
        ClimbingChalkHands.Tick,
        CalderaTide.Tick,
        KilnAsh.Tick,
        WeatherGrace.Tick,
        A25_AllWeather.Tick,
        ExtraTornadoes.Tick,
        SixSlotBackpack.Tick,
    };

    private readonly bool[] failed = new bool[Ticks.Length];

    private void Update()
    {
        if (!Plugin.Ready)
        {
            return;
        }
        for (int i = 0; i < Ticks.Length; i++)
        {
            if (failed[i])
            {
                continue;
            }
            try
            {
                Ticks[i]();
            }
            catch (Exception ex)
            {
                // One broken rule must not take the others down; log it once and switch it off.
                failed[i] = true;
                Plugin.Log.LogError($"{Ticks[i].Method.DeclaringType?.Name}.{Ticks[i].Method.Name} failed and was switched off for this session: {ex}");
            }
        }
    }

    public override void OnJoinedRoom()
    {
        Net.SetLocalPlayer(BoardingPassUi.VersionKey, Plugin.Version);
        HostOptions.Publish();
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        A11_RuleZero.Reset();
        HostOptions.Publish();
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
    }
}
