using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Photon.Pun;
using Unity.Profiling;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Zorro.Core;
using Object = UnityEngine.Object;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// The frame rate on the beach, in the Tropics and in the third segment (the Alpine or the Mesa), and where the mod's time
/// goes ("perf", only when asked). Each place:
/// the plain frame rate with the scout standing still (the frame rate cap and vsync off for it, in memory only), what is
/// in the scene, then every method of the mod timed (its patches, its MonoBehaviours, its coroutines, its per-frame
/// rules) and the frame rate with the mod's per-frame rules held off and with the mod's own objects hidden.
/// "perfvanilla" switches the mod off at the airport (its patches and per-frame rules), so the same climb is measured
/// as the game alone plays it.
/// </summary>
internal sealed partial class Runner
{
    private const float PerfSeconds = 15f;

    private bool PerfVanilla => only != null && only.Any(w => w.Equals("perfvanilla", StringComparison.OrdinalIgnoreCase));

    /// <summary>At the airport, before departure: the mod's patches and per-frame rules gone, for the game's own frame rate.</summary>
    private void ModOff()
    {
        Harmony.UnpatchID(Plugin.Guid);
        GameObject runtime = GameObject.Find("UltimateAscentRuntime");
        if (runtime != null)
        {
            Object.Destroy(runtime);
        }
        int left = Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m)?.Owners.Contains(Plugin.Guid) == true);
        Info("the mod switched off for the game's own frame rate (perfvanilla)", $"its per-frame rules {(runtime != null ? "removed" : "not found")}, methods still patched by it: {left}");
    }

    private IEnumerator PerfCheck()
    {
        int savedRate = Application.targetFrameRate;
        int savedSync = QualitySettings.vSyncCount;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        Info("perf: the frame rate cap and vsync off for the measurements", $"they were {savedRate} and {savedSync}; screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height}, quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}");
        try
        {
            yield return PerfPlace("the Shore");
            if (Singleton<MapHandler>.Instance.currentSegment != (int)Segment.Tropics)
            {
                MapHandler.JumpToSegment(Segment.Tropics);
                yield return new WaitForSeconds(12f);
            }
            yield return PerfPlace($"the {Map.CurrentPlace}");
            // The third segment (the Alpine or the Mesa), where the goats are.
            MapHandler.JumpToSegment(Segment.Alpine);
            yield return new WaitForSeconds(12f);
            yield return PerfPlace($"the {Map.CurrentPlace}");
        }
        finally
        {
            Application.targetFrameRate = savedRate;
            QualitySettings.vSyncCount = savedSync;
        }
    }

    private IEnumerator PerfPlace(string place)
    {
        Character local = Local;
        local.data.lookValues = new Vector2(0f, 0f);
        yield return new WaitForSecondsRealtime(5f);
        Info($"perf in {place}: the scene", Census());
        List<BerryVine> vines = Object.FindObjectsByType<BerryVine>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(v => UltimateAscent.Features.GloomFood.IsBananaVine(v)).ToList();
        Info($"perf in {place}: the map's banana vines", string.Join("; ", vines.GroupBy(v => Map.TryGetBiome(v, out Biome.BiomeType b) ? b.ToString() : "none")
            .Select(g => $"{g.Key}: {g.Count()} vines, chances {string.Join("/", g.Select(v => v.baseSpawnChance.ToString("0.##")).Distinct())}, bananas hanging {g.Sum(v => v.GetComponentsInChildren<Item>(true).Length)}")));
        yield return Measure($"{place}, {(PerfVanilla ? "the game alone" : "with the mod")}");
        if (PerfVanilla)
        {
            yield break;
        }

        PerfMeter.Install(typeof(Plugin).Assembly, out int timed, out int failedToTime);
        Info($"perf in {place}: methods of the mod timed", $"{timed} ({failedToTime} could not be)");
        yield return new WaitForSecondsRealtime(1f);
        PerfMeter.Clear();
        int frames0 = Time.frameCount;
        int unposed0 = Snake.PosesSkipped;
        yield return new WaitForSecondsRealtime(PerfSeconds);
        int frames = Mathf.Max(1, Time.frameCount - frames0);
        Info($"perf in {place}: the mod's time, ms a frame (inclusive; {frames} frames)", PerfMeter.Report(frames, 70));
        Info($"perf in {place}: goats", $"{Goats.All.Count(g => g != null)} out");
        List<int> tracks = Snakes.All.Where(s => s != null).Select(s => s.TrackPoints).OrderByDescending(n => n).ToList();
        Info($"perf in {place}: snakes", $"{Snakes.All.Count} out, {(Snake.PosesSkipped - unposed0) / (float)frames:0.#} of them unseen (not posed) in a frame on average; "
            + $"their tracks' points: {(tracks.Count > 0 ? $"most {tracks[0]}, mean {tracks.Average():0}, {string.Join("/", tracks.Take(8))}" : "none")}");
        PerfMeter.Uninstall();
        yield return new WaitForSecondsRealtime(1f);

        // Its per-frame rules held off for a while.
        GameObject runtime = GameObject.Find("UltimateAscentRuntime");
        Behaviour ticks = runtime != null ? runtime.GetComponent<Runtime>() : null;
        if (ticks != null)
        {
            ticks.enabled = false;
            yield return new WaitForSecondsRealtime(1f);
            yield return Measure($"{place}, the mod's per-frame rules held off", 10f);
            ticks.enabled = true;
        }

        // Its own objects hidden (anything carrying one of its components, but its HUD lines and the scout).
        List<GameObject> hidden = OwnObjects().Where(g => g.activeSelf).ToList();
        foreach (GameObject g in hidden)
        {
            g.SetActive(false);
        }
        yield return new WaitForSecondsRealtime(1f);
        yield return Measure($"{place}, the mod's {hidden.Count} own objects hidden ({string.Join(", ", hidden.GroupBy(g => Clean(g.name)).OrderByDescending(x => x.Count()).Take(8).Select(x => $"{x.Key} x{x.Count()}"))})", 10f);
        foreach (GameObject g in hidden)
        {
            if (g != null)
            {
                g.SetActive(true);
            }
        }
        yield return new WaitForSecondsRealtime(2f);
    }

    /// <summary>Scene objects carrying one of the mod's components, outermost first (not the HUD, the scouts or the mod's own runtime).</summary>
    private static List<GameObject> OwnObjects()
    {
        Assembly mod = typeof(Plugin).Assembly;
        HashSet<GameObject> found = new HashSet<GameObject>();
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (behaviour == null || behaviour.GetType().Assembly != mod || behaviour is Runtime || behaviour.GetComponentInParent<Canvas>() != null || behaviour.GetComponentInParent<Character>() != null)
            {
                continue;
            }
            found.Add(behaviour.gameObject);
        }
        // Outermost only, and never a big part of the map (a segment, the level) that happens to carry one.
        return found.Where(g => !found.Any(o => o != g && g.transform.IsChildOf(o.transform)) && g.GetComponentsInChildren<Transform>(true).Length <= 300 && g.GetComponentInChildren<Camera>(true) == null).ToList();
    }

    private static string Clean(string name) => name.Replace("(Clone)", "").Trim();

    private static readonly (ProfilerCategory category, string name)[] Counters =
    {
        (ProfilerCategory.Internal, "Main Thread"),
        (ProfilerCategory.Render, "Batches Count"),
        (ProfilerCategory.Render, "SetPass Calls Count"),
        (ProfilerCategory.Render, "Draw Calls Count"),
        (ProfilerCategory.Render, "Triangles Count"),
        (ProfilerCategory.Render, "Shadow Casters Count"),
        (ProfilerCategory.Render, "Visible Skinned Meshes Count"),
        (ProfilerCategory.Memory, "GC Allocated In Frame"),
        (ProfilerCategory.Physics, "Active Dynamic Bodies"),
        (ProfilerCategory.Physics, "Active Kinematic Bodies"),
    };

    /// <summary>The frame rate over a while, with the scout standing still: mean, median and slow frames, garbage collections, the engine's counters.</summary>
    private IEnumerator Measure(string what, float seconds = PerfSeconds)
    {
        List<ProfilerRecorder> recorders = Counters.Select(c => ProfilerRecorder.StartNew(c.category, c.name)).ToList();
        List<double>[] values = Counters.Select(_ => new List<double>()).ToArray();
        List<float> frames = new List<float>();
        int gc = GC.CollectionCount(0);
        float end = Time.realtimeSinceStartup + seconds;
        FrameTiming[] timing = new FrameTiming[1];
        List<double> cpu = new List<double>();
        List<double> gpu = new List<double>();
        List<double> mainCpu = new List<double>();
        yield return null;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime);
            for (int i = 0; i < recorders.Count; i++)
            {
                if (recorders[i].Valid)
                {
                    values[i].Add(recorders[i].LastValue);
                }
            }
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                cpu.Add(timing[0].cpuFrameTime);
                gpu.Add(timing[0].gpuFrameTime);
                mainCpu.Add(timing[0].cpuMainThreadFrameTime);
            }
        }
        int collections = GC.CollectionCount(0) - gc;
        foreach (ProfilerRecorder recorder in recorders)
        {
            recorder.Dispose();
        }
        List<float> sorted = frames.OrderBy(f => f).ToList();
        float total = frames.Sum();
        float Pct(float p) => sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1)] * 1000f;
        StringBuilder detail = new StringBuilder();
        detail.Append($"{frames.Count / Mathf.Max(total, 0.001f):F1} FPS ({frames.Count} frames in {total:F1} s): mean {total / Mathf.Max(1, frames.Count) * 1000f:F2} ms, median {Pct(0.5f):F2} ms, 95% {Pct(0.95f):F2} ms, 99% {Pct(0.99f):F2} ms, slowest {Pct(1f):F1} ms; {collections} garbage collection(s)");
        for (int i = 0; i < Counters.Length; i++)
        {
            if (values[i].Count > 0)
            {
                double mean = values[i].Average();
                string shown = Counters[i].name == "Main Thread" ? $"{mean / 1e6:F2} ms" : Counters[i].name.StartsWith("GC") ? $"{mean / 1024:F1} KB" : $"{mean:F0}";
                detail.Append($"; {Counters[i].name} {shown}");
            }
        }
        if (cpu.Count > 0 && cpu.Any(c => c > 0))
        {
            detail.Append($"; frame timing: CPU {cpu.Average():F2} ms (main thread {mainCpu.Average():F2} ms), GPU {gpu.Average():F2} ms");
        }
        Info("perf: " + what, detail.ToString());
    }

    /// <summary>What is in the scene: objects, items by name, renderers, particles, bodies, animators, lights, sounds, the mod's components.</summary>
    private static string Census()
    {
        Assembly mod = typeof(Plugin).Assembly;
        GameObject[] objects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        Item[] items = Object.FindObjectsByType<Item>(FindObjectsSortMode.None);
        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        ParticleSystem[] particles = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
        Rigidbody[] bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        Animator[] animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        AudioSource[] sounds = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        PhotonView[] views = Object.FindObjectsByType<PhotonView>(FindObjectsSortMode.None);
        Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        string Top<T>(IEnumerable<T> list, Func<T, string> key, int n) => string.Join(", ", list.GroupBy(key).OrderByDescending(g => g.Count()).Take(n).Select(g => $"{g.Key} x{g.Count()}"));
        return $"{objects.Length} active objects; {items.Length} items ({Top(items, i => Clean(i.gameObject.name), 15)}); "
            + $"{renderers.Count(r => r.enabled)} renderers on ({renderers.Count(r => r.enabled && r.isVisible)} in view, {renderers.Count(r => r is SkinnedMeshRenderer && r.enabled)} skinned); "
            + $"{particles.Count(p => p.isPlaying)} particle systems playing ({particles.Sum(p => p.particleCount)} particles; most: {Top(particles.Where(p => p.isPlaying), p => Clean(p.transform.root.name) + "/" + p.name, 6)}); "
            + $"{bodies.Count(b => !b.isKinematic && !b.IsSleeping())} bodies awake of {bodies.Length}; {animators.Count(a => a.isActiveAndEnabled)} animators; "
            + $"{lights.Count(l => l.isActiveAndEnabled)} lights ({lights.Count(l => l.isActiveAndEnabled && l.shadows != LightShadows.None)} with shadows); {sounds.Count(s => s.isPlaying)} sounds playing; "
            + $"{views.Length} photon views; {colliders.Length} colliders; {behaviours.Length} behaviours ({Top(behaviours, b => b.GetType().Name, 12)}); "
            + $"the mod's: {Top(behaviours.Where(b => b.GetType().Assembly == mod), b => b.GetType().Name, 15)}";
    }
}

/// <summary>Times methods: a prefix and postfix of its own on each, adding up the time and the calls.</summary>
internal static class PerfMeter
{
    private const string Id = "UltimateAscent.SmokeTest.Perf";

    private sealed class Entry
    {
        internal string name;
        internal long ticks;
        internal long calls;
    }

    private static readonly Dictionary<MethodBase, Entry> entries = new Dictionary<MethodBase, Entry>();
    private static Harmony harmony;

    private static readonly string[] Messages = { "Update", "LateUpdate", "FixedUpdate", "OnGUI", "OnTriggerStay", "OnCollisionStay", "OnRenderObject", "OnWillRenderObject", "OnAnimatorMove", "OnAnimatorIK" };

    internal static void Install(Assembly mod, out int timed, out int failed)
    {
        harmony ??= new Harmony(Id);
        entries.Clear();
        Dictionary<MethodBase, string> targets = new Dictionary<MethodBase, string>();
        // The mod's patches: their prefixes, postfixes and finalizers; methods it transpiles are timed whole.
        foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToList())
        {
            Patches info = Harmony.GetPatchInfo(original);
            if (info == null)
            {
                continue;
            }
            string of = $"{original.DeclaringType?.Name}.{original.Name}";
            foreach ((IEnumerable<Patch> list, string kind) in new[] { (info.Prefixes, "prefix"), (info.Postfixes, "postfix"), (info.Finalizers, "finalizer") })
            {
                foreach (Patch patch in list.Where(p => p.owner == Plugin.Guid))
                {
                    targets[patch.PatchMethod] = $"{Short(patch.PatchMethod.DeclaringType)}.{patch.PatchMethod.Name} ({kind} of {of})";
                }
            }
            if (info.Transpilers.Any(p => p.owner == Plugin.Guid))
            {
                targets[original] = $"{of} whole (the mod transpiles it)";
            }
        }
        foreach (Type type in AccessTools.GetTypesFromAssembly(mod))
        {
            if (typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                foreach (string message in Messages)
                {
                    MethodInfo method = type.GetMethod(message, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (method != null && !method.IsAbstract && !method.ContainsGenericParameters)
                    {
                        targets[method] = $"{Short(type)}.{message}";
                    }
                }
            }
            if (type.Name.Contains("<") && typeof(IEnumerator).IsAssignableFrom(type))
            {
                MethodInfo next = type.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (next != null && !type.ContainsGenericParameters)
                {
                    targets[next] = $"{Short(type)} (coroutine)";
                }
            }
        }
        // Every method of the new creatures, for where their time goes.
        foreach (Type creature in new[] { typeof(Snake), typeof(BeachCrab), typeof(CrabLook), typeof(Goat) })
        {
            foreach (MethodInfo method in creature.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!method.IsAbstract && !method.ContainsGenericParameters && !method.Name.StartsWith("get_") && !targets.ContainsKey(method))
                {
                    targets[method] = $"{Short(creature)}.{method.Name}";
                }
            }
        }
        // The per-frame rules.
        FieldInfo ticksField = typeof(Runtime).GetField("Ticks", BindingFlags.Static | BindingFlags.NonPublic);
        if (ticksField?.GetValue(null) is Action[] ticks)
        {
            foreach (Action tick in ticks)
            {
                targets[tick.Method] = $"{Short(tick.Method.DeclaringType)}.{tick.Method.Name} (per-frame rule)";
            }
        }
        targets[AccessTools.Method(typeof(Runtime), "Update")] = "Runtime.Update (all the per-frame rules)";
        HarmonyMethod before = new HarmonyMethod(typeof(PerfMeter), nameof(Before)) { priority = Priority.First };
        HarmonyMethod after = new HarmonyMethod(typeof(PerfMeter), nameof(After)) { priority = Priority.Last };
        timed = 0;
        failed = 0;
        foreach (KeyValuePair<MethodBase, string> target in targets)
        {
            if (target.Key.ContainsGenericParameters || target.Key.GetMethodBody() == null)
            {
                failed++;
                continue;
            }
            entries[target.Key] = new Entry { name = target.Value };
            try
            {
                harmony.Patch(target.Key, prefix: before, postfix: after);
                timed++;
            }
            catch (Exception)
            {
                entries.Remove(target.Key);
                failed++;
            }
        }
    }

    internal static void Uninstall()
    {
        harmony?.UnpatchSelf();
        entries.Clear();
    }

    internal static void Clear()
    {
        foreach (Entry entry in entries.Values)
        {
            entry.ticks = 0;
            entry.calls = 0;
        }
    }

    private static string Short(Type type)
    {
        if (type == null)
        {
            return "?";
        }
        string name = type.FullName ?? type.Name;
        return name.Replace("UltimateAscent.Features.", "").Replace("UltimateAscent.Core.", "").Replace("UltimateAscent.Compat.", "").Replace("UltimateAscent.", "").Replace('+', '.');
    }

    private static void Before(out long __state)
    {
        __state = Stopwatch.GetTimestamp();
    }

    private static void After(long __state, MethodBase __originalMethod)
    {
        if (__originalMethod != null && entries.TryGetValue(__originalMethod, out Entry entry))
        {
            entry.ticks += Stopwatch.GetTimestamp() - __state;
            entry.calls++;
        }
    }

    internal static string Report(int frames, int top)
    {
        double toMs = 1000.0 / Stopwatch.Frequency;
        List<Entry> busy = entries.Values.Where(e => e.calls > 0).OrderByDescending(e => e.ticks).ToList();
        return $"{busy.Count} methods ran; " + string.Join("; ", busy.Take(top).Select(e => $"{e.name} {e.ticks * toMs / frames:F3} ms ({e.calls / (double)frames:0.#} calls)"));
    }
}
