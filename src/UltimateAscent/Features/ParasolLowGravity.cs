using HarmonyLib;

namespace UltimateAscent.Features;

/// <summary>
/// The Lightweight Parasol under low gravity (an Initiative gem's, a shroomberry's) lifts the scout up the way balloons
/// do: held open in the air while the low gravity lasts, it pulls as eleven balloons would (the fewest that lift a
/// scout, by the game's own balloon numbers: the float, and how long into a fall the pull keeps growing). On every
/// ascent; from ascent 14 the pull is half as strong, so it carries the scout half as high. The regular Parasol keeps
/// the game's behaviour. The scout's own game moves their body, so it is done there.
/// </summary>
internal static class ParasolLowGravity
{
    /// <summary>The balloons the lift is worth.</summary>
    internal const int LiftAsBalloons = 11;

    /// <summary>From ascent 14: the share of the pull left (half as high).</summary>
    internal const float LiftWorse = 0.5f;

    /// <summary>After the game has worked out the scout's balloons for this physics step.</summary>
    [HarmonyPatch(typeof(CharacterBalloons), "FixedUpdate")]
    private static class LiftsLikeBalloons
    {
        private static void Postfix(CharacterBalloons __instance)
        {
            Character character = __instance.character;
            if (character == null || !character.IsLocal || character.data.isGrounded || character.data.lowGravAmount <= 0
                || !A14_Items.IsLightweightParasol(character.data.currentItem)
                || !character.data.currentItem.TryGetComponent(out Parasol parasol) || !parasol.isOpen)
            {
                return;
            }
            int balloons = System.Math.Max(LiftAsBalloons, __instance.currentBalloonCount);
            float lift = 1f - __instance.balloonFloatAmount * balloons;
            if (lift < 0f)
            {
                lift -= __instance.extraFloatUpwardMultiplier;
                if (Core.Rules.Enabled(Core.Lvl.WorseItems))
                {
                    lift *= LiftWorse;
                }
            }
            CharacterMovement movement = character.refs.movement;
            movement.balloonFloatMultiplier = System.Math.Min(movement.balloonFloatMultiplier, lift);
            float cap = UnityEngine.Mathf.Clamp(2f - __instance.balloonSinceGroundedCapAmount * balloons, 0.5f, 2f);
            if (character.data.sinceGrounded > cap)
            {
                character.data.sinceGrounded = cap;
            }
        }
    }
}
