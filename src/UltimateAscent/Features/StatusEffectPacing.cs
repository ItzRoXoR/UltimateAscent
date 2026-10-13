using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The game plays a status's screen shake, screen overlay and sound for every 0.025 of it that a
/// scout gets. The stronger hazards of these ascents add so much per second (ascent 17's bees: about
/// two bars of poison a second) that a new effect would start every frame and the shakes would pile
/// up into violent shaking. From ascent 9 on, each status plays its effects at most once per effect
/// length (0.3 s; 1 s for heat and cold, which shake for a second), so being stung feels like the
/// game's own bee attack. The statuses themselves are untouched; injuries always play.
/// </summary>
internal static class StatusEffectPacing
{
    private const float Gap = 0.3f;
    private const float LongGap = 1f;

    private static readonly float[] screen = new float[64];
    private static ConditionalWeakTable<CharacterAfflictions, float[]> sounds = new ConditionalWeakTable<CharacterAfflictions, float[]>();

    private static bool On => Rules.Enabled(Lvl.First);

    private static bool Allow(float[] lastPlayed, CharacterAfflictions.STATUSTYPE type)
    {
        int index = (int)type;
        if (type == CharacterAfflictions.STATUSTYPE.Injury || index < 0 || index >= lastPlayed.Length)
        {
            return true;
        }
        float gap = type == CharacterAfflictions.STATUSTYPE.Hot || type == CharacterAfflictions.STATUSTYPE.Cold ? LongGap : Gap;
        float now = Time.unscaledTime;
        if (now - lastPlayed[index] < gap && now >= lastPlayed[index])
        {
            return false;
        }
        lastPlayed[index] = now;
        return true;
    }

    [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.AddStatusFX))]
    private static class ScreenEffects
    {
        private static bool Prefix(CharacterAfflictions.STATUSTYPE type)
        {
            return !On || Allow(screen, type);
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.StatusSFX))]
    private static class Sounds
    {
        private static bool Prefix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE sT)
        {
            return !On || Allow(sounds.GetValue(__instance, _ => new float[64]), sT);
        }
    }
}
