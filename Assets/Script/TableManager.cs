using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Lets the player walk up to a table and play. Tables with an arena use a climbing transition
// onto a giant table top. In the final round, only the boss's table is open.
public class TableManager : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private PlayerController player;
    [SerializeField] private List<TableStation> tables;
    [SerializeField] private TMP_Text gameTitleText;
    [SerializeField] private Button interactButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private GameObject bossModel;
    [SerializeField] private float interactRange = 1.5f;
    [SerializeField] private int switchCostAfterPlaying = 1;

    [Header("Table Transition")]
    [SerializeField] private ScreenFader fader;
    [SerializeField] private CinemachineBrain cameraBrain;
    [SerializeField] private float hopTime = 0.35f;
    [SerializeField] private float hopHeight = 1.5f;
    [SerializeField] private float fadeTime = 0.25f;

    private TableStation currentTable;
    private TableStation nearbyTable;
    private TMP_Text interactButtonText;
    private bool isTransitioning;
    private Vector3 lobbyReturnPosition;   // Where the chibi stood before climbing onto a table

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
        if (isTransitioning)
        {
            return;
        }

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

    private bool IsTableClosed(TableStation table)
    {
        return run.IsBossFight && table != BossTable;
    }

    private bool CanSit()
    {
        if (isTransitioning || nearbyTable == null || !run.CanPlay || IsTableClosed(nearbyTable))
        {
            return false;
        }

        int cost = GetSitCost();
        return cost == 0 || run.PlaysLeft > cost;
    }

    private bool CanLeave()
    {
        return !isTransitioning && currentTable != null && run.CanPlay && !run.IsMinigameBusy && !run.IsBossFight;
    }

    private void TrySitAtNearbyTable()
    {
        if (!CanSit())
        {
            return;
        }

        StartCoroutine(SitRoutine(nearbyTable, GetSitCost()));
    }

    private IEnumerator SitRoutine(TableStation table, int cost)
    {
        isTransitioning = true;
        currentTable = table;
        nearbyTable = null;
        player.CanMove = false;
        RefreshButtons();

        if (cost > 0)
        {
            run.SpendPlays(cost);
        }

        if (table.Arena != null)
        {
            lobbyReturnPosition = player.transform.position;

            // Hop toward the table's edge, then fade to black while the chibi "climbs up"
            Vector3 edge = table.ClosestPoint(player.transform.position);
            Vector3 hopTarget = new Vector3(edge.x, player.transform.position.y + 1f, edge.z);
            yield return player.HopTo(hopTarget, hopHeight, hopTime);
            yield return fader.FadeOut(fadeTime);

            // While the screen is black, cut the camera instantly instead of blending across the map
            CinemachineBlendDefinition previousBlend = cameraBrain.DefaultBlend;
            cameraBrain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

            player.SetWalkArea(table.Arena.WalkMin, table.Arena.WalkMax);
            player.TeleportTo(table.Arena.SpawnPoint.position);
            table.SetOpen(true);

            // Give the camera a couple of frames to switch, then restore the smooth blend
            yield return null;
            yield return null;
            cameraBrain.DefaultBlend = previousBlend;

            ShowSeatedInfo(table);
            yield return fader.FadeIn(fadeTime);
        }
        else
        {
            table.SetOpen(true);
            ShowSeatedInfo(table);
        }

        // On a giant table the chibi can walk around (not during the boss fight yet)
        player.CanMove = table.Arena != null && !run.IsBossFight;
        isTransitioning = false;
        RefreshButtons();
    }

    private void ShowSeatedInfo(TableStation table)
    {
        gameTitleText.text = table.DisplayName;

        if (run.IsBossFight)
        {
            run.ShowMessage($"{run.FinalBoss.DisplayName} challenges you! Win hands to deal damage.");
        }
    }

    private void LeaveTable()
    {
        if (!CanLeave())
        {
            return;
        }

        StartCoroutine(LeaveRoutine(BuildWalkMessage()));
    }

    // message can be null to keep the current message
    private IEnumerator LeaveRoutine(string message)
    {
        isTransitioning = true;
        TableStation table = currentTable;
        player.CanMove = false;
        RefreshButtons();

        if (table != null && table.Arena != null)
        {
            yield return fader.FadeOut(fadeTime);

            CinemachineBlendDefinition previousBlend = cameraBrain.DefaultBlend;
            cameraBrain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

            table.SetOpen(false);
            player.ResetWalkArea();
            player.TeleportTo(lobbyReturnPosition);

            yield return null;
            yield return null;
            cameraBrain.DefaultBlend = previousBlend;

            currentTable = null;
            gameTitleText.text = "Casino";
            yield return fader.FadeIn(fadeTime);
        }
        else
        {
            if (table != null)
            {
                table.SetOpen(false);
            }

            currentTable = null;
            gameTitleText.text = "Casino";
        }

        player.CanMove = true;

        if (message != null)
        {
            run.ShowMessage(message);
        }

        isTransitioning = false;
        RefreshButtons();
    }

    private void HandleRunStarted()
    {
        HideBoss();

        if (currentTable != null)
        {
            StartCoroutine(LeaveRoutine("Walk to a table and press E to play."));
        }
        else
        {
            run.ShowMessage("Walk to a table and press E to play.");
        }
    }

    private void HandleRoundStarted()
    {
        if (run.IsBossFight)
        {
            TableStation bossTable = BossTable;

            if (bossTable == null)
            {
                Debug.LogError($"No table has Minigame set to {run.FinalBoss.FavoriteGame}. Check the Minigame field on each TableStation.");
                return;
            }

            if (currentTable != null && currentTable != bossTable)
            {
                StartCoroutine(LeaveRoutine(null));
            }
            else if (currentTable == bossTable)
            {
                // Already at the boss's table: stand still for the fight
                player.CanMove = false;
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
        if (bossModel == null)
        {
            Debug.LogError("Boss Model is not assigned on TableManager.");
            return;
        }

        if (table == null)
        {
            Debug.LogError("No boss table found. Check the Minigame field on each TableStation.");
            return;
        }

        if (table.BossSpot == null)
        {
            Debug.LogError($"{table.name} has no Boss Spot assigned in its TableStation.");
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
        bool showInteract = !isTransitioning && currentTable == null && nearbyTable != null;
        interactButton.gameObject.SetActive(showInteract);

        if (showInteract)
        {
            if (IsTableClosed(nearbyTable))
            {
                string bossTableName = BossTable != null ? BossTable.DisplayName : "boss";
                interactButtonText.text = $"Closed. The boss waits at the {bossTableName} table.";
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

        leaveButton.gameObject.SetActive(currentTable != null && !isTransitioning);
        leaveButton.interactable = CanLeave();
    }
}