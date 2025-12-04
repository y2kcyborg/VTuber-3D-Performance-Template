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
    
    private MaterialEditor materialEditor;
    private Material currentMaterial;
    
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
        // TODO Messy, based on example code I found
        
        // OnGUI and CreateGUI aren't friends. Move this bit down a little
        GUILayout.Space(120);
/*
        if (currentMaterial != null)
        {
            EditorGUI.BeginChangeCheck();
            
            currentMaterial = (Material) EditorGUILayout.ObjectField(currentMaterial, typeof(Material), true);
            
            if (EditorGUI.EndChangeCheck())
            {
                if (materialEditor != null)
                {
                    DestroyImmediate(materialEditor);
                }
            }
        }
*/
       
        
       
        if (currentMaterial != null)
        {
            if (materialEditor != null)
            {
                DestroyImmediate(materialEditor);
            }
            // I guess we actually do want to create one each frame, but that sucks
            // TODO check whether we can set materialEditor.target instead on a persistent one
            materialEditor = (MaterialEditor)Editor.CreateEditor(currentMaterial);

            
            // Must be expanded for this to work
            // WIP this won't work yet because there must be a PropertyEditor set 
            // such that PropertyEditor propertyViewer = materialEditor.propertyViewer as PropertyEditor;
            // and propertyViewer.tracker.activeEditors[0].target as GameObject == our material of interest
            
            // I feel like at that point, you might as well open the whole inspector window...
            UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(currentMaterial, true);
            materialEditor.DrawHeader();
            materialEditor.OnInspectorGUI();
            // GUIStyle bgColor = new GUIStyle();
            // bgColor.normal.background = previewBackgroundTexture;
            // materialEditor.OnInteractivePreviewGUI(GUILayoutUtility.GetRect (200,200), bgColor);
        }
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
                // Force inspector?
                // UnityEditor.Selection.activeObject = sharedMaterials[i];
                currentMaterial = sharedMaterials[i];
                Focus(); // grab focus, otherwise we guarantee nothing
                yield return null; // wait a frame
            }
            renderer.sharedMaterials = sharedMaterials;
        }
      
        currentMaterial = null;
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
        
        // Unity Toon shader gui doesn't implement ShaderGUI.ValidateMaterial!
        // Instead it's setting keywords in OnGUI.
        
        // Options:
        // - Legitimately create an editor window for one frame
        // - Hackily create something that pretends to be an editor window, hack into UTS3GUI's assembly and namespace
        // - Copy the logic from UTS3GUI - will execute fast, but be harder to maintain
        // - Hackily derive from UTS3GUI and provide a ValidateMaterial implementation; Override the shader gui per material.
        
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
