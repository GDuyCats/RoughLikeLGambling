using UnityEngine;

// All kinds of effects an upgrade can have
public enum UpgradeType
{
    BonusChipsPerWin,   // Extra chips on every correct guess
    ExtraGuesses,       // Extra guesses each round
    TieReward,          // Chips gained on a tie
    RoundEndInterest,   // Percent of current chips gained when a round is cleared
    LossReduction       // Fewer chips lost on a wrong guess
}

// Adds "Casino > Upgrade" to the Create menu in the Project window
[CreateAssetMenu(fileName = "NewUpgrade", menuName = "Casino/Upgrade")]
public class Upgrade : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private UpgradeType type;
    [SerializeField] private int value;

    // Read-only access for other scripts
    public string DisplayName => displayName;
    public string Description => description;
    public UpgradeType Type => type;
    public int Value => value;
}