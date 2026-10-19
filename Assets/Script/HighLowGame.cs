using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HighLowGame : MonoBehaviour
{
    private const int CardCount = 13;

    // The three guesses a player can make
    private enum GuessType
    {
        Higher,
        Lower,
        Tie
    }

    [Header("UI")]
    [SerializeField] private TMP_Text cardText;
    [SerializeField] private TMP_Text chipsText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text ownedUpgradesText;
    [SerializeField] private Button higherButton;
    [SerializeField] private Button lowerButton;
    [SerializeField] private Button tieButton;
    [SerializeField] private Button restartButton;

    [Header("Upgrade Screen")]
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private Button[] upgradeButtons;
    [SerializeField] private List<Upgrade> upgradePool;

    [Header("Rarity")]
    [SerializeField] private int commonWeight = 70;      // Higher weight = appears more often
    [SerializeField] private int rareWeight = 25;
    [SerializeField] private int legendaryWeight = 5;
    [SerializeField] private Color commonColor = new Color(0.85f, 0.85f, 0.85f);
    [SerializeField] private Color rareColor = new Color(0.55f, 0.75f, 1f);
    [SerializeField] private Color legendaryColor = new Color(1f, 0.8f, 0.25f);
    [Header("Rules")]
    [SerializeField] private int startingChips = 100;
    [SerializeField] private int betAmount = 10;
    [SerializeField] private float payoutMultiplier = 1.2f;
    [SerializeField] private int baseWinBonus = 3;   // Flat chips on every win, so safe guesses still feel rewarding
    [SerializeField] private int guessesPerRound = 10;
    [SerializeField] private int baseRoundGoal = 40;
    [SerializeField] private int goalIncreasePerRound = 10;
    [SerializeField] private int upgradeChoices = 3;

    private readonly List<Upgrade> ownedUpgrades = new List<Upgrade>();

    private TMP_Text higherButtonText;
    private TMP_Text lowerButtonText;
    private TMP_Text tieButtonText;

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
        tieButtonText = tieButton.GetComponentInChildren<TMP_Text>();

        higherButton.onClick.AddListener(() => Guess(GuessType.Higher));
        lowerButton.onClick.AddListener(() => Guess(GuessType.Lower));
        tieButton.onClick.AddListener(() => Guess(GuessType.Tie));
        restartButton.onClick.AddListener(StartNewRun);

        StartNewRun();
    }

    private void StartNewRun()
    {
        chips = startingChips;
        ownedUpgrades.Clear();
        isGameOver = false;
        currentCard = DrawPlayableCard();

        upgradePanel.SetActive(false);
        restartButton.gameObject.SetActive(false);

        StartRound(1);
        resultText.text = "Higher, lower or tie?";
        UpdateUI();
    }

    private void StartRound(int number)
    {
        roundNumber = number;
        guessesLeft = guessesPerRound + GetUpgradeTotal(UpgradeType.ExtraGuesses);

        int chipsToEarn = baseRoundGoal + goalIncreasePerRound * (number - 1);
        targetChips = chips + chipsToEarn;
    }

    private void Guess(GuessType guess)
    {
        if (isGameOver)
        {
            return;
        }

        guessesLeft--;
        int nextCard = DrawCard();
        bool isTie = nextCard == currentCard;

        if (guess == GuessType.Tie)
        {
            if (isTie)
            {
                int winAmount = GetWinAmount(guess) + GetUpgradeTotal(UpgradeType.TieReward);
                chips += winAmount;
                resultText.text = $"TIE! +{winAmount} chips";
            }
            else
            {
                int lossAmount = GetLossAmount();
                chips -= lossAmount;
                resultText.text = $"Not a tie! -{lossAmount} chips";
            }
        }
        else if (isTie)
        {
            // Guessed higher or lower but the cards matched: no loss
            int tieReward = GetUpgradeTotal(UpgradeType.TieReward);
            chips += tieReward;
            resultText.text = tieReward > 0 ? $"Tie! +{tieReward} chips" : "Tie! No chips lost.";
        }
        else
        {
            bool isHigher = nextCard > currentCard;
            bool isCorrect = (guess == GuessType.Higher) == isHigher;

            if (isCorrect)
            {
                int winAmount = GetWinAmount(guess);
                chips += winAmount;
                resultText.text = $"Correct! +{winAmount} chips";
            }
            else
            {
                int lossAmount = GetLossAmount();
                chips -= lossAmount;
                resultText.text = $"Wrong! -{lossAmount} chips";
            }
        }

        currentCard = nextCard;

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

    private int GetCurrentBet()
    {
        return betAmount + GetUpgradeTotal(UpgradeType.BetIncrease);
    }

    // Number of card values that would win with this guess
    private int CountWinningCards(GuessType guess)
    {
        return guess switch
        {
            GuessType.Higher => CardCount - currentCard,
            GuessType.Lower => currentCard - 1,
            _ => 1   // Only the same value wins a tie guess
        };
    }

    // Number of card values that would lose with this guess
    private int CountLosingCards(GuessType guess)
    {
        return guess switch
        {
            GuessType.Higher => currentCard - 1,
            GuessType.Lower => CardCount - currentCard,
            _ => CardCount - 1
        };
    }

    private int GetWinAmount(GuessType guess)
    {
        int winningCards = CountWinningCards(guess);

        if (winningCards == 0)
        {
            return 0;
        }

        int losingCards = CountLosingCards(guess);
        float payout = GetCurrentBet() * payoutMultiplier * losingCards / winningCards;
        int baseWin = Mathf.Max(1, Mathf.RoundToInt(payout) + baseWinBonus);

        int percentBonus = GetUpgradeTotal(UpgradeType.WinPayoutPercent);
        int boostedWin = Mathf.RoundToInt(baseWin * (100 + percentBonus) / 100f);

        return boostedWin + GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
    }

    private int GetLossAmount()
    {
        return Mathf.Max(0, GetCurrentBet() - GetUpgradeTotal(UpgradeType.LossReduction));
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
        resultText.text = $"Got {upgrade.DisplayName}! Higher, lower or tie?";
        UpdateUI();
    }

    // Pick different upgrades, rarer ones less often
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

    // Like drawing a raffle ticket: each upgrade gets as many tickets as its rarity weight
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

    private void SetGuessButtonsActive(bool active)
    {
        higherButton.interactable = active && CountWinningCards(GuessType.Higher) > 0;
        lowerButton.interactable = active && CountWinningCards(GuessType.Lower) > 0;
        tieButton.interactable = active;
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

    private string GuessButtonLabel(string label, GuessType guess)
    {
        int winAmount = GetWinAmount(guess);
        return winAmount > 0 ? $"{label}\n+{winAmount}" : $"{label}\n-";
    }

    private void UpdateUI()
    {
        cardText.text = CardName(currentCard);
        chipsText.text = $"Chips: {chips}   Bet: {GetCurrentBet()}";
        roundText.text = $"Round {roundNumber}  |  Goal: {targetChips}  |  Guesses: {guessesLeft}";
        ownedUpgradesText.text = BuildOwnedUpgradesText();

        higherButtonText.text = GuessButtonLabel("HIGHER", GuessType.Higher);
        lowerButtonText.text = GuessButtonLabel("LOWER", GuessType.Lower);
        tieButtonText.text = GuessButtonLabel("TIE", GuessType.Tie);

        if (!isGameOver && !upgradePanel.activeSelf)
        {
            SetGuessButtonsActive(true);
        }
    }
}