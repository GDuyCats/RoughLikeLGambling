using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// An editor tool: Tools > Rearrange Tables. It also adds the player's chips to tables that have none yet.
// On every table:
// - the table gets 1.2 times bigger (36 x 21.6), with its walkable area, light and cameras (see TableResizer),
// - the chip pile sits in a gold square at the front of the table,
// - the minigame's buttons become a small ACTION panel left of the square, with the countdown ring above it,
// - a BET panel (+1, +5, +25, +100, CLEAR, ALL IN) sits right of the square (see TablePanelBuilder),
// - the message sign sits between the cards and the panels,
// - the dealer's chips sit in a real chip rack, with the dealer's hands on both sides (see DealerRackBuilder),
// - the old STACK button is removed (R stacks the chips; a hint lies next to the square),
// - the chibi lands in front of the square.
// Blackjack also gets a real casino layout: half-moon felt, printed payouts, hands on seats with frames,
// an 8-deck shoe and a discard holder (see BlackjackFeltBuilder and CardShoeBuilder).
// The new sound files are put into the AudioManager too (see SoundAssigner).
// Safe to run again: it always puts everything in the same places.
public static class TableLayoutBuilder
{
    // Spots in each arena's own space (x = left/right, z = toward the dealer): see TableSpots
    private const float MessageZ = TableSpots.MessageZ;
    private const float BlackjackMessageZ = TableSpots.BlackjackMessageZ;
    private const float BlackjackSignWidth = TableSpots.BlackjackSignWidth;   // Narrower, so it fits between the two panels
    private static Vector3 SpawnSpot => TableSpots.SpawnSpot;

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
        RunManager run = Object.FindFirstObjectByType<RunManager>(FindObjectsInactive.Include);
        TMP_FontAsset font = FindFont("Fredoka-Bold");
        SerializedProperty tables = new SerializedObject(tableManager).FindProperty("tables");

        for (int i = 0; i < tables.arraySize; i++)
        {
            TableStation table = tables.GetArrayElementAtIndex(i).objectReferenceValue as TableStation;

            if (table != null && table.Arena != null)
            {
                report.Add(TableResizer.Resize(table));
                report.Add(Rearrange(table, lineMaterial));
                report.Add(DealerRackBuilder.Build(table, font));
                report.Add(TablePanelBuilder.Arrange(table, run, font));
            }
        }

        TableResizer.ScaleSharedThings(report);
        RearrangeBlackjack(report, font);
        UpdateBetUpgrade(report);
        SoundAssigner.Assign(report);
        AssetDatabase.SaveAssets();

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
        MoveLocal(chips.Find("DealerTray"), PlayerChipsBuilder.DealerTraySpotFor(table.Minigame));
        PlayerChipsBuilder.MakeSquare(chips, lineMaterial);
        MoveLocal(chips.Find("ChipSquare"), PlayerChipsBuilder.PileSpot + Vector3.up * 0.006f);

        int removed = 0;

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
            }
        }

        // The message sign
        foreach (Transform child in arena.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("MessageSign"))
            {
                SetArenaZ(arena, child, isBlackjack ? BlackjackMessageZ : MessageZ);
            }
        }

        // Where the chibi lands: in front of the square, between the two panels
        TableArena tableArena = table.Arena;
        Transform spawn = new SerializedObject(tableArena).FindProperty("spawnPoint").objectReferenceValue as Transform;

        if (spawn != null)
        {
            Undo.RecordObject(spawn, "Rearrange Tables");
            Vector3 local = arena.InverseTransformPoint(spawn.position);
            spawn.position = arena.TransformPoint(new Vector3(SpawnSpot.x, local.y, SpawnSpot.z));
        }

        string removedText = removed > 0 ? ", STACK button removed" : "";
        return $"{table.name}: chips in the square{removedText}.";
    }

    // Blackjack: the casino layout, a shorter split arrow, and a narrower message sign
    private static void RearrangeBlackjack(List<string> report, TMP_FontAsset font)
    {
        BlackjackGame game = Object.FindFirstObjectByType<BlackjackGame>(FindObjectsInactive.Include);
        TableStation table = FindTableFor(MinigameType.Blackjack);

        if (game == null || table == null || table.Arena == null)
        {
            return;
        }

        SerializedObject fields = new SerializedObject(game);
        fields.FindProperty("arrowLength").floatValue = 1f;
        fields.FindProperty("arrowGap").floatValue = 0.2f;
        fields.ApplyModifiedProperties();

        BlackjackFeltBuilder.Build(table.Arena.transform, game, font, report);
        SetSignWidth(table, BlackjackSignWidth, report);
    }

    // The TableMessageMirror's entry for this table gets its own sign width
    private static void SetSignWidth(TableStation table, float width, List<string> report)
    {
        TableMessageMirror mirror = Object.FindFirstObjectByType<TableMessageMirror>(FindObjectsInactive.Include);
        GameObject panel = new SerializedObject(table).FindProperty("minigamePanel").objectReferenceValue as GameObject;

        if (mirror == null || panel == null)
        {
            return;
        }

        SerializedObject fields = new SerializedObject(mirror);
        SerializedProperty signs = fields.FindProperty("tables");

        for (int i = 0; i < signs.arraySize; i++)
        {
            SerializedProperty entry = signs.GetArrayElementAtIndex(i);

            if (entry.FindPropertyRelative("minigamePanel").objectReferenceValue == panel)
            {
                entry.FindPropertyRelative("width").floatValue = width;
            }
        }

        fields.ApplyModifiedProperties();
    }

    // The Extra Bet Level upgrade now raises the table limit: its description says so
    private static void UpdateBetUpgrade(List<string> report)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Upgrade"))
        {
            Upgrade upgrade = AssetDatabase.LoadAssetAtPath<Upgrade>(AssetDatabase.GUIDToAssetPath(guid));

            if (upgrade == null || upgrade.Type != UpgradeType.ExtraBetLevel)
            {
                continue;
            }

            SerializedObject fields = new SerializedObject(upgrade);
            fields.FindProperty("description").stringValue = "Raises every table's max bet by 50 chips";
            fields.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(upgrade);
            report.Add($"Upgrade {upgrade.DisplayName}: now raises the table limit.");
        }
    }

    private static TMP_FontAsset FindFont(string nameContains)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));

            if (font != null && font.name.Contains(nameContains))
            {
                return font;
            }
        }

        return null;
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
