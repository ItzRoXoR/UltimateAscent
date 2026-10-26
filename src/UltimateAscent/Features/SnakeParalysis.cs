using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Peak;
using Photon.Pun;
using TMPro;
using UltimateAscent.Core;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateAscent.Features;

/// <summary>
/// A tree snake's paralysis (see <see cref="Snakes"/>): for 2 minutes after its bite the scout collapses for 4 s every
/// 15-30 s (the first 5-15 s after the bite); below ascent 17 for 1 minute, 2 s each time. Bitten again meanwhile, it
/// does not stack: its time starts over, the collapses keeping their pace.
/// <list type="bullet">
/// <item>While it lasts (with the config's Effect Icons on), a poison drop shows above the stamina bar, past its right end in the Climbing Chalk hand's
/// place (beside the hand when both show); it drains as the time runs out, with the time left beside it. It shows
/// for the scout the screen follows: each player's game sends its own scout's time to the others.</item>
/// <item>The game's poison cures end it: a First Aid Kit, a Cure-All (in Russian the Panacea), a Blowgun's dart, a
/// Napberry, a Medicinal Root (the Mandrake hidden as one too) and a Remedy Fungus's cloud, used on the scout or
/// reaching them.</item>
/// </list>
/// </summary>
internal static class SnakeParalysis
{
    /// <summary>The prefab names of the items whose use ends it (the Remedy Fungus works through its cloud).</summary>
    private static readonly string[] CureItems = { "FirstAidKit", "Cure-All", "HealingDart", "Napberry", "MedicinalRoot", "Mandrake_Hidden" };

    private const string Message = "paralysis";
    private const string IconResource = "UltimateAscent.Paralysis_status.png";
    private const float IconSize = 40f;
    /// <summary>Beside the chalk's hand: one icon further along the row.</summary>
    private const float IconStep = 44f;
    private const float ClockSize = 18f;
    /// <summary>What has drained away: the drop faded, as the game fades its own flat status icons.</summary>
    private static readonly Color Drained = new Color(1f, 1f, 1f, 0.35f);

    /// <summary>The local scout's paralysis: when it ends (in the past: none), and when the next collapse comes.</summary>
    private static float until;
    private static float nextCollapse;
    private static Coroutine running;

    private static bool Paralysed => until > Time.time;

    /// <summary>When each scout's paralysis ends on the room's clock, as their own game sent it (by the scout's view).</summary>
    private static readonly Dictionary<int, double> endsAt = new Dictionary<int, double>();

    private static GameObject icon;
    private static Image drop;
    private static TMP_Text clock;
    private static Sprite sprite;
    private static bool spriteFailed;

    /// <summary>For the tests: seconds to the local scout's next collapse.</summary>
    internal static float NextCollapseIn => Paralysed ? nextCollapse - Time.time : 0f;

    /// <summary>For the tests: what ended it last, and how often a cure did.</summary>
    internal static string LastCure { get; private set; } = "";
    internal static int CuresSeen { get; private set; }

    /// <summary>For the tests: the drop shows, and the time beside it.</summary>
    internal static bool IconShowing => icon != null && icon.activeInHierarchy;
    internal static string IconText => clock != null ? clock.text : "";
    internal static Vector2 IconPosition => icon != null ? ((RectTransform)icon.transform).anchoredPosition : Vector2.zero;

    /// <summary>Each player's game hears the others' times.</summary>
    internal static void Listen()
    {
        Net.Handle(Message, args =>
        {
            if (args.Length > 1 && args[0] is int view && args[1] is double end)
            {
                endsAt[view] = end;
            }
        });
    }

    /// <summary>
    /// A tree snake's bite on the local scout: two minutes of collapses; bitten again while they last, the two minutes
    /// start over (it does not stack, and the next collapse stays when it was).
    /// </summary>
    internal static void Bite(Character scout)
    {
        float now = Time.time;
        if (!Paralysed)
        {
            nextCollapse = now + UnityEngine.Random.Range(Snakes.FirstCollapseMin, Snakes.FirstCollapseMax);
        }
        until = now + Snakes.ParalysisSeconds * NewMobs.Strength;
        if (running == null)
        {
            running = Snakes.Venom.Runner.StartCoroutine(Collapses(scout));
        }
        Send(scout);
    }

    /// <summary>Its collapses until the two minutes are over.</summary>
    private static IEnumerator Collapses(Character scout)
    {
        while (Paralysed)
        {
            yield return null;
            if (scout == null || scout.data.dead)
            {
                break;
            }
            float now = Time.time;
            if (now >= nextCollapse && now < until)
            {
                scout.Fall(Snakes.CollapseSeconds * NewMobs.Strength);
                nextCollapse = now + UnityEngine.Random.Range(Snakes.CollapseEveryMin, Snakes.CollapseEveryMax);
            }
        }
        until = 0f;
        running = null;
        Send(scout);
    }

    /// <summary>The time left, to every game (nothing once it is over).</summary>
    private static void Send(Character scout)
    {
        if (scout == null || scout.photonView == null)
        {
            return;
        }
        double end = Paralysed ? Net.Clock + (until - Time.time) : 0.0;
        Net.SendAll(Message, scout.photonView.ViewID, end);
    }

    /// <summary>A cure reached the local scout: the paralysis is over.</summary>
    private static void Cure(Character scout, string by)
    {
        if (scout == null || !scout.IsLocal || !Paralysed)
        {
            return;
        }
        Stop();
        LastCure = by;
        CuresSeen++;
        Plugin.Debug($"Tree snake paralysis cured by {by}.");
    }

    /// <summary>Over at once, no more collapses (a cure; the tests).</summary>
    internal static void Stop()
    {
        until = 0f;
        if (running != null)
        {
            Snakes.Venom.Runner.StopCoroutine(running);
            running = null;
        }
        Send(Character.localCharacter);
    }

    /// <summary>Seconds of paralysis a scout has left (0 if none), as their game sent it.</summary>
    internal static float Left(Character scout)
    {
        if (scout == null || scout.photonView == null || !endsAt.TryGetValue(scout.photonView.ViewID, out double end))
        {
            return 0f;
        }
        double left = end - Net.Clock;
        // Over, or written on another room's clock.
        return left > 0.0 && left <= Snakes.ParalysisSeconds + 1f ? (float)left : 0f;
    }

    private static bool CuresIt(Item item)
    {
        if (item == null)
        {
            return false;
        }
        string name = item.gameObject.name;
        return CureItems.Any(cure => name.StartsWith(cure, StringComparison.Ordinal));
    }

    private static void UsedOn(Character scout, Item item)
    {
        if (Paralysed && CuresIt(item))
        {
            Cure(scout, item.gameObject.name.Replace("(Clone)", ""));
        }
    }

    private static bool InRemedyCloud(Transform part)
    {
        for (Transform at = part; at != null; at = at.parent)
        {
            if (at.name.StartsWith(A14_Items.RemedyCloudParent, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A First Aid Kit, a Cure-All, a Medicinal Root: their healing runs on the game of the scout who gets it (the
    /// holder, or the scout it is fed to).
    /// </summary>
    [HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static class Healed
    {
        private static void Postfix(Action_ModifyStatus __instance)
        {
            Item item = __instance.item;
            UsedOn(item != null ? item.holderCharacter : null, item);
        }
    }

    /// <summary>A Napberry: fed to the scout, it clears their statuses on their own game.</summary>
    [HarmonyPatch(typeof(Action_ClearAllStatus), nameof(Action_ClearAllStatus.RunAction))]
    private static class Cleared
    {
        private static void Postfix(Action_ClearAllStatus __instance)
        {
            Item item = __instance.item;
            UsedOn(item != null ? item.holderCharacter : null, item);
        }
    }

    /// <summary>A Blowgun's dart takes effect on the game of the scout it hits.</summary>
    [HarmonyPatch(typeof(Action_RaycastDart), nameof(Action_RaycastDart.RPC_DartImpact))]
    private static class Darted
    {
        private static void Postfix(Action_RaycastDart __instance, int characterID)
        {
            PhotonView view = characterID != -1 ? PhotonNetwork.GetPhotonView(characterID) : null;
            Character hit = view != null ? view.GetComponent<Character>() : null;
            if (hit != null && hit.photonView.IsMine)
            {
                UsedOn(hit, __instance.item);
            }
        }
    }

    /// <summary>A Remedy Fungus's cloud heals the local scout inside it, on their own game (its status field).</summary>
    [HarmonyPatch(typeof(StatusFieldBase), nameof(StatusFieldBase.Update))]
    private static class InTheCloud
    {
        private static void Postfix(StatusFieldBase __instance)
        {
            if (Paralysed && __instance.characterInField && InRemedyCloud(__instance.transform))
            {
                Cure(Character.localCharacter, "Remedy Fungus cloud");
            }
        }
    }

    /// <summary>The same cloud, should it heal in bursts (each game takes its own scout in reach).</summary>
    [HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    private static class CloudBurst
    {
        private static void Postfix(AOE __instance)
        {
            Character local = Character.localCharacter;
            if (Paralysed && local != null && InRemedyCloud(__instance.transform) && Vector3.Distance(local.Center, __instance.transform.position) <= __instance.range)
            {
                Cure(local, "Remedy Fungus cloud");
            }
        }
    }

    /// <summary>
    /// The drop over the stamina bar for the scout the screen follows. It hangs off the bar itself, as the chalk's hand
    /// does (anything inside the bar's outline would be cut off by its mask).
    /// </summary>
    [HarmonyPatch(typeof(StaminaBar), nameof(StaminaBar.Update))]
    private static class Drop
    {
        private static void Postfix(StaminaBar __instance)
        {
            float left = Plugin.EffectIcons.Value ? Left(Character.observedCharacter) : 0f;
            if (icon == null || icon.transform.parent != __instance.transform)
            {
                if (left <= 0f)
                {
                    return;
                }
                icon = MakeIcon(__instance);
                if (icon == null)
                {
                    return;
                }
            }
            bool show = left > 0f;
            if (icon.activeSelf != show)
            {
                icon.SetActive(show);
            }
            if (!show)
            {
                return;
            }
            ((RectTransform)icon.transform).anchoredPosition = ClimbingChalkItem.StatusIconPosition + (ClimbingChalkItem.StatusIconShowing ? new Vector2(IconStep, 0f) : Vector2.zero);
            drop.fillAmount = Mathf.Clamp01(left / (Snakes.ParalysisSeconds * NewMobs.Strength));
            if (clock != null)
            {
                int seconds = Mathf.CeilToInt(left);
                clock.text = $"{seconds / 60}:{seconds % 60:00}";
            }
        }
    }

    private static GameObject MakeIcon(StaminaBar bar)
    {
        Sprite image = IconSprite();
        if (image == null)
        {
            return null;
        }
        GameObject root = new GameObject("UltimateAscent Paralysis Icon", typeof(RectTransform));
        root.layer = bar.gameObject.layer;
        RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(bar.transform, false);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(IconSize, IconSize);
        rect.anchoredPosition = ClimbingChalkItem.StatusIconPosition;
        Image empty = Part(root, "Drained");
        empty.sprite = image;
        empty.color = Drained;
        drop = Part(root, "Drop");
        drop.sprite = image;
        // It drains from the top as the time runs out.
        drop.type = Image.Type.Filled;
        drop.fillMethod = Image.FillMethod.Vertical;
        drop.fillOrigin = (int)Image.OriginVertical.Bottom;
        clock = Clock(root);
        root.SetActive(false);
        return root;
    }

    private static Image Part(GameObject root, string name)
    {
        GameObject part = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        part.layer = root.layer;
        RectTransform rect = (RectTransform)part.transform;
        rect.SetParent(root.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = part.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>The time left, just right of the drop, in the lettering of the game's own announcements.</summary>
    private static TMP_Text Clock(GameObject root)
    {
        TMP_Text lettering = GUIManager.instance != null && GUIManager.instance.gloomRises != null ? GUIManager.instance.gloomRises.GetComponentInChildren<TMP_Text>(true) : null;
        if (lettering == null)
        {
            return null;
        }
        GameObject part = new GameObject("Time Left", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        part.layer = root.layer;
        RectTransform rect = (RectTransform)part.transform;
        rect.SetParent(root.transform, false);
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(64f, 24f);
        rect.anchoredPosition = new Vector2(2f, 0f);
        TextMeshProUGUI text = part.GetComponent<TextMeshProUGUI>();
        text.font = lettering.font;
        text.fontSharedMaterial = lettering.fontSharedMaterial;
        text.fontSize = ClockSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        return text;
    }

    private static Sprite IconSprite()
    {
        if (sprite != null || spriteFailed)
        {
            return sprite;
        }
        try
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "Paralysis_status",
                wrapMode = TextureWrapMode.Clamp,
                // Shown at about a sixth of its size: blend the mipmaps.
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!texture.LoadImage(ClimbingChalkModel.Resource(IconResource), true))
            {
                throw new InvalidDataException("the icon is not a readable image");
            }
            sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "Paralysis_status";
            sprite.hideFlags = HideFlags.HideAndDontSave;
        }
        catch (Exception ex)
        {
            spriteFailed = true;
            Plugin.Log.LogError("The tree snake paralysis icon could not be loaded: " + ex.Message);
        }
        return sprite;
    }
}
