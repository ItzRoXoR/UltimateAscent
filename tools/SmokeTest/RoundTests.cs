using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;
using Zorro.Core.Serizalization;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the round after 1.4.0: crab, ghosts and the Torch, the Citadel, the Nadir, ravens, the multiplayer six-slot backpack.</summary>
internal sealed partial class Runner
{
    /// <summary>Arrows the traps fire (their visuals message, which every shot sends).</summary>
    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.FireArrowVisuals_RPC))]
    internal static class ArrowShots
    {
        internal static int Count;

        private static void Prefix()
        {
            Count++;
        }
    }

    /// <summary>The climbing speed the local scout's climbing works with (read where the game reads it).</summary>
    [HarmonyPatch(typeof(CharacterClimbing), nameof(CharacterClimbing.GetRequestedPostition))]
    internal static class ClimbSpeedRecorder
    {
        internal static float Last = -1f;

        private static void Prefix(CharacterClimbing __instance)
        {
            if (__instance.character != null && __instance.character.IsLocal && __instance.character.data.climbMod != null)
            {
                Last = __instance.character.data.climbMod.speedMultiplier;
            }
        }
    }

    /// <summary>Petrification the game gives (and, while blocking, holds back: a test must not turn the scout to stone).</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddPetrify), typeof(int))]
    internal static class PetrifyRecorder
    {
        internal static bool Blocking;
        internal static readonly List<int> Given = new List<int>();

        private static bool Prefix(int petrify)
        {
            if (!Blocking)
            {
                return true;
            }
            Given.Add(petrify);
            return false;
        }
    }

    private void PatchRoundRecorders(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(ArrowShots)).Patch();
        harmony.CreateClassProcessor(typeof(ClimbSpeedRecorder)).Patch();
        harmony.CreateClassProcessor(typeof(PetrifyRecorder)).Patch();
        harmony.CreateClassProcessor(typeof(RepeatedSpawnFromHistory)).Patch();
        harmony.CreateClassProcessor(typeof(LootTablesBuilt)).Patch();
        harmony.CreateClassProcessor(typeof(BlindModeUnseen)).Patch();
        harmony.CreateClassProcessor(typeof(HoldLootTables)).Patch();
        harmony.CreateClassProcessor(typeof(EruptionsSeen)).Patch();
        harmony.CreateClassProcessor(typeof(SoundsPlayed)).Patch();
    }

    /// <summary>The sounds played through the game's sound player, while a check listens.</summary>
    [HarmonyPatch(typeof(SFX_Player), nameof(SFX_Player.PlaySFX))]
    internal static class SoundsPlayed
    {
        internal static bool Listening;
        internal static readonly List<SFX_Instance> Heard = new List<SFX_Instance>();

        private static void Prefix(SFX_Instance SFX)
        {
            if (Listening && SFX != null)
            {
                Heard.Add(SFX);
            }
        }
    }

    /// <summary>Every eruption that breaks out on this game: when and where.</summary>
    [HarmonyPatch(typeof(EruptionSpawner), nameof(EruptionSpawner.RPCA_SpawnEruption))]
    internal static class EruptionsSeen
    {
        internal static readonly List<(float at, Vector3 where)> All = new List<(float, Vector3)>();

        /// <summary>The Caldera's lava river's level when each one broke out (NaN without one).</summary>
        internal static readonly List<float> Lava = new List<float>();

        private static Lava river;

        private static void Postfix(Vector3 position)
        {
            All.Add((Time.time, position));
            if (river == null)
            {
                river = FindObjectsByType<Lava>(FindObjectsSortMode.None).FirstOrDefault(l => l.name == "River");
            }
            Lava.Add(river != null ? river.transform.position.y : float.NaN);
        }
    }

    /// <summary>While a check picks from pools it has set, the Lantern's sync before a pick (it may rebuild every table) waits.</summary>
    [HarmonyPatch(typeof(MoreLanterns), nameof(MoreLanterns.Sync))]
    internal static class HoldLootTables
    {
        internal static bool Hold;

        private static bool Prefix() => !Hold;
    }

    /// <summary>
    /// Blind Mode stays on for the climb (the VR Headset needs it the whole climb), but the screen is not darkened while
    /// the checks run, so they can be watched; only the checks of the darkening itself show it.
    /// </summary>
    [HarmonyPatch(typeof(ChallengeModes), "get_Blinding")]
    internal static class BlindModeUnseen
    {
        internal static bool Shown;

        private static void Postfix(ref bool __result)
        {
            __result &= Shown;
        }
    }

    /// <summary>How many times the game's loot tables have been built.</summary>
    [HarmonyPatch(typeof(LootData), nameof(LootData.PopulateLootData))]
    internal static class LootTablesBuilt
    {
        internal static int Count;

        private static void Prefix()
        {
            Count++;
        }
    }

    /// <summary>
    /// The test jumps back to segments whose campfire has already spawned its items. The game's SingleItemSpawner then
    /// replays its spawn history, which comes back empty (null) there, and throws (its other spawners check for that),
    /// so the jump stops half way. In the test such a repeat spawns nothing, as the game's other spawners do.
    /// </summary>
    [HarmonyPatch(typeof(SingleItemSpawner), nameof(SingleItemSpawner.TrySpawnItems))]
    internal static class RepeatedSpawnFromHistory
    {
        internal static int Caught;

        private static Exception Finalizer(SingleItemSpawner __instance, Exception __exception, ref List<PhotonView> __result)
        {
            if (__exception is NullReferenceException && __instance != null && __instance.HasSpawnTracking(out var tracker) && tracker.HasSpawnHistory)
            {
                Caught++;
                __result = new List<PhotonView>();
                return null;
            }
            return __exception;
        }
    }

    /// <summary>
    /// How loud the rock smash is where the scout hears it, next to the Coconut's bonk and the game's coconut break,
    /// each played alone at the same spot 2 m in front of the scout (what the scout hears is recorded): at least as
    /// loud as the coconut break, and within 6 dB of the bonk.
    /// </summary>
    private IEnumerator SmashLoudness()
    {
        Item coconut = ItemByName("Item_Coconut");
        SFX_Instance bonk = coconut != null && coconut.TryGetComponent(out Bonkable bonkable) ? bonkable.bonk.FirstOrDefault(b => b != null) : null;
        SFX_Instance coconutBreak = coconut != null && coconut.TryGetComponent(out Breakable breakable) ? breakable.breakSFX.FirstOrDefault(b => b != null) : null;
        Character local = Local;
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        List<string> heard = new List<string>();
        Dictionary<string, float> loudest = new Dictionary<string, float>();
        foreach ((string name, SFX_Instance sound) in new[] { ("the rock smash", StoneItem.BreakSound()), ("the coconut bonk", bonk), ("the coconut break", coconutBreak) })
        {
            if (sound == null)
            {
                heard.Add(name + ": none");
                continue;
            }
            ListenerRecorder recorder = ListenerRecorder.Attach();
            if (recorder == null)
            {
                yield break;
            }
            recorder.Begin();
            yield return new WaitForSeconds(0.3f);
            sound.lastTimePlayed = -100f;
            sound.Play(local.Center + forward * 2f);
            yield return new WaitForSeconds(1.8f);
            string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "UltimateAscent.SmokeTest.loudness-" + name.Replace(' ', '-') + ".wav");
            recorder.End(path);
            float[] levels = Levels(path);
            loudest[name] = levels[1];
            heard.Add($"{name} ({sound.name}, volume {sound.settings.volume:0.##}, clip {(sound.clips.Length > 0 && sound.clips[0] != null ? sound.clips[0].name : "none")}): peak {levels[0]:0.0} dBFS, loudest 50 ms {levels[1]:0.0} dBFS, before it {levels[2]:0.0} dBFS");
        }
        bool known = loudest.TryGetValue("the rock smash", out float smash) & loudest.TryGetValue("the coconut bonk", out float bonked) & loudest.TryGetValue("the coconut break", out float cracked);
        Check("the rock smash is heard: at least as loud as the game's coconut break, within 6 dB of the Coconut's bonk (2 m away, recorded)",
            known && smash >= cracked - 0.5f && smash >= bonked - 6f, string.Join("; ", heard));
    }

    /// <summary>
    /// One sound played alone at a spot, recorded where the scout hears it: its peak, its loudest 50 ms and the level
    /// before it (dBFS), and a line describing it.
    /// </summary>
    private IEnumerator MeasureSound(string name, SFX_Instance sound, Vector3 at, Action<float[], string> done, Action<Vector3> play = null)
    {
        ListenerRecorder recorder = ListenerRecorder.Attach();
        if (sound == null || recorder == null)
        {
            done(null, name + ": " + (sound == null ? "no sound" : "no listener"));
            yield break;
        }
        recorder.Begin();
        yield return new WaitForSeconds(0.3f);
        sound.lastTimePlayed = -100f;
        if (play != null)
        {
            play(at);
        }
        else
        {
            sound.Play(at);
        }
        yield return null;
        yield return null;
        AudioSource playing = FindObjectsByType<AudioSource>(FindObjectsSortMode.None).FirstOrDefault(a => a != null && a.isPlaying && a.clip != null && sound.clips.Contains(a.clip));
        string played = playing != null ? $"played at volume {playing.volume:0.###}, clip {playing.clip.name}" : "no source found playing it";
        yield return new WaitForSeconds(1.75f);
        string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "UltimateAscent.SmokeTest.loudness-" + name.Replace(' ', '-') + ".wav");
        recorder.End(path);
        float[] levels = Levels(path);
        float away = MainCamera.instance != null ? Vector3.Distance(MainCamera.instance.transform.position, at) : -1f;
        done(levels, $"{name} ({sound.name}, volume {sound.settings.volume:0.##} (variation {sound.settings.volume_Variation:0.##}), {sound.clips.Length} clip(s), range {sound.settings.range:0}, blend {sound.settings.spatialBlend:0.##}, {away:0.0} m away; {played}): peak {levels[0]:0.0} dBFS, loudest 50 ms {levels[1]:0.0} dBFS, before it {levels[2]:0.0} dBFS");
    }

    /// <summary>A recording's peak and loudest 50 ms (dBFS) after its first 0.3 s, and its level before.</summary>
    private static float[] Levels(string path)
    {
        byte[] wav = System.IO.File.ReadAllBytes(path);
        int channels = BitConverter.ToInt16(wav, 22);
        int rate = BitConverter.ToInt32(wav, 24);
        int frames = (wav.Length - 44) / 2 / channels;
        float Sample(int frame)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                sum += BitConverter.ToInt16(wav, 44 + 2 * (frame * channels + c)) / 32768f;
            }
            return sum / channels;
        }
        float Db(float v) => v > 0f ? 20f * Mathf.Log10(v) : -120f;
        int start = Mathf.Min(frames, (int)(rate * 0.3f));
        int window = rate / 20;
        float peak = 0f;
        float loudest = 0f;
        for (int at = start; at + window <= frames; at += window)
        {
            float sum = 0f;
            for (int i = at; i < at + window; i++)
            {
                float v = Sample(i);
                sum += v * v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            loudest = Mathf.Max(loudest, Mathf.Sqrt(sum / window));
        }
        float before = 0f;
        for (int i = 0; i < start; i++)
        {
            float v = Sample(i);
            before += v * v;
        }
        return new[] { Db(peak), Db(loudest), Db(start > 0 ? Mathf.Sqrt(before / start) : 0f) };
    }

    /// <summary>
    /// Ascent 14's Parasol: taken up high and opened, it slows the fall as much as the game's (the same fall with the
    /// rule off, in memory), and every second of gliding takes a second off its 20 s, shown on its use bar; when the
    /// time is gone it is used up.
    /// </summary>
    private IEnumerator ParasolWearsOut()
    {
        Item prefab = ItemByName("Parasol");
        if (prefab == null || !Rules.Enabled(Lvl.WorseItems))
        {
            Info("the Parasol check is skipped", prefab == null ? "no Parasol in the item database" : $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Vector3 home = local.Center;
        ConfigEntry<bool> worse = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        yield return SpawnIntoHand(prefab);
        Item item = held;
        Parasol parasol = item != null ? item.GetComponent<Parasol>() : null;
        if (parasol == null)
        {
            yield break;
        }
        float Left() => item != null && item.data != null && item.data.TryGetDataEntry(DataEntryKey.Fuel, out FloatItemData fuel) ? fuel.Value : -1f;
        float Bar() => item != null && item.data != null && item.data.TryGetDataEntry(DataEntryKey.UseRemainingPercentage, out FloatItemData bar) ? bar.Value : -1f;
        float gameSpeed = 0f;
        float speed = 0f;
        float before = 0f;
        float after = 0f;
        bool inAir = false;
        try
        {
            // A glide of 3 s: the speed of the last second, once the canopy has slowed the fall.
            IEnumerator Glide(Action<float> done)
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up * 80f, false);
                yield return new WaitForSeconds(0.4f);
                if (!parasol.isOpen)
                {
                    parasol.ToggleOpen();
                }
                float sum = 0f;
                int frames = 0;
                for (float until = Time.time + 3f; Time.time < until;)
                {
                    if (until - Time.time < 1f)
                    {
                        sum += -local.GetBodypartRig(BodypartType.Hip).linearVelocity.y;
                        frames++;
                    }
                    yield return null;
                }
                inAir = !local.data.isGrounded;
                done(sum / Mathf.Max(1, frames));
            }
            worse.Value = false;
            HostOptions.Invalidate();
            yield return Glide(v => gameSpeed = v);
            worse.Value = true;
            HostOptions.Invalidate();
            before = Left() < 0f ? A14_WorseItems.ParasolSeconds : Left();
            yield return Glide(v => speed = v);
            after = Left();
            float bar = Bar();
            Screenshot("parasol-gliding");
            Check("ascent 14's Parasol slows the fall as much as the game's, and 3 s of gliding take about 3 s off its 20 s, shown on its use bar (14)",
                inAir && speed <= gameSpeed * 1.1f + 0.3f && Mathf.Abs(before - after - 3.4f) < 0.5f && Mathf.Abs(bar - after / A14_WorseItems.ParasolSeconds) < 0.01f,
                $"in the air {inAir}, falling at {speed:0.0} m/s (the game's Parasol {gameSpeed:0.0} m/s), {before:0.0} s -> {after:0.0} s left (3.4 s in the air), use bar {bar:0.00}");
            // The last half second of it.
            item.GetData<FloatItemData>(DataEntryKey.Fuel).Value = 0.5f;
            for (float until = Time.time + 2f; Time.time < until && item != null && item.gameObject.activeInHierarchy;)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            bool gone = item == null || !item.gameObject.activeInHierarchy;
            bool inSlots = new byte[] { 0, 1, 2, 250 }.Select(slot => local.player.GetItemSlot(slot)).Any(slot => slot != null && !slot.IsEmpty() && slot.prefab != null && slot.prefab.itemID == prefab.itemID);
            Check("when its time is gone the Parasol is used up (14)", gone && !inSlots, $"gone {gone}, still in a slot {inSlots}");
        }
        finally
        {
            worse.Value = true;
            HostOptions.Invalidate();
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up * 1f, false);
        }
        yield return new WaitForSeconds(1f);
        yield return PutAway(prefab);
    }

    /// <summary>The clown chest's pool holds the Lightweight Parasol at 0.5% of its picks (the game's tables, as built).</summary>
    private void ClownChestParasolShare()
    {
        LootData.PopulateLootData();
        Item parasol = ItemByName(A14_Items.RootsParasolPrefab);
        Dictionary<ushort, int> pool = LootData.AllSpawnWeightData != null && LootData.AllSpawnWeightData.TryGetValue(SpawnPool.LuggageClown, out Dictionary<ushort, int> found) ? found : null;
        int weight = pool != null && parasol != null && pool.TryGetValue(parasol.itemID, out int w) ? w : 0;
        int total = pool != null ? pool.Values.Sum() : 0;
        float share = total > 0 ? (float)weight / total : 0f;
        Check("the clown chest's pool holds the Lightweight Parasol at 0.5% of its picks", Mathf.Abs(share - ClownChestParasol.Share) < 0.0005f,
            $"{share * 100f:0.00}% ({weight} of {total}, {(pool != null ? pool.Count : 0)} items in the pool)");
    }

    /// <summary>
    /// Blindness is taken away by the First Aid Kit and the Cure-All (used from the hand, the game's way): the blindness
    /// effect and the darkened screen (the game's own "blind" flag on the scout).
    /// </summary>
    private IEnumerator BlindnessCuresCheck()
    {
        foreach (string name in new[] { BlindnessCures.FirstAidKitPrefab, BlindnessCures.CureAllPrefab })
        {
            Item prefab = ItemByName(name);
            if (prefab == null)
            {
                Fail(name + " is in the item database");
                continue;
            }
            Afflictions.AddAffliction(new Peak.Afflictions.Affliction_Blind { totalTime = 60f });
            yield return new WaitForSeconds(0.5f);
            bool blindBefore = Afflictions.HasAfflictionType(Peak.Afflictions.Affliction.AfflictionType.Blind, out _) && Local.data.isBlind;
            yield return SpawnIntoHand(prefab);
            if (held == null)
            {
                continue;
            }
            yield return EatHeld();
            bool effectAfter = Afflictions.HasAfflictionType(Peak.Afflictions.Affliction.AfflictionType.Blind, out _);
            bool screenAfter = Local.data.isBlind;
            Screenshot("blindness-cured-" + name);
            Check($"the {Plain(prefab.GetItemName())} cures blindness: the effect and the darkened screen", blindBefore && !effectAfter && !screenAfter,
                $"blind before {blindBefore}; after: the effect {effectAfter}, the screen dark {screenAfter}");
            Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.Blind);
            yield return PutAway(prefab);
        }
    }

    /// <summary>
    /// The new items on every ascent: with the ascent set to 3 for a moment (in memory), they may spawn with "New Items"
    /// on, the Roots parasol is the Lightweight Parasol and the Torch swings; with "New Items" off none spawns.
    /// </summary>
    private void NewItemsOnEveryAscent()
    {
        int ascent = Ascents._currentAscent;
        ConfigEntry<bool> newItems = UltimateAscent.Plugin.NewItems;
        bool saved = newItems.Value;
        Item chalk = ItemByName(ClimbingChalkItem.PrefabName);
        try
        {
            Ascents._currentAscent = 3;
            newItems.Value = true;
            HostOptions.Invalidate();
            bool available = NewItems.Available && NewItems.WildOn && TorchItem.Works && A14_Items.LightweightParasolExists && chalk != null && chalk.IsValidToSpawn();
            bool dagger = Traverse.Create(typeof(ZombieDagger)).Property("On").GetValue<bool>();
            bool breakingFree = Traverse.Create(typeof(Struggling)).Property("On").GetValue<bool>();
            newItems.Value = false;
            HostOptions.Invalidate();
            bool off = !NewItems.Available && !NewItems.WildOn && (chalk == null || !chalk.IsValidToSpawn());
            Check("on ascent 3 the new items spawn with \"New Items\" on (the Roots parasol is the Lightweight Parasol, the Torch swings), and none with it off",
                available && off, $"on: available {available}; off: none {off}");
            Check("on ascent 3 the Ritual Dagger works on zombies and breaking free from webs and fly traps is the mod's", dagger && breakingFree,
                $"dagger {dagger}, breaking free {breakingFree}");
        }
        finally
        {
            Ascents._currentAscent = ascent;
            newItems.Value = saved;
            HostOptions.Invalidate();
        }
    }

    /// <summary>
    /// The peak's zombies (17): the scout is taken to the top of the peak's hill; the ten places are on its level ground, and each zombie appears as the scout comes near, dressed in one of the two
    /// looks (skin, outfit, hat, eyes, mouth, freckles), without sash or medal. Pictured, then the scout goes back.
    /// </summary>
    private IEnumerator PeakZombiesCheck()
    {
        if (!Rules.Enabled(Lvl.Hazards))
        {
            Info("the peak zombie check is skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Vector3 home = local.Center;
        Transform summit = PeakHill.Summit();
        if (summit == null)
        {
            Fail("the summit (the game's flare boxes at the peak)");
            yield break;
        }
        // The last part of the climb (the game's own jump; it brings that part of the map), then near the summit: the
        // places are picked once a scout is within 400 m of it.
        if (Singleton<MapHandler>.Instance.GetCurrentSegment() < Segment.TheKiln)
        {
            MapHandler.JumpToSegment(Segment.TheKiln);
            yield return new WaitForSeconds(8f);
        }
        // On top of the hill: straight down onto its ground from high above the summit (the summit's own point lies in it).
        Vector3 onTop = summit.position + Vector3.up * 3f;
        if (Physics.Raycast(summit.position + Vector3.up * 80f, Vector3.down, out RaycastHit top, 200f, HelperFunctions.terrainMapMask))
        {
            onTop = top.point + Vector3.up * 1.5f;
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, onTop, false);
        for (float until = Time.time + 8f; Time.time < until && PeakZombies.Places.Count == 0;)
        {
            yield return null;
        }
        Check("ten places for zombies on the level ground of the peak's hill, away from the summit (17)",
            PeakZombies.Places.Count == PeakZombies.Count && PeakZombies.Places.All(p => Vector3.Distance(p, summit.position) >= PeakHill.AwayFromSummit),
            $"{PeakZombies.Places.Count} places, {string.Join(", ", PeakZombies.Places.Select(p => $"{Vector3.Distance(p, summit.position):0} m"))} from the summit at {summit.position}");
        Vector2 look = local.data.lookValues;
        Protect(true);
        // Every place in turn: the scout comes near, its zombie appears; each is looked at as it is found.
        Dictionary<int, MushroomZombie> seen = new Dictionary<int, MushroomZombie>();
        List<string> wrong = new List<string>();
        int[] looks = new int[PeakZombies.Looks.Length];
        HashSet<int> pictured = new HashSet<int>();
        Customization options = Singleton<Customization>.Instance;
        try
        {
            foreach (Vector3 place in PeakZombies.Places.ToList())
            {
                Vector3 toward = summit.position - place;
                toward.y = 0f;
                Vector3 stand = place + (toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward) * 2.5f + Vector3.up * 1.5f;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
                MushroomZombie found = null;
                for (float until = Time.time + 4f; Time.time < until && found == null;)
                {
                    found = FindObjectsByType<MushroomZombie>(FindObjectsSortMode.None)
                        .FirstOrDefault(z => PeakZombies.IsPeakZombie(z, out _) && Vector3.Distance(z.transform.position, place) < 6f && !seen.ContainsKey(z.photonView.ViewID));
                    yield return null;
                }
                if (found == null)
                {
                    wrong.Add($"none at the place {Vector3.Distance(place, summit.position):0} m from the summit");
                    continue;
                }
                // Dressed a frame or two in, and again once the game has faded it in.
                yield return new WaitForSeconds(1.8f);
                if (found == null)
                {
                    continue;
                }
                seen[found.photonView.ViewID] = found;
                PeakZombies.IsPeakZombie(found, out int index);
                looks[index]++;
                PeakZombies.Look wanted = PeakZombies.Looks[index];
                CustomizationRefs refs = found.character.refs.customization.refs;
                CustomizationOption fit = options.fits.First(f => f != null && f.name == wanted.Fit);
                int hat = Array.FindIndex(options.hats, h => h != null && h.name == wanted.Hat);
                Color skin = options.skins.First(o => o != null && o.name == wanted.Skin).color;
                bool fitOn = refs.mainRenderer.sharedMesh == fit.fitMesh;
                Renderer hatRenderer = hat >= 0 && hat < refs.playerHats.Length ? refs.playerHats[hat] : null;
                bool hatOn = hatRenderer != null && hatRenderer.gameObject.activeInHierarchy && hatRenderer.enabled;
                bool skinOn = refs.PlayerRenderers.Length > 0 && refs.PlayerRenderers[0].material.HasProperty("_SkinColor") && (refs.PlayerRenderers[0].material.GetColor("_SkinColor") - skin).maxColorComponent < 0.02f;
                bool sash = refs.sashRenderer != null && refs.sashRenderer.enabled;
                bool medal = refs.medalRenderer != null && refs.medalRenderer.gameObject.activeSelf;
                bool eyesOn = refs.EyeRenderers.Length > 0 && refs.EyeRenderers[0].material.GetTexture(CharacterCustomization.MainTex) == options.eyes.First(o => o != null && o.name == wanted.Eyes).texture;
                bool mouthOn = refs.mouthRenderer.material.GetTexture(CharacterCustomization.MainTex) == options.mouths.First(o => o != null && o.name == wanted.Mouth).texture;
                bool mushrooms = found.mushroomVisuals.Any(m => m != null && m.activeInHierarchy);
                string otherHats = string.Join(",", refs.playerHats.Select((r, i) => (r, i)).Where(t => t.r != null && t.r.gameObject.activeInHierarchy).Select(t => t.i));
                if (!fitOn || !hatOn || !skinOn || !eyesOn || !mouthOn || sash || medal || mushrooms)
                {
                    wrong.Add($"look {index}: outfit {fitOn}, hat {hatOn} (hats on: {otherHats}; {refs.playerHats.Length} hats), skin {skinOn}, eyes {eyesOn}, mouth {mouthOn}, sash {sash}, medal {medal}, mushrooms {mushrooms}");
                }
                if (pictured.Add(index))
                {
                    // Back a few steps, so the zombie lying asleep in the flowers is in the picture.
                    Vector3 back = local.Center - found.character.Center;
                    back.y = 0f;
                    Vector3 view = found.character.Center + (back.sqrMagnitude > 0.01f ? back.normalized : Vector3.forward) * 3.5f;
                    if (Physics.Raycast(view + Vector3.up * 5f, Vector3.down, out RaycastHit floor, 15f, HelperFunctions.terrainMapMask))
                    {
                        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, floor.point + Vector3.up * 1.5f, false);
                        yield return new WaitForSeconds(0.8f);
                    }
                    local.data.lookValues = HelperFunctions.DirectionToLook(found.character.Center - MainCamera.instance.transform.position);
                    yield return new WaitForSeconds(0.6f);
                    Screenshot("peak-zombie-look-" + index);
                    Info($"peak zombie look {index}", $"hats on: {otherHats}; hat {hat} '{wanted.Hat}' renderer {(hatRenderer != null ? hatRenderer.name + " enabled " + hatRenderer.enabled + " active " + hatRenderer.gameObject.activeInHierarchy : "none")}");
                }
            }
            Check("each of the peak's ten zombies appears as a scout comes near its place, half in each look, dressed as scouts (outfit, hat, skin, eyes, mouth) without sash, medal or mushrooms (17)",
                seen.Count == PeakZombies.Count && looks.All(n => n == PeakZombies.Count / PeakZombies.Looks.Length) && wrong.Count == 0,
                $"{seen.Count} zombies ({string.Join(" / ", looks)} in each look), dressed {PeakZombies.Dressed} time(s){(wrong.Count > 0 ? "; " + string.Join("; ", wrong) : "")}");
        }
        finally
        {
            local.data.lookValues = look;
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
        }
        yield return new WaitForSeconds(2f);
        ClearStatuses(Afflictions);
    }

    /// <summary>
    /// The waiting bee swarms (17): twenty on the peak's hill (put out with its zombie places), apart; still there 10 s
    /// later (no hive, no dispersing); the scout goes near one, which turns angry and goes for them; taken far away,
    /// the scout loses it and it disperses. Run after the zombies' check, which takes the scout to the hill.
    /// </summary>
    private IEnumerator WaitingBeesCheck()
    {
        if (!Rules.Enabled(Lvl.Hazards))
        {
            Info("the waiting bees check is skipped", $"testing ascent {target}");
            yield break;
        }
        List<BeeSwarm> Waiting() => FindObjectsByType<BeeSwarm>(FindObjectsSortMode.None).Where(b => b != null && ScatteredBees.IsWaiting(b) && !b.dispersing).ToList();
        for (float until = Time.time + 5f; Time.time < until && Waiting().Count < ScatteredBees.Count;)
        {
            yield return null;
        }
        List<BeeSwarm> swarms = Waiting();
        Transform summit = PeakHill.Summit();
        float closest = swarms.SelectMany(a => swarms.Where(b => b != a).Select(b => Vector3.Distance(a.transform.position, b.transform.position))).DefaultIfEmpty(float.MaxValue).Min();
        float nearSummit = summit != null ? swarms.Select(b => Vector3.Distance(b.transform.position, summit.position)).DefaultIfEmpty(0f).Min() : 0f;
        float farthest = summit != null ? swarms.Select(b => Vector3.Distance(b.transform.position, summit.position)).DefaultIfEmpty(0f).Max() : 0f;
        Check("twenty bee swarms without hives wait on the peak's hill, 8 m apart or more, away from the summit (17)",
            swarms.Count == ScatteredBees.Count && closest >= 7.9f && nearSummit >= PeakHill.AwayFromSummit && farthest < 250f,
            $"{swarms.Count} swarms waiting (placed {ScatteredBees.Placed.Count}), the closest two {closest:0} m apart, {nearSummit:0}-{farthest:0} m from the summit");
        if (swarms.Count == 0)
        {
            yield break;
        }
        yield return new WaitForSeconds(10f);
        int stillThere = Waiting().Count;
        Character local = Local;
        Vector3 home = local.Center;
        BeeSwarm swarm = Waiting().OrderBy(b => Vector3.Distance(b.transform.position, local.Center)).First();
        Vector3 place = swarm.transform.position;
        Protect(true);
        bool angry = false;
        bool chasing = false;
        bool dispersed = false;
        try
        {
            if (Physics.Raycast(place + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 10f, HelperFunctions.terrainMapMask))
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, ground.point + (local.Center - place).normalized * 2f + Vector3.up * 1.2f, false);
            }
            for (float until = Time.time + 6f; Time.time < until && !(angry && chasing);)
            {
                angry = swarm != null && swarm.beesAngry;
                chasing = swarm != null && swarm.currentAggroCharacter == local;
                yield return null;
            }
            Screenshot("waiting-bees-roused");
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
            for (float until = Time.time + 25f; Time.time < until && !dispersed;)
            {
                dispersed = swarm == null || swarm.dispersing;
                yield return null;
            }
        }
        finally
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
            ClearStatuses(Afflictions);
        }
        Check("a waiting swarm stays put, turns angry and goes for a scout who comes near, and disperses once it has lost them (17)",
            stillThere == swarms.Count && angry && chasing && dispersed,
            $"still there after 10 s {stillThere} of {swarms.Count}; angry {angry}, after the scout {chasing}; dispersed once left behind {dispersed}");
        yield return new WaitForSeconds(1f);
        ClearStatuses(Afflictions);
    }

    /// <summary>
    /// The Lightweight Parasol under low gravity: held open in the air with a shroomberry's low gravity, it lifts the
    /// scout up like balloons below ascent 14, and half as high with ascent 14's rule (both in memory).
    /// </summary>
    private IEnumerator ParasolLowGravityCheck()
    {
        Item prefab = ItemByName(A14_Items.RootsParasolPrefab);
        if (prefab == null)
        {
            Fail("the Lightweight Parasol is in the item database");
            yield break;
        }
        Character local = Local;
        Vector3 home = local.Center;
        ConfigEntry<bool> worse = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool saved = worse.Value;
        yield return SpawnIntoHand(prefab);
        Item item = held;
        Parasol parasol = item != null ? item.GetComponent<Parasol>() : null;
        if (parasol == null)
        {
            yield break;
        }
        Dictionary<bool, float> rise = new Dictionary<bool, float>();
        try
        {
            foreach (bool rule in new[] { false, true })
            {
                worse.Value = rule;
                HostOptions.Invalidate();
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up * 60f, false);
                yield return new WaitForSeconds(0.4f);
                if (!parasol.isOpen)
                {
                    parasol.ToggleOpen();
                }
                Afflictions.AddAffliction(new Peak.Afflictions.Affliction_LowGravity(3, 8f));
                yield return new WaitForSeconds(0.5f);
                float startHeight = local.Center.y;
                float rising = 0f;
                for (float until = Time.time + 3f; Time.time < until;)
                {
                    rising = Mathf.Max(rising, local.GetBodypartRig(BodypartType.Hip).linearVelocity.y);
                    yield return null;
                }
                float endHeight = local.Center.y;
                Screenshot("parasol-low-gravity-" + (rule ? "14" : "below-14"));
                Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.LowGravity);
                rise[rule] = endHeight - startHeight;
                Check($"the Lightweight Parasol open under a shroomberry's low gravity lifts the scout up like balloons ({(rule ? "14: half as high" : "below 14")})",
                    rule ? rise.ContainsKey(false) && rise[true] > 1f && Mathf.Abs(rise[true] / rise[false] - ParasolLowGravity.LiftWorse) < 0.12f : endHeight > startHeight + 1f,
                    $"height {startHeight:0.0} -> {endHeight:0.0} m in 3 s, rising at up to {rising:0.0} m/s{(rule && rise.ContainsKey(false) ? $"; {rise[true] / rise[false] * 100f:0}% of the rise below 14" : "")}");
            }
        }
        finally
        {
            Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.LowGravity);
            worse.Value = saved;
            HostOptions.Invalidate();
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
        }
        yield return new WaitForSeconds(1f);
        yield return PutAway(prefab);
    }

    /// <summary>
    /// Ascent 22's episodes, each brought on directly (their timing is random): nausea from eating while poisoned (no
    /// hunger, the stomach heard, standing still, bent over: pictured); dizziness (fades in and out smoothly, the view
    /// sways, stamina back at half pace); spores (numb, then the burst); cold (knocked down, shivering: pictured).
    /// </summary>
    private IEnumerator Ascent22Episodes()
    {
        if (!Rules.Enabled(Lvl.UniqueAilments))
        {
            Info("ascent 22's episodes are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Protect(false);
        Action calm = CalmForMeasuring();
        GameObject camera = null;
        try
        {
            // Nausea: 50 poison makes it certain.
            Item food = ItemByName("Granola Bar");
            yield return SpawnIntoHand(food);
            if (held != null)
            {
                ClearStatuses(Afflictions);
                Afflictions.SetStatus(Status.Hunger, 0.3f);
                Afflictions.SetStatus(Status.Poison, 0.5f);
                int nauseas = A22_Episodes.Nauseas;
                int heard = A22_Episodes.StomachsHeard;
                // Fully up first: not knocked down, on the ground, in full control of the body, and settled.
                for (float until = Time.time + 10f; Time.time < until && (local.data.fallSeconds > 0f || !local.data.isGrounded || local.data.currentRagdollControll < 0.99f);)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(1.5f);
                float standingHip = local.GetBodypart(BodypartType.Hip).Rig.position.y - local.data.groundPos.y;
                float standingHead = local.GetBodypart(BodypartType.Head).Rig.position.y - local.GetBodypart(BodypartType.Hip).Rig.position.y;
                yield return EatHeld();
                float hunger = Afflictions.GetCurrentStatus(Status.Hunger);
                bool still = A22_Episodes.Nauseous;
                camera = ClearViewCamera(local, 3f);
                yield return new WaitForSeconds(0.6f);
                RenderView(camera, "a22-nausea-bent-over");
                yield return new WaitForSeconds(0.8f);
                RenderView(camera, "a22-nausea-bent-over-2");
                float bentHip = local.GetBodypart(BodypartType.Hip).Rig.position.y - local.data.groundPos.y;
                float headHeight = local.GetBodypart(BodypartType.Head).Rig.position.y - local.GetBodypart(BodypartType.Hip).Rig.position.y;
                Destroy(camera);
                camera = null;
                Check("eating while poisoned can bring nausea (50 poison: always): no hunger from that food, the stomach growls, the scout stands still (22)",
                    A22_Episodes.Nauseas == nauseas + 1 && A22_Episodes.StomachsHeard == heard + 1 && Mathf.Abs(hunger - 0.3f) < 0.001f && still,
                    $"nausea {A22_Episodes.Nauseas - nauseas}, stomach heard {A22_Episodes.StomachsHeard - heard}, hunger 30 -> {hunger * 100f:0.#}, standing still {still}; head {headHeight:0.00} m over the hip");
                Check("the bent-over scout keeps standing on their feet: the hip as high as standing (22)", Mathf.Abs(bentHip - standingHip) < 0.08f && headHeight < standingHead - 0.1f,
                    $"hip {bentHip:0.00} m over the ground bent over, {standingHip:0.00} m standing; head {headHeight:0.00} m over the hip bent over, {standingHead:0.00} m standing");
                ClearStatuses(Afflictions);
                yield return new WaitForSeconds(3f);
            }

            // Dizziness: fades in over 1.5 s, holds, fades out by 6 s; stamina comes back at half pace meanwhile.
            List<float> curve = new List<float>();
            float darkest = 0f;
            // The game's own pace once it is coming back steadily (after its short wait), well below the cap: 20% a second.
            local.data.currentStamina = 0.05f;
            yield return new WaitForSeconds(1.5f);
            float before = local.data.currentStamina;
            yield return new WaitForSeconds(1f);
            float normalGain = local.data.currentStamina - before;
            local.data.currentStamina = 0.05f;
            A22_Episodes.StartDizziness();
            float start = Time.time;
            float gainStart = 0f;
            float dizzyGain = 0f;
            while (Time.time < start + A22_Episodes.DizzySeconds + 0.5f)
            {
                float t = Time.time - start;
                curve.Add(A22_Episodes.Dizziness);
                darkest = Mathf.Max(darkest, A22_Episodes.Darkness);
                if (gainStart == 0f && t >= 2f)
                {
                    gainStart = local.data.currentStamina;
                }
                if (dizzyGain == 0f && t >= 3f && gainStart > 0f)
                {
                    dizzyGain = local.data.currentStamina - gainStart;
                    Screenshot("a22-dizzy");
                }
                yield return null;
            }
            float biggestStep = curve.Zip(curve.Skip(1), (a, b) => Mathf.Abs(b - a)).DefaultIfEmpty(0f).Max();
            Check("heat's dizzy spell darkens and sways the view, fading in and out smoothly, and stamina comes back at half pace (22)",
                curve.Max() > 0.99f && curve.Last() < 0.001f && darkest > 0.55f && A22_Episodes.Darkness < 0.001f && biggestStep < 0.05f && Mathf.Abs(normalGain - 0.2f) < 0.03f && Mathf.Abs(dizzyGain - 0.2f * A22_Episodes.DizzyRegen) < 0.02f,
                $"strength up to {curve.Max():0.00}, {curve.Last():0.00} after it, the middle of the view darkened to {darkest:0.00}, the biggest step in a frame {biggestStep:0.000}; stamina back {normalGain * 100f:0.#}%/s normally, {dizzyGain * 100f:0.#}%/s dizzy");

            // Spores: numb at once, the burst 3 s later.
            ClearStatuses(Afflictions);
            int bursts = A22_Episodes.Bursts;
            A22_Episodes.StartSpores(local);
            yield return null;
            bool numb = Afflictions.HasAfflictionType(Peak.Afflictions.Affliction.AfflictionType.Numb, out Peak.Afflictions.Affliction numbness);
            yield return new WaitForSeconds(A22_Episodes.BurstDelay - 0.5f);
            camera = ClearViewCamera(local, 5f);
            for (float until = Time.time + 1f; Time.time < until && A22_Episodes.Bursts == bursts;)
            {
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            RenderView(camera, "a22-spore-burst");
            yield return new WaitForSeconds(0.5f);
            RenderView(camera, "a22-spore-burst-2");
            Destroy(camera);
            camera = null;
            Check("spores bring 20 s of numbness, then the game's spore burst 3 s later (22)", numb && Mathf.Abs(numbness.totalTime - A22_Episodes.NumbSeconds) < 0.01f && A22_Episodes.Bursts == bursts + 1 && A22_Episodes.BurstMadeOf.Contains("ParticleSystem"),
                $"numb {numb} for {(numb ? numbness.totalTime : 0f):0} s, bursts {A22_Episodes.Bursts - bursts}; the burst made of {A22_Episodes.BurstMadeOf}");
            Afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.Numb);

            // Cold: knocked down shivering for 5 s.
            int knockdowns = A22_Episodes.ColdKnockdowns;
            yield return new WaitForSeconds(4f);
            A22_Episodes.StartCold(local);
            yield return new WaitForSeconds(0.8f);
            camera = ClearViewCamera(local, 3f);
            Transform hip = local.GetBodypart(BodypartType.Hip).transform;
            Transform chest = local.GetBodypart(BodypartType.Torso).transform;
            Vector3 hip0 = hip.position;
            Vector3 chest0 = chest.position - hip.position;
            float trembling = 0f;
            float drift = 0f;
            for (float until = Time.time + 1.5f; Time.time < until;)
            {
                yield return null;
                trembling = Mathf.Max(trembling, Vector3.Distance(chest.position - hip.position, chest0));
                drift = Mathf.Max(drift, Vector3.Distance(hip.position, hip0));
            }
            RenderView(camera, "a22-cold-shiver");
            bool down = local.data.fallSeconds > 0f;
            Check("from 70 cold the scout is knocked down for 5 s, shivering in place (22)", A22_Episodes.ColdKnockdowns == knockdowns + 1 && down && trembling > 0.01f && drift < 0.5f,
                $"knocked down {down} ({local.data.fallSeconds:0.0} s left), the chest trembling by up to {trembling * 100f:0.#} cm, the body shifted {drift * 100f:0.#} cm");
            yield return new WaitForSeconds(3.5f);
        }
        finally
        {
            if (camera != null)
            {
                Destroy(camera);
            }
            ClearStatuses(Afflictions);
            calm();
            Protect(true);
        }
    }

    /// <summary>What a test camera sees, rendered straight into BepInEx/UltimateAscent.SmokeTest.NAME.png (whatever the screen shows).</summary>
    private void RenderView(GameObject holder, string name)
    {
        Camera camera = holder != null ? holder.GetComponent<Camera>() : null;
        if (camera == null)
        {
            return;
        }
        RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = previousTarget;
        RenderTexture.ReleaseTemporary(target);
        string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "UltimateAscent.SmokeTest." + name + ".png");
        System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        Destroy(image);
        Info("picture", System.IO.Path.GetFileName(path));
    }

    /// <summary>A camera beside the scout with a clear view of them: the first side (right, left, front, back) with nothing in between.</summary>
    private static GameObject ClearViewCamera(Character local, float distance)
    {
        Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 target = local.Center;
        Vector3 spot = target + right * distance + Vector3.up * 0.4f;
        foreach (Vector3 side in new[] { right, -right, forward, -forward })
        {
            Vector3 candidate = target + side * distance + Vector3.up * 0.4f;
            if (!Physics.Linecast(target, candidate, HelperFunctions.terrainMapMask))
            {
                spot = candidate;
                break;
            }
        }
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        Camera camera = holder.AddComponent<Camera>();
        camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
        camera.fieldOfView = 50f;
        camera.nearClipPlane = 0.05f;
        holder.transform.position = spot;
        holder.transform.LookAt(target);
        return holder;
    }

    /// <summary>
    /// The Caldera's last stretch ablaze (17): from the Caldera's statue to the campfire that opens the Kiln, eruptions
    /// at most 6 s apart while a scout is on it, none within 20 m of the campfire or the statue, spread over the
    /// stretch (each away from the last few: simulated over 100 of them, about 7 minutes). Pictured from above (the
    /// open ground it erupts on, the clear circles, the eruptions seen) and from beside the scout.
    /// </summary>
    private IEnumerator CalderaStretchCheck()
    {
        if (!Rules.Enabled(Lvl.Hazards))
        {
            Info("the Caldera stretch check is skipped", $"testing ascent {target}");
            yield break;
        }
        if (!Map.InVolcanoCaldera)
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(8f);
        }
        if (!Map.InVolcanoCaldera)
        {
            Info("the Caldera stretch check is skipped", $"this map has no Caldera (place {Map.CurrentPlace})");
            yield break;
        }
        Character local = Local;
        bool built = CalderaPathEruptions.Build();
        IReadOnlyList<Vector3> ground = CalderaPathEruptions.Ground;
        Vector3 campfire = CalderaPathEruptions.Campfire;
        Vector3 statue = CalderaPathEruptions.Statue;
        int all = ground.Count + CalderaPathEruptions.Cleared;
        Check("the Caldera's last stretch is found: from its statue to the campfire that opens the Kiln (17)", built && CalderaPathEruptions.HasStatue && ground.Count > 100,
            $"statue at {statue}, campfire at {campfire}, z {CalderaPathEruptions.FromZ:0} to {CalderaPathEruptions.ToZ:0}; {ground.Count} spots of open ground to erupt on (3 m apart, about {ground.Count * 9f:0} m²), {CalderaPathEruptions.Cleared} kept clear");
        if (!built || ground.Count == 0)
        {
            yield break;
        }

        // Spread: 100 eruptions picked the way the host picks them; how much of the stretch's open ground came within 10 m of one.
        System.Reflection.MethodInfo pick = AccessTools.Method(typeof(CalderaPathEruptions), "Pick");
        List<Vector3> picks = new List<Vector3>();
        for (int i = 0; i < 100; i++)
        {
            picks.Add((Vector3)pick.Invoke(null, null));
        }
        int reached = ground.Count(g => picks.Any(e => Flat(e, g) <= 10f));
        float share = all > 0 ? reached / (float)all : 0f;
        float reachable = all > 0 ? ground.Count / (float)all : 0f;
        Check("eruptions spread over the stretch: after 100 (about 7 minutes) at least 80% of its open ground has been within 10 m of one (17)", share >= 0.8f && reachable >= 0.8f,
            $"{share * 100f:0}% reached by 100 eruptions; {reachable * 100f:0}% can be reached at all (the rest within 20 m of the campfire and the statue)");

        // On the stretch, halfway: watch 25 s of eruptions.
        Vector3 middle = Vector3.Lerp(statue, campfire, 0.5f);
        Vector3 standAt = ground.OrderBy(g => Flat(g, middle)).First();
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standAt + Vector3.up * 1.5f, false);
        yield return new WaitForSeconds(2f);
        int before = EruptionsSeen.All.Count;
        int spawnedBefore = CalderaPathEruptions.Spawned;
        float watchFrom = Time.time;
        CalderaPathEruptions.LongestWait = 0f;
        GameObject side = null;
        for (float until = Time.time + 25f; Time.time < until;)
        {
            yield return null;
            if (side == null && EruptionsSeen.All.Count > before && Time.time - EruptionsSeen.All[EruptionsSeen.All.Count - 1].at > 1.5f
                && CalderaPathEruptions.OnTheStretch(EruptionsSeen.All[EruptionsSeen.All.Count - 1].where))
            {
                Vector3 fire = EruptionsSeen.All[EruptionsSeen.All.Count - 1].where;
                side = new GameObject("UltimateAscentTestCamera");
                Camera sideCamera = side.AddComponent<Camera>();
                sideCamera.fieldOfView = 60f;
                sideCamera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
                Vector3 away = (local.Center - fire).normalized;
                away.y = 0f;
                side.transform.position = fire + away.normalized * 35f + Vector3.up * 12f;
                side.transform.LookAt(fire + Vector3.up * 12f);
                RenderView(side, "caldera-stretch-eruption");
            }
        }
        if (side != null)
        {
            Destroy(side);
        }
        List<(float at, Vector3 where)> seen = EruptionsSeen.All.Skip(before).ToList();
        List<(float at, Vector3 where)> onStretch = seen.Where(e => e.where.z >= CalderaPathEruptions.FromZ - 1f && e.where.z <= CalderaPathEruptions.ToZ + 1f && Mathf.Abs(e.where.y - standAt.y) < 60f).ToList();
        List<float> times = new List<float> { watchFrom };
        times.AddRange(onStretch.Select(e => e.at));
        times.Add(Time.time);
        float longest = times.Zip(times.Skip(1), (a, b) => b - a).Max();
        Check("on the stretch an eruption breaks out at least every 6 s (17)", onStretch.Count >= 4 && longest <= CalderaPathEruptions.EveryTo + 0.3f,
            $"{onStretch.Count} eruptions on the stretch in 25 s (at {string.Join(", ", onStretch.Select(e => $"{e.at - watchFrom:0.0}"))} s), the longest wait {longest:0.0} s; the host sent {CalderaPathEruptions.Spawned - spawnedBefore}; the scout at {local.Center}, on the stretch {CalderaPathEruptions.OnTheStretch(local.Center)}");
        float nearestCampfire = seen.Count > 0 ? seen.Min(e => Flat(e.where, campfire)) : float.MaxValue;
        float nearestStatue = seen.Count > 0 ? seen.Min(e => Flat(e.where, statue)) : float.MaxValue;
        Check("no eruption breaks out within 20 m of the campfire or the statue (17)", nearestCampfire >= CalderaPathEruptions.KeepClear && nearestStatue >= CalderaPathEruptions.KeepClear,
            $"the nearest {nearestCampfire:0} m from the campfire, {nearestStatue:0} m from the statue ({seen.Count} eruptions in all)");

        // From above, below the volcano's smoke: the open ground (orange), the clear circles (blue), the eruptions seen (red).
        Vector3 centre = Vector3.Lerp(statue, campfire, 0.5f);
        float half = Mathf.Max(Mathf.Abs(campfire.z - statue.z) * 0.5f + 40f, 140f);
        const int Size = 1400;
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        Camera camera = holder.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = half;
        camera.aspect = 1f;
        float above = 500f;
        camera.nearClipPlane = above - (Mathf.Max(campfire.y, statue.y) + 30f - centre.y) - 0f;
        camera.farClipPlane = above + 300f;
        holder.transform.position = new Vector3(centre.x, centre.y + above, centre.z);
        holder.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        RenderTexture texture = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        Texture2D image = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        RenderTexture.active = previous;
        camera.targetTexture = null;
        RenderTexture.ReleaseTemporary(texture);
        Destroy(holder);
        float perMetre = Size / (half * 2f);
        void Dot(Vector3 at, float radius, Color colour, float alpha)
        {
            int cx = Mathf.RoundToInt((at.x - centre.x) * perMetre + Size / 2f);
            int cy = Mathf.RoundToInt((at.z - centre.z) * perMetre + Size / 2f);
            int r = Mathf.Max(1, Mathf.RoundToInt(radius * perMetre));
            for (int y = cy - r; y <= cy + r; y++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= Size || y >= Size || (x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r)
                    {
                        continue;
                    }
                    image.SetPixel(x, y, Color.Lerp(image.GetPixel(x, y), colour, alpha));
                }
            }
        }
        void Ring(Vector3 at, float radius, Color colour)
        {
            for (int a = 0; a < 720; a++)
            {
                Vector3 point = at + Quaternion.Euler(0f, a * 0.5f, 0f) * Vector3.forward * radius;
                Dot(point, 0.4f, colour, 1f);
            }
        }
        foreach (Vector3 g in ground)
        {
            Dot(g, 0.9f, new Color(1f, 0.55f, 0.1f), 0.35f);
        }
        Ring(campfire, CalderaPathEruptions.KeepClear, new Color(0.2f, 0.6f, 1f));
        Ring(statue, CalderaPathEruptions.KeepClear, new Color(0.2f, 0.9f, 1f));
        Dot(campfire, 2f, new Color(0.2f, 0.6f, 1f), 1f);
        Dot(statue, 2f, new Color(0.2f, 0.9f, 1f), 1f);
        foreach ((float at, Vector3 where) in seen)
        {
            Dot(where, 2.5f, Color.red, 1f);
        }
        Dot(standAt, 1.5f, Color.white, 1f);
        image.Apply();
        string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "UltimateAscent.SmokeTest.caldera-stretch-above.png");
        System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        Destroy(image);
        Info("picture", $"caldera-stretch-above: {half * 2f:0} m across, north (towards the Kiln) up");
    }

    /// <summary>
    /// An eruption burns (17): one set off 4 m from the scout on the Caldera's last stretch, then one in the Kiln; the
    /// heat each tries to put on the scout over 2 s is recorded (the statuses stay locked, so nothing lands).
    /// </summary>
    private IEnumerator EruptionHeatCheck()
    {
        if (!Rules.Enabled(Lvl.Hazards))
        {
            Info("the eruption heat check is skipped", $"testing ascent {target}");
            yield break;
        }
        foreach (Segment segment in new[] { Segment.Caldera, Segment.TheKiln })
        {
            MapHandler.JumpToSegment(segment);
            yield return new WaitForSeconds(6f);
            if (!Map.InVolcano)
            {
                Info("the eruption heat check is skipped", $"no Caldera and Kiln here ({Map.CurrentPlace})");
                yield break;
            }
            Character local = Local;
            if (segment == Segment.Caldera && CalderaPathEruptions.Build() && CalderaPathEruptions.Ground.Count > 0)
            {
                Vector3 middle = Vector3.Lerp(CalderaPathEruptions.Statue, CalderaPathEruptions.Campfire, 0.5f);
                Vector3 standAt = CalderaPathEruptions.Ground.OrderBy(g => Flat(g, middle)).First();
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standAt + Vector3.up * 1.5f, false);
                yield return new WaitForSeconds(2f);
            }
            EruptionSpawner spawner = FindObjectsByType<EruptionSpawner>(FindObjectsSortMode.None).FirstOrDefault();
            if (spawner == null)
            {
                Fail($"the {Map.CurrentPlace} has the game's eruption spawner");
                continue;
            }
            Vector3 at = local.Center + Vector3.ProjectOnPlane(local.data.lookDirection_Flat, Vector3.up).normalized * 4f;
            if (Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 20f, HelperFunctions.terrainMapMask))
            {
                at = ground.point;
            }
            CalderaPathEruptions.Held = true;
            yield return new WaitForSeconds(1f);
            HeatSources.Clear();
            HeatSources.On = true;
            spawner.photonView.RPC("RPCA_SpawnEruption", RpcTarget.All, at);
            yield return null;
            GameObject made = FindObjectsByType<RemoveAfterSeconds>(FindObjectsSortMode.None).Select(r => r.gameObject).Where(g => g.name.StartsWith("Eruption", StringComparison.Ordinal)).OrderBy(g => Vector3.Distance(g.transform.position, at)).FirstOrDefault();
            List<string> timeline = new List<string>();
            float lastHeat = 0f;
            List<float> perSecond = new List<float>();
            for (float t = 0f; t < 37f; t += 1f)
            {
                StatusEmitter[] emitters = made != null ? made.GetComponentsInChildren<StatusEmitter>(true) : new StatusEmitter[0];
                timeline.Add($"{t:0}s: " + string.Join(" ", emitters.Select(e => $"[{(e.gameObject.activeInHierarchy ? "on" : "off")}{(e.enabled ? "" : " disabled")} d{Vector3.Distance(local.Center, e.transform.position):0.0} r{e.radius + e.outerFade:0} {e.statusType} {e.amount:0.##}]")) + $" heat this second {(HeatSources.Totals.Values.Sum() - lastHeat) * 100f:0.#}");
                perSecond.Add(HeatSources.Totals.Values.Sum() - lastHeat);
                lastHeat = HeatSources.Totals.Values.Sum();
                yield return new WaitForSeconds(1f);
            }
            CalderaPathEruptions.Held = false;
            Info("eruption over its life", $"{(made != null ? made.name + " at " + made.transform.position + " rot " + made.transform.rotation.eulerAngles : "not found")}; " + string.Join(" | ", timeline));
            HeatSources.On = false;
            float heat = HeatSources.Totals.Where(kv => kv.Key.StartsWith("StatusEmitter", StringComparison.Ordinal) || kv.Key.Contains("BurnFrom")).Sum(kv => kv.Value);
            string sources = string.Join(", ", HeatSources.Totals.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value * 100f:0.#}"));
            float peak = perSecond.Count > 0 ? perSecond.Max() : 0f;
            Check($"an eruption burns the scout next to it, at its height 20 heat a second, in the {Map.CurrentPlace} (17)", heat > 0.2f && Mathf.Abs(peak - A17_Hazards.EruptionPeakHeat) <= 0.03f,
                $"the most in a second {peak * 100f:0.#}; over its life 4 m away: {heat * 100f:0} heat from the eruption (locked, so none landed); all heat: {sources}");
            if (segment == Segment.Caldera)
            {
                // The Caldera's own eruptions (not the stretch's): each on the ground or the lava's surface below it.
                Lava river = FindObjectsByType<Lava>(FindObjectsSortMode.None).FirstOrDefault(l => l.name == "River");
                // At the statue: inside the Caldera spawner's reach (its start, by the Alpine's campfire, is just outside).
                Vector3 byStatue = CalderaPathEruptions.Statue + Vector3.ProjectOnPlane(CalderaPathEruptions.Campfire - CalderaPathEruptions.Statue, Vector3.up).normalized * 4f + Vector3.up * 1.5f;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, byStatue, false);
                yield return new WaitForSeconds(1f);
                int before = EruptionsSeen.All.Count;
                yield return new WaitForSeconds(25f);
                List<string> heights = new List<string>();
                bool onSurface = true;
                int own = 0;
                for (int e = before; e < EruptionsSeen.All.Count; e++)
                {
                    Vector3 where = EruptionsSeen.All[e].where;
                    if (CalderaPathEruptions.OnTheStretch(where))
                    {
                        continue;
                    }
                    own++;
                    float lavaThen = EruptionsSeen.Lava[e];
                    RaycastHit under = HelperFunctions.LineCheck(where + Vector3.up * 150f, where + Vector3.down * 30f, HelperFunctions.LayerType.TerrainMap);
                    float surface = Mathf.Max(under.transform != null ? under.point.y : float.MinValue, float.IsNaN(lavaThen) ? where.y : lavaThen);
                    onSurface &= Mathf.Abs(where.y - surface) < 1.5f;
                    heights.Add($"{where.y:0} (surface {surface:0})");
                }
                Check("the Caldera's own eruptions break out on the ground or the lava's surface, not at the lava's lowest level (17)", own > 0 && onSurface,
                    $"{own} in 25 s at heights {string.Join(", ", heights)}");
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, local.Center + Vector3.up * 0.5f - Vector3.ProjectOnPlane(local.data.lookDirection_Flat, Vector3.up).normalized * 25f, false);
            yield return new WaitForSeconds(1f);
        }
    }

    /// <summary>
    /// Zombies share out the scouts (17), the host's sharing tried on made-up places: 5 zombies and 3 scouts (each scout
    /// at least one, the 2 left over to the fewest-chased, nearest); 2 zombies and 3 scouts (two different scouts); a
    /// scout 150 m away (nobody goes for them); a zombie keeps its scout over one only a little nearer, and for its first
    /// 10 s over any other.
    /// </summary>
    private void ZombieSharingCheck()
    {
        Vector3 X(float x) => new Vector3(x, 0f, 0f);
        List<Vector3> scouts = new List<Vector3> { X(0f), X(40f), X(80f) };
        List<Vector3> five = new List<Vector3> { X(2f), X(4f), X(6f), X(8f), X(10f) };
        int[] pick = ZombieTargets.Share(five, null, scouts);
        int[] counts = Enumerable.Range(0, 3).Select(i => pick.Count(p => p == i)).ToArray();
        Check("5 zombies, 3 scouts: every scout is chased, the 2 left over go to the fewest-chased (17)", counts.All(c => c >= 1) && counts.Max() - counts.Min() <= 1 && pick.All(p => p >= 0),
            $"zombies near the first scout go for scouts {string.Join(", ", pick)}; chasers per scout {string.Join("/", counts)}");
        int[] two = ZombieTargets.Share(new List<Vector3> { X(2f), X(4f) }, null, scouts);
        Check("2 zombies, 3 scouts: each goes for a different scout (17)", two[0] >= 0 && two[1] >= 0 && two[0] != two[1], $"scouts {string.Join(", ", two)}");
        int[] far = ZombieTargets.Share(new List<Vector3> { X(0f), X(1f) }, null, new List<Vector3> { X(5f), X(150f) });
        Check("a scout more than 100 m away is not hunted (17)", far.All(p => p == 0), $"scouts {string.Join(", ", far)}");
        int[] sticky = ZombieTargets.Share(new List<Vector3> { X(0f) }, new List<int> { 1 }, new List<Vector3> { X(10f), X(-11f) });
        int[] fresh = ZombieTargets.Share(new List<Vector3> { X(0f) }, null, new List<Vector3> { X(10f), X(-11f) });
        Check("a zombie keeps its scout over one only a little nearer (17)", sticky[0] == 1 && fresh[0] == 0, $"chasing the one 11 m away: keeps {sticky[0]}; chasing nobody: takes {fresh[0]}");
        int[] held = ZombieTargets.Share(new List<Vector3> { X(0f), X(1f) }, new List<int> { 1, -1 }, new List<Vector3> { X(3f), X(-60f) }, new List<bool> { true, false });
        Check("a zombie keeps the scout it was given for its first 10 s, even with a much nearer one; that scout counts as chased (17)", held[0] == 1 && held[1] == 0,
            $"the held zombie goes for {held[0]} (60 m away rather than 3 m), the other for {held[1]}");
    }

    /// <summary>
    /// Shorter reach from ascent 14: the Rope Cannon and the Anti-Rope Cannon 25 m (the game: 30); the Rescue Claw 20 m
    /// aimed level or up and 30 m aimed down (the game: 30 and 40). Each held in the hands and read back.
    /// </summary>
    private IEnumerator ShorterReachCheck()
    {
        if (!Rules.Enabled(Lvl.WorseItems))
        {
            Info("the reach check is skipped", $"testing ascent {target}");
            yield break;
        }
        foreach (string name in new[] { "RopeShooter", "RopeShooterAnti" })
        {
            yield return SpawnIntoHand(ItemByName(name));
            yield return new WaitForSeconds(0.3f);
            RopeShooter cannon = held != null ? held.GetComponent<RopeShooter>() : null;
            Check($"the {(held != null ? held.GetName() : name)} reaches 25 m from ascent 14 (14)", cannon != null && Mathf.Abs(cannon.maxLength - A14_WorseItems.RopeCannonReach) < 0.01f,
                $"reach {(cannon != null ? cannon.maxLength : -1f):0.#} m (the game: {(cannon != null ? Overrides.Original(cannon, "maxLength", -1f) : -1f):0.#})");
        }
        yield return SpawnIntoHand(ItemByName("RescueHook"));
        yield return new WaitForSeconds(0.3f);
        RescueHook claw = held != null ? held.GetComponent<RescueHook>() : null;
        Check("the Rescue Claw reaches 20 m aimed level or up, 30 m aimed down, from ascent 14 (14)",
            claw != null && Mathf.Abs(claw.range - A14_WorseItems.RescueClawReach) < 0.01f && Mathf.Abs(claw.rangeDownward - A14_WorseItems.RescueClawReachDown) < 0.01f,
            claw != null ? $"level or up {claw.range:0.#} m, down {claw.rangeDownward:0.#} m (the game: {Overrides.Original(claw, "range", -1f):0.#} and {Overrides.Original(claw, "rangeDownward", -1f):0.#})" : "no claw in the hands");
    }

    /// <summary>
    /// What the Energy Drink and the Toxic Drink take off and the boost they give, drunk the game's way with 90 heat
    /// and 90 drowsiness, with ascent 14's rule on and switched off (in memory). The Energy Drink: 30 heat (from 14,
    /// 60% of it), all the drowsiness, the game's boosts (walking +50%, climbing +150%) on every ascent. The Toxic
    /// Drink: 60 heat (from 14, 60% of it), all the drowsiness, from 14 at most 80. Heat lands in 2.5 steps.
    /// </summary>
    private IEnumerator DrinkNumbers()
    {
        Character local = Local;
        BepInEx.Configuration.ConfigEntry<bool> worse = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool saved = worse.Value;
        Protect(false);
        Action calm = CalmForMeasuring();
        try
        {
            foreach (bool rule in new[] { true, false })
            {
                worse.Value = rule && saved;
                HostOptions.Invalidate();
                foreach (Item drink in new[] { ItemByName("Energy Drink"), ToxicDrink.Template })
                {
                    if (drink == null)
                    {
                        continue;
                    }
                    yield return SpawnIntoHand(drink);
                    if (held == null)
                    {
                        continue;
                    }
                    ClearStatuses(Afflictions);
                    foreach (Peak.Afflictions.Affliction old in Afflictions.afflictionList.ToList())
                    {
                        Afflictions.RemoveAffliction(old.GetAfflictionType());
                    }
                    Afflictions.SetStatus(Status.Hot, 0.9f);
                    Afflictions.SetStatus(Status.Drowsy, 0.9f);
                    yield return new WaitForSeconds(0.3f);
                    float hot = Afflictions.GetCurrentStatus(Status.Hot);
                    float drowsy = Afflictions.GetCurrentStatus(Status.Drowsy);
                    yield return EatHeld();
                    yield return new WaitForSeconds(0.5f);
                    float hotTaken = hot - Afflictions.GetCurrentStatus(Status.Hot);
                    float drowsyTaken = drowsy - Afflictions.GetCurrentStatus(Status.Drowsy);
                    Peak.Afflictions.Affliction_FasterBoi boost = Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_FasterBoi>().FirstOrDefault();
                    bool on = rule && saved;
                    bool toxic = drink == ToxicDrink.Template;
                    float heatWanted = (toxic ? ToxicDrink.Cooling : 0.3f) * (on ? A14_WorseItems.CureShare : 1f);
                    float drowsyWanted = toxic && on ? ToxicDrink.WakesWorse : drowsy;
                    bool boostRight = toxic || (boost != null && Mathf.Abs(boost.moveSpeedMod - 0.5f) < 0.01f && Mathf.Abs(boost.climbSpeedMod - 1.5f) < 0.01f);
                    Check($"the {(toxic ? "Toxic Drink" : "Energy Drink")} takes off {heatWanted * 100f:0} heat and {(drowsyWanted >= drowsy ? "all the drowsiness" : $"{drowsyWanted * 100f:0} drowsiness")}"
                        + (toxic ? "" : ", and walks +50% and climbs +150%") + $", ascent 14's rule {(on ? "on" : "off")} (14)",
                        Mathf.Abs(hotTaken - heatWanted) <= 0.026f && Mathf.Abs(drowsyTaken - drowsyWanted) <= 0.011f && boostRight,
                        $"took {hotTaken * 100f:0.#} heat, {drowsyTaken * 100f:0.#} drowsiness" + (boost != null ? $"; walking +{boost.moveSpeedMod * 100f:0}%, climbing +{boost.climbSpeedMod * 100f:0}%" : ""));
                    Info($"{drink.GetName()} drunk, ascent 14's rule {(rule && saved ? "on" : "off")} (climb on ascent {target})",
                        $"heat {hot * 100f:0} -> {Afflictions.GetCurrentStatus(Status.Hot) * 100f:0} (took {hotTaken * 100f:0.#}), drowsiness {drowsy * 100f:0} -> {Afflictions.GetCurrentStatus(Status.Drowsy) * 100f:0} (took {drowsyTaken * 100f:0.#}); "
                        + (boost != null ? $"boost walking +{boost.moveSpeedMod * 100f:0}%, climbing +{boost.climbSpeedMod * 100f:0}%, {boost.totalTime:0.#} s, drowsiness at its end {boost.drowsyOnEnd * 100f:0.#}" : "no speed boost"));
                    ClearStatuses(Afflictions);
                    foreach (Peak.Afflictions.Affliction left in Afflictions.afflictionList.ToList())
                    {
                        Afflictions.RemoveAffliction(left.GetAfflictionType());
                    }
                    yield return new WaitForSeconds(0.5f);
                }
            }
        }
        finally
        {
            worse.Value = saved;
            HostOptions.Invalidate();
            ClearStatuses(Afflictions);
            calm();
            Protect(true);
        }
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }

    /// <summary>
    /// Ascent 25 in the Caldera and the Gloom: a storm brings neither cold nor heat (the snowstorm blowing on the scout,
    /// day and night), and the wind and the snowstorm never blow together (whichever began later waits).
    /// </summary>
    private IEnumerator CalderaStormsCheck()
    {
        if (!Rules.Enabled(Lvl.AllWeather))
        {
            Info("the Caldera storm check is skipped", $"testing ascent {target}");
            yield break;
        }
        if (Map.CurrentPlace != Place.Caldera && Map.CurrentPlace != Place.Gloom)
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(8f);
        }
        WindChillZone[] zones = FindObjectsByType<WindChillZone>(FindObjectsSortMode.None);
        WindChillZone snow = zones.FirstOrDefault(z => z.name == "SnowStorm");
        WindChillZone wind = zones.FirstOrDefault(z => z.name == "WindStorm");
        if (snow == null || wind == null)
        {
            Fail("the Caldera has ascent 25's wind and snowstorm", $"snow {snow != null}, wind {wind != null}, place {Map.CurrentPlace}");
            yield break;
        }
        Character local = Local;
        Protect(false);
        Action calm = CalmForMeasuring();
        try
        {
            // The snowstorm blowing on the scout for 3 s, as at its height.
            System.Reflection.MethodInfo apply = AccessTools.Method(typeof(WindChillZone), "ApplyStatus");
            ClearStatuses(Afflictions);
            float cold0 = Afflictions.GetCurrentStatus(Status.Cold);
            float hot0 = Afflictions.GetCurrentStatus(Status.Hot);
            for (float until = Time.time + 3f; Time.time < until;)
            {
                snow.hasBeenActiveFor = 10f;
                snow.currentForceMult = 1f;
                apply.Invoke(snow, new object[] { local });
                yield return null;
            }
            float cold = Afflictions.GetCurrentStatus(Status.Cold) - cold0;
            float hot = Afflictions.GetCurrentStatus(Status.Hot) - hot0;
            Check($"ascent 25: in the {Map.CurrentPlace} a storm brings neither cold nor heat (25)", cold < 0.001f && hot < 0.001f,
                $"3 s in the snowstorm: cold +{cold * 100f:0.##}, heat +{hot * 100f:0.##}");
        }
        finally
        {
            ClearStatuses(Afflictions);
            calm();
            Protect(true);
        }

        // Both set blowing, the wind 5 s first: the snowstorm waits, the wind keeps blowing.
        Type phases = typeof(UltimateAscent.Features.Weather.WeatherPhases);
        Type phaseType = AccessTools.Inner(phases, "Phase");
        object Phase(double startedAgo)
        {
            object phase = AccessTools.CreateInstance(phaseType);
            Traverse t = Traverse.Create(phase);
            t.Field("run").SetValue(Net.RunKey);
            t.Field("clock").SetValue(Net.Clock - startedAgo);
            t.Field("duration").SetValue(60f);
            t.Field("active").SetValue(true);
            t.Field("direction").SetValue(1);
            return phase;
        }
        Traverse.Create(phases).Field("wind").Field("state").SetValue(Phase(5.0));
        Traverse.Create(phases).Field("snow").Field("state").SetValue(Phase(0.0));
        UltimateAscent.Features.Weather.WeatherPhases.Apply(wind);
        UltimateAscent.Features.Weather.WeatherPhases.Apply(snow);
        bool windBlows = wind.windActive;
        bool snowBlows = snow.windActive;
        Check("ascent 25: in the Caldera and the Gloom the wind and the snowstorm never blow together; the later one waits (25)", windBlows && !snowBlows,
            $"wind (began 5 s ago) blowing {windBlows}, snowstorm (began now) blowing {snowBlows}");
        UltimateAscent.Features.Weather.WeatherPhases.Reset();
        yield return new WaitForSeconds(1f);
    }

    /// <summary>The held item lit the game's way (the use button held until the cast is done).</summary>
    private IEnumerator LightHeld(Item item)
    {
        item.StartUsePrimary();
        for (float until = Time.realtimeSinceStartup + 3f; Time.realtimeSinceStartup < until && item != null && !item.finishedCast;)
        {
            item.ContinueUsePrimary();
            yield return null;
        }
        if (item != null)
        {
            item.CancelUsePrimary();
        }
        yield return new WaitForSeconds(0.8f);
    }

    /// <summary>To a map's Gloom (its third part), if this map has one.</summary>
    private IEnumerator ToTheGloom()
    {
        if (!Map.InGloom)
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(8f);
        }
    }

    /// <summary>
    /// Pandora's Lunchbox (14): its crab is rolled at half the share, as the game rolls its curse. The lunchbox's own
    /// effect is given 1000 times; crab and curse come out about even (the full share gave about twice the curse).
    /// </summary>
    private IEnumerator PandoraCrabCheck()
    {
        if (!Rules.Enabled(Lvl.WorseItems))
        {
            Info("the Pandora check is skipped", $"testing ascent {target}");
            yield break;
        }
        Item pandora = ItemByName("PandorasBox");
        Peak.Afflictions.Affliction_Chaos chaos = pandora != null ? pandora.GetComponents<Action_ApplyAffliction>().Select(a => a.affliction).OfType<Peak.Afflictions.Affliction_Chaos>().FirstOrDefault() : null;
        if (chaos == null)
        {
            Fail("Pandora's Lunchbox and its effect are in the item database");
            yield break;
        }
        // Each roll's amount varies a lot: 1000 of them, so half the share (about 1) stays clear of the full share (about 2).
        const int Lunchboxes = 1000;
        Action undo = CalmForMeasuring();
        float crab = 0f;
        float curse = 0f;
        int crabRolls = 0;
        try
        {
            Unlocked(() =>
            {
                for (int i = 0; i < Lunchboxes; i++)
                {
                    // Crab stays through the lunchbox's own clear (ascent 14): each one counted from nothing.
                    Afflictions.SetStatus(Status.Crab, 0f);
                    Afflictions.SetStatus(Status.Curse, 0f);
                    Afflictions.AddAffliction(chaos);
                    float c = Afflictions.GetCurrentStatus(Status.Crab);
                    crab += c;
                    curse += Afflictions.GetCurrentStatus(Status.Curse);
                    crabRolls += c > 0f ? 1 : 0;
                }
                ClearStatuses(Afflictions);
                Afflictions.SetStatus(Status.Crab, 0f);
                Afflictions.SetStatus(Status.Curse, 0f);
            });
        }
        finally
        {
            undo();
        }
        float ratio = curse > 0f ? crab / curse : 0f;
        Check("Pandora's Lunchbox gives crab at half the share, like its curse (14)", crabRolls > 100 && ratio > 0.7f && ratio < 1.5f,
            $"{Lunchboxes} lunchboxes: crab in {crabRolls}, {crab * 100f / Lunchboxes:0.0} crab and {curse * 100f / Lunchboxes:0.0} curse on average (crab/curse {ratio:0.00}; the full share gave about 2)");
        yield return null;
    }

    /// <summary>
    /// The Gloom's ghost and a burning Torch (9, 17): the ghost comes for the scout holding a lit Torch, is pushed
    /// back and never arms. Then its burst, set off far from anyone: 50 s of blindness, 60 cold.
    /// </summary>
    private IEnumerator GhostAndTorchCheck()
    {
        if (Rules.Level < Lvl.Hazards)
        {
            Info("the ghost checks are skipped", $"testing ascent {target}");
            yield break;
        }
        yield return ToTheGloom();
        Peak.GhostBallSpawner spawner = Peak.GhostBallSpawner.Instance;
        if (!Map.InGloom || spawner == null || TorchItem.Template == null)
        {
            Info("the ghost checks are skipped", $"no Gloom on this map (place {Map.CurrentPlace}); run with -Biome AS");
            yield break;
        }
        Character local = Local;
        Vector2 look = local.data.lookValues;
        GameObject made = null;
        try
        {
            yield return SpawnIntoHand(TorchItem.Template);
            Item torch = held;
            if (torch == null)
            {
                yield break;
            }
            yield return LightHeld(torch);
            Lantern flame = torch.GetComponent<Lantern>();
            Check("the Torch burns in the scout's hands", flame != null && flame.lit, $"lit {flame != null && flame.lit}");
            if (spawner.currentGhostBall == null)
            {
                spawner.ForceSpawnGhost();
            }
            for (float until = Time.time + 6f; Time.time < until && spawner.currentGhostBall == null;)
            {
                yield return null;
            }
            if (spawner.currentGhostBall == null)
            {
                // None came by itself (no scout where it hunts): one brought in ahead of the scout, the game's ghost.
                Vector3 ahead = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
                GameObject brought = PhotonNetwork.Instantiate(spawner.ghostBallPrefab.name, local.Center + ahead * 18f + Vector3.up * 3f, Quaternion.identity, 0);
                spawner.currentGhostBall = brought != null ? brought.GetComponent<Peak.GhostBall>() : null;
                Info("a ghost brought in for the check", $"none came by itself in 6 s (valid target {spawner.ValidTargetExists()})");
            }
            Peak.GhostBall ghost = spawner.currentGhostBall;
            if (ghost == null)
            {
                Info("the ghost check is skipped", "no ghost came within 12 s");
                yield break;
            }
            made = ghost.gameObject;
            float closest = float.MaxValue;
            bool armed = false;
            bool pushed = false;
            bool targeted = false;
            int shots = 0;
            for (float until = Time.time + 18f; Time.time < until && ghost != null;)
            {
                if (flame != null && flame.fuel < 10f)
                {
                    flame.fuel = 40f;
                }
                if (!ghost.risingFromSpawn)
                {
                    targeted |= ghost.target == local;
                    closest = Mathf.Min(closest, Vector3.Distance(ghost.transform.position, torch.transform.position));
                    armed |= ghost.readyToExplode;
                    pushed |= ghost.burnTick > 0f;
                }
                local.data.lookValues = HelperFunctions.DirectionToLook(ghost.transform.position - MainCamera.instance.transform.position);
                if (shots < 2 && Time.time > until - 18f + 6f * (shots + 1))
                {
                    Screenshot("ghost-and-torch-" + shots);
                    shots++;
                }
                yield return null;
            }
            Check("a ghost hunting the scout with a burning Torch is pushed back and never arms (9, 17)",
                targeted && pushed && !armed && closest > TorchItem.GhostReach,
                $"hunting the scout {targeted}, pushed {pushed}, armed {armed}, came within {closest:0.0} m of the Torch (it keeps safe {TorchItem.GhostReach} m around it)");
            if (made != null)
            {
                PhotonNetwork.Destroy(made);
                made = null;
            }
            yield return PutAway(TorchItem.Template);

            // The ghost's burst, set off 200 m below everyone: what it would do.
            GameObject burst = Instantiate(ghost != null ? ghost.explosionPrefab : spawner.ghostBallPrefab.explosionPrefab, local.Center + Vector3.down * 200f, Quaternion.identity);
            yield return null;
            yield return null;
            AOE blind = burst != null ? burst.GetComponentsInChildren<AOE>(true).FirstOrDefault(a => string.Equals(a.illegalStatus, "BLIND", StringComparison.OrdinalIgnoreCase)) : null;
            int cold = blind != null && blind.addtlStatus != null ? Array.IndexOf(blind.addtlStatus, Status.Cold) : -1;
            Check("the ghost's burst blinds for 50 s and chills for 60 (17)",
                blind != null && Mathf.Abs(blind.statusAmount - 50f) < 0.01f && cold >= 0 && blind.addlStatusAmountOverrides != null && Mathf.Abs(blind.addlStatusAmountOverrides[cold] - 0.6f) < 0.001f,
                blind == null ? "no blinding burst" : $"blind {blind.statusAmount:0.#} s, cold {(cold >= 0 && blind.addlStatusAmountOverrides != null && blind.addlStatusAmountOverrides.Count > cold ? blind.addlStatusAmountOverrides[cold] * 100f : -1f):0.#}");
            if (burst != null)
            {
                Destroy(burst);
            }
            Step("the explosions that knock you out", () =>
            {
                string Burst(string item) => ItemByName(item)?.GetComponent<ItemCooking>()?.explosionPrefab?.name ?? "none";
                string lantern = Burst("Lantern");
                string faerie = Burst("Lantern_Faerie");
                string ghostBurst = spawner.ghostBallPrefab.explosionPrefab.name;
                bool Out(string name) => A17_Hazards.KnockOutBlasts.Any(n => name.StartsWith(n, StringComparison.Ordinal));
                Check("only a cooked Lantern's or Faerie Lantern's burst and the ghost's burst knock you out; dynamite and the rest only knock you down (17)",
                    Out(lantern) && Out(faerie) && Out(ghostBurst) && !Out("VFX_DynamiteExplosion(Clone)") && !Out("VFX_FireworkExplosion") && !Out("VFX_SporeExploExplo") && !Out("Explosion") && !Out("Hitbox"),
                    $"knock out: {string.Join(", ", A17_Hazards.KnockOutBlasts)}; the Lantern's burst '{lantern}', the Faerie Lantern's '{faerie}', the ghost's '{ghostBurst}'");
            });
        }
        finally
        {
            if (made != null)
            {
                PhotonNetwork.Destroy(made);
            }
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// The Gloom's ravens (17): an Early Worm put down in front of the scout, in view, brings a raven (every time, for
    /// the test) that flies in, lands beside it, eats it and leaves. A second worm, picked up while the raven comes,
    /// is not eaten.
    /// </summary>
    private IEnumerator RavensCheck()
    {
        if (Rules.Level < Lvl.Hazards)
        {
            Info("the raven checks are skipped", $"testing ascent {target}");
            yield break;
        }
        yield return ToTheGloom();
        Item wormItem = ItemByName("EarlyWorm");
        if (!Map.InGloom || wormItem == null)
        {
            Info("the raven checks are skipped", $"no Gloom on this map (place {Map.CurrentPlace}); run with -Biome AS");
            yield break;
        }
        Character local = Local;
        Vector2 look = local.data.lookValues;
        GloomCrows.AlwaysForTests = true;
        try
        {
            foreach (bool pickUp in new[] { false, true })
            {
                local.refs.items.EquipSlot(Optionable<byte>.None);
                Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
                GameObject placed = SpawnInFront(wormItem, 4f);
                Item worm = placed != null ? placed.GetComponent<Item>() : null;
                if (worm == null)
                {
                    Fail("an Early Worm put down in front of the scout");
                    yield break;
                }
                yield return new WaitForSeconds(1.5f);
                local.data.lookValues = HelperFunctions.DirectionToLook(worm.transform.position - MainCamera.instance.transform.position);
                int eaten = GloomCrows.Eaten;
                int heardEating = GloomCrows.EatingHeard;
                GloomCrows.CrowFlight crow = null;
                for (float until = Time.time + 4f; Time.time < until && crow == null;)
                {
                    local.data.lookValues = HelperFunctions.DirectionToLook(worm.transform.position - MainCamera.instance.transform.position);
                    crow = GloomCrows.Flying.LastOrDefault();
                    yield return null;
                }
                if (crow == null)
                {
                    Fail(pickUp ? "a raven comes for the second worm" : "a raven comes for an Early Worm in view (17)", $"none within 4 s; seen {GloomCrows.Sees(MainCamera.instance.transform, worm.transform.position)}");
                    yield break;
                }
                Vector3 from = crow.transform.position;
                if (pickUp)
                {
                    yield return new WaitForSeconds(0.5f);
                    Step("pick the worm up before the raven lands", () => worm.Interact(local));
                }
                else
                {
                    yield return new WaitForSeconds(0.8f);
                    Screenshot("raven-flying-in");
                }
                for (float until = Time.time + 8f; Time.time < until && crow != null && !crow.Landed;)
                {
                    yield return null;
                }
                if (!pickUp && crow != null)
                {
                    yield return new WaitForSeconds(0.3f);
                    Screenshot("raven-landed");
                }
                for (float until = Time.time + 4f; Time.time < until && crow != null && !crow.Leaving;)
                {
                    yield return null;
                }
                bool ate = crow != null && crow.Ate;
                if (!pickUp)
                {
                    yield return new WaitForSeconds(0.6f);
                    Screenshot("raven-leaving");
                    float eatenAway = Vector3.Distance(MainCamera.instance.transform.position, GloomCrows.LastEatingAt);
                    Check("a raven flies in to an Early Worm in view, lands beside it, eats it (with the sound of a scout finishing food) and flies off (17)",
                        crow != null && ate && GloomCrows.Eaten == eaten + 1 && placed == null && Vector3.Distance(from, crow.transform.position) > 5f && GloomCrows.EatingHeard == heardEating + 1,
                        $"came from {Vector3.Distance(from, local.Center):0} m away, ate it {ate}, worm gone {placed == null}, eaten {GloomCrows.Eaten - eaten}, eating heard {GloomCrows.EatingHeard - heardEating}, {eatenAway:0.0} m from the camera");
                    // How loud the eating is where the scout hears it: at the raven's distance, next to the game's coconut bonk there.
                    yield return new WaitForSeconds(2f);
                    Vector3 spot = MainCamera.instance.transform.position + (GloomCrows.LastEatingAt - MainCamera.instance.transform.position).normalized * Mathf.Max(1f, eatenAway);
                    float[] eatingLevels = null;
                    float[] bonkLevels = null;
                    string eatingLine = "";
                    string bonkLine = "";
                    Item coconut = ItemByName("Item_Coconut");
                    SFX_Instance bonk = coconut != null && coconut.TryGetComponent(out Bonkable bonkable) ? bonkable.bonk.FirstOrDefault(b => b != null) : null;
                    yield return MeasureSound("the raven eating", GloomCrows.EatingSfx(), spot, (levels, line) => { eatingLevels = levels; eatingLine = line + $" (played at {GloomCrows.EatingVolume:0.##})"; }, GloomCrows.PlayEating);
                    yield return MeasureSound("the coconut bonk there", bonk, spot, (levels, line) => { bonkLevels = levels; bonkLine = line; });
                    Check("the raven's eating is heard where it eats over the Gloom's own sound: at least as loud as the Coconut's bonk there (recorded)",
                        eatingLevels != null && bonkLevels != null && eatingLevels[1] >= bonkLevels[1] - 1.5f,
                        eatingLine + "; " + bonkLine);
                }
                else
                {
                    Item inHand = local.data.currentItem;
                    Check("an Early Worm picked up before the raven lands is not eaten (17)",
                        !ate && GloomCrows.Eaten == eaten && inHand != null && inHand.itemID == wormItem.itemID,
                        $"eaten {ate}, in the hand {(inHand != null ? inHand.name : "nothing")}");
                    yield return PutAway(wormItem);
                }
                yield return new WaitForSeconds(3f);
            }
        }
        finally
        {
            GloomCrows.AlwaysForTests = false;
            local.data.lookValues = look;
        }
    }

    /// <summary>
    /// The Citadel's ladders (17): the scout grabs one and holds on; the climb works with 1.2 times rock's speed and the
    /// stamina it spends is 0.9 of rock's, against the game's 3 and 0.25 (ascent 17's rule off in memory).
    /// </summary>
    private IEnumerator LaddersCheck()
    {
        if (Rules.Level < Lvl.Hazards)
        {
            Info("the ladder checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        ClimbModifierSurface ladder = FindObjectsByType<ClimbModifierSurface>(FindObjectsSortMode.None)
            .Where(c => c.staminaUsageMultiplier <= A17_Traps.GameLadderStamina && c.GetComponent<Collider>() != null)
            .OrderBy(c => Vector3.Distance(c.transform.position, local.Center)).FirstOrDefault();
        if (ladder == null)
        {
            Info("the ladder checks are skipped", $"no ladder here ({Map.CurrentPlace}); run with -Biome AS");
            yield break;
        }
        Collider rungs = ladder.GetComponent<Collider>();
        Vector3 middle = rungs.bounds.center;
        // In front of it at its foot: the side it faces is the open one.
        Vector3 facing = Vector3.ProjectOnPlane(ladder.transform.forward, Vector3.up).normalized;
        Vector3 front = Vector3.zero;
        foreach (Vector3 side in new[] { facing, -facing, ladder.transform.right, -ladder.transform.right })
        {
            Vector3 probe = middle + side * 2f;
            if (!Physics.Linecast(probe, middle, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore) ||
                Physics.Linecast(probe, middle, out RaycastHit hit, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore) && hit.collider == rungs)
            {
                front = side;
                break;
            }
        }
        if (front == Vector3.zero)
        {
            Info("the ladder checks are skipped", "no open side found in front of the nearest ladder");
            yield break;
        }
        Vector3 stand = new Vector3(middle.x, rungs.bounds.min.y + 1f, middle.z) + front * 0.9f;
        ConfigEntry<bool> hazards = UltimateAscent.Plugin.AscentSwitches[Lvl.Hazards];
        bool saved = hazards.Value;
        Vector2 look = local.data.lookValues;
        Dictionary<bool, float> drain = new Dictionary<bool, float>();
        Dictionary<bool, float> speed = new Dictionary<bool, float>();
        try
        {
            foreach (bool on in new[] { true, false })
            {
                hazards.Value = on;
                HostOptions.Invalidate();
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stand, false);
                yield return new WaitForSeconds(1.5f);
                for (float until = Time.time + 3f; Time.time < until && !(local.data.isClimbing && local.data.climbMod == ladder);)
                {
                    Vector3 toward = Vector3.ProjectOnPlane(middle - MainCamera.instance.transform.position, Vector3.up).normalized;
                    local.data.lookValues = new Vector2(Quaternion.LookRotation(toward).eulerAngles.y, 10f);
                    local.refs.climbing.TryToStartWallClimb(forceAttempt: true, toward);
                    yield return null;
                }
                if (!(local.data.isClimbing && local.data.climbMod == ladder))
                {
                    Info("the scout did not get hold of the ladder", $"climbing {local.data.isClimbing}, surface {(local.data.climbMod != null ? local.data.climbMod.name : "rock or none")}");
                    break;
                }
                if (on)
                {
                    Screenshot("ladder-held");
                }
                ClimbSpeedRecorder.Last = -1f;
                local.data.currentStamina = 1f;
                local.data.extraStamina = 0f;
                float startStamina = local.data.currentStamina;
                float started = Time.time;
                bool stayed = true;
                for (float until = Time.time + 1.5f; Time.time < until;)
                {
                    stayed &= local.data.isClimbing && local.data.climbMod == ladder;
                    yield return null;
                }
                drain[on] = (startStamina - local.data.currentStamina) / (Time.time - started);
                speed[on] = ClimbSpeedRecorder.Last;
                local.refs.climbing.StopAnyClimbing();
                yield return new WaitForSeconds(1f);
                if (!stayed)
                {
                    Info("the scout let go of the ladder while it was measured");
                }
            }
        }
        finally
        {
            hazards.Value = saved;
            HostOptions.Invalidate();
            local.data.lookValues = look;
        }
        if (drain.Count == 2)
        {
            float ratio = drain[false] > 0f ? drain[true] / drain[false] : 0f;
            float expected = A17_Traps.LadderStamina / ladder.staminaUsageMultiplier;
            Check("ladders: climbing one is 1.2 times as fast as rock and costs 90% of rock's stamina (17; the game: 3 times, 25%)",
                Mathf.Abs(speed[true] - A17_Traps.LadderSpeed) < 0.001f && Mathf.Abs(speed[false] - ladder.speedMultiplier) < 0.001f && Mathf.Abs(ratio - expected) < expected * 0.1f
                && Mathf.Abs(ladder.staminaUsageMultiplier - 0.25f) < 0.001f,
                $"speed {speed[true]:0.##} (ascent 17's rule off: {speed[false]:0.##}), stamina {drain[true] * 100f:0.00}/s against {drain[false] * 100f:0.00}/s with the rule off = x{ratio:0.00} (expected x{expected:0.00}); the ladder's own fields stay {ladder.speedMultiplier:0.##}/{ladder.staminaUsageMultiplier:0.##}");
        }
    }

    /// <summary>
    /// The Citadel's mimic chests (17): the game's own mimic looks like an ordinary small chest; about a third of the
    /// ordinary chests are mimics too, the same ones every time it is asked (every player's game asks the same).
    /// </summary>
    private IEnumerator MimicChestsCheck()
    {
        if (Rules.Level < Lvl.Hazards)
        {
            Info("the mimic checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Luggage[] chests = Luggage.ALL_LUGGAGE.Where(l => l != null && Map.InCitadelSegment(l)).ToArray();
        if (chests.Length == 0)
        {
            Info("the mimic checks are skipped", $"no Citadel here ({Map.CurrentPlace}); run with -Biome AS");
            yield break;
        }
        yield return new WaitForSeconds(2.5f);
        Luggage plain = chests.FirstOrDefault(l => l.name.StartsWith("LuggageSmall", StringComparison.Ordinal) && !A17_Traps.IsGameMimic(l));
        Luggage mimic = chests.FirstOrDefault(A17_Traps.IsGameMimic);
        if (plain != null && mimic != null)
        {
            MeshFilter Part(Luggage chest, string path) => chest.transform.Find(path)?.GetComponent<MeshFilter>();
            MeshFilter mimicBody = Part(mimic, "luggage");
            MeshFilter mimicLid = Part(mimic, "luggage/Lid/Mesh");
            MeshFilter plainBody = Part(plain, "luggage");
            MeshFilter plainLid = Part(plain, "luggage/Lid");
            bool same = mimicBody != null && plainBody != null && mimicLid != null && plainLid != null && mimicBody.sharedMesh == plainBody.sharedMesh && mimicLid.sharedMesh == plainLid.sharedMesh
                        && mimicBody.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(plainBody.GetComponent<MeshRenderer>().sharedMaterials)
                        && mimicLid.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(plainLid.GetComponent<MeshRenderer>().sharedMaterials);
            Check("the game's mimic chest looks like an ordinary small chest (17)", same,
                $"body '{mimicBody?.sharedMesh?.name}' (ordinary '{plainBody?.sharedMesh?.name}'), lid '{mimicLid?.sharedMesh?.name}' (ordinary '{plainLid?.sharedMesh?.name}')");
            yield return SideBySideChests(mimic, plain);
        }
        else
        {
            Info("the mimic look check is skipped", $"a game's mimic {mimic != null}, an ordinary small chest {plain != null} in this Citadel");
        }
        Luggage[] ordinary = chests.Where(A17_Traps.IsCitadelChest).ToArray();
        int mimics = ordinary.Count(A17_Traps.IsMimicChest);
        bool steady = ordinary.All(l => A17_Traps.IsMimicChest(l) == A17_Traps.IsMimicChest(l));
        float share = ordinary.Length > 0 ? (float)mimics / ordinary.Length : 0f;
        Check("a share of the Citadel's ordinary chests are mimics, by this climb's id (17: 35%)", ordinary.Length >= 4 && mimics >= 1 && mimics < ordinary.Length && steady,
            $"{mimics} of {ordinary.Length} ordinary chests ({share * 100f:0}%), the same answer asked twice {steady}");
    }

    /// <summary>The game's mimic beside an ordinary small chest, from the same angle (a picture of each).</summary>
    private IEnumerator SideBySideChests(Luggage mimic, Luggage plain)
    {
        foreach ((Luggage chest, string name) in new[] { (mimic, "citadel-mimic-chest"), (plain, "citadel-ordinary-chest") })
        {
            GameObject holder = new GameObject("UltimateAscentTestCamera");
            try
            {
                Camera camera = holder.AddComponent<Camera>();
                camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
                camera.fieldOfView = 40f;
                camera.nearClipPlane = 0.05f;
                Vector3 centre = chest.transform.position + Vector3.up * 0.3f;
                holder.transform.position = centre + chest.transform.forward * 2.6f + Vector3.up * 1.6f;
                holder.transform.LookAt(centre);
                yield return new WaitForSeconds(0.5f);
                Screenshot(name);
                yield return new WaitForSeconds(0.5f);
            }
            finally
            {
                Destroy(holder);
            }
        }
    }

    /// <summary>
    /// An arrow trap and a swinging spike ball (9): a loose body put in a trap's line sets it off (the game's rule); the
    /// same body as part of a spike ball does not.
    /// </summary>
    private IEnumerator ArrowTrapCheck()
    {
        ArrowShooter Shooter() => FindObjectsByType<ArrowShooter>(FindObjectsSortMode.None).Where(s => s != null && s.isActiveAndEnabled && !s.empty && !s.reloading)
            .OrderBy(s => Vector3.Distance(s.pos, Local.Center)).FirstOrDefault();
        ArrowShooter shooter = Shooter();
        // Not reached yet (a check run on its own): to the last part of the climb, the Citadel on a Gloom map.
        if (shooter == null && Resources.FindObjectsOfTypeAll<ArrowShooter>().Any(s => s != null && s.gameObject.scene.IsValid())
            && Singleton<MapHandler>.Instance.GetCurrentSegment() < Segment.TheKiln)
        {
            MapHandler.JumpToSegment(Segment.TheKiln);
            yield return new WaitForSeconds(8f);
            shooter = Shooter();
        }
        SwingingAxe axe = FindObjectsByType<SwingingAxe>(FindObjectsSortMode.None).FirstOrDefault(a => a != null && a.isActiveAndEnabled);
        if (shooter == null || axe == null)
        {
            Info("the arrow trap check is skipped", $"arrow trap {shooter != null}, spike ball {axe != null} ({Map.CurrentPlace}); run with -Biome AS");
            yield break;
        }
        Vector3 spot = shooter.pos + shooter.forward * 1.5f;
        Dictionary<bool, int> fired = new Dictionary<bool, int>();
        foreach (bool partOfBall in new[] { false, true })
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "UltimateAscent Test Body";
            body.layer = LayerMask.NameToLayer("Default");
            body.transform.localScale = Vector3.one * 0.4f;
            if (partOfBall)
            {
                body.transform.SetParent(axe.transform, true);
            }
            Rigidbody rig = body.AddComponent<Rigidbody>();
            rig.useGravity = false;
            int before = ArrowShots.Count;
            for (int i = 0; i < 6; i++)
            {
                body.transform.position = spot;
                rig.position = spot;
                rig.linearVelocity = Vector3.zero;
                yield return new WaitForFixedUpdate();
            }
            fired[partOfBall] = ArrowShots.Count - before;
            Destroy(body);
            yield return new WaitForSeconds(Mathf.Max(0.6f, shooter.reloadTime + 0.2f));
        }
        Check("an arrow trap fires at a loose body in its line, but not at a swinging spike ball (9)", fired[false] > 0 && fired[true] == 0,
            $"a loose body: {fired[false]} shots, the same body as part of a spike ball: {fired[true]}");
        // The green warning line: half a metre out of the opening from 17, the game's whole line with the rule off.
        float LineLength()
        {
            LineRenderer line = shooter.lineRenderer;
            if (line == null || line.positionCount < 2)
            {
                return -1f;
            }
            Vector3 a = line.useWorldSpace ? line.GetPosition(0) : line.transform.TransformPoint(line.GetPosition(0));
            Vector3 b = line.useWorldSpace ? line.GetPosition(1) : line.transform.TransformPoint(line.GetPosition(1));
            return Vector3.Distance(a, b);
        }
        if (Rules.Enabled(Lvl.Hazards))
        {
            float shortLine = LineLength();
            ConfigEntry<bool> hazards = UltimateAscent.Plugin.AscentSwitches[Lvl.Hazards];
            hazards.Value = false;
            HostOptions.Invalidate();
            yield return new WaitForSeconds(2.5f);
            float gameLine = LineLength();
            hazards.Value = true;
            HostOptions.Invalidate();
            yield return new WaitForSeconds(2.5f);
            float againLine = LineLength();
            Screenshot("arrow-trap-warning-line");
            Check("an arrow trap's green warning line shows only its first half metre (17); with the rule off the game's whole line",
                shortLine > 0.05f && shortLine <= A17_Traps.WarningLineReach + 0.01f && gameLine > A17_Traps.WarningLineReach + 0.5f && Mathf.Abs(againLine - shortLine) < 0.01f,
                $"{shortLine:0.00} m, with the rule off {gameLine:0.00} m, on again {againLine:0.00} m");
        }
    }

    /// <summary>
    /// The Warp Compass's reasons (9): with nobody to warp to, a line in the middle of the screen says so, in the game's
    /// lettering, without a sound.
    /// </summary>
    private void WarpNoticeCheck()
    {
        Transform shown = GUIManager.instance.gloomRises != null ? GUIManager.instance.gloomRises.transform.parent.Find("UltimateAscent Notice") : null;
        bool quiet = shown != null && shown.GetComponentsInChildren<AudioSource>(true).Length == 0 && !shown.GetComponentsInChildren<Component>(true).Any(c => c != null && c.GetType().Name == "Animator");
        Check("the Warp Compass says why it did not warp: nobody to warp to (9)",
            Notice.Last == WarpCompass.NoOne.English && shown != null && shown.gameObject.activeInHierarchy && quiet,
            $"last line '{Notice.Last}', shown {(shown != null && shown.gameObject.activeInHierarchy)}, without sound or animation {quiet}");
        Screenshot("warp-compass-notice");
    }

    /// <summary>
    /// The multiplayer six-slot backpack (as in multiplayer, in memory): the start's Backpack, spawned the game's way, is
    /// the Six-Slot Backpack with six slots, the model and the icon; read back it keeps all six; worn, the back shows the
    /// six-slot model, and a game's Backpack worn after it the game's again. Scout's Generosity costs 100 petrification
    /// for it (held back here), its regular price for the game's Backpack.
    /// </summary>
    private IEnumerator SixSlotMultiplayerCheck()
    {
        Item gameBackpack = ItemByName("Backpack");
        Item six = SixSlotBackpack.Template;
        SingleItemSpawner start = FindObjectsByType<SingleItemSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(s => s.prefab != null && s.prefab.name == "Backpack" && s.transform.parent != null && s.transform.parent.name == "crashed plane");
        if (gameBackpack == null || six == null || start == null)
        {
            Fail("the Six-Slot Backpack, the Backpack and the start's backpack spawner are there", $"six-slot {six != null}, Backpack {gameBackpack != null}, start spawner {start != null}");
            yield break;
        }
        Character local = Local;
        Vector2 look = local.data.lookValues;
        Check("the Six-Slot Backpack is an item of its own: its name, its icon, never in a suitcase or on the Custom Run screen",
            Plain(six.GetItemName()) == "SIX-SLOT BACKPACK" && six.UIData.icon == SixSlotBackpack.icon && six.GetComponent<LootData>().spawnLocations == SpawnPool.None
            && six.GetComponent<LootData>().excludeFromCustomRunSelection && !UltimateAscent.Plugin.SixSlotBackpackMultiplayer.Value,
            $"name '{Plain(six.GetItemName())}', icon '{six.UIData.icon?.name}', pools {six.GetComponent<LootData>().spawnLocations}, the config switch on {UltimateAscent.Plugin.SixSlotBackpackMultiplayer.Value} (off by default)");
        SixSlotBackpack.AsMultiplayer = true;
        SixSlotBackpack.SpawnForTests = true;
        Backpack spawned = null;
        Backpack plain = null;
        try
        {
            List<PhotonView> views = null;
            // The game spawns once per spawner (its record then says so); this spawner's own Backpack is already out.
            Peak.SpawnedItemTracker record = start.GetComponent<Peak.SpawnedItemTracker>();
            if (record != null)
            {
                record.HasSpawnHistory = false;
            }
            Step("the start's Backpack spawned the game's way, as in multiplayer with the switch on", () => views = start.TrySpawnItems());
            spawned = views?.FirstOrDefault()?.GetComponent<Backpack>();
            yield return new WaitForSeconds(1.5f);
            if (spawned == null)
            {
                Fail("the start's spawner spawned a backpack");
                yield break;
            }
            MeshFilter ground = spawned.groundMesh != null ? spawned.groundMesh.GetComponent<MeshFilter>() : null;
            BackpackData data = spawned.GetData<BackpackData>(DataEntryKey.BackpackData);
            Check("in multiplayer the start's Backpack is the Six-Slot Backpack: six slots, its model on the ground",
                SixSlotBackpack.IsSix(spawned) && !SixSlotBackpack.On && data.itemSlots.Length == 6 && ground != null && ground.sharedMesh == SixSlotBackpack.ground,
                $"'{spawned.name}', six-slot {SixSlotBackpack.IsSix(spawned)}, {data.itemSlots.Length} slots, ground mesh '{ground?.sharedMesh?.name}'");
            // Read at once: the wheel closes as soon as the interact button is not held.
            Step("open the Six-Slot Backpack's wheel", () =>
            {
                spawned.Interact(local);
                BackpackWheel wheel = GUIManager.instance.backpackWheel;
                int active = wheel.slices.Count(slice => slice != null && slice.gameObject.activeSelf);
                Check("its wheel offers six slots", wheel.gameObject.activeSelf && active == 7, $"open {wheel.gameObject.activeSelf}, {active} slices shown");
                Screenshot("six-slot-multiplayer-wheel");
            });
            yield return null;
            GUIManager.instance.CloseBackpackWheel();
            yield return new WaitForSeconds(0.3f);
            Item food = ItemByName("Airplane Food");
            foreach (byte slot in new byte[] { 4, 5 })
            {
                yield return SpawnIntoHand(food);
                byte into = slot;
                Step($"stash food in slot {slot + 1} of the six-slot backpack", () => spawned.Stash(local, into));
                yield return new WaitForSeconds(0.6f);
            }
            BinarySerializer writer = new BinarySerializer(4000, Unity.Collections.Allocator.Temp);
            data.Serialize(writer);
            byte[] bytes = writer.buffer.ToByteArray();
            writer.Dispose();
            BackpackData copy = new BackpackData();
            copy.Init();
            BinaryDeserializer reader = new BinaryDeserializer(bytes, Unity.Collections.Allocator.Temp);
            copy.Deserialize(reader);
            reader.Dispose();
            Check("slots 5 and 6 hold their items, and the contents read back (as another player's game reads them) keep all six slots",
                !data.itemSlots[4].IsEmpty() && !data.itemSlots[5].IsEmpty() && copy.itemSlots.Length == 6 && !copy.itemSlots[4].IsEmpty() && !copy.itemSlots[5].IsEmpty(),
                $"slots 5/6 {(!data.itemSlots[4].IsEmpty())}/{(!data.itemSlots[5].IsEmpty())}; read back {copy.itemSlots.Length} slots (a new four-slot one before reading: {new BackpackData { }.itemSlots.Length})");

            // Scout's Generosity aimed at it, then at the game's Backpack.
            plain = SpawnInFront(gameBackpack, 2.6f)?.GetComponent<Backpack>();
            yield return new WaitForSeconds(1.2f);
            Item gem = ItemByName("Amulet_Clone");
            yield return SpawnIntoHand(gem);
            Item inHand = held;
            Peak.Action_CloneSelectedItem clone = inHand != null ? inHand.GetComponent<Peak.Action_CloneSelectedItem>() : null;
            Dictionary<string, int> costs = new Dictionary<string, int>();
            PetrifyRecorder.Blocking = true;
            try
            {
                foreach ((Backpack target, string label) in new[] { (spawned, "six"), (plain, "game") })
                {
                    if (clone == null || target == null)
                    {
                        break;
                    }
                    HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
                    local.data.lookValues = HelperFunctions.DirectionToLook(target.rig.worldCenterOfMass - MainCamera.instance.transform.position);
                    yield return new WaitForSeconds(0.6f);
                    PetrifyRecorder.Given.Clear();
                    inHand.StartUsePrimary();
                    for (float until = Time.realtimeSinceStartup + 5f; Time.realtimeSinceStartup < until && inHand != null && !inHand.finishedCast;)
                    {
                        inHand.ContinueUsePrimary();
                        yield return null;
                    }
                    if (inHand != null)
                    {
                        inHand.CancelUsePrimary();
                    }
                    yield return new WaitForSeconds(0.8f);
                    costs[label] = PetrifyRecorder.Given.Sum();
                    foreach (Item made in Item.ALL_ITEMS.Where(i => i != null && !before.Contains(i)).ToList())
                    {
                        if (made.photonView.IsMine)
                        {
                            PhotonNetwork.Destroy(made.gameObject);
                        }
                    }
                }
            }
            finally
            {
                PetrifyRecorder.Blocking = false;
            }
            if (clone != null && costs.Count == 2)
            {
                Check("Scout's Generosity clones the Six-Slot Backpack only for 100 petrification (death), the game's Backpack for its regular price",
                    costs["six"] == SixSlotBackpack.ClonePetrify && costs["game"] == clone.petrify,
                    $"six-slot {costs["six"]}, the game's Backpack {costs["game"]} (the gem's regular price {clone.petrify})");
            }
            yield return PutAway(gem);

            // Worn: the six-slot model on the back; the game's Backpack worn instead: the game's model again.
            BackpackOnBackVisuals back = local.GetComponentsInChildren<BackpackOnBackVisuals>(true).FirstOrDefault(v => v.backpackType == BackpackSlot.BackpackType.Backpack);
            Dictionary<string, bool> sixOnBack = new Dictionary<string, bool>();
            foreach ((Backpack wear, string label) in new[] { (spawned, "six"), (plain, "game") })
            {
                if (wear == null)
                {
                    continue;
                }
                local.refs.items.lastEquippedSlotTime = 0f;
                wear.Wear(local);
                for (float until = Time.realtimeSinceStartup + 4f; Time.realtimeSinceStartup < until && local.player.backpackSlot.IsEmpty();)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(1f);
                sixOnBack[label] = back != null && back.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh == SixSlotBackpack.worn);
                if (label == "six")
                {
                    BackpackData worn = null;
                    local.player.backpackSlot.data.TryGetDataEntry(DataEntryKey.BackpackData, out worn);
                    Check("worn, the Six-Slot Backpack keeps its six slots and the back shows the six-slot model",
                        worn != null && worn.itemSlots.Length == 6 && sixOnBack[label], $"{worn?.itemSlots.Length ?? 0} slots, six-slot model on the back {sixOnBack[label]}");
                    yield return BackPicture(local, "six-slot-multiplayer-back");
                }
                // Off the back again, dropped where it was; the six-slot one twice (worn again in between), staying itself.
                for (int round = 1; round <= (label == "six" ? 2 : 1); round++)
                {
                    if (round == 2)
                    {
                        local.refs.items.lastEquippedSlotTime = 0f;
                        spawned.Wear(local);
                        for (float until = Time.realtimeSinceStartup + 4f; Time.realtimeSinceStartup < until && local.player.backpackSlot.IsEmpty();)
                        {
                            yield return null;
                        }
                        yield return new WaitForSeconds(1f);
                    }
                    HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
                    local.refs.items.lastEquippedSlotTime = 0f;
                    local.refs.items.EquipSlot(Optionable<byte>.Some(3));
                    yield return new WaitForSeconds(0.7f);
                    Item onBack = local.data.currentItem;
                    bool sixInHands = SixSlotBackpack.IsSix(onBack);
                    if (onBack is Backpack)
                    {
                        local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 0f, (byte)3, onBack.transform.position + Vector3.up * 0.25f, Vector3.zero, onBack.transform.rotation, local.player.GetItemSlot(3).data, false);
                        local.refs.items.EquipSlot(Optionable<byte>.None);
                    }
                    yield return new WaitForSeconds(1f);
                    if (label == "six")
                    {
                        Backpack dropped = Item.ALL_ITEMS.OfType<Backpack>().FirstOrDefault(b => b != null && !before.Contains(b));
                        BackpackData kept = dropped != null ? dropped.GetData<BackpackData>(DataEntryKey.BackpackData) : null;
                        Check($"taken off the back into the hands and dropped{(round == 2 ? " a second time, after wearing it again" : "")}, the Six-Slot Backpack stays itself with what it holds (multiplayer)",
                            sixInHands && SixSlotBackpack.IsSix(dropped) && kept != null && kept.itemSlots.Length == 6 && !kept.itemSlots[4].IsEmpty() && !kept.itemSlots[5].IsEmpty(),
                            $"in the hands six-slot {sixInHands}; on the ground '{(dropped != null ? dropped.name : "nothing")}', six-slot {SixSlotBackpack.IsSix(dropped)}, {kept?.itemSlots.Length ?? 0} slots, slots 5/6 full {(kept != null && kept.itemSlots.Length == 6 && !kept.itemSlots[4].IsEmpty() && !kept.itemSlots[5].IsEmpty())}");
                        if (dropped != null)
                        {
                            spawned = dropped;
                        }
                    }
                }
            }
            if (sixOnBack.ContainsKey("game"))
            {
                Check("the game's Backpack worn in multiplayer shows the game's model on the back", !sixOnBack["game"], $"six-slot model {sixOnBack["game"]}");
            }
        }
        finally
        {
            SixSlotBackpack.AsMultiplayer = false;
            SixSlotBackpack.SpawnForTests = false;
            local.data.lookValues = look;
            foreach (Backpack left in Item.ALL_ITEMS.OfType<Backpack>().Where(b => b != null && b.photonView.IsMine && Vector3.Distance(b.transform.position, local.Center) < 8f).ToList())
            {
                PhotonNetwork.Destroy(left.gameObject);
            }
        }
    }

    /// <summary>
    /// The Nadir (17, 18; only with -Only Nadir: the jump there ends the climb's other checks): ailments heal at half
    /// the rate (ascent 17's rule off in memory for the comparison), blindness wears off half as fast, and gravity
    /// pulls twice as hard (ascent 18's rule off for the comparison).
    /// </summary>
    private IEnumerator NadirCheck()
    {
        if (only == null)
        {
            Info("the Nadir checks are skipped", "the jump to the Nadir ends the other checks; run with -Only Nadir");
            yield break;
        }
        if (Rules.Level < Lvl.NadirGravity)
        {
            Info("the Nadir checks are skipped", $"testing ascent {target}");
            yield break;
        }
        MapHandler.JumpToSegment(Segment.Void);
        yield return new WaitForSeconds(10f);
        Character local = Local;
        if (!Map.InNadir)
        {
            Fail("the scout is in the Nadir after the jump", $"place {Map.CurrentPlace}");
            yield break;
        }
        Screenshot("nadir");
        CharacterAfflictions afflictions = Afflictions;
        ConfigEntry<bool> recovery = UltimateAscent.Plugin.AscentSwitches[Lvl.NadirRecovery];
        ConfigEntry<bool> gravity = UltimateAscent.Plugin.AscentSwitches[Lvl.NadirGravity];
        bool savedRecovery = recovery.Value;
        bool savedGravity = gravity.Value;
        try
        {
            Dictionary<bool, float[]> rates = new Dictionary<bool, float[]>();
            foreach (bool on in new[] { true, false })
            {
                recovery.Value = on;
                HostOptions.Invalidate();
                yield return null;
                yield return null;
                rates[on] = new[] { afflictions.drowsyReductionPerSecond, afflictions.poisonReductionPerSecond, afflictions.hotReductionPerSecond, afflictions.sporesReductionPerSecond };
            }
            bool halved = Enumerable.Range(0, 4).All(i => rates[false][i] > 0f && Mathf.Abs(rates[true][i] - rates[false][i] * 0.5f) < 0.00001f);
            Check("in the Nadir drowsiness, poison, heat and spores heal at half the rate (17)", halved,
                $"drowsiness/poison/heat/spores per second: {string.Join(" / ", rates[true].Select(r => (r * 100f).ToString("0.###")))} against {string.Join(" / ", rates[false].Select(r => (r * 100f).ToString("0.###")))} with the rule off");
            recovery.Value = true;
            HostOptions.Invalidate();
            afflictions.AddAffliction(new Peak.Afflictions.Affliction_Blind { totalTime = 10f });
            yield return new WaitForSeconds(0.2f);
            Peak.Afflictions.Affliction blind = afflictions.afflictionList.FirstOrDefault(a => a.GetAfflictionType() == Peak.Afflictions.Affliction.AfflictionType.Blind);
            float before = blind != null ? blind.timeElapsed : -1f;
            float started = Time.time;
            yield return new WaitForSeconds(2f);
            float worn = blind != null ? blind.timeElapsed - before : -1f;
            float passed = Time.time - started;
            afflictions.RemoveAffliction(Peak.Afflictions.Affliction.AfflictionType.Blind);
            Check("in the Nadir blindness wears off half as fast (17)", blind != null && Mathf.Abs(worn - passed * 0.5f) < 0.1f,
                blind == null ? "no blindness" : $"{worn:0.00} s of its time gone in {passed:0.00} s");

            // Gravity: the pull the game works out for a scout in the air, with ascent 18's rule and without.
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, local.Center + Vector3.up * 15f, false);
            yield return new WaitForSeconds(0.25f);
            Dictionary<bool, float> pull = new Dictionary<bool, float>();
            // Both in the same frame (the pull grows the longer the scout has been in the air).
            foreach (bool on in new[] { true, false })
            {
                gravity.Value = on;
                HostOptions.Invalidate();
                pull[on] = local.refs.movement.GetGravityForce().y;
            }
            Check("in the Nadir gravity pulls a scout in the air twice as hard (18)", pull[false] < 0f && Mathf.Abs(pull[true] - pull[false] * 2f) < Mathf.Abs(pull[false]) * 0.05f,
                $"pull {pull[true]:0.0} against {pull[false]:0.0} with the rule off, {local.data.sinceGrounded:0.00} s in the air");
            yield return new WaitForSeconds(3f);
        }
        finally
        {
            recovery.Value = savedRecovery;
            gravity.Value = savedGravity;
            HostOptions.Invalidate();
        }
    }
}
