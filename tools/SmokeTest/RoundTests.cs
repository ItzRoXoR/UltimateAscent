using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// Ascent 15's Parasol: taken up high and opened, it slows the fall as much as the game's (the same fall with the
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
            Check("ascent 15's Parasol slows the fall as much as the game's, and 3 s of gliding take about 3 s off its 20 s, shown on its use bar (15)",
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
            Check("when its time is gone the Parasol is used up (15)", gone && !inSlots, $"gone {gone}, still in a slot {inSlots}");
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
    /// scout up like balloons below ascent 15, and half as high with ascent 15's rule (both in memory).
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
            // In the Kiln: a wide flat spot first (the jump can leave the scout on the slope above its lava, where a
            // stumble ends in the volcano).
            Vector3 eruptAlong = Vector3.ProjectOnPlane(local.data.lookDirection_Flat, Vector3.up).normalized;
            if (segment == Segment.TheKiln && FlatKilnSpot(local.Center, out Vector3 safe, out Vector3 along))
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, safe + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(2f);
                eruptAlong = along;
                Info("standing on flat ground in the Kiln for the eruption", $"at {local.Center:F1}");
            }
            Vector3 at = local.Center + eruptAlong * 4f;
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
    /// Shorter reach from ascent 15: the Rope Cannon and the Anti-Rope Cannon 25 m (the game: 30); the Rescue Claw 20 m
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
            Check($"the {(held != null ? held.GetName() : name)} reaches 25 m from ascent 15 (15)", cannon != null && Mathf.Abs(cannon.maxLength - A14_WorseItems.RopeCannonReach) < 0.01f,
                $"reach {(cannon != null ? cannon.maxLength : -1f):0.#} m (the game: {(cannon != null ? Overrides.Original(cannon, "maxLength", -1f) : -1f):0.#})");
        }
        yield return SpawnIntoHand(ItemByName("RescueHook"));
        yield return new WaitForSeconds(0.3f);
        RescueHook claw = held != null ? held.GetComponent<RescueHook>() : null;
        Check("the Rescue Claw reaches 20 m aimed level or up, 30 m aimed down, from ascent 15 (15)",
            claw != null && Mathf.Abs(claw.range - A14_WorseItems.RescueClawReach) < 0.01f && Mathf.Abs(claw.rangeDownward - A14_WorseItems.RescueClawReachDown) < 0.01f,
            claw != null ? $"level or up {claw.range:0.#} m, down {claw.rangeDownward:0.#} m (the game: {Overrides.Original(claw, "range", -1f):0.#} and {Overrides.Original(claw, "rangeDownward", -1f):0.#})" : "no claw in the hands");
    }

    /// <summary>
    /// What the Energy Drink and the Toxic Drink take off and the boost they give, drunk the game's way with 90 heat
    /// and 90 drowsiness, with ascent 15's rule on and switched off (in memory). The Energy Drink: 30 heat (from 15,
    /// 60% of it), all the drowsiness, the game's boosts (walking +50%, climbing +150%) on every ascent, from 15 for 6 s
    /// (cooked 8 s) from the moment it is drunk (the game holds its clock for the first 3 s unless you climb). The Toxic
    /// Drink: 60 heat (from 15, 60% of it), all the drowsiness, from 15 at most 80. Heat lands in 2.5 steps.
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
                foreach ((Item drink, bool cooked) in new[] { (ItemByName("Energy Drink"), false), (ItemByName("Energy Drink"), true), (ToxicDrink.Template, false) })
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
                    if (cooked)
                    {
                        // Cooked once in the hand (the cooked can's own boost; it bursts when drunk).
                        held.cooking.FinishCooking();
                        yield return new WaitForSeconds(0.5f);
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
                    float drunkAt = Time.time;
                    yield return new WaitForSeconds(0.5f);
                    float hotTaken = hot - Afflictions.GetCurrentStatus(Status.Hot);
                    float drowsyTaken = drowsy - Afflictions.GetCurrentStatus(Status.Drowsy);
                    Peak.Afflictions.Affliction_FasterBoi boost = Afflictions.afflictionList.OfType<Peak.Afflictions.Affliction_FasterBoi>().FirstOrDefault();
                    bool on = rule && saved;
                    bool toxic = drink == ToxicDrink.Template;
                    float heatWanted = (toxic ? ToxicDrink.Cooling : 0.3f) * (on ? A14_WorseItems.CureShare : 1f);
                    float drowsyWanted = toxic && on ? ToxicDrink.WakesWorse : drowsy;
                    bool boostRight = toxic || (boost != null && Mathf.Abs(boost.moveSpeedMod - 0.5f) < 0.01f && Mathf.Abs(boost.climbSpeedMod - 1.5f) < 0.01f);
                    if (!cooked)
                    {
                        Check($"the {(toxic ? "Toxic Drink" : "Energy Drink")} takes off {heatWanted * 100f:0} heat and {(drowsyWanted >= drowsy ? "all the drowsiness" : $"{drowsyWanted * 100f:0} drowsiness")}"
                            + (toxic ? "" : ", and walks +50% and climbs +150%") + $", ascent 15's rule {(on ? "on" : "off")} (15)",
                            Mathf.Abs(hotTaken - heatWanted) <= 0.026f && Mathf.Abs(drowsyTaken - drowsyWanted) <= 0.011f && boostRight,
                            $"took {hotTaken * 100f:0.#} heat, {drowsyTaken * 100f:0.#} drowsiness" + (boost != null ? $"; walking +{boost.moveSpeedMod * 100f:0}%, climbing +{boost.climbSpeedMod * 100f:0}%" : ""));
                    }
                    Info($"{(cooked ? "cooked " : "")}{drink.GetName()} drunk, ascent 15's rule {(rule && saved ? "on" : "off")} (climb on ascent {target})",
                        $"heat {hot * 100f:0} -> {Afflictions.GetCurrentStatus(Status.Hot) * 100f:0} (took {hotTaken * 100f:0.#}), drowsiness {drowsy * 100f:0} -> {Afflictions.GetCurrentStatus(Status.Drowsy) * 100f:0} (took {drowsyTaken * 100f:0.#}); "
                        + (boost != null ? $"boost walking +{boost.moveSpeedMod * 100f:0}%, climbing +{boost.climbSpeedMod * 100f:0}%, {boost.totalTime:0.#} s, drowsiness at its end {boost.drowsyOnEnd * 100f:0.#}" : "no speed boost"));
                    if (!toxic && boost != null)
                    {
                        // How long the boost lasts, the scout standing (not climbing): from 15, 6 s (cooked 8 s) from the
                        // moment it is drunk; without the rule the game's time, its clock held for the first 3 s.
                        // Its whole life: what it has run (and held) when found, then until it ends.
                        float hold = boost.climbDelay;
                        float foundAt = Time.time;
                        float before = boost.timeElapsed + Mathf.Max(0f, hold - Mathf.Max(0f, boost.bonusTime));
                        while (Time.time - drunkAt < 16f && Afflictions.afflictionList.Contains(boost))
                        {
                            yield return null;
                        }
                        float lasted = before + Time.time - foundAt;
                        float wanted = cooked ? A14_Items.EnergyDrinkCookedSeconds : A14_Items.EnergyDrinkSeconds;
                        Check($"the {(cooked ? "cooked " : "")}Energy Drink's boost lasts {(on ? $"{wanted:0} s from the moment it is drunk, its clock not held" : "the game's time, its clock held at first")}, "
                            + $"ascent 15's rule {(on ? "on" : "off")} (15)",
                            on ? Mathf.Abs(lasted - wanted) <= 0.3f && hold == 0f : lasted >= 8f,
                            $"lasted {lasted:0.0} s standing (counted from its own clock); {boost.totalTime:0.#} s, the clock held for {hold:0.#} s");
                    }
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
    /// Pandora's Lunchbox (15): its crab is rolled at half the share, as the game rolls its curse. The lunchbox's own
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
                    // Crab stays through the lunchbox's own clear (ascent 15): each one counted from nothing.
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
        Check("Pandora's Lunchbox gives crab at half the share, like its curse (15)", crabRolls > 100 && ratio > 0.7f && ratio < 1.5f,
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
            && six.GetComponent<LootData>().excludeFromCustomRunSelection && !(bool)UltimateAscent.Plugin.SixSlotBackpackMultiplayer.DefaultValue,
            $"name '{Plain(six.GetItemName())}', icon '{six.UIData.icon?.name}', pools {six.GetComponent<LootData>().spawnLocations}, the config switch on {UltimateAscent.Plugin.SixSlotBackpackMultiplayer.Value}, by default {UltimateAscent.Plugin.SixSlotBackpackMultiplayer.DefaultValue} (should be off)");
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

    /// <summary>
    /// The multiplayer round: ascent 8's switch (in memory), the Scoutmaster's 35 m, PlayersInfo's endless time as "∞"
    /// (and the game's lettering has the sign), face and body thorns (17), more wild food in multiplayer (told to act
    /// as in multiplayer, in memory), and a revive statue clearing every status where another revive keeps 25% (15).
    /// </summary>
    /// <summary>
    /// A reported oddity (2026-10-25, single player with True Final Ascent, Apocalypse off): after throwing a Warp
    /// Fungus and slipping on a jellyfish just before it landed, taking out any item lowered the stamina at once and
    /// putting it away gave it back. Tried here: three slots full (so the fungus sits in the hand slot), the fungus
    /// thrown the game's way at full charge, a jellyfish's own slip set off a little earlier each attempt before the
    /// landing (measured on the first throw), then each slot's item taken out and put away with the stamina, the most
    /// stamina and the weight compared. Only with -Only warpslip (it takes a few minutes).
    /// </summary>
    private IEnumerator WarpSlipRepro()
    {
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;
        Item fungus = ItemByName("WarpFungus") ?? ItemByName("Warp Fungus");
        string[] fillers = { "Granola Bar", "Airplane Food", "Apple Berry Red" };
        SlipperyJellyfish jelly = FindObjectsByType<SlipperyJellyfish>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (fungus == null || jelly == null)
        {
            Fail("the Warp Fungus slip check has a Warp Fungus and a jellyfish", $"fungus {fungus != null}, jellyfish {jelly != null} (run it while the beach is loaded)");
            yield break;
        }
        Vector3 home = local.Center;
        Vector2 look = local.data.lookValues;
        float flight = -1f;
        List<string> odd = new List<string>();
        Protect(true);
        float[] earlier = { -1f, 0.6f, 0.45f, 0.35f, 0.25f, 0.2f, 0.15f, 0.1f, 0.07f, 0.04f, 0.02f, 0f };
        for (int attempt = 0; attempt < earlier.Length; attempt++)
        {
            // Pockets full: the three slots each hold something, the hand empty.
            local.refs.items.EquipSlot(Optionable<byte>.None);
            yield return new WaitForSeconds(0.3f);
            for (byte slot = 0; slot < 3; slot++)
            {
                if (local.player.itemSlots[slot].IsEmpty())
                {
                    local.refs.items.lastEquippedSlotTime = 0f;
                    local.refs.items.SpawnItemInHand(fillers[slot]);
                    yield return new WaitForSeconds(0.6f);
                    local.refs.items.EquipSlot(Optionable<byte>.None);
                    yield return new WaitForSeconds(0.4f);
                }
            }
            // The fungus into the hand: with every slot full it lands in the hand slot.
            local.refs.items.lastEquippedSlotTime = 0f;
            local.refs.items.SpawnItemInHand(fungus.gameObject.name);
            float until = Time.time + 4f;
            while (Time.time < until && (local.data.currentItem == null || local.data.currentItem.itemID != fungus.itemID))
            {
                yield return null;
            }
            Item inHand = local.data.currentItem;
            if (inHand == null || inHand.itemID != fungus.itemID)
            {
                odd.Add($"attempt {attempt}: the fungus never reached the hand");
                continue;
            }
            byte from = local.refs.items.currentSelectedSlot.ValueOr(byte.MaxValue);
            local.data.lookValues = new Vector2(look.x, 25f);
            yield return new WaitForSeconds(0.3f);
            // The game's own throw (as letting go of the drop button after a full charge).
            ItemSlot fromSlot = local.player.GetItemSlot(from);
            float thrownAt = Time.time;
            local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 1f, from, inHand.transform.position, inHand.rig.linearVelocity, inHand.transform.rotation, fromSlot.data, false);
            local.refs.items.EquipSlot(Optionable<byte>.None);
            yield return null;
            Item flying = FindObjectsByType<Item>(FindObjectsSortMode.None).Where(i => i.itemID == fungus.itemID && i.itemState == ItemState.Ground).OrderBy(i => Vector3.Distance(i.transform.position, local.Center)).FirstOrDefault();
            bool slipped = false;
            float slipAt = earlier[attempt] < 0f || flight < 0f ? float.MaxValue : thrownAt + Mathf.Max(0f, flight - earlier[attempt]);
            Vector3 before = local.Center;
            float end = Time.time + 6f;
            while (Time.time < end && flying != null)
            {
                if (!slipped && Time.time >= slipAt)
                {
                    jelly.Trigger(local.photonView.ViewID);
                    slipped = true;
                }
                yield return null;
            }
            float landed = Time.time - thrownAt;
            bool warped = Vector3.Distance(local.Center, before) > 3f;
            if (flight < 0f && flying == null)
            {
                flight = landed;
            }
            yield return new WaitForSeconds(2.5f);
            // Each slot's item taken out and put away: the stamina, the most stamina and the weight should not move.
            for (byte slot = 0; slot < 3; slot++)
            {
                if (local.player.itemSlots[slot].IsEmpty())
                {
                    continue;
                }
                local.data.currentStamina = local.GetMaxStamina();
                float stowedMost = local.GetMaxStamina();
                float stowedStamina = local.data.currentStamina;
                float stowedWeight = afflictions.GetCurrentStatus(Status.Weight);
                local.refs.items.lastEquippedSlotTime = 0f;
                local.refs.items.EquipSlot(Optionable<byte>.Some(slot));
                yield return new WaitForSeconds(0.4f);
                float heldMost = local.GetMaxStamina();
                float heldStamina = local.data.currentStamina;
                float heldWeight = afflictions.GetCurrentStatus(Status.Weight);
                string state = HoldDiagnostics.Describe(local);
                local.refs.items.EquipSlot(Optionable<byte>.None);
                yield return new WaitForSeconds(0.4f);
                if (Mathf.Abs(heldMost - stowedMost) > 0.005f || heldStamina < stowedStamina - 0.02f || Mathf.Abs(heldWeight - stowedWeight) > 0.001f)
                {
                    odd.Add($"attempt {attempt} (slip {(slipped ? $"{earlier[attempt]:0.00} s before the landing" : "none")}, warped {warped}): slot {slot} held: most {stowedMost * 100f:0.#} -> {heldMost * 100f:0.#}, stamina {stowedStamina * 100f:0.#} -> {heldStamina * 100f:0.#}, weight {stowedWeight * 100f:0.#} -> {heldWeight * 100f:0.#}; {state}");
                }
            }
            Info($"Warp Fungus slip attempt {attempt}", $"slip {(slipped ? $"{earlier[attempt]:0.00} s before the landing (flight {flight:0.00} s)" : "none (measuring the flight)")}; landed or gone after {landed:0.00} s, warped {warped}; {HoldDiagnostics.Describe(local)}");
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home, false);
            local.data.lookValues = look;
            yield return new WaitForSeconds(3f);
        }
        Check("after a Warp Fungus throw and a jellyfish slip just before it lands, taking out an item changes neither the stamina nor the weight",
            odd.Count == 0, odd.Count == 0 ? $"{earlier.Length} attempts, the slip from 0.6 s to 0 s before a {flight:0.00} s flight" : string.Join(" | ", odd));
        for (byte slot = 0; slot < 3; slot++)
        {
            local.player.EmptySlot(Optionable<byte>.Some(slot));
        }
        afflictions.UpdateWeight();
    }

    /// <summary>
    /// Flat open ground in the Kiln with flat ground at the same level all around it (6 m), searched outwards from
    /// near the scout; the direction to put an eruption in comes with it.
    /// </summary>
    private static bool FlatKilnSpot(Vector3 near, out Vector3 spot, out Vector3 along)
    {
        spot = Vector3.zero;
        along = Vector3.forward;
        for (float radius = 0f; radius <= 80f; radius += 8f)
        {
            for (int angle = 0; angle < 360; angle += radius == 0f ? 360 : 30)
            {
                Vector3 from = near + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius + Vector3.up * 30f;
                RaycastHit hit = HelperFunctions.LineCheck(from, from + Vector3.down * 80f, HelperFunctions.LayerType.TerrainMap);
                if (hit.transform == null || hit.normal.y < 0.85f || !KilnAsh.InKiln(hit.point))
                {
                    continue;
                }
                bool flatAround = true;
                for (int around = 0; around < 360 && flatAround; around += 45)
                {
                    foreach (float reach in new[] { 3f, 6f })
                    {
                        Vector3 probe = hit.point + Quaternion.Euler(0f, around, 0f) * Vector3.forward * reach + Vector3.up * 3f;
                        RaycastHit below = HelperFunctions.LineCheck(probe, probe + Vector3.down * 6f, HelperFunctions.LayerType.TerrainMap);
                        if (below.transform == null || Mathf.Abs(below.point.y - hit.point.y) > 1.2f || below.normal.y < 0.7f)
                        {
                            flatAround = false;
                            break;
                        }
                    }
                }
                if (flatAround)
                {
                    spot = hit.point;
                    along = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Snakes (17), in the Tropics: 21 on the ground, seven in ten of them on the climb itself, and 7 in trees. A ground snake chases and bites
    /// a scout standing near it (10 injury, then 40 poison over 10 s); a Stick's swing stuns a snake; a tree snake
    /// sleeps coiled, wakes for a scout within 5 m and bites (30 injury), and the scout collapses within 15 s after.
    /// </summary>
    private IEnumerator SnakeChecks()
    {
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;
        MapHandler.JumpToSegment(Segment.Tropics);
        yield return new WaitForSeconds(8f);
        if (Map.CurrentPlace != Place.Tropics)
        {
            Info("the snake checks are skipped", $"no Tropics here ({Map.CurrentPlace}); run with -Biome and a map number that has the Tropics");
            yield break;
        }
        for (float until = Time.time + 10f; Time.time < until && Snakes.All.Count == 0;)
        {
            yield return null;
        }
        List<Snake> snakes = Snakes.All.Where(s => s != null).ToList();
        int trees = snakes.Count(s => s.IsTree);
        Check("snakes live in the Tropics, 21 on the ground and a third as many (7) in trees, whatever the map (17)",
            snakes.Count == Snakes.GroundCount + Snakes.TreeCount && trees <= Snakes.TreeCount,
            $"{snakes.Count} snakes ({trees} in trees, {snakes.Count - trees} on the ground); this map's third part: {Singleton<MapHandler>.Instance.segments[(int)Segment.Alpine].biome}; "
            + $"tree snakes {string.Join(", ", snakes.Where(s => s.IsTree).Select(s => s.Doing))}");
        Protect(false);
        Action calm = CalmForMeasuring();
        // No bee ambush meanwhile (the scout stands unprotected to be bitten; stung too, they would die at 24).
        A17_BeeAmbush.HeldOff = true;
        try
        {
            // A ground snake: the scout 3 m from it, waiting for its bite.
            Snake ground = snakes.Where(s => !s.IsTree && AboveGround(s.Head) < 0.5f).OrderBy(s => Vector3.Distance(s.Head, local.Center)).FirstOrDefault();
            Info("the ground snakes", string.Join("; ", snakes.Where(s => !s.IsTree).Select(s => $"{s.Doing} at {s.Head:F0}, {AboveGround(s.Head):0.0} m above the ground, {Vector3.Distance(s.Head, s.Home):0} m from its spot")));
            // Only the snake under test goes for the scout (another near it would bite too, and every bite is counted).
            foreach (Snake other in snakes.Where(s => s != ground))
            {
                other.Ignore();
            }
            if (ground != null)
            {
                // Left to itself (it notices no scout), it wanders about as with nobody near: 30 s of it close by, to hear its slither and see it crawl.
                Info("snake left alone", "a ground snake that notices nobody wanders about for 30 s, the scout watching from close by (listen for its slither)");
                AloneSeen alone = new AloneSeen();
                Vector3 foundAt = ground.Head;
                yield return WatchAlone(local, ground, 30f, "snake-alone", alone);
                Check("left alone, a ground snake wanders about at its calm pace, slithering as it goes (17)",
                    alone.crawled >= 5f && alone.heard >= alone.seconds * 0.5f && alone.doing == "Wandering" && alone.bites == 0,
                    $"crawled {alone.crawled:0.0} m in {alone.seconds:0} s (its calm pace {Snakes.WanderSpeed:0.0} m/s); its slither heard for {alone.heard:0} s of it, up to {alone.loudest:0.00}; it was {alone.doing}; bites {alone.bites}");
                float wanderSlither = alone.loudest;
                // Back where it was found for the checks that follow (it may have wandered off where there is no room to stand by it).
                ground.PlaceAt(foundAt, foundAt + Vector3.forward);
                yield return new WaitForSeconds(0.5f);
                // Firm footing if there is any (a ground snake's bite does not knock the scout down, so any top will do else).
                Vector3? besideSpot = StandNear(ground.Head, 2.5f, 3.6f) ?? StandNear(ground.Head, 2.5f, 5f) ?? StandNear(ground.Head, 2.5f, 3.6f, firm: false);
                if (!besideSpot.HasValue)
                {
                    Fail("a place to stand by the ground snake (the open top of solid ground, not inside a rock)", $"none 2.5-3.6 m from {ground.Head:F0}");
                    yield break;
                }
                Vector3 beside = besideSpot.Value;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, beside + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(0.6f);
                Vector3 toSnake = ground.Head - local.Center;
                local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(toSnake.x, 0f, toSnake.z)).eulerAngles.y, -15f);
                yield return new WaitForSeconds(0.5f);
                Screenshot("snake-ground");
                ClearStatuses(afflictions);
                int bitesBefore = Snakes.BitesTaken;
                int biteSoundsBefore = Snakes.BiteSounds;
                int glintsBefore = Snakes.FangGlints, sparksBefore = Snakes.FangSparks;
                float injuryBefore = afflictions.GetCurrentStatus(Status.Injury);
                float loudestSlither = wanderSlither, loudestRattle = 0f, widest = 0f;
                AttackSeen groundAttack = new AttackSeen();
                bool strikeShot = false;
                for (float until = Time.time + 15f; Time.time < until && Snakes.BitesTaken == bitesBefore;)
                {
                    yield return null;
                    loudestSlither = Mathf.Max(loudestSlither, ground.SlitherLoudness);
                    loudestRattle = Mathf.Max(loudestRattle, ground.RattleLoudness);
                    widest = Mathf.Max(widest, ground.MouthOpen);
                    groundAttack.Watch(ground);
                    if (!strikeShot && ground.Doing == "Lunging" && ground.MouthOpen > 0.6f)
                    {
                        strikeShot = true;
                        Screenshot("snake-strike");
                    }
                }
                bool bitten = Snakes.BitesTaken > bitesBefore;
                yield return null;
                float injury = afflictions.GetCurrentStatus(Status.Injury) - injuryBefore;
                Check("a chasing ground snake slithers and rattles, and its bite lands with the game's hit sound (17)",
                    loudestSlither > 0.05f && loudestRattle > 0.3f && Snakes.BiteSounds > biteSoundsBefore,
                    $"slither up to {loudestSlither:0.00}, rattle up to {loudestRattle:0.00} (volumes), bite sounds {Snakes.BiteSounds - biteSoundsBefore}");
                Check("its fangs spark as the bite lands and glint after the strike, as a scorpion's sting does (17)",
                    Snakes.FangGlints > glintsBefore && Snakes.FangSparks > sparksBefore,
                    $"glints {Snakes.FangGlints - glintsBefore}, sparks {Snakes.FangSparks - sparksBefore}");
                Check("rearing up and striking, a ground snake keeps its back to the sky: nothing it reaches out with lies on its side or shows its belly (17)",
                    groundAttack.Roll < RaisedRollLimit,
                    $"its back turned from the sky by up to {groundAttack.rising:0} degrees rearing up, {groundAttack.striking:0} striking, where it reached out (0 back up, 90 on its side, 180 belly up)");
                Check("as it strikes out of its S, no part of a ground snake jumps (17)", groundAttack.handOver < HandOverJumpLimit,
                    $"in the frame the strike began a part of it moved {groundAttack.handOver * 100f:0.0} cm (its head stays where it was)");
                Info("the ground snake's frames at its worst, rearing up", groundAttack.risingAt);
                Info("the ground snake's frames at its worst, striking", groundAttack.strikingAt);
                Meter poison = new Meter(Status.Poison);
                int bite = Snakes.PoisonByBite.Count - 1;
                // After its bite it keeps off, slithering round the scout, until it may strike again (the scout stays 2.5 s).
                float shutIn = -1f;
                float lifted = float.PositiveInfinity;
                float afterBite = 0f;
                float nearestAfter = float.MaxValue;
                Vector3 lastSeen = ground.Head;
                int bitesAfter = Snakes.BitesTaken;
                for (float since = 0f; since < 2f; since += Time.deltaTime)
                {
                    yield return null;
                    if (shutIn < 0f && ground.MouthOpen < 0.05f)
                    {
                        shutIn = since;
                    }
                    if (since >= 1f)
                    {
                        // Its lowest from 1 s on: left in the air it stays up; crossing a ledge just then it is up a moment.
                        lifted = Mathf.Min(lifted, AboveGround(ground.Head));
                    }
                    if (since >= 0.3f)
                    {
                        afterBite += Vector3.ProjectOnPlane(ground.Head - lastSeen, Vector3.up).magnitude;
                    }
                    if (since >= 0.6f)
                    {
                        nearestAfter = Mathf.Min(nearestAfter, Vector3.ProjectOnPlane(ground.Head - local.Center, Vector3.up).magnitude);
                    }
                    lastSeen = ground.Head;
                }
                Check("it opens its mouth wide and bares its fangs as it strikes, and shuts it after the bite (17)", widest > 0.85f && shutIn >= 0f && shutIn < 0.6f,
                    $"mouth open up to {widest:0.00}, shut {(shutIn >= 0f ? $"{shutIn:0.00} s after the bite" : "not again within 2.5 s")}");
                Check("after its jump the snake drops back to the ground, not left in the air (17)", lifted < 0.35f,
                    $"its head {lifted:0.00} m above the ground at the lowest 1-2 s after the bite (the snake {ground.Doing})");
                Check("after its bite it does not freeze: it slithers round the scout, keeping off, until it may strike again (17)",
                    afterBite >= 1.2f && nearestAfter >= 1.4f && Snakes.BitesTaken == bitesAfter,
                    $"crawled {afterBite:0.0} m in 1.7 s, never nearer than {nearestAfter:0.0} m, bites meanwhile {Snakes.BitesTaken - bitesAfter}");
                StepAway(local, ground.Head);
                yield return new WaitForSeconds(9f);
                poison.Stop();
                float venom = bite >= 0 ? Snakes.PoisonByBite[bite] : 0f;
                int bites = Snakes.BitesTaken - bitesBefore;
                Check("a ground snake chases and bites a scout near it: 10 injury, then 40 poison over 10 s (17)",
                    bitten && bites == 1 && Mathf.Abs(injury - Snakes.GroundBiteInjury) < 0.013f && Mathf.Abs(venom - Snakes.GroundPoison) < 0.001f && poison.Total >= venom - 0.03f,
                    $"bitten {bitten} (the snake {ground.Doing}); bites while measuring {bites}; +{injury * 100f:0.#} injury, this bite's venom +{venom * 100f:0.#} poison in {poison.Seconds:0.#} s (on the bar +{poison.Total * 100f:0.#}, anything else there included)");
                ClearStatuses(afflictions);

                // A Stick's swing stuns it.
                Item stick = StickItem.Template;
                if (stick != null)
                {
                    yield return SpawnIntoHand(stick);
                    yield return new WaitForSeconds(0.5f);
                    // On open, flat ground with room behind it for its knock-back: the snake lies there facing the scout, who
                    // stands 1.6 m in front of it and swings (it notices nobody until the swing lands).
                    Vector3? stickStage = null;
                    Vector3 stickWay = Vector3.forward;
                    for (int angle = 0; angle < 360 && !stickStage.HasValue; angle += 30)
                    {
                        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                        Vector3? centre = FlatOpen(ground.Head + dir * 2f);
                        if (centre.HasValue && FlatOpen(centre.Value - dir * 1.6f).HasValue && FlatOpen(centre.Value + dir).HasValue && FlatOpen(centre.Value + dir * 2f).HasValue)
                        {
                            stickStage = centre;
                            stickWay = dir;
                        }
                    }
                    if (stickStage.HasValue)
                    {
                        ground.Unaware = true;
                        ground.Puppet = true;
                        ground.PlaceAt(stickStage.Value + Vector3.up * 0.06f, stickStage.Value - stickWay);
                        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, stickStage.Value - stickWay * 1.6f + Vector3.up * 1.2f, false);
                        yield return new WaitForSeconds(1.5f);
                        Vector3 at = ground.Head - local.Center;
                        local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(at.x, 0f, at.z)).eulerAngles.y, Mathf.Clamp(-Mathf.Atan2(-at.y, new Vector2(at.x, at.z).magnitude) * Mathf.Rad2Deg, -60f, 30f));
                        local.data.currentStamina = 1f;
                        yield return new WaitForSeconds(0.2f);
                        ground.Puppet = false;
                    }
                    if (!stickStage.HasValue)
                    {
                        Info("the Stick-on-snake check is skipped", $"no open, flat ground by the snake at {ground.Head:F0}");
                    }
                    else if (held != null && Vector3.Distance(ground.Head, local.Center) < 3f)
                    {
                        // The swing, and where the snake was as it landed (the frame before it was stunned).
                        Vector3 hitAt = ground.Head;
                        int hitSoundsBefore = Snakes.HitSounds;
                        held.StartUsePrimary();
                        float swingDone = Time.time + StickItem.StrikeDelay + 0.2f;
                        for (float until = Time.time + StickItem.StrikeDelay + 0.6f; Time.time < until && ground.Doing != "Stunned";)
                        {
                            hitAt = ground.Head;
                            yield return null;
                        }
                        ground.Unaware = false;
                        Check("a Stick's swing stuns a snake in reach, the blow heard with the game's impact sound (17)", ground != null && ground.Doing == "Stunned" && Snakes.HitSounds > hitSoundsBefore,
                            $"the snake {(ground != null ? ground.Doing : "gone")}, {Vector3.Distance(ground.Head, local.Center):0.0} m away; impact sounds {Snakes.HitSounds - hitSoundsBefore}");
                        // Hit, it is knocked back in a quick slide (no jump), then lies curled up, without striking, for the rest of its stun.
                        int bitesHit = Snakes.BitesTaken;
                        float biggestStep = 0f;
                        float knockedBack = 0f;
                        float hitLift = float.PositiveInfinity;
                        float crawled = 0f;
                        float closest = float.MaxValue;
                        bool curledSeen = false;
                        float sunk = 0f, nextHitShot = 0f, stunnedSound = 0f;
                        int hitShot = 0;
                        Vector3 last = hitAt;
                        for (float since = 0f; since < StickItem.Knockdown - 0.6f && ground != null && ground.Doing == "Stunned"; since += Time.deltaTime)
                        {
                            yield return null;
                            if (swingDone > 0f && Time.time >= swingDone)
                            {
                                held.CancelUsePrimary();
                                swingDone = 0f;
                            }
                            if (since >= 0.5f)
                            {
                                // Silent while stunned (its slither and rattle gone a moment after the hit).
                                stunnedSound = Mathf.Max(stunnedSound, Mathf.Max(ground.SlitherLoudness, ground.RattleLoudness));
                            }
                            if (since < 1.5f)
                            {
                                // How far any of it is under the ground while knocked back and curling up; pictures of it.
                                foreach (Vector3 part in ground.Parts())
                                {
                                    sunk = Mathf.Max(sunk, UnderGround(part));
                                }
                                if (since >= nextHitShot)
                                {
                                    nextHitShot += 0.25f;
                                    Screenshot($"snake-hit-{++hitShot}");
                                }
                            }
                            float stepNow = Vector3.Distance(Vector3.ProjectOnPlane(ground.Head, Vector3.up), Vector3.ProjectOnPlane(last, Vector3.up));
                            if (since < 0.4f)
                            {
                                biggestStep = Mathf.Max(biggestStep, stepNow);
                                knockedBack = Vector3.ProjectOnPlane(ground.Head - hitAt, Vector3.up).magnitude;
                            }
                            crawled += stepNow;
                            last = ground.Head;
                            if (since >= 0.5f)
                            {
                                closest = Mathf.Min(closest, Vector3.ProjectOnPlane(ground.Head - local.Center, Vector3.up).magnitude);
                            }
                            curledSeen |= since > 0.9f && ground.Curled && ground.Doing == "Stunned";
                            if (float.IsPositiveInfinity(hitLift) && since >= 1.5f)
                            {
                                hitLift = AboveGround(ground.Head);
                            }
                        }
                        if (swingDone > 0f)
                        {
                            held.CancelUsePrimary();
                        }
                        Check("the snake the swing pushed back lies on the ground, not in the air (17)", hitLift < 0.35f, $"its head {hitLift:0.00} m above the ground 1.5 s after the hit");
                        Check("a hit knocks it back in a quick slide, not a jump (17)",
                            ground.LastShove >= 0.3f && knockedBack >= Mathf.Min(1f, ground.LastShove * 0.8f) && biggestStep < 0.4f,
                            $"knocked back {knockedBack:0.0} m in 0.4 s (its shove {ground.LastShove:0.0} m of the full 1.5, as far as the ground behind it allows{(ground.LastShoveStop.Length > 0 ? $"; cut short by {ground.LastShoveStop}" : "")}), the biggest step in a frame {biggestStep:0.00} m");
                        Check("stunned, the snake makes no sound: no slither, no rattle (17)", stunnedSound < 0.01f,
                            $"its loudest sound from 0.5 s after the hit until the stun is nearly over: {stunnedSound:0.000}");
                        Check("knocked back and curling up, no part of it sinks into the ground (17)", sunk < 0.06f,
                            $"at most {sunk:0.00} m of it under the ground in the 1.5 s after the hit");
                        Check("hit, it curls up for as long as it is stunned, and does not strike meanwhile (17)",
                            curledSeen && crawled - knockedBack < 0.6f && Snakes.BitesTaken == bitesHit,
                            $"curled up {curledSeen}, moved {crawled - knockedBack:0.0} m after its shove, bites meanwhile {Snakes.BitesTaken - bitesHit}");
                        // The stun over, it goes for the scout who stunned it (here within its reach for aggression).
                        for (float until = Time.time + 1.5f; Time.time < until && ground.Doing == "Stunned";)
                        {
                            yield return null;
                        }
                        Check("once its stun is over, it goes for the scout who stunned it (17)", ground.Doing != "Stunned" && ground.Target == local,
                            $"the snake {ground.Doing}, going for {(ground.Target == local ? "the scout who hit it" : ground.Target != null ? "another scout" : "nobody")} ({Vector3.Distance(ground.Head, local.Center):0.0} m from them)");
                        // That chase lasts at least 10 s however far the scout runs: they go 60-80 m off at once (well beyond the
                        // 20 m it gives up at), and it keeps after them until its 10 s are up, then gives up.
                        float chaseFrom = Time.time;
                        StepAway(local, ground.Head, 60f, 80f);
                        yield return new WaitForSeconds(0.5f);
                        float nearestAway = float.MaxValue;
                        float gaveUpAfter = -1f;
                        for (float until = chaseFrom + Snakes.MinChaseSeconds + 4f; Time.time < until && gaveUpAfter < 0f;)
                        {
                            yield return null;
                            nearestAway = Mathf.Min(nearestAway, Vector3.Distance(ground.Head, local.Center));
                            if (ground.Target != local)
                            {
                                gaveUpAfter = Time.time - chaseFrom;
                            }
                        }
                        Check("a snake keeps after a scout at least 10 s however far they run, then gives up (17)",
                            nearestAway > Snakes.GroundGiveUpDistance && gaveUpAfter >= Snakes.MinChaseSeconds - 0.2f && gaveUpAfter <= Snakes.MinChaseSeconds + 1.5f,
                            $"the scout {nearestAway:0} m off at the nearest (it gives up beyond {Snakes.GroundGiveUpDistance:0} m); {(gaveUpAfter >= 0f ? $"it gave up {gaveUpAfter:0.0} s into its chase" : "it never gave up")} (the snake {ground.Doing})");
                    }
                    else
                    {
                        Info("the Stick-on-snake check is skipped", $"the snake is {Vector3.Distance(ground.Head, local.Center):0.0} m away");
                    }
                    yield return PutAway(stick);
                }
            }

            // A tree snake: asleep, woken at 5 m, a bite and a collapse after. The nearest with firm footing by it (where the
            // scout cannot slip off a branch), or else the nearest.
            List<Snake> treeSnakes = snakes.Where(s => s.IsTree).OrderBy(s => Vector3.Distance(s.Head, local.Center)).ToList();
            Snake tree = treeSnakes.FirstOrDefault(s => StandNear(s.Head, 2.2f, 4.3f).HasValue) ?? treeSnakes.FirstOrDefault();
            if (tree != null)
            {
                // Now only this one goes for the scout.
                foreach (Snake other in snakes.Where(s => s != tree))
                {
                    other.Ignore();
                }
                tree.Unaware = false;
                Vector3? view = StandNear(tree.Head, 6f, 9f, seeIt: true);
                Vector3? near = StandNear(tree.Head, 2.2f, 4.3f);
                Info("the tree snakes' ways up", $"{Snakes.ClimbingWays} of {snakes.Count(s => s.IsTree)} climb a way up their tree; this one's way {tree.WayLength:0} m");
                Info("the tree snake", $"in its tree at {tree.Head:F0} ({AboveGround(tree.Head):0.0} m above what is under it), {tree.Doing}; a place to look from {(view.HasValue ? $"{Vector3.Distance(view.Value, tree.Head):0.0} m off" : "none")}, to stand by it {(near.HasValue ? $"{Vector3.Distance(near.Value + Vector3.up, tree.Head):0.0} m off" : "none")}");
                string asleep = tree.Doing;
                if (view.HasValue)
                {
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, view.Value + Vector3.up * 1.2f, false);
                    yield return new WaitForSeconds(1.5f);
                    Vector3 to = tree.Head - local.Center;
                    local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y,
                        Mathf.Clamp(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
                    yield return new WaitForSeconds(0.5f);
                    asleep = tree.Doing;
                    Screenshot("snake-tree-asleep");
                }
                if (!near.HasValue)
                {
                    Fail("a tree snake has a place by it to stand on (its giant tree)", $"none within 4.3 m of {tree.Head:F0}");
                    yield break;
                }
                // Straight there by a warp: the snake leaves a scout alone while a warp runs, and wakes once it is over.
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, near.Value + Vector3.up * 1.2f, false);
                ClearStatuses(afflictions);
                int bitesBefore = Snakes.BitesTaken;
                float injuryBefore = afflictions.GetCurrentStatus(Status.Injury);
                bool uncoiled = false, uncoilShot = false;
                float wokeAt = -1f;
                AttackSeen treeAttack = new AttackSeen();
                for (float until = Time.time + 12f; Time.time < until && Snakes.BitesTaken == bitesBefore;)
                {
                    yield return null;
                    treeAttack.Watch(tree);
                    if (tree.Doing == "Waking")
                    {
                        uncoiled = true;
                        if (wokeAt < 0f)
                        {
                            wokeAt = Time.time;
                            Vector3 look = tree.Head - local.Center;
                            local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y,
                                Mathf.Clamp(Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
                        }
                        if (!uncoilShot && Time.time - wokeAt > Snakes.UncoilSeconds * 0.7f)
                        {
                            uncoilShot = true;
                            Screenshot("snake-tree-uncoil");
                        }
                    }
                }
                bool bitten = Snakes.BitesTaken > bitesBefore;
                float bittenAt = Time.time;
                yield return null;
                float injury = afflictions.GetCurrentStatus(Status.Injury) - injuryBefore;
                Screenshot("snake-tree-bite");
                // After its bite it keeps off as a ground snake does, slithering round the scout until it may strike again
                // (the scout stays 2 s; the collapse comes later).
                float treeCrawled = 0f, treeNearest = float.MaxValue;
                Vector3 treeLast = tree.Head;
                int treeBitesAfter = Snakes.BitesTaken;
                string treeAfter = tree.Doing;
                for (float since = 0f; since < 2f; since += Time.deltaTime)
                {
                    yield return null;
                    if (since >= 0.3f)
                    {
                        treeCrawled += Vector3.ProjectOnPlane(tree.Head - treeLast, Vector3.up).magnitude;
                    }
                    if (since >= 0.6f)
                    {
                        treeNearest = Mathf.Min(treeNearest, Vector3.ProjectOnPlane(tree.Head - local.Center, Vector3.up).magnitude);
                    }
                    treeLast = tree.Head;
                }
                treeAfter += " -> " + tree.Doing;
                int treeBitesMeanwhile = Snakes.BitesTaken - treeBitesAfter;
                StepAway(local, tree.Head);
                bool collapsed = false;
                for (float until = Time.time + Snakes.FirstCollapseMax + 1f; Time.time < until && !collapsed;)
                {
                    collapsed = local.data.fallSeconds > Snakes.CollapseSeconds - 0.5f;
                    yield return null;
                }
                Check("a tree snake sleeps curled up, uncoils for a scout within 5 m and bites: 30 injury, then a 4 s collapse within 15 s (17)",
                    asleep == "Sleeping" && uncoiled && bitten && Mathf.Abs(injury - Snakes.TreeBiteInjury) < 0.013f && collapsed,
                    $"before: {asleep}; uncoiled first {uncoiled}; bitten {bitten}; +{injury * 100f:0.#} injury; collapsed {collapsed} ({Time.time - bittenAt:0.0} s after the bite)");
                Check("uncoiling and striking, a tree snake keeps its back to the sky: nothing it reaches out with lies on its side or shows its belly (17)",
                    treeAttack.Roll < RaisedRollLimit,
                    $"its back turned from the sky by up to {treeAttack.rising:0} degrees uncoiling, {treeAttack.striking:0} striking, where it reached out (0 back up, 90 on its side, 180 belly up)");
                Check("as it strikes out of its uncoiling, no part of a tree snake jumps (17)", treeAttack.handOver < HandOverJumpLimit,
                    $"in the frame the strike began a part of it moved {treeAttack.handOver * 100f:0.0} cm (its head stays where it was)");
                Info("the tree snake's frames at its worst, uncoiling", treeAttack.risingAt);
                Info("the tree snake's frames at its worst, striking", treeAttack.strikingAt);
                Check("after its bite a tree snake does not freeze either: it slithers round the scout, keeping off, until it may strike again (17)",
                    treeCrawled >= 1.2f && treeNearest >= 1.4f && treeBitesMeanwhile == 0,
                    $"crawled {treeCrawled:0.0} m in 1.7 s, never nearer than {treeNearest:0.0} m, bites meanwhile {treeBitesMeanwhile} (the snake {treeAfter})");
                // From here on nothing measures statuses: the scout is kept from harm (a poison bush by the spot they stepped
                // away to once added to the bite's injury, and passing out is death at 24).
                Protect(true);
                // While it lasts (once the collapse under way is over): a poison drop over the stamina bar, past its right
                // end, draining, the time left beside it; beside the Climbing Chalk's hand when both show.
                for (float until = Time.time + Snakes.CollapseSeconds + 2f; Time.time < until && local.data.fallSeconds > 0f;)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(0.5f);
                float paralysisLeft = SnakeParalysis.Left(local);
                Vector2 dropAlone = SnakeParalysis.IconPosition;
                Screenshot("snake-paralysis-drop");
                Check("while a tree snake's paralysis lasts, a poison drop over the stamina bar drains, the time left beside it (17)",
                    SnakeParalysis.IconShowing && paralysisLeft > 60f && paralysisLeft <= Snakes.ParalysisSeconds && SnakeParalysis.IconText.Contains(":"),
                    $"the drop shown {SnakeParalysis.IconShowing} at {dropAlone}, {paralysisLeft:0} s left, '{SnakeParalysis.IconText}' beside it");
                Item chalk = ItemByName(ClimbingChalkItem.PrefabName);
                if (chalk != null)
                {
                    yield return SpawnIntoHand(chalk);
                    if (held != null)
                    {
                        yield return EatHeld();
                        yield return new WaitForSeconds(0.5f);
                        Vector2 dropBeside = SnakeParalysis.IconPosition;
                        Screenshot("snake-paralysis-drop-and-chalk");
                        Check("with the Climbing Chalk's hand showing too, the drop sits beside it, not on it (17)",
                            ClimbingChalkItem.StatusIconShowing && SnakeParalysis.IconShowing && dropBeside.x - ClimbingChalkItem.StatusIconPosition.x >= 40f,
                            $"the hand shown {ClimbingChalkItem.StatusIconShowing} at {ClimbingChalkItem.StatusIconPosition}, the drop at {dropBeside} (alone at {dropAlone})");
                    }
                    yield return PutAway(chalk);
                }
                // Bitten again while it lasts, it does not stack: its two minutes start over, the next collapse stays when it was.
                float leftBefore = SnakeParalysis.Left(local);
                float nextBefore = SnakeParalysis.NextCollapseIn;
                SnakeParalysis.Bite(local);
                yield return null;
                float leftAfter = SnakeParalysis.Left(local);
                float nextAfter = SnakeParalysis.NextCollapseIn;
                Check("a second tree snake bite does not stack the paralysis: its two minutes start over (17)",
                    leftBefore < Snakes.ParalysisSeconds - 2f && leftAfter > Snakes.ParalysisSeconds - 1f && leftAfter <= Snakes.ParalysisSeconds && Mathf.Abs(nextAfter - nextBefore) < 0.2f,
                    $"{leftBefore:0} s left before the second bite, {leftAfter:0} s after; the next collapse in {nextBefore:0.0} s before it, {nextAfter:0.0} s after");
                // The game's poison cures end it, each used the game's way (the bite's own paralysis first).
                yield return CureParalysis(local, "FirstAidKit", "eaten", true);
                yield return CureParalysis(local, "Cure-All", "used", false);
                yield return CureParalysis(local, "MedicinalRoot", "eaten", false);
                yield return CureParalysis(local, "Napberry", "fed", false);
                yield return CureParalysis(local, "HealingDart Variant", "darted", false);
                yield return CureParalysis(local, "HealingPuffShroom", "thrown at the feet", false);
                // No more collapses while the scout goes on to watch it climb; the statuses go.
                Snakes.Venom.EndAll();
                ClearStatuses(afflictions);

                // Its climb home: from just above the foot of its tree, up its way for 20 s, winding round the trunk; a picture on the way.
                if (tree.WayLength < 25f)
                {
                    Info("the climb check is skipped", $"this tree snake has no way up its tree long enough ({tree.WayLength:0} m)");
                }
                else
                {
                    // Climbing is what is checked here, not noticing scouts: it pays the scout (wherever they are) no heed meanwhile.
                    tree.Unaware = true;
                    tree.ClimbFrom(2f);
                    float startAlong = tree.AlongWay;
                    float scoutOff = Vector3.Distance(local.Center, tree.Head);
                    float lowest = float.MaxValue, highest = float.MinValue, turned = 0f;
                    Vector3 lastFlat = Vector3.zero;
                    Vector3 lastHead = tree.Head;
                    bool climbShot = false;
                    for (float since = 0f; since < 20f; since += Time.deltaTime)
                    {
                        yield return null;
                        lowest = Mathf.Min(lowest, tree.Head.y);
                        highest = Mathf.Max(highest, tree.Head.y);
                        Vector3 flat = Vector3.ProjectOnPlane(tree.Head - lastHead, Vector3.up);
                        if (flat.magnitude > 0.01f)
                        {
                            if (lastFlat != Vector3.zero)
                            {
                                turned += Vector3.SignedAngle(lastFlat, flat, Vector3.up);
                            }
                            lastFlat = flat;
                            lastHead = tree.Head;
                        }
                        if (!climbShot && since > 8f)
                        {
                            climbShot = true;
                            Vector3? watch = StandNear(tree.Head, 6f, 10f, seeIt: true);
                            if (watch.HasValue)
                            {
                                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, watch.Value + Vector3.up * 1.2f, false);
                                yield return new WaitForSeconds(1.2f);
                                since += 1.2f;
                                Vector3 look = tree.Head - local.Center;
                                local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y,
                                    Mathf.Clamp(Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
                                yield return new WaitForSeconds(0.4f);
                                since += 0.4f;
                                Screenshot("snake-tree-climbing");
                                StepAway(local, tree.Head);
                            }
                        }
                    }
                    bool climbing = tree.Clinging && tree.Doing == "Returning";
                    float along = tree.AlongWay - startAlong;
                    tree.Unaware = false;
                    tree.GoHomeNow();
                    Check("it climbs home up its giant tree as snakes do, along its way winding round the trunk (17)",
                        along >= 15f && climbing,
                        $"in 20 s {along:0.0} m up its way (it climbs 1 m/s), rising {highest - lowest:0.0} m and winding {Mathf.Abs(turned):0} degrees round as the way goes (still climbing: {climbing}); its way {tree.WayLength:0} m long; the scout {scoutOff:0} m off at the start");
                }
            }
            else
            {
                Info("the tree snake check is skipped", "no tree snakes on this map (no giant trees found)");
            }
        }
        finally
        {
            foreach (Snake left in Snakes.All.Where(s => s != null))
            {
                left.Unaware = false;
            }
            A17_BeeAmbush.HeldOff = false;
            ClearStatuses(afflictions);
            calm();
            Protect(true);
        }
    }

    /// <summary>
    /// A slow look at the snakes' attack (only when asked; the snakes are told what to do, nobody is bitten): a ground
    /// snake rears up and strikes at a spot on open ground at a tenth of the speed, seen from close by at the side and
    /// then head on, twelve pictures through each strike; every tree snake asleep in its giant tree, from two distances;
    /// one climbing its tree (six pictures); and one, curled up, uncoiling and striking at a quarter of the speed.
    /// </summary>
    private IEnumerator SnakeAttackPreview()
    {
        Character local = Local;
        MapHandler.JumpToSegment(Segment.Tropics);
        yield return new WaitForSeconds(8f);
        for (float until = Time.time + 10f; Time.time < until && Snakes.All.Count == 0;)
        {
            yield return null;
        }
        List<Snake> grounds = Snakes.All.Where(s => s != null && !s.IsTree && AboveGround(s.Head) < 0.5f).OrderBy(s => Vector3.Distance(s.Head, local.Center)).ToList();
        Snake ground = grounds.FirstOrDefault();
        Snake tree = Snakes.All.Where(s => s != null && s.IsTree).OrderBy(s => Vector3.Distance(s.Head, local.Center)).FirstOrDefault();
        if (ground == null && tree == null)
        {
            Info("the snake preview is skipped", "no snakes here (run with -Biome and a map number that has the Tropics)");
            yield break;
        }
        float timeScale = Time.timeScale;
        Protect(true);
        // No bee ambush meanwhile (a swarm in front of the camera hides the snake).
        A17_BeeAmbush.HeldOff = true;
        try
        {
            // A stage: open, flat ground by a ground snake (the nearest that has one), with room for the scout to watch
            // from the side and from the front.
            Vector3? stage = null;
            Vector3 way = Vector3.forward;
            foreach (Snake candidate in grounds)
            {
                for (int angle = 0; angle < 360 && !stage.HasValue; angle += 30)
                {
                    Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    Vector3? centre = FlatOpen(candidate.Head + dir * 3f);
                    if (centre.HasValue && FlatOpen(centre.Value - dir * 1.2f).HasValue && FlatOpen(centre.Value + dir * 1.4f).HasValue
                        && FlatOpen(centre.Value + side * 1.5f).HasValue && FlatOpen(centre.Value + dir * 2.6f).HasValue)
                    {
                        stage = centre;
                        way = dir;
                    }
                }
                if (stage.HasValue)
                {
                    ground = candidate;
                    break;
                }
            }
            if (!stage.HasValue)
            {
                // The tree snakes are shown all the same.
                Info("the ground snake preview is skipped", $"no open, flat ground near any of the {grounds.Count} ground snakes");
            }
            else
            {
                Vector3 middle = stage.Value;
                Vector3 right = Vector3.Cross(Vector3.up, way);

                // A calm snake left to itself (it notices no scout), wandering about as with nobody near, the scout watching close by: its slither.
                Info("snake preview", "a ground snake that notices nobody wanders about for 20 s, close by (listen for its slither)");
                AloneSeen calmSeen = new AloneSeen();
                yield return WatchAlone(local, ground, 20f, "snake-calm", calmSeen);
                Info("calm snake", $"crawled {calmSeen.crawled:0.0} m in {calmSeen.seconds:0} s ({Snakes.WanderSpeed:0.0} m/s); its slither heard for {calmSeen.heard:0} s, up to {calmSeen.loudest:0.00} (of the snake's sound source); {calmSeen.doing}");

                // A Stick's hit (at a quarter of the speed): knocked back in a slide, it curls up for its 4 s, then uncoils and goes.
                Vector3 watchHit = FlatOpen(middle + right * 1.5f).Value;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, watchHit + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(1.5f);
                ground.Unaware = true;
                ground.PlaceAt(middle - way * 0.8f + Vector3.up * 0.06f, middle + way);
                yield return new WaitForSeconds(1.5f);
                Vector3 seeHit = middle + Vector3.up * 0.15f - local.Center;
                local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(seeHit.x, 0f, seeHit.z)).eulerAngles.y,
                    Mathf.Clamp(Mathf.Atan2(seeHit.y, new Vector2(seeHit.x, seeHit.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
                Info("snake preview", "a ground snake is hit by a Stick: knocked back, it curls up (a quarter of the speed)");
                Time.timeScale = 0.25f;
                Snakes.Hit(ground, StickItem.Knockdown, way, local);
                for (int frame = 1; frame <= 8; frame++)
                {
                    yield return new WaitForSeconds(0.12f);
                    Screenshot($"snake-hit-{frame}");
                }
                Time.timeScale = timeScale;
                yield return new WaitForSeconds(2f);
                Screenshot("snake-hit-curled");
                for (float until = Time.time + 4f; Time.time < until && ground.Doing == "Stunned";)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(1f);
                Screenshot("snake-hit-uncoiled");
                ground.Unaware = false;
                ground.Puppet = true;
                string[] views = { "side", "front" };
                foreach (string view in views)
                {
                    // Close by: beside it, back far enough to see it rear up where it lies and jump; or in front of where it ends.
                    Vector3 eye = view == "side" ? (FlatOpen(middle - way * 0.4f + right * 2.6f) ?? FlatOpen(middle + right * 1.5f)).Value : FlatOpen(middle + way * 2.6f).Value;
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, eye + Vector3.up * 1.2f, false);
                    yield return new WaitForSeconds(1.5f);
                    ground.PlaceAt(middle - way * 1.2f + Vector3.up * 0.06f, middle + way);
                    yield return new WaitForSeconds(1.5f);
                    Vector3 look = (view == "side" ? middle - way * 0.4f : middle) + Vector3.up * 0.25f - local.Center;
                    local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y,
                        Mathf.Clamp(Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
                    yield return new WaitForSeconds(0.5f);
                    Info("snake preview", $"a ground snake strikes, seen from the {view}, at a tenth of the speed");
                    Time.timeScale = 0.1f;
                    ground.StrikeAt(middle + way * 1.3f + Vector3.up * 0.35f);
                    // Rearing up (0.3 s), the strike (about 0.35 s) and the bite: twelve pictures over 0.72 s of its time.
                    AttackSeen seen = new AttackSeen();
                    for (int frame = 1; frame <= 12; frame++)
                    {
                        yield return WatchAttack(ground, 0.06f, seen);
                        Screenshot($"snake-attack-{view}-{frame}");
                    }
                    Info("snake preview", $"the ground snake's back turned from the sky where it reached out ({view}): up to {seen.rising:0} degrees rearing up, {seen.striking:0} striking; as the strike began a part of it moved {seen.handOver * 100f:0.0} cm");
                    Info("snake preview", $"the ground snake's frames at its worst, striking ({view}): {seen.strikingAt}");
                    yield return new WaitForSeconds(0.3f);
                    Time.timeScale = timeScale;
                    yield return new WaitForSeconds(1.5f);
                }
                ground.Puppet = false;
            }

            // Every tree snake asleep, curled up in its giant tree (kept asleep while the picture is taken).
            int treeShot = 0;
            foreach (Snake sleeper in Snakes.All.Where(s => s != null && s.IsTree).ToList())
            {
                treeShot++;
                Vector3? spot = StandNear(sleeper.Home, 3f, 6f, seeIt: true) ?? StandNear(sleeper.Home, 3f, 6f, seeIt: true, firm: false);
                if (!spot.HasValue)
                {
                    Info($"tree snake {treeShot}", $"no place to see it from near its spot at {sleeper.Home:F0}");
                    continue;
                }
                sleeper.Puppet = true;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot.Value + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(2f);
                Vector3 see = sleeper.Home + Vector3.up * 0.3f - local.Center;
                local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(see.x, 0f, see.z)).eulerAngles.y,
                    Mathf.Clamp(Mathf.Atan2(see.y, new Vector2(see.x, see.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
                yield return new WaitForSeconds(0.6f);
                Info($"tree snake {treeShot}", $"{sleeper.Doing} at {sleeper.Home:F0}, its way up {sleeper.WayLength:0} m, seen from {Vector3.Distance(local.Center, sleeper.Home):0.0} m");
                Screenshot($"tree-snake-{treeShot}-asleep");
                // A second look, from further off (another side of it, where there is one).
                Vector3? further = StandNear(sleeper.Home, 6.5f, 10f, seeIt: true) ?? StandNear(sleeper.Home, 6.5f, 10f, seeIt: true, firm: false);
                if (further.HasValue)
                {
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, further.Value + Vector3.up * 1.2f, false);
                    yield return new WaitForSeconds(2f);
                    Vector3 again = sleeper.Home + Vector3.up * 0.3f - local.Center;
                    local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(again.x, 0f, again.z)).eulerAngles.y,
                        Mathf.Clamp(Mathf.Atan2(again.y, new Vector2(again.x, again.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
                    yield return new WaitForSeconds(0.6f);
                    Screenshot($"tree-snake-{treeShot}-asleep-far");
                }
                sleeper.Puppet = false;
            }

            // A tree snake climbing its tree as snakes do: up its way from just above the foot, six pictures 2.5 s apart.
            Snake climber = Snakes.All.Where(s => s != null && s.IsTree && s.WayLength > 25f).OrderByDescending(s => s.WayLength).FirstOrDefault();
            if (climber != null)
            {
                climber.Unaware = true;
                climber.ClimbFrom(2f);
                yield return new WaitForSeconds(0.5f);
                Vector3? view = StandNear(climber.Head, 6f, 10f, seeIt: true) ?? StandNear(climber.Head, 6f, 12f, seeIt: true, firm: false) ?? StandNear(climber.Head, 6f, 12f);
                if (view.HasValue)
                {
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, view.Value + Vector3.up * 1.2f, false);
                    yield return new WaitForSeconds(1.5f);
                    Info("snake preview", $"a tree snake climbs its tree, winding round the trunk (its way {climber.WayLength:0} m)");
                    for (int frame = 1; frame <= 6; frame++)
                    {
                        // Wound round the trunk out of sight (or climbed far up): the scout goes after it, to where it shows.
                        if (Physics.Linecast(local.Center + Vector3.up * 0.4f, climber.Head, HelperFunctions.terrainMapMask) || Vector3.Distance(local.Center, climber.Head) > 12f)
                        {
                            Vector3? again = StandNear(climber.Head, 5f, 9f, seeIt: true) ?? StandNear(climber.Head, 5f, 12f, seeIt: true, firm: false);
                            if (again.HasValue)
                            {
                                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, again.Value + Vector3.up * 1.2f, false);
                                yield return new WaitForSeconds(1.2f);
                            }
                        }
                        Vector3 see = climber.Head - local.Center;
                        local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(see.x, 0f, see.z)).eulerAngles.y,
                            Mathf.Clamp(Mathf.Atan2(see.y, new Vector2(see.x, see.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
                        yield return new WaitForSeconds(0.3f);
                        Screenshot($"snake-tree-climb-{frame}");
                        yield return new WaitForSeconds(2.2f);
                    }
                }
                else
                {
                    Info("the climbing preview is skipped", $"no place to watch from near {climber.Head:F0}");
                }
                climber.Unaware = false;
                climber.GoHomeNow();
            }

            if (tree != null)
            {
                Vector3? near = StandNear(tree.Home, 2.5f, 6f, seeIt: true) ?? StandNear(tree.Home, 2.5f, 6f, seeIt: true, firm: false) ?? StandNear(tree.Home, 2.5f, 6f);
                if (!near.HasValue)
                {
                    Info("the tree snake preview is skipped", $"no place to watch from near its spot at {tree.Home:F0}");
                }
                else
                {
                    // Its attack at a tenth of the speed, as the ground snake's: curled up, it uncoils (an S, head raised), then
                    // strikes; once across the scout's view (from the side), once at the scout (head on). Twelve pictures each.
                    string[] treeViews = { "side", "front" };
                    foreach (string view in treeViews)
                    {
                        tree.Puppet = true;
                        tree.PlaceAt(tree.Home, tree.Home + Vector3.forward);
                        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, near.Value + Vector3.up * 1.2f, false);
                        yield return new WaitForSeconds(2.5f);
                        Vector3 look = tree.Home + Vector3.up * 0.3f - local.Center;
                        local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y,
                            Mathf.Clamp(Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
                        yield return new WaitForSeconds(0.5f);
                        Vector3 toScout = Vector3.ProjectOnPlane(local.Center - tree.Home, Vector3.up).normalized;
                        Vector3 across = Vector3.Cross(Vector3.up, toScout);
                        Vector3 aim = view == "side"
                            ? tree.Home + across * 2f + Vector3.up * 0.2f
                            : tree.Home + toScout * Mathf.Max(1f, Vector3.ProjectOnPlane(local.Center - tree.Home, Vector3.up).magnitude - 1.2f) + Vector3.up * 0.2f;
                        Info("snake preview", $"a tree snake uncoils and strikes, seen from the {view}, at a tenth of the speed");
                        Time.timeScale = 0.1f;
                        tree.UncoilAt(aim);
                        // Uncoiling (0.8 s) and the strike (about 0.4 s): twelve pictures over 1.2 s of its time.
                        AttackSeen seen = new AttackSeen();
                        for (int frame = 1; frame <= 12; frame++)
                        {
                            yield return WatchAttack(tree, 0.1f, seen);
                            Screenshot($"snake-tree-attack-{view}-{frame}");
                        }
                        Info("snake preview", $"the tree snake's back turned from the sky where it reached out ({view}): up to {seen.rising:0} degrees uncoiling, {seen.striking:0} striking; as the strike began a part of it moved {seen.handOver * 100f:0.0} cm");
                        Info("snake preview", $"the tree snake's frames at its worst ({view}): uncoiling {seen.risingAt}; striking {seen.strikingAt}");
                        yield return new WaitForSeconds(0.3f);
                        Time.timeScale = timeScale;
                        yield return new WaitForSeconds(1.5f);
                        tree.GoHomeNow();
                    }
                    tree.Puppet = false;
                }
            }
        }
        finally
        {
            Time.timeScale = timeScale;
            if (ground != null)
            {
                ground.Puppet = false;
            }
            if (tree != null)
            {
                tree.Puppet = false;
            }
            foreach (Snake left in Snakes.All.Where(s => s != null))
            {
                left.Unaware = false;
            }
            A17_BeeAmbush.HeldOff = false;
            Protect(false);
        }
    }

    /// <summary>Ground about level all round a point (2 m out), nothing to fall from.</summary>
    private static bool RoomAround(Vector3 at)
    {
        for (int angle = 0; angle < 360; angle += 60)
        {
            Vector3 round = at + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2f;
            if (!Physics.Raycast(round + Vector3.up * 2f, Vector3.down, out RaycastHit floor, 4f, HelperFunctions.terrainMapMask) || Mathf.Abs(floor.point.y - at.y) > 0.8f)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The open, flat ground at a point (room to stand, not inside anything), or null.</summary>
    private static Vector3? FlatOpen(Vector3 near)
    {
        if (!Physics.Raycast(near + Vector3.up * 4f, Vector3.down, out RaycastHit floor, 10f, HelperFunctions.terrainMapMask) || floor.normal.y < 0.9f
            || Physics.CheckCapsule(floor.point + Vector3.up * 0.5f, floor.point + Vector3.up * 1.6f, 0.35f, HelperFunctions.terrainMapMask)
            || InsideSomething(floor.point + Vector3.up) || Hazard(floor.point + Vector3.up, 1.5f))
        {
            return null;
        }
        return floor.point;
    }

    private static bool forcingInput;
    private static Vector2 forcedMove;
    private static bool forcedSprint;

    /// <summary>While a speed probe runs: the local scout's keys held as the probe says (after the game reads the real ones).</summary>
    private static void ForceInput(CharacterInput __instance)
    {
        Character local = Character.localCharacter;
        if (forcingInput && local != null && local.input == __instance)
        {
            __instance.movementInput = forcedMove;
            __instance.sprintIsPressed = forcedSprint;
        }
    }

    /// <summary>
    /// How fast a scout walks and runs (only when asked): along 32 m of flat, open ground on the beach the scout walks for
    /// 4 s, then sprints on for 4 s (full stamina, nothing carried); the speed is timed over the last 2 s of each, after it
    /// has got going, and set against the snakes' (calm and hunting).
    /// </summary>
    private IEnumerator SpeedProbe()
    {
        Character local = Local;
        MapHandler.JumpToSegment(Segment.Beach);
        yield return new WaitForSeconds(6f);
        Vector3 start = local.Center;
        Vector3 bestWay = Vector3.zero;
        Vector3 from0 = start;
        for (int place = 0; place < 9 && bestWay == Vector3.zero; place++)
        {
            Vector3 origin = place == 0 ? start : start + Quaternion.Euler(0f, place * 45f, 0f) * Vector3.forward * 20f;
            if (!Physics.Raycast(origin + Vector3.up * 20f, Vector3.down, out RaycastHit there, 60f, HelperFunctions.terrainMapMask) || InsideSomething(there.point + Vector3.up))
            {
                continue;
            }
            for (int angle = 0; angle < 360 && bestWay == Vector3.zero; angle += 15)
            {
                Vector3 way = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                bool flat = true;
                for (float d = 0f; d <= 32f && flat; d += 1f)
                {
                    Vector3 p = there.point + way * d;
                    flat = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit floor, 8f, HelperFunctions.terrainMapMask) && floor.normal.y >= 0.85f
                        && Mathf.Abs(floor.point.y - there.point.y) < 1.5f
                        && !Physics.CheckCapsule(floor.point + Vector3.up * 0.6f, floor.point + Vector3.up * 1.6f, 0.35f, HelperFunctions.terrainMapMask);
                }
                if (flat)
                {
                    bestWay = way;
                    from0 = there.point;
                }
            }
        }
        if (bestWay == Vector3.zero)
        {
            Info("the speed probe is skipped", $"no 32 m of flat open ground near {start:F0}");
            yield break;
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, from0 + Vector3.up * 1.2f, false);
        yield return new WaitForSeconds(1.5f);
        Harmony keys = new Harmony("UltimateAscent.SmokeTest.keys");
        keys.Patch(AccessTools.Method(typeof(CharacterInput), "Sample"), postfix: new HarmonyMethod(typeof(Runner), nameof(ForceInput)));
        float walk = 0f, run = 0f;
        try
        {
            for (int leg = 0; leg < 2; leg++)
            {
                Vector3 way = bestWay;
                local.data.lookValues = new Vector2(Quaternion.LookRotation(way).eulerAngles.y, 0f);
                local.data.currentStamina = 1f;
                forcedMove = new Vector2(0f, 1f);
                forcedSprint = leg == 1;
                forcingInput = true;
                yield return new WaitForSeconds(2f);
                Vector3 from = local.Center;
                float began = Time.time;
                yield return new WaitForSeconds(2f);
                float speed = Vector3.ProjectOnPlane(local.Center - from, Vector3.up).magnitude / (Time.time - began);
                if (leg == 0)
                {
                    walk = speed;
                }
                else
                {
                    run = speed;
                }
                forcingInput = false;
                yield return new WaitForSeconds(1f);
            }
        }
        finally
        {
            forcingInput = false;
            keys.UnpatchSelf();
        }
        Info("how fast a scout goes", $"walking {walk:0.00} m/s, sprinting {run:0.00} m/s (flat ground on the beach, nothing carried, full stamina); snakes: calm {Snakes.WanderSpeed:0.0} m/s, "
            + $"hunting: ground snakes {Snakes.GroundChaseSpeed:0.0} m/s, tree snakes {Snakes.TreeChaseSpeed:0.0} m/s, keeping off {Snakes.CircleSpeed:0.0} m/s");
    }

    /// <summary>
    /// What makes the scorpion's tail shimmer after its sting (only when asked): its prefab's parts, with their particle
    /// systems (and how they play), renderers' materials and shaders, and its animator's clips with their events.
    /// </summary>
    private IEnumerator ScorpionProbe()
    {
        yield return null;
        Item scorpion = SingletonAsset<ItemDatabase>.Instance.Objects.FirstOrDefault(i => i != null && i.GetComponentInChildren<Scorpion>(true) != null);
        if (scorpion == null)
        {
            Info("scorpion probe", "no scorpion in the item database");
            yield break;
        }
        foreach (Transform part in scorpion.GetComponentsInChildren<Transform>(true))
        {
            List<string> what = new List<string>();
            foreach (Component c in part.GetComponents<Component>())
            {
                if (c is ParticleSystem ps)
                {
                    ParticleSystem.MainModule main = ps.main;
                    ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                    what.Add($"ParticleSystem(playOnAwake {main.playOnAwake}, loop {main.loop}, duration {main.duration:0.##}, rate {ps.emission.rateOverTimeMultiplier:0.#}, bursts {ps.emission.burstCount}, "
                        + $"life {main.startLifetimeMultiplier:0.##}, size {main.startSizeMultiplier:0.###}, colour {main.startColor.color}, material '{psr?.sharedMaterial?.name}' shader '{psr?.sharedMaterial?.shader?.name}')");
                }
                else if (c is Renderer r && !(c is ParticleSystemRenderer))
                {
                    what.Add($"{c.GetType().Name}(materials [{string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => $"'{m.name}' shader '{m.shader?.name}'"))}])");
                }
                else if (c is Animator anim && anim.runtimeAnimatorController != null)
                {
                    what.Add($"Animator('{anim.runtimeAnimatorController.name}': clips [{string.Join("; ", anim.runtimeAnimatorController.animationClips.Where(a => a != null).Select(a => $"{a.name} {a.length:0.##} s events [{string.Join(", ", a.events.Select(e => $"{e.functionName}@{e.time:0.##}({e.stringParameter}|{e.objectReferenceParameter?.name})"))}]"))}])");
                }
                else if (!(c is Transform))
                {
                    what.Add(c.GetType().Name);
                }
            }
            Info($"scorpion part '{PathOf(part, scorpion.transform)}'", string.Join("; ", what));
        }
        // The materials' animatable properties (a shimmer may be a material property the attack animates).
        foreach (Renderer r in scorpion.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material m in r.sharedMaterials.Where(m => m != null))
            {
                Shader shader = m.shader;
                if (shader == null)
                {
                    continue;
                }
                List<string> properties = new List<string>();
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    properties.Add(shader.GetPropertyName(i));
                }
                Info($"scorpion material '{m.name}'", $"shader '{shader.name}': {string.Join(", ", properties)}");
            }
        }
        // Every serialized setting of its parts' scripts (for the beach crabs built from it): the Mob's speeds, reach and
        // timings, its sounds and their clips, how it is held, its collider and how big it is.
        foreach (Component part in scorpion.GetComponentsInChildren<Component>(true))
        {
            if (part == null || part is Transform || part is Renderer || part is ParticleSystem || part is ParticleSystemRenderer || part is MeshFilter)
            {
                continue;
            }
            string fields = SettingsOf(part);
            if (fields.Length > 0)
            {
                Info($"scorpion settings '{PathOf(part.transform, scorpion.transform)}' {part.GetType().Name}", fields);
            }
        }
        foreach (SFX_PlayOneShot sfx in scorpion.GetComponentsInChildren<SFX_PlayOneShot>(true))
        {
            IEnumerable<SFX_Instance> all = new[] { sfx.sfx }.Concat(sfx.sfxs ?? new SFX_Instance[0]).Where(x => x != null);
            Info($"scorpion sound '{PathOf(sfx.transform, scorpion.transform)}'", $"on start {sfx.playOnStart}, on enable {sfx.playOnEnable}; "
                + string.Join("; ", all.Select(x => $"'{x.name}' clips [{string.Join(", ", (x.clips ?? new AudioClip[0]).Where(c => c != null).Select(c => $"{c.name} {c.length:0.00} s"))}] {SettingsOf(x.settings)}")));
        }
        foreach (Renderer r in scorpion.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)))
        {
            Info($"scorpion size '{PathOf(r.transform, scorpion.transform)}'", $"bounds {r.bounds.size:F3} at {scorpion.transform.InverseTransformPoint(r.bounds.center):F3}; local position {r.transform.localPosition:F3}, rotation {r.transform.localEulerAngles:F1}, scale {r.transform.lossyScale:F3}");
        }
        foreach (Collider c in scorpion.GetComponentsInChildren<Collider>(true))
        {
            Info($"scorpion collider '{PathOf(c.transform, scorpion.transform)}'", $"{c.GetType().Name} bounds {c.bounds.size:F3}, local position {c.transform.localPosition:F3}, rotation {c.transform.localEulerAngles:F1}, scale {c.transform.lossyScale:F3}"
                + (c is MeshCollider mc ? $", convex {mc.convex}, mesh '{mc.sharedMesh?.name}' {mc.sharedMesh?.bounds.size:F3}" : ""));
        }
        foreach (Transform t in scorpion.GetComponentsInChildren<Transform>(true).Where(t => t.parent == scorpion.transform || t.name.StartsWith("Hand") || t.name == "VisualParent" || t.name == "ScorpionModel"))
        {
            Info($"scorpion pose '{PathOf(t, scorpion.transform)}'", $"local position {t.localPosition:F3}, rotation {t.localEulerAngles:F1}, scale {t.localScale:F3}");
        }
    }

    /// <summary>A script's serialized settings (public or [SerializeField], its own and its bases' up to MonoBehaviour), as name=value.</summary>
    private static string SettingsOf(object part)
    {
        if (part == null)
        {
            return "";
        }
        List<string> values = new List<string>();
        for (Type type = part.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(MonoBehaviourPun) && type != typeof(MonoBehaviourPunCallbacks) && type != typeof(object) && type != typeof(ScriptableObject); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsNotSerialized || (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null) || typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }
                object value;
                try
                {
                    value = field.GetValue(part);
                }
                catch
                {
                    continue;
                }
                values.Add($"{field.Name}={Shown(value)}");
            }
        }
        return string.Join(", ", values);
    }

    private static string Shown(object value)
    {
        switch (value)
        {
            case null:
                return "null";
            case UnityEngine.Object o:
                return o == null ? "null" : $"'{o.name}'";
            case float f:
                return f.ToString("0.###");
            case Vector3 v:
                return v.ToString("F3");
            case string text:
                return $"\"{text}\"";
            case System.Collections.IList list when !(value is string):
                return $"[{string.Join(" ", list.Cast<object>().Take(12).Select(Shown))}{(list.Count > 12 ? " ..." : "")}]";
            default:
                Type type = value.GetType();
                if (!type.IsPrimitive && !type.IsEnum && type.IsSerializable && type.Namespace == null)
                {
                    return "{" + SettingsOf(value) + "}";
                }
                return value.ToString();
        }
    }

    /// <summary>
    /// A look around for the tree snakes' giant trees and for a sound of an item burning away (only when asked): the
    /// Tropics' tall meshes, grouped by mesh name with their size, pictures of the tallest kinds, and the game's fire,
    /// poof and breaking sounds with their clips.
    /// </summary>
    private IEnumerator TropicsProbe()
    {
        Character local = Local;
        MapHandler.JumpToSegment(Segment.Tropics);
        yield return new WaitForSeconds(8f);
        MapHandler map = Singleton<MapHandler>.Instance;
        Transform segment = map != null && map.segments.Length > 1 && map.segments[1].segmentParent != null ? map.segments[1].segmentParent.transform : null;
        if (segment == null)
        {
            Info("probe", "no Tropics segment");
            yield break;
        }
        // Tall meshes, by mesh name: how many, how tall and wide, where, and the path to them.
        Dictionary<string, List<MeshRenderer>> tall = new Dictionary<string, List<MeshRenderer>>();
        foreach (MeshRenderer renderer in segment.GetComponentsInChildren<MeshRenderer>(false))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || renderer.bounds.size.y < 14f)
            {
                continue;
            }
            string name = filter.sharedMesh.name;
            if (!tall.TryGetValue(name, out List<MeshRenderer> list))
            {
                tall[name] = list = new List<MeshRenderer>();
            }
            list.Add(renderer);
        }
        List<KeyValuePair<string, List<MeshRenderer>>> kinds = tall.OrderByDescending(k => k.Value.Max(r => r.bounds.size.y)).ToList();
        foreach (KeyValuePair<string, List<MeshRenderer>> kind in kinds)
        {
            MeshRenderer biggest = kind.Value.OrderByDescending(r => r.bounds.size.y).First();
            Collider[] colliders = biggest.GetComponentsInChildren<Collider>().Concat(biggest.GetComponentsInParent<Collider>()).Distinct().ToArray();
            Info($"tall mesh '{kind.Key}'",
                $"{kind.Value.Count}x, {kind.Value.Min(r => r.bounds.size.y):0}-{kind.Value.Max(r => r.bounds.size.y):0} m tall, {biggest.bounds.size.x:0}x{biggest.bounds.size.z:0} m wide; biggest at {biggest.bounds.center:F0}, path '{PathOf(biggest.transform, segment)}', "
                + $"material '{biggest.sharedMaterial?.name}', colliders [{string.Join(", ", colliders.Select(c => $"{c.GetType().Name} '{c.name}' layer {LayerMask.LayerToName(c.gameObject.layer)} {c.bounds.size.y:0} m{(c.isTrigger ? " trigger" : "")}"))}]");
        }
        // Pictures of the five tallest kinds, from 45 m away.
        for (int i = 0; i < Mathf.Min(5, kinds.Count); i++)
        {
            Bounds bounds = kinds[i].Value.OrderByDescending(r => r.bounds.size.y).First().bounds;
            Vector3 flat = Vector3.ProjectOnPlane(local.Center - bounds.center, Vector3.up);
            Vector3 from = bounds.center + (flat.sqrMagnitude > 1f ? flat.normalized : Vector3.forward) * (Mathf.Max(bounds.extents.x, bounds.extents.z) + 35f);
            if (Physics.Raycast(from + Vector3.up * 200f, Vector3.down, out RaycastHit floor, 400f, HelperFunctions.terrainMapMask))
            {
                from = floor.point;
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, from + Vector3.up * 1.5f, false);
            yield return new WaitForSeconds(1.5f);
            Vector3 to = bounds.center - local.Center;
            local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y,
                Mathf.Clamp(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
            yield return new WaitForSeconds(0.6f);
            Info($"probe picture {i + 1}", $"'{kinds[i].Key}' at {bounds.center:F0}, {bounds.size.y:0} m tall, seen from {local.Center:F0}");
            Screenshot($"probe-tall-{i + 1}");
        }

        // Sounds: the campfire's, the poof of a warp, things that break, and every loaded sound with a fitting name.
        string Clips(SFX_Instance sound) => sound == null ? "none" : $"'{sound.name}' [{string.Join(", ", (sound.clips ?? new AudioClip[0]).Where(c => c != null).Select(c => $"{c.name} {c.length:0.00} s"))}] volume {sound.settings.volume:0.##}";
        Campfire campfire = FindObjectsByType<Campfire>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (campfire != null)
        {
            foreach (FieldInfo field in typeof(Campfire).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(SFX_Instance))
                {
                    Info($"campfire sound {field.Name}", Clips(field.GetValue(campfire) as SFX_Instance));
                }
                else if (field.FieldType == typeof(SFX_Instance[]) && field.GetValue(campfire) is SFX_Instance[] many)
                {
                    Info($"campfire sounds {field.Name}", string.Join("; ", many.Select(Clips)));
                }
            }
        }
        if (local.poofSFX != null)
        {
            Info("warp poof sounds", string.Join("; ", local.poofSFX.Select(Clips)));
        }
        string[] words = { "fire", "burn", "flame", "ash", "crumb", "poof", "whoosh", "ignit", "torch", "lantern", "explo", "sizzl", "crack", "break", "smoke", "dust", "puff", "cook", "lava", "ember", "match", "flare" };
        foreach (SFX_Instance sound in Resources.FindObjectsOfTypeAll<SFX_Instance>().Where(s => s != null && words.Any(w => s.name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).GroupBy(s => s.name).Select(g => g.First()).OrderBy(s => s.name))
        {
            Info("sound", Clips(sound));
        }
    }

    private static string PathOf(Transform t, Transform stop)
    {
        List<string> names = new List<string>();
        for (; t != null && t != stop; t = t.parent)
        {
            names.Add(t.name);
        }
        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>
    /// The scout goes 25-45 m away (or as far as asked) from a snake that has bitten them, out of its chase (it would bite
    /// again 3 s later): to the open ground there farthest from every snake.
    /// </summary>
    private static void StepAway(Character local, Vector3 snake, float nearest = 25f, float farthest = 45f)
    {
        Vector3? best = null;
        float bestRoom = -1f;
        for (int angle = 0; angle < 360; angle += 30)
        {
            for (float reach = nearest; reach <= farthest; reach += 10f)
            {
                Vector3 there = local.Center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * reach;
                // The lowest open, flat ground there with room all round, down to 90 m below (never a perch up a tree, where a
                // collapse rolls the scout off; not further down, or the snakes near are left with nobody within their 120 m).
                Vector3? ground = null;
                foreach (RaycastHit hit in Physics.RaycastAll(there + Vector3.up * 30f, Vector3.down, 120f, HelperFunctions.terrainMapMask).OrderByDescending(h => h.distance))
                {
                    if (hit.normal.y >= 0.8f && FlatOpen(hit.point).HasValue && RoomAround(hit.point) && !Hazard(hit.point + Vector3.up, 2f))
                    {
                        ground = hit.point;
                        break;
                    }
                }
                if (!ground.HasValue)
                {
                    continue;
                }
                float room = Snakes.All.Where(s => s != null).Select(s => Vector3.Distance(s.Head, ground.Value)).DefaultIfEmpty(999f).Min();
                if (room > bestRoom)
                {
                    bestRoom = room;
                    best = ground.Value;
                }
            }
        }
        // None found: the scout stays where they are (never sent into the air).
        if (best.HasValue)
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, best.Value + Vector3.up * 1.2f, false);
        }
    }

    /// <summary>
    /// One of the game's poison cures reaching a scout paralysed by a tree snake (17), the game's way: eaten or used from
    /// the hand, fed (a Napberry: the game's feeding, as it runs on the fed scout's game), a Blowgun's dart landing on them
    /// (the dart's own landing on their game), or a Remedy Fungus thrown at their feet. Statuses are held meanwhile and
    /// what the cure brings is taken off after (a Napberry or a dart sends the scout to sleep, which is death at 24).
    /// </summary>
    private IEnumerator CureParalysis(Character local, string prefabName, string how, bool fromBite)
    {
        Item prefab = ItemByName(prefabName);
        if (prefab == null)
        {
            Fail($"the {prefabName} is in the item database");
            yield break;
        }
        CharacterAfflictions afflictions = local.refs.afflictions;
        if (!fromBite)
        {
            // Paralysed as by a tree snake's bite.
            SnakeParalysis.Bite(local);
        }
        yield return new WaitForSeconds(0.3f);
        bool before = SnakeParalysis.Left(local) > 0f && SnakeParalysis.IconShowing;
        int curesBefore = SnakeParalysis.CuresSeen;
        List<Peak.Afflictions.Affliction> had = afflictions.afflictionList.ToList();
        bool locked = local.statusesLocked;
        local.statusesLocked = true;
        if (how == "thrown at the feet")
        {
            // Thrown hard at the ground a step ahead, the game's throw, facing where it lands clear: it breaks there.
            float yaw = local.data.lookValues.x;
            for (int angle = 0; angle < 360; angle += 45)
            {
                Vector3 way = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 at = local.Center + way * 1.2f;
                if (!Physics.CheckSphere(at, 0.4f, HelperFunctions.terrainMapMask) && Physics.Raycast(at, Vector3.down, out RaycastHit floor, 3f, HelperFunctions.terrainMapMask) && floor.normal.y > 0.6f)
                {
                    yaw = Quaternion.LookRotation(way).eulerAngles.y;
                    break;
                }
            }
            yield return SpawnIntoHand(prefab);
            if (held != null)
            {
                local.data.lookValues = new Vector2(yaw, -55f);
                yield return new WaitForSeconds(0.4f);
                byte slot = local.refs.items.currentSelectedSlot.Value;
                local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 1f, slot, held.transform.position, Vector3.zero, held.transform.rotation, local.player.GetItemSlot(slot).data, false);
                held = null;
            }
            for (float until = Time.time + 5f; Time.time < until && SnakeParalysis.Left(local) > 0f;)
            {
                yield return null;
            }
            // What became of it (should it not have cured): the cloud and what it is made of, or the shroom still whole.
            foreach (Transform cloud in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.StartsWith(A14_Items.RemedyCloudParent, System.StringComparison.Ordinal) && (t.parent == null || !t.parent.name.StartsWith(A14_Items.RemedyCloudParent, System.StringComparison.Ordinal))))
            {
                Info("the Remedy Fungus cloud", $"{cloud.name} {Vector3.Distance(cloud.position, local.Center):0.0} m off: " + string.Join(", ", cloud.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct()));
            }
            foreach (Item whole in Item.ALL_ITEMS.Where(i => i != null && i.itemID == prefab.itemID))
            {
                Info("the Remedy Fungus", $"still whole, {Vector3.Distance(whole.transform.position, local.Center):0.0} m off, {whole.itemState}, moving {(whole.rig != null ? whole.rig.linearVelocity.magnitude : -1f):0.0} m/s");
            }
        }
        else
        {
            yield return SpawnIntoHand(prefab);
            Item item = held;
            if (item != null && how == "fed")
            {
                local.FeedItem(item);
                yield return new WaitForSeconds(0.8f);
            }
            else if (item != null && how == "darted")
            {
                Action_RaycastDart dart = item.GetComponent<Action_RaycastDart>();
                if (dart != null)
                {
                    dart.photonView.RPC("RPC_DartImpact", RpcTarget.All, local.photonView.ViewID, local.Center + local.data.lookDirection * 4f, local.Center);
                }
                yield return new WaitForSeconds(0.8f);
            }
            else if (item != null)
            {
                yield return EatHeld();
            }
        }
        yield return null;
        bool cured = SnakeParalysis.Left(local) <= 0f;
        bool dropGone = !SnakeParalysis.IconShowing;
        Screenshot("snake-paralysis-cured-" + prefabName.Replace(' ', '-'));
        foreach (Peak.Afflictions.Affliction brought in afflictions.afflictionList.Where(a => !had.Contains(a)).ToList())
        {
            afflictions.RemoveAffliction(brought);
        }
        ClearStatuses(afflictions);
        local.statusesLocked = locked;
        Check($"a {Plain(prefab.GetItemName())} ({how}) ends a tree snake's paralysis, and the drop goes (17)",
            before && cured && dropGone && SnakeParalysis.CuresSeen > curesBefore,
            $"paralysed before {before}; after: over {cured}, the drop gone {dropGone}, ended by '{SnakeParalysis.LastCure}'");
        SnakeParalysis.Stop();
        if (how != "thrown at the feet")
        {
            yield return PutAway(prefab);
        }
    }

    /// <summary>How far a snake's back may turn from the sky where it reaches out (degrees): on its side is 90.</summary>
    private const float RaisedRollLimit = 35f;

    /// <summary>
    /// Waterfalls (every ascent): each stream's and splash's hitbox against the water it belongs to (how wide it is, where it
    /// lands); a scout dropped into a stream, ours against the game's (knocked down once, pushed down and out of it, never
    /// held or flung); and, where a splash is about, a scout stepping into it at its edge and where the water lands.
    /// </summary>
    private IEnumerator WaterfallLook()
    {
        Character local = Local;
        // The waterfalls lie in a part of the climb further on (the rest of the map sleeps meanwhile): there first.
        int inMap = FindObjectsByType<WaterfallTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        foreach (Segment part in new[] { Segment.Tropics, Segment.Alpine, Segment.Caldera })
        {
            if (inMap == 0 || FindObjectsByType<WaterfallTrigger>(FindObjectsSortMode.None).Any(w => w != null && w.isActiveAndEnabled))
            {
                break;
            }
            MapHandler.JumpToSegment(part);
            yield return new WaitForSeconds(8f);
        }
        List<WaterfallTrigger> streams = FindObjectsByType<WaterfallTrigger>(FindObjectsSortMode.None).Where(w => w != null && w.isActiveAndEnabled).ToList();
        List<WaterfallPusher> splashes = FindObjectsByType<WaterfallPusher>(FindObjectsSortMode.None).Where(w => w != null && w.isActiveAndEnabled).ToList();
        List<WaterFallConfig> waters = FindObjectsByType<WaterFallConfig>(FindObjectsSortMode.None).Where(w => w != null && w.mesh != null).ToList();
        Info("waterfalls in this map", $"{inMap} streams in the map; about here ({Map.CurrentPlace}): {streams.Count} streams, {splashes.Count} splashes, {waters.Count} waterfalls the map placed (each with its water)");
        if (streams.Count == 0)
        {
            Info("the waterfall look is skipped", $"no waterfalls here ({Map.CurrentPlace})");
            yield break;
        }

        // A capsule trigger as it lies in the world: its middle, the way it runs, its radius and its length.
        (Vector3 middle, Vector3 axis, float radius, float length) Capsule(CapsuleCollider capsule)
        {
            Transform t = capsule.transform;
            Vector3 s = t.lossyScale;
            Vector3 axis = capsule.direction == 0 ? t.right : capsule.direction == 2 ? t.forward : t.up;
            float along = Mathf.Abs(capsule.direction == 0 ? s.x : capsule.direction == 2 ? s.z : s.y);
            float across = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)) : capsule.direction == 2 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)) : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            float radius = capsule.radius * across;
            return (t.TransformPoint(capsule.center), axis.normalized, radius, Mathf.Max(capsule.height * along, radius * 2f));
        }
        // Water meshes about (by their own name or their material's), for streams no waterfall of the map's stands by.
        List<Renderer> wet = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r != null && r.enabled && r is MeshRenderer
            && (r.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 || r.name.IndexOf("fall", StringComparison.OrdinalIgnoreCase) >= 0
                || (r.sharedMaterial != null && (r.sharedMaterial.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 || r.sharedMaterial.name.IndexOf("fall", StringComparison.OrdinalIgnoreCase) >= 0)))).ToList();
        Renderer WetNear(Vector3 at, float within)
        {
            return wet.Where(r => r.bounds.SqrDistance(at) < within * within).OrderBy(r => r.bounds.SqrDistance(at)).ThenBy(r => r.bounds.size.magnitude).FirstOrDefault();
        }
        // How wide the water is where a hitbox is: its mesh's points within 1.5 m of the hitbox's middle along the stream,
        // measured across it (the widest way and the thinnest), and how far the water's middle lies off the hitbox's line.
        string WaterAcross(Renderer mesh, Vector3 middle, Vector3 axis, float radius)
        {
            MeshFilter filter = mesh.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
            {
                return "its mesh cannot be read";
            }
            Vector3 u = Vector3.ProjectOnPlane(Vector3.up, axis);
            u = u.sqrMagnitude > 1e-4f ? u.normalized : Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            int counted = 0;
            Transform t = mesh.transform;
            foreach (Vector3 point in filter.sharedMesh.vertices)
            {
                Vector3 off = t.TransformPoint(point) - middle;
                if (Mathf.Abs(Vector3.Dot(off, axis)) > 1.5f || off.sqrMagnitude > 100f)
                {
                    continue;
                }
                float a = Vector3.Dot(off, u), b = Vector3.Dot(off, v);
                minU = Mathf.Min(minU, a);
                maxU = Mathf.Max(maxU, a);
                minV = Mathf.Min(minV, b);
                maxV = Mathf.Max(maxV, b);
                counted++;
            }
            if (counted < 3)
            {
                return "no water mesh points across the hitbox's middle";
            }
            float wide = Mathf.Max(maxU - minU, maxV - minV), thin = Mathf.Min(maxU - minU, maxV - minV);
            float offLine = new Vector2((maxU + minU) * 0.5f, (maxV + minV) * 0.5f).magnitude;
            return $"there the water is {wide:0.0} m wide and {thin:0.0} m thick ({counted} points), its middle {offLine:0.0} m off the hitbox's line: "
                + $"the hitbox reaches up to {radius - wide * 0.5f + offLine:0.0} m beyond its sides and {radius - thin * 0.5f + offLine:0.0} m out in front of and behind it";
        }
        // The water a trigger belongs to: the nearest waterfall the map placed, its sheet's width and height, where it lands
        // (or else the nearest water mesh: how far the hitbox reaches out of it).
        string AgainstWater(Vector3 middle, Vector3 axis, float radius, float length)
        {
            WaterFallConfig water = waters.Where(w => Vector3.Distance(w.mesh.bounds.center, middle) < 25f).OrderBy(w => Vector3.Distance(w.mesh.bounds.center, middle)).FirstOrDefault();
            if (water == null)
            {
                Renderer mesh = WetNear(middle, 12f);
                if (mesh == null)
                {
                    return "no water mesh within 12 m";
                }
                Bounds box = mesh.bounds;
                Vector3 inside = box.ClosestPoint(middle);
                return $"the nearest water mesh '{mesh.name}' ({(mesh.sharedMaterial != null ? mesh.sharedMaterial.name : "-")}), a box {box.size.x:0.0} x {box.size.y:0.0} x {box.size.z:0.0} m, "
                    + $"{Vector3.Distance(inside, middle):0.0} m from the hitbox's middle; the hitbox {radius * 2f:0.0} m across and {length:0.0} m long; " + WaterAcross(mesh, middle, axis, radius);
            }
            Bounds local = water.mesh.localBounds;
            Vector3 scale = water.mesh.transform.lossyScale;
            float width = Mathf.Abs(local.size.x * scale.x);
            float tall = Mathf.Abs(local.size.y * scale.y);
            float deep = Mathf.Abs(local.size.z * scale.z);
            Bounds world = water.mesh.bounds;
            float lands = water.endRock != null ? water.endRock.position.y : world.min.y;
            return $"its water {Vector3.Distance(world.center, middle):0.0} m off: a sheet {width:0.0} m wide, {deep:0.0} m thick, {tall:0.0} m tall ({world.max.y - lands:0.0} m from its top to where it lands); "
                + $"the hitbox {radius * 2f:0.0} m across ({radius * 2f - Mathf.Max(width, deep):+0.0;-0.0} m wider than the water), from {middle.y - length * 0.5f - lands:+0.0;-0.0} m to {middle.y + length * 0.5f - lands:+0.0;-0.0} m about where the water lands";
        }
        foreach (WaterfallTrigger stream in streams.OrderBy(w => Vector3.Distance(w.transform.position, local.Center)).Take(4))
        {
            CapsuleCollider capsule = stream.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                continue;
            }
            (Vector3 middle, Vector3 axis, float radius, float length) = Capsule(capsule);
            Info("a waterfall stream (the game's WaterfallTrigger)", $"at {middle:F0}, {Vector3.Distance(middle, local.Center):0} m off, running {Vector3.Angle(axis, Vector3.up):0} degrees from upright; " + AgainstWater(middle, axis, radius, length));
        }
        foreach (WaterfallPusher splash in splashes.OrderBy(w => Vector3.Distance(w.transform.position, local.Center)).Take(3))
        {
            CapsuleCollider capsule = splash.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                continue;
            }
            (Vector3 middle, Vector3 axis, float radius, float length) = Capsule(capsule);
            Info("a waterfall splash (the game's WaterfallPusher)", $"at {middle:F0}, {Vector3.Distance(middle, local.Center):0} m off, running {Vector3.Angle(axis, Vector3.up):0} degrees from upright; " + AgainstWater(middle, axis, radius, length));
        }

        // Ours against the game's (every ascent): a scout dropped into the falling water, up the stream from a hitbox's
        // middle, ours first. Ours: knocked down once, pushed down the stream and out of it, out within 2 s, never flung;
        // the game's: knocked down again and again while in it, and held or flung.
        WaterfallTrigger into = streams.Where(w => w.GetComponent<CapsuleCollider>() != null).OrderBy(w => Vector3.Distance(w.transform.position, local.Center)).FirstOrDefault();
        if (into == null)
        {
            Info("the waterfall checks are skipped", "no stream hitbox here is a capsule");
            yield break;
        }
        CapsuleCollider intoCapsule = into.GetComponent<CapsuleCollider>();
        (Vector3 dropMiddle, Vector3 dropAxis, float dropRadius, float dropLength) = Capsule(intoCapsule);
        Vector3 downStream = dropAxis.y > 0f ? -dropAxis : dropAxis;
        // The highest point inside it with open air round it, on its line or up to 2 m off it (a stream often runs down
        // over rock, its line in the rock).
        Vector3 dropAt = dropMiddle;
        Vector3 across = Vector3.ProjectOnPlane(Vector3.up, downStream).sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(Vector3.up, downStream).normalized : Vector3.ProjectOnPlane(Vector3.forward, downStream).normalized;
        bool found = false;
        for (float k = -0.4f; k <= 0.4f && !found; k += 0.05f)
        {
            foreach (float r in new[] { 0f, 1f, 2f })
            {
                for (int turn = 0; turn < 360 && !found; turn += r > 0f ? 45 : 360)
                {
                    Vector3 at = dropMiddle + downStream * dropLength * k + Quaternion.AngleAxis(turn, downStream) * across * r;
                    if (!Physics.CheckSphere(at, 0.6f, HelperFunctions.terrainMapMask))
                    {
                        dropAt = at;
                        found = true;
                    }
                }
                if (found)
                {
                    break;
                }
            }
        }
        float dropHalf = Mathf.Max(0f, dropLength * 0.5f - dropRadius);
        float FromLine(Vector3 at) => Vector3.Distance(at, dropMiddle + dropAxis * Mathf.Clamp(Vector3.Dot(at - dropMiddle, dropAxis), -dropHalf, dropHalf));
        int hipLayer = local.GetBodypart(BodypartType.Hip).gameObject.layer;
        Info("the stream the scout is dropped into", $"at {dropMiddle:F0}, its hitbox on layer '{LayerMask.LayerToName(into.gameObject.layer)}' (meets the scout's hip layer '{LayerMask.LayerToName(hipLayer)}': {!Physics.GetIgnoreLayerCollision(into.gameObject.layer, hipLayer)}), "
            + $"enabled {intoCapsule.enabled}, a trigger {intoCapsule.isTrigger}; dropped {Vector3.Distance(dropAt, dropMiddle):0.0} m from its middle, in open air {!Physics.CheckSphere(dropAt, 0.7f, HelperFunctions.terrainMapMask)}");
        // In any stream's hitbox (the scout's middle, give or take their size).
        bool InWater(Vector3 at)
        {
            foreach (WaterfallTrigger w in streams)
            {
                CapsuleCollider c = w != null ? w.GetComponent<CapsuleCollider>() : null;
                if (c == null)
                {
                    continue;
                }
                (Vector3 m, Vector3 a, float r, float l) = Capsule(c);
                float half = Mathf.Max(0f, l * 0.5f - r);
                Vector3 onLine = m + a * Mathf.Clamp(Vector3.Dot(at - m, a), -half, half);
                if (Vector3.Distance(at, onLine) <= r + 0.3f)
                {
                    return true;
                }
            }
            return false;
        }
        Vector3 cameFrom = local.Center;
        Protect(true);
        Dictionary<bool, string> seen = new Dictionary<bool, string>();
        List<string> oursSeen = new List<string>();
        bool oursFair = true;
        try
        {
            foreach (bool game in new[] { false, false, true })
            {
                FairWaterfalls.Off = game;
                int touches = FairWaterfalls.StreamTouches, scoutTouches = FairWaterfalls.StreamScoutTouches, hitsGiven = FairWaterfalls.Hits;
                float nearestLine = float.MaxValue;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, dropAt, false);
                int knockdowns = 0;
                float lastFall = local.data.fallSeconds, inWater = 0f, top = 0f, outFor = 0f, wetAgo = 99f, farthest = 0f;
                bool leftIt = false, landed = false;
                Vector3 last = local.Center;
                for (float since = 0f; since < 4f; since += Time.deltaTime)
                {
                    yield return null;
                    // Until the warp has put them there, nothing counts.
                    if (!landed)
                    {
                        landed = (!local.warping && Vector3.Distance(local.Center, dropAt) < 3f) || since > 1f;
                        last = local.Center;
                        lastFall = local.data.fallSeconds;
                        continue;
                    }
                    bool wetNow = InWater(local.Center);
                    nearestLine = Mathf.Min(nearestLine, FromLine(local.Center));
                    farthest = Mathf.Max(farthest, Vector3.Distance(local.Center, dropAt));
                    wetAgo = wetNow ? 0f : wetAgo + Time.deltaTime;
                    // Knock-downs while in the water (not a hard landing after it).
                    if (local.data.fallSeconds > lastFall + 0.01f && wetAgo < 0.2f)
                    {
                        knockdowns++;
                    }
                    lastFall = local.data.fallSeconds;
                    if (wetNow)
                    {
                        inWater += Time.deltaTime;
                        outFor = 0f;
                        if (Time.deltaTime > 0f)
                        {
                            top = Mathf.Max(top, Vector3.Distance(local.Center, last) / Time.deltaTime);
                        }
                    }
                    else if (inWater > 0f)
                    {
                        outFor += Time.deltaTime;
                        leftIt |= outFor > 0.5f;
                    }
                    last = local.Center;
                }
                if (!game)
                {
                    // Ours counts its own knock-downs (the first can come in the very frame the scout lands).
                    knockdowns = FairWaterfalls.Hits - hitsGiven;
                }
                seen[game] = $"knocked down {knockdowns} times, {inWater:0.0} s in the water{(leftIt ? "" : " (still in it, or never out for 0.5 s)")}, at most {top:0.0} m/s while in it, flung {farthest:0.0} m at the farthest, ending {Vector3.Distance(local.Center, dropAt):0.0} m from where they were dropped; "
                    + $"the water touched anything {FairWaterfalls.StreamTouches - touches} times, the scout {FairWaterfalls.StreamScoutTouches - scoutTouches}, knock-downs given {FairWaterfalls.Hits - hitsGiven}; the scout came within {nearestLine:0.0} m of the hitbox's line (its radius {dropRadius:0.0} m)";
                if (!game)
                {
                    oursSeen.Add(seen[game]);
                    oursFair &= knockdowns >= 1 && knockdowns <= 3 && (leftIt || inWater < 0.5f) && inWater <= 2f && top < 25f && farthest < 15f;
                }
            }
        }
        finally
        {
            FairWaterfalls.Off = false;
        }
        Check("dropped into a waterfall's stream, a scout is knocked down when it hits (again only if up while still in it), washed out of it within 2 s, never flung (every ascent)",
            oursFair, "ours, twice from the same spot: " + string.Join(" | ", oursSeen));
        Info("dropped into a waterfall's stream, the game's way", seen[true]);
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, cameFrom, false);
        yield return new WaitForSeconds(1.5f);

        // The splash at the foot: where the map has them, and whether one is about here; if so, stepped into at its edge and
        // where the water lands (ours: thrown only there, half as hard as the game's; the game's: thrown at its edge too).
        List<WaterfallPusher> everySplash = FindObjectsByType<WaterfallPusher>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(w => w != null).ToList();
        Info("waterfall splashes in the map", everySplash.Count == 0 ? "none" : string.Join("; ", everySplash.Take(6).Select(w => $"{PathOf(w.transform, null)} ({(w.isActiveAndEnabled ? "about" : "asleep")})")));
        WaterfallPusher splashHere = everySplash.FirstOrDefault(w => w.isActiveAndEnabled && w.GetComponent<CapsuleCollider>() != null);
        // None about (the map keeps its splashes switched off): one switched on for the check alone, then off again.
        List<GameObject> wokenForCheck = new List<GameObject>();
        if (splashHere == null)
        {
            splashHere = everySplash.FirstOrDefault(w => w.GetComponent<CapsuleCollider>() != null && Vector3.Distance(w.transform.position, local.Center) < 400f);
            if (splashHere == null)
            {
                Info("the splash check is skipped", $"no splash near here ({Map.CurrentPlace})");
                yield break;
            }
            for (Transform up = splashHere.transform; up != null && !up.gameObject.activeInHierarchy; up = up.parent)
            {
                if (!up.gameObject.activeSelf)
                {
                    wokenForCheck.Add(up.gameObject);
                    up.gameObject.SetActive(true);
                }
            }
            splashHere.enabled = true;
            Info("a splash switched on for the check", $"{PathOf(splashHere.transform, null)} ({wokenForCheck.Count} objects switched on; off again after)");
            yield return new WaitForFixedUpdate();
        }
        (Vector3 splashMiddle, Vector3 splashAxis, float splashRadius, float splashLength) = Capsule(splashHere.GetComponent<CapsuleCollider>());
        Vector3 aside = Vector3.ProjectOnPlane(splashHere.transform.forward, Vector3.up).normalized;
        float bottom = splashMiddle.y - splashLength * 0.5f;
        // A place to stand in the splash this far from its middle (the floor under it, within the splash's height).
        Vector3? StandIn(float off)
        {
            for (int turn = 0; turn < 360; turn += 30)
            {
                Vector3 at = splashMiddle + Quaternion.Euler(0f, turn, 0f) * aside * off;
                if (Physics.Raycast(at + Vector3.up * splashLength, Vector3.down, out RaycastHit floor, splashLength * 2f, HelperFunctions.terrainMapMask)
                    && floor.point.y + 1f > bottom && floor.point.y + 1f < bottom + splashLength)
                {
                    return floor.point;
                }
            }
            return null;
        }
        Protect(true);
        try
        {
            foreach ((string where, float off) in new[] { ("at its edge", splashRadius - 1.2f), ("where the water lands", 1f) })
            {
                Vector3? spot = StandIn(off);
                if (!spot.HasValue)
                {
                    Info($"the splash {where} is skipped", "no floor there within the splash");
                    continue;
                }
                foreach (bool game in new[] { false, true })
                {
                    FairWaterfalls.Off = game;
                    // From outside the splash, so stepping in is entering it.
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, splashMiddle + aside * (splashRadius + 6f) + Vector3.up * 2f, false);
                    yield return new WaitForSeconds(1.5f);
                    int thrown = FairWaterfalls.Throws;
                    float fallBefore = local.data.fallSeconds;
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot.Value + Vector3.up * 1.2f, false);
                    Vector3 start = spot.Value + Vector3.up * 1.2f;
                    bool knocked = false;
                    for (float since = 0f; since < 1.5f; since += Time.deltaTime)
                    {
                        yield return null;
                        knocked |= local.data.fallSeconds > fallBefore + 0.1f;
                    }
                    float moved = Vector3.ProjectOnPlane(local.Center - start, Vector3.up).magnitude;
                    string what = $"knocked down {knocked}, moved {moved:0.0} m in 1.5 s";
                    if (!game)
                    {
                        bool shouldThrow = off <= FairWaterfalls.SplashReach;
                        Check($"stepping into a waterfall's splash {where}, a scout is {(shouldThrow ? "thrown (half as hard as the game's)" : "left alone")} (every ascent)",
                            shouldThrow ? knocked && FairWaterfalls.Throws > thrown : !knocked && FairWaterfalls.Throws == thrown, $"ours: {what} ({off:0.0} m from where the water lands)");
                    }
                    else
                    {
                        Info($"stepping into a waterfall's splash {where}, the game's way", what);
                    }
                }
            }
        }
        finally
        {
            FairWaterfalls.Off = false;
            for (int i = wokenForCheck.Count - 1; i >= 0; i--)
            {
                wokenForCheck[i].SetActive(false);
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, cameFrom, false);
        }
        yield return new WaitForSeconds(1.5f);
    }

    /// <summary>
    /// A snake's attack, watched frame by frame: the furthest its back turned from the sky where it reached out, rising
    /// (rearing up, uncoiling) and striking; and how far a part of it jumped in the frame it went over from rising to
    /// the strike (its head stays where it was drawn then, so nothing else should move in that frame).
    /// </summary>
    private sealed class AttackSeen
    {
        internal float rising;
        internal float striking;
        internal float handOver;
        /// <summary>How its frames lay at the worst of each (see Snake.RollReport).</summary>
        internal string risingAt = "";
        internal string strikingAt = "";
        private string doing = "";
        private List<Vector3> parts = new List<Vector3>();

        internal float Roll => Mathf.Max(rising, striking);

        internal void Watch(Snake snake)
        {
            string now = snake.Doing;
            float roll = snake.RaisedRoll;
            if ((now == "Rearing" || now == "Waking") && roll > rising)
            {
                rising = roll;
                risingAt = snake.RollReport;
            }
            else if (now == "Lunging" && roll > striking)
            {
                striking = roll;
                strikingAt = snake.RollReport;
            }
            List<Vector3> body = snake.Parts().Skip(1).ToList();
            if (now == "Lunging" && (doing == "Rearing" || doing == "Waking") && body.Count == parts.Count)
            {
                for (int i = 0; i < body.Count; i++)
                {
                    handOver = Mathf.Max(handOver, Vector3.Distance(body[i], parts[i]));
                }
            }
            doing = now;
            parts = body;
        }
    }

    /// <summary>A while of a snake's time (scaled), its attack watched every frame meanwhile.</summary>
    private static IEnumerator WatchAttack(Snake snake, float seconds, AttackSeen seen)
    {
        for (float until = Time.time + seconds; Time.time < until;)
        {
            yield return null;
            seen.Watch(snake);
        }
    }

    /// <summary>How far a part of a snake may move in the frame its rising goes over into the strike (metres).</summary>
    private const float HandOverJumpLimit = 0.02f;

    /// <summary>What the scout saw and heard of a snake left alone.</summary>
    private sealed class AloneSeen
    {
        internal float seconds;
        internal float crawled;
        internal float loudest;
        /// <summary>How long its slither could be heard (louder than a whisper).</summary>
        internal float heard;
        internal string doing = "";
        internal int bites;
    }

    /// <summary>
    /// A ground snake left to itself (it notices no scout): it wanders about by its own rules, as with nobody near, while
    /// the scout watches it from a few metres off, keeping it in view and going after it when it gets away (a picture every
    /// 5 s), to hear its slither and see it crawl.
    /// </summary>
    private IEnumerator WatchAlone(Character local, Snake snake, float seconds, string shots, AloneSeen seen)
    {
        int bitesBefore = Snakes.BitesTaken;
        snake.WanderAlone();
        Vector3? spot = StandNear(snake.Head, 2.5f, 3.5f, seeIt: true) ?? StandNear(snake.Head, 2.5f, 5f) ?? StandNear(snake.Head, 3f, 8f);
        if (spot.HasValue)
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot.Value + Vector3.up * 1.2f, false);
            yield return new WaitForSeconds(1f);
        }
        HashSet<string> doing = new HashSet<string>();
        Vector3 last = snake.Head;
        float nextShot = 2.5f, nextMove = 0f;
        int shot = 0;
        for (float since = 0f; since < seconds; since += Time.deltaTime)
        {
            yield return null;
            seen.seconds += Time.deltaTime;
            seen.crawled += Vector3.ProjectOnPlane(snake.Head - last, Vector3.up).magnitude;
            last = snake.Head;
            seen.loudest = Mathf.Max(seen.loudest, snake.SlitherLoudness);
            if (snake.SlitherLoudness > 0.15f)
            {
                seen.heard += Time.deltaTime;
            }
            doing.Add(snake.Doing);
            // More than 5 m off, or out of sight: the scout goes after it, to a few metres from it again.
            if (Time.time >= nextMove && (Flat(snake.Head, local.Center) > 5f
                || Physics.Linecast(local.Center + Vector3.up * 0.4f, snake.Head + Vector3.up * 0.1f, HelperFunctions.terrainMapMask)))
            {
                nextMove = Time.time + 2f;
                Vector3? again = StandNear(snake.Head, 2.5f, 3.5f, seeIt: true) ?? StandNear(snake.Head, 2.5f, 5f) ?? StandNear(snake.Head, 3f, 8f);
                if (again.HasValue)
                {
                    local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, again.Value + Vector3.up * 1.2f, false);
                }
            }
            Vector3 see = snake.Head + Vector3.up * 0.05f - local.Center;
            local.data.lookValues = new Vector2(Quaternion.LookRotation(new Vector3(see.x, 0f, see.z)).eulerAngles.y,
                Mathf.Clamp(Mathf.Atan2(see.y, new Vector2(see.x, see.z).magnitude) * Mathf.Rad2Deg, -60f, 60f));
            if (since >= nextShot)
            {
                nextShot += 5f;
                Screenshot($"{shots}-{++shot}");
            }
        }
        snake.Unaware = false;
        seen.doing = string.Join(", ", doing);
        seen.bites = Snakes.BitesTaken - bitesBefore;
    }

    /// <summary>
    /// A place to stand by a snake: the open top of something solid with firm footing (about level all round under the
    /// scout's feet, so they cannot slip off it, as off the rounded top of a branch; not firm, any top that is not too
    /// steep, for a scout who only looks on), the scout's middle between the distances given from it.
    /// </summary>
    private static Vector3? StandNear(Vector3 snake, float nearest, float farthest, bool seeIt = false, bool firm = true)
    {
        for (int ring = 0; ring <= 5; ring++)
        {
            float reach = Mathf.Lerp(nearest, farthest, ring / 5f);
            for (int angle = 0; angle < 360; angle += 20)
            {
                Vector3 above = snake + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * reach + Vector3.up * 4f;
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit floor, 8f, HelperFunctions.terrainMapMask) || floor.normal.y < (firm ? 0.8f : 0.6f)
                    || InsideSomething(floor.point + Vector3.up * 1f) || (firm && !Foothold(floor.point)) || Hazard(floor.point + Vector3.up, 1.5f))
                {
                    continue;
                }
                float off = Vector3.Distance(floor.point + Vector3.up, snake);
                if (off >= nearest && off <= farthest && !Physics.CheckCapsule(floor.point + Vector3.up * 0.5f, floor.point + Vector3.up * 1.6f, 0.3f, HelperFunctions.terrainMapMask)
                    && (!seeIt || !Physics.Linecast(floor.point + Vector3.up * 1.6f, snake + Vector3.up * 0.15f, HelperFunctions.terrainMapMask)))
                {
                    return floor.point;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Something harmful near a spot, that would hurt a scout put there (the map's, not what is tested): thorny vines,
    /// cacti, urchins and the like (a touch that hurts), status triggers and emitters, bursts (spore mines), ticks, webs,
    /// fly traps, bees, traps, saws, spike rollers, axes, jellyfish, tumbleweeds, kill planes.
    /// </summary>
    private static bool Hazard(Vector3 at, float reach)
    {
        foreach (Collider near in Physics.OverlapSphere(at, reach, ~0, QueryTriggerInteraction.Collide))
        {
            CollisionModifier touch = near.GetComponentInParent<CollisionModifier>();
            if ((touch != null && touch.applyEffects && (touch.damage > 0f || touch.applyPetrify))
                || near.GetComponentInParent<StatusTrigger>() != null || near.GetComponentInParent<StatusEmitter>() != null
                || near.GetComponentInParent<AOE>() != null || near.GetComponentInParent<TickTrigger>() != null
                || near.GetComponentInParent<SpiderTrigger>() != null || near.GetComponentInParent<VenusFlyTrap>() != null
                || near.GetComponentInParent<Beehive>() != null || near.GetComponentInParent<BeeSwarm>() != null
                || near.GetComponentInParent<Peak.SpikeTrap>() != null || near.GetComponentInParent<Peak.MovingSawBlade>() != null
                || near.GetComponentInParent<Peak.SpikeRoller>() != null || near.GetComponentInParent<SwingingAxe>() != null
                || near.GetComponentInParent<SlipperyJellyfish>() != null || near.GetComponentInParent<TumbleWeed>() != null
                || near.GetComponentInParent<Peak.KillPlane>() != null)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// How far a point is under the ground over it (0 if it is not): the ground within a little above it (a bush or a
    /// rock further up, over a snake lying in the open, is not ground it sank under).
    /// </summary>
    private static float UnderGround(Vector3 at)
    {
        return Physics.Raycast(at + Vector3.up * 0.8f, Vector3.down, out RaycastHit ground, 1.6f, HelperFunctions.terrainMapMask) ? Mathf.Max(0f, ground.point.y - at.y) : 0f;
    }

    /// <summary>Firm footing at a point: solid and about level under the scout's feet all round it (not the rounded edge of a branch).</summary>
    private static bool Foothold(Vector3 at)
    {
        for (int angle = 0; angle < 360; angle += 45)
        {
            Vector3 round = at + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 0.45f;
            if (!Physics.Raycast(round + Vector3.up * 0.5f, Vector3.down, out RaycastHit under, 1f, HelperFunctions.terrainMapMask)
                || Mathf.Abs(under.point.y - at.y) > 0.25f || under.normal.y < 0.7f)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// A point inside something solid of the map (a rock, a trunk, under the ground): rays out from it, counting the
    /// insides of surfaces too, meet an inside first in most directions. (Unity's overlap checks miss the inside of a
    /// solid mesh, and a ray started in one goes out through its bottom unseen.)
    /// </summary>
    private static bool InsideSomething(Vector3 at)
    {
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            int inside = 0;
            Vector3[] ways = { Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            foreach (Vector3 way in ways)
            {
                if (Physics.Raycast(at, way, out RaycastHit hit, 60f, HelperFunctions.terrainMapMask) && Vector3.Dot(hit.normal, way) > 0f)
                {
                    inside++;
                }
            }
            return inside >= 3;
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
    }

    /// <summary>How high a point is above what is under it (anything solid of the map).</summary>
    private static float AboveGround(Vector3 at)
    {
        return Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, out RaycastHit floor, 200f, HelperFunctions.terrainMapMask) ? at.y - floor.point.y : float.PositiveInfinity;
    }

    private static int cookingExplosions;

    private static void CountCookingExplosion()
    {
        cookingExplosions++;
    }

    private IEnumerator MultiplayerRoundChecks()
    {
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;

        // Ascent 8: switched off, the flares at the peak call the helicopter again.
        ConfigEntry<bool> ascent8 = UltimateAscent.Plugin.Ascent8;
        bool saved8 = ascent8.Value;
        bool requiredOn, requiredOff;
        try
        {
            ascent8.Value = true;
            HostOptions.Invalidate();
            requiredOn = Ascents.shouldRequireNadir;
            ascent8.Value = false;
            HostOptions.Invalidate();
            requiredOff = Ascents.shouldRequireNadir;
        }
        finally
        {
            ascent8.Value = saved8;
            HostOptions.Invalidate();
        }
        Check("the \"Ascent 8\" switch: on, a climb from ascent 8 is won only in the Nadir; off, the flares at the peak call the helicopter",
            requiredOn && !requiredOff, $"Nadir required: switch on {requiredOn}, off {requiredOff}");

        // The Scoutmaster gives up with another scout within 35 m of his target (the game: 15 m).
        System.Reflection.MethodInfo verify = AccessTools.Method(typeof(Scoutmaster), "VerifyTarget");
        Patches patches = verify != null ? Harmony.GetPatchInfo(verify) : null;
        bool transpiled = patches != null && patches.Transpilers.Any(p => p.owner == UltimateAscent.Plugin.Guid);
        Check("the Scoutmaster gives up on a target with another scout within 35 m (9), and the rule-zero hunt needs nobody within 35 m (11)",
            transpiled && Mathf.Approximately(A11_RuleZero.GiveUpDistance(), 35f) && Mathf.Approximately(A11_RuleZero.CompanionRadius, 35f),
            $"give-up check patched {transpiled}, distance {A11_RuleZero.GiveUpDistance():0} m, hunt radius {A11_RuleZero.CompanionRadius:0} m");

        // PlayersInfo: an endless time shows "∞", and the game's lettering has the sign.
        Type helper = AccessTools.TypeByName("PlayersInfo.Helpers.AfflictionTimeHelper");
        System.Reflection.MethodInfo format = helper != null ? AccessTools.Method(helper, "FormatTime", new[] { typeof(float) }) : null;
        if (format != null)
        {
            string endless = (string)format.Invoke(null, new object[] { float.PositiveInfinity });
            string huge = (string)format.Invoke(null, new object[] { 3.4e38f });
            string normal = (string)format.Invoke(null, new object[] { 75f });
            TMPro.TMP_FontAsset font = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>().FirstOrDefault(f => f.name.Contains("DarumaDropOne"));
            bool glyph = font != null && font.HasCharacter('∞', true, true);
            Check("PlayersInfo shows an endless time as ∞ (a normal one as before), and the game's lettering has the sign",
                endless == "∞" && huge == "∞" && normal == "1:15" && glyph,
                $"endless '{endless}', 3.4e38 s '{huge}', 75 s '{normal}'; font '{(font != null ? font.name : "none")}' has ∞ {glyph}");
        }
        else
        {
            Info("the PlayersInfo check is skipped", "PlayersInfo is not installed in this profile");
        }

        // Tumbleweeds (17): test-made ones (no Mesa needed) get power x1.5, rolling x1.15 and a cone of 10-240 degrees.
        if (Rules.Enabled(Lvl.Hazards))
        {
            System.Reflection.MethodInfo start = AccessTools.Method(typeof(TumbleWeed), "Start");
            List<float> cones = new List<float>();
            float power = 0f, roll = 0f;
            for (int i = 0; i < 60; i++)
            {
                GameObject made = new GameObject("UA test tumbleweed", typeof(Rigidbody));
                made.SetActive(false);
                TumbleWeed weed = made.AddComponent<TumbleWeed>();
                weed.rollForce = 10f;
                float gamePower = weed.powerMultiplier;
                UnityEngine.Random.InitState(1000 + i);
                start.Invoke(weed, null);
                cones.Add(weed.maxAngle);
                power = weed.powerMultiplier / gamePower;
                roll = weed.rollForce / 10f;
                UnityEngine.Object.Destroy(made);
            }
            Check("tumbleweeds hit 1.5 times as hard, roll 15% faster and steer within a cone of 10-240 degrees (17)",
                Mathf.Abs(power - 1.5f) < 0.001f && roll > 0.5f * 1.15f - 0.001f && roll <= 1.15f + 0.001f && cones.All(c => c >= 10f && c <= 240f) && cones.Min() < 20f && cones.Max() > 100f,
                $"power x{power:0.##}, rolling x{roll:0.##} of a random 0.5-1 share, cones {cones.Min():0}-{cones.Max():0} degrees (60 tumbleweeds, half under {cones.OrderBy(c => c).ElementAt(30):0})");
        }

        // Scout warmth: a scout gives off warmth once nothing has made them cold for 2 s (the game: 3 s).
        CharacterHeatEmission emitter = local.refs.heatEmission;
        if (emitter != null)
        {
            Info("scout warmth (the game's numbers)", $"each scout takes {emitter.heatAmount * 100f:0.##} cold every {emitter.rate:0.##} s off the scouts within {emitter.radius:0.##} m of their hips (their own too), once they have had no cold for a while");
            // The game's own update, run twice in one frame with its rate's time up: 1.9 s and then 2.1 s since the last cold.
            Action warmCalm = CalmForMeasuring();
            Protect(false);
            float tooSoon = 0f;
            float inTime = 0f;
            float lastCold = local.data.sinceAddedCold;
            Traverse counter = Traverse.Create(emitter).Field("counter");
            try
            {
                // What it takes off: off the bar, or still owed (the game takes cold off in steps of 2.5, more than one warmth).
                float Taken() => -afflictions.GetCurrentStatus(Status.Cold) + afflictions.currentDecrementalStatuses[(int)Status.Cold];
                afflictions.SetStatus(Status.Hot, 0f);
                afflictions.SetStatus(Status.Cold, 0.5f);
                float before = Taken();
                local.data.sinceAddedCold = ScoutWarmth.ColdFreeSeconds - 0.1f;
                counter.SetValue(emitter.rate);
                emitter.Update();
                tooSoon = Taken() - before;
                afflictions.SetStatus(Status.Cold, 0.5f);
                before = Taken();
                local.data.sinceAddedCold = ScoutWarmth.ColdFreeSeconds + 0.1f;
                counter.SetValue(emitter.rate);
                emitter.Update();
                inTime = Taken() - before;
            }
            finally
            {
                local.data.sinceAddedCold = lastCold;
                afflictions.SetStatus(Status.Cold, 0f);
                warmCalm();
                Protect(true);
            }
            bool inOwnReach = Vector3.Distance(local.refs.hip.transform.position, local.Center) < emitter.radius;
            Check("a scout gives off warmth once they have had no cold for 2 s (the game: 3 s; every ascent)",
                !inOwnReach || (tooSoon < 0.0001f && Mathf.Abs(inTime - emitter.heatAmount) < 0.0001f),
                inOwnReach ? $"cold taken off at 1.9 s {tooSoon * 100f:0.##}, at 2.1 s {inTime * 100f:0.##} (the game's step {emitter.heatAmount * 100f:0.##})" : "the scout is outside their own warmth's reach: not measured");
        }

        // A Faerie Lantern burned by the sun explodes once, as when overcooked, and is gone (the sun does every cooking
        // step at once, so without care it went off at each).
        Item faerie = ItemByName("Lantern_Faerie") ?? ItemByName("Faerie Lantern");
        System.Reflection.MethodInfo explodeImpl = AccessTools.Method(typeof(ItemCooking), "CookingExplodeImpl");
        System.Reflection.MethodInfo burnNow = AccessTools.Method(typeof(A23_HotSun), "BurnNow");
        if (faerie != null && explodeImpl != null && burnNow != null)
        {
            Harmony counter = new Harmony("UltimateAscent.SmokeTest.explosions");
            counter.Patch(explodeImpl, postfix: new HarmonyMethod(typeof(Runner), nameof(CountCookingExplosion)));
            cookingExplosions = 0;
            yield return SpawnIntoHand(faerie);
            Item lantern = held;
            if (lantern != null)
            {
                Protect(true);
                burnNow.Invoke(null, new object[] { lantern });
                yield return new WaitForSeconds(1.5f);
                bool gone = lantern == null && (local.data.currentItem == null || local.data.currentItem.itemID != faerie.itemID);
                Check("a Faerie Lantern burned by the sun explodes once, as when overcooked, and is gone (23)", cookingExplosions == 1 && gone,
                    $"{cookingExplosions} explosion(s), gone {gone}");
            }
            counter.UnpatchSelf();
            held = null;
        }
        else
        {
            Info("the Faerie Lantern sun check is skipped", $"lantern {faerie != null}, explosion {explodeImpl != null}, sun burn {burnNow != null}");
        }

        // The kick's own numbers (the scout's, as set in the game's data; Stick and Torch swings borrow its reach and aim).
        CharacterGrabbing kick = local.refs.grabbing;
        if (kick != null)
        {
            Info("the kick (the game's, Kick Mode)", $"knocks down {kick.kickRagdollTime:0.##} s, reach {kick.kickDistance:0.##} m, aim {kick.kickAngle:0} degrees, push {kick.kickForce:0.#} (spread {kick.kickRange:0.#}), {kick.kickDelay:0.##} s after the press");
        }

        // Waterfalls: what they are and where they reach (for the question; no rule changes them).
        List<WaterfallPusher> pushers = FindObjectsByType<WaterfallPusher>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();
        List<WaterfallTrigger> streams = FindObjectsByType<WaterfallTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();
        string Shape(Component c)
        {
            return string.Join(" + ", c.GetComponents<Collider>().Select(col =>
            {
                Vector3 s = col.transform.lossyScale;
                switch (col)
                {
                    case BoxCollider box:
                        return $"box {Vector3.Scale(box.size, s).x:0.#}x{Vector3.Scale(box.size, s).y:0.#}x{Vector3.Scale(box.size, s).z:0.#} m{(box.isTrigger ? " trigger" : "")}";
                    case CapsuleCollider cap:
                        return $"capsule r {cap.radius * Mathf.Max(s.x, s.z):0.##} m, h {cap.height * s.y:0.#} m{(cap.isTrigger ? " trigger" : "")}";
                    case SphereCollider sph:
                        return $"sphere r {sph.radius * s.x:0.##} m{(sph.isTrigger ? " trigger" : "")}";
                    case MeshCollider mesh:
                        return $"mesh '{(mesh.sharedMesh != null ? mesh.sharedMesh.name : "none")}' {col.bounds.size.x:0.#}x{col.bounds.size.y:0.#}x{col.bounds.size.z:0.#} m{(mesh.isTrigger ? " trigger" : "")}{(mesh.convex ? " convex" : "")}";
                    default:
                        return col.GetType().Name;
                }
            }));
        }
        Info("waterfalls: pushers (the game's WaterfallPusher)", $"{pushers.Count} in the map; " + string.Join("; ", pushers.GroupBy(w => $"'{w.name}' under '{(w.transform.parent != null ? w.transform.parent.name : "-")}': {Shape(w)}; knocked down {w.fallTime:0.##} s, pushed {w.knockback:0.#} out + {w.downwardKnockback:0.#} down, once per {w.cooldown:0.#} s").Take(6).Select(g => $"{g.Count()}x {g.Key}")));
        Info("waterfalls: streams (the game's WaterfallTrigger)", $"{streams.Count} in the map; " + string.Join("; ", streams.GroupBy(w => $"'{w.name}' under '{(w.transform.parent != null ? w.transform.parent.name : "-")}': {Shape(w)}; force {w.force:0.#}, drag {w.drag:0.##}{(w.ragdoll ? $", knocked down {w.ragdollLength:0.#} s" : "")}").Take(6).Select(g => $"{g.Count()}x {g.Key}")));

        // Thorns (17): one in the face pops out at the game's time, one on the body never does.
        if (Rules.Enabled(Lvl.Hazards))
        {
            afflictions.RemoveAllThorns();
            yield return null;
            afflictions.AddThorn(local.GetBodypart(BodypartType.Head).transform.position, 0);
            afflictions.AddThorn(local.GetBodypart(BodypartType.Foot_R).transform.position, 0);
            yield return new WaitForSeconds(0.5f);
            List<ThornOnMe> stuck = afflictions.physicalThorns.Where(t => t != null && t.stuckIn && t.isThorn).ToList();
            string Where(ThornOnMe t)
            {
                string path = t.transform.name;
                for (Transform up = t.transform.parent; up != null && up.GetComponent<Character>() == null; up = up.parent)
                {
                    path = up.name + "/" + path;
                }
                Bodypart nearest = local.refs.ragdoll.partList.Where(p => p != null).OrderBy(p => (p.transform.position - t.transform.position).sqrMagnitude).FirstOrDefault();
                return $"{path} (nearest {(nearest != null ? nearest.partType.ToString() : "none")})";
            }
            Info("thorns stuck for the face/body check", string.Join("; ", stuck.Select(Where)));
            List<ThornOnMe> headSlots = afflictions.physicalThorns.Where(t => t != null && t.isThorn && A17_Hazards.InTheFace(t)).ToList();
            Info("thorn places on the head", $"{headSlots.Count} of {afflictions.physicalThorns.Count(t => t != null && t.isThorn)}: {string.Join("; ", headSlots.Take(6).Select(Where))}");
            ThornOnMe face = stuck.FirstOrDefault(A17_Hazards.InTheFace);
            ThornOnMe body = stuck.FirstOrDefault(t => !A17_Hazards.InTheFace(t));
            float faceIn = face != null ? face.popOutTime - Time.time : -1f;
            float bodyIn = body != null ? body.popOutTime - Time.time : -1f;
            float gameSolo = face != null ? face.popOutTimeSolo : -1f;
            Check("a thorn in the face pops out at the game's time; a thorn on the body never pops out by itself (17)",
                face != null && body != null && faceIn > 0f && faceIn <= gameSolo + 0.1f && float.IsPositiveInfinity(body.popOutTime),
                $"{stuck.Count} thorns stuck; face: pops out in {faceIn:0.0} s (the game's solo time {gameSolo:0} s); body: {(body != null ? (float.IsPositiveInfinity(body.popOutTime) ? "never" : $"in {bodyIn:0.0} s") : "none")}");
            afflictions.RemoveAllThorns();
            yield return null;
        }

        // More wild food in multiplayer: a bush grows half as much food again (a half left over is a coin toss).
        BerryBush bush = FindObjectsByType<BerryBush>(FindObjectsSortMode.None).FirstOrDefault(b => b.spawnMode == Spawner.SpawnMode.SpawnPool && b.spawnSpots != null && b.spawnSpots.Count >= 2);
        if (bush != null)
        {
            ConfigEntry<bool> wild = UltimateAscent.Plugin.MoreWildFood;
            bool savedWild = wild.Value;
            List<PhotonView> grown = null;
            int grownBefore = MoreWildFood.Grown;
            try
            {
                wild.Value = true;
                MoreWildFood.AsMultiplayer = true;
                for (int attempt = 0; attempt < 5 && (grown == null || grown.Count == 0 || !MoreWildFood.IsFood(grown[0].GetComponent<Item>())); attempt++)
                {
                    if (grown != null)
                    {
                        grown.ForEach(v => { if (v != null) PhotonNetwork.Destroy(v.gameObject); });
                    }
                    grown = bush.SpawnItems(bush.spawnSpots);
                }
            }
            finally
            {
                wild.Value = savedWild;
                MoreWildFood.AsMultiplayer = false;
            }
            int extra = MoreWildFood.Grown - grownBefore;
            int all = grown != null ? grown.Count : 0;
            int game = all - extra;
            float half = game * MoreWildFood.Extra;
            Check("with \"More Wild Food in Multiplayer\" on, a bush in multiplayer grows half as much food again",
                game > 0 && (extra == Mathf.FloorToInt(half) || extra == Mathf.CeilToInt(half)),
                $"'{bush.name}': {game} from the game, {extra} more ({(grown != null && grown.Count > 0 ? grown[0].name : "nothing")})");
            yield return new WaitForSeconds(1f);
            Screenshot("more-wild-food");
            if (grown != null)
            {
                grown.ForEach(v => { if (v != null) PhotonNetwork.Destroy(v.gameObject); });
            }
        }
        else
        {
            Info("the wild food check is skipped", "no berry bush near");
        }

        // A revive statue clears every status (the game's revive curse and hunger after); another revive keeps 25% (15).
        RespawnChest statue = Luggage.ALL_LUGGAGE.OfType<RespawnChest>().Where(c => c != null).OrderBy(c => Vector3.Distance(c.transform.position, local.Center)).FirstOrDefault();
        if (statue != null && Rules.Enabled(Lvl.WorseItems))
        {
            Vector3 home = local.Center;
            float curseAtStart = afflictions.GetCurrentStatus(Status.Curse);
            float hungerAtStart = afflictions.GetCurrentStatus(Status.Hunger);
            Protect(false);
            try
            {
                afflictions.SetStatus(Status.Poison, 0.4f);
                local.RPCA_ReviveAtPosition(home, true, -1);
                yield return new WaitForSeconds(1.5f);
                float kept = afflictions.GetCurrentStatus(Status.Poison);
                afflictions.SetStatus(Status.Poison, 0.4f);
                float curseBefore = afflictions.GetCurrentStatus(Status.Curse);
                afflictions.SetStatus(Status.Hunger, 0f);
                local.data.SetPetrify(40);
                local.RPCA_ReviveAtPosition(statue.transform.position + Vector3.up * 1.5f, true, (int)statue.SegmentNumber);
                yield return new WaitForSeconds(1.5f);
                int petrifyAfterStatue = local.data.petrifyAmount;
                float afterStatue = afflictions.GetCurrentStatus(Status.Poison);
                float curseAdded = afflictions.GetCurrentStatus(Status.Curse) - curseBefore;
                float hunger = afflictions.GetCurrentStatus(Status.Hunger);
                Check("a revive statue clears every status and gives the game's revive curse and hunger; another revive keeps 25% (15)",
                    Mathf.Abs(kept - 0.1f) < 0.013f && afterStatue < 0.001f && Mathf.Abs(curseAdded - Ascents.revivalCurse) < 0.013f && hunger > 0.2f,
                    $"40 poison: {kept * 100f:0.#} left after another revive, {afterStatue * 100f:0.#} after the statue's; the statue's added {curseAdded * 100f:0.#} curse (the game's {Ascents.revivalCurse * 100f:0}) and {hunger * 100f:0.#} hunger");
                Check("a revive statue takes no petrification away (15)", petrifyAfterStatue == 40,
                    $"petrification {petrifyAfterStatue} after the statue's revive, 40 before it (the game takes 75 off)");
            }
            finally
            {
                // Both revives' curse and hunger taken back, so the next checks start as before.
                afflictions.SetStatus(Status.Curse, curseAtStart);
                afflictions.SetStatus(Status.Hunger, hungerAtStart);
                afflictions.SetStatus(Status.Poison, 0f);
                local.data.SetPetrify(0);
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home, false);
                Protect(true);
            }
            yield return new WaitForSeconds(1.5f);
        }
        else
        {
            Info("the revive statue check is skipped", statue == null ? "no revive statue in the map" : "ascent 15's rule is off");
        }
    }
}
