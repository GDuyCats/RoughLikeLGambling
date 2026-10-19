using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Lets the player choose which minigame to play each round
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
    [SerializeField] private List<MinigameEntry> minigames;
    [SerializeField] private int choicesOffered = 3;   // Set to the total number of minigames for a free choice

    private void OnEnable()
    {
        run.RoundStarted += ShowChoices;
    }

    private void OnDisable()
    {
        run.RoundStarted -= ShowChoices;
    }

    // Hide every minigame and let the player pick the next one
    private void ShowChoices()
    {
        foreach (MinigameEntry entry in minigames)
        {
            entry.panel.SetActive(false);
        }

        gameTitleText.text = "Choose a game";
        run.ShowMessage("Pick the game for this round.");

        List<MinigameEntry> offered = PickRandomEntries(choicesOffered);

        for (int i = 0; i < choiceButtons.Length; i++)
        {
            Button button = choiceButtons[i];

            if (i >= offered.Count)
            {
                button.gameObject.SetActive(false);
                continue;
            }

            MinigameEntry choice = offered[i];
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<TMP_Text>().text = $"<b>{choice.displayName}</b>\n\n{choice.description}";

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => ChooseMinigame(choice));
        }

        choicePanel.SetActive(true);
    }

    private void ChooseMinigame(MinigameEntry entry)
    {
        choicePanel.SetActive(false);
        entry.panel.SetActive(true);
        gameTitleText.text = entry.displayName;
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