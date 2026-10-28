using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A big arcade button lying on the table. It stands in for a button on the screen:
// it copies that button's label and on/off state, and pressing it clicks that screen button.
// That way the minigame rules don't need to know these table buttons exist.
// The button is solid: the chibi can't walk through it, but can stand on top of it.
public class TableButton : MonoBehaviour
{
    [SerializeField] private Button screenButton;      // The screen button this one stands in for
    [SerializeField] private GameObject body;          // Everything you can see and bump into (hidden with the screen button)
    [SerializeField] private Transform cap;            // The dome that sinks when pressed
    [SerializeField] private Renderer capRenderer;
    [SerializeField] private TMP_Text label;           // Floating text above the button
    [SerializeField] private float standHeight = 0.65f;   // Height of the top of the dome, where the chibi lands
    [SerializeField] private float pressDepth = 0.25f;
    [SerializeField] private float pressTime = 0.12f;
    [SerializeField, Range(0f, 1f)] private float disabledBrightness = 0.35f;   // Locked buttons look darker

    private TMP_Text screenLabel;
    private Vector3 capRestPosition;
    private Color capColor;

    // Visible and unlocked: the button can be pressed right now
    public bool CanPress => body.activeSelf && screenButton.interactable;

    // The middle of the top of the button, where the chibi stands when pressing it
    public Vector3 TopCenter => transform.position + Vector3.up * standHeight;

    private void Awake()
    {
        screenLabel = screenButton.GetComponentInChildren<TMP_Text>(true);
        capRestPosition = cap.localPosition;
        capColor = capRenderer.material.color;
    }

    // Copy the screen button's state every frame.
    // Hiding the body also switches off its colliders, so a hidden button can't block anyone.
    private void Update()
    {
        bool visible = screenButton.gameObject.activeInHierarchy;

        if (body.activeSelf != visible)
        {
            body.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        if (screenLabel != null)
        {
            label.text = screenLabel.text;
        }

        capRenderer.material.color = screenButton.interactable ? capColor : Darken(capColor, disabledBrightness);
    }

    // Press the button. Returns true if the press counted.
    public bool Press()
    {
        if (!CanPress)
        {
            return false;
        }

        StopAllCoroutines();
        StartCoroutine(PressAnimation());
        AudioManager.Play(SoundId.ButtonPress);
        screenButton.onClick.Invoke();
        return true;
    }

    // The dome sinks quickly, waits a moment, then pops back up
    private IEnumerator PressAnimation()
    {
        Vector3 down = capRestPosition + Vector3.down * pressDepth;

        yield return MoveCap(cap.localPosition, down, pressTime * 0.5f);
        yield return new WaitForSeconds(0.1f);
        yield return MoveCap(down, capRestPosition, pressTime);
    }

    private IEnumerator MoveCap(Vector3 from, Vector3 to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            cap.localPosition = Vector3.Lerp(from, to, t / duration);
            yield return null;
        }

        cap.localPosition = to;
    }

    private Color Darken(Color color, float brightness)
    {
        return new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
    }
}