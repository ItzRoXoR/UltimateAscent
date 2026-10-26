using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateAscent.Features;

/// <summary>
/// The mod's new creatures: the beach's crabs (<see cref="Crabs"/>), the Tropics' snakes (<see cref="Snakes"/>) and the
/// Alps' mountain goats (<see cref="Goats"/>). With the config's "New Mobs" on (the host's) they live in regular runs on
/// every ascent; custom runs have a switch for each among the Custom Run screen's threats, in its biome's group. Below
/// ascent 17 (with its rule off, and in custom runs, where the ascents' rules are off) all they do to a scout is half
/// as strong: half the injury, poison and knockback, knocked down or paralysed half as long; and snakes chase a third
/// slower.
/// </summary>
internal static class NewMobs
{
    /// <summary>
    /// Their switches among the Custom Run screen's threats: run settings of the game's own kind (saved, synced and reset
    /// as its own, on by default), under numbers its setting type does not use; each after the last threat of its biome's
    /// group there (the beach's urchins, the Tropics' rain, the Alps' blizzards).
    /// </summary>
    internal static readonly (RunSettings.SETTINGTYPE setting, string key, string english, string russian, RunSettings.SETTINGTYPE after)[] Threats =
    {
        ((RunSettings.SETTINGTYPE)91000, "UA_THREAT_CRABS", "Crabs", "Крабы", RunSettings.SETTINGTYPE.Hazard_Urchins),
        ((RunSettings.SETTINGTYPE)91010, "UA_THREAT_SNAKES", "Snakes", "Змеи", RunSettings.SETTINGTYPE.Hazard_Rain),
        ((RunSettings.SETTINGTYPE)91020, "UA_THREAT_GOATS", "Mountain Goats", "Горные козлы", RunSettings.SETTINGTYPE.Hazard_Storm),
    };

    internal static RunSettings.SETTINGTYPE CrabsThreat => Threats[0].setting;
    internal static RunSettings.SETTINGTYPE SnakesThreat => Threats[1].setting;
    internal static RunSettings.SETTINGTYPE GoatsThreat => Threats[2].setting;

    /// <summary>The config's switch lets them live in this climb: a regular run on any ascent, unless the host switched them off.</summary>
    internal static bool On => NewItems.RegularRun && HostOptions.NewMobsOn;

    /// <summary>This one lives in this climb: in a regular run with the config's switch on; in a custom run with its threat on.</summary>
    internal static bool Allowed(RunSettings.SETTINGTYPE threat)
    {
        if (!Plugin.Ready || !Rules.InRun)
        {
            return false;
        }
        return RunSettings.IsCustomRun ? RunSettings.GetValue(threat) == 1 : HostOptions.NewMobsOn;
    }

    /// <summary>In full from ascent 17 (hazards are more dangerous), weaker below it.</summary>
    private static bool Full => Rules.Enabled(Lvl.Hazards);

    /// <summary>How hard they hit: in full from ascent 17, half below it.</summary>
    internal static float Strength => Full ? 1f : 0.5f;

    /// <summary>How fast snakes chase: in full from ascent 17, two thirds below it.</summary>
    internal static float SnakeChase => Full ? 1f : 2f / 3f;

    /// <summary>Their run settings, set up with the game's own (before it orders, loads and syncs them).</summary>
    [HarmonyPatch(typeof(RunSettings), nameof(RunSettings.InitializeDefaultValues))]
    private static class ThreatSettings
    {
        private static void Postfix()
        {
            foreach ((RunSettings.SETTINGTYPE setting, _, _, _, _) in Threats)
            {
                RunSettings.AddDefaultSetting(setting, 1, 1);
            }
        }
    }

    /// <summary>
    /// Their switches on the Custom Run screen: the switch of the last threat in their biome's group copied, right after
    /// it, with their own setting and name; the threats' columns then a row longer, so every switch still fits.
    /// </summary>
    [HarmonyPatch(typeof(CustomOptionsWindow), nameof(CustomOptionsWindow.Initialize))]
    private static class ThreatToggles
    {
        private static void Postfix(CustomOptionsWindow __instance)
        {
            CustomOptionToggle[] toggles = __instance.GetComponentsInChildren<CustomOptionToggle>(true);
            CustomOptionToggle scorpions = toggles.FirstOrDefault(t => t.settingType == RunSettings.SETTINGTYPE.Hazard_Scorpions);
            if (scorpions == null)
            {
                Plugin.Log.LogWarning("New mobs: the Custom Run screen has no threats to copy; their switches are not on it (custom runs keep them on).");
                return;
            }
            int added = 0;
            foreach ((RunSettings.SETTINGTYPE setting, string key, _, _, RunSettings.SETTINGTYPE after) in Threats)
            {
                if (toggles.Any(t => t.settingType == setting))
                {
                    continue;
                }
                CustomOptionToggle neighbour = toggles.FirstOrDefault(t => t.settingType == after && t.transform.parent == scorpions.transform.parent) ?? scorpions;
                GameObject made = Object.Instantiate(neighbour.gameObject, neighbour.transform.parent);
                made.name = key;
                made.transform.SetSiblingIndex(neighbour.transform.GetSiblingIndex() + 1);
                made.GetComponent<CustomOptionToggle>().settingType = setting;
                foreach (LocalizedText label in made.GetComponentsInChildren<LocalizedText>(true))
                {
                    label.SetIndex(key);
                }
                added++;
            }
            if (added > 0)
            {
                FitColumns(scorpions.transform.parent, added);
            }
        }
    }

    /// <summary>
    /// The threats' grid (switches and the dotted lines between the biomes, filling column after column) as many columns
    /// wide as before ours: its rows lowered so that a column holds what it takes.
    /// </summary>
    private static void FitColumns(Transform list, int added)
    {
        GridLayoutGroup grid = list.GetComponent<GridLayoutGroup>();
        if (grid == null || grid.startAxis != GridLayoutGroup.Axis.Vertical)
        {
            Plugin.Log.LogWarning("New mobs: the Custom Run screen's threats are not in columns as expected ("
                + string.Join(", ", list.GetComponents<Component>().Select(c => c.GetType().Name)) + "); the last of them may not fit.");
            return;
        }
        int shown = 0;
        foreach (Transform child in list)
        {
            if (child.gameObject.activeSelf && !(child.TryGetComponent(out LayoutElement element) && element.ignoreLayout))
            {
                shown++;
            }
        }
        int rows = grid.constraint == GridLayoutGroup.Constraint.FixedRowCount
            ? grid.constraintCount
            : Mathf.Max(1, Mathf.FloorToInt((((RectTransform)list).rect.height - grid.padding.vertical + grid.spacing.y + 0.01f) / (grid.cellSize.y + grid.spacing.y)));
        int columns = Mathf.Max(1, Mathf.CeilToInt((shown - added) / (float)rows));
        int needed = Mathf.CeilToInt(shown / (float)columns);
        if (needed <= rows)
        {
            return;
        }
        // A hair under the exact share, so the last row is not lost to rounding.
        float scale = rows / (float)needed * 0.998f;
        grid.cellSize = new Vector2(grid.cellSize.x, grid.cellSize.y * scale);
        grid.spacing = new Vector2(grid.spacing.x, grid.spacing.y * scale);
        if (grid.constraint == GridLayoutGroup.Constraint.FixedRowCount)
        {
            grid.constraintCount = needed;
        }
    }
}
