using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UltimateAscent.Core;
using UltimateAscent.Features;
using UnityEngine;
using Tfa = UltimateAscent.Compat.Tfa;

namespace UltimateAscent.SmokeTest;

/// <summary>
/// The Builder maps pack (smoketest.ps1 -WithTfa -BuilderMaps &lt;folder&gt;): its maps in the map picker after the original
/// ones, and with "-Only buildermap" a climb on the first of them, which TFA lays out from the file as FINAL ASCENT on the
/// test's ascent: the map's own biomes, the gem statue at the Peak, and the Nadir after it.
/// </summary>
internal sealed partial class Runner
{
    private BuilderMaps.Map builderForClimb;

    /// <summary>The picker on the pass: after the original maps (by number) come the pack's, "Builder: MAP 1" on, and round again.</summary>
    private void BuilderMapPickerChecks(BoardingPass pass)
    {
        if (BuilderMaps.All.Count == 0)
        {
            Info("the Builder maps' checks are skipped", "no Builder maps installed (smoketest.ps1 -WithTfa -BuilderMaps <folder>)");
            return;
        }
        Check("the Builder maps are offered with True Final Ascent installed", BuilderMaps.Offered && MapPicker.Total == MapPicker.Count + BuilderMaps.All.Count,
            $"{BuilderMaps.All.Count} maps found ({string.Join(", ", BuilderMaps.All.Take(5).Select(m => $"{m.Number} {m.Code}"))}...); TFA can start them {Compat.TfaMaps.Ready}; picker total {MapPicker.Total}");
        AllBuilderMapsCurrent();
        List<int> order = MapPicker.Order();
        int lastOriginal = order.Where(m => m < MapPicker.Count).Last();
        MapPicker.Picked = lastOriginal;
        MapPicker.Step(1);
        string afterOriginals = Plain(MapPicker.Label.text);
        MapPicker.Picked = order.Last();
        pass.UpdateAscent();
        string lastBuilder = Plain(MapPicker.Label.text);
        MapPicker.Step(1);
        string wrapped = Plain(MapPicker.Label.text);
        int first = order.First();
        BuilderMaps.Map one = BuilderMaps.All[0];
        Check("after the original maps the arrows go on to the Builder maps, 'Builder: MAP 1' on, and from the last of them round to the first original map",
            afterOriginals.StartsWith("Builder: MAP " + one.Number, StringComparison.Ordinal) && lastBuilder.StartsWith("Builder: MAP " + BuilderMaps.All.Last().Number, StringComparison.Ordinal)
                && wrapped == Plain(MapPicker.Name(first)),
            $"right of {MapPicker.Name(lastOriginal)}: '{afterOriginals}'; the last: '{lastBuilder}'; right of it: '{wrapped}'");
        MapPicker.Picked = MapPicker.Count;
        ModSave.Reload();
        Check("a Builder map picked is kept in the mod's save by its number", MapPicker.Picked == MapPicker.Count && ModSave.Get("map.pick") == "b" + one.Number,
            $"saved '{ModSave.Get("map.pick")}', read back {MapPicker.Name(MapPicker.Picked)}");
        if (only != null && only.Any(w => w.Equals("buildermap", StringComparison.OrdinalIgnoreCase)))
        {
            builderForClimb = one;
            pass.UpdateAscent();
            Info("map for the climb", $"{MapPicker.Name(MapPicker.Picked)}: {System.IO.Path.GetFileName(one.Path)} ({one.Code})");
            Screenshot("map-picker-builder");
        }
        else
        {
            MapPicker.Picked = MapPicker.Today;
            pass.UpdateAscent();
        }
    }

    /// <summary>
    /// Every map of the pack as the installed True Final Ascent judges it, with its own code: its header reads (EntryOf),
    /// its body matches its print (else TFA would delete it as damaged), it is not outdated (its layout stamp is this
    /// TFA's for its chain, else TFA builds a new island instead) and it needs no add-on biomes (else TFA rebuilds too).
    /// </summary>
    private void AllBuilderMapsCurrent()
    {
        Type cache = AccessTools.TypeByName("TrueFinalAscent.FinalAscentMapCache");
        var entryOf = AccessTools.Method(cache, "EntryOf");
        var outdated = AccessTools.Method(cache, "Outdated");
        var head = AccessTools.Method(cache, "Head");
        var printOf = AccessTools.Method(cache, "PrintOf");
        var layoutFor = AccessTools.Method(cache, "LayoutFor");
        string version = BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Where(p => p.Metadata.Name.IndexOf("Final Ascent", StringComparison.OrdinalIgnoreCase) >= 0 || p.Metadata.GUID.IndexOf("finalascent", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(p => p.Metadata.Version.ToString()).FirstOrDefault() ?? "?";
        if (entryOf == null || outdated == null || head == null || printOf == null)
        {
            Check("every Builder map passes the installed True Final Ascent's own checks", false, $"TFA {version}: its map checks were not found (EntryOf {entryOf != null}, Outdated {outdated != null}, Head {head != null}, PrintOf {printOf != null})");
            return;
        }
        List<string> bad = new List<string>();
        Dictionary<string, int> layouts = new Dictionary<string, int>();
        foreach (BuilderMaps.Map map in BuilderMaps.All)
        {
            object entry = entryOf.Invoke(null, new object[] { map.Path });
            if (entry == null)
            {
                bad.Add($"{map.Number}: header not read");
                continue;
            }
            string layout = Traverse.Create(entry).Field("Layout").GetValue<string>();
            string biomes = Traverse.Create(entry).Field("Biomes").GetValue<string>();
            string print = Traverse.Create(entry).Field("Print").GetValue<string>();
            string variants = Traverse.Create(entry).Field("Variants").GetValue<string>();
            string wanted = layoutFor != null ? layoutFor.Invoke(null, new object[] { variants }) as string : "?";
            layouts[$"{layout} (TFA wants {wanted})"] = layouts.TryGetValue($"{layout} (TFA wants {wanted})", out int n) ? n + 1 : 1;
            byte[] all = System.IO.File.ReadAllBytes(map.Path);
            object[] args = { all, 0 };
            if (head.Invoke(null, args) == null)
            {
                bad.Add($"{map.Number}: body not found");
                continue;
            }
            int body = (int)args[1];
            byte[] packed = new byte[all.Length - body];
            Buffer.BlockCopy(all, body, packed, 0, packed.Length);
            if ((string)printOf.Invoke(null, new object[] { packed }) != print)
            {
                bad.Add($"{map.Number}: print differs (TFA would delete it)");
            }
            if ((bool)outdated.Invoke(null, new[] { entry }))
            {
                bad.Add($"{map.Number}: outdated, layout {layout} (TFA wants {wanted})");
            }
            if (!string.IsNullOrEmpty(biomes))
            {
                bad.Add($"{map.Number}: needs add-on biomes {biomes}");
            }
        }
        Check($"every Builder map passes the installed True Final Ascent's own checks (TFA {version})", bad.Count == 0 && BuilderMaps.All.Count > 0,
            bad.Count > 0 ? $"{bad.Count} of {BuilderMaps.All.Count}: " + string.Join("; ", bad.Take(8)) : $"{BuilderMaps.All.Count} maps read, prints match, none outdated; layouts: {string.Join(", ", layouts.Select(l => $"{l.Value} x {l.Key}"))}");
    }

    /// <summary>With "-Only buildermap": the climb is TFA's FINAL ASCENT on the picked Builder map, laid out from its file.</summary>
    private IEnumerator BuilderMapClimbCheck()
    {
        if (builderForClimb == null)
        {
            Info("the Builder map climb is skipped", "run with -WithTfa -BuilderMaps <folder> -Only buildermap");
            yield break;
        }
        BuilderMaps.Map map = builderForClimb;
        MapPicker.Picked = MapPicker.Today;
        // TFA notes that it laid the island out from a saved map (a field of its mod biomes' bookkeeping).
        bool stored = (bool)(AccessTools.Field(AccessTools.TypeByName("TrueFinalAscent.FinalAscentModBiomes"), "FromStored")?.GetValue(null) ?? false);
        string kinds = string.Join(",", Tfa.Stages().Select(s => s.Kind));
        string expected = string.Join(",", map.Code.Select((c, i) => i == 0 ? "Shore" : i == 1 ? (c == 'T' ? "Tropics" : "Roots") : i == 2 ? (c == 'A' ? "Alpine" : "Mesa") : (c == 'V' ? "Caldera,Kiln" : "Gloom,Citadel")));
        Check("the Builder map picked on the pass plays as TFA's FINAL ASCENT on the test's ascent, laid out from the map's file (not built anew), with the map's biomes",
            Tfa.FinalAscentActive && Tfa.BuilderActive && Ascents.currentAscent == target && stored && kinds == expected,
            $"FINAL ASCENT {Tfa.FinalAscentActive}, Builder {Tfa.BuilderActive}, ascent {Ascents.currentAscent} (test's {target}); from the saved map {stored}; blocks {kinds} (the map's {expected})");

        // The Peak after the chain (on it: TFA switches far parts of its mountain on only as the scouts come near), its gem
        // statue, and the Nadir (TFA builds the game's own in every FINAL ASCENT).
        // TFA wakes a block (and the Peak on the last one) as the climb gets there: first the game's jump to the Kiln's (or
        // the Citadel's) segment, then onto the summit.
        MapHandler.JumpToSegment(Segment.TheKiln);
        yield return new WaitForSeconds(8f);
        yield return UntilUp(Local);
        Transform summit = PeakHill.Summit();
        if (summit != null)
        {
            RaycastHit ground = HelperFunctions.LineCheck(summit.position + Vector3.up * 10f, summit.position + Vector3.down * 30f, HelperFunctions.LayerType.TerrainMap);
            yield return WarpOnto((ground.transform != null ? ground.point : summit.position) + Vector3.up * 1.5f);
            yield return new WaitForSeconds(3f);
        }
        Peak.ScoutStatue statue = summit != null
            ? FindObjectsByType<Peak.ScoutStatue>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(st => st != null).OrderBy(st => Vector3.Distance(st.transform.position, summit.position)).FirstOrDefault()
            : null;
        float fromSummit = statue != null ? Vector3.Distance(statue.transform.position, summit.position) : -1f;
        if (statue != null)
        {
            LookAtPoint(Local, statue.transform.position);
            yield return new WaitForSeconds(0.5f);
            Screenshot("builder-map-statue");
        }
        Type voidType = AccessTools.TypeByName("TrueFinalAscent.FinalAscentVoid");
        GameObject nadir = voidType != null ? AccessTools.Method(voidType, "Find")?.Invoke(null, null) as GameObject : null;
        Check("the Builder map's Peak has the statue the gems go in, and the Nadir is there after it",
            statue != null && statue.isActiveAndEnabled && fromSummit >= 0f && fromSummit < 80f && nadir != null,
            $"the gem statue {(statue != null ? $"{statue.transform.position:F0} (on {statue.isActiveAndEnabled})" : "none")}, {fromSummit:0} m from the summit {(summit != null ? summit.position.ToString("F0") : "none")}; the scout at {Local.Center:F0}; the Nadir {(nadir != null ? nadir.name : "none")}");
        if (nadir != null)
        {
            MapHandler.JumpToSegment(Segment.Void);
            yield return new WaitForSeconds(8f);
            yield return UntilUp(Local);
            Check("a jump to the Nadir lands the scout there, past the Peak", Map.CurrentPlace == Place.Nadir && Map.InNadir,
                $"in the {Map.CurrentPlace}, the Nadir {Map.InNadir}, at {Local.Center:F0}");
        }
    }
}
