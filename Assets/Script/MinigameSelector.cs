using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Lets the player choose a minigame each round, and switch games during a round
public class MinigameSelector : MonoBehaviour
{
    [System.Serializable]
    public class MinigameEntry
    {
        public string displayName;
        [TextArea] public string description;
        public GameObject panel;
    }

    [SerializeField] private RunManager run;
    [SerializeField] private TMP_Text gameTitleText;
    [SerializeField] private GameObject choicePanel;
    [SerializeField] private Button[] choiceButtons;
    [SerializeField] private Button backButton;
    [SerializeField] private Button switchGameButton;
    [SerializeField] private List<MinigameEntry> minigames;
    [SerializeField] private int choicesOffered = 3;            // Set to the total number of minigames for a free choice
    [SerializeField] private int switchCostAfterPlaying = 1;    // Plays lost when switching after playing this round

    private readonly List<MinigameEntry> offeredThisRound = new List<MinigameEntry>();
    private MinigameEntry currentEntry;
    private TMP_Text switchButtonText;

    private void Awake()
    {
        switchButtonText = switchGameButton.GetComponentInChildren<TMP_Text>();

        switchGameButton.onClick.AddListener(OpenSwitchMenu);
        backButton.onClick.AddListener(CloseSwitchMenu);
    }

    private void OnEnable()
    {
        run.RoundStarted += StartRoundChoice;
        run.StateChanged += RefreshSwitchButton;
    }

    private void OnDisable()
    {
        run.RoundStarted -= StartRoundChoice;
        run.StateChanged -= RefreshSwitchButton;
    }

    // A new round: hide every minigame and make the player pick one
    private void StartRoundChoice()
    {
        foreach (MinigameEntry entry in minigames)
        {
            entry.panel.SetActive(false);
        }

        currentEntry = null;

        // Choices are fixed for the whole round, so reopening the menu can't reroll them
        offeredThisRound.Clear();
        offeredThisRound.AddRange(PickRandomEntries(choicesOffered));

        gameTitleText.text = "Choose a game";
        run.ShowMessage("Pick the game for this round.");
        ShowChoices(false);
    }

    // Opened from the SWITCH GAME button during a round
    private void OpenSwitchMenu()
    {
        if (!CanSwitch())
        {
            return;
        }

        int cost = GetSwitchCost();
        run.ShowMessage(cost > 0
            ? $"Switching costs {cost} play. Pick a game or go back."
            : "Switching is free. Pick a game or go back.");

        ShowChoices(true);
    }

    // BACK button: return to the current game without paying anything
    private void CloseSwitchMenu()
    {
        choicePanel.SetActive(false);
        run.ShowMessage($"Back to {currentEntry.displayName}.");
    }

    private void ShowChoices(bool canGoBack)
    {
        for (int i = 0; i < choiceButtons.Length; i++)
        {
            Button button = choiceButtons[i];

            if (i >= offeredThisRound.Count)
            {
                button.gameObject.SetActive(false);
                continue;
            }

            MinigameEntry choice = offeredThisRound[i];
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<TMP_Text>().text = $"<b>{choice.displayName}</b>\n\n{choice.description}";

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => ChooseMinigame(choice));
        }

        backButton.gameObject.SetActive(canGoBack);
        choicePanel.SetActive(true);
    }

    private void ChooseMinigame(MinigameEntry entry)
    {
        choicePanel.SetActive(false);

        // Picking the game you're already playing changes nothing and costs nothing
        if (entry == currentEntry)
        {
            run.ShowMessage($"Staying on {entry.displayName}.");
            return;
        }

        bool isSwitching = currentEntry != null;
        int cost = isSwitching ? GetSwitchCost() : 0;

        if (isSwitching)
        {
            currentEntry.panel.SetActive(false);
        }

        currentEntry = entry;
        entry.panel.SetActive(true);
        gameTitleText.text = entry.displayName;

        if (cost > 0)
        {
            run.SpendPlays(cost);
        }

        RefreshSwitchButton();
    }

    // Free before the first play of the round, costs plays afterwards
    private int GetSwitchCost()
    {
        return run.HasPlayedThisRound ? switchCostAfterPlaying : 0;
    }

    // Must be playing a game, and keep at least one play after paying
    private bool CanSwitch()
    {
        int cost = GetSwitchCost();
        return currentEntry != null && run.CanPlay && (cost == 0 || run.PlaysLeft > cost);
    }

    private void RefreshSwitchButton()
    {
        int cost = GetSwitchCost();
        switchGameButton.interactable = CanSwitch();
        switchButtonText.text = cost > 0 ? $"SWITCH GAME\n-{cost} play" : "SWITCH GAME";
    }

    private List<MinigameEntry> PickRandomEntries(int count)
    {
        List<MinigameEntry> candidates = new List<MinigameEntry>(minigames);
        List<MinigameEntry> result = new List<MinigameEntry>();

        while (result.Count < count && candidates.Count > 0)
        {
            int index = Random.Range(0, candidates.Count);
            result.Add(candidates[index]);
            candidates.RemoveAt(index);
        }

        return result;
    }
}