using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 20 (More Ascents): Bing Bong calls upon an evil force if not given attention.
/// Somebody has to be holding Bing Bong: holding it keeps the timer at zero for as long as it is
/// held (More Ascents only reset it when Bing Bong was picked up or put away, so holding it too long
/// let the evil through anyway). After 10 seconds without, Bing Bong starts talking, line after line,
/// in the voice he has once burned, for every scout to hear; he falls silent as soon as someone holds
/// him, or when after 15 seconds every scout starts gaining crab status, until Bing Bong is held again.
/// Only the Cursed Skull cures that crab status (see <see cref="A14_Items"/>); Pandora's Lunchbox can
/// even add to it. A burned Bing Bong (cooked three times or more; at ascent 16 one campfire cooking
/// does it) never counts, and while freshly burned it burns the hand that holds it
/// (<see cref="BurnedItems"/>). The timer is paused while the scouts are still waking up on the beach.
/// A custom run's Mini Run that starts past the beach (where the game's Bing Bong lies) hands the host a Bing Bong to
/// hold, as the game hands out Grapple Mode's claw: once they are awake and on the ground for 3 s.
/// </summary>
internal static class A20_BingBong
{
    private const float WarningAfter = 10f;
    private const float PunishAfter = 15f;
    private const float CrabPerSecond = 0.05f;

    /// <summary>The pause between two of his lines.</summary>
    private const float LineGap = 0.4f;

    internal static float neglectedFor;

    /// <summary>His lines, and the pitch and volume his voice has once he is burned.</summary>
    private static AudioClip[] lines;
    private static float burnedPitch = 1f;
    private static float burnedVolume = 1f;
    private static bool voiceRead;

    private static AudioSource voice;
    private static float nextLine;

    /// <summary>He is talking right now (for the in-game test).</summary>
    internal static bool Speaking => voice != null && voice.isPlaying;

    /// <summary>The lines he has started in this run, and his burned voice (for the in-game test).</summary>
    internal static int LinesSpoken { get; private set; }

    internal static float VoicePitch => burnedPitch;

    internal static float VoiceVolume => burnedVolume;

    /// <summary>The game's Bing Bong (the item, by its prefab's name).</summary>
    private const string BingBongPrefab = "BingBong";

    /// <summary>How long the host is on the ground, awake, before Bing Bong is in their hands (the game's own wait for a challenge item).</summary>
    private const float HandOverAfter = 3f;

    /// <summary>A Mini Run past the beach has just started: the host is owed a Bing Bong.</summary>
    private static bool owed;
    private static float groundedSince = -1f;

    /// <summary>For the in-game test: Bing Bong has been handed to the host in this climb.</summary>
    internal static bool HandedOver { get; private set; }

    internal static void Reset()
    {
        neglectedFor = 0f;
        LinesSpoken = 0;
        owed = false;
        groundedSince = -1f;
        HandedOver = false;
        Silence();
    }

    /// <summary>
    /// The host starts a fresh Mini Run (a campfire save's run is not started again, so it owes nothing: its Bing Bong is
    /// where it was saved) past the beach.
    /// </summary>
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.JumpToMiniRunBiomeWhenReady))]
    private static class MiniRunStarts
    {
        private static void Prefix()
        {
            owed = RunSettings.GetValue(RunSettings.SETTINGTYPE.MiniRunBiome) > 0;
            groundedSince = -1f;
        }
    }

    /// <summary>Host: once in the Mini Run's part, awake and on the ground for 3 s, Bing Bong into their hands.</summary>
    private static void HandOver(Character local)
    {
        if (!owed || local == null || local.data == null)
        {
            return;
        }
        if (!Rules.Enabled(Lvl.BingBong) || !RunSettings.isMiniRun || !PhotonNetwork.IsMasterClient)
        {
            owed = false;
            return;
        }
        MapHandler map = Singleton<MapHandler>.Instance;
        if (map == null || map.GetCurrentSegment() != MiniRunStart() || local.data.dead || local.data.passedOutOnTheBeach > 0f || local.warping || !local.data.isGrounded)
        {
            groundedSince = -1f;
            return;
        }
        if (groundedSince < 0f)
        {
            groundedSince = Time.time;
        }
        if (Time.time - groundedSince < HandOverAfter)
        {
            return;
        }
        owed = false;
        if (!ItemDatabase.TryGetItem(BingBongPrefab, out Item prefab) || prefab == null)
        {
            Plugin.Log.LogWarning("Ascent 20: the game has no Bing Bong to hand to the host in this Mini Run.");
            return;
        }
        local.refs.items.SpawnItemInHand(prefab.gameObject.name);
        HandedOver = true;
        Plugin.Log.LogInfo("Ascent 20: a Mini Run past the beach; Bing Bong is in the host's hands.");
    }

    /// <summary>The part a Mini Run starts in, as the game jumps there.</summary>
    private static Segment MiniRunStart() => RunSettings.GetValue(RunSettings.SETTINGTYPE.MiniRunBiome) switch
    {
        1 => Segment.Tropics,
        2 => Segment.Alpine,
        3 => Segment.Caldera,
        _ => Segment.Beach,
    };

    internal static void Tick()
    {
        Character local = Character.localCharacter;
        HandOver(local);
        if (!Rules.Enabled(Lvl.BingBong) || local == null || local.data == null || local.data.dead)
        {
            neglectedFor = 0f;
            Silence();
            return;
        }
        if (local.data.passedOutOnTheBeach > 0f || SomeoneHoldsBingBong())
        {
            neglectedFor = 0f;
            Silence();
            return;
        }
        neglectedFor += Time.deltaTime;
        if (neglectedFor > WarningAfter && neglectedFor < PunishAfter)
        {
            Speak();
        }
        else
        {
            Silence();
        }
        if (neglectedFor >= PunishAfter)
        {
            local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Crab, CrabPerSecond * Time.deltaTime);
        }
    }

    /// <summary>One line after another, with a short pause between them.</summary>
    private static void Speak()
    {
        if (!ReadVoice())
        {
            return;
        }
        if (voice == null)
        {
            GameObject holder = new GameObject("UltimateAscent Bing Bong Voice");
            Object.DontDestroyOnLoad(holder);
            voice = holder.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            voice.outputAudioMixerGroup = voiceGroup;
        }
        if (voice.isPlaying || Time.unscaledTime < nextLine)
        {
            return;
        }
        AudioClip line = lines[Random.Range(0, lines.Length)];
        voice.pitch = burnedPitch;
        voice.volume = burnedVolume;
        voice.clip = line;
        voice.Play();
        LinesSpoken++;
        nextLine = Time.unscaledTime + line.length / Mathf.Max(0.1f, Mathf.Abs(burnedPitch)) + LineGap;
    }

    private static void Silence()
    {
        if (voice != null && voice.isPlaying)
        {
            voice.Stop();
        }
        nextLine = 0f;
    }

    private static UnityEngine.Audio.AudioMixerGroup voiceGroup;

    /// <summary>
    /// His lines and burned voice, from the Bing Bong item: his answers when asked, played on his own
    /// voice, whose pitch and volume his cooking behaviours turn down with every cooking, here as far as
    /// a burned Bing Bong (three cookings) has it.
    /// </summary>
    private static bool ReadVoice()
    {
        if (voiceRead)
        {
            return lines != null && lines.Length > 0;
        }
        voiceRead = true;
        Item prefab = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values
            .Where(i => i != null && (i.itemTags & Item.ItemTags.BingBong) != 0 && i.GetComponent<Action_AskBingBong>() != null)
            .OrderBy(i => i.gameObject.name.Length)
            .FirstOrDefault();
        Action_AskBingBong ask = prefab != null ? prefab.GetComponent<Action_AskBingBong>() : null;
        if (ask == null || ask.responses == null || ask.source == null)
        {
            Plugin.Log.LogWarning("Ascent 20: Bing Bong's voice was not found; his warning stays silent.");
            return false;
        }
        lines = ask.responses.Where(r => r != null && r.sfx != null && r.sfx.clips != null && r.sfx.clips.Length > 0 && r.sfx.clips[0] != null)
            .Select(r => r.sfx.clips[0]).ToArray();
        voiceGroup = ask.source.outputAudioMixerGroup;
        burnedPitch = ask.source.pitch;
        burnedVolume = ask.source.volume;
        ItemCooking cooking = prefab.GetComponent<ItemCooking>();
        AdditionalCookingBehavior[] behaviours = cooking != null ? cooking.additionalCookingBehaviors : new AdditionalCookingBehavior[0];
        bool[] triggered = new bool[behaviours.Length];
        for (int cooked = 1; cooked <= BurnedItems.BurntCookLevel; cooked++)
        {
            for (int i = 0; i < behaviours.Length; i++)
            {
                AdditionalCookingBehavior behaviour = behaviours[i];
                if (behaviour == null || cooked < behaviour.cookedAmountToTrigger || (behaviour.onlyOnce && triggered[i]))
                {
                    continue;
                }
                triggered[i] = true;
                if (behaviour is CookingBehavior_MessUpAudio mess && mess.source == ask.source && cooked <= mess.max)
                {
                    burnedPitch -= mess.pitchReductionPerCooking;
                    burnedVolume -= mess.volumeReductionPerCooking;
                }
                else if (behaviour is CookingBehavior_ModifyAudioSourcePitch shift && shift.source == ask.source)
                {
                    burnedPitch += shift.changePerCooking * cooked;
                }
            }
        }
        burnedVolume = Mathf.Clamp01(burnedVolume);
        Plugin.Log.LogInfo($"Ascent 20: Bing Bong's burned voice: {lines.Length} lines, pitch {burnedPitch:0.###} (normal {ask.source.pitch:0.###}), volume {burnedVolume:0.###} (normal {ask.source.volume:0.###}).");
        return lines.Length > 0;
    }

    private static bool SomeoneHoldsBingBong()
    {
        foreach (Character character in Character.AllCharacters)
        {
            if (character == null || character.isBot || character.data == null || character.data.dead)
            {
                continue;
            }
            Item item = character.data.currentItem;
            if (item != null && (item.itemTags & Item.ItemTags.BingBong) != 0 && !BurnedItems.IsBurned(item))
            {
                return true;
            }
        }
        return false;
    }
}
