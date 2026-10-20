using TMPro;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// A short line of text in the middle of the screen, in the place and lettering of the game's "THE GLOOM RISES" (a
/// copy of it), without its sound, its swing in and its wobbling letters: it fades in, stays a moment and fades out.
/// Shown only on this player's screen.
/// </summary>
internal static class Notice
{
    private const float FadeIn = 0.2f;
    private const float Hold = 2.6f;
    private const float FadeOut = 0.6f;

    /// <summary>A little smaller than the game's own announcements, which are about the whole climb.</summary>
    private const float FontScale = 0.7f;

    private static GameObject shown;
    private static TMP_Text text;
    private static Fader fader;

    /// <summary>The last line shown (for the tests).</summary>
    internal static string Last { get; private set; }

    internal static void Show(string english, string russian)
    {
        string line = LocalizedText.CURRENT_LANGUAGE == LocalizedText.Language.Russian && !string.IsNullOrEmpty(russian) ? russian : english;
        Last = line;
        if (!Ensure())
        {
            Plugin.Log.LogInfo("Notice: " + line);
            return;
        }
        text.text = line;
        shown.SetActive(true);
        fader.Restart();
    }

    private static bool Ensure()
    {
        if (shown != null)
        {
            return true;
        }
        GameObject source = GUIManager.instance != null ? GUIManager.instance.gloomRises : null;
        if (source == null)
        {
            return false;
        }
        bool wasActive = source.activeSelf;
        source.SetActive(false);
        GameObject copy = Object.Instantiate(source, source.transform.parent, false);
        source.SetActive(wasActive);
        copy.name = "UltimateAscent Notice";
        foreach (Component part in copy.GetComponentsInChildren<Component>(true))
        {
            // Its swing in (the Animator), its wobble (the text effect), its translation (it shows our words) and its sound.
            if (part is Behaviour behaviour && !(part is TMP_Text) && !(part is CanvasGroup) && !(part is UnityEngine.UI.Graphic) && !(part is UnityEngine.UI.BaseMeshEffect))
            {
                Object.DestroyImmediate(behaviour);
            }
            else if (part is AudioSource)
            {
                Object.DestroyImmediate(part.gameObject);
            }
        }
        text = copy.GetComponentInChildren<TMP_Text>(true);
        CanvasGroup group = copy.GetComponent<CanvasGroup>() ?? copy.AddComponent<CanvasGroup>();
        if (text == null)
        {
            Object.Destroy(copy);
            return false;
        }
        text.fontSize *= FontScale;
        text.transform.localScale = Vector3.one;
        copy.transform.localScale = Vector3.one;
        fader = copy.AddComponent<Fader>();
        fader.group = group;
        shown = copy;
        return true;
    }

    private sealed class Fader : MonoBehaviour
    {
        internal CanvasGroup group;
        private float started;

        internal void Restart()
        {
            started = Time.unscaledTime;
            Update();
        }

        private void Update()
        {
            float t = Time.unscaledTime - started;
            if (t >= FadeIn + Hold + FadeOut)
            {
                gameObject.SetActive(false);
                return;
            }
            group.alpha = t < FadeIn ? t / FadeIn : t < FadeIn + Hold ? 1f : 1f - (t - FadeIn - Hold) / FadeOut;
        }
    }
}
