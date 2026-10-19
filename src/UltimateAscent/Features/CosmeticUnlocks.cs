using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// The challenges that unlock the mod's cosmetics (<see cref="ModCosmetics"/>), each counted for the player
/// who reaches the peak (any ascent, alone or with others; not custom runs, not the Nadir):
/// <list type="bullet">
/// <item>VR Headset: Blind Mode on for the whole climb.</item>
/// <item>Orange Spacesuit and Orange Helmet: the Astronaut suit and the Astronaut helmet worn together for the whole climb, with the sun that
/// incinerates held items: Hot Mode on (below ascent 23), or ascent 23's own sun (its rule on).</item>
/// </list>
/// What is unlocked stays in the mod's save. The run's statistics show each new cosmetic as a small icon where
/// a Scout badge would go, then the game's own "new outfit / new hat" window for it.
/// </summary>
internal static class CosmeticUnlocks
{
    internal const string VrHeadset = "vrheadset";
    internal const string OrangeSpacesuit = "orangespacesuit";

    private const string SaveKey = "unlocked";

    /// <summary>The cosmetics' icons in the statistics are this size of a badge's.</summary>
    private const float IconScale = 0.75f;

    private static bool climbing;
    private static bool blindAllClimb;
    private static bool hotAllClimb;
    private static bool astronautAllClimb;
    private static float nextLook;

    /// <summary>Unlocked during this climb, still to be shown in its statistics.</summary>
    internal static readonly List<string> NewThisClimb = new List<string>();

    internal static bool Has(string unlock) => Array.IndexOf(ModSave.Get(SaveKey).Split(','), unlock) >= 0;

    private static void Add(string unlock)
    {
        if (Has(unlock))
        {
            return;
        }
        string saved = ModSave.Get(SaveKey);
        ModSave.Set(SaveKey, saved.Length == 0 ? unlock : saved + "," + unlock);
        NewThisClimb.Add(unlock);
        Plugin.Log.LogInfo("Cosmetics unlocked: " + unlock + ".");
    }

    /// <summary>How to unlock a cosmetic, as the passport tells it (the mod's name first).</summary>
    internal static string Requirement(string unlock)
    {
        bool russian = LocalizedText.CURRENT_LANGUAGE == LocalizedText.Language.Russian;
        string how = unlock == VrHeadset
            ? (russian ? "Доберитесь до вершины, не выключая Слепой режим весь подъём." : "Reach the peak with Blind Mode on for the whole climb.")
            : (russian ? "Доберитесь до вершины в костюме и шлеме астронавта с Жарким режимом (или на 23+)." : "Reach the peak in the Astronaut suit and helmet with Hot Mode on (or on ascent 23+).");
        // The mod's name in the passport's own blue, the one its field names wear.
        return "<color=#4A8FE7>ULTIMATE ASCENT:</color> " + how;
    }

    /// <summary>The local scout wears the game's Astronaut suit and its Astronaut helmet, both at once.</summary>
    internal static bool WearsAstronaut()
    {
        Customization customization = Singleton<Customization>.Instance;
        if (customization == null || !PhotonNetwork.InRoom)
        {
            return false;
        }
        CharacterCustomizationData data = GameHandler.GetService<PersistentPlayerDataService>().GetPlayerData(PhotonNetwork.LocalPlayer).customizationData;
        int outfit = data.currentOutfit;
        int hat = data.currentHat;
        return outfit >= 0 && outfit < customization.fits.Length && customization.fits[outfit] != null && customization.fits[outfit].name == ModCosmetics.AstronautFit
            && hat >= 0 && hat < customization.hats.Length && customization.hats[hat] != null && customization.hats[hat].name == ModCosmetics.AstronautHat;
    }

    /// <summary>Every frame: a climb's challenges start when it does and break if a condition stops holding.</summary>
    internal static void Tick()
    {
        if (!Rules.InRun)
        {
            climbing = false;
            return;
        }
        if (Character.localCharacter == null)
        {
            return;
        }
        if (!climbing)
        {
            climbing = true;
            NewThisClimb.Clear();
            bool counts = !RunSettings.IsCustomRun;
            blindAllClimb = counts && ChallengeModes.Blind;
            hotAllClimb = counts && ChallengeModes.Hot;
            astronautAllClimb = counts && WearsAstronaut();
            nextLook = 0f;
        }
        if (Time.unscaledTime < nextLook)
        {
            return;
        }
        nextLook = Time.unscaledTime + 1f;
        blindAllClimb &= ChallengeModes.Blind;
        hotAllClimb &= ChallengeModes.Hot;
        astronautAllClimb &= WearsAstronaut();
    }

    /// <summary>The challenges' state in this climb, for the tests.</summary>
    internal static string State => $"blind {blindAllClimb}, hot {hotAllClimb}, astronaut {astronautAllClimb}";

    /// <summary>The local scout has won the climb at the peak.</summary>
    internal static void Won()
    {
        if (!climbing)
        {
            return;
        }
        if (blindAllClimb && ChallengeModes.Blind)
        {
            Add(VrHeadset);
        }
        bool incinerating = Rules.Enabled(Lvl.HotSun) || (hotAllClimb && ChallengeModes.Hot && Ascents._currentAscent < Lvl.HotSun);
        if (astronautAllClimb && WearsAstronaut() && incinerating)
        {
            Add(OrangeSpacesuit);
        }
    }

    [HarmonyPatch(typeof(CharacterStats), nameof(CharacterStats.Win))]
    private static class AtThePeak
    {
        private static void Postfix(CharacterStats __instance)
        {
            if (__instance.character != null && __instance.character.IsLocal && !__instance.wonViaNadir)
            {
                Won();
            }
        }
    }

    /// <summary>After the badges of the climb's statistics: the new cosmetics, each a small icon in the badges' row and the game's unlock window.</summary>
    [HarmonyPatch(typeof(EndScreen), nameof(EndScreen.BadgeRoutine))]
    private static class InTheStatistics
    {
        private static void Postfix(EndScreen __instance, ref IEnumerator __result)
        {
            __result = After(__result, __instance);
        }
    }

    private static IEnumerator After(IEnumerator badges, EndScreen screen)
    {
        while (badges.MoveNext())
        {
            yield return badges.Current;
        }
        List<string> unlocked = new List<string>(NewThisClimb);
        NewThisClimb.Clear();
        foreach (string unlock in unlocked)
        {
            foreach (CustomizationOption option in ModCosmetics.OptionsOf(unlock))
            {
                ShowIcon(screen, option);
                yield return new WaitForSeconds(0.2f);
                yield return screen.CosmeticUnlockRoutine(option);
                yield return new WaitForSeconds(0.2f);
            }
        }
    }

    /// <summary>The cosmetic's icon, smaller, where the climb's badges are (a badge's frame, without a badge behind it).</summary>
    private static void ShowIcon(EndScreen screen, CustomizationOption option)
    {
        if (screen.badge == null || screen.badgeParentTF == null)
        {
            return;
        }
        BadgeUI frame = UnityEngine.Object.Instantiate(screen.badge, screen.badgeParentTF);
        frame.name = "UltimateAscentCosmetic " + option.name;
        frame.gameObject.SetActive(true);
        frame.icon.texture = option.texture;
        frame.icon.color = Color.white;
        frame.icon.enabled = true;
        frame.icon.transform.localScale = Vector3.one * IconScale;
        if (frame.blank != null)
        {
            frame.blank.enabled = false;
        }
        if (frame.canvasGroup != null)
        {
            frame.canvasGroup.alpha = 1f;
        }
        // No badge behind it: hovering it must not ask the badge list about one.
        UnityEngine.Object.Destroy(frame);
    }
}
