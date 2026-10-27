using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Raven = global::Peak.Island.VoiceThief;
using RavenPerch = global::Peak.Island.RavenPerch;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 17: the Gloom's ravens go for its Early Worms. When a scout sees an Early Worm lying close by (in view, in
/// the open), three times in four a raven flies over, lands beside it, eats it and flies off again: from the nearest
/// raven perch around, where a raven sitting there takes off (it becomes the one that comes), or else out of the sky.
/// A worm someone picks up in the meantime is not eaten; the raven leaves without it. Eating it, the raven makes the
/// sound a scout makes eating food (the game's), heard by every player around.
/// <para>The host decides, once per worm, whether a raven comes (any scout's game tells it when its scout sees one)
/// and takes the eaten worm away for everyone; every player's game draws the raven itself, the same flight for all.</para>
/// </summary>
internal static class GloomCrows
{
    /// <summary>A worm counts as seen this close to a scout's eyes...</summary>
    internal const float SeeDistance = 15f;

    /// <summary>...this near the middle of the view.</summary>
    private const float SeeAngle = 40f;

    /// <summary>How often a raven comes for a worm that was seen.</summary>
    internal const float Chance = 0.75f;

    /// <summary>A perch this far from the worm, or nearer, is where the raven comes from.</summary>
    private const float PerchReach = 60f;

    /// <summary>With no perch around, the raven comes from this far away and this high up.</summary>
    private const float SkyDistance = 22f;
    private const float SkyHeight = 12f;

    private const string SeenMessage = "worm-seen";
    private const string CrowMessage = "crow";

    private static bool On => Rules.Enabled(Lvl.Hazards) && Rules.Hazard(RunSettings.SETTINGTYPE.Hazard_Ravens) && Map.InGloom;

    /// <summary>For the tests: a raven comes for every worm seen (instead of three in four).</summary>
    internal static bool AlwaysForTests;

    /// <summary>Worms eaten on this game (the host's count), for the tests.</summary>
    internal static int Eaten;

    /// <summary>The ravens flying on this game, for the tests.</summary>
    internal static readonly List<CrowFlight> Flying = new List<CrowFlight>();

    private static readonly HashSet<int> seen = new HashSet<int>();
    private static readonly HashSet<int> decided = new HashSet<int>();
    private static global::Peak.EarlyWorm[] worms = new global::Peak.EarlyWorm[0];
    private static float nextLookup;
    private static float nextCheck;
    private static GameObject model;

    /// <summary>The food the eating sound is taken from (the game's sound of a scout eating).</summary>
    private const string EatingFood = "Marshmallow";
    private const string EatingSound = "SFXI Heal Hunger Normal";
    private static SFX_Instance eating;

    /// <summary>Ravens heard eating on this game; for the tests.</summary>
    internal static int EatingHeard;

    /// <summary>Where the last raven ate (for the tests).</summary>
    internal static Vector3 LastEatingAt;

    /// <summary>The sound a scout makes eating food.</summary>
    internal static SFX_Instance EatingSfx()
    {
        if (eating == null)
        {
            eating = ItemDatabase.TryGetItem(EatingFood, out Item food) && food != null && food.TryGetComponent(out ItemUseFeedback feedback) ? feedback.sfxUsed : null;
            eating ??= Resources.FindObjectsOfTypeAll<SFX_Instance>().FirstOrDefault(sound => sound != null && sound.name == EatingSound);
        }
        return eating;
    }

    /// <summary>
    /// The raven's eating is played at full volume (the game plays it at 0.1, from the eating scout's own mouth). At
    /// 0.1 a raven a few metres away was lost in the Gloom's own sound (recorded in the game: 3.5 dB over it at 4.5 m).
    /// </summary>
    internal const float EatingVolume = 1f;

    /// <summary>The sound a scout makes eating food, at the raven's beak (on every player's game).</summary>
    internal static void PlayEating(Vector3 at)
    {
        SFX_Instance sound = EatingSfx();
        if (sound != null && SFX_Player.instance != null)
        {
            SFX_Player.instance.PlaySFX(sound, at, null, null, sound.settings.volume > 0f ? EatingVolume / sound.settings.volume : 1f);
            LastEatingAt = at;
            EatingHeard++;
        }
    }

    internal static void Reset()
    {
        seen.Clear();
        decided.Clear();
        worms = new global::Peak.EarlyWorm[0];
        nextLookup = 0f;
        nextCheck = 0f;
        Flying.Clear();
    }

    internal static void Listen()
    {
        Net.Handle(SeenMessage, args =>
        {
            if (PhotonNetwork.IsMasterClient && args.Length > 0 && args[0] is int id)
            {
                Decide(id);
            }
        });
        Net.Handle(CrowMessage, args =>
        {
            if (args.Length > 1 && args[0] is int id && args[1] is Vector3 from)
            {
                Launch(id, from);
            }
        });
    }

    /// <summary>A worm lying where a raven may take it: on the ground, never cooked, in the Gloom's own climb.</summary>
    internal static bool Takeable(Item worm)
    {
        return worm != null && worm.itemState == ItemState.Ground && worm.photonView != null && (worm.cooking == null || worm.cooking.timesCookedLocal == 0);
    }

    /// <summary>The local scout's eyes on the worms around.</summary>
    internal static void Tick()
    {
        if (!On || Time.time < nextCheck)
        {
            return;
        }
        nextCheck = Time.time + 0.25f;
        if (Time.time >= nextLookup)
        {
            nextLookup = Time.time + 3f;
            worms = Object.FindObjectsByType<global::Peak.EarlyWorm>(FindObjectsSortMode.None);
        }
        Character local = Character.localCharacter;
        Camera eyes = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (local == null || local.data.dead || !local.data.fullyConscious || eyes == null)
        {
            return;
        }
        foreach (global::Peak.EarlyWorm worm in worms)
        {
            Item item = worm != null ? worm.item : null;
            if (!Takeable(item) || seen.Contains(item.photonView.ViewID))
            {
                continue;
            }
            if (!Sees(eyes.transform, item.transform.position))
            {
                continue;
            }
            int id = item.photonView.ViewID;
            seen.Add(id);
            if (PhotonNetwork.IsMasterClient)
            {
                Decide(id);
            }
            else
            {
                Net.SendTo(PhotonNetwork.MasterClient, SeenMessage, id);
            }
        }
    }

    /// <summary>Close, in the middle of the view and not behind anything.</summary>
    internal static bool Sees(Transform eyes, Vector3 point)
    {
        Vector3 to = point + Vector3.up * 0.15f - eyes.position;
        return to.magnitude <= SeeDistance && Vector3.Angle(eyes.forward, to) <= SeeAngle
               && !Physics.Linecast(eyes.position, point + Vector3.up * 0.15f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>On the host's game, once per worm: whether a raven comes for it, and from where.</summary>
    private static void Decide(int id)
    {
        if (!decided.Add(id))
        {
            return;
        }
        PhotonView view = PhotonView.Find(id);
        Item worm = view != null ? view.GetComponent<Item>() : null;
        if (!Takeable(worm) || (!AlwaysForTests && Random.value >= Chance))
        {
            return;
        }
        Vector3 at = worm.transform.position;
        RavenPerch perch = Object.FindObjectsByType<RavenPerch>(FindObjectsSortMode.None)
            .Where(p => p != null && p.isActiveAndEnabled && Vector3.Distance(p.transform.position, at) <= PerchReach)
            .OrderBy(p => Vector3.Distance(p.transform.position, at))
            .FirstOrDefault();
        Vector3 from;
        if (perch != null)
        {
            from = perch.transform.position;
            // The raven sitting there (one that is only sitting) is the one that comes: it leaves its perch now.
            Raven sitting = perch.PerchedRaven;
            if (sitting != null && !sitting.IsAssigned && !sitting.IsLeaving && sitting.TryGetComponent(out PhotonView ravenView) && ravenView.IsMine)
            {
                PhotonNetwork.Destroy(ravenView);
            }
        }
        else
        {
            Vector2 side = Random.insideUnitCircle.normalized;
            from = at + new Vector3(side.x, 0f, side.y) * SkyDistance + Vector3.up * SkyHeight;
        }
        Net.SendAll(CrowMessage, id, from);
    }

    /// <summary>On every game: the raven, drawn here, flies from there to the worm.</summary>
    private static void Launch(int id, Vector3 from)
    {
        PhotonView view = PhotonView.Find(id);
        Item worm = view != null ? view.GetComponent<Item>() : null;
        GameObject template = Model();
        if (worm == null || template == null)
        {
            return;
        }
        GameObject raven = Object.Instantiate(template, from, Quaternion.identity);
        raven.name = "UltimateAscent Raven";
        raven.SetActive(true);
        CrowFlight flight = raven.AddComponent<CrowFlight>();
        flight.Begin(worm, from);
        Flying.Add(flight);
    }

    /// <summary>The raven's look (the voice thief's model with its animations and sounds), nothing of its behaviour.</summary>
    private static GameObject Model()
    {
        if (model != null)
        {
            return model;
        }
        Raven prefab = Object.FindObjectsByType<RavenPerch>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(p => p.voiceThiefPrefab).FirstOrDefault(p => p != null);
        Transform view = prefab != null ? prefab.transform.Find("View") : null;
        if (view == null)
        {
            return null;
        }
        GameObject holder = new GameObject("UltimateAscent Raven Template");
        holder.SetActive(false);
        Object.DontDestroyOnLoad(holder);
        model = Object.Instantiate(view.gameObject, holder.transform);
        model.name = "Raven";
        return model;
    }

    /// <summary>The host takes the eaten worm away for everyone.</summary>
    internal static void Eat(Item worm)
    {
        if (!PhotonNetwork.IsMasterClient || !Takeable(worm) || !worm.photonView.IsMine)
        {
            return;
        }
        Eaten++;
        PhotonNetwork.Destroy(worm.gameObject);
    }

    /// <summary>
    /// One raven's visit: the flight in, a moment on the ground (it eats the worm), the flight off. The game has the
    /// raven only take off (its fly-away animation lifts it off its perch and away): on the way in the bird beats its
    /// wings as in the middle of that animation while it follows its own path, and once down it stands as on a perch.
    /// It leaves the game's way, back toward where it came from.
    /// </summary>
    internal sealed class CrowFlight : MonoBehaviour
    {
        private const float Speed = 9f;
        private const float MinFlight = 1.6f;
        private const float MaxFlight = 5f;
        private const float Arc = 5f;
        private const float OnGround = 1.6f;
        private const float EatAfter = 0.7f;
        private const float LeaveSeconds = 3f;

        /// <summary>The wingbeats in the game's fly-away animation (after the take-off, before the bird is gone).</summary>
        private const float FlapFrom = 0.45f;
        private const float FlapTo = 0.95f;

        /// <summary>It lands beside the worm, this far on the side it comes from.</summary>
        private const float Beside = 0.35f;

        private Item worm;
        private Vector3 from;
        private Vector3 landing;
        private Vector3 wormAt;
        private float flight;
        private float started;
        private Animator animator;
        private AnimationClip flyAway;
        private Transform body;
        private Vector3 perchedBody;
        private bool landed;
        private bool pecked;
        private bool ate;
        private bool leaving;

        internal bool Landed => landed;
        internal bool Ate => ate;
        internal bool Leaving => leaving;

        internal void Begin(Item target, Vector3 start)
        {
            worm = target;
            from = start;
            wormAt = target.transform.position;
            Vector3 side = Vector3.ProjectOnPlane(start - wormAt, Vector3.up);
            side = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.forward;
            landing = wormAt + side * Beside;
            if (Physics.Raycast(landing + Vector3.up * 1f, Vector3.down, out RaycastHit ground, 3f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
            {
                landing = ground.point;
            }
            flight = Mathf.Clamp(Vector3.Distance(start, landing) / Speed, MinFlight, MaxFlight);
            started = Time.time;
            animator = GetComponentInChildren<Animator>(true);
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                flyAway = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c != null && c.name.IndexOf("FlyAway", System.StringComparison.OrdinalIgnoreCase) >= 0);
                body = animator.transform.Find("Armature");
            }
            if (flyAway != null && body != null)
            {
                // The bird's pose is drawn from the animation by hand on the way in.
                animator.enabled = false;
                flyAway.SampleAnimation(animator.gameObject, 0f);
                perchedBody = transform.InverseTransformPoint(body.position);
            }
            Place(0f);
        }

        /// <summary>Where the bird's body is on its way, along an arc from where it starts to just above where it lands.</summary>
        private Vector3 Along(float t)
        {
            Vector3 end = landing + Vector3.up * perchedBody.y;
            Vector3 middle = (from + end) * 0.5f + Vector3.up * Arc;
            return Vector3.Lerp(Vector3.Lerp(from, middle, t), Vector3.Lerp(middle, end, t), t);
        }

        private void Place(float t)
        {
            Vector3 at = Along(t);
            Vector3 ahead = Vector3.ProjectOnPlane(Along(Mathf.Min(1f, t + 0.02f)) - at, Vector3.up);
            if (ahead.sqrMagnitude < 0.0001f)
            {
                ahead = Vector3.ProjectOnPlane(wormAt - at, Vector3.up);
            }
            if (ahead.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(ahead.normalized, Vector3.up);
            }
            if (flyAway == null || body == null)
            {
                transform.position = at;
                return;
            }
            // A wingbeat from the fly-away animation, the whole bird shifted so its body is on the path.
            flyAway.SampleAnimation(animator.gameObject, FlapFrom + Mathf.Repeat(Time.time - started, FlapTo - FlapFrom));
            transform.position = at;
            transform.position = at - (body.position - transform.position);
        }

        private void Update()
        {
            float t = Time.time - started;
            if (!landed)
            {
                float along = Mathf.Clamp01(t / flight);
                Place(along);
                if (along >= 1f)
                {
                    landed = true;
                    started = Time.time;
                    transform.position = landing;
                    Vector3 look = Vector3.ProjectOnPlane(wormAt - transform.position, Vector3.up);
                    if (look.sqrMagnitude > 0.0001f)
                    {
                        transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
                    }
                    if (animator != null)
                    {
                        animator.enabled = true;
                        animator.Play("Idle", 0, 0f);
                    }
                }
                return;
            }
            if (!leaving)
            {
                if (!pecked && t >= EatAfter)
                {
                    pecked = true;
                    // Still lying there (nobody picked it up meanwhile): eaten.
                    ate = Takeable(worm);
                    if (ate)
                    {
                        PlayEating(transform.position + Vector3.up * 0.3f);
                        Eat(worm);
                    }
                }
                if (t >= OnGround)
                {
                    leaving = true;
                    started = Time.time;
                    Vector3 away = Vector3.ProjectOnPlane(from - transform.position, Vector3.up);
                    if (away.sqrMagnitude > 0.0001f)
                    {
                        transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                    }
                    if (animator != null)
                    {
                        animator.Play("FlyAway", 0, 0f);
                    }
                }
                return;
            }
            if (t >= LeaveSeconds)
            {
                Flying.Remove(this);
                Destroy(gameObject);
            }
        }
    }
}
