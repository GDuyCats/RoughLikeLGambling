using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// The player's own chips on one table: a real pile of casino chips that always matches the chip count.
// Colors by value, like a casino: white 1, red 5, green 25, black 100, purple 500.
// The pile sits in the square in front of the player. The chibi can take chips off it and throw them (E).
// R brings them all back (a hint lies on the table next to the square).
// When a play starts, the bet slides onto the bet circle. A win brings chips over from the dealer's tray,
// a loss sends the bet over to the dealer. Chips that fall off the table fly back on their own.
public class PlayerChipPile : MonoBehaviour
{
    private static readonly int[] ChipValues = { 500, 100, 25, 5, 1 };

    [SerializeField] private TableChip chipPrefab;
    [SerializeField] private Transform betSpot;           // The bet circle
    [SerializeField] private Transform dealerTray;        // Where winnings come from and lost bets go
    [SerializeField] private TMP_FontAsset font;

    [Header("Look")]
    [SerializeField] private Color[] valueColors =        // Same order as the values: 500, 100, 25, 5, 1
    {
        new Color(0.55f, 0.25f, 0.85f),   // Purple 500
        new Color(0.12f, 0.12f, 0.14f),   // Black 100
        new Color(0.15f, 0.65f, 0.25f),   // Green 25
        new Color(0.85f, 0.15f, 0.15f),   // Red 5
        new Color(0.92f, 0.92f, 0.92f)    // White 1
    };
    [SerializeField] private float columnSpacing = 1.3f;
    [SerializeField] private float chipThickness = 0.2f;
    [SerializeField] private int maxChips = 60;           // The pile never shows more chips than this
    [SerializeField] private float labelFontSize = 7f;
    [SerializeField] private Vector3 countLabelOffset = new Vector3(-3.9f, 0f, 0f);   // The chip count, left of the square
    [SerializeField] private Vector3 hintLabelOffset = new Vector3(3.9f, 0f, 0f);     // The R hint, right of the square
    [SerializeField] private string hintText = "<size=60%>PRESS</size>\nR  STACK";

    [Header("Timing")]
    [SerializeField] private float flyTime = 0.35f;
    [SerializeField] private float chipGap = 0.05f;          // Chips leave one after another, this far apart
    [SerializeField] private float pushReturnDelay = 1.2f;   // A play that changed nothing: the bet comes back after this long
    [SerializeField] private float activeRange = 30f;        // The player is at this table when this close

    private RunManager run;
    private PlayerController player;
    private Material[] valueMaterials;
    private TextMeshPro pileLabel;
    private TextMeshPro betLabel;

    private readonly List<TableChip> pileChips = new List<TableChip>();   // Lying in the pile
    private readonly List<TableChip> looseChips = new List<TableChip>();  // Taken off the pile: held or thrown
    private readonly List<TableChip> betChips = new List<TableChip>();    // On the bet circle (just pictures)
    private readonly List<TableChip> flyingChips = new List<TableChip>(); // Pictures on their way somewhere, then gone
    private int betValue;
    private int shownTotal;          // pile + loose + bet, as last shown
    private bool isAnimating;
    private float idleSince = -1f;   // When the play ended with the bet still out (a push)

    private bool IsPlayerHere
    {
        get
        {
            if (player == null)
            {
                return false;
            }

            Vector3 offset = player.transform.position - transform.position;
            offset.y = 0f;
            return offset.magnitude < activeRange;
        }
    }

    private int LooseValue
    {
        get
        {
            int total = 0;

            foreach (TableChip chip in looseChips)
            {
                total += chip.Value;
            }

            return total;
        }
    }

    private void Awake()
    {
        run = FindFirstObjectByType<RunManager>();
        player = FindFirstObjectByType<PlayerController>();

        CreateMaterials();
        pileLabel = CreateLabel("ChipCount", transform.position + countLabelOffset);
        betLabel = CreateLabel("BetAmount", betSpot != null ? betSpot.position + Vector3.right * 1.8f : transform.position);

        TextMeshPro hint = CreateLabel("StackHint", transform.position + hintLabelOffset);
        hint.text = hintText;
        hint.fontSize = labelFontSize * 0.6f;
        hint.color = new Color(1f, 1f, 1f, 0.75f);
        hint.gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        if (run != null)
        {
            run.BetPlaced += OnBetPlaced;
            run.StateChanged += OnStateChanged;
        }
    }

    private void OnDisable()
    {
        if (run != null)
        {
            run.BetPlaced -= OnBetPlaced;
            run.StateChanged -= OnStateChanged;
        }
    }

    private void Start()
    {
        shownTotal = run != null ? run.Chips : 0;
        RebuildPile();
    }

    private void Update()
    {
        if (!IsPlayerHere || GameMenu.IsPaused)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            Restack();
        }

        // The play is over but the chips didn't change (a push): the bet comes back
        if (betValue > 0 && !isAnimating && !run.IsMinigameBusy)
        {
            if (idleSince < 0f)
            {
                idleSince = Time.time;
            }
            else if (Time.time - idleSince > pushReturnDelay)
            {
                StartCoroutine(ReturnBetRoutine());
            }
        }
        else
        {
            idleSince = -1f;
        }
    }

    // ---------- Called by the STACK button, the R key and the chips ----------

    // Every chip lying around flies back onto the pile (the one on the chibi's head stays there)
    public void Restack()
    {
        foreach (TableChip chip in looseChips.ToArray())
        {
            if (!chip.IsHeld)
            {
                looseChips.Remove(chip);
                chip.MakeDecoration();
                StartCoroutine(FlyAndVanish(chip, PileTop(), 0f));
            }
        }

        RebuildPile();
    }

    // The chibi picked a chip off the pile
    public void OnChipTaken(TableChip chip)
    {
        if (pileChips.Remove(chip))
        {
            looseChips.Add(chip);
            RebuildPile();
        }
    }

    // A chip fell off the table (or the chibi left with it): it flies back to the pile
    public void OnChipLost(TableChip chip)
    {
        looseChips.Remove(chip);
        pileChips.Remove(chip);
        chip.MakeDecoration();
        StartCoroutine(FlyAndVanish(chip, PileTop(), 0f));
        RebuildPile();
    }

    // ---------- Bets ----------

    private void OnBetPlaced()
    {
        if (!IsPlayerHere)
        {
            return;
        }

        // A new play started while the last one was still being shown: skip to the end of that
        StopAnimations();

        // Chips lying around come home before the bet
        Restack();

        betValue = Mathf.Min(run.GetCurrentBet(), run.Chips);
        shownTotal = run.Chips;
        RebuildPile();

        // The bet slides from the pile onto the bet circle
        if (betSpot != null)
        {
            List<int> chips = SplitIntoChips(betValue);

            for (int i = 0; i < chips.Count; i++)
            {
                TableChip chip = SpawnChip(chips[i], PileTop(), true);
                betChips.Add(chip);
                StartCoroutine(FlyTo(chip.transform, BetStackPosition(i), i * chipGap));
            }
        }

        ShowBetLabel();
    }

    // The chip count changed: settle the bet with a little show, or just redraw the pile
    private void OnStateChanged()
    {
        if (run == null || run.Chips == shownTotal)
        {
            return;
        }

        int change = run.Chips - shownTotal;
        shownTotal = run.Chips;

        if (betValue > 0 && IsPlayerHere && betSpot != null && dealerTray != null && !isAnimating)
        {
            StartCoroutine(SettleBetRoutine(change));
        }
        else
        {
            ClearBetChips();
            betValue = 0;
            ShowBetLabel();
            RebuildPile();
        }
    }

    private IEnumerator SettleBetRoutine(int change)
    {
        isAnimating = true;

        if (change > 0)
        {
            // Winnings come over from the dealer's tray onto the bet...
            List<int> winnings = SplitIntoChips(change);
            int start = betChips.Count;

            for (int i = 0; i < winnings.Count; i++)
            {
                TableChip chip = SpawnChip(winnings[i], dealerTray.position, true);
                betChips.Add(chip);
                StartCoroutine(FlyTo(chip.transform, BetStackPosition(start + i), i * chipGap));
            }

            yield return new WaitForSeconds(flyTime + winnings.Count * chipGap + 0.4f);

            // ...then everything slides home to the pile
            yield return SendBetChips(PileTop());
        }
        else
        {
            // The dealer takes the bet
            yield return SendBetChips(dealerTray.position);
        }

        betValue = 0;
        ShowBetLabel();
        RebuildPile();
        isAnimating = false;
    }

    // Nothing was won or lost: the bet goes back to the pile
    private IEnumerator ReturnBetRoutine()
    {
        isAnimating = true;
        idleSince = -1f;
        yield return SendBetChips(PileTop());
        betValue = 0;
        ShowBetLabel();
        RebuildPile();
        isAnimating = false;
    }

    private IEnumerator SendBetChips(Vector3 target)
    {
        for (int i = 0; i < betChips.Count; i++)
        {
            StartCoroutine(FlyAndVanish(betChips[i], target, i * chipGap));
        }

        float wait = flyTime + betChips.Count * chipGap;
        betChips.Clear();
        yield return new WaitForSeconds(wait);
    }

    // Stops every chip animation at once and removes the chips that were only flying for show
    private void StopAnimations()
    {
        StopAllCoroutines();

        foreach (TableChip chip in flyingChips)
        {
            if (chip != null)
            {
                Destroy(chip.gameObject);
            }
        }

        flyingChips.Clear();
        ClearBetChips();
        isAnimating = false;
    }

    private void ClearBetChips()
    {
        foreach (TableChip chip in betChips)
        {
            if (chip != null)
            {
                Destroy(chip.gameObject);
            }
        }

        betChips.Clear();
    }

    private void ShowBetLabel()
    {
        betLabel.gameObject.SetActive(betValue > 0);
        betLabel.text = $"<size=55%>BET</size>\n{betValue}";
    }

    // ---------- The pile ----------

    // Lays the pile out again from the chip count: the chips not lying around and not on the bet
    private void RebuildPile()
    {
        foreach (TableChip chip in pileChips)
        {
            if (chip != null)
            {
                Destroy(chip.gameObject);
            }
        }

        pileChips.Clear();

        int total = run != null ? run.Chips : 0;
        int pileValue = Mathf.Max(0, total - betValue - LooseValue);
        List<int> chips = SplitIntoChips(pileValue);
        int[] heights = new int[ChipValues.Length];

        foreach (int value in chips)
        {
            int column = System.Array.IndexOf(ChipValues, value);
            Vector3 position = ColumnPosition(column) + Vector3.up * chipThickness * (heights[column] + 0.5f);
            heights[column]++;

            TableChip chip = SpawnChip(value, position, false);
            chip.HoldStill();
            pileChips.Add(chip);
        }

        pileLabel.text = pileValue.ToString();
        pileLabel.gameObject.SetActive(total > 0 || pileValue > 0);
    }

    // 500s, 100s, 25s, 5s and 1s, fewest chips first (like a casino cashier)
    private List<int> SplitIntoChips(int amount)
    {
        List<int> chips = new List<int>();

        foreach (int value in ChipValues)
        {
            while (amount >= value && chips.Count < maxChips)
            {
                chips.Add(value);
                amount -= value;
            }
        }

        return chips;
    }

    // Two rows of columns: 500, 100, 25 at the back, 5 and 1 at the front
    private Vector3 ColumnPosition(int column)
    {
        float x = column < 3 ? (column - 1) * columnSpacing : (column - 3.5f) * columnSpacing;
        float z = column < 3 ? columnSpacing * 0.5f : -columnSpacing * 0.5f;
        return transform.position + new Vector3(x, 0f, z);
    }

    private Vector3 PileTop()
    {
        return transform.position + Vector3.up * 1f;
    }

    private Vector3 BetStackPosition(int index)
    {
        return betSpot.position + Vector3.up * chipThickness * (index + 0.5f);
    }

    // A chip of the player's: a real one for the pile, or just a picture for the bet
    private TableChip SpawnChip(int value, Vector3 position, bool decoration)
    {
        TableChip chip = Instantiate(chipPrefab, position, Quaternion.identity, transform);
        chip.SetLook(valueMaterials[System.Array.IndexOf(ChipValues, value)]);
        chip.Value = value;

        if (decoration)
        {
            chip.MakeDecoration();
        }
        else
        {
            chip.Pile = this;
        }

        return chip;
    }

    // ---------- Flying chips ----------

    private IEnumerator FlyTo(Transform chip, Vector3 target, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        if (chip == null)
        {
            yield break;
        }

        Vector3 from = chip.position;

        for (float t = 0f; t < flyTime; t += Time.deltaTime)
        {
            if (chip == null)
            {
                yield break;
            }

            float progress = t / flyTime;
            chip.position = Vector3.Lerp(from, target, progress) + Vector3.up * Mathf.Sin(progress * Mathf.PI) * 1.2f;
            yield return null;
        }

        if (chip != null)
        {
            chip.position = target;
        }
    }

    private IEnumerator FlyAndVanish(TableChip chip, Vector3 target, float delay)
    {
        flyingChips.Add(chip);
        yield return FlyTo(chip.transform, target, delay);
        AudioManager.Play(SoundId.ChipHit, 0.4f);
        flyingChips.Remove(chip);

        if (chip != null)
        {
            Destroy(chip.gameObject);
        }
    }

    // ---------- Setup ----------

    private void CreateMaterials()
    {
        Renderer prefabRenderer = chipPrefab.GetComponentInChildren<Renderer>();
        Material baseMaterial = prefabRenderer != null ? prefabRenderer.sharedMaterial : null;
        valueMaterials = new Material[ChipValues.Length];

        for (int i = 0; i < ChipValues.Length; i++)
        {
            Material material = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = i < valueColors.Length ? valueColors[i] : Color.white;
            material.name = $"Chip{ChipValues[i]}";
            valueMaterials[i] = material;
        }
    }

    // A number lying flat on the table, readable from the player's side
    private TextMeshPro CreateLabel(string objectName, Vector3 position)
    {
        GameObject labelObject = new GameObject(objectName, typeof(RectTransform));
        labelObject.transform.SetParent(transform, true);
        labelObject.transform.SetPositionAndRotation(position + Vector3.up * 0.02f, Quaternion.Euler(90f, 0f, 0f));

        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();

        if (font != null)
        {
            label.font = font;
        }

        label.fontSize = labelFontSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(4f, 2f);
        label.color = Color.white;
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(0, 0, 0, 255);
        labelObject.SetActive(false);
        return label;
    }
}
