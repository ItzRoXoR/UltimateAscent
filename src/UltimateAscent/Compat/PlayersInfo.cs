using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace UltimateAscent.Compat;

/// <summary>
/// PlayersInfo (WUYACHIYU) shows, next to each status on your stamina bar and your teammates', how
/// long it takes to wear off. It works that out from the scout's own recovery fields, which carry this
/// ascent's rates (<see cref="Features.StatusRecovery"/>), and for thorns from when they pop out by
/// themselves. Cactus thorns that never pop out by themselves (ascent 17 with others) would make it
/// show a broken time; they show none.
/// </summary>
internal static class PlayersInfo
{
    internal const string Guid = "com.players.info";

    private static bool? present;

    internal static bool Present => present ??= Chainloader.PluginInfos.ContainsKey(Guid);

    private static MethodBase ThornsRemaining() => AccessTools.Method(AccessTools.TypeByName("PlayersInfo.Helpers.AfflictionTimeHelper"), "GetThornsRemaining");

    [HarmonyPatch]
    private static class ThornsThatStay
    {
        private static bool Prepare() => Present && ThornsRemaining() != null;

        private static MethodBase TargetMethod() => ThornsRemaining();

        private static void Postfix(ref float __result)
        {
            if (float.IsNaN(__result) || float.IsInfinity(__result))
            {
                __result = 0f;
            }
        }
    }
}
