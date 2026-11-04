using System;
using System.Collections;
using System.Linq;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>The Burnt Skin: in the passport, worn (its char and embers on the body, not the face), and off again.</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The Burnt Skin in the airport: the last skin in the passport, in its colour, locked (a click tells it takes the peak
    /// on ascent 23 or higher with all modifiers on); unlocked (in the mod's save), a click puts it on the passport's scout
    /// and the scout, saved in the mod's save and not in a Steam stat, its char and embers on the body and none on the
    /// head; another skin takes them off again. The skin and the save are put back as they were.
    /// </summary>
    private IEnumerator BurntSkinPassport()
    {
        Customization customization = Singleton<Customization>.Instance;
        int burnt = ModCosmetics.IndexOf(ModCosmetics.BurntSkinKey);
        int skinBefore = MyData.customizationData.currentSkin;
        string savedSkin = ModSave.Get("skin");
        string savedUnlocks = ModSave.Get("unlocked");
        ModSave.Set("unlocked", "");
        CustomizationOption option = burnt >= 0 && burnt < customization.skins.Length ? customization.skins[burnt] : null;
        Check("the Burnt Skin is the last skin, after the game's nine, in its own colour, locked and with no badge of its own",
            option != null && burnt == customization.skins.Length - 1 && burnt == 9 && option.color == BurntSkin.Colour && option.IsLocked && option.requiredAchievement == ACHIEVEMENTTYPE.NONE,
            $"skin {burnt} of {customization.skins.Length}, colour {option?.color}, locked {option?.IsLocked}");
        if (option == null)
        {
            yield break;
        }
        BoardingPass pass = GUIManager.instance.boardingPass;
        if (pass.isOpen)
        {
            pass.Close();
            yield return new WaitForSecondsRealtime(1f);
        }
        PassportManager passport = PassportManager.instance;
        passport.ToggleOpen();
        yield return new WaitForSecondsRealtime(1.5f);
        try
        {
            passport.OpenTab(Customization.Type.Skin);
            yield return new WaitForSecondsRealtime(0.5f);
            for (int page = 0; page < burnt / 28; page++)
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
            Check("a click on the locked Burnt Skin tells it takes the peak on ascent 23 or higher with all modifiers on, and it stays off",
                button != null && button.lockedIcon.gameObject.activeSelf && note != null && note.Contains("ULTIMATE ASCENT:") && note.Contains("ascent 23 or higher")
                && note.Contains("all modifiers") && MyData.customizationData.currentSkin == skinBefore,
                $"button {button != null}, note '{note}'");
            Screenshot("passport-locked-burnt-skin");
            yield return new WaitForSecondsRealtime(1f);

            ModSave.Set("unlocked", "burntskin");
            passport.SetButtons();
            yield return new WaitForSecondsRealtime(0.5f);
            NoSteamStatWrites.Asked.Clear();
            button = Button();
            if (button != null)
            {
                ClickOn((RectTransform)button.button.transform);
            }
            yield return new WaitForSecondsRealtime(1f);
            CustomizationRefs scout = Local.refs.customization.refs;
            CustomizationRefs dummy = passport.dummy.refs;
            string Spots(CustomizationRefs refs)
            {
                Material body = refs.mainRenderer.sharedMaterial;
                Renderer head = refs.PlayerRenderers.FirstOrDefault(r => r != null && r != refs.mainRenderer && r.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0);
                Material face = head != null ? head.sharedMaterial : null;
                return $"body char {body.GetColor("_Color3").a:0.##}, embers {body.GetColor("_Color4").a:0.##}, laid in space {body.GetFloat("_Triplanar3On"):0}; "
                    + $"head {(face != null ? $"char {face.GetColor("_Color3").a:0.##}, embers {face.GetColor("_Color4").a:0.##}" : "none")}; colour {body.GetColor("_SkinColor")}";
            }
            bool Burnt(CustomizationRefs refs)
            {
                Material body = refs.mainRenderer.sharedMaterial;
                Renderer head = refs.PlayerRenderers.FirstOrDefault(r => r != null && r != refs.mainRenderer && r.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0);
                return body.GetColor("_Color3").a > 0.9f && body.GetColor("_Color4").a > 0.9f && body.GetFloat("_Triplanar3On") > 0.5f
                    && head != null && head.sharedMaterial.GetColor("_Color3").a < 0.01f && head.sharedMaterial.GetColor("_Color4").a < 0.01f;
            }
            Check("unlocked, the Burnt Skin is worn: its char and embers on the body of the passport's scout and the scout, none on the head; in the mod's save, no Steam stat",
                MyData.customizationData.currentSkin == burnt && Burnt(scout) && Burnt(dummy) && ModSave.Get("skin") == "burntskin"
                && !NoSteamStatWrites.Asked.Any(a => a.StartsWith("Cosmetic_Skin", StringComparison.Ordinal)),
                $"skin {MyData.customizationData.currentSkin}; scout: {Spots(scout)}; passport's scout: {Spots(dummy)}; saved '{ModSave.Get("skin")}', Steam stats asked: {string.Join(", ", NoSteamStatWrites.Asked)}");
            Screenshot("passport-burnt-skin");
            yield return new WaitForSecondsRealtime(1f);

            // Another skin: the spots come off.
            int other = skinBefore != burnt ? skinBefore : 0;
            CharacterCustomization.SetCharacterSkinColor(other);
            passport.dummy.UpdateDummy();
            yield return new WaitForSecondsRealtime(0.5f);
            bool Clean(CustomizationRefs refs) => refs.mainRenderer.sharedMaterial.GetColor("_Color3").a < 0.01f && refs.mainRenderer.sharedMaterial.GetColor("_Color4").a < 0.01f;
            Check("another skin takes the Burnt Skin's char and embers off again", Clean(scout) && Clean(dummy) && ModSave.Get("skin") == "",
                $"skin {MyData.customizationData.currentSkin}; scout: {Spots(scout)}; passport's scout: {Spots(dummy)}; saved '{ModSave.Get("skin")}'");
        }
        finally
        {
            CharacterCustomization.SetCharacterSkinColor(skinBefore);
            ModSave.Set("skin", savedSkin ?? "");
            ModSave.Set("unlocked", savedUnlocks ?? "");
            passport.ToggleOpen();
        }
        yield return new WaitForSecondsRealtime(1f);
    }
}
