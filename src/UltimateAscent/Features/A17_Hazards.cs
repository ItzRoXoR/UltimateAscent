using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17 (13DDA): hazards are more dangerous. Every hazard is worse; some are uniquely worse.
/// Jellyfish, urchins, bees, beetles, cacti, dynamite, spiders, ticks, tumbleweeds, mushroom
/// zombies, explosions, banana peels, rickety bridges, lava, eruptions, hazardous rocks, poison
/// clouds and spore emitters. Burned items burn the hands holding them (<see cref="BurnedItems"/>),
/// and holding a snowball chills.
/// Ticks and bee ambushes (<see cref="A17_BeeAmbush"/>) only happen in the Tropics.
/// </summary>
internal static class A17_Hazards
{
    private static bool On => Rules.Enabled(Lvl.Hazards);

    /// <summary>On for this hazard: not one True Final Ascent's events spawned (its blood moon, cactus rain, mines), which keep TFA's numbers.</summary>
    private static bool OnFor(Component hazard) => On && !Compat.Tfa.Spawned(hazard);

    private const CharacterAfflictions.STATUSTYPE Poison = CharacterAfflictions.STATUSTYPE.Poison;

    // Targets: what lands on the bar from this ascent on, ascent 15's boost included (see A15_StrongerAilments.Raw).

    /// <summary>A jellyfish sting: 20 poison (the game: 5).</summary>
    private const float JellyfishPoison = 0.2f;

    /// <summary>A sea urchin: 40 poison per touch (the game: 10).</summary>
    private const float UrchinPoison = 0.4f;

    /// <summary>Thorny vines: 60 poison per touch (the game: 10).</summary>
    private const float VinePoison = 0.6f;

    /// <summary>Urchins and thorny vines push you away 1.5 times as hard.</summary>
    private const float SpikyKnockback = 1.5f;

    /// <summary>A bounce on a Roots mushroom gives back 5 stamina, on a purple (spore) one 2.5 (the game: 10 on both).</summary>
    private const float RedBounceStamina = 0.05f;
    private const float PurpleBounceStamina = 0.025f;

    /// <summary>A beetle's bonk also deals 10 injury (the game: none).</summary>
    private const float BeetleInjury = 0.1f;

    /// <summary>The Gloom's spiked fences: 15 injury per touch (the game: 2.5).</summary>
    private const float FenceInjury = 0.15f;

    /// <summary>Lava streams: 50 injury and 100 heat per touch (the game: 20 and 50).</summary>
    private const float StreamInjury = 0.5f;
    private const float StreamHeat = 1f;

    /// <summary>Falling into lava: 100 injury and 200 heat (the game: 25 and 25).</summary>
    private const float LavaInjury = 1f;
    private const float LavaHeat = 2f;

    /// <summary>Holding a snowball: 2.5 cold a second (the game: none).</summary>
    private const float HeldSnowballCold = 0.025f;

    /// <summary>Called every frame: a snowball held in the hand chills.</summary>
    internal static void Tick()
    {
        Character local = Character.localCharacter;
        if (!On || local == null || local.data.dead)
        {
            return;
        }
        Item held = local.data.currentItem;
        if (held != null && held.TryGetComponent(out Snowball _))
        {
            ExactStatus.Add(local.refs.afflictions, CharacterAfflictions.STATUSTYPE.Cold,
                A15_StrongerAilments.FromAscent15(CharacterAfflictions.STATUSTYPE.Cold, HeldSnowballCold * Time.deltaTime), "held snowball");
        }
    }

    private static void Slip(Character character, float fallMultiplier, float hipMultiplier)
    {
        Rigidbody footR = character.GetBodypartRig(BodypartType.Foot_R);
        Rigidbody footL = character.GetBodypartRig(BodypartType.Foot_L);
        Rigidbody hip = character.GetBodypartRig(BodypartType.Hip);
        Rigidbody head = character.GetBodypartRig(BodypartType.Head);
        character.RPCA_Fall(2f * fallMultiplier, 0f);
        footR.AddForce((character.data.lookDirection_Flat + Vector3.up) * 200f, ForceMode.Impulse);
        footL.AddForce((character.data.lookDirection_Flat + Vector3.up) * 200f, ForceMode.Impulse);
        hip.AddForce(Vector3.up * 1500f * hipMultiplier, ForceMode.Impulse);
        head.AddForce(character.data.lookDirection_Flat * -300f, ForceMode.Impulse);
    }

    /// <summary>Jellyfish: a much harder slip (5x longer, 3x higher) and 20 poison.</summary>
    [HarmonyPatch(typeof(SlipperyJellyfish), nameof(SlipperyJellyfish.Trigger))]
    private static class Jellyfish
    {
        private static bool Prefix(SlipperyJellyfish __instance, int targetID)
        {
            if (!On)
            {
                return true;
            }
            PhotonView view = PhotonView.Find(targetID);
            Character character = view != null ? view.GetComponent<Character>() : null;
            if (character == null)
            {
                return false;
            }
            Slip(character, 5f, 3f);
            character.refs.afflictions.AddStatus(Poison, A15_StrongerAilments.Raw(Poison, JellyfishPoison), fromRPC: true);
            foreach (SFX_Instance sfx in __instance.slipSFX)
            {
                sfx.Play(__instance.transform.position);
            }
            return false;
        }
    }

    /// <summary>
    /// Banana peels: a much harder slip (5x longer, 3x higher), and 3 seconds later the screen darkens like
    /// when the Scoutmaster comes for you and the sound is muffled, for 20 seconds (see <see cref="A17_BananaDarkness"/>).
    /// </summary>
    [HarmonyPatch(typeof(BananaPeel), nameof(BananaPeel.RPCA_TriggerBanana))]
    private static class Banana
    {
        private static bool Prefix(BananaPeel __instance, int viewID)
        {
            if (!On)
            {
                return true;
            }
            PhotonView view = PhotonView.Find(viewID);
            Character character = view != null ? view.GetComponent<Character>() : null;
            if (character == null)
            {
                return false;
            }
            __instance.GetComponent<Rigidbody>().AddForce((character.data.lookDirection_Flat * 0.5f + Vector3.up) * 40f, ForceMode.Impulse);
            Slip(character, 5f, 3f);
            foreach (SFX_Instance sfx in __instance.slipSFX)
            {
                sfx.Play(__instance.transform.position);
            }
            if (character.IsLocal)
            {
                A17_BananaDarkness.Begin();
            }
            return false;
        }
    }

    private sealed class Kind
    {
        internal bool urchin;
        internal bool fence;
    }

    private static readonly ConditionalWeakTable<CollisionModifier, Kind> kinds = new ConditionalWeakTable<CollisionModifier, Kind>();

    private static Kind KindOf(CollisionModifier modifier)
    {
        if (!kinds.TryGetValue(modifier, out Kind kind))
        {
            kind = new Kind();
            for (Transform parent = modifier.transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.name == "Urch" || parent.name.StartsWith("Urch (", StringComparison.Ordinal))
                {
                    kind.urchin = true;
                    break;
                }
                if (parent.name.StartsWith("Fence", StringComparison.Ordinal))
                {
                    kind.fence = true;
                    break;
                }
            }
            kinds.Add(modifier, kind);
        }
        return kind;
    }

    /// <summary>A lava stream: injury on touch plus heat (the lava pillars' "Lava" has no injury of its own).</summary>
    private static CollisionMod StreamHeatMod(CollisionModifier modifier)
    {
        if (modifier.statusType != CharacterAfflictions.STATUSTYPE.Injury || modifier.additionalMods == null)
        {
            return null;
        }
        foreach (CollisionMod mod in modifier.additionalMods)
        {
            if (mod != null && mod.statusType == CharacterAfflictions.STATUSTYPE.Hot)
            {
                return mod;
            }
        }
        return null;
    }

    /// <summary>
    /// Touch hazards: sea urchins land 40 poison and thorny vines 60, both pushing 1.5 times as hard;
    /// the Gloom's spiked fences 15 injury; lava streams 50 injury and 100 heat; petrifying stones 4
    /// petrification (only while you are not climbing them, see <see cref="A17_ClimbingHazards"/>).
    /// Everything else that hurts on touch does twice as much (cold and heat at least 2). The Roots'
    /// bouncy mushrooms give back 5 stamina per bounce, the purple (spore) ones 2.5.
    /// </summary>
    [HarmonyPatch(typeof(CollisionModifier), nameof(CollisionModifier.Collide))]
    private static class TouchHazards
    {
        private static bool Prefix(CollisionModifier __instance, Character character)
        {
            if (!On || __instance.name == "HangBridge")
            {
                return true;
            }
            float original = Overrides.Original(__instance, "damage", __instance.damage);
            if (__instance.useBounceCoroutine && __instance.name.StartsWith("Flat Top Mush", StringComparison.Ordinal))
            {
                bool purple = __instance.statusType == CharacterAfflictions.STATUSTYPE.Spores && original > 0f;
                Overrides.Set(__instance, nameof(CollisionModifier.staminaBackOnBounce), purple ? PurpleBounceStamina : RedBounceStamina);
            }
            if (__instance.applyPetrify)
            {
                if (character != null && character.IsLocal && character.data.isClimbing && character.data.climbMod != null && character.data.climbMod.applyPetrify)
                {
                    // Climbing a stone petrifies through its climbing effect instead.
                    return false;
                }
                Overrides.Set(__instance, nameof(CollisionModifier.damage), original * 2f);
                return true;
            }
            if (original <= 0f)
            {
                return true;
            }
            Kind kind = KindOf(__instance);
            switch (__instance.statusType)
            {
                case CharacterAfflictions.STATUSTYPE.Poison:
                    Overrides.Set(__instance, nameof(CollisionModifier.damage), A15_StrongerAilments.Raw(Poison, kind.urchin ? UrchinPoison : VinePoison));
                    Overrides.Set(__instance, nameof(CollisionModifier.knockback), Overrides.Original(__instance, "knockback", __instance.knockback) * SpikyKnockback);
                    Overrides.Set(__instance, nameof(CollisionModifier.ragdolledKnockback), Overrides.Original(__instance, "ragdolled", __instance.ragdolledKnockback) * SpikyKnockback);
                    break;
                case CharacterAfflictions.STATUSTYPE.Cold:
                case CharacterAfflictions.STATUSTYPE.Hot:
                    Overrides.Set(__instance, nameof(CollisionModifier.damage), Mathf.Max(original * 2f, 0.02f));
                    break;
                case CharacterAfflictions.STATUSTYPE.Injury when kind.fence:
                    Overrides.Set(__instance, nameof(CollisionModifier.damage), FenceInjury);
                    break;
                case CharacterAfflictions.STATUSTYPE.Injury when StreamHeatMod(__instance) is CollisionMod heat:
                    Overrides.Set(__instance, nameof(CollisionModifier.damage), StreamInjury);
                    Overrides.Set(heat, nameof(CollisionMod.amount), A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Hot, StreamHeat));
                    break;
                default:
                    Overrides.Set(__instance, nameof(CollisionModifier.damage), original * 2f);
                    break;
            }
            return true;
        }
    }

    /// <summary>Poison clouds and other status triggers hit harder and poison for much longer.</summary>
    [HarmonyPatch(typeof(StatusTrigger), nameof(StatusTrigger.OnTriggerEnter))]
    private static class StatusTriggers
    {
        private static void Prefix(StatusTrigger __instance)
        {
            if (!On)
            {
                return;
            }
            Overrides.Set(__instance, nameof(StatusTrigger.poisonOverTimeDuration), 999f);
            Overrides.Set(__instance, nameof(StatusTrigger.statusAmount), Mathf.Max(Overrides.Original(__instance, "statusAmount", __instance.statusAmount), 0.15f));
            Overrides.Set(__instance, nameof(StatusTrigger.poisonOverTimeAmountPerSecond), Mathf.Max(Overrides.Original(__instance, "pot", __instance.poisonOverTimeAmountPerSecond), 0.1f));
        }
    }

    /// <summary>Calm bees sting for 10 poison a second, angry ones for 15 (the game: 3 and 6).</summary>
    private const float CalmBeePoison = 0.1f;
    private const float AngryBeePoison = 0.15f;

    /// <summary>Bees sting within 2 m (the game: 1.3 m).</summary>
    private const float StingRadius = 2f;

    /// <summary>Angry bees stay angry for 50 s (the game: 8 s after the hive is grabbed, 12 s after it is destroyed).</summary>
    internal const float BeeAngerSeconds = 50f;

    /// <summary>A swarm chases up to 120 m from its hive or from where an ambush swarm appeared (the game: 20 m).</summary>
    internal const float BeeChaseDistance = 120f;

    private sealed class SwarmOf
    {
        internal BeeSwarm swarm;
    }

    /// <summary>The swarms' sting fields.</summary>
    private static readonly ConditionalWeakTable<global::Peak.StatusFieldBase, SwarmOf> stingers = new ConditionalWeakTable<global::Peak.StatusFieldBase, SwarmOf>();

    [HarmonyPatch(typeof(BeeSwarm), nameof(BeeSwarm.Awake))]
    private static class KnowStingers
    {
        private static void Postfix(BeeSwarm __instance)
        {
            if (__instance.stingerField != null)
            {
                stingers.GetValue(__instance.stingerField, _ => new SwarmOf()).swarm = __instance;
            }
        }
    }

    /// <summary>Bees: a wider sting, a gentle sting on entering, 50 s of anger and a chase up to 120 m from the hive.</summary>
    [HarmonyPatch(typeof(BeeSwarm), nameof(BeeSwarm.Update))]
    private static class Bees
    {
        private static void Postfix(BeeSwarm __instance)
        {
            if (!On || __instance.stingerField == null)
            {
                return;
            }
            Overrides.Set(__instance, nameof(BeeSwarm.beesAngerTimeHiveStolen), BeeAngerSeconds);
            Overrides.Set(__instance, nameof(BeeSwarm.beesAngerTimeHiveDestroyed), BeeAngerSeconds);
            Overrides.Set(__instance, nameof(BeeSwarm.maxHiveDistance), BeeChaseDistance);
            // Entering a swarm stings for the game's 5 poison, which ascent 15 already makes 7.5.
            Overrides.Set(__instance.stingerField, nameof(StatusField.radius), StingRadius);
        }
    }

    /// <summary>A swarm's stings land exactly 10 (calm) or 15 (angry) poison a second.</summary>
    [HarmonyPatch(typeof(StatusField), nameof(StatusField.TickStatus))]
    private static class Stings
    {
        private static bool Prefix(StatusField __instance)
        {
            if (!On || Character.localCharacter == null || !stingers.TryGetValue(__instance, out SwarmOf owner) || owner.swarm == null)
            {
                return true;
            }
            float perSecond = owner.swarm.beesAngry ? AngryBeePoison : CalmBeePoison;
            ExactStatus.Add(Character.localCharacter.refs.afflictions, Poison, perSecond * Time.deltaTime, "bees");
            return false;
        }
    }

    /// <summary>A frog's tongue pull poisons the pulled scout for 10 (the game: nothing).</summary>
    private const float FrogPoison = 0.1f;

    [HarmonyPatch(typeof(FrogTongue), nameof(FrogTongue.LetGo))]
    private static class PoisonousFrogs
    {
        private static void Prefix(FrogTongue __instance)
        {
            Character target = __instance._targetCharacter;
            if (OnFor(__instance) && target != null && target.IsLocal)
            {
                target.refs.afflictions.AddStatus(Poison, A15_StrongerAilments.Raw(Poison, FrogPoison));
            }
        }
    }

    /// <summary>Beetles hit 10x harder, from 2 m further, deal 10 injury and knock you down for 10 seconds.</summary>
    [HarmonyPatch(typeof(Beetle), nameof(Beetle.InflictAttack))]
    private static class Beetles
    {
        internal struct Stats
        {
            internal bool changed;
            internal float force;
            internal float range;
            internal float ragdoll;
        }

        private static void Prefix(Beetle __instance, out Stats __state)
        {
            __state = default;
            if (!OnFor(__instance))
            {
                return;
            }
            __state = new Stats { changed = true, force = __instance.bonkForce, range = __instance.bonkRange, ragdoll = __instance.ragdollTime };
            __instance.bonkForce *= 10f;
            __instance.bonkRange += 2f;
            __instance.ragdollTime = 10f;
        }

        private static void Postfix(Beetle __instance, Character character)
        {
            if (OnFor(__instance) && character != null && character.IsLocal)
            {
                character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Injury, BeetleInjury));
            }
        }

        private static Exception Finalizer(Beetle __instance, Stats __state, Exception __exception)
        {
            if (__state.changed)
            {
                __instance.bonkForce = __state.force;
                __instance.bonkRange = __state.range;
                __instance.ragdollTime = __state.ragdoll;
            }
            return __exception;
        }
    }

    /// <summary>Cacti stick to every body part (head and torso too) and leave two thorns.</summary>
    [HarmonyPatch(typeof(StickyCactus), nameof(StickyCactus.OnCollide))]
    private static class Cacti
    {
        private static bool Prefix(StickyCactus __instance, Character character, Collision collision, Bodypart bodypart)
        {
            if (!OnFor(__instance))
            {
                return true;
            }
            if (character.IsLocal && RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_Thorns) != 0 && !character.warping && !character.data.isInvincible && !character.data.isSkeleton &&
                character.TryStickBodypart(bodypart, collision.contacts[0].point, CharacterAfflictions.STATUSTYPE.Thorns, 0f) && __instance.applyThorn)
            {
                character.refs.afflictions.AddThorn(collision.contacts[0].point, 0);
                character.refs.afflictions.AddThorn(collision.contacts[0].point, 0);
            }
            return false;
        }
    }

    /// <summary>Dynamite lights when you come within 6 m (the game: 5 m).</summary>
    private const float DynamiteLightRadius = 6f;

    /// <summary>Its fuse burns for 3 s (the game: 5 s).</summary>
    private const float DynamiteFuseSeconds = 3f;

    /// <summary>Exploding in your hand it deals 100 injury (the game: 25), on top of the blast.</summary>
    private const float DynamiteInHandInjury = 1f;

    /// <summary>The game's own injury for dynamite exploding in your hand.</summary>
    private const float GameInHandInjury = 0.25f;

    private static void DynamiteSettings(Dynamite dynamite)
    {
        // The game squares the light radius when the stick wakes up.
        Overrides.Set(dynamite, nameof(Dynamite.lightFuseRadius), DynamiteLightRadius * DynamiteLightRadius);
        Overrides.Set(dynamite, nameof(Dynamite.startingFuseTime), DynamiteFuseSeconds);
    }

    /// <summary>Dynamite: a shorter fuse, lit from further away, and 100 injury if it blows up in your hand.</summary>
    [HarmonyPatch(typeof(Dynamite), nameof(Dynamite.Update))]
    private static class Dynamites
    {
        private static void Prefix(Dynamite __instance)
        {
            if (!OnFor(__instance))
            {
                return;
            }
            DynamiteSettings(__instance);
            if (!__instance.photonView.IsMine || !__instance.GetData<BoolItemData>(DataEntryKey.FlareActive).Value)
            {
                return;
            }
            float fuse = __instance.GetData(DataEntryKey.Fuel, __instance.SetupDefaultFuel).Value;
            Character local = Character.localCharacter;
            if (fuse - Time.deltaTime <= 0f && local != null && local.data.currentItem == __instance.item)
            {
                local.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, DynamiteInHandInjury - GameInHandInjury);
            }
        }
    }

    /// <summary>The fuse a fresh stick starts with.</summary>
    [HarmonyPatch(typeof(Dynamite), nameof(Dynamite.SetupDefaultFuel))]
    private static class DynamiteFuse
    {
        private static void Prefix(Dynamite __instance)
        {
            if (OnFor(__instance))
            {
                DynamiteSettings(__instance);
            }
        }
    }

    /// <summary>Spiders (Roots): 12 poison per bite (the game: 5), a web of 40 (the game: 25) and a 50% longer knock-down.</summary>
    private const float SpiderPoison = 0.12f;
    private const float SpiderWeb = 0.4f;

    [HarmonyPatch(typeof(Spider), nameof(Spider.FixedUpdate))]
    private static class Spiders
    {
        private static void Prefix(Spider __instance)
        {
            if (!On)
            {
                return;
            }
            Overrides.Set(__instance, nameof(Spider.webDamage), SpiderWeb);
            Overrides.Set(__instance, nameof(Spider.fallTime), Overrides.Original(__instance, "fall", __instance.fallTime) * 1.5f);
        }
    }

    /// <summary>
    /// A spider bite: 12 poison is not a whole number of the bar's 2.5-point steps, so the bites land
    /// 10, 12.5, 12.5, 12.5, 12.5... (12 on average) instead of the game dropping the rest every time.
    /// </summary>
    [HarmonyPatch(typeof(Spider), nameof(Spider.UpdateAttack))]
    private static class SpiderBites
    {
        private static void Prefix(Spider __instance, out float? __state)
        {
            __state = null;
            if (On)
            {
                __state = __instance.poisonDamage;
                __instance.poisonDamage = 0f;
            }
        }

        private static void Postfix(Spider __instance, bool isLocal, float? __state)
        {
            // The bite resets the poison timer.
            if (__state.HasValue && isLocal && __instance.sincePoison == 0f && __instance.targetPlayer != null)
            {
                ExactStatus.Add(__instance.targetPlayer.refs.afflictions, Poison, SpiderPoison, "spider");
            }
        }

        private static Exception Finalizer(Spider __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.poisonDamage = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>A rescue grapple that catches a webbed scout pulls them out of the web.</summary>
    [HarmonyPatch(typeof(RescueHook), nameof(RescueHook.RPCA_RescueCharacter))]
    private static class GrappleOutOfWeb
    {
        private static void Postfix(RescueHook __instance)
        {
            Character target = __instance.targetPlayer;
            if (On && target != null && target.IsLocal && target.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Web) > 0f)
            {
                target.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Web, 1f);
            }
        }
    }

    /// <summary>Venus fly traps (the Gloom's poisonous plants): 10 poison a second while held (the game: 5), a 50% stronger bite and knock-down.</summary>
    private const float FlyTrapPoison = 0.1f;

    [HarmonyPatch(typeof(VenusFlyTrap), nameof(VenusFlyTrap.FixedUpdate))]
    private static class FlyTraps
    {
        private static void Prefix(VenusFlyTrap __instance)
        {
            if (!OnFor(__instance))
            {
                return;
            }
            Overrides.Set(__instance, nameof(VenusFlyTrap.poisonDamage), A15_StrongerAilments.Raw(Poison, FlyTrapPoison * __instance.poisonFrequency));
            Overrides.Set(__instance, nameof(VenusFlyTrap.damage), Overrides.Original(__instance, "bite", __instance.damage) * 1.5f);
            Overrides.Set(__instance, nameof(VenusFlyTrap.fallTime), Overrides.Original(__instance, "fall", __instance.fallTime) * 1.5f);
        }
    }

    /// <summary>Fly traps can catch you by any body part, head and torso included.</summary>
    [HarmonyPatch(typeof(VenusFlyTrap), nameof(VenusFlyTrap.OnCollide))]
    private static class FlyTrapsBiteAnywhere
    {
        private static bool Prefix(VenusFlyTrap __instance, Character character)
        {
            if (!OnFor(__instance))
            {
                return true;
            }
            if (character.IsLocal && !character.warping && character.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.FlyTrap) <= 0.001f)
            {
                __instance.view.RPC("RPC_BitePlayer", RpcTarget.All, character.photonView);
            }
            return false;
        }
    }

    /// <summary>Scorpions sting within 1.7 m (the game: 1.5 m)...</summary>
    private const float ScorpionReach = 1.7f;

    /// <summary>...once a second: the 1 s wind-up with no rest after it (the game rests 2 s).</summary>
    private const float ScorpionRest = 0f;

    [HarmonyPatch(typeof(Mob), nameof(Mob.Attacking))]
    private static class ScorpionPace
    {
        private static void Prefix(Mob __instance)
        {
            if (!(__instance is Scorpion) || !OnFor(__instance))
            {
                return;
            }
            Overrides.Set(__instance, nameof(Mob.attackDistance), ScorpionReach);
            Overrides.Set(__instance, nameof(Mob.attackStartDistance), Mathf.Max(Overrides.Original(__instance, "start", __instance.attackStartDistance), ScorpionReach + 0.4f));
            Overrides.Set(__instance, nameof(Mob.attackCooldown), ScorpionRest);
        }
    }

    /// <summary>Scorpion stings (Mesa) pump their poison in twice as fast.</summary>
    [HarmonyPatch(typeof(Scorpion), nameof(Scorpion.InflictAttack))]
    private static class Scorpions
    {
        private static void Prefix(Scorpion __instance, out float? __state)
        {
            __state = null;
            if (OnFor(__instance))
            {
                __state = __instance.totalPoisonTime;
                __instance.totalPoisonTime *= 0.5f;
            }
        }

        private static Exception Finalizer(Scorpion __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.totalPoisonTime = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>Cactus balls (Mesa) stick twice as many thorns into you: 40 on the bar instead of 20...</summary>
    [HarmonyPatch(typeof(CactusBall), nameof(CactusBall.StickToCharacterLocal))]
    private static class CactusBalls
    {
        private static void Prefix(CactusBall __instance)
        {
            DoubleThorns(__instance);
        }
    }

    /// <summary>...and so does one held in the hand (picked up): the game counts the held one's own thorns as well, which stayed 20.</summary>
    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.UpdateWeight))]
    private static class HeldCactusBalls
    {
        private static void Prefix(CharacterAfflictions __instance)
        {
            if (__instance.character != null && __instance.character.data.currentStickyItem is CactusBall ball)
            {
                DoubleThorns(ball);
            }
        }
    }

    private static void DoubleThorns(CactusBall ball)
    {
        if (OnFor(ball))
        {
            Overrides.Set(ball, nameof(StickyItemComponent.addThornsToStuckPlayer), (int)Overrides.Original(ball, "thorns", ball.addThornsToStuckPlayer) * 2);
        }
    }

    /// <summary>Arrow traps (Citadel) never run out and fire every 0.5 s (the game: 3 arrows, 8 s apart).</summary>
    private const float ArrowReloadSeconds = 0.5f;

    /// <summary>An arrow: 15 on the bar (the game: 7.5), in steps of 2.5.</summary>
    private const int ArrowSteps = 6;

    /// <summary>Pulling an arrow out: 25 injury (the game: 12.5).</summary>
    private const float ArrowPullInjury = 0.25f;

    /// <summary>Arrow traps (Citadel): endless arrows, fired every 0.5 s, knocking you back twice as hard.</summary>
    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.FixedUpdate))]
    private static class ArrowTraps
    {
        private static void Prefix(ArrowShooter __instance, out float? __state)
        {
            __state = null;
            if (!On)
            {
                return;
            }
            Overrides.Set(__instance, nameof(ArrowShooter.reloadTime), ArrowReloadSeconds);
            if (__instance.ammo <= 0)
            {
                __instance.ammo = 3;
            }
            __state = __instance.knockback;
            __instance.knockback *= 2f;
        }

        private static Exception Finalizer(ArrowShooter __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.knockback = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>A trap never empties: every client refills it after each shot.</summary>
    [HarmonyPatch(typeof(ArrowShooter), nameof(ArrowShooter.FireArrowVisuals_RPC))]
    private static class EndlessArrows
    {
        private static void Postfix(ArrowShooter __instance)
        {
            if (On && __instance.ammo <= 0)
            {
                __instance.ammo = 3;
            }
        }
    }

    /// <summary>An arrow stuck in you counts 15 and costs 25 injury to pull out.</summary>
    [HarmonyPatch(typeof(ThornOnMe), nameof(ThornOnMe.GetThornDamage))]
    private static class ArrowDamage
    {
        private static bool Prefix(ThornOnMe __instance, ref int __result)
        {
            if (!On || !__instance.isArrow)
            {
                return true;
            }
            Overrides.Set(__instance, nameof(ThornOnMe.statusToAddOnRemoveAmt), ArrowPullInjury);
            __result = ArrowSteps;
            return false;
        }
    }

    /// <summary>Pulling an arrow out costs 25 injury even if it never counted towards the bar yet.</summary>
    [HarmonyPatch(typeof(ThornOnMe), nameof(ThornOnMe.OnPulledOut))]
    private static class ArrowPull
    {
        private static void Prefix(ThornOnMe __instance)
        {
            if (On && __instance.isArrow)
            {
                Overrides.Set(__instance, nameof(ThornOnMe.statusToAddOnRemoveAmt), ArrowPullInjury);
            }
        }
    }

    /// <summary>Cactus thorns pop out by themselves after 120 s when you play alone (the game: 30 s); with others they stay until pulled out (the game: 120 s).</summary>
    private const float SoloThornSeconds = 120f;

    [HarmonyPatch(typeof(ThornOnMe), nameof(ThornOnMe.GetPopOutTime))]
    private static class StubbornThorns
    {
        private static bool Prefix(ThornOnMe __instance, bool solo, ref float __result)
        {
            if (!On || !__instance.isThorn)
            {
                return true;
            }
            __result = solo ? SoloThornSeconds : float.PositiveInfinity;
            return false;
        }
    }

    /// <summary>Share of the tick spots that hold a tick (the game: 1%).</summary>
    private const float TickChance = 0.35f;
    private const int MaxTicksPerScout = 5;

    /// <summary>Tick spots removed outside the Tropics in this climb.</summary>
    internal static int TickSpotsRemoved { get; private set; }

    private static readonly HashSet<Biome.BiomeType> tickBiomesLogged = new HashSet<Biome.BiomeType>();

    internal static void Reset()
    {
        TickSpotsRemoved = 0;
        tickBiomesLogged.Clear();
        latchingTicks.Clear();
        LastTickBite = -1000f;
        kilnEruptionAt = -1f;
        kilnEruptionMinute = -1;
        kilnEruptedMinute = -1;
    }

    /// <summary>
    /// Ticks only live in the Tropics: tick spots in any other biome are removed. In the Tropics,
    /// 35% of the tick spots hold a tick when the area loads (the game: 1%).
    /// </summary>
    [HarmonyPatch(typeof(TickTrigger), nameof(TickTrigger.Start))]
    private static class MoreTicks
    {
        private static bool Prefix(TickTrigger __instance, out float? __state)
        {
            __state = null;
            if (!On)
            {
                return true;
            }
            if (Map.TryGetBiome(__instance, out Biome.BiomeType biome) && biome != Biome.BiomeType.Tropics)
            {
                TickSpotsRemoved++;
                if (tickBiomesLogged.Add(biome))
                {
                    Plugin.Log.LogInfo($"Ascent 17: ticks only live in the Tropics; tick spots in the {biome} are removed.");
                }
                UnityEngine.Object.Destroy(__instance.gameObject);
                return false;
            }
            __state = __instance.tickChance;
            __instance.tickChance = Mathf.Max(__instance.tickChance, TickChance);
            return true;
        }

        private static Exception Finalizer(TickTrigger __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue && __instance != null)
            {
                __instance.tickChance = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>Ticks spawned by this client that have not latched on yet (they attach one frame later).</summary>
    private static readonly List<Bugfix> latchingTicks = new List<Bugfix>();

    internal const string TickMarker = "UltimateAscent.tick";

    /// <summary>A freshly latched tick starts at this size (the game grows it from nothing over five minutes).</summary>
    private const float FreshTickSize = 0.6f;

    /// <summary>...and swells to this size (three times the game's full-grown tick) within a minute.</summary>
    private const float GrownTickSize = 3f;
    private const float TickGrowSeconds = 60f;

    /// <summary>Ticks on one scout keep at least this far apart.</summary>
    private const float TickSpacing = 0.15f;

    /// <summary>
    /// Where a tick latches on: like a cactus, on the body part that brushed the tick spot, at its
    /// surface facing the spot. A tick never sits on top of another one: if that place is taken it
    /// goes round the same body part, then to the nearest other body parts. Sent along with the tick,
    /// so every player sees it in the same place.
    /// </summary>
    private static object[] TickAnchor(TickTrigger spot, Collider touched, Character character)
    {
        if (touched == null)
        {
            return null;
        }
        List<Vector3> taken = TickPositions(character);
        List<Collider> parts = new List<Collider> { touched };
        Vector3 touchedCenter = touched.bounds.center;
        foreach (KeyValuePair<Collider, Character> pair in CharacterRagdoll.COLLIDERS_TO_CHARACTERS)
        {
            if (pair.Value == character && pair.Key != null && pair.Key != touched)
            {
                parts.Add(pair.Key);
            }
        }
        parts.Sort(1, parts.Count - 1, Comparer<Collider>.Create((a, b) =>
            Vector3.Distance(a.bounds.center, touchedCenter).CompareTo(Vector3.Distance(b.bounds.center, touchedCenter))));
        foreach (Collider part in parts)
        {
            Bodypart bodypart = part.GetComponentInParent<Bodypart>();
            if (bodypart == null || bodypart.Rig == null)
            {
                continue;
            }
            Transform bone = bodypart.Rig.transform;
            Vector3 center = part.bounds.center;
            Vector3 toward = spot.transform.position - center;
            if (toward.sqrMagnitude < 0.0001f)
            {
                toward = UnityEngine.Random.onUnitSphere;
            }
            // Facing the spot first, then round the body part.
            for (int turn = 0; turn < 12; turn++)
            {
                Quaternion around = Quaternion.AngleAxis(turn % 6 * 60f, bone.up) * Quaternion.AngleAxis(turn < 6 ? 0f : 35f, bone.right);
                Vector3 surface = part.ClosestPoint(center + around * toward.normalized * 2f);
                if (taken.TrueForAll(other => Vector3.Distance(other, surface) >= TickSpacing))
                {
                    Vector3 local = bone.InverseTransformPoint(surface);
                    return new object[] { TickMarker, (int)bodypart.partType, local.x, local.y, local.z };
                }
            }
        }
        return null;
    }

    /// <summary>Where the ticks on a scout sit, the ones still latching on included.</summary>
    private static List<Vector3> TickPositions(Character character)
    {
        List<Vector3> positions = new List<Vector3>();
        foreach (KeyValuePair<Bugfix, Character> bug in Bugfix.AllAttachedBugs)
        {
            if (bug.Value == character && bug.Key != null)
            {
                positions.Add(bug.Key.transform.position);
            }
        }
        foreach (Bugfix bug in latchingTicks)
        {
            if (bug != null && bug.targetCharacterID == character.photonView.ViewID && TryGetAnchor(bug, character, out Rigidbody rig, out Vector3 local))
            {
                positions.Add(rig.transform.TransformPoint(local));
            }
        }
        return positions;
    }

    private static bool TryGetAnchor(Bugfix bug, Character character, out Rigidbody rig, out Vector3 local)
    {
        rig = null;
        local = Vector3.zero;
        PhotonView view = bug.GetComponent<PhotonView>();
        object[] data = view != null ? view.InstantiationData : null;
        if (data == null || data.Length < 5 || data[0] as string != TickMarker || !(data[1] is int part) || !(data[2] is float x) || !(data[3] is float y) || !(data[4] is float z))
        {
            return false;
        }
        rig = character.GetBodypartRig((BodypartType)part);
        local = new Vector3(x, y, z);
        return rig != null;
    }

    /// <summary>A scout can carry up to five ticks at once (the game allows one).</summary>
    [HarmonyPatch(typeof(TickTrigger), nameof(TickTrigger.OnTriggerEnter))]
    private static class SeveralTicks
    {
        private static bool Prefix(TickTrigger __instance, Collider other)
        {
            if (!On)
            {
                return true;
            }
            if (__instance.attachedBug || !CharacterRagdoll.TryGetCharacterFromCollider(other, out Character character) || !character.IsLocal)
            {
                return false;
            }
            int attached = 0;
            foreach (KeyValuePair<Bugfix, Character> bug in Bugfix.AllAttachedBugs)
            {
                if (bug.Value == character)
                {
                    attached++;
                }
            }
            // Two tick spots touched in the same frame must not slip past the limit.
            latchingTicks.RemoveAll(bug => bug == null || Bugfix.AllAttachedBugs.ContainsKey(bug));
            int viewId = character.photonView.ViewID;
            foreach (Bugfix bug in latchingTicks)
            {
                if (bug.targetCharacterID == viewId)
                {
                    attached++;
                }
            }
            if (attached >= MaxTicksPerScout)
            {
                return false;
            }
            Bugfix tick = PhotonNetwork.Instantiate("BugfixOnYou", Vector3.zero, Quaternion.identity, 0, TickAnchor(__instance, other, character)).GetComponent<Bugfix>();
            tick.AttachBug(viewId);
            latchingTicks.Add(tick);
            __instance.attachedBug = true;
            UnityEngine.Object.Destroy(__instance.gameObject);
            return false;
        }
    }

    /// <summary>The game always puts a tick on the right knee; ours sits where it touched (see <see cref="TickAnchor"/>).</summary>
    [HarmonyPatch(typeof(Bugfix), nameof(Bugfix.TryFindCharacterAndAttachBug))]
    private static class TickWhereItTouched
    {
        private static void Postfix(Bugfix __instance)
        {
            if (__instance.targetCharacter == null || !TryGetAnchor(__instance, __instance.targetCharacter, out Rigidbody rig, out Vector3 local))
            {
                return;
            }
            // Same pose relative to the body part as the game's knee tick.
            Transform bone = rig.transform;
            __instance.leg = bone;
            __instance.localPos = local;
            __instance.transform.SetPositionAndRotation(bone.TransformPoint(__instance.localPos), bone.rotation * Quaternion.Euler(0f, 55f, 0f));
            __instance.forward = bone.InverseTransformDirection(__instance.transform.forward);
            __instance.up = bone.InverseTransformDirection(__instance.transform.up);
        }
    }

    /// <summary>A tick bites every half second (the game: every 29 s)...</summary>
    private const float TickBiteSeconds = 0.5f;

    /// <summary>...for 2.5 poison (the game: 5), and keeps biting until it is pulled off (the game stops after 10 bites once your poison is 50).</summary>
    private const float TickBitePoison = 0.025f;

    /// <summary>After each bite your poison does not heal for 10 s (the game's own "no healing" effect never did anything).</summary>
    internal const float TickNoHealingSeconds = 10f;

    /// <summary>When a tick last bit the local scout.</summary>
    internal static float LastTickBite { get; private set; } = -1000f;

    internal static bool PoisonHealingBlocked => On && Time.time - LastTickBite < TickNoHealingSeconds;

    /// <summary>
    /// Ticks bite every half second for 2.5 poison until they are pulled off, and block poison
    /// recovery for 10 s after each bite. A tick that latched on is visible at once and swells to
    /// three times the game's full-grown size within a minute (the game grows it from nothing over
    /// five minutes).
    /// </summary>
    [HarmonyPatch(typeof(Bugfix), nameof(Bugfix.LateUpdate))]
    private static class Ticks
    {
        private static void Prefix(Bugfix __instance)
        {
            Character target = __instance.targetCharacter;
            if (!On || target == null || !target.IsLocal || target.data.dead || __instance.counter < TickBiteSeconds)
            {
                return;
            }
            // Our bite instead of the game's: its counter never gets near 29 s again.
            target.refs.afflictions.AddStatus(Poison, A15_StrongerAilments.Raw(Poison, TickBitePoison));
            LastTickBite = Time.time;
            __instance.counter = 0f;
        }

        private static void Postfix(Bugfix __instance)
        {
            if (On && __instance != null && __instance.targetCharacter != null && !__instance.targetCharacter.data.dead)
            {
                __instance.transform.localScale = Vector3.one * Mathf.Lerp(FreshTickSize, GrownTickSize, __instance.lifeTime / TickGrowSeconds);
            }
        }
    }

    /// <summary>Tumbleweeds roll 15% faster than in the game.</summary>
    private const float TumbleweedRollForce = 1.15f;

    [HarmonyPatch(typeof(TumbleWeed), nameof(TumbleWeed.Start))]
    private static class TumbleweedSpawn
    {
        private static void Postfix(TumbleWeed __instance)
        {
            // 15% more rolling force than the game's (13DDA slowed them down by up to half instead).
            if (On)
            {
                __instance.rollForce *= TumbleweedRollForce;
                __instance.powerMultiplier *= 2f;
            }
        }
    }

    /// <summary>Tumbleweeds hit twice as hard and always leave an extra thorn.</summary>
    [HarmonyPatch(typeof(TumbleWeed), nameof(TumbleWeed.OnCollisionEnter))]
    private static class TumbleweedHit
    {
        private static bool Prefix(TumbleWeed __instance, Collision collision)
        {
            if (!On)
            {
                return true;
            }
            Character character = collision.gameObject.GetComponentInParent<Character>();
            if (!character || !character.IsLocal || __instance.ignored.Contains(character))
            {
                return false;
            }
            __instance.StartCoroutine(__instance.IgnoreTarget(character));
            float scale = __instance.originalScale == 0f ? 1f : Mathf.Clamp01(__instance.transform.localScale.x / __instance.originalScale);
            float power = __instance.testFullPower ? 1f : Mathf.Clamp01(__instance.rig.linearVelocity.magnitude * scale * __instance.powerMultiplier);
            if (power < 0.2f)
            {
                return false;
            }
            character.Fall(2f * power);
            character.AddForceAtPosition(__instance.rig.linearVelocity.normalized * __instance.collisionForce * power, collision.contacts[0].point, 2f);
            character.refs.afflictions.AddThorn(collision.contacts[0].point, 0);
            character.refs.afflictions.AddThorn(collision.contacts[0].point, 0);
            if (power > 0.6f)
            {
                character.refs.afflictions.AddThorn(collision.contacts[0].point, 0);
            }
            return false;
        }
    }

    /// <summary>Mushroom zombies wake from 30 m (the game: 20 m)...</summary>
    private const float ZombieWakeDistance = 30f;

    /// <summary>...a bite knocks you down for 4 s (the game: 3 s)...</summary>
    internal const float ZombieStunSeconds = 4f;

    /// <summary>...and a zombie lives for 300 s once awake (the game: 120 s).</summary>
    private const float ZombieLifetime = 300f;

    /// <summary>Up to 12 zombies are awake at once (the game: 1).</summary>
    private const int MaxZombies = 12;

    /// <summary>Mushroom zombies wake from 30 m, chase at once and last 300 s; they run (<see cref="ZombiesRun"/>), and their bite is <see cref="ZombieBite"/>.</summary>
    [HarmonyPatch(typeof(MushroomZombieSpawner), nameof(MushroomZombieSpawner.Spawn))]
    private static class Zombies
    {
        private static void Postfix(MushroomZombieSpawner __instance)
        {
            MushroomZombie zombie = __instance.spawnedZombie;
            if (!On || zombie == null)
            {
                return;
            }
            Overrides.Set(zombie, nameof(MushroomZombie.distanceBeforeWakeup), ZombieWakeDistance);
            Overrides.Set(zombie, nameof(MushroomZombie.distanceToEnable), Mathf.Max(Overrides.Original(zombie, "enable", zombie.distanceToEnable), ZombieWakeDistance));
            Overrides.Set(zombie, nameof(MushroomZombie.distanceBeforeChase), 0f);
            Overrides.Set(zombie, nameof(MushroomZombie.lifetime), ZombieLifetime);
        }
    }

    /// <summary>
    /// A mushroom zombie's bite knocks you down for 4 s. The game reads the knock-down on the bitten
    /// scout's own game, from that game's copy of the zombie (the zombie is set up on the host's only),
    /// so it is set there for the bite. Zombies that rose from scouts and TFA's keep the game's 3 s.
    /// </summary>
    [HarmonyPatch(typeof(MushroomZombieBiteCollider), "OnTriggerEnter")]
    private static class ZombieBite
    {
        private static void Prefix(MushroomZombieBiteCollider __instance, out float? __state)
        {
            __state = null;
            MushroomZombie zombie = __instance.parentZombie;
            if (On && zombie != null && zombie.isNPCZombie && !Compat.Tfa.Tagged(zombie.photonView))
            {
                __state = zombie.biteStunTime;
                zombie.biteStunTime = ZombieStunSeconds;
            }
        }

        private static Exception Finalizer(MushroomZombieBiteCollider __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue && __instance.parentZombie != null)
            {
                __instance.parentZombie.biteStunTime = __state.Value;
            }
            return __exception;
        }
    }

    /// <summary>
    /// A mushroom zombie runs wherever it goes, as it does when it lunges (the game: it walks until it has chased
    /// for 3 s, and runs only within 20 m of a scout it can see). Its own jump over a drop still stops the run for
    /// that jump. Zombies that rose from scouts and TFA's keep the game's pace.
    /// </summary>
    [HarmonyPatch(typeof(MushroomZombie), "WalkTowards")]
    private static class ZombiesRun
    {
        private static void Prefix(MushroomZombie __instance, ref bool forceSprint)
        {
            if (On && __instance.isNPCZombie && !Compat.Tfa.Tagged(__instance.photonView))
            {
                forceSprint = true;
            }
        }
    }

    [HarmonyPatch(typeof(ZombieManager), nameof(ZombieManager.Update))]
    private static class MoreZombies
    {
        private static void Prefix(ZombieManager __instance)
        {
            if (On)
            {
                Overrides.Set(__instance, nameof(ZombieManager.maxActiveZombies), Math.Max(MaxZombies, (int)Overrides.Original(__instance, "max", __instance.maxActiveZombies)));
            }
        }
    }

    /// <summary>A single scout is enough to break a rickety bridge.</summary>
    [HarmonyPatch(typeof(BreakableBridge), nameof(BreakableBridge.FixedUpdate))]
    private static class RicketyBridge
    {
        private static void Postfix(BreakableBridge __instance)
        {
            if (On && __instance.peopleOnBridge > 0)
            {
                __instance.peopleOnBridge *= 200;
            }
        }
    }

    /// <summary>Falling into lava (the Caldera's river, the Kiln's rising lava, lava pools): 100 injury and 200 heat.</summary>
    [HarmonyPatch(typeof(Lava), nameof(Lava.HitPlayer))]
    private static class LavaContact
    {
        private static bool Prefix(Character item)
        {
            if (!On || !Rules.Owns(item))
            {
                return true;
            }
            item.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, LavaInjury);
            item.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, A15_StrongerAilments.Raw(CharacterAfflictions.STATUSTYPE.Hot, LavaHeat));
            item.data.sinceGrounded = 0f;
            return false;
        }
    }

    /// <summary>
    /// The Kiln erupts too: the Caldera's eruption (a column of fire), near one of the scouts in it, once a minute, taking
    /// turns with the Kiln's ash (ascent 18) and never with it, at most 10 s apart: it breaks out 2-9.5 s after the ash has
    /// cleared and burns for 30 s (the Caldera's: 35 s), so it is out by 39.5 s, before the next ash comes at 40 s.
    /// Without ascent 18's ash, the same minutes.
    /// </summary>
    private const float KilnEruptionFrom = 2f;
    private const float KilnEruptionTo = 9.5f;
    internal const float KilnEruptionSeconds = 30f;

    /// <summary>A Kiln eruption is out by this far into the minute (the ash's warning starts at 40 s).</summary>
    internal const float KilnEruptionOutBy = 39.5f;

    /// <summary>How far from the scout a Kiln eruption breaks out (its heat reaches 15 m).</summary>
    private const float KilnEruptionNear = 18f;
    private const float KilnEruptionFar = 32f;

    private static float kilnEruptionAt = -1f;
    private static int kilnEruptionMinute = -1;
    private static int kilnEruptedMinute = -1;

    /// <summary>
    /// The Caldera erupts more often, and from the Kiln's start on the Kiln erupts as well (the host chooses
    /// where and sends the game's own eruption message, as for the Caldera's).
    /// </summary>
    [HarmonyPatch(typeof(EruptionSpawner), nameof(EruptionSpawner.Update))]
    private static class Eruptions
    {
        private static bool Prefix(EruptionSpawner __instance)
        {
            if (!On)
            {
                return true;
            }
            int minute = KilnAsh.ClockMinute;
            if (PhotonNetwork.IsMasterClient && Map.InVolcanoKiln && minute >= 0 && minute != kilnEruptedMinute)
            {
                if (minute != kilnEruptionMinute)
                {
                    kilnEruptionMinute = minute;
                    kilnEruptionAt = UnityEngine.Random.Range(KilnEruptionFrom, KilnEruptionTo);
                }
                float phase = KilnAsh.ClockPhase;
                if (phase >= kilnEruptionAt && phase <= KilnEruptionTo + 0.5f)
                {
                    kilnEruptedMinute = minute;
                    if (KilnEruptionSpot(out Vector3 spot))
                    {
                        __instance.photonView.RPC("RPCA_SpawnEruption", RpcTarget.All, spot);
                    }
                }
            }
            if (PhotonNetwork.IsMasterClient && HelperFunctions.AnyPlayerInZRange(__instance.min.position.z, __instance.max.position.z))
            {
                __instance.counter -= Time.deltaTime;
                if (__instance.counter < 0f)
                {
                    __instance.counter = UnityEngine.Random.Range(-5f, 12f);
                    Vector3 position = __instance.transform.position;
                    position.x += UnityEngine.Random.Range(-155f, 155f);
                    position.z += UnityEngine.Random.Range(-140f, 140f);
                    __instance.photonView.RPC("RPCA_SpawnEruption", RpcTarget.All, position);
                }
            }
            return false;
        }
    }

    /// <summary>A Kiln eruption burns 30 s, out before the ash comes (on every scout's game: each makes its own copy).</summary>
    [HarmonyPatch(typeof(EruptionSpawner), nameof(EruptionSpawner.RPCA_SpawnEruption))]
    private static class ShortKilnEruptions
    {
        private static void Postfix(Vector3 position)
        {
            if (!On || !KilnAsh.InKiln(position))
            {
                return;
            }
            float phase = KilnAsh.ClockPhase;
            float seconds = phase >= 0f ? Mathf.Clamp(KilnEruptionOutBy - phase, 1f, KilnEruptionSeconds) : KilnEruptionSeconds;
            foreach (RemoveAfterSeconds made in UnityEngine.Object.FindObjectsByType<RemoveAfterSeconds>(FindObjectsSortMode.None))
            {
                if (made != null && made.seconds > seconds && Vector3.Distance(made.transform.position, position) < 0.1f)
                {
                    made.seconds = seconds;
                }
            }
        }
    }

    /// <summary>A spot on the ground 18-32 m from one of the scouts in the Kiln (on its level, give or take 25 m), inside the Kiln.</summary>
    private static bool KilnEruptionSpot(out Vector3 spot)
    {
        spot = Vector3.zero;
        Character[] inKiln = Character.AllCharacters.Where(c => c != null && !c.data.dead && KilnAsh.InKiln(c.Center)).ToArray();
        if (inKiln.Length == 0)
        {
            return false;
        }
        Character scout = inKiln[UnityEngine.Random.Range(0, inKiln.Length)];
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3 offset = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * UnityEngine.Random.Range(KilnEruptionNear, KilnEruptionFar);
            Vector3 above = scout.Center + offset + Vector3.up * 15f;
            RaycastHit ground = HelperFunctions.LineCheck(above, above + Vector3.down * 40f, HelperFunctions.LayerType.TerrainMap);
            if (ground.transform != null && ground.normal.y > 0.5f && KilnAsh.InKiln(ground.point) && Mathf.Abs(ground.point.y - scout.Center.y) < 25f)
            {
                spot = ground.point;
                return true;
            }
        }
        return false;
    }

    /// <summary>Climbing a petrifying stone: 8 petrification a second, twice its touch (the game: its climbing effect is off).</summary>
    private const float PetrifyClimbPoints = 8f;

    /// <summary>
    /// Climbable hazards apply twice as much (ice rocks: 2 cold every 0.3 s instead of 1). Climbing a
    /// petrifying stone petrifies like touching it, doubled. Magma rocks have no climbing effect of
    /// their own; see <see cref="A17_ClimbingHazards"/>.
    /// </summary>
    [HarmonyPatch(typeof(ClimbModifierSurface), nameof(ClimbModifierSurface.OnClimb))]
    private static class HazardousRocks
    {
        internal struct Saved
        {
            internal bool changed;
            internal bool applyStatus;
            internal float amount;
        }

        private static void Prefix(ClimbModifierSurface __instance, out Saved __state)
        {
            __state = default;
            if (!On)
            {
                return;
            }
            if (__instance.applyPetrify)
            {
                __state = new Saved { changed = true, applyStatus = __instance.applyStatus, amount = __instance.statusAmount };
                __instance.applyStatus = true;
                __instance.statusAmount = PetrifyClimbPoints;
            }
            else if (__instance.applyStatus)
            {
                __state = new Saved { changed = true, applyStatus = true, amount = __instance.statusAmount };
                __instance.statusAmount *= 2f;
            }
        }

        private static Exception Finalizer(ClimbModifierSurface __instance, Saved __state, Exception __exception)
        {
            if (__state.changed)
            {
                __instance.applyStatus = __state.applyStatus;
                __instance.statusAmount = __state.amount;
            }
            return __exception;
        }
    }

    /// <summary>A geyser's warm spot reaches 2 m (the game: 3 m) and removes the game's 5 cold a second.</summary>
    private const float GeyserWarmRadius = 2f;

    /// <summary>A geyser erupts on 80% of the steps onto it (the game: 33%).</summary>
    private const float GeyserChance = 0.8f;

    [HarmonyPatch(typeof(TriggerEvent), nameof(TriggerEvent.TriggerEntered))]
    private static class Geysers
    {
        private static void Prefix(TriggerEvent __instance)
        {
            if (On && !__instance.onlyOnce && __instance.name.StartsWith("Geyser", StringComparison.Ordinal))
            {
                Overrides.Set(__instance, nameof(TriggerEvent.triggerChance), GeyserChance);
            }
        }
    }

    /// <summary>Spore and gas emitters are four times as strong; geysers' warm spots are smaller.</summary>
    [HarmonyPatch(typeof(StatusEmitter), nameof(StatusEmitter.Update))]
    private static class Emitters
    {
        private static void Prefix(StatusEmitter __instance, out float? __state)
        {
            __state = null;
            if (!OnFor(__instance))
            {
                return;
            }
            if (__instance.amount < 0f && __instance.statusType == CharacterAfflictions.STATUSTYPE.Cold && __instance.transform.parent != null && __instance.transform.parent.name.StartsWith("Geyser", StringComparison.Ordinal))
            {
                Overrides.Set(__instance, nameof(StatusEmitter.radius), GeyserWarmRadius);
                Overrides.Set(__instance, nameof(StatusEmitter.outerFade), 0f);
            }
            else if (__instance.amount > 0f)
            {
                __state = __instance.amount;
                __instance.amount *= 4f;
            }
        }

        private static Exception Finalizer(StatusEmitter __instance, float? __state, Exception __exception)
        {
            if (__state.HasValue)
            {
                __instance.amount = __state.Value;
            }
            return __exception;
        }
    }

    // ---- Explosions: double status, much stronger knockback, 3x longer knock-down that also knocks you out.
    // Some bursts land exact targets instead (amounts given as they land on the bar, before falloff). A cooked
    // Energy Drink's burst is all of that but the knock-out.

    /// <summary>Poison mines: up to 50 poison (the game: 20), then up to 5 every 0.5 s in their cloud for 11 s (the game: 2.5).</summary>
    private const float PoisonMineBurst = 0.5f;

    /// <summary>Spore mines: up to 45 spores (the game: 20), then up to 5 every 0.5 s in their cloud for 11 s.</summary>
    private const float SporeMineBurst = 0.45f;
    private const float MineCloudTick = 0.05f;

    /// <summary>Flash plants blind for up to 180 s, less with distance (the game: 45 s).</summary>
    private const float FlashBlindSeconds = 180f;

    /// <summary>The Gloom's ghost: 60 cold (the game: 35) and 80 s of blindness (the game: 20 s).</summary>
    private const float GhostCold = 0.6f;
    private const float GhostBlindSeconds = 80f;

    /// <summary>
    /// A Mandrake's scream: up to 75 drowsiness per scream, falloff included (the game: 5 a hit, 40 up close).
    /// A whole scream lands 8 hits (measured in the game; its 0.25 s timer fires about every 0.5 s).
    /// </summary>
    private const float MandrakeScream = 0.75f;
    private const float MandrakeScreamTicks = 8f;

    /// <summary>A player who cannot hear the scream still gets this share of it (the game: nothing).</summary>
    internal const float MutedScreamShare = 0.5f;

    /// <summary>The game counts a volume at or below this as switched off.</summary>
    private const float MutedVolume = 0.01f;

    /// <summary>
    /// Whether this player hears a Mandrake: both their sound-effects and their master volume above 1%,
    /// the game's own test (in the game a player who does not hear it gets no drowsiness). Read
    /// afresh for every tick of the scream.
    /// </summary>
    internal static bool HearsMandrakes()
    {
        SettingsHandler settings = GameHandler.Instance != null ? GameHandler.Instance.SettingsHandler : null;
        SFXVolumeSetting effects = settings?.GetSetting<SFXVolumeSetting>();
        MasterVolumeSetting master = settings?.GetSetting<MasterVolumeSetting>();
        return effects == null || master == null || (effects.Value > MutedVolume && master.Value > MutedVolume);
    }

    /// <summary>
    /// The game runs a scream's drowsiness ticks only for players who hear it: every frame
    /// <see cref="Mandrake.CheckScream"/> switches them on while the Mandrake screams and the player's
    /// sound is on. From ascent 17 that one switch keeps them running for muted players too, who get
    /// <see cref="MutedScreamShare"/> of the scream (see <see cref="Retune"/>).
    /// </summary>
    [HarmonyPatch(typeof(Mandrake), nameof(Mandrake.CheckScream))]
    private static class MutedScream
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo setEnabled = AccessTools.PropertySetter(typeof(Behaviour), nameof(Behaviour.enabled));
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int at = code.FindIndex(instruction => instruction.Calls(setEnabled));
            if (at < 0 || code.FindIndex(at + 1, instruction => instruction.Calls(setEnabled)) >= 0)
            {
                throw new InvalidOperationException("The Mandrake's scream switch was not found once; muted players get no scream.");
            }
            // timeEvent.enabled = heard  ->  SwitchScreamTicks(timeEvent, heard, this)
            CodeInstruction self = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[at]);
            code[at].opcode = OpCodes.Call;
            code[at].operand = AccessTools.Method(typeof(A17_Hazards), nameof(SwitchScreamTicks));
            code.Insert(at, self);
            return code;
        }
    }

    private static void SwitchScreamTicks(Behaviour ticks, bool heard, Mandrake mandrake)
    {
        ticks.enabled = heard || (mandrake.screaming && OnFor(mandrake));
    }

    private static bool IsMineBurst(AOE aoe) =>
        aoe.name.StartsWith("VFX_PoisonExplo", StringComparison.Ordinal) ||
        (aoe.name.StartsWith("VFX_SporeExplo", StringComparison.Ordinal) && !aoe.name.StartsWith("VFX_SporeExploExplo", StringComparison.Ordinal));

    private static bool IsMineCloud(AOE aoe) => aoe.name.StartsWith("VFX_SporePoisonExplo", StringComparison.Ordinal);

    /// <summary>Sets an explosion's amounts; returns true when its statuses are exact targets.</summary>
    private static bool Retune(AOE aoe)
    {
        bool blind = string.Equals(aoe.illegalStatus, "BLIND", StringComparison.OrdinalIgnoreCase);
        int cold = aoe.addtlStatus != null ? Array.IndexOf(aoe.addtlStatus, CharacterAfflictions.STATUSTYPE.Cold) : -1;
        if (blind && cold >= 0)
        {
            aoe.statusAmount = GhostBlindSeconds;
            if (aoe.addlStatusAmountOverrides == null)
            {
                aoe.addlStatusAmountOverrides = new System.Collections.Generic.List<float>();
            }
            while (aoe.addlStatusAmountOverrides.Count <= cold)
            {
                aoe.addlStatusAmountOverrides.Add(GhostCold);
            }
            aoe.addlStatusAmountOverrides[cold] = GhostCold;
            return true;
        }
        if (blind)
        {
            aoe.statusAmount = FlashBlindSeconds;
            return false;
        }
        if (IsMineCloud(aoe))
        {
            aoe.statusAmount = MineCloudTick;
            return true;
        }
        if (IsMineBurst(aoe))
        {
            aoe.statusAmount = aoe.statusType == CharacterAfflictions.STATUSTYPE.Spores ? SporeMineBurst : PoisonMineBurst;
            return true;
        }
        if (aoe.statusType == CharacterAfflictions.STATUSTYPE.Drowsy && aoe.GetComponentInParent<Mandrake>() != null)
        {
            // Only the local scout can be made drowsy here: in full if this player hears the scream, else half.
            aoe.statusAmount = (HearsMandrakes() ? MandrakeScream : MandrakeScream * MutedScreamShare) / MandrakeScreamTicks;
            return true;
        }
        return false;
    }

    /// <summary>The burst a cooked Energy Drink lets out when drunk: its own copy of the game's (see <see cref="OwnEnergyDrinkBurst"/>).</summary>
    internal const string EnergyDrinkBurstName = "VFX_EnergyDrinkBurst";

    /// <summary>A cooked Energy Drink's burst: an explosion, but one that knocks nobody out.</summary>
    internal static bool IsEnergyDrinkBurst(AOE aoe) => aoe.name.StartsWith(EnergyDrinkBurstName, StringComparison.Ordinal);

    /// <summary>
    /// Gives the Energy Drink its own copy of the burst it lets out cooked (the game's balloon pop with
    /// knockback), so its burst can be told from the explosions that knock you out.
    /// </summary>
    internal static void OwnEnergyDrinkBurst()
    {
        if (!ItemDatabase.TryGetItem(ToxicDrink.EnergyDrinkPrefab, out Item energy) || energy == null || !energy.TryGetComponent(out Action_Spawn burst) || burst.objectToSpawn == null)
        {
            Plugin.Log.LogWarning("The Energy Drink's burst was not found; from ascent 17 it knocks the drinker out like an explosion.");
            return;
        }
        burst.objectToSpawn = ModItems.Keep(burst.objectToSpawn, EnergyDrinkBurstName);
    }

    [HarmonyPatch(typeof(AOE), nameof(AOE.Explode))]
    private static class Explosions
    {
        [ThreadStatic] internal static int Depth;
        [ThreadStatic] internal static string ExactSource;
        [ThreadStatic] internal static bool KnocksNobodyOut;

        internal struct Scope
        {
            internal bool entered;
            internal string previous;
            internal bool previousKnocksNobodyOut;
        }

        private static void Prefix(AOE __instance, out Scope __state)
        {
            // A Shroomberry's spore burst has its own numbers (ascent 14), not an explosion's, and a cooked Toxic
            // Drink's burst is no explosion; True Final Ascent's blasts (its mines, its cactus rain's dynamite)
            // keep TFA's. A cooked Energy Drink's burst is an explosion that knocks nobody out.
            __state = new Scope
            {
                entered = OnFor(__instance) && !A14_Shroomberries.IsSporeBurst(__instance) && !ToxicDrink.IsBurst(__instance) && !Compat.Tfa.Causing,
                previous = ExactSource,
                previousKnocksNobodyOut = KnocksNobodyOut,
            };
            if (__state.entered)
            {
                Depth++;
                ExactSource = Retune(__instance) ? "aoe:" + __instance.name : null;
                KnocksNobodyOut = IsEnergyDrinkBurst(__instance);
            }
        }

        private static Exception Finalizer(Exception __exception, Scope __state)
        {
            if (__state.entered)
            {
                Depth--;
                ExactSource = __state.previous;
                KnocksNobodyOut = __state.previousKnocksNobodyOut;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AdjustStatus))]
    private static class ExplosionStatus
    {
        private static bool Prefix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount)
        {
            if (Explosions.Depth <= 0 || amount <= 0f)
            {
                return true;
            }
            if (Explosions.ExactSource == null)
            {
                amount *= 2f;
                return true;
            }
            // An exact target: lands as given (falloff included), carrying what the bar's steps drop.
            if (__instance.character != null && __instance.character.IsLocal)
            {
                ExactStatus.Add(__instance, statusType, amount, Explosions.ExactSource);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddForce), typeof(Vector3), typeof(float), typeof(float))]
    private static class ExplosionKnockback
    {
        private static void Prefix(ref float minRandomMultiplier, ref float maxRandomMultiplier)
        {
            if (Explosions.Depth > 0)
            {
                minRandomMultiplier *= 3f;
                maxRandomMultiplier *= 4f;
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Fall))]
    private static class ExplosionKnockdown
    {
        private static void Prefix(Character __instance, ref float seconds)
        {
            if (Explosions.Depth <= 0 || !Rules.Owns(__instance))
            {
                return;
            }
            seconds *= 3f;
            if (!Explosions.KnocksNobodyOut)
            {
                KnockOut(__instance);
            }
        }
    }

    /// <summary>Tops drowsiness up until the scout passes out.</summary>
    private static void KnockOut(Character character)
    {
        CharacterAfflictions afflictions = character.refs.afflictions;
        for (int i = 0; i < 4096; i++)
        {
            if (A12_Drowsy.AilmentSum(afflictions) >= 1.02f || afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Drowsy) >= afflictions.GetStatusCap(CharacterAfflictions.STATUSTYPE.Drowsy))
            {
                break;
            }
            if (!afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Drowsy, 0.025f))
            {
                break;
            }
        }
    }
}
