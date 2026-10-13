using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 23 (13DDA + More Ascents, plus a new rule): the sun is hot everywhere except the Alpine,
/// and whatever you hold up to shield yourself from it is slowly roasted.
/// <list type="bullet">
/// <item>Standing in direct sunlight adds heat (the Mesa keeps its own, stronger sun). Sunscreen
/// still protects. In the Alpine the sun does nothing at all; its nights are long instead
/// (<see cref="A18_AlpineNights"/>).</item>
/// <item>Whatever you shield yourself with against direct sunlight protects you, but takes the sun
/// instead: an open parasol, a glider held overhead, or any held item that blocks the sun. Only
/// items in that use roast; anything else you carry is safe. A parasol roasts 1.5 times slower than
/// other items, because it is made for the sun.</item>
/// <item>A roasting item smokes, like a backpack on hot rock, and when its time is up it is fully
/// burned at once, skipping the cooking stages in between: after 30 seconds of sunshine, 45 for a
/// parasol. The Mesa's sun roasts twice as fast (15 s, parasols 22.5 s). In the shade it stops
/// smoking and cools down again at a third of the speed it roasted at. A burned item stays in
/// your hands.</item>
/// </list>
/// Burned items burn your hands from ascent 17 on (<see cref="BurnedItems"/>).
/// </summary>
internal static class A23_HotSun
{
    private const float HeatPerSecond = 0.005f;
    private const float BurnSeconds = 30f;
    private const float ParasolSlowdown = 1.5f;
    private const float MesaSpeedup = 2f;
    private const float CoolingRate = 1f / 3f;

    private sealed class Exposure
    {
        /// <summary>0 = cold, 1 = burned.</summary>
        internal float progress;
        /// <summary>How long the item would last at the sun it got last (cooling uses the same pace).</summary>
        internal float burnSeconds = BurnSeconds;
        internal bool smoking;
    }

    private static readonly Dictionary<Item, Exposure> exposures = new Dictionary<Item, Exposure>();
    private static readonly List<Item> warmItems = new List<Item>();

    internal static void Reset()
    {
        exposures.Clear();
    }

    /// <summary>Called every frame for the local scout.</summary>
    internal static void Tick()
    {
        Character local = Character.localCharacter;
        Item roasted = null;
        if (Rules.Enabled(Lvl.HotSun) && local != null && local.data != null && !local.data.dead && !local.inAirport && Map.Ready && !Map.InAlpine &&
            DayNightManager.instance != null && DayNightManager.instance.sun != null && DayNightManager.instance.sun.intensity > 1f)
        {
            roasted = Sunbathe(local);
        }
        if (exposures.Count == 0)
        {
            return;
        }
        // Everything that is not in the sun right now cools down.
        warmItems.Clear();
        warmItems.AddRange(exposures.Keys);
        foreach (Item item in warmItems)
        {
            if (item == null)
            {
                exposures.Remove(item);
            }
            else if (item != roasted && Cool(item, exposures[item]))
            {
                exposures.Remove(item);
            }
        }
    }

    /// <summary>Applies the sun to the local scout; returns the held item that is roasting, if any.</summary>
    private static Item Sunbathe(Character local)
    {
        Transform sun = DayNightManager.instance.sun.transform;
        Item held = local.data.currentItem;
        RaycastHit hit = HelperFunctions.LineCheck(local.Center + sun.forward * -1000f, local.Center, HelperFunctions.LayerType.AllPhysical);
        bool blockedByHeld = hit.transform != null && held != null && hit.transform.GetComponentInParent<Item>() == held;
        bool inSunlight = hit.transform == null || hit.transform.root == local.transform.root || blockedByHeld;
        if (!inSunlight)
        {
            return null;
        }
        Parasol parasol = null;
        bool isParasol = held != null && held.TryGetComponent(out parasol);
        bool shields = held != null && (blockedByHeld || held.gliderHold || (isParasol && parasol.isOpen));
        Item roasting = null;
        if (shields)
        {
            float burnSeconds = isParasol ? BurnSeconds * ParasolSlowdown : BurnSeconds;
            Roast(held, Map.InMesa ? burnSeconds / MesaSpeedup : burnSeconds);
            roasting = held;
        }
        if (shields || Map.InMesa || local.data.wearingSunscreen || (RunSettings.IsCustomRun && RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_Heat) == 0))
        {
            return roasting;
        }
        local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, HeatPerSecond * Time.deltaTime);
        return roasting;
    }

    private static void Roast(Item item, float burnSeconds)
    {
        exposures.TryGetValue(item, out Exposure exposure);
        if (item.cooking == null || BurnedItems.CookedLevel(item) >= BurnedItems.BurntCookLevel)
        {
            // Already burned: nothing left to roast.
            if (exposure != null)
            {
                SetSmoke(item, exposure, false);
                exposures.Remove(item);
            }
            return;
        }
        if (exposure == null)
        {
            exposure = new Exposure();
            exposures[item] = exposure;
        }
        exposure.burnSeconds = burnSeconds;
        exposure.progress += Time.deltaTime / burnSeconds;
        SetSmoke(item, exposure, true);
        if (exposure.progress >= 1f)
        {
            BurnNow(item);
        }
    }

    private static void SetSmoke(Item item, Exposure exposure, bool on)
    {
        if (exposure.smoking == on || item.photonView == null)
        {
            return;
        }
        exposure.smoking = on;
        item.photonView.RPC("SyncCookingStateRPC", RpcTarget.All, on ? ItemCooking.NetworkedCooking.StartSmoking : ItemCooking.NetworkedCooking.StopSmoking);
    }

    /// <summary>Cools an item that is out of the sun; returns true once it has fully cooled down.</summary>
    private static bool Cool(Item item, Exposure exposure)
    {
        SetSmoke(item, exposure, false);
        exposure.progress = Mathf.Max(0f, exposure.progress - Time.deltaTime * CoolingRate / exposure.burnSeconds);
        return exposure.progress <= 0f;
    }

    /// <summary>
    /// Fully burned in one go: the cooking steps it still lacks all happen in the same frame, so the
    /// item goes straight from smoking to burned for everyone (each step stops the smoke as well).
    /// </summary>
    private static void BurnNow(Item item)
    {
        exposures.Remove(item);
        if (item.photonView == null || !item.photonView.AmController)
        {
            return;
        }
        for (int level = BurnedItems.CookedLevel(item); level < BurnedItems.BurntCookLevel; level++)
        {
            item.cooking.FinishCooking();
        }
        Plugin.Log.LogInfo($"Ascent 23: {NameOf(item)} burned in the sun.");
    }

    /// <summary>
    /// Items the game gave no smoke of their own (a parasol, for one) borrow a backpack's, so a
    /// roasting shield smokes for everybody like a backpack on hot rock.
    /// </summary>
    [HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.SyncCookingStateRPC))]
    private static class BorrowedSmoke
    {
        private static void Prefix(ItemCooking __instance, ItemCooking.NetworkedCooking newState)
        {
            if (newState != ItemCooking.NetworkedCooking.StartSmoking || !Rules.Enabled(Lvl.HotSun))
            {
                return;
            }
            ItemParticles particles = __instance.item != null ? __instance.item.particles : null;
            if (particles == null || particles.smoke != null)
            {
                return;
            }
            ParticleSystem template = SmokeTemplate();
            if (template == null)
            {
                return;
            }
            ParticleSystem smoke = Object.Instantiate(template, __instance.transform);
            smoke.transform.localPosition = template.transform.localPosition;
            smoke.transform.localRotation = template.transform.localRotation;
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.smoke = smoke;
        }
    }

    private static ParticleSystem smokeTemplate;

    private static ParticleSystem SmokeTemplate()
    {
        if (smokeTemplate != null)
        {
            return smokeTemplate;
        }
        ParticleSystem any = null;
        foreach (Item item in SingletonAsset<ItemDatabase>.Instance.itemLookup.Values)
        {
            ItemParticles particles = item != null ? item.GetComponent<ItemParticles>() : null;
            if (particles == null || particles.smoke == null)
            {
                continue;
            }
            if (item is Backpack)
            {
                return smokeTemplate = particles.smoke;
            }
            if (any == null)
            {
                any = particles.smoke;
            }
        }
        return smokeTemplate = any;
    }

    private static string NameOf(Item item)
    {
        return item.UIData != null && !string.IsNullOrEmpty(item.UIData.itemName) ? item.UIData.itemName : item.name;
    }
}
