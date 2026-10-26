using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: magma rocks (and other hot rock) burn you while you climb them, the way ice rocks
/// chill you: 2.5 heat every 0.3 s, the same numbers as ice rocks' cold, on top of touching them.
/// The game gives hot rock no climbing effect at all. (Ice rocks and petrifying stones have one of
/// their own; see <see cref="A17_Hazards"/>.)
/// </summary>
internal static class A17_ClimbingHazards
{
    /// <summary>Ice rocks' climbing interval.</summary>
    private const float TickSeconds = 0.3f;

    /// <summary>What an ice rock's climbing tick puts on the bar at this ascent: 1 cold, x2 for ascent 17, x1.3 for ascent 14, in steps of 2.5.</summary>
    private const float HeatPerTick = 0.025f;

    private static float lastTick = -10f;

    private sealed class Heat
    {
        internal bool hot;
    }

    private static readonly ConditionalWeakTable<Collider, Heat> rocks = new ConditionalWeakTable<Collider, Heat>();

    internal static void Reset()
    {
        lastTick = -10f;
    }

    private static bool IsHotRock(Collider collider)
    {
        if (!rocks.TryGetValue(collider, out Heat heat))
        {
            heat = new Heat();
            CollisionModifier modifier = collider.GetComponent<CollisionModifier>();
            if (modifier != null && modifier.applyEffects && !modifier.applyPetrify && collider.GetComponent<ClimbModifierSurface>() == null)
            {
                heat.hot = modifier.statusType == CharacterAfflictions.STATUSTYPE.Hot && modifier.damage > 0f;
                if (!heat.hot && modifier.additionalMods != null)
                {
                    foreach (CollisionMod mod in modifier.additionalMods)
                    {
                        if (mod != null && mod.statusType == CharacterAfflictions.STATUSTYPE.Hot && mod.amount > 0f)
                        {
                            heat.hot = true;
                            break;
                        }
                    }
                }
            }
            rocks.Add(collider, heat);
        }
        return heat.hot;
    }

    /// <summary>The game samples the wall every physics step while you climb.</summary>
    [HarmonyPatch(typeof(CharacterClimbing), nameof(CharacterClimbing.SampleWall))]
    private static class HotRockClimbing
    {
        private static void Postfix(CharacterClimbing __instance, RaycastHit __result)
        {
            if (!Rules.Enabled(Lvl.Hazards) || __result.collider == null || Time.time < lastTick + TickSeconds)
            {
                return;
            }
            Character character = __instance.character;
            if (character == null || !character.IsLocal || !character.data.isClimbing || !IsHotRock(__result.collider))
            {
                return;
            }
            lastTick = Time.time;
            character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Hot, HeatPerTick));
        }
    }
}
