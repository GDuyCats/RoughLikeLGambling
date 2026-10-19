using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
// Both System and UnityEngine have a "Random", so we tell C# to use Unity's one
using Random = UnityEngine.Random;

// Manages everything shared by all minigames: chips, bets, rounds, upgrades, game over
public class RunManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text chipsText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text ownedUpgradesText;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button betDownButton;
    [SerializeField] private Button betUpButton;

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
    [SerializeField] private int startingMaxBetLevel = 3;   // Bet levels available without upgrades (x1, x2, x3)
    [SerializeField] private int playsPerRound = 12;
    [SerializeField] private int baseRoundGoal = 20;
    [SerializeField] private int goalIncreasePerRound = 10;
    [SerializeField] private int upgradeChoices = 3;
    [SerializeField, Range(0f, 1f)] private float stackFalloff = 0.5f;   // Each extra copy is worth this much of the previous copy
    // Events let minigames react without RunManager knowing which minigame is running
    public event Action RunStarted;     // A new run has begun
    public event Action StateChanged;   // Chips, bet, round or upgrades changed
    public event Action RoundStarted;   // A new round has begun

    private readonly List<Upgrade> ownedUpgrades = new List<Upgrade>();

    private int chips;
    private int roundNumber;
    private int targetChips;
    private int playsLeft;
    private int playsUsedThisRound;
    private int betLevel = 1;
    private bool isGameOver;

    public bool CanPlay => !isGameOver && !upgradePanel.activeSelf;
    public int PlaysLeft => playsLeft;
    public int RoundNumber => roundNumber;
    public int Chips => chips;
    public int BetLevel => betLevel;
    public int MaxBetLevel => startingMaxBetLevel + GetUpgradeTotal(UpgradeType.ExtraBetLevel);
    public bool HasPlayedThisRound => playsUsedThisRound > 0;

    // True while a minigame is in the middle of a play (e.g. a blackjack hand)
    public bool IsMinigameBusy { get; private set; }

    private void Start()
    {
        restartButton.onClick.AddListener(StartNewRun);
        betDownButton.onClick.AddListener(() => ChangeBetLevel(-1));
        betUpButton.onClick.AddListener(() => ChangeBetLevel(1));

        StartNewRun();
    }

    public void StartNewRun()
    {
        chips = startingChips;
        ownedUpgrades.Clear();
        betLevel = 1;
        isGameOver = false;
        IsMinigameBusy = false;

        upgradePanel.SetActive(false);
        restartButton.gameObject.SetActive(false);

        StartRound(1);
        RunStarted?.Invoke();
        UpdateUI();
    }

    public void SetMinigameBusy(bool busy)
    {
        IsMinigameBusy = busy;
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
        playsUsedThisRound++;
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

    // Used when the player pays plays for something, such as switching games
    public void SpendPlays(int amount)
    {
        playsLeft = Mathf.Max(0, playsLeft - amount);
        UpdateUI();
    }

    public int GetCurrentBet()
    {
        return GetBetForLevel(betLevel);
    }

    // Base bet (plus upgrades) multiplied by the bet level
    private int GetBetForLevel(int level)
    {
        return (betAmount + GetUpgradeTotal(UpgradeType.BetIncrease)) * level;
    }

    // Bets can't change during a hand or while choosing an upgrade
    private bool CanChangeBet()
    {
        return CanPlay && !IsMinigameBusy;
    }

    private void ChangeBetLevel(int change)
    {
        if (!CanChangeBet())
        {
            return;
        }

        int newLevel = Mathf.Clamp(betLevel + change, 1, MaxBetLevel);

        // Never allow raising the bet above the chips the player has
        if (change > 0 && GetBetForLevel(newLevel) > chips)
        {
            return;
        }

        betLevel = newLevel;
        UpdateUI();
    }

    public int GetUpgradeTotal(UpgradeType type)
    {
        float total = 0f;
        List<Upgrade> counted = new List<Upgrade>();

        foreach (Upgrade upgrade in ownedUpgrades)
        {
            // Handle each different upgrade once, together with all its copies
            if (upgrade.Type != type || counted.Contains(upgrade))
            {
                continue;
            }

            counted.Add(upgrade);
            total += GetStackedValue(upgrade, CountOwned(upgrade));
        }

        return Mathf.RoundToInt(total);
    }

    // With diminishing stacks: 50 + 25 + 12.5 + ... instead of 50 + 50 + 50
    private float GetStackedValue(Upgrade upgrade, int copies)
    {
        if (!upgrade.HasDiminishingStacks)
        {
            return upgrade.Value * copies;
        }

        float total = 0f;
        float copyValue = upgrade.Value;

        for (int i = 0; i < copies; i++)
        {
            total += copyValue;
            copyValue *= stackFalloff;
        }

        return total;
    }

    private void StartRound(int number)
    {
        roundNumber = number;
        playsLeft = playsPerRound + GetUpgradeTotal(UpgradeType.ExtraGuesses);
        playsUsedThisRound = 0;

        int chipsToEarn = baseRoundGoal + goalIncreasePerRound * (number - 1);
        targetChips = chips + chipsToEarn;

        RoundStarted?.Invoke();
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
    $"<b>{choice.DisplayName}</b>\n<size=70%>{choice.Rarity}</size>\n\n{choice.Description}{BuildStackHint(choice)}";
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

        // Keep the new round's message and add the upgrade name in front
        messageText.text = $"Got {upgrade.DisplayName}!\n{messageText.text}";
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


    // Shows how much the next copy of an upgrade is worth, e.g. "Owned x1. Next copy: +25%"
    private string BuildStackHint(Upgrade upgrade)
    {
        int owned = CountOwned(upgrade);

        if (owned == 0)
        {
            return "";
        }

        float nextValue = GetStackedValue(upgrade, owned + 1) - GetStackedValue(upgrade, owned);
        return $"\n<size=70%>Owned x{owned}. Next copy: +{nextValue:0.#}{GetUnitSuffix(upgrade.Type)}</size>";
    }

    private string GetUnitSuffix(UpgradeType type)
    {
        return type switch
        {
            UpgradeType.WinPayoutPercent or UpgradeType.RoundEndInterest => "%",
            UpgradeType.ExtraGuesses => " plays",
            UpgradeType.ExtraBetLevel => " bet level",
            _ => " chips"
        };
    }

    // A summary of the player's current numbers. Bonuses at 0 are hidden.
    private string BuildStatsText()
    {
        string text = "Stats:";
        text += $"\nBet: {GetCurrentBet()} (x{betLevel} of x{MaxBetLevel})";
        text += $"\nPlays per round: {playsPerRound + GetUpgradeTotal(UpgradeType.ExtraGuesses)}";

        int winPercent = GetUpgradeTotal(UpgradeType.WinPayoutPercent);
        if (winPercent > 0)
        {
            text += $"\nWin bonus: +{winPercent}%";
        }

        int flatWinBonus = GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
        if (flatWinBonus > 0)
        {
            text += $"\nExtra chips per win: +{flatWinBonus}";
        }

        int lossReduction = GetUpgradeTotal(UpgradeType.LossReduction);
        if (lossReduction > 0)
        {
            text += $"\nLoss reduction: -{lossReduction} chips";
        }

        int tieReward = GetUpgradeTotal(UpgradeType.TieReward);
        if (tieReward > 0)
        {
            text += $"\nHigh-Low tie reward: +{tieReward} chips";
        }

        int interest = GetUpgradeTotal(UpgradeType.RoundEndInterest);
        if (interest > 0)
        {
            text += $"\nRound interest: {interest}%";
        }

        return text;
    }

    private void UpdateUI()
    {
        // If losses pushed chips below the current bet, lower the bet automatically
        while (betLevel > 1 && GetCurrentBet() > chips)
        {
            betLevel--;
        }

        chipsText.text = $"Chips: {chips}   Bet: {GetCurrentBet()} (x{betLevel})";
        roundText.text = $"Round {roundNumber}  |  Goal: {targetChips}  |  Plays: {playsLeft}";
        ownedUpgradesText.text = BuildOwnedUpgradesText();
        statsText.text = BuildStatsText();
        betDownButton.interactable = CanChangeBet() && betLevel > 1;
        betUpButton.interactable = CanChangeBet()
            && betLevel < MaxBetLevel
            && GetBetForLevel(betLevel + 1) <= chips;

        // Tell the active minigame to refresh its own UI
        StateChanged?.Invoke();
    }
}