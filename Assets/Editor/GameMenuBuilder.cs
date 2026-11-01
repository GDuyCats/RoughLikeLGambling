using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// A one-time editor tool: Tools > Build Game Menu.
// It builds the main menu, pause menu, settings panel and the circling menu camera,
// then fills in every field of the GameMenu component. It only adds new objects; it changes nothing that already exists.
public static class GameMenuBuilder
{
    private const string MenuCanvasName = "MenuCanvas";
    private const string GameTitle = "All I Have To Do Is Gambling";

    private static readonly Color Gold = new Color32(255, 213, 79, 255);
    private static readonly Color LightButton = new Color32(240, 240, 240, 255);
    private static readonly Color DarkText = new Color32(40, 40, 40, 255);
    private static readonly Color SoftWhite = new Color32(220, 220, 220, 255);

    private static TMP_FontAsset boldFont;
    private static TMP_FontAsset mediumFont;
    private static UnityEngine.UI.DefaultControls.Resources uiResources;
    private static TMP_DefaultControls.Resources tmpResources;

    [MenuItem("Tools/Build Game Menu")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Tell("Please stop Play mode first, then run Tools > Build Game Menu again.");
            return;
        }

        if (GameObject.Find(MenuCanvasName) != null)
        {
            Tell("A MenuCanvas already exists in the scene, so nothing was built.\nDelete it first if you want to build the menu again.");
            return;
        }

        // ---------- Find what the menu needs in the scene ----------

        RunManager run = Object.FindFirstObjectByType<RunManager>(FindObjectsInactive.Include);
        PlayerController player = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        TableManager tableManager = Object.FindFirstObjectByType<TableManager>(FindObjectsInactive.Include);

        if (run == null || player == null || tableManager == null)
        {
            Tell("Could not find RunManager, PlayerController or TableManager in the open scene. Is the game scene open?");
            return;
        }

        Canvas gameplayCanvas = FindGameplayCanvas(run);

        if (gameplayCanvas == null)
        {
            Tell("Could not find the game's main Canvas (the one holding the Message Text of the Run Manager).");
            return;
        }

        GameObject[] minigamePanels = FindMinigamePanels();
        LoadResources();
        EnsureEventSystem();

        // ---------- Menu canvas ----------

        GameObject canvasObject = new GameObject(MenuCanvasName, typeof(RectTransform));
        canvasObject.layer = UiLayer();

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;   // Always on top of the game screen

        UnityEngine.UI.CanvasScaler scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // ---------- Main menu ----------

        GameObject mainMenuPanel = CreatePanel(canvasObject.transform, "MainMenuPanel", 0.6f);

        TextMeshProUGUI title = CreateText(mainMenuPanel.transform, "TitleText", GameTitle, boldFont, 96f, Gold,
            new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1600f, 200f));
        title.enableAutoSizing = true;
        title.fontSizeMin = 40f;
        title.fontSizeMax = 96f;

        RectTransform mainColumn = CreateColumn(mainMenuPanel.transform, "ButtonColumn", new Vector2(0f, -40f), new Vector2(420f, 318f));
        UnityEngine.UI.Button playButton = CreateButton(mainColumn, "PlayButton", "PLAY", Gold);
        UnityEngine.UI.Button settingsButton = CreateButton(mainColumn, "SettingsButton", "SETTINGS", LightButton);
        UnityEngine.UI.Button quitButton = CreateButton(mainColumn, "QuitButton", "QUIT", LightButton);

        TextMeshProUGUI recordsText = CreateText(mainMenuPanel.transform, "RecordsText", "No records yet. Good luck!", mediumFont, 32f, SoftWhite,
            new Vector2(0.5f, 0f), new Vector2(0f, 80f), new Vector2(1500f, 60f));

        // ---------- Pause menu ----------

        GameObject pausePanel = CreatePanel(canvasObject.transform, "PausePanel", 0.7f);

        CreateText(pausePanel.transform, "PauseTitle", "PAUSED", boldFont, 90f, Gold,
            new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(800f, 140f));

        RectTransform pauseColumn = CreateColumn(pausePanel.transform, "ButtonColumn", new Vector2(0f, -60f), new Vector2(420f, 432f));
        UnityEngine.UI.Button resumeButton = CreateButton(pauseColumn, "ResumeButton", "RESUME", Gold);
        UnityEngine.UI.Button pauseSettingsButton = CreateButton(pauseColumn, "SettingsButton", "SETTINGS", LightButton);
        UnityEngine.UI.Button mainMenuButton = CreateButton(pauseColumn, "MainMenuButton", "MAIN MENU", LightButton);
        UnityEngine.UI.Button pauseQuitButton = CreateButton(pauseColumn, "QuitButton", "QUIT", LightButton);

        // ---------- Settings ----------

        GameObject settingsPanel = CreatePanel(canvasObject.transform, "SettingsPanel", 0.85f);

        CreateText(settingsPanel.transform, "SettingsTitle", "SETTINGS", boldFont, 80f, Gold,
            new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(800f, 130f));

        RectTransform settingsColumn = CreateColumn(settingsPanel.transform, "SettingsColumn", new Vector2(0f, 20f), new Vector2(920f, 300f));
        settingsColumn.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().spacing = 20f;

        UnityEngine.UI.Slider masterSlider = CreateSliderRow(settingsColumn, "Master volume", 1f);
        UnityEngine.UI.Slider musicSlider = CreateSliderRow(settingsColumn, "Music volume", 0.5f);
        UnityEngine.UI.Slider effectsSlider = CreateSliderRow(settingsColumn, "Effects volume", 1f);
        UnityEngine.UI.Toggle fullscreenToggle = CreateToggleRow(settingsColumn, "Fullscreen");

        UnityEngine.UI.Button backButton = CreateButton(settingsPanel.transform, "BackButton", "BACK", Gold);
        Place((RectTransform)backButton.transform, new Vector2(0.5f, 0f), new Vector2(0f, 160f), new Vector2(360f, 90f));

        pausePanel.SetActive(false);
        settingsPanel.SetActive(false);

        // ---------- Menu camera ----------

        GameObject cameraPivot = new GameObject("MenuCameraPivot");
        cameraPivot.transform.position = Vector3.zero;
        cameraPivot.AddComponent<MenuCameraOrbit>();

        GameObject cameraObject = new GameObject("MenuCamera");
        cameraObject.transform.SetParent(cameraPivot.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 12f, -16f);
        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, 0f) - cameraObject.transform.position);

        CinemachineCamera menuCamera = cameraObject.AddComponent<CinemachineCamera>();
        menuCamera.Priority = 50;   // Above every other camera while the menu is open
        LensSettings lens = menuCamera.Lens;
        lens.FieldOfView = 45f;
        menuCamera.Lens = lens;

        // ---------- Fill in the GameMenu component ----------

        GameMenu gameMenu = canvasObject.AddComponent<GameMenu>();
        SerializedObject fields = new SerializedObject(gameMenu);

        Link(fields, "run", run);
        Link(fields, "player", player);
        Link(fields, "tableManager", tableManager);
        Link(fields, "gameplayCanvas", gameplayCanvas);
        Link(fields, "menuCamera", cameraPivot);
        LinkArray(fields, "minigamePanels", minigamePanels);

        Link(fields, "mainMenuPanel", mainMenuPanel);
        Link(fields, "playButton", playButton);
        Link(fields, "settingsButton", settingsButton);
        Link(fields, "quitButton", quitButton);
        Link(fields, "recordsText", recordsText);

        Link(fields, "pausePanel", pausePanel);
        Link(fields, "resumeButton", resumeButton);
        Link(fields, "pauseSettingsButton", pauseSettingsButton);
        Link(fields, "mainMenuButton", mainMenuButton);
        Link(fields, "pauseQuitButton", pauseQuitButton);

        Link(fields, "settingsPanel", settingsPanel);
        Link(fields, "masterSlider", masterSlider);
        Link(fields, "musicSlider", musicSlider);
        Link(fields, "effectsSlider", effectsSlider);
        Link(fields, "fullscreenToggle", fullscreenToggle);
        Link(fields, "backButton", backButton);

        fields.ApplyModifiedPropertiesWithoutUndo();

        // ---------- Finish ----------

        Undo.RegisterCreatedObjectUndo(canvasObject, "Build Game Menu");
        Undo.RegisterCreatedObjectUndo(cameraPivot, "Build Game Menu");
        EditorSceneManager.MarkSceneDirty(canvasObject.scene);
        Selection.activeGameObject = canvasObject;

        Tell($"Game menu built!\n\n" +
             $"Found {minigamePanels.Length} minigame panels (should be 3).\n\n" +
             "Press Ctrl + S to save the scene, then press Play.");
    }

    // ---------- Finding things ----------

    // The game's own canvas: the one that holds the Run Manager's message text
    private static Canvas FindGameplayCanvas(RunManager run)
    {
        SerializedProperty messageText = new SerializedObject(run).FindProperty("messageText");
        TMP_Text text = messageText != null ? messageText.objectReferenceValue as TMP_Text : null;

        if (text == null)
        {
            return null;
        }

        Canvas canvas = text.GetComponentInParent<Canvas>(true);
        return canvas != null ? canvas.rootCanvas : null;
    }

    private static GameObject[] FindMinigamePanels()
    {
        System.Collections.Generic.List<GameObject> panels = new System.Collections.Generic.List<GameObject>();

        foreach (HighLowGame game in Object.FindObjectsByType<HighLowGame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            panels.Add(game.gameObject);
        }

        foreach (DiceGame game in Object.FindObjectsByType<DiceGame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            panels.Add(game.gameObject);
        }

        foreach (BlackjackGame game in Object.FindObjectsByType<BlackjackGame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            panels.Add(game.gameObject);
        }

        return panels.ToArray();
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        Undo.RegisterCreatedObjectUndo(eventSystem, "Build Game Menu");
    }

    private static void LoadResources()
    {
        boldFont = FindFont("Fredoka-Bold");
        mediumFont = FindFont("Fredoka-Medium");

        Sprite standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        Sprite background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        Sprite checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");

        uiResources = new UnityEngine.UI.DefaultControls.Resources
        {
            standard = standard,
            background = background,
            knob = knob,
            checkmark = checkmark
        };

        tmpResources = new TMP_DefaultControls.Resources
        {
            standard = standard,
            background = background,
            knob = knob,
            checkmark = checkmark
        };
    }

    // Finds a TextMeshPro font asset whose name contains the given text, or falls back to TMP's default font
    private static TMP_FontAsset FindFont(string nameContains)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

            if (font != null && font.name.Contains(nameContains))
            {
                return font;
            }
        }

        return TMP_Settings.defaultFontAsset;
    }

    // ---------- Building UI ----------

    private static RectTransform NewUiObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = UiLayer();
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // Anchor, pivot and position at the same point (like Alt + Shift in the Anchor Presets)
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // A dark see-through layer covering the whole screen. It also blocks clicks to the game behind it.
    private static GameObject CreatePanel(Transform parent, string name, float darkness)
    {
        RectTransform rect = NewUiObject(name, parent);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(0f, 0f, 0f, darkness);
        image.raycastTarget = true;
        return rect.gameObject;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string content, TMP_FontAsset font, float size,
        Color color, Vector2 anchor, Vector2 position, Vector2 boxSize)
    {
        RectTransform rect = NewUiObject(name, parent);
        Place(rect, anchor, position, boxSize);

        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;

        if (font != null)
        {
            text.font = font;
        }

        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    // A column that stacks its buttons from top to bottom, evenly spaced
    private static RectTransform CreateColumn(Transform parent, string name, Vector2 position, Vector2 size)
    {
        RectTransform rect = NewUiObject(name, parent);
        Place(rect, new Vector2(0.5f, 0.5f), position, size);

        UnityEngine.UI.VerticalLayoutGroup layout = rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return rect;
    }

    private static UnityEngine.UI.Button CreateButton(Transform parent, string name, string label, Color color)
    {
        GameObject go = TMP_DefaultControls.CreateButton(tmpResources);
        go.name = name;
        go.transform.SetParent(parent, false);
        SetLayer(go);

        go.GetComponent<UnityEngine.UI.Image>().color = color;

        TextMeshProUGUI text = go.GetComponentInChildren<TextMeshProUGUI>(true);
        text.text = label;

        if (boldFont != null)
        {
            text.font = boldFont;
        }

        text.fontSize = 44f;
        text.color = DarkText;
        text.raycastTarget = false;

        UnityEngine.UI.LayoutElement size = go.AddComponent<UnityEngine.UI.LayoutElement>();
        size.minHeight = 90f;
        size.preferredHeight = 90f;

        return go.GetComponent<UnityEngine.UI.Button>();
    }

    // One settings row: a label on the left, a row of controls on the right
    private static RectTransform CreateRow(Transform column, string label)
    {
        RectTransform row = NewUiObject(label.Replace(" ", "") + "Row", column);

        UnityEngine.UI.HorizontalLayoutGroup layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 30f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        UnityEngine.UI.LayoutElement rowSize = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        rowSize.minHeight = 60f;
        rowSize.preferredHeight = 60f;

        TextMeshProUGUI text = CreateText(row, "Label", label, mediumFont, 36f, Color.white,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 60f));
        text.alignment = TextAlignmentOptions.MidlineLeft;

        UnityEngine.UI.LayoutElement labelSize = text.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        labelSize.preferredWidth = 320f;
        labelSize.preferredHeight = 60f;

        return row;
    }

    private static UnityEngine.UI.Slider CreateSliderRow(Transform column, string label, float startValue)
    {
        RectTransform row = CreateRow(column, label);

        GameObject go = UnityEngine.UI.DefaultControls.CreateSlider(uiResources);
        go.name = "Slider";
        go.transform.SetParent(row, false);
        SetLayer(go);

        UnityEngine.UI.LayoutElement size = go.AddComponent<UnityEngine.UI.LayoutElement>();
        size.preferredWidth = 560f;
        size.preferredHeight = 36f;

        Transform fill = go.transform.Find("Fill Area/Fill");

        if (fill != null)
        {
            fill.GetComponent<UnityEngine.UI.Image>().color = Gold;
        }

        UnityEngine.UI.Slider slider = go.GetComponent<UnityEngine.UI.Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = startValue;
        return slider;
    }

    private static UnityEngine.UI.Toggle CreateToggleRow(Transform column, string label)
    {
        RectTransform row = CreateRow(column, label);

        GameObject go = UnityEngine.UI.DefaultControls.CreateToggle(uiResources);
        go.name = "Toggle";
        go.transform.SetParent(row, false);

        // The default toggle comes with an old-style text label; the row already has its own label
        Transform oldLabel = go.transform.Find("Label");

        if (oldLabel != null)
        {
            Object.DestroyImmediate(oldLabel.gameObject);
        }

        SetLayer(go);

        // A bigger box, easier to click
        RectTransform box = go.transform.Find("Background") as RectTransform;

        if (box != null)
        {
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.anchoredPosition = Vector2.zero;
            box.sizeDelta = new Vector2(44f, 44f);

            RectTransform check = box.Find("Checkmark") as RectTransform;

            if (check != null)
            {
                check.anchorMin = Vector2.zero;
                check.anchorMax = Vector2.one;
                check.offsetMin = new Vector2(4f, 4f);
                check.offsetMax = new Vector2(-4f, -4f);
            }
        }

        UnityEngine.UI.LayoutElement size = go.AddComponent<UnityEngine.UI.LayoutElement>();
        size.preferredWidth = 44f;
        size.preferredHeight = 44f;

        UnityEngine.UI.Toggle toggle = go.GetComponent<UnityEngine.UI.Toggle>();
        toggle.isOn = true;
        return toggle;
    }

    // ---------- Small helpers ----------

    private static int UiLayer()
    {
        int layer = LayerMask.NameToLayer("UI");
        return layer >= 0 ? layer : 0;
    }

    private static void SetLayer(GameObject root)
    {
        int layer = UiLayer();

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }

    private static void Link(SerializedObject fields, string fieldName, Object value)
    {
        SerializedProperty property = fields.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"GameMenuBuilder: the GameMenu field '{fieldName}' was not found.");
            return;
        }

        property.objectReferenceValue = value;
    }

    private static void LinkArray(SerializedObject fields, string fieldName, GameObject[] values)
    {
        SerializedProperty property = fields.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"GameMenuBuilder: the GameMenu field '{fieldName}' was not found.");
            return;
        }

        property.arraySize = values.Length;

        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }

    private static void Tell(string message)
    {
        EditorUtility.DisplayDialog("Build Game Menu", message, "OK");
    }
}
