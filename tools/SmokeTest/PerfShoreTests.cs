using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Unity.Profiling;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// What each part of the mod costs on the Shore ("perfshore", only when asked; run it on its own, it takes the mod's
/// patches off at the end). The clock is held at 10:00 (the sun and the shadows move otherwise, and the frame time with
/// them), the scout stands still, the frame rate cap and vsync are off. Each part (the crabs, the Stones and Sticks lying
/// about, the Lookers, the per-frame rules, then all of them) is taken away and put back four times over, 3 s each way,
/// so a slow drift counts on both sides; then all of them away and the patches off. With "perfvanilla" (the mod off at
/// the airport) only the game's own frame time there, held at the same hour.
/// </summary>
internal sealed partial class Runner
{
    private const float HeldHour = 10f;

    private IEnumerator PerfShoreStages()
    {
        int savedRate = Application.targetFrameRate;
        int savedSync = QualitySettings.vSyncCount;
        DayNightManager clock = DayNightManager.instance;
        float savedLength = clock != null ? clock.dayLengthInMinutes : 0f;
        float savedTime = clock != null ? clock.timeOfDay : 0f;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        if (clock != null)
        {
            clock.dayLengthInMinutes = 1e7f;
            clock.timeOfDay = HeldHour;
        }
        Character local = Local;
        local.data.lookValues = new Vector2(0f, 0f);
        string place = $"the {Map.CurrentPlace}";
        try
        {
            yield return new WaitForSecondsRealtime(5f);
            Info($"perfshore: {place}, the scene (clock held at {HeldHour:0}:00)", Census());
            if (PerfVanilla)
            {
                yield return Measure($"{place} at {HeldHour:0}:00, the game alone", StageSeconds);
                yield return Measure($"{place} at {HeldHour:0}:00, the game alone (again)", StageSeconds);
                yield break;
            }
            yield return Measure($"{place} at {HeldHour:0}:00, with the mod", StageSeconds);

            List<GameObject> crabs = Object.FindObjectsByType<BeachCrab>(FindObjectsSortMode.None).Select(c => c.gameObject).Distinct().ToList();
            List<GameObject> lying = Object.FindObjectsByType<Item>(FindObjectsSortMode.None)
                .Where(i => i.itemState == ItemState.Ground && (Clean(i.gameObject.name) == StoneItem.PrefabName || Clean(i.gameObject.name) == StickItem.PrefabName))
                .Select(i => i.gameObject).ToList();
            List<GameObject> lookers = Object.FindObjectsByType<Looker>(FindObjectsSortMode.None).Select(l => l.gameObject).Distinct().ToList();
            GameObject runtime = GameObject.Find("UltimateAscentRuntime");
            Behaviour ticks = runtime != null ? runtime.GetComponent<Runtime>() : null;
            Info($"perfshore: {place}, the Lookers shown", $"{lookers.Count}: " + string.Join("; ", lookers.Select(l =>
                $"{Vector3.Distance(l.transform.position, local.Center):0} m away, {l.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} skinned meshes ({l.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s => s.sharedMesh != null ? s.sharedMesh.triangles.Length / 3 : 0)} triangles), "
                + $"animator culling {(l.GetComponent<Animator>() != null ? l.GetComponent<Animator>().cullingMode.ToString() : "none")}, seen {l.GetComponentsInChildren<Renderer>().Any(r => r.isVisible)}")));
            Info($"perfshore: {place}, the Stones and Sticks lying about", $"{lying.Count}: materials {lying.SelectMany(g => g.GetComponentsInChildren<Renderer>()).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count()} different among "
                + $"{lying.SelectMany(g => g.GetComponentsInChildren<Renderer>()).Count()} renderers; triangles each {string.Join("/", lying.Select(g => g.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh != null ? f.sharedMesh.triangles.Length / 3 : 0)).Distinct())}");

            if (UltimateAscent.Compat.ItemStats.Present)
            {
                int skipped = UltimateAscent.Compat.ItemStats.Skipped;
                int rebuilt = UltimateAscent.Compat.ItemStats.Rebuilt;
                yield return Toggled(place, "Item Stats rebuilding its numbers on every slot every frame (as it does without the mod's skip)", on => UltimateAscent.Compat.ItemStats.SkipUnchanged = !on);
                UltimateAscent.Compat.ItemStats.SkipUnchanged = true;
                Info($"perfshore: {place}, Item Stats' slot rebuilds meanwhile", $"{UltimateAscent.Compat.ItemStats.Rebuilt - rebuilt} done, {UltimateAscent.Compat.ItemStats.Skipped - skipped} skipped (nothing changed)");
            }
            yield return Toggled(place, $"its {crabs.Count} crabs", on => Show(crabs, on));
            yield return Toggled(place, $"its {lying.Count} Stones and Sticks lying about", on => Show(lying, on));
            yield return Toggled(place, $"the {lookers.Count} Lookers shown (ascent 17; the game shows one in 20 climbs)", on => Show(lookers, on));
            if (ticks != null)
            {
                yield return Toggled(place, "its per-frame rules", on => ticks.enabled = on);
            }
            yield return Toggled(place, "all of these together", on =>
            {
                Show(crabs, on);
                Show(lying, on);
                Show(lookers, on);
                if (ticks != null)
                {
                    ticks.enabled = on;
                }
            });

            // All of them away, then the patches off as well (for good).
            Show(crabs, false);
            Show(lying, false);
            Show(lookers, false);
            if (ticks != null)
            {
                ticks.enabled = false;
            }
            yield return new WaitForSecondsRealtime(1f);
            yield return Measure($"{place} at {HeldHour:0}:00, all of these away", StageSeconds);
            Harmony.UnpatchID(Plugin.Guid);
            int left = Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m)?.Owners.Contains(Plugin.Guid) == true);
            yield return new WaitForSecondsRealtime(2f);
            yield return Measure($"{place} at {HeldHour:0}:00, all of these away and its patches off ({left} methods still patched by it)", StageSeconds);
            Info($"perfshore: {place}, the scene at the end", Census());
        }
        finally
        {
            Application.targetFrameRate = savedRate;
            QualitySettings.vSyncCount = savedSync;
            if (clock != null)
            {
                clock.dayLengthInMinutes = savedLength;
                clock.timeOfDay = savedTime;
            }
        }
    }

    private static void Show(List<GameObject> objects, bool on)
    {
        foreach (GameObject g in objects)
        {
            if (g != null && g.activeSelf != on)
            {
                g.SetActive(on);
            }
        }
    }

    /// <summary>A part taken away and put back four times over, 3 s each way (the first 0.6 s after each change left out): its cost a frame.</summary>
    private IEnumerator Toggled(string place, string part, Action<bool> set)
    {
        List<float> on = new List<float>();
        List<float> off = new List<float>();
        string[] names = { "SetPass Calls Count", "Batches Count", "Visible Skinned Meshes Count", "Shadow Casters Count" };
        List<ProfilerRecorder> recorders = names.Select(n => ProfilerRecorder.StartNew(ProfilerCategory.Render, n)).ToList();
        List<double>[] onCounts = names.Select(_ => new List<double>()).ToArray();
        List<double>[] offCounts = names.Select(_ => new List<double>()).ToArray();
        try
        {
            for (int cycle = 0; cycle < 4; cycle++)
            {
                foreach (bool shown in new[] { false, true })
                {
                    set(shown);
                    yield return new WaitForSecondsRealtime(0.6f);
                    float end = Time.realtimeSinceStartup + 3f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        yield return null;
                        (shown ? on : off).Add(Time.unscaledDeltaTime);
                        for (int i = 0; i < recorders.Count; i++)
                        {
                            if (recorders[i].Valid)
                            {
                                (shown ? onCounts : offCounts)[i].Add(recorders[i].LastValue);
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            set(true);
            foreach (ProfilerRecorder recorder in recorders)
            {
                recorder.Dispose();
            }
        }
        float Mean(List<float> f) => f.Count == 0 ? 0f : f.Sum() / f.Count * 1000f;
        float Median(List<float> f) => f.Count == 0 ? 0f : f.OrderBy(x => x).ElementAt(f.Count / 2) * 1000f;
        float onMs = Mean(on);
        float offMs = Mean(off);
        string counts = string.Join(", ", names.Select((n, i) => $"{n.Replace(" Count", "")} {(onCounts[i].Count > 0 ? onCounts[i].Average() : 0):0} / {(offCounts[i].Count > 0 ? offCounts[i].Average() : 0):0}"));
        Info($"perfshore: {place}, {part}", $"costs {onMs - offMs:F2} ms a frame (median {Median(on) - Median(off):F2} ms): with it {onMs:F2} ms = {1000f / Mathf.Max(onMs, 0.01f):F1} FPS, without {offMs:F2} ms = {1000f / Mathf.Max(offMs, 0.01f):F1} FPS; "
            + $"{on.Count}/{off.Count} frames; with / without: {counts}");
    }

    private const float StageSeconds = 12f;
}
