using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HighLowGame : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text cardText;
    [SerializeField] private TMP_Text chipsText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private Button higherButton;
    [SerializeField] private Button lowerButton;

    [Header("Upgrade Screen")]
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private Button[] upgradeButtons;
    [SerializeField] private List<Upgrade> upgradePool;   // Every upgrade that can be offered

    [Header("Rules")]
    [SerializeField] private int startingChips = 100;
    [SerializeField] private int betAmount = 10;
    [SerializeField] private int guessesPerRound = 10;
    [SerializeField] private int baseRoundGoal = 40;
    [SerializeField] private int goalIncreasePerRound = 10;
    [SerializeField] private int upgradeChoices = 3;

    private readonly List<Upgrade> ownedUpgrades = new List<Upgrade>();

    private int chips;
    private int currentCard;   // 1 = A, 11 = J, 12 = Q, 13 = K
    private int roundNumber;
    private int targetChips;
    private int guessesLeft;
    private bool isGameOver;

    private void Start()
    {
        chips = startingChips;
        currentCard = DrawCard();

        higherButton.onClick.AddListener(() => Guess(true));
        lowerButton.onClick.AddListener(() => Guess(false));

        upgradePanel.SetActive(false);
        StartRound(1);
        resultText.text = "Higher or lower?";
        UpdateUI();
    }

    private void StartRound(int number)
    {
        roundNumber = number;
        guessesLeft = guessesPerRound + GetUpgradeTotal(UpgradeType.ExtraGuesses);

        int chipsToEarn = baseRoundGoal + goalIncreasePerRound * (number - 1);
        targetChips = chips + chipsToEarn;
    }

    private void Guess(bool guessHigher)
    {
        if (isGameOver)
        {
            return;
        }

        guessesLeft--;
        int nextCard = DrawCard();

        if (nextCard == currentCard)
        {
            int tieReward = GetUpgradeTotal(UpgradeType.TieReward);
            chips += tieReward;
            resultText.text = tieReward > 0 ? $"Tie! +{tieReward} chips" : "Tie! No chips lost.";
        }
        else
        {
            bool isHigher = nextCard > currentCard;

            if (isHigher == guessHigher)
            {
                int winAmount = betAmount + GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
                chips += winAmount;
                resultText.text = $"Correct! +{winAmount} chips";
            }
            else
            {
                int lossAmount = Mathf.Max(0, betAmount - GetUpgradeTotal(UpgradeType.LossReduction));
                chips -= lossAmount;
                resultText.text = $"Wrong! -{lossAmount} chips";
            }
        }

        currentCard = nextCard;

        if (chips >= targetChips)
        {
            ClearRound();
        }
        else if (chips <= 0 || guessesLeft <= 0)
        {
            GameOver();
        }

        UpdateUI();
    }

    private void ClearRound()
    {
        int interestPercent = GetUpgradeTotal(UpgradeType.RoundEndInterest);
        int interest = chips * interestPercent / 100;
        chips += interest;

        resultText.text = interest > 0
            ? $"Round {roundNumber} cleared! +{interest} interest"
            : $"Round {roundNumber} cleared!";

        ShowUpgradeChoices();
    }

    // Open the upgrade screen with random choices from the pool
    private void ShowUpgradeChoices()
    {
        SetGuessButtonsActive(false);
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
            button.GetComponentInChildren<TMP_Text>().text = $"<b>{choice.DisplayName}</b>\n\n{choice.Description}";

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
        SetGuessButtonsActive(true);
        resultText.text = $"Got {upgrade.DisplayName}! Higher or lower?";
        UpdateUI();
    }

    // Pick different upgrades at random (the same upgrade can still be chosen again in later rounds)
    private List<Upgrade> PickRandomUpgrades(int count)
    {
        List<Upgrade> candidates = new List<Upgrade>(upgradePool);
        List<Upgrade> result = new List<Upgrade>();

        while (result.Count < count && candidates.Count > 0)
        {
            int index = Random.Range(0, candidates.Count);
            result.Add(candidates[index]);
            candidates.RemoveAt(index);
        }

        return result;
    }

    // Add up the values of every owned upgrade of one type (so upgrades stack)
    private int GetUpgradeTotal(UpgradeType type)
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

    private void SetGuessButtonsActive(bool active)
    {
        higherButton.interactable = active;
        lowerButton.interactable = active;
    }

    private void GameOver()
    {
        isGameOver = true;
        resultText.text = $"Game over! You reached round {roundNumber}.";
        SetGuessButtonsActive(false);
    }

    private int DrawCard()
    {
        return Random.Range(1, 14);
    }

    private string CardName(int value)
    {
        return value switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => value.ToString()
        };
    }

    private void UpdateUI()
    {
        cardText.text = CardName(currentCard);
        chipsText.text = $"Chips: {chips}";
        roundText.text = $"Round {roundNumber}  |  Goal: {targetChips}  |  Guesses: {guessesLeft}";
    }
}