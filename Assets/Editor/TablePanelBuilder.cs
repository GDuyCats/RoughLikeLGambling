using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

// Used by Tools > Rearrange Tables. On every table it builds the chibi's two control panels,
// one on each side of the chip square:
// - left, the ACTION panel: the minigame's own buttons (Hit, Stand, Deal...), smaller, in a 3 x 2 grid,
// - right, the BET panel: +1, +5, +25, +100, CLEAR and ALL IN, with the bet printed on the felt above it.
// Safe to run again: the bet buttons are only made once, and everything is put back in the same places.
public static class TablePanelBuilder
{
    private const string MaterialFolder = "Assets/Materials/Buttons";
    private const float PanelButtonScale = 0.6f;   // The table buttons were 0.8

    // Spots in the arena's own space (x = left/right, z = toward the dealer), see TableSpots. Filled in this order:
    // the action panel from the far left, the bet panel from the middle out.
    private static readonly Vector2[] ActionSlots = Slots(-1f, true);
    private static readonly Vector2[] BetSlots = Slots(1f, false);

    private static Vector3 BetBoardSpot => TableSpots.BetBoardSpot;
    private static Vector2 PlateSize => TableSpots.PlateSize;
    private static float PlateZ => TableSpots.PlateZ;

    private static Vector2[] Slots(float side, bool outsideFirst)
    {
        Vector2[] slots = new Vector2[6];
        float[] columns = TableSpots.PanelColumns;

        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                float x = columns[outsideFirst ? 2 - column : column] * side;
                slots[row * 3 + column] = new Vector2(x, TableSpots.PanelRows[row]);
            }
        }

        return slots;
    }

    // The order of the action buttons (Blackjack: Hit, Stand, Deal on the first row)
    private static readonly string[] ActionOrder =
    {
        "Hit", "Stand", "Deal", "Surrender", "Split", "Double",
        "Higher", "Lower", "CashOut", "Tie",
        "Under", "Seven", "Over"
    };

    // The bet buttons: label, amount (0 = CLEAR, -1 = ALL IN), cap color
    private static readonly (string Label, int Amount, Color Color)[] BetButtons =
    {
        ("+1", 1, new Color(0.92f, 0.92f, 0.92f)),
        ("+5", 5, new Color(0.85f, 0.15f, 0.15f)),
        ("+25", 25, new Color(0.15f, 0.65f, 0.25f)),
        ("+100", 100, new Color(0.12f, 0.12f, 0.14f)),
        ("CLEAR", 0, new Color(0.45f, 0.45f, 0.5f)),
        ("ALL IN", -1, new Color(0.95f, 0.7f, 0.1f))
    };

    // Every action button gets a color that says what it does (green = go on, red = stop, gold = start...)
    private static readonly (string Name, Color Color)[] ActionColors =
    {
        ("Hit", new Color(0.2f, 0.7f, 0.3f)),
        ("Stand", new Color(0.85f, 0.2f, 0.2f)),
        ("Deal", new Color(1f, 0.6f, 0.1f)),
        ("Double", new Color(0.2f, 0.45f, 0.9f)),
        ("Split", new Color(0.6f, 0.3f, 0.85f)),
        ("Surrender", new Color(0.5f, 0.5f, 0.55f)),
        ("NoInsurance", new Color(0.5f, 0.5f, 0.55f)),
        ("Insurance", new Color(0.1f, 0.65f, 0.7f)),
        ("Higher", new Color(0.2f, 0.7f, 0.3f)),
        ("Lower", new Color(0.85f, 0.2f, 0.2f)),
        ("CashOut", new Color(1f, 0.75f, 0.15f)),
        ("Tie", new Color(0.6f, 0.3f, 0.85f)),
        ("Under", new Color(0.2f, 0.45f, 0.9f)),
        ("Seven", new Color(1f, 0.75f, 0.15f)),
        ("Over", new Color(0.85f, 0.2f, 0.2f))
    };

    private const float GlowStrength = 0.3f;   // How much each cap glows in its own color (low, so the color stays clear)
    private static readonly Color PlateColor = new Color(0.05f, 0.07f, 0.09f);

    public static string Arrange(TableStation table, RunManager run, TMP_FontAsset font)
    {
        Transform arena = table.Arena.transform;
        GameObject minigamePanel = new SerializedObject(table).FindProperty("minigamePanel").objectReferenceValue as GameObject;
        string cashOutReport = table.Minigame == MinigameType.HighLow ? EnsureCashOutButton(arena) : "";

        // The minigame's own buttons, grouped by spot (Insurance shares Hit's spot, No Thanks shares Stand's)
        List<List<TableButton>> groups = GroupActionButtons(arena);
        groups.Sort((a, b) => GroupOrder(a).CompareTo(GroupOrder(b)));

        for (int i = 0; i < groups.Count; i++)
        {
            Vector2 slot = i < ActionSlots.Length
                ? ActionSlots[i]
                : new Vector2(-TableSpots.PanelColumns[2] - 2f, TableSpots.PanelRows[0] - 1.9f * (i - ActionSlots.Length));   // More than 6: an extra column on the far left

            foreach (TableButton button in groups[i])
            {
                PlaceButton(arena, button, slot);
                ColorActionButton(button);
            }
        }

        // The bet panel
        string betReport = "";

        if (minigamePanel == null || groups.Count == 0)
        {
            betReport = " No bet panel (no minigame panel or buttons found).";
        }
        else
        {
            betReport = BuildBetPanel(arena, minigamePanel, run, groups[0][0], font);
        }

        MakePlate(arena, "ActionPlate", -TableSpots.PanelCenter);
        MakePlate(arena, "BetPlate", TableSpots.PanelCenter);
        string timerReport = RemoveDecisionTimer(table, arena);

        return $"{table.name}: {groups.Count} action buttons in the left panel.{cashOutReport}{betReport}{timerReport}";
    }

    // ---------- Action buttons ----------

    private static List<List<TableButton>> GroupActionButtons(Transform arena)
    {
        List<List<TableButton>> groups = new List<List<TableButton>>();
        List<Vector2> spots = new List<Vector2>();

        foreach (TableButton button in arena.GetComponentsInChildren<TableButton>(true))
        {
            if (button.name.StartsWith("TableBet"))
            {
                continue;
            }

            Vector3 local = arena.InverseTransformPoint(button.transform.position);
            Vector2 spot = new Vector2(local.x, local.z);
            int index = spots.FindIndex(s => Vector2.Distance(s, spot) < 0.1f);

            if (index < 0)
            {
                spots.Add(spot);
                groups.Add(new List<TableButton>());
                index = groups.Count - 1;
            }

            groups[index].Add(button);
        }

        return groups;
    }

    private static int GroupOrder(List<TableButton> group)
    {
        int best = ActionOrder.Length;

        foreach (TableButton button in group)
        {
            string name = ScreenButtonName(button);

            for (int i = 0; i < ActionOrder.Length; i++)
            {
                if (name.StartsWith(ActionOrder[i]) && i < best)
                {
                    best = i;
                }
            }
        }

        return best;
    }

    private static void ColorActionButton(TableButton button)
    {
        string name = ScreenButtonName(button);

        foreach (var entry in ActionColors)
        {
            if (name.StartsWith(entry.Name))
            {
                SetCapColor(button, entry.Color, $"Action{entry.Name}");
                return;
            }
        }
    }

    private static string ScreenButtonName(TableButton button)
    {
        Button screen = new SerializedObject(button).FindProperty("screenButton").objectReferenceValue as Button;
        return screen != null ? screen.name : button.name;
    }

    // Puts a button on its spot and makes it the panel size (the chibi lands lower on a smaller button)
    private static void PlaceButton(Transform arena, TableButton button, Vector2 slot)
    {
        Transform t = button.transform;
        Undo.RecordObject(t, "Rearrange Tables");

        Vector3 local = arena.InverseTransformPoint(t.position);
        t.position = arena.TransformPoint(new Vector3(slot.x, local.y, slot.y));

        float current = t.lossyScale.x;

        if (current > 0.001f && Mathf.Abs(current - PanelButtonScale) > 0.001f)
        {
            float factor = PanelButtonScale / current;
            t.localScale *= factor;

            SerializedObject fields = new SerializedObject(button);
            SerializedProperty standHeight = fields.FindProperty("standHeight");
            standHeight.floatValue *= factor;
            fields.ApplyModifiedProperties();
        }
    }

    // High-Low is a ladder now: it needs a CASH OUT button (a copy of TIE, screen and table)
    private static string EnsureCashOutButton(Transform arena)
    {
        HighLowGame game = Object.FindFirstObjectByType<HighLowGame>(FindObjectsInactive.Include);

        if (game == null)
        {
            return " No High-Low game found for the CASH OUT button.";
        }

        SerializedObject gameFields = new SerializedObject(game);

        if (gameFields.FindProperty("cashOutButton").objectReferenceValue != null)
        {
            return "";
        }

        Button tie = gameFields.FindProperty("tieButton").objectReferenceValue as Button;
        TableButton tieOnTable = tie != null ? FindTableButtonFor(arena, tie) : null;

        if (tie == null || tieOnTable == null)
        {
            return " Could not copy the TIE button for CASH OUT.";
        }

        Button cashOut = CopyScreenButton(tie, "CashOutButton", "CASH OUT");
        TableButton cashOutOnTable = CopyTableButton(tieOnTable, cashOut, "TableCashOut");

        // Off to the side for now, so it gets its own place in the panel
        cashOutOnTable.transform.position += Vector3.right * 3f;

        gameFields.FindProperty("cashOutButton").objectReferenceValue = cashOut;
        gameFields.ApplyModifiedProperties();
        return " CASH OUT button added.";
    }

    // ---------- Bet panel ----------

    private static string BuildBetPanel(Transform arena, GameObject minigamePanel, RunManager run, TableButton template, TMP_FontAsset font)
    {
        BetPanel panel = minigamePanel.GetComponent<BetPanel>();

        if (panel == null)
        {
            panel = Undo.AddComponent<BetPanel>(minigamePanel);
        }

        SerializedObject panelFields = new SerializedObject(panel);
        panelFields.FindProperty("run").objectReferenceValue = run;
        bool made = panelFields.FindProperty("clearButton").objectReferenceValue == null;

        Button templateScreen = new SerializedObject(template).FindProperty("screenButton").objectReferenceValue as Button;

        if (templateScreen == null)
        {
            return " No bet panel (the first button has no screen button to copy).";
        }

        SerializedProperty addButtons = panelFields.FindProperty("addButtons");
        SerializedProperty addAmounts = panelFields.FindProperty("addAmounts");
        addButtons.arraySize = 4;
        addAmounts.arraySize = 4;

        for (int i = 0; i < BetButtons.Length; i++)
        {
            var info = BetButtons[i];
            string id = info.Amount > 0 ? $"Plus{info.Amount}" : info.Amount == 0 ? "Clear" : "AllIn";

            // The screen button (invisible, like the minigame's own) and its table button
            Button screen = FindScreenButton(minigamePanel, $"Bet{id}Button");

            if (screen == null)
            {
                screen = CopyScreenButton(templateScreen, $"Bet{id}Button", info.Label);
            }

            TableButton tableButton = FindTableButtonFor(arena, screen);

            if (tableButton == null)
            {
                tableButton = CopyTableButton(template, screen, $"TableBet{id}");
            }

            PlaceButton(arena, tableButton, BetSlots[i]);
            SetCapColor(tableButton, info.Color, $"Bet{id}");

            if (info.Amount > 0)
            {
                addButtons.GetArrayElementAtIndex(i).objectReferenceValue = screen;
                addAmounts.GetArrayElementAtIndex(i).intValue = info.Amount;
            }
            else if (info.Amount == 0)
            {
                panelFields.FindProperty("clearButton").objectReferenceValue = screen;
            }
            else
            {
                panelFields.FindProperty("allInButton").objectReferenceValue = screen;
            }
        }

        panelFields.FindProperty("betBoard").objectReferenceValue = MakeBetBoard(arena, font);
        panelFields.ApplyModifiedProperties();

        return made ? " Bet panel made on the right." : " Bet panel put back in place.";
    }

    private static Button FindScreenButton(GameObject minigamePanel, string name)
    {
        foreach (Button button in minigamePanel.GetComponentsInChildren<Button>(true))
        {
            if (button.name == name)
            {
                return button;
            }
        }

        return null;
    }

    private static Button CopyScreenButton(Button original, string name, string label)
    {
        GameObject copy = Object.Instantiate(original.gameObject, original.transform.parent);
        copy.name = name;
        copy.SetActive(true);   // The bet buttons are always there while the table is open
        Undo.RegisterCreatedObjectUndo(copy, "Rearrange Tables");

        Button button = copy.GetComponent<Button>();

        // Start clean: the BetPanel adds the click action in code
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        }

        button.interactable = true;
        TMP_Text text = copy.GetComponentInChildren<TMP_Text>(true);

        if (text != null)
        {
            text.text = label;
        }

        return button;
    }

    private static TableButton CopyTableButton(TableButton original, Button screen, string name)
    {
        GameObject copy = Object.Instantiate(original.gameObject, original.transform.parent);
        copy.name = name;
        Undo.RegisterCreatedObjectUndo(copy, "Rearrange Tables");

        TableButton button = copy.GetComponent<TableButton>();
        SerializedObject fields = new SerializedObject(button);
        fields.FindProperty("screenButton").objectReferenceValue = screen;
        fields.ApplyModifiedProperties();
        return button;
    }

    private static TableButton FindTableButtonFor(Transform arena, Button screen)
    {
        foreach (TableButton button in arena.GetComponentsInChildren<TableButton>(true))
        {
            if (new SerializedObject(button).FindProperty("screenButton").objectReferenceValue == screen)
            {
                return button;
            }
        }

        return null;
    }

    // "BET 35 / MAX 100" lying on the felt above the bet panel
    private static TMP_Text MakeBetBoard(Transform arena, TMP_FontAsset font)
    {
        Transform existing = arena.Find("BetBoard");
        GameObject boardObject;

        if (existing != null)
        {
            boardObject = existing.gameObject;
        }
        else
        {
            boardObject = new GameObject("BetBoard", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(boardObject, "Rearrange Tables");
            boardObject.transform.SetParent(arena, false);
            boardObject.AddComponent<TextMeshPro>();
        }

        boardObject.transform.position = arena.TransformPoint(BetBoardSpot);
        boardObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // Flat on the felt, readable from the player's side

        TextMeshPro text = boardObject.GetComponent<TextMeshPro>();

        if (font != null)
        {
            text.font = font;
        }

        text.rectTransform.sizeDelta = new Vector2(6f, 1.6f);
        text.fontSize = 6f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = Color.white;
        text.text = "BET 10\n<size=60%>MAX 100</size>";
        return text;
    }

    // The countdown ring is not used any more (8 decks make counting cards pointless): remove it from the table
    private static string RemoveDecisionTimer(TableStation table, Transform arena)
    {
        TableMaterials.RemoveOld(arena, "DecisionTimer");

        MonoBehaviour game = table.Minigame switch
        {
            MinigameType.HighLow => (MonoBehaviour)Object.FindFirstObjectByType<HighLowGame>(FindObjectsInactive.Include),
            MinigameType.Dice => Object.FindFirstObjectByType<DiceGame>(FindObjectsInactive.Include),
            _ => Object.FindFirstObjectByType<BlackjackGame>(FindObjectsInactive.Include)
        };

        if (game != null)
        {
            SerializedObject gameFields = new SerializedObject(game);
            gameFields.FindProperty("decisionTimer").objectReferenceValue = null;
            gameFields.ApplyModifiedProperties();
        }

        return " No time limit.";
    }

    // (Not used now.) The countdown ring above the action panel, linked to this table's minigame
    private static string MakeDecisionTimer(TableStation table, Transform arena, TMP_FontAsset font)
    {
        Transform existing = arena.Find("DecisionTimer");
        GameObject timerObject = existing != null ? existing.gameObject : new GameObject("DecisionTimer");

        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(timerObject, "Rearrange Tables");
            timerObject.transform.SetParent(arena, false);
        }

        timerObject.transform.localPosition = TableSpots.TimerSpot;
        timerObject.transform.localRotation = Quaternion.identity;

        DecisionTimer timer = timerObject.GetComponent<DecisionTimer>();

        if (timer == null)
        {
            timer = Undo.AddComponent<DecisionTimer>(timerObject);
        }

        SerializedObject timerFields = new SerializedObject(timer);
        timerFields.FindProperty("font").objectReferenceValue = font;
        timerFields.FindProperty("seeThroughMaterial").objectReferenceValue = TableMaterials.FrameLine;
        timerFields.ApplyModifiedProperties();

        MonoBehaviour game = table.Minigame switch
        {
            MinigameType.HighLow => (MonoBehaviour)Object.FindFirstObjectByType<HighLowGame>(FindObjectsInactive.Include),
            MinigameType.Dice => Object.FindFirstObjectByType<DiceGame>(FindObjectsInactive.Include),
            _ => Object.FindFirstObjectByType<BlackjackGame>(FindObjectsInactive.Include)
        };

        if (game == null)
        {
            return " No minigame found for the timer.";
        }

        SerializedObject gameFields = new SerializedObject(game);
        gameFields.FindProperty("decisionTimer").objectReferenceValue = timer;
        gameFields.ApplyModifiedProperties();

        float seconds = gameFields.FindProperty("decisionSeconds").floatValue;
        return $" Timer: {seconds:0} s.";
    }

    // A dark plate under each panel, so the buttons read as one group
    private static void MakePlate(Transform arena, string name, float x)
    {
        Transform plate = arena.Find(name);

        if (plate == null)
        {
            GameObject plateObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plateObject.name = name;
            Object.DestroyImmediate(plateObject.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(plateObject, "Rearrange Tables");
            plateObject.transform.SetParent(arena, false);
            plate = plateObject.transform;
        }

        plate.localPosition = new Vector3(x, 0.004f, PlateZ);
        plate.localRotation = Quaternion.identity;
        plate.localScale = new Vector3(PlateSize.x, 0.008f, PlateSize.y);
        plate.GetComponent<Renderer>().sharedMaterial = UnlitMaterial("PanelPlate", PlateColor);
        MakePlateBorder(arena, name + "Border", x);
    }

    // A thin gold frame around the plate, like the chip square
    private static void MakePlateBorder(Transform arena, string name, float x)
    {
        Transform old = arena.Find(name);

        if (old != null)
        {
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        GameObject border = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(border, "Rearrange Tables");
        border.transform.SetParent(arena, false);
        border.transform.localPosition = new Vector3(x, 0.012f, PlateZ);

        Material gold = PlayerChipsBuilder.MakeLineMaterial();
        const float width = 0.1f;
        float halfX = PlateSize.x / 2f;
        float halfZ = PlateSize.y / 2f;

        MakeLine(border.transform, new Vector3(0f, 0f, halfZ), new Vector3(PlateSize.x + width, 0.01f, width), gold);
        MakeLine(border.transform, new Vector3(0f, 0f, -halfZ), new Vector3(PlateSize.x + width, 0.01f, width), gold);
        MakeLine(border.transform, new Vector3(-halfX, 0f, 0f), new Vector3(width, 0.01f, PlateSize.y), gold);
        MakeLine(border.transform, new Vector3(halfX, 0f, 0f), new Vector3(width, 0.01f, PlateSize.y), gold);
    }

    private static void MakeLine(Transform parent, Vector3 position, Vector3 size, Material material)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(line.GetComponent<Collider>());
        line.transform.SetParent(parent, false);
        line.transform.localPosition = position;
        line.transform.localScale = size;

        if (material != null)
        {
            line.GetComponent<Renderer>().sharedMaterial = material;
        }
    }

    // ---------- Materials ----------

    // Gives one button its own cap color (a copy of its cap material, saved in Assets/Materials/Buttons).
    // The glow (emission) is the same color, kept low: the old purple glow made every button look purple.
    private static void SetCapColor(TableButton button, Color color, string materialName)
    {
        Renderer cap = new SerializedObject(button).FindProperty("capRenderer").objectReferenceValue as Renderer;

        if (cap == null || cap.sharedMaterial == null)
        {
            return;
        }

        string path = $"{MaterialFolder}/{materialName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            EnsureFolder();
            material = new Material(cap.sharedMaterial);
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;

        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", color * GlowStrength);
            material.EnableKeyword("_EMISSION");
        }

        EditorUtility.SetDirty(material);

        Undo.RecordObject(cap, "Rearrange Tables");
        cap.sharedMaterial = material;
    }

    public static Material UnlitMaterial(string materialName, Color color)
    {
        string path = $"{MaterialFolder}/{materialName}.mat";
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

        EnsureFolder();
        material = new Material(shader);
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "Buttons");
        }
    }
}
