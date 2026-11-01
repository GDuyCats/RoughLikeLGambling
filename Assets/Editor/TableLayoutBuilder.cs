using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// An editor tool: Tools > Rearrange Tables. It also adds the player's chips to tables that have none yet.
// On every table it makes room for the player's chips in a square right in front of the player:
// - the chip pile sits in a gold square at the front of the table,
// - the row of buttons moves up toward the dealer,
// - the message sign moves between the cards and the buttons,
// - the bet circle and the dealer's chip tray get their new spots,
// - the old STACK button is removed (R stacks the chips; a hint lies next to the square),
// - the chibi lands beside the square instead of on the chips.
// Blackjack's card rows move up a little too, so each hand's total, the split arrow and the message fit above the buttons.
// Safe to run again: it always puts everything in the same places.
public static class TableLayoutBuilder
{
    // Spots in each arena's own space (x = left/right, z = toward the dealer)
    private const float ButtonRowZ = -4.8f;
    private const float MessageZ = -2.9f;
    private const float BlackjackMessageZ = -3.3f;
    private const float BlackjackPlayerRowZ = 0.6f;
    private const float BlackjackDealerRowZ = 3.6f;
    private static readonly Vector3 SpawnSpot = new Vector3(-6.5f, 0f, -7.3f);

    [MenuItem("Tools/Rearrange Tables")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Tell("Please stop Play mode first, then run Tools > Rearrange Tables again.");
            return;
        }

        TableManager tableManager = Object.FindFirstObjectByType<TableManager>(FindObjectsInactive.Include);

        if (tableManager == null)
        {
            Tell("Could not find the TableManager in the open scene. Is the game scene open?");
            return;
        }

        List<string> report = new List<string>();
        Material lineMaterial = PlayerChipsBuilder.MakeLineMaterial();
        SerializedProperty tables = new SerializedObject(tableManager).FindProperty("tables");

        for (int i = 0; i < tables.arraySize; i++)
        {
            TableStation table = tables.GetArrayElementAtIndex(i).objectReferenceValue as TableStation;

            if (table != null && table.Arena != null)
            {
                report.Add(Rearrange(table, lineMaterial));
            }
        }

        RearrangeBlackjack(report);

        EditorSceneManager.MarkSceneDirty(tableManager.gameObject.scene);
        Tell(string.Join("\n", report) + "\n\nPress Ctrl + S to save the scene.");
    }

    private static string Rearrange(TableStation table, Material lineMaterial)
    {
        Transform arena = table.Arena.transform;

        // No chips on this table yet: add them first (same as Tools > Set Up Player Chips)
        string added = PlayerChipsBuilder.SetUpTableIfMissing(table);
        Transform chips = arena.Find("PlayerChips");

        if (chips == null)
        {
            return added ?? $"{table.name}: could not add the player's chips.";
        }

        bool isBlackjack = table.Minigame == MinigameType.Blackjack;

        // The chips: the pile in the square, the bet circle near the cards, the tray on the dealer's side
        MoveLocal(chips.Find("ChipPile"), PlayerChipsBuilder.PileSpot);
        MoveLocal(chips.Find("BetSpot"), PlayerChipsBuilder.BetSpotFor(table.Minigame) + Vector3.up * 0.006f);
        MoveLocal(chips.Find("DealerTray"), PlayerChipsBuilder.DealerTraySpot);
        PlayerChipsBuilder.MakeSquare(chips, lineMaterial);

        int removed = 0;
        int moved = 0;

        foreach (TableButton button in arena.GetComponentsInChildren<TableButton>(true))
        {
            // The STACK button is not needed any more: R does it
            if (button.name == "TableStackButton")
            {
                Button screenButton = new SerializedObject(button).FindProperty("screenButton").objectReferenceValue as Button;

                if (screenButton != null)
                {
                    Undo.DestroyObjectImmediate(screenButton.gameObject);
                }

                Undo.DestroyObjectImmediate(button.gameObject);
                removed++;
                continue;
            }

            // Every other button: same left/right place, in the row a little further up
            SetArenaZ(arena, button.transform, ButtonRowZ);
            moved++;
        }

        // The message sign
        foreach (Transform child in arena.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("MessageSign"))
            {
                SetArenaZ(arena, child, isBlackjack ? BlackjackMessageZ : MessageZ);
            }
        }

        // Where the chibi lands: beside the square, not on the chips
        TableArena tableArena = table.Arena;
        Transform spawn = new SerializedObject(tableArena).FindProperty("spawnPoint").objectReferenceValue as Transform;

        if (spawn != null)
        {
            Undo.RecordObject(spawn, "Rearrange Tables");
            Vector3 local = arena.InverseTransformPoint(spawn.position);
            spawn.position = arena.TransformPoint(new Vector3(SpawnSpot.x, local.y, SpawnSpot.z));
        }

        string removedText = removed > 0 ? $", STACK button removed" : "";
        return $"{table.name}: {moved} buttons moved up, chips in the square{removedText}.";
    }

    // Blackjack: the player's cards move up a little, and the split arrow gets shorter, so everything fits
    private static void RearrangeBlackjack(List<string> report)
    {
        BlackjackGame game = Object.FindFirstObjectByType<BlackjackGame>(FindObjectsInactive.Include);

        if (game == null)
        {
            return;
        }

        SerializedObject fields = new SerializedObject(game);
        Transform playerRow = fields.FindProperty("playerRowStart").objectReferenceValue as Transform;
        Transform dealerRow = fields.FindProperty("dealerRowStart").objectReferenceValue as Transform;
        TableStation table = FindTableFor(MinigameType.Blackjack);

        if (table != null && table.Arena != null)
        {
            if (playerRow != null)
            {
                SetArenaZ(table.Arena.transform, playerRow, BlackjackPlayerRowZ);
            }

            if (dealerRow != null)
            {
                SetArenaZ(table.Arena.transform, dealerRow, BlackjackDealerRowZ);
            }
        }

        fields.FindProperty("arrowLength").floatValue = 1f;
        fields.FindProperty("arrowGap").floatValue = 0.2f;
        fields.ApplyModifiedProperties();
        report.Add("Blackjack: both card rows moved up a little, the split arrow is shorter.");
    }

    private static TableStation FindTableFor(MinigameType game)
    {
        foreach (TableStation table in Object.FindObjectsByType<TableStation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (table.Minigame == game)
            {
                return table;
            }
        }

        return null;
    }

    // ---------- Helpers ----------

    private static void MoveLocal(Transform target, Vector3 localPosition)
    {
        if (target == null)
        {
            return;
        }

        Undo.RecordObject(target, "Rearrange Tables");
        target.localPosition = localPosition;
    }

    // Keeps x and height, puts the object at this distance from the arena's middle (toward the dealer)
    private static void SetArenaZ(Transform arena, Transform target, float z)
    {
        Undo.RecordObject(target, "Rearrange Tables");
        Vector3 local = arena.InverseTransformPoint(target.position);
        target.position = arena.TransformPoint(new Vector3(local.x, local.y, z));
    }

    private static void Tell(string message)
    {
        EditorUtility.DisplayDialog("Rearrange Tables", message, "OK");
    }
}
