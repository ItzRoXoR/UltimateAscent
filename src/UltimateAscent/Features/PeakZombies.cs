using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: zombies dressed as scouts wait on the peak's grassy hill (<see cref="PeakHill"/>: the hill after the
/// Kiln or the Citadel, not the summit with the flare boxes). Ten places on its level ground, 6 m apart, are picked
/// for each climb by the host once a scout is near enough for the hill to be in the map; the hill's bee swarms are put
/// out then too (<see cref="ScatteredBees"/>). Each place gets its zombie as a scout comes within the game's own zombie
/// spawning range (the game removes zombies with nobody within 100 m), so they wait asleep like the game's; one the
/// game took away that way comes back when a scout does, a dead one does not. Half wear one look, half the other:
/// <list type="bullet">
/// <item>yellow skin, the Fairy outfit and hat (the flower), sparkling eyes, a toothy grin, freckles;</item>
/// <item>lime skin, the green tee with cargo shorts, the Sprout hat, solid angry eyes, a small frown, freckles.</item>
/// </list>
/// No sash and no medal, and none of the game's zombie mushrooms growing on them (they would hide the hats). Every
/// player's game dresses them (the look comes with the zombie); a player who has the game's zombie phobia setting on
/// sees the game's mushroom men instead. Otherwise they are the game's zombies, with ascent 17's zombie rules.
/// </summary>
internal static class PeakZombies
{
    internal const string Marker = "UltimateAscent.peakzombie";
    internal const int Count = 10;
    private const float Apart = 6f;
    private const float MaxSlope = 30f;
    private const string ZombiePrefab = "MushroomZombie";

    /// <summary>How often, and how many times, the hill is looked for while a scout is near it and it is not found.</summary>
    private const float LookAgain = 10f;
    private const int MostLooks = 6;

    /// <summary>A look: indices into the game's skins, eyes, mouths, accessories, outfits and hats.</summary>
    internal sealed class Look
    {
        internal string Skin, Eyes, Mouth, Accessory, Fit, Hat;
    }

    internal static readonly Look[] Looks =
    {
        new Look { Skin = "Skin_Yellow", Eyes = "Eye_Anime", Mouth = "Mouth_Teeth", Accessory = "Accessory_Face_Paint", Fit = "Fit_Fairy", Hat = "Hat_34_Fairy" },
        new Look { Skin = "Skin_Lime", Eyes = "Eye_Aggro", Mouth = "Mouth_Sad", Accessory = "Accessory_Face_Paint", Fit = "Fit_GreenTeeCargo", Hat = "Hat_35_Sprout" },
    };

    private static bool On => Rules.Enabled(Lvl.Hazards);

    /// <summary>The places of this climb (host).</summary>
    internal static readonly List<Vector3> Places = new List<Vector3>();

    /// <summary>Each place's zombie while it is there, and the places whose zombie has died (killed, or its time up).</summary>
    private static readonly Dictionary<int, MushroomZombie> standing = new Dictionary<int, MushroomZombie>();
    private static readonly HashSet<int> dead = new HashSet<int>();
    private static bool hillDone;
    private static int looks;
    private static float nextPick;
    private static float nextLook;
    private static float spawnRange = -1f;

    /// <summary>The zombies this game has dressed (for the tests).</summary>
    internal static int Dressed;

    internal static void Reset()
    {
        Places.Clear();
        standing.Clear();
        dead.Clear();
        hillDone = false;
        looks = 0;
        nextPick = 0f;
        nextLook = 0f;
        PeakHill.Reset();
    }

    internal static bool IsPeakZombie(MushroomZombie zombie, out int look)
    {
        look = 0;
        object[] data = zombie != null && zombie.photonView != null ? zombie.photonView.InstantiationData : null;
        if (data == null || data.Length < 2 || data[0] as string != Marker || !(data[1] is int index))
        {
            return false;
        }
        look = Mathf.Clamp(index, 0, Looks.Length - 1);
        return true;
    }

    /// <summary>Host; every frame.</summary>
    internal static void Tick()
    {
        if (!On || !Rules.IsHost || !PhotonNetwork.InRoom || !MapHandler.ExistsAndInitialized || LoadingScreenHandler.loading || Time.time < nextLook)
        {
            return;
        }
        nextLook = Time.time + 0.5f;
        // The hill, once a scout is near enough for it to be in the map: its zombie places and its bees, once a climb.
        if (!hillDone && looks < MostLooks && Time.time >= nextPick && PeakHill.ScoutNear())
        {
            nextPick = Time.time + LookAgain;
            looks++;
            OnTheHill();
        }
        if (!Rules.Hazard(RunSettings.SETTINGTYPE.Hazard_Zombies))
        {
            return;
        }
        Transform summit = PeakHill.Summit();
        float range = SpawnRange();
        for (int i = 0; i < Places.Count; i++)
        {
            // A zombie the game took away with nobody near (not dead) comes back when a scout does; a dead one does not.
            if (standing.TryGetValue(i, out MushroomZombie there) && there != null)
            {
                if (there.currentState == MushroomZombie.State.Dead)
                {
                    dead.Add(i);
                }
                continue;
            }
            if (dead.Contains(i) || !Character.AllCharacters.Any(c => c != null && !c.isBot && Vector3.Distance(c.Center, Places[i]) <= range))
            {
                continue;
            }
            Vector3 away = Places[i] - (summit != null ? summit.position : Places[i]);
            away.y = 0f;
            Quaternion facing = away.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-away.normalized) : Quaternion.identity;
            GameObject made = PhotonNetwork.Instantiate(ZombiePrefab, Places[i] + Vector3.up * 0.1f, facing, 0, new object[] { Marker, i % Looks.Length });
            standing[i] = made != null ? made.GetComponent<MushroomZombie>() : null;
            Plugin.Debug($"Ascent 17: a zombie on the peak's hill ({i + 1} of {Places.Count}).");
        }
    }

    /// <summary>How near a scout comes before a zombie is put there (the game's own zombie spawning range).</summary>
    private static float SpawnRange()
    {
        if (spawnRange < 0f)
        {
            GameObject prefab = Resources.Load<GameObject>(ZombiePrefab);
            MushroomZombie zombie = prefab != null ? prefab.GetComponent<MushroomZombie>() : null;
            spawnRange = zombie != null && zombie.distanceToEnable > 0f ? zombie.distanceToEnable : 60f;
        }
        return spawnRange;
    }

    /// <summary>The hill's ground (one pass over it): ten level places for the zombies, and the bee swarms.</summary>
    internal static void OnTheHill()
    {
        List<RaycastHit> ground = PeakHill.Ground(out string counts);
        List<Vector3> level = ground.Where(h => Vector3.Angle(h.normal, Vector3.up) < MaxSlope).Select(h => h.point).ToList();
        Places.Clear();
        Places.AddRange(PeakHill.Spread(level, Count, Apart));
        Plugin.Log.LogInfo($"Ascent 17: the peak's hill: {counts}, {level.Count} level; {Places.Count} places for zombies.");
        if (ground.Count == 0)
        {
            return;
        }
        hillDone = true;
        ScatteredBees.SpawnOnHill(ground.Select(h => h.point).ToList(), Places);
    }

    /// <summary>Every game dresses a peak zombie once it has set itself up (the game's own setup takes a frame).</summary>
    [HarmonyPatch(typeof(MushroomZombie), "Start")]
    private static class DressedOnStart
    {
        private static void Postfix(MushroomZombie __instance)
        {
            if (IsPeakZombie(__instance, out int look))
            {
                __instance.StartCoroutine(Dress(__instance, look));
            }
        }
    }

    /// <summary>The game picks shorts or a skirt for its own zombies; a peak zombie keeps what its outfit has.</summary>
    [HarmonyPatch(typeof(MushroomZombie), "RPC_SetOutfit")]
    private static class OwnOutfit
    {
        private static bool Prefix(MushroomZombie __instance) => !IsPeakZombie(__instance, out _);
    }

    private static IEnumerator Dress(MushroomZombie zombie, int look)
    {
        yield return null;
        yield return null;
        Apply(zombie, look);
        // The game fades its zombies in after a second, and puts the mushroom man on then for those who want him.
        yield return new WaitForSeconds(1.2f);
        Apply(zombie, look);
    }

    internal static void Apply(MushroomZombie zombie, int look)
    {
        if (zombie == null || zombie.character == null || zombie.isMushroomMan)
        {
            return;
        }
        CharacterCustomization customization = zombie.character.refs.customization;
        Customization options = Singleton<Customization>.Instance;
        if (customization == null || options == null)
        {
            return;
        }
        Look chosen = Looks[look];
        PersistentPlayerData data = new PersistentPlayerData();
        data.customizationData.currentSkin = Index(options.skins, chosen.Skin);
        data.customizationData.currentEyes = Index(options.eyes, chosen.Eyes);
        data.customizationData.currentMouth = Index(options.mouths, chosen.Mouth);
        data.customizationData.currentAccessory = Index(options.accessories, chosen.Accessory);
        data.customizationData.currentOutfit = Index(options.fits, chosen.Fit);
        data.customizationData.currentHat = Index(options.hats, chosen.Hat);
        bool ignored = customization.ignorePlayerCosmetics;
        customization.ignorePlayerCosmetics = false;
        try
        {
            customization.OnPlayerDataChange(data);
        }
        finally
        {
            customization.ignorePlayerCosmetics = ignored;
        }
        zombie.ClearMushroomVisuals();
        CustomizationRefs refs = customization.refs;
        if (refs.sashRenderer != null)
        {
            refs.sashRenderer.enabled = false;
        }
        if (refs.medalRenderer != null)
        {
            refs.medalRenderer.gameObject.SetActive(false);
        }
        Dressed++;
    }

    private static int Index(CustomizationOption[] options, string name)
    {
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i] != null && options[i].name == name)
            {
                return i;
            }
        }
        Plugin.Log.LogWarning($"Ascent 17: the cosmetic {name} was not found; a peak zombie wears the first one instead.");
        return 0;
    }
}
