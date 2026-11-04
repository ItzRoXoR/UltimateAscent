using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The Burnt Skin (one of the mod's cosmetics, <see cref="ModCosmetics"/>; reaching the peak on ascent 23 or higher with
/// every modifier on unlocks it): a charred maroon skin, the arms, legs and body spotted with char and small glowing
/// embers. The head is left plain, so the face stays clean. The spots are the scout skin material's two free layers (the
/// game's skin uses two of its four), laid in space (triplanar: the body's own texture layout squeezes the legs into one
/// spot), from the Pain Sash's textures (the galaxy sash's smoke, the spotted sash's splatter). They go on whenever a
/// scout's skin is set, every scout's and the passport's, and come off again for any other skin.
/// </summary>
internal static class BurntSkin
{
    /// <summary>The skin's own colour: charred, dark red-brown, in the Pain Sash's colours.</summary>
    internal static readonly Color Colour = new Color(0.36f, 0.09f, 0.07f, 1f);

    private static readonly Color Char = new Color(0.03f, 0.012f, 0.01f, 1f);
    private static readonly Color Ember = new Color(1f, 0.38f, 0.05f, 1f);

    /// <summary>The skin materials the spots are on now.</summary>
    private static readonly HashSet<Material> painted = new HashSet<Material>();

    /// <summary>Puts the spots on a scout's body (or takes them off, for another skin).</summary>
    internal static void Paint(CustomizationRefs refs, bool burnt)
    {
        if (refs == null || refs.mainRenderer == null)
        {
            return;
        }
        Material skin = refs.mainRenderer.material;
        if (skin == null)
        {
            return;
        }
        painted.RemoveWhere(m => m == null);
        if (!burnt)
        {
            if (painted.Remove(skin))
            {
                for (int n = 3; n <= 4; n++)
                {
                    skin.SetColor($"_Color{n}", Color.clear);
                    skin.SetFloat($"_Triplanar{n}", 0f);
                    skin.SetFloat($"_Triplanar{n}On", 0f);
                }
            }
            return;
        }
        Material pain = PainSash.MaterialFrom(refs.sashAscentMaterials);
        Texture smoke = pain != null ? pain.GetTexture("_Texture2") : null;
        Texture splatter = pain != null ? pain.GetTexture("_Texture3") : null;
        if (smoke == null || splatter == null)
        {
            return;
        }
        // Layer windows are linear values (the textures are read as colour); the scale is the spots' size in space.
        Layer(skin, 3, smoke, Char, 0.31f, 0.41f, 2.5f);
        Layer(skin, 4, splatter, Ember, 0.6f, 0.7f, 3.5f);
        painted.Add(skin);
    }

    private static void Layer(Material m, int n, Texture texture, Color colour, float from, float to, float scale)
    {
        m.SetTexture($"_Texture{n}", texture);
        m.SetColor($"_Color{n}", colour);
        m.SetVector($"_Remap{n}", new Vector4(from, to, 0f, 0f));
        m.SetFloat($"_UV{n}", scale);
        m.SetFloat($"_Triplanar{n}", 1f);
        m.SetFloat($"_Triplanar{n}On", 1f);
    }

    private static bool IsBurnt(int skin) => skin >= 0 && skin == ModCosmetics.IndexOf(ModCosmetics.BurntSkinKey);

    /// <summary>A scout's cosmetics set (every scout's, on every player's game).</summary>
    [HarmonyPatch(typeof(CharacterCustomization), nameof(CharacterCustomization.OnPlayerDataChange))]
    private static class OnScout
    {
        private static void Postfix(CharacterCustomization __instance, PersistentPlayerData playerData)
        {
            if (playerData == null || __instance.refs == null || __instance._character == null || __instance._character.data.isScoutmaster)
            {
                return;
            }
            Paint(__instance.refs, IsBurnt(CharacterCustomization.GetSkinIndex(playerData)));
        }
    }

    /// <summary>The passport's scout in a skin.</summary>
    [HarmonyPatch(typeof(PlayerCustomizationDummy), nameof(PlayerCustomizationDummy.SetPlayerColor))]
    private static class OnPassport
    {
        private static void Postfix(PlayerCustomizationDummy __instance, int index) => Paint(__instance.refs, IsBurnt(index));
    }
}
