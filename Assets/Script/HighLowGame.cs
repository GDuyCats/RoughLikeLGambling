using System.Collections;
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
    [SerializeField] private Image cardImage;
    [SerializeField] private Button higherButton;
    [SerializeField] private Button lowerButton;
    [SerializeField] private Button tieButton;

    [Header("Rules")]
    [SerializeField] private float payoutMultiplier = 1.2f;
    [SerializeField] private int baseWinBonus = 3;

    [Header("Animation")]
    [SerializeField] private float flipTime = 0.25f;     // Time to turn a card over
    [SerializeField] private float revealPause = 0.4f;   // Pause on the drawn card before a fresh one is dealt

    private TMP_Text higherButtonText;
    private TMP_Text lowerButtonText;
    private TMP_Text tieButtonText;

    private int currentCard;   // 1 = A, 11 = J, 12 = Q, 13 = K
    private int currentSuit;   // Only for the picture: High-Low ignores suits
    private bool isAnimating;

    private void Awake()
    {
        higherButtonText = higherButton.GetComponentInChildren<TMP_Text>();
        lowerButtonText = lowerButton.GetComponentInChildren<TMP_Text>();
        tieButtonText = tieButton.GetComponentInChildren<TMP_Text>();

        higherButton.onClick.AddListener(() => Guess(GuessType.Higher));
        lowerButton.onClick.AddListener(() => Guess(GuessType.Lower));
        tieButton.onClick.AddListener(() => Guess(GuessType.Tie));
    }

    private void OnEnable()
    {
        run.StateChanged += RefreshUI;
        ResetGame();
        RefreshUI();
    }

    private void OnDisable()
    {
        run.StateChanged -= RefreshUI;
    }

    private void ResetGame()
    {
        // If the table was closed in the middle of an animation, start clean
        isAnimating = false;
        cardImage.rectTransform.localScale = Vector3.one;

        SetCard(DrawPlayableCard());
        run.ShowMessage("Higher, lower or tie?");
    }

    private void Guess(GuessType guess)
    {
        if (!run.CanPlay || isAnimating)
        {
            return;
        }

        StartCoroutine(GuessRoutine(guess));
    }

    // Work out the result first, then show the cards turning over, then report to the RunManager
    private IEnumerator GuessRoutine(GuessType guess)
    {
        isAnimating = true;
        run.SetMinigameBusy(true);

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

        SetCard(nextCard);
        Sprite revealedCard = CardArt.GetCard(currentCard, currentSuit);

        // After a tie or an A/K, a fresh card is dealt so the table visibly changes
        bool dealNewCard = isTie || IsExtremeCard(currentCard);

        if (dealNewCard)
        {
            SetCard(DrawPlayableCardExcept(nextCard));
            message += $"\n{CardName(nextCard)} drawn. Dealing a new card.";
        }

        yield return FlipTo(revealedCard);

        if (dealNewCard)
        {
            yield return new WaitForSeconds(revealPause);
            yield return FlipTo(CardArt.GetCard(currentCard, currentSuit));
        }

        isAnimating = false;
        run.SetMinigameBusy(false);
        run.FinishPlay(chipChange, message);
    }

    // Squash the card to nothing, swap the picture, then stretch it back: it looks like turning over
    private IEnumerator FlipTo(Sprite sprite)
    {
        RectTransform card = cardImage.rectTransform;
        float halfTime = flipTime / 2f;

        for (float t = 0f; t < halfTime; t += Time.deltaTime)
        {
            card.localScale = new Vector3(1f - t / halfTime, 1f, 1f);
            yield return null;
        }

        cardImage.sprite = sprite;

        for (float t = 0f; t < halfTime; t += Time.deltaTime)
        {
            card.localScale = new Vector3(t / halfTime, 1f, 1f);
            yield return null;
        }

        card.localScale = Vector3.one;
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

    private int DrawPlayableCardExcept(int value)
    {
        int card = DrawPlayableCard();

        while (card == value)
        {
            card = DrawPlayableCard();
        }

        return card;
    }

    private void SetCard(int value)
    {
        currentCard = value;
        currentSuit = Random.Range(0, 4);
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
        // During an animation the coroutine controls the card, and the buttons stay locked
        if (isAnimating)
        {
            higherButton.interactable = false;
            lowerButton.interactable = false;
            tieButton.interactable = false;
            return;
        }

        cardImage.sprite = CardArt.GetCard(currentCard, currentSuit);

        higherButtonText.text = GuessButtonLabel("HIGHER", GuessType.Higher);
        lowerButtonText.text = GuessButtonLabel("LOWER", GuessType.Lower);
        tieButtonText.text = GuessButtonLabel("TIE", GuessType.Tie);

        higherButton.interactable = run.CanPlay && CountWinningCards(GuessType.Higher) > 0;
        lowerButton.interactable = run.CanPlay && CountWinningCards(GuessType.Lower) > 0;
        tieButton.interactable = run.CanPlay;
    }
}