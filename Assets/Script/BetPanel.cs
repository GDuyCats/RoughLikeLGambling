using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The bet panel of one table: +1, +5, +25, +100, CLEAR and ALL IN.
// Like every table button, each one stands in for a button on the screen (see TableButton),
// so this script only works with the screen buttons. Made by Tools > Rearrange Tables.
public class BetPanel : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private Button[] addButtons;              // +1, +5, +25, +100
    [SerializeField] private int[] addAmounts = { 1, 5, 25, 100 };
    [SerializeField] private Button clearButton;
    [SerializeField] private Button allInButton;
    [SerializeField] private TMP_Text betBoard;                // "BET 35 / MAX 100", printed on the felt above the panel

    private void Awake()
    {
        if (run == null)
        {
            run = FindFirstObjectByType<RunManager>();
        }

        for (int i = 0; i < addButtons.Length && i < addAmounts.Length; i++)
        {
            int amount = addAmounts[i];

            if (addButtons[i] != null)
            {
                addButtons[i].onClick.AddListener(() => run.AddToBet(amount));
            }
        }

        if (clearButton != null)
        {
            clearButton.onClick.AddListener(run.ClearBet);
        }

        if (allInButton != null)
        {
            allInButton.onClick.AddListener(run.GoAllIn);
        }
    }

    // Lock the buttons that can't do anything right now, and keep the board up to date
    private void Update()
    {
        if (run == null)
        {
            return;
        }

        bool canChange = run.CanChangeBet;
        int bet = run.GetCurrentBet();
        bool atTop = !run.IsAllIn && bet >= run.HighestBet;

        foreach (Button button in addButtons)
        {
            if (button != null)
            {
                button.interactable = canChange && (run.IsAllIn || !atTop);
            }
        }

        if (clearButton != null)
        {
            clearButton.interactable = canChange && (run.IsAllIn || bet > run.MinBet);
        }

        if (allInButton != null)
        {
            allInButton.interactable = canChange && !run.IsAllIn && run.Chips > 0;
        }

        if (betBoard != null)
        {
            string limit = run.IsAllIn ? "<color=#FF6B6B>ALL IN</color>" : $"MAX {run.TableLimit}";
            betBoard.text = $"BET {bet}\n<size=60%>{limit}</size>";
        }
    }
}
