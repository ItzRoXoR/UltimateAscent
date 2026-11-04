using System.Collections.Generic;
using System.Linq;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 18: the Roots wind carries debris. While a wind storm blows where a scout is, it brings spruce needles,
/// sticks, Spruce Branches and rocks at them from upwind (outside the Roots, ascent 25's wind brings needles only):
/// <list type="bullet">
/// <item>A needle sticks in like a cactus thorn, shown as a spruce needle (see <see cref="SpruceNeedles"/>).</item>
/// <item>A stick or a branch knocks the scout down as a stick's swing does (4 s, the kick's push).</item>
/// <item>A rock hits as a thrown stone that shatters on a scout does: down for 2 s and 20 injury, the rock smash and
/// its dust for everyone.</item>
/// </list>
/// Each gust brings 200-250 needles, 7-10 sticks, 13-17 branches and 4-6 rocks (the user's; outside the Roots the
/// needles only), none in the gust's first 1.5 s, then each kind spread evenly over the rest of it, to its last moment
/// (the far needles also wait out those 1.5 s). Every piece flies in
/// from 25-35 m upwind along the wind (in sight for two seconds and more; a stick, branch or rock whooshes as it comes
/// near). Of the needles 65% fly past the scout, over their head or off to one side, and 35% at them; of the sticks,
/// branches and rocks half fly past and half at them (the user's). What flies at the scout goes each at its own point
/// over their silhouette as the wind sees it (where they are as it sets off, so a scout who moves can dodge it), the
/// needles spread over the whole of it and a little past its edge, so they stick in all over and a few graze by. Nothing hits a scout sheltered from the wind (under a roof, behind a hill, rock or wall: the game's and
/// the mod's own shelter, see <see cref="SnowstormShelter"/>), and what flies at one passes them by; anything solid it
/// meets on its way stops it (the land, rocks, trees, props; not the fog walls between segments, nothing there to see),
/// the far needles too, and nothing sets off where its way
/// would take it out through the back of a surface (from inside a rock or a hill: their surfaces are hollow to the
/// physics, so a piece set off inside one would come out of it). An item a scout holds is solid too, so it shields
/// them: a needle that hits it sticks in it (see <see cref="SpruceNeedles"/>), a stick, branch or rock bounces off it
/// and falls (a worn backpack is no shield: there is nothing solid of it). What lands lies a moment and fades (nothing to pick up). Each player's game makes the debris round its own
/// scout, only within 35 m of them (what has flown past them is gone beyond that); farther out the wind shows flying
/// needles drawn by a particle system only (nothing solid, nothing that hits).
/// <para>With True Final Ascent: no debris comes to a scout its Builder's rest stops spare (the wind there neither
/// pushes nor chills them); what flies passes the Builder's invisible walls between its blocks (nothing there to see);
/// and how near it hits, and how big a needle stuck in is, go by the scout's size (TFA's Mini and Mega).</para>
/// </summary>
internal static class WindDebris
{
    internal enum Kind
    {
        Needle,
        Stick,
        Branch,
        Rock,
    }

    private const string RockMessage = "winddebrisrock";

    /// <summary>What debris meets and stops at: anything solid (the land, rocks, trees, props, items, ropes), only scouts left to the hits.</summary>
    private static int Solid => HelperFunctions.AllPhysicalExceptCharacter;

    /// <summary>
    /// What a gust brings, by kind (needles, sticks, branches, rocks): between these many, the user's; outside the Roots
    /// the needles only.
    /// </summary>
    internal static readonly (int least, int most)[] PerGust = { (200, 250), (7, 10), (13, 17), (4, 6) };

    /// <summary>No debris sets off in a gust's first this many seconds (the user's), the far needles neither.</summary>
    internal const float StartSeconds = 1.5f;

    /// <summary>A piece due in a gust that cannot set off yet (nowhere in the open upwind) is tried again for this long, then dropped.</summary>
    private const float RetrySeconds = 0.5f;

    /// <summary>
    /// What flies past the scout (the user's: 65% of the needles, half the sticks, branches and rocks): over their head,
    /// this high above it (spread this far across), or off to one side, this far; the rest comes at them.
    /// </summary>
    internal const float PassingShare = 0.65f;
    internal const float PiecePassingShare = 0.5f;
    internal const float OverheadMin = 2f;
    internal const float OverheadMax = 8f;
    internal const float OverheadSpread = 5f;
    internal const float BesideMin = 1.5f;
    internal const float BesideMax = 8f;

    /// <summary>Where a piece comes from (this far upwind) and how fast it flies.</summary>
    internal const float FromMin = 25f;
    internal const float FromMax = 35f;
    internal const float NeedleSpeed = 14f;
    internal const float PieceSpeed = 12f;

    /// <summary>How hard it sinks on its way (a needle drifts, a stick or a rock falls a little).</summary>
    private const float NeedleSink = 0.6f;
    private const float PieceSink = 1.2f;

    /// <summary>What flies at a sheltered scout passes them by, this far to one side.</summary>
    internal const float ShelteredMiss = 3f;

    /// <summary>The needles at the scout are aimed over an oval this much wider all round than their body's parts (a few graze by at its edge).</summary>
    internal const float SilhouetteMargin = 0.15f;

    /// <summary>A piece hits a part of the scout's body it passes this near (on a scout of the game's size).</summary>
    internal const float NeedleReach = 0.22f;
    internal const float PieceReach = 0.32f;

    /// <summary>How near a piece hits this scout: as much nearer or farther as they are smaller or bigger (True Final Ascent's Mini and Mega).</summary>
    internal static float ReachOf(Character scout, Kind kind) => (kind == Kind.Needle ? NeedleReach : PieceReach) * Compat.Tfa.SizeOf(scout);

    /// <summary>A scout is out in the wind (and can be hit) from this exposure on: the game's own and the land's shelter (0 sheltered, 1 open).</summary>
    internal const float ExposedFrom = 0.2f;

    /// <summary>Pieces only this near the scout, and at most so many at a time (more than a gust keeps in the air: a short gust's 250 needles come 30 a second, some 150 at a time).</summary>
    internal const float NearRadius = 35f;
    internal const int MostNeedles = 250;
    internal const int MostPieces = 40;

    /// <summary>A stick, branch or rock that hits an item a scout holds bounces off it with this share of its speed, and falls.</summary>
    internal const float BounceKeeps = 0.3f;

    /// <summary>A landed stick, branch or rock lies this long and then fades away in this long; a needle that meets the ground is gone at once.</summary>
    internal const float LiesSeconds = 2f;
    internal const float FadeSeconds = 1f;

    /// <summary>A stick, branch or rock whooshes as it comes this near the scout.</summary>
    internal const float WhooshFrom = 9f;

    /// <summary>
    /// The far needles: needles drawn by a particle system (nothing that hits), flying past from between these distances,
    /// so many a second the whole gust through, drawn this much bigger than a needle (so that they show that far off).
    /// </summary>
    internal const float FarFrom = 36f;
    internal const float FarTo = 90f;
    internal const float FarPerSecond = 160f;
    private const float FarLife = 4f;
    private const float FarSize = 2.5f;

    private sealed class Piece
    {
        internal Kind kind;
        internal GameObject look;
        internal Vector3 at;
        internal Vector3 velocity;
        internal float sink;
        internal Vector3 spinAxis;
        internal float spin;
        internal float born;
        internal float landedAt = -1f;
        internal bool whooshed;
        internal bool harmless;
        internal bool cameNear;
        internal Vector3 size;
    }

    private static readonly List<Piece> pieces = new List<Piece>();
    private static readonly List<WindChillZone> zones = new List<WindChillZone>();
    private static float zonesFound = float.NegativeInfinity;
    private static ParticleSystem far;
    private static float farOwed;

    /// <summary>The gust's debris: when in it (0 its start, 1 its end) each piece is due, the next due, and the gust it is for.</summary>
    private static readonly List<(float at, Kind kind)> plan = new List<(float, Kind)>();
    private static int planNext;
    private static WindChillZone planZone;
    private static float planLength = -1f;

    /// <summary>For the tests: pieces sent and pieces that hit the local scout, by kind; what the last hit was; the wind's strength the last frame.</summary>
    internal static readonly int[] Sent = new int[4];
    internal static readonly int[] Hits = new int[4];
    /// <summary>
    /// For the tests: pieces sent past the scout (by kind), pieces the land stopped, pieces that came within 2.5 m of the
    /// scout, pieces of a gust dropped (nowhere to set off from in time), places to set off from turned down as inside
    /// something solid, and what the gust brings (by kind).
    /// </summary>
    internal static readonly int[] PassedBy = new int[4];
    internal static int Stopped;
    internal static int CameNear;
    /// <summary>For the tests: needles stuck in held items, and pieces bounced off them.</summary>
    internal static int NeedlesInItems;
    internal static int BouncedOffItems;
    internal static int Dropped;
    internal static int StartsInside;
    internal static readonly int[] Planned = new int[4];
    /// <summary>For the tests: where the last piece sent set off, the parts of the body the needles hit, and what stopped pieces (the latest 256).</summary>
    internal static Vector3 LastStart { get; private set; }
    internal static readonly Dictionary<string, int> NeedlesOn = new Dictionary<string, int>();
    internal static readonly List<Collider> StoppedBy = new List<Collider>();
    internal static string LastHit = "";
    internal static float Strength { get; private set; }
    /// <summary>For the tests: the next piece's kind, once; and no pieces of the debris's own (only those the tests send).</summary>
    internal static Kind? NextKind;
    internal static bool OnlySent;

    internal static int Needles => pieces.Count(p => p.kind == Kind.Needle);
    internal static int Others => pieces.Count(p => p.kind != Kind.Needle);
    internal static float FarthestFrom(Vector3 at) => pieces.Select(p => Vector3.Distance(p.at, at)).DefaultIfEmpty(0f).Max();
    internal static int FarCount => far != null ? far.particleCount : 0;
    /// <summary>For the tests: far needles sent.</summary>
    internal static int FarSent;
    internal static string FarShading { get; private set; } = "";

    /// <summary>For the tests: where the newest piece of a kind still flying is.</summary>
    internal static Vector3? Newest(Kind kind)
    {
        Piece piece = pieces.LastOrDefault(p => p.kind == kind && p.landedAt < 0f);
        return piece != null ? piece.at : (Vector3?)null;
    }

    private static bool On => Plugin.Ready && Rules.InRun && Map.Ready && Rules.Enabled(Lvl.ExtremeWeather);

    internal static void Reset()
    {
        foreach (Piece piece in pieces)
        {
            if (piece.look != null)
            {
                Object.Destroy(piece.look);
            }
        }
        pieces.Clear();
        zones.Clear();
        zonesFound = float.NegativeInfinity;
        if (far != null)
        {
            Object.Destroy(far.gameObject);
        }
        far = null;
        farOwed = 0f;
        plan.Clear();
        planNext = 0;
        planZone = null;
        planLength = -1f;
        NextKind = null;
    }

    internal static void Listen()
    {
        Net.Handle(RockMessage, args =>
        {
            if (args.Length > 0 && args[0] is Vector3 at)
            {
                StoneItem.BreakSound()?.Play(at);
                GameObject dust = StoneItem.DustTemplate();
                if (dust != null)
                {
                    Object.Instantiate(dust, at, Quaternion.identity);
                }
            }
        });
    }

    /// <summary>The wind storm blowing where the local scout is, if one is (and not held calm, nor the scout resting at one of True Final Ascent's rest stops).</summary>
    internal static WindChillZone Blowing(Character scout)
    {
        if (Compat.Tfa.RestSpares(scout))
        {
            return null;
        }
        if (Time.time - zonesFound > 2f)
        {
            zonesFound = Time.time;
            zones.Clear();
            zones.AddRange(Object.FindObjectsByType<WindChillZone>(FindObjectsSortMode.None).Where(z => z != null && z.name == "WindStorm"));
        }
        foreach (WindChillZone zone in zones)
        {
            if (zone != null && zone.isActiveAndEnabled && zone.windActive && zone.localCharacterInsideBounds && !A25_AllWeather.SuppressZone(zone))
            {
                return zone;
            }
        }
        return null;
    }

    /// <summary>How far into its gust a wind storm is, 0 (its start) to 1 (its end): reckoned here, as the game sets its own a frame late.</summary>
    private static float Progress(WindChillZone zone) => Mathf.Clamp01(1f - zone.untilSwitch / Mathf.Max(zone.timeUntilNextWind, 0.01f));

    /// <summary>How long the gust has blown (the game's and ascent 25's phases both keep its length and what is left of it).</summary>
    private static float SecondsInto(WindChillZone zone) => zone.timeUntilNextWind - zone.untilSwitch;

    /// <summary>Every frame on every game, for its own scout.</summary>
    internal static void Tick()
    {
        Character local = Character.localCharacter;
        WindChillZone wind = On && local != null && !local.data.dead ? Blowing(local) : null;
        Strength = wind != null ? wind.currentForceMult : 0f;
        if (wind != null && !OnlySent)
        {
            Plan(wind);
            float progress = Progress(wind);
            while (planNext < plan.Count && plan[planNext].at <= progress)
            {
                if ((progress - plan[planNext].at) * planLength > RetrySeconds)
                {
                    Dropped++;
                    planNext++;
                    continue;
                }
                if (!Send(local, wind, NextKind ?? plan[planNext].kind, null))
                {
                    // Tried again next frame.
                    break;
                }
                NextKind = null;
                planNext++;
            }
        }
        Move(local, wind);
        Far(local, wind);
    }

    /// <summary>
    /// A new gust's debris, once (a gust is told from the last by its length, the game's random pick): if the scout
    /// comes into the storm partway through it, what was due before is not sent all at once.
    /// </summary>
    private static void Plan(WindChillZone wind)
    {
        if (wind == planZone && wind.timeUntilNextWind == planLength)
        {
            return;
        }
        planZone = wind;
        planLength = wind.timeUntilNextWind;
        plan.Clear();
        plan.AddRange(PlanGust(Map.CurrentPlace == Place.Roots, planLength));
        for (int kind = 0; kind < Planned.Length; kind++)
        {
            Planned[kind] = plan.Count(p => (int)p.kind == kind);
        }
        planNext = 0;
        float progress = Progress(wind);
        while (planNext < plan.Count && (progress - plan[planNext].at) * planLength > RetrySeconds)
        {
            planNext++;
        }
    }

    /// <summary>
    /// What a gust this many seconds long brings and when in it (0 its start, 1 its end): of each kind between its two
    /// counts (in the Roots; elsewhere the needles only), none in its first <see cref="StartSeconds"/>, spread evenly over
    /// the rest of it (each in its own share of that), in the order they come. A gust no longer than that brings nothing.
    /// </summary>
    internal static List<(float at, Kind kind)> PlanGust(bool roots, float seconds)
    {
        List<(float at, Kind kind)> made = new List<(float, Kind)>();
        if (seconds <= StartSeconds)
        {
            return made;
        }
        float from = StartSeconds / seconds;
        for (int kind = 0; kind < PerGust.Length; kind++)
        {
            if (!roots && kind != (int)Kind.Needle)
            {
                continue;
            }
            int count = Random.Range(PerGust[kind].least, PerGust[kind].most + 1);
            for (int i = 0; i < count; i++)
            {
                made.Add((from + (1f - from) * (i + Random.Range(0.15f, 0.85f)) / count, (Kind)kind));
            }
        }
        made.Sort((a, b) => a.at.CompareTo(b.at));
        return made;
    }

    /// <summary>
    /// A piece sent from upwind, past the scout (65% of the needles, half of the rest) or at them, or at the point given
    /// (for the tests); false if there are as many of its kind as there may be, or nowhere in the open upwind to send it
    /// from.
    /// </summary>
    internal static bool Send(Character scout, WindChillZone wind, Kind kind, Vector3? aimAt)
    {
        bool needle = kind == Kind.Needle;
        if ((needle ? Needles >= MostNeedles : Others >= MostPieces) || scout == null || wind == null)
        {
            return false;
        }
        Vector3 along = Vector3.ProjectOnPlane(wind.currentWindDirection, Vector3.up);
        if (along.sqrMagnitude < 0.0001f)
        {
            return false;
        }
        along.Normalize();
        Vector3 across = Vector3.Cross(Vector3.up, along);
        Vector3 aim;
        bool passing = false;
        if (aimAt.HasValue)
        {
            aim = aimAt.Value;
        }
        else if (Random.value < (needle ? PassingShare : PiecePassingShare))
        {
            // One flying past: overhead across the sky, or off to the left or the right.
            passing = true;
            aim = Random.value < 0.5f
                ? scout.Center + across * Random.Range(-OverheadSpread, OverheadSpread) + Vector3.up * Random.Range(OverheadMin, OverheadMax)
                : scout.Center + across * ((Random.value < 0.5f ? -1f : 1f) * Random.Range(BesideMin, BesideMax)) + Vector3.up * Random.Range(-0.5f, 3f);
        }
        else
        {
            // At the scout: its own point over their silhouette, where they are as the piece sets off.
            aim = OnSilhouette(scout, across, needle ? SilhouetteMargin : 0f);
            if (wind.windPlayerFactor < ExposedFrom)
            {
                // A sheltered scout: it passes them by, to one side.
                aim += across * ((Random.value < 0.5f ? -1f : 1f) * ShelteredMiss);
            }
        }
        float speed = needle ? NeedleSpeed : PieceSpeed;
        float sink = needle ? NeedleSink : PieceSink;
        Vector3 way = Quaternion.AngleAxis(Random.Range(-8f, 8f), Vector3.up) * along;
        float distance = Random.Range(FromMin, FromMax);
        Vector3 start = Vector3.zero;
        bool open = false;
        foreach (float share in new[] { 1f, 0.7f, 0.45f })
        {
            float seconds = distance * share / speed;
            start = aim - way * (distance * share) + Vector3.up * (0.5f * sink * seconds * seconds);
            if (InOpen(start, way, distance * share + 5f))
            {
                open = true;
                break;
            }
        }
        if (!open)
        {
            return false;
        }
        GameObject look = Look(kind);
        if (look == null)
        {
            return false;
        }
        Piece piece = new Piece
        {
            kind = kind,
            look = look,
            at = start,
            velocity = way * speed,
            sink = sink,
            spinAxis = Random.onUnitSphere,
            spin = needle ? 0f : Random.Range(360f, 720f),
            born = Time.time,
            size = look.transform.localScale,
        };
        look.transform.SetPositionAndRotation(start, needle ? Quaternion.LookRotation(piece.velocity) : Random.rotation);
        pieces.Add(piece);
        Sent[(int)kind]++;
        PassedBy[(int)kind] += passing ? 1 : 0;
        LastStart = start;
        return true;
    }

    /// <summary>
    /// A point over a scout's silhouette as the wind sees it (across the wind and up, where their body is now), anywhere
    /// in an oval round their body's parts this much wider all round: what flies at them spreads over the whole of them,
    /// not all at one spot.
    /// </summary>
    private static Vector3 OnSilhouette(Character scout, Vector3 across, float margin)
    {
        margin *= Compat.Tfa.SizeOf(scout);
        Vector3 middle = scout.Center;
        float left = float.PositiveInfinity, right = float.NegativeInfinity, low = float.PositiveInfinity, high = float.NegativeInfinity;
        foreach (Bodypart part in scout.refs.ragdoll.partList)
        {
            if (part == null)
            {
                continue;
            }
            Vector3 off = part.transform.position - middle;
            float side = Vector3.Dot(off, across);
            left = Mathf.Min(left, side);
            right = Mathf.Max(right, side);
            low = Mathf.Min(low, off.y);
            high = Mathf.Max(high, off.y);
        }
        if (float.IsInfinity(left))
        {
            return middle;
        }
        Vector2 at = Random.insideUnitCircle;
        return middle + across * ((left + right) * 0.5f + at.x * ((right - left) * 0.5f + margin))
            + Vector3.up * ((low + high) * 0.5f + at.y * ((high - low) * 0.5f + margin));
    }

    /// <summary>
    /// Whether a piece setting off here, this way, is in the open air: clear of anything solid round it, and its way (this
    /// far) never out through the back of a surface. A rock's, a hill's or the ground's surface is hollow to the physics
    /// (a cast does not meet it from behind), so from inside one the first surface met on the way is the back of it; its
    /// front, from outside, stops the piece as it flies.
    /// </summary>
    private static bool InOpen(Vector3 at, Vector3 way, float reach)
    {
        if (Physics.CheckSphere(at, 0.4f, Solid, QueryTriggerInteraction.Ignore))
        {
            return false;
        }
        bool backs = Physics.queriesHitBackfaces;
        try
        {
            Physics.queriesHitBackfaces = false;
            float front = Physics.Raycast(at, way, out RaycastHit outside, reach, Solid, QueryTriggerInteraction.Ignore) ? outside.distance : float.PositiveInfinity;
            Physics.queriesHitBackfaces = true;
            float first = Physics.Raycast(at, way, out RaycastHit any, reach, Solid, QueryTriggerInteraction.Ignore) ? any.distance : float.PositiveInfinity;
            if (first < front - 0.01f)
            {
                StartsInside++;
                return false;
            }
            return true;
        }
        finally
        {
            Physics.queriesHitBackfaces = backs;
        }
    }

    /// <summary>A piece's look: a needle built here, or a copy of what the Stick, the Spruce Branch or the Stone show (nothing solid).</summary>
    private static GameObject Look(Kind kind)
    {
        GameObject made;
        if (kind == Kind.Needle)
        {
            made = new GameObject("UltimateAscent Wind Needle");
            made.AddComponent<MeshFilter>().sharedMesh = SpruceLook.Needle;
            made.AddComponent<MeshRenderer>().sharedMaterial = SpruceLook.Materials[SpruceLook.Green];
            made.transform.localScale = SpruceNeedles.Scale;
            return made;
        }
        Transform source = kind == Kind.Rock ? Part(StoneItem.Template, "Stone") : Part(kind == Kind.Stick ? StickItem.Template : SpruceBranch.Template, SpruceBranch.StickLook);
        if (source == null)
        {
            return null;
        }
        made = Object.Instantiate(source.gameObject);
        made.name = "UltimateAscent Wind " + kind;
        made.transform.localScale = source.lossyScale;
        foreach (Collider solid in made.GetComponentsInChildren<Collider>(true))
        {
            Object.Destroy(solid);
        }
        made.SetActive(true);
        return made;
    }

    private static Transform Part(Item item, string name)
    {
        return item != null ? item.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) : null;
    }

    /// <summary>The pieces on their way: flying on, meeting the land, hitting the scout, lying and fading.</summary>
    private static void Move(Character scout, WindChillZone wind)
    {
        float now = Time.time;
        float dt = Time.deltaTime;
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            Piece piece = pieces[i];
            if (piece.look == null)
            {
                pieces.RemoveAt(i);
                continue;
            }
            if (piece.landedAt >= 0f)
            {
                float lain = now - piece.landedAt;
                if (lain >= LiesSeconds + FadeSeconds)
                {
                    Remove(i);
                }
                else if (lain > LiesSeconds)
                {
                    piece.look.transform.localScale = piece.size * (1f - (lain - LiesSeconds) / FadeSeconds);
                }
                continue;
            }
            Vector3 fromScout = scout != null ? piece.at - scout.Center : Vector3.zero;
            if (now - piece.born > 8f || scout == null || fromScout.magnitude > NearRadius * 1.5f
                || (fromScout.magnitude > NearRadius && Vector3.Dot(piece.velocity, fromScout) > 0f))
            {
                Remove(i);
                continue;
            }
            bool needle = piece.kind == Kind.Needle;
            piece.velocity += Vector3.down * (piece.sink * dt);
            Vector3 step = piece.velocity * dt;
            float radius = needle ? 0.02f : 0.08f;
            if (Meets(piece.at, radius, step, out RaycastHit ground))
            {
                Stopped++;
                if (StoppedBy.Count < 256)
                {
                    StoppedBy.Add(ground.collider);
                }
                Item held = HeldItem(ground.collider);
                if (needle)
                {
                    if (held != null)
                    {
                        SpruceNeedles.StickInItem(held, ground.point, piece.velocity);
                        NeedlesInItems++;
                    }
                    Remove(i);
                    continue;
                }
                if (held != null)
                {
                    // Off the item it hit, back away from its surface with little of its speed, and down to the ground.
                    piece.velocity = Vector3.Reflect(piece.velocity, ground.normal) * BounceKeeps;
                    piece.sink = 9.8f;
                    piece.harmless = true;
                    piece.at = ground.point + ground.normal * (radius + 0.02f);
                    piece.look.transform.position = piece.at;
                    BouncedOffItems++;
                    continue;
                }
                piece.at = ground.point + ground.normal * radius;
                piece.landedAt = now;
                piece.look.transform.position = piece.at;
                continue;
            }
            if (!piece.harmless && !scout.data.dead && Touches(scout, piece.at, piece.at + step, ReachOf(scout, piece.kind), out Vector3 point))
            {
                if (wind == null || wind.windPlayerFactor < ExposedFrom)
                {
                    // Sheltered: it passes them by.
                    piece.harmless = true;
                }
                else
                {
                    Hit(scout, piece, point);
                    if (piece.kind == Kind.Stick || piece.kind == Kind.Branch)
                    {
                        // It drops off them to the ground.
                        piece.velocity = -piece.velocity.normalized * 1.5f + Vector3.up * 1f;
                        piece.sink = 9.8f;
                        piece.harmless = true;
                    }
                    else
                    {
                        // A needle stuck in (or, every thorn taken, falling away), a rock shattered.
                        Remove(i);
                        continue;
                    }
                }
            }
            if (!piece.cameNear && Vector3.Distance(piece.at, scout.Center) < 2.5f)
            {
                piece.cameNear = true;
                CameNear++;
            }
            if (!piece.whooshed && !needle && Vector3.Distance(piece.at, scout.Center) < WhooshFrom)
            {
                piece.whooshed = true;
                StickItem.SwingSound()?.Play(piece.at);
            }
            piece.at += step;
            piece.look.transform.position = piece.at;
            if (needle)
            {
                piece.look.transform.rotation = Quaternion.LookRotation(piece.velocity);
            }
            else
            {
                piece.look.transform.rotation = Quaternion.AngleAxis(piece.spin * dt, piece.spinAxis) * piece.look.transform.rotation;
            }
        }
    }

    private static readonly RaycastHit[] met = new RaycastHit[8];

    /// <summary>The item a scout holds that this collider is part of, if it is one.</summary>
    private static Item HeldItem(Collider collider)
    {
        Item item = collider != null ? collider.GetComponentInParent<Item>() : null;
        return item != null && item.itemState == ItemState.Held ? item : null;
    }

    /// <summary>
    /// The first thing solid a piece's way this frame meets, if any: past the fog walls between the segments (the
    /// game's, and True Final Ascent's between its Builder's blocks), which there is nothing to see of but fog.
    /// </summary>
    private static bool Meets(Vector3 from, float radius, Vector3 step, out RaycastHit first)
    {
        if (!Physics.SphereCast(from, radius, step.normalized, out first, step.magnitude, Solid, QueryTriggerInteraction.Ignore))
        {
            return false;
        }
        if (!Map.IsFogWall(first.collider))
        {
            return true;
        }
        bool found = false;
        int count = Physics.SphereCastNonAlloc(from, radius, step.normalized, met, step.magnitude, Solid, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            // What the sphere is already in at the start comes back at 0 with no point: as a single cast, it is passed.
            bool overlapped = met[i].distance <= 0f && met[i].point == Vector3.zero;
            if (!overlapped && (!found || met[i].distance < first.distance) && !Map.IsFogWall(met[i].collider))
            {
                first = met[i];
                found = true;
            }
        }
        return found;
    }

    private static void Remove(int index)
    {
        if (pieces[index].look != null)
        {
            Object.Destroy(pieces[index].look);
        }
        pieces.RemoveAt(index);
    }

    /// <summary>Whether a piece's way this frame passes this near a part of the scout's body, and where on its way it passes nearest (where it hits them).</summary>
    private static bool Touches(Character scout, Vector3 from, Vector3 to, float reach, out Vector3 point)
    {
        point = scout.Center;
        float best = reach;
        bool touched = false;
        foreach (Bodypart part in scout.refs.ragdoll.partList)
        {
            if (part == null)
            {
                continue;
            }
            Vector3 at = part.transform.position;
            Vector3 way = to - from;
            float t = way.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(at - from, way) / way.sqrMagnitude) : 0f;
            float distance = Vector3.Distance(at, from + way * t);
            if (distance < best)
            {
                best = distance;
                point = from + way * t;
                touched = true;
            }
        }
        return touched;
    }

    /// <summary>What a piece does to the scout it hits, on their own game; false if it did nothing (a needle with no thorn free).</summary>
    private static bool Hit(Character scout, Piece piece, Vector3 point)
    {
        Vector3 way = piece.velocity.normalized;
        switch (piece.kind)
        {
            case Kind.Needle:
                if (!SpruceNeedles.StickIn(scout, point))
                {
                    return false;
                }
                break;
            case Kind.Stick:
            case Kind.Branch:
                CharacterGrabbing kick = scout.refs.grabbing;
                scout.refs.view.RPC(nameof(CharacterGrabbing.RPCA_KickImpact), Photon.Pun.RpcTarget.All, point);
                scout.Fall(StickItem.Knockdown);
                scout.AddForceAtPosition(way * (kick != null ? kick.kickForce : 200f), point, kick != null ? kick.kickRange : 1f);
                break;
            case Kind.Rock:
                scout.Fall(StoneItem.KnockdownMarked);
                Net.SendAll(RockMessage, point);
                break;
        }
        Hits[(int)piece.kind]++;
        string on = scout.refs.ragdoll.partList.Where(p => p != null).OrderBy(p => Vector3.Distance(p.transform.position, point)).Select(p => p.partType.ToString()).FirstOrDefault() ?? "";
        LastHit = $"{piece.kind} on {on}";
        if (piece.kind == Kind.Needle)
        {
            NeedlesOn[on] = NeedlesOn.TryGetValue(on, out int before) ? before + 1 : 1;
        }
        return true;
    }

    /// <summary>
    /// The far needles: pictures in a shell round the scout, flying along the wind, the same the whole gust through after
    /// its first <see cref="StartSeconds"/>, each setting off in the open air (not inside a hill or a rock, which it would
    /// come out of).
    /// </summary>
    private static void Far(Character scout, WindChillZone wind)
    {
        Vector3 along = wind != null ? Vector3.ProjectOnPlane(wind.currentWindDirection, Vector3.up) : Vector3.zero;
        if (Strength <= 0f || scout == null || along.sqrMagnitude < 0.0001f || SecondsInto(wind) < StartSeconds)
        {
            farOwed = 0f;
            return;
        }
        if (far == null)
        {
            far = MakeFar();
            if (far == null)
            {
                return;
            }
        }
        Vector3 velocity = along.normalized * NeedleSpeed + Vector3.down * 0.5f;
        farOwed += FarPerSecond * Strength * Time.deltaTime;
        while (farOwed >= 1f)
        {
            farOwed -= 1f;
            Vector3 at = scout.Center + Random.onUnitSphere * Random.Range(FarFrom, FarTo);
            if (InOpen(at, velocity.normalized, velocity.magnitude * FarLife))
            {
                far.Emit(new ParticleSystem.EmitParams { position = at, velocity = velocity, startLifetime = FarLife }, 1);
                FarSent++;
            }
        }
    }

    /// <summary>
    /// Needles drawn by a particle system: the needle itself drawn as each particle (at this many times a needle's size),
    /// in the needles' shading, along the way it flies (the game's particle shadings do not draw a picture of the mod's
    /// own in this pipeline), each ending where it meets what is in these layers; none sent yet.
    /// </summary>
    private static ParticleSystem NeedleSystem(string name, float size, int most, int meets)
    {
        Material needle = SpruceLook.Materials[SpruceLook.Green];
        FarShading = $"{needle.name} ({needle.shader?.name}), meshes";
        GameObject holder = new GameObject(name);
        ParticleSystem system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.duration = 5f;
        main.startSpeed = 0f;
        Vector3 scale = SpruceNeedles.Scale * size;
        main.startSize3D = true;
        main.startSizeX = scale.x;
        main.startSizeY = scale.y;
        main.startSizeZ = scale.z;
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = most;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTimeMultiplier = 0f;
        // They end where they meet anything solid (a tree, a hill), not through it.
        ParticleSystem.CollisionModule meetsSolid = system.collision;
        meetsSolid.enabled = true;
        meetsSolid.type = ParticleSystemCollisionType.World;
        meetsSolid.mode = ParticleSystemCollisionMode.Collision3D;
        meetsSolid.quality = ParticleSystemCollisionQuality.High;
        meetsSolid.collidesWith = meets;
        meetsSolid.enableDynamicColliders = false;
        meetsSolid.lifetimeLoss = 1f;
        meetsSolid.radiusScale = 0.2f;
        ParticleSystemRenderer look = holder.GetComponent<ParticleSystemRenderer>();
        look.renderMode = ParticleSystemRenderMode.Mesh;
        look.mesh = SpruceLook.Needle;
        look.alignment = ParticleSystemRenderSpace.Velocity;
        look.sharedMaterial = needle;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return system;
    }

    /// <summary>The far needles: bigger than a needle, each sent on its own (its place and way).</summary>
    private static ParticleSystem MakeFar()
    {
        ParticleSystem system = NeedleSystem("UltimateAscent Wind Needles", FarSize, 1000, Solid);
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;
        system.Play();
        return system;
    }
}
