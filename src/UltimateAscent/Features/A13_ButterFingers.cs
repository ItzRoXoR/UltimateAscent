using UltimateAscent.Core;

namespace UltimateAscent.Features;

/// <summary>
/// Ascent 13 (More Ascents): butter fingers. Taking fall damage drops every item, including the
/// backpack. Triggered from <see cref="FallDamage"/>.
/// </summary>
internal static class A13_ButterFingers
{
    internal static void DropEverything(Character character)
    {
        if (!Rules.Owns(character) || character.refs?.items == null || character.data.dead)
        {
            return;
        }
        character.refs.items.DropAllItems(includeBackpack: true);
    }
}
