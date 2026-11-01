using System.Collections;
using TMPro;
using UnityEngine;

// A giant dealer sitting behind a table. Each table's dealer is also one of the final bosses.
// Most of the time they just sit there, breathing and blinking. Once the run's boss is revealed,
// that dealer's eyes glow red. A thrown chip on the head makes a dealer flinch and grumble
// (just for fun: it changes nothing in the game).
public class TableDealer : MonoBehaviour
{
    [SerializeField] private FinalBoss boss;            // Which boss this dealer is
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Renderer headRenderer;
    [SerializeField] private Renderer[] eyeRenderers;
    [SerializeField] private TMP_Text speech;           // Floating text for grumbles
    [SerializeField, TextArea] private string[] grumbles = { "Hey! Watch it!" };

    [Header("Idle")]
    [SerializeField] private float breatheHeight = 0.15f;
    [SerializeField] private float breatheSpeed = 1.5f;
    [SerializeField] private float minBlinkGap = 2f;
    [SerializeField] private float maxBlinkGap = 5f;

    [Header("Eyes")]
    [SerializeField] private Color calmEyeColor = new Color(0.9f, 0.9f, 1f);
    [SerializeField] private Color bossEyeColor = new Color(1f, 0.1f, 0.05f);
    [SerializeField] private float eyeGlow = 3f;

    [Header("Chip Hits")]
    [SerializeField] private float grumbleCooldown = 2f;   // At most one grumble this often
    [SerializeField] private float speechTime = 2f;

    private Vector3 headRest;
    private Quaternion headRestRotation;
    private Vector3[] eyeScales;
    private float nextBlinkTime;
    private float nextGrumbleTime;
    private Coroutine flinchRoutine;
    private Coroutine speechRoutine;

    public FinalBoss Boss => boss;
    public Transform Head => head;
    public Transform LeftHand => leftHand;
    public Transform RightHand => rightHand;
    public Renderer HeadRenderer => headRenderer;

    private void Awake()
    {
        headRest = head.localPosition;
        headRestRotation = head.localRotation;

        eyeScales = new Vector3[eyeRenderers.Length];

        for (int i = 0; i < eyeRenderers.Length; i++)
        {
            eyeScales[i] = eyeRenderers[i].transform.localScale;
        }

        SetEyeColor(calmEyeColor);

        if (speech != null)
        {
            speech.gameObject.SetActive(false);
        }

        nextBlinkTime = Time.time + Random.Range(minBlinkGap, maxBlinkGap);
    }

    private void Update()
    {
        // Breathing: the head bobs gently up and down
        head.localPosition = headRest + Vector3.up * Mathf.Sin(Time.time * breatheSpeed) * breatheHeight;

        if (Time.time >= nextBlinkTime && flinchRoutine == null)
        {
            nextBlinkTime = Time.time + Random.Range(minBlinkGap, maxBlinkGap);
            StartCoroutine(BlinkRoutine());
        }
    }

    // true: this dealer is the run's revealed boss, so the eyes glow red. false: calm eyes again.
    public void SetMarked(bool marked)
    {
        SetEyeColor(marked ? bossEyeColor : calmEyeColor);
    }

    // A thrown chip hit this dealer on the head
    public void OnChipHit()
    {
        AudioManager.Play(SoundId.DealerBonk);

        if (flinchRoutine != null)
        {
            StopCoroutine(flinchRoutine);
        }

        flinchRoutine = StartCoroutine(FlinchRoutine());

        if (speech != null && grumbles.Length > 0 && Time.time >= nextGrumbleTime)
        {
            nextGrumbleTime = Time.time + grumbleCooldown;

            if (speechRoutine != null)
            {
                StopCoroutine(speechRoutine);
            }

            speechRoutine = StartCoroutine(SpeechRoutine(grumbles[Random.Range(0, grumbles.Length)]));
        }
    }

    // Eyes shut, head jerks back, then an angry little shake
    private IEnumerator FlinchRoutine()
    {
        SetEyesOpen(false);

        const float jerkTime = 0.08f;
        const float shakeTime = 0.45f;

        for (float t = 0f; t < jerkTime; t += Time.deltaTime)
        {
            head.localRotation = headRestRotation * Quaternion.Euler(-15f * (t / jerkTime), 0f, 0f);
            yield return null;
        }

        for (float t = 0f; t < shakeTime; t += Time.deltaTime)
        {
            float fade = 1f - t / shakeTime;
            float shake = Mathf.Sin(t * 45f) * 8f * fade;
            head.localRotation = headRestRotation * Quaternion.Euler(-15f * fade, shake, 0f);
            yield return null;
        }

        head.localRotation = headRestRotation;
        SetEyesOpen(true);
        flinchRoutine = null;
    }

    private IEnumerator BlinkRoutine()
    {
        SetEyesOpen(false);
        yield return new WaitForSeconds(0.12f);

        // A flinch keeps the eyes shut on its own
        if (flinchRoutine == null)
        {
            SetEyesOpen(true);
        }
    }

    private IEnumerator SpeechRoutine(string line)
    {
        speech.text = line;
        speech.gameObject.SetActive(true);
        yield return new WaitForSeconds(speechTime);
        speech.gameObject.SetActive(false);
        speechRoutine = null;
    }

    // Closed eyes are just the eyes squashed flat
    private void SetEyesOpen(bool open)
    {
        for (int i = 0; i < eyeRenderers.Length; i++)
        {
            Vector3 scale = eyeScales[i];
            eyeRenderers[i].transform.localScale = open ? scale : new Vector3(scale.x, scale.y * 0.1f, scale.z);
        }
    }

    private void SetEyeColor(Color color)
    {
        foreach (Renderer eye in eyeRenderers)
        {
            Material material = eye.material;
            material.color = color;

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * eyeGlow);
            }
        }
    }
}
