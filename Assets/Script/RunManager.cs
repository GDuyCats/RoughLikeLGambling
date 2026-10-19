using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
// Both System and UnityEngine have a "Random", so we tell C# to use Unity's one
using Random = UnityEngine.Random;

// Manages everything shared by all minigames: chips, rounds, upgrades, game over
public class RunManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text chipsText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text ownedUpgradesText;
    [SerializeField] private Button restartButton;

    [Header("Upgrade Screen")]
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private Button[] upgradeButtons;
    [SerializeField] private List<Upgrade> upgradePool;

    [Header("Rarity")]
    [SerializeField] private int commonWeight = 70;
    [SerializeField] private int rareWeight = 25;
    [SerializeField] private int legendaryWeight = 5;
    [SerializeField] private Color commonColor = new Color(0.85f, 0.85f, 0.85f);
    [SerializeField] private Color rareColor = new Color(0.55f, 0.75f, 1f);
    [SerializeField] private Color legendaryColor = new Color(1f, 0.8f, 0.25f);

    [Header("Rules")]
    [SerializeField] private int startingChips = 100;
    [SerializeField] private int betAmount = 10;
    [SerializeField] private int playsPerRound = 12;
    [SerializeField] private int baseRoundGoal = 20;
    [SerializeField] private int goalIncreasePerRound = 10;
    [SerializeField] private int upgradeChoices = 3;

    // Events let minigames react without RunManager knowing which minigame is running
    public event Action RunStarted;     // A new run has begun
    public event Action StateChanged;   // Chips, round or upgrades changed

    private readonly List<Upgrade> ownedUpgrades = new List<Upgrade>();

    private int chips;
    private int roundNumber;
    private int targetChips;
    private int playsLeft;
    private bool isGameOver;

    // Minigames check this before letting the player act
    public bool CanPlay => !isGameOver && !upgradePanel.activeSelf;

    private void Start()
    {
        restartButton.onClick.AddListener(StartNewRun);
        StartNewRun();
    }

    public void StartNewRun()
    {
        chips = startingChips;
        ownedUpgrades.Clear();
        isGameOver = false;

        upgradePanel.SetActive(false);
        restartButton.gameObject.SetActive(false);

        StartRound(1);
        RunStarted?.Invoke();
        UpdateUI();
    }

    // Called by a minigame after each play. chipChange is negative for a loss.
    public void FinishPlay(int chipChange, string message)
    {
        if (!CanPlay)
        {
            return;
        }

        chips += chipChange;
        playsLeft--;
        messageText.text = message;

        if (chips >= targetChips)
        {
            ClearRound();
        }
        else if (chips <= 0 || playsLeft <= 0)
        {
            GameOver();
        }

        UpdateUI();
    }

    public void ShowMessage(string message)
    {
        messageText.text = message;
    }

    public int GetCurrentBet()
    {
        return betAmount + GetUpgradeTotal(UpgradeType.BetIncrease);
    }

    public int GetUpgradeTotal(UpgradeType type)
    {
        int total = 0;

        foreach (Upgrade upgrade in ownedUpgrades)
        {
            if (upgrade.Type == type)
            {
                total += upgrade.Value;
            }
        }

        return total;
    }

    private void StartRound(int number)
    {
        roundNumber = number;
        playsLeft = playsPerRound + GetUpgradeTotal(UpgradeType.ExtraGuesses);

        int chipsToEarn = baseRoundGoal + goalIncreasePerRound * (number - 1);
        targetChips = chips + chipsToEarn;
    }

    private void ClearRound()
    {
        int interestPercent = GetUpgradeTotal(UpgradeType.RoundEndInterest);
        int interest = chips * interestPercent / 100;
        chips += interest;

        messageText.text += interest > 0
            ? $"\nRound {roundNumber} cleared! +{interest} interest"
            : $"\nRound {roundNumber} cleared!";

        ShowUpgradeChoices();
    }

    private void GameOver()
    {
        isGameOver = true;
        messageText.text += $"\nGame over! You reached round {roundNumber}.";
        restartButton.gameObject.SetActive(true);
    }

    private void ShowUpgradeChoices()
    {
        List<Upgrade> offered = PickRandomUpgrades(upgradeChoices);

        for (int i = 0; i < upgradeButtons.Length; i++)
        {
            Button button = upgradeButtons[i];

            if (i >= offered.Count)
            {
                button.gameObject.SetActive(false);
                continue;
            }

            Upgrade choice = offered[i];
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<TMP_Text>().text =
                $"<b>{choice.DisplayName}</b>\n<size=70%>{choice.Rarity}</size>\n\n{choice.Description}";
            button.image.color = GetRarityColor(choice.Rarity);

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => ChooseUpgrade(choice));
        }

        upgradePanel.SetActive(true);
    }

    private void ChooseUpgrade(Upgrade upgrade)
    {
        ownedUpgrades.Add(upgrade);
        upgradePanel.SetActive(false);

        StartRound(roundNumber + 1);
        messageText.text = $"Got {upgrade.DisplayName}!";
        UpdateUI();
    }

    private List<Upgrade> PickRandomUpgrades(int count)
    {
        List<Upgrade> candidates = new List<Upgrade>(upgradePool);
        List<Upgrade> result = new List<Upgrade>();

        while (result.Count < count && candidates.Count > 0)
        {
            int index = PickWeightedIndex(candidates);
            result.Add(candidates[index]);
            candidates.RemoveAt(index);
        }

        return result;
    }

    private int PickWeightedIndex(List<Upgrade> candidates)
    {
        int totalWeight = 0;

        foreach (Upgrade upgrade in candidates)
        {
            totalWeight += GetRarityWeight(upgrade.Rarity);
        }

        if (totalWeight <= 0)
        {
            return Random.Range(0, candidates.Count);
        }

        int roll = Random.Range(0, totalWeight);

        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= GetRarityWeight(candidates[i].Rarity);

            if (roll < 0)
            {
                return i;
            }
        }

        return candidates.Count - 1;
    }

    private int GetRarityWeight(UpgradeRarity rarity)
    {
        return rarity switch
        {
            UpgradeRarity.Rare => rareWeight,
            UpgradeRarity.Legendary => legendaryWeight,
            _ => commonWeight
        };
    }

    private Color GetRarityColor(UpgradeRarity rarity)
    {
        return rarity switch
        {
            UpgradeRarity.Rare => rareColor,
            UpgradeRarity.Legendary => legendaryColor,
            _ => commonColor
        };
    }

    private int CountOwned(Upgrade upgrade)
    {
        int count = 0;

        foreach (Upgrade owned in ownedUpgrades)
        {
            if (owned == upgrade)
            {
                count++;
            }
        }

        return count;
    }

    private string BuildOwnedUpgradesText()
    {
        if (ownedUpgrades.Count == 0)
        {
            return "Upgrades:\nNone yet";
        }

        string text = "Upgrades:";
        List<Upgrade> listed = new List<Upgrade>();

        foreach (Upgrade upgrade in ownedUpgrades)
        {
            if (listed.Contains(upgrade))
            {
                continue;
            }

            listed.Add(upgrade);
            int count = CountOwned(upgrade);
            text += count > 1 ? $"\n{upgrade.DisplayName} x{count}" : $"\n{upgrade.DisplayName}";
        }

        return text;
    }

    private void UpdateUI()
    {
        chipsText.text = $"Chips: {chips}   Bet: {GetCurrentBet()}";
        roundText.text = $"Round {roundNumber}  |  Goal: {targetChips}  |  Plays: {playsLeft}";
        ownedUpgradesText.text = BuildOwnedUpgradesText();

        // Tell the active minigame to refresh its own UI
        StateChanged?.Invoke();
    }
}