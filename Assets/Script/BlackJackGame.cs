using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Blackjack minigame: get closer to 21 than the dealer without going over.
// Cards are dealt onto the table: the dealer's row, and the player's hand(s).
// Casino split rules: split a pair of equal value into up to 4 hands.
// When the dealer shows an ace, the player is offered insurance (or even money with a blackjack).
public class BlackjackGame : MonoBehaviour
{
    private const int BlackjackValue = 21;
    private const int DealerStandValue = 17;
    private const int DeckSize = 52;
    private const int RanksPerSuit = 13;

    // One of the player's hands. Without a split there is only one.
    private class PlayerHand
    {
        public readonly List<int> Cards = new List<int>();
        public readonly List<TableCard> CardObjects = new List<TableCard>();
        public bool IsDoubled;
        public bool IsSplitHand;   // Made by a split: 21 here is not a natural blackjack
        public bool IsSplitAces;   // Split aces get exactly one card each

        public int BetUnits => IsDoubled ? 2 : 1;
    }

    [Header("Run")]
    [SerializeField] private RunManager run;

    [Header("UI")]
    [SerializeField] private TMP_Text dealerText;
    [SerializeField] private TMP_Text playerText;
    [SerializeField] private Button dealButton;
    [SerializeField] private Button hitButton;
    [SerializeField] private Button standButton;
    [SerializeField] private Button doubleButton;
    [SerializeField] private Button splitButton;
    [SerializeField] private Button surrenderButton;

    [Header("Table")]
    [SerializeField] private TableCard cardPrefab;
    [SerializeField] private Transform deckSpot;          // New cards come from here
    [SerializeField] private Transform dealerRowStart;    // The dealer's first card; the rest line up to the right
    [SerializeField] private Transform playerRowStart;
    [SerializeField] private Transform discardSpot;       // Old cards fly away here
    [SerializeField] private Transform activeHandMarker;  // Old glowing disc: now hidden, its material colors the arrow
    [SerializeField] private float cardSpacing = 2f;      // Distance between cards in a row (a bit more than a card's width)
    [SerializeField] private float handSpacing = 4f;      // Distance between split hands
    [SerializeField] private float splitCardOffset = 0.7f;   // Cards in a split hand overlap, fanned by this much

    [Header("Seats")]
    [SerializeField] private float seatArcRadius = 8.6f;   // The hands sit on an arc around the dealer, like seats at a real table (0 = one straight row)
    [SerializeField] private float seatAngle = 26f;        // Degrees between two seats
    [SerializeField] private float seatCardSpread = 1f;    // Cards of a hand on a seat overlap, fanned by this much (the corners stay readable)
    [SerializeField] private Renderer[] seatFrames;        // The 5 frames printed on the felt, left to right (made by Tools > Rearrange Tables)
    [SerializeField] private Color frameIdleColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color frameUsedColor = new Color(1f, 1f, 1f, 0.8f);
    [SerializeField] private Color frameActiveColor = new Color(1f, 0.8f, 0.25f, 1f);

    [Header("Shoe and Timer")]
    [SerializeField] private CardShoe shoe;                // 8 decks with a cut card (empty = a fresh deck every hand, the old way)
    [SerializeField] private DecisionTimer decisionTimer;  // The countdown ring on the felt (empty = no time limit)
    [SerializeField] private float decisionSeconds = 10f;  // Time to choose; when it runs out the hand stands (insurance: no thanks)
    [SerializeField] private float discardGap = 0.05f;     // Used cards go to the discard holder one after another

    [Header("Rules")]
    [SerializeField] private float winMultiplier = 1.5f;
    [SerializeField] private float blackjackMultiplier = 2.5f;
    [SerializeField] private int baseWinBonus = 3;
    [SerializeField] private int maxHands = 4;
    [SerializeField] private int splitBonusPerHand = 2;   // Boss fight: each extra winning (or losing) hand adds this much
    [SerializeField] private bool allowSurrender = true;  // Untick to remove the surrender option completely

    [Header("Numbers on the Table")]
    [SerializeField] private bool showScreenTotals = false;   // The old "Dealer / You" totals on the screen
    [SerializeField] private float cardHalfLength = 1.3f;     // Half a card's length on the table
    [SerializeField] private float totalFontSize = 8f;
    [SerializeField] private Color totalColor = Color.white;
    [SerializeField] private Color activeTotalColor = new Color(1f, 0.84f, 0.31f);   // The hand being played, after a split
    [SerializeField] private Color bustColor = new Color(1f, 0.35f, 0.35f);

    [Header("Active Hand Arrow")]
    [SerializeField] private float arrowLength = 1.4f;
    [SerializeField] private float arrowWidth = 1.1f;
    [SerializeField] private float arrowGap = 0.35f;     // Space between the hand's number and the arrow's tip
    [SerializeField] private float arrowBob = 0.15f;     // The arrow nudges toward the hand like this
    [SerializeField] private float arrowBobSpeed = 6f;

    [Header("Insurance")]
    [SerializeField] private Button insuranceButton;      // INSURANCE, or EVEN MONEY when the player has a blackjack
    [SerializeField] private Button noInsuranceButton;    // NO THANKS
    [SerializeField] private bool allowInsurance = true;  // Untick to remove insurance and even money completely

    [Header("Animation")]
    [SerializeField] private float dealDelay = 0.2f;         // Time between cards when dealing a new hand
    [SerializeField] private float dealerDrawDelay = 0.6f;   // Pause between the dealer's cards

    private readonly List<int> deck = new List<int>();
    private readonly List<PlayerHand> hands = new List<PlayerHand>();
    private readonly List<int> dealerHand = new List<int>();
    private readonly List<TableCard> dealerCards = new List<TableCard>();

    private TMP_Text dealButtonText;
    private TMP_Text doubleButtonText;
    private TMP_Text splitButtonText;
    private TMP_Text surrenderButtonText;
    private TMP_Text insuranceButtonText;

    private int activeIndex;           // Which hand the player is playing now
    private bool isHandInProgress;
    private bool isDealerTurn;
    private bool isAnimating;          // Cards are moving: buttons stay locked
    private bool dealerCardRevealed;   // Has the dealer's face-down card been turned over?
    private int runningAnimations;     // How many card animations are still playing

    private TextMeshPro dealerTotal;   // Numbers lying on the table
    private readonly List<TextMeshPro> handTotals = new List<TextMeshPro>();
    private Transform handArrow;
    private Vector3 arrowTip;

    private Material[] frameMaterials;
    private bool timedOut;             // The clock ran out this hand: the game chose for the player

    private bool isAskingInsurance;    // The dealer shows an ace: waiting for the player's answer
    private bool isEvenMoneyOffer;     // The player has a blackjack, so the offer is even money
    private int insuranceChoice;       // 0 = no answer yet, 1 = took it, 2 = no thanks
    private int insuranceResult;       // Chips won (+) or lost (-) on insurance, added when the hand is settled
    private string insuranceNote;      // The insurance line in the result message

    private PlayerHand ActiveHand => hands.Count > 0 ? hands[Mathf.Min(activeIndex, hands.Count - 1)] : null;
    private bool HasBlackjackAfterSplit => run.GetUpgradeTotal(UpgradeType.BlackjackAfterSplit) > 0;
    private bool HasInsurance => allowInsurance && insuranceButton != null && noInsuranceButton != null;
    private int InsurancePayout => 2 + run.GetUpgradeTotal(UpgradeType.InsurancePayout);   // Pays this many to 1

    private void Awake()
    {
        dealButtonText = dealButton.GetComponentInChildren<TMP_Text>();
        doubleButtonText = doubleButton.GetComponentInChildren<TMP_Text>();
        splitButtonText = splitButton.GetComponentInChildren<TMP_Text>();
        surrenderButtonText = surrenderButton.GetComponentInChildren<TMP_Text>();

        dealButton.onClick.AddListener(Deal);
        hitButton.onClick.AddListener(Hit);
        standButton.onClick.AddListener(Stand);
        doubleButton.onClick.AddListener(DoubleDown);
        splitButton.onClick.AddListener(Split);
        surrenderButton.onClick.AddListener(Surrender);

        if (insuranceButton != null)
        {
            insuranceButtonText = insuranceButton.GetComponentInChildren<TMP_Text>();
            insuranceButton.onClick.AddListener(TakeInsurance);
        }

        if (noInsuranceButton != null)
        {
            noInsuranceButton.onClick.AddListener(DeclineInsurance);
        }

        CreateTableNumbers();
        CreateHandArrow();
        SetUpSeatFrames();

        if (!showScreenTotals)
        {
            dealerText.gameObject.SetActive(false);
            playerText.gameObject.SetActive(false);
        }
    }

    // The arrow nudges toward the hand being played
    private void Update()
    {
        UpdateSeatFrames();

        if (handArrow != null && handArrow.gameObject.activeSelf)
        {
            float nudge = (Mathf.Sin(Time.time * arrowBobSpeed) + 1f) * 0.5f * arrowBob;
            handArrow.position = arrowTip + Vector3.forward * nudge;
        }
    }

    private void OnEnable()
    {
        run.StateChanged += RefreshUI;
        run.RunStarted += OnRunStarted;
        ResetGame();
        RefreshUI();
    }

    private void OnDisable()
    {
        run.StateChanged -= RefreshUI;
        run.RunStarted -= OnRunStarted;
        StopDecisionClock();
    }

    // A new run starts with a full, freshly shuffled shoe
    private void OnRunStarted()
    {
        if (shoe != null)
        {
            ResetGame();
            shoe.ResetShoe();
        }
    }

    private void ResetGame()
    {
        StopAllCoroutines();
        StopDecisionClock();
        ClearTable();

        hands.Clear();
        dealerHand.Clear();
        activeIndex = 0;
        isHandInProgress = false;
        isDealerTurn = false;
        isAnimating = false;
        dealerCardRevealed = false;
        ClearInsurance();

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
        dealerCardRevealed = false;

        // Forget the last hand straight away, so nothing from it (like the split hands' marker)
        // shows up again while its cards are being cleared off the table
        List<TableCard> oldCards = AllCardObjects();
        hands.Clear();
        dealerHand.Clear();
        dealerCards.Clear();
        activeIndex = 0;
        ClearInsurance();
        RefreshUI();   // The old hand's numbers and arrow go away with its cards

        run.SetMinigameBusy(true);
        timedOut = false;
        yield return DiscardAllRoutine(oldCards);

        // The cut card came out last hand: shuffle the shoe before dealing
        if (shoe != null && shoe.NeedsShuffle)
        {
            run.ShowMessage("Shuffling the shoe...");
            yield return shoe.ShuffleRoutine(OnTable(DealerSlot(0) + Vector3.right * cardSpacing * 0.5f));
        }

        if (shoe == null)
        {
            BuildDeck();
        }

        PlayerHand hand = new PlayerHand();
        hands.Add(hand);

        // Player, dealer, player, dealer. The cards fly one after another, overlapping a little.
        StartCoroutine(Track(DealToPlayerRoutine(hand)));
        yield return new WaitForSeconds(dealDelay);
        StartCoroutine(Track(DealToDealerRoutine(true)));
        yield return new WaitForSeconds(dealDelay);
        StartCoroutine(Track(DealToPlayerRoutine(hand)));
        yield return new WaitForSeconds(dealDelay);
        StartCoroutine(Track(DealToDealerRoutine(false)));

        // Wait until every card has landed and turned over
        yield return new WaitUntil(() => runningAnimations == 0);
        isAnimating = false;

        // The dealer shows an ace: offer insurance (or even money) before checking for blackjack
        if (HasInsurance && DealerShowsAce())
        {
            bool handOver = false;
            yield return InsuranceRoutine(hand, over => handOver = over);

            if (handOver)
            {
                yield break;
            }
        }

        // A natural blackjack on either side ends the hand right away
        if (IsNaturalBlackjack(hand) || IsDealerBlackjack())
        {
            yield return RevealDealerCard();
            FinishHand();
            yield break;
        }

        yield return ContinueActiveHand();
    }

    private void Hit()
    {
        if (!CanAct())
        {
            return;
        }

        StopDecisionClock();
        StartCoroutine(HitRoutine());
    }

    private IEnumerator HitRoutine()
    {
        isAnimating = true;
        RefreshUI();

        yield return DealToPlayerRoutine(ActiveHand);
        isAnimating = false;

        // Bust or 21: this hand is done
        if (HandValue(ActiveHand.Cards) >= BlackjackValue)
        {
            yield return NextHandRoutine();
        }
        else
        {
            run.ShowMessage(HandPrefix() + "Hit or stand?");
            RefreshUI();
            StartDecisionClock();
        }
    }

    private void Stand()
    {
        if (!CanAct())
        {
            return;
        }

        StopDecisionClock();
        StartCoroutine(NextHandRoutine());
    }

    // Double the bet on this hand, take exactly one more card, then stand
    private void DoubleDown()
    {
        if (!CanDoubleDown())
        {
            return;
        }

        StopDecisionClock();
        StartCoroutine(DoubleRoutine());
    }

    private IEnumerator DoubleRoutine()
    {
        ActiveHand.IsDoubled = true;
        isAnimating = true;
        RefreshUI();

        yield return DealToPlayerRoutine(ActiveHand);
        isAnimating = false;

        yield return NextHandRoutine();
    }

    // Split a pair into two hands. Each gets one new card.
    private void Split()
    {
        if (!CanSplit())
        {
            return;
        }

        StopDecisionClock();
        StartCoroutine(SplitRoutine());
    }

    private IEnumerator SplitRoutine()
    {
        isAnimating = true;
        RefreshUI();

        PlayerHand current = ActiveHand;
        PlayerHand newHand = new PlayerHand();

        // Move the second card into the new hand
        newHand.Cards.Add(current.Cards[1]);
        newHand.CardObjects.Add(current.CardObjects[1]);
        current.Cards.RemoveAt(1);
        current.CardObjects.RemoveAt(1);

        bool aces = GetRank(current.Cards[0]) == 1;
        current.IsSplitHand = true;
        newHand.IsSplitHand = true;
        current.IsSplitAces = aces;
        newHand.IsSplitAces = aces;

        hands.Insert(activeIndex + 1, newHand);

        // Slide the cards into their new places, then give each hand a second card
        yield return RelayoutPlayerCards();
        yield return DealToPlayerRoutine(current);
        yield return DealToPlayerRoutine(newHand);

        isAnimating = false;
        yield return ContinueActiveHand();
    }

    // Give up the hand right away and lose only half the bet (only before any split)
    private void Surrender()
    {
        if (!CanSurrender())
        {
            return;
        }

        StopDecisionClock();
        StartCoroutine(SurrenderRoutine());
    }

    private IEnumerator SurrenderRoutine()
    {
        isAnimating = true;
        RefreshUI();

        yield return RevealDealerCard();
        isAnimating = false;

        int lossAmount = GetSurrenderLoss();
        int damage = run.AtBaseBet(GetSurrenderLoss);
        isHandInProgress = false;

        string message = $"Surrendered. -{lossAmount} chips";

        if (insuranceNote != null)
        {
            message += "\n" + insuranceNote;
        }

        run.SetMinigameBusy(false);
        run.FinishPlay(-lossAmount + insuranceResult, -damage, message);
    }

    // ---------- Moving between hands ----------

    // Ask the player what to do with the active hand, or skip it if it can't take more cards
    private IEnumerator ContinueActiveHand()
    {
        PlayerHand hand = ActiveHand;

        if (hand.IsSplitAces || HandValue(hand.Cards) >= BlackjackValue)
        {
            yield return NextHandRoutine();
            yield break;
        }

        run.ShowMessage(HandPrefix() + BuildChoicesMessage());
        RefreshUI();
        StartDecisionClock();
    }

    // This hand is done: move on to the next one, or to the dealer when all hands are done
    private IEnumerator NextHandRoutine()
    {
        activeIndex++;

        if (activeIndex < hands.Count)
        {
            RefreshUI();
            yield return ContinueActiveHand();
            yield break;
        }

        activeIndex = hands.Count - 1;

        // Every hand went bust: the dealer doesn't need to draw
        if (AllHandsBust())
        {
            yield return RevealDealerCard();
            FinishHand();
        }
        else
        {
            yield return DealerTurnRoutine();
        }
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
            yield return DealToDealerRoutine(true);
            yield return new WaitForSeconds(dealerDrawDelay);
        }

        isDealerTurn = false;
        FinishHand();
    }

    private bool AllHandsBust()
    {
        foreach (PlayerHand hand in hands)
        {
            if (HandValue(hand.Cards) <= BlackjackValue)
            {
                return false;
            }
        }

        return true;
    }

    private string HandPrefix()
    {
        return hands.Count > 1 ? $"Hand {activeIndex + 1} of {hands.Count}: " : "";
    }

    // ---------- What the player is allowed to do ----------

    private int TotalBetUnits()
    {
        int units = 0;

        foreach (PlayerHand hand in hands)
        {
            units += hand.BetUnits;
        }

        return units;
    }

    // Enough chips to put one more bet on the table?
    private bool CanAffordOneMoreBet()
    {
        return run.Chips >= run.GetCurrentBet() * (TotalBetUnits() + 1);
    }

    private bool CanAct()
    {
        return isHandInProgress && !isDealerTurn && !isAnimating && !isAskingInsurance && run.CanPlay && ActiveHand != null;
    }

    // Double and split are only possible on the first two cards of a hand
    private bool IsOnFirstTwoCards()
    {
        return CanAct() && ActiveHand.Cards.Count == 2 && !ActiveHand.IsSplitAces;
    }

    private bool CanDoubleDown()
    {
        // Doubling is allowed on any hand's first two cards, split hands included
        return IsOnFirstTwoCards() && CanAffordOneMoreBet();
    }

    private bool CanSplit()
    {
        return IsOnFirstTwoCards() && IsPair(ActiveHand) && hands.Count < maxHands && CanAffordOneMoreBet();
    }

    // Two cards of the same value (8 and 8, or K and J): the hand could be split
    private bool IsPair(PlayerHand hand)
    {
        if (hand == null || hand.Cards.Count != 2)
        {
            return false;
        }

        return GetCardValue(GetRank(hand.Cards[0])) == GetCardValue(GetRank(hand.Cards[1]));
    }

    // Lists only the moves the player can make right now, and says why split or double is locked
    private string BuildChoicesMessage()
    {
        List<string> moves = new List<string> { "Hit", "stand" };

        if (CanDoubleDown())
        {
            moves.Add("double");
        }

        if (CanSplit())
        {
            moves.Add("split");
        }

        if (CanSurrender())
        {
            moves.Add("surrender");
        }

        string lastMove = moves[moves.Count - 1];
        moves.RemoveAt(moves.Count - 1);
        string message = $"{string.Join(", ", moves)} or {lastMove}?";

        int chipsNeeded = run.GetCurrentBet() * (TotalBetUnits() + 1);
        bool onFirstTwoCards = IsOnFirstTwoCards();

        if (onFirstTwoCards && IsPair(ActiveHand) && !CanSplit())
        {
            message += hands.Count >= maxHands
                ? $"\nCan't split: {maxHands} hands is the limit."
                : $"\nNot enough chips to split (need {chipsNeeded}).";
        }
        else if (onFirstTwoCards && !CanAffordOneMoreBet())
        {
            message += $"\nNot enough chips to double (need {chipsNeeded}).";
        }

        return message;
    }

    private bool CanSurrender()
    {
        return allowSurrender && IsOnFirstTwoCards() && hands.Count == 1;
    }

    // ---------- Why a button is locked (null = it isn't, or it's just waiting for cards to land) ----------

    private string DoubleLockReason()
    {
        if (!CanAct() || CanDoubleDown())
        {
            return null;
        }

        if (ActiveHand.Cards.Count != 2)
        {
            return "First 2 cards only";
        }

        if (ActiveHand.IsSplitAces)
        {
            return "Not on split aces";
        }

        return $"Need {ChipsForOneMoreBet()} chips";
    }

    private string SplitLockReason()
    {
        if (!CanAct() || CanSplit())
        {
            return null;
        }

        if (ActiveHand.Cards.Count != 2)
        {
            return "First 2 cards only";
        }

        if (ActiveHand.IsSplitAces)
        {
            return "Not on split aces";
        }

        if (!IsPair(ActiveHand))
        {
            return "Needs a pair";
        }

        if (hands.Count >= maxHands)
        {
            return $"{maxHands} hands max";
        }

        return $"Need {ChipsForOneMoreBet()} chips";
    }

    private string SurrenderLockReason()
    {
        if (!CanAct() || CanSurrender())
        {
            return null;
        }

        return hands.Count > 1 ? "Not after a split" : "First 2 cards only";
    }

    private int ChipsForOneMoreBet()
    {
        return run.GetCurrentBet() * (TotalBetUnits() + 1);
    }

    // ---------- Insurance and even money ----------

    private bool DealerShowsAce()
    {
        return dealerHand.Count >= 1 && GetRank(dealerHand[0]) == 1;
    }

    // Insurance costs half the bet
    private int GetInsuranceCost()
    {
        return Mathf.CeilToInt(run.GetCurrentBet() / 2f);
    }

    private bool CanAffordInsurance()
    {
        return run.Chips >= run.GetCurrentBet() + GetInsuranceCost();
    }

    private void TakeInsurance()
    {
        if (!isAskingInsurance || insuranceChoice != 0 || (!isEvenMoneyOffer && !CanAffordInsurance()))
        {
            return;
        }

        insuranceChoice = 1;
        StopDecisionClock();
    }

    private void DeclineInsurance()
    {
        if (!isAskingInsurance || insuranceChoice != 0)
        {
            return;
        }

        insuranceChoice = 2;
        StopDecisionClock();
    }

    private void ClearInsurance()
    {
        isAskingInsurance = false;
        isEvenMoneyOffer = false;
        insuranceChoice = 0;
        insuranceResult = 0;
        insuranceNote = null;
    }

    // Ask the question, wait for the answer, then the dealer checks for blackjack.
    // Calls back true if the hand is already over (the player took even money).
    private IEnumerator InsuranceRoutine(PlayerHand hand, System.Action<bool> onDone)
    {
        isEvenMoneyOffer = IsNaturalBlackjack(hand);
        isAskingInsurance = true;
        insuranceChoice = 0;
        run.ShowMessage(BuildInsuranceMessage(hand));
        RefreshUI();
        StartDecisionClock();

        yield return new WaitUntil(() => insuranceChoice != 0);

        bool took = insuranceChoice == 1;
        isAskingInsurance = false;
        RefreshUI();

        // Even money: a sure win, paid like a normal win, and the hand ends right here
        if (isEvenMoneyOffer && took)
        {
            int chips = GetWinAmount(hand, winMultiplier);
            int damage = run.AtBaseBet(() => GetWinAmount(hand, winMultiplier));

            yield return RevealDealerCard();
            isHandInProgress = false;
            run.SetMinigameBusy(false);
            RefreshUI();
            run.FinishPlay(chips, damage, $"Even money! +{chips} chips");
            onDone(true);
            yield break;
        }

        if (took)
        {
            int cost = GetInsuranceCost();

            if (IsDealerBlackjack())
            {
                insuranceResult = cost * InsurancePayout;
                insuranceNote = $"Insurance pays {InsurancePayout} to 1: +{insuranceResult} chips";
            }
            else
            {
                insuranceResult = -cost;
                insuranceNote = $"Insurance lost: -{cost} chips";
            }
        }

        // The dealer peeks at the face-down card
        run.ShowMessage("The dealer checks for blackjack...");
        yield return new WaitForSeconds(0.7f);

        if (!IsDealerBlackjack())
        {
            run.ShowMessage(took ? "No blackjack. Insurance lost." : "No blackjack. Play on!");
            yield return new WaitForSeconds(0.9f);
        }

        onDone(false);
    }

    private string BuildInsuranceMessage(PlayerHand hand)
    {
        if (isEvenMoneyOffer)
        {
            return $"Dealer shows an Ace! Take EVEN MONEY for a sure +{GetWinAmount(hand, winMultiplier)} chips, "
                + "or say NO THANKS and hope the dealer has no blackjack.";
        }

        string message = $"Dealer shows an Ace! INSURANCE costs {GetInsuranceCost()} chips and pays {InsurancePayout} to 1 "
            + "if the dealer has blackjack.";

        if (!CanAffordInsurance())
        {
            message += $" (Not enough chips: you need {run.GetCurrentBet() + GetInsuranceCost()}.)";
        }

        return message;
    }

    // ---------- Cards on the table ----------

    // Where a card of the player's goes. One hand: a normal row. Split hands: small fanned groups side by side.
    // With seats: every hand sits on its own seat along the arc; the first two cards are centered on the seat.
    private Vector3 PlayerSlot(int handIndex, int cardIndex)
    {
        if (seatArcRadius > 0f)
        {
            int seat = SeatFor(handIndex);

            return SeatPosition(seat)
                + SeatRotation(seat) * Vector3.right * seatCardSpread * (cardIndex - 0.5f)
                + Vector3.up * 0.01f * cardIndex;
        }

        if (hands.Count <= 1)
        {
            return playerRowStart.position + Vector3.right * cardSpacing * cardIndex;
        }

        return playerRowStart.position
            + Vector3.right * (handSpacing * handIndex + splitCardOffset * cardIndex)
            + Vector3.up * 0.01f * cardIndex;   // Later cards lie slightly on top
    }

    // Which seat a hand sits on: -2 (far left) to 2 (far right). One hand sits in the middle;
    // split hands spread out evenly: 2 hands on -1 and 1, 3 on -1, 0, 1, 4 on -2, -1, 1, 2.
    private int SeatFor(int handIndex)
    {
        switch (hands.Count)
        {
            case 2: return handIndex == 0 ? -1 : 1;
            case 3: return handIndex - 1;
            case 4: return handIndex < 2 ? handIndex - 2 : handIndex - 1;
            default: return 0;
        }
    }

    // The middle of a seat. The middle seat is where a 2-card hand used to start (playerRowStart);
    // the others go around the arc's center, which lies toward the dealer.
    private Vector3 SeatPosition(int seat)
    {
        Vector3 middleSeat = playerRowStart.position + Vector3.right * cardSpacing * 0.5f;
        Vector3 arcCenter = middleSeat + Vector3.forward * seatArcRadius;
        Vector3 fromCenter = SeatRotation(seat) * (Vector3.back * seatArcRadius);
        return new Vector3(arcCenter.x + fromCenter.x, middleSeat.y, arcCenter.z + fromCenter.z);
    }

    // Seats turn with the arc, so their cards face the arc's center (the dealer)
    private Quaternion SeatRotation(int seat)
    {
        return seatArcRadius > 0f ? Quaternion.Euler(0f, -seat * seatAngle, 0f) : Quaternion.identity;
    }

    private Quaternion HandRotation(int handIndex)
    {
        return SeatRotation(SeatFor(handIndex));
    }

    private Vector3 DealerSlot(int cardIndex)
    {
        return dealerRowStart.position + Vector3.right * cardSpacing * cardIndex;
    }

    private TableCard SpawnCard()
    {
        if (shoe != null)
        {
            return shoe.SpawnCard(cardPrefab);
        }

        TableCard cardObject = Instantiate(cardPrefab, deckSpot.position, Quaternion.identity);
        cardObject.SetSprite(CardArt.GetCardBack());
        return cardObject;
    }

    // From the shoe, the card is pulled out of the mouth first
    private IEnumerator PullFromShoe(TableCard cardObject)
    {
        if (shoe != null)
        {
            yield return shoe.SlideOut(cardObject);
        }
    }

    private IEnumerator DealToPlayerRoutine(PlayerHand hand)
    {
        int card = DrawCard();
        hand.Cards.Add(card);

        TableCard cardObject = SpawnCard();
        hand.CardObjects.Add(cardObject);

        yield return PullFromShoe(cardObject);
        int handIndex = hands.IndexOf(hand);
        yield return cardObject.MoveTo(PlayerSlot(handIndex, hand.CardObjects.Count - 1), HandRotation(handIndex));
        yield return cardObject.FlipTo(CardSprite(card));
        RefreshUI();
    }

    private IEnumerator DealToDealerRoutine(bool faceUp)
    {
        int card = DrawCard();
        dealerHand.Add(card);

        TableCard cardObject = SpawnCard();
        dealerCards.Add(cardObject);

        yield return PullFromShoe(cardObject);
        yield return cardObject.MoveTo(DealerSlot(dealerCards.Count - 1), Quaternion.identity);

        if (faceUp)
        {
            yield return cardObject.FlipTo(CardSprite(card));
        }

        RefreshUI();
    }

    // After a split, every card of the player slides to its new place
    private IEnumerator RelayoutPlayerCards()
    {
        for (int handIndex = 0; handIndex < hands.Count; handIndex++)
        {
            List<TableCard> cardObjects = hands[handIndex].CardObjects;

            for (int cardIndex = 0; cardIndex < cardObjects.Count; cardIndex++)
            {
                StartCoroutine(Track(cardObjects[cardIndex].MoveTo(PlayerSlot(handIndex, cardIndex), HandRotation(handIndex), false)));
            }
        }

        yield return new WaitUntil(() => runningAnimations == 0);
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

    // Fly every given card off the table, and wait until they are all gone
    private IEnumerator DiscardAllRoutine(List<TableCard> cards)
    {
        foreach (TableCard card in cards)
        {
            StartCoroutine(Track(DiscardRoutine(card)));

            if (shoe != null)
            {
                yield return new WaitForSeconds(discardGap);
            }
        }

        yield return new WaitUntil(() => runningAnimations == 0);
    }

    private IEnumerator DiscardRoutine(TableCard card)
    {
        if (card == null)
        {
            yield break;
        }

        // Into the discard holder, face down
        if (shoe != null)
        {
            Vector3 spot = shoe.ReserveDiscardSpot(out Quaternion rotation);
            StartCoroutine(card.FlipTo(CardArt.GetCardBack(), false));
            yield return card.MoveTo(spot, rotation);
            shoe.Land(card);
            yield break;
        }

        yield return card.MoveTo(discardSpot.position);
        Destroy(card.gameObject);
    }

    // Instantly remove every card (used when the table is reset)
    private void ClearTable()
    {
        List<TableCard> onTable = AllCardObjects();

        if (shoe != null)
        {
            shoe.AddDiscardedInstantly(onTable.Count);
        }

        foreach (TableCard card in onTable)
        {
            if (card != null)
            {
                Destroy(card.gameObject);
            }
        }

        dealerCards.Clear();

        foreach (PlayerHand hand in hands)
        {
            hand.CardObjects.Clear();
        }

        runningAnimations = 0;
    }

    private List<TableCard> AllCardObjects()
    {
        List<TableCard> all = new List<TableCard>(dealerCards);

        foreach (PlayerHand hand in hands)
        {
            all.AddRange(hand.CardObjects);
        }

        return all;
    }

    private Sprite CardSprite(int card)
    {
        return CardArt.GetCard(GetRank(card), GetSuit(card));
    }

    // ---------- Results ----------

    private void FinishHand()
    {
        isHandInProgress = false;

        int chipsWon = 0;
        int chipsLost = 0;
        int winningHands = 0;
        int losingHands = 0;
        List<string> results = new List<string>();

        int damageDealt = 0;    // Boss fight: worked out at bet x1, so the bet level only changes chips
        int damageTaken = 0;

        for (int i = 0; i < hands.Count; i++)
        {
            PlayerHand settled = hands[i];
            int change = SettleHand(settled, out string reason);
            int damage = run.AtBaseBet(() => SettleHand(settled, out _));

            if (damage > 0)
            {
                damageDealt += damage;
            }
            else
            {
                damageTaken -= damage;
            }

            string chipsPart = change > 0 ? $"+{change}" : change < 0 ? $"{change}" : "0";

            results.Add(hands.Count > 1 ? $"Hand {i + 1}: {reason} {chipsPart}" : $"{reason} {chipsPart} chips");

            if (change > 0)
            {
                chipsWon += change;
                winningHands++;
            }

            if (PaysAsBlackjack(hands[i]) && change > 0)
            {
                AudioManager.Play(SoundId.Blackjack);
            }
            else if (change < 0)
            {
                chipsLost -= change;
                losingHands++;
            }
        }

        // Boss fight: each extra winning or losing hand adds a little damage
        if (run.IsBossFight)
        {
            damageDealt += splitBonusPerHand * Mathf.Max(0, winningHands - 1);
            damageTaken += splitBonusPerHand * Mathf.Max(0, losingHands - 1);
        }

        // Insurance only ever moves chips, never damage
        if (insuranceResult > 0)
        {
            chipsWon += insuranceResult;
        }
        else
        {
            chipsLost -= insuranceResult;
        }

        // Split hands share one line ("Hand 1: You win! +18  ·  Hand 2: Bust! -10"), so the message stays short
        string message = string.Join(hands.Count > 1 ? "  ·  " : "\n", results);

        if (insuranceNote != null)
        {
            message += "\n" + insuranceNote;
        }

        if (hands.Count > 1 || insuranceNote != null)
        {
            int total = chipsWon - chipsLost;
            message += total >= 0 ? $"\nTotal: +{total} chips" : $"\nTotal: {total} chips";
        }

        if (timedOut)
        {
            message += "\nTime ran out, so you stood.";
        }

        if (shoe != null && shoe.NeedsShuffle)
        {
            message += "\nThe cut card is out: the shoe is shuffled before the next hand.";
        }

        StopDecisionClock();

        run.SetMinigameBusy(false);
        run.FinishMixedPlay(chipsWon, chipsLost, damageDealt, damageTaken, message);
    }

    // How many chips one hand wins (positive) or loses (negative) against the dealer
    private int SettleHand(PlayerHand hand, out string reason)
    {
        int playerValue = HandValue(hand.Cards);
        int dealerValue = HandValue(dealerHand);
        bool playerBlackjack = PaysAsBlackjack(hand);
        bool dealerBlackjack = IsDealerBlackjack();

        int chipChange;

        if (playerValue > BlackjackValue)
        {
            chipChange = -GetLossAmount(hand);
            reason = "Bust!";
        }
        else if (playerBlackjack && dealerBlackjack)
        {
            chipChange = 0;
            reason = "Both have blackjack!";
        }
        else if (playerBlackjack)
        {
            chipChange = GetWinAmount(hand, blackjackMultiplier);
            reason = "BLACKJACK!";
        }
        else if (dealerBlackjack)
        {
            chipChange = -GetLossAmount(hand);
            reason = "Dealer has blackjack!";
        }
        else if (dealerValue > BlackjackValue)
        {
            chipChange = GetWinAmount(hand, winMultiplier);
            reason = "Dealer busts!";
        }
        else if (playerValue > dealerValue)
        {
            chipChange = GetWinAmount(hand, winMultiplier);
            reason = "You win!";
        }
        else if (playerValue < dealerValue)
        {
            chipChange = -GetLossAmount(hand);
            reason = "Dealer wins!";
        }
        else
        {
            chipChange = 0;
            reason = "Push!";
        }

        if (hand.IsDoubled)
        {
            reason = "Doubled! " + reason;
        }

        return chipChange;
    }

    private int GetHandBet(PlayerHand hand)
    {
        return run.GetCurrentBet() * hand.BetUnits;
    }

    private int GetWinAmount(PlayerHand hand, float multiplier)
    {
        int baseWin = Mathf.Max(1, Mathf.RoundToInt(GetHandBet(hand) * multiplier) + baseWinBonus);

        int percentBonus = run.GetUpgradeTotal(UpgradeType.WinPayoutPercent);
        int boostedWin = Mathf.RoundToInt(baseWin * (100 + percentBonus) / 100f);

        return boostedWin + run.GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
    }

    private int GetLossAmount(PlayerHand hand)
    {
        return Mathf.Max(0, GetHandBet(hand) - run.GetUpgradeTotal(UpgradeType.LossReduction));
    }

    private int GetSurrenderLoss()
    {
        return Mathf.CeilToInt(GetLossAmount(hands[0]) / 2f);
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
        if (shoe != null)
        {
            return shoe.Draw();
        }

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

    private int HandValue(List<int> cards)
    {
        int total = 0;
        int acesAsEleven = 0;

        foreach (int card in cards)
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

    // Only the first two cards of an unsplit hand can be a natural blackjack
    private bool IsNaturalBlackjack(PlayerHand hand)
    {
        return !hand.IsSplitHand && hand.Cards.Count == 2 && HandValue(hand.Cards) == BlackjackValue;
    }

    // A natural blackjack, or (with the Blackjack After Split upgrade) two cards making 21 on a split hand
    private bool PaysAsBlackjack(PlayerHand hand)
    {
        if (IsNaturalBlackjack(hand))
        {
            return true;
        }

        return HasBlackjackAfterSplit
            && hand.IsSplitHand
            && hand.Cards.Count == 2
            && HandValue(hand.Cards) == BlackjackValue;
    }

    private bool IsDealerBlackjack()
    {
        return dealerHand.Count == 2 && HandValue(dealerHand) == BlackjackValue;
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

        playerText.text = BuildPlayerLabel();

        // DEAL between hands; the other buttons during a hand; only the two insurance buttons while insurance is offered
        bool playing = isHandInProgress && !isAskingInsurance;
        dealButton.gameObject.SetActive(!isHandInProgress);
        hitButton.gameObject.SetActive(playing);
        standButton.gameObject.SetActive(playing);
        doubleButton.gameObject.SetActive(playing);
        splitButton.gameObject.SetActive(playing);
        surrenderButton.gameObject.SetActive(playing && allowSurrender);

        if (HasInsurance)
        {
            insuranceButton.gameObject.SetActive(isAskingInsurance);
            noInsuranceButton.gameObject.SetActive(isAskingInsurance);
            insuranceButton.interactable = isAskingInsurance && (isEvenMoneyOffer || CanAffordInsurance());
            noInsuranceButton.interactable = isAskingInsurance;

            if (insuranceButtonText != null && isAskingInsurance)
            {
                insuranceButtonText.text = isEvenMoneyOffer
                    ? $"EVEN MONEY\nTake +{GetWinAmount(hands[0], winMultiplier)} chips"
                    : CanAffordInsurance()
                        ? $"INSURANCE\nBet +{GetInsuranceCost()} chips"
                        : $"INSURANCE\nNeed {run.GetCurrentBet() + GetInsuranceCost()} chips";
            }
        }

        dealButton.interactable = run.CanPlay && !isAnimating;
        hitButton.interactable = CanAct();
        standButton.interactable = CanAct();
        doubleButton.interactable = CanDoubleDown();
        splitButton.interactable = CanSplit();
        surrenderButton.interactable = CanSurrender();

        // Each button says what it costs, or, when it's locked, why
        int bet = run.GetCurrentBet();
        dealButtonText.text = run.Chips < bet && run.CanPlay ? $"DEAL\nNeed {bet} chips" : $"DEAL\nBet {bet} chips";
        doubleButtonText.text = "DOUBLE\n" + (DoubleLockReason() ?? $"Bet +{bet} chips");
        splitButtonText.text = "SPLIT\n" + (SplitLockReason() ?? $"Bet +{bet} chips");
        surrenderButtonText.text = "SURRENDER\n" + (SurrenderLockReason()
            ?? $"Lose {Mathf.CeilToInt(Mathf.Max(0, bet - run.GetUpgradeTotal(UpgradeType.LossReduction)) / 2f)} chips");

        UpdateTableNumbers();
        UpdateHandArrow();
    }

    // "You 18", or with split hands "You 12  [18]  20" (the active hand in brackets)
    private string BuildPlayerLabel()
    {
        if (hands.Count == 0 || hands[0].Cards.Count == 0)
        {
            return "You";
        }

        if (hands.Count == 1)
        {
            return $"You\n{HandValue(hands[0].Cards)}";
        }

        List<string> values = new List<string>();
        bool showActive = isHandInProgress && !isDealerTurn;

        for (int i = 0; i < hands.Count; i++)
        {
            string value = HandValue(hands[i].Cards).ToString();
            values.Add(showActive && i == activeIndex ? $"<color=#FFD54F>[{value}]</color>" : value);
        }

        return "You\n" + string.Join("  ", values);
    }

    // ---------- Numbers and arrow on the table ----------

    // The dealer's total sits left of the dealer's cards; each hand's total sits just below it
    private void UpdateTableNumbers()
    {
        if (dealerTotal != null)
        {
            bool showDealer = dealerHand.Count > 0;
            dealerTotal.gameObject.SetActive(showDealer);

            if (showDealer)
            {
                string value = !dealerCardRevealed && dealerHand.Count >= 2
                    ? $"{GetCardValue(GetRank(dealerHand[0]))} + ?"
                    : TotalText(dealerHand, false);

                dealerTotal.text = $"<size=55%>DEALER</size>\n{value}";
                dealerTotal.color = HandValue(dealerHand) > BlackjackValue && dealerCardRevealed ? bustColor : totalColor;
                dealerTotal.transform.position = OnTable(DealerSlot(0) + Vector3.left * (cardSpacing * 0.5f + 0.3f));
            }
        }

        bool split = hands.Count > 1;
        bool playing = isHandInProgress && !isDealerTurn;

        for (int i = 0; i < handTotals.Count; i++)
        {
            TextMeshPro total = handTotals[i];
            bool show = i < hands.Count && hands[i].CardObjects.Count > 0;
            total.gameObject.SetActive(show);

            if (!show)
            {
                continue;
            }

            PlayerHand hand = hands[i];
            int value = HandValue(hand.Cards);
            total.text = TotalText(hand.Cards, IsNaturalBlackjack(hand));
            total.color = value > BlackjackValue ? bustColor
                : split && playing && i == activeIndex ? activeTotalColor
                : totalColor;
            Quaternion seatRotation = HandRotation(i);
            total.transform.SetPositionAndRotation(
                OnTable(HandCenter(i) + seatRotation * Vector3.back * (cardHalfLength + 0.6f)),
                seatRotation * Quaternion.Euler(90f, 0f, 0f));
        }
    }

    private string TotalText(List<int> cards, bool naturalBlackjack)
    {
        int value = HandValue(cards);

        if (value > BlackjackValue)
        {
            return "BUST";
        }

        return naturalBlackjack ? "BLACKJACK" : value.ToString();
    }

    // The arrow points at the hand being played, only when there are split hands
    private void UpdateHandArrow()
    {
        if (handArrow == null)
        {
            return;
        }

        // With seat frames, the active hand's frame lights up instead
        bool hasFrames = seatFrames != null && seatFrames.Length > 0 && seatArcRadius > 0f;
        bool show = !hasFrames && isHandInProgress && !isDealerTurn && !isAskingInsurance && hands.Count > 1 && ActiveHand.CardObjects.Count > 0;
        handArrow.gameObject.SetActive(show);

        if (show)
        {
            // Below the hand's number, pointing up at the cards
            arrowTip = OnTable(HandCenter(activeIndex) + Vector3.back * (cardHalfLength + 1f + arrowGap));
            handArrow.position = arrowTip;
        }
    }

    // The middle of a hand's cards on the table
    private Vector3 HandCenter(int handIndex)
    {
        int cardCount = Mathf.Max(1, hands[handIndex].CardObjects.Count);

        if (seatArcRadius > 0f)
        {
            return PlayerSlot(handIndex, 0) + HandRotation(handIndex) * Vector3.right * seatCardSpread * (cardCount - 1) / 2f;
        }

        float spread = hands.Count > 1 ? splitCardOffset : cardSpacing;
        return PlayerSlot(handIndex, 0) + Vector3.right * spread * (cardCount - 1) / 2f;
    }

    // Just above the table surface, so nothing flickers into the cloth
    private Vector3 OnTable(Vector3 point)
    {
        point.y = playerRowStart.position.y + 0.02f;
        return point;
    }

    private void CreateTableNumbers()
    {
        dealerTotal = CreateTableText("DealerTotal");
        dealerTotal.alignment = TextAlignmentOptions.Right;
        dealerTotal.rectTransform.pivot = new Vector2(1f, 0.5f);   // Its right edge sits next to the dealer's cards

        for (int i = 0; i < maxHands; i++)
        {
            handTotals.Add(CreateTableText($"HandTotal{i + 1}"));
        }
    }

    // White text lying flat on the table, readable from the player's side
    private TextMeshPro CreateTableText(string objectName)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.transform.SetParent(playerRowStart.parent, false);
        textObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        TextMeshPro text = textObject.AddComponent<TextMeshPro>();

        if (dealerText != null)
        {
            text.font = dealerText.font;
        }

        text.fontSize = totalFontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(4f, 2f);
        text.color = totalColor;
        text.outlineWidth = 0.2f;
        text.outlineColor = new Color32(0, 0, 0, 255);

        textObject.SetActive(false);
        return text;
    }

    // ---------- Decision clock ----------

    private void StartDecisionClock()
    {
        if (decisionTimer != null)
        {
            decisionTimer.Begin(decisionSeconds, OnDecisionTimeout, () => run.CanPlay && (CanAct() || isAskingInsurance));
        }
    }

    private void StopDecisionClock()
    {
        if (decisionTimer != null)
        {
            decisionTimer.Cancel();
        }
    }

    // Time's up: the safest choice. Insurance: no thanks. Otherwise: stand (no more cards, no more chips at risk).
    private void OnDecisionTimeout()
    {
        if (isAskingInsurance)
        {
            DeclineInsurance();
            return;
        }

        timedOut = true;
        Stand();
    }

    // ---------- Seat frames ----------

    // Each frame gets its own material, so frames can light up on their own
    private void SetUpSeatFrames()
    {
        if (seatFrames == null)
        {
            return;
        }

        frameMaterials = new Material[seatFrames.Length];

        for (int i = 0; i < seatFrames.Length; i++)
        {
            if (seatFrames[i] != null)
            {
                frameMaterials[i] = seatFrames[i].material;
            }
        }
    }

    // Every frame sits on its seat. Frames with cards glow; the hand being played (after a split) glows gold.
    private void UpdateSeatFrames()
    {
        if (frameMaterials == null || seatArcRadius <= 0f)
        {
            return;
        }

        int middle = seatFrames.Length / 2;
        bool playing = isHandInProgress && !isDealerTurn && !isAskingInsurance;

        for (int i = 0; i < seatFrames.Length; i++)
        {
            if (seatFrames[i] == null)
            {
                continue;
            }

            int seat = i - middle;
            Transform frame = seatFrames[i].transform;
            frame.SetPositionAndRotation(OnTable(SeatPosition(seat)) + Vector3.up * 0.005f, SeatRotation(seat));

            Color color = frameIdleColor;

            for (int h = 0; h < hands.Count; h++)
            {
                if (SeatFor(h) != seat || hands[h].CardObjects.Count == 0)
                {
                    continue;
                }

                color = frameUsedColor;

                if (playing && hands.Count > 1 && h == activeIndex)
                {
                    float pulse = (Mathf.Sin(Time.time * 5f) + 1f) * 0.5f;
                    color = Color.Lerp(frameActiveColor, Color.white, pulse * 0.35f);
                }
            }

            frameMaterials[i].SetColor("_BaseColor", color);
        }
    }

    // A flat arrow pointing away from the player (+Z), its tip at the object's position.
    // It uses the old marker's glowing material.
    private void CreateHandArrow()
    {
        float headLength = arrowLength * 0.5f;
        float halfWidth = arrowWidth * 0.5f;
        float halfShaft = arrowWidth * 0.2f;

        Vector3[] points =
        {
            new Vector3(0f, 0f, 0f),                         // Tip
            new Vector3(-halfWidth, 0f, -headLength),
            new Vector3(halfWidth, 0f, -headLength),
            new Vector3(-halfShaft, 0f, -headLength),        // Shaft
            new Vector3(halfShaft, 0f, -headLength),
            new Vector3(-halfShaft, 0f, -arrowLength),
            new Vector3(halfShaft, 0f, -arrowLength)
        };

        // Both sides, so it shows whichever way the triangles face
        int[] triangles =
        {
            0, 2, 1, 0, 1, 2,
            3, 4, 6, 3, 6, 4,
            3, 6, 5, 3, 5, 6
        };

        Mesh mesh = new Mesh { name = "HandArrow" };
        Vector3[] normals = new Vector3[points.Length];

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.up;
        }

        mesh.vertices = points;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        GameObject arrow = new GameObject("HandArrow");
        arrow.transform.SetParent(playerRowStart.parent, false);
        arrow.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer arrowRenderer = arrow.AddComponent<MeshRenderer>();
        arrowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (activeHandMarker != null)
        {
            Renderer markerRenderer = activeHandMarker.GetComponent<Renderer>();

            if (markerRenderer != null)
            {
                arrowRenderer.sharedMaterial = markerRenderer.sharedMaterial;
            }

            activeHandMarker.gameObject.SetActive(false);
        }

        arrow.SetActive(false);
        handArrow = arrow.transform;
    }
}