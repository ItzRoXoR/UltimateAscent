using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The clown chest's fly trap (21). On a map with the Gloom, where the game's Venus fly traps grow, one springs up under
/// the scout who opened the chest and closes its jaws on them: a copy of the map's own plant, with its bite, chewing and
/// spit, holding them the game's way (a grip of 45 on the bar) until they break free (jumping, a friend's help); then it
/// spits them out and sinks back into the ground. It never poisons. On a map without the Gloom there is no fly trap to
/// copy, and in a custom run with the fly traps off none grows: the trick paralyses the scout once instead, for as long
/// as a Shroomberry's knock-down at these ascents. Every player's game shows the plant (the opener's says where).
/// </summary>
internal static class ClownFlyTrap
{
    private const string ShowMessage = "clownflytrap";
    private const string LetGoMessage = "clownflytrapgo";

    /// <summary>Where no fly trap grows: the scout knocked down this long, once.</summary>
    internal const float ParalysisSeconds = 12f;

    /// <summary>How big the plant is (the Gloom's grow at 1-1.25), how long it takes to spring up, to spit and to sink back, and how long it holds at most.</summary>
    internal const float Size = 1.1f;
    internal const float GrowSeconds = 0.35f;
    internal const float SpitSeconds = 0.6f;
    internal const float SinkSeconds = 0.5f;
    internal const float MostSeconds = 90f;

    /// <summary>The plants up now (on this game).</summary>
    internal static readonly List<ClownFlyTrapPlant> Plants = new List<ClownFlyTrapPlant>();

    /// <summary>For the tests: plants shown on this game, and paralyses dealt here.</summary>
    internal static int Shown { get; private set; }
    internal static int Paralyses { get; private set; }

    private static VenusFlyTrap source;
    private static GameObject holder;

    internal static void Reset()
    {
        foreach (ClownFlyTrapPlant plant in Plants.Where(p => p != null).ToList())
        {
            Object.Destroy(plant.gameObject);
        }
        Plants.Clear();
        source = null;
    }

    internal static void Listen()
    {
        Net.Handle(ShowMessage, args =>
        {
            if (args.Length > 2 && args[0] is int view && args[1] is Vector3 at && args[2] is float yaw)
            {
                Show(Scout(view), at, yaw);
            }
        });
        Net.Handle(LetGoMessage, args =>
        {
            if (args.Length > 0 && args[0] is int view)
            {
                foreach (ClownFlyTrapPlant plant in Plants.Where(p => p != null && p.ScoutView == view && p.Holding).ToList())
                {
                    plant.LetGo();
                }
            }
        });
    }

    private static Character Scout(int view)
    {
        PhotonView found = PhotonView.Find(view);
        return found != null ? found.GetComponent<Character>() : null;
    }

    /// <summary>One of the map's own fly traps (the Gloom's), to copy; null on a map without the Gloom.</summary>
    private static VenusFlyTrap Source()
    {
        if (source == null)
        {
            source = Resources.FindObjectsOfTypeAll<VenusFlyTrap>().FirstOrDefault(t => t != null && t.gameObject.scene.IsValid());
        }
        return source;
    }

    /// <summary>Whether a fly trap springs up here: the map has the Gloom's, and a custom run has not switched them off.</summary>
    internal static bool Grows => Source() != null && !(RunSettings.IsCustomRun && RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_Flytraps) == 0);

    /// <summary>The trick, on the game of the scout who opened the chest: the fly trap's grip where one grows, else the paralysis.</summary>
    internal static void Play(Character scout, float grip)
    {
        if (!Grows)
        {
            Paralyses++;
            scout.Fall(ParalysisSeconds);
            return;
        }
        scout.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.FlyTrap, grip);
        Vector3 at = Physics.Raycast(scout.Center, Vector3.down, out RaycastHit ground, 3f, HelperFunctions.terrainMapMask) ? ground.point : scout.Center + Vector3.down;
        Vector3 look = scout.data.lookDirection_Flat;
        float yaw = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f;
        Net.SendAll(ShowMessage, scout.photonView.ViewID, at, yaw);
    }

    /// <summary>
    /// The plant, on every game: a copy of the map's fly trap without what makes it the game's own (its bite, poison and
    /// network, its solid parts, the switches that turn it off far away or in a custom run), rooted at the spot.
    /// </summary>
    private static void Show(Character scout, Vector3 at, float yaw)
    {
        VenusFlyTrap game = Source();
        if (game == null || scout == null)
        {
            return;
        }
        if (holder == null)
        {
            holder = new GameObject("UltimateAscent Clown Fly Traps");
            holder.SetActive(false);
        }
        GameObject copy = Object.Instantiate(game.gameObject, holder.transform);
        copy.name = "UltimateAscent Clown Fly Trap";
        Transform trapRoot = game.trapRoot != null ? copy.transform.Find(PathOf(game.trapRoot, game.transform)) : null;
        Transform dragTo = game.dragToPoint != null ? copy.transform.Find(PathOf(game.dragToPoint, game.transform)) : null;
        foreach (VenusFlyTrap logic in copy.GetComponentsInChildren<VenusFlyTrap>(true))
        {
            Object.DestroyImmediate(logic);
        }
        foreach (Component part in copy.GetComponentsInChildren<Component>(true)
            .Where(c => c is Peak.GenericOptimizer || c is DisableBasedOnRunSettings || c is CollisionModifier).ToList())
        {
            Object.DestroyImmediate(part);
        }
        foreach (Collider solid in copy.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(solid);
        }
        foreach (PhotonView view in copy.GetComponentsInChildren<PhotonView>(true))
        {
            Object.DestroyImmediate(view);
        }
        ClownFlyTrapPlant plant = copy.AddComponent<ClownFlyTrapPlant>();
        plant.Set(scout, copy.GetComponent<Animator>(), trapRoot, dragTo, game.extraDrag, game.dragForce, game.forceCurve);
        copy.transform.SetParent(null, false);
        copy.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        copy.transform.localScale = Vector3.one * 0.05f;
        copy.SetActive(true);
        Plants.Add(plant);
        Shown++;
    }

    /// <summary>The way down from a plant to one of its parts.</summary>
    private static string PathOf(Transform part, Transform root)
    {
        List<string> names = new List<string>();
        for (Transform t = part; t != null && t != root; t = t.parent)
        {
            names.Add(t.name);
        }
        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>The scout's own game: they are free (or gone), and every game's plant lets them go.</summary>
    internal static void Free(Character scout)
    {
        Net.SendAll(LetGoMessage, scout.photonView.ViewID);
    }
}

/// <summary>A clown chest's fly trap: springing up, holding its scout in its jaws, spitting them out and sinking away.</summary>
internal sealed class ClownFlyTrapPlant : MonoBehaviour
{
    private Character scout;
    private Animator anim;
    private Transform trapRoot;
    private Transform dragTo;
    private Quaternion trapRest;
    private float extraDrag;
    private float dragForce;
    private AnimationCurve forceCurve;
    private Rigidbody torso;
    private float born;
    private float letGoAt = -1f;

    internal int ScoutView { get; private set; }
    internal Character Scout => scout;
    internal bool Holding => letGoAt < 0f;
    /// <summary>For the tests: its jaws closed, and where it holds its scout.</summary>
    internal bool Biting => anim != null && anim.GetBool("Biting");
    internal Vector3 HoldPoint => dragTo != null ? dragTo.position : transform.position;

    internal void Set(Character scout, Animator anim, Transform trapRoot, Transform dragTo, float extraDrag, float dragForce, AnimationCurve forceCurve)
    {
        this.scout = scout;
        ScoutView = scout.photonView.ViewID;
        this.anim = anim;
        this.trapRoot = trapRoot;
        this.dragTo = dragTo;
        this.extraDrag = extraDrag;
        this.dragForce = dragForce;
        this.forceCurve = forceCurve;
        torso = scout.GetBodypartRig(BodypartType.Torso);
    }

    private void Start()
    {
        born = Time.time;
        if (trapRoot != null)
        {
            trapRest = trapRoot.rotation;
        }
        if (anim != null)
        {
            anim.SetBool("Biting", true);
        }
    }

    private void Update()
    {
        float now = Time.time;
        float size = ClownFlyTrap.Size;
        if (Holding)
        {
            transform.localScale = Vector3.one * Mathf.Lerp(0.05f, size, 1f - Mathf.Pow(1f - Mathf.Clamp01((now - born) / ClownFlyTrap.GrowSeconds), 3f));
            // The scout's own game watches whether they are free (or gone); another game lets go of a scout who left.
            if (scout == null)
            {
                LetGo();
            }
            else if (scout.IsLocal && (scout.data.dead || scout.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.FlyTrap) < 0.025f || now - born > ClownFlyTrap.MostSeconds))
            {
                if (scout.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.FlyTrap) > 0f)
                {
                    scout.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.FlyTrap, 1f);
                }
                ClownFlyTrap.Free(scout);
            }
            else if (!scout.IsLocal && now - born > ClownFlyTrap.MostSeconds + 5f)
            {
                LetGo();
            }
            return;
        }
        float sinking = now - letGoAt - ClownFlyTrap.SpitSeconds;
        if (trapRoot != null)
        {
            trapRoot.rotation = Quaternion.Slerp(trapRoot.rotation, trapRest, Time.deltaTime * 8f);
        }
        if (sinking > 0f)
        {
            float left = 1f - sinking / ClownFlyTrap.SinkSeconds;
            if (left <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            transform.localScale = Vector3.one * size * left;
        }
    }

    /// <summary>The game's hold: its head turned to the scout, and on the scout's own game their body pulled into its jaws.</summary>
    private void FixedUpdate()
    {
        if (!Holding || scout == null)
        {
            return;
        }
        if (trapRoot != null)
        {
            Quaternion facing = Quaternion.LookRotation(scout.Center - trapRoot.position, -transform.forward);
            trapRoot.rotation = Quaternion.Slerp(trapRoot.rotation, facing, Time.fixedDeltaTime * 5f);
        }
        if (!scout.IsLocal || dragTo == null || torso == null)
        {
            return;
        }
        Vector3 toJaws = dragTo.position - scout.Center;
        float pull = forceCurve != null ? forceCurve.Evaluate(new Vector3(toJaws.x, 0f, toJaws.z).sqrMagnitude) : 0f;
        scout.refs.movement.ApplyExtraDrag(extraDrag, ignoreRagdoll: true);
        scout.AddForceToBodyPart(torso, toJaws.normalized * (dragForce * 0.2f) * pull, toJaws.normalized * dragForce * pull);
    }

    internal void LetGo()
    {
        if (!Holding)
        {
            return;
        }
        letGoAt = Time.time;
        if (anim != null)
        {
            anim.SetBool("Biting", false);
        }
    }

    private void OnDestroy()
    {
        ClownFlyTrap.Plants.Remove(this);
    }
}
