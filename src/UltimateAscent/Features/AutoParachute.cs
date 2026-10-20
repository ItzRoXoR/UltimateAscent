using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateAscent.Core;
using UnityEngine;
using Zorro.Core;
using Object = UnityEngine.Object;

namespace UltimateAscent.Features;

/// <summary>
/// The Auto-Parachute, the game's cut item, comes back as something to wear in the backpack slot
/// (slot 4) instead of a backpack. Worn, it opens by itself after 1.5 s of falling (the game's own
/// parachute, which the game still has on every scout) and saves you from the fall. It holds 3
/// charges (shown on its slot); from ascent 14 each save takes 2 (multi-use items lose 2 uses per
/// use), so it saves twice. With the last charge it is gone.
/// <para>It spawns where the Jetpack does, as rare (Mythic), on every ascent (the config's "New Items"
/// switch) and in custom runs (its own switch on the Custom Run screen).</para>
/// <para>The game knows four things to wear in that slot (backpack, fanny pack, jetpack, rocket pack),
/// each with its own model on the scout's back: the parachute is a fifth kind, built from the Jetpack
/// with the cut parachute's pack, and wears a copy of the Jetpack's back model with that pack.</para>
/// </summary>
internal static class AutoParachute
{
    internal const string PrefabName = "AutoParachute";
    internal const ushort ItemId = 9001;
    internal const BackpackSlot.BackpackType Type = (BackpackSlot.BackpackType)5;
    internal const int Charges = 3;

    /// <summary>A save from ascent 14 takes this many charges (multi-use items lose 2 uses per use there).</summary>
    internal const int WorseItemsCost = 2;

    private const string JetpackPrefab = "Jetpack";
    private const string CutParachutePrefab = "Parachute";
    private const string PackChild = "PChute";
    private const string ParachuteTag = "Parachute";

    /// <summary>How much faster the canopy folds away after landing than in the game, which takes it away after 2 s.</summary>
    internal const float FoldSpeed = 3f;
    internal const float GameFoldSeconds = 2f;

    /// <summary>The pack on the back, as large against the back model as against the item.</summary>
    private const float BackScale = 0.8f;

    private static Backpack template;
    private static Mesh packMesh;
    private static Material[] packMaterials;
    private static readonly ConditionalWeakTable<CharacterBackpackHandler, BackpackOnBackVisuals> onBack = new ConditionalWeakTable<CharacterBackpackHandler, BackpackOnBackVisuals>();

    internal static Item Template => template;

    internal static bool IsParachute(Item item) => item != null && template != null && item.itemID == ItemId;

    /// <summary>The scout wears the parachute.</summary>
    internal static bool Wears(Character character)
    {
        return character != null && character.player != null && character.player.backpackSlot != null && character.player.backpackSlot.backpackType == Type;
    }

    /// <summary>Charges left on a parachute's data (a parachute that never had its uses set up is full).</summary>
    internal static int ChargesLeft(ItemInstanceData data)
    {
        return data != null && data.TryGetDataEntry(DataEntryKey.ItemUses, out OptionableIntItemData uses) && uses.HasData ? uses.Value : Charges;
    }

    private static void SetCharges(ItemInstanceData data, int left)
    {
        if (data == null)
        {
            return;
        }
        OptionableIntItemData uses = data.TryGetDataEntry(DataEntryKey.ItemUses, out OptionableIntItemData found) ? found : data.RegisterNewEntry<OptionableIntItemData>(DataEntryKey.ItemUses);
        uses.HasData = true;
        uses.Value = left;
        FloatItemData shown = data.TryGetDataEntry(DataEntryKey.UseRemainingPercentage, out FloatItemData had) ? had : data.RegisterNewEntry<FloatItemData>(DataEntryKey.UseRemainingPercentage);
        shown.Value = Mathf.Clamp01((float)left / Charges);
    }

    /// <summary>What one save costs here and now.</summary>
    internal static int Cost => Rules.Enabled(Lvl.WorseItems) ? WorseItemsCost : 1;

    /// <summary>Spawns in this run: on every ascent with the config's "New Items" on (custom runs: its own switch).</summary>
    private static bool Allowed => NewItems.Available;

    /// <summary>The parachute: the Jetpack (a thing to wear in slot 4) with the cut parachute's pack, name and icon.</summary>
    internal static void Build()
    {
        if (!ItemDatabase.TryGetItem(JetpackPrefab, out Item jetpack) || !(jetpack is Backpack) || !ItemDatabase.TryGetItem(CutParachutePrefab, out Item cut) || cut == null)
        {
            Plugin.Log.LogWarning("The Jetpack or the cut Auto-Parachute is missing from the item database; the Auto-Parachute is not added.");
            return;
        }
        Transform pack = cut.transform.Find(PackChild);
        MeshFilter packFilter = pack != null ? pack.GetComponent<MeshFilter>() : null;
        MeshRenderer packRenderer = pack != null ? pack.GetComponent<MeshRenderer>() : null;
        if (packFilter == null || packRenderer == null)
        {
            Plugin.Log.LogWarning("The cut Auto-Parachute has no pack model; the Auto-Parachute is not added.");
            return;
        }
        packMesh = packFilter.sharedMesh;
        packMaterials = packRenderer.sharedMaterials;

        Backpack parachute = (Backpack)ModItems.Copy(jetpack, PrefabName);
        Object.DestroyImmediate(parachute.GetComponent<JetpackItem>());
        foreach (Transform child in parachute.transform.Cast<Transform>().Where(c => c.name.StartsWith("vfx", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Object.DestroyImmediate(child.gameObject);
        }
        if (parachute.groundMesh != null)
        {
            Object.DestroyImmediate(parachute.groundMesh);
        }
        if (parachute.heldMesh != null)
        {
            Object.DestroyImmediate(parachute.heldMesh);
        }
        // The pack where the cut parachute carries it, once for the ground and once for the hands.
        parachute.groundMesh = CopyPack(pack, parachute.transform, "Pack_Ground");
        parachute.heldMesh = CopyPack(pack, parachute.transform, "Pack_Held");
        parachute.heldMesh.SetActive(false);
        parachute.colliders = parachute.GetComponentsInChildren<Collider>(true);
        parachute.mainRenderer = parachute.groundMesh.GetComponent<Renderer>();
        parachute.addtlRenderers = new[] { parachute.heldMesh.GetComponent<Renderer>() };

        parachute.backpackType = Type;
        parachute.slotCount = 0;
        parachute.totalUses = Charges;
        parachute.carryWeight = cut.carryWeight;
        parachute.mass = cut.mass;
        parachute.UIData.itemName = cut.UIData.itemName;
        parachute.UIData.icon = cut.UIData.icon;
        parachute.UIData.hasAltIcon = false;
        parachute.UIData.hideFuel = false;
        if (ModItems.Register(parachute, ItemId))
        {
            template = parachute;
        }
    }

    private static GameObject CopyPack(Transform pack, Transform parent, string name)
    {
        GameObject copy = Object.Instantiate(pack.gameObject, parent);
        copy.name = name;
        copy.transform.localPosition = pack.localPosition;
        copy.transform.localRotation = pack.localRotation;
        copy.transform.localScale = pack.localScale;
        foreach (MeshCollider collider in copy.GetComponentsInChildren<MeshCollider>(true))
        {
            collider.convex = true;
        }
        return copy;
    }

    /// <summary>Dropping it, or a backpack wheel showing it, asks for the prefab of its kind by name.</summary>
    [HarmonyPatch(typeof(BackpackSlot), nameof(BackpackSlot.GetPrefabName))]
    private static class PrefabOfItsKind
    {
        private static void Postfix(BackpackSlot __instance, ref string __result)
        {
            if (__instance.backpackType == Type)
            {
                __result = PrefabName;
            }
        }
    }

    /// <summary>The wheel's "wear it" slice loads the prefab of the kind on the ground by the kind's name.</summary>
    [HarmonyPatch(typeof(BackpackWheelSlice), nameof(BackpackWheelSlice.SharedInit))]
    private static class WheelLoadsItsKind
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo load = typeof(Resources).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Resources.Load) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string))
                .MakeGenericMethod(typeof(GameObject));
            MethodInfo ours = AccessTools.Method(typeof(AutoParachute), nameof(LoadKind));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(load))
                {
                    instruction.operand = ours;
                }
                yield return instruction;
            }
        }
    }

    private static GameObject LoadKind(string path)
    {
        return template != null && path == "0_Items/" + Type ? template.gameObject : Resources.Load<GameObject>(path);
    }

    /// <summary>Every scout gets a back model for the parachute: a copy of the Jetpack's with the parachute's pack.</summary>
    [HarmonyPatch(typeof(CharacterBackpackHandler), nameof(CharacterBackpackHandler.Awake))]
    private static class BackModel
    {
        private static void Postfix(CharacterBackpackHandler __instance)
        {
            if (packMesh == null || __instance.jetpackVisuals == null)
            {
                return;
            }
            BackpackOnBackVisuals source = __instance.jetpackVisuals;
            GameObject copy = Object.Instantiate(source.gameObject, source.transform.parent);
            copy.name = "OnBack_" + PrefabName;
            foreach (Peak.Jetpack jet in copy.GetComponentsInChildren<Peak.Jetpack>(true))
            {
                Object.DestroyImmediate(jet);
            }
            foreach (Transform child in copy.transform.Cast<Transform>().Where(c => c.name.StartsWith("vfx", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                Object.DestroyImmediate(child.gameObject);
            }
            BackpackOnBackVisuals visuals = copy.GetComponent<BackpackOnBackVisuals>();
            visuals.backpackType = Type;
            visuals.slotCount = 0;
            foreach (MeshFilter filter in visuals.view.GetComponentsInChildren<MeshFilter>(true))
            {
                filter.sharedMesh = packMesh;
                filter.transform.localScale *= BackScale;
                if (filter.TryGetComponent(out MeshRenderer renderer))
                {
                    renderer.sharedMaterials = packMaterials;
                }
                if (filter.TryGetComponent(out MeshCollider collider))
                {
                    collider.sharedMesh = packMesh;
                }
            }
            visuals.InitRenderers();
            visuals.SetViewActive(false);
            onBack.Remove(__instance);
            onBack.Add(__instance, visuals);
        }
    }

    [HarmonyPatch(typeof(CharacterBackpackHandler), "get_" + nameof(CharacterBackpackHandler.activeBackpackVisuals))]
    private static class WornModel
    {
        private static void Postfix(CharacterBackpackHandler __instance, ref BackpackOnBackVisuals __result)
        {
            if (__instance.character != null && Wears(__instance.character) && onBack.TryGetValue(__instance, out BackpackOnBackVisuals visuals) && visuals != null)
            {
                __result = visuals;
            }
        }
    }

    /// <summary>Shows the scout's own back model as well (the in-game test takes a picture of it from behind).</summary>
    internal static bool ShowOwnBackModel;

    /// <summary>The game shows and hides only its four back models; the parachute's follows the same rule.</summary>
    [HarmonyPatch(typeof(CharacterBackpackHandler), nameof(CharacterBackpackHandler.LateUpdate))]
    private static class HideModel
    {
        private static void Postfix(CharacterBackpackHandler __instance)
        {
            if (!onBack.TryGetValue(__instance, out BackpackOnBackVisuals visuals) || visuals == null || __instance.character == null)
            {
                return;
            }
            Character character = __instance.character;
            bool slotSelected = __instance.characterItems.currentSelectedSlot.IsSome && __instance.characterItems.currentSelectedSlot.Value == 3;
            bool shown = Wears(character) && !slotSelected && (!character.photonView.IsMine || MainCameraMovement.IsSpectating || ShowOwnBackModel);
            visuals.SetViewActive(shown);
        }
    }

    /// <summary>Behind another scout: "Name's Auto-Parachute".</summary>
    [HarmonyPatch(typeof(BackpackOnBackVisuals), nameof(BackpackOnBackVisuals.GetName))]
    private static class BackModelName
    {
        private static void Postfix(BackpackOnBackVisuals __instance, ref string __result)
        {
            if (__instance.backpackType == Type && template != null && __instance.character != null)
            {
                __result = LocalizedText.GetText("SOMEONESPACK").Replace("@", LocalizedText.GetText("NAME_" + template.UIData.itemName)).Replace("#", __instance.character.characterName);
            }
        }
    }

    /// <summary>
    /// The backpack wheel of one on the ground: "Wear Auto-Parachute" (the game names the kind on its wear slice by
    /// the kind's name key, which a fifth kind does not have: it showed "loc: name_5").
    /// </summary>
    [HarmonyPatch(typeof(BackpackWheel), nameof(BackpackWheel.Hover))]
    private static class WheelName
    {
        private static void Postfix(BackpackWheel __instance, BackpackWheelSlice.SliceData sliceData)
        {
            if (__instance.backpackType == Type && template != null && sliceData.isBackpackWear && sliceData.backpackReference.type != BackpackReference.BackpackType.Equipped && __instance.chosenItemText != null)
            {
                __instance.chosenItemText.text = LocalizedText.GetText("WEAR").Replace("#", LocalizedText.GetText("NAME_" + template.UIData.itemName));
            }
        }
    }

    /// <summary>
    /// Once the scout lands, the canopy folds away 3 times as fast: the game plays its fold animation and takes it
    /// away after 2 s, which looked too long. Every scout's game does this with its own copy.
    /// </summary>
    [HarmonyPatch(typeof(Parachute), nameof(Parachute.Close))]
    private static class QuickFold
    {
        private static bool Prefix(Parachute __instance)
        {
            if (!__instance._closing && __instance.gameObject.activeInHierarchy)
            {
                __instance._closing = true;
                __instance.StartCoroutine(Fold(__instance));
            }
            return false;
        }

        private static System.Collections.IEnumerator Fold(Parachute parachute)
        {
            Animator animator = parachute.GetComponent<Animator>();
            animator.SetTrigger("Die");
            animator.speed = FoldSpeed;
            yield return new WaitForSeconds(GameFoldSeconds / FoldSpeed);
            animator.speed = 1f;
            parachute.gameObject.SetActive(false);
            parachute._closing = false;
        }
    }

    /// <summary>Opening again (which stops a fold half way) plays at the normal speed.</summary>
    [HarmonyPatch(typeof(Parachute), nameof(Parachute.Open))]
    private static class OpenAtNormalSpeed
    {
        private static void Prefix(Parachute __instance)
        {
            if (__instance.TryGetComponent(out Animator animator))
            {
                animator.speed = 1f;
            }
        }
    }

    /// <summary>Worn with charges left, it is the scout's parachute (the game only looks in the hands, pockets and the backpack).</summary>
    [HarmonyPatch(typeof(CharacterData), nameof(CharacterData.UpdateHasParachute))]
    private static class WornParachute
    {
        private static void Postfix(CharacterData __instance)
        {
            Character character = __instance.character;
            if (!__instance.hasParachute && Wears(character) && ChargesLeft(character.player.backpackSlot.data) > 0)
            {
                __instance.hasParachute = true;
            }
        }
    }

    /// <summary>
    /// A parachute opening reaches every client: each one takes the charge off that scout's worn parachute
    /// (unless the game opened one carried in the hands or the pockets). The scout's own game takes the
    /// emptied parachute off.
    /// </summary>
    [HarmonyPatch(typeof(CharacterBalloons), nameof(CharacterBalloons.RPC_SetParachute))]
    private static class SpendCharge
    {
        private static void Prefix(CharacterBalloons __instance, out bool __state)
        {
            __state = __instance._isParachuteOpen;
        }

        private static void Postfix(CharacterBalloons __instance, bool open, bool __state)
        {
            Character character = __instance.character;
            if (!open || __state || !Wears(character) || CarriesGameParachute(character))
            {
                return;
            }
            ItemInstanceData data = character.player.backpackSlot.data;
            int left = Mathf.Max(0, ChargesLeft(data) - Cost);
            SetCharges(data, left);
            Plugin.Debug($"{character.characterName}'s Auto-Parachute opened: {left} of {Charges} charges left.");
            if (left == 0 && character.IsLocal)
            {
                character.player.EmptySlot(Optionable<byte>.Some(3));
            }
        }
    }

    private static bool CarriesGameParachute(Character character)
    {
        if (character.data.currentItem != null && character.data.currentItem.CompareTag(ParachuteTag))
        {
            return true;
        }
        return character.player.itemSlots.Any(slot => !slot.IsEmpty() && slot.prefab != null && slot.prefab.CompareTag(ParachuteTag));
    }

    /// <summary>Regular runs: only with the config's "New Items" on (on every ascent).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.IsValidToSpawn))]
    private static class OnlyWithNewItems
    {
        private static void Postfix(Item __instance, ref bool __result)
        {
            if (__result && !Allowed && IsParachute(__instance))
            {
                __result = false;
            }
        }
    }
}
