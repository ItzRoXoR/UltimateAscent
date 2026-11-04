using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// The Builder maps (an optional download, the "Builder maps" pack): maps built with True Final Ascent's Builder with
/// the original maps' layout (the Shore; the Tropics or the Roots; the Alpine or the Mesa; the Caldera and the Kiln or
/// the Gloom and the Citadel; the Peak, and the Nadir through the gem statue), their biomes' variants spread evenly.
/// The pack's maps lie in a "BuilderMaps" folder anywhere under BepInEx's plugins, each file named for its number
/// first ("001_..."). The map picker lists them after the original maps as "Builder: MAP 1"; only with TFA installed
/// (it builds the island from the map) and not in a custom run. A picked one plays as TFA's FINAL ASCENT on that map
/// at the ascent the boarding pass shows (see <see cref="Compat.TfaMaps"/>); TFA sends it to the guests.
/// </summary>
internal static class BuilderMaps
{
    private const string FolderName = "BuilderMaps";
    private const string Magic = "TFAMap 1";

    internal sealed class Map
    {
        internal int Number;
        internal string Path;
        /// <summary>The map's biomes as the game's own maps list them (S; T or R; A or M; V or S), "" if unknown.</summary>
        internal string Code = "";
    }

    private static List<Map> found;

    /// <summary>The pack's maps, by number (looked for once).</summary>
    internal static List<Map> All => found ??= Find();

    /// <summary>The pack's maps are offered: TFA can start them, and the climb is no custom run.</summary>
    internal static bool Offered => All.Count > 0 && Compat.TfaMaps.Ready && !RunSettings.IsCustomRun;

    internal static int Count => Offered ? All.Count : 0;

    internal static string Name(Map map) => "Builder: " + (LocalizedText.CURRENT_LANGUAGE == LocalizedText.Language.Russian ? "КАРТА " : "MAP ") + map.Number;

    private static List<Map> Find()
    {
        List<Map> maps = new List<Map>();
        try
        {
            if (!Directory.Exists(Paths.PluginPath))
            {
                return maps;
            }
            foreach (string folder in Directory.GetDirectories(Paths.PluginPath, FolderName, SearchOption.AllDirectories))
            {
                foreach (string file in Directory.GetFiles(folder, "*.map"))
                {
                    Match number = Regex.Match(Path.GetFileName(file), @"^(\d+)_");
                    if (!number.Success)
                    {
                        continue;
                    }
                    string code = CodeOf(file, out bool isMap);
                    if (isMap)
                    {
                        maps.Add(new Map { Number = int.Parse(number.Groups[1].Value), Path = file, Code = code });
                    }
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Builder maps: the maps could not be listed (" + e.Message + ").");
        }
        // One map a number (the first found, should two packs hold the same).
        maps = maps.GroupBy(m => m.Number).Select(g => g.First()).OrderBy(m => m.Number).ToList();
        if (maps.Count > 0)
        {
            Plugin.Log.LogInfo($"Builder maps: {maps.Count} found{(Compat.TfaMaps.Ready ? "" : " (True Final Ascent is needed to play them)")}.");
        }
        return maps;
    }

    /// <summary>
    /// The biomes of the map in this file, from its header (TFA's "key" line: seed, then the chain, "ch=S2,T5,A1,C,K"),
    /// and whether it is a TFA map at all.
    /// </summary>
    private static string CodeOf(string file, out bool isMap)
    {
        isMap = false;
        byte[] start = new byte[8192];
        int read;
        using (FileStream stream = File.OpenRead(file))
        {
            read = stream.Read(start, 0, start.Length);
        }
        string head = Encoding.UTF8.GetString(start, 0, read);
        string[] lines = head.Split('\n');
        if (lines.Length < 2 || lines[0].TrimEnd('\r') != Magic)
        {
            return "";
        }
        isMap = true;
        string key = lines.FirstOrDefault(l => l.StartsWith("key=", StringComparison.Ordinal));
        string[] parts = key != null ? key.Substring(4).Split('\t') : new string[0];
        string chain = parts.Length > 1 ? parts[1].Split('|').FirstOrDefault(p => p.StartsWith("ch=", StringComparison.Ordinal)) : null;
        if (chain == null)
        {
            return "";
        }
        StringBuilder code = new StringBuilder();
        foreach (string block in chain.Substring(3).Split(','))
        {
            switch (block.Length > 0 ? block[0] : ' ')
            {
                case 'S': code.Append('S'); break;
                case 'T': code.Append('T'); break;
                case 'R': code.Append('R'); break;
                case 'A': code.Append('A'); break;
                case 'M': code.Append('M'); break;
                // The Caldera with the Kiln is the game's volcano, the Gloom with the Citadel its swamp.
                case 'C': code.Append('V'); break;
                case 'G': code.Append('S'); break;
            }
        }
        return code.ToString();
    }
}
