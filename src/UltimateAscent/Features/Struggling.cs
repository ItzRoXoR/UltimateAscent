using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Breaking free from a spider's web or a fly trap, on every ascent. Caught, every jump takes some of the
/// web or the trap off the bar. The game counted such a jump only while the scout had stamina left, each
/// cost stamina (5%, 7% from the game's ascent 6) that did not come back while the jumping went on, and
/// the web, the trap and their poison take up the bar the stamina lives in: at the higher ascents the
/// jumps stopped counting half way and nobody got free alone. And a press within about 0.4 s of the last
/// counted jump was dropped without a trace.
/// <list type="bullet">
/// <item>Jumps while caught need no stamina and cost none.</item>
/// <item>A press that comes too early is kept and counts as soon as the next jump can (one at a time,
/// for at most 0.5 s).</item>
/// </list>
/// </summary>
internal static class Struggling
{
    private static bool On => Plugin.Ready && Rules.InRun;

    /// <summary>How long a press made too early waits for the next jump.</summary>
    private const float PressKeptSeconds = 0.5f;

    /// <summary>The game takes a jump's stamina 0.1 s into it, when the last jump may have freed the scout already.</summary>
    private const float JumpStaminaSeconds = 0.3f;

    private static float pressedAt = -10f;
    private static float freeUntil = -10f;

    /// <summary>Struggle jumps counted on this game (for the tests).</summary>
    internal static int Jumps { get; private set; }

    private static bool Caught(Character character) => character.refs.afflictions.isStruggling;

    /// <summary>The game's own conditions for a jump while caught, without the stamina.</summary>
    private static bool CanJump(Character character)
    {
        CharacterData data = character.data;
        return character.CheckJump() && !data.chargingJump && data.sinceJump >= 0.1f && (data.sinceJump >= 0.3f || data.extraJumps > 0);
    }

    private static void Jump(Character character)
    {
        pressedAt = -10f;
        Jumps++;
        character.refs.view.RPC("JumpRpc", RpcTarget.All, false);
    }

    [HarmonyPatch(typeof(CharacterMovement), "TryToJump")]
    private static class PressWhileCaught
    {
        private static bool Prefix(CharacterMovement __instance)
        {
            Character character = __instance.character;
            if (!On || character == null || !character.IsLocal || !Caught(character))
            {
                return true;
            }
            if (CanJump(character))
            {
                Jump(character);
            }
            else
            {
                pressedAt = Time.time;
            }
            return false;
        }
    }

    /// <summary>A press kept from too early counts once the next jump can.</summary>
    [HarmonyPatch(typeof(CharacterMovement), "Update")]
    private static class KeptPress
    {
        private static void Postfix(CharacterMovement __instance)
        {
            Character character = __instance.character;
            if (pressedAt < 0f || character == null || !character.IsLocal)
            {
                return;
            }
            if (!On || !Caught(character) || Time.time - pressedAt > PressKeptSeconds)
            {
                pressedAt = -10f;
            }
            else if (CanJump(character))
            {
                Jump(character);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.JumpRpc))]
    private static class FreeJump
    {
        private static void Prefix(CharacterMovement __instance)
        {
            Character character = __instance.character;
            if (On && character != null && character.IsLocal && Caught(character))
            {
                freeUntil = Time.time + JumpStaminaSeconds;
            }
        }
    }

    /// <summary>Caught (or just jumped free), nothing costs stamina.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    private static class NoStaminaWhileCaught
    {
        private static bool Prefix(Character __instance, ref bool __result)
        {
            if (On && __instance.IsLocal && (Caught(__instance) || Time.time < freeUntil))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
