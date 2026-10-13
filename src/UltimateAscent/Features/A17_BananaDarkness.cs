using HarmonyLib;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: slipping on a banana peel darkens your screen the way the Scoutmaster does when he
/// comes for you, for 20 seconds, with no Scoutmaster anywhere. It drives his own screen effect,
/// which only the scout who slipped sees.
/// </summary>
internal static class A17_BananaDarkness
{
    private const float Seconds = 20f;
    private const float FadeInSeconds = 1f;
    private const float FadeOutSeconds = 2f;

    private static float startedAt = -1000f;

    internal static void Begin()
    {
        startedAt = Time.time;
    }

    internal static void Reset()
    {
        startedAt = -1000f;
    }

    /// <summary>How dark the screen is right now, 0 to 1.</summary>
    internal static float Strength
    {
        get
        {
            float t = Time.time - startedAt;
            if (t < 0f || t >= Seconds)
            {
                return 0f;
            }
            return Mathf.Min(Mathf.Clamp01(t / FadeInSeconds), Mathf.Clamp01((Seconds - t) / FadeOutSeconds));
        }
    }

    internal static bool Active => Strength > 0f;

    /// <summary>The Scoutmaster sets his screen effect every frame (there is always one in the level, hidden away).</summary>
    [HarmonyPatch(typeof(Scoutmaster), nameof(Scoutmaster.DoVisuals))]
    private static class Darken
    {
        private static void Postfix(Scoutmaster __instance)
        {
            float strength = Strength;
            if (strength <= 0f || __instance.mat == null)
            {
                return;
            }
            __instance.mat.SetFloat(__instance.GRAINMULTID, GUIManager.instance != null && GUIManager.instance.photosensitivity ? 0f : 1f);
            __instance.mat.SetFloat(__instance.STRENGTHID, Mathf.Max(__instance.mat.GetFloat(__instance.STRENGTHID), strength));
        }
    }
}
