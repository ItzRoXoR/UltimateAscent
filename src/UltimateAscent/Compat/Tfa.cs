using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;

namespace UltimateAscent.Compat;

/// <summary>
/// True Final Ascent (CptPICHU) integration. TFA has no public API, so everything goes through
/// reflection and is guarded: if TFA changes, the integration switches itself off and the rest of
/// the mod keeps working.
/// <list type="bullet">
/// <item>FINAL ASCENT stacks every biome into the vanilla segments; TFA's stage tracking tells
/// where the scouts really are, and its cold-night count tells where the nights end. Its Builder
/// (TFA 3.0) packs any chain of biomes into them, repeats included: there its blocks tell each one's
/// ground and campfires (<see cref="Stages"/>), its rest stops spare the resting scouts from the
/// weather and the sun (<see cref="RestSpares"/>), and the sun shines through its invisible walls
/// (<see cref="SunLineCheck"/>).</item>
/// <item>Apocalypse mode: TFA decides how hard its own events are, so this mod's rules leave them
/// alone. What TFA's code does to a scout (<see cref="Causing"/>), what its events spawn
/// (<see cref="Spawned"/>) and, for what cannot be traced, the weather TFA is running
/// (<see cref="Weather"/>) are kept out of the rules that would change them. The same goes for a
/// weather another mod starts through TFA (TFA 3.0 on), in any climb.</item>
/// </list>
/// </summary>
internal static class Tfa
{
    internal const string Guid = "com.CptPICHU.peak.truefinalascent";

    private const string Ns = "TrueFinalAscent.";

    /// <summary>What TFA puts in the network data of the things its events spawn ("FA_BloodMoon", "FA_CactusRain", "FA_SUPPLY").</summary>
    private const string SpawnTag = "FA_";

    /// <summary>
    /// TFA components that TFA adds to the game's own objects, not to what its events spawn: the
    /// voice lines it gives every Mandrake. They do not make an object TFA's.
    /// </summary>
    private static readonly string[] OnGameObjects = { Ns + "FinalAscentMandrakeLine" };

    private static bool? present;
    private static bool broken;

    private static Assembly assembly;
    private static PropertyInfo finalAscentActive;
    private static MethodInfo placeCurrent;
    private static MethodInfo placeInRun;
    private static PropertyInfo apocalypseActive;
    private static PropertyInfo weatherApplied;
    private static PropertyInfo modWeatherApplied;
    private static PropertyInfo iceFogDrawing;
    private static MethodInfo terrainComeBack;
    private static bool terrainFailed;
    private static MethodInfo coldNightReached;
    private static PropertyInfo mildNights;
    private static PropertyInfo chainBuild;
    private static PropertyInfo currentStage;
    private static MethodInfo allStages;
    private static FieldInfo stageKind;
    private static FieldInfo stageRoot;
    private static FieldInfo stageTerrain;
    private static FieldInfo stageCampfire;
    private static FieldInfo stageOrdinal;
    private static PropertyInfo stageExit;
    private static PropertyInfo summitKeeper;
    private static PropertyInfo restPaused;
    private static MethodInfo restSpares;
    private static MethodInfo sunLineCheck;
    private static Func<Transform, bool> builderWall;
    private static Func<Character, float> sizeOf;

    internal static bool Present => present ??= Chainloader.PluginInfos.ContainsKey(Guid) && Bind();

    private static bool Bind()
    {
        try
        {
            Type finalAscent = AccessTools.TypeByName(Ns + "FinalAscent");
            Type place = AccessTools.TypeByName(Ns + "FinalAscentPlace");
            Type apocalypse = AccessTools.TypeByName(Ns + "FinalAscentApocalypse");
            Type builder = AccessTools.TypeByName(Ns + "FinalAscentBuilder");
            Type weather = AccessTools.TypeByName(Ns + "FinalAscentWeather");

            finalAscentActive = AccessTools.Property(finalAscent, "Active");
            placeCurrent = AccessTools.Method(place, "Current");
            placeInRun = AccessTools.Method(place, "InRun");
            apocalypseActive = AccessTools.Property(apocalypse, "Active");
            weatherApplied = AccessTools.Property(weather, "Applied");
            // Optional (TFA 3.0 on): a weather another mod started through TFA, in any climb.
            modWeatherApplied = weather != null ? AccessTools.Property(weather, "AppliedApi") : null;
            // Optional: without it the fog colour simply does not check for TFA's Ice Fog.
            iceFogDrawing = AccessTools.Property(AccessTools.TypeByName(Ns + "FinalAscentIceFog"), "Drawing");
            // Optional: without it, leaving FINAL ASCENT by wrapping around leaves the TerrainRandomiser mod as TFA set it.
            Type terrain = AccessTools.TypeByName(Ns + "FinalAscentTerrainRandomiser");
            terrainComeBack = terrain != null ? AccessTools.Method(terrain, "ComeBack") : null;
            // Optional (TFA 2.9 on): how far FINAL ASCENT has come for the cold nights; without it the progress points count.
            Type coldNight = AccessTools.TypeByName(Ns + "FinalAscentColdNight");
            coldNightReached = coldNight != null ? AccessTools.Method(coldNight, "Reached", new[] { typeof(MountainProgressHandler) }) : null;
            // Optional (TFA 3.0 on): a biome of another mod that has mild nights, where TFA's cold nights stay away.
            Type modBiomes = AccessTools.TypeByName(Ns + "FinalAscentModBiomes");
            mildNights = modBiomes != null ? AccessTools.Property(modBiomes, "MildNights") : null;
            // Optional (TFA 3.0 on): the Builder's climb, block by block; without it the Builder is taken as FINAL ASCENT.
            Type segments = AccessTools.TypeByName(Ns + "FinalAscentSegments");
            Type stage = AccessTools.TypeByName(Ns + "FinalAscentStage");
            chainBuild = builder != null ? AccessTools.Property(builder, "ChainBuild") : null;
            summitKeeper = builder != null ? AccessTools.Property(builder, "SummitKeeper") : null;
            currentStage = segments != null ? AccessTools.Property(segments, "CurrentStage") : null;
            allStages = segments != null ? AccessTools.Method(segments, "AllStages") : null;
            if (stage != null)
            {
                stageKind = AccessTools.Field(stage, "Kind");
                stageRoot = AccessTools.Field(stage, "Root");
                stageTerrain = AccessTools.Field(stage, "Terrain");
                stageCampfire = AccessTools.Field(stage, "Campfire");
                stageOrdinal = AccessTools.Field(stage, "Ordinal");
                stageExit = AccessTools.Property(stage, "Exit");
            }
            if (chainBuild == null || currentStage == null || allStages == null || stageKind == null || stageRoot == null || stageTerrain == null || stageCampfire == null || stageOrdinal == null || stageExit == null)
            {
                chainBuild = null;
            }
            // Optional (TFA 3.0 on): the Builder's rest stops, where the weather and the sun leave resting scouts alone.
            Type rest = AccessTools.TypeByName(Ns + "FinalAscentRestPause");
            restPaused = rest != null ? AccessTools.Property(rest, "Paused") : null;
            restSpares = rest != null ? AccessTools.Method(rest, "Spares", new[] { typeof(Character) }) : null;
            // Optional (TFA 3.0 on): a look at the sun that passes through the Builder's invisible walls between its blocks.
            Type shelter = AccessTools.TypeByName(Ns + "FinalAscentSunShelter");
            sunLineCheck = shelter != null ? AccessTools.Method(shelter, "LineCheck") : null;
            // Optional (TFA 3.0 on): the Builder's invisible walls between its blocks, which what the wind carries passes.
            MethodInfo wall = builder != null ? AccessTools.Method(builder, "IsFogWall", new[] { typeof(Transform) }) : null;
            builderWall = wall != null ? Delegate.CreateDelegate(typeof(Func<Transform, bool>), wall, false) as Func<Transform, bool> : null;
            // Optional (TFA 3.0 on): how big its Mini and Mega sizes make a scout.
            MethodInfo size = AccessTools.Method(AccessTools.TypeByName(Ns + "FinalAscentSizeRange"), "Of", new[] { typeof(Character) });
            sizeOf = size != null ? Delegate.CreateDelegate(typeof(Func<Character, float>), size, false) as Func<Character, float> : null;

            if (finalAscent == null || finalAscentActive == null || placeCurrent == null || placeInRun == null || apocalypseActive == null || weatherApplied == null)
            {
                Plugin.Log.LogWarning("True Final Ascent was found, but not the parts Ultimate Ascent works with; TFA integration is off.");
                return false;
            }
            assembly = finalAscent.Assembly;
            Plugin.Log.LogInfo("True Final Ascent integration ready.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("True Final Ascent integration is off: " + ex.Message);
            return false;
        }
    }

    private static T Guard<T>(Func<T> call, T fallback)
    {
        if (broken || !Present)
        {
            return fallback;
        }
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            broken = true;
            Plugin.Log.LogWarning("True Final Ascent integration stopped after an error: " + ex.GetBaseException().Message);
            return fallback;
        }
    }

    private static int flagsFrame = -1;
    private static bool finalAscentCached;
    private static bool apocalypseCached;
    private static string weatherCached = "";
    private static string modWeatherCached = "";

    private static void RefreshFlags()
    {
        int frame = Time.frameCount;
        if (frame == flagsFrame)
        {
            return;
        }
        flagsFrame = frame;
        finalAscentCached = Rules.InRun && Guard(() => (bool)finalAscentActive.GetValue(null), false);
        apocalypseCached = Rules.InRun && Guard(() => (bool)apocalypseActive.GetValue(null) && (bool)placeInRun.Invoke(null, null), false);
        weatherCached = apocalypseCached ? Guard(() => weatherApplied.GetValue(null) as string ?? "", "") : "";
        modWeatherCached = Rules.InRun && modWeatherApplied != null ? Guard(() => modWeatherApplied.GetValue(null) as string ?? "", "") : "";
    }

    /// <summary>A FINAL ASCENT climb is loaded.</summary>
    internal static bool FinalAscentActive
    {
        get
        {
            if (!Present || broken)
            {
                return false;
            }
            RefreshFlags();
            return finalAscentCached;
        }
    }

    /// <summary>TFA's Apocalypse weather replaces the vanilla weather in this climb.</summary>
    internal static bool ApocalypseActive
    {
        get
        {
            if (!Present || broken)
            {
                return false;
            }
            RefreshFlags();
            return apocalypseCached;
        }
    }

    // ---- Apocalypse mode and other mods' weather: TFA's events are TFA's own ----

    /// <summary>
    /// The weather TFA is running right now ("" for none), e.g. "dark", "watch", "sleepfog", "sun": its Apocalypse
    /// weather, or one another mod started through TFA (TFA 3.0 on; in any climb, with or without Apocalypse).
    /// </summary>
    internal static string Weather
    {
        get
        {
            if (!Present || broken)
            {
                return "";
            }
            RefreshFlags();
            return weatherCached.Length > 0 ? weatherCached : modWeatherCached;
        }
    }

    /// <summary>A weather of TFA's is running (its Apocalypse's, or another mod's through TFA).</summary>
    internal static bool WeatherRunning => Weather.Length > 0;

    /// <summary>A weather another mod started through TFA is running (TFA 3.0 on).</summary>
    internal static bool ModWeatherRunning
    {
        get
        {
            if (!Present || broken)
            {
                return false;
            }
            RefreshFlags();
            return modWeatherCached.Length > 0;
        }
    }

    /// <summary>TFA's Blackout or Watch is driving the Scoutmaster.</summary>
    internal static bool ScoutmasterEvent
    {
        get
        {
            string weather = Weather;
            return weather == "dark" || weather == "watch";
        }
    }

    /// <summary>
    /// TFA's own code is doing this right now, in Apocalypse mode or while another mod's weather runs through
    /// TFA: one of its methods is on the call stack. That covers what its events do to the local scout on this
    /// game, including the knock-downs, pushes and faints it sends (the game runs a scout's own messages at
    /// once). Only asked where a rule would change something, since it reads the stack.
    /// </summary>
    internal static bool Causing
    {
        get
        {
            if (assembly == null || !(ApocalypseActive || ModWeatherRunning))
            {
                return false;
            }
            StackFrame[] frames = new StackTrace(1, false).GetFrames();
            if (frames == null)
            {
                return false;
            }
            foreach (StackFrame frame in frames)
            {
                Type type = frame.GetMethod()?.DeclaringType;
                if (type != null && type.Assembly == assembly)
                {
                    return true;
                }
            }
            return false;
        }
    }

    private static ConditionalWeakTable<GameObject, StrongBox<bool>> spawned = new ConditionalWeakTable<GameObject, StrongBox<bool>>();

    /// <summary>
    /// Spawned by one of TFA's events, on every player's game: TFA tags what its events spawn over the
    /// network ("FA_..."), or builds it with components of its own (other than those it adds to the
    /// game's own objects, <see cref="OnGameObjects"/>). Looked up the object and its parents, never
    /// past a scout (TFA equips the scouts too); remembered per object.
    /// </summary>
    internal static bool Spawned(Component component)
    {
        if (assembly == null || broken || component == null)
        {
            return false;
        }
        GameObject self = component.gameObject;
        if (spawned.TryGetValue(self, out StrongBox<bool> known))
        {
            return known.Value;
        }
        bool result = false;
        for (Transform level = self.transform; level != null && !result; level = level.parent)
        {
            if (level.GetComponent<Character>() != null)
            {
                break;
            }
            foreach (MonoBehaviour behaviour in level.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null)
                {
                    continue;
                }
                if (behaviour is PhotonView view && Tagged(view))
                {
                    result = true;
                    break;
                }
                if (behaviour.GetType().Assembly == assembly && Array.IndexOf(OnGameObjects, behaviour.GetType().FullName) < 0)
                {
                    result = true;
                    break;
                }
            }
        }
        spawned.Add(self, new StrongBox<bool>(result));
        return result;
    }

    /// <summary>
    /// Spawned over the network by one of TFA's events (tagged "FA_..."), on every player's game. For
    /// what is a scout itself, such as the zombies of its Blood Moon, which <see cref="Spawned"/> does
    /// not look into.
    /// </summary>
    internal static bool Tagged(PhotonView view)
    {
        return assembly != null && !broken && view != null && view.InstantiationData is object[] data && data.Length > 0
            && data[0] is string tag && tag.StartsWith(SpawnTag, StringComparison.Ordinal);
    }

    /// <summary>
    /// TFA's Ice Fog is drawing its fog with the game's fog sphere: while it runs it takes the rising
    /// fog's sphere over, in its own colour and with its own cold.
    /// </summary>
    /// <summary>
    /// The boarding pass leaves FINAL ASCENT: TFA switches the TerrainRandomiser mod back on if it switched
    /// it off for FINAL ASCENT (its own left arrow does this; the wrap-around to Tenderfoot is this mod's).
    /// </summary>
    internal static void LeaveFinalAscent()
    {
        if (terrainFailed || terrainComeBack == null || !Present)
        {
            return;
        }
        try
        {
            terrainComeBack.Invoke(null, null);
        }
        catch (Exception ex)
        {
            terrainFailed = true;
            Plugin.Log.LogWarning("Could not hand the TerrainRandomiser mod back to True Final Ascent: " + ex.GetBaseException().Message);
        }
    }

    internal static bool IceFogDrawing => iceFogDrawing != null && Guard(() => (bool)iceFogDrawing.GetValue(null), false);

    // ---- FINAL ASCENT ----

    internal static bool TryGetFinalAscentPlace(out Place place)
    {
        place = Place.Unknown;
        if (!FinalAscentActive)
        {
            return false;
        }
        string name = Guard(() => placeCurrent.Invoke(null, null)?.ToString(), null);
        // TFA 3.0 named its places in English; before, the lower ones had German names.
        switch (name)
        {
            case "Shore":
            case "Kueste": place = Place.Shore; break;
            case "Tropics":
            case "Tropen": place = Place.Tropics; break;
            case "Roots":
            case "Wurzeln": place = Place.Roots; break;
            case "Alpine":
            case "Schnee": place = Place.Alpine; break;
            case "Mesa": place = Place.Mesa; break;
            case "Caldera": place = Place.Caldera; break;
            case "Kiln": place = Place.Kiln; break;
            case "Gloom": place = Place.Gloom; break;
            case "Citadel": place = Place.Citadel; break;
            case "Peak": place = Place.Peak; break;
            case "Nadir": place = Place.Nadir; break;
            default: return false;
        }
        return true;
    }

    /// <summary>
    /// How far the climb has come for the cold nights, counted the way the game's night cold counts it.
    /// FINAL ASCENT has ten progress points instead of five, so the game's "before point 3" would end
    /// the nights at its Alpine; TFA (2.9 on) counts there in segments instead, which ends them at its
    /// Mesa's campfire, where its Caldera begins.
    /// </summary>
    internal static int ColdNightReached(MountainProgressHandler progress)
    {
        int reached = progress.maxProgressPointReached;
        if (!FinalAscentActive || coldNightReached == null)
        {
            return reached;
        }
        // Where another mod's biome has mild nights, TFA's nights are not cold there: as if past them.
        if (mildNights != null && Guard(() => (bool)mildNights.GetValue(null), false))
        {
            return int.MaxValue;
        }
        return Guard(() => (int)coldNightReached.Invoke(null, new object[] { progress }), reached);
    }

    // ---- The Builder (TFA 3.0 on) ----

    /// <summary>
    /// A block of the Builder's climb. The Builder packs its blocks into the game's five segments (the next one only when a
    /// block lies higher on the regular mountain), so the game's segment no longer tells the block; repeated biomes are copies.
    /// </summary>
    internal sealed class Stage
    {
        /// <summary>The block's kind ("Shore", "Tropics", "Roots", "Alpine", "Mesa", "Caldera", "Kiln", "Gloom", "Citadel", "Nadir").</summary>
        internal string Kind;
        internal GameObject Root;
        /// <summary>The block's ground, as a segment's on a regular map.</summary>
        internal GameObject Terrain;
        /// <summary>
        /// The campfire the scouts go on from (none for the last block): the one at its end, or where the next block has a
        /// camp of its own, that camp. The campfire at the end of its own ground (where TFA keeps its first one then). And
        /// the campfire the scouts come into it from (none for the first).
        /// </summary>
        internal GameObject Campfire;
        internal GameObject Exit;
        internal GameObject Before;
        internal bool First;
    }

    private static int builderFrame = -1;
    private static bool builderCached;

    /// <summary>The climb is a Builder climb (asked at most once a frame).</summary>
    internal static bool BuilderActive
    {
        get
        {
            if (chainBuild == null || !Present || broken)
            {
                return false;
            }
            if (builderFrame != Time.frameCount)
            {
                builderFrame = Time.frameCount;
                builderCached = Guard(() => (bool)chainBuild.GetValue(null), false);
            }
            return builderCached;
        }
    }

    private static int stagesFrame = -1;
    private static List<Stage> stagesCached = new List<Stage>();

    /// <summary>The Builder's blocks in the climb's order (none outside a Builder climb); read at most once a frame.</summary>
    internal static List<Stage> Stages()
    {
        if (!BuilderActive)
        {
            return new List<Stage>();
        }
        if (stagesFrame == Time.frameCount)
        {
            return stagesCached;
        }
        stagesFrame = Time.frameCount;
        stagesCached = Guard(() =>
        {
            List<(int order, Stage stage)> read = new List<(int, Stage)>();
            foreach (object raw in (IEnumerable)allStages.Invoke(null, null))
            {
                if (raw != null)
                {
                    read.Add(((int)stageOrdinal.GetValue(raw), Read(raw)));
                }
            }
            read.Sort((a, b) => a.order.CompareTo(b.order));
            List<Stage> stages = read.Select(r => r.stage).ToList();
            for (int i = 0; i < stages.Count; i++)
            {
                stages[i].First = i == 0;
                stages[i].Before = i > 0 ? stages[i - 1].Campfire : null;
            }
            return stages;
        }, new List<Stage>());
        return stagesCached;
    }

    private static Stage Read(object raw) => new Stage
    {
        Kind = stageKind.GetValue(raw)?.ToString() ?? "",
        Root = stageRoot.GetValue(raw) as GameObject,
        Terrain = stageTerrain.GetValue(raw) as GameObject,
        Campfire = stageCampfire.GetValue(raw) as GameObject,
        Exit = stageExit.GetValue(raw) as GameObject,
    };

    /// <summary>The Builder block the scouts are in (null outside a Builder climb).</summary>
    internal static Stage CurrentStage
    {
        get
        {
            if (!BuilderActive)
            {
                return null;
            }
            object raw = Guard(() => currentStage.GetValue(null), null);
            GameObject terrain = raw != null ? Guard(() => stageTerrain.GetValue(raw) as GameObject, null) : null;
            return terrain != null ? Stages().FirstOrDefault(s => s.Terrain == terrain) : null;
        }
    }

    /// <summary>The peak of a Builder climb, which TFA puts after the chain's last block (null outside a Builder climb).</summary>
    internal static Transform BuilderSummit
    {
        get
        {
            if (summitKeeper == null || !BuilderActive)
            {
                return null;
            }
            Component peak = Guard(() => summitKeeper.GetValue(null) as Component, null);
            return peak != null ? peak.transform : null;
        }
    }

    /// <summary>The Builder block a part of the map belongs to (its ground, or something on it); null outside a Builder climb.</summary>
    internal static Stage StageOf(Transform part)
    {
        if (part == null)
        {
            return null;
        }
        List<Stage> stages = Stages();
        return stages.FirstOrDefault(s => s.Terrain != null && part.IsChildOf(s.Terrain.transform))
            ?? stages.FirstOrDefault(s => s.Root != null && part.IsChildOf(s.Root.transform));
    }

    /// <summary>Most of the players rest at one of the Builder's rest stops: the weather holds still meanwhile.</summary>
    internal static bool RestPaused => restPaused != null && FinalAscentActive && Guard(() => (bool)restPaused.GetValue(null), false);

    /// <summary>The Builder's rest stops spare this scout from the weather and the sun right now (all rest, or they are at one).</summary>
    internal static bool RestSpares(Character character) => restSpares != null && character != null && FinalAscentActive
        && Guard(() => (bool)restSpares.Invoke(null, new object[] { character }), false);

    /// <summary>
    /// What a ray towards the sun meets first. The Builder walls its blocks off with invisible walls that would shade the
    /// sun; TFA's look at the sun passes through them.
    /// </summary>
    internal static RaycastHit SunLineCheck(Vector3 from, Vector3 to, HelperFunctions.LayerType layer)
    {
        if (sunLineCheck != null && FinalAscentActive)
        {
            object hit = Guard(() => sunLineCheck.Invoke(null, new object[] { from, to, layer, 0f, QueryTriggerInteraction.Ignore }), null);
            if (hit is RaycastHit found)
            {
                return found;
            }
        }
        return HelperFunctions.LineCheck(from, to, layer);
    }

    /// <summary>Whether a collider is one of the Builder's invisible walls between its blocks: nothing there to see, so what flies passes it.</summary>
    internal static bool BuilderWall(Collider collider) => builderWall != null && collider != null && BuilderActive
        && Guard(() => builderWall(collider.transform), false);

    /// <summary>How big TFA has made a scout, to the game's size (its Mini and Mega sizes, in its Apocalypse or a run of a mod's weather); 1 without TFA.</summary>
    internal static float SizeOf(Character character) => sizeOf != null && character != null && Present
        ? Mathf.Clamp(Guard(() => sizeOf(character), 1f), 0.1f, 10f)
        : 1f;

    internal static void Reset()
    {
        spawned = new ConditionalWeakTable<GameObject, StrongBox<bool>>();
    }
}
