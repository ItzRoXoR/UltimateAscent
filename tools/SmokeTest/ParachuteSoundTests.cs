using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>The Auto-Parachute in the hands: how it sounds and how it is held.</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// The Auto-Parachute in the hands (9): held by its sides, and it sounds like the Backpack, never like metal. The
    /// Jetpack (which the parachute is built from), the Backpack and the parachute are each put on, taken into the hands
    /// (slot 4), the scout makes three running jumps with it, and it is dropped to the ground. The Jetpack still knocks
    /// like metal (the game's own: the check hears the knocks); the Backpack and the parachute never do.
    /// </summary>
    private IEnumerator ParachuteSounds()
    {
        Item jetpack = ItemByName("Jetpack");
        Item backpack = ItemByName("Backpack");
        Item parachute = AutoParachute.Template;
        if (jetpack == null || backpack == null || parachute == null)
        {
            Fail("the Jetpack, the Backpack and the Auto-Parachute are in the item database", $"Jetpack {jetpack != null}, Backpack {backpack != null}, Auto-Parachute {parachute != null}");
            yield break;
        }
        if (Rules.Level < Lvl.First)
        {
            Info("the Auto-Parachute sound check is skipped", $"testing ascent {target}");
            yield break;
        }
        HashSet<SFX_Instance> metal = new HashSet<SFX_Instance>(jetpack.GetComponent<ItemImpactSFX>()?.impact ?? new SFX_Instance[0]);
        Character local = Local;
        Vector2 look = local.data.lookValues;
        Dictionary<Item, (int held, int ground)> knocks = new Dictionary<Item, (int, int)>();
        Dictionary<Item, (float left, float right)> reach = new Dictionary<Item, (float, float)>();
        Protect(true);
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        // The jumps are running ones (to and fro along open ground), as a held Jetpack bumps hard enough to knock then.
        Vector3 way = OpenWay(local, 25f);
        if (way == Vector3.zero)
        {
            Info("the jumps are made standing", $"no 25 m of flat, open ground near {local.Center:F0}");
        }
        Harmony keys = new Harmony("UltimateAscent.SmokeTest.keys");
        keys.Patch(AccessTools.Method(typeof(CharacterInput), "Sample"), postfix: new HarmonyMethod(typeof(Runner), nameof(ForceInput)));
        try
        {
            Item[] packs = { jetpack, backpack, parachute };
            for (int i = 0; i < packs.Length; i++)
            {
                yield return HoldJumpAndDrop(packs[i], packs[i] == parachute ? "Auto-Parachute" : packs[i].gameObject.name, i % 2 == 0 ? way : -way, metal, knocks, reach);
            }
        }
        finally
        {
            forcingInput = false;
            keys.UnpatchSelf();
            local.data.lookValues = look;
        }
        if (reach.ContainsKey(parachute))
        {
            Check("held, the hands hold the Auto-Parachute by its sides, as they hold the Jetpack and the Backpack",
                reach[parachute].left < 0.06f && reach[parachute].right < 0.06f,
                string.Join("; ", reach.Select(r => $"{(r.Key == parachute ? "Auto-Parachute" : r.Key.gameObject.name)}: left hand {r.Value.left:0.00} m from the pack, right {r.Value.right:0.00} m")));
        }
        if (knocks.Count < 3)
        {
            yield break;
        }
        BackPackAudio ownSounds = parachute.GetComponent<BackPackAudio>();
        BackPackAudio backpackSounds = backpack.GetComponent<BackPackAudio>();
        bool sameAsBackpack = ownSounds != null && backpackSounds != null && ownSounds.holdSFX.SequenceEqual(backpackSounds.holdSFX) && ownSounds.dropSFX.SequenceEqual(backpackSounds.dropSFX);
        Check("the Jetpack still knocks like metal, held as the scout jumps and on the ground (the game's own: the check hears the knocks)",
            knocks[jetpack].held > 0 && knocks[jetpack].ground > 0, $"metal knocks: {knocks[jetpack].held} in three jumps, {knocks[jetpack].ground} as it lands");
        Check("the Auto-Parachute sounds like the Backpack, never like metal: held as the scout jumps and on the ground (9)",
            knocks[parachute].held == 0 && knocks[parachute].ground == 0 && knocks[backpack].held == 0 && knocks[backpack].ground == 0 && parachute.GetComponent<ItemImpactSFX>() == null && sameAsBackpack,
            $"metal knocks in three jumps / as it lands: Auto-Parachute {knocks[parachute].held} / {knocks[parachute].ground}, Backpack {knocks[backpack].held} / {knocks[backpack].ground}; " +
            $"the Backpack's pick-up and drop sounds {sameAsBackpack}");
    }

    /// <summary>
    /// One pack put on, taken into the hands, the scout jumping three times with it (running along the way, when there is
    /// one) and dropped to the ground from above the head: the metal knocks heard held and as it lands, and how far each
    /// hand is from the pack.
    /// </summary>
    private IEnumerator HoldJumpAndDrop(Item prefab, string name, Vector3 way, HashSet<SFX_Instance> metal, Dictionary<Item, (int held, int ground)> knocks, Dictionary<Item, (float left, float right)> reach)
    {
        Character local = Local;
        Vector2 look = local.data.lookValues;
        Backpack pack = SpawnInFront(prefab, 1.4f)?.GetComponent<Backpack>();
        if (pack == null)
        {
            Fail($"a {name} can be put on the ground");
            yield break;
        }
        yield return new WaitForSeconds(0.5f);
        local.refs.items.lastEquippedSlotTime = 0f;
        pack.Wear(local);
        for (float until = Time.realtimeSinceStartup + 4f; Time.realtimeSinceStartup < until && local.player.backpackSlot.IsEmpty();)
        {
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);
        local.refs.items.lastEquippedSlotTime = 0f;
        local.refs.items.EquipSlot(Optionable<byte>.Some(3));
        local.data.lookValues = new Vector2(look.x, 0f);
        yield return new WaitForSeconds(1f);
        Item inHand = local.data.currentItem;
        if (inHand == null || inHand.itemID != prefab.itemID)
        {
            Fail($"the worn {name} comes into the hands", $"in the hands: {(inHand != null ? inHand.gameObject.name : "nothing")}");
            yield break;
        }
        reach[prefab] = (FromThePack(inHand, local.GetBodypart(BodypartType.Hand_L).transform.position), FromThePack(inHand, local.GetBodypart(BodypartType.Hand_R).transform.position));
        yield return HeldPicture(local, inHand, "held-" + prefab.gameObject.name.ToLowerInvariant());

        SoundsPlayed.Heard.Clear();
        SoundsPlayed.Listening = true;
        try
        {
            if (way != Vector3.zero)
            {
                local.data.lookValues = new Vector2(Quaternion.LookRotation(way).eulerAngles.y, 0f);
                local.data.currentStamina = 1f;
                forcedMove = new Vector2(0f, 1f);
                forcedSprint = true;
                forcingInput = true;
                yield return new WaitForSeconds(0.6f);
            }
            for (int jump = 0; jump < 3; jump++)
            {
                local.data.currentStamina = 1f;
                Step($"jump with the {name} in the hands" + (way != Vector3.zero ? ", running" : ""), () => local.refs.movement.TryToJump());
                yield return new WaitForSeconds(1.3f);
            }
            forcingInput = false;
            yield return new WaitForSeconds(0.5f);
            int held = SoundsPlayed.Heard.Count(metal.Contains);

            // Dropped to the ground from above the head.
            SoundsPlayed.Heard.Clear();
            HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
            Step($"drop the {name} to the ground", () =>
            {
                local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 0f, (byte)3, local.Head + local.data.lookDirection_Flat * 0.6f + Vector3.up * 0.5f, Vector3.zero, inHand.transform.rotation, local.player.GetItemSlot(3).data, false);
                local.refs.items.EquipSlot(Optionable<byte>.None);
            });
            yield return new WaitForSeconds(2f);
            knocks[prefab] = (held, SoundsPlayed.Heard.Count(metal.Contains));
            foreach (Item dropped in Item.ALL_ITEMS.Where(i => i != null && !before.Contains(i) && i.itemID == prefab.itemID && i.photonView != null).ToList())
            {
                PhotonNetwork.Destroy(dropped.gameObject);
            }
        }
        finally
        {
            forcingInput = false;
            SoundsPlayed.Listening = false;
            SoundsPlayed.Heard.Clear();
        }
        yield return new WaitForSeconds(0.3f);
    }

    /// <summary>A way along flat, open ground from where the scout stands, at least this long (zero when there is none).</summary>
    private static Vector3 OpenWay(Character local, float length)
    {
        if (!Physics.Raycast(local.Center + Vector3.up, Vector3.down, out RaycastHit there, 6f, HelperFunctions.terrainMapMask))
        {
            return Vector3.zero;
        }
        for (int angle = 0; angle < 360; angle += 15)
        {
            Vector3 way = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            bool flat = true;
            for (float d = 1f; d <= length && flat; d += 1f)
            {
                Vector3 p = there.point + way * d;
                flat = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit floor, 8f, HelperFunctions.terrainMapMask) && floor.normal.y >= 0.85f
                    && Mathf.Abs(floor.point.y - there.point.y) < 1.5f
                    && !Physics.CheckCapsule(floor.point + Vector3.up * 0.6f, floor.point + Vector3.up * 1.6f, 0.35f, HelperFunctions.terrainMapMask)
                    && !Hazard(floor.point + Vector3.up, 1.5f);
            }
            if (flat)
            {
                return way;
            }
        }
        return Vector3.zero;
    }

    /// <summary>How far a point is from the held pack's surface (its solid colliders; 0 inside).</summary>
    private static float FromThePack(Item item, Vector3 point)
    {
        float nearest = float.MaxValue;
        foreach (Collider collider in item.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || (collider is MeshCollider mesh && !mesh.convex))
            {
                continue;
            }
            nearest = Mathf.Min(nearest, Vector3.Distance(point, collider.ClosestPoint(point)));
        }
        return nearest;
    }

    /// <summary>A picture of the scout holding an item, from the front and a little to the side.</summary>
    private IEnumerator HeldPicture(Character local, Item item, string name)
    {
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        try
        {
            Camera camera = holder.AddComponent<Camera>();
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.05f;
            Vector3 forward = local.data.lookDirection_Flat.sqrMagnitude > 0.01f ? local.data.lookDirection_Flat.normalized : local.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 at = item.transform.position;
            holder.transform.position = at + forward * 2.2f + right * 1.3f + Vector3.up * 0.2f;
            holder.transform.LookAt(at);
            yield return new WaitForSeconds(0.3f);
            Screenshot(name);
            yield return new WaitForSeconds(0.3f);
        }
        finally
        {
            Destroy(holder);
        }
    }
}
