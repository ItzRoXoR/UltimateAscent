using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Stick: the game's cut stick (its own model, name, icon and two-handed grip) made something to hit
/// with. Sticks lie on the ground from ascent 9 (see <see cref="GroundItems"/>). Swinging one (the use
/// button) costs 33% stamina (not possible out of stamina, like a kick) and knocks down for 3 s every
/// scout and zombie a kick would reach, in front of the swinger; it does not hurt them.
/// <para>The swing moves the whole hold, both hands with the stick in them, as the game moves held items:
/// up and back over the head, then a chop forward and down, played on every client. (The Ritual Dagger's
/// animation turns its blade in the hand into a stab grip, which held a stick wrongly.) The hit uses the
/// kick's reach, aim and push, checked by the swinger when the chop lands.</para>
/// <para>A lit Torch swings the same way (<see cref="TorchItem"/>).</para>
/// </summary>
internal static class StickItem
{
    internal const string PrefabName = "FireWood";
    internal const string SwingPrompt = "SWING";

    internal const float SwingStamina = 0.33f;
    internal const float Knockdown = 3f;

    /// <summary>
    /// The swing, from the press: where the hold is (as for every held item: from the head, x right, y up,
    /// z forward; the first and the last are the rest pose) and how far the hold is tipped back (degrees;
    /// forward when negative): raised up and back over the head, the chop, a short follow-through, back to
    /// rest. The hold's targets lie beyond what the arms reach, as the game's own Ritual Dagger strike does,
    /// so the arms swing all the way within the time (chosen from three sizes compared in game).
    /// </summary>
    internal static (float at, Vector3 hold, float tipBack)[] Swing =
    {
        (0f, Vector3.zero, 0f),
        (0.35f, new Vector3(0.1f, 1.2f, -0.6f), 70f),
        (0.5f, new Vector3(0f, -0.6f, 2.2f), -78f),
        (0.65f, new Vector3(0f, -0.6f, 1.8f), -78f),
        (1f, Vector3.zero, 0f),
    };

    /// <summary>The step of the swing at which the chop lands.</summary>
    internal const int ChopStep = 2;

    internal static float StrikeDelay => Swing[ChopStep].at;
    internal static float SwingSeconds => Swing[Swing.Length - 1].at;

    private static readonly string[] SwingSounds = { "SFXI KickAttempt", "SFXI ScoutMaster Grab Swing 1", "SFXI ScoutMaster Grab Swing 2" };

    private static Item template;
    private static float readyAt;
    private static SFX_Instance swingSound;
    private static bool soundLooked;
    private static Vector3 restHold;
    private static Vector3 restFacing;

    internal static Item Template => template;

    /// <summary>For the tests: the scouts and zombies the last strike of this client hit, and how many swings this client made.</summary>
    internal static readonly List<Character> LastHits = new List<Character>();
    internal static int Swings;

    internal static bool IsStick(Item item) => item != null && template != null && item.itemID == template.itemID;

    /// <summary>The game's cut stick, given a use: the swing (and the game's message that plays an item's animation for everyone, to swing it there too).</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(PrefabName, out Item stick) || stick == null)
        {
            Plugin.Log.LogWarning("The cut Stick is missing from the item database; the Stick is not added.");
            return;
        }
        stick.UIData.hasMainInteract = true;
        stick.UIData.mainInteractPrompt = SwingPrompt;
        stick.usingTimePrimary = 0f;
        stick.showUseProgress = false;
        if (stick.GetComponent<Action_PlayItemAnimation>() == null)
        {
            Action_PlayItemAnimation message = stick.gameObject.AddComponent<Action_PlayItemAnimation>();
            message.useRPC = true;
        }
        if (stick.TryGetComponent(out LootData loot))
        {
            loot.spawnLocations = SpawnPool.None;
        }
        restHold = stick.defaultPos;
        restFacing = stick.defaultForward;
        template = stick;
    }

    /// <summary>The use button swings the stick (its holder's client decides and checks the hit).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.StartUsePrimary))]
    private static class SwingOnUse
    {
        private static void Postfix(Item __instance)
        {
            if (IsStick(__instance))
            {
                TrySwing(__instance, (target, zombie) => Knockdown);
            }
        }
    }

    /// <summary>
    /// A swing of an item held like the stick (the Stick, a lit Torch) by its local holder, if they can (awake, the
    /// last swing over, stamina left, as for a kick): its stamina, its motion on every client and its strike, which
    /// knocks each scout and zombie it reaches down for the seconds given (a scout or a zombie). False if no swing.
    /// </summary>
    internal static bool TrySwing(Item item, Func<Character, bool, float> knockdown)
    {
        Character holder = item.holderCharacter;
        if (holder == null || !holder.IsLocal || holder.data.dead || !holder.data.fullyConscious || Time.time < readyAt || holder.OutOfRegularStamina())
        {
            return false;
        }
        readyAt = Time.time + SwingSeconds;
        Swings++;
        holder.UseStamina(SwingStamina, useBonusStamina: true, ignoreAscents: true);
        item.photonView.RPC(nameof(Action_PlayItemAnimation.PlayAnimationRPC), RpcTarget.All);
        item.StartCoroutine(Strike(item, holder, knockdown));
        return true;
    }

    /// <summary>The strike, when it lands: the kick's reach and aim, the knock-down given, the kick's push without its stamina boost.</summary>
    private static IEnumerator Strike(Item stick, Character holder, Func<Character, bool, float> knockdown)
    {
        yield return new WaitForSeconds(StrikeDelay);
        LastHits.Clear();
        if (stick == null || holder == null || holder.data.dead || holder.data.currentItem != stick)
        {
            yield break;
        }
        CharacterGrabbing kick = holder.refs.grabbing;
        float reach = kick != null ? kick.kickDistance : 2.5f;
        float aim = kick != null ? kick.kickAngle : 55f;
        float push = kick != null ? kick.kickForce : 200f;
        float spread = kick != null ? kick.kickRange : 1f;
        Vector3 look = holder.data.lookDirection;
        if (look.y > 0f)
        {
            look.y = 0f;
            look.Normalize();
        }
        List<Character> zombies = ZombieManager.Instance != null
            ? ZombieManager.Instance.zombies.Where(z => z != null && z.character != null).Select(z => z.character).ToList()
            : new List<Character>();
        foreach (Character target in Character.AllCharacters.Concat(zombies).Distinct())
        {
            if (target == null || target == holder)
            {
                continue;
            }
            float distance = Vector3.Distance(holder.Center, target.Center);
            if (distance > reach || Vector3.Angle(look, target.Center - holder.Center) > aim)
            {
                continue;
            }
            Vector3 point = holder.Center + look * distance;
            holder.refs.view.RPC(nameof(CharacterGrabbing.RPCA_KickImpact), RpcTarget.All, point);
            target.Fall(knockdown(target, zombies.Contains(target)));
            target.AddForceAtPosition(look * push, point, spread);
            LastHits.Add(target);
        }
    }

    /// <summary>The swing on every client (the Stick's, a Torch's): the hold's motion and the sound of a swing.</summary>
    [HarmonyPatch(typeof(Action_PlayItemAnimation), nameof(Action_PlayItemAnimation.PlayAnimationRPC))]
    private static class SwingLook
    {
        private static bool Prefix(Action_PlayItemAnimation __instance)
        {
            Item item = __instance.GetComponent<Item>();
            if (!IsStick(item) && !TorchItem.IsTorch(item))
            {
                return true;
            }
            item.StartCoroutine(SwingMotion(item));
            SwingSound()?.Play(item.transform.position);
            return false;
        }
    }

    /// <summary>The hold follows the swing; at the end it is back at rest (the item's own, as its prefab holds it).</summary>
    private static IEnumerator SwingMotion(Item item)
    {
        Vector3 rest = restHold;
        Vector3 restAim = restFacing;
        if (!IsStick(item) && ItemDatabase.TryGetItem(item.itemID, out Item prefab) && prefab != null)
        {
            rest = prefab.defaultPos;
            restAim = prefab.defaultForward;
        }
        float start = Time.time;
        while (item != null)
        {
            float t = Time.time - start;
            Pose(t, rest, restAim, out Vector3 hold, out Vector3 facing);
            item.defaultPos = hold;
            item.defaultForward = facing;
            if (t >= SwingSeconds)
            {
                item.defaultPos = rest;
                item.defaultForward = restAim;
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>The Stick's swing at a time (see <see cref="Pose(float, Vector3, Vector3, out Vector3, out Vector3)"/>).</summary>
    internal static void Pose(float time, out Vector3 hold, out Vector3 facing) => Pose(time, restHold, restFacing, out hold, out facing);

    /// <summary>The swing's hold at a time: between two of its steps, eased in and out; the hold faces forward, tipped back by the step's angle.</summary>
    internal static void Pose(float time, Vector3 restHold, Vector3 restFacing, out Vector3 hold, out Vector3 facing)
    {
        int last = Swing.Length - 1;
        int i = 1;
        while (i < last && time > Swing[i].at)
        {
            i++;
        }
        (float at0, Vector3 hold0, float tip0) = Swing[i - 1];
        (float at1, Vector3 hold1, float tip1) = Swing[i];
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - at0) / Mathf.Max(at1 - at0, 1e-4f)));
        hold = Vector3.Lerp(i - 1 == 0 ? restHold : hold0, i == last ? restHold : hold1, k);
        float tip = Mathf.Lerp(tip0, tip1, k) * Mathf.Deg2Rad;
        Vector3 flat = new Vector3(restFacing.x, 0f, restFacing.z);
        flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
        facing = flat * Mathf.Cos(tip) + Vector3.up * Mathf.Sin(tip);
    }

    private static SFX_Instance SwingSound()
    {
        if (!soundLooked || swingSound == null)
        {
            soundLooked = true;
            SFX_Instance[] loaded = Resources.FindObjectsOfTypeAll<SFX_Instance>();
            swingSound = SwingSounds.Select(name => loaded.FirstOrDefault(s => s != null && s.name == name)).FirstOrDefault(s => s != null);
        }
        return swingSound;
    }
}
