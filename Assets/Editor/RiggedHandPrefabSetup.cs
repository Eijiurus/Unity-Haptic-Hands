using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// 编辑器工具：基于 Rigged Hand.fbx 生成左手 Prefab 并自动配置场景。
/// 使用方法：Unity 菜单栏 → Tools → Setup Rigged Hand Prefab
/// </summary>
public class RiggedHandPrefabSetup : EditorWindow
{
    const string FbxPath        = "Assets/Models/Rigged Hand.fbx";
    const string PrefabDir      = "Assets/Prefabs/Hands";
    const string LeftPrefabPath = PrefabDir + "/LeftHand.prefab";
    const string SkinMatPath    = "Assets/Materials/HandSkin.mat";

    [MenuItem("Tools/Setup Rigged Hand Prefab")]
    public static void SetupHand()
    {
        GameObject fbxModel = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxModel == null)
        {
            EditorUtility.DisplayDialog("错误",
                "找不到：" + FbxPath + "\n请确认 FBX 已导入到 Assets/Models/", "确定");
            return;
        }

        EnsureFolders();
        Material skinMat = GetOrCreateSkinMaterial();

        GameObject leftGo = CreateHandFromFBX(fbxModel, "LeftHand", skinMat);
        PrefabUtility.SaveAsPrefabAsset(leftGo, LeftPrefabPath);
        Object.DestroyImmediate(leftGo);

        AssetDatabase.Refresh();
        SetupScene();

        Debug.Log("[RiggedHandPrefabSetup] 完成！");
        EditorUtility.DisplayDialog("完成",
            "自动配置完成！\n\n" +
            "✔ 左手 Prefab 已生成（右手网格已禁用）\n" +
            "✔ 摄像机将在运行时自动对准手部\n" +
            "✔ GloveDataReceiver 已连接（键盘模拟模式）\n" +
            "✔ WitMotionConnector 已添加（自动扫描蓝牙传感器）\n\n" +
            "按 Play 后：\n" +
            "  · 摄像机自动对准手部模型\n" +
            "  · 按 1-5 键：单指弯曲\n" +
            "  · 按 Space：握拳\n\n" +
            "连接手套：取消勾选 Use Keyboard Simulation\n" +
            "连接姿态传感器：开启 WT9011DCL-BT50 电源即可",
            "好的");
    }

    static GameObject CreateHandFromFBX(GameObject fbxModel, string handName, Material skinMat)
    {
        GameObject go = Object.Instantiate(fbxModel);
        go.name = handName;

        foreach (Camera cam in go.GetComponentsInChildren<Camera>(true))
            Object.DestroyImmediate(cam.gameObject);
        foreach (Light light in go.GetComponentsInChildren<Light>(true))
            Object.DestroyImmediate(light.gameObject);

        // Blender 导出的灯光/摄像机对象可能没有 Unity 组件，按名称清理
        var toDestroy = new List<GameObject>();
        foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
        {
            if (child == go.transform) continue;
            string n = child.name;
            bool isBlenderObject = (n == "Hemi" || n == "Point" || n == "Sun" ||
                                    n == "Spot" || n == "Area" || n == "Camera" || n == "Lamp");
            if (isBlenderObject && child.GetComponent<Renderer>() == null)
                toDestroy.Add(child.gameObject);
        }
        foreach (var obj in toDestroy)
            Object.DestroyImmediate(obj);

        // 禁用右手网格：Blender 镜像的右手 localScale 为 (-1,-1,-1)
        foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.transform.localScale.x < 0)
            {
                smr.gameObject.SetActive(false);
                Debug.Log($"[RiggedHandPrefabSetup] 已禁用右手网格: {smr.gameObject.name} (scale={smr.transform.localScale})");
            }
            else
            {
                Debug.Log($"[RiggedHandPrefabSetup] 保留左手网格: {smr.gameObject.name} (scale={smr.transform.localScale})");
            }
        }

        foreach (Renderer rend in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = new Material[rend.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = skinMat;
            rend.sharedMaterials = mats;
        }

        DataGloveHandDriver driver = go.AddComponent<DataGloveHandDriver>();
        SerializedObject so = new SerializedObject(driver);
        SerializedProperty suffixProp = so.FindProperty("boneSuffix");
        if (suffixProp != null)
        {
            suffixProp.stringValue = ".L";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        HandInteractionRig hir = go.AddComponent<HandInteractionRig>();
        SerializedObject hirSo = new SerializedObject(hir);
        SerializedProperty hirSuffix = hirSo.FindProperty("boneSuffix");
        if (hirSuffix != null)
        {
            hirSuffix.stringValue = ".L";
            hirSo.ApplyModifiedPropertiesWithoutUndo();
        }

        return go;
    }

    static void SetupScene()
    {
        GameObject leftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeftPrefabPath);
        if (leftPrefab == null) return;

        DestroyAllByName("LeftHand");
        DestroyAllByName("RightHand");
        DestroyAllByName("GloveManager");
        DestroyAllByName("SceneSetup");
        DestroyAllByName("_HandSceneSetup");
        DestroyAllByName("HandCamera");
        DestroyAllByName("XR Device Simulator");
        DestroyAllByName("XR Interaction Setup");
        DestroyAllByName("XR Interaction Manager");
        DestroyAllByName("XR Origin");
        DestroyAllByName("XR Origin (XR Rig)");

        GameObject leftInScene = (GameObject)PrefabUtility.InstantiatePrefab(leftPrefab);
        leftInScene.name = "LeftHand";
        leftInScene.transform.position = Vector3.zero;

        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            Camera[] allCams = Object.FindObjectsOfType<Camera>();
            if (allCams.Length > 0)
                mainCam = allCams[0];
        }
        if (mainCam == null)
        {
            var camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            mainCam = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
        }

        mainCam.clearFlags = CameraClearFlags.Skybox;
        mainCam.nearClipPlane = 0.01f;
        mainCam.farClipPlane = 100f;
        mainCam.fieldOfView = 60f;

        // 清理摄像机上可能存在的旧脚本（包括 missing script）
        foreach (var comp in mainCam.GetComponents<Component>())
        {
            if (comp == null) // missing script
                continue;
            if (comp is HandSceneSetup)
                Object.DestroyImmediate(comp);
        }

        HandSceneSetup setup = mainCam.gameObject.AddComponent<HandSceneSetup>();
        SerializedObject setupSO = new SerializedObject(setup);
        SerializedProperty targetProp = setupSO.FindProperty("target");
        if (targetProp != null)
        {
            targetProp.objectReferenceValue = leftInScene.transform;
            setupSO.ApplyModifiedProperties();
        }

        GameObject gloveManager = new GameObject("GloveManager");
        GloveDataReceiver receiver = gloveManager.AddComponent<GloveDataReceiver>();
        gloveManager.AddComponent<WitMotionConnector>();

        WireGloveData(leftInScene, receiver);
        WireHandInteraction(leftInScene, receiver);

        Selection.activeGameObject = leftInScene;
        SceneView.FrameLastActiveSceneView();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[RiggedHandPrefabSetup] 场景配置完成。");
    }

    static void WireGloveData(GameObject handObj, GloveDataReceiver receiver)
    {
        DataGloveHandDriver driver = handObj.GetComponent<DataGloveHandDriver>();
        if (driver == null)
            driver = handObj.GetComponentInChildren<DataGloveHandDriver>();
        if (driver == null) return;

        SerializedObject so = new SerializedObject(driver);
        SerializedProperty gloveProp = so.FindProperty("gloveData");
        if (gloveProp != null)
        {
            gloveProp.objectReferenceValue = receiver;
            so.ApplyModifiedProperties();
            Debug.Log($"[RiggedHandPrefabSetup] {handObj.name} 的 GloveData 已连接");
        }
    }

    static void WireHandInteraction(GameObject handObj, GloveDataReceiver receiver)
    {
        HandInteractionRig hir = handObj.GetComponent<HandInteractionRig>();
        if (hir == null)
            hir = handObj.GetComponentInChildren<HandInteractionRig>();
        if (hir == null) return;

        SerializedObject so = new SerializedObject(hir);
        SerializedProperty p = so.FindProperty("gloveData");
        if (p != null)
        {
            p.objectReferenceValue = receiver;
            so.ApplyModifiedProperties();
            Debug.Log($"[RiggedHandPrefabSetup] {handObj.name} 的 HandInteractionRig 已连接 GloveDataReceiver");
        }
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabDir))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Hands");
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
    }

    static Material GetOrCreateSkinMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(SkinMatPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        Color skinColor = new Color(0.87f, 0.72f, 0.60f, 1f);
        mat.color = skinColor;
        mat.SetColor("_BaseColor", skinColor);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.3f);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0f);
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", 0f);

        AssetDatabase.CreateAsset(mat, SkinMatPath);
        return mat;
    }

    static void DestroyAllByName(string name)
    {
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>())
        {
            if (go != null && go.name.StartsWith(name))
                Object.DestroyImmediate(go);
        }
    }
}
