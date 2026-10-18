using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HighLowGame : MonoBehaviour
{
    // These fields appear in the Inspector so you can drag objects into them
    [Header("UI")]
    [SerializeField] private TMP_Text cardText;
    [SerializeField] private TMP_Text chipsText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button higherButton;
    [SerializeField] private Button lowerButton;

    // Game rules, adjustable in the Inspector without editing code
    [Header("Rules")]
    [SerializeField] private int startingChips = 100;
    [SerializeField] private int betAmount = 10;

    private int chips;        // Current chip count
    private int currentCard;  // Card on screen (1 = A, 11 = J, 12 = Q, 13 = K)

    // Start runs once when the game begins
    private void Start()
    {
        chips = startingChips;
        currentCard = DrawCard();

        // Connect the buttons to the Guess method
        higherButton.onClick.AddListener(() => Guess(true));
        lowerButton.onClick.AddListener(() => Guess(false));

        resultText.text = "Higher or lower?";
        UpdateUI();
    }

    // Called when the player clicks a button. guessHigher = true means "HIGHER"
    private void Guess(bool guessHigher)
    {
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
        UpdateUI();

        if (chips <= 0)
        {
            resultText.text = "Out of chips! Game over.";
            higherButton.interactable = false;
            lowerButton.interactable = false;
        }
    }

    // Draw a random card from 1 to 13
    private int DrawCard()
    {
        return Random.Range(1, 14);
    }

    // Convert a card value to its display name
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

    // Refresh the text on screen
    private void UpdateUI()
    {
        cardText.text = CardName(currentCard);
        chipsText.text = $"Chips: {chips}";
    }
}