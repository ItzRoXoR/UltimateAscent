using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Peak.Afflictions;
using UltimateAscent.Features;
using UnityEngine;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>The Energy Drink and the Toxic Drink overriding each other (9).</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The starting curse (7): a scout up on the island without it (as a joining player's game could lose it while setting
    /// the scout up) has what is missing made up, once; a scout who has it keeps it as it is.
    /// </summary>
    private IEnumerator StartingCurseMadeUp()
    {
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;
        float curse = Ascents.startingCurse;
        if (curse <= 0f)
        {
            Info("the starting curse check is skipped", $"no starting curse on ascent {target}");
            yield break;
        }
        float before = afflictions.GetCurrentStatus(Status.Curse);
        Protect(false);
        try
        {
            afflictions.SetStatus(Status.Curse, 0f);
            yield return LateJoiners.MakeUpStartingCurse(local, "test");
            float madeUp = afflictions.GetCurrentStatus(Status.Curse);
            afflictions.SetStatus(Status.Curse, curse + 0.05f);
            yield return LateJoiners.MakeUpStartingCurse(local, "test");
            float kept = afflictions.GetCurrentStatus(Status.Curse);
            Check("a scout up without the starting curse gets it made up, once; one who has it keeps what they have (7)",
                Mathf.Abs(madeUp - curse) < 0.006f && Mathf.Abs(kept - (curse + 0.05f)) < 0.006f,
                $"from 0: {madeUp * 100f:0.#} (the starting curse {curse * 100f:0}); from {(curse + 0.05f) * 100f:0}: {kept * 100f:0.#}");
        }
        finally
        {
            afflictions.SetStatus(Status.Curse, before);
            Protect(true);
        }
    }

    /// <summary>Speed boosts as they end: when, their walking bonus, and the drowsiness they leave.</summary>
    [HarmonyPatch(typeof(Affliction_FasterBoi), nameof(Affliction_FasterBoi.OnRemoved))]
    internal static class BoostsEnded
    {
        internal static readonly List<(float at, float walk, float crash)> Seen = new List<(float, float, float)>();

        private static void Prefix(Affliction_FasterBoi __instance)
        {
            if (__instance.character != null && __instance.character.IsLocal)
            {
                Seen.Add((Time.time, __instance.moveSpeedMod, __instance.drowsyOnEnd));
            }
        }
    }

    /// <summary>
    /// The Energy Drink and the Toxic Drink override each other (9; from ascent 15 with that rule's numbers): an Energy
    /// Drink, then 2 s into its boost a Toxic Drink: only the Toxic Drink's boost runs (its walking, its climbing, its
    /// whole time) and the Energy Drink's boost ended then, with its drowsiness. The other way round: only the Energy
    /// Drink's runs and the Toxic Drink's poison begins at once. The same drink twice keeps one boost (the game's way).
    /// </summary>
    private IEnumerator SpeedDrinksOverride()
    {
        Item energy = ItemByName("Energy Drink");
        Item toxic = ToxicDrink.Template;
        if (energy == null || toxic == null)
        {
            Fail("the Energy Drink and the Toxic Drink are in the item database", $"Energy Drink {energy != null}, Toxic Drink {toxic != null}");
            yield break;
        }
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;
        List<Affliction_FasterBoi> Boosts() => afflictions.afflictionList.OfType<Affliction_FasterBoi>().ToList();
        // Only drowsiness and poison are measured: the rest (the sun from 23) is kept off.
        IEnumerator Wait(float seconds)
        {
            for (float until = Time.time + seconds; Time.time < until;)
            {
                foreach (Status type in CareStatuses.Where(t => t != Status.Drowsy && t != Status.Poison && afflictions.GetCurrentStatus(t) > 0f))
                {
                    afflictions.SetStatus(type, 0f);
                }
                yield return null;
            }
        }
        IEnumerator Fresh()
        {
            afflictions.RemoveAffliction(Affliction.AfflictionType.FasterBoi);
            // A boost taken away here (not run out) leaves its poison still to come: gone too.
            ToxicDrink.Reset();
            yield return null;
            ClearStatuses(afflictions);
            yield return Wait(0.3f);
        }
        IEnumerator Drink(Item drink)
        {
            yield return SpawnIntoHand(drink);
            if (held != null)
            {
                yield return EatHeld();
            }
        }
        Protect(false);
        float walkBase = local.refs.movement.movementModifier;
        float climbBase = local.refs.climbing.climbSpeedMod;
        try
        {
            // An Energy Drink, then a Toxic Drink 2 s into its boost.
            yield return Fresh();
            float walk0 = local.refs.movement.movementModifier;
            float climb0 = local.refs.climbing.climbSpeedMod;
            yield return Drink(energy);
            float energyClimb = local.refs.climbing.climbSpeedMod - climb0;
            yield return Wait(2f);
            BoostsEnded.Seen.Clear();
            float switchAt = Time.time;
            yield return Drink(toxic);
            List<Affliction_FasterBoi> running = Boosts();
            float walk = local.refs.movement.movementModifier - walk0;
            float climb = local.refs.climbing.climbSpeedMod - climb0;
            bool energyEnded = BoostsEnded.Seen.Any(b => b.at >= switchAt && Mathf.Abs(b.walk - 0.5f) < 0.01f);
            float crash = BoostsEnded.Seen.Where(b => b.at >= switchAt && Mathf.Abs(b.walk - 0.5f) < 0.01f).Select(b => b.crash).DefaultIfEmpty(0f).First();
            Check("drunk 2 s into an Energy Drink's boost, a Toxic Drink overrides it: only the Toxic Drink's boost runs, and the Energy Drink's ends then with its drowsiness (9, 15)",
                running.Count == 1 && ToxicDrink.IsBoost(running[0]) && Mathf.Abs(running[0].totalTime - ToxicDrink.SecondsNow) < 0.01f && running[0].timeElapsed < 2f
                && Mathf.Abs(walk - (ToxicDrink.SpeedNow - 1f)) < 0.01f && Mathf.Abs(climb - (ToxicDrink.ClimbSpeedNow - 1f)) < 0.01f && energyEnded && crash > 0f && Mathf.Abs(energyClimb - 1.5f) < 0.01f,
                $"the Energy Drink climbing +{energyClimb:0.##}; then {running.Count} boost(s): {string.Join(", ", running.Select(b => $"{b.totalTime:0.#} s ({b.timeElapsed:0.0} s gone)"))}, "
                + $"walking +{walk:0.##} and climbing +{climb:0.##} (times {1f + walk:0.##} / {1f + climb:0.##}); the Energy Drink's boost ended {energyEnded}, its drowsiness {crash * 100f:0.#}");

            // A Toxic Drink, then an Energy Drink 2 s into its boost.
            yield return Fresh();
            yield return Drink(toxic);
            yield return Wait(2f);
            afflictions.SetStatus(Status.Poison, 0f);
            BoostsEnded.Seen.Clear();
            switchAt = Time.time;
            yield return Drink(energy);
            running = Boosts();
            walk = local.refs.movement.movementModifier - walk0;
            climb = local.refs.climbing.climbSpeedMod - climb0;
            bool toxicEnded = BoostsEnded.Seen.Any(b => b.at >= switchAt && b.walk >= 1.5f);
            float endedAt = BoostsEnded.Seen.Where(b => b.at >= switchAt && b.walk >= 1.5f).Select(b => b.at).DefaultIfEmpty(Time.time).First();
            yield return Wait(1.5f);
            float poison = afflictions.GetCurrentStatus(Status.Poison);
            // As much as lands from the switch on, at most (its rate, in the bar's steps of 2.5).
            float most = ToxicDrink.PoisonNow * Mathf.Clamp01((Time.time - endedAt) / ToxicDrink.PoisonSeconds) + 0.026f;
            float boostLeft = running.Count == 1 ? running[0].totalTime - running[0].timeElapsed : -1f;
            Check("drunk 2 s into a Toxic Drink's boost, an Energy Drink overrides it: only the Energy Drink's boost runs, and the Toxic Drink's poison begins at once (9, 15)",
                running.Count == 1 && !ToxicDrink.IsBoost(running[0]) && Mathf.Abs(walk - 0.5f) < 0.01f && Mathf.Abs(climb - 1.5f) < 0.01f && toxicEnded && poison > 0f && poison <= most,
                $"{running.Count} boost(s), walking +{walk:0.##} and climbing +{climb:0.##} (times {1f + walk:0.##} / {1f + climb:0.##}), {boostLeft:0.#} s left; "
                + $"the Toxic Drink's boost ended {toxicEnded}; poison {poison * 100f:0.#} {Time.time - endedAt:0.0} s after the switch (at most {most * 100f:0.#}; the boost would have run {ToxicDrink.SecondsNow - 2f:0} s more)");

            // The same drink twice: one boost, the game's way.
            yield return Fresh();
            yield return Wait(ToxicDrink.PoisonSeconds);
            ClearStatuses(afflictions);
            yield return Drink(energy);
            Affliction_FasterBoi first = Boosts().FirstOrDefault();
            yield return Wait(2f);
            yield return Drink(energy);
            running = Boosts();
            climb = local.refs.climbing.climbSpeedMod - climb0;
            Check("the same drink twice keeps one boost, as in the game (9)",
                running.Count == 1 && running[0] == first && Mathf.Abs(climb - 1.5f) < 0.01f,
                $"{running.Count} boost(s), the first one still {(running.Count == 1 && running[0] == first)}, climbing +{climb:0.##}");
        }
        finally
        {
            afflictions.RemoveAffliction(Affliction.AfflictionType.FasterBoi);
            ClearStatuses(afflictions);
            Protect(true);
        }
        yield return new WaitForSeconds(ToxicDrink.PoisonSeconds + 0.5f);
        ClearStatuses(afflictions);
        Info("speed bonuses back to normal", $"walking {local.refs.movement.movementModifier:0.##} (was {walkBase:0.##}), climbing {local.refs.climbing.climbSpeedMod:0.##} (was {climbBase:0.##})");
    }
}
