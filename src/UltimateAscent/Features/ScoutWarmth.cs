using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace UltimateAscent.Features;

/// <summary>
/// Scouts warm each other (the game's CharacterHeatEmission: each scout takes cold off the scouts close to them), but a
/// scout gives off warmth only once nothing has made them cold for a while: 2 s instead of the game's 3 s, on every
/// ascent.
/// </summary>
internal static class ScoutWarmth
{
    /// <summary>How long a scout must have had no cold before they warm the others (the game: 3 s).</summary>
    internal const float ColdFreeSeconds = 2f;

    private const float GameColdFreeSeconds = 3f;

    [HarmonyPatch(typeof(CharacterHeatEmission), nameof(CharacterHeatEmission.Update))]
    private static class SoonerWarmth
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            List<int> at = new List<int>();
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ldc_R4 && code[i].operand is float value && value == GameColdFreeSeconds)
                {
                    at.Add(i);
                }
            }
            if (at.Count != 1)
            {
                Plugin.Log.LogWarning($"Scout warmth: the game's 3 s without cold was found {at.Count} times; it stays the game's.");
                return code;
            }
            code[at[0]].operand = ColdFreeSeconds;
            return code;
        }
    }
}
