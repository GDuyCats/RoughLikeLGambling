using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// A one-time editor tool: Tools > Build Table Dealers.
// It copies the existing boss body once for every table, gives each copy its own colors and lines,
// puts it behind its table, and links it to the table, the TableManager and the BossCombat.
// The old boss body is kept (hidden) and nothing else is changed.
public static class TableDealerBuilder
{
    private const string MaterialFolder = "Assets/Materials/Dealers";

    // One dealer's look and personality, chosen by the game at their table
    private struct DealerStyle
    {
        public Color BodyColor;
        public string[] Grumbles;
    }

    [MenuItem("Tools/Build Table Dealers")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Tell("Please stop Play mode first, then run Tools > Build Table Dealers again.");
            return;
        }

        if (Object.FindFirstObjectByType<TableDealer>(FindObjectsInactive.Include) != null)
        {
            Tell("The scene already has table dealers, so nothing was built.\nDelete the existing dealers first if you want to build them again.");
            return;
        }

        TableManager tableManager = Object.FindFirstObjectByType<TableManager>(FindObjectsInactive.Include);
        BossCombat bossCombat = Object.FindFirstObjectByType<BossCombat>(FindObjectsInactive.Include);

        if (tableManager == null || bossCombat == null)
        {
            Tell("Could not find the TableManager or the BossCombat in the open scene. Is the game scene open?");
            return;
        }

        SerializedObject managerFields = new SerializedObject(tableManager);
        GameObject template = managerFields.FindProperty("bossModel").objectReferenceValue as GameObject;

        // If the TableManager has no boss body, try the one the BossCombat uses
        if (template == null)
        {
            Transform combatBody = new SerializedObject(bossCombat).FindProperty("bossModel").objectReferenceValue as Transform;
            template = combatBody != null ? combatBody.gameObject : null;
        }

        if (template == null || template.transform.Find("Head") == null
            || template.transform.Find("LeftHand") == null || template.transform.Find("RightHand") == null)
        {
            Tell("Could not find the boss body to copy. The TableManager's Boss Model field should point to the BossModel with Head, LeftHand and RightHand inside it.");
            return;
        }

        List<FinalBoss> bosses = LoadBosses();
        SerializedProperty tableList = managerFields.FindProperty("tables");
        List<TableDealer> built = new List<TableDealer>();
        List<string> skipped = new List<string>();

        for (int i = 0; i < tableList.arraySize; i++)
        {
            TableStation table = tableList.GetArrayElementAtIndex(i).objectReferenceValue as TableStation;

            if (table == null)
            {
                continue;
            }

            FinalBoss boss = bosses.Find(candidate => candidate.FavoriteGame == table.Minigame);

            if (boss == null || table.BossSpot == null)
            {
                skipped.Add(table.name);
                continue;
            }

            TableDealer dealer = BuildDealer(template, table, boss);
            built.Add(dealer);

            SerializedObject tableFields = new SerializedObject(table);
            tableFields.FindProperty("dealer").objectReferenceValue = dealer;
            tableFields.ApplyModifiedProperties();
        }

        if (built.Count == 0)
        {
            Tell("No dealers were built. Each table needs a Boss Spot, and a Final Boss asset whose Favorite Game matches the table.");
            return;
        }

        // The TableManager tells the BossCombat which dealer fights
        managerFields.FindProperty("bossCombat").objectReferenceValue = bossCombat;
        managerFields.ApplyModifiedProperties();

        // Point the BossCombat at a dealer right away, so nothing is empty before the first fight
        TableDealer first = built[0];
        SerializedObject combatFields = new SerializedObject(bossCombat);
        combatFields.FindProperty("bossModel").objectReferenceValue = first.transform;
        combatFields.FindProperty("bossRenderer").objectReferenceValue = first.HeadRenderer;
        combatFields.FindProperty("head").objectReferenceValue = first.Head;
        combatFields.FindProperty("leftHand").objectReferenceValue = first.LeftHand;
        combatFields.FindProperty("rightHand").objectReferenceValue = first.RightHand;
        combatFields.ApplyModifiedProperties();

        // The old single boss body stays in the scene, hidden
        Undo.RecordObject(template, "Build Table Dealers");
        template.SetActive(false);

        EditorSceneManager.MarkSceneDirty(template.scene);
        Selection.activeGameObject = first.gameObject;

        string message = $"Built {built.Count} table dealers (should be 3).";

        if (skipped.Count > 0)
        {
            message += $"\nSkipped: {string.Join(", ", skipped)} (no Boss Spot, or no boss for that game).";
        }

        Tell(message + "\n\nPress Ctrl + S to save the scene.");
    }

    private static TableDealer BuildDealer(GameObject template, TableStation table, FinalBoss boss)
    {
        DealerStyle style = StyleFor(table.Minigame);
        string dealerName = "Dealer_" + boss.DisplayName.Replace(" ", "");

        GameObject dealerObject = Object.Instantiate(template);
        dealerObject.name = dealerName;
        dealerObject.SetActive(true);
        Undo.RegisterCreatedObjectUndo(dealerObject, "Build Table Dealers");

        // Behind the table, tidied under its arena in the Hierarchy
        dealerObject.transform.SetPositionAndRotation(table.BossSpot.position, template.transform.rotation);

        if (table.Arena != null)
        {
            dealerObject.transform.SetParent(table.Arena.transform, true);
        }

        Transform head = dealerObject.transform.Find("Head");
        Transform leftHand = dealerObject.transform.Find("LeftHand");
        Transform rightHand = dealerObject.transform.Find("RightHand");
        Renderer headRenderer = head.GetComponent<Renderer>();

        // The eyes are every renderer inside the head
        List<Renderer> eyes = new List<Renderer>();

        foreach (Renderer candidate in head.GetComponentsInChildren<Renderer>(true))
        {
            if (candidate != headRenderer)
            {
                eyes.Add(candidate);
            }
        }

        // Own materials, so each dealer has its own color and its own eyes
        Material body = SaveMaterial(headRenderer.sharedMaterial, style.BodyColor, dealerName + "_Body");
        AssignMaterial(headRenderer, body);
        AssignMaterial(leftHand.GetComponent<Renderer>(), body);
        AssignMaterial(rightHand.GetComponent<Renderer>(), body);

        if (eyes.Count > 0)
        {
            Material eyeMaterial = SaveMaterial(eyes[0].sharedMaterial, Color.white, dealerName + "_Eyes");

            foreach (Renderer eye in eyes)
            {
                AssignMaterial(eye, eyeMaterial);
            }
        }

        // The head can be hit by thrown chips
        if (head.GetComponent<Collider>() == null)
        {
            head.gameObject.AddComponent<SphereCollider>();
        }

        TMP_Text speech = CreateSpeech(dealerObject.transform);

        TableDealer dealer = dealerObject.AddComponent<TableDealer>();
        SerializedObject fields = new SerializedObject(dealer);
        fields.FindProperty("boss").objectReferenceValue = boss;
        fields.FindProperty("head").objectReferenceValue = head;
        fields.FindProperty("leftHand").objectReferenceValue = leftHand;
        fields.FindProperty("rightHand").objectReferenceValue = rightHand;
        fields.FindProperty("headRenderer").objectReferenceValue = headRenderer;
        fields.FindProperty("speech").objectReferenceValue = speech;

        SerializedProperty eyeList = fields.FindProperty("eyeRenderers");
        eyeList.arraySize = eyes.Count;

        for (int i = 0; i < eyes.Count; i++)
        {
            eyeList.GetArrayElementAtIndex(i).objectReferenceValue = eyes[i];
        }

        SerializedProperty lines = fields.FindProperty("grumbles");
        lines.arraySize = style.Grumbles.Length;

        for (int i = 0; i < style.Grumbles.Length; i++)
        {
            lines.GetArrayElementAtIndex(i).stringValue = style.Grumbles[i];
        }

        fields.ApplyModifiedPropertiesWithoutUndo();
        return dealer;
    }

    // Floating white text in front of the dealer, hidden until they grumble
    private static TMP_Text CreateSpeech(Transform dealer)
    {
        GameObject speechObject = new GameObject("Speech", typeof(RectTransform));
        speechObject.transform.SetParent(dealer, false);
        speechObject.transform.localPosition = new Vector3(0f, 3f, 2f);
        speechObject.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);

        TextMeshPro text = speechObject.AddComponent<TextMeshPro>();
        ((RectTransform)speechObject.transform).sizeDelta = new Vector2(16f, 3f);
        text.text = "Hey! Watch it!";

        TMP_FontAsset font = FindFont("Fredoka-Bold");

        if (font != null)
        {
            text.font = font;
        }

        text.enableAutoSizing = true;
        text.fontSizeMin = 3f;
        text.fontSizeMax = 8f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        speechObject.SetActive(false);
        return text;
    }

    private static DealerStyle StyleFor(MinigameType game)
    {
        switch (game)
        {
            case MinigameType.HighLow:   // The Oracle: mysterious
                return new DealerStyle
                {
                    BodyColor = new Color32(126, 87, 194, 255),
                    Grumbles = new[]
                    {
                        "I foresaw that.",
                        "The cards warned me about you.",
                        "Your future just got a little darker.",
                        "Fate remembers, little one.",
                        "I saw this coming three rounds ago."
                    }
                };

            case MinigameType.Dice:      // Dice King: pompous
                return new DealerStyle
                {
                    BodyColor = new Color32(212, 160, 23, 255),
                    Grumbles = new[]
                    {
                        "How DARE you!",
                        "Bow before the King, peasant!",
                        "That chip belongs to the crown now!",
                        "Off with your... chips!",
                        "Do you know who I am?!"
                    }
                };

            default:                     // Card Shark: sly
                return new DealerStyle
                {
                    BodyColor = new Color32(30, 60, 130, 255),
                    Grumbles = new[]
                    {
                        "Careful, kid. I count every chip.",
                        "Throwing money at me? I'll take it.",
                        "Cute trick. Mine are better.",
                        "Watch the suit, pal.",
                        "That's coming out of your winnings."
                    }
                };
        }
    }

    // ---------- Helpers ----------

    private static List<FinalBoss> LoadBosses()
    {
        List<FinalBoss> bosses = new List<FinalBoss>();

        foreach (string guid in AssetDatabase.FindAssets("t:FinalBoss"))
        {
            FinalBoss boss = AssetDatabase.LoadAssetAtPath<FinalBoss>(AssetDatabase.GUIDToAssetPath(guid));

            if (boss != null)
            {
                bosses.Add(boss);
            }
        }

        return bosses;
    }

    // A copy of the original material with a new color, saved as an asset so it stays in the scene
    private static Material SaveMaterial(Material original, Color color, string name)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "Dealers");
        }

        Material material = original != null ? new Material(original) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color;

        string path = AssetDatabase.GenerateUniqueAssetPath($"{MaterialFolder}/{name}.mat");
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void AssignMaterial(Renderer renderer, Material material)
    {
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
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

    private static void Tell(string message)
    {
        EditorUtility.DisplayDialog("Build Table Dealers", message, "OK");
    }
}
