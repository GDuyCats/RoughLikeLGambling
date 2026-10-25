using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The High-Low minigame: cards are dealt onto the table. The RunManager handles the rest.
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
    [SerializeField] private Button higherButton;
    [SerializeField] private Button lowerButton;
    [SerializeField] private Button tieButton;

    [Header("Table")]
    [SerializeField] private TableCard cardPrefab;
    [SerializeField] private Transform deckSpot;      // New cards come from here
    [SerializeField] private Transform cardSpot;      // The current card
    [SerializeField] private Transform revealSpot;    // The drawn card is turned over here
    [SerializeField] private Transform discardSpot;   // Used cards fly away here

    [Header("Rules")]
    [SerializeField] private float payoutMultiplier = 1.2f;
    [SerializeField] private int baseWinBonus = 3;

    [Header("Animation")]
    [SerializeField] private float revealPause = 0.4f;   // Pause on the drawn card before the table is tidied

    private readonly List<TableCard> cardsOnTable = new List<TableCard>();
    private TableCard currentCardObject;

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

    // Clear the table and deal a fresh opening card
    private void ResetGame()
    {
        StopAllCoroutines();
        ClearTable();

        SetCard(DrawPlayableCard());
        run.ShowMessage("Higher, lower or tie?");
        StartCoroutine(DealOpeningCard());
    }

    private IEnumerator DealOpeningCard()
    {
        isAnimating = true;
        RefreshUI();

        currentCardObject = SpawnCard();
        yield return currentCardObject.MoveTo(cardSpot.position);
        yield return currentCardObject.FlipTo(CurrentSprite());

        isAnimating = false;
        RefreshUI();
    }

    private void Guess(GuessType guess)
    {
        if (!run.CanPlay || isAnimating)
        {
            return;
        }

        StartCoroutine(GuessRoutine(guess));
    }

    // Work out the result first, then deal and turn the cards, then report to the RunManager
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
        Sprite drawnSprite = CurrentSprite();

        // After a tie or an A/K, a fresh card is dealt so the table visibly changes
        bool dealNewCard = isTie || IsExtremeCard(currentCard);

        if (dealNewCard)
        {
            SetCard(DrawPlayableCardExcept(nextCard));
            message += $"\n{CardName(nextCard)} drawn. Dealing a new card.";
        }

        // Deal the drawn card next to the current one and turn it over
        TableCard drawn = SpawnCard();
        yield return drawn.MoveTo(revealSpot.position);
        yield return drawn.FlipTo(drawnSprite);
        yield return new WaitForSeconds(revealPause);

        // The old card flies away and the drawn card takes its place
        StartCoroutine(Discard(currentCardObject));
        yield return drawn.MoveTo(cardSpot.position);
        currentCardObject = drawn;

        if (dealNewCard)
        {
            yield return new WaitForSeconds(revealPause);

            StartCoroutine(Discard(currentCardObject));
            TableCard fresh = SpawnCard();
            yield return fresh.MoveTo(cardSpot.position);
            yield return fresh.FlipTo(CurrentSprite());
            currentCardObject = fresh;
        }

        isAnimating = false;
        run.SetMinigameBusy(false);
        run.FinishPlay(chipChange, message);
    }

    // A new face-down card appears on the deck
    private TableCard SpawnCard()
    {
        TableCard card = Instantiate(cardPrefab, deckSpot.position, Quaternion.identity);
        card.SetSprite(CardArt.GetCardBack());
        cardsOnTable.Add(card);
        return card;
    }

    private IEnumerator Discard(TableCard card)
    {
        if (card == null)
        {
            yield break;
        }

        yield return card.MoveTo(discardSpot.position);
        cardsOnTable.Remove(card);
        Destroy(card.gameObject);
    }

    private void ClearTable()
    {
        foreach (TableCard card in cardsOnTable)
        {
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        cardsOnTable.Clear();
        currentCardObject = null;
    }

    private Sprite CurrentSprite()
    {
        return CardArt.GetCard(currentCard, currentSuit);
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
        // While cards are moving, the buttons stay locked
        if (isAnimating)
        {
            higherButton.interactable = false;
            lowerButton.interactable = false;
            tieButton.interactable = false;
            return;
        }

        higherButtonText.text = GuessButtonLabel("HIGHER", GuessType.Higher);
        lowerButtonText.text = GuessButtonLabel("LOWER", GuessType.Lower);
        tieButtonText.text = GuessButtonLabel("TIE", GuessType.Tie);

        higherButton.interactable = run.CanPlay && CountWinningCards(GuessType.Higher) > 0;
        lowerButton.interactable = run.CanPlay && CountWinningCards(GuessType.Lower) > 0;
        tieButton.interactable = run.CanPlay;
    }
}