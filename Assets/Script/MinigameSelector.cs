using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Picks which minigame is played each round
public class MinigameSelector : MonoBehaviour
{
    // One entry per minigame. [Serializable] lets Unity show it in the Inspector.
    [System.Serializable]
    public class MinigameEntry
    {
        public string displayName;
        public GameObject panel;
    }

    [SerializeField] private RunManager run;
    [SerializeField] private TMP_Text gameTitleText;
    [SerializeField] private List<MinigameEntry> minigames;
    [SerializeField] private bool avoidRepeats = true;   // Don't play the same minigame two rounds in a row

    private int lastIndex = -1;

    private void OnEnable()
    {
        run.RoundStarted += SelectMinigame;
    }

    private void OnDisable()
    {
        run.RoundStarted -= SelectMinigame;
    }

    private void SelectMinigame()
    {
        if (minigames.Count == 0)
        {
            return;
        }

        int index = PickIndex();
        lastIndex = index;

        // Switch every minigame off first, so the chosen one always restarts fresh
        foreach (MinigameEntry entry in minigames)
        {
            entry.panel.SetActive(false);
        }

        minigames[index].panel.SetActive(true);
        gameTitleText.text = minigames[index].displayName;
    }

    private int PickIndex()
    {
        if (!avoidRepeats || minigames.Count < 2 || lastIndex < 0)
        {
            return Random.Range(0, minigames.Count);
        }

        // Pick from every minigame except the last one played
        int index = Random.Range(0, minigames.Count - 1);

        if (index >= lastIndex)
        {
            index++;
        }

        return index;
    }
}