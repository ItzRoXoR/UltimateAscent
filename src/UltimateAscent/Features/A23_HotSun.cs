using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 23 (13DDA + More Ascents, plus a new rule): the sun is hot everywhere except the Alpine,
/// up to the Caldera and the Gloom (from there on it is the game's sun again), and whatever you hold up
/// to shield yourself from it is roasted.
/// <list type="bullet">
/// <item>Standing in direct sunlight adds heat (the Mesa keeps its own, stronger sun). Sunscreen
/// still protects, though from ascent 14 a quarter of the heat gets through (<see cref="A14_Items"/>).
/// In the Alpine the sun does nothing at all; its nights are long instead
/// (<see cref="A18_AlpineNights"/>).</item>
/// <item>Whatever you shield yourself with against direct sunlight protects you, but takes the sun
/// instead: an open parasol, a glider held overhead, or any held item that blocks the sun. Only
/// items in that use roast; anything else you carry is safe. From ascent 18 this happens in the
/// Mesa's intense midday sun (the game's own hot sun there); from 23 wherever the sun is
/// scorching.</item>
/// <item>A roasting item first stays clean for 5 s, then smokes heavily for 15 s, and is then
/// fully burned at once, skipping the cooking stages in between. A parasol, made for the sun, takes
/// 1.5 times as long; the Mesa's sun roasts twice as fast. In the shade it stops smoking and cools
/// down again at a third of the speed it roasted at. A burned item stays in your hands.</item>
/// </list>
/// Burned items burn your hands from ascent 17 on (<see cref="BurnedItems"/>). While True Final
/// Ascent runs an Apocalypse weather the sun rules rest, heat and roasting alike, the way TFA rests
/// More Ascents' sun: its weather, its own sun events included, is TFA's to tune. They rest too while a
/// snowstorm blows where the scout is (ascent 18: no sun burns through one, see A18_ExtremeWeather).
/// A player's Hot Mode (<see cref="ChallengeModes"/>) brings this sun to ascents below 23: the same roasting, 65% of the heat.
/// </summary>
internal static class A23_HotSun
{
    private const float HeatPerSecond = 0.005f;

    /// <summary>Ascent 23's scorching sun applies where the scouts are: on, and not yet at the Caldera or the Gloom.</summary>
    internal static bool ScorchingHere => Rules.Enabled(Lvl.HotSun) && A09_FogAndKiln.BeforeTheVolcano;

    /// <summary>How long an item takes to burn in the sun, and the share of it before it starts smoking (5 of 20 s).</summary>
    private const float BurnSeconds = 20f;
    private const float SmokeFrom = 0.25f;

    private const float ParasolSlowdown = 1.5f;
    private const float MesaSpeedup = 2f;
    private const float CoolingRate = 1f / 3f;

    /// <summary>The game's own Mesa sun burns from this sun intensity on (its HotSun).</summary>
    private const float MesaSunIntensity = 5f;

    /// <summary>
    /// Sun smoke on and off, sent as the game's cooking-state message so every client shows it. The
    /// game knows states 0-3; ascent 16 uses 85.
    /// </summary>
    private const ItemCooking.NetworkedCooking SunSmokeOn = (ItemCooking.NetworkedCooking)86;
    private const ItemCooking.NetworkedCooking SunSmokeOff = (ItemCooking.NetworkedCooking)87;

    internal const string SunSmokeName = "UltimateAscent Sun Smoke";

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
        if (local != null && local.data != null && !local.data.dead && !local.inAirport && Map.Ready && !Map.InAlpine &&
            DayNightManager.instance != null && DayNightManager.instance.sun != null && !Compat.Tfa.WeatherRunning && !A18_ExtremeWeather.SnowstormHidesSun)
        {
            float intensity = DayNightManager.instance.sun.intensity;
            bool scorching = ScorchingHere && intensity > 1f;
            bool mesaSun = Rules.Enabled(Lvl.ExtremeWeather) && Map.InMesa && intensity >= MesaSunIntensity;
            // Hot Mode: this ascent's sun below ascent 23, where it would scorch: it roasts the same, and heats at 65%.
            bool hotMode = ChallengeModes.HotRoasts && A09_FogAndKiln.BeforeTheVolcano && intensity > 1f;
            if (scorching || mesaSun || hotMode)
            {
                roasted = Sunbathe(local, scorching ? 1f : hotMode ? ChallengeModes.HotModeHeat : 0f);
            }
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

    /// <summary>
    /// Applies the sun to the local scout; returns the held item that is roasting, if any. The scout
    /// is only heated by ascent 23's scorching sun, at <paramref name="heat"/> of its strength (Hot Mode's is half;
    /// the Mesa's own sun heats by itself).
    /// </summary>
    private static Item Sunbathe(Character local, float heat)
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
        if (heat <= 0f || shields || Map.InMesa || (local.data.wearingSunscreen && !A14_Items.SunscreenLeaks) || (RunSettings.IsCustomRun && RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_Heat) == 0))
        {
            return roasting;
        }
        // Ascent 14: sunscreen keeps off only three quarters of the heat.
        float share = local.data.wearingSunscreen ? A14_Items.SunscreenLeak : 1f;
        local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, HeatPerSecond * heat * share * Time.deltaTime);
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
        SetSmoke(item, exposure, exposure.progress >= SmokeFrom);
        if (exposure.progress >= 1f)
        {
            SetSmoke(item, exposure, false);
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
        item.photonView.RPC("SyncCookingStateRPC", RpcTarget.All, on ? SunSmokeOn : SunSmokeOff);
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
    /// item goes straight from smoking to burned for everyone.
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
    /// The sun's smoke is a heavier copy of the item's own cooking smoke (or a backpack's, for items
    /// the game gave none, a parasol for one), so a campfire's cooking smoke stays as in the game.
    /// </summary>
    [HarmonyPatch(typeof(ItemCooking), nameof(ItemCooking.SyncCookingStateRPC))]
    private static class SunSmoke
    {
        private static bool Prefix(ItemCooking __instance, ItemCooking.NetworkedCooking newState)
        {
            if (newState != SunSmokeOn && newState != SunSmokeOff)
            {
                return true;
            }
            ParticleSystem smoke = SmokeOf(__instance, newState == SunSmokeOn);
            if (smoke != null)
            {
                if (newState == SunSmokeOn)
                {
                    smoke.Play(true);
                }
                else
                {
                    smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            return false;
        }
    }

    private static ParticleSystem SmokeOf(ItemCooking cooking, bool create)
    {
        Transform existing = cooking.transform.Find(SunSmokeName);
        if (existing != null || !create)
        {
            return existing != null ? existing.GetComponent<ParticleSystem>() : null;
        }
        ItemParticles particles = cooking.item != null ? cooking.item.particles : null;
        ParticleSystem template = particles != null && particles.smoke != null ? particles.smoke : SmokeTemplate();
        if (template == null)
        {
            return null;
        }
        ParticleSystem smoke = Object.Instantiate(template, cooking.transform);
        smoke.name = SunSmokeName;
        if (template.transform.IsChildOf(cooking.transform))
        {
            // The item's own smoke: same spot.
            smoke.transform.SetPositionAndRotation(template.transform.position, template.transform.rotation);
        }
        else
        {
            // A backpack's: where it sits on the backpack.
            smoke.transform.localPosition = template.transform.localPosition;
            smoke.transform.localRotation = template.transform.localRotation;
        }
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        Thicken(smoke);
        return smoke;
    }

    /// <summary>Three times the particles, bigger, longer-lived and darker than cooking smoke.</summary>
    private static void Thicken(ParticleSystem smoke)
    {
        ParticleSystem.MainModule main = smoke.main;
        main.startSizeMultiplier *= 1.6f;
        main.startLifetimeMultiplier *= 1.3f;
        main.maxParticles = Mathf.Max(main.maxParticles, 2000);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.32f, 0.3f, 0.3f, 1f), new Color(0.48f, 0.47f, 0.47f, 1f));
        ParticleSystem.EmissionModule emission = smoke.emission;
        emission.rateOverTimeMultiplier *= 3f;
    }

    private static ParticleSystem smokeTemplate;

    internal static ParticleSystem SmokeTemplate()
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
