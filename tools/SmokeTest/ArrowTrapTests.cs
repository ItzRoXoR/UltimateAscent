using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;

namespace UltimateAscent.SmokeTest;

/// <summary>The Citadel's arrow traps: where their lines leave the walls, and what shields a scout from them.</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// Along a trap's line: how far it first meets the map from outside (<c>entry</c>), and how far the solid it starts in
    /// reaches (<c>exit</c>: 0 when it starts in the open). A ray starting inside a collider does not see it, so the exit is
    /// found from the far end back: the last face the line leaves before it first enters anything.
    /// </summary>
    private static (float entry, float exit, string entryName, string exitName) Bore(ArrowShooter trap)
    {
        float entry = float.PositiveInfinity;
        string entryName = "nothing";
        if (Physics.Raycast(trap.pos, trap.forward, out RaycastHit ahead, trap.range, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            entry = ahead.distance;
            entryName = ahead.collider.name;
        }
        float exit = 0f;
        string exitName = "none";
        Vector3 far = trap.pos + trap.forward * trap.range;
        foreach (RaycastHit back in Physics.RaycastAll(far, -trap.forward, trap.range, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            float along = trap.range - back.distance;
            if (along >= 0f && along < entry && along > exit)
            {
                exit = along;
                exitName = back.collider.name;
            }
        }
        return (entry, exit, entryName, exitName);
    }

    /// <summary>A trap's green line in the world: its start and end.</summary>
    private static (Vector3 start, Vector3 end) LineEnds(ArrowShooter trap)
    {
        LineRenderer line = trap.lineRenderer;
        if (line == null || line.positionCount < 2)
        {
            return (trap.pos, trap.pos);
        }
        Vector3 a = line.useWorldSpace ? line.GetPosition(0) : line.transform.TransformPoint(line.GetPosition(0));
        Vector3 b = line.useWorldSpace ? line.GetPosition(1) : line.transform.TransformPoint(line.GetPosition(1));
        return (a, b);
    }

    /// <summary>To the Citadel (the last part of a Gloom map), if not there yet; true once its arrow traps are about.</summary>
    private IEnumerator ToTheCitadel(System.Action<bool> there)
    {
        bool Any() => FindObjectsByType<ArrowShooter>(FindObjectsSortMode.None).Any(s => s != null && s.isActiveAndEnabled);
        if (!Any() && Resources.FindObjectsOfTypeAll<ArrowShooter>().Any(s => s != null && s.gameObject.scene.IsValid())
            && Singleton<MapHandler>.Instance.GetCurrentSegment() < Segment.TheKiln)
        {
            MapHandler.JumpToSegment(Segment.TheKiln);
            yield return new WaitForSeconds(8f);
        }
        there(Any());
    }

    /// <summary>
    /// The Citadel's arrow traps, measured ("-Only arrowprobe"): for each, how deep in the solid its line starts and what
    /// it leaves, where it first meets the map, and where its green line starts and ends against the opening; a picture of
    /// the nearest trap that starts in the open or just inside its wall, and of the one that starts deepest inside.
    /// </summary>
    private IEnumerator ArrowTrapProbe()
    {
        bool there = false;
        yield return ToTheCitadel(t => there = t);
        if (!there)
        {
            Info("the arrow trap probe is skipped", $"no arrow traps ({Map.CurrentPlace}); run with -Biome 81");
            yield break;
        }
        Character local = Local;
        List<ArrowShooter> traps = FindObjectsByType<ArrowShooter>(FindObjectsSortMode.None).Where(s => s != null && s.isActiveAndEnabled).OrderBy(s => Vector3.Distance(s.pos, local.Center)).ToList();
        List<string> rows = new List<string>();
        foreach (ArrowShooter trap in traps)
        {
            (float entry, float exit, string entryName, string exitName) = Bore(trap);
            (Vector3 start, Vector3 end) = LineEnds(trap);
            float startAlong = Vector3.Dot(start - trap.pos, trap.forward);
            float endAlong = Vector3.Dot(end - trap.pos, trap.forward);
            rows.Add($"{Vector3.Distance(trap.pos, local.Center):0} m off: starts {(exit > 0f ? $"{exit:0.00} m inside '{exitName}'" : "in the open")}, meets '{entryName}' at {(float.IsInfinity(entry) ? "-" : entry.ToString("0.0"))} m; "
                + $"line {startAlong:+0.00;-0.00}..{endAlong:+0.00;-0.00} m along (on {(trap.lineRenderer != null && trap.lineRenderer.enabled)}), {(trap.forward.y < -0.75f ? "points down" : trap.forward.y > 0.75f ? "points up" : "level")}");
        }
        Info($"the Citadel's {traps.Count} arrow traps awake (of {Resources.FindObjectsOfTypeAll<ArrowShooter>().Count(s => s != null && s.gameObject.scene.IsValid())} in the level)", string.Join(" | ", rows));
        ArrowShooter near = traps.FirstOrDefault(t => Bore(t).exit < 0.6f && Bore(t).entry > 3f) ?? traps.FirstOrDefault();
        if (near == null)
        {
            yield break;
        }
        LineRenderer line = near.lineRenderer;
        (float _, float mouth, string _, string mouthName) = Bore(near);
        RaycastHit[] own = Physics.RaycastAll(near.pos + near.forward * 2f, -near.forward, 2f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore);
        string Path(Transform tr) => tr == null ? "-" : (tr.parent != null ? Path(tr.parent) + "/" : "") + tr.name;
        Info("the nearest trap's mouth and line",
            $"its mouth is the face of '{mouthName}' ({string.Join(", ", own.Select(h => $"'{Path(h.collider.transform)}' at {2f - h.distance:0.00} m, the trap's own {h.collider.GetComponentInParent<ArrowShooter>() == near}"))}); "
            + $"the trap at '{Path(near.transform)}', its shooter '{Path(near.shooter)}'; "
            + (line == null ? "no line" : $"line '{Path(line.transform)}' (layer {LayerMask.LayerToName(line.gameObject.layer)}, scale {line.transform.lossyScale.x:0.###}), enabled {line.enabled}, world space {line.useWorldSpace}, "
                + $"{line.positionCount} points, width {line.startWidth:0.###}-{line.endWidth:0.###} x{line.widthMultiplier:0.###}, texture mode {line.textureMode}, alignment {line.alignment}, "
                + $"material '{(line.sharedMaterial != null ? line.sharedMaterial.name : "none")}' ({(line.sharedMaterial != null ? line.sharedMaterial.shader.name : "-")}), "
                + $"colours {string.Join(" ", line.colorGradient.colorKeys.Select(k => $"{k.time:0.00}:#{ColorUtility.ToHtmlStringRGB(k.color)}"))}, alpha {string.Join(" ", line.colorGradient.alphaKeys.Select(k => $"{k.time:0.00}:{k.alpha:0.00}"))}"));
        Vector3 opening = near.pos + near.forward * mouth;
        Vector3 side = Vector3.Cross(Vector3.up, near.forward).sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, near.forward).normalized : Vector3.right;
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        try
        {
            Camera camera = holder.AddComponent<Camera>();
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.05f;
            holder.transform.position = opening + near.forward * 2.5f + side * 1f + Vector3.up * 0.5f;
            holder.transform.LookAt(opening + near.forward * 0.5f);
            yield return new WaitForSeconds(0.6f);
            Screenshot("arrow-trap-line-now");
            // A metre out of the opening.
            if (line != null)
            {
                Vector3 end = opening + near.forward * 1f;
                line.SetPosition(1, line.useWorldSpace ? end : line.transform.InverseTransformPoint(end));
            }
            yield return new WaitForSeconds(0.3f);
            Screenshot("arrow-trap-line-1m");
            // The game's whole line.
            near.DrawWarningLine();
            yield return new WaitForSeconds(0.3f);
            Screenshot("arrow-trap-line-game");
            holder.transform.position = opening + near.forward * 6f + side * 1.5f + Vector3.up * 1f;
            holder.transform.LookAt(opening + near.forward * 2f);
            yield return new WaitForSeconds(0.3f);
            Screenshot("arrow-trap-line-game-far");
        }
        finally
        {
            Destroy(holder);
        }
    }

    /// <summary>A picture of a trap's warning line from its side (its whole length across the picture), 2 m off it.</summary>
    private IEnumerator ArrowLinePicture(ArrowShooter trap, float mouth, string picture)
    {
        Vector3 opening = trap.pos + trap.forward * mouth;
        Vector3 side = Vector3.Cross(Vector3.up, trap.forward).sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, trap.forward).normalized : Vector3.right;
        GameObject holder = new GameObject("UltimateAscentTestCamera");
        try
        {
            Camera camera = holder.AddComponent<Camera>();
            camera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 10f;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.05f;
            // Off whichever side of the line has room (the trap is in a wall).
            if (Physics.Raycast(opening + trap.forward * 0.6f, side, 2.2f, HelperFunctions.terrainMapMask))
            {
                side = -side;
            }
            holder.transform.position = opening + trap.forward * 0.6f + side * 2f + Vector3.up * 0.3f;
            holder.transform.LookAt(opening + trap.forward * 0.6f);
            yield return new WaitForSeconds(0.5f);
            Screenshot(picture);
            // The picture is taken at the end of the frame: the camera stays till then.
            yield return new WaitForSeconds(0.3f);
        }
        finally
        {
            Destroy(holder);
        }
    }

    /// <summary>A block of the map (the Map layer) for the tests, lined up with a trap's line.</summary>
    private static GameObject TestBlock(string name, Vector3 centre, Vector3 size, Vector3 forward)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "UltimateAscent Test " + name;
        block.layer = LayerMask.NameToLayer("Map");
        block.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z).sqrMagnitude > 0.01f ? new Vector3(forward.x, 0f, forward.z) : Vector3.forward));
        block.transform.localScale = size;
        Physics.SyncTransforms();
        return block;
    }

    /// <summary>
    /// What stops an arrow trap's arrows (9): the scout stands on a test floor in a trap's line, 4 m out of its opening.
    /// In the open the trap hits them. A Backpack held loose in the line between them (as one lying there would be)
    /// takes the shots instead, as in the game. A test block put round the trap's mouth, as a platform the level puts over
    /// a trap: the game's trap shoots straight through it (shown with the fix switched off); now it holds its fire and
    /// hides its warning line, and shows it again once the block is gone.
    /// </summary>
    private IEnumerator ArrowTrapShieldCheck()
    {
        bool there = false;
        yield return ToTheCitadel(t => there = t);
        Item backpack = ItemByName("Backpack");
        if (!there || backpack == null)
        {
            Info("the arrow trap shield check is skipped", $"arrow traps {there} ({Map.CurrentPlace}), Backpack {backpack != null}; run with -Biome 81");
            yield break;
        }
        Character local = Local;
        Vector3 home = local.Center;
        // The nearest level trap, not buried, with a clear line for 6 m.
        ArrowShooter trap = FindObjectsByType<ArrowShooter>(FindObjectsSortMode.None)
            .Where(s => s != null && s.isActiveAndEnabled && Mathf.Abs(s.forward.y) < 0.3f && !GameFixes.Bore(s).buried && Bore(s).entry > GameFixes.Bore(s).mouth + 6f)
            .OrderBy(s => Vector3.Distance(s.pos, home)).FirstOrDefault();
        if (trap == null)
        {
            Info("the arrow trap shield check is skipped", "no level arrow trap with a clear line for 6 m");
            yield break;
        }
        float mouth = GameFixes.Bore(trap).mouth;
        Vector3 standAt = trap.pos + trap.forward * (mouth + 4f);
        List<GameObject> blocks = new List<GameObject>();
        GameObject shield = null;
        Protect(true);
        try
        {
            // A floor for the scout, its top 0.9 m under the line (the line then crosses their chest).
            blocks.Add(TestBlock("Floor", standAt + Vector3.down * 1.1f, new Vector3(1.6f, 0.4f, 1.6f), trap.forward));
            IEnumerator StandInTheLine()
            {
                local.refs.afflictions.RemoveAllThorns();
                local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, standAt + Vector3.up * 0.2f, false);
                yield return new WaitForSeconds(0.1f);
                for (float until = Time.time + 3f; Time.time < until && local.warping;)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(0.6f);
            }
            IEnumerator Count(float seconds, System.Action<int, int> counted)
            {
                int hits = ArrowShots.Hits;
                int shots = ArrowShots.Count;
                yield return new WaitForSeconds(seconds);
                counted(ArrowShots.Hits - hits, ArrowShots.Count - shots);
            }

            // In the open.
            yield return StandInTheLine();
            int openHits = 0, openShots = 0;
            yield return Count(2f, (h, s) => { openHits = h; openShots = s; });

            // Behind a Backpack held loose in the line.
            yield return StandInTheLine();
            Vector3 between = trap.pos + trap.forward * (mouth + 1.5f);
            shield = PhotonNetwork.InstantiateItemRoom(backpack.gameObject.name, between, Quaternion.LookRotation(trap.forward)).gameObject;
            Rigidbody rig = shield.GetComponent<Rigidbody>();
            int shieldedHits = ArrowShots.Hits;
            int shieldedShots = ArrowShots.Count;
            for (float until = Time.time + 2.5f; Time.time < until;)
            {
                if (shield != null && rig != null)
                {
                    rig.useGravity = false;
                    rig.position = between;
                    rig.linearVelocity = Vector3.zero;
                    rig.angularVelocity = Vector3.zero;
                }
                yield return new WaitForFixedUpdate();
            }
            shieldedHits = ArrowShots.Hits - shieldedHits;
            shieldedShots = ArrowShots.Count - shieldedShots;
            Item item = shield != null ? shield.GetComponent<Item>() : null;
            Check("an arrow trap hits a scout in its line, and a Backpack lying in the line between shields them, as in the game (9)",
                openHits > 0 && shieldedHits == 0,
                $"in the open: {openHits} hits of {openShots} shots in 2 s; behind the Backpack: {shieldedHits} hits of {shieldedShots} shots in 2.5 s "
                + $"(the Backpack's layer {(item != null ? LayerMask.LayerToName(item.gameObject.layer) : "?")}, kinematic {(rig != null && rig.isKinematic)}); the trap's mouth {mouth:0.00} m past its muzzle");
            if (shield != null)
            {
                PhotonNetwork.Destroy(shield);
                shield = null;
            }

            // Inside a block round its mouth (1.2 m across, 1 m out of the opening): the game's trap shoots through it.
            GameObject platform = TestBlock("Platform", trap.pos + trap.forward * (mouth + 0.3f), new Vector3(1.2f, 1.2f, 1.6f), trap.forward);
            blocks.Add(platform);
            yield return new WaitForSeconds(1.2f);
            bool buried = GameFixes.Bore(trap).buried;
            GameFixes.LetBuriedTrapsShoot = true;
            yield return StandInTheLine();
            int throughHits = 0, throughShots = 0;
            yield return Count(2f, (h, s) => { throughHits = h; throughShots = s; });
            GameFixes.LetBuriedTrapsShoot = false;
            yield return StandInTheLine();
            int heldHits = 0, heldShots = 0;
            yield return Count(2.5f, (h, s) => { heldHits = h; heldShots = s; });
            bool lineHidden = trap.lineRenderer != null && !trap.lineRenderer.enabled;
            yield return ArrowLinePicture(trap, mouth, "arrow-trap-buried");
            blocks.Remove(platform);
            Destroy(platform);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(1.5f);
            bool lineBack = trap.lineRenderer != null && trap.lineRenderer.enabled && !GameFixes.Bore(trap).buried;
            Check("an arrow trap set inside a platform no longer shoots through it (the game's does): it holds its fire and hides its warning line until the platform is gone (every ascent)",
                buried && throughHits > 0 && heldShots == 0 && heldHits == 0 && lineHidden && lineBack,
                $"seen as inside {buried}; the game's way: {throughHits} hits of {throughShots} shots through the platform in 2 s; now: {heldHits} hits of {heldShots} shots in 2.5 s, "
                + $"line hidden {lineHidden}; the platform gone: line back {lineBack}");
        }
        finally
        {
            GameFixes.LetBuriedTrapsShoot = false;
            if (shield != null && shield.GetComponent<PhotonView>() != null)
            {
                PhotonNetwork.Destroy(shield);
            }
            foreach (GameObject block in blocks.Where(b => b != null))
            {
                Destroy(block);
            }
            local.refs.afflictions.RemoveAllThorns();
            local.photonView.RPC("WarpPlayerRPC", RpcTarget.All, home + Vector3.up, false);
        }
        yield return new WaitForSeconds(1.5f);
    }
}
