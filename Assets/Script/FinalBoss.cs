using UnityEngine;

// The minigames in the casino.
// IMPORTANT: only add new minigames at the END of this list.
public enum MinigameType
{
    HighLow,
    Dice,
    Blackjack
}

// The boss waiting at the end of every run, with its own fighting style
[CreateAssetMenu(fileName = "NewFinalBoss", menuName = "Casino/Final Boss")]
public class FinalBoss : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private MinigameType favoriteGame;
    [SerializeField] private int maxHealth = 250;
    [SerializeField] private int jackpot = 500;

    [Header("Attack Style")]
    [SerializeField, Range(0f, 1f)] private float projectileChance = 0.5f;   // The rest are melee strikes
    [SerializeField] private int minBullets = 2;
    [SerializeField] private int maxBullets = 4;
    [SerializeField] private float bulletMinGap = 0.2f;     // Keep above twice the perfect parry window
    [SerializeField] private float bulletMaxGap = 0.6f;
    [SerializeField] private float meleeMinWindup = 0.5f;
    [SerializeField] private float meleeMaxWindup = 1.2f;
    [SerializeField, Range(0f, 1f)] private float parryChance = 0.25f;       // Chance to parry the player's reflected bullets and counters

    public string DisplayName => displayName;
    public string Description => description;
    public MinigameType FavoriteGame => favoriteGame;
    public int MaxHealth => maxHealth;
    public int Jackpot => jackpot;
    public float ProjectileChance => projectileChance;
    public int MinBullets => minBullets;
    public int MaxBullets => maxBullets;
    public float BulletMinGap => bulletMinGap;
    public float BulletMaxGap => bulletMaxGap;
    public float MeleeMinWindup => meleeMinWindup;
    public float MeleeMaxWindup => meleeMaxWindup;
    public float ParryChance => parryChance;

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