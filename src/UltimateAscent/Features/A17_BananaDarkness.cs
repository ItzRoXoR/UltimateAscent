using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: 3 seconds after slipping on a banana peel, your screen darkens the way the Scoutmaster does
/// when he comes for you, for 20 seconds, with no Scoutmaster anywhere, and everything you hear is muffled
/// meanwhile, the way True Final Ascent's Ice Fog muffles it: sound effects dull (low-passed down to 900 Hz)
/// and 8 dB quieter, the music gone, your teammates' voices duller (down to 3,800 Hz) and half as loud. It
/// drives the Scoutmaster's own screen effect; only the scout who slipped sees and hears it.
/// </summary>
internal static class A17_BananaDarkness
{
    private const float DelaySeconds = 3f;
    private const float Seconds = 20f;
    private const float FadeInSeconds = 1f;
    private const float FadeOutSeconds = 2f;

    // The muffling, at full strength (TFA's Ice Fog numbers).
    private const float OpenCutoff = 22000f;
    private const float SoundCutoff = 900f;
    private const float VoiceCutoff = 3800f;
    private const float VoiceVolume = 0.5f;
    private const float SoundDrop = -8f;
    private const float Silence = -80f;
    private const string SoundKnob = "SFXVolume";
    private const string MusicKnob = "MusicVolume";

    /// <summary>How often the scene is looked over for sound sources to muffle.</summary>
    private const float ScanSeconds = 0.5f;

    private static float startedAt = -1000f;

    private static AudioMixer mixer;
    private static bool tookKnobs;
    private static float soundWas;
    private static float musicWas;
    private static readonly List<AudioLowPassFilter> filters = new List<AudioLowPassFilter>();
    private static readonly HashSet<AudioSource> seen = new HashSet<AudioSource>();
    private static float nextScan;

    internal static void Begin()
    {
        startedAt = Time.time + DelaySeconds;
    }

    internal static void Reset()
    {
        startedAt = -1000f;
        Muffle(0f);
    }

    /// <summary>How dark the screen is right now, 0 to 1 (and how muffled the sound).</summary>
    internal static float Strength
    {
        get
        {
            float t = Time.time - startedAt;
            if (t < 0f || t >= Seconds)
            {
                return 0f;
            }
            return Mathf.Min(Mathf.Clamp01(t / FadeInSeconds), Mathf.Clamp01((Seconds - t) / FadeOutSeconds));
        }
    }

    internal static bool Active => Strength > 0f;

    /// <summary>Muffled sound effects, for the tests: how many sources are, and their cutoff now.</summary>
    internal static int MuffledSources => filters.Count;

    internal static float SoundCutoffNow => Mathf.Lerp(OpenCutoff, SoundCutoff, Strength);

    internal static void Tick()
    {
        Muffle(Strength);
    }

    private static void Muffle(float strength)
    {
        Knobs(strength);
        if (strength <= 0f)
        {
            if (filters.Count > 0 || seen.Count > 0)
            {
                foreach (AudioLowPassFilter filter in filters)
                {
                    if (filter != null)
                    {
                        UnityEngine.Object.Destroy(filter);
                    }
                }
                filters.Clear();
                seen.Clear();
            }
            return;
        }
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + ScanSeconds;
            Scan();
        }
        float cutoff = Mathf.Lerp(OpenCutoff, SoundCutoff, strength);
        for (int i = filters.Count - 1; i >= 0; i--)
        {
            if (filters[i] == null)
            {
                filters.RemoveAt(i);
            }
            else if (Mathf.Abs(filters[i].cutoffFrequency - cutoff) > 30f)
            {
                filters[i].cutoffFrequency = cutoff;
            }
        }
    }

    /// <summary>Sound effects (not the music, not voices, not what has a low-pass of its own already) get one.</summary>
    private static void Scan()
    {
        foreach (AudioSource source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (source == null || !seen.Add(source))
            {
                continue;
            }
            string group = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "";
            if (group.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0 || group.IndexOf("Voice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.GetComponent<CharacterVoiceHandler>() != null || source.GetComponent<AudioLowPassFilter>() != null)
            {
                continue;
            }
            AudioLowPassFilter filter = source.gameObject.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = SoundCutoffNow;
            filters.Add(filter);
        }
    }

    /// <summary>
    /// The sound effects' and the music's volume, put back as they were afterwards. While TFA's Ice Fog is out,
    /// it has these knobs (it muffles the same way).
    /// </summary>
    private static void Knobs(float strength)
    {
        if (strength <= 0f || Compat.Tfa.IceFogDrawing)
        {
            if (tookKnobs)
            {
                if (mixer != null)
                {
                    mixer.SetFloat(SoundKnob, soundWas);
                    mixer.SetFloat(MusicKnob, musicWas);
                }
                tookKnobs = false;
            }
            return;
        }
        if (mixer == null)
        {
            mixer = SettingsHandler.OriginalMixer;
            if (mixer == null)
            {
                return;
            }
        }
        if (!tookKnobs)
        {
            tookKnobs = mixer.GetFloat(SoundKnob, out soundWas) & mixer.GetFloat(MusicKnob, out musicWas);
            if (!tookKnobs)
            {
                return;
            }
        }
        mixer.SetFloat(SoundKnob, soundWas + SoundDrop * strength);
        mixer.SetFloat(MusicKnob, Mathf.Lerp(musicWas, Silence, Mathf.Clamp01(strength * 1.5f)));
    }

    /// <summary>The Scoutmaster sets his screen effect every frame (there is always one in the level, hidden away).</summary>
    [HarmonyPatch(typeof(Scoutmaster), nameof(Scoutmaster.DoVisuals))]
    private static class Darken
    {
        private static void Postfix(Scoutmaster __instance)
        {
            float strength = Strength;
            if (strength <= 0f || __instance.mat == null)
            {
                return;
            }
            __instance.mat.SetFloat(__instance.GRAINMULTID, GUIManager.instance != null && GUIManager.instance.photosensitivity ? 0f : 1f);
            __instance.mat.SetFloat(__instance.STRENGTHID, Mathf.Max(__instance.mat.GetFloat(__instance.STRENGTHID), strength));
        }
    }

    /// <summary>A teammate's voice is duller: its own low-pass (set every frame for walls in between) goes no higher.</summary>
    [HarmonyPatch(typeof(VoiceObscuranceFilter), "Update")]
    private static class DullVoices
    {
        private static void Postfix(VoiceObscuranceFilter __instance)
        {
            float strength = Strength;
            if (strength > 0f && __instance.lowPass != null && __instance.lowPass.enabled)
            {
                __instance.lowPass.cutoffFrequency = Mathf.Min(__instance.lowPass.cutoffFrequency, Mathf.Lerp(OpenCutoff, VoiceCutoff, strength));
            }
        }
    }

    /// <summary>...and quieter (the game sets the voice's volume every frame).</summary>
    [HarmonyPatch(typeof(CharacterVoiceHandler), "Update")]
    private static class QuieterVoices
    {
        private static void Postfix(CharacterVoiceHandler __instance)
        {
            float strength = Strength;
            if (strength > 0f && __instance.m_source != null && __instance.m_character != null && !__instance.m_character.IsLocal)
            {
                __instance.m_source.volume *= Mathf.Lerp(1f, VoiceVolume, strength);
            }
        }
    }
}
