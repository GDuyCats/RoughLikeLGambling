using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The main menu, the pause menu and the settings panel.
// The menus sit on their own canvas on top of the game. While a menu is open,
// the gameplay screen is hidden or frozen, and the chibi and the tables stop listening to input.
public class GameMenu : MonoBehaviour
{
    private enum MenuState
    {
        MainMenu,
        Playing,
        Paused
    }

    private const string MasterVolumeKey = "Settings.MasterVolume";
    private const string MusicVolumeKey = "Settings.MusicVolume";
    private const string EffectsVolumeKey = "Settings.EffectsVolume";
    private const string FullscreenKey = "Settings.Fullscreen";

    [Header("Game")]
    [SerializeField] private RunManager run;
    [SerializeField] private PlayerController player;
    [SerializeField] private TableManager tableManager;
    [SerializeField] private Canvas gameplayCanvas;      // The normal game screen (chips, buttons, panels)
    [SerializeField] private GameObject[] minigamePanels; // Esc leaves a table instead of pausing while one of these is open
    [SerializeField] private GameObject menuCamera;       // The slowly circling camera behind the main menu

    [Header("Main Menu")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private TMP_Text recordsText;

    [Header("Pause Menu")]
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button pauseSettingsButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button pauseQuitButton;

    [Header("Settings")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider effectsSlider;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Button backButton;

    private MenuState state;
    private GameObject panelBeforeSettings;   // Where the Back button returns to
    private bool enableGameplayNextFrame;     // See Resume()

    private void Awake()
    {
        playButton.onClick.AddListener(Play);
        settingsButton.onClick.AddListener(OpenSettings);
        quitButton.onClick.AddListener(Quit);

        resumeButton.onClick.AddListener(Resume);
        pauseSettingsButton.onClick.AddListener(OpenSettings);
        mainMenuButton.onClick.AddListener(BackToMainMenu);
        pauseQuitButton.onClick.AddListener(Quit);

        backButton.onClick.AddListener(CloseSettings);
    }

    private void Start()
    {
        LoadSettings();
        ShowMainMenu();
    }

    private void Update()
    {
        // After resuming, wait one frame before the chibi and tables listen again,
        // so the key that closed the menu doesn't also leave the table or jump
        if (enableGameplayNextFrame)
        {
            enableGameplayNextFrame = false;
            SetGameplayInput(true);
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        bool pressedP = keyboard.pKey.wasPressedThisFrame;
        bool pressedEscape = keyboard.escapeKey.wasPressedThisFrame;

        if (state == MenuState.Playing)
        {
            // P pauses anywhere. Esc pauses only on the casino floor (at a table, Esc leaves the table).
            if (pressedP || (pressedEscape && !IsAtTable()))
            {
                Pause();
            }
        }
        else if (state == MenuState.Paused && (pressedP || pressedEscape))
        {
            if (settingsPanel.activeSelf)
            {
                CloseSettings();
            }
            else
            {
                Resume();
            }
        }
    }

    // ---------- Main menu ----------

    private void ShowMainMenu()
    {
        state = MenuState.MainMenu;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        gameplayCanvas.enabled = false;
        SetGameplayInput(false);
        menuCamera.SetActive(true);

        mainMenuPanel.SetActive(true);
        pausePanel.SetActive(false);
        settingsPanel.SetActive(false);

        recordsText.text = RecordBook.HasAnyRecord
            ? $"Best round: {RecordBook.BestRound}   ·   Wins: {RecordBook.Wins}   ·   Most chips: {RecordBook.MostChips}"
            : "No records yet. Good luck!";
    }

    private void Play()
    {
        state = MenuState.Playing;
        mainMenuPanel.SetActive(false);
        menuCamera.SetActive(false);   // The view glides from the menu camera to the casino
        gameplayCanvas.enabled = true;

        // Turn the tables back on first, so they hear the new run start
        SetGameplayInput(true);
        run.StartNewRun();
    }

    // ---------- Pause menu ----------

    private void Pause()
    {
        state = MenuState.Paused;
        Time.timeScale = 0f;          // Everything that moves stops: dice, cards, the dealer's hands
        AudioListener.pause = true;   // Sound effects pause; the music keeps playing
        SetGameplayInput(false);

        pausePanel.SetActive(true);
        settingsPanel.SetActive(false);
    }

    private void Resume()
    {
        state = MenuState.Playing;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        pausePanel.SetActive(false);
        settingsPanel.SetActive(false);
        enableGameplayNextFrame = true;
    }

    // The simplest clean reset: load the whole scene again. It starts on the main menu.
    private void BackToMainMenu()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---------- Settings ----------

    private void OpenSettings()
    {
        panelBeforeSettings = state == MenuState.Paused ? pausePanel : mainMenuPanel;
        panelBeforeSettings.SetActive(false);

        // Show the saved values without triggering the "value changed" events
        masterSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
        musicSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(MusicVolumeKey, 0.5f));
        effectsSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(EffectsVolumeKey, 1f));
        fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);

        masterSlider.onValueChanged.AddListener(OnVolumeChanged);
        musicSlider.onValueChanged.AddListener(OnVolumeChanged);
        effectsSlider.onValueChanged.AddListener(OnVolumeChanged);
        fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);

        settingsPanel.SetActive(true);
    }

    private void CloseSettings()
    {
        masterSlider.onValueChanged.RemoveListener(OnVolumeChanged);
        musicSlider.onValueChanged.RemoveListener(OnVolumeChanged);
        effectsSlider.onValueChanged.RemoveListener(OnVolumeChanged);
        fullscreenToggle.onValueChanged.RemoveListener(OnFullscreenChanged);

        PlayerPrefs.Save();
        settingsPanel.SetActive(false);

        if (panelBeforeSettings != null)
        {
            panelBeforeSettings.SetActive(true);
        }
    }

    // Any volume slider moved: save all three and hear the change right away
    private void OnVolumeChanged(float _)
    {
        PlayerPrefs.SetFloat(MasterVolumeKey, masterSlider.value);
        PlayerPrefs.SetFloat(MusicVolumeKey, musicSlider.value);
        PlayerPrefs.SetFloat(EffectsVolumeKey, effectsSlider.value);
        AudioManager.SetVolumes(masterSlider.value, musicSlider.value, effectsSlider.value);
    }

    private void OnFullscreenChanged(bool isOn)
    {
        PlayerPrefs.SetInt(FullscreenKey, isOn ? 1 : 0);
        Screen.fullScreen = isOn;
    }

    // Apply the saved settings when the game starts
    private void LoadSettings()
    {
        AudioManager.SetVolumes(
            PlayerPrefs.GetFloat(MasterVolumeKey, 1f),
            PlayerPrefs.GetFloat(MusicVolumeKey, 0.5f),
            PlayerPrefs.GetFloat(EffectsVolumeKey, 1f));

        if (PlayerPrefs.HasKey(FullscreenKey))
        {
            Screen.fullScreen = PlayerPrefs.GetInt(FullscreenKey) == 1;
        }
    }

    // ---------- Helpers ----------

    private void Quit()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        // In the Unity editor, "quitting" just stops Play mode
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // While a menu is open, the chibi and the tables ignore the keyboard and mouse
    private void SetGameplayInput(bool on)
    {
        player.enabled = on;
        tableManager.enabled = on;
    }

    private bool IsAtTable()
    {
        foreach (GameObject panel in minigamePanels)
        {
            if (panel != null && panel.activeInHierarchy)
            {
                return true;
            }
        }

        return false;
    }
}