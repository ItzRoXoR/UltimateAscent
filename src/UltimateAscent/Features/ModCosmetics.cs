using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Three cosmetics the game made and never offered, now in the passport (on every run, alone or with others):
/// the Orange Spacesuit (the Astronaut suit in the game's own orange texture), the Orange Helmet (the orange
/// space helmet every scout already carries, switched off) and the VR Headset (likewise). Each is unlocked by
/// one of the mod's challenges (<see cref="CosmeticUnlocks"/>); none has a Scout badge or touches the badge
/// ribbon. Clicking one while it is locked tells how to unlock it.
/// <para>What a player unlocked and which of these they wear is kept in the mod's save (<see cref="ModSave"/>),
/// not in Steam: the game would write the choice into a Steam stat, so for these it does not. Players without
/// the mod see the game's default in their place (the game's own correction of unknown cosmetics).</para>
/// <para>The hats: the game's hat list is shorter than each scout's row of hat models (the last two models are
/// the Bundled outfits' ice hoods, worn through those outfits), and a hat's place in the list is its model's
/// place in the row. So the two hats join the list at its end and their models go in at the same places, the
/// ice hoods moving two along (the Bundled outfits are told so).</para>
/// </summary>
internal static class ModCosmetics
{
    private sealed class Piece
    {
        internal string Key;
        internal string Unlock;
        internal Customization.Type Type;
        internal string Model;
        internal string Icon;
        internal CustomizationOption Option;
        internal int Index = -1;
    }

    private static readonly Piece OrangeHelmet = new Piece { Key = "orangehelmet", Unlock = CosmeticUnlocks.OrangeSpacesuit, Type = Customization.Type.Hat, Model = "SpaceHelmet Orange", Icon = "OrangeHelmet_icon.png" };
    private static readonly Piece VrHeadset = new Piece { Key = "vrheadset", Unlock = CosmeticUnlocks.VrHeadset, Type = Customization.Type.Hat, Model = "VRHeadset", Icon = "VRHeadset_icon.png" };
    private static readonly Piece OrangeSpacesuit = new Piece { Key = "orangespacesuit", Unlock = CosmeticUnlocks.OrangeSpacesuit, Type = Customization.Type.Fit, Icon = "OrangeSpacesuit_icon.png" };
    private static readonly Piece[] Pieces = { OrangeHelmet, VrHeadset, OrangeSpacesuit };

    internal const string AstronautFit = "Fit_Astronaut";
    internal const string AstronautHat = "Hat_26_Astronaut";

    private const string HatKey = "hat";
    private const string OutfitKey = "outfit";

    private static Customization extended;
    private static int hatsBefore = -1;
    private static readonly HashSet<CustomizationOption> movedHoods = new HashSet<CustomizationOption>();
    private static bool failed;

    /// <summary>The cosmetics a challenge unlocks.</summary>
    internal static IEnumerable<CustomizationOption> OptionsOf(string unlock)
    {
        Ensure();
        foreach (Piece piece in Pieces)
        {
            if (piece.Unlock == unlock && piece.Option != null)
            {
                yield return piece.Option;
            }
        }
    }

    /// <summary>Where a cosmetic stands in its list (for the tests), or -1.</summary>
    internal static int IndexOf(string key)
    {
        Ensure();
        foreach (Piece piece in Pieces)
        {
            if (piece.Key == key)
            {
                return piece.Index;
            }
        }
        return -1;
    }

    private static Piece PieceOf(CustomizationOption option)
    {
        if (option == null)
        {
            return null;
        }
        foreach (Piece piece in Pieces)
        {
            if (piece.Option == option)
            {
                return piece;
            }
        }
        return null;
    }

    private static Piece PieceAt(Customization.Type type, int index)
    {
        foreach (Piece piece in Pieces)
        {
            if (piece.Type == type && piece.Index == index && index >= 0)
            {
                return piece;
            }
        }
        return null;
    }

    /// <summary>Adds the three to the game's lists, once per Customization (they are there before anything reads an index).</summary>
    internal static void Ensure()
    {
        Customization customization = Singleton<Customization>.Instance;
        if (failed || customization == null || customization == extended || customization.hats == null || customization.fits == null)
        {
            return;
        }
        extended = customization;
        try
        {
            CustomizationOption astronaut = Array.Find(customization.fits, fit => fit != null && fit.name == AstronautFit);
            CustomizationRefs scout = ScoutRefs();
            Renderer orangeHelmet = scout != null ? Model(scout, OrangeHelmet.Model) : null;
            Material orangeSuit = orangeHelmet != null && orangeHelmet.sharedMaterials.Length > 1 ? orangeHelmet.sharedMaterials[1] : null;
            if (astronaut == null || orangeSuit == null)
            {
                throw new InvalidOperationException($"the game's pieces are missing (Astronaut suit {astronaut != null}, orange suit material {orangeSuit != null})");
            }

            // The options are made once; a Customization made again (its lists fresh from the game) gets the same ones.
            List<CustomizationOption> hats = new List<CustomizationOption>(customization.hats);
            hats.RemoveAll(hat => PieceOf(hat) != null);
            hatsBefore = hats.Count;
            foreach (Piece piece in new[] { OrangeHelmet, VrHeadset })
            {
                piece.Option ??= NewOption(piece, Customization.Type.Hat);
                piece.Index = hats.Count;
                hats.Add(piece.Option);
            }
            customization.hats = hats.ToArray();
            // The ice hoods move two along in every scout's row of hat models (the outfits are the game's own
            // assets, kept between scenes: each moves only once).
            foreach (CustomizationOption fit in customization.fits)
            {
                if (fit != null && fit.overrideHat && fit.overrideHatIndex >= hatsBefore && movedHoods.Add(fit))
                {
                    fit.overrideHatIndex += 2;
                }
            }

            if (OrangeSpacesuit.Option == null)
            {
                CustomizationOption orange = UnityEngine.Object.Instantiate(astronaut);
                orange.name = "UA_Fit_OrangeSpacesuit";
                orange.hideFlags = HideFlags.DontUnloadUnusedAsset;
                orange.requiredAchievement = ACHIEVEMENTTYPE.NONE;
                orange.requiresAscent = false;
                orange.customRequirement = CustomizationOption.CUSTOMREQUIREMENT.None;
                orange.fitMaterial = orangeSuit;
                orange.fitMaterialOverrideHat = OrangeCap(astronaut.fitMaterialOverrideHat, orangeSuit);
                orange.texture = Icon(OrangeSpacesuit.Icon) ?? astronaut.texture;
                OrangeSpacesuit.Option = orange;
            }
            List<CustomizationOption> fits = new List<CustomizationOption>(customization.fits);
            fits.RemoveAll(fit => PieceOf(fit) != null);
            OrangeSpacesuit.Index = fits.Count;
            fits.Add(OrangeSpacesuit.Option);
            customization.fits = fits.ToArray();
            Plugin.Log.LogInfo($"Cosmetics: Orange Helmet and VR Headset are hats {OrangeHelmet.Index} and {VrHeadset.Index}, the Orange Spacesuit is outfit {OrangeSpacesuit.Index}.");
        }
        catch (Exception ex)
        {
            failed = true;
            Plugin.Log.LogWarning("Cosmetics: the mod's cosmetics could not be added (" + ex.Message + ").");
        }
    }

    private static CustomizationOption NewOption(Piece piece, Customization.Type type)
    {
        CustomizationOption option = ScriptableObject.CreateInstance<CustomizationOption>();
        option.name = "UA_" + type + "_" + piece.Key;
        option.hideFlags = HideFlags.DontUnloadUnusedAsset;
        option.type = type;
        option.texture = Icon(piece.Icon);
        return option;
    }

    /// <summary>The Astronaut suit's cap and beret in orange (the cap material with the orange suit's texture).</summary>
    private static Material OrangeCap(Material white, Material orangeSuit)
    {
        if (white == null)
        {
            return null;
        }
        Material orange = new Material(white) { name = white.name + " (Orange)", hideFlags = HideFlags.DontUnloadUnusedAsset };
        Texture texture = orangeSuit.GetTexture("_MainTex");
        if (texture != null && orange.HasProperty("_MainTex"))
        {
            orange.SetTexture("_MainTex", texture);
        }
        return orange;
    }

    private static Texture2D Icon(string name)
    {
        using Stream stream = typeof(ModCosmetics).Assembly.GetManifestResourceStream("UltimateAscent." + name);
        if (stream == null)
        {
            return null;
        }
        byte[] bytes = new byte[stream.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int n = stream.Read(bytes, read, bytes.Length - read);
            if (n <= 0)
            {
                break;
            }
            read += n;
        }
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true) { name = "UA_" + Path.GetFileNameWithoutExtension(name), hideFlags = HideFlags.DontUnloadUnusedAsset };
        texture.LoadImage(bytes);
        return texture;
    }

    /// <summary>A scout's customization parts, from the scout prefab (any scout carries every hat model).</summary>
    private static CustomizationRefs ScoutRefs()
    {
        GameObject prefab = Resources.Load<GameObject>("Character");
        CustomizationRefs refs = prefab != null ? prefab.GetComponentInChildren<CustomizationRefs>(true) : null;
        return refs != null ? refs : UnityEngine.Object.FindAnyObjectByType<CustomizationRefs>(FindObjectsInactive.Include);
    }

    /// <summary>A hat model among a scout's hats, by name.</summary>
    private static Renderer Model(CustomizationRefs refs, string name)
    {
        if (refs.playerHats == null || refs.playerHats.Length == 0 || refs.playerHats[0] == null)
        {
            return null;
        }
        Transform hats = refs.playerHats[0].transform.parent;
        Transform model = hats != null ? hats.Find(name) : null;
        return model != null ? model.GetComponent<Renderer>() : null;
    }

    /// <summary>Puts the two hat models into this scout's row at the hats' places (once per scout).</summary>
    private static void Dress(CustomizationRefs refs)
    {
        Ensure();
        if (refs == null || refs.playerHats == null || hatsBefore < 0 || OrangeHelmet.Option == null || refs.playerHats.Length < hatsBefore)
        {
            return;
        }
        if (refs.playerHats.Length > OrangeHelmet.Index && refs.playerHats[OrangeHelmet.Index] != null && refs.playerHats[OrangeHelmet.Index].name == OrangeHelmet.Model)
        {
            return;
        }
        List<Renderer> row = new List<Renderer>(refs.playerHats);
        row.Insert(hatsBefore, ModelOrStandIn(refs, VrHeadset.Model));
        row.Insert(hatsBefore, ModelOrStandIn(refs, OrangeHelmet.Model));
        refs.playerHats = row.ToArray();
    }

    /// <summary>The model, or an empty stand-in (so a scout made without it still has every place in the row).</summary>
    private static Renderer ModelOrStandIn(CustomizationRefs refs, string name)
    {
        Renderer model = Model(refs, name);
        if (model != null)
        {
            return model;
        }
        GameObject standIn = new GameObject(name);
        standIn.SetActive(false);
        standIn.transform.SetParent(refs.playerHats[0].transform.parent, false);
        standIn.AddComponent<MeshFilter>();
        return standIn.AddComponent<MeshRenderer>();
    }

    internal static bool Unlocked(CustomizationOption option)
    {
        Piece piece = PieceOf(option);
        return piece == null || CosmeticUnlocks.Has(piece.Unlock);
    }

    [HarmonyPatch(typeof(CustomizationOption), "get_IsLocked")]
    private static class Locked
    {
        private static void Postfix(CustomizationOption __instance, ref bool __result)
        {
            Piece piece = PieceOf(__instance);
            if (piece != null)
            {
                __result = !CosmeticUnlocks.Has(piece.Unlock);
            }
        }
    }

    // The lists are complete before anything reads them: TFA hooks the same places for its own cosmetics.
    [HarmonyPatch(typeof(CharacterCustomizationData), nameof(CharacterCustomizationData.CorrectValues))]
    private static class BeforeCorrecting
    {
        private static void Prefix() => Ensure();
    }

    [HarmonyPatch(typeof(Customization), nameof(Customization.GetRandomUnlockedIndex))]
    private static class BeforeRandom
    {
        private static void Prefix() => Ensure();
    }

    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.SetButtons))]
    private static class BeforePassport
    {
        private static void Prefix() => Ensure();
    }

    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.OnPlayerDataChange))]
    private static class DressScout
    {
        private static void Prefix(CharacterCustomization __instance) => Dress(__instance.refs);
    }

    [HarmonyPatch(typeof(PlayerCustomizationDummy), nameof(PlayerCustomizationDummy.UpdateDummy))]
    private static class DressDummy
    {
        private static void Prefix(PlayerCustomizationDummy __instance) => Dress(__instance.refs);
    }

    [HarmonyPatch(typeof(PlayerCustomizationDummy), nameof(PlayerCustomizationDummy.SetPlayerHat))]
    private static class DressDummyHat
    {
        private static void Prefix(PlayerCustomizationDummy __instance) => Dress(__instance.refs);
    }

    /// <summary>While the game loads the player's cosmetics from Steam (it would forget a choice of ours).</summary>
    private static int loading;

    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.TryGetCosmeticsFromSteam))]
    private static class LoadFromSteam
    {
        private static void Prefix()
        {
            Ensure();
            loading++;
        }

        private static void Postfix()
        {
            loading--;
            Restore(Customization.Type.Hat, HatKey);
            Restore(Customization.Type.Fit, OutfitKey);
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                loading = 0;
            }
            return __exception;
        }
    }

    /// <summary>Puts a saved choice of ours back on (if it is still unlocked).</summary>
    private static void Restore(Customization.Type type, string saveKey)
    {
        string key = ModSave.Get(saveKey);
        foreach (Piece piece in Pieces)
        {
            if (piece.Key == key && piece.Type == type && piece.Index >= 0 && CosmeticUnlocks.Has(piece.Unlock))
            {
                Wear(piece);
                return;
            }
        }
    }

    private static void Wear(Piece piece)
    {
        CharacterCustomizationData data = CharacterCustomization.GetCustomizationData(PhotonNetwork.LocalPlayer);
        if (piece.Type == Customization.Type.Hat)
        {
            data.currentHat = piece.Index;
            ModSave.Set(HatKey, piece.Key);
        }
        else
        {
            data.currentOutfit = piece.Index;
            ModSave.Set(OutfitKey, piece.Key);
        }
        CharacterCustomization.SetCustomizationData(data, PhotonNetwork.LocalPlayer);
    }

    /// <summary>A hat of ours goes on without the game's Steam stat; any other hat goes on as usual (and ours is no longer saved).</summary>
    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.SetCharacterHat))]
    private static class ChooseHat
    {
        private static bool Prefix(int index) => Choose(Customization.Type.Hat, HatKey, index);
    }

    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.SetCharacterOutfit))]
    private static class ChooseOutfit
    {
        private static bool Prefix(int index) => Choose(Customization.Type.Fit, OutfitKey, index);
    }

    private static bool Choose(Customization.Type type, string saveKey, int index)
    {
        Ensure();
        Piece piece = PieceAt(type, index);
        if (piece != null)
        {
            Wear(piece);
            return false;
        }
        if (loading == 0)
        {
            ModSave.Set(saveKey, "");
        }
        return true;
    }

    /// <summary>A click on one of these while it is locked shows how to unlock it, on the passport's empty upper page.</summary>
    [HarmonyPatch(typeof(PassportButton), nameof(PassportButton.Click))]
    private static class HowToUnlock
    {
        private static bool Prefix(PassportButton __instance)
        {
            Piece piece = PieceOf(__instance.currentOption);
            if (piece == null || !__instance.currentOption.IsLocked || __instance.manager.testUnlockAll)
            {
                // Any other item (the game's, TFA's, an unlocked one of ours): the note was about another one.
                Requirement.Hide();
                return true;
            }
            Requirement.Show(__instance.manager, CosmeticUnlocks.Requirement(piece.Unlock));
            return false;
        }
    }

    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.PageNext))]
    private static class HideOnNextPage
    {
        private static void Postfix() => Requirement.Hide();
    }

    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.PagePrev))]
    private static class HideOnPreviousPage
    {
        private static void Postfix() => Requirement.Hide();
    }

    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.OpenTab))]
    private static class HideOnTab
    {
        private static void Postfix() => Requirement.Hide();
    }

    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.SetOption))]
    private static class HideOnChoice
    {
        private static void Postfix() => Requirement.Hide();
    }

    /// <summary>The note on the passport's empty upper page, in the passport's own lettering.</summary>
    internal static class Requirement
    {
        private const float Seconds = 6f;

        private static TextMeshProUGUI label;
        private static float shownAt;

        internal static string Shown => label != null && label.gameObject.activeSelf ? label.text : null;

        internal static void Show(PassportManager manager, string text)
        {
            if (label == null)
            {
                label = Make(manager);
                if (label == null)
                {
                    return;
                }
            }
            label.text = text;
            Place(label.rectTransform, (RectTransform)label.transform.parent);
            label.gameObject.SetActive(true);
            shownAt = Time.unscaledTime;
            manager.StartCoroutine(HideLater(shownAt));
        }

        internal static void Hide()
        {
            if (label != null)
            {
                label.gameObject.SetActive(false);
            }
        }

        private static IEnumerator HideLater(float since)
        {
            yield return new WaitForSecondsRealtime(Seconds);
            if (shownAt == since)
            {
                Hide();
            }
        }

        /// <summary>
        /// The passport's empty upper page (only its lower part is on the screen): in the passport's canvas, the page
        /// above the one with the name.
        /// </summary>
        private static RectTransform UpperPage(PassportManager manager)
        {
            Canvas canvas = manager.nameText != null ? manager.nameText.canvas : null;
            if (canvas == null)
            {
                return null;
            }
            float nameTop = Top(manager.nameText.rectTransform);
            RectTransform best = null;
            float width = 0f;
            foreach (UnityEngine.UI.Graphic part in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                RectTransform rect = part.rectTransform;
                if (part is TMP_Text || Bottom(rect) < nameTop || rect.rect.width < 200f)
                {
                    continue;
                }
                float wide = Width(rect);
                // The widest such page; the one named as the game names it wins a tie.
                if (wide > width + 1f || (Mathf.Abs(wide - width) <= 1f && part.name == "BG (1)"))
                {
                    width = wide;
                    best = rect;
                }
            }
            return best;
        }

        /// <summary>Across the middle of the page's part that is on the screen, a little narrower than the page.</summary>
        private static void Place(RectTransform rect, RectTransform page)
        {
            Vector3[] corners = new Vector3[4];
            page.GetWorldCorners(corners);
            float bottom = corners[0].y;
            float top = Mathf.Min(corners[1].y, Screen.height);
            Vector3 middle = new Vector3((corners[0].x + corners[2].x) / 2f, (bottom + Mathf.Max(top, bottom)) / 2f, corners[0].z);
            float scale = page.lossyScale.x > 0f ? page.lossyScale.x : 1f;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // A margin on every side, so the lettering keeps clear of the page's edges.
            rect.sizeDelta = new Vector2((corners[2].x - corners[0].x) / scale - 80f, Mathf.Max(top - bottom, 40f) / scale - 16f);
            rect.position = middle;
        }

        private static float Top(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners[1].y;
        }

        private static float Bottom(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Mathf.Min(corners[0].y, corners[1].y);
        }

        private static float Width(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Mathf.Abs(corners[2].x - corners[0].x);
        }

        private static TextMeshProUGUI Make(PassportManager manager)
        {
            RectTransform page = UpperPage(manager);
            if (page == null)
            {
                return null;
            }
            GameObject holder = new GameObject("UltimateAscentRequirement", typeof(RectTransform));
            holder.transform.SetParent(page, false);
            TextMeshProUGUI text = holder.AddComponent<TextMeshProUGUI>();
            text.font = manager.nameText.font;
            text.fontSharedMaterial = manager.nameText.fontSharedMaterial;
            text.color = manager.nameText.color;
            // As big as the page's room allows (it stays inside the page).
            text.enableAutoSizing = true;
            text.fontSizeMax = 24f;
            text.fontSizeMin = 14f;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Truncate;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            Place((RectTransform)holder.transform, page);
            holder.SetActive(false);
            return text;
        }
    }
}
