using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Peak;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UltimateAscent.Features.Fixes;
using UnityEngine;
using Zorro.Core;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the game's bugs the mod fixes on every ascent (the user's list of 2026-11-04).</summary>
internal sealed partial class Runner
{
    /// <summary>The first item in the database that carries a component of this kind.</summary>
    private static Item ItemWith<T>() where T : Component =>
        SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i != null && i.GetComponent<T>() != null);

    /// <summary>The explosion a stick of dynamite makes (its fuse's), for the checks that need one.</summary>
    private static GameObject DynamiteBlast() => ItemWith<Dynamite>()?.GetComponent<Dynamite>()?.explosionPrefab;

    /// <summary>The highest the local scout's middle rose over a spot's height during the last measure.</summary>
    private float peak;

    /// <summary>
    /// The game's bugs, fixed on every ascent (gamebugs; on a map with the Roots, 81): on the beach, the fog walls (not
    /// held through, not stood on), a scout stuck on a cactus gets no stamina off the ground, the Jetpack gives nothing
    /// to a knocked-down scout and pushes as at 60 fps, a drop with the hand in the ground appears out of it, a stick of
    /// dynamite explodes once, balloons pop in a blast (and from 15 after 30 s), a Warp Fungus an explosion throws flies
    /// as a scout would, a warp finds room, the Scout Cannon is thrown with half the force from 15, a backpack fired from
    /// it flies off without the scout, a flag brings a scout who died invincible back with its statuses, and two
    /// Anti-Zooka bubbles lift no higher than one; in the Roots, a limp scout's mushroom bounce and a warp onto a
    /// mushroom mid-fall are plain bounces, and a scout a condor carries holds no vine; the Nadir's wall, where there is
    /// one, lets no warp through.
    /// </summary>
    private IEnumerator GameBugChecks()
    {
        Character local = Local;
        Protect(true);
        yield return UntilUp(local);
        Vector3 open = GroundUnder(local.Center);
        Info("the game's bugs: where the checks start", $"at {open:F0} ({Map.CurrentPlace})");

        yield return FogWallChecks(open);
        yield return BackTo(open);
        yield return CactusStamina(open);
        yield return BackTo(open);
        yield return JetpackKnockedDown(open);
        yield return BackTo(open);
        yield return SafeDropCheck();
        yield return DynamiteOnceCheck();
        yield return BalloonBlast();
        yield return FungusBlast();
        yield return BackTo(open);
        yield return WarpRoom();
        yield return BackTo(open);
        yield return CannonThrowCheck();
        yield return CannonBackpack(open);
        yield return BackTo(open);
        yield return FlagWithMilk();
        yield return BackTo(open);
        yield return AntiBubbles(open);
        yield return BackTo(open);
        yield return BalloonTimer();
        yield return CarryLooseEnds(open);
        yield return NadirWall();

        if (Map.CurrentPlace != Place.Roots)
        {
            MapHandler.JumpToSegment(Segment.Tropics);
            yield return new WaitForSeconds(8f);
        }
        if (Map.CurrentPlace == Place.Roots)
        {
            yield return MushroomBounces();
            yield return CondorVine();
            yield return ShotRopeStays();
        }
        else
        {
            Info("the Roots' checks are skipped", $"no Roots here ({Map.CurrentPlace}); run with -Biome 81");
        }
        Protect(true);
    }

    private IEnumerator BackTo(Vector3 spot)
    {
        Character local = Local;
        local.data.carrierCondor = null;
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spot + Vector3.up * 1.2f, false);
        yield return new WaitForSeconds(1.5f);
        yield return UntilUp(local);
    }

    /// <summary>The face of a fog wall's slab most turned up, and how far off level it is.</summary>
    private static Vector3 FaceUp(Collider wall) => Vector3.Dot(wall.transform.up, Vector3.up) >= 0f ? wall.transform.up : -wall.transform.up;

    private IEnumerator FogWallChecks(Vector3 open)
    {
        Character local = Local;
        MapHandler map = Singleton<MapHandler>.Instance;
        List<Collider> walls = map.segments.Where(s => s != null).SelectMany(s => new[] { s.wallNext, s.wallPrevious }).Where(w => w != null)
            .SelectMany(w => w.GetComponentsInChildren<Collider>(true)).Distinct().ToList();
        Info("the fog walls between the segments", string.Join("; ", walls.Select(c =>
            $"{c.transform.parent?.name}/{c.name} on {LayerMask.LayerToName(c.gameObject.layer)} {(c.enabled && c.gameObject.activeInHierarchy ? "on" : "off")}, its face {Vector3.Angle(FaceUp(c), Vector3.up):0}° off level, at {c.bounds.center:F0}")));
        Check("the game's fog walls are known for what they are (nothing to stand on, hold or stop at)", walls.Count > 0 && walls.All(Map.IsFogWall),
            $"{walls.Count} walls, all known {walls.All(Map.IsFogWall)}");
        Collider wall = walls.FirstOrDefault(c => c.enabled && c.gameObject.activeInHierarchy);
        if (wall == null)
        {
            Info("the fog wall checks are skipped", "no fog wall is up here");
            yield break;
        }

        // Pressed against a fog wall, a hold of what is behind it (as the hands' look through the slab finds it).
        Vector3 normal = wall.transform.up;
        if (Vector3.Dot(open - wall.bounds.center, normal) < 0f)
        {
            normal = -normal;
        }
        Vector3 face = wall.ClosestPoint(wall.bounds.center + normal * 100f);
        int refused = ClimbFixes.GrabsRefused;
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, face + normal * 1.2f, false);
        for (float until = Time.time + 3f; Time.time < until && (local.warping || Vector3.Distance(local.Center, face + normal * 1.2f) > 3f);)
        {
            yield return null;
        }
        Vector3 at = local.Center;
        float off = Vector3.Dot(at - face, normal);
        Vector3 behind = at - normal * (off + 0.8f);
        local.refs.climbing.view.RPC("StartClimbRpc", RpcTarget.All, behind, normal);
        yield return new WaitForSeconds(0.3f);
        bool climbing = local.data.isClimbing;
        Check("a fog wall cannot be climbed: nothing behind it is held through it",
            !climbing && ClimbFixes.GrabsRefused > refused && Map.ThroughFogWall(at, behind),
            $"at {wall.transform.parent?.name}, {off:0.0} m off its face; a hold 0.8 m behind the face started a climb {climbing}; grabs refused {ClimbFixes.GrabsRefused - refused}");
        if (local.data.isClimbing)
        {
            local.refs.climbing.view.RPC("StopClimbingRpc", RpcTarget.All, 0f);
        }

        // Standing on a fog wall flat enough for the game to take it for ground (up to 50°).
        Collider floor = walls.Where(c => Vector3.Angle(FaceUp(c), Vector3.up) < 50f).OrderBy(c => Vector3.Distance(c.bounds.center, open)).FirstOrDefault();
        if (floor == null)
        {
            Info("the fog wall ground check is skipped", "no fog wall lies flat enough to stand on");
            yield break;
        }
        // The game turns its walls on by segment: this one on for the check, if it is off here, and back after.
        GameObject floorWall = floor.transform.parent != null && floor.transform.parent.name.StartsWith("Fogwall", StringComparison.Ordinal) ? floor.transform.parent.gameObject : floor.gameObject;
        bool wasOn = floorWall.activeSelf;
        floorWall.SetActive(true);
        try
        {
        yield return BackTo(open);
        Vector3 up = FaceUp(floor);
        Vector3 top = floor.ClosestPoint(floor.bounds.center + up * 100f);
        int refusedGround = ClimbFixes.GroundRefused;
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, top + up * 1.3f, false);
        for (float until = Time.time + 3f; Time.time < until && local.warping;)
        {
            yield return null;
        }
        int groundedOnIt = 0, frames = 0;
        for (float until = Time.time + 1.5f; Time.time < until;)
        {
            yield return new WaitForFixedUpdate();
            frames++;
            float above = Vector3.Dot(local.Center - top, up);
            if (local.data.isGrounded && above < 1.6f && floor.bounds.Contains(floor.ClosestPoint(local.Center)))
            {
                groundedOnIt++;
            }
        }
        Check("a fog wall is no ground: a scout on one flat enough to stand on slides off it, with no footing (nor stamina back)",
            ClimbFixes.GroundRefused > refusedGround && groundedOnIt == 0,
            $"on {floor.transform.parent?.name} ({Vector3.Angle(up, Vector3.up):0}° off level{(wasOn ? "" : ", switched on for the check")}): touches of it not taken as ground {ClimbFixes.GroundRefused - refusedGround}; physics steps with footing on it {groundedOnIt} of {frames}");
        }
        finally
        {
            floorWall.SetActive(wasOn);
        }
    }

    private IEnumerator CactusStamina(Vector3 open)
    {
        Character local = Local;
        // Off the ground, stuck by a hand as to a cactus on a wall.
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, open + Vector3.up * 4f, false);
        for (float until = Time.time + 3f; Time.time < until && local.warping;)
        {
            yield return null;
        }
        Bodypart hand = local.GetBodypart(BodypartType.Hand_R);
        // The warp ends with the game's own unsticking, after which a scout sticks to no cactus for 3 s: that pause over.
        local.data.sinceUnstuck = 5f;
        bool stuck = local.TryStickBodypart(hand, hand.transform.position, Status.Injury, 0f);
        yield return new WaitForSeconds(0.4f);
        local.data.currentStamina = 0.4f;
        local.data.sinceUseStamina = 5f;
        float before = local.data.currentStamina;
        bool stayedStuck = true;
        for (float until = Time.time + 2f; Time.time < until;)
        {
            yield return null;
            stayedStuck &= local.IsStuck();
        }
        float inAir = local.data.currentStamina - before;
        Check("stuck on a cactus off the ground (on a wall), a scout gets no stamina back (the game: 20 a second)",
            stuck && stayedStuck && inAir < 0.02f,
            $"stuck {stuck}, the whole time {stayedStuck}; stamina {before * 100f:0} -> {local.data.currentStamina * 100f:0} in 2 s, {local.data.sinceGrounded:0.00} s off the ground");
        for (float until = Time.time + 6f; Time.time < until && local.IsStuck();)
        {
            yield return null;
        }
        yield return UntilUp(local);
        yield return new WaitForSeconds(3.2f);

        // On the ground stuck, stamina comes back as it does unstuck.
        hand = local.GetBodypart(BodypartType.Hand_R);
        bool stuckDown = local.TryStickBodypart(hand, hand.transform.position, Status.Injury, 0f);
        yield return new WaitForSeconds(0.3f);
        local.data.currentStamina = 0.4f;
        local.data.sinceUseStamina = 5f;
        before = local.data.currentStamina;
        yield return new WaitForSeconds(2f);
        float onGround = local.data.currentStamina - before;
        Check("stuck on a cactus on the ground, stamina comes back as it does unstuck",
            stuckDown && onGround > 0.2f,
            $"stuck {stuckDown}; stamina {before * 100f:0} -> {local.data.currentStamina * 100f:0} in 2 s (unstuck on the ground: 40)");
        for (float until = Time.time + 6f; Time.time < until && local.IsStuck();)
        {
            yield return null;
        }
        local.data.currentStamina = 1f;
    }

    private static float JetpackFuel(Character scout) =>
        scout.player.backpackSlot.data != null && scout.player.backpackSlot.data.TryGetDataEntry(DataEntryKey.Fuel, out FloatItemData fuel) ? fuel.Value : -1f;

    private IEnumerator JetpackKnockedDown(Vector3 open)
    {
        Character local = Local;
        Item jetpack = ItemByName("Jetpack");
        if (jetpack == null)
        {
            Fail("the Jetpack is in the item database");
            yield break;
        }
        yield return WearJetpack(jetpack, ItemByName("Granola Bar"));
        if (local.player.backpackSlot.IsEmpty() || JetpackFuel(local) <= 0f)
        {
            Fail("wear a fuelled Jetpack", $"backpack slot empty {local.player.backpackSlot.IsEmpty()}, fuel {JetpackFuel(local):0}");
            yield break;
        }
        MethodInfo fly = AccessTools.Method(typeof(CharacterMovement), "TryJetpack");
        yield return BackTo(open);

        // Knocked down (as by an explosion or a Rescue Claw), the Jetpack held on.
        local.Fall(2f);
        float y0 = local.Center.y;
        float fuel0 = JetpackFuel(local);
        int held0 = LaunchFixes.JetpackHeld;
        float rose = 0f;
        for (float until = Time.time + 1.5f; Time.time < until;)
        {
            fly.Invoke(local.refs.movement, null);
            rose = Mathf.Max(rose, local.Center.y - y0);
            yield return null;
        }
        Check("the Jetpack gives a knocked-down scout no push (nor burns fuel): no flight out of an explosion or a Rescue Claw's pull",
            rose < 0.6f && LaunchFixes.JetpackHeld > held0 && fuel0 - JetpackFuel(local) < 0.5f,
            $"held on for 1.5 s while knocked down: rose {rose:0.00} m, fuel {fuel0:0.0} -> {JetpackFuel(local):0.0}, pushes held back {LaunchFixes.JetpackHeld - held0}");
        yield return UntilUp(local);
        yield return new WaitForSeconds(2f);

        // In control, in the air (as after a jump): it flies, as at 60 fps.
        CharacterMovement movement = local.refs.movement;
        float original = Overrides.Original(movement, nameof(CharacterMovement.jetpackForce), movement.jetpackForce);
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, open + Vector3.up * 4f, false);
        for (float until = Time.time + 3f; Time.time < until && local.warping;)
        {
            yield return null;
        }
        float y1 = local.Center.y;
        float pushSeen = 0f, paceSeen = 0f;
        for (float until = Time.time + 1f; Time.time < until;)
        {
            fly.Invoke(movement, null);
            pushSeen = movement.jetpackForce;
            paceSeen = Time.deltaTime / Time.fixedDeltaTime;
            yield return null;
        }
        float flew = local.Center.y - y1;
        float wanted = original * Mathf.Clamp01(local.data.currentRagdollControll) * Mathf.Clamp(paceSeen, 0f, 3f);
        Check("in control the Jetpack flies, pushing as at 60 fps whatever the frame rate",
            flew > -1f && Mathf.Abs(pushSeen - wanted) < original * 0.05f,
            $"in the air for 1 s: {flew:0.0} m up (a fall would be 10 m down); its push a drawn frame {pushSeen:0.00} at {1f / Time.smoothDeltaTime:0} fps (the game: {original:0.0} every frame; as at 60 fps: {wanted:0.00})");
        local.player.EmptySlot(Optionable<byte>.Some((byte)3), true);
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>
    /// A dropped item on the scout's side of the ground: nothing solid between the scout's middle and it (the fix's own
    /// promise), or not under the ground where the scout stands (on a slope it lies lower, on the surface).
    /// </summary>
    private static bool OutOfTheGround(Character scout, Vector3 at, Vector3 ground)
    {
        return !Physics.Linecast(scout.Center, at, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore) || at.y > ground.y - 0.05f;
    }

    private IEnumerator SafeDropCheck()
    {
        Character local = Local;
        yield return SpawnIntoHand(ItemByName("Granola Bar"));
        Item bar = held;
        if (bar == null)
        {
            yield break;
        }
        Vector3 ground = GroundUnder(local.Center);
        Vector3 inGround = ground - Vector3.up * 0.6f;
        HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
        int moved = ItemFixes.DropsMoved;
        Step("drop an item with the hand in the ground", () =>
        {
            byte slot = local.refs.items.currentSelectedSlot.Value;
            local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 0f, slot, inGround, Vector3.zero, bar.transform.rotation, local.player.GetItemSlot(slot).data, false);
        });
        Item dropped = null;
        for (float until = Time.time + 2f; Time.time < until && dropped == null;)
        {
            dropped = Item.ALL_ITEMS.FirstOrDefault(i => i != null && !before.Contains(i) && i.itemID == bar.itemID);
            yield return null;
        }
        yield return new WaitForSeconds(0.6f);
        Vector3 at = dropped != null ? dropped.Center() : Vector3.zero;
        Check("an item dropped with the hand in the ground appears short of it, not in it (nothing dropped falls into the rock)",
            dropped != null && ItemFixes.DropsMoved > moved && OutOfTheGround(local, at, ground),
            $"the hand 0.6 m under the ground at {ground:F1}: the item {(dropped != null ? $"at {at:F2}, {at.y - ground.y:0.00} m over the ground there, rock between the scout and it {Physics.Linecast(local.Center, at, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)}" : "not found")}; drops moved {ItemFixes.DropsMoved - moved}");
        if (dropped != null && dropped.photonView != null)
        {
            PhotonNetwork.Destroy(dropped.gameObject);
        }

        // Out of a slot (the item-dropping rules, a scout passing out: the backpack too), toward a spot in the ground.
        yield return SpawnIntoHand(ItemByName("Granola Bar"));
        if (held == null)
        {
            yield break;
        }
        byte pocket = local.refs.items.currentSelectedSlot.Value;
        int itemID = held.itemID;
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.5f);
        ground = GroundUnder(local.Center);
        inGround = ground - Vector3.up * 0.6f;
        before = new HashSet<Item>(Item.ALL_ITEMS);
        moved = ItemFixes.DropsMoved;
        Step("drop an item out of a slot toward a spot in the ground", () => local.refs.items.photonView.RPC("DropItemFromSlotRPC", RpcTarget.All, pocket, inGround));
        dropped = null;
        for (float until = Time.time + 2f; Time.time < until && dropped == null;)
        {
            dropped = Item.ALL_ITEMS.FirstOrDefault(i => i != null && !before.Contains(i) && i.itemID == itemID);
            yield return null;
        }
        yield return new WaitForSeconds(0.6f);
        at = dropped != null ? dropped.Center() : Vector3.zero;
        Check("an item dropped out of a slot (the dropping rules, passing out, the backpack too) appears short of the ground, not in it",
            dropped != null && ItemFixes.DropsMoved > moved && OutOfTheGround(local, at, ground),
            $"aimed 0.6 m under the ground at {ground:F1}: the item {(dropped != null ? $"at {at:F2}, {at.y - ground.y:0.00} m over the ground there, rock between the scout and it {Physics.Linecast(local.Center, at, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore)}" : "not found")}; drops moved {ItemFixes.DropsMoved - moved}");
        if (dropped != null && dropped.photonView != null)
        {
            PhotonNetwork.Destroy(dropped.gameObject);
        }
    }

    /// <summary>How many explosions of these kinds are in the scene now.</summary>
    private static int Blasts(params string[] names) =>
        UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.parent == null && names.Any(n => n != null && t.name == n + "(Clone)"));

    private IEnumerator DynamiteOnceCheck()
    {
        Item prefab = ItemWith<Dynamite>();
        GameObject stick = prefab != null ? SpawnInFront(prefab, 7f) : null;
        if (stick == null)
        {
            Fail("a stick of dynamite on the ground");
            yield break;
        }
        yield return new WaitForSeconds(1f);
        Item item = stick.GetComponent<Item>();
        Dynamite dynamite = stick.GetComponent<Dynamite>();
        ItemCooking cooking = stick.GetComponent<ItemCooking>();
        string fuse = dynamite.explosionPrefab != null ? dynamite.explosionPrefab.name : null;
        string cooked = cooking != null && cooking.explosionPrefab != null ? cooking.explosionPrefab.name : null;
        int blastsBefore = Blasts(fuse, cooked);
        int repeats = ItemFixes.RepeatBlasts;
        Step("set the stick off by its fuse and by cooking in the same moment", () =>
        {
            AccessTools.Method(typeof(Dynamite), "RPC_Explode").Invoke(dynamite, null);
            if (cooking != null)
            {
                AccessTools.Method(typeof(ItemCooking), "CookingExplodeImpl").Invoke(cooking, null);
            }
        });
        yield return new WaitForSeconds(0.3f);
        int blasts = Blasts(fuse, cooked) - blastsBefore;
        Check("a stick of dynamite explodes once, however it is set off (its fuse and cooking both)",
            blasts == 1 && ItemFixes.RepeatBlasts > repeats,
            $"explosions {blasts} ({fuse}, {cooked}); second ones held back {ItemFixes.RepeatBlasts - repeats}");
        if (item != null && item.photonView != null && item.photonView.IsMine)
        {
            PhotonNetwork.Destroy(item.gameObject);
        }
        yield return new WaitForSeconds(1f);
        ClearStatuses(Afflictions);
    }

    private IEnumerator BalloonBlast()
    {
        Character local = Local;
        GameObject blast = DynamiteBlast();
        if (blast == null)
        {
            Fail("an explosion for the balloon check");
            yield break;
        }
        CharacterBalloons balloons = local.refs.balloons;
        local.refs.balloons.TieNewBalloon(0);
        yield return new WaitForSeconds(1.2f);
        TiedBalloon balloon = balloons.tiedBalloons.LastOrDefault();
        if (balloon == null)
        {
            Fail("tie a balloon");
            yield break;
        }
        int count = balloons.tiedBalloons.Count;
        int popped = ItemFixes.BalloonsPopped;
        UnityEngine.Object.Instantiate(blast, balloon.rb.position + Vector3.right * 2f, Quaternion.identity);
        yield return new WaitForSeconds(0.6f);
        Check("a tied balloon pops in an explosion that reaches it",
            balloons.tiedBalloons.Count == count - 1 && ItemFixes.BalloonsPopped > popped && ItemFixes.LastPop == "blast",
            $"balloons {count} -> {balloons.tiedBalloons.Count}; popped by the mod {ItemFixes.BalloonsPopped - popped} ({ItemFixes.LastPop})");
        yield return UntilUp(local);
        ClearStatuses(Afflictions);
        foreach (TiedBalloon left in balloons.tiedBalloons.ToList())
        {
            left.Pop();
        }
    }

    private IEnumerator BalloonTimer()
    {
        if (!Rules.Enabled(Lvl.WorseItems))
        {
            Info("the balloon timer check is skipped", $"testing ascent {target}: it comes from 15");
            yield break;
        }
        CharacterBalloons balloons = Local.refs.balloons;
        balloons.TieNewBalloon(4);
        yield return new WaitForSeconds(1f);
        TiedBalloon balloon = balloons.tiedBalloons.LastOrDefault();
        if (balloon == null)
        {
            Fail("tie a balloon");
            yield break;
        }
        float tied = Time.time - 1f;
        bool noHeight = float.IsPositiveInfinity(balloon.popHeight);
        float timer = balloon.popTime;
        while (balloon != null && Time.time - tied < 34f)
        {
            yield return new WaitForSeconds(0.25f);
        }
        float lasted = Time.time - tied;
        Check("from ascent 15 a tied balloon pops 30 s after it is tied, however high it rises (the game: 120 s, or 100 m up)",
            noHeight && Mathf.Approximately(timer, A14_WorseItems.BalloonSeconds) && balloon == null && lasted > 29f && lasted < 31.5f,
            $"its timer {timer:0} s, its height limit {(noHeight ? "none" : balloon != null ? balloon.popHeight.ToString("0") : "?")}; it lasted {lasted:0.0} s");
    }

    private IEnumerator FungusBlast()
    {
        Character local = Local;
        Item fungus = ItemByName("WarpFungus");
        GameObject blast = DynamiteBlast();
        GameObject lying = fungus != null ? SpawnInFront(fungus, 4f) : null;
        if (lying == null || blast == null)
        {
            Fail("a Warp Fungus on the ground and an explosion");
            yield break;
        }
        yield return new WaitForSeconds(1.5f);
        Rigidbody rig = lying.GetComponent<Item>().rig;
        bool stillHeld = rig.isKinematic;
        if (stillHeld)
        {
            lying.GetComponent<PhotonView>().RPC("SetKinematicRPC", RpcTarget.All, false, lying.transform.position, lying.transform.rotation);
            yield return new WaitForSeconds(0.5f);
        }
        Vector3 away = Vector3.ProjectOnPlane(rig.position - local.Center, Vector3.up).normalized;
        AOE aoe = blast.GetComponentInChildren<AOE>(true);
        int eased = WarpFixes.BlastsEased;
        float fastest = 0f;
        for (int attempt = 0; attempt < 2 && WarpFixes.BlastsEased == eased; attempt++)
        {
            if (rig == null)
            {
                break;
            }
            UnityEngine.Object.Instantiate(blast, rig.position + Vector3.up * 0.6f - away * 1.2f, Quaternion.identity);
            for (float until = Time.time + 0.4f; Time.time < until;)
            {
                yield return new WaitForFixedUpdate();
                if (rig != null)
                {
                    fastest = Mathf.Max(fastest, rig.linearVelocity.magnitude);
                }
            }
        }
        float scoutLike = aoe != null ? (aoe.knockback + Mathf.Abs(aoe.additionalVerticalKnockback)) * Time.fixedDeltaTime : 25f;
        Check("a Warp Fungus an explosion reaches flies off once, as hard as a scout there would",
            WarpFixes.BlastsEased > eased && fastest > 1f && fastest <= scoutLike * 1.6f + 2f,
            $"{(stillHeld ? "(it lay held still: let loose) " : "")}its fastest {fastest:0.0} m/s (a scout there: about {scoutLike:0.0} m/s); the explosion's push {(aoe != null ? aoe.knockback : 0f):0}, for items x{(aoe != null ? aoe.itemKnockbackMultiplier : 0f):0.##}; throws eased {WarpFixes.BlastsEased - eased}");
        if (lying != null && lying.GetComponent<PhotonView>() != null)
        {
            PhotonNetwork.Destroy(lying);
        }
        yield return UntilUp(local);
        ClearStatuses(Afflictions);
    }

    /// <summary>
    /// A way to throw from where the scout stands: no rock for 12 m and ground there no lower than here (not out over the
    /// sea), nearest the way to the peak.
    /// </summary>
    private static Vector3 OpenWay(Character scout)
    {
        float here = GroundUnder(scout.Center).y;
        Vector3 best = Vector3.forward;
        float nearest = float.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            Vector3 way = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
            Vector3 there = scout.Center + way * 12f;
            if (HelperFunctions.LineCheck(scout.Center + Vector3.up, there + Vector3.up, HelperFunctions.LayerType.TerrainMap).transform != null
                || !Physics.Raycast(there + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 10f, HelperFunctions.terrainMapMask)
                || ground.point.y < here - 0.5f)
            {
                continue;
            }
            float off = Vector3.Angle(way, Vector3.forward);
            if (off < nearest)
            {
                nearest = off;
                best = way;
            }
        }
        return best;
    }

    private IEnumerator WarpRoom()
    {
        Character local = Local;
        Vector3 ground = GroundUnder(local.Center);
        Vector3 spot = ground - Vector3.up * 0.2f;
        bool room = WarpFixes.FindRoom(ref spot, new[] { Vector3.up });
        Check("a warp that would put the scout into the rock moves out to where there is room",
            room && spot.y > ground.y && !Physics.CheckSphere(spot, 0.29f, HelperFunctions.terrainMapMask),
            $"aimed 0.2 m under the ground at {ground:F1}: room found {room}, at {spot.y - ground.y:0.00} m over the ground");

        Item fungus = ItemByName("WarpFungus");
        yield return SpawnIntoHand(fungus);
        if (held == null)
        {
            yield break;
        }
        WarpOnThrow warp = held.GetComponent<WarpOnThrow>();
        Info("the Warp Fungus's warp", warp == null ? "none" : $"faster than {warp.minVelocity} m/s, {warp.minTime}-{warp.maxTime} s after the throw, {warp.moveAwayFromWallDistance} m off the surface");
        Vector2 look = local.data.lookValues;
        Vector3 inland = OpenWay(local);
        local.data.lookValues = new Vector2(HelperFunctions.DirectionToLook(inland).x, 20f);
        yield return new WaitForSeconds(0.5f);
        Vector3 lastWarp = WarpFixes.LastWarp;
        int fizzled = WarpFixes.WarpsFizzled;
        Step("throw a Warp Fungus onto open ground", () =>
        {
            byte slot = local.refs.items.currentSelectedSlot.Value;
            local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 1f, slot, held.transform.position, Vector3.zero, held.transform.rotation, local.player.GetItemSlot(slot).data, false);
        });
        local.data.lookValues = look;
        for (float until = Time.time + 6f; Time.time < until && WarpFixes.LastWarp == lastWarp && WarpFixes.WarpsFizzled == fizzled;)
        {
            yield return null;
        }
        for (float until = Time.time + 3f; Time.time < until && local.warping;)
        {
            yield return null;
        }
        bool warped = WarpFixes.LastWarp != lastWarp;
        // Where the scout came out: the nearest they were to the warp's spot in the 1.2 s after it (something
        // there, a mushroom, may throw them on at once), and never inside the rock.
        float nearest = float.MaxValue;
        bool inRock = false;
        Vector3 cameOut = local.Center;
        for (float until = Time.time + 1.2f; Time.time < until && warped;)
        {
            yield return null;
            float off = Vector3.Distance(local.Center, WarpFixes.LastWarp);
            if (off < nearest)
            {
                nearest = off;
                cameOut = local.Center;
            }
            inRock |= Physics.CheckSphere(local.Center, 0.2f, HelperFunctions.terrainMapMask);
        }
        Check("a Warp Fungus thrown onto open ground warps the scout there, as in the game, with room for them",
            warped && nearest < 2.5f && !inRock,
            $"warped {warped} to {WarpFixes.LastWarp:F1}, the scout came out at {cameOut:F1} ({nearest:0.0} m off), in the rock {inRock}, at {local.Center:F1} after; fizzled {WarpFixes.WarpsFizzled - fizzled}, moved for room {WarpFixes.WarpsMoved}");
    }

    private IEnumerator CannonThrowCheck()
    {
        if (target < Lvl.WorseItems)
        {
            Info("the Scout Cannon's throw check is skipped", $"testing ascent {target}: it comes from 15");
            yield break;
        }
        Character local = Local;
        Item cannon = ItemWith<Action_ConstructableScoutCannonScroll>();
        if (cannon == null)
        {
            Fail("the Scout Cannon is in the item database");
            yield break;
        }
        ConfigEntryHolder rule = new ConfigEntryHolder(UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems]);
        Vector2 look = local.data.lookValues;
        float[] speeds = new float[2];
        try
        {
            for (int pass = 0; pass < 2; pass++)
            {
                rule.Set(pass == 0);
                yield return SpawnIntoHand(cannon);
                if (held == null)
                {
                    continue;
                }
                local.data.lookValues = new Vector2(look.x, 0f);
                yield return new WaitForSeconds(0.5f);
                HashSet<Item> before = new HashSet<Item>(Item.ALL_ITEMS);
                Step("throw the Scout Cannon", () =>
                {
                    byte slot = local.refs.items.currentSelectedSlot.Value;
                    local.refs.items.photonView.RPC("DropItemRpc", RpcTarget.All, 1f, slot, held.transform.position, Vector3.zero, held.transform.rotation, local.player.GetItemSlot(slot).data, false);
                });
                Item thrown = null;
                for (float until = Time.time + 2f; Time.time < until && thrown == null;)
                {
                    thrown = Item.ALL_ITEMS.FirstOrDefault(i => i != null && !before.Contains(i) && i.itemID == cannon.itemID && i.rig != null);
                    if (thrown == null)
                    {
                        yield return null;
                    }
                }
                speeds[pass] = thrown != null ? Vector3.ProjectOnPlane(thrown.rig.linearVelocity, Vector3.up).magnitude : -1f;
                if (thrown != null && thrown.photonView != null)
                {
                    PhotonNetwork.Destroy(thrown.gameObject);
                }
                yield return new WaitForSeconds(0.5f);
            }
        }
        finally
        {
            rule.Restore();
            local.data.lookValues = look;
        }
        float game = local.refs.items.maxThrowForce * 0.5f * cannon.throwForceMultiplier;
        Check("from ascent 15 the Scout Cannon is thrown (Q) with half the force; with ascent 15's rule off, as in the game",
            speeds[0] > 0f && Mathf.Abs(speeds[0] - game * LaunchShare()) < game * 0.03f && Mathf.Abs(speeds[1] - game) < game * 0.03f,
            $"thrown at {speeds[0]:0.##} m/s (half: {game * LaunchShare():0.##}); with the rule off {speeds[1]:0.##} m/s (the game: {game:0.##})");
    }

    private static float LaunchShare() => A14_WorseItems.CannonThrowShare;

    private IEnumerator CannonBackpack(Vector3 open)
    {
        Character local = Local;
        Item cannonItem = ItemWith<Action_ConstructableScoutCannonScroll>();
        Constructable build = cannonItem != null ? cannonItem.GetComponent<Constructable>() : null;
        if (build == null || build.constructedPrefab == null)
        {
            Fail("the Scout Cannon can be set up");
            yield break;
        }
        // The cannon glitch rule (15) knocks every scout out at a shot with an item and a scout: off meanwhile (in memory).
        ConfigEntryHolder worse = new ConfigEntryHolder(UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems]);
        GameObject placed = null, pack = null;
        try
        {
            worse.Set(false);
            Vector3 ahead = Vector3.ProjectOnPlane(local.data.lookDirection, Vector3.up).normalized;
            if (ahead.sqrMagnitude < 0.01f)
            {
                ahead = Vector3.forward;
            }
            Vector3 spot = GroundUnder(open + ahead * 5f);
            placed = PhotonNetwork.Instantiate(build.constructedPrefab.name, spot, Quaternion.LookRotation(ahead), 0);
            yield return new WaitForSeconds(1.5f);
            ScoutCannon cannon = placed != null ? placed.GetComponentInChildren<ScoutCannon>() : null;
            if (cannon == null)
            {
                Fail("set up a Scout Cannon", "no cannon");
                yield break;
            }
            pack = PhotonNetwork.InstantiateItemRoom("Backpack", cannon.tube.position, Quaternion.identity);
            PhotonView packView = pack != null ? pack.GetComponent<PhotonView>() : null;
            packView?.RPC("SetKinematicRPC", RpcTarget.All, false, pack.transform.position, pack.transform.rotation);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, cannon.entry.position, false);
            for (float until = Time.time + 3f; Time.time < until && local.warping;)
            {
                yield return null;
            }
            cannon.target = local;
            int alone = LaunchFixes.BackpacksAlone;
            Step("light the Scout Cannon with a backpack and the scout in it", () => cannon.Light());
            float fastest = 0f;
            for (float until = Time.time + cannon.fireTime + 3f; Time.time < until;)
            {
                yield return new WaitForFixedUpdate();
                fastest = Mathf.Max(fastest, local.data.avarageVelocity.magnitude);
            }
            float launch = cannon.launchForce * Time.fixedDeltaTime;
            Check("a backpack fired from the Scout Cannon with a scout in it flies off alone: the scout flies as a scout alone does",
                LaunchFixes.BackpacksAlone > alone && fastest < launch * 2f + 10f,
                $"the scout's fastest {fastest:0} m/s (the cannon's push alone: {launch:0} m/s); backpacks fired alone {LaunchFixes.BackpacksAlone - alone}");
            yield return new WaitForSeconds(2f);
        }
        finally
        {
            worse.Restore();
            if (placed != null && placed.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(placed);
            }
            if (pack != null && pack.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(pack);
            }
        }
        yield return UntilUp(local);
    }

    private IEnumerator FlagWithMilk()
    {
        Item flagPrefab = ItemByName("Flag_Plantable_Checkpoint");
        Item milk = ItemByName("FortifiedMilk");
        if (flagPrefab == null || milk == null)
        {
            Fail("the checkpoint flag and the milk are in the item database");
            yield break;
        }
        Character local = Local;
        RemoveTicks(local);
        Action calm = CalmForMeasuring();
        try
        {
            Protect(false);
            ClearStatuses(Afflictions);
            Afflictions.SetStatus(Status.Crab, 0f);
            Afflictions.AddStatus(Status.Drowsy, 0.1f);
            Afflictions.AddStatus(Status.Cold, 0.05f);
            yield return SpawnIntoHand(flagPrefab);
            Item flagItem = held;
            if (flagItem == null)
            {
                yield break;
            }
            Vector2 look = local.data.lookValues;
            local.data.lookValues = new Vector2(look.x, -60f);
            yield return new WaitForSeconds(0.6f);
            flagItem.StartUsePrimary();
            for (float end = Time.realtimeSinceStartup + 5f; Time.realtimeSinceStartup < end && flagItem != null && flagItem.gameObject.activeInHierarchy && !flagItem.finishedCast;)
            {
                flagItem.ContinueUsePrimary();
                yield return null;
            }
            local.data.lookValues = look;
            yield return new WaitForSeconds(0.5f);
            CheckpointFlag flag = local.data.checkpointFlags.LastOrDefault(f => f != null);
            if (flag == null)
            {
                Fail("plant a checkpoint flag");
                yield break;
            }
            float[] saved = (float[])flag.currentStatuses.Clone();

            // Milk, then death (the milk still on).
            yield return SpawnIntoHand(milk);
            Item drink = held;
            if (drink != null)
            {
                drink.StartUsePrimary();
                for (float end = Time.realtimeSinceStartup + 6f; Time.realtimeSinceStartup < end && drink != null && drink.gameObject.activeInHierarchy && !drink.finishedCast;)
                {
                    drink.ContinueUsePrimary();
                    yield return null;
                }
            }
            yield return new WaitForSeconds(0.5f);
            bool invincible = local.data.isInvincible;
            int past = ItemFixes.FlagsPastInvincibility;
            Step("die invincible", () => local.DieInstantly());
            for (float until = Time.realtimeSinceStartup + 6f; Time.realtimeSinceStartup < until && local.data.checkpointFlags.Contains(flag);)
            {
                yield return null;
            }
            yield return new WaitForSeconds(1f);
            List<string> wrong = new List<string>();
            foreach (Status type in new[] { Status.Injury, Status.Hunger, Status.Cold, Status.Poison, Status.Drowsy, Status.Hot, Status.Spores })
            {
                float now = Afflictions.GetCurrentStatus(type);
                if (Mathf.Abs(now - saved[(int)type]) > 0.001f)
                {
                    wrong.Add($"{type} {now * 100f:0.#} (saved {saved[(int)type] * 100f:0.#})");
                }
            }
            bool alive = !local.data.dead && !local.data.passedOut;
            Check("a checkpoint flag brings a scout who died invincible (milk) back with the statuses it saved, and the milk goes on",
                invincible && alive && ItemFixes.FlagsPastInvincibility > past && wrong.Count == 0 && local.data.isInvincible,
                $"invincible at death {invincible}; alive {alive}; {(wrong.Count == 0 ? "every status as saved" : string.Join(", ", wrong))} (saved drowsiness {saved[(int)Status.Drowsy] * 100f:0.#}, cold {saved[(int)Status.Cold] * 100f:0.#}); invincible after {local.data.isInvincible}");
        }
        finally
        {
            calm();
            ClearStatuses(Afflictions);
            Protect(true);
        }
    }

    private IEnumerator Rise(GameObject bubble, int count, Vector3 open)
    {
        Character local = Local;
        yield return BackTo(open);
        yield return new WaitForSeconds(0.5f);
        float from = local.Center.y;
        List<GameObject> made = new List<GameObject>();
        for (int i = 0; i < count; i++)
        {
            made.Add(UnityEngine.Object.Instantiate(bubble, local.Center - Vector3.up * (0.6f * i), Quaternion.identity));
        }
        peak = 0f;
        for (float until = Time.time + 6f; Time.time < until;)
        {
            yield return null;
            peak = Mathf.Max(peak, local.Center.y - from);
        }
        foreach (GameObject one in made.Where(m => m != null))
        {
            UnityEngine.Object.Destroy(one);
        }
        yield return new WaitForSeconds(2f);
        yield return UntilUp(local);
        ClearStatuses(Afflictions);
    }

    private IEnumerator AntiBubbles(Vector3 open)
    {
        Item zooka = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i != null && i.GetComponent<ItemCooking>() != null
            && i.GetComponent<ItemCooking>().explosionPrefab != null && i.GetComponent<ItemCooking>().explosionPrefab.GetComponentInChildren<AntiSphere>(true) != null);
        GameObject bubble = zooka != null ? zooka.GetComponent<ItemCooking>().explosionPrefab : null;
        if (bubble == null)
        {
            Fail("the Anti-Zooka's cooked bubble");
            yield break;
        }
        yield return Rise(bubble, 1, open);
        float one = peak;
        yield return Rise(bubble, 2, open);
        float two = peak;
        Check("two Anti-Zooka bubbles (a cooked one's with another under the scout) lift a scout no higher than one",
            one > 1f && two <= one * 1.5f + 1f,
            $"{zooka.gameObject.name}'s cooked bubble {bubble.name}: one lifted the scout {one:0.0} m, two {two:0.0} m");
    }

    /// <summary>A jumping mushroom with open sky over it, nearest the scout, and the top of it.</summary>
    private static CollisionModifier OpenMushroom(Vector3 near, out Vector3 top)
    {
        top = Vector3.zero;
        foreach (CollisionModifier mushroom in UnityEngine.Object.FindObjectsByType<CollisionModifier>(FindObjectsSortMode.None)
            .Where(m => m.useBounceCoroutine && m.knockback >= 1000f && m.GetComponent<Collider>() != null).OrderBy(m => Vector3.Distance(m.transform.position, near)))
        {
            Collider body = mushroom.GetComponent<Collider>();
            Vector3 cap = body.ClosestPoint(body.bounds.center + Vector3.up * 200f);
            if (!Physics.Linecast(cap + Vector3.up * 0.5f, cap + Vector3.up * 55f, HelperFunctions.AllPhysicalExceptCharacter, QueryTriggerInteraction.Ignore))
            {
                top = cap;
                return mushroom;
            }
        }
        return null;
    }

    /// <summary>The scout put this high over a mushroom's top (after this long in the air, as if falling), its bounce's height measured; limp: they play dead as they touch it.</summary>
    private IEnumerator Bounce(Vector3 top, float high, float airborne, bool limp, bool limpBefore = false)
    {
        Character local = Local;
        yield return UntilUp(local);
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, top + Vector3.up * high, false);
        yield return null;
        local.data.sinceGrounded = airborne;
        for (float until = Time.time + 3f; Time.time < until && local.warping;)
        {
            yield return null;
        }
        float bounced = local.data.lastBouncedTime;
        bool fell = false;
        for (float until = Time.time + 6f; Time.time < until && Mathf.Approximately(local.data.lastBouncedTime, bounced);)
        {
            yield return new WaitForFixedUpdate();
            if (limpBefore && !fell && local.Center.y - top.y < 4f)
            {
                // Playing dead just before landing on it.
                local.Fall(3f);
                fell = true;
            }
        }
        if (limp)
        {
            // The Play Dead emote's own knock-down.
            local.Fall(3f);
        }
        peak = 0f;
        for (float until = Time.time + 6f; Time.time < until;)
        {
            yield return null;
            peak = Mathf.Max(peak, local.Center.y - top.y);
        }
        yield return new WaitForSeconds(1f);
        ClearStatuses(Afflictions);
    }

    private IEnumerator MushroomBounces()
    {
        Character local = Local;
        CollisionModifier mushroom = OpenMushroom(local.Center, out Vector3 top);
        if (mushroom == null)
        {
            Info("the mushroom bounce checks are skipped", "no jumping mushroom with open sky over it");
            yield break;
        }
        Info("the jumping mushroom", $"{mushroom.name} at {top:F0}: push {mushroom.knockback}, limp {mushroom.ragdolledKnockback}, x{mushroom.superBouncePow} after a fall");
        Vector3 home = top + Vector3.up * 2f;
        yield return Bounce(top, 50f, 0f, false);
        float fell = peak;
        yield return BackTo(home);
        int eased = LaunchFixes.LimpBouncesEased;
        int dragKept = LaunchFixes.LimpFlightDragKept;
        yield return Bounce(top, 50f, 0f, true);
        float fellLimp = peak;
        yield return BackTo(home);
        yield return Bounce(top, 50f, 0f, false, limpBefore: true);
        float limpLanding = peak;
        yield return BackTo(home);
        yield return Bounce(top, 1.2f, 0f, false);
        float still = peak;
        yield return BackTo(home);
        yield return Bounce(top, 1.2f, 4f, false);
        float warpedMidFall = peak;
        Check("a scout going limp on a jumping mushroom's bounce (a chain run out, playing dead) gets the plain bounce, not the long fall's",
            fellLimp < fell * 0.6f && limpLanding < fell * 0.6f && LaunchFixes.LimpFlightDragKept > dragKept,
            $"from 50 m up: bounced {fell:0.0} m high in control, {fellLimp:0.0} m going limp as they touched it, {limpLanding:0.0} m limp just before; "
            + $"eased {LaunchFixes.LimpBouncesEased - eased}, physics steps a limp scout kept their drag rising {LaunchFixes.LimpFlightDragKept - dragKept}");
        Check("warped onto a jumping mushroom in the middle of a fall, a scout bounces as from a standstill, not as after the fall",
            warpedMidFall <= still * 1.3f + 1f && warpedMidFall < fell * 0.6f,
            $"set down still on it: {still:0.0} m high; set down after 4 s in the air: {warpedMidFall:0.0} m; after a 50 m fall: {fell:0.0} m");
    }

    private IEnumerator CondorVine()
    {
        Character local = Local;
        JungleVine vine = UnityEngine.Object.FindObjectsByType<JungleVine>(FindObjectsSortMode.None).OrderBy(v => Vector3.Distance(v.transform.position, local.Center)).FirstOrDefault();
        Collider body = vine != null ? vine.GetComponentInChildren<Collider>() : null;
        if (body == null)
        {
            Info("the condor's vine check is skipped", "no vine here");
            yield break;
        }
        GameObject fake = new GameObject("UltimateAscent Test Condor");
        fake.SetActive(false);
        Condor condor = fake.AddComponent<Condor>();
        try
        {
            Vector3 on = body.bounds.center;
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, on + Vector3.right * 0.4f, false);
            for (float until = Time.time + 3f; Time.time < until && local.warping;)
            {
                yield return null;
            }
            local.data.carrierCondor = condor;
            vine.Interact(local);
            yield return new WaitForSeconds(0.4f);
            bool grabbedCarried = local.data.isVineClimbing;
            local.data.carrierCondor = null;
            local.refs.vineClimbing.lastInteractedVineTime = 0f;
            vine.Interact(local);
            yield return new WaitForSeconds(0.5f);
            bool grabbed = local.data.isVineClimbing;
            int letGo = ClimbFixes.CondorLetGo;
            local.data.carrierCondor = condor;
            yield return new WaitForSeconds(0.4f);
            bool holding = local.data.isVineClimbing;
            local.data.carrierCondor = null;
            Check("a scout a condor carries cannot grab a vine (nor a rope), and lets go of one they still hold",
                !grabbedCarried && grabbed && !holding && ClimbFixes.CondorLetGo > letGo,
                $"carried: grabbed the vine {grabbedCarried}; not carried: grabbed it {grabbed}; carried while holding it: still holding {holding} (let go {ClimbFixes.CondorLetGo - letGo})");
        }
        finally
        {
            local.data.carrierCondor = null;
            UnityEngine.Object.Destroy(fake);
        }
        yield return new WaitForSeconds(1f);
    }

    /// <summary>For the carry check: the carrier's own game kept blind to the carried scout waking (as when it never sees it).</summary>
    private static bool carrierBlind;

    private static bool CarrierSees() => !carrierBlind;

    /// <summary>
    /// A scout left carried with no one to drop them is set free (one game, the scout carried by themselves for the
    /// check): carried by someone carrying another, they are let go after a second; carried while awake with the
    /// carrier's game blind to it (its own drop held back for the check), their game asks for the game's drop after 3 s.
    /// </summary>
    private IEnumerator CarryLooseEnds(Vector3 open)
    {
        Character local = Local;
        CharacterData data = local.data;
        HarmonyLib.Harmony blind = new HarmonyLib.Harmony("UltimateAscent.SmokeTest.carry");
        try
        {
            // Carried by someone carrying someone else (here: by themselves, carrying no one).
            int freed = CarryFixes.Freed;
            data.isCarried = true;
            data.carrier = local;
            data.carriedPlayer = null;
            float tied = Time.time;
            for (float until = Time.time + 3f; Time.time < until && (data.isCarried || data.carrier != null);)
            {
                yield return new WaitForFixedUpdate();
            }
            float freedAfter = Time.time - tied;
            Check("a scout left carried by someone who carries someone else (or no one) is let go after a second",
                !data.isCarried && data.carrier == null && CarryFixes.Freed > freed && freedAfter >= CarryFixes.OrphanSeconds - 0.1f,
                $"carried {data.isCarried}, carrier {(data.carrier != null ? data.carrier.characterName : "none")} after {freedAfter:0.0} s; set free {CarryFixes.Freed - freed}");
            yield return BackTo(open);

            // Carried while awake, the carrier's game blind to it.
            blind.Patch(AccessTools.Method(typeof(CharacterCarrying), "Update"), prefix: new HarmonyLib.HarmonyMethod(typeof(Runner), nameof(CarrierSees)));
            carrierBlind = true;
            int asked = CarryFixes.DropsAsked;
            data.isCarried = true;
            data.carrier = local;
            data.carriedPlayer = local;
            tied = Time.time;
            for (float until = Time.time + 6f; Time.time < until && (data.isCarried || data.carriedPlayer != null);)
            {
                yield return new WaitForFixedUpdate();
            }
            float droppedAfter = Time.time - tied;
            Check("a scout awake and still carried (the carrier's game blind to it) is dropped by the game's own drop after 3 s",
                !data.isCarried && data.carrier == null && data.carriedPlayer == null && CarryFixes.DropsAsked > asked && droppedAfter >= CarryFixes.AwakeSeconds - 0.1f,
                $"carried {data.isCarried}, the carrier still carrying {data.carriedPlayer != null} after {droppedAfter:0.0} s; drops asked {CarryFixes.DropsAsked - asked}");
        }
        finally
        {
            carrierBlind = false;
            blind.UnpatchSelf();
            data.isCarried = false;
            data.carrier = null;
            data.carriedPlayer = null;
        }
        yield return BackTo(open);
    }

    /// <summary>A rope put up as a Rope Shooter's shot puts one: its anchor (held where it is) and the rope it lets down, this high over the ground.</summary>
    private static Rope ShotRope(Vector3 ground, float high, out GameObject anchorMade)
    {
        anchorMade = null;
        RopeShooter shooter = ItemWith<RopeShooter>()?.GetComponent<RopeShooter>();
        GameObject prefab = shooter != null ? shooter.ropeAnchorWithRopePref : null;
        if (prefab == null)
        {
            return null;
        }
        anchorMade = PhotonNetwork.Instantiate(prefab.name, ground + Vector3.up * high, Quaternion.identity, 0);
        foreach (Rigidbody body in anchorMade.GetComponentsInChildren<Rigidbody>())
        {
            body.isKinematic = true;
        }
        RopeAnchorWithRope anchor = anchorMade.GetComponent<RopeAnchorWithRope>();
        return anchor != null ? anchor.SpawnRope() : null;
    }

    /// <summary>How high over the ground a point is (up to 6 m; more, or no ground under it, is "too high").</summary>
    private static float OverGround(Vector3 at)
    {
        return Physics.Raycast(at, Vector3.down, out RaycastHit ground, 6f, HelperFunctions.terrainMapMask) ? ground.distance : float.MaxValue;
    }

    /// <summary>A spot this high over the ground (give or take), within the Scoutmaster's reach from the ground under it.</summary>
    private const float ReachableHigh = 1.8f;

    /// <summary>
    /// The Scoutmaster keeps his target with one scout in the room: the game lets him go after nobody with fewer than two
    /// scouts alive (each frame), so a test alone stands in for the second (while on).
    /// </summary>
    private static HarmonyLib.Harmony KeepHisTarget()
    {
        HarmonyLib.Harmony keep = new HarmonyLib.Harmony("UltimateAscent.SmokeTest.scoutmaster");
        keep.Patch(AccessTools.Method(typeof(Scoutmaster), "VerifyTarget"), prefix: new HarmonyLib.HarmonyMethod(typeof(Runner), nameof(HisTargetKept)));
        return keep;
    }

    private static bool HisTargetKept() => false;

    /// <summary>
    /// A rope put up by a scout stays: when the host changes while the scout who put it up is still in the room, and
    /// when its maker leaves. (The Scoutmaster pulling a scout off a hold is checked on the test ledge: smroutes.)
    /// </summary>
    private IEnumerator ShotRopeStays()
    {
        Character local = Local;
        yield return UntilUp(local);
        Vector3 ground = GroundUnder(local.Center);
        Rope rope = ShotRope(ground, 7f, out GameObject anchor);
        yield return new WaitForSeconds(6f);
        List<Transform> segments = rope != null ? rope.GetRopeSegments() : new List<Transform>();
        Info("a rope put up as a Rope Shooter's shot", rope == null ? "none" : $"{segments.Count} segments from {anchor.transform.position:F1}, climbable {rope.isClimbable}");

        // A change of host with the rope's maker still here.
        if (rope != null && segments.Count > 0)
        {
            int kept = RopeFixes.KeptAtHostChange;
            int count = segments.Count;
            rope.OnMasterClientSwitched(PhotonNetwork.LocalPlayer);
            yield return new WaitForSeconds(0.5f);
            int after = rope.GetRopeSegments().Count;
            Check("a rope stays up when the host changes while the scout who put it up is still here (the game: it vanishes on their screen)",
                !rope.creatorLeft && after == count && RopeFixes.KeptAtHostChange > kept,
                $"segments {count} -> {after}, taken as its maker gone {rope.creatorLeft}; kept {RopeFixes.KeptAtHostChange - kept}");
        }

        if (rope != null && rope.GetRopeSegments().Count > 0)
        {
            yield return MakerLeaves(rope, anchor);
        }
        if (anchor != null && anchor.GetComponent<PhotonView>() != null)
        {
            PhotonNetwork.Destroy(anchor);
        }
        if (rope != null && rope.photonView != null)
        {
            PhotonNetwork.Destroy(rope.photonView);
        }
        yield return new WaitForSeconds(1f);
    }

    /// <summary>
    /// A rope's maker leaving, on the game of a scout still in the room: Photon takes the leaver's things off it, and
    /// what was put up for good (the rope tied to its anchor, the anchor) stays, hanging as it last was, still to climb;
    /// anything else of theirs goes as before. The scout here plays the host still in the room (the rope as its maker
    /// last sent it).
    /// </summary>
    private IEnumerator MakerLeaves(Rope rope, GameObject anchor)
    {
        Character local = Local;
        MethodInfo remove = AccessTools.Method(typeof(PhotonNetwork), "RemoveInstantiatedGO");
        if (remove == null)
        {
            Fail("Photon's removal of a leaving player's things");
            yield break;
        }
        rope.SetSyncData(rope.GetSyncData());
        int count = rope.GetRopeSegments().Count;
        int kept = RopeFixes.KeptAfterLeaving;
        bool otherKept = RopeFixes.PutUpForGood(local.gameObject);
        int leaving = RopeFixes.Leaving;
        RopeFixes.Leaving = PhotonNetwork.LocalPlayer.ActorNumber;
        try
        {
            remove.Invoke(null, new object[] { rope.gameObject, true });
            remove.Invoke(null, new object[] { anchor, true });
        }
        finally
        {
            RopeFixes.Leaving = leaving;
        }
        yield return new WaitForSeconds(1f);
        List<Transform> left = rope != null ? rope.GetRopeSegments() : new List<Transform>();
        // Low parts of it first (nearest a scout's reach over the ground); where it hangs over a drop, its middle.
        List<RopeSegment> lows = left.Select((s, i) => (segment: s.GetComponent<RopeSegment>(), high: OverGround(s.position), index: i))
            .Where(s => s.segment != null)
            .OrderBy(s => s.high < 4f ? Mathf.Abs(s.high - ReachableHigh) : 100f + Mathf.Abs(s.index - left.Count / 2))
            .Select(s => s.segment).Take(3).ToList();
        bool climbed = false;
        int tries = 0;
        foreach (RopeSegment low in lows)
        {
            tries++;
            yield return UntilUp(local);
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, low.transform.position + Vector3.right * 0.4f, false);
            for (float until = Time.time + 3f; Time.time < until && local.warping;)
            {
                yield return null;
            }
            local.refs.ropeHandling.lastInteractedRopeTime = 0f;
            low.Interact(local);
            yield return new WaitForSeconds(0.6f);
            climbed = local.data.isRopeClimbing && local.data.heldRope == rope;
            if (local.data.isRopeClimbing)
            {
                local.refs.ropeHandling.Stop();
            }
            if (climbed)
            {
                break;
            }
        }
        Check("what a leaving scout put up stays for those still there (a rope tied to its anchor, the anchor), hanging as it last was, still to climb",
            rope != null && anchor != null && rope.creatorLeft && RopeFixes.KeptAfterLeaving - kept == 2 && left.Count == count && climbed && !otherKept,
            $"kept {RopeFixes.KeptAfterLeaving - kept} (rope {rope != null}, anchor {anchor != null}); taken as its maker gone {rope != null && rope.creatorLeft}; "
            + $"segments {count} -> {left.Count}; climbed {climbed} ({tries} tr{(tries == 1 ? "y" : "ies")}); a scout taken as put up {otherKept}");
        yield return UntilUp(local);
    }

    /// <summary>
    /// The airport's training rope comes back (airportrope, airport): taken down as a run's end leaves it (the scouts
    /// back in the airport, the room kept, the rope not put up again), the host puts it up again.
    /// </summary>
    private IEnumerator AirportRope()
    {
        List<RopeAnchorWithRope> anchors = UnityEngine.Object.FindObjectsByType<RopeAnchorWithRope>(FindObjectsSortMode.None)
            .Where(a => a != null && a.photonView != null && !a.photonView.isRuntimeInstantiated && a.anchor != null).ToList();
        Rope Attached(RopeAnchorWithRope a) => UnityEngine.Object.FindObjectsByType<Rope>(FindObjectsSortMode.None).FirstOrDefault(r => r != null && r.attachedToAnchor == a.anchor);
        Info("the airport's rope anchors", string.Join("; ", anchors.Select(a => $"{a.name} at {a.transform.position:F0}, its rope {(Attached(a) != null ? "up" : "missing")}")));
        RopeAnchorWithRope training = anchors.FirstOrDefault();
        if (training == null)
        {
            Fail("the airport's training rope anchor");
            yield break;
        }
        Rope rope = Attached(training);
        if (rope != null && rope.photonView != null)
        {
            PhotonNetwork.Destroy(rope.photonView);
        }
        training.rope = null;
        training.ropeInstance = null;
        int putUp = RopeFixes.PutUpAgain;
        Rope back = null;
        for (float until = Time.time + 5f; Time.time < until && back == null;)
        {
            yield return new WaitForSeconds(0.25f);
            back = Attached(training);
        }
        yield return new WaitForSeconds(6f);
        int segments = back != null ? back.GetRopeSegments().Count : 0;
        Check("the airport's training rope is put up again when it is missing (after a run, or the host leaving)",
            back != null && RopeFixes.PutUpAgain > putUp && segments > 0,
            $"taken down; back {back != null} ({segments} segments); put up again {RopeFixes.PutUpAgain - putUp}");
    }

    private IEnumerator NadirWall()
    {
        VoidInvisWall wall = UnityEngine.Object.FindObjectsByType<VoidInvisWall>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        Collider part = wall != null && wall.wall != null ? wall.wall.GetComponentInChildren<Collider>(true) : null;
        if (part == null)
        {
            Info("the Nadir wall check is skipped", "no Nadir wall on this map");
            yield break;
        }
        bool wasUp = wall.wall.activeInHierarchy;
        GameObject standIn = null;
        // Not up here (it stands only until the Nadir's soul is freed): put up for the check, as it stands there.
        GameObject raised = wasUp ? null : wall.wall;
        bool parentOff = raised != null && raised.transform.parent != null && !raised.transform.parent.gameObject.activeInHierarchy;
        if (raised != null && !parentOff)
        {
            raised.SetActive(true);
            Physics.SyncTransforms();
        }
        try
        {
            Info("the Nadir's black wall", $"{part.name} on {LayerMask.LayerToName(part.gameObject.layer)}, up {wasUp}{(raised != null ? (parentOff ? " (its parent off: not put up)" : " (put up for the check)") : "")}, "
                + $"trigger {part.isTrigger}, at {part.bounds.center:F0} size {part.bounds.size:F0}");
            if (!wall.wall.activeInHierarchy)
            {
                // Its part of the map is off here: a stand-in, the wall's own shape where it stands, under a wall of the same kind.
                standIn = new GameObject("UltimateAscentTestSoulWall");
                standIn.SetActive(false);
                // Made under the stand-in while it is off: nothing of the copy wakes (no second network view) until it is ready.
                GameObject shape = UnityEngine.Object.Instantiate(part.gameObject, part.transform.position, part.transform.rotation, standIn.transform);
                shape.transform.localScale = part.transform.lossyScale;
                foreach (PhotonView copied in shape.GetComponentsInChildren<PhotonView>(true))
                {
                    UnityEngine.Object.DestroyImmediate(copied);
                }
                shape.SetActive(true);
                VoidInvisWall stand = standIn.AddComponent<VoidInvisWall>();
                stand.wall = shape;
                standIn.SetActive(true);
                Physics.SyncTransforms();
                part = shape.GetComponent<Collider>();
                // The warp's check looks the walls up every 2 s.
                yield return new WaitForSeconds(2.2f);
                Info("the Nadir's black wall: a stand-in", $"its shape at {part.bounds.center:F0} size {part.bounds.size:F0}");
            }
            // Lines through its whole extent (from 5 m outside it to 5 m outside it), level ones at its middle height and
            // across at quarter heights; and one well clear of it.
            Bounds box = part.bounds;
            List<string> crossing = new List<string>();
            int lines = 0;
            foreach (float height in new[] { 0f, -0.25f, 0.25f })
            {
                foreach (Vector3 way in new[] { Vector3.right, Vector3.forward, (Vector3.right + Vector3.forward).normalized, (Vector3.right - Vector3.forward).normalized })
                {
                    Vector3 middle = box.center + Vector3.up * (box.size.y * height);
                    float reach = box.extents.magnitude + 5f;
                    lines++;
                    if (WarpFixes.ThroughSoulWall(middle - way * reach, middle + way * reach))
                    {
                        crossing.Add($"{way:F1} at {middle.y:0}");
                    }
                }
            }
            Vector3 clear = box.center + Vector3.right * (box.extents.x + 10f);
            bool past = WarpFixes.ThroughSoulWall(clear, clear + Vector3.forward * 20f);
            Check("no warp goes through the Nadir's black wall while it stands (no way past the soul's freeing)", crossing.Count > 0 && !past,
                $"{part.GetType().Name} {box.size:F0}: {crossing.Count} of {lines} lines through it stopped ({string.Join(", ", crossing)}); one clear of it stopped {past}");
        }
        finally
        {
            if (raised != null && !parentOff)
            {
                raised.SetActive(false);
            }
            if (standIn != null)
            {
                UnityEngine.Object.Destroy(standIn);
            }
        }
    }

    /// <summary>
    /// The game's lava bugs, fixed on every ascent (gamebugslava; on a map with the Caldera, -Biome V): a touch of a lava
    /// pillar's lava gives no stamina, and a tied balloon pops under the lava's surface.
    /// </summary>
    /// <summary>Whether a collider is lava (by its name or what it is part of, or a lava river).</summary>
    private static bool LavaPart(Collider part)
    {
        for (Transform up = part.transform; up != null; up = up.parent)
        {
            if (up.name.IndexOf("Lava", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return part.GetComponentInParent<LavaRiver>() != null;
    }

    /// <summary>
    /// Climbing a lava stream (the user's report: it can be climbed, and it gave stamina): what its surfaces do to a
    /// climber (their climbing stamina cost), and a climb on the nearest, its stamina over 2 s held on it.
    /// </summary>
    private IEnumerator LavaClimb(Vector3 home)
    {
        Character local = Local;
        List<ClimbModifierSurface> surfaces = UnityEngine.Object.FindObjectsByType<ClimbModifierSurface>(FindObjectsSortMode.None)
            .Where(s => s.GetComponent<Collider>() != null && LavaPart(s.GetComponent<Collider>())).ToList();
        Info("lava's climbing surfaces", surfaces.Count == 0 ? "none" : string.Join("; ", surfaces.GroupBy(s => s.name).Select(g =>
        {
            ClimbModifierSurface s = g.First();
            return $"{g.Key} x{g.Count()} (stamina cost x{s.staminaUsageMultiplier:0.##}{(s.staticClimbCost ? ", fixed" : "")}, speed x{s.speedMultiplier:0.##}, slip {s.slipModifier:0.##}"
                + $"{(s.onlySlideDown ? ", slides down" : "")}{(s.applyStatus ? $", {s.statusType} {s.statusAmount * 100f:0} every {s.statusCooldown:0.#} s" : "")})";
        })));
        ClimbModifierSurface stream = surfaces.OrderBy(s => Vector3.Distance(s.transform.position, local.Center)).FirstOrDefault();
        if (stream == null)
        {
            Info("the lava climb check is skipped", "no climbing surface on the lava here");
            yield break;
        }
        Collider body = stream.GetComponent<Collider>();
        // Its face from the side: a line in at its middle from 4 m out.
        RaycastHit face = default;
        bool found = false;
        foreach (Vector3 side in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            Vector3 from = body.bounds.center + side * (Vector3.Scale(body.bounds.extents, new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z))).magnitude + 4f);
            if (body.Raycast(new Ray(from, -side), out face, 40f) && Mathf.Abs(face.normal.y) < 0.7f)
            {
                found = true;
                break;
            }
        }
        if (!found)
        {
            Info("the lava climb check is skipped", $"no face of {stream.name} to climb found");
            yield break;
        }
        local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, face.point + face.normal * 0.8f, false);
        for (float until = Time.time + 3f; Time.time < until && local.warping;)
        {
            yield return null;
        }
        local.photonView.RPC("StartClimbRpc", RpcTarget.All, face.point, face.normal);
        yield return new WaitForSeconds(0.3f);
        local.data.currentStamina = 0.5f;
        float before = local.data.currentStamina;
        bool held = local.data.isClimbing;
        float onIt = 0f;
        for (float until = Time.time + 2f; Time.time < until;)
        {
            yield return null;
            if (local.data.isClimbing && local.data.climbMod == stream)
            {
                onIt += Time.deltaTime;
            }
        }
        float after = local.data.currentStamina;
        Check("a lava stream gives no stamina to a scout climbing it (it stays climbable)",
            held && onIt > 1f && after <= before + 0.005f,
            $"{stream.name} (stamina cost x{stream.staminaUsageMultiplier:0.##}): climbing it {held}, on it {onIt:0.0} of 2 s; stamina {before * 100f:0.#} -> {after * 100f:0.#} held still on it");
        local.refs.climbing.StopAnyClimbing();
        yield return BackTo(home);
        ClearStatuses(Afflictions);
    }

    private IEnumerator GameBugLavaChecks()
    {
        Character local = Local;
        Protect(true);
        if (Map.CurrentPlace != Place.Caldera)
        {
            MapHandler.JumpToSegment(Segment.Caldera);
            yield return new WaitForSeconds(8f);
        }
        if (Map.CurrentPlace != Place.Caldera)
        {
            Info("the lava checks are skipped", $"no Caldera here ({Map.CurrentPlace}); run with -Biome V");
            yield break;
        }
        yield return UntilUp(local);
        Vector3 home = GroundUnder(local.Center);
        List<CollisionModifier> lavas = UnityEngine.Object.FindObjectsByType<CollisionModifier>(FindObjectsSortMode.None).Where(ClimbFixes.IsLava).ToList();
        Info("the lava that touches", string.Join("; ", lavas.GroupBy(m => m.name).Select(g => $"{g.Key} x{g.Count()} (push {g.First().knockback:0}, stamina back {Overrides.Original(g.First(), nameof(CollisionModifier.staminaBackOnBounce), g.First().staminaBackOnBounce) * 100f:0})")));
        CollisionModifier pillar = lavas.Where(m => m.knockback > 0f && m.GetComponent<Collider>() != null && Overrides.Original(m, nameof(CollisionModifier.staminaBackOnBounce), m.staminaBackOnBounce) > 0f)
            .OrderBy(m => Vector3.Distance(m.transform.position, local.Center)).FirstOrDefault();
        // No lava here bounces a scout off with stamina back (the game gives it only with a bounce): a piece of lava
        // that has stamina back to give (a lava stream's) is made to bounce for the check (a river's push, any side), and
        // put back after.
        float own = -1f;
        float ownDot = 0f;
        if (pillar == null)
        {
            pillar = lavas.Where(m => m.GetComponent<Collider>() != null && Overrides.Original(m, nameof(CollisionModifier.staminaBackOnBounce), m.staminaBackOnBounce) > 0f)
                .OrderBy(m => Vector3.Distance(m.transform.position, local.Center)).FirstOrDefault();
            if (pillar != null)
            {
                own = pillar.knockback;
                ownDot = pillar.bounceDotMinimum;
                pillar.knockback = lavas.Select(m => m.knockback).DefaultIfEmpty(0f).Max() > 0f ? lavas.Select(m => m.knockback).Max() : 1000f;
                pillar.bounceDotMinimum = -1f;
            }
        }
        if (pillar == null)
        {
            Info("the lava stamina check is skipped", "no lava here with stamina back to give");
        }
        else
        {
            Collider body = pillar.GetComponent<Collider>();
            // Onto its top from above where it has one (a river's lava), else at its side.
            Vector3 surface = body.bounds.center;
            Vector3 outward = Vector3.up;
            foreach (Vector3 side in new[] { Vector3.up, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Vector3 from = body.bounds.center + side * (body.bounds.extents.magnitude + 4f);
                if (body.Raycast(new Ray(from, -side), out RaycastHit onIt, body.bounds.extents.magnitude * 2f + 8f))
                {
                    surface = onIt.point;
                    outward = onIt.normal;
                    break;
                }
            }
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, surface + outward * 0.9f, false);
            for (float until = Time.time + 3f; Time.time < until && local.warping;)
            {
                yield return null;
            }
            local.data.currentStamina = 0.5f;
            local.data.sinceUseStamina = 0f;
            float before = local.data.currentStamina;
            float bounced = local.data.lastBouncedTime;
            for (float until = Time.time + 3f; Time.time < until && Mathf.Approximately(local.data.lastBouncedTime, bounced);)
            {
                local.AddForce(-outward * 60f);
                yield return new WaitForFixedUpdate();
            }
            bool touched = !Mathf.Approximately(local.data.lastBouncedTime, bounced);
            float after = local.data.currentStamina;
            Check("lava gives no stamina: lava that bounces a scout off gives none back (the game: a lava stream's piece 10)",
                touched && after <= before + 0.01f && pillar.staminaBackOnBounce == 0f,
                $"{pillar.name} under {(pillar.transform.parent != null ? pillar.transform.parent.name : "nothing")}{(own >= 0f ? $" (made to bounce for the check: push {pillar.knockback:0}; its own {own:0})" : "")}: "
                + $"touched {touched}; stamina {before * 100f:0} -> {after * 100f:0}; its stamina back now {pillar.staminaBackOnBounce * 100f:0} (was {Overrides.Original(pillar, nameof(CollisionModifier.staminaBackOnBounce), pillar.staminaBackOnBounce) * 100f:0})");
            if (own >= 0f)
            {
                pillar.knockback = own;
                pillar.bounceDotMinimum = ownDot;
            }
            yield return BackTo(home);
            ClearStatuses(Afflictions);
        }

        yield return LavaClimb(home);

        Lava sea = UnityEngine.Object.FindObjectsByType<Lava>(FindObjectsSortMode.None).FirstOrDefault(l => l.isActiveAndEnabled);
        if (sea == null)
        {
            Info("the lava balloon check is skipped", "no lava sea here");
            yield break;
        }
        CharacterBalloons balloons = local.refs.balloons;
        balloons.TieNewBalloon(2);
        yield return new WaitForSeconds(1.2f);
        TiedBalloon balloon = balloons.tiedBalloons.LastOrDefault();
        if (balloon == null)
        {
            Fail("tie a balloon");
            yield break;
        }
        Bounds area = AccessTools.FieldRefAccess<Lava, Bounds>("bounds")(sea);
        int popped = ItemFixes.BalloonsPopped;
        balloon.rb.position = new Vector3(area.center.x, sea.transform.position.y - 0.5f, area.center.z);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return new WaitForSeconds(0.2f);
        Check("a tied balloon pops when it touches lava (under the lava's surface)",
            balloon == null && ItemFixes.BalloonsPopped > popped && ItemFixes.LastPop == "lava",
            $"popped by the mod {ItemFixes.BalloonsPopped - popped} ({ItemFixes.LastPop}); the balloon {(balloon == null ? "gone" : "still tied")}");
    }
}
