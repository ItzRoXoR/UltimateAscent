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
/// <item>Every spot shows a Looker with 15% (about 3 per map).</item>
/// <item>Watching one while it peeks out (in the middle of your view, within 40 m, nothing in
/// between) curses you for 5 a second and gives you a lollipop's sugar rush for 5 s, then not
/// again until 10 s after it wore off. Only looking counts: walking up to it without looking
/// costs nothing.</item>
/// <item>After 5 s of watching in all, it leaves and sends the Scoutmaster after the scout who
/// stared, for 70 s; then he gives up. Walking within 5 m still makes it leave, without him.</item>
/// </list>
/// Every player watches with their own eyes (the game only counted the host's).
/// </summary>
internal static class A17_Looker
{
    private static bool On => Rules.Enabled(Lvl.Hazards);

    private const float SpotChance = 0.15f;
    private const float CursePerSecond = 0.05f;
    private const float SugarRushSeconds = 5f;
    private const float SugarRushCooldown = 10f;
    private const float StareSeconds = 5f;
    private const float ScoutmasterSeconds = 70f;

    /// <summary>The game's own "looking at it": within ~37 degrees of the middle of the view, closer than 40 m; closer than 5 m it flees.</summary>
    private const float LookDot = 0.8f;
    private const float LookRange = 40f;
    private const float FleeRange = 5f;

    private static float sugarRushReadyAt;
    private static Scoutmaster summoned;
    private static Character summonedFor;
    private static float releaseAt;

    internal static void Reset()
    {
        sugarRushReadyAt = 0f;
        summoned = null;
        summonedFor = null;
        releaseAt = 0f;
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
            if (Watched(__instance, eye, distance))
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

    /// <summary>Peeking out (every player sees the same thing), in the middle of this player's view, in range, nothing in between.</summary>
    private static bool Watched(Looker looker, Transform eye, float distance)
    {
        Character local = Character.localCharacter;
        if (local == null || local.data.dead || !local.data.fullyConscious || looker.guy == null || !looker.guy.activeInHierarchy ||
            looker.anim == null || !looker.anim.GetBool("IsActive") || distance >= LookRange)
        {
            return false;
        }
        Vector3 toward = (looker.pivot.position - eye.position).normalized;
        if (Vector3.Dot(eye.forward, toward) <= LookDot)
        {
            return false;
        }
        RaycastHit hit = HelperFunctions.LineCheck(eye.position, looker.pivot.position - toward * 0.5f, HelperFunctions.LayerType.TerrainMap);
        return hit.transform == null;
    }

    private static void Stared(Looker looker)
    {
        CharacterAfflictions afflictions = Character.localCharacter.refs.afflictions;
        ExactStatus.Add(afflictions, CharacterAfflictions.STATUSTYPE.Curse, CursePerSecond * Time.deltaTime, "looker");
        if (Time.time >= sugarRushReadyAt)
        {
            afflictions.AddAffliction(new Affliction_InfiniteStamina(SugarRushSeconds));
            sugarRushReadyAt = Time.time + SugarRushSeconds + SugarRushCooldown;
        }
        if (looker.hasLookedAtMeFor >= StareSeconds)
        {
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
        Plugin.Log.LogInfo($"Ascent 17: {starer.characterName} stared at the Looker too long; the Scoutmaster comes for 70 s.");
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
