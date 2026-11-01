using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// The Street Fighter 6 style perfect parry moment, for parried hand attacks (never for chips):
// the whole game freezes for a second, the world turns dark and grey, the one who parried stays
// in full color with a glowing outline, paint splashes out from the hit, and they shout a line.
// Then time snaps back through a short slow motion.
//
// How the "one character in color" trick works: during the freeze the parrying character is moved
// to the ParryHighlight layer, and a Render Objects renderer feature draws that layer again AFTER
// post-processing, so the grey filter doesn't touch it. Tools > Set Up Parry Freeze sets all of this up.
public class ParryFreeze : MonoBehaviour
{
    public enum Side
    {
        Player,
        Boss
    }

    private static ParryFreeze instance;

    // 1 normally. The pause menu resumes to this speed, so pausing in the middle of a freeze doesn't break it.
    public static float GameSpeed { get; private set; } = 1f;

    // True while the game stands still. Parry input is ignored meanwhile.
    public static bool IsFrozen { get; private set; }

    [Header("Timing (real seconds)")]
    [SerializeField] private float freezeTime = 1f;
    [SerializeField] private float slowMotionTime = 0.3f;              // After the freeze, the game speeds back up over this long
    [SerializeField, Range(0.05f, 1f)] private float slowMotionStartSpeed = 0.3f;

    [Header("Grey World")]
    [SerializeField, Range(-100f, 0f)] private float worldSaturation = -100f;
    [SerializeField] private float worldExposure = -1.5f;               // Darker
    [SerializeField] private float worldContrast = 20f;

    [Header("Glow")]
    [SerializeField] private string highlightLayerName = "ParryHighlight";
    [SerializeField] private Material outlineMaterial;                  // Unlit, front faces culled
    [SerializeField] private float outlineWidth = 0.12f;                // 0.12 = the outline shell is 12% bigger
    [SerializeField] private Color playerGlow = new Color(0.15f, 0.45f, 1f);
    [SerializeField] private Color bossGlow = new Color(1f, 0.15f, 0.2f);

    [Header("Flash")]
    [SerializeField, Range(0f, 1f)] private float flashStrength = 0.6f;
    [SerializeField] private float flashFadeTime = 0.15f;

    [Header("Paint Splash")]
    [SerializeField] private Material splashMaterial;                   // Unlit
    [SerializeField] private int splashBlobs = 30;
    [SerializeField] private float playerSplashRadius = 1.6f;
    [SerializeField] private float bossSplashRadius = 3.5f;
    [SerializeField] private Color[] playerSplashColors =
    {
        new Color(0.1f, 0.25f, 1f),
        new Color(0.2f, 0.75f, 1f),
        new Color(0.45f, 0.2f, 1f),
        new Color(0.6f, 0.9f, 1f)
    };
    [SerializeField] private Color[] bossSplashColors =
    {
        new Color(1f, 0.1f, 0.15f),
        new Color(1f, 0.45f, 0.1f),
        new Color(0.8f, 0.05f, 0.4f),
        new Color(1f, 0.75f, 0.3f)
    };

    [Header("Lines")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private float lineTime = 1.6f;                     // How long a line stays up (real seconds)
    [SerializeField] private float lineFontSize = 56f;
    [SerializeField] private float playerLineHeight = 2.2f;             // How far above the chibi the line floats
    [SerializeField] private float bossLineHeight = 5.5f;               // How far above the dealer's head
    [SerializeField] private string[] playerLines =
    {
        "I see that!",
        "So easy!",
        "Too slow!",
        "Not today!",
        "Nice try!",
        "Read you like a book!"
    };

    [Header("Shake")]
    [SerializeField] private CinemachineImpulseSource resumeShake;      // Kicks in when time starts again (optional)
    [SerializeField] private float resumeShakeForce = 0.8f;

    // One paint blob of the splash
    private class Blob
    {
        public Transform Transform;
        public Vector3 Start;
        public Vector3 Direction;
        public Vector3 Scale;
    }

    private int highlightLayer = -1;
    private Volume volume;
    private Image flashImage;
    private TextMeshProUGUI lineText;
    private Mesh blobMesh;
    private MaterialPropertyBlock colorBlock;

    private Coroutine freezeRoutine;
    private Coroutine lineRoutine;
    private readonly List<(GameObject target, int layer)> layerChanges = new List<(GameObject, int)>();
    private readonly List<GameObject> outlines = new List<GameObject>();
    private readonly List<Blob> blobs = new List<Blob>();

    private void Awake()
    {
        instance = this;
        GameSpeed = 1f;
        IsFrozen = false;

        highlightLayer = LayerMask.NameToLayer(highlightLayerName);

        if (highlightLayer < 0)
        {
            Debug.LogWarning($"ParryFreeze: there is no '{highlightLayerName}' layer, so the parrying character will turn grey too. Run Tools > Set Up Parry Freeze.");
        }

        colorBlock = new MaterialPropertyBlock();
        CreateVolume();
        CreateScreenUI();
        CreateBlobMesh();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            GameSpeed = 1f;
            IsFrozen = false;
        }
    }

    // ---------- Called by the boss and the dealers ----------

    // The chibi perfect parried a hand. hitPoint is where the hand met the chibi.
    public static void PlayerParried(Transform chibi, Vector3 hitPoint, bool talk)
    {
        if (instance == null || chibi == null)
        {
            return;
        }

        string line = talk ? instance.PickPlayerLine() : null;
        instance.Play(chibi, chibi, hitPoint, Side.Player, line);
    }

    // The boss parried the chibi's counter. line can be empty for no line.
    public static void BossParried(Transform boss, Transform head, Vector3 hitPoint, string line)
    {
        if (instance == null || boss == null)
        {
            return;
        }

        instance.Play(boss, head != null ? head : boss, hitPoint, Side.Boss, line);
    }

    private string PickPlayerLine()
    {
        return playerLines.Length > 0 ? playerLines[Random.Range(0, playerLines.Length)] : null;
    }

    private void Play(Transform highlightRoot, Transform lineAnchor, Vector3 hitPoint, Side side, string line)
    {
        // A new parry during a freeze (rare) replaces the old one
        if (freezeRoutine != null)
        {
            StopCoroutine(freezeRoutine);
            EndFreeze();
        }

        freezeRoutine = StartCoroutine(FreezeRoutine(highlightRoot, hitPoint, side));

        if (!string.IsNullOrEmpty(line))
        {
            if (lineRoutine != null)
            {
                StopCoroutine(lineRoutine);
            }

            lineRoutine = StartCoroutine(LineRoutine(line, lineAnchor, side));
        }
    }

    // ---------- The freeze ----------

    private IEnumerator FreezeRoutine(Transform highlightRoot, Vector3 hitPoint, Side side)
    {
        IsFrozen = true;
        SetGameSpeed(0f);

        Color glow = side == Side.Player ? playerGlow : bossGlow;
        Highlight(highlightRoot, glow);
        SpawnSplash(hitPoint, side);

        Color flash = Color.Lerp(Color.white, glow, 0.25f);

        // Stand still. Time spent in the pause menu doesn't count.
        float elapsed = 0f;

        while (elapsed < freezeTime)
        {
            if (!GameMenu.IsPaused)
            {
                elapsed += Time.unscaledDeltaTime;
            }

            volume.weight = Mathf.Clamp01(elapsed / 0.05f);
            flash.a = flashStrength * (1f - Mathf.Clamp01(elapsed / flashFadeTime));
            flashImage.color = flash;
            yield return null;
        }

        flashImage.color = Color.clear;
        ClearHighlight();
        IsFrozen = false;

        if (resumeShake != null)
        {
            resumeShake.GenerateImpulseWithForce(resumeShakeForce);
        }

        // Slow motion back to normal speed, while the world gets its colors back and the paint flies off
        elapsed = 0f;

        while (elapsed < slowMotionTime)
        {
            if (!GameMenu.IsPaused)
            {
                elapsed += Time.unscaledDeltaTime;
            }

            float progress = Mathf.Clamp01(elapsed / slowMotionTime);
            SetGameSpeed(Mathf.Lerp(slowMotionStartSpeed, 1f, progress));
            volume.weight = 1f - progress;
            UpdateSplash(progress);
            yield return null;
        }

        EndFreeze();
        freezeRoutine = null;
    }

    // Puts everything back to normal
    private void EndFreeze()
    {
        ClearHighlight();
        ClearSplash();
        flashImage.color = Color.clear;
        volume.weight = 0f;
        IsFrozen = false;
        SetGameSpeed(1f);
    }

    private void SetGameSpeed(float speed)
    {
        GameSpeed = speed;

        // While the pause menu is open, time stays stopped; the menu resumes to GameSpeed
        if (!GameMenu.IsPaused)
        {
            Time.timeScale = speed;
        }
    }

    // ---------- Glow ----------

    private void Highlight(Transform root, Color glow)
    {
        colorBlock.SetColor("_BaseColor", glow);

        foreach (Renderer target in root.GetComponentsInChildren<Renderer>())
        {
            // Only solid shapes: skip text and effects
            if (!(target is MeshRenderer) || !target.enabled || target.GetComponent<TMP_Text>() != null)
            {
                continue;
            }

            if (highlightLayer >= 0)
            {
                layerChanges.Add((target.gameObject, target.gameObject.layer));
                target.gameObject.layer = highlightLayer;
            }

            MeshFilter shape = target.GetComponent<MeshFilter>();

            if (outlineMaterial == null || shape == null || shape.sharedMesh == null)
            {
                continue;
            }

            // A slightly bigger copy showing only its inside: looks like a glowing outline
            GameObject outline = new GameObject("ParryOutline");
            outline.layer = highlightLayer >= 0 ? highlightLayer : target.gameObject.layer;
            outline.transform.SetParent(target.transform, false);
            outline.transform.localScale = Vector3.one * (1f + outlineWidth);
            outline.AddComponent<MeshFilter>().sharedMesh = shape.sharedMesh;

            MeshRenderer outlineRenderer = outline.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = outlineMaterial;
            outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            outlineRenderer.SetPropertyBlock(colorBlock);

            outlines.Add(outline);
        }
    }

    private void ClearHighlight()
    {
        foreach ((GameObject target, int layer) in layerChanges)
        {
            if (target != null)
            {
                target.layer = layer;
            }
        }

        layerChanges.Clear();

        foreach (GameObject outline in outlines)
        {
            if (outline != null)
            {
                Destroy(outline);
            }
        }

        outlines.Clear();
    }

    // ---------- Paint splash ----------

    // Blobs of paint burst out from the hit and hang frozen in the air
    private void SpawnSplash(Vector3 hitPoint, Side side)
    {
        ClearSplash();

        if (splashMaterial == null || splashBlobs <= 0)
        {
            return;
        }

        Color[] colors = side == Side.Player ? playerSplashColors : bossSplashColors;
        float radius = side == Side.Player ? playerSplashRadius : bossSplashRadius;

        for (int i = 0; i < splashBlobs; i++)
        {
            // Mostly sideways and up, not into the table
            Vector3 direction = Random.onUnitSphere;
            direction.y = Mathf.Abs(direction.y) * 0.6f;
            direction.Normalize();

            float distance = radius * Mathf.Lerp(0.15f, 1f, Mathf.Sqrt(Random.value));
            float size = radius * Random.Range(0.06f, 0.2f) * (1.2f - distance / radius * 0.6f);

            GameObject blob = new GameObject("ParrySplash");
            blob.layer = highlightLayer >= 0 ? highlightLayer : 0;
            blob.transform.SetParent(transform, false);
            blob.transform.position = hitPoint + direction * distance;

            // Stretched along the direction it flies, like a splash of paint
            blob.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            Vector3 scale = new Vector3(size, size * Random.Range(0.4f, 0.8f), size * Random.Range(1.3f, 2.8f));
            blob.transform.localScale = scale;

            blob.AddComponent<MeshFilter>().sharedMesh = blobMesh;
            MeshRenderer blobRenderer = blob.AddComponent<MeshRenderer>();
            blobRenderer.sharedMaterial = splashMaterial;
            blobRenderer.shadowCastingMode = ShadowCastingMode.Off;
            blobRenderer.receiveShadows = false;

            if (colors.Length > 0)
            {
                colorBlock.SetColor("_BaseColor", colors[Random.Range(0, colors.Length)]);
                blobRenderer.SetPropertyBlock(colorBlock);
            }

            blobs.Add(new Blob
            {
                Transform = blob.transform,
                Start = blob.transform.position,
                Direction = direction,
                Scale = scale
            });
        }
    }

    // After the freeze the paint flies a bit further out and shrinks away
    private void UpdateSplash(float progress)
    {
        foreach (Blob blob in blobs)
        {
            if (blob.Transform == null)
            {
                continue;
            }

            blob.Transform.position = blob.Start + blob.Direction * progress * 1.2f;
            blob.Transform.localScale = blob.Scale * (1f - progress);
        }
    }

    private void ClearSplash()
    {
        foreach (Blob blob in blobs)
        {
            if (blob.Transform != null)
            {
                Destroy(blob.Transform.gameObject);
            }
        }

        blobs.Clear();
    }

    // ---------- Lines ----------

    private IEnumerator LineRoutine(string line, Transform anchor, Side side)
    {
        float height = side == Side.Player ? playerLineHeight : bossLineHeight;
        Color glow = side == Side.Player ? playerGlow : bossGlow;

        lineText.text = line;
        lineText.outlineColor = Color.Lerp(glow, Color.black, 0.4f);
        lineText.gameObject.SetActive(true);

        float elapsed = 0f;

        while (elapsed < lineTime && anchor != null)
        {
            if (!GameMenu.IsPaused)
            {
                elapsed += Time.unscaledDeltaTime;
            }

            // Pops in big, then settles
            float pop = Mathf.Clamp01(elapsed / 0.12f);
            lineText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, pop);

            Camera view = Camera.main;

            if (view != null)
            {
                Vector3 screenPoint = view.WorldToScreenPoint(anchor.position + Vector3.up * height);
                bool inFront = screenPoint.z > 0f;
                lineText.enabled = inFront;

                if (inFront)
                {
                    lineText.rectTransform.position = new Vector3(screenPoint.x, screenPoint.y, 0f);
                }
            }

            yield return null;
        }

        lineText.gameObject.SetActive(false);
        lineRoutine = null;
    }

    // ---------- Setup ----------

    // A global grey-and-dark filter, faded in during the freeze
    private void CreateVolume()
    {
        GameObject volumeObject = new GameObject("ParryVolume");
        volumeObject.transform.SetParent(transform, false);
        volumeObject.layer = 0;

        volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.weight = 0f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        ColorAdjustments adjustments = profile.Add<ColorAdjustments>(false);
        adjustments.saturation.Override(worldSaturation);
        adjustments.postExposure.Override(worldExposure);
        adjustments.contrast.Override(worldContrast);
        volume.sharedProfile = profile;
    }

    // The white flash and the line text, drawn on top of the game but under the menus
    private void CreateScreenUI()
    {
        GameObject canvasObject = new GameObject("ParryCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject flashObject = new GameObject("Flash", typeof(RectTransform));
        flashObject.transform.SetParent(canvasObject.transform, false);
        RectTransform flashRect = (RectTransform)flashObject.transform;
        flashRect.anchorMin = Vector2.zero;
        flashRect.anchorMax = Vector2.one;
        flashRect.offsetMin = Vector2.zero;
        flashRect.offsetMax = Vector2.zero;

        flashImage = flashObject.AddComponent<Image>();
        flashImage.color = Color.clear;
        flashImage.raycastTarget = false;

        GameObject lineObject = new GameObject("Line", typeof(RectTransform));
        lineObject.transform.SetParent(canvasObject.transform, false);
        ((RectTransform)lineObject.transform).sizeDelta = new Vector2(900f, 120f);

        lineText = lineObject.AddComponent<TextMeshProUGUI>();

        if (font != null)
        {
            lineText.font = font;
        }

        lineText.fontSize = lineFontSize;
        lineText.fontStyle = FontStyles.Bold;
        lineText.color = Color.white;
        lineText.alignment = TextAlignmentOptions.Center;
        lineText.textWrappingMode = TextWrappingModes.NoWrap;
        lineText.raycastTarget = false;
        lineText.outlineWidth = 0.25f;
        lineObject.SetActive(false);
    }

    // A low-poly ball for the paint blobs, taken from Unity's built-in sphere
    private void CreateBlobMesh()
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        blobMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(sphere);   // Right away, so its collider never touches anything
    }
}
