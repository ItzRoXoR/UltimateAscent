using System.Collections.Generic;
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
/// slower. Seven in ten of the crabs, the ground snakes and the goats live on the climb itself, the rest on the way to
/// it (see <see cref="Place"/>).
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

    /// <summary>
    /// Of the crabs, the ground snakes and the goats this share lives on the climb itself (the segment's wall, up to its
    /// campfire), the rest on the way to it (the segment's ground); the tree snakes live in their trees.
    /// </summary>
    internal const float ClimbShare = 0.7f;

    /// <summary>How many of so many live on the climb itself.</summary>
    internal static int OnClimb(int count) => Mathf.RoundToInt(count * ClimbShare);

    /// <summary>
    /// Whether a spot is on the climb itself: the map's terrain under it is a wall (the game's "Wall..." meshes: the
    /// segment's wall, its top at the campfire, the Alps' two walls, the props laid on them), not the ground the way to
    /// it runs over ("Ground"). Anything else counts as what lies under it; false where neither is found below.
    /// </summary>
    internal static bool OnTheClimb(Vector3 spot)
    {
        RaycastHit[] hits = Physics.RaycastAll(spot + Vector3.up * 0.5f, Vector3.down, 80f, HelperFunctions.terrainMapMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            int kind = TerrainKind(hit.collider);
            if (kind >= 0)
            {
                return kind == 1;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether a spot is under the map: on ground buried under the map's surface, inside the mountain, or below a gap
    /// between the map's pieces (a ray from above can fall through one). Looked at from the spot up into the sky,
    /// straight up and slanting up all round, the map's surface seen from below: its upper side, facing away. Ground
    /// under an overhang or in a cave is not under the map (its roof faces down at it, or the sky shows).
    /// </summary>
    internal static bool UnderTheMap(Vector3 spot)
    {
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            int under = 0;
            Vector3 from = spot + Vector3.up * 0.3f;
            foreach (Vector3 look in SkyLooks)
            {
                if (Physics.Raycast(from, look, out RaycastHit hit, 400f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)
                    && Vector3.Dot(hit.normal, look) > 0f)
                {
                    under++;
                }
            }
            return under >= UnderLooks;
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
    }

    /// <summary>The looks up from a spot: straight up, and all round 30 and 60 degrees from it; this many seeing the map from below is under it.</summary>
    private static readonly Vector3[] SkyLooks = MakeSkyLooks();
    private const int UnderLooks = 5;

    private static Vector3[] MakeSkyLooks()
    {
        List<Vector3> looks = new List<Vector3> { Vector3.up };
        foreach (float tilt in new[] { 30f, 60f })
        {
            for (int turn = 0; turn < 6; turn++)
            {
                looks.Add(Quaternion.Euler(0f, turn * 60f + tilt, 0f) * Quaternion.Euler(tilt, 0f, 0f) * Vector3.up);
            }
        }
        return looks.ToArray();
    }

    /// <summary>For the tests and the log: the new mobs found under the map (fallen through a gap) and put back at their spot.</summary>
    internal static int PutBack;

    /// <summary>
    /// How high a spot is above the map's own terrain right under it (its ground or its wall, see <see cref="OnTheClimb"/>):
    /// about 0 on the ground, a rock's height on a rock, far up on a tree's crown; infinity where none is found below.
    /// </summary>
    internal static float AboveTerrain(Vector3 spot)
    {
        RaycastHit[] hits = Physics.RaycastAll(spot + Vector3.up * 0.5f, Vector3.down, 200f, HelperFunctions.terrainMapMask);
        float above = float.PositiveInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (TerrainKind(hit.collider) >= 0)
            {
                above = Mathf.Min(above, Mathf.Max(0f, spot.y - hit.point.y));
            }
        }
        return above;
    }

    private static readonly Dictionary<int, int> terrainKinds = new Dictionary<int, int>();

    /// <summary>The terrain a collider is part of: 1 a wall, 0 the ground, -1 neither (a prop, a tree, a test block). Remembered per collider.</summary>
    private static int TerrainKind(Collider collider)
    {
        int id = collider.GetInstanceID();
        if (terrainKinds.TryGetValue(id, out int kind))
        {
            return kind;
        }
        kind = -1;
        for (Transform part = collider.transform; part != null && part.GetComponent<Biome>() == null; part = part.parent)
        {
            if (part.name.StartsWith("Wall", System.StringComparison.Ordinal))
            {
                kind = 1;
                break;
            }
            if (part.name == "Ground" || part.name == "GroundMesh")
            {
                kind = 0;
                break;
            }
        }
        if (terrainKinds.Count > 8192)
        {
            terrainKinds.Clear();
        }
        terrainKinds[id] = kind;
        return kind;
    }

    /// <summary>
    /// So many spots out of those found along a part of the map's way (each with how far along it lies): the climb's
    /// share of them on the climb itself, the rest on the way to it, each part spread along its own stretch, all this far
    /// apart; where one part has too little room, the other takes the rest.
    /// </summary>
    internal static List<Vector3> Place(List<(Vector3 spot, float share)> found, int count, float apart, out int onClimb)
    {
        List<(Vector3 spot, float share)> climb = new List<(Vector3, float)>();
        List<(Vector3 spot, float share)> way = new List<(Vector3, float)>();
        foreach ((Vector3 spot, float share) f in found)
        {
            (OnTheClimb(f.spot) ? climb : way).Add(f);
        }
        List<Vector3> taken = new List<Vector3>();
        List<Vector3> high = Spread(climb, OnClimb(count), apart, taken);
        List<Vector3> low = Spread(way, count - high.Count, apart, taken);
        high.AddRange(Spread(climb, count - high.Count - low.Count, apart, taken));
        onClimb = high.Count;
        high.AddRange(low);
        return high;
    }

    /// <summary>
    /// Spots out of those found along a stretch of the way (each with how far along it lies): this many, one at each such
    /// share of the stretch they cover (the first near its start, the last near its end), each at least this far from
    /// those already taken (and added to them), never under the map (see <see cref="UnderTheMap"/>; such a spot is
    /// dropped and another taken). Fewer where there is no more room.
    /// </summary>
    internal static List<Vector3> Spread(List<(Vector3 spot, float share)> found, int count, float apart, List<Vector3> taken)
    {
        List<Vector3> picked = new List<Vector3>();
        if (found.Count == 0 || count <= 0)
        {
            return picked;
        }
        found = new List<(Vector3 spot, float share)>(found);
        float from = found.Min(f => f.share);
        float length = Mathf.Max(found.Max(f => f.share) - from, 1e-3f);
        for (int c = 0; c < count; c++)
        {
            float wanted = from + (c + 0.5f) / count * length;
            float band = 0.5f / count * length;
            while (true)
            {
                List<(Vector3 spot, float share)> open = found.Where(f => taken.All(t => Goats.Flat(t, f.spot) >= apart)).ToList();
                List<(Vector3 spot, float share)> inBand = open.Where(f => Mathf.Abs(f.share - wanted) <= band).ToList();
                List<(Vector3 spot, float share)> pick = inBand.Count > 0 ? inBand : open.OrderBy(f => Mathf.Abs(f.share - wanted)).Take(5).ToList();
                if (pick.Count == 0)
                {
                    return picked;
                }
                (Vector3 spot, float share) chosen = pick[Random.Range(0, pick.Count)];
                if (UnderTheMap(chosen.spot))
                {
                    UnderMapSpots++;
                    found.Remove(chosen);
                    continue;
                }
                picked.Add(chosen.spot);
                taken.Add(chosen.spot);
                break;
            }
        }
        return picked;
    }

    /// <summary>For the tests and the log: spots found for the new mobs that lay under the map, and were passed over.</summary>
    internal static int UnderMapSpots;

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
