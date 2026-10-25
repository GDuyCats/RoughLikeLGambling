using UnityEngine;
using UnityEngine.InputSystem;

// Shows or hides some UI (like the Stats and Upgrades panels) when a key is pressed
public class PanelToggle : MonoBehaviour
{
    [SerializeField] private GameObject[] panels;
    [SerializeField] private Key toggleKey = Key.Tab;
    [SerializeField] private bool startVisible = true;

    private bool isVisible;

    private void Start()
    {
        SetVisible(startVisible);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
        {
            SetVisible(!isVisible);
        }
    }

    private void SetVisible(bool visible)
    {
        isVisible = visible;

        foreach (GameObject panel in panels)
        {
            panel.SetActive(visible);
        }
    }
}