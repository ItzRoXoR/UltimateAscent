using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UltimateAscent.Core;

namespace UltimateAscent.Compat;

/// <summary>
/// True Final Ascent's saved Builder maps, started the way its own Maps page starts one ("Play"): the map pinned, its
/// seed chosen, the setup confirmed for the moment of the start, and FINAL ASCENT started from the kiosk on the grade
/// asked for, the ascent the boarding pass shows (TFA's FINAL ASCENT plays on a grade: the ascent's rules, this mod's
/// too). TFA then loads its own island and lays the saved map out instead of building a new one, and sends it to the
/// guests. What TFA had chosen itself (its seed, its grade) is put back at once: the start reads them only there. All
/// through reflection: if TFA changes, the maps are simply not offered.
/// </summary>
internal static class TfaMaps
{
    private const string Ns = "TrueFinalAscent.";

    private static bool? ready;
    private static MethodInfo entryOf;
    private static MethodInfo pin;
    private static FieldInfo entrySeed;
    private static PropertyInfo chosenSeed;
    private static PropertyInfo chosenGrade;
    private static PropertyInfo confirmed;
    private static MethodInfo fromLine;
    private static FieldInfo entryVariants;
    private static MethodInfo costOf;
    private static MethodInfo ramProblem;
    private static MethodInfo warn;
    private static MethodInfo gb;
    private static PropertyInfo systemRam;
    private static FieldInfo costRam;

    /// <summary>TFA is installed and its saved maps can be started.</summary>
    internal static bool Ready => ready ??= Tfa.Present && Bind();

    private static bool Bind()
    {
        try
        {
            Type cache = AccessTools.TypeByName(Ns + "FinalAscentMapCache");
            Type entry = cache != null ? AccessTools.Inner(cache, "Entry") : null;
            Type setup = AccessTools.TypeByName(Ns + "FinalAscentSetup");
            Type difficulty = AccessTools.TypeByName(Ns + "FinalAscentDifficulty");
            Type window = AccessTools.TypeByName(Ns + "FinalAscentSetupWindow");
            entryOf = AccessTools.Method(cache, "EntryOf", new[] { typeof(string) });
            pin = AccessTools.Method(cache, "Pin");
            entrySeed = AccessTools.Field(entry, "Seed");
            entryVariants = AccessTools.Field(entry, "Variants");
            chosenSeed = AccessTools.Property(setup, "ChosenSeed");
            chosenGrade = AccessTools.Property(difficulty, "Chosen");
            confirmed = AccessTools.Property(window, "Confirmed");
            if (entryOf == null || pin == null || entrySeed == null || entryVariants == null || chosenSeed == null || chosenGrade == null || confirmed == null || confirmed.GetSetMethod(true) == null)
            {
                Plugin.Log.LogWarning("True Final Ascent: its saved maps could not be bound; the Builder maps are not offered.");
                return false;
            }
            // Optional: TFA's own memory check before a start (its Maps page makes it); without it the start goes ahead.
            Type chain = AccessTools.TypeByName(Ns + "FinalAscentChain");
            Type toast = AccessTools.TypeByName(Ns + "FinalAscentRunToast");
            fromLine = chain != null ? AccessTools.Method(chain, "FromLine", new[] { typeof(string) }) : null;
            costOf = chain != null ? AccessTools.Method(chain, "CostOf") : null;
            ramProblem = chain != null ? AccessTools.Method(chain, "RamProblem") : null;
            gb = chain != null ? AccessTools.Method(chain, "Gb") : null;
            systemRam = chain != null ? AccessTools.Property(chain, "SystemRamMb") : null;
            warn = toast != null ? AccessTools.Method(toast, "WarnAgain") : null;
            costRam = costOf != null ? AccessTools.Field(costOf.ReturnType, "RamMb") : null;
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("True Final Ascent: its saved maps could not be bound (" + e.Message + "); the Builder maps are not offered.");
            return false;
        }
    }

    /// <summary>
    /// Starts FINAL ASCENT on the saved map at this path, on this grade (the ascent): true when the island is loading. TFA
    /// may refuse (a guest without TFA, not enough memory): it says why itself, and nothing starts.
    /// </summary>
    internal static bool Start(AirportCheckInKiosk kiosk, string path, int grade, byte[] runSettings)
    {
        if (!Ready || kiosk == null)
        {
            return false;
        }
        object entry;
        try
        {
            entry = entryOf.Invoke(null, new object[] { path });
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Builder maps: TFA cannot read {path} ({e.InnerException?.Message ?? e.Message}).");
            return false;
        }
        if (entry == null)
        {
            Plugin.Log.LogWarning($"Builder maps: TFA cannot read {path}.");
            return false;
        }
        if (NotEnoughMemory(entry))
        {
            return false;
        }
        int seedWas = (int)chosenSeed.GetValue(null);
        int gradeWas = (int)chosenGrade.GetValue(null);
        bool loadingWas = LoadingScreenHandler.loading;
        pin.Invoke(null, new[] { entry });
        chosenSeed.SetValue(null, entrySeed.GetValue(entry));
        chosenGrade.SetValue(null, grade);
        confirmed.SetValue(null, true);
        try
        {
            kiosk.LoadIslandMaster(Features.BoardingPassUi.FinalAscentIndex, runSettings);
        }
        finally
        {
            confirmed.SetValue(null, false);
            chosenGrade.SetValue(null, gradeWas);
            chosenSeed.SetValue(null, seedWas);
            // The pin stays while the island loads (TFA lays the map out from it), as its Maps page leaves it.
            if (loadingWas || !LoadingScreenHandler.loading)
            {
                pin.Invoke(null, new object[] { null });
            }
        }
        return !loadingWas && LoadingScreenHandler.loading;
    }

    /// <summary>TFA's own check before it starts a saved map: the map needs more memory than this computer has (it says so).</summary>
    private static bool NotEnoughMemory(object entry)
    {
        if (fromLine == null || costOf == null || ramProblem == null)
        {
            return false;
        }
        try
        {
            object chain = fromLine.Invoke(null, new[] { entryVariants.GetValue(entry) });
            if (!(chain is IList))
            {
                return false;
            }
            object cost = costOf.Invoke(null, new[] { chain });
            if (ramProblem.Invoke(null, new[] { cost }) == null)
            {
                return false;
            }
            if (warn != null && gb != null && systemRam != null && costRam != null)
            {
                warn.Invoke(null, new object[] { "chain.cost.ram", new object[] { gb.Invoke(null, new[] { costRam.GetValue(cost) }), gb.Invoke(null, new[] { systemRam.GetValue(null) }) } });
            }
            return true;
        }
        catch (Exception e)
        {
            Plugin.Debug("Builder maps: TFA's memory check failed: " + e.Message);
            return false;
        }
    }
}
