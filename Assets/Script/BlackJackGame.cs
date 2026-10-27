using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Blackjack minigame: get closer to 21 than the dealer without going over.
// Cards are dealt onto the table: the dealer's row, and the player's hand(s).
// Casino split rules: split a pair of equal value into up to 4 hands.
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
    [SerializeField] private Transform activeHandMarker;  // Glowing marker under the hand being played (after a split)
    [SerializeField] private float cardSpacing = 2f;      // Distance between cards in a row (a bit more than a card's width)
    [SerializeField] private float handSpacing = 4f;      // Distance between split hands
    [SerializeField] private float splitCardOffset = 0.7f;   // Cards in a split hand overlap, fanned by this much

    [Header("Rules")]
    [SerializeField] private float winMultiplier = 1.5f;
    [SerializeField] private float blackjackMultiplier = 2.5f;
    [SerializeField] private int baseWinBonus = 3;
    [SerializeField] private int maxHands = 4;
    [SerializeField] private int splitBonusPerHand = 2;   // Boss fight: each extra winning (or losing) hand adds this much
    [SerializeField] private bool allowSurrender = true;  // Untick to remove the surrender option completely

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

    private int activeIndex;           // Which hand the player is playing now
    private bool isHandInProgress;
    private bool isDealerTurn;
    private bool isAnimating;          // Cards are moving: buttons stay locked
    private bool dealerCardRevealed;   // Has the dealer's face-down card been turned over?
    private int runningAnimations;     // How many card animations are still playing

    private PlayerHand ActiveHand => hands.Count > 0 ? hands[Mathf.Min(activeIndex, hands.Count - 1)] : null;
    private bool HasDoubleAfterSplit => run.GetUpgradeTotal(UpgradeType.DoubleAfterSplit) > 0;

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

        hands.Clear();
        dealerHand.Clear();
        activeIndex = 0;
        isHandInProgress = false;
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
        dealerCardRevealed = false;
        run.SetMinigameBusy(true);

        // Clear last hand's cards off the table first
        yield return ClearTableRoutine();

        BuildDeck();
        hands.Clear();
        dealerHand.Clear();
        activeIndex = 0;

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
        }
    }

    private void Stand()
    {
        if (!CanAct())
        {
            return;
        }

        StartCoroutine(NextHandRoutine());
    }

    // Double the bet on this hand, take exactly one more card, then stand
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

        string choices = CanSurrender() ? "Hit, stand, double, split or surrender?" : "Hit, stand, double or split?";
        run.ShowMessage(HandPrefix() + choices);
        RefreshUI();
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
        return isHandInProgress && !isDealerTurn && !isAnimating && run.CanPlay && ActiveHand != null;
    }

    // Double and split are only possible on the first two cards of a hand
    private bool IsOnFirstTwoCards()
    {
        return CanAct() && ActiveHand.Cards.Count == 2 && !ActiveHand.IsSplitAces;
    }

    private bool CanDoubleDown()
    {
        bool allowedAfterSplit = hands.Count == 1 || HasDoubleAfterSplit;
        return IsOnFirstTwoCards() && allowedAfterSplit && CanAffordOneMoreBet();
    }

    private bool CanSplit()
    {
        if (!IsOnFirstTwoCards() || hands.Count >= maxHands || !CanAffordOneMoreBet())
        {
            return false;
        }

        int firstRank = GetRank(ActiveHand.Cards[0]);
        int secondRank = GetRank(ActiveHand.Cards[1]);

        return GetCardValue(firstRank) == GetCardValue(secondRank);
    }

    private bool CanSurrender()
    {
        return allowSurrender && IsOnFirstTwoCards() && hands.Count == 1;
    }

    // ---------- Cards on the table ----------

    // Where a card of the player's goes. One hand: a normal row. Split hands: small fanned groups side by side.
    private Vector3 PlayerSlot(int handIndex, int cardIndex)
    {
        if (hands.Count <= 1)
        {
            return playerRowStart.position + Vector3.right * cardSpacing * cardIndex;
        }

        return playerRowStart.position
            + Vector3.right * (handSpacing * handIndex + splitCardOffset * cardIndex)
            + Vector3.up * 0.01f * cardIndex;   // Later cards lie slightly on top
    }

    private Vector3 DealerSlot(int cardIndex)
    {
        return dealerRowStart.position + Vector3.right * cardSpacing * cardIndex;
    }

    private TableCard SpawnCard()
    {
        TableCard cardObject = Instantiate(cardPrefab, deckSpot.position, Quaternion.identity);
        cardObject.SetSprite(CardArt.GetCardBack());
        return cardObject;
    }

    private IEnumerator DealToPlayerRoutine(PlayerHand hand)
    {
        int card = DrawCard();
        hand.Cards.Add(card);

        TableCard cardObject = SpawnCard();
        hand.CardObjects.Add(cardObject);

        yield return cardObject.MoveTo(PlayerSlot(hands.IndexOf(hand), hand.CardObjects.Count - 1));
        yield return cardObject.FlipTo(CardSprite(card));
        RefreshUI();
    }

    private IEnumerator DealToDealerRoutine(bool faceUp)
    {
        int card = DrawCard();
        dealerHand.Add(card);

        TableCard cardObject = SpawnCard();
        dealerCards.Add(cardObject);

        yield return cardObject.MoveTo(DealerSlot(dealerCards.Count - 1));

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
                StartCoroutine(Track(cardObjects[cardIndex].MoveTo(PlayerSlot(handIndex, cardIndex))));
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

    private IEnumerator ClearTableRoutine()
    {
        foreach (TableCard card in AllCardObjects())
        {
            StartCoroutine(Track(DiscardRoutine(card)));
        }

        dealerCards.Clear();

        foreach (PlayerHand hand in hands)
        {
            hand.CardObjects.Clear();
        }

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
        foreach (TableCard card in AllCardObjects())
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

        for (int i = 0; i < hands.Count; i++)
        {
            int change = SettleHand(hands[i], out string reason);
            string chipsPart = change > 0 ? $"+{change}" : change < 0 ? $"{change}" : "0";

            results.Add(hands.Count > 1 ? $"Hand {i + 1}: {reason} {chipsPart}" : $"{reason} {chipsPart} chips");

            if (change > 0)
            {
                chipsWon += change;
                winningHands++;
            }
            else if (change < 0)
            {
                chipsLost -= change;
                losingHands++;
            }
        }

        // Boss fight: each extra winning or losing hand adds a small flat bonus
        if (run.IsBossFight)
        {
            chipsWon += splitBonusPerHand * Mathf.Max(0, winningHands - 1);
            chipsLost += splitBonusPerHand * Mathf.Max(0, losingHands - 1);
        }

        string message = string.Join("\n", results);

        if (hands.Count > 1)
        {
            int total = chipsWon - chipsLost;
            message += total >= 0 ? $"\nTotal: +{total} chips" : $"\nTotal: {total} chips";
        }

        run.SetMinigameBusy(false);
        run.FinishMixedPlay(chipsWon, chipsLost, message);
    }

    // How many chips one hand wins (positive) or loses (negative) against the dealer
    private int SettleHand(PlayerHand hand, out string reason)
    {
        int playerValue = HandValue(hand.Cards);
        int dealerValue = HandValue(dealerHand);
        bool playerBlackjack = IsNaturalBlackjack(hand);
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

        // DEAL between hands; the other buttons during a hand
        dealButton.gameObject.SetActive(!isHandInProgress);
        hitButton.gameObject.SetActive(isHandInProgress);
        standButton.gameObject.SetActive(isHandInProgress);
        doubleButton.gameObject.SetActive(isHandInProgress);
        splitButton.gameObject.SetActive(isHandInProgress);
        surrenderButton.gameObject.SetActive(isHandInProgress && allowSurrender);

        dealButton.interactable = run.CanPlay && !isAnimating;
        hitButton.interactable = CanAct();
        standButton.interactable = CanAct();
        doubleButton.interactable = CanDoubleDown();
        splitButton.interactable = CanSplit();
        surrenderButton.interactable = CanSurrender();

        int bet = run.GetCurrentBet();
        dealButtonText.text = $"DEAL\nBet {bet}";
        doubleButtonText.text = $"DOUBLE\n+{bet}";
        splitButtonText.text = $"SPLIT\n+{bet}";
        surrenderButtonText.text = $"SURRENDER\n-{Mathf.CeilToInt(Mathf.Max(0, bet - run.GetUpgradeTotal(UpgradeType.LossReduction)) / 2f)}";

        UpdateActiveHandMarker();
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

    // The glowing marker sits under the hand being played, only when there are split hands
    private void UpdateActiveHandMarker()
    {
        if (activeHandMarker == null)
        {
            return;
        }

        bool show = isHandInProgress && !isDealerTurn && hands.Count > 1 && ActiveHand.CardObjects.Count > 0;
        activeHandMarker.gameObject.SetActive(show);

        if (!show)
        {
            return;
        }

        // Centered under the fan of cards of the active hand
        int cardCount = ActiveHand.CardObjects.Count;
        Vector3 center = PlayerSlot(activeIndex, 0) + Vector3.right * splitCardOffset * (cardCount - 1) / 2f;
        center.y = playerRowStart.position.y - 0.03f;
        activeHandMarker.position = center;
    }
}