using UnityEngine;

// All kinds of effects an upgrade can have.
// IMPORTANT: only add new types at the END of this list.
// Unity saves each type as a number, so reordering would change existing upgrades.
public enum UpgradeType
{
    BonusChipsPerWin,
    ExtraGuesses,
    TieReward,
    RoundEndInterest,
    LossReduction,
    WinPayoutPercent,
    BetIncrease,
    ExtraBetLevel,
    MaxHealth,            // Extra max health
    BossLifesteal,        // Percent of boss damage dealt that heals the player
    BossShield,           // Number of boss attacks blocked completely
    ParryRewardPercent,   // Extra percent on perfect parry rewards
    PerfectWindowMs       // Extra milliseconds for the perfect parry window
}

// How rare an upgrade is. Rarer upgrades appear less often.
// IMPORTANT: only add new rarities at the END of this list.
public enum UpgradeRarity
{
    Common,
    Rare,
    Legendary
}

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "Casino/Upgrade")]
public class Upgrade : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private UpgradeType type;
    [SerializeField] private int value;
    [SerializeField] private UpgradeRarity rarity;

    // If true, each extra copy of this upgrade is worth less than the previous one
    [SerializeField] private bool diminishingStacks = true;

    // If true, only chibis that can perfect parry are offered this upgrade
    [SerializeField] private bool requiresPerfectParry;

    public string DisplayName => displayName;
    public string Description => description;
    public UpgradeType Type => type;
    public int Value => value;
    public UpgradeRarity Rarity => rarity;
    public bool HasDiminishingStacks => diminishingStacks;
    public bool RequiresPerfectParry => requiresPerfectParry;
}