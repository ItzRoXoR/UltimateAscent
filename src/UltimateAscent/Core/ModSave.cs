using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UltimateAscent.Core;

/// <summary>
/// The mod's own small save, next to the game's (its persistent data folder, UltimateAscent/Save.txt): one
/// "key=value" per line. It keeps what belongs to the player rather than to the config: the cosmetics they
/// unlocked, the mod's cosmetics they wear, and their Blind Mode and Hot Mode boxes. Nothing of it goes to
/// Steam, and the config file is never touched.
/// </summary>
internal static class ModSave
{
    private static Dictionary<string, string> values;

    internal static string FilePath => Path.Combine(Application.persistentDataPath, "UltimateAscent", "Save.txt");

    private static Dictionary<string, string> Values
    {
        get
        {
            if (values == null)
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(FilePath))
                    {
                        foreach (string line in File.ReadAllLines(FilePath))
                        {
                            int split = line.IndexOf('=');
                            if (split > 0)
                            {
                                values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Could not read " + FilePath + ": " + ex.Message);
                }
            }
            return values;
        }
    }

    internal static string Get(string key) => Values.TryGetValue(key, out string value) ? value : "";

    internal static bool GetBool(string key) => Get(key) == "1";

    internal static void Set(string key, string value)
    {
        value ??= "";
        if (Get(key) == value)
        {
            return;
        }
        if (value.Length == 0)
        {
            Values.Remove(key);
        }
        else
        {
            Values[key] = value;
        }
        Write();
    }

    internal static void SetBool(string key, bool value) => Set(key, value ? "1" : "");

    /// <summary>Forgets what was read, so the next question reads the file again (for the tests).</summary>
    internal static void Reload() => values = null;

    private static void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> pair in Values)
            {
                lines.Add(pair.Key + "=" + pair.Value);
            }
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Could not write " + FilePath + ": " + ex.Message);
        }
    }
}
