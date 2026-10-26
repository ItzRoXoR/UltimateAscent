using System;
using System.Collections;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 15: the Roots' Shroomberries. Each colour still gets one random effect per run (the
/// game's own pick), but the effects are worse:
/// <list type="bullet">
/// <item>low gravity 8 s (the game: 15 s); invincibility 5 s (10 s); the healing one restores 7.5
/// hunger, injury and poison (15 each);</item>
/// <item>+30 spores (+25); blind 120 s (60 s); knocked down 12 s (8 s); numb 120 s (60 s);</item>
/// <item>the spore burst throws back and knocks down the scouts within 10 m (4.8 m) for up to 4 s
/// (1 s), with the game's falloff; it is not an explosion for ascent 17's rules.</item>
/// </list>
/// Infinite stamina (4 s) and the speed boost (5 s) are unchanged. The game's version runs these
/// numbers in a coroutine, so the whole effect is replayed here with the new ones.
/// </summary>
internal static class A14_Shroomberries
{
    private static bool On => Rules.Enabled(Lvl.WorseItems);

    private const string SporeBurst = "VFX_SporeExploExploEdibleSpawn";
    private const string SporeBurstNoKnockback = "VFX_SporeExploExploEdibleSpawn_NoKnockback";

    private const float LowGravitySeconds = 8f;
    private const float InvincibleSeconds = 5f;

    /// <summary>The healing effect: 7.5 hunger, injury and poison (whole steps; the bar drops anything less).</summary>
    private const float Healing = 0.0751f;

    /// <summary>The spore effect: 30 spores (with ascent 14's boost, always on from 15).</summary>
    private const float ExtraSpores = 0.3f;
    private const float GameExtraSpores = 0.25f;

    private const float BlindSeconds = 120f;
    private const float KnockdownSeconds = 12f;
    private const float NumbSeconds = 120f;

    /// <summary>The burst reaches 10 m: the game stops an explosion below 20% strength, so its range is 12.5.</summary>
    private const float BurstRange = 12.5f;
    private const float BurstKnockdownSeconds = 4f;

    internal static bool IsSporeBurst(AOE aoe)
    {
        return aoe != null && aoe.name.StartsWith(SporeBurst, StringComparison.Ordinal) && !aoe.name.StartsWith(SporeBurstNoKnockback, StringComparison.Ordinal);
    }

    [HarmonyPatch(typeof(Action_RandomMushroomEffect), nameof(Action_RandomMushroomEffect.RunAction))]
    private static class Effects
    {
        private static bool Prefix(Action_RandomMushroomEffect __instance)
        {
            Character character = __instance.character;
            if (!On || character == null)
            {
                return true;
            }
            int effect;
            if (__instance.useDebugEffect)
            {
                effect = __instance.debugEffect;
            }
            else
            {
                MushroomManager manager = MushroomManager.instance;
                if (manager == null)
                {
                    return false;
                }
                int type = __instance.mushroomTypeIndex % manager.mushroomEffects.Length;
                effect = manager.mushroomEffects[type];
                character.AddExtraStamina(manager.mushroomStamAmt[type] * 0.05f);
            }
            character.StartCoroutine(Run(character, effect, __instance.fartForce));
            return false;
        }
    }

    /// <summary>The game's effects (same order, same 3 s delays), with this ascent's numbers.</summary>
    private static IEnumerator Run(Character character, int effect, float fartForce)
    {
        switch (effect)
        {
            case 0:
                yield return new WaitForSeconds(3f);
                character.refs.afflictions.AddAffliction(new Affliction_InfiniteStamina(4f));
                break;
            case 1:
                yield return new WaitForSeconds(3f);
                character.refs.afflictions.AddAffliction(new Affliction_FasterBoi { moveSpeedMod = 0.5f, climbSpeedMod = 1.5f, totalTime = 5f, climbDelay = 1f });
                break;
            case 2:
                yield return new WaitForSeconds(3f);
                character.refs.afflictions.AddAffliction(new Affliction_LowGravity(3, LowGravitySeconds));
                break;
            case 3:
                character.refs.afflictions.AddAffliction(new Affliction_Invincibility { totalTime = InvincibleSeconds });
                break;
            case 4:
                character.refs.afflictions.AdjustStatus(CharacterAfflictions.STATUSTYPE.Hunger, -Healing);
                character.refs.afflictions.AdjustStatus(CharacterAfflictions.STATUSTYPE.Injury, -Healing);
                character.refs.afflictions.AdjustStatus(CharacterAfflictions.STATUSTYPE.Poison, -Healing);
                character.refs.afflictions.ClearPoisonAfflictions();
                break;
            case 5:
                yield return new WaitForSeconds(3f);
                GameUtils.instance.SpawnResourceAtPositionNetworked(SporeBurst, character.Center, RpcTarget.Others);
                GameUtils.instance.RPC_SpawnResourceAtPosition(SporeBurstNoKnockback, character.Center);
                character.AddForceToBodyPart(character.GetBodypartRig(BodypartType.Hip), Vector3.zero, Vector3.up * fartForce);
                break;
            case 6:
                yield return new WaitForSeconds(3f);
                character.refs.afflictions.AddAffliction(new Affliction_Blind { totalTime = BlindSeconds });
                break;
            case 7:
                yield return new WaitForSeconds(3f);
                character.Fall(KnockdownSeconds);
                break;
            case 8:
                // Never less than the game's 25 (what ascent 13 gives), which 30 without ascent 14's boost would be.
                CharacterAfflictions.STATUSTYPE spores = CharacterAfflictions.STATUSTYPE.Spores;
                float amount = Mathf.Max(GameExtraSpores, A15_StrongerAilments.FromAscent15(spores, ExtraSpores));
                ExactStatus.Add(character.refs.afflictions, spores, amount, "shroomberry");
                break;
            case 9:
                yield return new WaitForSeconds(3f);
                character.refs.afflictions.AddAffliction(new Affliction_Numb { totalTime = NumbSeconds });
                break;
        }
    }

    /// <summary>The burst that reaches the other scouts (each client explodes its own copy).</summary>
    [HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    private static class Burst
    {
        private static void Prefix(AOE __instance)
        {
            if (On && IsSporeBurst(__instance))
            {
                __instance.range = BurstRange;
                __instance.fallTime = BurstKnockdownSeconds;
            }
        }
    }
}
