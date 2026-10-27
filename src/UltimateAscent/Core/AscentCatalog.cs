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
        internal readonly Color Color;

        internal Entry(string description, Color color)
        {
            Description = description;
            Color = color;
        }
    }

    // Shown on the boarding pass. The game appends "and all other ascents" by itself.
    private static readonly Entry[] Entries =
    {
        // 9
        new Entry("The fog, the Kiln's lava and the Citadel's gloom start rising sooner and go faster.",
            new Color32(255, 166, 77, 255)),
        // 10
        new Entry("Big and Explorer's luggage have a chance to contain only one item.",
            new Color32(255, 140, 64, 255)),
        // 11
        new Entry("Rule zero is stricter.",
            new Color32(255, 115, 51, 255)),
        // 12
        new Entry("Falling and helping people up make you drowsy.",
            new Color32(242, 92, 64, 255)),
        // 13
        new Entry("Fall damage makes you drop everything.",
            new Color32(230, 72, 72, 255)),
        // 14
        new Entry("Cold, drowsiness, poison, spores and heat apply more.",
            new Color32(217, 56, 80, 255)),
        // 15
        new Entry("Items are worse.",
            new Color32(204, 46, 92, 255)),
        // 16
        new Entry("Heat in the Caldera and drowsiness in the Gloom heal slower.",
            new Color32(191, 38, 106, 255)),
        // 17
        new Entry("Hazards are more dangerous. Campfires are extremely hot.",
            new Color32(173, 33, 122, 255)),
        // 18
        new Entry("The weather is more extreme.",
            new Color32(153, 31, 140, 255)),
        // 19
        new Entry("No more revive statues, and reviving comes at a price.",
            new Color32(133, 31, 153, 255)),
        // 20
        new Entry("Bing Bong calls upon an evil force if not given attention.",
            new Color32(112, 33, 163, 255)),
        // 21
        new Entry("Suitcases are painful. Rarer luggage hurts more.",
            new Color32(92, 36, 168, 255)),
        // 22
        new Entry("All status ailments have unique effects.",
            new Color32(74, 38, 163, 255)),
        // 23
        new Entry("The sun is very hot.",
            new Color32(56, 38, 148, 255)),
        // 24
        new Entry("Going unconscious is instant death.",
            new Color32(41, 33, 125, 255)),
        // 25
        new Entry("All weather is present everywhere, at slightly less effect.",
            new Color32(20, 20, 20, 255)),
    };

    internal static bool Registered { get; private set; }

    /// <summary>The ascent entries this mod added to the game's list.</summary>
    private static readonly HashSet<AscentData.AscentInstanceData> added = new HashSet<AscentData.AscentInstanceData>();

    /// <summary>
    /// The ascent's title, which the game also uses as its text key, readable like the game's own
    /// "Ascent 1" to "Ascent 8": other mods (True Final Ascent's run records, daily board and Continue
    /// panel) show this field as it is.
    /// </summary>
    internal static string TitleKey(int level) => "Ascent " + level;

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
                // Told apart by the entry itself: other ascent mods title theirs the same way.
                if (list[index] != null && added.Contains(list[index]))
                {
                    continue;
                }
                Plugin.Log.LogError($"Ascent slot {level} is already used by \"{list[index]?.title}\" (another ascent mod?). Ultimate Ascent levels were not added.");
                return false;
            }
            Entry entry = Entries[level - Lvl.First];
            AscentData.AscentInstanceData ascent = new AscentData.AscentInstanceData
            {
                title = TitleKey(level),
                // Ascents 9-25 have no reward.
                titleReward = string.Empty,
                description = entry.Description,
                color = entry.Color,
                sashSprite = ascent8.sashSprite
            };
            list.Add(ascent);
            added.Add(ascent);
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

    /// <summary>Names of items this mod adds or renames (key: the game's "NAME_" key).</summary>
    internal static readonly Dictionary<string, string> ItemNames = new Dictionary<string, string>
    {
        [Features.A14_Items.LightweightParasolKey] = "Lightweight Parasol",
        [Features.ClimbingChalkItem.NameKey] = "Climbing Chalk",
        [Features.WildFoods.WonderberryNameKey] = "Wonderberry",
        [Features.ToxicDrink.NameKey] = "Toxic Drink",
        [Features.CherryLollipop.NameKey] = "Cherry Lollipop",
        [Features.VanillaWafers.NameKey] = "Vanilla Wafers",
        [Features.SixSlotBackpack.NameKey] = Features.SixSlotBackpack.ItemName,
        [Features.Crabs.NameKey] = "Crab",
        [Features.Crabs.LegNameKey] = "Crab Leg",
    };

    /// <summary>The Russian for the names above and the prompts below (the game's style: berries named like its own "-ика" berries; prompts in the infinitive); every other language shows the English.</summary>
    internal static readonly Dictionary<string, string> Russian = new Dictionary<string, string>
    {
        [Features.A14_Items.LightweightParasolKey] = "Легкий зонтик",
        [Features.ClimbingChalkItem.NameKey] = "Магнезия",
        [Features.WildFoods.WonderberryNameKey] = "Чудника",
        [Features.ToxicDrink.NameKey] = "Токсичный напиток",
        [Features.CherryLollipop.NameKey] = "Вишневый леденец",
        [Features.VanillaWafers.NameKey] = "Ванильные вафли",
        [Features.SixSlotBackpack.NameKey] = "Рюкзак на шесть мест",
        [Features.Crabs.NameKey] = "Краб",
        [Features.Crabs.LegNameKey] = "Крабовая ножка",
        [Features.StickItem.SwingPrompt] = "ударить",
    };

    /// <summary>What the use button does, for items whose use the game has no word for (shown as written).</summary>
    internal static readonly Dictionary<string, string> Prompts = new Dictionary<string, string>
    {
        [Features.StickItem.SwingPrompt] = "swing",
    };

    private static void AddTexts()
    {
        Dictionary<string, List<string>> table = LocalizedText.mainTable;
        if (textsAdded && table.ContainsKey(TitleKey(Lvl.First).ToUpperInvariant()))
        {
            return;
        }
        for (int level = Lvl.First; level <= Lvl.Last; level++)
        {
            Entry entry = Entries[level - Lvl.First];
            Put(table, TitleKey(level), DisplayTitle(level).ToUpperInvariant());
            Put(table, LocalizedText.GetDescriptionIndex(TitleKey(level)), entry.Description.ToUpperInvariant());
        }
        foreach (KeyValuePair<string, string> name in ItemNames)
        {
            Put(table, name.Key, name.Value.ToUpperInvariant(), Russian.TryGetValue(name.Key, out string russian) ? russian.ToUpperInvariant() : null);
        }
        foreach (KeyValuePair<string, string> prompt in Prompts)
        {
            if (!table.ContainsKey(prompt.Key.ToUpperInvariant()))
            {
                Put(table, prompt.Key, prompt.Value, Russian.TryGetValue(prompt.Key, out string russian) ? russian : null);
            }
        }
        // The mod's threats on the Custom Run screen, in capitals if the game writes its own "Scorpions" so.
        bool capitals = false;
        foreach (List<string> texts in table.Values)
        {
            if (texts.Count > 0 && texts[0] == "SCORPIONS")
            {
                capitals = true;
                break;
            }
        }
        foreach ((_, string key, string english, string russian, _) in Features.CustomRunThreats.Threats)
        {
            Put(table, key, capitals ? english.ToUpperInvariant() : english, capitals ? russian.ToUpperInvariant() : russian);
        }
        // The Custom Run screen's Ascent setting, its name written like the threats'.
        Put(table, Features.CustomRunAscent.LabelKey, capitals ? "ASCENT" : "Ascent", capitals ? "ВОСХОЖДЕНИЕ" : "Восхождение");
        textsAdded = true;
    }

    private static void Put(Dictionary<string, List<string>> table, string key, string text, string russian = null)
    {
        int languages = Enum.GetValues(typeof(LocalizedText.Language)).Length;
        List<string> values = new List<string>(languages);
        for (int i = 0; i < languages; i++)
        {
            values.Add(i == (int)LocalizedText.Language.Russian && russian != null ? russian : text);
        }
        table[key.ToUpperInvariant()] = values;
    }
}
