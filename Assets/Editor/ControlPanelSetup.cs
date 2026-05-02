using UnityEngine;
using UnityEngine.Events;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// T1 场景搭建：在当前场景下生成 ControlPanel（桌子 + 2 按钮 + 1 旋钮 + 2 灯）。
/// 使用方法：Unity 菜单栏 → Tools → Setup Control Panel
/// 重复执行会先删掉旧的 ControlPanel 再重建，方便迭代。
/// </summary>
public static class ControlPanelSetup
{
    const string MatDir       = "Assets/Materials/ControlPanel";
    const string GrayMatPath  = MatDir + "/Panel_Gray.mat";
    const string RedMatPath   = MatDir + "/Panel_Red.mat";
    const string BlueMatPath  = MatDir + "/Panel_Blue.mat";
    const string LampMatPath  = MatDir + "/Panel_Lamp.mat";

    const string FingerTipTag = "FingerTip";

    [MenuItem("Tools/Setup Control Panel")]
    public static void Build()
    {
        EnsureFolders();
        EnsureFingerTipTag();

        Material grayMat  = GetOrCreateLitMaterial(GrayMatPath, new Color(0.55f, 0.55f, 0.55f), 0.4f, 0.1f);
        Material redMat   = GetOrCreateLitMaterial(RedMatPath,  new Color(0.85f, 0.15f, 0.15f), 0.5f, 0.0f);
        Material blueMat  = GetOrCreateLitMaterial(BlueMatPath, new Color(0.15f, 0.35f, 0.85f), 0.5f, 0.0f);
        Material lampMat  = GetOrCreateLitMaterial(LampMatPath, new Color(1.0f, 0.95f, 0.7f),   0.7f, 0.0f, emission: new Color(1.0f, 0.9f, 0.5f) * 0.0f);

        // 移除场景中已有的同名物体，方便反复运行
        DestroyAllByName("ControlPanel");

        GameObject panel = new GameObject("ControlPanel");
        panel.transform.position = new Vector3(0f, 0f, 0.3f);

        // 1) 桌面
        GameObject table = CreatePrimitive(PrimitiveType.Cube, "Table", panel.transform,
            localPos: Vector3.zero,
            localScale: new Vector3(0.5f, 0.02f, 0.3f),
            material: grayMat);

        // 桌面顶面 y = 0.01；后续控件中心放在 y = 0.02 即可贴在桌上
        const float surfaceY = 0.02f;

        // 2) 按钮 01（红） — 桌面左侧
        BuildButton(panel.transform, "Button_01",
            localPos: new Vector3(-0.1f, surfaceY, 0f),
            visualMat: redMat,
            pressableIdleColor: GetMaterialBaseColor(redMat));

        // 3) 按钮 02（蓝） — 桌面右侧
        BuildButton(panel.transform, "Button_02",
            localPos: new Vector3(0.1f, surfaceY, 0f),
            visualMat: blueMat,
            pressableIdleColor: GetMaterialBaseColor(blueMat));

        // 4) 旋钮 — 桌面中间靠后
        BuildKnob(panel.transform, "Knob_01",
            localPos: new Vector3(0f, surfaceY, -0.08f),
            visualMat: grayMat);

        // 5) 两盏灯 — 桌面后方稍高的位置
        BuildLamp(panel.transform, "Lamp_01",
            localPos: new Vector3(-0.15f, 0.08f, -0.12f),
            bulbMat: lampMat);
        BuildLamp(panel.transform, "Lamp_02",
            localPos: new Vector3(0.15f, 0.08f, -0.12f),
            bulbMat: lampMat);

        WirePressableToggleToLamp(
            panel.transform.Find("Button_01/Visual")?.GetComponent<PressableButton>(),
            panel.transform.Find("Lamp_01")?.GetComponent<LampController>());
        WirePressableToggleToLamp(
            panel.transform.Find("Button_02/Visual")?.GetComponent<PressableButton>(),
            panel.transform.Find("Lamp_02")?.GetComponent<LampController>());

        Selection.activeGameObject = panel;
        SceneView.FrameLastActiveSceneView();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[ControlPanelSetup] ControlPanel 搭建完成。");
        EditorUtility.DisplayDialog("完成",
            "ControlPanel 已生成在 (0, 0, 0.3)。\n\n" +
            "包含：\n" +
            "  · Table（灰）\n" +
            "  · Button_01（红） + TriggerZone\n" +
            "  · Button_02（蓝） + TriggerZone\n" +
            "  · Knob_01（旋钮） + GrabZone\n" +
            "  · Lamp_01 / Lamp_02（LampController + Point Light）\n" +
            "  · Button_01.onPressed → Lamp_01.Toggle\n" +
            "  · Button_02.onPressed → Lamp_02.Toggle\n\n" +
            "FingerTip Tag 已确保存在；HandInteractionRig 运行时会给指尖球自动打这个 Tag。",
            "好的");
    }

    // ---------- 构造工具函数 ----------

    static void BuildButton(Transform parent, string name, Vector3 localPos, Material visualMat,
                            Color pressableIdleColor)
    {
        GameObject btn = new GameObject(name);
        btn.transform.SetParent(parent, false);
        btn.transform.localPosition = localPos;

        GameObject visual = CreatePrimitive(PrimitiveType.Cube, "Visual", btn.transform,
            localPos: Vector3.zero,
            localScale: new Vector3(0.04f, 0.02f, 0.04f),
            material: visualMat);

        Renderer visualRend = visual.GetComponent<Renderer>();
        PressableButton pressable = visual.AddComponent<PressableButton>();
        pressable.buttonRenderer = visualRend;
        pressable.idleColor = pressableIdleColor;
        pressable.pressedColor = Color.green;

        GameObject trigger = CreatePrimitive(PrimitiveType.Cube, "TriggerZone", btn.transform,
            localPos: Vector3.zero,
            localScale: new Vector3(0.06f, 0.06f, 0.06f),
            material: null);

        // 关掉 MeshRenderer，让触发体只用于碰撞检测
        var rend = trigger.GetComponent<MeshRenderer>();
        if (rend != null) rend.enabled = false;

        // CreatePrimitive 自带 BoxCollider，把它改成 Trigger
        var col = trigger.GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = true;

        ButtonTriggerZone zone = trigger.AddComponent<ButtonTriggerZone>();
        zone.TargetButton = pressable;
    }

    static Color GetMaterialBaseColor(Material mat)
    {
        if (mat == null)
            return new Color(0.5f, 0.5f, 0.5f);
        if (mat.HasProperty("_BaseColor"))
            return mat.GetColor("_BaseColor");
        return mat.color;
    }

    static void BuildKnob(Transform parent, string name, Vector3 localPos, Material visualMat)
    {
        GameObject knob = new GameObject(name);
        knob.transform.SetParent(parent, false);
        knob.transform.localPosition = localPos;

        // Cylinder 默认高度为 2，scale.y = 0.02 → 实际高度 0.04（≈ 4cm 厚的旋钮顶面）
        CreatePrimitive(PrimitiveType.Cylinder, "Visual", knob.transform,
            localPos: Vector3.zero,
            localScale: new Vector3(0.06f, 0.02f, 0.06f),
            material: visualMat);

        GameObject grab = CreatePrimitive(PrimitiveType.Sphere, "GrabZone", knob.transform,
            localPos: Vector3.zero,
            localScale: new Vector3(0.1f, 0.1f, 0.1f),
            material: null);

        var rend = grab.GetComponent<MeshRenderer>();
        if (rend != null) rend.enabled = false;

        var col = grab.GetComponent<SphereCollider>();
        if (col != null) col.isTrigger = true;
    }

    static void BuildLamp(Transform parent, string name, Vector3 localPos, Material bulbMat)
    {
        GameObject lamp = new GameObject(name);
        lamp.transform.SetParent(parent, false);
        lamp.transform.localPosition = localPos;

        GameObject bulb = CreatePrimitive(PrimitiveType.Sphere, "Bulb", lamp.transform,
            localPos: Vector3.zero,
            localScale: new Vector3(0.08f, 0.08f, 0.08f),
            material: bulbMat);

        GameObject lightGo = new GameObject("PointLight");
        lightGo.transform.SetParent(lamp.transform, false);
        lightGo.transform.localPosition = Vector3.zero;
        Light light = lightGo.AddComponent<Light>();
        light.type      = LightType.Point;
        light.intensity = 0f;
        light.range     = 0.6f;
        light.color     = new Color(1.0f, 0.9f, 0.7f);

        LampController ctrl = lamp.AddComponent<LampController>();
        ctrl.pointLight   = light;
        ctrl.bulbRenderer = bulb.GetComponent<Renderer>();
    }

    /// <summary>
    /// 在 PressableButton.onPressed 上添加持久化监听：调用 LampController.Toggle。
    /// </summary>
    static void WirePressableToggleToLamp(PressableButton button, LampController lamp)
    {
        if (button == null || lamp == null)
            return;

        SerializedObject so = new SerializedObject(button);
        SerializedProperty evtProp = so.FindProperty("onPressed");
        if (evtProp == null)
            return;

        SerializedProperty calls = evtProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
        if (calls == null)
            return;

        int idx = calls.arraySize;
        calls.arraySize++;
        SerializedProperty call = calls.GetArrayElementAtIndex(idx);

        call.FindPropertyRelative("m_Target").objectReferenceValue = lamp;

        SerializedProperty asm = call.FindPropertyRelative("m_TargetAssemblyTypeName");
        if (asm != null)
            asm.stringValue = typeof(LampController).AssemblyQualifiedName;

        call.FindPropertyRelative("m_MethodName").stringValue = nameof(LampController.Toggle);

        SerializedProperty mode = call.FindPropertyRelative("m_Mode");
        if (mode != null)
            mode.enumValueIndex = (int)PersistentListenerMode.Void;

        SerializedProperty callState = call.FindPropertyRelative("m_CallState");
        if (callState != null)
            callState.intValue = 2;

        so.ApplyModifiedProperties();
    }

    static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent,
                                      Vector3 localPos, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = localScale;

        if (material != null)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = material;
        }
        return go;
    }

    // ---------- 资源/标签辅助 ----------

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/Materials", "ControlPanel");
    }

    static Material GetOrCreateLitMaterial(string path, Color baseColor,
                                           float smoothness, float metallic,
                                           Color? emission = null)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.color = baseColor;
        if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   metallic);

        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", emission.Value);
        }

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFingerTipTag()
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");
        if (tagsProp == null) return;

        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == FingerTipTag)
                return;
        }

        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = FingerTipTag;
        tagManager.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log("[ControlPanelSetup] 已添加 Tag: " + FingerTipTag);
    }

    static void DestroyAllByName(string name)
    {
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>())
        {
            if (go != null && go.name == name)
                Object.DestroyImmediate(go);
        }
    }
}
