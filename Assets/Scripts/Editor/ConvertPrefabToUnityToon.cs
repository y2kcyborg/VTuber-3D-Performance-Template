using System.Collections;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using UniGLTF;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using UniVRM10;

public class ConvertPrefabToUnityToon : EditorWindow
{
    // Semi-magical UI Builder member
    [SerializeField]
    private VisualTreeAsset m_VisualTreeAsset = default;

    private Dictionary<string, Material> m_convertedMats;

    private Shader m_unityToonShader;
    private UnityEngine.Rendering.RenderPipelineAsset m_renderPipeline;
    
    // Using ID is faster than string.
    // We could also group these by shader but ugh
    private static int PropMainTex = Shader.PropertyToID("_MainTex");
    private static int PropColor = Shader.PropertyToID("_Color");
    
    private static int PropBaseAs1st = Shader.PropertyToID("_Use_BaseAs1st");
    private static int Prop1stAs2nd = Shader.PropertyToID("_Use_1stAs2nd");

    private static int PropAutoRenderQueue = Shader.PropertyToID("_AutoRenderQueue");
    private static int PropClippingMode = Shader.PropertyToID("_ClippingMode");
    
    private static int PropBlendMode = Shader.PropertyToID("_BlendMode");
    private static int PropCutoff = Shader.PropertyToID("_Cutoff");
    private static int PropCullMode = Shader.PropertyToID("_CullMode");
    
    private static int PropIsBaseMapAlphaAsClippingMask = Shader.PropertyToID("_IsBaseMapAlphaAsClippingMask");
    
    private static int PropTransparentEnabled = Shader.PropertyToID("_TransparentEnabled");
    
    private static int PropClippingLevel = Shader.PropertyToID("_Clipping_Level");
    
    // private static int Prop = Shader.PropertyToID("_");
    
    
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
        
        var imguiContainer = new IMGUIContainer(IMGUICode);
        root.Add(imguiContainer);
    }

    private bool ValidateProject()
    {
      
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset)
        {
            CoreEditorUtils.DrawFixMeBox("Invalid project settings:\n- Project must be using URP", EnableURP);
            return false;
        }

        return true;
    }

    private bool ValidateTarget(Object obj)
    {
        if (!obj)
        {
            return false;
        }
        string assetPath = AssetDatabase.GetAssetPath(obj);
        AssetImporter importer = AssetImporter.GetAtPath(assetPath);
        var vrmImporter = importer as UniVRM10.VrmScriptedImporter;
        return ValidateImporter(obj, vrmImporter);
    }

    private bool ValidateImporter(Object obj, VrmScriptedImporter importer)
    {
        if (importer == null)
        {
            CoreEditorUtils.DrawHeader("Not a VRM!");
            return false;
        }
        
        // Implicit: Render pipeline must be URP

        bool validMigrate = (obj is not DefaultAsset) || importer.MigrateToVrm1;
        bool validVrmPipeline = importer.RenderPipeline !=
                                ImporterRenderPipelineTypes.BuiltinRenderPipeline;
        bool validImportSettings = validMigrate && validVrmPipeline;
        if (!validImportSettings)
        {
            string msg = "Invalid VRM import settings:";
            if (!validMigrate)
            {
                msg += "\n- VRM must be migrated to VRM 1.0";
            }
            if (!validVrmPipeline)
            {
                msg += "\n- VRM must use URP materials";
            }

            CoreEditorUtils.DrawFixMeBox(msg,
                () => SetVRMImportSettings(importer));
            return false;
        }

        return true;
    }

    [UsedImplicitly]
    void IMGUICode()
    {
        var obj = rootVisualElement.Q<ObjectField>().value;

        if (m_unityToonShader == null)
        {
            m_unityToonShader = Shader.Find("Toon");
        }

        m_unityToonShader = (Shader)EditorGUILayout.ObjectField("Toon Shader", m_unityToonShader, typeof(Shader), false);
        
        // If the user passed in a VRM0 asset, unconverted:
        // obj is UnityEngine.DefaultAsset
        // VRM0 asset converted, or VRM1:
        // obj is VRM

        bool valid = ValidateProject() && ValidateTarget(obj);

        rootVisualElement.Q<Button>().SetEnabled( valid );
        
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
        if (materialsToResave.Count > 0)
        {
            AssetDatabase.StartAssetEditing();
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
            AssetDatabase.StopAssetEditing();
        }
    }

    private void EnableURP()
    {
        string[] guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
        if (guids.Length == 0)
        {
            Debug.LogError("No URP assets found!");
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        Debug.Log($"Choosing '{path}' as URP asset");

        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
        GraphicsSettings.defaultRenderPipeline = urp;
    }

    // Logic based on FilmGrainEditor.cs, haven't seen another good example...
    private void SetVRMImportSettings(VrmScriptedImporter importer)
    {
        var assetPath = importer.assetPath;
        importer.MigrateToVrm1 = true;
        importer.RenderPipeline = ImporterRenderPipelineTypes.UniversalRenderPipeline;
        EditorUtility.SetDirty(importer); // Okay, this is the crucial step that was missing
        importer.SaveAndReimport();
        AssetDatabase.Refresh(); // Shouldn't be required...
        // Restore the field after reimport
        var obj = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        rootVisualElement.Q<ObjectField>().value = obj;
    }

    public void ConvertPrefab()
    {
        if (m_unityToonShader == null)
        {
            Debug.LogError("Unity toon shader not found!");
            return;
        }

        var obj = rootVisualElement.Q<ObjectField>().value;
        
        // If the user passed in a VRM0 asset, unconverted:
        // obj is UnityEngine.DefaultAsset
        // VRM0 asset converted, or VRM1:
        // obj is VRM

        string assetPath = AssetDatabase.GetAssetPath(obj);
        Debug.Log($"Object '{obj.name}' has path '{assetPath}'");

        // We've ensured that the asset has been reimported as a VRM10 prefab with URP shaders,
        // now grab it.
        var gameObject = obj as GameObject;

        if (gameObject == null)
        {
            Debug.LogError($"'{obj.name}' is not a GameObject!");
            return;
        }
        
        if (PrefabUtility.GetPrefabAssetType(gameObject) == PrefabAssetType.NotAPrefab)
        {
            Debug.LogError($"'{obj.name}' is not a prefab!");
            return;
        }
        
        // Create required paths and directories
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
        Debug.Log($"Prefab path: '{prefabPath}'");

        string prefabDir = Path.GetDirectoryName(prefabPath);
        string prefabName = $"{Path.GetFileNameWithoutExtension(prefabPath)}_UnityToon";
        
        string newPrefabPath =
            $"{prefabDir}\\{prefabName}.prefab";
        Debug.Log($"Converted prefab path: '{newPrefabPath}'");
        
        string parentDir = Path.GetDirectoryName(assetPath);
        string matDirName = $"{prefabName}.Materials";
        string matDirPath = $"{parentDir}\\{matDirName}";
        
        if (!AssetDatabase.IsValidFolder(matDirPath))
        {
            AssetDatabase.CreateFolder(parentDir, matDirName);
        }
        
        // Create prefab instance
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        var prefabInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        prefabInstance.name += "_UnityToon";

        EditorCoroutineUtility.StartCoroutine( ConvertMaterialsCoroutine(prefabInstance, newPrefabPath, matDirPath), this );
    }

    // Async not well supported in 2022.3, so let's use Editor Coroutines
    // Maybe need to look into UniTask or whatever it was called
    public IEnumerator ConvertMaterialsCoroutine(GameObject prefabInstance, string newPrefabPath, string matDirPath)
    {
        m_convertedMats = new();
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
                sharedMaterials[i] = ConvertMaterial(matDirPath, sharedMaterials[i]);
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

    // Intermediate representation for the properties we know how to transfer.
    // Let's see if this grows into an unwieldy representation in itself
    public struct MaterialData
    {
        public Texture mainTex;
        public Vector2 mainTexOffset;
        public Vector2 mainTexScale;

        public Color color;

        // Opaque, Cutout, Transparent
        public float renderMode;
        
        // Off, Front, Back
        public float cullMode;

        public float cutoff;
    }

    // Parse the properties we know about
    // TODO register in a map instead
    public MaterialData ParseMaterial(Material mat)
    {
        if (mat.shader.name == "VRM/MToon")
        {
            return ParseMaterialMToon0(mat);
        }

        Debug.LogError($"No handler for material shader type: {mat.shader.name}");
        return new MaterialData();
    }

    public MaterialData ParseMaterialMToon0(Material mat)
    {
        var data = new MaterialData();
        
        data.mainTex = mat.GetTexture(PropMainTex);
        data.mainTexOffset = mat.GetTextureOffset(PropMainTex);
        data.mainTexScale = mat.GetTextureScale(PropMainTex);

        data.color = mat.GetColor(PropColor);

        // Enum values match between our RenderMode, MToon BlendMode, and UnityToon ClippingMode 
        data.renderMode = mat.GetFloat(PropBlendMode);

        data.cutoff = mat.GetFloat(PropCutoff);

        data.cullMode = mat.GetFloat(PropCullMode);
        
        // TODO: read MToon0 Shade Color texture
        
        // TODO: read MToon0 outline params
        
        return data;
    }

    public Material CreateUnityToon(MaterialData materialData)
    {
        var newMat = new Material(m_unityToonShader);
        
        // Here we need to set a minimal subset of properties, if we pick the right ones then the
        // shader gui will do the rest, after we force it to run in our OnGUI
        
        // Let's start with the simplest thing
        newMat.SetTexture(PropMainTex, materialData.mainTex);
        newMat.SetTextureOffset(PropMainTex, materialData.mainTexOffset);
        newMat.SetTextureScale(PropMainTex, materialData.mainTexScale);

        // TODO: check this prop has same effects, there are multiple in Unity Toon with similar names
        newMat.SetColor(PropColor, materialData.color);

        // Needed to for tex to be used for all 3 shade levels
        newMat.SetFloat(PropBaseAs1st, 1);
        newMat.SetFloat(Prop1stAs2nd, 1);

        // Opaque, Cutout, Transparent
        // These are annoying...
        newMat.SetFloat(PropClippingMode, (materialData.renderMode == 0) ? 0 : 2);
        newMat.SetFloat(PropTransparentEnabled, (materialData.renderMode == 2) ? 1 : 0);
        
        // Auto render queue on; set based on Cutout/Transparent/Opaque mode
        newMat.SetFloat(PropAutoRenderQueue, 1);
        
        // Cutoff for alpha clip; < vs <= discrepancy
        // newMat.SetFloat(PropCutoff, Mathf.Max(materialData.cutoff - 0.001f, 0));
        // Actually I think it's something else, and cutoff should stay
        // Why
        newMat.SetFloat(PropClippingLevel, Mathf.Max(materialData.cutoff - 0.001f, 0));
        
        
        // In our case this should always default to 1?
        newMat.SetFloat(PropIsBaseMapAlphaAsClippingMask, 1);
        
        // NOTE: _BlendMode, _SurfaceType are declared but not used by shader or gui

        // TODO: something is very messed up in UnityToon itself, if we have alpha clip on on the body tex and no
        // alpha clip?
        
        newMat.SetFloat(PropCullMode, materialData.cullMode);
        
        // TODO:
        // props in the UI:
        // Transparency On/Off
        // Clipping Off,On,Clip Transparency
        // Clipping Level
        // Transparency Level
        // Use Base Map Alpha as Clipping Mask (turn this on by default??)
        
        // TODO:
        // May need to set material.SetOverrideTag("RenderType", renderType); for opaque/transparent?
        
        // TODO: Disable outline by default, it's doing something weird wrt clipping.
        // It's a pain to enable/disable cross-render-pipeline, see UTS3GUI.GUI_Outline
        
        // TODO: okay, looks like MToon handles an alpha-clipped mat with outline just fine,
        // but UnityToon does not; look into how exactly the render passes are set up.
        // We have the option of generating additional textures, if that helps

        return newMat;
    }

    // TODO: consider an intermediate data structure that we can read from VRM shaders and write to UnityToon, etc shaders
    public Material ConvertMaterial(string destDirPath, Material mat)
    {
        string oldPath = AssetDatabase.GetAssetPath(mat);
        string key = $"{oldPath}:{mat.name}";
        
        if (m_convertedMats.TryGetValue(key, out Material newValue))
        {
            return newValue;
        }

        // Extract known properties from known shaders
        MaterialData materialData = ParseMaterial(mat);
        
        var newMat = CreateUnityToon( materialData );
        
        AssetDatabase.CreateAsset(
            newMat,
            $"{destDirPath}\\{mat.name}_UnityToon.mat");
        
        m_convertedMats[key] = newMat;
        
        return newMat;
    }
}
