using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Photon.Pun;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core.Serizalization;

namespace UltimateAscent.Features;

/// <summary>
/// In single-player (the game's offline solo mode) the Backpack holds six items instead of four and looks
/// it: four orange pouches on its sides (two stacked on each), its side handles gone. Slots 1 and 2 hang
/// in the bag as in the game, 3 to 6 in the pouches. In multiplayer every backpack stays the game's.
/// <list type="bullet">
/// <item>Data: every backpack's contents have six slots in solo (the game's are four; the fanny pack
/// still shows two of them, the jetpack none). The game writes as many slots as there are and reads
/// four back; in solo all six are read (and a game saved with four loads into the first four).</item>
/// <item>The backpack wheel gets the game's two unused slot slices and spreads its seven slices (wearing
/// it, six slots) evenly around the circle.</item>
/// <item>The model (Resources/SixSlotBackpack.bin, built by tools/SixSlotBackpack/build_mesh.py) replaces
/// the backpack's worn and ground meshes on each backpack and on each scout's back, with the game's own
/// material; the anchors of slots 3 to 6 move into the pouches.</item>
/// <item>Its icon (Resources/SixSlotBackpack_icon.png: the game's unused six-slot backpack picture, toned
/// to its Backpack icon's colours) replaces the Backpack's while the six-slot backpack is on.</item>
/// </list>
/// The config switch "Six-Slot Backpack" turns it all off, single-player included.
/// <para>In multiplayer, with the host's config switch "Six-Slot Backpack in Multiplayer" on (off by default), the
/// Backpack lying by the crashed plane at the start of the beach is a Six-Slot Backpack instead: an item of its own
/// with the six slots, the model and the icon above; every other backpack stays the game's. Its contents travel
/// with it (all six slots, to every player), and Scout's Generosity clones it only for 100 petrification, which is
/// death.</para>
/// </summary>
internal static class SixSlotBackpack
{
    internal const int Slots = 6;

    /// <summary>The multiplayer six-slot backpack, an item of its own.</summary>
    internal const string PrefabName = "UA_SixSlotBackpack";
    internal const string ItemName = "Six-Slot Backpack";
    internal const string NameKey = "NAME_SIX-SLOT BACKPACK";
    internal const ushort ItemId = 9006;

    /// <summary>What Scout's Generosity costs for cloning it.</summary>
    internal const int ClonePetrify = 100;

    /// <summary>The spawner of the Backpack by the crashed plane at the start of the beach.</summary>
    private const string StartSpawnerParent = "crashed plane";
    private const string GameBackpack = "Backpack";

    private static Item template;

    internal static Item Template => template;

    /// <summary>The multiplayer six-slot backpack (in hands, on the ground, or its prefab in a slot).</summary>
    internal static bool IsSix(Item item) => item != null && template != null && item.itemID == template.itemID;

    /// <summary>Whether a backpack of this prefab holds six: every Backpack in single-player, the six-slot one anywhere.</summary>
    internal static bool HoldsSix(Item prefab) => IsSix(prefab) || (On && prefab is Backpack backpack && backpack.backpackType == BackpackSlot.BackpackType.Backpack);

    /// <summary>For the tests: spawned at the start in this climb.</summary>
    internal static int SpawnedAtStart;

    private const string MeshResource = "UltimateAscent.SixSlotBackpack.bin";
    private const string IconResource = "UltimateAscent.SixSlotBackpack_icon.png";
    private const string WornMeshName = "Cube";
    private const string GroundMeshName = "Backpack_Ground";

    /// <summary>The wheel's two unused slot slices, which hold slots 5 and 6.</summary>
    private static readonly string[] SpareSlices = { "UI_BackpackSegment (6)", "UI_BackpackSegment (7)" };

    /// <summary>Where the wearing slice stays (its rotation in the wheel), the slots following it
    /// counter-clockwise in this order (0-based): the game's 4, 2, 1, 3 with 6 after 4 and 5 after 3.</summary>
    private const float WearAngle = 135f;
    private static readonly int[] SlotOrder = { 3, 5, 1, 0, 2, 4 };

    /// <summary>A slice's icon is turned back by the slice's rotation plus this (the game's own layout).</summary>
    private const float IconTurn = 23.8f;

    private static Mesh worn;
    private static Mesh ground;
    private static Vector3[] pouches;
    private static bool loadFailed;

    /// <summary>For the tests: behave as in multiplayer although the game is offline.</summary>
    internal static bool AsMultiplayer;

    /// <summary>Single-player (the game's offline solo mode, which nobody can join), unless switched off.</summary>
    internal static bool On => PhotonNetwork.OfflineMode && !AsMultiplayer && (Plugin.SixSlotBackpack == null || Plugin.SixSlotBackpack.Value);

    private static Texture2D icon;
    private static bool iconFailed;

    /// <summary>The Backpack item (the game's own, which its inventory and wheel draw from) and its own icons.</summary>
    private static Item backpackPrefab;
    private static Texture2D gameIcon;
    private static Texture2D gameAltIcon;
    private static bool iconShown;

    /// <summary>The Backpack's icon is the six-slot one while the six-slot backpack is on, the game's otherwise.</summary>
    internal static void Tick()
    {
        bool show = On && Icon() != null;
        if (show == iconShown && (!show || backpackPrefab != null))
        {
            return;
        }
        if (backpackPrefab == null)
        {
            GameObject loaded = Resources.Load<GameObject>("0_Items/Backpack");
            backpackPrefab = loaded != null ? loaded.GetComponent<Item>() : null;
            if (backpackPrefab == null)
            {
                return;
            }
            gameIcon = backpackPrefab.UIData.icon;
            gameAltIcon = backpackPrefab.UIData.altIcon;
        }
        SetIcon(backpackPrefab, show);
        iconShown = show;
    }

    private static void SetIcon(Item item, bool six)
    {
        item.UIData.icon = six ? icon : gameIcon;
        if (gameAltIcon != null)
        {
            item.UIData.altIcon = six ? icon : gameAltIcon;
        }
    }

    private static Texture2D Icon()
    {
        if (icon == null && !iconFailed)
        {
            icon = ModItems.LoadTexture(IconResource, "SixSlotBackpack_icon", TextureWrapMode.Clamp);
            iconFailed = icon == null;
        }
        return icon;
    }

    /// <summary>Every backpack's contents get six slots in solo.</summary>
    [HarmonyPatch(typeof(BackpackData), nameof(BackpackData.Init))]
    private static class SixSlots
    {
        private static void Prefix(BackpackData __instance)
        {
            if (On && (__instance.itemSlots == null || __instance.itemSlots.Length < Slots))
            {
                __instance.itemSlots = new ItemSlot[Slots];
            }
        }
    }

    /// <summary>
    /// The game reads four slots back (it writes all of them): in solo, all six, and anywhere all that were written (a
    /// six-slot backpack's six; the game's own backpacks write four, read as the game reads them).
    /// </summary>
    [HarmonyPatch(typeof(BackpackData), nameof(BackpackData.DeserializeValue))]
    private static class ReadSixSlots
    {
        private static bool Prefix(BackpackData __instance, BinaryDeserializer deserializer)
        {
            InventorySyncData sync = default;
            sync.Deserialize(deserializer);
            int count = Math.Max(Math.Max(On ? Slots : 4, sync.slots.Length), __instance.itemSlots?.Length ?? 0);
            if (__instance.itemSlots == null || __instance.itemSlots.Length < count)
            {
                Array.Resize(ref __instance.itemSlots, count);
            }
            for (byte b = 0; b < __instance.itemSlots.Length; b++)
            {
                if (__instance.itemSlots[b] == null)
                {
                    __instance.itemSlots[b] = new ItemSlot(b);
                }
                if (b < sync.slots.Length)
                {
                    __instance.itemSlots[b].SetItem(ItemDatabase.TryGetItem(sync.slots[b].ItemID, out Item item) ? item : null, sync.slots[b].Data);
                }
                else
                {
                    __instance.itemSlots[b].EmptyOut();
                }
            }
            return false;
        }
    }

    /// <summary>The items shown in a backpack: the game looks at four slots, this at all of them.</summary>
    [HarmonyPatch(typeof(BackpackVisuals), nameof(BackpackVisuals.RefreshVisuals))]
    private static class ShowAllSlots
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int found = 0;
            for (int i = 0; i + 1 < code.Count; i++)
            {
                OpCode next = code[i + 1].opcode;
                if (code[i].opcode == OpCodes.Ldc_I4_4 && (next == OpCodes.Blt || next == OpCodes.Blt_S || next == OpCodes.Clt))
                {
                    code[i] = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[i]);
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SixSlotBackpack), nameof(SlotCount))));
                    found++;
                }
            }
            if (found != 1)
            {
                throw new InvalidOperationException($"the backpack's four-slot loop was not found ({found} matches); backpacks show only four items.");
            }
            return code;
        }
    }

    private static int SlotCount(BackpackVisuals visuals)
    {
        return visuals.backpackData?.itemSlots?.Length ?? 4;
    }

    /// <summary>The Backpack's wheel offers six slots in solo, and the six-slot backpack's anywhere (the game's are four).</summary>
    [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.OpenBackpackWheel))]
    private static class SixSlotWheel
    {
        private static void Prefix(BackpackReference backpackReference, ref int slotCount, BackpackSlot.BackpackType backpackType)
        {
            if (backpackType == BackpackSlot.BackpackType.Backpack && (On || IsSix(backpackReference)))
            {
                slotCount = Slots;
                Ensure(backpackReference.GetData());
            }
        }
    }

    /// <summary>A six-slot wheel has a slice for every slot: the game's two unused ones are added and all seven spread
    /// evenly around the circle (and put back the game's way for the next four-slot wheel).</summary>
    [HarmonyPatch(typeof(BackpackWheel), nameof(BackpackWheel.InitWheel))]
    private static class SevenSlices
    {
        private static void Prefix(BackpackWheel __instance, int slotCount)
        {
            if (On || slotCount >= Slots || gameAngles.Count > 0)
            {
                Arrange(__instance, slotCount >= Slots);
            }
        }
    }

    /// <summary>The backpack a reference points at is the six-slot one (in hands or on the ground, or on a scout's back).</summary>
    private static bool IsSix(BackpackReference reference)
    {
        if (!reference.exists || template == null)
        {
            return false;
        }
        if (reference.type == BackpackReference.BackpackType.Item)
        {
            return IsSix(reference.view.GetComponent<Item>());
        }
        Character wearer = reference.view.GetComponent<Character>();
        return wearer != null && IsSix(wearer.player.backpackSlot.prefab);
    }

    /// <summary>A six-slot backpack's contents get their six slots (the game made them with four).</summary>
    internal static void Ensure(BackpackData data)
    {
        if (data == null || (data.itemSlots != null && data.itemSlots.Length >= Slots))
        {
            return;
        }
        int had = data.itemSlots?.Length ?? 0;
        Array.Resize(ref data.itemSlots, Slots);
        for (byte b = (byte)had; b < Slots; b++)
        {
            data.itemSlots[b] = new ItemSlot(b);
        }
    }

    private static void Ensure(Item backpack)
    {
        if (IsSix(backpack))
        {
            Ensure(backpack.GetData<BackpackData>(DataEntryKey.BackpackData));
        }
    }

    private static void EnsureWorn(Character wearer)
    {
        BackpackSlot slot = wearer != null && wearer.player != null ? wearer.player.backpackSlot : null;
        if (slot != null && !slot.IsEmpty() && IsSix(slot.prefab))
        {
            if (!slot.data.TryGetDataEntry(DataEntryKey.BackpackData, out BackpackData data))
            {
                data = slot.data.RegisterNewEntry<BackpackData>(DataEntryKey.BackpackData);
            }
            Ensure(data);
        }
    }

    /// <summary>Wherever the game reads a six-slot backpack's contents, they have their six slots first.</summary>
    [HarmonyPatch(typeof(BackpackReference), nameof(BackpackReference.GetData))]
    private static class SixInReference
    {
        private static void Postfix(BackpackReference __instance, BackpackData __result)
        {
            if (IsSix(__instance))
            {
                Ensure(__result);
            }
        }
    }

    [HarmonyPatch(typeof(ItemBackpackVisuals), nameof(ItemBackpackVisuals.GetBackpackData))]
    private static class SixInItemVisuals
    {
        private static void Postfix(ItemBackpackVisuals __instance, BackpackData __result)
        {
            if (IsSix(__instance.GetComponent<Item>()))
            {
                Ensure(__result);
            }
        }
    }

    [HarmonyPatch(typeof(BackpackOnBackVisuals), nameof(BackpackOnBackVisuals.GetBackpackData))]
    private static class SixOnBack
    {
        private static void Prefix(BackpackOnBackVisuals __instance)
        {
            EnsureWorn(__instance.character);
        }
    }

    [HarmonyPatch(typeof(Backpack), nameof(Backpack.RPCAddItemToBackpack))]
    private static class SixWhenStashed
    {
        private static void Prefix(Backpack __instance)
        {
            Ensure(__instance);
        }
    }

    [HarmonyPatch(typeof(CharacterBackpackHandler), nameof(CharacterBackpackHandler.RPCAddItemToCharacterBackpack))]
    private static class SixWhenStashedOnBack
    {
        private static void Prefix(CharacterBackpackHandler __instance)
        {
            EnsureWorn(__instance.character);
        }
    }

    /// <summary>The six-slot backpack, a copy of the game's Backpack under its own name, icon and id (it spawns only where it is put).</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(GameBackpack, out Item backpack) || !(backpack is Backpack))
        {
            Plugin.Log.LogWarning("The Backpack is missing from the item database; there is no six-slot backpack in multiplayer.");
            return;
        }
        Item six = ModItems.Copy(backpack, PrefabName);
        six.UIData.itemName = ItemName;
        if (Icon() != null)
        {
            six.UIData.icon = icon;
            six.UIData.altIcon = icon;
        }
        if (six.TryGetComponent(out LootData loot))
        {
            loot.spawnLocations = SpawnPool.None;
            loot.excludeFromCustomRunSelection = true;
        }
        if (ModItems.Register(six, ItemId))
        {
            template = six;
        }
    }

    /// <summary>The host's multiplayer climbs (not single-player) with the host's switch on.</summary>
    private static bool SpawnsInThisClimb => template != null && (SpawnForTests
        || (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && Plugin.SixSlotBackpackMultiplayer != null && Plugin.SixSlotBackpackMultiplayer.Value));

    /// <summary>For the tests: the start's backpack is the six-slot one although the game is single-player.</summary>
    internal static bool SpawnForTests;

    /// <summary>The Backpack by the crashed plane at the start of the beach is the six-slot one (on the host's game, which spawns it).</summary>
    [HarmonyPatch(typeof(SingleItemSpawner), nameof(SingleItemSpawner.TrySpawnItems))]
    private static class AtTheStart
    {
        private static void Prefix(SingleItemSpawner __instance, out GameObject __state)
        {
            __state = null;
            if (__instance.prefab == null || __instance.prefab.name != GameBackpack || __instance.transform.parent == null
                || __instance.transform.parent.name != StartSpawnerParent || !SpawnsInThisClimb)
            {
                return;
            }
            __state = __instance.prefab;
            __instance.prefab = template.gameObject;
            SpawnedAtStart++;
        }

        private static Exception Finalizer(SingleItemSpawner __instance, GameObject __state, Exception __exception)
        {
            if (__state != null)
            {
                __instance.prefab = __state;
            }
            return __exception;
        }
    }

    /// <summary>Scout's Generosity aimed at the six-slot backpack costs 100 petrification (after every other price is set).</summary>
    [HarmonyPatch(typeof(global::Peak.Action_CloneSelectedItem), nameof(global::Peak.Action_CloneSelectedItem.RunAction))]
    [HarmonyPriority(Priority.Last)]
    private static class DeadlyToClone
    {
        private static void Prefix(global::Peak.Action_CloneSelectedItem __instance, out int[] __state)
        {
            __state = null;
            Character holder = __instance.item != null ? __instance.item.holderCharacter : null;
            if (template == null || holder == null || !holder.IsLocal || Interaction.instance == null)
            {
                return;
            }
            Interaction.instance.DoInteractableRaycasts(out IInteractible aimed, __instance.range);
            if (aimed is Item target && IsSix(target))
            {
                __state = new[] { __instance.petrify, __instance.petrifyMystical };
                __instance.petrify = ClonePetrify;
                __instance.petrifyMystical = ClonePetrify;
            }
        }

        private static Exception Finalizer(global::Peak.Action_CloneSelectedItem __instance, int[] __state, Exception __exception)
        {
            if (__state != null)
            {
                __instance.petrify = __state[0];
                __instance.petrifyMystical = __state[1];
            }
            return __exception;
        }
    }

    /// <summary>The game's angles of the wheel's slices and their icons, to put back for the other packs.</summary>
    private static readonly Dictionary<BackpackWheelSlice, (Quaternion slice, Quaternion icon)> gameAngles = new Dictionary<BackpackWheelSlice, (Quaternion, Quaternion)>();

    private static void Arrange(BackpackWheel wheel, bool sixSlots)
    {
        BackpackWheelSlice[] slices = wheel.slices;
        if (slices == null || slices.Length < 5)
        {
            return;
        }
        if (slices.Length < Slots + 1)
        {
            Transform parent = slices[1].transform.parent;
            List<BackpackWheelSlice> all = new List<BackpackWheelSlice>(slices);
            foreach (string name in SpareSlices)
            {
                BackpackWheelSlice spare = parent.Find(name)?.GetComponent<BackpackWheelSlice>();
                if (spare == null)
                {
                    Plugin.Log.LogWarning($"Six-slot backpack: the wheel has no '{name}'; it shows four slots.");
                    return;
                }
                all.Add(spare);
            }
            slices = wheel.slices = all.ToArray();
        }
        foreach (BackpackWheelSlice gone in gameAngles.Keys.Where(key => key == null).ToList())
        {
            gameAngles.Remove(gone);
        }
        foreach (BackpackWheelSlice slice in slices)
        {
            if (slice != null && !gameAngles.ContainsKey(slice))
            {
                gameAngles[slice] = (slice.transform.localRotation, slice.image != null ? slice.image.transform.localRotation : Quaternion.identity);
            }
        }
        if (!sixSlots)
        {
            foreach (BackpackWheelSlice slice in slices)
            {
                if (slice != null && gameAngles.TryGetValue(slice, out (Quaternion slice, Quaternion icon) angles))
                {
                    slice.transform.localRotation = angles.slice;
                    if (slice.image != null)
                    {
                        slice.image.transform.localRotation = angles.icon;
                    }
                }
            }
            return;
        }
        float step = 360f / (Slots + 1);
        Turn(slices[0], WearAngle);
        for (int position = 0; position < SlotOrder.Length; position++)
        {
            Turn(slices[SlotOrder[position] + 1], WearAngle + step * (position + 1));
        }
    }

    private static void Turn(BackpackWheelSlice slice, float angle)
    {
        slice.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        if (slice.image != null)
        {
            slice.image.transform.localRotation = Quaternion.Euler(0f, 0f, -angle - IconTurn);
        }
    }

    /// <summary>A Backpack item: the six-slot model, worn and on the ground, and its pouch anchors.</summary>
    [HarmonyPatch(typeof(Backpack), "Start")]
    private static class DressBackpack
    {
        private static void Postfix(Backpack __instance)
        {
            if (!(On || IsSix(__instance)) || __instance.backpackType != BackpackSlot.BackpackType.Backpack || !Load())
            {
                return;
            }
            if (On && Icon() != null && gameIcon != null)
            {
                SetIcon(__instance, true);
            }
            MeshFilter groundFilter = Filter(__instance.groundMesh, GroundMeshName);
            if (groundFilter != null)
            {
                groundFilter.sharedMesh = ground;
            }
            MeshFilter wornFilter = Filter(__instance.heldMesh, WornMeshName);
            if (wornFilter != null)
            {
                wornFilter.sharedMesh = worn;
                PlaceAnchors(__instance.GetComponent<BackpackVisuals>(), wornFilter.transform);
            }
        }
    }

    /// <summary>A scout's back as it was first seen: the game's worn mesh and where its slots hang.</summary>
    private sealed class WornLook
    {
        internal MeshFilter filter;
        internal Mesh gameMesh;
        internal Vector3[] anchors;
        internal bool six;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BackpackOnBackVisuals, WornLook> wornLooks =
        new System.Runtime.CompilerServices.ConditionalWeakTable<BackpackOnBackVisuals, WornLook>();

    /// <summary>
    /// A scout's back: the six-slot model and its pouch anchors while the backpack worn holds six (every Backpack in
    /// solo, the six-slot one anywhere), the game's otherwise; it follows the backpack the scout wears.
    /// </summary>
    [HarmonyPatch(typeof(CharacterBackpackHandler), "LateUpdate")]
    private static class DressBack
    {
        private static void Postfix(CharacterBackpackHandler __instance)
        {
            BackpackOnBackVisuals visuals = __instance.backpackVisuals;
            Player player = __instance.character != null ? __instance.character.player : null;
            if (visuals == null || player == null || player.backpackSlot == null)
            {
                return;
            }
            bool six = (On || (!player.backpackSlot.IsEmpty() && IsSix(player.backpackSlot.prefab))) && Load();
            if (!wornLooks.TryGetValue(visuals, out WornLook look))
            {
                if (!six)
                {
                    return;
                }
                MeshFilter filter = visuals.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null && f.sharedMesh.name == WornMeshName);
                if (filter == null)
                {
                    return;
                }
                look = new WornLook
                {
                    filter = filter,
                    gameMesh = filter.sharedMesh,
                    anchors = visuals.backpackSlots != null ? visuals.backpackSlots.Select(a => a != null ? a.localPosition : Vector3.zero).ToArray() : new Vector3[0],
                };
                wornLooks.Add(visuals, look);
            }
            if (look.six == six || look.filter == null)
            {
                return;
            }
            look.six = six;
            look.filter.sharedMesh = six ? worn : look.gameMesh;
            if (six)
            {
                PlaceAnchors(visuals, look.filter.transform);
            }
            else if (visuals.backpackSlots != null)
            {
                for (int i = 0; i < visuals.backpackSlots.Length && i < look.anchors.Length; i++)
                {
                    if (visuals.backpackSlots[i] != null)
                    {
                        visuals.backpackSlots[i].localPosition = look.anchors[i];
                    }
                }
            }
        }
    }

    private static MeshFilter Filter(GameObject holder, string meshName)
    {
        MeshFilter filter = holder != null ? holder.GetComponent<MeshFilter>() : null;
        return filter != null && filter.sharedMesh != null && filter.sharedMesh.name == meshName ? filter : null;
    }

    /// <summary>Slots 3 to 6 hang in the pouches (the game's anchors for them were by the side handles).</summary>
    private static void PlaceAnchors(BackpackVisuals visuals, Transform mesh)
    {
        Transform[] anchors = visuals != null ? visuals.backpackSlots : null;
        if (anchors == null || anchors.Length < Slots)
        {
            return;
        }
        for (int i = 0; i < pouches.Length; i++)
        {
            Transform anchor = anchors[i + 2];
            if (anchor != null)
            {
                anchor.position = mesh.TransformPoint(pouches[i]);
            }
        }
    }

    /// <summary>The two meshes and the pouch centres, read once.</summary>
    internal static bool Load()
    {
        if (worn != null)
        {
            return true;
        }
        if (loadFailed)
        {
            return false;
        }
        try
        {
            Read(ClimbingChalkModel.Resource(MeshResource));
            return true;
        }
        catch (Exception ex)
        {
            loadFailed = true;
            Plugin.Log.LogError($"Six-slot backpack: the model could not be read, backpacks keep the game's look: {ex}");
            return false;
        }
    }

    private static void Read(byte[] data)
    {
        using (BinaryReader reader = new BinaryReader(new MemoryStream(data)))
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "U6BP" || reader.ReadInt32() != 1)
            {
                throw new InvalidDataException("not a six-slot backpack file");
            }
            int count = reader.ReadInt32();
            Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
            for (int m = 0; m < count; m++)
            {
                string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                int vertices = reader.ReadInt32();
                Vector3[] positions = new Vector3[vertices];
                Vector3[] normals = new Vector3[vertices];
                Vector2[] uvs = new Vector2[vertices];
                for (int i = 0; i < vertices; i++)
                {
                    positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                for (int i = 0; i < vertices; i++)
                {
                    normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                for (int i = 0; i < vertices; i++)
                {
                    uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
                int[] triangles = new int[reader.ReadInt32() * 3];
                for (int i = 0; i < triangles.Length; i++)
                {
                    triangles[i] = reader.ReadUInt16();
                }
                Mesh mesh = new Mesh { name = name + " (six slots)" };
                mesh.vertices = positions;
                mesh.normals = normals;
                mesh.uv = uvs;
                // The game's backpack carries white vertex colours, which its shader multiplies in.
                mesh.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), vertices).ToArray();
                mesh.triangles = triangles;
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                mesh.hideFlags = HideFlags.HideAndDontSave;
                meshes[name] = mesh;
            }
            pouches = new Vector3[4];
            for (int i = 0; i < pouches.Length; i++)
            {
                pouches[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            if (!meshes.TryGetValue(WornMeshName, out Mesh wornMesh) || !meshes.TryGetValue(GroundMeshName, out Mesh groundMesh))
            {
                throw new InvalidDataException("the worn or the ground mesh is missing");
            }
            ground = groundMesh;
            worn = wornMesh;
        }
    }
}
