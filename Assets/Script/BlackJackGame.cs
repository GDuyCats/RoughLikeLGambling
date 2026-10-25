using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Blackjack minigame: get closer to 21 than the dealer without going over
public class BlackjackGame : MonoBehaviour
{
    private const int BlackjackValue = 21;
    private const int DealerStandValue = 17;
    private const int DeckSize = 52;
    private const int RanksPerSuit = 13;

    // If these show up as empty squares, replace them with "S", "H", "D", "C"
    private static readonly string[] SuitSymbols = { "♠", "♥", "♦", "♣" };

    [Header("Run")]
    [SerializeField] private RunManager run;

    [Header("UI")]
    [SerializeField] private TMP_Text dealerText;
    [SerializeField] private TMP_Text playerText;
    [SerializeField] private RectTransform dealerHandArea;   // Card pictures line up here
    [SerializeField] private RectTransform playerHandArea;
    [SerializeField] private Image cardPrefab;

    [Header("Animation")]
    [SerializeField] private float popTime = 0.15f;    // How long a new card takes to pop in
    [SerializeField] private float flipTime = 0.25f;   // How long the dealer's hidden card takes to turn over
    [SerializeField] private float dealerDrawDelay = 0.6f;
    private int dealerCardsShown;   // How many dealer cards are already on screen
    private int playerCardsShown;
    private bool dealerCardHidden;  // Was the dealer's second card face down last time we drew the table?               // The template card picture
    [SerializeField] private Button dealButton;
    [SerializeField] private Button hitButton;
    [SerializeField] private Button standButton;
    [SerializeField] private Button doubleButton;
    [SerializeField] private Button surrenderButton;

    [Header("Rules")]
    [SerializeField] private float winMultiplier = 1.5f;
    [SerializeField] private float blackjackMultiplier = 2.5f;
    [SerializeField] private int baseWinBonus = 3;

    private readonly List<int> deck = new List<int>();
    private readonly List<int> playerHand = new List<int>();
    private readonly List<int> dealerHand = new List<int>();

    private TMP_Text dealButtonText;
    private TMP_Text doubleButtonText;
    private TMP_Text surrenderButtonText;

    private bool isHandInProgress;
    private bool isDoubled;
    private bool isDealerTurn;   // The player is done, the dealer is drawing
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
        playerHand.Clear();
        dealerHand.Clear();
        isHandInProgress = false;
        isDoubled = false;
        run.SetMinigameBusy(false);
        run.ShowMessage("Press DEAL to start a hand.");
        isDealerTurn = false;
    }

    private void Deal()
    {
        if (!run.CanPlay || isHandInProgress)
        {
            return;
        }

        BuildDeck();
        dealerCardsShown = 0;
        playerCardsShown = 0;
        playerHand.Clear();
        dealerHand.Clear();
        isDoubled = false;

        playerHand.Add(DrawCard());
        dealerHand.Add(DrawCard());
        playerHand.Add(DrawCard());
        dealerHand.Add(DrawCard());

        isHandInProgress = true;

        if (IsBlackjack(playerHand) || IsBlackjack(dealerHand))
        {
            FinishHand();
            return;
        }

        run.ShowMessage("Hit, stand, double or surrender?");
        run.SetMinigameBusy(true);
    }

    private void Hit()
    {
        if (!isHandInProgress || isDealerTurn)
        {
            return;
        }

        playerHand.Add(DrawCard());
        int value = HandValue(playerHand);

        if (value > BlackjackValue)
        {
            FinishHand();
        }
        else if (value == BlackjackValue)
        {
            Stand();
        }
        else
        {
            run.ShowMessage("Hit or stand?");
            RefreshUI();
        }
    }

    private void Stand()
    {
        if (!isHandInProgress || isDealerTurn)
        {
            return;
        }

        StartCoroutine(DealerTurnRoutine());
    }

    // The dealer flips the hidden card, then draws one card at a time
    private IEnumerator DealerTurnRoutine()
    {
        isDealerTurn = true;
        run.ShowMessage("Dealer's turn...");
        RefreshUI();   // Turns the hidden card over

        yield return new WaitForSeconds(flipTime + dealerDrawDelay);

        while (HandValue(dealerHand) < DealerStandValue)
        {
            dealerHand.Add(DrawCard());
            RefreshUI();   // The new card pops in
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

        isDoubled = true;
        playerHand.Add(DrawCard());

        if (HandValue(playerHand) > BlackjackValue)
        {
            FinishHand();
        }
        else
        {
            Stand();
        }
    }

    // Give up the hand right away and lose only half the bet
    private void Surrender()
    {
        if (!CanMakeFirstDecision())
        {
            return;
        }

        int lossAmount = GetSurrenderLoss();
        isHandInProgress = false;

        run.SetMinigameBusy(false);
        run.FinishPlay(-lossAmount, $"Surrendered. -{lossAmount} chips");
    }

    // Double and surrender are only allowed on the first two cards
    private bool CanMakeFirstDecision()
    {
        return isHandInProgress && !isDealerTurn && playerHand.Count == 2 && run.CanPlay;
    }

    private bool CanDoubleDown()
    {
        return CanMakeFirstDecision() && run.Chips >= run.GetCurrentBet() * 2;
    }

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

    // The bet for this hand: doubled after a double down
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

    // Half of the normal loss, rounded up
    private int GetSurrenderLoss()
    {
        return Mathf.CeilToInt(GetLossAmount() / 2f);
    }

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

    private string CardName(int card)
    {
        int rank = GetRank(card);
        int suit = GetSuit(card);

        string rankName = rank switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => rank.ToString()
        };

        string text = rankName + SuitSymbols[suit];

        bool isRed = suit == 1 || suit == 2;
        return isRed ? $"<color=#D9363E>{text}</color>" : text;
    }

    private string HandText(List<int> hand, bool hideSecondCard)
    {
        List<string> names = new List<string>();

        for (int i = 0; i < hand.Count; i++)
        {
            names.Add(hideSecondCard && i == 1 ? "??" : CardName(hand[i]));
        }

        return string.Join("   ", names);
    }
    // Reuses card pictures already in the area, adds new ones when needed, hides the extras.
    // Cards that weren't on screen before pop in.
    private void ShowHand(RectTransform area, List<int> hand, bool hideSecondCard, ref int cardsShown)
    {
        for (int i = 0; i < hand.Count; i++)
        {
            Image cardImage = i < area.childCount
                ? area.GetChild(i).GetComponent<Image>()
                : Instantiate(cardPrefab, area);

            bool faceDown = hideSecondCard && i == 1;

            cardImage.gameObject.SetActive(true);
            cardImage.sprite = faceDown
                ? CardArt.GetCardBack()
                : CardArt.GetCard(GetRank(hand[i]), GetSuit(hand[i]));

            if (i >= cardsShown)
            {
                StartCoroutine(PopIn(cardImage.rectTransform));
            }
        }

        for (int i = hand.Count; i < area.childCount; i++)
        {
            area.GetChild(i).gameObject.SetActive(false);
        }

        cardsShown = hand.Count;
    }

    // A new card grows from small to full size with a tiny bounce
    private IEnumerator PopIn(RectTransform card)
    {
        for (float t = 0f; t < popTime; t += Time.deltaTime)
        {
            float progress = t / popTime;
            float scale = Mathf.Lerp(0.3f, 1.1f, progress);
            card.localScale = Vector3.one * scale;
            yield return null;
        }

        card.localScale = Vector3.one;
    }

    // Squash the card, swap the picture, stretch it back: looks like turning it over
    private IEnumerator FlipCard(Image card, Sprite faceUp)
    {
        RectTransform rect = card.rectTransform;
        float halfTime = flipTime / 2f;

        for (float t = 0f; t < halfTime; t += Time.deltaTime)
        {
            rect.localScale = new Vector3(1f - t / halfTime, 1f, 1f);
            yield return null;
        }

        card.sprite = faceUp;

        for (float t = 0f; t < halfTime; t += Time.deltaTime)
        {
            rect.localScale = new Vector3(t / halfTime, 1f, 1f);
            yield return null;
        }

        rect.localScale = Vector3.one;
    }
    private void RefreshUI()
    {
        // The dealer's second card stays face down until the dealer's turn (or the end of the hand)
        bool hideDealerCard = isHandInProgress && !isDealerTurn;
        bool revealDealerCard = dealerCardHidden && !hideDealerCard && dealerHand.Count >= 2;

        ShowHand(dealerHandArea, dealerHand, hideDealerCard || revealDealerCard, ref dealerCardsShown);
        ShowHand(playerHandArea, playerHand, false, ref playerCardsShown);

        if (revealDealerCard)
        {
            Image hiddenCard = dealerHandArea.GetChild(1).GetComponent<Image>();
            StartCoroutine(FlipCard(hiddenCard, CardArt.GetCard(GetRank(dealerHand[1]), GetSuit(dealerHand[1]))));
        }

        dealerCardHidden = hideDealerCard && dealerHand.Count >= 2;

        // The labels now only show the totals
        if (dealerHand.Count == 0)
        {
            dealerText.text = "Dealer";
        }
        else if (hideDealerCard)
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

        dealButton.interactable = run.CanPlay;
        hitButton.interactable = run.CanPlay && !isDealerTurn;
        standButton.interactable = run.CanPlay && !isDealerTurn;
        doubleButton.interactable = CanDoubleDown();
        surrenderButton.interactable = CanMakeFirstDecision();

        int bet = run.GetCurrentBet();
        dealButtonText.text = $"DEAL\nBet {bet}";
        doubleButtonText.text = $"DOUBLE\nBet {bet * 2}";
        surrenderButtonText.text = $"SURRENDER\n-{Mathf.CeilToInt(Mathf.Max(0, bet - run.GetUpgradeTotal(UpgradeType.LossReduction)) / 2f)}";
    }
}