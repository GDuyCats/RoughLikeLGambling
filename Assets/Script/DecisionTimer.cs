using System;
using TMPro;
using UnityEngine;

// A countdown ring printed on the felt: the player has this long to choose, or the game chooses for them.
// The ring empties as time runs out; it turns yellow at half time and red (and pulses) for the last 3 seconds.
// It only counts while the player can actually choose: not during the pause menu, an upgrade choice,
// a boss attack, or while cards or dice are still moving.
public class DecisionTimer : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Material seeThroughMaterial;   // A transparent unlit material to copy (set by Tools > Rearrange Tables)
    [SerializeField] private float radius = 0.95f;
    [SerializeField] private float thickness = 0.22f;
    [SerializeField] private int segments = 64;
    [SerializeField] private Color calmColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color hurryColor = new Color(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color lastColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color trackColor = new Color(0f, 0f, 0f, 0.35f);
    [SerializeField] private float warningTime = 3f;

    private Mesh ringMesh;
    private MeshRenderer ringRenderer;
    private MeshRenderer trackRenderer;
    private TextMeshPro label;
    private Material ringMaterial;

    private float duration;
    private float remaining;
    private Action onTimeout;
    private Func<bool> canCount;
    private float shown;   // 0..1: fades the timer in and out

    public bool IsRunning { get; private set; }

    private void Awake()
    {
        Material baseMaterial = seeThroughMaterial != null ? seeThroughMaterial : MakeMaterial();
        ringMaterial = new Material(baseMaterial);

        trackRenderer = MakeRing("Track", BuildRing(1f), new Material(baseMaterial));
        trackRenderer.sharedMaterial.SetColor("_BaseColor", trackColor);

        ringMesh = new Mesh { name = "TimerRing" };
        ringRenderer = MakeRing("Ring", ringMesh, ringMaterial);

        GameObject labelObject = new GameObject("Seconds", typeof(RectTransform));
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // Flat, readable from the player's side

        label = labelObject.AddComponent<TextMeshPro>();

        if (font != null)
        {
            label.font = font;
        }

        label.fontSize = 7f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(2f, 1.2f);
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(0, 0, 0, 255);

        SetVisible(0f);
    }

    // Start counting. canCount says when the clock may run (for example: no cards moving right now).
    public void Begin(float seconds, Action timeout, Func<bool> canCountNow)
    {
        duration = Mathf.Max(0.5f, seconds);
        remaining = duration;
        onTimeout = timeout;
        canCount = canCountNow;
        IsRunning = true;
    }

    public void Cancel()
    {
        IsRunning = false;
        onTimeout = null;
        canCount = null;
    }

    private void Update()
    {
        // Fade in while running, out when stopped
        shown = Mathf.MoveTowards(shown, IsRunning ? 1f : 0f, Time.unscaledDeltaTime * 6f);

        if (IsRunning && !GameMenu.IsPaused && (canCount == null || canCount()))
        {
            remaining -= Time.deltaTime;

            if (remaining <= 0f)
            {
                Action timeout = onTimeout;
                Cancel();
                timeout?.Invoke();
            }
        }

        Draw();
    }

    private void Draw()
    {
        SetVisible(shown);

        if (shown <= 0f)
        {
            return;
        }

        float fraction = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;
        BuildRing(fraction, ringMesh);

        Color color = remaining <= warningTime ? lastColor : fraction <= 0.5f ? hurryColor : calmColor;
        color.a *= shown;
        ringMaterial.SetColor("_BaseColor", color);

        label.text = Mathf.CeilToInt(Mathf.Max(0f, remaining)).ToString();
        label.color = new Color(color.r, color.g, color.b, shown);

        // The last seconds pulse
        float pulse = IsRunning && remaining <= warningTime ? 1f + Mathf.Abs(Mathf.Sin(remaining * Mathf.PI)) * 0.12f : 1f;
        transform.localScale = Vector3.one * pulse;
    }

    private void SetVisible(float amount)
    {
        bool visible = amount > 0f;

        if (ringRenderer.enabled != visible)
        {
            ringRenderer.enabled = visible;
            trackRenderer.enabled = visible;
            label.enabled = visible;
        }

        Color track = trackColor;
        track.a *= amount;
        trackRenderer.sharedMaterial.SetColor("_BaseColor", track);
    }

    private MeshRenderer MakeRing(string name, Mesh mesh, Material material)
    {
        GameObject ring = new GameObject(name);
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        ring.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = ring.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    private Mesh BuildRing(float fraction)
    {
        Mesh mesh = new Mesh { name = "TimerTrack" };
        BuildRing(fraction, mesh);
        return mesh;
    }

    // A flat ring from 12 o'clock going clockwise, as much of it as is left
    private void BuildRing(float fraction, Mesh mesh)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(segments * fraction));
        Vector3[] points = new Vector3[(steps + 1) * 2];
        int[] triangles = new int[steps * 6];
        float inner = radius - thickness * 0.5f;
        float outer = radius + thickness * 0.5f;

        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.PI * 2f * fraction * i / steps;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));   // 12 o'clock is toward the dealer
            points[i * 2] = direction * inner;
            points[i * 2 + 1] = direction * outer;

            if (i < steps)
            {
                int t = i * 6;
                int p = i * 2;
                triangles[t] = p;
                triangles[t + 1] = p + 1;
                triangles[t + 2] = p + 2;
                triangles[t + 3] = p + 2;
                triangles[t + 4] = p + 1;
                triangles[t + 5] = p + 3;
            }
        }

        mesh.Clear();
        mesh.vertices = points;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // See-through unlit, so the ring can fade
    private static Material MakeMaterial()
    {
        Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", 0f);   // Both sides
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return material;
    }
}
