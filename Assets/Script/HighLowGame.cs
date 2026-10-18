using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HighLowGame : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text cardText;
    [SerializeField] private TMP_Text chipsText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private Button higherButton;
    [SerializeField] private Button lowerButton;

    [Header("Rules")]
    [SerializeField] private int startingChips = 100;
    [SerializeField] private int betAmount = 10;
    [SerializeField] private int guessesPerRound = 10;
    [SerializeField] private int baseRoundGoal = 40;         // Chips to earn in round 1
    [SerializeField] private int goalIncreasePerRound = 10;  // Extra chips required each new round

    private int chips;
    private int currentCard;   // 1 = A, 11 = J, 12 = Q, 13 = K
    private int roundNumber;
    private int targetChips;   // Chip total needed to clear the current round
    private int guessesLeft;
    private bool isGameOver;

    private void Start()
    {
        chips = startingChips;
        currentCard = DrawCard();

        higherButton.onClick.AddListener(() => Guess(true));
        lowerButton.onClick.AddListener(() => Guess(false));

        StartRound(1);
        resultText.text = "Higher or lower?";
        UpdateUI();
    }

    // Set up a new round: reset guesses and calculate the new goal
    private void StartRound(int number)
    {
        roundNumber = number;
        guessesLeft = guessesPerRound;

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
            resultText.text = "Tie! No chips lost.";
        }
        else
        {
            bool isHigher = nextCard > currentCard;

            if (isHigher == guessHigher)
            {
                chips += betAmount;
                resultText.text = $"Correct! +{betAmount} chips";
            }
            else
            {
                chips -= betAmount;
                resultText.text = $"Wrong! -{betAmount} chips";
            }
        }

        currentCard = nextCard;

        if (chips >= targetChips)
        {
            resultText.text = $"Round {roundNumber} cleared!";
            StartRound(roundNumber + 1);
        }
        else if (chips <= 0 || guessesLeft <= 0)
        {
            GameOver();
        }

        UpdateUI();
    }

    private void GameOver()
    {
        isGameOver = true;
        resultText.text = $"Game over! You reached round {roundNumber}.";
        higherButton.interactable = false;
        lowerButton.interactable = false;
    }

    private int DrawCard()
    {
        return Random.Range(1, 14);
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

    private void UpdateUI()
    {
        cardText.text = CardName(currentCard);
        chipsText.text = $"Chips: {chips}";
        roundText.text = $"Round {roundNumber}  |  Goal: {targetChips}  |  Guesses: {guessesLeft}";
    }
}