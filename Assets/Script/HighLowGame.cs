using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The High-Low minigame, played as a ladder:
// the bet goes on the table once, then the player guesses again and again whether the next card is
// higher, lower or the same. Every right guess multiplies the pot by the odds of that guess
// (an easy guess a little, a hard one a lot). CASH OUT takes the pot home at any time.
// A wrong guess loses the bet, and the pot is gone. The same card on HIGHER or LOWER changes nothing.
// After 8 right guesses the pot is cashed out by itself. A whole ladder counts as one play.
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
    [SerializeField] private Button cashOutButton;   // Made by Tools > Rearrange Tables

    [Header("Table")]
    [SerializeField] private TableCard cardPrefab;
    [SerializeField] private Transform deckSpot;      // New cards come from here
    [SerializeField] private Transform cardSpot;      // The current card
    [SerializeField] private Transform revealSpot;    // The drawn card is turned over here
    [SerializeField] private Transform discardSpot;   // Used cards fly away here

    [Header("Ladder")]
    [SerializeField] private float stepBonus = 1.02f;     // Each step pays a little over the fair odds, in the player's favor
    [SerializeField] private float tieMultiplier = 12f;   // A right TIE (1 chance in 13) multiplies the pot by this
    [SerializeField] private int maxSteps = 8;            // After this many right guesses the pot is cashed out

    [Header("Animation")]
    [SerializeField] private float revealPause = 0.4f;   // Pause on the drawn card before the table is tidied

    [Header("Timer")]
    [SerializeField] private DecisionTimer decisionTimer;  // Not used now (empty): a countdown ring on the felt
    [SerializeField] private float decisionSeconds = 6f;

    private readonly List<TableCard> cardsOnTable = new List<TableCard>();
    private TableCard currentCardObject;

    private TMP_Text higherButtonText;
    private TMP_Text lowerButtonText;
    private TMP_Text tieButtonText;
    private TMP_Text cashOutButtonText;

    private int currentCard;   // 1 = A, 11 = J, 12 = Q, 13 = K
    private int currentSuit;   // Only for the picture: High-Low ignores suits
    private bool isAnimating;

    // The ladder
    private bool isClimbing;          // A bet is on the table and the player is guessing
    private float multiplier = 1f;    // The pot is the bet times this
    private int steps;                // Right guesses so far
    private int sameCardBonus;        // Tie Reward upgrade: chips earned on same-card draws, paid with the pot

    private void Awake()
    {
        higherButtonText = higherButton.GetComponentInChildren<TMP_Text>();
        lowerButtonText = lowerButton.GetComponentInChildren<TMP_Text>();
        tieButtonText = tieButton.GetComponentInChildren<TMP_Text>();

        higherButton.onClick.AddListener(() => Guess(GuessType.Higher));
        lowerButton.onClick.AddListener(() => Guess(GuessType.Lower));
        tieButton.onClick.AddListener(() => Guess(GuessType.Tie));

        if (cashOutButton != null)
        {
            cashOutButtonText = cashOutButton.GetComponentInChildren<TMP_Text>();
            cashOutButton.onClick.AddListener(CashOut);
        }
    }

    private void OnEnable()
    {
        run.StateChanged += RefreshUI;
        ResetGame();
        RefreshUI();
    }

    // Leaving the table in the middle of a ladder: the pot is cashed out, nothing is lost
    private void OnDisable()
    {
        run.StateChanged -= RefreshUI;
        StopClock();

        if (isClimbing)
        {
            StopAllCoroutines();
            isAnimating = false;
            FinishLadder(true, "You left the table: the pot was cashed out.");
        }
    }

    // Clear the table and deal a fresh opening card
    private void ResetGame()
    {
        StopAllCoroutines();
        ClearTable();

        isClimbing = false;
        multiplier = 1f;
        steps = 0;
        sameCardBonus = 0;

        SetCard(DrawPlayableCard());
        run.ShowMessage("HIGHER, LOWER or TIE?\nEvery right guess grows the pot. CASH OUT whenever you like.");
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
        if (!run.CanPlay || isAnimating || CountWinningCards(guess) == 0)
        {
            return;
        }

        StopClock();
        StartCoroutine(GuessRoutine(guess));
    }

    // Work out the result first, then deal and turn the cards, then move up the ladder (or fall off)
    private IEnumerator GuessRoutine(GuessType guess)
    {
        isAnimating = true;

        // The first guess puts the bet on the table
        if (!isClimbing)
        {
            isClimbing = true;
            multiplier = 1f;
            steps = 0;
            sameCardBonus = 0;
            run.SetMinigameBusy(true);
        }

        RefreshUI();

        float stepMultiplier = StepMultiplier(guess);
        int nextCard = DrawCard();
        bool isSame = nextCard == currentCard;
        bool isPush = isSame && guess != GuessType.Tie;
        bool isRight = guess == GuessType.Tie ? isSame : !isSame && (guess == GuessType.Higher) == (nextCard > currentCard);
        string cardNote = "";

        SetCard(nextCard);
        Sprite drawnSprite = CurrentSprite();

        // After the same card or an A/K, a fresh card is dealt so there is always a real choice
        bool dealNewCard = isSame || IsExtremeCard(currentCard);

        if (dealNewCard)
        {
            SetCard(DrawPlayableCardExcept(nextCard));
            cardNote = $"\n{CardName(nextCard)} drawn. Dealing a new card.";
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

        if (isPush)
        {
            sameCardBonus += run.GetUpgradeTotal(UpgradeType.TieReward);
            run.ShowMessage($"Same card! Nothing changes. Pot: {Pot(run.GetCurrentBet())} chips{cardNote}");
            RefreshUI();
            StartClock();
            yield break;
        }

        if (!isRight)
        {
            FinishLadder(false, $"Wrong! The pot is gone.{cardNote}");
            yield break;
        }

        multiplier *= stepMultiplier;
        steps++;

        if (steps >= maxSteps)
        {
            FinishLadder(true, $"{maxSteps} right in a row! The pot is cashed out.{cardNote}");
            yield break;
        }

        AudioManager.Play(SoundId.Win, 0.6f);
        string right = guess == GuessType.Tie ? "TIE!" : "Right!";
        run.ShowMessage($"{right} x{stepMultiplier:0.00}  Pot: {Pot(run.GetCurrentBet())} chips  (step {steps}/{maxSteps})"
            + $"\nGuess again, or CASH OUT.{cardNote}");
        RefreshUI();
        StartClock();
    }

    private void CashOut()
    {
        if (!isClimbing || steps == 0 || isAnimating || !run.CanPlay)
        {
            return;
        }

        StopClock();
        FinishLadder(true, $"Cashed out after {steps} right guess{(steps > 1 ? "es" : "")}!");
    }

    // The ladder is over: the pot comes home (cashed), or the bet is lost
    private void FinishLadder(bool cashed, string headline)
    {
        int chipChange;
        int damage;   // Boss fight: always worked out at the smallest bet
        string message;

        if (cashed)
        {
            chipChange = GetProfit();
            damage = run.AtBaseBet(GetProfit);
            message = chipChange > 0 ? $"{headline}\n+{chipChange} chips (x{multiplier:0.00})" : $"{headline}\nNo chips won or lost.";
        }
        else
        {
            chipChange = -GetLossAmount();
            damage = -run.AtBaseBet(GetLossAmount);
            message = $"{headline}\n{chipChange} chips";
        }

        isClimbing = false;
        multiplier = 1f;
        steps = 0;
        sameCardBonus = 0;

        run.SetMinigameBusy(false);
        run.FinishPlay(chipChange, damage, message);
        RefreshUI();
    }

    // ---------- Numbers ----------

    // How much the pot grows on a right guess: the fair odds (a same card on HIGHER/LOWER is a free retry), plus a little
    private float StepMultiplier(GuessType guess)
    {
        if (guess == GuessType.Tie)
        {
            return tieMultiplier;
        }

        int winningCards = CountWinningCards(guess);
        return winningCards > 0 ? (CardCount - 1f) / winningCards * stepBonus : 0f;
    }

    private int Pot(int bet)
    {
        return Mathf.RoundToInt(bet * multiplier);
    }

    // What cashing out wins, upgrades included
    private int GetProfit()
    {
        int bet = run.GetCurrentBet();
        int profit = Pot(bet) - bet;

        if (profit > 0)
        {
            int percentBonus = run.GetUpgradeTotal(UpgradeType.WinPayoutPercent);
            profit = Mathf.RoundToInt(profit * (100 + percentBonus) / 100f) + run.GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
        }

        return Mathf.Max(0, profit) + sameCardBonus;
    }

    private int GetLossAmount()
    {
        return Mathf.Max(0, run.GetCurrentBet() - run.GetUpgradeTotal(UpgradeType.LossReduction));
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

    // ---------- Cards ----------

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

    // ---------- Clock (not used now: no timer on the table) ----------

    private void StartClock()
    {
        if (decisionTimer != null && isClimbing)
        {
            decisionTimer.Begin(decisionSeconds, CashOut, () => run.CanPlay && !isAnimating);
        }
    }

    private void StopClock()
    {
        if (decisionTimer != null)
        {
            decisionTimer.Cancel();
        }
    }

    // ---------- UI ----------

    // "HIGHER x1.22": how much the pot grows on a right guess
    private string GuessButtonLabel(string label, GuessType guess)
    {
        return CountWinningCards(guess) > 0 ? $"{label}\nx{StepMultiplier(guess):0.00}" : $"{label}\n-";
    }

    private void RefreshUI()
    {
        bool canGuess = run.CanPlay && !isAnimating;

        higherButtonText.text = GuessButtonLabel("HIGHER", GuessType.Higher);
        lowerButtonText.text = GuessButtonLabel("LOWER", GuessType.Lower);
        tieButtonText.text = GuessButtonLabel("TIE", GuessType.Tie);

        higherButton.interactable = canGuess && CountWinningCards(GuessType.Higher) > 0;
        lowerButton.interactable = canGuess && CountWinningCards(GuessType.Lower) > 0;
        tieButton.interactable = canGuess;

        if (cashOutButton == null)
        {
            return;
        }

        bool canCashOut = canGuess && isClimbing && steps > 0;
        cashOutButton.interactable = canCashOut;

        if (cashOutButtonText != null)
        {
            if (!isClimbing || steps == 0)
            {
                cashOutButtonText.text = "CASH OUT\nWin once first";
            }
            else if (run.IsBossFight)
            {
                cashOutButtonText.text = $"CASH OUT\n+{GetProfit()} chips\n{run.AtBaseBet(GetProfit)} damage";
            }
            else
            {
                cashOutButtonText.text = $"CASH OUT\n+{GetProfit()} chips";
            }
        }
    }
}
