using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Blackjack minigame: get closer to 21 than the dealer without going over.
// Cards are dealt onto the table in two rows: the dealer's and the player's.
public class BlackjackGame : MonoBehaviour
{
    private const int BlackjackValue = 21;
    private const int DealerStandValue = 17;
    private const int DeckSize = 52;
    private const int RanksPerSuit = 13;

    [Header("Run")]
    [SerializeField] private RunManager run;

    [Header("UI")]
    [SerializeField] private TMP_Text dealerText;
    [SerializeField] private TMP_Text playerText;
    [SerializeField] private Button dealButton;
    [SerializeField] private Button hitButton;
    [SerializeField] private Button standButton;
    [SerializeField] private Button doubleButton;
    [SerializeField] private Button surrenderButton;

    [Header("Table")]
    [SerializeField] private TableCard cardPrefab;
    [SerializeField] private Transform deckSpot;         // New cards come from here
    [SerializeField] private Transform dealerRowStart;   // The dealer's first card; the rest line up to the right
    [SerializeField] private Transform playerRowStart;
    [SerializeField] private Transform discardSpot;      // Old cards fly away here
    [SerializeField] private float cardSpacing = 2f;     // Distance between cards in a row (a bit more than a card's width)

    [Header("Rules")]
    [SerializeField] private float winMultiplier = 1.5f;
    [SerializeField] private float blackjackMultiplier = 2.5f;
    [SerializeField] private int baseWinBonus = 3;

    [Header("Animation")]
    [SerializeField] private float dealDelay = 0.2f;         // Time between cards when dealing a new hand
    [SerializeField] private float dealerDrawDelay = 0.6f;   // Pause between the dealer's cards

    private readonly List<int> deck = new List<int>();
    private readonly List<int> playerHand = new List<int>();
    private readonly List<int> dealerHand = new List<int>();
    private readonly List<TableCard> playerCards = new List<TableCard>();
    private readonly List<TableCard> dealerCards = new List<TableCard>();

    private TMP_Text dealButtonText;
    private TMP_Text doubleButtonText;
    private TMP_Text surrenderButtonText;

    private bool isHandInProgress;
    private bool isDoubled;
    private bool isDealerTurn;
    private bool isAnimating;          // Cards are moving: buttons stay locked
    private bool dealerCardRevealed;   // Has the dealer's face-down card been turned over?
    private int runningAnimations;     // How many card animations are still playing

    private void Awake()
    {
        dealButtonText = dealButton.GetComponentInChildren<TMP_Text>();
        doubleButtonText = doubleButton.GetComponentInChildren<TMP_Text>();
        surrenderButtonText = surrenderButton.GetComponentInChildren<TMP_Text>();

        dealButton.onClick.AddListener(Deal);
        hitButton.onClick.AddListener(Hit);
        standButton.onClick.AddListener(Stand);
        doubleButton.onClick.AddListener(DoubleDown);
        surrenderButton.onClick.AddListener(Surrender);
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
        StopAllCoroutines();
        ClearTable();

        playerHand.Clear();
        dealerHand.Clear();
        isHandInProgress = false;
        isDoubled = false;
        isDealerTurn = false;
        isAnimating = false;
        dealerCardRevealed = false;

        run.SetMinigameBusy(false);
        run.ShowMessage("Press DEAL to start a hand.");
    }

    // ---------- Player actions ----------

    private void Deal()
    {
        if (!run.CanPlay || isHandInProgress || isAnimating)
        {
            return;
        }

        StartCoroutine(DealRoutine());
    }

    private IEnumerator DealRoutine()
    {
        isAnimating = true;
        isHandInProgress = true;
        isDoubled = false;
        dealerCardRevealed = false;
        run.SetMinigameBusy(true);

        // Clear last hand's cards off the table first
        yield return ClearTableRoutine();

        BuildDeck();
        playerHand.Clear();
        dealerHand.Clear();

        // Player, dealer, player, dealer. The cards fly one after another, overlapping a little.
        StartCoroutine(Track(DealCardRoutine(playerHand, playerCards, playerRowStart, true)));
        yield return new WaitForSeconds(dealDelay);
        StartCoroutine(Track(DealCardRoutine(dealerHand, dealerCards, dealerRowStart, true)));
        yield return new WaitForSeconds(dealDelay);
        StartCoroutine(Track(DealCardRoutine(playerHand, playerCards, playerRowStart, true)));
        yield return new WaitForSeconds(dealDelay);
        StartCoroutine(Track(DealCardRoutine(dealerHand, dealerCards, dealerRowStart, false)));

        // Wait until every card has landed and turned over
        yield return new WaitUntil(() => runningAnimations == 0);
        isAnimating = false;

        // A natural blackjack on either side ends the hand right away
        if (IsBlackjack(playerHand) || IsBlackjack(dealerHand))
        {
            yield return RevealDealerCard();
            FinishHand();
            yield break;
        }

        run.ShowMessage("Hit, stand, double or surrender?");
        RefreshUI();
    }

    private void Hit()
    {
        if (!isHandInProgress || isDealerTurn || isAnimating)
        {
            return;
        }

        StartCoroutine(HitRoutine());
    }

    private IEnumerator HitRoutine()
    {
        isAnimating = true;
        RefreshUI();

        yield return DealCardRoutine(playerHand, playerCards, playerRowStart, true);
        isAnimating = false;

        int value = HandValue(playerHand);

        if (value > BlackjackValue)
        {
            yield return RevealDealerCard();
            FinishHand();
        }
        else if (value == BlackjackValue)
        {
            // 21 can't be improved, so stand automatically
            yield return DealerTurnRoutine();
        }
        else
        {
            run.ShowMessage("Hit or stand?");
            RefreshUI();
        }
    }

    private void Stand()
    {
        if (!isHandInProgress || isDealerTurn || isAnimating)
        {
            return;
        }

        StartCoroutine(DealerTurnRoutine());
    }

    // The dealer turns the hidden card over, then draws one card at a time
    private IEnumerator DealerTurnRoutine()
    {
        isDealerTurn = true;
        run.ShowMessage("Dealer's turn...");
        RefreshUI();

        yield return RevealDealerCard();
        yield return new WaitForSeconds(dealerDrawDelay);

        while (HandValue(dealerHand) < DealerStandValue)
        {
            yield return DealCardRoutine(dealerHand, dealerCards, dealerRowStart, true);
            yield return new WaitForSeconds(dealerDrawDelay);
        }

        isDealerTurn = false;
        FinishHand();
    }

    // Double the bet, take exactly one more card, then stand
    private void DoubleDown()
    {
        if (!CanDoubleDown())
        {
            return;
        }

        StartCoroutine(DoubleRoutine());
    }

    private IEnumerator DoubleRoutine()
    {
        isDoubled = true;
        isAnimating = true;
        RefreshUI();

        yield return DealCardRoutine(playerHand, playerCards, playerRowStart, true);
        isAnimating = false;

        if (HandValue(playerHand) > BlackjackValue)
        {
            yield return RevealDealerCard();
            FinishHand();
        }
        else
        {
            yield return DealerTurnRoutine();
        }
    }

    // Give up the hand right away and lose only half the bet
    private void Surrender()
    {
        if (!CanMakeFirstDecision())
        {
            return;
        }

        StartCoroutine(SurrenderRoutine());
    }

    private IEnumerator SurrenderRoutine()
    {
        isAnimating = true;
        RefreshUI();

        yield return RevealDealerCard();
        isAnimating = false;

        int lossAmount = GetSurrenderLoss();
        isHandInProgress = false;

        run.SetMinigameBusy(false);
        run.FinishPlay(-lossAmount, $"Surrendered. -{lossAmount} chips");
    }

    private bool CanMakeFirstDecision()
    {
        return isHandInProgress && !isDealerTurn && !isAnimating && playerHand.Count == 2 && run.CanPlay;
    }

    private bool CanDoubleDown()
    {
        return CanMakeFirstDecision() && run.Chips >= run.GetCurrentBet() * 2;
    }

    // ---------- Cards on the table ----------

    // Take a card from the deck, fly it to the next spot in the row, and turn it over if face up
    private IEnumerator DealCardRoutine(List<int> hand, List<TableCard> cards, Transform rowStart, bool faceUp)
    {
        int card = DrawCard();
        hand.Add(card);

        TableCard cardObject = Instantiate(cardPrefab, deckSpot.position, Quaternion.identity);
        cardObject.SetSprite(CardArt.GetCardBack());
        cards.Add(cardObject);

        Vector3 slot = rowStart.position + Vector3.right * cardSpacing * (cards.Count - 1);
        yield return cardObject.MoveTo(slot);

        if (faceUp)
        {
            yield return cardObject.FlipTo(CardSprite(card));
        }

        RefreshUI();
    }

    private IEnumerator RevealDealerCard()
    {
        if (dealerCardRevealed || dealerCards.Count < 2)
        {
            yield break;
        }

        dealerCardRevealed = true;
        yield return dealerCards[1].FlipTo(CardSprite(dealerHand[1]));
        RefreshUI();
    }

    // Counts animations that are playing, so we can wait for all of them to finish
    private IEnumerator Track(IEnumerator routine)
    {
        runningAnimations++;
        yield return routine;
        runningAnimations--;
    }

    private IEnumerator ClearTableRoutine()
    {
        foreach (TableCard card in dealerCards)
        {
            StartCoroutine(Track(DiscardRoutine(card)));
        }

        foreach (TableCard card in playerCards)
        {
            StartCoroutine(Track(DiscardRoutine(card)));
        }

        dealerCards.Clear();
        playerCards.Clear();

        yield return new WaitUntil(() => runningAnimations == 0);
    }

    private IEnumerator DiscardRoutine(TableCard card)
    {
        if (card == null)
        {
            yield break;
        }

        yield return card.MoveTo(discardSpot.position);
        Destroy(card.gameObject);
    }

    // Instantly remove every card (used when the table is reset)
    private void ClearTable()
    {
        foreach (TableCard card in dealerCards)
        {
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        foreach (TableCard card in playerCards)
        {
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        dealerCards.Clear();
        playerCards.Clear();
        runningAnimations = 0;
    }

    private Sprite CardSprite(int card)
    {
        return CardArt.GetCard(GetRank(card), GetSuit(card));
    }

    // ---------- Results ----------

    private void FinishHand()
    {
        isHandInProgress = false;

        int playerValue = HandValue(playerHand);
        int dealerValue = HandValue(dealerHand);
        bool playerBlackjack = IsBlackjack(playerHand);
        bool dealerBlackjack = IsBlackjack(dealerHand);

        int chipChange;
        string reason;

        if (playerValue > BlackjackValue)
        {
            chipChange = -GetLossAmount();
            reason = "Bust!";
        }
        else if (playerBlackjack && dealerBlackjack)
        {
            chipChange = 0;
            reason = "Both have blackjack!";
        }
        else if (playerBlackjack)
        {
            chipChange = GetWinAmount(blackjackMultiplier);
            reason = "BLACKJACK!";
        }
        else if (dealerBlackjack)
        {
            chipChange = -GetLossAmount();
            reason = "Dealer has blackjack!";
        }
        else if (dealerValue > BlackjackValue)
        {
            chipChange = GetWinAmount(winMultiplier);
            reason = "Dealer busts!";
        }
        else if (playerValue > dealerValue)
        {
            chipChange = GetWinAmount(winMultiplier);
            reason = "You win!";
        }
        else if (playerValue < dealerValue)
        {
            chipChange = -GetLossAmount();
            reason = "Dealer wins!";
        }
        else
        {
            chipChange = 0;
            reason = "Push!";
        }

        if (isDoubled)
        {
            reason = "Doubled! " + reason;
        }

        string message = chipChange > 0
            ? $"{reason} +{chipChange} chips"
            : chipChange < 0
                ? $"{reason} {chipChange} chips"
                : $"{reason} No chips won or lost.";

        run.SetMinigameBusy(false);
        run.FinishPlay(chipChange, message);
    }

    private int GetHandBet()
    {
        return run.GetCurrentBet() * (isDoubled ? 2 : 1);
    }

    private int GetWinAmount(float multiplier)
    {
        int baseWin = Mathf.Max(1, Mathf.RoundToInt(GetHandBet() * multiplier) + baseWinBonus);

        int percentBonus = run.GetUpgradeTotal(UpgradeType.WinPayoutPercent);
        int boostedWin = Mathf.RoundToInt(baseWin * (100 + percentBonus) / 100f);

        return boostedWin + run.GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
    }

    private int GetLossAmount()
    {
        return Mathf.Max(0, GetHandBet() - run.GetUpgradeTotal(UpgradeType.LossReduction));
    }

    private int GetSurrenderLoss()
    {
        return Mathf.CeilToInt(GetLossAmount() / 2f);
    }

    // ---------- Cards and hands ----------

    private void BuildDeck()
    {
        deck.Clear();

        for (int card = 0; card < DeckSize; card++)
        {
            deck.Add(card);
        }
    }

    private int DrawCard()
    {
        int index = Random.Range(0, deck.Count);
        int card = deck[index];
        deck.RemoveAt(index);
        return card;
    }

    private int GetRank(int card)
    {
        return card % RanksPerSuit + 1;
    }

    private int GetSuit(int card)
    {
        return card / RanksPerSuit;
    }

    private int GetCardValue(int rank)
    {
        if (rank == 1)
        {
            return 11;
        }

        return Mathf.Min(rank, 10);
    }

    private int HandValue(List<int> hand)
    {
        int total = 0;
        int acesAsEleven = 0;

        foreach (int card in hand)
        {
            int rank = GetRank(card);
            total += GetCardValue(rank);

            if (rank == 1)
            {
                acesAsEleven++;
            }
        }

        while (total > BlackjackValue && acesAsEleven > 0)
        {
            total -= 10;
            acesAsEleven--;
        }

        return total;
    }

    private bool IsBlackjack(List<int> hand)
    {
        return hand.Count == 2 && HandValue(hand) == BlackjackValue;
    }

    // ---------- UI ----------

    private void RefreshUI()
    {
        // The labels only show the totals; the cards themselves are on the table
        if (dealerHand.Count == 0)
        {
            dealerText.text = "Dealer";
        }
        else if (!dealerCardRevealed && dealerHand.Count >= 2)
        {
            dealerText.text = $"Dealer\n{GetCardValue(GetRank(dealerHand[0]))} + ?";
        }
        else
        {
            dealerText.text = $"Dealer\n{HandValue(dealerHand)}";
        }

        playerText.text = playerHand.Count == 0 ? "You" : $"You\n{HandValue(playerHand)}";

        // DEAL between hands; HIT, STAND, DOUBLE, SURRENDER during a hand
        dealButton.gameObject.SetActive(!isHandInProgress);
        hitButton.gameObject.SetActive(isHandInProgress);
        standButton.gameObject.SetActive(isHandInProgress);
        doubleButton.gameObject.SetActive(isHandInProgress);
        surrenderButton.gameObject.SetActive(isHandInProgress);

        dealButton.interactable = run.CanPlay && !isAnimating;
        hitButton.interactable = run.CanPlay && !isDealerTurn && !isAnimating;
        standButton.interactable = run.CanPlay && !isDealerTurn && !isAnimating;
        doubleButton.interactable = CanDoubleDown();
        surrenderButton.interactable = CanMakeFirstDecision();

        int bet = run.GetCurrentBet();
        dealButtonText.text = $"DEAL\nBet {bet}";
        doubleButtonText.text = $"DOUBLE\nBet {bet * 2}";
        surrenderButtonText.text = $"SURRENDER\n-{Mathf.CeilToInt(Mathf.Max(0, bet - run.GetUpgradeTotal(UpgradeType.LossReduction)) / 2f)}";
    }
}