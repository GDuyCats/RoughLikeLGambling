using UnityEditor;
using UnityEngine;

// Materials for the things built on the tables (shoe, discard holder, chip rack, seat frames), saved in Assets/Materials/Table.
// Made once; running the tools again reuses them, so colors changed by hand in the Inspector stay.
public static class TableMaterials
{
    private const string Folder = "Assets/Materials/Table";

    public static Material Wood => Lit("Wood", new Color(0.24f, 0.12f, 0.06f), 0f, 0.55f);
    public static Material DarkWood => Lit("DarkWood", new Color(0.12f, 0.06f, 0.03f), 0f, 0.5f);
    public static Material Gold => Lit("Gold", new Color(1f, 0.78f, 0.34f), 1f, 0.75f);
    public static Material BlackFelt => Lit("BlackFelt", new Color(0.05f, 0.05f, 0.06f), 0f, 0.1f);
    public static Material CardEdge => Lit("CardEdge", new Color(0.93f, 0.92f, 0.87f), 0f, 0.3f);
    public static Material CutCard => Lit("CutCard", new Color(1f, 0.85f, 0.1f), 0f, 0.4f);
    public static Material Acrylic => SeeThroughLit("RedAcrylic", new Color(0.65f, 0.05f, 0.08f, 0.45f));
    public static Material FrameLine => SeeThroughUnlit("SeatFrame", new Color(1f, 1f, 1f, 0.6f));

    private static Material Lit(string name, Color color, float metallic, float smoothness)
    {
        Material material = Load(name);

        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        return Save(material, name);
    }

    private static Material SeeThroughLit(string name, Color color)
    {
        Material material = Load(name);

        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.85f);
        MakeSeeThrough(material);
        return Save(material, name);
    }

    private static Material SeeThroughUnlit(string name, Color color)
    {
        Material material = Load(name);

        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", color);
        MakeSeeThrough(material);
        return Save(material, name);
    }

    public static void MakeSeeThrough(Material material)
    {
        material.SetFloat("_Surface", 1f);   // Transparent
        material.SetFloat("_Blend", 0f);     // Alpha
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private static Material Load(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/{name}.mat");
    }

    private static Material Save(Material material, string name)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        if (!AssetDatabase.IsValidFolder(Folder))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "Table");
        }

        material.name = name;
        AssetDatabase.CreateAsset(material, $"{Folder}/{name}.mat");
        return material;
    }

    // ---------- Building blocks ----------

    // A box with no collider, placed in its parent's space
    public static Transform Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        Object.DestroyImmediate(box.GetComponent<Collider>());
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box.transform;
    }

    public static Transform Ball(Transform parent, string name, Vector3 position, float size, Material material)
    {
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = name;
        Object.DestroyImmediate(ball.GetComponent<Collider>());
        ball.transform.SetParent(parent, false);
        ball.transform.localPosition = position;
        ball.transform.localScale = Vector3.one * size;
        ball.GetComponent<Renderer>().sharedMaterial = material;
        return ball.transform;
    }

    public static Transform Empty(Transform parent, string name, Vector3 position)
    {
        GameObject empty = new GameObject(name);
        empty.transform.SetParent(parent, false);
        empty.transform.localPosition = position;
        return empty.transform;
    }

    // Removes an old copy so a tool can build it fresh
    public static void RemoveOld(Transform parent, string name)
    {
        Transform old = parent.Find(name);

        if (old != null)
        {
            Undo.DestroyObjectImmediate(old.gameObject);
        }
    }
}
