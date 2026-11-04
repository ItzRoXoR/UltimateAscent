using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: the Looker. In the game it is a harmless easter egg: the map has 20 hiding spots, one
/// of which shows a Looker in 5% of runs; it peeks out now and then and leaves for good once the
/// host has watched it for 3 s or anyone comes within 5 m. Here:
/// <list type="bullet">
/// <item>Every spot shows a Looker with 70% (about 14 per map).</item>
/// <item>Looking into its eyes while it peeks out (your line of sight within 0.5 m of them,
/// within 40 m, its eyes in plain view: not even its rock in between) curses you for 2.5 a second
/// and, once that curse lands on your bar, gives you a lollipop's sugar rush for 5 s, then not
/// again until 10 s after it wore off. Looking at its rock, or anywhere else, costs nothing; so
/// does walking up to it.</item>
/// <item>Once the Lookers have given you 12.5 curse (5 s of eye contact, one Looker or several;
/// curse from anything else does not count), the one you are looking at leaves and sends the
/// Scoutmaster after you for 70 s (then he gives up); the count starts over. Walking within
/// 5 m still makes a Looker leave, without him.</item>
/// </list>
/// Every player watches with their own eyes and has their own count (the game only counted the host's).
/// </summary>
internal static class A17_Looker
{
    private static bool On => Rules.Enabled(Lvl.Hazards);

    private const float SpotChance = 0.7f;
    private const float CursePerSecond = 0.025f;
    private const float SugarRushSeconds = 5f;
    private const float SugarRushCooldown = 10f;
    private const float ScoutmasterSeconds = 70f;

    /// <summary>The curse from Lookers that sends the Scoutmaster: 5 s of eye contact.</summary>
    private const float SummoningCurse = CursePerSecond * 5f;

    /// <summary>Looking into its eyes: your line of sight passes this close to them, closer than 40 m; closer than 5 m it flees.</summary>
    private const float EyeReach = 0.5f;
    private const float LookRange = 40f;
    private const float FleeRange = 5f;

    private static float sugarRushReadyAt;
    private static Scoutmaster summoned;
    private static Character summonedFor;
    private static float releaseAt;

    /// <summary>The curse the Lookers (all of them) have put on this player's bar since the Scoutmaster last came for that.</summary>
    private static float cursed;

    /// <summary>Each Looker's eye cards (the two eyes on its face).</summary>
    private static readonly Dictionary<Looker, Renderer[]> eyeCards = new Dictionary<Looker, Renderer[]>();

    internal static void Reset()
    {
        sugarRushReadyAt = 0f;
        summoned = null;
        summonedFor = null;
        releaseAt = 0f;
        cursed = 0f;
        eyeCards.Clear();
    }

    /// <summary>The host decides which spots show a Looker, once, for everybody.</summary>
    [HarmonyPatch(typeof(Looker), nameof(Looker.ToggleLookers))]
    private static class MoreLookers
    {
        private static bool Prefix(Looker __instance)
        {
            if (!On)
            {
                return true;
            }
            int shown = 0;
            foreach (Looker looker in __instance.transform.parent.GetComponentsInChildren<Looker>())
            {
                if (Random.value < SpotChance)
                {
                    shown++;
                    continue;
                }
                looker.view.RPC("RPCA_DisableLooker", RpcTarget.AllBuffered);
            }
            Plugin.Debug($"Ascent 17: {shown} Looker(s) this run.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Looker), nameof(Looker.Update))]
    private static class Watching
    {
        private static bool Prefix(Looker __instance)
        {
            if (!On)
            {
                return true;
            }
            if (!PhotonNetwork.InRoom || MainCamera.instance == null || __instance.pivot == null)
            {
                return false;
            }
            if (!__instance.hasChecked)
            {
                if (PhotonNetwork.IsMasterClient && __instance.transform.GetSiblingIndex() == 0)
                {
                    __instance.ToggleLookers();
                }
                __instance.hasChecked = true;
            }
            Transform eye = MainCamera.instance.transform;
            __instance.pivot.LookAt(eye.position);
            if (__instance.neverReturn)
            {
                __instance.selfDestructCounter -= Time.deltaTime;
                if (__instance.selfDestructCounter <= 0f)
                {
                    Object.Destroy(__instance.pivot.gameObject);
                    Object.Destroy(__instance);
                }
                return false;
            }
            __instance.untilSwitch -= Time.deltaTime;
            float distance = Vector3.Distance(eye.position, __instance.pivot.position);
            if (Watched(__instance, eye))
            {
                // Watched, it ducks away sooner, as in the game.
                __instance.untilSwitch -= Time.deltaTime * 5f;
                __instance.hasLookedAtMeFor += Time.deltaTime;
                Stared(__instance);
            }
            if (__instance.view.IsMine && __instance.untilSwitch <= 0f)
            {
                __instance.isActive = !__instance.isActive;
                __instance.view.RPC("RPCA_Switch", RpcTarget.All, __instance.isActive);
            }
            if (!__instance.neverReturn && distance < FleeRange)
            {
                __instance.view.RPC("RPCA_CodeRed", RpcTarget.AllBuffered);
            }
            return false;
        }
    }

    /// <summary>
    /// Looking into its eyes: it peeks out (every player sees the same thing), its eyes are in plain view within 40 m
    /// (nothing in between, not even the rock it hides behind) and this player's line of sight passes within 0.5 m of them.
    /// </summary>
    private static bool Watched(Looker looker, Transform eye)
    {
        Character local = Character.localCharacter;
        if (local == null || local.data.dead || !local.data.fullyConscious || looker.guy == null || !looker.guy.activeInHierarchy ||
            looker.anim == null || !looker.anim.GetBool("IsActive") || !Eyes(looker, out Vector3 eyes))
        {
            return false;
        }
        Vector3 toEyes = eyes - eye.position;
        float ahead = Vector3.Dot(eye.forward, toEyes);
        if (ahead <= 0f || toEyes.magnitude >= LookRange || (toEyes - eye.forward * ahead).magnitude > EyeReach)
        {
            return false;
        }
        return HelperFunctions.LineCheck(eye.position, eyes, HelperFunctions.LayerType.TerrainMap).transform == null;
    }

    /// <summary>Between the Looker's eyes (the eye cards on its face).</summary>
    internal static bool Eyes(Looker looker, out Vector3 eyes)
    {
        if (!eyeCards.TryGetValue(looker, out Renderer[] cards))
        {
            cards = looker.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Eye Card", System.StringComparison.Ordinal)).ToArray();
            eyeCards[looker] = cards;
        }
        eyes = Vector3.zero;
        int found = 0;
        foreach (Renderer card in cards)
        {
            if (card != null)
            {
                eyes += card.bounds.center;
                found++;
            }
        }
        eyes = found > 0 ? eyes / found : eyes;
        return found > 0;
    }

    private static void Stared(Looker looker)
    {
        CharacterAfflictions afflictions = Character.localCharacter.refs.afflictions;
        float landed = ExactStatus.Add(afflictions, CharacterAfflictions.STATUSTYPE.Curse, CursePerSecond * Time.deltaTime, "looker");
        cursed += landed;
        // The sugar rush comes with the curse: once it lands on the bar.
        if (landed > 0f && Time.time >= sugarRushReadyAt)
        {
            afflictions.AddAffliction(new Affliction_InfiniteStamina(SugarRushSeconds));
            sugarRushReadyAt = Time.time + SugarRushSeconds + SugarRushCooldown;
        }
        if (cursed >= SummoningCurse - 0.0001f)
        {
            cursed = 0f;
            looker.view.RPC("RPCA_CodeRed", RpcTarget.AllBuffered);
            Summon(Character.localCharacter);
        }
    }

    private static void Summon(Character starer)
    {
        if (!Scoutmaster.GetPrimaryScoutmaster(out Scoutmaster scoutmaster) || scoutmaster == null)
        {
            return;
        }
        if (scoutmaster.targetForced && scoutmaster.currentTarget != starer)
        {
            Plugin.Debug("Ascent 17: the Scoutmaster is busy with someone else; the Looker's call goes unanswered.");
            return;
        }
        scoutmaster.SetCurrentTarget(starer, ScoutmasterSeconds);
        summoned = scoutmaster;
        summonedFor = starer;
        releaseAt = Time.time + ScoutmasterSeconds;
        Plugin.Log.LogInfo($"Ascent 17: {starer.characterName} has had 12.5 curse from the Lookers; the Scoutmaster comes for 70 s.");
    }

    /// <summary>Called every frame: after 70 s the Scoutmaster gives up on the scout the Looker sent him after.</summary>
    internal static void Tick()
    {
        if (summoned == null || Time.time < releaseAt)
        {
            return;
        }
        if (summoned.currentTarget == summonedFor && !summoned.targetForced)
        {
            summoned.SetCurrentTarget(null);
        }
        summoned = null;
        summonedFor = null;
    }
}
