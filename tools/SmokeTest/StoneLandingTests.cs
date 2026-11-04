using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;

namespace UltimateAscent.SmokeTest;

/// <summary>A scout landing on a Stone, and a Stone shattering right on a scout (9).</summary>
internal sealed partial class Runner
{
    /// <summary>Every status effect given to one character: its kind and where it came from.</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddAffliction), typeof(Affliction), typeof(bool))]
    internal static class AfflictionRecorder
    {
        internal static Character Of;
        internal static readonly List<string> Seen = new List<string>();

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(CharacterAfflictions __instance, Affliction affliction)
        {
            if (Of == null || __instance.character != Of || affliction == null)
            {
                return;
            }
            IEnumerable<string> callers = new System.Diagnostics.StackTrace(2, false).GetFrames()?.Take(6)
                .Select(f => f.GetMethod()).Where(m => m != null).Select(m => $"{m.DeclaringType?.Name}.{m.Name}") ?? Enumerable.Empty<string>();
            Seen.Add($"{affliction.GetAfflictionType()} from {string.Join(" < ", callers)}");
        }
    }

    /// <summary>Every touch a Stone checks for a hit: how fast the two came together, how fast the stone itself flew, a scout or not, a hit or not.</summary>
    [HarmonyPatch(typeof(Bonkable), nameof(Bonkable.VelocityAboveThreshold))]
    internal static class StoneContacts
    {
        internal static readonly List<(float together, float own, bool scout, bool hit)> Seen = new List<(float, float, bool, bool)>();

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Bonkable __instance, Collision coll, bool __result)
        {
            if (!StoneItem.IsStone(__instance.GetComponent<Item>()))
            {
                return;
            }
            Breakable breakable = __instance.GetComponent<Breakable>();
            Seen.Add((coll.relativeVelocity.magnitude, breakable != null ? breakable.lastVelocity.magnitude : -1f, coll.gameObject.GetComponentInParent<Character>() != null, __result));
        }
    }

    /// <summary>Each part of an object and what it is made of: "name[Component, ...]".</summary>
    private static string Parts(GameObject root) =>
        string.Join("; ", root.GetComponentsInChildren<Transform>(true).Select(t =>
            $"{t.name}[{string.Join(", ", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name))}]"));

    /// <summary>
    /// Stones laid on level ground (a patch of nine, 0.45 m apart), then the scout warped 16 m above them and knocked down
    /// there, so their body falls limp onto them (a scout landing on their feet floats on the game's ground spring and
    /// barely touches small things): they touch at more than 18 m/s (the shattering speed), but a stone lying still hits
    /// nobody and does not shatter (only a stone's own speed counts).
    /// </summary>
    private IEnumerator LandingOnAStone(Item stone)
    {
        Character local = Local;
        Vector3 home = local.Center;
        Vector3? ground = StandNear(local.Center, 3f, 8f, seeIt: false, firm: true);
        if (ground == null)
        {
            Fail("level ground beside the scout to lay Stones on");
            yield break;
        }
        List<Item> lying = new List<Item>();
        Step("lay nine Stones on the ground beside the scout", () =>
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3 above = ground.Value + new Vector3(x * 0.45f, 2f, z * 0.45f);
                    if (Physics.Raycast(above, Vector3.down, out RaycastHit floor, 4f, HelperFunctions.terrainMapMask))
                    {
                        lying.Add(PhotonNetwork.InstantiateItemRoom(stone.gameObject.name, floor.point + Vector3.up * 0.12f, Quaternion.identity).GetComponent<Item>());
                    }
                }
            }
        });
        yield return new WaitForSeconds(2f);
        Vector3 middle = lying.Where(i => i != null).Select(i => i.transform.position).Aggregate(Vector3.zero, (a, b) => a + b) / Mathf.Max(1, lying.Count(i => i != null));
        int hitsBefore = StoneItem.Hits;
        int breaksBefore = StoneItem.Breaks;
        StoneContacts.Seen.Clear();
        Protect(true);
        try
        {
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, middle + Vector3.up * 16f, false);
            yield return new WaitForSeconds(0.1f);
            for (float until = Time.time + 3f; Time.time < until && local.warping;)
            {
                yield return null;
            }
            float off = new Vector2(local.Center.x - middle.x, local.Center.z - middle.z).magnitude;
            Step("knock the scout down 16 m above the Stones", () => local.Fall(4f));
            for (float until = Time.time + 4f; Time.time < until && !local.data.isGrounded;)
            {
                yield return null;
            }
            yield return new WaitForSeconds(1.5f);
            Screenshot("landed-on-stones");
            List<(float together, float own, bool scout, bool hit)> onScout = StoneContacts.Seen.Where(c => c.scout).ToList();
            Check("a scout landing on Stones lying still, faster than 18 m/s, takes nothing from them (no knock-down, no injury) and they stay whole (9)",
                onScout.Any(c => c.together > StoneItem.BreakSpeed) && onScout.All(c => !c.hit) && StoneItem.Hits == hitsBefore && StoneItem.Breaks == breaksBefore && lying.All(i => i != null),
                $"dropped {off:0.0} m off the Stones' middle; touches with the scout: {(onScout.Count == 0 ? "none" : string.Join(", ", onScout.Select(c => $"{c.together:0.0} m/s together, the stone's own {c.own:0.0} m/s{(c.hit ? ", a hit" : "")}")))}; "
                + $"hits {StoneItem.Hits - hitsBefore}, breaks {StoneItem.Breaks - breaksBefore}, stones left {lying.Count(i => i != null)} of {lying.Count}");
        }
        finally
        {
            foreach (Item item in lying.Where(i => i != null && i.photonView != null))
            {
                PhotonNetwork.Destroy(item.gameObject);
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
        }
        yield return new WaitForSeconds(1.5f);
    }

    /// <summary>
    /// A Stone flung down onto the scout's head at 25 m/s: it shatters on them and injures them for 10 (20 with ascent
    /// 17's rule on; both switched in memory) 0.25 s after the hit, its rock smash before the damage sound; its dust cloud
    /// gives nothing (not the Sunscreen's effect, whose spray cloud it is made from) and plays no spray sound. What the
    /// dust and the Sunscreen's spray are made of, and every effect and sound of the moment, are reported.
    /// </summary>
    private IEnumerator StoneShattersOnTheScout(Item stone)
    {
        Character local = Local;
        GameObject dust = StoneItem.DustTemplate();
        GameObject spray = ItemByName("Sunscreen")?.GetComponent<Action_Spawn>()?.objectToSpawn;
        Info("the Stone's dust cloud, part by part", dust != null ? Parts(dust) : "none");
        Info("the Sunscreen's spray cloud, part by part", spray != null ? Parts(spray) : "none");
        ConfigEntry<bool> hazards = UltimateAscent.Plugin.AscentSwitches[Lvl.Hazards];
        bool saved = hazards.Value;
        bool first = true;
        try
        {
            foreach (bool on in Rules.Level >= Lvl.Hazards ? new[] { true, false } : new[] { false })
            {
                hazards.Value = on;
                HostOptions.Invalidate();
                Protect(false);
                ClearStatuses(Afflictions);
                yield return new WaitForSeconds(0.3f);
                float expected = on ? StoneItem.InjuryHazards : StoneItem.Injury;
                int hitsBefore = StoneItem.Hits;
                int injuringBefore = StoneItem.Injuring;
                int breaksBefore = StoneItem.Breaks;
                int dustBefore = StoneItem.DustClouds;
                AfflictionRecorder.Seen.Clear();
                AfflictionRecorder.Of = local;
                SoundsPlayed.Heard.Clear();
                SoundsPlayed.Listening = true;
                bool sunscreen = false;
                SFX_Instance smash = StoneItem.BreakSound();
                SFX_Instance hurt = local.refs.afflictions.injurySmall;
                float smashBefore = smash != null ? smash.lastTimePlayed : 0f;
                float hurtBefore = hurt != null ? hurt.lastTimePlayed : 0f;
                Item flung = null;
                Step("a Stone flung down onto the scout's head at 25 m/s" + (on ? "" : " (ascent 17's rule off)"), () =>
                {
                    flung = PhotonNetwork.InstantiateItemRoom(stone.gameObject.name, local.Head + Vector3.up * 2.5f, Quaternion.identity).GetComponent<Item>();
                });
                yield return new WaitForFixedUpdate();
                if (flung != null && flung.rig != null)
                {
                    flung.rig.linearVelocity = Vector3.down * 25f;
                }
                bool pictured = !first;
                for (float until = Time.time + 3f; Time.time < until;)
                {
                    sunscreen |= local.data.wearingSunscreen;
                    // What the scout sees as it shatters on them: its dust cloud from inside.
                    if (!pictured && StoneItem.Breaks > breaksBefore)
                    {
                        pictured = true;
                        yield return new WaitForSeconds(0.15f);
                        Screenshot("stone-shatters-on-the-scout");
                    }
                    yield return null;
                }
                SoundsPlayed.Listening = false;
                AfflictionRecorder.Of = null;
                float injury = Afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Injury);
                Check($"a Stone shattering on a scout knocks them down and injures them for {expected * 100f:0} ({(on ? "17" : "9; ascent 17's rule off")})",
                    StoneItem.Hits > hitsBefore && StoneItem.Injuring > injuringBefore && StoneItem.Breaks > breaksBefore && Mathf.Abs(injury - expected) < 0.006f,
                    $"hits {StoneItem.Hits - hitsBefore} ({StoneItem.Injuring - injuringBefore} injuring), breaks {StoneItem.Breaks - breaksBefore}, {injury * 100f:0.#} injury");
                if (first)
                {
                    List<string> sounds = SoundsPlayed.Heard.Where(s => s != null).Select(s => s.name).Distinct().ToList();
                    bool spraySound = sounds.Any(n => n.IndexOf("spray", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("sunscreen", System.StringComparison.OrdinalIgnoreCase) >= 0);
                    bool sunscreenGiven = sunscreen || AfflictionRecorder.Seen.Any(a => a.StartsWith("Sunscreen", System.StringComparison.Ordinal));
                    float smashAt = smash != null && smash.lastTimePlayed > smashBefore ? smash.lastTimePlayed : -1f;
                    float hurtAt = hurt != null && hurt.lastTimePlayed > hurtBefore ? hurt.lastTimePlayed : -1f;
                    Check("its rock smash comes before the damage sound: the injury lands 0.25 s after the hit (9)",
                        smashAt > 0f && hurtAt > 0f && hurtAt - smashAt >= StoneItem.InjuryDelay - 0.05f,
                        $"the smash at {(smashAt > 0f ? "0" : "never")}, the damage sound {(hurtAt > 0f && smashAt > 0f ? $"{hurtAt - smashAt:+0.00;-0.00} s after" : "never")}");
                    Check("its dust is all it leaves: no Sunscreen effect, no spray sound (9)",
                        StoneItem.DustClouds > dustBefore && !sunscreenGiven && !spraySound,
                        $"dust clouds {StoneItem.DustClouds - dustBefore}; wearing sunscreen {sunscreen}; "
                        + $"effects given: {(AfflictionRecorder.Seen.Count == 0 ? "none" : string.Join(" | ", AfflictionRecorder.Seen))}; sounds: {(sounds.Count == 0 ? "none" : string.Join(", ", sounds))}");
                }
                first = false;
                if (local.data.wearingSunscreen)
                {
                    local.refs.afflictions.RemoveAffliction(Affliction.AfflictionType.Sunscreen);
                }
                ClearStatuses(Afflictions);
                Protect(true);
                yield return new WaitForSeconds(2f);
            }
        }
        finally
        {
            hazards.Value = saved;
            HostOptions.Invalidate();
            SoundsPlayed.Listening = false;
            AfflictionRecorder.Of = null;
            ClearStatuses(Afflictions);
            Protect(true);
        }
    }
}
