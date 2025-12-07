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
using VRM10.MToon10;
using System.Linq;

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
    private static int PropUTMainTex = Shader.PropertyToID("_MainTex");
    private static int PropUTColor = Shader.PropertyToID("_Color");
    
    private static int PropUTBaseAs1st = Shader.PropertyToID("_Use_BaseAs1st");
    private static int PropUT1stAs2nd = Shader.PropertyToID("_Use_1stAs2nd");

    private static int PropUTAutoRenderQueue = Shader.PropertyToID("_AutoRenderQueue");
    private static int PropUTClippingMode = Shader.PropertyToID("_ClippingMode");
    
    private static int PropUTBlendMode = Shader.PropertyToID("_BlendMode");
    private static int PropUTCutoff = Shader.PropertyToID("_Cutoff");
    private static int PropUTCullMode = Shader.PropertyToID("_CullMode");
    
    private static int PropUTIsBaseMapAlphaAsClippingMask = Shader.PropertyToID("_IsBaseMapAlphaAsClippingMask");
    
    private static int PropUTTransparentEnabled = Shader.PropertyToID("_TransparentEnabled");
    
    private static int PropUTClippingLevel = Shader.PropertyToID("_Clipping_Level");

    // Maybe corresponds to world vs screen?
    private static int PropUTOutlineMode = Shader.PropertyToID("_OUTLINE");
    private static int PropUTOutlineWidth = Shader.PropertyToID("_Outline_Width");
    private static int PropUTOutlineColor = Shader.PropertyToID("_Outline_Color");
    // private static int PropUT = Shader.PropertyToID("_");
   
    private static int PropM10AlphaMode = Shader.PropertyToID("_AlphaMode");
    private static int PropM10TransparentWithZWrite = Shader.PropertyToID("_TransparentWithZWrite");
    private static int PropM10AlphaCutoff = Shader.PropertyToID("_Cutoff");
    private static int PropM10RenderQueueOffsetNumber = Shader.PropertyToID("_RenderQueueOffset");
    private static int PropM10DoubleSided = Shader.PropertyToID("_DoubleSided");

    private static int PropM10BaseColorFactor = Shader.PropertyToID("_Color");
    private static int PropM10BaseColorTexture = Shader.PropertyToID("_MainTex");
    private static int PropM10ShadeColorFactor = Shader.PropertyToID("_ShadeColor");
    private static int PropM10ShadeColorTexture = Shader.PropertyToID("_ShadeTex");
    private static int PropM10NormalTexture = Shader.PropertyToID("_BumpMap");
    private static int PropM10NormalTextureScale = Shader.PropertyToID("_BumpScale");
    private static int PropM10ShadingShiftFactor = Shader.PropertyToID("_ShadingShiftFactor");
    private static int PropM10ShadingShiftTexture = Shader.PropertyToID("_ShadingShiftTex");
    private static int PropM10ShadingShiftTextureScale = Shader.PropertyToID("_ShadingShiftTexScale");
    private static int PropM10ShadingToonyFactor = Shader.PropertyToID("_ShadingToonyFactor");

    private static int PropM10GiEqualizationFactor = Shader.PropertyToID("_GiEqualization");

    private static int PropM10EmissiveFactor = Shader.PropertyToID("_EmissionColor");
    private static int PropM10EmissiveTexture = Shader.PropertyToID("_EmissionMap");

    private static int PropM10MatcapColorFactor = Shader.PropertyToID("_MatcapColor");
    private static int PropM10MatcapTexture = Shader.PropertyToID("_MatcapTex");
    private static int PropM10ParametricRimColorFactor = Shader.PropertyToID("_RimColor");
    private static int PropM10ParametricRimFresnelPowerFactor = Shader.PropertyToID("_RimFresnelPower");
    private static int PropM10ParametricRimLiftFactor = Shader.PropertyToID("_RimLift");
    private static int PropM10RimMultiplyTexture = Shader.PropertyToID("_RimTex");
    private static int PropM10RimLightingMixFactor = Shader.PropertyToID("_RimLightingMix");

    private static int PropM10OutlineWidthMode = Shader.PropertyToID("_OutlineWidthMode");
    private static int PropM10OutlineWidthFactor = Shader.PropertyToID("_OutlineWidth");
    private static int PropM10OutlineWidthMultiplyTexture = Shader.PropertyToID("_OutlineWidthTex");
    private static int PropM10OutlineColorFactor = Shader.PropertyToID("_OutlineColor");
    private static int PropM10OutlineLightingMixFactor = Shader.PropertyToID("_OutlineLightingMix");

    private static int PropM10UvAnimationMaskTexture = Shader.PropertyToID("_UvAnimMaskTex");
    private static int PropM10UvAnimationScrollXSpeedFactor = Shader.PropertyToID("_UvAnimScrollXSpeed");
    private static int PropM10UvAnimationScrollYSpeedFactor = Shader.PropertyToID("_UvAnimScrollYSpeed");
    private static int PropM10UvAnimationRotationSpeedFactor = Shader.PropertyToID("_UvAnimRotationSpeed");

    private static int PropM10UnityCullMode = Shader.PropertyToID("_M_CullMode");
    private static int PropM10UnitySrcBlend = Shader.PropertyToID("_M_SrcBlend");
    private static int PropM10UnityDstBlend = Shader.PropertyToID("_M_DstBlend");
    private static int PropM10UnityZWrite = Shader.PropertyToID("_M_ZWrite");
    private static int PropM10UnityAlphaToMask = Shader.PropertyToID("_M_AlphaToMask");

    private static int PropM10EditorEditMode = Shader.PropertyToID("_M_EditMode");
    
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

        public float outline;
        public float outlineWidth;
        public Color outlineColor;
    }

    // Parse the properties we know about
    // TODO register in a map instead
    public MaterialData ParseMaterial(Material mat)
    {
        if (mat.shader.name == "VRM10/MToon10" || mat.shader.name == "VRM10/Universal Render Pipeline/MToon10")
        {
            return ParseMaterialMToon10(mat);
        }

        Debug.LogError($"No handler for material shader type: {mat.shader.name}");
        return new MaterialData();
    }

    public MaterialData ParseMaterialMToon10(Material mat)
    {
        var data = new MaterialData();
        
        // TODO why does MToon have reflection over all the shader properties, but still end up accessing them by string name?
        
        // Some primary properties:
        // MToon10Prop.AlphaMode, enum matches standard values
        data.renderMode = mat.GetFloat(PropM10AlphaMode);
        // MToon10Prop.TransparentWithZWrite
        // NYI
        // MToon10Prop.RenderQueueOffsetNumber
        // NYI
        // MToon10Prop.DoubleSided
        data.cullMode = mat.GetFloat(PropM10DoubleSided) > 0 ? 0 : 2;
        // MToon10Prop.AlphaCutoff
        data.cutoff = mat.GetFloat(PropM10AlphaCutoff);
        
        // MToon10Prop.BaseColorTexture
        data.mainTex = mat.GetTexture(PropM10BaseColorTexture);
        // MToon10Prop.BaseColorFactor
        data.color = mat.GetColor(PropM10BaseColorFactor);
        // MToon10Prop.ShadeColorTexture
        // MToon10Prop.ShadeColorFactor
        // MToon10Prop.NormalTexture
        // MToon10Prop.NormalTextureScale
        // MToon10Prop.ShadingToonyFactor
        // MToon10Prop.ShadingShiftFactor
        // MToon10Prop.ShadingShiftTexture
        // MToon10Prop.ShadingShiftTextureScale
        // MToon10Prop.GiEqualizationFactor
        // MToon10Prop.EmissiveTexture
        // MToon10Prop.EmissiveFactor
        // MToon10Prop.RimMultiplyTexture
        // MToon10Prop.RimLightingMixFactor
        // MToon10Prop.MatcapTexture
        // MToon10Prop.MatcapColorFactor
        // MToon10Prop.ParametricRimColorFactor
        // MToon10Prop.ParametricRimFresnelPowerFactor
        // MToon10Prop.ParametricRimLiftFactor
        
        // MToon10Prop.OutlineWidthMode
        // Screen width not implemented in Unity Toon?
        data.outline = mat.GetFloat(PropM10OutlineWidthMode) > 0 ? 1 : 0;
        
        // MToon10Prop.OutlineWidthMultiplyTexture
        // MToon10Prop.OutlineWidthFactor
        data.outlineWidth = mat.GetFloat(PropM10OutlineWidthFactor);
        // MToon10Prop.OutlineColorFactor
        data.outlineColor = mat.GetColor(PropM10OutlineColorFactor);
        // MToon10Prop.OutlineLightingMixFactor
        
        // MToon10Prop.UvAnimationMaskTexture
        // MToon10Prop.UvAnimationScrollXSpeedFactor
        // MToon10Prop.UvAnimationScrollYSpeedFactor
        // MToon10Prop.UvAnimationRotationSpeedFactor
        
        // class MToonValidator used to validate?
        // Also doesn't implement standard ValidateMaterial...but we know
        // MToonValidator.Validate is how the validation happens, and
        // MToonInspector.OnGUI is where the primary properties are edited.
        
        return data;
    }
    
    public Material CreateUnityToon(MaterialData data)
    {
        var mat = new Material(m_unityToonShader);
        
        // Here we need to set a minimal subset of properties, if we pick the right ones then the
        // shader gui will do the rest, after we force it to run in our OnGUI
        
        // Let's start with the simplest thing
        mat.SetTexture(PropUTMainTex, data.mainTex);
        mat.SetTextureOffset(PropUTMainTex, data.mainTexOffset);
        // struct can't have default ctor, have to deal with this somewhere
        if (data.mainTexScale.x == 0) { data.mainTexScale.x = 1; }
        if (data.mainTexScale.y == 0) { data.mainTexScale.y = 1; }
        mat.SetTextureScale(PropUTMainTex, data.mainTexScale);

        // TODO: check this prop has same effects, there are multiple in Unity Toon with similar names
        mat.SetColor(PropUTColor, data.color);

        // Needed to for tex to be used for all 3 shade levels
        mat.SetFloat(PropUTBaseAs1st, 1);
        mat.SetFloat(PropUT1stAs2nd, 1);

        // Opaque, Cutout, Transparent
        // These are annoying...
        mat.SetFloat(PropUTClippingMode, (data.renderMode == 0) ? 0 : 2);
        mat.SetFloat(PropUTTransparentEnabled, (data.renderMode == 2) ? 1 : 0);
        
        // Auto render queue on; set based on Cutout/Transparent/Opaque mode
        mat.SetFloat(PropUTAutoRenderQueue, 1);
        
        // Cutoff for alpha clip; < vs <= discrepancy
        mat.SetFloat(PropUTClippingLevel, Mathf.Max(data.cutoff - 0.001f, 0));
        
        // In our case this should always default to 1?
        mat.SetFloat(PropUTIsBaseMapAlphaAsClippingMask, 1);
        
        mat.SetFloat(PropUTCullMode, data.cullMode);

        mat.SetShaderPassEnabled("SRPDefaultUnlit", data.outline > 0);
        mat.SetFloat(PropUTOutlineWidth, data.outlineWidth * 1000);
        mat.SetColor(PropUTOutlineColor, data.outlineColor);

        return mat;
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
