using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Lucky Dice minigame: roll two dice and guess if the total is under, exactly, or over 7
public class DiceGame : MonoBehaviour
{
    private const int DiceSides = 6;
    private const int TargetTotal = 7;

    private enum GuessType
    {
        Under,
        Seven,
        Over
    }

    [Header("Run")]
    [SerializeField] private RunManager run;

    [Header("UI")]
    [SerializeField] private TMP_Text diceText;
    [SerializeField] private Button underButton;
    [SerializeField] private Button sevenButton;
    [SerializeField] private Button overButton;

    [Header("Rules")]
    [SerializeField] private float payoutMultiplier = 1.2f;
    [SerializeField] private int baseWinBonus = 3;

    private TMP_Text underButtonText;
    private TMP_Text sevenButtonText;
    private TMP_Text overButtonText;

    private void Awake()
    {
        underButtonText = underButton.GetComponentInChildren<TMP_Text>();
        sevenButtonText = sevenButton.GetComponentInChildren<TMP_Text>();
        overButtonText = overButton.GetComponentInChildren<TMP_Text>();

        underButton.onClick.AddListener(() => Guess(GuessType.Under));
        sevenButton.onClick.AddListener(() => Guess(GuessType.Seven));
        overButton.onClick.AddListener(() => Guess(GuessType.Over));
    }

    // Runs every time this minigame is switched on: listen for updates and start fresh
    private void OnEnable()
    {
        run.StateChanged += RefreshUI;
        ResetGame();
        RefreshUI();
    }

    // Stop listening when this minigame is switched off
    private void OnDisable()
    {
        run.StateChanged -= RefreshUI;
    }

    private void ResetGame()
    {
        diceText.text = "?   ?";
        run.ShowMessage($"Under {TargetTotal}, exactly {TargetTotal}, or over {TargetTotal}?");
    }

    private void Guess(GuessType guess)
    {
        if (!run.CanPlay)
        {
            return;
        }

        int firstDie = RollDie();
        int secondDie = RollDie();
        int total = firstDie + secondDie;

        diceText.text = $"{firstDie}   {secondDie}";

        int chipChange;
        string message;

        if (IsWinningTotal(guess, total))
        {
            chipChange = GetWinAmount(guess);
            message = $"Rolled {total}! +{chipChange} chips";
        }
        else
        {
            chipChange = -GetLossAmount();
            message = $"Rolled {total}! {chipChange} chips";
        }

        run.FinishPlay(chipChange, message);
    }

    private int RollDie()
    {
        return Random.Range(1, DiceSides + 1);
    }

    private bool IsWinningTotal(GuessType guess, int total)
    {
        return guess switch
        {
            GuessType.Under => total < TargetTotal,
            GuessType.Seven => total == TargetTotal,
            _ => total > TargetTotal
        };
    }

    // Check all 36 possible dice combinations and count how many win this guess
    private int CountWinningCombos(GuessType guess)
    {
        int count = 0;

        for (int first = 1; first <= DiceSides; first++)
        {
            for (int second = 1; second <= DiceSides; second++)
            {
                if (IsWinningTotal(guess, first + second))
                {
                    count++;
                }
            }
        }

        return count;
    }

    // Same payout idea as High-Low: riskier guesses pay more
    private int GetWinAmount(GuessType guess)
    {
        int winningCombos = CountWinningCombos(guess);

        if (winningCombos == 0)
        {
            return 0;
        }

        int losingCombos = DiceSides * DiceSides - winningCombos;
        float payout = run.GetCurrentBet() * payoutMultiplier * losingCombos / winningCombos;
        int baseWin = Mathf.Max(1, Mathf.RoundToInt(payout) + baseWinBonus);

        int percentBonus = run.GetUpgradeTotal(UpgradeType.WinPayoutPercent);
        int boostedWin = Mathf.RoundToInt(baseWin * (100 + percentBonus) / 100f);

        return boostedWin + run.GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
    }

    private int GetLossAmount()
    {
        return Mathf.Max(0, run.GetCurrentBet() - run.GetUpgradeTotal(UpgradeType.LossReduction));
    }

    private void RefreshUI()
    {
        underButtonText.text = $"UNDER {TargetTotal}\n+{GetWinAmount(GuessType.Under)}";
        sevenButtonText.text = $"SEVEN\n+{GetWinAmount(GuessType.Seven)}";
        overButtonText.text = $"OVER {TargetTotal}\n+{GetWinAmount(GuessType.Over)}";

        underButton.interactable = run.CanPlay;
        sevenButton.interactable = run.CanPlay;
        overButton.interactable = run.CanPlay;
    }
}