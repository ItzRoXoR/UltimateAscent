using System.Runtime.CompilerServices;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// From ascent 9 the Ritual Dagger works on mushroom zombies, the Roots' and the scouts who turned into
/// zombies (the game never lets you aim an item at one). As on a teammate, only a stab in the back
/// counts (any side once it is out cold, as the game allows on a teammate). A zombie dies after 5 stabs
/// (8 with ascent 14's rule) the way zombies die in the game: it drops, and 10 s later it is gone,
/// leaving a skeleton. A stab on a zombie gives nobody anything (the sacrifice's cure and extra stamina
/// are for teammates) and keeps the dagger.
/// <para>The stabbing scout's game picks the zombie; the stab reaches the zombie's own game (the
/// host's) through the game's own message, and is counted there. A stab that leaves the zombie standing
/// makes it grunt; the last one knocks it out.</para>
/// </summary>
internal static class ZombieDagger
{
    /// <summary>Stabs that kill a zombie...</summary>
    internal const int Stabs = 5;

    /// <summary>...and with ascent 14's rule.</summary>
    internal const int StabsWorse = 8;

    /// <summary>What a stab's prompt calls a zombie (the game's name for every computer-controlled character is "Bot").</summary>
    internal const string ZombieName = "Zombie";

    private const int GruntSound = 0;
    private const int KnockoutSound = 1;

    private static ConditionalWeakTable<MushroomZombie, StrongBox<int>> stabs = new ConditionalWeakTable<MushroomZombie, StrongBox<int>>();

    internal static int StabsNow => Rules.Enabled(Lvl.WorseItems) ? StabsWorse : Stabs;

    /// <summary>Zombies killed with the dagger (on this game); for the tests.</summary>
    internal static int Kills { get; private set; }

    private static bool On => Rules.InExtraAscent;

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
                Count(zombie);
            }
            return false;
        }
    }

    private static void Count(MushroomZombie zombie)
    {
        StrongBox<int> count = stabs.GetValue(zombie, _ => new StrongBox<int>());
        count.Value++;
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
    }
}
