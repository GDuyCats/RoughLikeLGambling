using UnityEngine;

// The rule a boss changes for its round.
// IMPORTANT: only add new effects at the END of this list.
public enum BossEffect
{
    GoalIncreasePercent,   // The round's goal is this many percent higher
    FewerPlays             // The round has this many fewer plays
}

// A boss that appears in boss rounds and makes that round harder
[CreateAssetMenu(fileName = "NewBoss", menuName = "Casino/Boss")]
public class BossModifier : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private BossEffect effect;
    [SerializeField] private int value;

    public string DisplayName => displayName;
    public string Description => description;
    public BossEffect Effect => effect;
    public int Value => value;
}