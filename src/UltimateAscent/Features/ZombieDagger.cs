using System.Runtime.CompilerServices;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// On every ascent the Ritual Dagger works on mushroom zombies, the Roots' and the scouts who turned into
/// zombies (the game never lets you aim an item at one). As on a teammate, only a stab in the back
/// counts (any side once it is out cold, as the game allows on a teammate). A zombie dies after 5 stabs
/// (8 with ascent 15's rule) the way zombies die in the game: it drops, and 10 s later it is gone,
/// leaving a skeleton. A stab on a zombie gives nobody the sacrifice's cure and extra stamina (they are
/// for teammates) and keeps the dagger; the stab that kills it takes 15 crab off the scout who made it,
/// exactly 15 on every ascent (ascent 15's rule, which keeps cure-alls off the crab status, included).
/// <para>The stabbing scout's game picks the zombie; the stab reaches the zombie's own game (the
/// host's) through the game's own message, and is counted there. Every stab that counts is heard by every
/// player: the game's sound of a scout taking a hit, at the zombie (the Roots' mushroom men make no sound of
/// their own); a stab that leaves the zombie standing also makes it grunt, the last one knocks it out with
/// the game's knock-out sound.</para>
/// </summary>
internal static class ZombieDagger
{
    /// <summary>Stabs that kill a zombie...</summary>
    internal const int Stabs = 5;

    /// <summary>...and with ascent 15's rule.</summary>
    internal const int StabsWorse = 8;

    /// <summary>What a stab's prompt calls a zombie (the game's name for every computer-controlled character is "Bot").</summary>
    internal const string ZombieName = "Zombie";

    private const int GruntSound = 0;
    private const int KnockoutSound = 1;

    private static ConditionalWeakTable<MushroomZombie, StrongBox<int>> stabs = new ConditionalWeakTable<MushroomZombie, StrongBox<int>>();

    internal static int StabsNow => Rules.Enabled(Lvl.WorseItems) ? StabsWorse : Stabs;

    /// <summary>The crab status a kill takes off the scout who made it.</summary>
    internal const float KillCrabCure = 0.15f;

    private const string KillMessage = "dagger-kill";
    private const string HitMessage = "dagger-hit";

    /// <summary>The game's sounds of a scout taking a hit (one of them, at random, for a stab).</summary>
    private const string HitSounds = "SFXI Dmg Impact";

    private static SFX_Instance[] hitSounds;

    /// <summary>Stabs heard on this game; for the tests.</summary>
    internal static int HitsHeard { get; private set; }

    /// <summary>Zombies killed with the dagger (on this game); for the tests.</summary>
    internal static int Kills { get; private set; }

    private static bool On => Plugin.Ready && Rules.InRun;

    /// <summary>The stabs a zombie has taken so far (counted on its own game, the host's).</summary>
    internal static int StabsTaken(MushroomZombie zombie) => zombie != null && stabs.TryGetValue(zombie, out StrongBox<int> count) ? count.Value : 0;

    internal static bool IsDagger(Item item) => item != null && item.GetComponent<Peak.RitualDaggerFeedBehavior>() != null;

    private static MushroomZombie ZombieOf(Character character) => character != null && character.isBot ? character.GetComponent<MushroomZombie>() : null;

    /// <summary>The dagger in the local scout's hands is aimed at a zombie.</summary>
    internal static bool AimedAtZombie => Interaction.instance != null && Interaction.instance.bestCharacter != null && ZombieOf(Interaction.instance.bestCharacter.character) != null;

    /// <summary>A zombie can be stabbed with the dagger; the game leaves every computer-controlled character out.</summary>
    [HarmonyPatch(typeof(CharacterInteractible), nameof(CharacterInteractible.IsInteractible))]
    private static class Target
    {
        private static void Postfix(CharacterInteractible __instance, Character interactor, ref bool __result)
        {
            if (__result || !On || interactor == null || !IsDagger(interactor.data.currentItem))
            {
                return;
            }
            MushroomZombie zombie = ZombieOf(__instance.character);
            if (zombie != null && zombie.currentState != MushroomZombie.State.Dead)
            {
                // The game's own check for a teammate: in the back, or any side once out cold.
                __result = __instance.IsSecondaryInteractible(interactor);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterInteractible), nameof(CharacterInteractible.GetName))]
    private static class Name
    {
        private static void Postfix(CharacterInteractible __instance, ref string __result)
        {
            if (On && ZombieOf(__instance.character) != null)
            {
                __result = ZombieName;
            }
        }
    }

    /// <summary>On the stabbing scout's game: no cure and no extra stamina for anyone, and the dagger is not used up.</summary>
    [HarmonyPatch(typeof(Peak.RitualDaggerFeedBehavior), nameof(Peak.RitualDaggerFeedBehavior.FeederAction))]
    private static class NoReward
    {
        private static bool Prefix(Character fedCharacter)
        {
            return ZombieOf(fedCharacter) == null;
        }
    }

    /// <summary>On the zombie's game, which the stab's message goes to: the stab is counted, and the last one kills it.</summary>
    [HarmonyPatch(typeof(Peak.Action_SacrificeFriend), nameof(Peak.Action_SacrificeFriend.RunAction))]
    private static class Stab
    {
        private static bool Prefix(Peak.Action_SacrificeFriend __instance)
        {
            MushroomZombie zombie = ZombieOf(__instance.character);
            if (zombie == null || !On)
            {
                return true;
            }
            if (zombie.photonView.IsMine && zombie.currentState != MushroomZombie.State.Dead)
            {
                Count(zombie, __instance.item != null ? __instance.item.trueHolderCharacter : null);
            }
            return false;
        }
    }

    /// <summary>The stabbing scout's game takes the crab off (each player's game keeps its own scout's statuses).</summary>
    internal static void Listen()
    {
        Net.Handle(KillMessage, _ => CureCrab(Character.localCharacter));
        Net.Handle(HitMessage, args =>
        {
            if (args.Length > 0 && args[0] is Vector3 at)
            {
                int stabber = args.Length > 1 && args[1] is int id ? id : -1;
                PlayHit(at, stabber);
            }
        });
    }

    /// <summary>A stab heard where the zombie is (on every player's game).</summary>
    /// <summary>The game's sounds of a scout taking a hit.</summary>
    internal static SFX_Instance[] HitSfx()
    {
        if (hitSounds == null || hitSounds.Length == 0)
        {
            hitSounds = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(Resources.FindObjectsOfTypeAll<SFX_Instance>(),
                sound => sound != null && sound.name.StartsWith(HitSounds, System.StringComparison.Ordinal)));
        }
        return hitSounds;
    }

    /// <summary>
    /// The game plays these sounds where the scout who takes the hit is (at their own ears: full strength); a stab
    /// played at the zombie, a couple of metres away, came out about 9 dB quieter. The scout who stabs hears it at their
    /// own ears, like their own hits; everyone else hears it at the zombie.
    /// </summary>
    internal static void PlayHit(Vector3 at, int stabberViewId = -1)
    {
        SFX_Instance[] sounds = HitSfx();
        if (sounds.Length == 0)
        {
            return;
        }
        Character local = Character.localCharacter;
        bool mine = local != null && local.photonView != null && local.photonView.ViewID == stabberViewId && MainCamera.instance != null;
        sounds[Random.Range(0, sounds.Length)].Play(mine ? MainCamera.instance.transform.position : at);
        HitsHeard++;
    }

    private static void CureCrab(Character scout)
    {
        if (scout == null || scout.data.dead)
        {
            return;
        }
        CharacterAfflictions afflictions = scout.refs.afflictions;
        float crab = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Crab);
        if (crab > 0f)
        {
            afflictions.SetStatus(CharacterAfflictions.STATUSTYPE.Crab, Mathf.Max(0f, crab - KillCrabCure));
        }
        Plugin.Debug($"Ritual Dagger: a zombie killed, crab {crab * 100f:0.#} -> {afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Crab) * 100f:0.#}.");
    }

    private static void Count(MushroomZombie zombie, Character stabber)
    {
        StrongBox<int> count = stabs.GetValue(zombie, _ => new StrongBox<int>());
        count.Value++;
        Net.SendAll(HitMessage, zombie.character != null ? zombie.character.Center : zombie.transform.position, stabber != null && stabber.photonView != null ? stabber.photonView.ViewID : -1);
        Plugin.Debug($"Ritual Dagger: stab {count.Value} of {StabsNow} on {zombie.name}.");
        if (count.Value < StabsNow)
        {
            zombie.photonView.RPC("RPC_PlaySFX", RpcTarget.All, GruntSound);
            return;
        }
        zombie.photonView.RPC("RPC_PlaySFX", RpcTarget.All, KnockoutSound);
        zombie.currentState = MushroomZombie.State.Dead;
        // The game removes the dead zombies it keeps track of 10 s later, with a skeleton.
        ZombieManager manager = ZombieManager.Instance;
        if (manager != null)
        {
            manager.RegisterZombie(zombie);
        }
        Kills++;
        if (stabber != null && !stabber.isBot)
        {
            Net.SendTo(stabber.photonView.Owner, KillMessage);
        }
    }
}
