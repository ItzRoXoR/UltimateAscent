using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 14 (13DDA): items are worse.
/// Food and stamina items give half, healing gives 60%, cure-alls only partly cure, consumables
/// lose two uses at a time, ropes are shorter, chain/balloons weaker, energy drinks slower,
/// lollipops make you three times as drowsy, poisonous food is much worse and cooking no longer
/// removes poison, parasols make you fall faster, gliders drain more stamina, rescue hooks drop
/// you, the cannon burns, skeletons are fragile and invincibility only blocks 75%.
/// </summary>
internal static class A14_WorseItems
{
    private static bool On => Rules.Enabled(Lvl.WorseItems);

    [HarmonyPatch(typeof(Action_RestoreHunger), nameof(Action_RestoreHunger.RunAction))]
    private static class HalfFood
    {
        private static bool Prefix(Action_RestoreHunger __instance)
        {
            if (!On)
            {
                return true;
            }
            // Ticks are no food any more.
            if (!Ticks.IsTick(__instance.item))
            {
                __instance.character.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Hunger, __instance.restorationAmount * 0.5f);
            }
            return false;
        }
    }

    /// <summary>
    /// The item you get by pulling a tick off. It is recognised by the game's own tick prefab
    /// ("BugfixOnYou" and the item it hands out), not by its name.
    /// </summary>
    internal static class Ticks
    {
        private static bool looked;
        private static bool found;
        private static ushort tickItemId;

        internal static bool IsTick(Item item)
        {
            if (item == null)
            {
                return false;
            }
            if (!looked)
            {
                looked = true;
                Bugfix bug = Resources.Load<GameObject>("BugfixOnYou")?.GetComponent<Bugfix>();
                if (bug != null && bug.bugItem != null)
                {
                    tickItemId = bug.bugItem.itemID;
                    found = true;
                }
                else
                {
                    Plugin.Log.LogWarning("Ascent 14: the tick item was not found; eaten ticks keep restoring hunger.");
                }
            }
            return found && item.itemID == tickItemId;
        }
    }

    /// <summary>Food that restores hunger through a status change: none from ticks either.</summary>
    [HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static class NoHungerFromTicks
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Action_ModifyStatus __instance)
        {
            return !(On && __instance.statusType == CharacterAfflictions.STATUSTYPE.Hunger && __instance.changeAmount < 0f && Ticks.IsTick(__instance.item));
        }
    }

    [HarmonyPatch(typeof(Action_GiveExtraStamina), nameof(Action_GiveExtraStamina.RunAction))]
    private static class HalfExtraStamina
    {
        private static bool Prefix(Action_GiveExtraStamina __instance)
        {
            if (!On)
            {
                return true;
            }
            __instance.character.AddExtraStamina(__instance.amount * 0.5f);
            return false;
        }
    }

    /// <summary>Healing and other status items work at 60% (big heals are capped at 0.7 first).</summary>
    [HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static class WeakerMedicine
    {
        private static void Prefix(Action_ModifyStatus __instance, out float? __state)
        {
            __state = null;
            if (On && __instance.statusType != CharacterAfflictions.STATUSTYPE.Petrify)
            {
                __state = __instance.changeAmount;
                __instance.changeAmount = (__instance.changeAmount > 1f ? 0.7f : __instance.changeAmount) * 0.6f;
            }
        }

        private static Exception Finalizer(Action_ModifyStatus __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.changeAmount = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>Cure-alls remove at most half a bar of each status.</summary>
    [HarmonyPatch(typeof(Action_ClearAllStatus), nameof(Action_ClearAllStatus.RunAction))]
    private static class PartialCureAll
    {
        private static bool Prefix(Action_ClearAllStatus __instance)
        {
            if (!On)
            {
                return true;
            }
            int count = Enum.GetNames(typeof(CharacterAfflictions.STATUSTYPE)).Length;
            for (int i = 0; i < count; i++)
            {
                CharacterAfflictions.STATUSTYPE type = (CharacterAfflictions.STATUSTYPE)i;
                if (__instance.defaultExclusions.Contains(type) || (__instance.excludeCurse && type == CharacterAfflictions.STATUSTYPE.Curse) || __instance.otherExclusions.Contains(type))
                {
                    continue;
                }
                __instance.character.refs.afflictions.SubtractStatus(type, i <= 11 ? 0.5f : 5f);
            }
            return false;
        }
    }

    /// <summary>Poisonous food poisons for twice as long and much harder.</summary>
    [HarmonyPatch(typeof(Action_InflictPoison), nameof(Action_InflictPoison.RunAction))]
    private static class WorsePoisonFood
    {
        private static void Prefix(Action_InflictPoison __instance)
        {
            if (On && __instance.character != null)
            {
                __instance.character.refs.afflictions.AddAffliction(new Affliction_PoisonOverTime(__instance.inflictionTime * 2f, __instance.delay * 3f, __instance.poisonPerSecond * 50f));
            }
        }
    }

    /// <summary>Cooking no longer removes poison from food.</summary>
    [HarmonyPatch(typeof(CookingBehavior_DisableScripts), nameof(CookingBehavior_DisableScripts.TriggerBehaviour))]
    private static class CookingKeepsPoison
    {
        private static void Postfix(CookingBehavior_DisableScripts __instance)
        {
            if (!On || __instance.scriptsToDisable == null)
            {
                return;
            }
            foreach (MonoBehaviour script in __instance.scriptsToDisable)
            {
                if (script != null)
                {
                    script.enabled = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Affliction_AdjustDrowsyOverTime), nameof(Affliction_AdjustDrowsyOverTime.UpdateEffect))]
    private static class SleepierLollipop
    {
        private static bool Prefix(Affliction_AdjustDrowsyOverTime __instance)
        {
            if (!On)
            {
                return true;
            }
            __instance.character.refs.afflictions.AdjustStatus(CharacterAfflictions.STATUSTYPE.Drowsy, __instance.statusPerSecond * Time.deltaTime * 3f);
            return false;
        }
    }

    [HarmonyPatch(typeof(Affliction_FasterBoi), nameof(Affliction_FasterBoi.OnApplied))]
    private static class SlowerEnergyDrink
    {
        private static void Prefix(Affliction_FasterBoi __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(Affliction_FasterBoi.moveSpeedMod), 0.6f);
                Overrides.Set(__instance, nameof(Affliction_FasterBoi.climbSpeedMod), 0.7f);
            }
        }
    }

    [HarmonyPatch(typeof(TiedBalloon), nameof(TiedBalloon.FixedUpdate))]
    private static class WeakerBalloon
    {
        private static void Prefix(TiedBalloon __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(TiedBalloon.floatForce), 5f);
            }
        }
    }

    [HarmonyPatch(typeof(VineShooter), nameof(VineShooter.Update))]
    private static class ShorterChain
    {
        private static void Prefix(VineShooter __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(VineShooter.maxLength), 30f);
            }
        }
    }

    [HarmonyPatch(typeof(RopeShooter), nameof(RopeShooter.OnPrimaryFinishedCast))]
    private static class ShorterRope
    {
        private static void Prefix(RopeShooter __instance, out float? __state)
        {
            __state = null;
            if (On)
            {
                __state = __instance.length;
                __instance.length *= 0.75f;
            }
        }

        private static Exception Finalizer(RopeShooter __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.length = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>Items with several uses lose two uses per use.</summary>
    [HarmonyPatch(typeof(Action_ReduceUses), nameof(Action_ReduceUses.ReduceUsesRPC))]
    private static class DoubleUseCost
    {
        private static bool Prefix(Action_ReduceUses __instance)
        {
            if (!On)
            {
                return true;
            }
            Item item = __instance.item;
            OptionableIntItemData data = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (data.HasData && data.Value > 0)
            {
                if (data.Value >= 2)
                {
                    data.Value--;
                }
                data.Value--;
                if (item.totalUses > 0)
                {
                    item.SetUseRemainingPercentage((float)data.Value / item.totalUses);
                }
                Character character = __instance.character;
                if (data.Value == 0 && __instance.consumeOnFullyUsed && character != null && character.IsLocal && character.data.currentItem == item)
                {
                    item.StartCoroutine(item.ConsumeDelayed());
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Glider), nameof(Glider.FixedUpdate))]
    private static class HungryGlider
    {
        private static void Prefix(Glider __instance, out float? __state)
        {
            __state = null;
            if (On)
            {
                __state = __instance.stamUse;
                __instance.stamUse *= 1.5f;
            }
        }

        private static Exception Finalizer(Glider __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.stamUse = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>An open parasol pulls you down instead of slowing your fall.</summary>
    [HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.ApplyParasolDrag))]
    private static class ParasolScope
    {
        [ThreadStatic] internal static int Depth;

        private static void Prefix()
        {
            Depth++;
        }

        private static Exception Finalizer(Exception __exception)
        {
            Depth--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Bodypart), nameof(Bodypart.ParasolDrag))]
    private static class HeavyParasol
    {
        private static bool Prefix(Bodypart __instance)
        {
            if (!On || ParasolScope.Depth <= 0 || !Rules.Owns(__instance.character))
            {
                return true;
            }
            if (__instance.rig.isKinematic)
            {
                return false;
            }
            if (__instance.rig.linearVelocity.y < 0f)
            {
                __instance.rig.linearVelocity -= new Vector3(0f, 200f * Time.deltaTime, 0f);
            }
            return false;
        }
    }

    /// <summary>Letting go of a rescue hook makes you fall and drop everything.</summary>
    [HarmonyPatch(typeof(RescueHook), nameof(RescueHook.RPCA_LetGo))]
    private static class RoughRescueHook
    {
        private static void Prefix(RescueHook __instance)
        {
            if (!On)
            {
                return;
            }
            Character victim = null;
            if (Rules.Owns(__instance.targetPlayer))
            {
                victim = __instance.targetPlayer;
            }
            else if (__instance.targetPlayer == null && Rules.Owns(__instance.playerHoldingItem))
            {
                victim = __instance.playerHoldingItem;
            }
            if (victim != null)
            {
                victim.Fall(2f);
                victim.refs.items.DropAllItems(includeBackpack: true);
            }
        }
    }

    private sealed class CannonShot
    {
        internal readonly HashSet<int> launched = new HashSet<int>();
        internal bool punished;
    }

    private static readonly ConditionalWeakTable<ScoutCannon, CannonShot> shots = new ConditionalWeakTable<ScoutCannon, CannonShot>();

    [HarmonyPatch(typeof(ScoutCannon), nameof(ScoutCannon.RPCA_Light))]
    private static class CannonLit
    {
        private static void Prefix(ScoutCannon __instance)
        {
            CannonShot shot = shots.GetOrCreateValue(__instance);
            shot.launched.Clear();
            shot.punished = false;
        }
    }

    /// <summary>Being shot out of the cannon burns (+50 heat).</summary>
    [HarmonyPatch(typeof(ScoutCannon), nameof(ScoutCannon.RPCA_LaunchTarget))]
    private static class HotCannon
    {
        private static void Postfix(ScoutCannon __instance, int targetID)
        {
            if (!On)
            {
                return;
            }
            PhotonView view = PhotonView.Find(targetID);
            Character character = view != null ? view.GetComponent<Character>() : null;
            if (character != null && shots.GetOrCreateValue(__instance).launched.Add(targetID) && Rules.Owns(character))
            {
                character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, 0.5f);
            }
        }
    }

    /// <summary>The "cannon glitch" (an item launched in the same shot as a scout) knocks every scout out.</summary>
    [HarmonyPatch(typeof(ScoutCannon), nameof(ScoutCannon.RPCA_LaunchItem))]
    private static class CannonGlitch
    {
        private static void Postfix(ScoutCannon __instance, int targetID)
        {
            if (!On)
            {
                return;
            }
            CannonShot shot = shots.GetOrCreateValue(__instance);
            PhotonView view = PhotonView.Find(targetID);
            if (shot.punished || shot.launched.Count == 0 || view == null || view.GetComponent<Item>() == null)
            {
                return;
            }
            shot.punished = true;
            Character local = Character.localCharacter;
            if (local != null && !local.data.dead)
            {
                local.refs.afflictions.SetStatus(CharacterAfflictions.STATUSTYPE.Hot, 0f);
                local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Crab, 200f);
                local.PassOutInstantly();
            }
        }
    }

    /// <summary>Reviving keeps 25% of the cleared statuses (host option "Nerfed Revives").</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.ClearAllStatus))]
    private static class ClearLeavesResidue
    {
        private static void Prefix(CharacterAfflictions __instance, bool excludeCurse, bool excludePetrify, out float[] __state)
        {
            __state = null;
            if (!On || !HostOptions.NerfedRevives || CheckpointScope.Depth > 0 || !Rules.Owns(__instance.character))
            {
                return;
            }
            __state = new float[12];
            for (int i = 0; i < __state.Length; i++)
            {
                CharacterAfflictions.STATUSTYPE type = (CharacterAfflictions.STATUSTYPE)i;
                if (__instance.StatusIsCurable(type, !excludeCurse, !excludePetrify))
                {
                    __state[i] = __instance.GetCurrentStatus(type) * 0.25f;
                }
            }
        }

        private static void Postfix(CharacterAfflictions __instance, float[] __state)
        {
            if (__state == null)
            {
                return;
            }
            bool changed = false;
            for (int i = 0; i < __state.Length; i++)
            {
                if (__state[i] > 0f)
                {
                    __instance.SetStatus((CharacterAfflictions.STATUSTYPE)i, __state[i], pushStatus: false);
                    changed = true;
                }
            }
            if (changed)
            {
                __instance.PushStatuses();
            }
        }
    }

    /// <summary>A checkpoint flag restores its saved statuses exactly; no residue there.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.TryCheckpoint))]
    private static class CheckpointScope
    {
        [ThreadStatic] internal static int Depth;

        private static void Prefix()
        {
            Depth++;
        }

        private static Exception Finalizer(Exception __exception)
        {
            Depth--;
            return __exception;
        }
    }

    /// <summary>Revives that would give no status give curse and hunger instead (host option "Nerfed Revives").</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.ReviveCharacter))]
    private static class RevivalPenalty
    {
        private static void Prefix(Character __instance, out bool __state)
        {
            __state = On && Rules.Owns(__instance) && (__instance.data.dead || __instance.data.passedOut || __instance.data.fullyPassedOut);
        }

        private static void Postfix(Character __instance, bool applyStatus, bool __state)
        {
            if (__state && !applyStatus && HostOptions.NerfedRevives)
            {
                // From ascent 19 every revive already curses for 15.
                if (!Rules.Enabled(Lvl.NoReviveStatues))
                {
                    __instance.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, 0.1f);
                }
                __instance.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hunger, 0.2f);
            }
        }
    }

    /// <summary>
    /// Skeletons: statuses that normally skip skeletons apply at 25%, injuries outside the fog are
    /// lethal (x99). Invincibility only blocks 75% of a status (curse still goes through fully).
    /// </summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    internal static class FragileSkeletonsAndInvincibility
    {
        [ThreadStatic] private static int skeletonScope;

        internal static bool InSkeletonScope => skeletonScope > 0;

        private static void Prefix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount, ref bool ignoreInvincibility, out bool __state)
        {
            __state = false;
            if (!On || StatusReplication.Active || !Rules.Owns(__instance.character) || statusType > CharacterAfflictions.STATUSTYPE.Web || amount <= 0f)
            {
                return;
            }
            Character character = __instance.character;
            if (character.data.isSkeleton)
            {
                if (!__instance.StatusAffectsSkeleton(statusType))
                {
                    amount *= 0.25f;
                }
                if (statusType == CharacterAfflictions.STATUSTYPE.Injury && !character.data.isInFog)
                {
                    amount *= 99f;
                }
                skeletonScope++;
                __state = true;
            }
            if (!ignoreInvincibility && character.data.isInvincible && statusType != CharacterAfflictions.STATUSTYPE.Curse)
            {
                amount *= 0.25f;
                ignoreInvincibility = true;
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                skeletonScope--;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.StatusAffectsSkeleton))]
    private static class SkeletonFeelsEverything
    {
        private static void Postfix(CharacterAfflictions.STATUSTYPE type, ref bool __result)
        {
            if (FragileSkeletonsAndInvincibility.InSkeletonScope && type <= CharacterAfflictions.STATUSTYPE.Web)
            {
                __result = true;
            }
        }
    }
}
