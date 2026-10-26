using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// What the fog does to you and what it looks like: from ascent 18 the fog is three times as
/// strong and turns poisonous (purple).
/// The game's two status calls inside <see cref="FogSphere.SetSharderVars"/> are redirected here.
/// </summary>
internal static class FogSphereRules
{
    [HarmonyPatch(typeof(FogSphere), nameof(FogSphere.SetSharderVars))]
    private static class StatusCalls
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo addStatus = AccessTools.Method(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus),
                new[] { typeof(CharacterAfflictions.STATUSTYPE), typeof(float), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
            if (addStatus == null)
            {
                throw new MissingMethodException("CharacterAfflictions.AddStatus with 7 parameters");
            }
            MethodInfo replacement = AccessTools.Method(typeof(FogSphereRules), nameof(AddFogStatus));
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int replaced = 0;
            foreach (CodeInstruction instruction in code)
            {
                if (instruction.Calls(addStatus))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replaced++;
                }
            }
            if (replaced != 2)
            {
                throw new InvalidOperationException($"Expected the fog to apply 2 statuses, found {replaced}; the fog rules are off.");
            }
            return code;
        }
    }

    private static bool AddFogStatus(CharacterAfflictions recipient, CharacterAfflictions.STATUSTYPE statusType, float amount, bool fromRPC, bool playEffects, bool notify, bool ignoreInvincibility, bool ignoreSkeleton)
    {
        Character character = recipient.character;
        if (amount > 0f && Rules.Owns(character) && !StatusReplication.Active && Map.Ready && Rules.Enabled(Lvl.ExtremeWeather))
        {
            if (statusType == CharacterAfflictions.STATUSTYPE.Cold)
            {
                statusType = CharacterAfflictions.STATUSTYPE.Poison;
                amount *= 3f;
            }
            else if (statusType == CharacterAfflictions.STATUSTYPE.Injury)
            {
                amount *= 3f;
            }
        }
        // Ascent 15 makes injuries lethal for skeletons outside the fog; the fog's own damage is not "outside".
        CharacterData data = character.data;
        bool markFog = Rules.Enabled(Lvl.WorseItems) && Rules.Owns(character) && data.isSkeleton && !data.isInFog;
        if (markFog)
        {
            data.isInFog = true;
        }
        try
        {
            return recipient.AddStatus(statusType, amount, fromRPC, playEffects, notify, ignoreInvincibility, ignoreSkeleton);
        }
        finally
        {
            if (markFog)
            {
                data.isInFog = false;
            }
        }
    }

    /// <summary>
    /// Colours the fog purple once it is poisonous (its tint is its material's one colour; its swirls
    /// are textures); ash grey while it draws the Kiln's ash (<see cref="KilnAsh"/>). True Final Ascent's
    /// Ice Fog borrows the same sphere for its own fog (ice blue, chilling, never the game's fog damage);
    /// it keeps its own colour.
    /// </summary>
    [HarmonyPatch(typeof(FogSphere), nameof(FogSphere.SetSharderVars))]
    private static class Tint
    {
        private sealed class State
        {
            internal readonly MaterialPropertyBlock original = new MaterialPropertyBlock();
            internal bool applied;
        }

        private static ConditionalWeakTable<FogSphere, State> states = new ConditionalWeakTable<FogSphere, State>();
        private static readonly int TintColor = Shader.PropertyToID("_FogtintColor");
        private static readonly Color Purple = new Color(0.5f, 0.05f, 0.5f, 0f);

        private static void Prefix(FogSphere __instance)
        {
            if (__instance.rend != null && states.TryGetValue(__instance, out State state) && state.applied)
            {
                __instance.rend.SetPropertyBlock(state.original);
                state.applied = false;
            }
        }

        private static void Postfix(FogSphere __instance)
        {
            if (__instance.rend == null || __instance.mpb == null || !Map.Ready || !Rules.InRun || !Rules.Enabled(Lvl.ExtremeWeather) || Compat.Tfa.IceFogDrawing)
            {
                return;
            }
            if (!states.TryGetValue(__instance, out State state))
            {
                state = new State();
                states.Add(__instance, state);
            }
            __instance.rend.GetPropertyBlock(state.original);
            __instance.mpb.SetColor(TintColor, KilnAsh.Drawing ? KilnAsh.AshColour : Purple);
            __instance.rend.SetPropertyBlock(__instance.mpb);
            state.applied = true;
        }

        internal static void Reset()
        {
            states = new ConditionalWeakTable<FogSphere, State>();
        }
    }

    internal static void Reset()
    {
        Tint.Reset();
    }
}
