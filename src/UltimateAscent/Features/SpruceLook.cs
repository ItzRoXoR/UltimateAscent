using System.Collections.Generic;
using UnityEngine;

namespace UltimateAscent.Features;

/// <summary>
/// The spruce of the Roots wind, built in code in the game's item shading (as the goats are), after the user's photo of
/// a real branch: long, thin, straight needles, sage green with a paler, yellowish base, some yellower than the rest,
/// standing two to a peg on a scaly grey-brown twig and leaning towards its tip. A single needle (stuck in a scout,
/// flying in the wind) is one of them; the Spruce Branch is a twig of them with a few side shoots.
/// </summary>
internal static class SpruceLook
{
    internal const int Green = 0, Yellow = 1, Bark = 2, Scales = 3, Kinds = 4;

    private static Material[] materials;
    private static Mesh needle;

    /// <summary>The materials, by kind: the needles' green and their yellower few (each paler at its base), the twigs' bark and its darker scales.</summary>
    internal static Material[] Materials => materials ??= new[]
    {
        Make("UA_SpruceNeedle", Along("UA_SpruceNeedle", new Color(0.6f, 0.61f, 0.38f), new Color(0.35f, 0.49f, 0.35f), new Color(0.29f, 0.43f, 0.31f)), 0.25f),
        Make("UA_SpruceNeedleYellow", Along("UA_SpruceNeedleYellow", new Color(0.68f, 0.64f, 0.35f), new Color(0.54f, 0.57f, 0.31f), new Color(0.43f, 0.5f, 0.31f)), 0.25f),
        Make("UA_SpruceBark", Along("UA_SpruceBark", new Color(0.44f, 0.37f, 0.31f), new Color(0.42f, 0.35f, 0.29f), new Color(0.4f, 0.33f, 0.28f)), 0.05f),
        Make("UA_SpruceScales", Along("UA_SpruceScales", new Color(0.33f, 0.27f, 0.22f), new Color(0.36f, 0.3f, 0.25f), new Color(0.46f, 0.4f, 0.34f)), 0.05f),
    };

    private static Material Make(string name, Texture2D texture, float smooth)
    {
        Material made = new Material(Snakes.Skin()) { name = name };
        made.SetTexture("_BaseTexture", texture);
        made.SetColor("_Tint", Color.white);
        made.SetColor("_BaseColor", Color.white);
        made.SetFloat("_BaseSmooth", smooth);
        foreach (string layer in new[] { "_Texture1", "_Texture2" })
        {
            if (made.HasProperty(layer))
            {
                made.SetTexture(layer, Texture2D.whiteTexture);
            }
        }
        return made;
    }

    /// <summary>A colour running along v (0 the base, 1 the tip): the base's, then the middle's from a quarter of the way on, the tip's at the end.</summary>
    private static Texture2D Along(string name, Color from, Color middle, Color to)
    {
        Texture2D texture = new Texture2D(4, 64, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++)
        {
            float v = (y + 0.5f) / 64f;
            Color colour = v < 0.25f ? Color.Lerp(from, middle, v / 0.25f) : Color.Lerp(middle, to, (v - 0.25f) / 0.75f);
            for (int x = 0; x < 4; x++)
            {
                texture.SetPixel(x, y, colour);
            }
        }
        texture.Apply(true, true);
        return texture;
    }

    /// <summary>
    /// A needle along z, a metre long (scaled to its size as it is used): blunt at its base (z -0.5), its thickest just
    /// above it (2.2% of its length across), four-sided, tapering to a sharp tip (z 0.5); the colour runs base to tip.
    /// </summary>
    internal static Mesh Needle => needle ??= MakeNeedle();
    internal const float NeedleRadius = 0.011f;

    private static Mesh MakeNeedle()
    {
        GoatMesh mesh = new GoatMesh(1);
        GoatRing[] rings =
        {
            new GoatRing(0f, 0f, -0.5f, 0.005f, 0.005f, 2f),
            new GoatRing(0f, 0f, -0.48f, NeedleRadius, NeedleRadius, 2.3f),
            new GoatRing(0f, 0f, 0.2f, 0.009f, 0.009f, 2.3f),
            new GoatRing(0f, 0f, 0.5f, 0.0004f, 0.0004f, 2f),
        };
        mesh.Tube(Green, rings, 4, Vector3.up, 1f, true, 1);
        return mesh.Build("UA_SpruceNeedle");
    }

    /// <summary>
    /// The Spruce Branch's look, in the space of the stick it is made from (the stick's length along z through the
    /// middle of its bounds): a scaly twig thinning to its tip, bare at the butt held in the hands, three side shoots
    /// from its middle, and along all of them pegs with two long needles each, leaning towards the tip, a tuft of them at
    /// every tip.
    /// </summary>
    internal static Mesh Branch(Bounds stick)
    {
        GoatMesh mesh = new GoatMesh(Kinds);
        System.Random dice = new System.Random(43);
        float length = stick.size.z;
        Vector3 butt = new Vector3(stick.center.x, stick.center.y, stick.min.z);
        Vector3 tip = new Vector3(stick.center.x, stick.center.y, stick.max.z);
        // Sizes from its length (the game's stick is about 0.9 m): the twig 2.8 cm thick at the butt, needles about 15 cm.
        float thick = length * 0.016f;
        Shoot(mesh, butt, tip, thick, 0.16f, length * 0.17f, length * 0.011f, dice);
        float[] at = { 0.38f, 0.55f, 0.7f };
        for (int i = 0; i < at.Length; i++)
        {
            Vector3 from = Vector3.Lerp(butt, tip, at[i]);
            float around = (i % 2 == 0 ? 1f : -1f) * (80f + 20f * Dice(dice)) + 15f * i;
            Vector3 outward = Quaternion.AngleAxis(around, Vector3.forward) * Vector3.up;
            Vector3 way = (outward * 0.75f + Vector3.forward).normalized;
            Vector3 end = from + way * (length * Mathf.Lerp(0.3f, 0.2f, i / 2f));
            Shoot(mesh, from, end, thick * 0.5f, 0.05f, length * 0.13f, length * 0.011f, dice);
        }
        return mesh.Build("UA_SpruceBranch");
    }

    private static float Dice(System.Random dice) => (float)dice.NextDouble();

    /// <summary>One twig of the branch: its scaly bark, and its needles from this share of its length on.</summary>
    private static void Shoot(GoatMesh mesh, Vector3 from, Vector3 to, float thick, float bareShare, float needleLength, float spacing, System.Random dice)
    {
        Vector3 along = to - from;
        float length = along.magnitude;
        along /= length;
        List<GoatRing> rings = new List<GoatRing>();
        for (int i = 0; i <= 5; i++)
        {
            float t = i / 5f;
            float r = Mathf.Lerp(thick, thick * 0.35f, t);
            rings.Add(new GoatRing(from + along * (length * t), r, r, 2f));
        }
        mesh.Tube(Bark, rings, 8, Mathf.Abs(along.y) < 0.9f ? Vector3.up : Vector3.right, length, true, 1);
        Vector3 side = Vector3.Cross(along, Mathf.Abs(along.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 other = Vector3.Cross(along, side);
        int rows = Mathf.Max(3, Mathf.RoundToInt(length / spacing));
        const int around = 6;
        for (int row = 0; row < rows; row++)
        {
            float t = (row + 0.5f) / rows;
            Vector3 centre = from + along * (length * t);
            float r = Mathf.Lerp(thick, thick * 0.35f, t);
            for (int k = 0; k < around; k++)
            {
                float angle = 2f * Mathf.PI * (k + (row % 2) * 0.5f + (Dice(dice) - 0.5f) * 0.35f) / around;
                Vector3 outward = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                // A peg on the bark, a scale over it, leaning forward.
                Vector3 peg = centre + outward * (r * 0.85f);
                Vector3 pegTip = peg + (outward * 0.9f + along * 0.6f).normalized * (thick * 0.55f);
                mesh.Tuft(Scales, peg, pegTip, thick * 0.22f);
                if (t < bareShare)
                {
                    continue;
                }
                // Two needles from the peg, a little apart, leaning towards the tip, the shorter for the tip's end.
                float size = needleLength * Mathf.Lerp(1f, 0.75f, t) * (0.85f + 0.3f * Dice(dice));
                Vector3 lean = (outward * 0.62f + along).normalized;
                for (int pair = -1; pair <= 1; pair += 2)
                {
                    Vector3 way = Quaternion.AngleAxis(pair * (5f + 5f * Dice(dice)), along) * Quaternion.AngleAxis((Dice(dice) - 0.5f) * 14f, outward) * lean;
                    int kind = Dice(dice) < 0.15f ? Yellow : Green;
                    mesh.Tuft(kind, pegTip - outward * (thick * 0.1f), pegTip + way * size, size * 0.012f);
                }
            }
        }
        // The tip's tuft: needles all round it, forward.
        for (int k = 0; k < 10; k++)
        {
            float angle = 2f * Mathf.PI * k / 10f;
            Vector3 outward = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
            Vector3 way = (outward * 0.35f + along).normalized;
            float size = needleLength * 0.7f * (0.85f + 0.3f * Dice(dice));
            mesh.Tuft(Green, to, to + way * size, size * 0.012f);
        }
    }
}
