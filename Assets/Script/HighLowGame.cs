using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HighLowGame : MonoBehaviour
{
    private const int CardCount = 13;

    [Header("UI")]
    [SerializeField] private TMP_Text cardText;
    [SerializeField] private TMP_Text chipsText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private Button higherButton;
    [SerializeField] private Button lowerButton;
    [SerializeField] private Button restartButton;

    [Header("Upgrade Screen")]
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private Button[] upgradeButtons;
    [SerializeField] private List<Upgrade> upgradePool;

    [Header("Rules")]
    [SerializeField] private int startingChips = 100;
    [SerializeField] private int betAmount = 10;
    [SerializeField] private float payoutMultiplier = 1.2f;  // Above 1 gives the player a small edge
    [SerializeField] private int guessesPerRound = 10;
    [SerializeField] private int baseRoundGoal = 40;
    [SerializeField] private int goalIncreasePerRound = 10;
    [SerializeField] private int upgradeChoices = 3;

    private readonly List<Upgrade> ownedUpgrades = new List<Upgrade>();

    private TMP_Text higherButtonText;
    private TMP_Text lowerButtonText;

    private int chips;
    private int currentCard;   // 1 = A, 11 = J, 12 = Q, 13 = K
    private int roundNumber;
    private int targetChips;
    private int guessesLeft;
    private bool isGameOver;

    private void Start()
    {
        higherButtonText = higherButton.GetComponentInChildren<TMP_Text>();
        lowerButtonText = lowerButton.GetComponentInChildren<TMP_Text>();

        higherButton.onClick.AddListener(() => Guess(true));
        lowerButton.onClick.AddListener(() => Guess(false));
        restartButton.onClick.AddListener(StartNewRun);

        StartNewRun();
    }

    // Reset everything for a fresh run
    private void StartNewRun()
    {
        chips = startingChips;
        ownedUpgrades.Clear();
        isGameOver = false;
        currentCard = DrawPlayableCard();

        upgradePanel.SetActive(false);
        restartButton.gameObject.SetActive(false);

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
                // Must be calculated before currentCard changes
                int winAmount = GetWinAmount(guessHigher);
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

        // If the revealed card is A or K, deal a fresh card for the next guess
        if (IsExtremeCard(currentCard))
        {
            currentCard = DrawPlayableCard();
            resultText.text += $"\n{CardName(nextCard)} drawn. Dealing a new card.";
        }

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

    // Number of card values that would win with this guess
    private int CountWinningCards(bool guessHigher)
    {
        return guessHigher ? CardCount - currentCard : currentCard - 1;
    }

    // Number of card values that would lose with this guess
    private int CountLosingCards(bool guessHigher)
    {
        return guessHigher ? currentCard - 1 : CardCount - currentCard;
    }

    // Riskier guesses pay more: payout is based on losing cards vs winning cards
    private int GetWinAmount(bool guessHigher)
    {
        int winningCards = CountWinningCards(guessHigher);

        if (winningCards == 0)
        {
            return 0;
        }

        int losingCards = CountLosingCards(guessHigher);
        float payout = betAmount * payoutMultiplier * losingCards / winningCards;
        int baseWin = Mathf.Max(1, Mathf.RoundToInt(payout));

        return baseWin + GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
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
        resultText.text = $"Got {upgrade.DisplayName}! Higher or lower?";
        UpdateUI();
    }

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

    // A guess button is only clickable if that guess can actually win
    private void SetGuessButtonsActive(bool active)
    {
        higherButton.interactable = active && CountWinningCards(true) > 0;
        lowerButton.interactable = active && CountWinningCards(false) > 0;
    }

    private void GameOver()
    {
        isGameOver = true;
        resultText.text = $"Game over! You reached round {roundNumber}.";
        SetGuessButtonsActive(false);
        restartButton.gameObject.SetActive(true);
    }

    private int DrawCard()
    {
        return Random.Range(1, CardCount + 1);
    }

    private bool IsExtremeCard(int value)
    {
        return value == 1 || value == CardCount;
    }

    // Draw a card from 2 to Q, so both guesses always carry some risk
    private int DrawPlayableCard()
    {
        return Random.Range(2, CardCount);
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

    private string GuessButtonLabel(string label, bool guessHigher)
    {
        int winAmount = GetWinAmount(guessHigher);
        return winAmount > 0 ? $"{label}\n+{winAmount}" : $"{label}\n-";
    }

    private void UpdateUI()
    {
        cardText.text = CardName(currentCard);
        chipsText.text = $"Chips: {chips}";
        roundText.text = $"Round {roundNumber}  |  Goal: {targetChips}  |  Guesses: {guessesLeft}";

        higherButtonText.text = GuessButtonLabel("HIGHER", true);
        lowerButtonText.text = GuessButtonLabel("LOWER", false);

        // Refresh which buttons are clickable for the new card
        if (!isGameOver && !upgradePanel.activeSelf)
        {
            SetGuessButtonsActive(true);
        }
    }
}