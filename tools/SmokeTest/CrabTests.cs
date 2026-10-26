using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>Checks of the beach's crabs (17).</summary>
internal sealed partial class Runner
{
    /// <summary>The crabs lying about on the beach (not held).</summary>
    private static List<BeachCrab> BeachCrabs()
    {
        return UnityEngine.Object.FindObjectsByType<BeachCrab>(FindObjectsSortMode.None)
            .Where(c => c != null && c.GetComponent<Item>() is Item item && item.itemState == ItemState.Ground && Crabs.Cooked(item) == 0).ToList();
    }

    /// <summary>
    /// The beach's crabs (17, on the beach at the start of the climb): up to 25, on the beach's ground, spread along it
    /// from where the scouts wake to its campfire; left alone, one crawls around the beach sideways at 1 m/s, its
    /// scuttle heard; a scout within 2 m is chased at 1 m/s for at least 6 s, then given up once more than 2 m off; its
    /// pinch (the big claw rising, opening and snapping, the scorpion's glint and sparks) deals 10 injury and knocks what
    /// the scout holds out of their hands; held, a crab pinches its holder and falls out of their hands; cooked, it is a
    /// crab leg (its model, icon and name), 10 hunger and 10 extra stamina (halved at 15); put away, a live crab is
    /// dropped, a cooked leg stays in its pocket, and it goes in a backpack; burnt as this ascent's campfires burn it, the
    /// leg can still be eaten and only poisons.
    /// </summary>
    private IEnumerator CrabChecks()
    {
        Character local = Local;
        CharacterAfflictions afflictions = local.refs.afflictions;
        if (Map.CurrentPlace != Place.Shore)
        {
            Info("the crab checks are skipped", $"they need the beach (here {Map.CurrentPlace}); run them with -Only crabs");
            yield break;
        }
        for (float until = Time.time + 10f; Time.time < until && BeachCrabs().Count == 0;)
        {
            yield return null;
        }
        List<BeachCrab> crabs = BeachCrabs();
        bool way = Crabs.Way(out Vector3 start, out Vector3 end);
        float[] shares = crabs.Select(c => Crabs.Share(c.transform.position, start, end)).OrderBy(s => s).ToArray();
        bool onBeach = crabs.All(c => Map.TryGetGroundBiome(c.transform.position + Vector3.up * 0.3f, out Biome.BiomeType biome) && biome == Biome.BiomeType.Shore);
        Check("crabs live on the beach, at most 25, spread along it from where the scouts wake to its campfire (17)",
            way && crabs.Count > 0 && crabs.Count <= Crabs.MaxCrabs && onBeach && shares.First() < 0.25f && shares.Last() > 0.75f,
            $"{crabs.Count} crabs (spawned {Crabs.Spawned}); all on the beach's ground: {onBeach}; along the way from where the scouts wake to the beach's campfire (0 to 1): "
            + $"{string.Join(", ", shares.Select(s => s.ToString("0.00")))}; spots found {Crabs.SpotsFound} of {Crabs.SpotsTried} looked at");
        if (crabs.Count == 0)
        {
            yield break;
        }
        Item stone = ItemByName(StoneItem.PrefabName);
        ConfigEntry<bool> halfFood = UltimateAscent.Plugin.AscentSwitches[Lvl.WorseItems];
        bool halfFoodWas = halfFood.Value;
        Protect(false);
        Action calm = CalmForMeasuring();
        try
        {
            foreach (BeachCrab crab in crabs)
            {
                crab.Unaware = true;
            }

            // Left alone: the crab nearest the scout, watched for 20 s from a few metres off.
            BeachCrab crawler = crabs.OrderBy(c => Vector3.Distance(c.transform.position, local.Center)).First();
            CrabLook crawlerLook = crawler.GetComponent<CrabLook>();
            Vector3? watch = StandNear(crawler.transform.position, 4f, 8f, seeIt: true) ?? StandNear(crawler.transform.position, 4f, 10f, seeIt: true, firm: false);
            if (watch.HasValue)
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, watch.Value + Vector3.up * 1.2f, false);
            }
            yield return new WaitForSeconds(1.5f);
            float walked = 0f, topSpeed = 0f, loudest = 0f, crawlTime = 0f, crawled = 0f;
            int moving = 0, sideways = 0, offBeach = 0;
            TurnWatch turning = new TurnWatch();
            Vector3 last = crawler.transform.position;
            Vector3 windowStart = last;
            float windowSince = 0f;
            float nextShot = 0f;
            int shot = 0;
            for (float since = 0f; since < 20f; since += Time.deltaTime)
            {
                yield return null;
                if (crawler == null)
                {
                    break;
                }
                float step = Goats.Flat(crawler.transform.position, last);
                last = crawler.transform.position;
                walked += step;
                float speed = Time.deltaTime > 0f ? step / Time.deltaTime : 0f;
                windowSince += Time.deltaTime;
                if (windowSince >= 0.33f)
                {
                    topSpeed = Mathf.Max(topSpeed, Goats.Flat(crawler.transform.position, windowStart) / windowSince);
                    windowStart = crawler.transform.position;
                    windowSince = 0f;
                }
                turning.Step(BeachCrabs().Where(c => Vector3.Distance(c.transform.position, local.Center) < 40f), Time.deltaTime);
                if (speed > 0.15f)
                {
                    moving++;
                    crawlTime += Time.deltaTime;
                    crawled += step;
                    loudest = Mathf.Max(loudest, crawlerLook.ScuttleLoudness);
                    if (crawlerLook.Sideways)
                    {
                        sideways++;
                    }
                }
                if (!Crabs.OnBeach(crawler.transform.position))
                {
                    offBeach++;
                }
                if (since >= nextShot)
                {
                    nextShot += 7f;
                    LookAtPoint(local, crawler.transform.position + Vector3.up * 0.1f);
                    yield return new WaitForSeconds(0.3f);
                    Screenshot($"crab-crawl-{++shot}");
                }
            }
            float average = crawlTime > 0f ? crawled / crawlTime : 0f;
            Check("left alone, a crab crawls around the beach at 1 m/s, scuttling sideways, its scuttle heard (17)",
                average >= Crabs.Speed * 0.7f && average <= Crabs.Speed * 1.3f && moving > 0 && sideways >= moving * 0.6f && loudest > 0.1f && offBeach == 0,
                $"it went {walked:0.0} m over the ground in 20 s, crawling for {crawlTime:0.0} s of them ({average:0.00} m/s while it crawled; its speed {Crabs.Speed:0.0}), at most {topSpeed:0.0} m/s over a third of a second; "
                + $"sideways in {sideways} of {moving} moving frames; its scuttle up to {loudest:0.00} of its loudness; off the beach in {offBeach} frames");
            Check("crawling about, crabs go from spot to spot, resting a moment at each, without circling (17)",
                turning.Watched > 0 && turning.Rests > 0 && turning.Most < 540f,
                $"{turning.Watched} crabs near the scout watched for 20 s: at most {turning.Most:0} degrees of turning within 2 s of crawling (circling a spot turns about 1000); {turning.Rests} rests at a spot");

            // Noticed: the scout 1.5 m from it; then kept 5 m off it (put back there every 0.4 s), it goes on after them
            // for 6 s, then gives up.
            BeachCrab chaser = crawler;
            Vector3? beside = StandNear(chaser.transform.position, 1.2f, 1.8f) ?? StandNear(chaser.transform.position, 1.2f, 2f, firm: false);
            bool chased = false, keptOn = true, gaveUp = false;
            float chaseTop = 0f, gaveUpAt = -1f;
            if (beside.HasValue)
            {
                // Held where it is while the scout steps in beside it (crawling on, it could leave the 2 m).
                chaser.Still = true;
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, beside.Value + Vector3.up * 1.2f, false);
                yield return new WaitForSeconds(0.6f);
                chaser.Still = false;
                chaser.Unaware = false;
                for (float until = Time.time + 3f; Time.time < until && chaser.Target != local;)
                {
                    yield return null;
                }
                chased = chaser.Target == local;
                float since = Time.time;
                Vector3 previous = chaser.transform.position;
                float window = 0f;
                float nextAway = 0f;
                while (chased && Time.time - since < 9f)
                {
                    yield return null;
                    window += Time.deltaTime;
                    if (window >= 0.33f)
                    {
                        chaseTop = Mathf.Max(chaseTop, Vector3.Distance(chaser.transform.position, previous) / window);
                        previous = chaser.transform.position;
                        window = 0f;
                    }
                    if (Time.time >= nextAway)
                    {
                        nextAway = Time.time + 0.4f;
                        Vector3? away = StandNear(chaser.transform.position, 4.5f, 5.5f, firm: false);
                        if (away.HasValue)
                        {
                            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, away.Value + Vector3.up * 1.2f, false);
                        }
                    }
                    float chasing = Time.time - since;
                    if (chaser.Target != local && chasing < Crabs.MinChaseSeconds - 0.2f)
                    {
                        keptOn = false;
                    }
                    if (chaser.Target == null && !gaveUp)
                    {
                        gaveUp = true;
                        gaveUpAt = chasing;
                        break;
                    }
                }
            }
            chaser.Unaware = true;
            Check("a scout within 2 m is chased at 1 m/s for at least 6 s however far they get, then given up once they are more than 2 m off (17)",
                chased && keptOn && gaveUp && gaveUpAt >= Crabs.MinChaseSeconds - 0.2f && chaseTop >= Crabs.Speed * 0.8f && chaseTop <= Crabs.Speed * 1.3f,
                $"noticed {chased} (the scout {(beside.HasValue ? "1.2-1.8 m" : "not")} beside it); kept on after them while 5 m off: {keptOn}; gave up {(gaveUp ? $"{gaveUpAt:0.0} s" : "never")} into the chase; "
                + $"top speed {chaseTop:0.0} m/s (its speed {Crabs.Speed:0.0})");

            // The pinch: the scout standing by a crab, holding a Stone.
            BeachCrab pincher = crawler;
            CrabLook pincherLook = pincher.GetComponent<CrabLook>();
            if (stone != null)
            {
                yield return SpawnIntoHand(stone);
            }
            Item inHand = held != null ? held : local.data.currentItem;
            Vector3? close = StandNear(pincher.transform.position, 1.1f, 1.5f) ?? StandNear(pincher.transform.position, 1.1f, 1.8f, firm: false);
            pincher.Still = true;
            if (close.HasValue)
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, close.Value + Vector3.up * 1.2f, false);
            }
            yield return new WaitForSeconds(0.5f);
            pincher.Still = false;
            inHand = local.data.currentItem;
            LookAtPoint(local, pincher.transform.position + Vector3.up * 0.1f);
            ClearStatuses(afflictions);
            float injuryBefore = afflictions.GetCurrentStatus(Status.Injury);
            int pinchesBefore = Crabs.PinchesTaken, knockedBefore = Crabs.ItemsKnockedOut, flashesBefore = Crabs.Flashes, soundsBefore = Crabs.PinchSounds;
            float widest = 0f;
            bool faced = false;
            int clicksBefore = pincherLook.ClicksHeard;
            pincher.Unaware = false;
            bool pinchShot = false;
            SoundWatch heard = new SoundWatch();
            for (float until = Time.time + 9f; Time.time < until && Crabs.PinchSounds == soundsBefore;)
            {
                yield return null;
                heard.Before();
                widest = Mathf.Max(widest, pincherLook.ClawOpen);
                faced |= pincher.Striking && pincherLook.Facing;
                if (!pinchShot && pincher.Striking && pincher.inRangeForAttackTime > 0.6f)
                {
                    pinchShot = true;
                    Screenshot("crab-pinch-windup");
                }
            }
            float snapAt = Time.time;
            yield return heard.After();
            float landedAfter = -1f;
            for (float until = Time.time + 1f; Time.time < until && Crabs.PinchesTaken == pinchesBefore;)
            {
                yield return null;
            }
            if (Crabs.PinchesTaken > pinchesBefore)
            {
                landedAfter = Time.time - snapAt;
            }
            yield return new WaitForSeconds(0.5f);
            pincher.Unaware = true;
            float injury = afflictions.GetCurrentStatus(Status.Injury) - injuryBefore;
            bool knocked = Crabs.ItemsKnockedOut > knockedBefore && local.data.currentItem == null;
            bool lying = stone != null && UnityEngine.Object.FindObjectsByType<Item>(FindObjectsSortMode.None).Any(i => i != null && i.itemID == stone.itemID && i.itemState == ItemState.Ground && Vector3.Distance(i.transform.position, local.Center) < 4f);
            int clicksHeard = pincherLook.ClicksHeard - clicksBefore;
            Check("a crab pinches a scout near it, facing them: its big claw rises, opens and snaps (one snap heard, no claw clicks before it), with the scorpion's glint and sparks; a landed pinch deals 10 injury and knocks the item out of their hands (17)",
                Crabs.PinchesTaken > pinchesBefore && Mathf.Abs(injury - Crabs.PinchInjury * NewMobs.Strength) < 0.006f && knocked && lying && Crabs.Flashes - flashesBefore >= 2 && Crabs.PinchSounds - soundsBefore == 1 && clicksHeard == 0 && heard.Audible && Mathf.Abs(landedAfter - Crabs.DamageAfterSnap) < 0.06f && widest >= 30f && faced,
                $"pinched {Crabs.PinchesTaken - pinchesBefore}; +{injury * 100f:0.0} injury; the Stone {(inHand != null ? "held" : "not held")} before, knocked out {knocked}, lying by the scout {lying}; "
                + $"glints and sparks {Crabs.Flashes - flashesBefore}; snaps heard {Crabs.PinchSounds - soundsBefore} ({heard}), the pinch landing {landedAfter:0.00} s after its snap, claw clicks {clicksHeard}; its claw opened up to {widest:0} degrees; facing the scout as it struck {faced}");
            ClearStatuses(afflictions);
            held = null;

            // Held: a crab in the scout's hands pinches them (as a scorpion stings its holder) and falls out of them.
            yield return SpawnIntoHand(Crabs.Template);
            Item heldCrab = held;
            CrabLook heldLook = heldCrab != null ? heldCrab.GetComponent<CrabLook>() : null;
            int heldClicksBefore = heldLook != null ? heldLook.ClicksHeard : 0;
            injuryBefore = afflictions.GetCurrentStatus(Status.Injury);
            pinchesBefore = Crabs.PinchesTaken;
            knockedBefore = Crabs.ItemsKnockedOut;
            soundsBefore = Crabs.PinchSounds;
            local.data.lookValues = new Vector2(local.data.lookValues.x, -35f);
            bool heldShot = false;
            // Its clicks counted while it is held (falling out of the hands, it is gone and a new crab lies on the ground).
            int heldClicks = heldLook != null ? 0 : -1;
            SoundWatch heldHeard = new SoundWatch();
            for (float until = Time.time + 8f; Time.time < until && Crabs.PinchSounds == soundsBefore;)
            {
                yield return null;
                heldHeard.Before();
                if (heldLook != null)
                {
                    heldClicks = heldLook.ClicksHeard - heldClicksBefore;
                }
                if (!heldShot && heldCrab != null && heldCrab.GetComponent<BeachCrab>() is BeachCrab inHands && inHands.Striking)
                {
                    heldShot = true;
                    Screenshot("crab-held-pinch");
                }
            }
            float heldSnapAt = Time.time;
            yield return heldHeard.After();
            float heldLandedAfter = -1f;
            for (float until = Time.time + 1f; Time.time < until && Crabs.PinchesTaken == pinchesBefore;)
            {
                yield return null;
                if (heldLook != null)
                {
                    heldClicks = heldLook.ClicksHeard - heldClicksBefore;
                }
            }
            if (Crabs.PinchesTaken > pinchesBefore)
            {
                heldLandedAfter = Time.time - heldSnapAt;
            }
            yield return new WaitForSeconds(0.7f);
            injury = afflictions.GetCurrentStatus(Status.Injury) - injuryBefore;
            bool fellOut = local.data.currentItem == null && Crabs.ItemsKnockedOut > knockedBefore;
            BeachCrab dropped = BeachCrabs().OrderBy(c => Vector3.Distance(c.transform.position, local.Center)).FirstOrDefault();
            bool droppedNear = dropped != null && Vector3.Distance(dropped.transform.position, local.Center) < 4f;
            if (dropped != null)
            {
                dropped.Unaware = true;
            }
            Check("a crab held pinches its holder (10 injury), as a scorpion stings, with one snap and no claw clicks, and falls out of their hands (17)",
                Crabs.PinchesTaken > pinchesBefore && Mathf.Abs(injury - Crabs.PinchInjury * NewMobs.Strength) < 0.006f && fellOut && droppedNear && Crabs.PinchSounds - soundsBefore == 1 && heldHeard.Audible && Mathf.Abs(heldLandedAfter - Crabs.DamageAfterSnap) < 0.06f && heldClicks == 0,
                $"pinched {Crabs.PinchesTaken - pinchesBefore}; +{injury * 100f:0.0} injury; snaps heard {Crabs.PinchSounds - soundsBefore} ({heldHeard}), the pinch landing {heldLandedAfter:0.00} s after its snap, claw clicks while held {heldClicks}; out of the hands {fellOut}; a crab on the ground by the scout {droppedNear}");
            ClearStatuses(afflictions);
            held = null;

            // Cooked: a crab leg (shown so, named so, its icon in the slot); eaten, its hunger and extra stamina, with ascent
            // 15's half food on (the climb's) and off; burnt, as this ascent's campfires burn it.
            string Eaten(bool half)
            {
                return half ? "with half food (15)" : "without it";
            }
            List<string> food = new List<string>();
            bool foodRight = true, looksRight = true;
            float shellShare = 1f;
            string named = "", iconName = "";
            foreach (bool half in new[] { true, false })
            {
                halfFood.Value = half;
                yield return SpawnIntoHand(Crabs.Template);
                Item leg = held;
                if (leg == null)
                {
                    foodRight = false;
                    food.Add($"{Eaten(half)}: no crab in the hand");
                    continue;
                }
                leg.GetComponent<BeachCrab>().Unaware = true;
                leg.cooking.FinishCooking();
                yield return new WaitForSeconds(0.6f);
                CrabLook legLook = leg.GetComponent<CrabLook>();
                looksRight &= legLook.ShowsLeg;
                Color shell = legLook.leg.GetComponent<MeshRenderer>().materials[CrabBody.Shell].GetColor("_Tint");
                Color liveShell = CrabBody.Tints[CrabBody.Shell];
                shellShare = (shell.r + shell.g + shell.b) / (liveShell.r + liveShell.g + liveShell.b);
                named = leg.GetItemName();
                iconName = InventoryIcon(local);
                if (half)
                {
                    Screenshot("crab-leg-held");
                }
                Unlocked(() =>
                {
                    afflictions.SetStatus(Status.Hunger, 0.4f);
                    local.data.extraStamina = 0f;
                });
                float hungerBefore = afflictions.GetCurrentStatus(Status.Hunger);
                float staminaBefore = local.data.extraStamina;
                yield return EatHeld();
                float hunger = hungerBefore - afflictions.GetCurrentStatus(Status.Hunger);
                float stamina = local.data.extraStamina - staminaBefore;
                float wanted = half ? 0.05f : 0.1f;
                foodRight &= Mathf.Abs(hunger - wanted) < 0.006f && Mathf.Abs(stamina - wanted) < 0.006f;
                food.Add($"{Eaten(half)}: {hunger * 100f:0.0} hunger, {stamina * 100f:0.0} extra stamina");
            }
            halfFood.Value = halfFoodWas;
            Check("cooked, a crab is a crab leg (its model, darker than the live crab, its icon and its name); eaten, 10 hunger and 10 extra stamina, 5 and 5 with ascent 15's half food (17)",
                looksRight && shellShare < 0.45f && foodRight && named.IndexOf(LocalizedText.GetText(Crabs.LegNameKey), StringComparison.OrdinalIgnoreCase) >= 0 && iconName == (Crabs.LegIcon != null ? Crabs.LegIcon.name : "?"),
                $"shown as a leg {looksRight}, its shell {shellShare:0.00} as bright as the live crab's; named '{named}'; the slot's icon '{iconName}'; {string.Join("; ", food)}");

            // Pockets: put away, a live crab is dropped (only the hands hold it, as a scorpion); a cooked crab leg stays in
            // its pocket and comes back out a leg, and goes in a backpack (the game's stash, as its wheel allows it).
            int LegsLying() => UnityEngine.Object.FindObjectsByType<BeachCrab>(FindObjectsSortMode.None)
                .Count(c => c != null && c.GetComponent<Item>() is Item item && item.itemState == ItemState.Ground && Crabs.Cooked(item) > 0);
            yield return SpawnIntoHand(Crabs.Template);
            Item live = held;
            bool liveRight = false;
            string liveDetail = "no crab in the hand";
            if (live != null && local.refs.items.currentSelectedSlot.IsSome)
            {
                live.GetComponent<BeachCrab>().Unaware = true;
                byte slot = local.refs.items.currentSelectedSlot.Value;
                bool pocketable = live.UIData.canPocket, packable = live.UIData.canBackpack;
                int lyingBefore = BeachCrabs().Count;
                local.refs.items.EquipSlot(Optionable<byte>.None);
                for (float until = Time.time + 0.8f; Time.time < until;)
                {
                    yield return null;
                    foreach (BeachCrab crab in BeachCrabs())
                    {
                        crab.Unaware = true;
                    }
                }
                bool slotEmpty = local.player.GetItemSlot(slot).IsEmpty();
                bool putDown = BeachCrabs().Count == lyingBefore + 1;
                liveRight = !pocketable && !packable && slotEmpty && putDown && local.data.currentItem == null;
                liveDetail = $"may go in a pocket {pocketable}, in a backpack {packable}; put away: its slot empty {slotEmpty}, dropped by the scout {putDown}";
            }
            held = null;
            Check("a live crab is only held, as a scorpion: put away, it is dropped (17)", liveRight, liveDetail);

            yield return SpawnIntoHand(Crabs.Template);
            Item pocketed = held;
            bool legRight = false, packRight = false;
            string legDetail = "no crab in the hand", packDetail = "no crab leg in the hand";
            if (pocketed != null && local.refs.items.currentSelectedSlot.IsSome)
            {
                pocketed.GetComponent<BeachCrab>().Unaware = true;
                pocketed.cooking.FinishCooking();
                yield return new WaitForSeconds(0.6f);
                byte slot = local.refs.items.currentSelectedSlot.Value;
                bool pocketable = pocketed.UIData.canPocket;
                int legsBefore = LegsLying();
                local.refs.items.EquipSlot(Optionable<byte>.None);
                yield return new WaitForSeconds(0.8f);
                ItemSlot pocket = local.player.GetItemSlot(slot);
                bool kept = !pocket.IsEmpty() && Crabs.IsCrab(pocket.prefab) && Crabs.Cooked(pocket.prefab, pocket.data) > 0 && local.data.currentItem == null;
                bool notDropped = LegsLying() == legsBefore;
                local.refs.items.EquipSlot(Optionable<byte>.Some(slot));
                yield return new WaitForSeconds(1f);
                Item back = local.data.currentItem;
                bool backAsLeg = back != null && Crabs.IsCrab(back) && Crabs.Cooked(back) > 0 && back.GetComponent<CrabLook>().ShowsLeg;
                legRight = pocketable && kept && notDropped && backAsLeg;
                legDetail = $"may go in a pocket {pocketable}; put away: still in its slot {kept}, dropped {!notDropped}; taken out again: a crab leg in the hand {backAsLeg}";

                Item packPrefab = ItemByName("Backpack");
                Backpack backpack = packPrefab != null && backAsLeg ? SpawnInFront(packPrefab, 1.2f)?.GetComponent<Backpack>() : null;
                packDetail = "no backpack put down";
                if (backpack != null)
                {
                    yield return new WaitForSeconds(1f);
                    BackpackReference reference = BackpackReference.GetFromBackpackItem(backpack);
                    bool allowed = local.data.currentItem != null && local.data.currentItem.UIData.canBackpack;
                    Step("a cooked crab leg into a backpack", () => backpack.Stash(local, 0));
                    yield return new WaitForSeconds(1f);
                    BackpackData data = reference.GetData();
                    ItemSlot inside = data != null && data.itemSlots != null && data.itemSlots.Length > 0 ? data.itemSlots[0] : null;
                    bool stashed = inside != null && !inside.IsEmpty() && Crabs.IsCrab(inside.prefab) && Crabs.Cooked(inside.prefab, inside.data) > 0;
                    bool shownLeg = reference.GetVisuals() != null && reference.GetVisuals().TryGetSpawnedItem(0, out Item shown) && shown.GetComponent<CrabLook>() is CrabLook shownLook && shownLook.ShowsLeg;
                    LookAtPoint(local, backpack.transform.position + Vector3.up * 0.2f);
                    yield return new WaitForSeconds(0.4f);
                    Screenshot("crab-leg-in-backpack");
                    Vector3 eye = MainCamera.instance != null ? MainCamera.instance.transform.position : local.Head;
                    Info("crabs near the scout when the backpack was pictured", string.Join("; ", UnityEngine.Object.FindObjectsByType<BeachCrab>(FindObjectsSortMode.None)
                        .Where(c => c != null && Vector3.Distance(c.transform.position, local.Center) < 6f)
                        .Select(c =>
                        {
                            Item crabItem = c.GetComponent<Item>();
                            CrabLook crabLook = c.GetComponent<CrabLook>();
                            return $"view {c.photonView.ViewID}: {crabItem.itemState}, cooked {Crabs.Cooked(crabItem)}, {Vector3.Distance(c.transform.position, eye):0.00} m from the camera, "
                                + $"body shown {crabLook.crabBody.activeInHierarchy}, leg shown {crabLook.leg.activeInHierarchy}, under '{(c.transform.parent != null ? c.transform.parent.name : "nothing")}', {c.mobState}";
                        })));
                    Info("items held when the backpack was pictured", string.Join("; ", UnityEngine.Object.FindObjectsByType<Item>(FindObjectsSortMode.None)
                        .Where(i => i != null && i.itemState == ItemState.Held)
                        .Select(i => $"{i.name} view {i.photonView.ViewID} by {(i.holderCharacter != null ? i.holderCharacter.name : "nobody")}, the scout's current item {i == local.data.currentItem}, {Vector3.Distance(i.transform.position, eye):0.00} m from the camera")));
                    packRight = allowed && stashed && local.data.currentItem == null && shownLeg;
                    packDetail = $"its backpack wheel lets it in {allowed}; in the backpack's first slot {stashed}, out of the hand {local.data.currentItem == null}; shown in the backpack as a leg {shownLeg}";
                    PhotonNetwork.Destroy(backpack.gameObject);
                }
            }
            held = null;
            Check("a cooked crab leg goes in a pocket like any food: put away, it stays in its slot and comes back out a leg (17)", legRight, legDetail);
            Check("a cooked crab leg goes in a backpack, like any food (17)", packRight, packDetail);

            yield return SpawnIntoHand(Crabs.Template);
            Item burnt = held;
            string burntDetail = "no crab in the hand";
            bool burntRight = false;
            if (burnt != null)
            {
                burnt.GetComponent<BeachCrab>().Unaware = true;
                // What this ascent's campfires do to anything cooked on them (see A17_HotCampfires).
                burnt.cooking.wreckWhenCooked = true;
                burnt.cooking.FinishCooking();
                yield return new WaitForSeconds(0.6f);
                CrabLook burntLook = burnt.GetComponent<CrabLook>();
                Material legSkin = burntLook.leg.GetComponent<MeshRenderer>().materials[CrabBody.Shell];
                Color tint = legSkin.GetColor("_Tint");
                float shade = (tint.r + tint.g + tint.b) / 3f;
                int cooked = Crabs.Cooked(burnt);
                bool eatable = burnt.CanUsePrimary();
                string burntName = burnt.GetItemName();
                bool burntShown = burntLook.ShowsLeg;
                Screenshot("crab-leg-burnt");
                // Eaten: the game's burnt food (cooked four times or more) gives no hunger and no extra stamina, and 10
                // poison for each cooking from the fourth on (ascent 14's boost on top).
                Unlocked(() =>
                {
                    afflictions.SetStatus(Status.Hunger, 0.4f);
                    afflictions.SetStatus(Status.Poison, 0f);
                    local.data.extraStamina = 0f;
                });
                float hungerBefore = afflictions.GetCurrentStatus(Status.Hunger);
                float poisonBefore = afflictions.GetCurrentStatus(Status.Poison);
                float staminaBefore = local.data.extraStamina;
                if (eatable)
                {
                    yield return EatHeld();
                }
                float hunger = hungerBefore - afflictions.GetCurrentStatus(Status.Hunger);
                float poison = afflictions.GetCurrentStatus(Status.Poison) - poisonBefore;
                float stamina = local.data.extraStamina - staminaBefore;
                float wantedPoison = 0.1f * Mathf.Max(0, cooked - 3) * A15_StrongerAilments.Factor(Status.Poison);
                bool eaten = local.data.currentItem == null;
                burntRight = burntShown && cooked >= 4 && shade < 0.1f && eatable && eaten
                    && Mathf.Abs(hunger) < 0.006f && Mathf.Abs(stamina) < 0.006f && wantedPoison > 0f && Mathf.Abs(poison - wantedPoison) < 0.013f;
                burntDetail = $"cooked {cooked} times' worth; shown as a leg {burntShown}, its shell's shade {shade:0.000}; named '{burntName}'; can be eaten {eatable}; "
                    + $"eaten {eaten}: {hunger * 100f:0.0} hunger, {stamina * 100f:0.0} extra stamina, {poison * 100f:0.0} poison (the game's burnt food, as it lands now: {wantedPoison * 100f:0.0})";
                local.refs.items.EquipSlot(Optionable<byte>.None);
            }
            held = null;
            ClearStatuses(afflictions);
            Check("a crab cooked as this ascent's campfires cook is a burnt crab leg, still eaten as burnt food is: no hunger or extra stamina, only poison (17)", burntRight, burntDetail);
        }
        finally
        {
            halfFood.Value = halfFoodWas;
            foreach (BeachCrab crab in UnityEngine.Object.FindObjectsByType<BeachCrab>(FindObjectsSortMode.None))
            {
                crab.Unaware = false;
            }
            NoCrabsInPockets(local);
            held = null;
            calm();
            Protect(true);
        }
    }

    /// <summary>
    /// How crabs turn while they crawl: the most any of them turned within 2 s of crawling (a crab circling a spot turns
    /// all the while it goes), and how often they rested at a spot.
    /// </summary>
    private sealed class TurnWatch
    {
        private sealed class Track
        {
            internal Vector3 position;
            internal Vector3 heading;
            internal bool resting;
            internal readonly Queue<(float time, float turn)> turns = new Queue<(float time, float turn)>();
            internal float sum;
        }

        private readonly Dictionary<BeachCrab, Track> tracks = new Dictionary<BeachCrab, Track>();

        internal float Most { get; private set; }
        internal int Rests { get; private set; }
        internal int Watched => tracks.Count;

        internal void Step(IEnumerable<BeachCrab> crabs, float dt)
        {
            float now = Time.time;
            foreach (BeachCrab crab in crabs)
            {
                Vector3 heading = Vector3.ProjectOnPlane(crab.transform.forward, Vector3.up);
                if (!tracks.TryGetValue(crab, out Track track))
                {
                    tracks[crab] = new Track { position = crab.transform.position, heading = heading, resting = crab.Resting };
                    continue;
                }
                bool crawling = dt > 0f && Goats.Flat(crab.transform.position, track.position) / dt > 0.15f && crab.mobState == Mob.MobState.Walking;
                if (crawling && heading.sqrMagnitude > 1e-4f && track.heading.sqrMagnitude > 1e-4f)
                {
                    float turn = Vector3.Angle(track.heading, heading);
                    track.turns.Enqueue((now, turn));
                    track.sum += turn;
                }
                while (track.turns.Count > 0 && now - track.turns.Peek().time > 2f)
                {
                    track.sum -= track.turns.Dequeue().turn;
                }
                Most = Mathf.Max(Most, track.sum);
                if (crab.Resting && !track.resting)
                {
                    Rests++;
                }
                track.resting = crab.Resting;
                track.position = crab.transform.position;
                track.heading = heading;
            }
        }
    }

    /// <summary>
    /// How loud the game's sound comes out (what the listener mixes, after the game's mixer): the loudest it was over the
    /// half second before a snap, and in the 90 ms after it, before the pinch lands with the game's sounds for the injury
    /// and the hit (the snap stands out of what was playing by 6 dB or more). With the game's sound silent (muted, or not playing out) it cannot be judged.
    /// </summary>
    private sealed class SoundWatch
    {
        private readonly Queue<(float time, float level)> before = new Queue<(float time, float level)>();
        private readonly float[] samples = new float[1024];
        private float ambient = -120f;
        private float snap = -120f;

        private float Level()
        {
            AudioListener.GetOutputData(samples, 0);
            float peak = 0f;
            foreach (float sample in samples)
            {
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            return peak > 1e-6f ? 20f * Mathf.Log10(peak) : -120f;
        }

        /// <summary>While waiting for the snap: what is playing, the last half second of it kept.</summary>
        internal void Before()
        {
            before.Enqueue((Time.time, Level()));
            while (before.Count > 0 && Time.time - before.Peek().time > 0.5f)
            {
                before.Dequeue();
            }
        }

        /// <summary>From the snap on, for 90 ms (the pinch lands, with its sounds, after 150).</summary>
        internal IEnumerator After()
        {
            // What played up to 0.1 s before the snap, so the snap's own start is not counted in it.
            float snapAt = Time.time;
            ambient = before.Where(b => b.time < snapAt - 0.1f).Select(b => b.level).DefaultIfEmpty(-120f).Max();
            for (float until = Time.unscaledTime + 0.09f; Time.unscaledTime < until;)
            {
                snap = Mathf.Max(snap, Level());
                yield return null;
            }
        }

        internal bool Silent => snap <= -100f && ambient <= -100f;
        internal bool Audible => Silent || snap - ambient >= 6f;

        public override string ToString()
        {
            return Silent ? "the game's sound is silent: not judged" : $"the snap at {snap:0.0} dB, {snap - ambient:0.0} dB over the {ambient:0.0} dB before it";
        }
    }

    /// <summary>Empties the scout's slots of crabs and crab legs (so the next one put in the hand is the only one).</summary>
    private static void NoCrabsInPockets(Character local)
    {
        local.refs.items.EquipSlot(Optionable<byte>.None);
        for (byte slot = 0; slot < local.player.itemSlots.Length; slot++)
        {
            ItemSlot itemSlot = local.player.itemSlots[slot];
            if (!itemSlot.IsEmpty() && Crabs.IsCrab(itemSlot.prefab))
            {
                local.player.EmptySlot(Optionable<byte>.Some(slot), true);
            }
        }
    }

    /// <summary>The icon shown in the inventory for the selected slot (its texture's name).</summary>
    private static string InventoryIcon(Character local)
    {
        Optionable<byte> slot = local.refs.items.currentSelectedSlot;
        InventoryItemUI[] slots = UnityEngine.Object.FindObjectsByType<InventoryItemUI>(FindObjectsSortMode.None);
        foreach (InventoryItemUI ui in slots)
        {
            if (slot.IsSome && !ui.isBackpack && ui.transform.GetSiblingIndex() == slot.Value && ui.icon != null && ui.icon.texture != null)
            {
                return ui.icon.texture.name;
            }
        }
        return "none";
    }

    /// <summary>
    /// A close look at a crab (only when asked): its model from the side, the front, three-quarters and above, 1.6 m off
    /// on the beach (the scout's own view); scuttling sideways, seen from beside it; its pinch at a quarter of the speed;
    /// held; and the cooked crab leg on the ground.
    /// </summary>
    private IEnumerator CrabPreview()
    {
        Character local = Local;
        if (Map.CurrentPlace != Place.Shore || Crabs.Template == null)
        {
            Info("the crab preview is skipped", "it needs the beach (run with -Only 'crabview')");
            yield break;
        }
        for (float until = Time.time + 10f; Time.time < until && BeachCrabs().Count == 0;)
        {
            yield return null;
        }
        BeachCrab crab = BeachCrabs().OrderBy(c => Vector3.Distance(c.transform.position, local.Center)).FirstOrDefault();
        if (crab == null)
        {
            Info("the crab preview is skipped", "no crabs on the beach");
            yield break;
        }
        float timeScale = Time.timeScale;
        Protect(true);
        Action calm = CalmForMeasuring();
        GameObject side = null;
        NoCrabsInPockets(local);
        try
        {
            foreach (BeachCrab other in BeachCrabs())
            {
                other.Unaware = true;
            }
            crab.Still = true;
            Vector3? eye = StandNear(crab.transform.position, 1.5f, 2.2f) ?? StandNear(crab.transform.position, 1.5f, 3f, firm: false);
            if (eye.HasValue)
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, eye.Value + Vector3.up * 1.2f, false);
            }
            yield return new WaitForSeconds(1.5f);
            Vector3 spot = crab.transform.position;
            Vector3 toScout = local.Center - spot;
            toScout.y = 0f;
            toScout = toScout.sqrMagnitude > 0.01f ? toScout.normalized : Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, toScout);
            // Its model: it stays where it is (unaware, the scout too far for it), turned by hand.
            foreach ((string view, Vector3 facing) in new[] { ("side", across), ("front", toScout), ("three-quarters", (toScout + across).normalized), ("back", -toScout) })
            {
                crab.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
                yield return new WaitForSeconds(0.6f);
                LookAtPoint(local, spot + Vector3.up * 0.08f);
                yield return new WaitForSeconds(0.4f);
                Screenshot($"crab-model-{view}");
                yield return new WaitForSeconds(0.6f);
            }

            // Scuttling: seen from 1.5 m off its side wherever it goes, a picture every twentieth of a second at a quarter
            // of the speed.
            crab.Unaware = true;
            crab.Still = false;
            Time.timeScale = 0.25f;
            for (int frame = 1; frame <= 12; frame++)
            {
                Vector3 at = crab.transform.position;
                if (side != null)
                {
                    Destroy(side);
                }
                side = ViewCamera(at + across * 1.5f + Vector3.up * 0.5f, at + Vector3.up * 0.1f);
                RenderView(side, $"crab-walk-{frame}");
                yield return new WaitForSeconds(0.05f);
            }
            Time.timeScale = timeScale;

            // The pinch at a quarter of the speed: the scout beside it, the side camera on them both.
            Vector3? close = StandNear(crab.transform.position, 1.1f, 1.5f) ?? StandNear(crab.transform.position, 1.1f, 1.8f, firm: false);
            if (close.HasValue)
            {
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, close.Value + Vector3.up * 1.2f, false);
            }
            yield return new WaitForSeconds(0.5f);
            Destroy(side);
            Vector3 middle = (crab.transform.position + local.Center + Vector3.down * 0.9f) * 0.5f;
            Vector3 line = local.Center - crab.transform.position;
            line.y = 0f;
            Vector3 aside = Vector3.Cross(Vector3.up, line.sqrMagnitude > 0.01f ? line.normalized : Vector3.forward);
            side = ViewCamera(middle + aside * 2.2f + Vector3.up * 0.6f, middle + Vector3.up * 0.25f);
            crab.Unaware = false;
            for (float until = Time.time + 6f; Time.time < until && !crab.Striking;)
            {
                yield return null;
            }
            Time.timeScale = 0.25f;
            for (int frame = 1; frame <= 16; frame++)
            {
                RenderView(side, $"crab-pinch-{frame}");
                yield return new WaitForSeconds(0.08f);
            }
            Time.timeScale = timeScale;
            crab.Unaware = true;
            yield return new WaitForSeconds(1f);

            // Held, as the scout sees it.
            yield return SpawnIntoHand(Crabs.Template);
            held ??= local.data.currentItem;
            if (held != null && held.GetComponent<BeachCrab>() is BeachCrab inHands)
            {
                inHands.Unaware = true;
                local.data.lookValues = new Vector2(local.data.lookValues.x, -35f);
                yield return new WaitForSeconds(0.8f);
                Screenshot("crab-held");
                // Cooked in the hand: the crab leg.
                held.cooking.FinishCooking();
                yield return new WaitForSeconds(0.8f);
                Screenshot("crab-leg-in-hand");
                local.refs.items.EquipSlot(Optionable<byte>.None);
            }
            held = null;
        }
        finally
        {
            Time.timeScale = timeScale;
            if (side != null)
            {
                Destroy(side);
            }
            foreach (BeachCrab other in UnityEngine.Object.FindObjectsByType<BeachCrab>(FindObjectsSortMode.None))
            {
                other.Unaware = false;
                other.Still = false;
            }
            calm();
        }
    }

    /// <summary>
    /// The crab's and the crab leg's icons (only when asked): their models, as the game's icons are framed (turned to show
    /// them, a camera fitted to them), on black and on white to take the background out (see the repository's notes).
    /// </summary>
    private IEnumerator CrabIcons()
    {
        Item crab = Crabs.Template;
        CrabLook look = crab != null ? crab.GetComponent<CrabLook>() : null;
        if (look == null)
        {
            Fail("the crab is in the item database");
            yield break;
        }
        foreach ((string name, GameObject source, Quaternion turn) in new[]
        {
            ("crab", look.crabBody, Quaternion.Euler(-38f, 0f, 0f) * Quaternion.Euler(0f, 205f, 0f)),
            ("crableg", look.leg, Quaternion.Euler(-28f, 0f, 0f) * Quaternion.Euler(0f, -40f, 0f) * Quaternion.Euler(0f, 0f, 30f)),
        })
        {
            GameObject model = new GameObject("UltimateAscentIconModel");
            model.transform.position = new Vector3(0f, 3000f, 0f);
            foreach (MeshRenderer renderer in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }
                GameObject part = new GameObject(renderer.name);
                part.transform.SetParent(model.transform, false);
                Matrix4x4 pose = look.crabRoot.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                part.transform.localPosition = pose.GetColumn(3);
                part.transform.localRotation = pose.rotation;
                part.transform.localScale = pose.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            model.transform.rotation = turn;
            yield return null;
            Bounds bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (Renderer part in model.GetComponentsInChildren<Renderer>())
            {
                bounds.Encapsulate(part.bounds);
            }
            Camera camera = new GameObject("UltimateAscentIconCamera").AddComponent<Camera>();
            camera.fieldOfView = 20f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            float radius = bounds.extents.magnitude;
            camera.transform.position = bounds.center - Vector3.forward * (radius / Mathf.Sin(10f * Mathf.Deg2Rad) * 1.05f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            RenderTexture target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = target;
            foreach ((string ground, Color colour) in new[] { ("black", Color.black), ("white", Color.white) })
            {
                camera.backgroundColor = colour;
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                Texture2D image = new Texture2D(512, 512, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, $"UltimateAscent.SmokeTest.iconrender-{name}-{ground}.png");
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
                Destroy(image);
                Info("picture", System.IO.Path.GetFileName(path));
            }
            camera.targetTexture = null;
            target.Release();
            Destroy(camera.gameObject);
            Destroy(model);
        }
    }
}
