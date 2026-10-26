using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 11 (13DDA): rule zero is stricter.
/// A scout who is far ahead of somebody and has nobody within 35 m gets hunted by the Scoutmaster
/// for 30 s (70 s from ascent 17); a scout coming within 35 m calls him off. On every ascent the game's own
/// Scoutmaster also gives up on his target when another scout is within 35 m of them (the game: 15 m).
/// Every death curses the survivors, and
/// being thrown adds curse. Eating a scout poisons you for 50 and curses you for 25 (the game: 10
/// curse).
/// </summary>
internal static class A11_RuleZero
{
    private const float CheckInterval = 5f;
    private const float ForceSeconds = 30f;
    private const float HazardForceSeconds = 70f;

    /// <summary>Cannibalism: 50 poison from ascent 14 (28.6 below it, without that ascent's boost) and 25 curse in all (the game adds 10 curse itself).</summary>
    internal const float CannibalPoison = 0.5f;
    internal const float CannibalCurse = 0.25f;
    internal const float GameCannibalCurse = 0.1f;
    /// <summary>Nobody within this of a scout: alone (the hunt starts on them; anyone coming this near calls it off).</summary>
    internal const float CompanionRadius = 35f;
    private const double CompanionRadiusSq = CompanionRadius * CompanionRadius;
    private const double LeadDistance = 150.0;

    private readonly struct Scout
    {
        internal readonly int Id;
        internal readonly Vector3 Position;

        internal Scout(int id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        /// <summary>How far up the mountain this scout is (the mountain rises along +Y and |Z|).</summary>
        internal double Progress => Position.y + Math.Abs(Position.z);
    }

    private static readonly List<Scout> scouts = new List<Scout>(16);
    private static Scoutmaster huntingScout;
    private static int huntedId;
    private static float nextCheck;

    /// <summary>This rule has the Scoutmaster hunting someone (host).</summary>
    internal static bool Hunting => huntingScout != null;

    internal static void Reset()
    {
        huntingScout = null;
        huntedId = 0;
        nextCheck = 0f;
    }

    /// <summary>Host only; called every frame.</summary>
    internal static void Tick()
    {
        if (!Rules.Enabled(Lvl.RuleZero) || !Rules.IsHost || LoadingScreenHandler.loading || Time.time < nextCheck)
        {
            return;
        }
        nextCheck = Time.time + CheckInterval;
        // True Final Ascent's Blackout and Watch send the Scoutmaster themselves: this rule's hunt steps aside.
        if (Compat.Tfa.ScoutmasterEvent)
        {
            if (huntingScout != null)
            {
                Character hunted = huntingScout.currentTarget;
                if (hunted != null && hunted.photonView != null && hunted.photonView.ViewID == huntedId && !huntingScout.isThrowing)
                {
                    huntingScout.SetCurrentTarget(null);
                }
                huntingScout = null;
            }
            return;
        }
        CollectScouts();

        if (huntingScout != null)
        {
            Character target = huntingScout.currentTarget;
            if (target == null || target.photonView == null || target.photonView.ViewID != huntedId)
            {
                // The Scoutmaster picked someone else on his own (or lost the target): not ours any more.
                huntingScout = null;
            }
            else if (!huntingScout.isThrowing && ShouldRelease())
            {
                huntingScout.SetCurrentTarget(null);
                huntingScout = null;
            }
            return;
        }

        if (Scoutmaster.AllScoutmasters.Count >= 2 || !Scoutmaster.GetPrimaryScoutmaster(out Scoutmaster scoutmaster))
        {
            return;
        }
        if (scoutmaster.currentTarget != null || scoutmaster.isThrowing || scoutmaster.preventSpawning)
        {
            return;
        }
        int id = PickTarget();
        if (id <= 0)
        {
            return;
        }
        PhotonView view = PhotonView.Find(id);
        Character character = view != null ? view.GetComponent<Character>() : null;
        if (character == null)
        {
            return;
        }
        scoutmaster.SetCurrentTarget(character, Rules.Enabled(Lvl.Hazards) ? HazardForceSeconds : ForceSeconds);
        huntingScout = scoutmaster;
        huntedId = id;
        Plugin.Debug($"Rule zero: the Scoutmaster now hunts {character.characterName}.");
    }

    private static void CollectScouts()
    {
        scouts.Clear();
        foreach (Character character in Character.AllCharacters)
        {
            if (character == null || character.isBot || character.isScoutmaster || character.data == null || !character.data.fullyConscious || character.photonView == null)
            {
                continue;
            }
            scouts.Add(new Scout(character.photonView.ViewID, character.Center));
        }
    }

    private static bool HasCompanion(Scout target)
    {
        foreach (Scout other in scouts)
        {
            if (other.Id != target.Id && (other.Position - target.Position).sqrMagnitude < CompanionRadiusSq)
            {
                return true;
            }
        }
        return false;
    }

    private static int PickTarget()
    {
        int best = -1;
        double bestProgress = double.NegativeInfinity;
        foreach (Scout target in scouts)
        {
            if (HasCompanion(target))
            {
                continue;
            }
            foreach (Scout other in scouts)
            {
                if (other.Id != target.Id && target.Progress > other.Progress + LeadDistance &&
                    (target.Progress > bestProgress || (target.Progress == bestProgress && target.Id < best)))
                {
                    bestProgress = target.Progress;
                    best = target.Id;
                }
            }
        }
        return best;
    }

    private static bool ShouldRelease()
    {
        if (scouts.Count < 2)
        {
            return true;
        }
        foreach (Scout scout in scouts)
        {
            if (scout.Id == huntedId)
            {
                return HasCompanion(scout);
            }
        }
        return true;
    }

    /// <summary>The game's Scoutmaster gives up on a target with another scout this near (on every ascent; the game: 15 m).</summary>
    internal static float GiveUpDistance() => CompanionRadius;

    private const float GameGiveUpDistance = 15f;

    [HarmonyPatch(typeof(Scoutmaster), nameof(Scoutmaster.VerifyTarget))]
    private static class GivesUpNearOthers
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            List<int> at = new List<int>();
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && code[i].operand is float value && value == GameGiveUpDistance)
                {
                    at.Add(i);
                }
            }
            if (at.Count != 1)
            {
                Plugin.Log.LogWarning($"Scoutmaster: the game's 15 m give-up distance was found {at.Count} times; it stays the game's.");
                return code;
            }
            code[at[0]] = new CodeInstruction(System.Reflection.Emit.OpCodes.Call, AccessTools.Method(typeof(A11_RuleZero), nameof(GiveUpDistance))).MoveLabelsFrom(code[at[0]]);
            return code;
        }
    }

    /// <summary>Survivors are cursed whenever a scout dies; bigger lobbies get less.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.RPCA_Die))]
    private static class CurseOnDeath
    {
        private static void Prefix(Character __instance, out bool __state)
        {
            __state = __instance.data != null && __instance.data.dead;
        }

        private static void Postfix(Character __instance, bool __state)
        {
            // A scout who has only just appeared and dies is the game's dead-on-arrival for a joiner, not a death.
            if (__state || !Rules.Enabled(Lvl.RuleZero) || __instance.isBot || __instance.isScoutmaster || LateJoiners.JustArrived(__instance))
            {
                return;
            }
            Character local = Character.localCharacter;
            if (local == null || local == __instance || local.data.dead)
            {
                return;
            }
            int players = 0;
            foreach (Character character in Character.AllCharacters)
            {
                if (character != null && !character.isBot && !character.isScoutmaster)
                {
                    players++;
                }
            }
            float curse = players <= 2 ? 0.12f : players <= 4 ? 0.08f : players <= 6 ? 0.04f : 0.02f;
            local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, curse);
        }
    }

    [HarmonyPatch(typeof(CharacterInteractible), nameof(CharacterInteractible.GetEaten))]
    private static class Cannibalism
    {
        private static void Postfix(Character eater)
        {
            if (Rules.Enabled(Lvl.RuleZero) && eater != null && eater.IsLocal)
            {
                CharacterAfflictions.STATUSTYPE poison = CharacterAfflictions.STATUSTYPE.Poison;
                ExactStatus.Add(eater.refs.afflictions, poison, A15_StrongerAilments.FromAscent15(poison, CannibalPoison), "cannibalism");
                eater.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, CannibalCurse - GameCannibalCurse);
            }
        }
    }

    /// <summary>Being thrown by the Scoutmaster adds 4 curse (not when True Final Ascent's Blackout or Watch sent him).</summary>
    [HarmonyPatch(typeof(Scoutmaster), nameof(Scoutmaster.IThrow))]
    private static class CurseOnThrow
    {
        private static void Prefix(Scoutmaster __instance)
        {
            if (Rules.Enabled(Lvl.RuleZero) && Rules.Owns(__instance.currentTarget) && !Compat.Tfa.ScoutmasterEvent)
            {
                __instance.currentTarget.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, 0.04f);
            }
        }
    }
}
