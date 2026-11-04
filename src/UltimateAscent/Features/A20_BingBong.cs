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
/// That crab status comes without the game's status effects (its glow over the scout, the injury flash and shake):
/// instead, from its first step on the bar, every scout's screen turns red, from a faint tint to a dark red over nearly
/// all of it in 10 seconds.
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

    /// <summary>The screen turns red over this long once the crab comes: from a faint tint to a dark red over nearly all of it.</summary>
    internal const float RedSeconds = 10f;

    /// <summary>How fast the red clears once Bing Bong is held again (or the scout is out of it).</summary>
    internal const float RedClearSeconds = 1f;

    private static readonly Color FaintRed = new Color(0.9f, 0.1f, 0.08f);
    private static readonly Color DarkRed = new Color(0.45f, 0f, 0.02f);

    /// <summary>The red all over the screen, and at its edges, from first to last.</summary>
    internal const float TintFaint = 0.06f;
    internal const float TintFull = 0.55f;
    internal const float EdgesFaint = 0.2f;
    internal const float EdgesFull = 0.97f;

    /// <summary>How far in the edges have closed at the last (the clear middle is this many times smaller).</summary>
    internal const float EdgesCloseIn = 3f;

    /// <summary>How far the red has come (0: the faint tint, 1: nearly all dark red), and how much of it shows (it clears as a whole).</summary>
    internal static float Redness { get; private set; }

    internal static float RedShown { get; private set; }

    /// <summary>How long Bing Bong had been left alone when the crab first landed on the bar (-1: not yet).</summary>
    private static float redSince = -1f;

    private static GameObject redCanvas;
    private static UnityEngine.UI.RawImage redTint;
    private static UnityEngine.UI.RawImage redEdges;

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
        redSince = -1f;
        Redness = 0f;
        RedShown = 0f;
        if (redCanvas != null)
        {
            redCanvas.SetActive(false);
        }
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
            redSince = -1f;
            Silence();
            Redden(false);
            return;
        }
        if (local.data.passedOutOnTheBeach > 0f || SomeoneHoldsBingBong())
        {
            neglectedFor = 0f;
            redSince = -1f;
            Silence();
            Redden(false);
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
            // Without the game's status effects: the red screen shows it instead, from the crab's first step on the bar.
            CharacterAfflictions afflictions = local.refs.afflictions;
            float before = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Crab);
            afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Crab, CrabPerSecond * Time.deltaTime, playEffects: false);
            if (redSince < 0f && afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Crab) > before)
            {
                redSince = neglectedFor;
            }
        }
        Redden(redSince >= 0f);
    }

    /// <summary>
    /// The scout's screen while the crab comes: from the crab's first step on the bar it turns red over 10 s, from a faint
    /// tint to a dark red that closes in from the edges until it covers nearly all of it, and clears within a second once
    /// Bing Bong is held again. It lies over the world but under the game's HUD, on every scout's own game (each one
    /// counts the neglect there).
    /// </summary>
    private static void Redden(bool crabComing)
    {
        if (crabComing)
        {
            Redness = Mathf.Clamp01((neglectedFor - redSince) / RedSeconds);
            RedShown = 1f;
        }
        else if (RedShown > 0f)
        {
            RedShown = Mathf.MoveTowards(RedShown, 0f, Time.deltaTime / RedClearSeconds);
            if (RedShown <= 0f)
            {
                Redness = 0f;
            }
        }
        if (RedShown <= 0f)
        {
            if (redCanvas != null && redCanvas.activeSelf)
            {
                redCanvas.SetActive(false);
            }
            return;
        }
        if (redCanvas == null)
        {
            MakeRed();
        }
        redCanvas.SetActive(true);
        Color red = Color.Lerp(FaintRed, DarkRed, Redness);
        redTint.color = new Color(red.r, red.g, red.b, RedShown * Mathf.Lerp(TintFaint, TintFull, Redness));
        redEdges.color = new Color(red.r, red.g, red.b, RedShown * Mathf.Lerp(EdgesFaint, EdgesFull, Redness));
        // Seen from further off, the edges' picture leaves a smaller clear middle (past its rim it stays solid).
        float span = Mathf.Lerp(1f, EdgesCloseIn, Redness);
        redEdges.uvRect = new Rect(0.5f - span / 2f, 0.5f - span / 2f, span, span);
    }

    /// <summary>The red: a tint over the whole screen, and its edges, solid from the rim and clear in the middle.</summary>
    private static void MakeRed()
    {
        const int size = 256;
        Texture2D edges = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UltimateAscent Bing Bong Red" };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Sqrt(2f);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.7f, r));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        edges.SetPixels32(pixels);
        edges.Apply(false, true);
        redCanvas = new GameObject("UltimateAscent Bing Bong Red");
        Object.DontDestroyOnLoad(redCanvas);
        Canvas canvas = redCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -100;
        redTint = FullScreen("Tint", Texture2D.whiteTexture);
        redEdges = FullScreen("Edges", edges);
    }

    private static UnityEngine.UI.RawImage FullScreen(string name, Texture texture)
    {
        GameObject holder = new GameObject(name, typeof(RectTransform));
        holder.transform.SetParent(redCanvas.transform, false);
        UnityEngine.UI.RawImage image = holder.AddComponent<UnityEngine.UI.RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return image;
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
