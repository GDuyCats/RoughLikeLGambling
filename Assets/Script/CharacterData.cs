using System.Collections.Generic;
using UnityEngine;

// A playable chibi. Each one has its own strengths and weaknesses.
[CreateAssetMenu(fileName = "NewCharacter", menuName = "Casino/Character")]
public class CharacterData : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private Color color = Color.white;      // Placeholder look until real 3D models exist
    [SerializeField] private int maxHealthBonus;             // Can be negative for fragile characters
    [SerializeField] private bool canPerfectParry = true;    // Reflect, heal and counter in boss fights
    [SerializeField] private bool canBlock = true;           // Hold right click to halve boss damage
    [SerializeField] private List<Upgrade> startingUpgrades = new List<Upgrade>();

    public string DisplayName => displayName;
    public string Description => description;
    public Color Color => color;
    public int MaxHealthBonus => maxHealthBonus;
    public bool CanPerfectParry => canPerfectParry;
    public bool CanBlock => canBlock;
    public List<Upgrade> StartingUpgrades => startingUpgrades;
}