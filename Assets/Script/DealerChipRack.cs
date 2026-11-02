using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The dealer's chip rack: five stacks of real casino chips (500, 100, 25, 5, 1), the same chips as the player's.
// Winnings are taken off the right stack and fly to the player; lost bets land back on the rack.
// A stack that runs out is topped up again by the house after a moment.
// The chibi can steal chips from the rack (the dealer notices, and the chip flies back after a while).
// Sits on the "DealerTray" object; the rack itself (wood, dividers) is made by Tools > Rearrange Tables.
public class DealerChipRack : MonoBehaviour
{
    [SerializeField] private TableChip chipPrefab;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private float slotSpacing = 1.45f;      // Distance between the stacks
    [SerializeField] private float floorHeight = 0.15f;      // The rack's floor, where the bottom chip lies
    [SerializeField] private float chipThickness = 0.2f;
    [SerializeField] private int[] startCounts = { 4, 7, 9, 10, 10 };   // Same order as the values: 500, 100, 25, 5, 1
    [SerializeField] private int maxPerStack = 14;
    [SerializeField] private float refillDelay = 1.5f;

    private readonly List<TableChip>[] stacks = new List<TableChip>[5];
    private readonly bool[] refilling = new bool[5];
    private Material baseMaterial;

    private void Awake()
    {
        Renderer prefabRenderer = chipPrefab != null ? chipPrefab.GetComponentInChildren<Renderer>() : null;
        baseMaterial = prefabRenderer != null ? prefabRenderer.sharedMaterial : null;

        for (int i = 0; i < stacks.Length; i++)
        {
            stacks[i] = new List<TableChip>();
        }
    }

    private void Start()
    {
        for (int i = 0; i < stacks.Length; i++)
        {
            for (int n = 0; n < startCounts[i]; n++)
            {
                AddChipNow(i);
            }
        }
    }

    // Where the next chip of this value would land on top of its stack
    public Vector3 SlotTop(int value)
    {
        int index = Mathf.Max(0, ChipLook.IndexOf(value));
        CleanStack(index);
        return StackBase(index) + Vector3.up * chipThickness * (stacks[index].Count + 0.5f);
    }

    // The dealer pays out: the top chip of that value leaves the rack. Returns where it was.
    public Vector3 TakeChip(int value)
    {
        int index = Mathf.Max(0, ChipLook.IndexOf(value));
        CleanStack(index);
        List<TableChip> stack = stacks[index];

        for (int i = stack.Count - 1; i >= 0; i--)
        {
            TableChip chip = stack[i];

            // A chip the chibi is carrying (or that lies somewhere else) is not in the rack right now
            if (chip.IsHeld || (chip.transform.position - ChipPosition(index, i)).sqrMagnitude > 0.25f)
            {
                continue;
            }

            Vector3 from = chip.transform.position;
            stack.RemoveAt(i);
            Destroy(chip.gameObject);

            if (stack.Count == 0 && !refilling[index])
            {
                StartCoroutine(RefillRoutine(index));
            }

            return from;
        }

        return SlotTop(value);
    }

    // A lost bet landed on the rack
    public void AddChip(int value)
    {
        int index = ChipLook.IndexOf(value);

        if (index < 0)
        {
            return;
        }

        CleanStack(index);

        if (stacks[index].Count < maxPerStack)
        {
            AddChipNow(index);
        }
    }

    // The house tops an empty stack up again: chips drop in one after another
    private IEnumerator RefillRoutine(int index)
    {
        refilling[index] = true;
        yield return new WaitForSeconds(refillDelay);

        for (int n = 0; n < startCounts[index]; n++)
        {
            TableChip chip = AddChipNow(index);
            Vector3 landing = chip.transform.position;
            chip.transform.position = landing + Vector3.up * 1.5f;

            for (float t = 0f; t < 0.12f; t += Time.deltaTime)
            {
                if (chip == null)
                {
                    break;
                }

                chip.transform.position = Vector3.Lerp(landing + Vector3.up * 1.5f, landing, t / 0.12f);
                yield return null;
            }

            if (chip != null)
            {
                chip.transform.position = landing;
                chip.HoldStill();
            }

            AudioManager.Play(SoundId.ChipHit, 0.3f);
        }

        refilling[index] = false;
    }

    private TableChip AddChipNow(int index)
    {
        List<TableChip> stack = stacks[index];
        Vector3 position = ChipPosition(index, stack.Count);
        TableChip chip = Instantiate(chipPrefab, position, Quaternion.identity, transform);
        int value = ChipLook.Values[index];

        chip.SetLook(ChipLook.GetMaterial(value, baseMaterial));
        chip.Value = value;
        chip.MakeDealerChip();
        chip.HoldStill();
        ChipLook.AddValueLabel(chip, value, font);
        stack.Add(chip);
        return chip;
    }

    // Chips that were destroyed (for example by a table reset) leave the list
    private void CleanStack(int index)
    {
        stacks[index].RemoveAll(chip => chip == null);
    }

    // 500 on the left, 1 on the right, as seen from the player
    private Vector3 StackBase(int index)
    {
        float x = (index - (stacks.Length - 1) / 2f) * slotSpacing;
        return transform.TransformPoint(new Vector3(x, floorHeight, 0f));
    }

    private Vector3 ChipPosition(int index, int height)
    {
        return StackBase(index) + Vector3.up * chipThickness * (height + 0.5f);
    }
}
