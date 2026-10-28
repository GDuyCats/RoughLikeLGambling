using System.Collections;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

// The Lucky Dice minigame: two big dice are thrown across the table and roll freely.
// If a die lands tilted or keeps spinning, the dealer kicks the table.
// Guess if the total is under, exactly, or over 7.
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
    [SerializeField] private Button underButton;
    [SerializeField] private Button sevenButton;
    [SerializeField] private Button overButton;

    [Header("Table")]
    [SerializeField] private TableDie firstDie;
    [SerializeField] private TableDie secondDie;
    [SerializeField] private Transform throwSpot;        // The dice are thrown from here (the dealer's side)
    [SerializeField] private Transform aimSpot;          // The dice are thrown toward here
    [SerializeField] private Transform firstRestSpot;    // Where the dice wait before the first throw
    [SerializeField] private Transform secondRestSpot;
    [SerializeField] private GameObject rollCamera;      // Wide camera that shows the whole table while the dice roll
    [SerializeField] private CinemachineImpulseSource tableShake;

    [Header("Throw")]
    [SerializeField] private float throwSpeed = 8f;
    [SerializeField] private float throwLift = 2f;
    [SerializeField] private float throwSpread = 0.3f;
    [SerializeField] private float throwSpin = 3f;

    [Header("Dealer Kick")]
    [SerializeField] private float settleTimeout = 8f;                   // Still rolling after this: kick
    [SerializeField] private float settleHoldTime = 0.8f;                // Dice must stay still this long to count as stopped
    [SerializeField] private float kickDelay = 1f;                       // The dealer waits and looks again before kicking
    [SerializeField, Range(0.5f, 1f)] private float flatEnough = 0.9f;   // Tilted more than this: kick
    [SerializeField] private float kickStrength = 5f;
    [SerializeField] private int maxKicks = 3;

    [Header("Rules")]
    [SerializeField] private float payoutMultiplier = 1.2f;
    [SerializeField] private int baseWinBonus = 3;

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

        if (rollCamera != null)
        {
            rollCamera.SetActive(false);
        }
    }

    private void ResetGame()
    {
        StopAllCoroutines();
        isRolling = false;

        if (rollCamera != null)
        {
            rollCamera.SetActive(false);
        }

        firstDie.ShowValue(1, firstRestSpot.position);
        secondDie.ShowValue(1, secondRestSpot.position);

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

    private IEnumerator RollRoutine(GuessType guess)
    {
        isRolling = true;
        run.SetMinigameBusy(true);
        run.ShowMessage("Rolling...");

        if (rollCamera != null)
        {
            rollCamera.SetActive(true);
        }

        ThrowDie(firstDie, -0.6f);
        ThrowDie(secondDie, 0.6f);
        AudioManager.Play(SoundId.DiceThrow);

        yield return new WaitForSeconds(0.5f);
        bool stopped = false;
        yield return WaitForDiceToStop(result => stopped = result);

        // A die that won't stop, or lies tilted: the dealer kicks the table
        for (int i = 0; i < maxKicks; i++)
        {
            if (stopped && !IsTilted(firstDie) && !IsTilted(secondDie))
            {
                break;
            }

            // Give the dice one last chance to fall flat by themselves
            yield return new WaitForSeconds(kickDelay);
            yield return WaitForDiceToStop(result => stopped = result);

            if (stopped && !IsTilted(firstDie) && !IsTilted(secondDie))
            {
                break;
            }

            yield return DealerKick();
            yield return WaitForDiceToStop(result => stopped = result);
        }

        int first = firstDie.ReadTopValue(out _);
        int second = secondDie.ReadTopValue(out _);
        int total = first + second;

        int chipChange;
        string message;

        if (IsWinningTotal(guess, total))
        {
            chipChange = GetWinAmount(guess);
            message = $"Rolled {first} + {second} = {total}! +{chipChange} chips";
        }
        else
        {
            chipChange = -GetLossAmount();
            message = $"Rolled {first} + {second} = {total}! {chipChange} chips";
        }

        // Let the player see the result on the wide view for a moment
        yield return new WaitForSeconds(0.6f);

        if (rollCamera != null)
        {
            rollCamera.SetActive(false);
        }

        isRolling = false;
        run.SetMinigameBusy(false);
        run.FinishPlay(chipChange, message);
    }

    // Throw a die from the dealer's side toward the aim spot, with a little random aim and spin
    private void ThrowDie(TableDie die, float sideOffset)
    {
        Vector3 from = throwSpot.position + throwSpot.right * sideOffset;

        Vector3 direction = aimSpot.position - from;
        direction.y = 0f;
        direction.Normalize();

        Vector3 sideways = Vector3.Cross(Vector3.up, direction) * Random.Range(-throwSpread, throwSpread);
        Vector3 push = (direction + sideways).normalized * throwSpeed + Vector3.up * throwLift;
        Vector3 spin = Random.onUnitSphere * throwSpin;

        die.Throw(from, push, spin);
    }

    // The whole table jolts: screen shake, and both dice jump and tumble again
    private IEnumerator DealerKick()
    {
        run.ShowMessage("The dealer kicks the table!");
        AudioManager.Play(SoundId.DealerKick);

        if (tableShake != null)
        {
            tableShake.GenerateImpulseWithForce(2f);
        }

        firstDie.Kick(kickStrength);
        secondDie.Kick(kickStrength);

        yield return new WaitForSeconds(0.5f);
    }

    // Waits until both dice stay still for a moment, or the timeout runs out.
    // Reports back whether they really stopped (true) or timed out (false).
    private IEnumerator WaitForDiceToStop(System.Action<bool> report)
    {
        float giveUpTime = Time.time + settleTimeout;
        float stillSince = -1f;

        while (Time.time < giveUpTime)
        {
            // A die fell off the table: put it back and start waiting again
            bool rescuedFirst = RescueLostDie(firstDie);
            bool rescuedSecond = RescueLostDie(secondDie);

            if (rescuedFirst || rescuedSecond)
            {
                stillSince = -1f;
                giveUpTime = Time.time + settleTimeout;
            }

            if (firstDie.IsSettled && secondDie.IsSettled)
            {
                if (stillSince < 0f)
                {
                    stillSince = Time.time;
                }

                if (Time.time - stillSince > settleHoldTime)
                {
                    report(true);
                    yield break;
                }
            }
            else
            {
                stillSince = -1f;
            }

            yield return null;
        }

        report(false);
    }

    // A die that flew off the table is dropped back onto the middle of it
    private bool RescueLostDie(TableDie die)
    {
        // Still on (or above) the table: nothing to do
        if (die.transform.position.y > aimSpot.position.y - 2f)
        {
            return false;
        }

        die.Throw(aimSpot.position + Vector3.up * 3f, Vector3.zero, Random.onUnitSphere * throwSpin);
        run.ShowMessage("The dealer puts a stray die back on the table!");
        return true;
    }

    private bool IsTilted(TableDie die)
    {
        die.ReadTopValue(out float flatness);
        return flatness < flatEnough;
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