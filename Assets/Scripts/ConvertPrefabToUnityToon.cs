using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class ConvertPrefabToUnityToon : EditorWindow
{
    [SerializeField]
    private VisualTreeAsset m_VisualTreeAsset = default;

    private Dictionary<string, Material> convertedMats;

    private static Shader s_unityToonShader;

    private static int PropMainTex = Shader.PropertyToID("_MainTex");
    private static int PropColor = Shader.PropertyToID("_Color");
    
    private static int PropBaseAs1st = Shader.PropertyToID("_Use_BaseAs1st");
    private static int Prop1stAs2nd = Shader.PropertyToID("_Use_1stAs2nd");
    
    [MenuItem("Window/VRM/ConvertPrefabToUnityToon")]
    public static void ShowExample()
    {
        ConvertPrefabToUnityToon wnd = GetWindow<ConvertPrefabToUnityToon>();
        wnd.titleContent = new GUIContent("ConvertPrefabToUnityToon");
    }

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        // Instantiate UXML
        VisualElement labelFromUXML = m_VisualTreeAsset.Instantiate();
        root.Add(labelFromUXML);

        root.Q<Button>().clicked += () => { ConvertPrefab(); };
    }

    public void ConvertPrefab()
    {
        var obj = rootVisualElement.Q<ObjectField>().value;
        var gameObject = obj as GameObject;
        if (gameObject == null)
        {
            Debug.LogError($"{obj.name} is not a GameObject!");
            return;
        }

        s_unityToonShader = Shader.Find("Toon");

        if (PrefabUtility.GetPrefabAssetType(gameObject) == PrefabAssetType.NotAPrefab)
        {
            Debug.LogError($"{obj.name} is not a prefab!");
            return;
        }

        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
        Debug.Log($"Prefab path: '{prefabPath}'");

        string newPrefabPath =
            $"{Path.GetDirectoryName(prefabPath)}\\{Path.GetFileNameWithoutExtension(prefabPath)}_UnityToon.prefab";
        Debug.Log($"Converted prefab path: '{newPrefabPath}'");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        var prefabInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        prefabInstance.name += "_UnityToon";

        ConvertMaterials(prefabInstance);

        PrefabUtility.SaveAsPrefabAssetAndConnect(prefabInstance, newPrefabPath, InteractionMode.UserAction);
    }

    public void ConvertMaterials(GameObject prefabInstance)
    {
        convertedMats = new();
        var renderers = prefabInstance.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            renderer.sharedMaterials = ConvertMaterials(renderer.sharedMaterials);
        }
        var renderersSkinned = prefabInstance.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var renderer in renderersSkinned)
        {
            renderer.sharedMaterials = ConvertMaterials(renderer.sharedMaterials);
        }
    }

    public Material[] ConvertMaterials(Material[] sharedMaterials)
    {
        for (int i = 0; i < sharedMaterials.Length; ++i)
        {
            var oldMat = sharedMaterials[i];
            string oldPath = AssetDatabase.GetAssetPath(oldMat);

            if (convertedMats.TryGetValue(oldPath, out Material newValue))
            {
                sharedMaterials[i] = newValue;
                continue;
            }

            string newPath = $"{Path.GetDirectoryName(oldPath)}\\{Path.GetFileNameWithoutExtension(oldPath)}_UnityToon.mat";

            var newMat = ConvertMaterial(oldMat);

            AssetDatabase.CreateAsset(newMat, newPath);
            
            convertedMats[oldPath] = newMat;
        }

        return sharedMaterials;
    }
    
    public Material ConvertMaterial(Material mat)
    {
        var newMat = new Material(s_unityToonShader);
        
        // Let's start with the simplest thing
        newMat.SetTexture(PropMainTex, mat.GetTexture(PropMainTex));
        newMat.SetTextureOffset(PropMainTex, mat.GetTextureOffset(PropMainTex));
        newMat.SetTextureScale(PropMainTex, mat.GetTextureScale(PropMainTex));

        // TODO: check this prop has same effects, there are multiple in Unity Toon with similar names
        newMat.SetColor(PropColor, mat.GetColor(PropColor));

        // Needed to for tex to be used for all 3 shade levels
        newMat.SetFloat(PropBaseAs1st, 1);
        newMat.SetFloat(Prop1stAs2nd, 1);

        float cutoff = mat.GetFloat("_Cutoff");
        // TODO look at ApplyQueueAndRenderType in UTS3GUI to see how to properly set the mat properties that will drive
        // blend mode etc
        
        return newMat;
    }
}
