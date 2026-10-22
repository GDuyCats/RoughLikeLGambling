using System.Collections.Generic;
using UnityEngine;

// Finds card and dice pictures by file name inside the Assets/Resources folder.
// A "static" class is never put on an object: any script can call CardArt.GetCard(...) directly.
public static class CardArt
{
    // Change these three lines if your picture files are named differently.
    // {0} and {1} are filled in by the code (for example "cardHearts" + "A").
    private const string CardPath = "Cards/card_{0}_{1}";
    private const string CardBackPath = "Cards/card_back";
    private const string DiePath = "Dices/dieWhite{0}";

    // Same order as the suits used in the minigames: 0 spades, 1 hearts, 2 diamonds, 3 clubs
    private static readonly string[] SuitNames = { "spades", "hearts", "diamonds", "clubs" };

    // Remember pictures already loaded, so each file is only searched for once
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // rank: 1 = A, 11 = J, 12 = Q, 13 = K
    public static Sprite GetCard(int rank, int suit)
    {
        string rankName = rank switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => rank.ToString("00")
        };

        return Load(string.Format(CardPath, SuitNames[suit], rankName));
    }

    public static Sprite GetCardBack()
    {
        return Load(CardBackPath);
    }

    public static Sprite GetDie(int value)
    {
        return Load(string.Format(DiePath, value));
    }

    private static Sprite Load(string path)
    {
        if (cache.TryGetValue(path, out Sprite cached))
        {
            return cached;
        }

        Sprite sprite = Resources.Load<Sprite>(path);

        if (sprite == null)
        {
            Debug.LogError($"Picture not found: Assets/Resources/{path}. Check the file name, and that it is imported as a Sprite.");
        }

        cache[path] = sprite;
        return sprite;
    }
}