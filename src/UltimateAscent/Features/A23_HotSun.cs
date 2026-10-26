using System.Collections.Generic;
using System.Linq;
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
/// still protects, though from ascent 15 a quarter of the heat gets through (<see cref="A14_Items"/>).
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
/// down again at a third of the speed it roasted at. A burned item still shields you, but smokes and crumbles
/// away after 5 s more in the sun (7.5 s for a parasol, 2.5 s in the Mesa): it goes up with the sound of burning
/// and turns to the Kiln's ash where it was.</item>
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
    internal const float BurnSeconds = 20f;
    private const float SmokeFrom = 0.25f;

    /// <summary>A burned item held up to the sun crumbles away after this long (a quarter of the time it took to burn).</summary>
    internal const float CrumbleSeconds = 5f;

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
    /// <summary>A burned item crumbles away: the sound of it burning and its ash, on every client.</summary>
    private const ItemCooking.NetworkedCooking SunCrumbles = (ItemCooking.NetworkedCooking)88;

    /// <summary>The ash a crumbling item leaves where it was (the Kiln's flakes and smoke): flakes per cubic metre of the item, and a few puffs.</summary>
    private const float FlakesPerCubicMetre = 900f;
    private const int FewestFlakes = 60;
    private const int MostFlakes = 400;
    private const int AshPuffsEach = 6;

    internal const string SunSmokeName = "UltimateAscent Sun Smoke";

    private sealed class Exposure
    {
        /// <summary>0 = cold, 1 = burned (crumbling: 1 = crumbled away).</summary>
        internal float progress;
        /// <summary>An already burned item, crumbling away in the sun.</summary>
        internal bool crumbling;
        /// <summary>Crumbled away: it is shrinking in its ash and about to be gone.</summary>
        internal bool crumbled;
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
        // Ascent 15: sunscreen keeps off only three quarters of the heat.
        float share = local.data.wearingSunscreen ? A14_Items.SunscreenLeak : 1f;
        // The sun's heat lands as it comes, without ascent 14's boost.
        A15_StrongerAilments.AddUnboosted(local.refs.afflictions, CharacterAfflictions.STATUSTYPE.Hot, HeatPerSecond * heat * share * Time.deltaTime);
        return roasting;
    }

    private static void Roast(Item item, float burnSeconds)
    {
        exposures.TryGetValue(item, out Exposure exposure);
        if (item.cooking == null)
        {
            return;
        }
        if (BurnedItems.CookedLevel(item) >= BurnedItems.BurntCookLevel)
        {
            Crumble(item, exposure, burnSeconds * CrumbleSeconds / BurnSeconds);
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

    /// <summary>
    /// An item already burned still shields you, but smokes and crumbles away after <see cref="CrumbleSeconds"/> in
    /// the sun (the parasol's and the Mesa's paces as for roasting; in the shade it recovers at a third of that pace).
    /// </summary>
    private static void Crumble(Item item, Exposure exposure, float crumbleSeconds)
    {
        if (exposure == null || !exposure.crumbling)
        {
            exposure = new Exposure { crumbling = true };
            exposures[item] = exposure;
        }
        if (exposure.crumbled)
        {
            return;
        }
        exposure.burnSeconds = crumbleSeconds;
        exposure.progress += Time.deltaTime / crumbleSeconds;
        SetSmoke(item, exposure, true);
        if (exposure.progress >= 1f && !item.consuming && item.photonView != null && item.photonView.IsMine)
        {
            SetSmoke(item, exposure, false);
            exposure.crumbled = true;
            Crumbled++;
            item.photonView.RPC("SyncCookingStateRPC", RpcTarget.All, SunCrumbles);
            item.StartCoroutine(item.ConsumeDelayed(ignoreActions: true));
            Plugin.Log.LogInfo($"Ascent 23: {NameOf(item)} crumbled away in the sun.");
        }
    }

    /// <summary>On every client: the sound of the item burning up, and the item turned to the Kiln's ash where it was.</summary>
    private static void ShowCrumbling(ItemCooking cooking)
    {
        Bounds bounds = BoundsOf(cooking);
        SFX_Instance[] sounds = BurnSounds();
        foreach (SFX_Instance sound in sounds)
        {
            sound.Play(bounds.center);
        }
        if (sounds.Length > 0)
        {
            CrumbleSoundPlays++;
        }
        if (!MakeAsh())
        {
            return;
        }
        Vector3 size = Vector3.Max(bounds.size, Vector3.one * 0.15f);
        int count = Mathf.Clamp(Mathf.RoundToInt(size.x * size.y * size.z * FlakesPerCubicMetre), FewestFlakes, MostFlakes);
        Emit(ashFlakes, bounds.center, size, count);
        Emit(ashSmoke, bounds.center, size, AshPuffsEach);
        AshPuffs++;
        AshFlakes += count;
    }

    /// <summary>Where the item is drawn: all its renderers together (a parasol's canopy as well as its handle).</summary>
    private static Bounds BoundsOf(ItemCooking cooking)
    {
        Bounds bounds = new Bounds(cooking.item != null ? cooking.item.Center() : cooking.transform.position, Vector3.zero);
        foreach (Renderer renderer in cooking.GetComponentsInChildren<Renderer>())
        {
            if (renderer.enabled && !(renderer is ParticleSystemRenderer))
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }

    private static void Emit(ParticleSystem particles, Vector3 at, Vector3 size, int count)
    {
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.scale = size;
        particles.Emit(new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true }, count);
    }

    /// <summary>The game's sound of fire burning a scout (a campfire's when lit), and wood splintering as the item falls apart.</summary>
    private static readonly string[] BurnSoundNames = { "SFXI Dmg Fire 1", "SFXI Torch Impact 2" };
    private static SFX_Instance[] burnSounds;

    private static SFX_Instance[] BurnSounds()
    {
        if (burnSounds == null || burnSounds.Length < BurnSoundNames.Length || burnSounds.Any(s => s == null))
        {
            SFX_Instance[] loaded = Resources.FindObjectsOfTypeAll<SFX_Instance>();
            burnSounds = BurnSoundNames.Select(name => loaded.FirstOrDefault(s => s != null && s.name == name)).Where(s => s != null).ToArray();
        }
        return burnSounds;
    }

    private static ParticleSystem ashFlakes;
    private static ParticleSystem ashSmoke;

    /// <summary>
    /// The Kiln's ash flakes and smoke (see <see cref="KilnAsh"/>), made once, emitting only when an item crumbles: the flakes
    /// drift down and the smoke rises; both show at once and fade at the end of their lives.
    /// </summary>
    private static bool MakeAsh()
    {
        if (ashFlakes != null && ashSmoke != null)
        {
            return true;
        }
        ParticleSystem template = SmokeTemplate();
        if (template == null)
        {
            return false;
        }
        ashFlakes = KilnAsh.Particles(template, "UltimateAscent Crumbled Ash", Vector3.one, 2000, 2.5f, 4.5f, KilnAsh.FlakeSizeMin, KilnAsh.FlakeSizeMax, 0.1f, 0.6f,
            KilnAsh.FlakeDark, KilnAsh.FlakeLight, 0.12f);
        ashSmoke = KilnAsh.Particles(template, "UltimateAscent Crumbled Ash Smoke", Vector3.one, 300, 1.2f, 2.2f, 0.12f, 0.3f, 0.15f, 0.45f,
            KilnAsh.SmokeDark, KilnAsh.SmokeLight, -0.02f);
        foreach (ParticleSystem made in new[] { ashFlakes, ashSmoke })
        {
            Object.DontDestroyOnLoad(made.gameObject);
            ParticleSystem.ColorOverLifetimeModule fade = made.colorOverLifetime;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);
            made.Play(true);
        }
        return true;
    }

    /// <summary>For the tests: items crumbled away in the sun, and the burning sounds, the ash and its flakes this client showed for them.</summary>
    internal static int Crumbled;
    internal static int CrumbleSoundPlays;
    internal static int AshPuffs;
    internal static int AshFlakes;

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
            if (ExplodesWhenCooked(item.cooking, level + 1))
            {
                // It blew up as overcooked items do (a Lantern, a Faerie Lantern) and is gone: no more steps, so it
                // cannot go off again before the game removes it.
                Plugin.Log.LogInfo($"Ascent 23: {NameOf(item)} exploded in the sun.");
                return;
            }
        }
        Plugin.Log.LogInfo($"Ascent 23: {NameOf(item)} burned in the sun.");
    }

    /// <summary>The item's own explosion goes off at this cooking step (the game's rule, its fuel condition included).</summary>
    private static bool ExplodesWhenCooked(ItemCooking cooking, int cooked)
    {
        foreach (AdditionalCookingBehavior behavior in cooking.additionalCookingBehaviors)
        {
            if (behavior is CookingBehavior_Explode explode && cooked >= explode.cookedAmountToTrigger
                && (!explode.dontRunIfOutOfFuel || cooking.item.GetData<FloatItemData>(DataEntryKey.Fuel).Value > 0f))
            {
                return true;
            }
        }
        return false;
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
            if (newState == SunCrumbles)
            {
                ShowCrumbling(__instance);
                return false;
            }
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
