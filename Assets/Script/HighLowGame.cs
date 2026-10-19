using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The High-Low minigame: only handles card rules, the RunManager handles the rest
public class HighLowGame : MonoBehaviour
{
    private const int CardCount = 13;

    private enum GuessType
    {
        Higher,
        Lower,
        Tie
    }

    [Header("Run")]
    [SerializeField] private RunManager run;

    [Header("UI")]
    [SerializeField] private TMP_Text cardText;
    [SerializeField] private Button higherButton;
    [SerializeField] private Button lowerButton;
    [SerializeField] private Button tieButton;

    [Header("Rules")]
    [SerializeField] private float payoutMultiplier = 1.2f;
    [SerializeField] private int baseWinBonus = 3;

    private TMP_Text higherButtonText;
    private TMP_Text lowerButtonText;
    private TMP_Text tieButtonText;

    private int currentCard;   // 1 = A, 11 = J, 12 = Q, 13 = K

    // Awake runs before any Start, so everything is ready when the run begins
    private void Awake()
    {
        higherButtonText = higherButton.GetComponentInChildren<TMP_Text>();
        lowerButtonText = lowerButton.GetComponentInChildren<TMP_Text>();
        tieButtonText = tieButton.GetComponentInChildren<TMP_Text>();

        higherButton.onClick.AddListener(() => Guess(GuessType.Higher));
        lowerButton.onClick.AddListener(() => Guess(GuessType.Lower));
        tieButton.onClick.AddListener(() => Guess(GuessType.Tie));
    }

    // Start listening to the RunManager's events
    // Runs every time this minigame is switched on: listen for updates and start fresh
    private void OnEnable()
    {
        run.StateChanged += RefreshUI;
        ResetGame();
    }

    // Stop listening when this minigame is switched off
    private void OnDisable()
    {
        run.StateChanged -= RefreshUI;
    }

    private void ResetGame()
    {
        currentCard = DrawPlayableCard();
        run.ShowMessage("Higher, lower or tie?");
    }

    private void Guess(GuessType guess)
    {
        if (!run.CanPlay)
        {
            return;
        }

        int nextCard = DrawCard();
        bool isTie = nextCard == currentCard;
        int chipChange;
        string message;

        if (guess == GuessType.Tie)
        {
            if (isTie)
            {
                chipChange = GetWinAmount(guess) + run.GetUpgradeTotal(UpgradeType.TieReward);
                message = $"TIE! +{chipChange} chips";
            }
            else
            {
                chipChange = -GetLossAmount();
                message = $"Not a tie! {chipChange} chips";
            }
        }
        else if (isTie)
        {
            chipChange = run.GetUpgradeTotal(UpgradeType.TieReward);
            message = chipChange > 0 ? $"Same card! +{chipChange} chips" : "Same card! No chips won or lost.";
        }
        else
        {
            bool isHigher = nextCard > currentCard;
            bool isCorrect = (guess == GuessType.Higher) == isHigher;

            if (isCorrect)
            {
                chipChange = GetWinAmount(guess);
                message = $"Correct! +{chipChange} chips";
            }
            else
            {
                chipChange = -GetLossAmount();
                message = $"Wrong! {chipChange} chips";
            }
        }

        currentCard = nextCard;

        // After a tie or an A/K, deal a fresh card so the table visibly changes
        if (isTie || IsExtremeCard(currentCard))
        {
            currentCard = DrawPlayableCardExcept(nextCard);
            message += $"\n{CardName(nextCard)} drawn. Dealing a new card.";
        }

        // Hand the result to the RunManager, which updates chips, rounds and UI
        run.FinishPlay(chipChange, message);
    }

    private int CountWinningCards(GuessType guess)
    {
        return guess switch
        {
            GuessType.Higher => CardCount - currentCard,
            GuessType.Lower => currentCard - 1,
            _ => 1
        };
    }

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
        float payout = run.GetCurrentBet() * payoutMultiplier * losingCards / winningCards;
        int baseWin = Mathf.Max(1, Mathf.RoundToInt(payout) + baseWinBonus);

        int percentBonus = run.GetUpgradeTotal(UpgradeType.WinPayoutPercent);
        int boostedWin = Mathf.RoundToInt(baseWin * (100 + percentBonus) / 100f);

        return boostedWin + run.GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
    }

    private int GetLossAmount()
    {
        return Mathf.Max(0, run.GetCurrentBet() - run.GetUpgradeTotal(UpgradeType.LossReduction));
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

    // Draw a playable card that is different from the given value
    private int DrawPlayableCardExcept(int value)
    {
        int card = DrawPlayableCard();

        while (card == value)
        {
            card = DrawPlayableCard();
        }

        return card;
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

    private void RefreshUI()
    {
        cardText.text = CardName(currentCard);

        higherButtonText.text = GuessButtonLabel("HIGHER", GuessType.Higher);
        lowerButtonText.text = GuessButtonLabel("LOWER", GuessType.Lower);
        tieButtonText.text = GuessButtonLabel("TIE", GuessType.Tie);

        higherButton.interactable = run.CanPlay && CountWinningCards(GuessType.Higher) > 0;
        lowerButton.interactable = run.CanPlay && CountWinningCards(GuessType.Lower) > 0;
        tieButton.interactable = run.CanPlay;
    }
}