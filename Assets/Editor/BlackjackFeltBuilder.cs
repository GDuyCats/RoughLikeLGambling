using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

// Used by Tools > Rearrange Tables. Lays the Blackjack table out like a real casino table:
// - a half-moon printed on the felt (the dealer on the straight side), the corners outside it a little darker,
// - three lines of text printed along arcs: payouts and the dealer's rule,
// - the shoe on the right, the discard on the left, the dealer's chip tray in the middle,
// - the player's hands sit on "seats" along an arc (split hands spread out around it).
// The table itself and the walkable area keep their shape: this only changes what is printed and where things sit.
// Safe to run again: the printed felt is rebuilt each time.
public static class BlackjackFeltBuilder
{
    private const string MeshFolder = "Assets/Materials/Blackjack";

    // In the arena's own space (x = left/right, z = toward the dealer): see TableSpots
    private static Vector3 ArcCenter => TableSpots.ArcCenter;
    private static float HalfMoonRadius => TableSpots.HalfMoonRadius;
    private static float TableHalfWidth => TableSpots.HalfWidth;
    private static float TableHalfDepth => TableSpots.HalfDepth;

    // The printed words: text, radius of the bottom of the letters, size
    private static readonly (string Text, float Radius, float Size)[] Lines =
    {
        ("BLACKJACK PAYS 5 TO 2", 6.1f, 6f),
        ("DEALER STANDS ON ALL 17s", 7.1f, 4.2f),
        ("INSURANCE PAYS 2 TO 1", 8.1f, 4.6f)
    };

    // The insurance band: two thin lines around "INSURANCE PAYS 2 TO 1"
    private const float BandInner = 7.5f;
    private const float BandOuter = 8.35f;

    private static readonly Color LineColor = new Color(0.95f, 0.95f, 0.9f);
    private static readonly Color TextColor = new Color(1f, 1f, 1f, 0.9f);
    private static readonly Color OutsideTint = new Color(0f, 0f, 0f, 0.28f);

    public static void Build(Transform arena, BlackjackGame game, TMP_FontAsset font, List<string> report)
    {
        // Fresh felt every time
        Transform old = arena.Find("BlackjackFelt");

        if (old != null)
        {
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        GameObject felt = new GameObject("BlackjackFelt");
        Undo.RegisterCreatedObjectUndo(felt, "Rearrange Tables");
        felt.transform.SetParent(arena, false);

        Material lineMaterial = TablePanelBuilder.UnlitMaterial("FeltLine", LineColor);
        Material tintMaterial = TintMaterial();

        AddMesh(felt.transform, "OutsideTint", SaveMesh(OutsideMesh(), "FeltOutside"), tintMaterial, 0.003f);
        AddMesh(felt.transform, "HalfMoonLine", SaveMesh(BandMesh(HalfMoonRadius - 0.08f, HalfMoonRadius + 0.08f), "FeltHalfMoon"), lineMaterial, 0.007f);
        AddMesh(felt.transform, "InsuranceLineInner", SaveMesh(BandMesh(BandInner - 0.04f, BandInner + 0.04f, 40f), "FeltBandInner"), lineMaterial, 0.007f);
        AddMesh(felt.transform, "InsuranceLineOuter", SaveMesh(BandMesh(BandOuter - 0.04f, BandOuter + 0.04f, 40f), "FeltBandOuter"), lineMaterial, 0.007f);

        foreach (var line in Lines)
        {
            AddArcText(felt.transform, line.Text, line.Radius, line.Size, font);
        }

        MoveSpots(arena, game);
        report.Add("Blackjack: half-moon felt with printed payouts, hands on seats along the arc.");
        CardShoeBuilder.Build(arena, game, report);
    }

    // ---------- Where things sit ----------

    private static void MoveSpots(Transform arena, BlackjackGame game)
    {
        SerializedObject fields = new SerializedObject(game);
        Transform dealerRow = fields.FindProperty("dealerRowStart").objectReferenceValue as Transform;
        Transform playerRow = fields.FindProperty("playerRowStart").objectReferenceValue as Transform;

        MoveTo(arena, dealerRow, TableSpots.DealerRowStart);
        MoveTo(arena, playerRow, TableSpots.PlayerRowStart);

        fields.FindProperty("seatArcRadius").floatValue = TableSpots.SeatRadius;
        fields.FindProperty("seatAngle").floatValue = TableSpots.SeatAngle;
        fields.FindProperty("seatCardSpread").floatValue = TableSpots.SeatCardSpread;
        fields.ApplyModifiedProperties();
    }

    // Keeps the height, sets x and z in the arena's space
    private static void MoveTo(Transform arena, Transform target, Vector3 spot)
    {
        if (target == null)
        {
            return;
        }

        Undo.RecordObject(target, "Rearrange Tables");
        Vector3 local = arena.InverseTransformPoint(target.position);
        target.position = arena.TransformPoint(new Vector3(spot.x, local.y, spot.z));
    }

    // ---------- Printed felt ----------

    private static void AddArcText(Transform parent, string words, float radius, float size, TMP_FontAsset font)
    {
        GameObject textObject = new GameObject(words, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = new Vector3(ArcCenter.x, 0.01f, ArcCenter.z - radius);
        textObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // Flat on the felt, readable from the player's side

        TextMeshPro text = textObject.AddComponent<TextMeshPro>();

        if (font != null)
        {
            text.font = font;
        }

        text.rectTransform.sizeDelta = new Vector2(30f, 2f);
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 4f;
        text.alignment = TextAlignmentOptions.Baseline;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = TextColor;
        text.text = words;

        ArcText arc = textObject.AddComponent<ArcText>();
        SerializedObject arcFields = new SerializedObject(arc);
        arcFields.FindProperty("radius").floatValue = radius;
        arcFields.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddMesh(Transform parent, string name, Mesh mesh, Material material, float height)
    {
        GameObject meshObject = new GameObject(name);
        meshObject.transform.SetParent(parent, false);
        meshObject.transform.localPosition = new Vector3(0f, height, 0f);
        meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // A point at this angle and distance from the half-moon's center.
    // Angle 0 points at the player; -90 is the left end of the straight side, +90 the right end.
    private static Vector3 Around(float degrees, float radius)
    {
        float angle = degrees * Mathf.Deg2Rad;
        return new Vector3(ArcCenter.x + Mathf.Sin(angle) * radius, 0f, ArcCenter.z - Mathf.Cos(angle) * radius);
    }

    // A curved strip between two radii (a printed line), from -maxAngle to +maxAngle
    private static Mesh BandMesh(float inner, float outer, float maxAngle = 90f)
    {
        const int segments = 96;
        List<Vector3> points = new List<Vector3>();
        List<int> triangles = new List<int>();

        for (int i = 0; i <= segments; i++)
        {
            float degrees = Mathf.Lerp(-maxAngle, maxAngle, i / (float)segments);
            points.Add(Around(degrees, inner));
            points.Add(Around(degrees, outer));

            if (i > 0)
            {
                AddQuad(triangles, points.Count - 4, points.Count - 3, points.Count - 2, points.Count - 1);
            }
        }

        return MakeMesh(points, triangles);
    }

    // The table's front corners, outside the half-moon: from the curve out to the table's edge
    private static Mesh OutsideMesh()
    {
        List<float> angles = new List<float>();

        for (int i = 0; i <= 120; i++)
        {
            angles.Add(Mathf.Lerp(-90f, 90f, i / 120f));
        }

        // The table's corners must be sampled exactly, or they get cut off
        float cornerAngle = Mathf.Atan2(TableHalfWidth, ArcCenter.z + TableHalfDepth) * Mathf.Rad2Deg;
        angles.Add(-cornerAngle);
        angles.Add(cornerAngle);
        angles.Sort();

        List<Vector3> points = new List<Vector3>();
        List<int> triangles = new List<int>();

        foreach (float degrees in angles)
        {
            points.Add(Around(degrees, HalfMoonRadius));
            points.Add(Around(degrees, DistanceToEdge(degrees)));

            if (points.Count >= 4)
            {
                AddQuad(triangles, points.Count - 4, points.Count - 3, points.Count - 2, points.Count - 1);
            }
        }

        return MakeMesh(points, triangles);
    }

    // How far a line from the half-moon's center, at this angle, goes before leaving the table
    private static float DistanceToEdge(float degrees)
    {
        float angle = degrees * Mathf.Deg2Rad;
        float sideways = Mathf.Abs(Mathf.Sin(angle));
        float forward = Mathf.Cos(angle);

        float toSide = sideways > 0.0001f ? TableHalfWidth / sideways : float.MaxValue;
        float toFront = forward > 0.0001f ? (ArcCenter.z + TableHalfDepth) / forward : float.MaxValue;
        return Mathf.Max(HalfMoonRadius, Mathf.Min(toSide, toFront));
    }

    // Two triangles from (a, b) on one side to (c, d) on the next, facing up
    private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(c); triangles.Add(b); triangles.Add(d);
    }

    private static Mesh MakeMesh(List<Vector3> points, List<int> triangles)
    {
        // Make every triangle face up (the camera looks down at the felt)
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 normal = Vector3.Cross(points[triangles[i + 1]] - points[triangles[i]], points[triangles[i + 2]] - points[triangles[i]]);

            if (normal.y < 0f)
            {
                (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            }
        }

        Mesh mesh = new Mesh();
        mesh.SetVertices(points);
        mesh.SetTriangles(triangles, 0);

        Vector3[] normals = new Vector3[points.Count];

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.up;
        }

        mesh.normals = normals;
        mesh.RecalculateBounds();
        return mesh;
    }

    // Meshes are saved as files, so they stay after the scene is closed and opened again
    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        EnsureFolder();
        string path = $"{MeshFolder}/{name}.asset";

        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }

        mesh.name = name;
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // See-through black, so the corners are a darker shade of the same felt
    private static Material TintMaterial()
    {
        EnsureFolder();
        string path = $"{MeshFolder}/FeltShade.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            return null;
        }

        material = new Material(shader);
        material.SetColor("_BaseColor", OutsideTint);
        material.SetFloat("_Surface", 1f);   // Transparent
        material.SetFloat("_Blend", 0f);     // Alpha
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        if (!AssetDatabase.IsValidFolder(MeshFolder))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "Blackjack");
        }
    }
}
