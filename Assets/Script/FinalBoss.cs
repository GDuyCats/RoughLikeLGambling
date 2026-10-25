using UnityEngine;

// The minigames in the casino.
// IMPORTANT: only add new minigames at the END of this list.
public enum MinigameType
{
    HighLow,
    Dice,
    Blackjack
}

// The attacks the giant dealer can use.
// IMPORTANT: only add new attacks at the END of this list.
public enum BossAttackType
{
    Slam,
    SlapCombo,
    Feint,
    Sweep,
    ChipFlick
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

    [Header("Attack Weights (higher = more often, 0 = never)")]
    [SerializeField] private int slamWeight = 3;
    [SerializeField] private int comboWeight = 2;
    [SerializeField] private int feintWeight = 1;
    [SerializeField] private int sweepWeight = 1;
    [SerializeField] private int chipWeight = 3;

    [Header("Attack Style")]
    [SerializeField] private int minChips = 2;
    [SerializeField] private int maxChips = 4;
    [SerializeField] private float chipMinGap = 0.2f;     // Keep above twice the perfect parry window
    [SerializeField] private float chipMaxGap = 0.6f;
    [SerializeField] private float minWindup = 0.6f;      // How long a hand hovers before slamming
    [SerializeField] private float maxWindup = 1.1f;
    [SerializeField, Range(0f, 1f)] private float parryChance = 0.25f;   // Chance to parry the player's reflected chips and counters

    public string DisplayName => displayName;
    public string Description => description;
    public MinigameType FavoriteGame => favoriteGame;
    public int MaxHealth => maxHealth;
    public int Jackpot => jackpot;
    public int MinChips => minChips;
    public int MaxChips => maxChips;
    public float ChipMinGap => chipMinGap;
    public float ChipMaxGap => chipMaxGap;
    public float MinWindup => minWindup;
    public float MaxWindup => maxWindup;
    public float ParryChance => parryChance;

    // Pick an attack at random, using the weights like raffle tickets
    public BossAttackType PickAttack()
    {
        int total = slamWeight + comboWeight + feintWeight + sweepWeight + chipWeight;

        if (total <= 0)
        {
            return BossAttackType.Slam;
        }

        int roll = Random.Range(0, total);

        if ((roll -= slamWeight) < 0) return BossAttackType.Slam;
        if ((roll -= comboWeight) < 0) return BossAttackType.SlapCombo;
        if ((roll -= feintWeight) < 0) return BossAttackType.Feint;
        if ((roll -= sweepWeight) < 0) return BossAttackType.Sweep;
        return BossAttackType.ChipFlick;
    }

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