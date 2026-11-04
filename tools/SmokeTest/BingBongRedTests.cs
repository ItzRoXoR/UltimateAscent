using System.Collections;
using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>Ascent 20's red screen.</summary>
internal sealed partial class Runner
{
    /// <summary>The game's glow over the local scout for its crab status, while a check listens.</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.PlayParticle))]
    internal static class CrabGlows
    {
        internal static bool Listening;
        internal static int Count;

        private static void Postfix(CharacterAfflictions __instance, Status statusType)
        {
            if (Listening && statusType == Status.Crab && __instance.character == Character.localCharacter)
            {
                Count++;
            }
        }
    }

    /// <summary>The game's screen flash for a crab status, while a check listens.</summary>
    [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.AddStatusFX))]
    internal static class CrabFlashes
    {
        internal static bool Listening;
        internal static int Count;

        private static void Postfix(Status type)
        {
            if (Listening && type == Status.Crab)
            {
                Count++;
            }
        }
    }

    /// <summary>
    /// A neglected Bing Bong turns the screen red (20): Bing Bong is held, then put away into a pocket. Before the crab
    /// comes (15 s) the screen stays clear; from its first step on the bar the screen turns red over 10 s, from a faint
    /// tint to a dark red over nearly all of it, while the crab fills the bar without the game's effects (no glow over
    /// the scout, no injury flash). Bing Bong back in the hands clears the red within a second.
    /// </summary>
    private IEnumerator BingBongRedScreen()
    {
        Item bingBong = SingletonAsset<ItemDatabase>.Instance.itemLookup.Values.FirstOrDefault(i => i != null && (i.itemTags & Item.ItemTags.BingBong) != 0);
        if (!Rules.Enabled(Lvl.BingBong) || bingBong == null)
        {
            Info("the red screen check is skipped", bingBong == null ? "no Bing Bong item in the item database" : $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        Protect(true);
        yield return SpawnIntoHand(bingBong);
        if (held == null)
        {
            yield break;
        }
        yield return new WaitForSeconds(A20_BingBong.RedClearSeconds + 0.2f);
        Step("put Bing Bong away into a pocket", () => local.refs.items.EquipSlot(Optionable<byte>.None));
        held = null;

        // Before the crab: clear.
        bool redEarly = false;
        float giveUp = Time.time + 25f;
        while (A20_BingBong.neglectedFor < 14.8f && Time.time < giveUp)
        {
            redEarly |= A20_BingBong.RedShown > 0f;
            yield return null;
        }
        Check("unheld, the screen stays clear until the crab comes at 15 s (20)", !redEarly && A20_BingBong.neglectedFor >= 14.8f,
            $"neglected for {A20_BingBong.neglectedFor:F1} s, red before {redEarly}");

        // From here the statuses are let through (the others cleared every frame): the crab fills the bar as the red comes.
        Protect(false);
        ClearStatuses(Afflictions);
        Meter crab = new Meter(Status.Crab);
        CrabGlows.Count = 0;
        CrabFlashes.Count = 0;
        CrabGlows.Listening = true;
        CrabFlashes.Listening = true;
        float redFrom = -1f;
        float crabAtRed = 0f;
        float crabFull = 0f;
        (float tint, float edges, float span) faint = (0f, 0f, 0f);
        (float tint, float edges, float span) full = (0f, 0f, 0f);
        try
        {
            // The red starts as the crab first lands on the bar.
            while (A20_BingBong.RedShown <= 0f && Time.time < giveUp + 5f)
            {
                ClearAllBut(Status.Crab);
                yield return null;
            }
            redFrom = A20_BingBong.neglectedFor;
            crabAtRed = Afflictions.GetCurrentStatus(Status.Crab);
            faint = RedNow();
            Screenshot("bingbong-red-faint");
            yield return CrabOnlyUntil(redFrom + A20_BingBong.RedSeconds / 2f, giveUp + 12f);
            Screenshot("bingbong-red-half");
            yield return CrabOnlyUntil(redFrom + A20_BingBong.RedSeconds + 0.3f, giveUp + 18f);
            full = RedNow();
            crabFull = Afflictions.GetCurrentStatus(Status.Crab);
            Screenshot("bingbong-red-full");
            yield return null;
        }
        finally
        {
            CrabGlows.Listening = false;
            CrabFlashes.Listening = false;
            crab.Stop();
            Protect(true);
        }
        yield return new WaitForSeconds(0.3f);
        Check("unheld, the screen turns red as the crab first lands on the bar (15 s on), over 10 s: a faint tint first, then dark red over nearly all of it (20)",
            redFrom >= 15f && redFrom < 15.7f && crabAtRed > 0f && faint.tint > 0f && faint.tint < 0.1f && faint.edges < 0.3f && A20_BingBong.Redness >= 0.999f
            && full.tint >= A20_BingBong.TintFull - 0.01f && full.edges >= A20_BingBong.EdgesFull - 0.01f && full.span >= A20_BingBong.EdgesCloseIn - 0.01f,
            $"red from {redFrom:F2} s of neglect, crab on the bar then {crabAtRed * 100f:0.#}: tint {faint.tint:0.00}, edges {faint.edges:0.00}, clear middle x{1f / faint.span:0.00}; "
            + $"10 s on: crab {crabFull * 100f:0.#}, tint {full.tint:0.00}, edges {full.edges:0.00}, clear middle x{1f / full.span:0.00}");
        Check("the crab comes as before, without the game's glow over the scout or its injury flash (20)", crabFull >= 0.45f && CrabGlows.Count == 0 && CrabFlashes.Count == 0,
            $"{crabFull * 100f:0.#} crab on the bar ({crab.Total:0.000} added in {crab.Seconds:F1} s); {CrabGlows.Count} glow(s), {CrabFlashes.Count} flash(es)");

        // Bing Bong back in the hands.
        int pocket = -1;
        for (int slot = 0; slot < local.player.itemSlots.Length; slot++)
        {
            ItemSlot itemSlot = local.player.itemSlots[slot];
            if (!itemSlot.IsEmpty() && itemSlot.prefab != null && itemSlot.prefab.itemID == bingBong.itemID)
            {
                pocket = slot;
            }
        }
        if (pocket < 0)
        {
            Fail("Bing Bong is in a pocket");
            yield break;
        }
        local.refs.items.lastEquippedSlotTime = 0f;
        Step("take Bing Bong out again", () => local.refs.items.EquipSlot(Optionable<byte>.Some((byte)pocket)));
        float heldAt = -1f;
        float clearedAt = -1f;
        for (float until = Time.time + 4f; Time.time < until && clearedAt < 0f;)
        {
            if (heldAt < 0f && local.data.currentItem != null && local.data.currentItem.itemID == bingBong.itemID)
            {
                heldAt = Time.time;
            }
            if (heldAt >= 0f && A20_BingBong.RedShown <= 0f)
            {
                clearedAt = Time.time;
            }
            yield return null;
        }
        Screenshot("bingbong-red-cleared");
        Check("Bing Bong back in the hands clears the red within a second (20)", heldAt >= 0f && clearedAt >= 0f && clearedAt - heldAt <= A20_BingBong.RedClearSeconds + 0.1f,
            heldAt < 0f ? "Bing Bong never came into the hands" : clearedAt < 0f ? $"still red {A20_BingBong.RedShown:0.00} after 4 s" : $"cleared {clearedAt - heldAt:0.00} s after he was in the hands");
        yield return new WaitForSeconds(0.3f);
        local.refs.items.EquipSlot(Optionable<byte>.None);
        yield return new WaitForSeconds(0.3f);
        local.player.EmptySlot(Optionable<byte>.Some((byte)pocket));
        Afflictions.UpdateWeight();
        Unlocked(() => ClearStatuses(Afflictions));
    }

    /// <summary>Waits for so long a neglect, with every status but the crab cleared every frame.</summary>
    private static IEnumerator CrabOnlyUntil(float seconds, float giveUp)
    {
        while (A20_BingBong.neglectedFor < seconds && Time.time < giveUp)
        {
            ClearAllBut(Status.Crab);
            yield return null;
        }
    }

    private static void ClearAllBut(Status keep)
    {
        foreach (Status type in CareStatuses)
        {
            if (type != keep && Afflictions.GetCurrentStatus(type) > 0f)
            {
                Afflictions.SetStatus(type, 0f);
            }
        }
    }

    /// <summary>The red as it shows now: the tint's and the edges' strength, and how far the edges have closed in.</summary>
    private static (float tint, float edges, float span) RedNow()
    {
        GameObject canvas = GameObject.Find("UltimateAscent Bing Bong Red");
        UnityEngine.UI.RawImage tint = canvas != null ? canvas.transform.Find("Tint")?.GetComponent<UnityEngine.UI.RawImage>() : null;
        UnityEngine.UI.RawImage edges = canvas != null ? canvas.transform.Find("Edges")?.GetComponent<UnityEngine.UI.RawImage>() : null;
        if (tint == null || edges == null || !canvas.activeInHierarchy)
        {
            return (0f, 0f, 0f);
        }
        return (tint.color.a, edges.color.a, edges.uvRect.width);
    }
}
