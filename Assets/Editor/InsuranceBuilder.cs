using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// A one-time editor tool: Tools > Add Insurance Buttons.
// 1. Copies the HIT and STAND buttons (screen button + table button) into INSURANCE and NO THANKS.
//    They sit in the same spots: HIT and STAND are hidden while insurance is offered, so nothing overlaps.
// 2. Creates the "Insured" upgrade (insurance pays 3 to 1) and adds it to the upgrade pool.
// 3. Fixes the old "Double After Split" upgrade file so it is "Blackjack After Split".
// Safe to run again: anything already done is skipped.
public static class InsuranceBuilder
{
    private const string UpgradeFolder = "Assets/Upgrades";

    [MenuItem("Tools/Add Insurance Buttons")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Tell("Please stop Play mode first, then run Tools > Add Insurance Buttons again.");
            return;
        }

        BlackjackGame game = Object.FindFirstObjectByType<BlackjackGame>(FindObjectsInactive.Include);
        RunManager run = Object.FindFirstObjectByType<RunManager>(FindObjectsInactive.Include);

        if (game == null || run == null)
        {
            Tell("Could not find the BlackjackGame or the RunManager in the open scene. Is the game scene open?");
            return;
        }

        List<string> report = new List<string>();
        List<string> problems = new List<string>();

        // 1. The buttons
        SerializedObject gameFields = new SerializedObject(game);

        if (gameFields.FindProperty("insuranceButton").objectReferenceValue != null)
        {
            report.Add("The insurance buttons were already there.");
        }
        else
        {
            Button hit = gameFields.FindProperty("hitButton").objectReferenceValue as Button;
            Button stand = gameFields.FindProperty("standButton").objectReferenceValue as Button;

            if (hit == null || stand == null)
            {
                problems.Add("The BlackjackGame has no Hit or Stand button, so the insurance buttons were not made.");
            }
            else
            {
                Button insurance = CopyButton(hit, "InsuranceButton", "INSURANCE", report, problems);
                Button noThanks = CopyButton(stand, "NoInsuranceButton", "NO THANKS", report, problems);

                gameFields.FindProperty("insuranceButton").objectReferenceValue = insurance;
                gameFields.FindProperty("noInsuranceButton").objectReferenceValue = noThanks;
                gameFields.ApplyModifiedProperties();
            }
        }

        // 2. The "Insured" upgrade
        Upgrade insured = MakeInsuredUpgrade(report);
        AddToPool(run, insured, report);

        // 3. The old split upgrade file
        FixSplitUpgrade(run, report);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);

        string message = string.Join("\n", report);

        if (problems.Count > 0)
        {
            message += "\n\nPROBLEMS:\n" + string.Join("\n", problems);
        }

        Tell(message + "\n\nPress Ctrl + S to save the scene.");
    }

    // ---------- Buttons ----------

    // Copies a screen button and the table button that stands in for it
    private static Button CopyButton(Button original, string name, string label, List<string> report, List<string> problems)
    {
        GameObject copy = Object.Instantiate(original.gameObject, original.transform.parent);
        copy.name = name;
        copy.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
        Undo.RegisterCreatedObjectUndo(copy, "Add Insurance Buttons");

        Button button = copy.GetComponent<Button>();

        // Start clean: the BlackjackGame adds the click action in code
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        }

        TMP_Text text = copy.GetComponentInChildren<TMP_Text>(true);

        if (text != null)
        {
            text.text = label;
        }

        copy.SetActive(false);   // The BlackjackGame shows it only while insurance is offered

        // The matching button on the table
        TableButton tableOriginal = FindTableButtonFor(original);

        if (tableOriginal == null)
        {
            problems.Add($"No table button stands in for {original.name}, so {name} only exists on the screen.");
            return button;
        }

        GameObject tableCopy = Object.Instantiate(tableOriginal.gameObject, tableOriginal.transform.parent);
        tableCopy.name = "Table" + name;
        tableCopy.transform.SetSiblingIndex(tableOriginal.transform.GetSiblingIndex() + 1);
        Undo.RegisterCreatedObjectUndo(tableCopy, "Add Insurance Buttons");

        SerializedObject tableFields = new SerializedObject(tableCopy.GetComponent<TableButton>());
        tableFields.FindProperty("screenButton").objectReferenceValue = button;
        tableFields.ApplyModifiedProperties();

        report.Add($"Made {name} (screen and table), in the same spot as {original.name}.");
        return button;
    }

    private static TableButton FindTableButtonFor(Button screenButton)
    {
        foreach (TableButton tableButton in Object.FindObjectsByType<TableButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (new SerializedObject(tableButton).FindProperty("screenButton").objectReferenceValue == screenButton)
            {
                return tableButton;
            }
        }

        return null;
    }

    // ---------- Upgrades ----------

    private static Upgrade MakeInsuredUpgrade(List<string> report)
    {
        string path = $"{UpgradeFolder}/Insured.asset";
        Upgrade upgrade = AssetDatabase.LoadAssetAtPath<Upgrade>(path);

        if (upgrade != null)
        {
            report.Add("The Insured upgrade was already there.");
            return upgrade;
        }

        if (!AssetDatabase.IsValidFolder(UpgradeFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Upgrades");
        }

        upgrade = ScriptableObject.CreateInstance<Upgrade>();
        AssetDatabase.CreateAsset(upgrade, path);

        SerializedObject fields = new SerializedObject(upgrade);
        fields.FindProperty("displayName").stringValue = "Insured";
        fields.FindProperty("description").stringValue = "Blackjack: insurance pays 3 to 1 instead of 2 to 1";
        fields.FindProperty("type").enumValueIndex = (int)UpgradeType.InsurancePayout;
        fields.FindProperty("value").intValue = 1;
        fields.FindProperty("rarity").enumValueIndex = (int)UpgradeRarity.Rare;
        fields.FindProperty("diminishingStacks").boolValue = false;
        fields.FindProperty("requiresPerfectParry").boolValue = false;
        fields.FindProperty("onlyOnce").boolValue = true;
        fields.ApplyModifiedPropertiesWithoutUndo();

        report.Add($"Created the Insured upgrade ({path}).");
        return upgrade;
    }

    private static void AddToPool(RunManager run, Upgrade upgrade, List<string> report)
    {
        SerializedObject runFields = new SerializedObject(run);
        SerializedProperty pool = runFields.FindProperty("upgradePool");

        for (int i = 0; i < pool.arraySize; i++)
        {
            if (pool.GetArrayElementAtIndex(i).objectReferenceValue == upgrade)
            {
                return;
            }
        }

        pool.arraySize++;
        pool.GetArrayElementAtIndex(pool.arraySize - 1).objectReferenceValue = upgrade;
        runFields.ApplyModifiedProperties();
        report.Add($"Added {upgrade.DisplayName} to the RunManager's upgrade pool.");
    }

    // The old "Double After Split" file still has the old, unused effect: turn it into Blackjack After Split
    private static void FixSplitUpgrade(RunManager run, List<string> report)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Upgrade"))
        {
            Upgrade upgrade = AssetDatabase.LoadAssetAtPath<Upgrade>(AssetDatabase.GUIDToAssetPath(guid));

            if (upgrade == null || upgrade.Type != UpgradeType.DoubleAfterSplit)
            {
                continue;
            }

            SerializedObject fields = new SerializedObject(upgrade);
            fields.FindProperty("displayName").stringValue = "Blackjack After Split";
            fields.FindProperty("description").stringValue = "Blackjack: two cards making 21 after a split pay as a blackjack";
            fields.FindProperty("type").enumValueIndex = (int)UpgradeType.BlackjackAfterSplit;
            fields.FindProperty("onlyOnce").boolValue = true;
            fields.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(upgrade);

            AddToPool(run, upgrade, report);
            report.Add($"Fixed {upgrade.name}: it is now Blackjack After Split.");
        }
    }

    private static void Tell(string message)
    {
        EditorUtility.DisplayDialog("Add Insurance Buttons", message, "OK");
    }
}
