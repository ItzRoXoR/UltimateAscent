using System.Linq;
using HarmonyLib;
using TMPro;
using UltimateAscent.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateAscent.Features;

/// <summary>
/// The Blind Mode and Hot Mode boxes, in the boarding pass's blue header to the right of its title, one above the
/// other, in the header's own style: a white box in the shape of the pass's buttons with the pass's blue tick, the
/// name beside it in the title's white lettering, and the Custom Run screen's click and hover sounds. Every player
/// can open the boarding pass and tick their own. Each row takes the click over its box and name only, clear of True
/// Final Ascent's light/dark switch at the header's right end. On ascent 23 and above the Hot Mode box shows ticked and cannot
/// be changed (its name dims): that ascent's own sun already does it.
/// </summary>
internal static class ChallengeModesUi
{
    /// <summary>Where the boxes sit on the pass (fractions of its card): its header, between the title and the dashed line.</summary>
    private static readonly Vector2 HeaderMin = new Vector2(0.40f, 0.80f);
    private static readonly Vector2 HeaderMax = new Vector2(0.665f, 0.965f);

    private const float LabelSize = 25f;
    private const float BoxSize = 28f;

    private static GameObject boxes;
    private static Toggle blind;
    private static Toggle hot;
    private static TMP_Text hotLabel;
    private static Image hotBox;
    private static SFX_Instance clickSound;
    private static SFX_Instance hoverSound;

    internal static bool Shown => boxes != null && boxes.activeInHierarchy;

    internal static Toggle BlindBox => blind;

    internal static Toggle HotBox => hot;

    private static bool Russian => LocalizedText.CURRENT_LANGUAGE == LocalizedText.Language.Russian;

    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.UpdateAscent))]
    private static class Refresh
    {
        private static void Postfix(BoardingPass __instance)
        {
            if (boxes == null)
            {
                boxes = Make(__instance);
            }
            Show(__instance);
        }
    }

    private static void Show(BoardingPass pass)
    {
        if (boxes == null)
        {
            return;
        }
        boxes.SetActive(ChallengeModes.Offered);
        blind.SetIsOnWithoutNotify(ChallengeModes.Blind);
        bool byAscent = ChallengeModes.SunByAscent(pass.ascentIndex);
        hot.interactable = !byAscent;
        hot.SetIsOnWithoutNotify(byAscent || ChallengeModes.Hot);
        hotLabel.alpha = byAscent ? 0.55f : 1f;
        hotBox.color = new Color(1f, 1f, 1f, byAscent ? 0.7f : 1f);
    }

    private static GameObject Make(BoardingPass pass)
    {
        CustomOptionToggle template = pass.customOptionsWindow != null ? pass.customOptionsWindow.GetComponentsInChildren<CustomOptionToggle>(true).FirstOrDefault(t => t.toggle != null) : null;
        RectTransform passCard = Card(pass);
        Image buttonLook = pass.incrementAscentButton != null ? pass.incrementAscentButton.image : null;
        Image tickLook = template != null ? template.toggle.graphic as Image : null;
        TMP_Text title = HeaderTitle(pass, passCard);
        if (passCard == null || buttonLook == null || tickLook == null || title == null)
        {
            Plugin.Log.LogWarning($"Challenge modes: the boarding pass has no place for their boxes (card {passCard != null}, button {buttonLook != null}, tick {tickLook != null}, title {title != null}).");
            return null;
        }
        clickSound = template.sfxClick;
        hoverSound = template.sfxHover;
        GameObject holder = new GameObject("UltimateAscentChallenges", typeof(RectTransform));
        RectTransform rect = (RectTransform)holder.transform;
        rect.SetParent(passCard, false);
        rect.anchorMin = HeaderMin;
        rect.anchorMax = HeaderMax;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        VerticalLayoutGroup rows = holder.AddComponent<VerticalLayoutGroup>();
        rows.childAlignment = TextAnchor.MiddleLeft;
        rows.spacing = 4f;
        rows.childControlWidth = true;
        rows.childControlHeight = true;
        // Each row only as wide as its box and name: the header's right end is where True Final Ascent puts its
        // light/dark switch, which a row reaching across would cover and take the click from.
        rows.childForceExpandWidth = false;
        rows.childForceExpandHeight = true;
        blind = Box(holder.transform, buttonLook, tickLook, title, pass.ascentTitle.color, Russian ? "СЛЕПОЙ РЕЖИМ" : "BLIND MODE", on => ChallengeModes.Blind = on, out _, out _);
        hot = Box(holder.transform, buttonLook, tickLook, title, pass.ascentTitle.color, Russian ? "ЖАРКИЙ РЕЖИМ" : "HOT MODE", on => ChallengeModes.Hot = on, out hotLabel, out hotBox);
        return holder;
    }

    /// <summary>The boarding pass's card: the child of the window that holds its ascent title.</summary>
    private static RectTransform Card(BoardingPass pass)
    {
        Transform part = pass.ascentTitle != null ? pass.ascentTitle.transform : null;
        while (part != null && part.parent != null && part.parent != pass.transform)
        {
            part = part.parent;
        }
        return part as RectTransform;
    }

    /// <summary>The header's title ("BOARDING PASS"): the white lettering highest up on the pass.</summary>
    private static TMP_Text HeaderTitle(BoardingPass pass, RectTransform passCard)
    {
        if (passCard == null)
        {
            return null;
        }
        return passCard.GetComponentsInChildren<TMP_Text>(true)
            .Where(t => t.color.r > 0.9f && t.color.g > 0.9f && t.color.b > 0.9f && t != pass.ascentTitle)
            .OrderByDescending(t => t.rectTransform.position.y)
            .FirstOrDefault();
    }

    private static Toggle Box(Transform parent, Image buttonLook, Image tickLook, TMP_Text title, Color blue, string text, System.Action<bool> changed, out TMP_Text label, out Image box)
    {
        GameObject row = new GameObject("UltimateAscent " + text, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        // The whole row (its box and its name) takes the click, not only the box.
        Image hit = row.AddComponent<Image>();
        hit.color = Color.clear;
        HorizontalLayoutGroup line = row.AddComponent<HorizontalLayoutGroup>();
        line.childAlignment = TextAnchor.MiddleLeft;
        line.spacing = 12f;
        line.childControlWidth = true;
        line.childControlHeight = true;
        line.childForceExpandWidth = false;
        line.childForceExpandHeight = false;

        GameObject frame = new GameObject("Box", typeof(RectTransform));
        frame.transform.SetParent(row.transform, false);
        box = frame.AddComponent<Image>();
        box.sprite = buttonLook.sprite;
        box.type = buttonLook.type;
        box.pixelsPerUnitMultiplier = buttonLook.pixelsPerUnitMultiplier;
        box.color = Color.white;
        LayoutElement boxSize = frame.AddComponent<LayoutElement>();
        boxSize.preferredWidth = boxSize.minWidth = BoxSize;
        boxSize.preferredHeight = boxSize.minHeight = BoxSize;

        GameObject mark = new GameObject("Tick", typeof(RectTransform));
        mark.transform.SetParent(frame.transform, false);
        Image tick = mark.AddComponent<Image>();
        tick.sprite = tickLook.sprite;
        tick.preserveAspect = true;
        tick.color = blue;
        tick.raycastTarget = false;
        RectTransform tickRect = (RectTransform)mark.transform;
        tickRect.anchorMin = Vector2.zero;
        tickRect.anchorMax = Vector2.one;
        tickRect.offsetMin = new Vector2(4f, 4f);
        tickRect.offsetMax = new Vector2(-4f, -4f);

        GameObject words = new GameObject("Label", typeof(RectTransform));
        words.transform.SetParent(row.transform, false);
        TextMeshProUGUI name = words.AddComponent<TextMeshProUGUI>();
        name.font = title.font;
        name.fontSharedMaterial = title.fontSharedMaterial;
        name.color = Color.white;
        name.fontSize = LabelSize;
        name.text = text;
        name.enableWordWrapping = false;
        name.alignment = TextAlignmentOptions.MidlineLeft;
        name.raycastTarget = false;
        label = name;

        Toggle toggle = row.AddComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = tick;
        ColorBlock colours = toggle.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(0.86f, 0.9f, 1f, 1f);
        colours.pressedColor = new Color(0.7f, 0.78f, 1f, 1f);
        colours.selectedColor = Color.white;
        colours.disabledColor = Color.white;
        toggle.colors = colours;
        toggle.onValueChanged.AddListener(on =>
        {
            Play(clickSound, row.transform);
            changed(on);
        });
        row.AddComponent<HoverSound>();
        return toggle;
    }

    private static void Play(SFX_Instance sound, Transform at)
    {
        if (sound != null && SFX_Player.instance != null)
        {
            SFX_Player.instance.PlaySFX(sound, at.position);
        }
    }

    /// <summary>The Custom Run screen's hover sound on the boxes.</summary>
    private sealed class HoverSound : MonoBehaviour, IPointerEnterHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            Selectable box = GetComponent<Selectable>();
            if (box != null && box.interactable)
            {
                Play(hoverSound, transform);
            }
        }
    }
}
