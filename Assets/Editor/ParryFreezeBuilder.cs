using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// A one-time editor tool: Tools > Set Up Parry Freeze.
// 1. Adds a "ParryHighlight" layer.
// 2. Creates the outline and paint materials in Assets/Materials/Parry.
// 3. Adds a "Render Objects" feature to the URP renderer, which draws the ParryHighlight layer
//    after post-processing (so the parrying character stays in color while the world turns grey).
// 4. Adds the ParryFreeze component next to the BossCombat and fills in its fields.
// Safe to run again: anything already there is reused.
public static class ParryFreezeBuilder
{
    private const string LayerName = "ParryHighlight";
    private const string FeatureName = "ParryHighlight";
    private const string MaterialFolder = "Assets/Materials/Parry";

    [MenuItem("Tools/Set Up Parry Freeze")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Tell("Please stop Play mode first, then run Tools > Set Up Parry Freeze again.");
            return;
        }

        BossCombat bossCombat = UnityEngine.Object.FindFirstObjectByType<BossCombat>(FindObjectsInactive.Include);

        if (bossCombat == null)
        {
            Tell("Could not find the BossCombat in the open scene. Is the game scene open?");
            return;
        }

        List<string> report = new List<string>();
        List<string> problems = new List<string>();

        // 1. The layer
        int layer = AddLayer(LayerName);

        if (layer < 0)
        {
            Tell("There is no free layer left for 'ParryHighlight'. Free one in Edit > Project Settings > Tags and Layers, then run this again.");
            return;
        }

        report.Add($"Layer '{LayerName}' is layer {layer}.");

        // 2. The materials
        Material outline = MakeUnlitMaterial("ParryOutline", new Color(0.15f, 0.45f, 1f), cullFront: true);
        Material splash = MakeUnlitMaterial("ParrySplash", new Color(0.2f, 0.6f, 1f), cullFront: false);

        if (outline == null || splash == null)
        {
            problems.Add("Could not find the URP Unlit shader, so the outline and paint materials were not made.");
        }
        else
        {
            report.Add($"Materials saved in {MaterialFolder}.");
        }

        // 3. The renderer feature, on every URP renderer in the project
        string[] rendererGuids = AssetDatabase.FindAssets("t:UniversalRendererData");

        if (rendererGuids.Length == 0)
        {
            problems.Add("No URP renderer asset was found, so the Render Objects feature was not added.");
        }

        foreach (string guid in rendererGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            string result = AddHighlightFeature(rendererData, layer);

            if (result.StartsWith("Could not"))
            {
                problems.Add(result);
            }
            else
            {
                report.Add(result);
            }
        }

        // 4. The component
        ParryFreeze freeze = bossCombat.GetComponent<ParryFreeze>();

        if (freeze == null)
        {
            freeze = Undo.AddComponent<ParryFreeze>(bossCombat.gameObject);
        }

        SerializedObject fields = new SerializedObject(freeze);
        fields.FindProperty("highlightLayerName").stringValue = LayerName;
        fields.FindProperty("outlineMaterial").objectReferenceValue = outline;
        fields.FindProperty("splashMaterial").objectReferenceValue = splash;

        TMP_FontAsset font = FindFont("Fredoka-Bold");

        if (font != null)
        {
            fields.FindProperty("font").objectReferenceValue = font;
        }

        // Reuse the camera shake the boss hits already use
        RunManager run = UnityEngine.Object.FindFirstObjectByType<RunManager>(FindObjectsInactive.Include);

        if (run != null)
        {
            UnityEngine.Object shake = new SerializedObject(run).FindProperty("hitShake").objectReferenceValue;

            if (shake != null)
            {
                fields.FindProperty("resumeShake").objectReferenceValue = shake;
            }
        }

        fields.ApplyModifiedProperties();
        report.Add($"ParryFreeze added to '{bossCombat.gameObject.name}'.");

        EditorSceneManager.MarkSceneDirty(bossCombat.gameObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = bossCombat.gameObject;

        string message = string.Join("\n", report);

        if (problems.Count > 0)
        {
            message += "\n\nPROBLEMS:\n" + string.Join("\n", problems);
        }

        Tell(message + "\n\nPress Ctrl + S to save the scene.");
    }

    // ---------- Layer ----------

    private static int AddLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);

        if (existing >= 0)
        {
            return existing;
        }

        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        // Layers 0 to 7 belong to Unity
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);

            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }

        return -1;
    }

    // ---------- Materials ----------

    private static Material MakeUnlitMaterial(string name, Color color, bool cullFront)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/Materials", "Parry");
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);

        // Cull front faces: only the inside of the bigger copy shows, which reads as an outline
        material.SetFloat("_Cull", cullFront ? (float)CullMode.Front : (float)CullMode.Back);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------- Renderer feature ----------

    private static string AddHighlightFeature(UniversalRendererData rendererData, int layer)
    {
        if (rendererData == null)
        {
            return "Could not load a URP renderer asset.";
        }

        ScriptableRendererFeature feature = rendererData.rendererFeatures.FirstOrDefault(f => f != null && f.name == FeatureName);

        if (feature == null)
        {
            // The built-in "Render Objects" feature, found by name so this works across URP versions
            Type renderObjectsType = TypeCache.GetTypesDerivedFrom<ScriptableRendererFeature>()
                .FirstOrDefault(t => t.Name == "RenderObjects");

            if (renderObjectsType == null)
            {
                return $"Could not find the Render Objects feature type. Add it to {rendererData.name} by hand.";
            }

            feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(renderObjectsType);
            feature.name = FeatureName;
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            SerializedObject data = new SerializedObject(rendererData);
            SerializedProperty list = data.FindProperty("m_RendererFeatures");
            SerializedProperty map = data.FindProperty("m_RendererFeatureMap");

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;

            if (map != null)
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            }

            data.ApplyModifiedProperties();
        }

        // Draw the ParryHighlight layer's solid objects again, after post-processing
        SerializedObject settings = new SerializedObject(feature);
        SerializedProperty passEvent = settings.FindProperty("settings.Event");
        SerializedProperty layerMask = settings.FindProperty("settings.filterSettings.LayerMask");
        SerializedProperty queue = settings.FindProperty("settings.filterSettings.RenderQueueType");

        if (passEvent == null || layerMask == null || queue == null)
        {
            return $"Could not set up the '{FeatureName}' feature on {rendererData.name}. Set it by hand: Event = AfterRenderingPostProcessing, Queue = Opaque, Layer Mask = {LayerName}.";
        }

        passEvent.intValue = (int)RenderPassEvent.AfterRenderingPostProcessing;
        layerMask.intValue = 1 << layer;
        queue.intValue = 0;   // Opaque
        settings.ApplyModifiedProperties();

        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(rendererData);
        return $"Render Objects feature '{FeatureName}' is on {rendererData.name}.";
    }

    // ---------- Helpers ----------

    private static TMP_FontAsset FindFont(string nameContains)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));

            if (font != null && font.name.Contains(nameContains))
            {
                return font;
            }
        }

        return null;
    }

    private static void Tell(string message)
    {
        EditorUtility.DisplayDialog("Set Up Parry Freeze", message, "OK");
    }
}
