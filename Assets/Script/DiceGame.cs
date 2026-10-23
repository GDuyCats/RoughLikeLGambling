using System.Collections;
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
    [SerializeField] private Image firstDieImage;
    [SerializeField] private Image secondDieImage;
    [SerializeField] private Button underButton;
    [SerializeField] private Button sevenButton;
    [SerializeField] private Button overButton;

    [Header("Rules")]
    [SerializeField] private float payoutMultiplier = 1.2f;
    [SerializeField] private int baseWinBonus = 3;

    [Header("Animation")]
    [SerializeField] private float rollTime = 0.7f;              // How long the dice tumble
    [SerializeField] private float faceChangeInterval = 0.06f;   // How often the faces change while tumbling
    [SerializeField] private float shakeAngle = 20f;             // How much the dice wobble while tumbling

    private TMP_Text underButtonText;
    private TMP_Text sevenButtonText;
    private TMP_Text overButtonText;

    private bool isRolling;

    private void Awake()
    {
        underButtonText = underButton.GetComponentInChildren<TMP_Text>();
        sevenButtonText = sevenButton.GetComponentInChildren<TMP_Text>();
        overButtonText = overButton.GetComponentInChildren<TMP_Text>();

        underButton.onClick.AddListener(() => Guess(GuessType.Under));
        sevenButton.onClick.AddListener(() => Guess(GuessType.Seven));
        overButton.onClick.AddListener(() => Guess(GuessType.Over));
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
        isRolling = false;
        firstDieImage.rectTransform.localRotation = Quaternion.identity;
        secondDieImage.rectTransform.localRotation = Quaternion.identity;

        ShowDice(1, 1, false);
        run.ShowMessage($"Under {TargetTotal}, exactly {TargetTotal}, or over {TargetTotal}?");
    }

    private void Guess(GuessType guess)
    {
        if (!run.CanPlay || isRolling)
        {
            return;
        }

        StartCoroutine(RollRoutine(guess));
    }

    // Decide the result first, let the dice tumble, then land on the real result
    private IEnumerator RollRoutine(GuessType guess)
    {
        isRolling = true;
        run.SetMinigameBusy(true);

        int firstDie = RollDie();
        int secondDie = RollDie();
        int total = firstDie + secondDie;

        float endTime = Time.time + rollTime;

        while (Time.time < endTime)
        {
            ShowDice(RollDie(), RollDie(), true);
            firstDieImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-shakeAngle, shakeAngle));
            secondDieImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-shakeAngle, shakeAngle));

            yield return new WaitForSeconds(faceChangeInterval);
        }

        firstDieImage.rectTransform.localRotation = Quaternion.identity;
        secondDieImage.rectTransform.localRotation = Quaternion.identity;
        ShowDice(firstDie, secondDie, true);

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

        isRolling = false;
        run.SetMinigameBusy(false);
        run.FinishPlay(chipChange, message);
    }

    private int RollDie()
    {
        return Random.Range(1, DiceSides + 1);
    }

    private void ShowDice(int first, int second, bool rolled)
    {
        Color color = rolled ? Color.white : new Color(1f, 1f, 1f, 0.35f);

        firstDieImage.sprite = CardArt.GetDie(first);
        secondDieImage.sprite = CardArt.GetDie(second);
        firstDieImage.color = color;
        secondDieImage.color = color;
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
        // While the dice tumble, the buttons stay locked
        if (isRolling)
        {
            underButton.interactable = false;
            sevenButton.interactable = false;
            overButton.interactable = false;
            return;
        }

        underButtonText.text = $"UNDER {TargetTotal}\n+{GetWinAmount(GuessType.Under)}";
        sevenButtonText.text = $"SEVEN\n+{GetWinAmount(GuessType.Seven)}";
        overButtonText.text = $"OVER {TargetTotal}\n+{GetWinAmount(GuessType.Over)}";

        underButton.interactable = run.CanPlay;
        sevenButton.interactable = run.CanPlay;
        overButton.interactable = run.CanPlay;
    }
}