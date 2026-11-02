using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// A one-time editor tool: Tools > Set Up Player Chips.
// On every table with an arena it adds:
// 1. The player's chip pile (PlayerChipPile) in a square right in front of the player, with a bet circle near the cards.
// 2. The dealer's chip tray: the old loose chip stacks move to the dealer's side and become the dealer's chips.
// Then run Tools > Rearrange Tables to move the buttons and signs out of the way.
// Safe to run again: tables that already have their chips are skipped.
public static class PlayerChipsBuilder
{
    private const string ChipPrefabPath = "Assets/Prefabs/TableChip.prefab";
    private const string MaterialFolder = "Assets/Materials/Chips";

    // Spots on each table, in the arena's own space (x = left/right, z = toward the dealer)
    public static Vector3 PileSpot => TableSpots.PileSpot;
    public static Vector2 SquareSize => TableSpots.SquareSize;
    private const float TrayStackSpacing = 1.5f;

    [MenuItem("Tools/Set Up Player Chips")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Tell("Please stop Play mode first, then run Tools > Set Up Player Chips again.");
            return;
        }

        TableManager tableManager = Object.FindFirstObjectByType<TableManager>(FindObjectsInactive.Include);
        TableChip chipPrefab = AssetDatabase.LoadAssetAtPath<TableChip>(ChipPrefabPath);

        if (tableManager == null || chipPrefab == null)
        {
            Tell($"Could not find the TableManager in the open scene, or the chip prefab at {ChipPrefabPath}.");
            return;
        }

        List<string> report = new List<string>();
        Material betMaterial = MakeLineMaterial();
        TMP_FontAsset font = FindFont("Fredoka-Bold");
        SerializedProperty tables = new SerializedObject(tableManager).FindProperty("tables");

        for (int i = 0; i < tables.arraySize; i++)
        {
            TableStation table = tables.GetArrayElementAtIndex(i).objectReferenceValue as TableStation;

            if (table == null || table.Arena == null)
            {
                continue;
            }

            report.Add(SetUpTable(table, chipPrefab, betMaterial, font));
        }

        EditorSceneManager.MarkSceneDirty(tableManager.gameObject.scene);
        AssetDatabase.SaveAssets();

        Tell(string.Join("\n", report) + "\n\nPress Ctrl + S to save the scene.");
    }

    // Used by Tools > Rearrange Tables, so one tool is enough: adds the chips to a table that has none yet
    public static string SetUpTableIfMissing(TableStation table)
    {
        if (table.Arena.transform.Find("PlayerChips") != null)
        {
            return null;
        }

        TableChip chipPrefab = AssetDatabase.LoadAssetAtPath<TableChip>(ChipPrefabPath);

        if (chipPrefab == null)
        {
            return $"{table.name}: could not find the chip prefab at {ChipPrefabPath}.";
        }

        return SetUpTable(table, chipPrefab, MakeLineMaterial(), FindFont("Fredoka-Bold"));
    }

    private static string SetUpTable(TableStation table, TableChip chipPrefab, Material betMaterial, TMP_FontAsset font)
    {
        Transform arena = table.Arena.transform;

        if (arena.Find("PlayerChips") != null)
        {
            return $"{table.name}: already has its chips.";
        }

        GameObject root = new GameObject("PlayerChips");
        Undo.RegisterCreatedObjectUndo(root, "Set Up Player Chips");
        root.transform.SetParent(arena, false);

        // The bet circle: a flat gold disc near the cards
        GameObject betSpot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        betSpot.name = "BetSpot";
        Object.DestroyImmediate(betSpot.GetComponent<Collider>());
        betSpot.transform.SetParent(root.transform, false);
        betSpot.transform.localPosition = BetSpotFor(table.Minigame) + Vector3.up * 0.006f;
        betSpot.transform.localScale = new Vector3(2.2f, 0.004f, 2.2f);

        if (betMaterial != null)
        {
            betSpot.GetComponent<Renderer>().sharedMaterial = betMaterial;
        }

        // The dealer's tray, in front of the dealer
        GameObject tray = new GameObject("DealerTray");
        tray.transform.SetParent(root.transform, false);
        tray.transform.localPosition = DealerTraySpotFor(table.Minigame);
        int stacksMoved = MoveOldChipsToTray(arena, root.transform, tray.transform);

        // The player's pile
        GameObject pileObject = new GameObject("ChipPile");
        pileObject.transform.SetParent(root.transform, false);
        pileObject.transform.localPosition = PileSpot;

        PlayerChipPile pile = pileObject.AddComponent<PlayerChipPile>();
        SerializedObject pileFields = new SerializedObject(pile);
        pileFields.FindProperty("chipPrefab").objectReferenceValue = chipPrefab;
        pileFields.FindProperty("betSpot").objectReferenceValue = betSpot.transform;
        pileFields.FindProperty("dealerTray").objectReferenceValue = tray.transform;
        pileFields.FindProperty("font").objectReferenceValue = font;
        pileFields.ApplyModifiedPropertiesWithoutUndo();

        MakeSquare(root.transform, betMaterial);
        return $"{table.name}: chip pile, bet circle and dealer tray ({stacksMoved} old chip stacks moved).";
    }

    public static Vector3 BetSpotFor(MinigameType game)
    {
        return TableSpots.BetSpotFor(game);
    }

    // The dealer's chip rack sits in the middle, right in front of the dealer, on every table
    public static Vector3 DealerTraySpotFor(MinigameType game)
    {
        return TableSpots.RackSpot;
    }

    // A thin gold outline on the table: the square the player's chips sit in
    public static void MakeSquare(Transform chipsRoot, Material material)
    {
        if (chipsRoot.Find("ChipSquare") != null)
        {
            return;
        }

        GameObject square = new GameObject("ChipSquare");
        Undo.RegisterCreatedObjectUndo(square, "Chip Square");
        square.transform.SetParent(chipsRoot, false);
        square.transform.localPosition = PileSpot + Vector3.up * 0.006f;

        const float lineWidth = 0.1f;
        float halfWidth = SquareSize.x / 2f;
        float halfDepth = SquareSize.y / 2f;

        MakeLine(square.transform, "Back", new Vector3(0f, 0f, halfDepth), new Vector3(SquareSize.x + lineWidth, 0.01f, lineWidth), material);
        MakeLine(square.transform, "Front", new Vector3(0f, 0f, -halfDepth), new Vector3(SquareSize.x + lineWidth, 0.01f, lineWidth), material);
        MakeLine(square.transform, "Left", new Vector3(-halfWidth, 0f, 0f), new Vector3(lineWidth, 0.01f, SquareSize.y), material);
        MakeLine(square.transform, "Right", new Vector3(halfWidth, 0f, 0f), new Vector3(lineWidth, 0.01f, SquareSize.y), material);
    }

    private static void MakeLine(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = name;
        Object.DestroyImmediate(line.GetComponent<Collider>());
        line.transform.SetParent(parent, false);
        line.transform.localPosition = position;
        line.transform.localScale = size;

        if (material != null)
        {
            line.GetComponent<Renderer>().sharedMaterial = material;
        }
    }

    // The old loose chips on this table become the dealer's chips, lined up on the tray
    private static int MoveOldChipsToTray(Transform arena, Transform newRoot, Transform tray)
    {
        List<Transform> stacks = new List<Transform>();

        foreach (TableChip chip in arena.GetComponentsInChildren<TableChip>(true))
        {
            if (chip.transform.IsChildOf(newRoot))
            {
                continue;
            }

            SerializedObject chipFields = new SerializedObject(chip);
            chipFields.FindProperty("belongsToDealer").boolValue = true;
            chipFields.ApplyModifiedProperties();

            // A chip inside a "ChipStack" moves with its stack
            Transform stack = chip.transform.parent != null && chip.transform.parent != arena ? chip.transform.parent : chip.transform;

            if (!stacks.Contains(stack))
            {
                stacks.Add(stack);
            }
        }

        for (int i = 0; i < stacks.Count; i++)
        {
            Undo.SetTransformParent(stacks[i], tray, "Set Up Player Chips");
            float x = (i - (stacks.Count - 1) / 2f) * TrayStackSpacing;
            stacks[i].localPosition = new Vector3(x, 0f, 0f);
        }

        return stacks.Count;
    }

    // ---------- Helpers ----------

    public static Material MakeLineMaterial()
    {
        string path = $"{MaterialFolder}/BetCircle.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            return null;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "Chips");
        }

        material = new Material(shader);
        material.SetColor("_BaseColor", new Color(0.75f, 0.6f, 0.2f));
        AssetDatabase.CreateAsset(material, path);
        return material;
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

    private static void Tell(string message)
    {
        EditorUtility.DisplayDialog("Set Up Player Chips", message, "OK");
    }
}
