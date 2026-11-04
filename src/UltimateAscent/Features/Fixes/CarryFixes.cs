using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace UltimateAscent.Features.Fixes;

/// <summary>
/// A scout left tied to the scout who carried them (the user's report: they wake, even climb, and are pulled back to
/// them for good), fixed on every ascent. A carried scout is pulled to their carrier for as long as their own record
/// says who carries them, and only the carrier's game ever ends a carry, for the one scout it has as carried. Two ways
/// leave a scout carried with no one to drop them: the carrier takes up another (the game makes the new one the carried
/// one; the first stays tied to them), or the carrier's game never sees the scout wake. A carry that goes right is not
/// touched; in either broken state the scout is set free:
/// <list type="bullet">
/// <item>Carried by someone gone, or by someone carrying another, for a second: every game lets go of them (only their
/// side of it; the carrier's is the carrier's).</item>
/// <item>Awake and still carried for 3 s: their own game asks for the game's own drop, as the carrier's game would
/// have the moment it saw them wake.</item>
/// </list>
/// </summary>
internal static class CarryFixes
{
    internal const float OrphanSeconds = 1f;
    internal const float AwakeSeconds = 3f;

    /// <summary>For the tests: scouts set free on this game, and drops their own game asked for.</summary>
    internal static int Freed;
    internal static int DropsAsked;

    private static readonly Dictionary<Character, float> orphanSince = new Dictionary<Character, float>();
    private static readonly Dictionary<Character, float> awakeSince = new Dictionary<Character, float>();

    internal static void Reset()
    {
        orphanSince.Clear();
        awakeSince.Clear();
    }

    [HarmonyPatch(typeof(CharacterCarrying), "FixedUpdate")]
    private static class LooseCarry
    {
        private static void Prefix(CharacterCarrying __instance, Character ___character)
        {
            Character scout = ___character;
            CharacterData data = scout != null ? scout.data : null;
            if (data == null)
            {
                return;
            }
            Character carrier = data.carrier;
            if (!data.isCarried && carrier == null)
            {
                orphanSince.Remove(scout);
                awakeSince.Remove(scout);
                return;
            }
            float now = Time.time;
            if (carrier == null || carrier.data == null || carrier.data.carriedPlayer != scout)
            {
                awakeSince.Remove(scout);
                if (!orphanSince.TryGetValue(scout, out float since))
                {
                    orphanSince[scout] = now;
                }
                else if (now - since >= OrphanSeconds)
                {
                    orphanSince.Remove(scout);
                    data.isCarried = false;
                    data.carrier = null;
                    __instance.ToggleCarryPhysics(setCarried: false);
                    scout.refs.afflictions.UpdateWeight();
                    Freed++;
                }
                return;
            }
            orphanSince.Remove(scout);
            if (!scout.IsLocal || data.dead || data.fullyPassedOut)
            {
                awakeSince.Remove(scout);
                return;
            }
            if (!awakeSince.TryGetValue(scout, out float woke))
            {
                awakeSince[scout] = now;
            }
            else if (now - woke >= AwakeSeconds)
            {
                // Asked again in 3 s if it does not land.
                awakeSince[scout] = now;
                DropsAsked++;
                carrier.refs.carriying.Drop(scout);
            }
        }
    }
}
