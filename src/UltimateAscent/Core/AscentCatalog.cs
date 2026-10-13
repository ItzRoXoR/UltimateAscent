using System;
using System.Collections.Generic;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Core;

/// <summary>
/// Adds ascents 9–25 to the game's own <see cref="AscentData"/> list, so the boarding pass,
/// the in-run ascent label, the end screen and campfire saves all handle them natively.
/// The vanilla list is indexed as <c>ascents[ascent + 2]</c> (0 = custom, 1 = tenderfoot, 2 = peak).
/// </summary>
internal static class AscentCatalog
{
    private readonly struct Entry
    {
        internal readonly string Description;
        internal readonly string Reward;
        internal readonly Color Color;

        internal Entry(string description, string reward, Color color)
        {
            Description = description;
            Reward = reward;
            Color = color;
        }
    }

    // Shown on the boarding pass. The game appends "and all other ascents" by itself.
    private static readonly Entry[] Entries =
    {
        // 9
        new Entry("The fog is twice as fast and starts almost at once, until the Caldera or the Gloom. The Kiln's lava and the Citadel's gloom rise sooner and faster.",
            "Fog Dodger", new Color32(255, 166, 77, 255)),
        // 10
        new Entry("Big and Explorer's luggage have a chance to contain only one item.",
            "Light Packer", new Color32(255, 140, 64, 255)),
        // 11
        new Entry("Rule zero is stricter: stray from your group and the Scoutmaster hunts you. Every death curses the survivors, and eating a scout poisons you.",
            "Rule Zero Keeper", new Color32(255, 115, 51, 255)),
        // 12
        new Entry("Falling and helping people up make you drowsy.",
            "Sleepwalker", new Color32(242, 92, 64, 255)),
        // 13
        new Entry("Butter fingers: fall damage makes you drop everything, your backpack included.",
            "Butterfingers", new Color32(230, 72, 72, 255)),
        // 14
        new Entry("Items are worse.",
            "Scrap Scavenger", new Color32(217, 56, 80, 255)),
        // 15
        new Entry("Cold, drowsiness, poison, spores and heat apply more.",
            "Thick-Skinned Scout", new Color32(204, 46, 92, 255)),
        // 16
        new Entry("Heat heals 3x slower in the Caldera and the Kiln, drowsiness in the Gloom and the Citadel. Campfires are extremely hot.",
            "Fire Tender", new Color32(191, 38, 106, 255)),
        // 17
        new Entry("Hazards are more dangerous.",
            "Hazard Wrangler", new Color32(173, 33, 122, 255)),
        // 18
        new Entry("Weather is more extreme, including the Caldera's lava. Nights are colder, and longer in the Alpine.",
            "Storm Chaser", new Color32(153, 31, 140, 255)),
        // 19
        new Entry("No more revive statues, and reviving comes at a price.",
            "Last Chance Scout", new Color32(133, 31, 153, 255)),
        // 20
        new Entry("Bing Bong calls upon an evil force if not given attention.",
            "Bing Bong's Keeper", new Color32(112, 33, 163, 255)),
        // 21
        new Entry("Suitcases are painful. Rarer luggage hurts more.",
            "Bruised Bellhop", new Color32(92, 36, 168, 255)),
        // 22
        new Entry("All status ailments have unique effects.",
            "Walking Ailment", new Color32(74, 38, 163, 255)),
        // 23
        new Entry("The sun is hot everywhere but the Alpine. Whatever you shield yourself with burns if used too long, twice as fast in the Mesa.",
            "Sun-Scorched", new Color32(56, 38, 148, 255)),
        // 24
        new Entry("Going unconscious is instant death.",
            "Wide Awake", new Color32(41, 33, 125, 255)),
        // 25
        new Entry("All weather is present everywhere, at slightly less effect.",
            "Ultimate Scout", new Color32(20, 20, 20, 255)),
    };

    internal static bool Registered { get; private set; }

    internal static string TitleKey(int level) => "ULTIMATEASCENT_" + level;

    internal static string RewardKey(int level) => "ULTIMATEASCENT_" + level + "_REWARD";

    internal static string DisplayTitle(int level) => "Ascent " + level;

    internal static string Description(int level) => Entries[level - Lvl.First].Description;

    /// <summary>Registers the extra ascents and their texts. Safe to call repeatedly.</summary>
    internal static bool Ensure(AscentData data)
    {
        if (data == null || data.ascents == null)
        {
            return false;
        }
        List<AscentData.AscentInstanceData> list = data.ascents;
        int ascent8Index = Lvl.VanillaLast + 2;
        if (list.Count <= ascent8Index)
        {
            Plugin.Log.LogError($"The game's ascent list is shorter than expected ({list.Count} entries); extra ascents were not added.");
            return false;
        }
        AscentData.AscentInstanceData ascent8 = list[ascent8Index];
        for (int level = Lvl.First; level <= Lvl.Last; level++)
        {
            int index = level + 2;
            if (index < list.Count)
            {
                if (list[index] != null && list[index].title == TitleKey(level))
                {
                    continue;
                }
                Plugin.Log.LogError($"Ascent slot {level} is already used by \"{list[index]?.title}\" (another ascent mod?). Ultimate Ascent levels were not added.");
                return false;
            }
            Entry entry = Entries[level - Lvl.First];
            list.Add(new AscentData.AscentInstanceData
            {
                title = TitleKey(level),
                titleReward = RewardKey(level),
                description = entry.Description,
                color = entry.Color,
                sashSprite = ascent8.sashSprite
            });
        }
        AddTexts();
        Registered = true;
        return true;
    }

    internal static bool EnsureDefault()
    {
        try
        {
            return Ensure(SingletonAsset<AscentData>.Instance);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("Could not register the extra ascents: " + ex);
            return false;
        }
    }

    private static bool textsAdded;

    /// <summary>If the game ever reloads its text table, add the ascent texts again.</summary>
    [HarmonyLib.HarmonyPatch(typeof(LocalizedText), nameof(LocalizedText.LoadMainTable))]
    private static class KeepTexts
    {
        private static void Postfix()
        {
            if (Registered)
            {
                AddTexts();
            }
        }
    }

    private static void AddTexts()
    {
        Dictionary<string, List<string>> table = LocalizedText.mainTable;
        if (textsAdded && table.ContainsKey(TitleKey(Lvl.First)))
        {
            return;
        }
        for (int level = Lvl.First; level <= Lvl.Last; level++)
        {
            Entry entry = Entries[level - Lvl.First];
            Put(table, TitleKey(level), DisplayTitle(level).ToUpperInvariant());
            Put(table, LocalizedText.GetDescriptionIndex(TitleKey(level)), entry.Description.ToUpperInvariant());
            Put(table, RewardKey(level), entry.Reward.ToUpperInvariant());
        }
        textsAdded = true;
    }

    private static void Put(Dictionary<string, List<string>> table, string key, string text)
    {
        int languages = Enum.GetValues(typeof(LocalizedText.Language)).Length;
        List<string> values = new List<string>(languages);
        for (int i = 0; i < languages; i++)
        {
            values.Add(text);
        }
        table[key.ToUpperInvariant()] = values;
    }
}
