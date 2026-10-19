using UnityEngine;

// IMPORTANT: only add new types at the END of this list.
public enum UpgradeType
{
    BonusChipsPerWin,
    ExtraGuesses,
    TieReward,
    RoundEndInterest,
    LossReduction,
    WinPayoutPercent,
    BetIncrease,
    ExtraBetLevel 
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

    public string DisplayName => displayName;
    public string Description => description;
    public UpgradeType Type => type;
    public int Value => value;
    public UpgradeRarity Rarity => rarity;
}