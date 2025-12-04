using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.EditorCoroutines.Editor;
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

    private static int ShaderPropAutoRenderQueue = Shader.PropertyToID("_AutoRenderQueue");
    
    private List<Material> materialsToResave = new();
    
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

    public void OnGUI()
    {
        // Unity Toon shader gui doesn't implement ShaderGUI.ValidateMaterial!
        // Instead it's setting keywords in OnGUI.
        
        // Options:
        // - Legitimately create an editor window for one frame
        // - Hackily create something that pretends to be an editor window, hack into UTS3GUI's assembly and namespace
        // - Copy the logic from UTS3GUI - will execute fast, but be harder to maintain
        // - Hackily derive from UTS3GUI and provide a ValidateMaterial implementation; Override the shader gui per material.

        // The least bad option is to force grab the focus and force embed an open inspector for the material
        
        // This is working correctly if the materials are saved with `_IS_ANGELRING_OFF` in `m_ValidKeywords`.
        
        // Draw all the inspectors, open, on top of each other, just to force UTS3GUI.OnGUI to run
        // Somehow, this works
        AssetDatabase.StartAssetEditing();
        if (materialsToResave.Count > 0)
        {
            for (int i = 0; i < materialsToResave.Count; ++i)
            {
                GUILayout.BeginArea(new Rect (0,120,1920,1080));
                var mat = materialsToResave[i];
                var materialEditor = (MaterialEditor)Editor.CreateEditor(mat);
                // For some reason, this flag has to be set on the material. Not the editor.
                UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(mat, true);
                materialEditor.DrawHeader();
                materialEditor.OnInspectorGUI();
                GUILayout.EndArea();
                DestroyImmediate(materialEditor); // Destroy after end of frame
            }
            materialsToResave.Clear();
        }
        AssetDatabase.StopAssetEditing();
    }

    public void ConvertPrefab()
    {
        s_unityToonShader = Shader.Find("Toon");
        var obj = rootVisualElement.Q<ObjectField>().value;
        var gameObject = obj as GameObject;
        if (gameObject == null)
        {
            Debug.LogError($"{obj.name} is not a GameObject!");
        }
        else if (PrefabUtility.GetPrefabAssetType(gameObject) == PrefabAssetType.NotAPrefab)
        {
            Debug.LogError($"{obj.name} is not a prefab!");
        }
        else
        {
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            Debug.Log($"Prefab path: '{prefabPath}'");

            string newPrefabPath =
                $"{Path.GetDirectoryName(prefabPath)}\\{Path.GetFileNameWithoutExtension(prefabPath)}_UnityToon.prefab";
            Debug.Log($"Converted prefab path: '{newPrefabPath}'");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            var prefabInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            prefabInstance.name += "_UnityToon";

            // AssetDatabase.StartAssetEditing(); // speeds up when doing lots of asset db operations in a row
            EditorCoroutineUtility.StartCoroutine( ConvertMaterialsCoroutine(prefabInstance, newPrefabPath), this );
            // AssetDatabase.StopAssetEditing();

        }
    }

    // Async not well supported in 2022.3, so let's use Editor Coroutines
    // Maybe need to look into UniTask or whatever it was called
    public IEnumerator ConvertMaterialsCoroutine(GameObject prefabInstance, string newPrefabPath)
    {
        convertedMats = new();
        var renderers = prefabInstance.GetComponentsInChildren<Renderer>();
        
        int totalRenderers = renderers.Length;
        int currentIdx = 0;
        
        int id = Progress.Start("Converting Materials...");
        
        foreach (var renderer in renderers)
        {
            Progress.Report(id, currentIdx, totalRenderers);
            currentIdx++;
            Material[] sharedMaterials = renderer.sharedMaterials;
            for (int i = 0; i < sharedMaterials.Length; ++i)
            {
                sharedMaterials[i] = ConvertMaterial(sharedMaterials[i]);
                materialsToResave.Add(sharedMaterials[i]);
            }
            renderer.sharedMaterials = sharedMaterials;
        }

        Focus();
        yield return null;
        
        // wait a frame
        PrefabUtility.SaveAsPrefabAssetAndConnect(prefabInstance, newPrefabPath, InteractionMode.UserAction);
        AssetDatabase.SaveAssets();
        Progress.Remove(id);
    }

    public Material ConvertMaterial(Material mat)
    {
        string oldPath = AssetDatabase.GetAssetPath(mat);

        if (convertedMats.TryGetValue(oldPath, out Material newValue))
        {
            return newValue;
        }

        string newPath = $"{Path.GetDirectoryName(oldPath)}\\{Path.GetFileNameWithoutExtension(oldPath)}_UnityToon.mat";

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
        
        // Auto render queue on; set based on Cutout/Transparent/Opaque mode
        newMat.SetFloat(ShaderPropAutoRenderQueue, 1);
        
        // Here we need to set a minimal subset of properties, if we pick the right ones then the
        // shader gui will do the rest
        
        // force material verify so keywords are set before we save it?
        
        // string currentCustomEditor = ShaderUtil.GetCurrentCustomEditor(s_unityToonShader);
        // this.m_CustomShaderGUI = ShaderGUIUtility.CreateShaderGUI(this.m_CustomEditorClassName);
        
        // We could put this material editor in the tool window, and iterate through the created materials...ugh,
        // why did they not correctly implement the gui??
        // var matEditor = Editor.CreateEditor(newMat);
        // matEditor.DrawHeader();
        // matEditor.OnInspectorGUI();
        
        // All of the options are very annoying. The GUI is quite convoluted.
        
        AssetDatabase.CreateAsset(newMat, newPath);
        
        convertedMats[oldPath] = newMat;
        
        return newMat;
    }
}
