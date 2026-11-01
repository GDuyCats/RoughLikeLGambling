using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A one-time editor tool: Tools > Restyle Table Buttons.
// 1. In the TableButton prefab (so every table changes): the button gets 20% smaller,
//    and its label lies flat on top of the dome instead of floating above it.
// (Where the buttons sit on each table: see Tools > Rearrange Tables.)
// Safe to run again: anything already done is skipped.
public static class TableButtonRestyler
{
    private const string PrefabPath = "Assets/Prefabs/TableButton.prefab";
    private const float ButtonScale = 0.8f;

    [MenuItem("Tools/Restyle Table Buttons")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Tell("Please stop Play mode first, then run Tools > Restyle Table Buttons again.");
            return;
        }

        List<string> report = new List<string>();

        RestylePrefab(report);
        // Where the buttons sit on each table is now done by Tools > Rearrange Tables

        Tell(string.Join("\n", report) + "\n\nPress Ctrl + S to save the scene.");
    }

    // ---------- The prefab ----------

    private static void RestylePrefab(List<string> report)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

        if (root == null)
        {
            report.Add($"Could not open {PrefabPath}.");
            return;
        }

        try
        {
            TableButton button = root.GetComponent<TableButton>();
            SerializedObject fields = new SerializedObject(button);
            TMP_Text label = fields.FindProperty("label").objectReferenceValue as TMP_Text;
            Transform cap = fields.FindProperty("cap").objectReferenceValue as Transform;

            if (label == null || cap == null)
            {
                report.Add("The TableButton prefab has no Label or Cap, so it was not changed.");
                return;
            }

            // Already flat on the dome: this was done before. Only make sure the label still has its font material.
            if (Vector3.Angle(label.transform.forward, Vector3.down) < 5f)
            {
                if (RepairLabelMaterial(label))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    report.Add("The table buttons were already restyled. Their label material was repaired.");
                }
                else
                {
                    report.Add("The table buttons were already restyled.");
                }

                return;
            }

            // 20% smaller. Where the chibi lands on top shrinks with it.
            root.transform.localScale = Vector3.one * ButtonScale;
            SerializedProperty standHeight = fields.FindProperty("standHeight");
            float domeTop = standHeight.floatValue;   // In the prefab's own size
            standHeight.floatValue = domeTop * ButtonScale;
            fields.ApplyModifiedPropertiesWithoutUndo();

            // The label lies flat on top of the dome, readable from the player's side
            RectTransform labelRect = label.rectTransform;
            labelRect.localPosition = new Vector3(0f, domeTop + 0.02f, 0f);
            labelRect.localRotation = Quaternion.Euler(90f, 0f, 0f);
            labelRect.sizeDelta = new Vector2(1.9f, 1.7f);

            label.enableAutoSizing = true;
            label.fontSizeMin = 0.3f;
            label.fontSizeMax = 4f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            RepairLabelMaterial(label);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            report.Add("Table buttons: 20% smaller, labels printed on top of the dome (all tables).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // The label must use its font's own material (an earlier version of this tool left it empty).
    // Returns true if something was fixed.
    private static bool RepairLabelMaterial(TMP_Text label)
    {
        if (label.font == null || label.fontSharedMaterial == label.font.material)
        {
            return false;
        }

        label.fontSharedMaterial = label.font.material;
        label.GetComponent<Renderer>().sharedMaterial = label.font.material;
        return true;
    }

    private static void Tell(string message)
    {
        EditorUtility.DisplayDialog("Restyle Table Buttons", message, "OK");
    }
}
