using System.Collections.Generic;
using TMPro;
using UnityEngine;

// How casino chips look, shared by the player's pile and the dealer's rack, so every chip on a table matches.
// Colors by value, like a casino: white 1, red 5, green 25, black 100, purple 500.
// Each face has 8 spots on the rim and a thin ring inside, and the value printed on top.
public static class ChipLook
{
    public static readonly int[] Values = { 500, 100, 25, 5, 1 };

    public static readonly Color[] Colors =
    {
        new Color(0.55f, 0.25f, 0.85f),   // Purple 500
        new Color(0.12f, 0.12f, 0.14f),   // Black 100
        new Color(0.15f, 0.65f, 0.25f),   // Green 25
        new Color(0.85f, 0.15f, 0.15f),   // Red 5
        new Color(0.92f, 0.92f, 0.92f)    // White 1
    };

    private static readonly Dictionary<int, Material> sharedMaterials = new Dictionary<int, Material>();

    public static int IndexOf(int value)
    {
        return System.Array.IndexOf(Values, value);
    }

    // One material per value, made once and shared by every chip of that value
    public static Material GetMaterial(int value, Material baseMaterial)
    {
        if (sharedMaterials.TryGetValue(value, out Material material) && material != null)
        {
            return material;
        }

        int index = Mathf.Max(0, IndexOf(value));
        material = MakeMaterial(Colors[index], baseMaterial);
        material.name = $"Chip{value}";
        sharedMaterials[value] = material;
        return material;
    }

    public static Material MakeMaterial(Color color, Material baseMaterial)
    {
        Material material = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = Color.white;   // The texture carries the color
        material.mainTexture = MakeTexture(color, IsLight(color) ? new Color(0.2f, 0.4f, 0.9f) : Color.white);
        return material;
    }

    public static bool IsLight(Color color)
    {
        return color.r * 0.3f + color.g * 0.59f + color.b * 0.11f > 0.7f;
    }

    // A round chip face drawn pixel by pixel: 8 spots on the rim and a thin ring inside
    public static Texture2D MakeTexture(Color baseColor, Color spotColor)
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "ChipFace",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float radius = Mathf.Sqrt(u * u + v * v);
                float angle = Mathf.Repeat(Mathf.Atan2(v, u) * Mathf.Rad2Deg, 360f);

                Color pixel = baseColor;

                if (radius > 0.78f && radius < 0.98f && Mathf.Repeat(angle, 45f) < 15f)
                {
                    pixel = spotColor;
                }
                else if (radius > 0.6f && radius < 0.66f)
                {
                    pixel = Color.Lerp(baseColor, spotColor, 0.6f);
                }

                pixels[y * size + x] = pixel;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    // The chip's value printed on its top face
    public static void AddValueLabel(TableChip chip, int value, TMP_FontAsset font)
    {
        Renderer face = chip.GetComponentInChildren<MeshRenderer>();

        if (face == null)
        {
            return;
        }

        GameObject labelObject = new GameObject("Value", typeof(RectTransform));
        labelObject.layer = face.gameObject.layer;
        labelObject.transform.SetParent(face.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 1.03f, 0f);   // Just above the top (a Unity cylinder is 2 units tall)
        labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        TextMeshPro text = labelObject.AddComponent<TextMeshPro>();

        if (font != null)
        {
            text.font = font;
        }

        text.text = value.ToString();
        text.rectTransform.sizeDelta = new Vector2(0.75f, 0.45f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.5f;
        text.fontSizeMax = 4f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        int index = IndexOf(value);
        bool lightChip = index >= 0 && IsLight(Colors[index]);
        text.color = lightChip ? new Color(0.1f, 0.1f, 0.15f) : Color.white;
        text.outlineWidth = 0.2f;
        text.outlineColor = lightChip ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
    }

    // 500s, 100s, 25s, 5s and 1s, fewest chips first (like a casino cashier)
    public static List<int> Split(int amount, int maxChips)
    {
        List<int> chips = new List<int>();

        foreach (int value in Values)
        {
            while (amount >= value && chips.Count < maxChips)
            {
                chips.Add(value);
                amount -= value;
            }
        }

        return chips;
    }
}
