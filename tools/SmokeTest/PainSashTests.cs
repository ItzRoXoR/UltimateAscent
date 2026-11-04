using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>The Pain Sash: in the passport, worn, its moving layers, its bloody footprints and its unlock on ascent 25.</summary>
internal sealed partial class Runner
{
    /// <summary>The sash worn and the save's sash and unlocks before the Pain Sash's checks (put back after them).</summary>
    private int sashBefore = -1;
    private string savedSash;
    private string savedSashUnlocks;

    /// <summary>
    /// The Pain Sash in the airport. The boarding pass's reward line shows it on ascent 25 while it is locked, and nothing
    /// on 24 or once it is unlocked (the Burnt Skin has no reward line). Then the passport:
    /// the last sash, its material in every scout's row, locked (a click
    /// tells it takes the peak on ascent 25 with all modifiers on); unlocked (in the mod's save), a click puts it on the passport's scout and the scout,
    /// saved in the mod's save and not in a Steam stat. Its moving layers are pictured close up, frame by frame. It stays
    /// on for the climb (the footprints' check).
    /// </summary>
    private IEnumerator PainSashPassport()
    {
        Customization customization = Singleton<Customization>.Instance;
        int pain = ModCosmetics.IndexOf("painsash");
        sashBefore = MyData.customizationData.currentSash;
        savedSash = ModSave.Get("sash");
        savedSashUnlocks = ModSave.Get("unlocked");
        ModSave.Set("unlocked", "");
        CustomizationRefs refs = Local.refs.customization.refs;
        CustomizationOption option = pain >= 0 && pain < customization.sashes.Length ? customization.sashes[pain] : null;
        Material inRow = pain >= 0 && pain < refs.sashAscentMaterials.Length ? refs.sashAscentMaterials[pain] : null;
        Check("the Pain Sash is the last sash, after the game's ten, its material at its place in the scout's row of sash materials, locked and with no badge of its own",
            option != null && pain == customization.sashes.Length - 1 && pain == 10 && inRow != null && inRow.name == PainSash.MaterialName
            && option.IsLocked && option.requiredAchievement == ACHIEVEMENTTYPE.NONE && option.texture != null && option.texture.name == "UA_PainSash_icon",
            $"sash {pain} of {customization.sashes.Length}, material there '{inRow?.name}' (row of {refs.sashAscentMaterials.Length}), locked {option?.IsLocked}, icon '{option?.texture?.name}'");
        if (option == null)
        {
            yield break;
        }

        // The boarding pass's reward line, on the ascents that give the mod's rewards.
        BoardingPass pass = GUIManager.instance.boardingPass;
        if (!pass.isOpen)
        {
            pass.Open();
        }
        yield return new WaitForSecondsRealtime(1.5f);
        int ascentBefore = pass.ascentIndex;
        List<string> lines = new List<string>();
        bool rewardsRight = true;
        foreach ((int ascent, string unlocked, string expected, string picture) in new[]
        {
            (25, "", "PAIN SASH", "pass-reward-pain-sash"),
            (24, "", null, null),
            (23, "", null, null),
            (25, "painsash", null, null),
        })
        {
            ModSave.Set("unlocked", unlocked);
            pass.ascentIndex = ascent;
            pass.UpdateAscent();
            yield return null;
            yield return null;
            string shown = pass.reward.activeSelf ? Plain(pass.rewardText.text) : null;
            rewardsRight &= shown == expected;
            lines.Add($"{ascent} ({(unlocked.Length == 0 ? "nothing unlocked" : unlocked)}): {shown ?? "none"}");
            if (picture != null)
            {
                Screenshot(picture);
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }
        Check("the boarding pass's reward line shows the Pain Sash on 25 while it is locked; nothing on 23 and 24 (the Burnt Skin has none) or once it is unlocked",
            rewardsRight, string.Join("; ", lines));
        ModSave.Set("unlocked", "");
        pass.ascentIndex = ascentBefore;
        pass.UpdateAscent();
        // The boarding pass would take the passport's clicks.
        pass.Close();
        yield return new WaitForSecondsRealtime(1f);
        PassportManager passport = PassportManager.instance;
        passport.ToggleOpen();
        yield return new WaitForSecondsRealtime(1.5f);
        passport.OpenTab(Customization.Type.Sash);
        yield return new WaitForSecondsRealtime(0.5f);
        for (int page = 0; page < pain / 28; page++)
        {
            passport.PageNext();
        }
        yield return new WaitForSecondsRealtime(0.5f);
        PassportButton Button() => passport.buttons.FirstOrDefault(b => b.gameObject.activeInHierarchy && b.currentOption == option);
        PassportButton button = Button();
        if (button != null)
        {
            ClickOn((RectTransform)button.button.transform);
        }
        yield return new WaitForSecondsRealtime(0.5f);
        string note = ModCosmetics.Requirement.Shown;
        Check("a click on the locked Pain Sash tells it takes the peak on ascent 25 with all modifiers on, and it stays off",
            button != null && button.lockedIcon.gameObject.activeSelf && note != null && note.Contains("ULTIMATE ASCENT:") && note.Contains("peak on ascent 25")
            && note.Contains("all modifiers") && MyData.customizationData.currentSash == sashBefore,
            $"button {button != null}, note '{note}'");
        Screenshot("passport-locked-pain-sash");
        yield return new WaitForSecondsRealtime(1f);

        ModSave.Set("unlocked", "painsash");
        passport.SetButtons();
        yield return new WaitForSecondsRealtime(0.5f);
        NoSteamStatWrites.Asked.Clear();
        button = Button();
        if (button != null)
        {
            ClickOn((RectTransform)button.button.transform);
        }
        yield return new WaitForSecondsRealtime(1f);
        string Worn(Renderer sash) => sash != null && sash.sharedMaterials.Length > 1 && sash.sharedMaterials[1] != null ? sash.sharedMaterials[1].name : "none";
        string onDummy = Worn(passport.dummy.refs.sashRenderer);
        string onScout = Worn(refs.sashRenderer);
        Check("unlocked, the Pain Sash is worn: on the passport's scout and the scout, in the mod's save, no Steam stat",
            MyData.customizationData.currentSash == pain && onDummy.StartsWith(PainSash.MaterialName, StringComparison.Ordinal) && PainSash.Wears(Local)
            && ModSave.Get("sash") == "painsash" && !NoSteamStatWrites.Asked.Any(a => a.StartsWith("Cosmetic_Sash", StringComparison.Ordinal)),
            $"sash {MyData.customizationData.currentSash}, passport scout '{onDummy}', scout '{onScout}', saved '{ModSave.Get("sash")}', Steam stats asked: {string.Join(", ", NoSteamStatWrites.Asked)}");
        Screenshot("passport-pain-sash");
        yield return new WaitForSecondsRealtime(1f);

        // Close up, frame by frame: the stains and the brightest spots drift.
        Camera camera = passport.dummyCamera;
        RenderTexture target = camera.targetTexture;
        Vector3 cameraAt = camera.transform.position;
        float size = camera.orthographicSize;
        RenderTexture shot = new RenderTexture(768, 768, 24);
        Renderer worn = passport.dummy.refs.sashRenderer;
        try
        {
            Bounds bounds = worn.bounds;
            Vector3 forward = camera.transform.forward;
            camera.transform.position = bounds.center - forward * Vector3.Dot(bounds.center - camera.transform.position, forward);
            camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.2f;
            for (int frame = 0; frame < 40; frame++)
            {
                camera.targetTexture = shot;
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = shot;
                Texture2D copy = new Texture2D(shot.width, shot.height, TextureFormat.RGB24, false);
                copy.ReadPixels(new Rect(0, 0, shot.width, shot.height), 0, 0);
                copy.Apply();
                RenderTexture.active = previous;
                camera.targetTexture = target;
                File.WriteAllBytes(Path.Combine(Paths.BepInExRootPath, $"UltimateAscent.SmokeTest.pain-sash-frame-{frame:00}.png"), copy.EncodeToPNG());
                Destroy(copy);
                yield return new WaitForSecondsRealtime(0.15f);
            }
            Info("the Pain Sash's moving layers", "40 close-ups 0.15 s apart: pain-sash-frame-00 to -39");
        }
        finally
        {
            camera.transform.position = cameraAt;
            camera.orthographicSize = size;
            camera.targetTexture = target;
            shot.Release();
        }
        passport.ToggleOpen();
        yield return new WaitForSecondsRealtime(1f);
        if (only != null && only.Contains("painsashnadir"))
        {
            ChallengeModes.Blind = true;
            Info("for the climb", "Blind Mode on: the Nadir's win unlocks the VR Headset too");
        }
    }

    /// <summary>
    /// The Pain Sash on the island: walking along open ground, the scout leaves a boot print at each step, left and right,
    /// on the ground along the way; they erode and are all gone 10 s after the last; with another sash on, walking leaves
    /// none. Then its unlock: only ascent 25 won in the Nadir with every modifier on for the whole climb unlocks it (not
    /// the peak, not 24, not with a modifier off), and the statistics' window calls it a new sash. The sash and the save
    /// are put back as they were.
    /// </summary>
    private IEnumerator PainSashFootprints()
    {
        Character local = Local;
        int pain = ModCosmetics.IndexOf("painsash");
        if (sashBefore < 0 || MyData.customizationData.currentSash != pain)
        {
            Info("the footprints' check is skipped", "the Pain Sash was not put on in the airport");
            yield break;
        }
        Protect(true);
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.5f);
        Vector3 way = OpenWay(local, 14f);
        Vector2 look = local.data.lookValues;
        Harmony keys = new Harmony("UltimateAscent.SmokeTest.keys");
        keys.Patch(AccessTools.Method(typeof(CharacterInput), "Sample"), postfix: new HarmonyMethod(typeof(Runner), nameof(ForceInput)));
        try
        {
            if (way == Vector3.zero)
            {
                Info("the footprints' check is skipped", $"no 14 m of flat, open ground near {local.Center:F0}");
                yield break;
            }
            local.data.lookValues = new Vector2(Quaternion.LookRotation(way).eulerAngles.y, 0f);
            yield return new WaitForSeconds(0.5f);
            // Prints from before (the scout's landing on the island) are not this walk's.
            HashSet<Transform> earlier = new HashSet<Transform>(PainSash.PrintsOnGround());
            Vector3 start = local.Center;
            List<Vector3> feet = new List<Vector3>();
            void Feet()
            {
                feet.Add(Flat(local.GetBodypart(BodypartType.Foot_L).transform.position));
                feet.Add(Flat(local.GetBodypart(BodypartType.Foot_R).transform.position));
            }
            forcedMove = new Vector2(0f, 1f);
            forcedSprint = false;
            forcingInput = true;
            for (float until = Time.time + 3f; Time.time < until;)
            {
                Feet();
                yield return null;
            }
            forcingInput = false;
            for (float until = Time.time + 0.6f; Time.time < until;)
            {
                Feet();
                yield return null;
            }
            Vector3 end = local.Center;
            float last = Time.time;
            List<Transform> prints = PainSash.PrintsOnGround().Where(p => !earlier.Contains(p)).ToList();
            List<string> off = new List<string>();
            foreach (Transform print in prints)
            {
                bool onGround = Physics.Raycast(print.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit floor, 1f, HelperFunctions.terrainMapMask)
                    && Mathf.Abs(floor.point.y - print.position.y) < 0.08f;
                float fromFeet = feet.Count > 0 ? feet.Min(f => Vector3.Distance(f, Flat(print.position))) : float.PositiveInfinity;
                if (!onGround || fromFeet > 0.4f)
                {
                    off.Add($"{print.position:F1} on the ground {onGround}, {fromFeet:0.00} m from where a foot was");
                }
            }
            int lefts = prints.Count(p => p.localScale.x < 0f);
            Check("walking with the Pain Sash on leaves a boot print at each step, left and right in turn, on the ground where a foot was",
                prints.Count >= 4 && prints.Count <= 40 && Mathf.Abs(lefts - (prints.Count - lefts)) <= 2 && off.Count == 0,
                $"{prints.Count} prints ({lefts} left, {prints.Count - lefts} right) over {Vector3.Distance(Flat(start), Flat(end)):0.0} m walked ({earlier.Count} from before the walk); {(off.Count > 0 ? string.Join("; ", off) : "all on the ground where a foot was")}");
            // Pictured: the trail from ahead of the scout, and one print from above.
            Vector3 middle = (start + end) * 0.5f;
            GameObject holder = new GameObject("UltimateAscentTestCamera");
            Camera view = holder.AddComponent<Camera>();
            view.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            view.fieldOfView = 50f;
            view.nearClipPlane = 0.05f;
            Vector3 side = Vector3.Cross(Vector3.up, way);
            holder.transform.position = middle + Vector3.up * 3.2f + side * 2.2f - way * 0.5f;
            holder.transform.LookAt(middle - Vector3.up * 0.9f);
            RenderView(holder, "pain-sash-footprints");
            if (prints.Count > 0)
            {
                Transform one = prints[prints.Count / 2];
                holder.transform.position = one.position + Vector3.up * 0.7f - way * 0.05f;
                holder.transform.LookAt(one.position);
                RenderView(holder, "pain-sash-print");
            }
            yield return new WaitForSeconds(Mathf.Max(0f, last + 8.2f - Time.time));
            holder.transform.position = middle + Vector3.up * 3.2f + side * 2.2f - way * 0.5f;
            holder.transform.LookAt(middle - Vector3.up * 0.9f);
            RenderView(holder, "pain-sash-footprints-fading");
            Destroy(holder);
            yield return new WaitForSeconds(Mathf.Max(0f, last + PainSash.PrintSeconds + 0.3f - Time.time));
            Check("the prints are gone 10 s after the last step", PainSash.PrintsShown == 0, $"{PainSash.PrintsShown} still on the ground");

            // Another sash: no prints.
            CharacterCustomization.SetCharacterSash(sashBefore == pain ? 0 : sashBefore);
            yield return new WaitForSeconds(0.5f);
            local.data.lookValues = new Vector2(Quaternion.LookRotation(-way).eulerAngles.y, 0f);
            forcingInput = true;
            yield return new WaitForSeconds(2f);
            forcingInput = false;
            yield return new WaitForSeconds(0.5f);
            Check("with another sash on, walking leaves no prints", !PainSash.Wears(local) && PainSash.PrintsShown == 0,
                $"wears the Pain Sash {PainSash.Wears(local)}, {PainSash.PrintsShown} prints");
        }
        finally
        {
            forcingInput = false;
            keys.UnpatchSelf();
            local.data.lookValues = look;
        }

        // Its unlock: ascent 25 won in the Nadir with every modifier on; not at the peak, not on 24, not with a modifier
        // off (the mod's count, called here as a win would; a switch off in memory).
        int ascent = Ascents._currentAscent;
        List<string> newBefore = new List<string>(CosmeticUnlocks.NewThisClimb);
        string savedUnlocks = ModSave.Get("unlocked");
        ConfigEntry<bool> seventeen = UltimateAscent.Plugin.AscentSwitches[Lvl.Hazards];
        bool saved17 = seventeen.Value;
        string Try(int on, bool inNadir, bool all)
        {
            ModSave.Set("unlocked", "");
            Ascents._currentAscent = on;
            seventeen.Value = all;
            HostOptions.Invalidate();
            CosmeticUnlocks.Won(inNadir);
            return $"{(inNadir ? "the Nadir" : "the peak")} on {on}{(all ? "" : " (17 off)")}: sash {CosmeticUnlocks.Has(CosmeticUnlocks.PainSash)}, skin {CosmeticUnlocks.Has(CosmeticUnlocks.BurntSkin)}";
        }
        List<string> tried = new List<string>();
        // A climb with a modifier off until now (the harness holds Bing Bong's rule back in a full run) unlocks nothing.
        bool allClimb = CosmeticUnlocks.AllModifiersAllClimb;
        bool unlockedRight;
        try
        {
            // (ascent, in the Nadir, every modifier on) and what it should unlock: the sash, the skin.
            unlockedRight = true;
            foreach ((int on, bool inNadir, bool all, bool sash, bool skin) in new[]
            {
                (25, false, true, false, false),
                (22, true, true, false, false),
                (23, true, true, false, true),
                (24, true, true, false, true),
                (25, true, false, false, false),
                (25, true, true, true, true),
            })
            {
                tried.Add(Try(on, inNadir, all));
                unlockedRight &= CosmeticUnlocks.Has(CosmeticUnlocks.PainSash) == (sash && allClimb) && CosmeticUnlocks.Has(CosmeticUnlocks.BurntSkin) == (skin && allClimb);
            }
            unlockedRight &= CosmeticUnlocks.NewThisClimb.Contains(CosmeticUnlocks.PainSash) == allClimb;
        }
        finally
        {
            Ascents._currentAscent = ascent;
            seventeen.Value = saved17;
            HostOptions.Invalidate();
            ModSave.Set("unlocked", savedUnlocks);
        }
        Check("won in the Nadir with every modifier on for the whole climb, ascent 25 unlocks the Pain Sash and the Burnt Skin, 23 and 24 the Burnt Skin; at the peak, on 22 or with a modifier off nothing",
            unlockedRight, string.Join("; ", tried) + $"; this climb so far: {CosmeticUnlocks.State}{(allClimb ? "" : " (a modifier was off: nothing unlocks)")}; off now: '{string.Join(", ", ModifiersLabel.Off())}'");
        CosmeticUnlocks.NewThisClimb.Clear();
        CosmeticUnlocks.NewThisClimb.AddRange(newBefore);

        // The statistics' unlock window names it a new sash.
        EndScreen screen = FindAnyObjectByType<EndScreen>(FindObjectsInactive.Include);
        CustomizationOption option = Singleton<Customization>.Instance.sashes[pain];
        if (screen != null)
        {
            Coroutine window = StartCoroutine(CosmeticUnlocks.Unlocked(screen, option));
            yield return null;
            yield return null;
            string title = screen.cosmeticUnlockTitle != null ? Plain(screen.cosmeticUnlockTitle.text) : "none";
            bool icon = screen.cosmeticUnlockIcon != null && screen.cosmeticUnlockIcon.texture == option.texture;
            screen.inPopupView = false;
            yield return new WaitForSeconds(0.5f);
            Check("the statistics' unlock window calls the Pain Sash a new sash, with its icon", title == Plain(LocalizedText.GetText("NEWSASH")) && icon,
                $"title '{title}' (the game's '{LocalizedText.GetText("NEWSASH")}'), icon {icon}");
        }
        else
        {
            Info("the unlock window check is skipped", "no end screen in the level");
        }
        CharacterCustomization.SetCharacterSash(sashBefore);
        ModSave.Set("sash", savedSash ?? "");
        ModSave.Set("unlocked", savedSashUnlocks ?? "");
        Info("the sash put back", $"sash {MyData.customizationData.currentSash}, saved '{ModSave.Get("sash")}', unlocked '{ModSave.Get("unlocked")}'");
    }

    /// <summary>
    /// The Pain Sash unlocked the real way (only with -Only painsashnadir, on ascent 25 with every modifier on; it ends
    /// the climb): the scout goes down to the Nadir and through its portal, the game's own ending there. The win in the
    /// Nadir unlocks the sash, and with Blind Mode on all climb the VR Headset too; the statistics show their small icons
    /// where badges go and the game's unlock window for each, the sash's named a new sash. (The harness keeps the win from
    /// Steam's stats and achievements.)
    /// </summary>
    private IEnumerator PainSashNadirEnding()
    {
        Character local = Local;
        if (target != Lvl.Last || !CosmeticUnlocks.AllModifiersOn())
        {
            Info("the Nadir ending is skipped", $"testing ascent {target}, modifiers off '{string.Join(", ", ModifiersLabel.Off())}' (it takes ascent 25 with every modifier on: -Ascent 25)");
            yield break;
        }
        ModSave.Set("unlocked", "");
        Protect(true);
        Info("the challenges before the Nadir", CosmeticUnlocks.State);
        MapHandler.JumpToSegment(Segment.Void);
        yield return new WaitForSeconds(10f);
        Check("the scout is in the Nadir after the jump, where a climb from ascent 8 is won", Map.InNadir && Ascents.shouldRequireNadir, $"place {Map.CurrentPlace}, Nadir required {Ascents.shouldRequireNadir}");
        Peak.PeakGatePortal portal = FindAnyObjectByType<Peak.PeakGatePortal>();
        if (portal == null)
        {
            Fail("the Nadir has its portal");
            yield break;
        }
        for (float until = Time.time + 30f; Time.time < until && (!local.refs.stats.IsInitialized || local.refs.stats.timelineInfo.Count == 0);)
        {
            yield return null;
        }
        // Through the portal, as a held interaction ends (2 s).
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, portal.transform.position + Vector3.up * 1.5f, false);
        yield return new WaitForSeconds(1.5f);
        portal.Interact(local);
        yield return new WaitForSeconds(portal.GetInteractTime(local));
        portal.Interact_CastFinished(local);
        EndScreen screen = null;
        for (float until = Time.realtimeSinceStartup + 120f; Time.realtimeSinceStartup < until && (screen == null || !screen.isOpen);)
        {
            screen = GUIManager.instance != null ? GUIManager.instance.endScreen : null;
            yield return null;
        }
        Check("the climb is won in the Nadir: the Pain Sash and the Burnt Skin unlock, and with Blind Mode on all climb the VR Headset too",
            local.refs.stats.won && local.refs.stats.wonViaNadir && CosmeticUnlocks.Has(CosmeticUnlocks.PainSash) && CosmeticUnlocks.Has(CosmeticUnlocks.BurntSkin) && CosmeticUnlocks.Has(CosmeticUnlocks.VrHeadset),
            $"won {local.refs.stats.won}, in the Nadir {local.refs.stats.wonViaNadir}, unlocked '{ModSave.Get("unlocked")}'");
        if (screen == null || !screen.isOpen)
        {
            Fail("the climb's statistics open");
            yield break;
        }
        CustomizationOption option = Singleton<Customization>.Instance.sashes[ModCosmetics.IndexOf("painsash")];
        CustomizationOption skin = Singleton<Customization>.Instance.skins[ModCosmetics.IndexOf(ModCosmetics.BurntSkinKey)];
        string title = null, skinTitle = null;
        bool icon = false, skinIcon = false;
        for (float until = Time.realtimeSinceStartup + 90f; Time.realtimeSinceStartup < until && !screen.buttons.activeSelf;)
        {
            if (screen.cosmeticUnlockObject.activeSelf && screen.inPopupView)
            {
                yield return new WaitForSecondsRealtime(1.5f);
                if (screen.cosmeticUnlockIcon.texture == option.texture)
                {
                    title = Plain(screen.cosmeticUnlockTitle.text);
                    icon = true;
                    Screenshot("statistics-pain-sash");
                }
                else if (screen.cosmeticUnlockIcon.texture == skin.texture && screen.cosmeticUnlockIcon.color == skin.color)
                {
                    skinTitle = Plain(screen.cosmeticUnlockTitle.text);
                    skinIcon = true;
                    Screenshot("statistics-burnt-skin");
                }
                yield return new WaitForSecondsRealtime(1f);
                screen.PopupNext();
                yield return new WaitForSecondsRealtime(0.6f);
            }
            else if ((screen.ascentsUnlockObject.activeSelf || screen.promotionUnlockObject.activeSelf) && screen.inPopupView)
            {
                screen.PopupNext();
                yield return new WaitForSecondsRealtime(0.6f);
            }
            yield return null;
        }
        Customization customization = Singleton<Customization>.Instance;
        CustomizationOption vr = customization.hats[ModCosmetics.IndexOf("vrheadset")];
        yield return new WaitForSecondsRealtime(1f);
        bool small = false, smallVr = false, smallSkin = false;
        Color skinShown = Color.clear;
        foreach (Transform shown in screen.badgeParentTF)
        {
            small |= shown.name == "UltimateAscentCosmetic " + option.name;
            smallVr |= shown.name == "UltimateAscentCosmetic " + vr.name;
            if (shown.name == "UltimateAscentCosmetic " + skin.name)
            {
                UnityEngine.UI.RawImage hand = shown.GetComponentsInChildren<UnityEngine.UI.RawImage>(true).FirstOrDefault(r => r.texture == skin.texture);
                skinShown = hand != null ? hand.color : Color.clear;
                smallSkin = hand != null && Mathf.Abs(hand.color.r - skin.color.r) < 0.02f && Mathf.Abs(hand.color.g - skin.color.g) < 0.02f && Mathf.Abs(hand.color.b - skin.color.b) < 0.02f;
            }
        }
        Screenshot("statistics-pain-sash-icons");
        Check("the statistics show the Pain Sash, the Burnt Skin (its hand in its colour) and the VR Headset as small icons where badges go, and the game's unlock windows: a new sash, a new look (the hand in the skin's colour)",
            small && smallVr && smallSkin && icon && title == Plain(LocalizedText.GetText("NEWSASH")) && skinIcon && skinTitle == Plain(LocalizedText.GetText("NEWLOOK")),
            $"icons in the badges' row: sash {small}, skin {smallSkin} (its hand {skinShown}, the skin {skin.color}), VR Headset {smallVr}; the sash's unlock window {icon} ('{title}'), the skin's {skinIcon} ('{skinTitle}')");
    }

    private static Vector3 Flat(Vector3 point) => new Vector3(point.x, 0f, point.z);

}
