using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;
using MovingSawBlade = global::Peak.MovingSawBlade;
using SpikeRoller = global::Peak.SpikeRoller;
using SpikeTrap = global::Peak.SpikeTrap;
using TrickLuggage = global::Peak.TrickLuggage;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: the Citadel's traps and chests, and the Alpine's napberries.
/// <list type="bullet">
/// <item>Wall saws hit for 30 on the first touch (the game: 15); the Central Tower's saw for 7.5 a
/// second (the game: nothing, its damage is left empty). Spike rollers hit for 10 (the game: 5),
/// swinging spike balls for up to 60 by their speed (the game: 25). Arrows and spiked fences: see
/// <see cref="A17_Hazards"/>.</item>
/// <item>The traps move 1.5 times as fast: the spike rollers (the Central Tower's ring saws among them) sweep
/// round and turn on their axles, the spike balls swing and turn their heads, and the saws run along their walls,
/// their blades wobbling 1.5 times as fast too. Every scout's game moves them by the run's shared clock, so they
/// stay in step. The pressure plates' spikes come up 1.5 times as soon (0.53 s after the
/// step; the game: 0.8 s).</item>
/// <item>Ladders help less: climbing one is 1.2 times as fast as climbing rock (the game: 3 times) and costs 90%
/// of its stamina (the game: 25%).</item>
/// <item>Mimic chests release gas that puts 5 drowsiness, 5 poison and 5 spores on you every 0.5 s
/// (the game: 5 drowsiness), and look like ordinary chests (the game gives its mimics a chest of their own). A
/// 35% share of the ordinary chests in the Citadel are mimics too, picked anew for each climb (the same ones for
/// every player): they release the same gas when opened, and keep their loot like the game's own mimics.</item>
/// <item>Napberries pulse once a second (the game: twice every 3 s).</item>
/// <item>An arrow trap's green warning line shows only its first half metre out of the opening (the game: all
/// the way to where the arrow would land).</item>
/// </list>
/// </summary>
internal static class A17_Traps
{
    private static bool On => Rules.Enabled(Lvl.Hazards);

    private const float SawFirstHit = 0.3f;
    private const float TowerSawHit = 0.075f;
    private const float RollerInjury = 0.1f;
    private const float SpikeBallInjury = 0.6f;

    /// <summary>How much faster the spike rollers and the swinging spike balls move.</summary>
    internal const float TrapSpeed = 1.5f;

    /// <summary>A ladder's climbing speed and stamina, against climbing rock (the game's: 3 and 0.25).</summary>
    internal const float LadderSpeed = 1.2f;
    internal const float LadderStamina = 0.9f;

    /// <summary>The game counts a climbing surface this cheap as a ladder (its ladder crosshair).</summary>
    internal const float GameLadderStamina = 0.34f;

    /// <summary>The share of the Citadel's ordinary chests that are mimics.</summary>
    internal const float MimicShare = 0.35f;

    /// <summary>What the mimic gas puts on the bar of each status every tick (0.5 s).</summary>
    private const float GasTick = 0.05f;

    /// <summary>The napberry animation holds two pulses in a 3 s loop; played 1.5x as fast, that is one a second.</summary>
    private const float NapberryPace = 1.5f;

    /// <summary>How long a mimic's gas object stays around after it is released (its gas lasts 10 s).</summary>
    private const float GasCleanupSeconds = 20f;

    [HarmonyPatch(typeof(MovingSawBlade), nameof(MovingSawBlade.OnTriggerStay))]
    private static class Saws
    {
        private static void Prefix(MovingSawBlade __instance)
        {
            if (!On)
            {
                return;
            }
            float first = Overrides.Original(__instance, "initial", __instance.initialInjury);
            if (first > 0f)
            {
                Overrides.Set(__instance, nameof(MovingSawBlade.initialInjury), SawFirstHit);
            }
            else if (Overrides.Original(__instance, "cooldown", __instance.initialInjuryCooldown) <= 0f)
            {
                // The Central Tower's saw: every hit counts as a first hit, once per its 1 s timeout.
                Overrides.Set(__instance, nameof(MovingSawBlade.initialInjury), TowerSawHit * Overrides.Original(__instance, "timeout", __instance.timeout));
            }
        }
    }

    /// <summary>
    /// A spike roller's sweep, as the game sets it from the run's clock, 1.5 times as fast, and its rollers turn on
    /// their axles 1.5 times as fast too.
    /// </summary>
    [HarmonyPatch(typeof(SpikeRoller), nameof(SpikeRoller.UpdateSpin))]
    private static class FasterRollers
    {
        private static bool Prefix(SpikeRoller __instance)
        {
            if (!On || RunManager.Instance == null)
            {
                return true;
            }
            float time = RunManager.Instance.TimeSinceRunStarted;
            __instance.rb.MoveRotation(Quaternion.Euler(0f, time * TrapSpeed * __instance.spinnerSpeed % 360f, 0f));
            foreach (Transform roller in __instance.roller)
            {
                roller.localEulerAngles = new Vector3(time * TrapSpeed * __instance._finalRollerSpeed % 360f, 0f, 0f);
            }
            return false;
        }
    }

    /// <summary>
    /// A swinging spike ball swings and turns its head 1.5 times as fast (also where it works out its swing again
    /// after bumping into rock).
    /// </summary>
    [HarmonyPatch]
    private static class FasterSpikeBalls
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SwingingAxe), nameof(SwingingAxe.SetAxeRotation));
            yield return AccessTools.Method(typeof(SwingingAxe), nameof(SwingingAxe.OnCollisionEnter));
        }

        private static void Prefix(SwingingAxe __instance, out float[] __state)
        {
            __state = null;
            if (On)
            {
                __state = new[] { __instance.swingSpeed, __instance.spikeHeadRotateSpeed };
                __instance.swingSpeed *= TrapSpeed;
                __instance.spikeHeadRotateSpeed *= TrapSpeed;
            }
        }

        private static Exception Finalizer(SwingingAxe __instance, float[] __state, Exception __exception)
        {
            if (__state != null)
            {
                __instance.swingSpeed = __state[0];
                __instance.spikeHeadRotateSpeed = __state[1];
            }
            return __exception;
        }
    }

    /// <summary>A ladder's speed and stamina while the climbing works them out (the crosshair still shows a ladder).</summary>
    [HarmonyPatch]
    private static class WeakerLadders
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CharacterClimbing), nameof(CharacterClimbing.Update));
            yield return AccessTools.Method(typeof(CharacterClimbing), nameof(CharacterClimbing.FixedUpdate));
        }

        private static void Prefix(CharacterClimbing __instance, out LadderValues __state)
        {
            __state = null;
            ClimbModifierSurface ladder = __instance.character != null ? __instance.character.data.climbMod : null;
            if (!On || ladder == null || ladder.staminaUsageMultiplier > GameLadderStamina)
            {
                return;
            }
            __state = new LadderValues { ladder = ladder, speed = ladder.speedMultiplier, stamina = ladder.staminaUsageMultiplier };
            ladder.speedMultiplier = LadderSpeed;
            ladder.staminaUsageMultiplier = LadderStamina;
        }

        private static Exception Finalizer(LadderValues __state, Exception __exception)
        {
            // The same ladder, even if the scout moved on to another surface meanwhile.
            if (__state != null && __state.ladder != null)
            {
                __state.ladder.speedMultiplier = __state.speed;
                __state.ladder.staminaUsageMultiplier = __state.stamina;
            }
            return __exception;
        }
    }

    private sealed class LadderValues
    {
        internal ClimbModifierSurface ladder;
        internal float speed;
        internal float stamina;
    }

    /// <summary>
    /// A saw runs along its wall 1.5 times as fast (its back-and-forth by the run's clock), and its blade wobbles 1.5
    /// times as fast too (the game's only turning of the blade).
    /// </summary>
    [HarmonyPatch(typeof(MovingSawBlade), nameof(MovingSawBlade.Update))]
    private static class FasterSaws
    {
        private static void Prefix(MovingSawBlade __instance)
        {
            if (!On)
            {
                return;
            }
            Overrides.Set(__instance, nameof(MovingSawBlade.pathLength), Overrides.Original(__instance, "pathLength", __instance.pathLength) * TrapSpeed);
            Overrides.Set(__instance, nameof(MovingSawBlade.sawOscillateSpeed), Overrides.Original(__instance, "oscillate", __instance.sawOscillateSpeed) * TrapSpeed);
        }
    }

    /// <summary>A pressure plate's spikes come up 1.5 times as soon after the step (every scout's game runs the plate's own timing).</summary>
    [HarmonyPatch(typeof(SpikeTrap), nameof(SpikeTrap.SpikeTrapRPC))]
    private static class QuickerPlates
    {
        private static void Prefix(SpikeTrap __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(SpikeTrap.spikeWaitTime), Overrides.Original(__instance, "wait", __instance.spikeWaitTime) / TrapSpeed);
                Overrides.Set(__instance, nameof(SpikeTrap.readyTweenTime), Overrides.Original(__instance, "ready", __instance.readyTweenTime) / TrapSpeed);
            }
        }
    }

    [HarmonyPatch(typeof(SpikeRoller), nameof(SpikeRoller.OnCollisionEnter))]
    private static class Rollers
    {
        private static void Prefix(SpikeRoller __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(SpikeRoller.injury), RollerInjury);
            }
        }
    }

    [HarmonyPatch(typeof(SwingingAxe), nameof(SwingingAxe.OnCollisionEnter))]
    private static class SpikeBalls
    {
        private static void Prefix(SwingingAxe __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(SwingingAxe.maxInjury), SpikeBallInjury);
            }
        }
    }

    private sealed class GasFlag
    {
        internal bool gas;
    }

    private static readonly ConditionalWeakTable<StatusField, GasFlag> gasFields = new ConditionalWeakTable<StatusField, GasFlag>();

    private static bool IsMimicGas(StatusField field)
    {
        if (!gasFields.TryGetValue(field, out GasFlag flag))
        {
            flag = new GasFlag { gas = field.GetComponent<TrickLuggage>() != null };
            gasFields.Add(field, flag);
        }
        return flag.gas;
    }

    /// <summary>Mimic gas: 5 drowsiness, 5 poison and 5 spores every tick, exactly.</summary>
    [HarmonyPatch(typeof(StatusField), nameof(StatusField.TickStatus))]
    private static class MimicGas
    {
        private static bool Prefix(StatusField __instance)
        {
            if (!On || !__instance.tickBased || Character.localCharacter == null || !IsMimicGas(__instance))
            {
                return true;
            }
            __instance.tick += Time.deltaTime;
            if (__instance.tick >= __instance.timeBetweenTicks)
            {
                __instance.tick = 0f;
                CharacterAfflictions afflictions = Character.localCharacter.refs.afflictions;
                afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Drowsy, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Drowsy, GasTick));
                afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Poison, GasTick));
                afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Spores, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Spores, GasTick));
            }
            return false;
        }
    }

    /// <summary>
    /// An ordinary chest in the Citadel (not a statue, a clown chest or one of the game's own mimics).
    /// The Citadel shares the Gloom's biome, so its chests are found by the Citadel's segment.
    /// </summary>
    internal static bool IsCitadelChest(Luggage luggage)
    {
        if (luggage == null || luggage is RespawnChest || luggage.CompareTag("ClownLuggage"))
        {
            return false;
        }
        if (luggage.activateOnOpen != null && luggage.activateOnOpen.GetComponent<TrickLuggage>() != null)
        {
            return false;
        }
        return Map.InCitadelSegment(luggage);
    }

    /// <summary>
    /// One of the 35% of chests that are mimics in this climb: by its network id and the climb's own id, which every
    /// player agrees on (a campfire save keeps the climb's id).
    /// </summary>
    internal static bool IsMimicChest(Luggage luggage)
    {
        if (luggage.photonView == null)
        {
            return false;
        }
        uint hash = 2166136261;
        foreach (char c in (Net.RunKey ?? "") + "/" + luggage.photonView.ViewID)
        {
            hash = (hash ^ c) * 16777619;
        }
        return hash % 1000 < MimicShare * 1000f;
    }

    /// <summary>The game's own mimic (a chest of its own, with the gas inside).</summary>
    internal static bool IsGameMimic(Luggage luggage) => luggage != null && luggage.activateOnOpen != null && luggage.activateOnOpen.GetComponent<TrickLuggage>() != null;

    private const string PlainChest = "LuggageSmall";
    private const string BodyName = "luggage";
    private const string LidName = "Lid";
    private const string MimicLidMesh = "Mesh";

    private static Mesh plainBody;
    private static Material[] plainBodyMaterials;
    private static Mesh plainLid;
    private static Material[] plainLidMaterials;

    /// <summary>The ordinary small chest's body and lid (the game's mimic chest is that chest with a face).</summary>
    private static bool FindPlainChest()
    {
        if (plainBody != null && plainLid != null)
        {
            return true;
        }
        foreach (Luggage chest in Resources.FindObjectsOfTypeAll<Luggage>())
        {
            if (chest == null || chest.name != PlainChest || IsGameMimic(chest))
            {
                continue;
            }
            Transform body = chest.transform.Find(BodyName);
            Transform lid = body != null ? body.Find(LidName) : null;
            MeshFilter bodyMesh = body != null ? body.GetComponent<MeshFilter>() : null;
            MeshFilter lidMesh = lid != null ? lid.GetComponent<MeshFilter>() : null;
            if (bodyMesh == null || lidMesh == null || bodyMesh.sharedMesh == null || lidMesh.sharedMesh == null)
            {
                continue;
            }
            plainBody = bodyMesh.sharedMesh;
            plainBodyMaterials = body.GetComponent<MeshRenderer>().sharedMaterials;
            plainLid = lidMesh.sharedMesh;
            plainLidMaterials = lid.GetComponent<MeshRenderer>().sharedMaterials;
            return true;
        }
        return false;
    }

    private static readonly System.Collections.Generic.HashSet<Luggage> disguised = new System.Collections.Generic.HashSet<Luggage>();
    private static float nextDisguise;

    internal static void Reset()
    {
        disguised.Clear();
        nextDisguise = 0f;
        // The arrow traps are kept: the level's traps wake before a climb starts; gone ones drop out in the tick.
    }

    /// <summary>How far an arrow trap's warning line reaches out of its opening.</summary>
    internal const float WarningLineReach = 0.5f;

    /// <summary>The arrow traps in the level, and whether their warning line is cut short now.</summary>
    private static readonly System.Collections.Generic.Dictionary<ArrowShooter, bool> arrowTraps = new System.Collections.Generic.Dictionary<ArrowShooter, bool>();

    /// <summary>
    /// The game draws the warning line from the opening to where the arrow would land (when the trap wakes, and again
    /// after it reloads); the line is cut short every couple of seconds (<see cref="Tick"/>), so a line the game draws
    /// again is cut too. Its drawing is inlined into the game's own methods, so it cannot be followed there.
    /// </summary>
    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.OnEnable))]
    private static class KnownArrowTraps
    {
        private static void Postfix(ArrowShooter __instance)
        {
            if (!arrowTraps.ContainsKey(__instance))
            {
                arrowTraps[__instance] = false;
            }
        }
    }

    private static bool Shorten(LineRenderer line)
    {
        if (line == null || line.positionCount < 2)
        {
            return false;
        }
        Vector3 start = line.useWorldSpace ? line.GetPosition(0) : line.transform.TransformPoint(line.GetPosition(0));
        Vector3 end = line.useWorldSpace ? line.GetPosition(1) : line.transform.TransformPoint(line.GetPosition(1));
        Vector3 along = end - start;
        if (along.magnitude > WarningLineReach)
        {
            end = start + along.normalized * WarningLineReach;
            line.SetPosition(1, line.useWorldSpace ? end : line.transform.InverseTransformPoint(end));
        }
        return true;
    }

    /// <summary>The game's mimic chests look like the ordinary small chest (each player's game dresses its own, once the level is there).</summary>
    internal static void Tick()
    {
        if (!Map.Ready || Time.time < nextDisguise)
        {
            return;
        }
        nextDisguise = Time.time + 2f;
        // The warning lines: cut short while the rule is on; drawn whole again (the game's way) when it is switched off.
        bool on = On;
        foreach (System.Collections.Generic.KeyValuePair<ArrowShooter, bool> trap in new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ArrowShooter, bool>>(arrowTraps))
        {
            if (trap.Key == null)
            {
                arrowTraps.Remove(trap.Key);
                continue;
            }
            if (on)
            {
                arrowTraps[trap.Key] = Shorten(trap.Key.lineRenderer);
            }
            else if (trap.Value)
            {
                trap.Key.DrawWarningLine();
                arrowTraps[trap.Key] = false;
            }
        }
        if (!On)
        {
            return;
        }
        foreach (Luggage luggage in Luggage.ALL_LUGGAGE)
        {
            if (luggage != null && !disguised.Contains(luggage) && IsGameMimic(luggage) && Disguise(luggage))
            {
                disguised.Add(luggage);
            }
        }
    }

    internal static bool Disguise(Luggage mimic)
    {
        Transform body = mimic.transform.Find(BodyName);
        Transform lid = body != null ? body.Find(LidName + "/" + MimicLidMesh) : null;
        MeshFilter bodyMesh = body != null ? body.GetComponent<MeshFilter>() : null;
        MeshFilter lidMesh = lid != null ? lid.GetComponent<MeshFilter>() : null;
        if (bodyMesh == null || lidMesh == null || !FindPlainChest())
        {
            Plugin.Log.LogWarning("Ascent 17: a mimic chest could not be made to look ordinary (its parts or the ordinary chest were not found).");
            return false;
        }
        bodyMesh.sharedMesh = plainBody;
        body.GetComponent<MeshRenderer>().sharedMaterials = plainBodyMaterials;
        lidMesh.sharedMesh = plainLid;
        lid.GetComponent<MeshRenderer>().sharedMaterials = plainLidMaterials;
        // The ordinary chest's lid mesh sits on the lid itself.
        lid.localPosition = Vector3.zero;
        lid.localRotation = Quaternion.identity;
        lid.localScale = Vector3.one;
        return true;
    }

    private static GameObject gasTemplate;

    private static GameObject GasTemplate()
    {
        if (gasTemplate != null)
        {
            return gasTemplate;
        }
        foreach (TrickLuggage trick in Resources.FindObjectsOfTypeAll<TrickLuggage>())
        {
            if (trick != null && trick.statusField != null)
            {
                return gasTemplate = trick.gameObject;
            }
        }
        return null;
    }

    /// <summary>Opening runs on every client, so each one releases the gas for its own scout.</summary>
    [HarmonyPatch(typeof(Luggage), nameof(Luggage.OpenLuggageRPC))]
    private static class CitadelMimics
    {
        private static void Prefix(Luggage __instance, out bool __state)
        {
            __state = __instance.state != Luggage.LuggageState.Open;
        }

        private static void Postfix(Luggage __instance, bool spawnItems, bool __state)
        {
            if (!On || !__state || !spawnItems || !IsCitadelChest(__instance) || !IsMimicChest(__instance))
            {
                return;
            }
            GameObject template = GasTemplate();
            if (template == null)
            {
                Plugin.Log.LogWarning("Ascent 17: no mimic gas found to release from a Citadel chest.");
                return;
            }
            GameObject gas = UnityEngine.Object.Instantiate(template, __instance.transform.position + Vector3.up * 0.5f, __instance.transform.rotation);
            gas.SetActive(true);
            UnityEngine.Object.Destroy(gas, GasCleanupSeconds);
            Plugin.Debug("Ascent 17: a Citadel chest was a mimic.");
        }
    }

    [HarmonyPatch(typeof(OrbThatMakesYouSleepy), nameof(OrbThatMakesYouSleepy.LateUpdate))]
    private static class NapberryPulse
    {
        private static void Prefix(OrbThatMakesYouSleepy __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(OrbThatMakesYouSleepy.animSpeed), NapberryPace);
            }
        }
    }
}
