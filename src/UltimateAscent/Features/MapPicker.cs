using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UltimateAscent.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// The boarding pass picks the map: any map of the game's current rotation (the maps it goes through one a day),
/// at the bottom right of the ascent card: the map's name in a rounded blue outline, "<" and ">" beside it (with the
/// game's button hover and click sounds), and a die after them that picks a map at random. The arrows go by number (from today's 9, left is 8 and right is 10), round
/// at the ends; today's map keeps its own name among them. An arrow held down runs through the maps, faster and faster. It opens on
/// today's map, "TODAY'S MAP (12)" (the game's lettering has no number sign); the others are "MAP 5". The pick is kept
/// in the mod's save with the day it was made, so it stays for the day's later climbs and goes back to today's map
/// when the day changes. The host's pick is the map (the host's game loads it); guests do not see the line. True
/// Final Ascent's FINAL ASCENT keeps its own map. With the config's "Map Biomes" on, every map but today's shows its
/// biomes after its number, by the first letters of their names: "MAP 90 (STMC)" for Shore, Tropics, Mesa, Caldera.
/// With the Builder maps pack and True Final Ascent installed, its maps come after the original ones, "Builder: MAP 1"
/// on (not in a custom run; see <see cref="BuilderMaps"/>): a picked one plays as TFA's FINAL ASCENT on that map, at the
/// ascent on the pass.
/// </summary>
internal static class MapPicker
{
    private const string DayKey = "map.day";
    private const string PickKey = "map.pick";

    private const float LabelSize = 22f;
    private const float ChevronSize = 66f;

    /// <summary>The font's chevrons sit low; lifted to the middle of the outline.</summary>
    private const float ChevronLift = 12f;
    private const float PillHeight = 36f;
    private const float Outline = 5f;

    private static GameObject line;
    private static TMP_Text label;
    private static LayoutElement labelWidth;
    private static bool? widthWithBiomes;

    private static bool Russian => LocalizedText.CURRENT_LANGUAGE == LocalizedText.Language.Russian;

    /// <summary>The maps the game goes through, one a day (its scenes, in their order).</summary>
    internal static int Count
    {
        get
        {
            MapBaker baker = SingletonAsset<MapBaker>.Instance;
            return baker != null && baker.ScenePaths != null ? baker.ScenePaths.Length : 0;
        }
    }

    /// <summary>The day's number (the game's level index: one more each day).</summary>
    internal static int Day
    {
        get
        {
            NextLevelService service = GameHandler.GetService<NextLevelService>();
            return service != null ? service.NextLevelIndexOrFallback : 0;
        }
    }

    /// <summary>Today's map, as a place in the rotation.</summary>
    internal static int Today => Count > 0 ? ((Day % Count) + Count) % Count : 0;

    /// <summary>The maps to pick from: the rotation's (0 to <see cref="Count"/> - 1), then the Builder maps offered.</summary>
    internal static int Total => Count + BuilderMaps.Count;

    /// <summary>The Builder map at this place among the maps to pick from, if it is one.</summary>
    internal static BuilderMaps.Map Builder(int map) => map >= Count && map < Total ? BuilderMaps.All[map - Count] : null;

    /// <summary>
    /// The map picked (a place among the maps to pick from): today's unless another was picked today. A Builder map is
    /// kept by its number ("b12"), so the pick holds should the pack change.
    /// </summary>
    internal static int Picked
    {
        get
        {
            string saved = ModSave.Get(PickKey) ?? "";
            if (Count == 0 || !ChallengeModes.Offered || ModSave.Get(DayKey) != Day.ToString())
            {
                return Today;
            }
            if (saved.StartsWith("b", System.StringComparison.Ordinal) && int.TryParse(saved.Substring(1), out int number))
            {
                int at = BuilderMaps.Offered ? BuilderMaps.All.FindIndex(m => m.Number == number) : -1;
                return at >= 0 ? Count + at : Today;
            }
            if (!int.TryParse(saved, out int pick) || pick < 0 || pick >= Count)
            {
                return Today;
            }
            return pick;
        }
        set
        {
            BuilderMaps.Map builder = Builder(value);
            if (value == Today || value < 0 || value >= Total)
            {
                ModSave.Set(PickKey, "");
                ModSave.Set(DayKey, "");
            }
            else
            {
                ModSave.Set(DayKey, Day.ToString());
                ModSave.Set(PickKey, builder != null ? "b" + builder.Number : value.ToString());
            }
        }
    }

    /// <summary>The map's number: the one in its scene's name (Level_12: 12), or its place in the rotation.</summary>
    internal static int Number(int map)
    {
        BuilderMaps.Map builder = Builder(map);
        if (builder != null)
        {
            return builder.Number;
        }
        MapBaker baker = SingletonAsset<MapBaker>.Instance;
        string scene = baker != null ? baker.GetLevel(map) : "";
        Match number = Regex.Match(scene ?? "", @"(\d+)$");
        return number.Success ? int.Parse(number.Value) : map;
    }

    /// <summary>
    /// The maps in the order the arrows go: by number (today's among them, under its own name), then the Builder maps by
    /// theirs, round at the ends.
    /// </summary>
    internal static List<int> Order() => Enumerable.Range(0, Count).OrderBy(Number).Concat(Enumerable.Range(Count, BuilderMaps.Count)).ToList();

    internal static string Name(int map)
    {
        BuilderMaps.Map builder = Builder(map);
        if (builder != null)
        {
            string letters = Plugin.MapBiomes.Value ? BiomesOf(builder.Code) : "";
            return letters.Length > 0 ? BuilderMaps.Name(builder) + " (" + letters + ")" : BuilderMaps.Name(builder);
        }
        if (map == Today)
        {
            return (Russian ? "КАРТА ДНЯ (" : "TODAY'S MAP (") + Number(map) + ")";
        }
        string name = (Russian ? "КАРТА " : "MAP ") + Number(map);
        string biomes = Plugin.MapBiomes.Value ? Biomes(map) : "";
        return biomes.Length > 0 ? name + " (" + biomes + ")" : name;
    }

    /// <summary>
    /// The game's letter for each part of the climb in its map list (its biome types' initials: the Caldera is its
    /// volcano, the Gloom its swamp), and the name it shows for that part.
    /// </summary>
    private static readonly Dictionary<char, string>[] BiomeNames =
    {
        new Dictionary<char, string> { ['S'] = "SHORE" },
        new Dictionary<char, string> { ['T'] = "TROPICS", ['R'] = "ROOTS" },
        new Dictionary<char, string> { ['A'] = "ALPINE", ['M'] = "MESA" },
        new Dictionary<char, string> { ['V'] = "CALDERA", ['S'] = "GLOOM" },
    };

    /// <summary>
    /// The map's four biomes by the first letters of the names the game shows for them, in its language (Shore,
    /// Tropics, Mesa, Caldera: STMC), from the game's map list; "" when the list has nothing for the map.
    /// </summary>
    internal static string Biomes(int map)
    {
        MapBaker baker = SingletonAsset<MapBaker>.Instance;
        return BiomesOf(baker != null && baker.BiomeIDs != null && map >= 0 && map < baker.BiomeIDs.Count && map < Count ? baker.BiomeIDs[map] : null);
    }

    /// <summary>The letters for a map list's biome code ("STAV"), as <see cref="Biomes"/> shows them; "" for none.</summary>
    internal static string BiomesOf(string code)
    {
        if (code == null || code.Length != BiomeNames.Length)
        {
            return "";
        }
        char[] letters = new char[code.Length];
        for (int part = 0; part < code.Length; part++)
        {
            if (!BiomeNames[part].TryGetValue(code[part], out string key))
            {
                return "";
            }
            string shown = LocalizedText.GetText(key)?.Trim();
            letters[part] = char.ToUpperInvariant(string.IsNullOrEmpty(shown) ? key[0] : shown[0]);
        }
        return new string(letters);
    }

    internal static void Step(int by)
    {
        List<int> order = Order();
        if (order.Count == 0)
        {
            return;
        }
        int at = Mathf.Max(0, order.IndexOf(Picked));
        Picked = order[((at + by) % order.Count + order.Count) % order.Count];
        Refresh();
    }

    /// <summary>The host loads the picked map instead of today's (TFA's FINAL ASCENT and the game's own day stay as they are).</summary>
    [HarmonyPatch(typeof(AirportCheckInKiosk), "LoadIslandMaster")]
    private static class LoadPicked
    {
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(AirportCheckInKiosk __instance, int ascent, byte[] serializedRunSettings, bool __runOriginal)
        {
            if (!__runOriginal || ascent >= BoardingPassUi.FinalAscentIndex || !PhotonNetwork.IsMasterClient || Count == 0)
            {
                return __runOriginal;
            }
            int map = Picked;
            if (map == Today)
            {
                return true;
            }
            BuilderMaps.Map builder = Builder(map);
            if (builder != null)
            {
                Plugin.Log.LogInfo($"Map picker: loading {Name(map)} ({System.IO.Path.GetFileName(builder.Path)}) as TFA's FINAL ASCENT on ascent {ascent}.");
                Compat.TfaMaps.Start(__instance, builder.Path, ascent, serializedRunSettings);
                return false;
            }
            string scene = SingletonAsset<MapBaker>.Instance.GetLevel(map);
            if (string.IsNullOrEmpty(scene))
            {
                return true;
            }
            Plugin.Log.LogInfo($"Map picker: loading {Name(map)} ({scene}) instead of today's.");
            __instance.photonView.RPC("BeginIslandLoadRPC", RpcTarget.All, scene, ascent, serializedRunSettings);
            return false;
        }
    }

    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.UpdateAscent))]
    private static class Shown
    {
        private static void Postfix(BoardingPass __instance)
        {
            if (line == null)
            {
                line = Make(__instance);
            }
            Refresh();
        }
    }

    /// <summary>Only the host picks (the host's game loads the map); a map at all only with the game's rotation there.</summary>
    private static bool Picks => Total > 1 && ChallengeModes.Offered && (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient);

    private static void Refresh()
    {
        if (line == null)
        {
            return;
        }
        line.SetActive(Picks);
        if (label != null)
        {
            if (labelWidth != null && widthWithBiomes != Plugin.MapBiomes.Value)
            {
                widthWithBiomes = Plugin.MapBiomes.Value;
                labelWidth.preferredWidth = WidestName(label);
            }
            label.text = Name(Picked);
        }
        // At the card's bottom right; in a custom run the Custom Options button is there, so at its bottom left.
        bool custom = RunSettings.IsCustomRun;
        RectTransform rect = (RectTransform)line.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(custom ? 0f : 1f, 0f);
        rect.anchoredPosition = new Vector2(custom ? 12f : -12f, 8f);
    }

    /// <summary>As wide as the longest name, so the outline does not jump from map to map.</summary>
    private static float WidestName(TMP_Text name) => Enumerable.Range(0, Mathf.Max(Total, 1)).Select(m => name.GetPreferredValues(Name(m)).x).DefaultIfEmpty(120f).Max() + 4f;

    /// <summary>
    /// The picker at the card's bottom right (bottom left in a custom run): "&lt;", the map's name in a rounded outline in
    /// the pass's blue (always as wide as the longest name), "&gt;"; the chevrons in the pass's lettering and blue, each
    /// taking the click.
    /// </summary>
    private static GameObject Make(BoardingPass pass)
    {
        RectTransform panel = pass.ascentDesc != null ? pass.ascentDesc.transform.parent as RectTransform : null;
        if (panel == null || pass.ascentTitle == null)
        {
            Plugin.Log.LogWarning("Map picker: the boarding pass has no place for it.");
            return null;
        }
        Color blue = pass.ascentTitle.color;
        GameObject holder = new GameObject("UltimateAscentMapPicker", typeof(RectTransform));
        RectTransform rect = (RectTransform)holder.transform;
        rect.SetParent(panel, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-12f, 8f);
        HorizontalLayoutGroup row = holder.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleCenter;
        row.spacing = 0f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        ContentSizeFitter fit = holder.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Chevron(pass, holder.transform, "Previous", "<", blue, -1);

        GameObject frame = new GameObject("Outline", typeof(RectTransform));
        frame.transform.SetParent(holder.transform, false);
        Image ring = frame.AddComponent<Image>();
        ring.sprite = PillOutline();
        ring.type = Image.Type.Sliced;
        ring.color = blue;
        ring.raycastTarget = false;
        HorizontalLayoutGroup inside = frame.AddComponent<HorizontalLayoutGroup>();
        inside.childAlignment = TextAnchor.MiddleCenter;
        inside.padding = new RectOffset(16, 16, 0, 0);
        inside.childControlWidth = true;
        inside.childControlHeight = true;
        inside.childForceExpandWidth = false;
        inside.childForceExpandHeight = true;
        LayoutElement frameHeight = frame.AddComponent<LayoutElement>();
        frameHeight.preferredHeight = frameHeight.minHeight = PillHeight;

        GameObject words = new GameObject("Name", typeof(RectTransform));
        words.transform.SetParent(frame.transform, false);
        TextMeshProUGUI name = words.AddComponent<TextMeshProUGUI>();
        name.font = pass.ascentDesc.font;
        name.fontSharedMaterial = pass.ascentDesc.fontSharedMaterial;
        name.color = pass.ascentDesc.color;
        name.fontSize = LabelSize;
        name.enableWordWrapping = false;
        name.alignment = TextAlignmentOptions.Midline;
        name.raycastTarget = false;
        label = name;
        labelWidth = words.AddComponent<LayoutElement>();
        labelWidth.preferredWidth = WidestName(name);
        widthWithBiomes = Plugin.MapBiomes.Value;

        Chevron(pass, holder.transform, "Next", ">", blue, 1);
        Dice(pass, holder.transform, blue);
        return holder;
    }

    /// <summary>A map at random from the list, another than the one picked now.</summary>
    internal static void PickAtRandom()
    {
        List<int> others = Order().Where(m => m != Picked).ToList();
        if (others.Count == 0)
        {
            return;
        }
        Picked = others[Random.Range(0, others.Count)];
        Refresh();
    }

    private const float DiceSize = 30f;

    /// <summary>The die after the arrows: in the pass's blue like them, a click on it picks a map at random (with the game's button sounds).</summary>
    private static void Dice(BoardingPass pass, Transform parent, Color blue)
    {
        GameObject end = new GameObject("UltimateAscent Map Dice", typeof(RectTransform));
        end.transform.SetParent(parent, false);
        LayoutElement size = end.AddComponent<LayoutElement>();
        size.preferredWidth = size.minWidth = DiceSize + 12f;
        size.preferredHeight = size.minHeight = PillHeight + 8f;
        Image hit = end.AddComponent<Image>();
        hit.color = Color.clear;
        GameObject face = new GameObject("Die", typeof(RectTransform));
        face.transform.SetParent(end.transform, false);
        Image die = face.AddComponent<Image>();
        die.sprite = DieFace();
        die.raycastTarget = false;
        RectTransform faceRect = (RectTransform)face.transform;
        faceRect.anchorMin = faceRect.anchorMax = new Vector2(0.5f, 0.5f);
        faceRect.sizeDelta = new Vector2(DiceSize, DiceSize);
        faceRect.anchoredPosition = new Vector2(2f, 0f);
        Button button = end.AddComponent<Button>();
        button.targetGraphic = die;
        ColorBlock colours = button.colors;
        colours.normalColor = blue;
        colours.highlightedColor = Color.Lerp(blue, Color.black, 0.35f);
        colours.pressedColor = Color.Lerp(blue, Color.black, 0.55f);
        colours.selectedColor = blue;
        colours.colorMultiplier = 1f;
        button.colors = colours;
        Navigation none = button.navigation;
        none.mode = Navigation.Mode.None;
        button.navigation = none;
        button.onClick.AddListener(PickAtRandom);
        (SFX_Instance clickSound, SFX_Instance hoverSound) = ButtonSounds(pass);
        if (clickSound != null || hoverSound != null)
        {
            NicksButtonSFX sounds = end.AddComponent<NicksButtonSFX>();
            sounds.button = button;
            sounds.sfxClick = clickSound;
            sounds.sfxHover = hoverSound;
        }
    }

    private static Sprite dieFace;

    /// <summary>A die's face, five pips in a rounded square outline (white, tinted by the button), drawn once.</summary>
    private static Sprite DieFace()
    {
        if (dieFace != null)
        {
            return dieFace;
        }
        const int Size = 96;
        const float Corner = 22f;
        const float Stroke = 9f;
        const float Pip = 8.5f;
        float half = Size / 2f;
        Vector2 centre = new Vector2(half, half);
        Vector2[] pips =
        {
            new Vector2(0.29f, 0.29f), new Vector2(0.71f, 0.29f), new Vector2(0.5f, 0.5f), new Vector2(0.29f, 0.71f), new Vector2(0.71f, 0.71f),
        };
        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "UltimateAscent Die", wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                // The rounded square's edge: distance from it, below 0 inside.
                Vector2 q = new Vector2(Mathf.Abs(p.x - centre.x), Mathf.Abs(p.y - centre.y)) - Vector2.one * (half - 1f - Corner);
                float edge = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - Corner;
                float alpha = Mathf.Clamp01(0.5f - edge) * Mathf.Clamp01(edge + Stroke + 0.5f);
                foreach (Vector2 pip in pips)
                {
                    alpha = Mathf.Max(alpha, Mathf.Clamp01(Pip - Vector2.Distance(p, pip * Size) + 0.5f));
                }
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * alpha));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        dieFace = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        dieFace.name = "UltimateAscent Die";
        return dieFace;
    }

    /// <summary>
    /// One end of the picker: the chevron in the pass's lettering and blue, its end of the outline taking the click (one
    /// map that way); held down, it runs through the maps (see <see cref="Hold"/>).
    /// </summary>
    private static void Chevron(BoardingPass pass, Transform parent, string name, string glyph, Color blue, int by)
    {
        GameObject end = new GameObject("UltimateAscent Map " + name, typeof(RectTransform));
        end.transform.SetParent(parent, false);
        LayoutElement size = end.AddComponent<LayoutElement>();
        size.preferredWidth = size.minWidth = 32f;
        size.preferredHeight = size.minHeight = PillHeight + 8f;
        Image hit = end.AddComponent<Image>();
        hit.color = Color.clear;
        GameObject words = new GameObject("Chevron", typeof(RectTransform));
        words.transform.SetParent(end.transform, false);
        TextMeshProUGUI chevron = words.AddComponent<TextMeshProUGUI>();
        chevron.font = pass.ascentTitle.font;
        chevron.fontSharedMaterial = pass.ascentTitle.fontSharedMaterial;
        chevron.color = Color.white;
        chevron.fontSize = ChevronSize;
        chevron.text = glyph;
        chevron.alignment = TextAlignmentOptions.Center;
        chevron.enableWordWrapping = false;
        chevron.raycastTarget = false;
        RectTransform words_ = (RectTransform)words.transform;
        words_.anchorMin = Vector2.zero;
        words_.anchorMax = Vector2.one;
        words_.offsetMin = new Vector2(0f, ChevronLift);
        words_.offsetMax = new Vector2(0f, ChevronLift);
        Button button = end.AddComponent<Button>();
        button.targetGraphic = chevron;
        ColorBlock colours = button.colors;
        colours.normalColor = blue;
        colours.highlightedColor = Color.Lerp(blue, Color.black, 0.35f);
        colours.pressedColor = Color.Lerp(blue, Color.black, 0.55f);
        colours.selectedColor = blue;
        colours.colorMultiplier = 1f;
        button.colors = colours;
        Navigation none = button.navigation;
        none.mode = Navigation.Mode.None;
        button.navigation = none;
        // The game's button sounds: a button's on the pass if it has one, otherwise the Custom Run screen's (its
        // options' hover and click, as the challenge boxes use).
        (SFX_Instance clickSound, SFX_Instance hoverSound) = ButtonSounds(pass);
        Hold hold = end.AddComponent<Hold>();
        hold.by = by;
        hold.tick = clickSound;
        button.onClick.AddListener(() =>
        {
            // The release that ends a run through the maps is not one more step.
            if (!hold.ran)
            {
                Step(by);
            }
            hold.ran = false;
        });
        if (clickSound != null || hoverSound != null)
        {
            NicksButtonSFX sounds = end.AddComponent<NicksButtonSFX>();
            sounds.button = button;
            sounds.sfxClick = clickSound;
            sounds.sfxHover = hoverSound;
        }
        else
        {
            Plugin.Log.LogWarning("Map picker: no button sounds found; its arrows are silent.");
        }
    }

    /// <summary>
    /// An arrow held down runs through the maps: after <see cref="Delay"/> it takes a step, faster and faster (from
    /// <see cref="SlowStep"/> to <see cref="FastStep"/> apart over <see cref="SpeedUp"/>), each with the click sound
    /// (at most every <see cref="TickGap"/>), until it is let go or the pointer leaves it.
    /// </summary>
    internal sealed class Hold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        internal const float Delay = 0.4f;
        internal const float SlowStep = 0.12f;
        internal const float FastStep = 0.025f;
        internal const float SpeedUp = 1.5f;
        private const float TickGap = 0.08f;

        internal int by;
        internal SFX_Instance tick;

        /// <summary>The arrow ran through maps since it was pressed.</summary>
        internal bool ran;

        private bool held;
        private float runFrom;
        private float nextStep;
        private float lastTick;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            held = true;
            ran = false;
            runFrom = Time.unscaledTime + Delay;
            nextStep = runFrom;
        }

        public void OnPointerUp(PointerEventData eventData) => held = false;

        public void OnPointerExit(PointerEventData eventData) => held = false;

        private void OnDisable() => held = false;

        private void Update()
        {
            float now = Time.unscaledTime;
            if (!held || now < nextStep)
            {
                return;
            }
            ran = true;
            Step(by);
            if (tick != null && SFX_Player.instance != null && now - lastTick >= TickGap)
            {
                lastTick = now;
                SFX_Player.instance.PlaySFX(tick, transform.position);
            }
            nextStep = now + Mathf.Lerp(SlowStep, FastStep, (now - runFrom) / SpeedUp);
        }
    }

    /// <summary>The hover and click sounds of the game's buttons near the pass.</summary>
    internal static (SFX_Instance click, SFX_Instance hover) ButtonSounds(BoardingPass pass)
    {
        NicksButtonSFX onPass = pass.GetComponentsInChildren<NicksButtonSFX>(true).FirstOrDefault(n => n.sfxClick != null);
        if (onPass != null)
        {
            return (onPass.sfxClick, onPass.sfxHover);
        }
        CustomOptionToggle option = pass.customOptionsWindow != null ? pass.customOptionsWindow.GetComponentsInChildren<CustomOptionToggle>(true).FirstOrDefault(t => t.sfxClick != null) : null;
        return option != null ? (option.sfxClick, option.sfxHover) : (null, null);
    }

    private static Sprite pill;

    /// <summary>A rounded outline (ends a half circle), 9-sliced so it stretches to any width, drawn once.</summary>
    private static Sprite PillOutline()
    {
        if (pill != null)
        {
            return pill;
        }
        const int Size = 64;
        float radius = Size / 2f;
        float thickness = Outline * 2f; // drawn at twice the size it shows
        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "UltimateAscent Pill", wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                float outer = Mathf.Clamp01(radius - d);
                float inner = Mathf.Clamp01(d - (radius - thickness));
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * outer * inner));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        float border = radius - 1f;
        pill = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 200f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        pill.name = "UltimateAscent Pill";
        return pill;
    }

    internal static TMP_Text Label => label;

    internal static GameObject Line => line;
}
