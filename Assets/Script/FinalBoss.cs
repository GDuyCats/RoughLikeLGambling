using UnityEngine;

// The minigames in the casino.
// IMPORTANT: only add new minigames at the END of this list.
public enum MinigameType
{
    HighLow,
    Dice,
    Blackjack
}

// The boss waiting at the end of every run
[CreateAssetMenu(fileName = "NewFinalBoss", menuName = "Casino/Final Boss")]
public class FinalBoss : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private MinigameType favoriteGame;   // The boss only fights with this minigame
    [SerializeField] private int maxHealth = 250;
    [SerializeField] private int jackpot = 500;           // Chips won for beating the boss

    public string DisplayName => displayName;
    public string Description => description;
    public MinigameType FavoriteGame => favoriteGame;
    public int MaxHealth => maxHealth;
    public int Jackpot => jackpot;

    public static string GetGameName(MinigameType type)
    {
        return type switch
        {
            MinigameType.HighLow => "High-Low",
            MinigameType.Dice => "Lucky Dice",
            _ => "Blackjack"
        };
    }
}