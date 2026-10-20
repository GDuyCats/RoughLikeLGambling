using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Lets the player walk up to a table, sit down to play, and leave again
public class TableManager : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private PlayerController player;
    [SerializeField] private List<TableStation> tables;
    [SerializeField] private TMP_Text gameTitleText;
    [SerializeField] private Button interactButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private float interactRange = 1.5f;        // How close the player must be to a table
    [SerializeField] private int switchCostAfterPlaying = 1;    // Plays lost when sitting down after playing this round

    private TableStation currentTable;   // The table the player is sitting at (null when walking)
    private TableStation nearbyTable;    // The closest table in range (null if none)
    private TMP_Text interactButtonText;

    private void Awake()
    {
        interactButtonText = interactButton.GetComponentInChildren<TMP_Text>();

        interactButton.onClick.AddListener(TrySitAtNearbyTable);
        leaveButton.onClick.AddListener(LeaveTable);

        foreach (TableStation table in tables)
        {
            table.SetOpen(false);
        }
    }

    private void OnEnable()
    {
        run.RunStarted += HandleRunStarted;
        run.RoundStarted += HandleRoundStarted;
        run.StateChanged += RefreshButtons;
    }

    private void OnDisable()
    {
        run.RunStarted -= HandleRunStarted;
        run.RoundStarted -= HandleRoundStarted;
        run.StateChanged -= RefreshButtons;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (currentTable != null)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                LeaveTable();
            }

            return;
        }

        nearbyTable = FindNearbyTable();
        RefreshButtons();

        if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            TrySitAtNearbyTable();
        }
    }

    private TableStation FindNearbyTable()
    {
        TableStation closest = null;
        float closestDistance = interactRange;

        foreach (TableStation table in tables)
        {
            float distance = table.DistanceTo(player.transform.position);

            if (distance <= closestDistance)
            {
                closest = table;
                closestDistance = distance;
            }
        }

        return closest;
    }

    // Free before the first play of the round, costs plays afterwards
    private int GetSitCost()
    {
        return run.HasPlayedThisRound ? switchCostAfterPlaying : 0;
    }

    private bool CanSit()
    {
        int cost = GetSitCost();
        return nearbyTable != null && run.CanPlay && (cost == 0 || run.PlaysLeft > cost);
    }

    // Can't walk away in the middle of a hand, while choosing an upgrade, or after game over
    private bool CanLeave()
    {
        return currentTable != null && run.CanPlay && !run.IsMinigameBusy;
    }

    private void TrySitAtNearbyTable()
    {
        if (!CanSit())
        {
            return;
        }

        int cost = GetSitCost();

        currentTable = nearbyTable;
        nearbyTable = null;
        player.CanMove = false;

        currentTable.SetOpen(true);
        gameTitleText.text = currentTable.DisplayName;

        if (cost > 0)
        {
            run.SpendPlays(cost);
        }

        RefreshButtons();
    }

    private void LeaveTable()
    {
        if (!CanLeave())
        {
            return;
        }

        StandUp();
        run.ShowMessage(BuildWalkMessage());
    }

    private void StandUp()
    {
        if (currentTable != null)
        {
            currentTable.SetOpen(false);
            currentTable = null;
        }

        player.CanMove = true;
        gameTitleText.text = "Casino";
        RefreshButtons();
    }

    // A new run (including after RESTART): stand up and choose a table again
    private void HandleRunStarted()
    {
        StandUp();
        run.ShowMessage("Walk to a table and press E to play.");
    }

    private void HandleRoundStarted()
    {
        if (currentTable != null && run.RoundNumber > 1)
        {
            run.ShowMessage($"New round! Keep playing {currentTable.DisplayName}, or leave the table to switch for free.");
        }
    }

    private string BuildWalkMessage()
    {
        int cost = GetSitCost();

        return cost > 0
            ? $"Walk to a table. Sitting down costs {cost} play this round."
            : "Walk to a table and press E to play.";
    }

    private void RefreshButtons()
    {
        bool showInteract = currentTable == null && nearbyTable != null;
        interactButton.gameObject.SetActive(showInteract);

        if (showInteract)
        {
            int cost = GetSitCost();
            interactButtonText.text = cost > 0
                ? $"Press E to play {nearbyTable.DisplayName}\n(-{cost} play)"
                : $"Press E to play {nearbyTable.DisplayName}";
            interactButton.interactable = CanSit();
        }

        leaveButton.gameObject.SetActive(currentTable != null);
        leaveButton.interactable = CanLeave();
    }
}