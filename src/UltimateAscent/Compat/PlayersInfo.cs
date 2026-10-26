using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace UltimateAscent.Compat;

/// <summary>
/// PlayersInfo (WUYACHIYU) shows, next to each status on your stamina bar and your teammates', how
/// long it takes to wear off. It works that out from the scout's own recovery fields, which carry this
/// ascent's rates (<see cref="Features.StatusRecovery"/>), and for thorns from when they pop out by
/// themselves. Thorns that never pop out by themselves (ascent 17's thorns on the body) show "∞": its clock
/// turned an endless time into -2147483648:-2147483648, as it does any time too long for its numbers.
/// </summary>
internal static class PlayersInfo
{
    internal const string Guid = "com.players.info";

    /// <summary>What an endless time shows.</summary>
    internal const string Endless = "∞";

    /// <summary>From this many seconds (100 hours) a time is endless: the clock's minutes would not fit its numbers.</summary>
    private const float EndlessFrom = 360000f;

    private static bool? present;

    internal static bool Present => present ??= Chainloader.PluginInfos.ContainsKey(Guid);

    private static System.Type Helper() => AccessTools.TypeByName("PlayersInfo.Helpers.AfflictionTimeHelper");

    private static MethodBase FormatTime() => Helper() != null ? AccessTools.Method(Helper(), "FormatTime", new[] { typeof(float) }) : null;

    [HarmonyPatch]
    private static class EndlessTime
    {
        private static bool Prepare() => Present && FormatTime() != null;

        private static MethodBase TargetMethod() => FormatTime();

        private static bool Prefix(float seconds, ref string __result)
        {
            if (float.IsNaN(seconds) || seconds < EndlessFrom)
            {
                return true;
            }
            __result = Endless;
            return false;
        }
    }
}
