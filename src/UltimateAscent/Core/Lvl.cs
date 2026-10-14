namespace UltimateAscent.Core;

/// <summary>
/// The ascent that switches each rule on. Ascents are cumulative: ascent N contains every
/// rule whose level is N or lower (plus all vanilla ascents 1–8).
/// </summary>
internal static class Lvl
{
    internal const int First = 9;
    internal const int Last = 25;
    internal const int Count = Last - First + 1;

    /// <summary>Highest vanilla ascent index (ascent 8).</summary>
    internal const int VanillaLast = 8;

    /// <summary>Value of the Steam MaxAscent stat once vanilla ascent 8 has been beaten.</summary>
    internal const int VanillaCompletedStat = 9;

    // 13DDA: fog x2, starts instantly, runs until the Kiln; Kiln lava starts sooner and rises faster.
    internal const int FogAndKiln = 9;
    // More Ascents: big luggage may contain only one item.
    internal const int LightLuggage = 10;
    // 13DDA: stricter rule zero.
    internal const int RuleZero = 11;
    // 13DDA + More Ascents: falling and helping people make you drowsy.
    internal const int Drowsy = 12;
    // More Ascents: butter fingers.
    internal const int ButterFingers = 13;
    // 13DDA: items are worse.
    internal const int WorseItems = 14;
    // 13DDA: cold, drowsy, poison and heat apply more.
    internal const int StrongerAilments = 15;
    // 13DDA + More Ascents: heat heals 3x slower in the Caldera/Kiln, campfires are extremely hot.
    internal const int SlowHeatHotCampfires = 16;
    // 13DDA: hazards are more dangerous.
    internal const int Hazards = 17;
    // 13DDA: weather is more extreme (including the Caldera lava).
    internal const int ExtremeWeather = 18;
    // More Ascents (extended): no revive statues; 20 starting curse, 15 curse per revive, revives keep petrification.
    internal const int NoReviveStatues = 19;
    // More Ascents: Bing Bong calls upon an evil force if not given attention.
    internal const int BingBong = 20;
    // 13DDA: suitcases are painful.
    internal const int PainfulLuggage = 21;
    // 13DDA: all status ailments have unique effects.
    internal const int UniqueAilments = 22;
    // 13DDA + More Ascents: the sun is hot everywhere and burns held sun protection.
    internal const int HotSun = 23;
    // More Ascents: going unconscious is instant death.
    internal const int NoUnconscious = 24;
    // 13DDA: all weather everywhere (TFA Apocalypse: TFA's own weather, plus tornadoes).
    internal const int AllWeather = 25;
}
