using System.Collections.Generic;
using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Game bugs, fixed from ascent 9 on.
/// <list type="bullet">
/// <item>A fire that is lit from the start (the Portable Stove) gave the scouts present when it was
/// placed no Gloom protection while it burned, and a permanent 7 m safe zone once it burned out: its
/// <see cref="Campfire.Awake"/> sets the fire lit before it finds its safe zone, and
/// <see cref="Campfire.Extinguish_Rpc"/> updates the zone while the fire still counts as lit. Late
/// joiners already got it right. Both now bring the safe zone in line with the fire.</item>
/// <item>A sugar rush without a crash (the Looker's, a Shroomberry's, the Bugle of Friendship's)
/// arriving during a lollipop's threw an error: the game adds the newcomer's crash time to the running
/// rush's without checking that it has one. It now adds nothing.</item>
/// <item>An arrow trap fired at the Citadel's swinging spike balls: it shoots at any loose body crossing its
/// line, and a spike ball swinging through it is one. A spike ball crossing it no longer sets it off, on every
/// ascent.</item>
/// <item>An arrow trap set inside something solid (a platform the level put over it) shot through it: its look
/// along its line starts inside, and a look that starts inside something does not see it. A trap whose line
/// starts inside anything but its own housing now holds its fire and shows no warning line, on every ascent
/// (<see cref="Bore"/>).</item>
/// <item>A campfire save put back the petrification the fire had taken when it was loaded, on every ascent
/// (<see cref="CampfireSavePetrify"/>).</item>
/// </list>
/// </summary>
internal static class GameFixes
{
    private static bool On => Rules.InExtraAscent;

    /// <summary>The trap's own look along its line (what it would shoot at): a swinging spike ball first is left alone.</summary>
    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.FixedUpdate))]
    private static class ArrowsIgnoreSpikeBalls
    {
        private static bool Prefix(ArrowShooter __instance)
        {
            if (!(Plugin.Ready && Rules.InRun) || __instance.empty || __instance.reloading
                || !UnityEngine.Physics.Raycast(__instance.pos, __instance.forward, out UnityEngine.RaycastHit hit, __instance.range, HelperFunctions.AllPhysical, UnityEngine.QueryTriggerInteraction.Ignore))
            {
                return true;
            }
            return hit.rigidbody == null || hit.rigidbody.GetComponentInParent<SwingingAxe>() == null;
        }
    }

    /// <summary>How far past the trap's muzzle something else may begin and still count as the wall it is set in.</summary>
    private const float SetInWall = 0.05f;

    /// <summary>How often a trap's line is looked along again.</summary>
    private const float BoreSeconds = 1f;

    /// <summary>Each arrow trap's line as last looked along: until when it holds, its mouth, and whether it starts inside something.</summary>
    private static readonly Dictionary<ArrowShooter, (float until, float mouth, bool buried)> bores = new Dictionary<ArrowShooter, (float, float, bool)>();

    /// <summary>The traps whose warning line this fix hid.</summary>
    private static readonly HashSet<ArrowShooter> hidden = new HashSet<ArrowShooter>();

    /// <summary>For the tests: buried traps shoot, as in the game.</summary>
    internal static bool LetBuriedTrapsShoot;

    /// <summary>
    /// Along an arrow trap's line, looked at once a second: how far its mouth is from the muzzle (where the line leaves
    /// the trap's own housing), and whether the line starts inside something else. A look that starts inside something
    /// does not see it, so the line is looked along from where it first meets the map, back to the muzzle: every face it
    /// leaves before that belongs to something the muzzle is inside.
    /// </summary>
    internal static (float mouth, bool buried) Bore(ArrowShooter trap)
    {
        if (bores.TryGetValue(trap, out (float until, float mouth, bool buried) known) && Time.time < known.until)
        {
            return (known.mouth, known.buried);
        }
        float entry = Physics.Raycast(trap.pos, trap.forward, out RaycastHit ahead, trap.range, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore) ? ahead.distance : trap.range;
        float mouth = 0f;
        bool buried = false;
        foreach (RaycastHit back in Physics.RaycastAll(trap.pos + trap.forward * entry, -trap.forward, entry, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            float along = entry - back.distance;
            if (along <= 0f || along >= entry - 0.001f)
            {
                continue;
            }
            if (back.collider.GetComponentInParent<ArrowShooter>() == trap)
            {
                mouth = Mathf.Max(mouth, along);
            }
            else if (along > SetInWall)
            {
                buried = true;
            }
        }
        if (bores.Count > 256)
        {
            bores.Clear();
        }
        bores[trap] = (Time.time + BoreSeconds, mouth, buried);
        return (mouth, buried);
    }

    /// <summary>A trap whose line starts inside something holds its fire and hides its warning line (shown again once it does not).</summary>
    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.FixedUpdate))]
    private static class BuriedTrapsHold
    {
        private static bool Prefix(ArrowShooter __instance)
        {
            if (!(Plugin.Ready && Rules.InRun) || LetBuriedTrapsShoot)
            {
                return true;
            }
            if (Bore(__instance).buried)
            {
                if (__instance.lineRenderer != null)
                {
                    __instance.lineRenderer.enabled = false;
                    hidden.Add(__instance);
                }
                return false;
            }
            if (hidden.Remove(__instance) && __instance.lineRenderer != null)
            {
                __instance.lineRenderer.enabled = !__instance.empty;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Awake))]
    private static class StoveLitFromTheStart
    {
        private static void Postfix(Campfire __instance)
        {
            if (On)
            {
                __instance.UpdateSafeZone();
            }
        }
    }

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Extinguish_Rpc))]
    private static class StoveBurnedOut
    {
        private static void Postfix(Campfire __instance)
        {
            if (On)
            {
                __instance.UpdateSafeZone();
            }
        }
    }

    /// <summary>The newcomer is a copy made for this call, so giving it an empty crash changes nothing else.</summary>
    [HarmonyPatch(typeof(Affliction_InfiniteStamina), nameof(Affliction_InfiniteStamina.Stack))]
    private static class RushWithoutCrash
    {
        private static void Prefix(Affliction_InfiniteStamina __instance, Affliction incomingAffliction)
        {
            if (On && __instance.drowsyAffliction != null && incomingAffliction is Affliction_InfiniteStamina incoming && incoming.drowsyAffliction == null)
            {
                incoming.drowsyAffliction = new Affliction_AdjustDrowsyOverTime(0f, 0f);
            }
        }
    }

    /// <summary>A scout's campfire record, as it was before the game's save added the fire's effects to it.</summary>
    private sealed class BeforeTheFire
    {
        internal float[] statuses;
        internal float arrows;
        internal int petrify;
        internal float extraStamina;
    }

    /// <summary>
    /// The campfire save (PEAK 3.0.0) is written the moment a fire is lit, before its effects reach anyone, so the
    /// game adds them to each scout's saved state itself; but the petrification it takes off lands on the arrows
    /// stuck in you (the status next to it) and never on petrification, which is saved as a number of its own. A save
    /// loaded after a crash put back all the petrification the fire had taken. Now the save holds what lighting the
    /// fire takes off (20, from ascent 17 only 10, see <see cref="A17_CampfirePetrify"/>), and the arrows stay as
    /// they were, in the save and in the game's own record of the scout. Written into the save once, as the fire is
    /// lit, so loading it again and again always gives the same. On every ascent.
    /// </summary>
    [HarmonyPatch(typeof(Peak.Quicksave.PlayerRunData), nameof(Peak.Quicksave.PlayerRunData.TryCreateFromReconnectData))]
    private static class CampfireSavePetrify
    {
        private const int Arrows = (int)CharacterAfflictions.STATUSTYPE.Arrow;

        private static void Prefix(string playerId, bool applyCampfireEffect, out BeforeTheFire __state)
        {
            __state = null;
            if (applyCampfireEffect && ReconnectHandler.TryGetReconnectData(playerId, out ReconnectData record, out _)
                && record.currentStatuses != null && record.currentStatuses.Length > Arrows)
            {
                __state = new BeforeTheFire
                {
                    statuses = record.currentStatuses,
                    arrows = record.currentStatuses[Arrows],
                    petrify = record.petrify,
                    extraStamina = record.extraStamina,
                };
            }
        }

        private static void Postfix(bool __result, ref Peak.Quicksave.PlayerRunData runData, BeforeTheFire __state)
        {
            if (!__result || __state == null)
            {
                return;
            }
            // The game's change went into the record's own list too (the save works on the same list).
            __state.statuses[Arrows] = __state.arrows;
            ReconnectData saved = runData.GetReconnectData();
            if (saved.currentStatuses == null || saved.currentStatuses.Length <= Arrows)
            {
                return;
            }
            float healed = Rules.Enabled(Lvl.HotCampfires) ? A17_CampfirePetrify.PetrifyHealed : -Campfire.PETRIFY_HEAL;
            saved.currentStatuses[Arrows] = __state.arrows;
            saved.petrify = UnityEngine.Mathf.Max(0, __state.petrify - UnityEngine.Mathf.RoundToInt(healed * 100f));
            // The game's own extra stamina from the fire, capped by what petrification is left (as it caps it).
            saved.extraStamina = UnityEngine.Mathf.Clamp(__state.extraStamina + Campfire.MORALE_BOOST_STAM, 0f, 1f - 0.01f * saved.petrify);
            runData.reconnectData = CustomTypeRPCSerialization.SerializeReconnectData(saved);
        }
    }
}
