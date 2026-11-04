using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Status = CharacterAfflictions.STATUSTYPE;

namespace UltimateAscent.SmokeTest;

/// <summary>What the rising hazards carry (the Kiln's lava, the Citadel's gloom, the Nadir's ghosts), read from the map.</summary>
internal sealed partial class Runner
{
    /// <summary>
    /// Every rising hazard on this map (sleeping ones too): its timing, and every script on what rises and under it, with
    /// its settings (numbers, switches and kinds; a status field's extra statuses too).
    /// </summary>
    private IEnumerator RisingProbe()
    {
        LavaRising[] all = Resources.FindObjectsOfTypeAll<LavaRising>().Where(r => r != null && r.gameObject.scene.IsValid()).ToArray();
        Info("rising hazards", $"{all.Length} on this map");
        foreach (LavaRising rising in all)
        {
            Info($"rising: {HierarchyPath(rising.transform)}", $"{rising.risingFieldType}, segment {rising.requiredSegment}, waits {rising.initialWaitTime} s, rises in {rising.travelTime} s, waits for an event {rising.waitForEvent}, "
                + $"active {rising.gameObject.activeInHierarchy}; what rises: {(rising.lava != null ? HierarchyPath(rising.lava.transform) : "nothing")}");
            foreach (Transform root in new[] { rising.transform, rising.lava != null ? rising.lava.transform : null }.Where(t => t != null).Distinct())
            {
                foreach (MonoBehaviour script in root.GetComponentsInChildren<MonoBehaviour>(true).Where(s => s != null && !(s is LavaRising)))
                {
                    Info($"  {script.GetType().FullName} on {HierarchyPath(script.transform)}", Settings(script));
                }
            }
        }
        yield break;
    }

    /// <summary>
    /// The Citadel's rising gloom and the Nadir's rising ghosts (18), the game's own on this map (it needs the Gloom: -Biome
    /// TMS or TAS). Each is held where it is and moved, for the check, to hang over the scout (its top a little above their
    /// head; the Nadir's kill plane switched off meanwhile), and what it gives is measured with ascent 18's rule on and
    /// off; then it goes back where it was. The gloom: 5 drowsiness a second (the game: 0.9, 1.17 with ascent 14's boost);
    /// the ghosts: 10 petrification a tick, one a second (the game: 5). The Nadir is one way: its half only runs with -Only.
    /// </summary>
    private IEnumerator RisingHazards()
    {
        if (!Rules.Enabled(Lvl.ExtremeWeather))
        {
            Info("the rising hazards' checks are skipped", $"testing ascent {target}");
            yield break;
        }
        Character local = Local;
        BepInEx.Configuration.ConfigEntry<bool> rule = UltimateAscent.Plugin.AscentSwitches[Lvl.ExtremeWeather];
        bool saved = rule.Value;
        LavaRising held = null;
        Vector3 heldAt = Vector3.zero;
        GameObject killPlane = null;
        // What rises, moved over the scout (its rising held meanwhile).
        void Over(LavaRising rising, float aboveHead)
        {
            held = rising;
            heldAt = rising.lava.position;
            rising.enabled = false;
            Vector3 top = new Vector3(heldAt.x, local.Head.y + aboveHead, heldAt.z);
            // Centred over the scout across, so they are well inside it.
            top.x = local.Head.x;
            top.z = local.Head.z;
            rising.lava.position = top;
            rising.lava.transform.position = top;
            Physics.SyncTransforms();
        }
        void Back()
        {
            if (held != null)
            {
                held.lava.position = heldAt;
                held.lava.transform.position = heldAt;
                held.enabled = true;
                held = null;
            }
            if (killPlane != null)
            {
                killPlane.SetActive(true);
                killPlane = null;
            }
        }
        try
        {
            // The Citadel, somewhere no lit bell tower, campfire, stove or lantern keeps the gloom off.
            MapHandler.JumpToSegment(Segment.TheKiln);
            yield return new WaitForSeconds(8f);
            LavaRising gloom = FindObjectsByType<LavaRising>(FindObjectsSortMode.None).FirstOrDefault(r => r.risingFieldType == LavaRising.RisingFieldType.Gloom && r.lava != null);
            Peak.StatusFieldGloom gloomField = gloom != null ? gloom.lava.GetComponent<Peak.StatusFieldGloom>() : null;
            if (gloomField == null)
            {
                Info("the Citadel's gloom checks are skipped", $"this map has no Citadel (the {Map.CurrentPlace})");
            }
            else
            {
                Vector3? open = null;
                foreach (float distance in new[] { 0f, 15f, 30f, 45f, 60f })
                {
                    for (int i = 0; i < 12 && open == null; i++)
                    {
                        Vector3? spot = FlatOpen(local.Center + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * distance);
                        if (spot != null && !(Peak.GloomSafeZone.TryGetClosestSafeZone(spot.Value + Vector3.up * 1.6f, out Peak.GloomSafeZone safe, out _, forSafety: true) && safe.PointInProtectionRadius(spot.Value + Vector3.up * 1.6f)))
                        {
                            open = spot;
                        }
                    }
                }
                if (open != null)
                {
                    local.photonView.RPC("WarpPlayerRPC", Photon.Pun.RpcTarget.All, open.Value + Vector3.up * 1.2f, false);
                    yield return new WaitForSeconds(1.5f);
                }
                Over(gloom, 4f);
                foreach (bool on in new[] { true, false })
                {
                    rule.Value = on;
                    HostOptions.Invalidate();
                    Protect(false);
                    Afflictions.SetStatus(Status.Drowsy, 0f);
                    yield return new WaitForSeconds(1f);
                    bool inside = gloomField.PointInField(local.Head);
                    float before = Afflictions.GetCurrentStatus(Status.Drowsy);
                    float from = Time.time;
                    if (on)
                    {
                        yield return new WaitForSeconds(1.5f);
                        Screenshot("citadel-gloom");
                        yield return new WaitForSeconds(2.5f);
                    }
                    else
                    {
                        yield return new WaitForSeconds(4f);
                    }
                    float gained = Afflictions.GetCurrentStatus(Status.Drowsy) - before;
                    float seconds = Time.time - from;
                    Afflictions.SetStatus(Status.Drowsy, 0f);
                    Protect(true);
                    float rate = on ? A18_ExtremeWeather.CitadelGloomDrowsiness : 0.009f * A15_StrongerAilments.Factor(Status.Drowsy);
                    Check(on ? "the Citadel's rising gloom makes a scout 5 drowsy a second (18)" : "with ascent 18's rule off the Citadel's rising gloom is the game's 0.9 a second (ascent 14's boost included)",
                        inside && Mathf.Abs(gained - rate * seconds) <= 0.026f,
                        $"{gained * 100f:0.#} drowsiness in {seconds:0.0} s ({rate * 100f * seconds:0.#} expected; the bar fills in steps of 2.5); the head in the gloom {inside}, place {Map.CurrentPlace}");
                    yield return new WaitForSeconds(0.5f);
                }
                Back();
            }

            // The Nadir: its ghosts' field (petrifying, a tick a second), its kill plane off while it hangs over the scout.
            rule.Value = saved;
            HostOptions.Invalidate();
            if (only == null)
            {
                Info("the Nadir's ghost checks are skipped", "the jump to the Nadir ends the other checks; run with -Only risinghazards");
                yield break;
            }
            MapHandler.JumpToSegment(Segment.Void);
            yield return new WaitForSeconds(8f);
            LavaRising ghosts = FindObjectsByType<LavaRising>(FindObjectsSortMode.None).FirstOrDefault(r => r.risingFieldType == LavaRising.RisingFieldType.VoidGhosts && r.lava != null);
            Peak.StatusFieldBounds ghostField = ghosts != null ? ghosts.lava.GetComponent<Peak.StatusFieldBounds>() : null;
            if (ghostField == null)
            {
                Info("the Nadir's ghost checks are skipped", $"no rising ghosts found (in the {Map.CurrentPlace})");
                yield break;
            }
            Peak.KillPlane plane = ghosts.lava.GetComponentInChildren<Peak.KillPlane>(true);
            if (plane != null)
            {
                killPlane = plane.gameObject;
                killPlane.SetActive(false);
            }
            Over(ghosts, 2f);
            foreach (bool on in new[] { true, false })
            {
                rule.Value = on;
                HostOptions.Invalidate();
                Protect(false);
                local.data.SetPetrify(0);
                List<int> ticks = new List<int>();
                int last = 0;
                bool pictured = false;
                bool inside = ghostField.PointInField(local.Head);
                for (float until = Time.time + 3.6f; Time.time < until;)
                {
                    int now = local.data.petrifyAmount;
                    if (now > last)
                    {
                        ticks.Add(now - last);
                    }
                    last = now;
                    if (on && !pictured && ticks.Count == 2)
                    {
                        Screenshot("nadir-ghosts");
                        pictured = true;
                    }
                    yield return null;
                }
                local.data.SetPetrify(0);
                Protect(true);
                int expected = on ? Mathf.RoundToInt(NadirRules.GhostPetrification) : 5;
                Check(on ? "the Nadir's rising ghosts petrify a scout 10 a second, a tick a second (18)" : "with ascent 18's rule off the Nadir's rising ghosts petrify the game's 5 a second",
                    inside && ticks.Count >= 3 && ticks.Count <= 4 && ticks.All(t => t == expected),
                    $"ticks of {string.Join(", ", ticks)} in 3.6 s ({expected} each expected); the head in the ghosts {inside}, place {Map.CurrentPlace}, kill plane {(plane != null ? "held off" : "none")}");
                yield return new WaitForSeconds(0.5f);
            }
        }
        finally
        {
            Back();
            rule.Value = saved;
            HostOptions.Invalidate();
            local.data.SetPetrify(0);
            Afflictions.SetStatus(Status.Drowsy, 0f);
            Protect(true);
        }
    }

    /// <summary>Every lava on this map (in the Kiln, after a jump there): where it is, what holds it, what it covers.</summary>
    private IEnumerator LavaProbe()
    {
        if (!Map.InVolcanoKiln)
        {
            MapHandler.JumpToSegment(Segment.TheKiln);
            yield return new WaitForSeconds(8f);
        }
        foreach (Lava lava in Resources.FindObjectsOfTypeAll<Lava>().Where(l => l != null && l.gameObject.scene.IsValid()))
        {
            Transform parent = lava.transform.parent;
            Renderer plane = lava.GetComponentInChildren<MeshRenderer>(true);
            Info($"lava: {HierarchyPath(lava.transform)}", $"enabled {lava.enabled}, active {lava.gameObject.activeInHierarchy}, at {lava.transform.position:F1} (local {lava.transform.localPosition:F1}); "
                + $"parent at {(parent != null ? parent.position.ToString("F1") : "none")}, rotation {(parent != null ? parent.eulerAngles.ToString("F0") : "-")}, scale {(parent != null ? parent.lossyScale.ToString("F2") : "-")}; "
                + $"animator {lava.GetComponent<Animator>() != null}; covers {(plane != null ? plane.bounds.min.ToString("F0") + " to " + plane.bounds.max.ToString("F0") : "nothing")}");
        }
    }

    /// <summary>A script's own settings: its numbers, switches and kinds (and the statuses in its lists).</summary>
    private static string Settings(object script)
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var parts = new System.Collections.Generic.List<string>();
        for (Type type = script.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(Fields | BindingFlags.DeclaredOnly))
            {
                object value = field.GetValue(script);
                if (value == null)
                {
                    continue;
                }
                if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(Vector3))
                {
                    parts.Add($"{field.Name} {value}");
                }
                else if (value is System.Collections.IList list && field.FieldType.IsGenericType && list.Count > 0 && list.Count <= 8)
                {
                    parts.Add($"{field.Name} [{string.Join("; ", list.Cast<object>().Select(item => item == null ? "none" : item.GetType().IsPrimitive || item is UnityEngine.Object ? item.ToString() : Settings(item)))}]");
                }
            }
        }
        return string.Join(", ", parts);
    }

    private static string HierarchyPath(Transform transform)
    {
        string path = transform.name;
        for (Transform up = transform.parent; up != null; up = up.parent)
        {
            path = up.name + "/" + path;
        }
        return path;
    }
}
