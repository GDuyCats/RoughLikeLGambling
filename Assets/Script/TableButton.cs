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
    [SerializeField] private TMP_Text label;           // Text printed on top of the dome (sinks with it when pressed)
    [SerializeField] private float standHeight = 0.65f;   // Height of the top of the dome, where the chibi lands
    [SerializeField] private float pressDepth = 0.25f;
    [SerializeField] private float pressTime = 0.12f;
    [SerializeField, Range(0f, 1f)] private float disabledBrightness = 0.35f;   // Locked buttons look darker
    [SerializeField, Range(0f, 0.5f)] private float labelOutline = 0.3f;        // Dark outline around the label's letters
    [SerializeField] private Color lockedLabelColor = new Color(0.65f, 0.65f, 0.7f);  // Locked buttons have grey text
    [SerializeField, Range(0f, 1f)] private float darkLabelAbove = 0.55f;   // Caps brighter than this (white, gold) get dark text

    private TMP_Text screenLabel;
    private Vector3 capRestPosition;
    private Vector3 labelRestPosition;
    private Color capColor;
    private Color capGlow;          // The cap's emission: on while the button can be pressed, off while locked
    private bool hasGlow;
    private Color labelColor;       // White on dark caps, dark on bright caps
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    // Visible and unlocked: the button can be pressed right now
    public bool CanPress => body.activeSelf && screenButton.interactable;

    // Visible but locked: pressing it does nothing (its label says why)
    public bool IsLocked => body.activeSelf && !screenButton.interactable;

    // The middle of the top of the button, where the chibi stands when pressing it
    public Vector3 TopCenter => transform.position + Vector3.up * standHeight;

    private void Awake()
    {
        screenLabel = screenButton.GetComponentInChildren<TMP_Text>(true);

        capRestPosition = cap.localPosition;
        labelRestPosition = label.transform.localPosition;
        capColor = capRenderer.material.color;
        hasGlow = capRenderer.material.HasProperty(EmissionId);
        capGlow = hasGlow ? capRenderer.material.GetColor(EmissionId) : Color.black;

        // Bold text that stands out from the dome: white with a dark outline on dark colors,
        // dark with a light outline on bright colors (white, gold)
        bool brightCap = capColor.r * 0.2126f + capColor.g * 0.7152f + capColor.b * 0.0722f > darkLabelAbove;
        labelColor = brightCap ? new Color(0.1f, 0.1f, 0.14f) : Color.white;
        label.color = labelColor;
        label.fontStyle |= FontStyles.Bold;
        label.outlineWidth = brightCap ? labelOutline * 0.5f : labelOutline;
        label.outlineColor = brightCap ? new Color32(255, 255, 255, 160) : new Color32(20, 20, 30, 255);
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

        bool on = screenButton.interactable;
        capRenderer.material.color = on ? capColor : Darken(capColor, disabledBrightness);
        label.color = on ? labelColor : lockedLabelColor;

        if (hasGlow)
        {
            capRenderer.material.SetColor(EmissionId, on ? capGlow : Color.black);
        }
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

    // Someone tried to press it while it's locked: a little "no" shake and a buzz.
    // The label already says why it's locked.
    public void Deny()
    {
        if (!body.activeSelf)
        {
            return;
        }

        StopAllCoroutines();
        SetCapPosition(capRestPosition);
        StartCoroutine(DenyShake());
        AudioManager.Play(SoundId.Denied);
    }

    private IEnumerator DenyShake()
    {
        const float duration = 0.3f;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float side = Mathf.Sin(t * 60f) * 0.12f * (1f - t / duration);
            SetCapPosition(capRestPosition + Vector3.right * side);
            yield return null;
        }

        SetCapPosition(capRestPosition);
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
            SetCapPosition(Vector3.Lerp(from, to, t / duration));
            yield return null;
        }

        SetCapPosition(to);
    }

    // The label printed on the dome moves down and up with it
    private void SetCapPosition(Vector3 position)
    {
        cap.localPosition = position;
        label.transform.localPosition = labelRestPosition + (position - capRestPosition);
    }

    private Color Darken(Color color, float brightness)
    {
        return new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
    }
}