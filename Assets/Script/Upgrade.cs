using UnityEngine;

// All kinds of effects an upgrade can have.
// IMPORTANT: only add new types at the END of this list.
// Unity saves each type as a number, so reordering would change existing upgrades.
public enum UpgradeType
{
    BonusChipsPerWin,   // Flat extra chips on every correct guess
    ExtraGuesses,       // Extra guesses each round
    TieReward,          // Chips gained on a tie
    RoundEndInterest,   // Percent of current chips gained when a round is cleared
    LossReduction,      // Fewer chips lost on a wrong guess
    WinPayoutPercent,   // Percent bonus on every win
    BetIncrease         // Higher bet: bigger wins and bigger losses
}

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "Casino/Upgrade")]
public class Upgrade : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private UpgradeType type;
    [SerializeField] private int value;

    public string DisplayName => displayName;
    public string Description => description;
    public UpgradeType Type => type;
    public int Value => value;
}