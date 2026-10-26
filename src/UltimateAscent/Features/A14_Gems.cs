using HarmonyLib;
using Peak;
using Peak.Afflictions;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 15: the scout statues' gems (the game's "amulets") cost more petrification and do less.
/// <list type="bullet">
/// <item>Scout's Initiative (red): 15 petrification per use (the game: 7); the super jump reaches the
/// scouts within 6 m (8 m) and the low gravity after it lasts 2 s (3 s).</item>
/// <item>Scout's Ambition (yellow): 6 s of infinite stamina for the scouts around you (8 s), 35
/// petrification per use (30).</item>
/// <item>Scout's Tenacity (blue): heals up to 40 (60) and gives 8 s of invincibility (10 s); it costs
/// 1.5 petrification per point it heals, 20 to 60 (the game: half a point, 10 to 30).</item>
/// <item>Scout's Generosity (green): cloning costs 35 petrification (25), 60 for a mystical item (45).</item>
/// </list>
/// </summary>
internal static class A14_Gems
{
    private static bool On => Rules.Enabled(Lvl.WorseItems);

    /// <summary>Scout's Tenacity's invincibility (the game: 10 s).</summary>
    internal const float TenacitySeconds = 8f;

    /// <summary>A petrification amount for the game's AddStatus, which drops anything short of a whole point.</summary>
    private static float Points(int points) => points / 100f + 0.0001f;

    /// <summary>
    /// Puts this ascent's numbers on a gem before it is used, for the mods that show what an item does
    /// (see Compat.ItemFigures). Using it would set the same; they hold until the climb ends either way.
    /// </summary>
    internal static void Prepare(Item item)
    {
        if (!On || item == null)
        {
            return;
        }
        foreach (Action_SuperJumpAmulet jump in item.GetComponents<Action_SuperJumpAmulet>())
        {
            TuneInitiative(jump);
        }
        foreach (Action_ApplyAffliction apply in item.GetComponents<Action_ApplyAffliction>())
        {
            TuneAmbition(apply);
        }
        foreach (Action_ModifyStatus status in item.GetComponents<Action_ModifyStatus>())
        {
            TuneAmbitionCost(status);
        }
        foreach (Action_HealingGem gem in item.GetComponents<Action_HealingGem>())
        {
            Tune(gem);
        }
        foreach (Action_CloneSelectedItem clone in item.GetComponents<Action_CloneSelectedItem>())
        {
            TuneGenerosity(clone);
        }
    }

    [HarmonyPatch(typeof(Action_SuperJumpAmulet), nameof(Action_SuperJumpAmulet.RunAction))]
    private static class Initiative
    {
        private static void Prefix(Action_SuperJumpAmulet __instance)
        {
            TuneInitiative(__instance);
        }
    }

    private static void TuneInitiative(Action_SuperJumpAmulet amulet)
    {
        if (!On)
        {
            return;
        }
        Overrides.Set(amulet, nameof(Action_SuperJumpAmulet.petrifyPerUse), Points(15));
        if (amulet.affliction is Affliction_MassSuperJump jump)
        {
            Overrides.Set(jump, nameof(Affliction_MassSuperJump.radius), 6f);
            Overrides.Set(jump, nameof(Affliction_MassSuperJump.lowGravTime), 2f);
        }
    }

    [HarmonyPatch(typeof(Action_ApplyAffliction), nameof(Action_ApplyAffliction.RunAction))]
    private static class Ambition
    {
        private static void Prefix(Action_ApplyAffliction __instance)
        {
            TuneAmbition(__instance);
        }
    }

    private static void TuneAmbition(Action_ApplyAffliction apply)
    {
        if (On && apply.affliction is Affliction_RadiateInfiniteStam radiate)
        {
            Overrides.Set(radiate, nameof(Affliction.totalTime), 6f);
        }
    }

    /// <summary>The Ambition gem petrifies through a status action of its own.</summary>
    [HarmonyPatch(typeof(Action_ModifyStatus), nameof(Action_ModifyStatus.RunAction))]
    private static class AmbitionCost
    {
        private static void Prefix(Action_ModifyStatus __instance)
        {
            TuneAmbitionCost(__instance);
        }
    }

    private static void TuneAmbitionCost(Action_ModifyStatus status)
    {
        if (On && status.statusType == CharacterAfflictions.STATUSTYPE.Petrify && status.changeAmount > 0f && status.GetComponent<AmuletBase>() != null)
        {
            Overrides.Set(status, nameof(Action_ModifyStatus.changeAmount), Points(35));
        }
    }

    [HarmonyPatch(typeof(Action_HealingGem), nameof(Action_HealingGem.RunAction))]
    private static class Tenacity
    {
        private static void Prefix(Action_HealingGem __instance)
        {
            Tune(__instance);
        }
    }

    /// <summary>Used on a teammate, the gem charges its holder through this step alone.</summary>
    [HarmonyPatch(typeof(Action_HealingGem), nameof(Action_HealingGem.FeederAction))]
    private static class TenacityCost
    {
        private static void Prefix(Action_HealingGem __instance)
        {
            Tune(__instance);
        }
    }

    private static void Tune(Action_HealingGem gem)
    {
        if (!On)
        {
            return;
        }
        if (gem.healingAffliction != null)
        {
            Overrides.Set(gem.healingAffliction, nameof(Affliction_HealAll.maxHealing), 0.4f);
        }
        if (gem.invincibilityAffliction != null)
        {
            Overrides.Set(gem.invincibilityAffliction, nameof(Affliction.totalTime), TenacitySeconds);
        }
        // Full healing (40) costs the maximum (60).
        Overrides.Set(gem, nameof(Action_HealingGem.healingToPetrifyRatio), 1.5f);
        Overrides.Set(gem, nameof(Action_HealingGem.minPetrify), Points(20));
        Overrides.Set(gem, nameof(Action_HealingGem.maxPetrify), Points(60));
    }

    [HarmonyPatch(typeof(Action_CloneSelectedItem), nameof(Action_CloneSelectedItem.RunAction))]
    private static class Generosity
    {
        private static void Prefix(Action_CloneSelectedItem __instance)
        {
            TuneGenerosity(__instance);
        }
    }

    private static void TuneGenerosity(Action_CloneSelectedItem clone)
    {
        if (!On)
        {
            return;
        }
        Overrides.Set(clone, nameof(Action_CloneSelectedItem.petrify), 35);
        Overrides.Set(clone, nameof(Action_CloneSelectedItem.petrifyMystical), 60);
    }
}
