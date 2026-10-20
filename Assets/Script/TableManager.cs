using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Lets the player walk up to a table, sit down to play, and leave again.
// In the final round, only the boss's table is open.
public class TableManager : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private PlayerController player;
    [SerializeField] private List<TableStation> tables;
    [SerializeField] private TMP_Text gameTitleText;
    [SerializeField] private Button interactButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private GameObject bossModel;            // Placeholder body of the final boss
    [SerializeField] private float interactRange = 1.5f;
    [SerializeField] private int switchCostAfterPlaying = 1;

    private TableStation currentTable;
    private TableStation nearbyTable;
    private TMP_Text interactButtonText;

    // The table where the final boss fights (its favorite minigame)
    private TableStation BossTable =>
        run.FinalBoss != null ? tables.Find(table => table.Minigame == run.FinalBoss.FavoriteGame) : null;

    private void Awake()
    {
        interactButtonText = interactButton.GetComponentInChildren<TMP_Text>();

        interactButton.onClick.AddListener(TrySitAtNearbyTable);
        leaveButton.onClick.AddListener(LeaveTable);

        foreach (TableStation table in tables)
        {
            table.SetOpen(false);
        }

        HideBoss();
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

    private int GetSitCost()
    {
        return run.HasPlayedThisRound ? switchCostAfterPlaying : 0;
    }

    // During the boss fight, every table except the boss's is closed
    private bool IsTableClosed(TableStation table)
    {
        return run.IsBossFight && table != BossTable;
    }

    private bool CanSit()
    {
        if (nearbyTable == null || !run.CanPlay || IsTableClosed(nearbyTable))
        {
            return false;
        }

        int cost = GetSitCost();
        return cost == 0 || run.PlaysLeft > cost;
    }

    // No walking away from a hand in progress, or from the final boss
    private bool CanLeave()
    {
        return currentTable != null && run.CanPlay && !run.IsMinigameBusy && !run.IsBossFight;
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

        if (run.IsBossFight)
        {
            run.ShowMessage($"{run.FinalBoss.DisplayName} challenges you! Win hands to deal damage.");
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

    private void HandleRunStarted()
    {
        StandUp();
        HideBoss();
        run.ShowMessage("Walk to a table and press E to play.");
    }

    private void HandleRoundStarted()
    {
        if (run.IsBossFight)
        {
            TableStation bossTable = BossTable;

            // Sitting at another table? Stand up and walk to the boss.
            if (currentTable != null && currentTable != bossTable)
            {
                StandUp();
            }

            PlaceBoss(bossTable);

            run.ShowMessage(currentTable == bossTable
                ? $"The final boss {run.FinalBoss.DisplayName} has arrived! Fight!"
                : $"The final boss {run.FinalBoss.DisplayName} waits at the {bossTable.DisplayName} table!");
            return;
        }

        HideBoss();

        if (currentTable != null && run.RoundNumber > 1)
        {
            run.ShowMessage($"New round! Keep playing {currentTable.DisplayName}, or leave the table to switch for free.");
        }
    }

    private void PlaceBoss(TableStation table)
    {
        if (bossModel == null || table == null || table.BossSpot == null)
        {
            return;
        }

        bossModel.transform.position = table.BossSpot.position;
        bossModel.SetActive(true);
    }

    private void HideBoss()
    {
        if (bossModel != null)
        {
            bossModel.SetActive(false);
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
            if (IsTableClosed(nearbyTable))
            {
                interactButtonText.text = $"Closed. The boss waits at the {BossTable.DisplayName} table.";
            }
            else if (run.IsBossFight)
            {
                interactButtonText.text = $"Press E to fight {run.FinalBoss.DisplayName}";
            }
            else
            {
                int cost = GetSitCost();
                interactButtonText.text = cost > 0
                    ? $"Press E to play {nearbyTable.DisplayName}\n(-{cost} play)"
                    : $"Press E to play {nearbyTable.DisplayName}";
            }

            interactButton.interactable = CanSit();
        }

        leaveButton.gameObject.SetActive(currentTable != null);
        leaveButton.interactable = CanLeave();
    }
}