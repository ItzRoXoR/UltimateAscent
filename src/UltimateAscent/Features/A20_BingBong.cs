using System.Linq;
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

    internal static void Reset()
    {
        neglectedFor = 0f;
        LinesSpoken = 0;
        Silence();
    }

    internal static void Tick()
    {
        Character local = Character.localCharacter;
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
