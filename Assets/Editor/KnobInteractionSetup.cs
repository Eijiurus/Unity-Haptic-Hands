using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键把当前场景里的旋钮交互配齐：
///   GrabZone 上挂 SphereCollider / Rigidbody / TouchableObject / GrabbableKnobAdapter；
///   Adapter 的 4 个引用全部填好（含 v7 的 handLockTarget）；
///   RotaryKnob 的 positionSource / useRawWristPosition 填好（v7：抓取期间手被锁住时仍能取到 raw 位置）；
///   MasterController 上挂 MasterBrightnessController，自动填好 lamps；
///   把 RotaryKnob.onValueChanged 通过 UnityEventTools 持久绑定到 master.SetGlobalBrightness。
///
/// 使用：菜单栏 Tools → Setup Knob Interaction。
/// 可重复执行：会先清掉 onValueChanged 里所有现存 persistent listener 再重新挂，避免叠加。
/// 操作走 Undo 系统，Ctrl+Z 可一步回退。
/// </summary>
public static class KnobInteractionSetup
{
    private const string UndoLabel = "Setup Knob Interaction";

    private const string KnobName = "Knob_01";
    private const string VisualChild = "Visual";
    private const string GrabZoneChild = "GrabZone";
    private const string MasterControllerName = "MasterController";

    [MenuItem("Tools/Setup Knob Interaction")]
    public static void Run()
    {
        try
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);

            var summary = new List<string>();

            // 1) 找 Knob_01
            var knobGO = GameObject.Find(KnobName);
            if (knobGO == null)
            {
                Fail($"Could not find a GameObject named '{KnobName}' in the active scene. " +
                     $"Run 'Tools/Setup Control Panel' first, or create '{KnobName}' manually.");
                return;
            }

            // 2) 找 Visual 和 GrabZone（直接子物体）
            var visualTransform = knobGO.transform.Find(VisualChild);
            var grabZoneTransform = knobGO.transform.Find(GrabZoneChild);
            if (visualTransform == null)
            {
                Fail($"'{KnobName}' has no direct child named '{VisualChild}'. " +
                     $"Expected hierarchy: {KnobName}/{VisualChild} (with RotaryKnob) and {KnobName}/{GrabZoneChild}.");
                return;
            }
            if (grabZoneTransform == null)
            {
                Fail($"'{KnobName}' has no direct child named '{GrabZoneChild}'. " +
                     $"Expected hierarchy: {KnobName}/{VisualChild} and {KnobName}/{GrabZoneChild}.");
                return;
            }

            var rotaryKnob = visualTransform.GetComponent<RotaryKnob>();
            if (rotaryKnob == null)
            {
                Fail($"'{KnobName}/{VisualChild}' has no RotaryKnob component. " +
                     $"Add one to the Visual child before running this menu.");
                return;
            }

            var grabZoneGO = grabZoneTransform.gameObject;

            // 2.5) 清理 Knob_01 根节点上残留的"旧抓取器"。
            // 历史成因：Tools/Setup Control Panel 会在 Knob_01 根直接挂 TouchableObject(+RB)。
            // 在 v7 / GrabZone 工作流下，这套旧抓取器会和 GrabZone 上的新抓取器同时存在，
            // 而手指 pinch 命中 Visual.CapsuleCollider 时，HandInteractionRig 沿父链找
            // TouchableObject 会先撞到 Knob_01 根上那个，把整个旋钮抓走——
            // 同时新版 GrabZone 链路完全不会被触发，导致：
            //   - 旋钮跟手晃动、不固定
            //   - 手腕不上锁、旋钮不旋转、灯亮度不变
            // 因此必须先把根上的 GrabbableKnobAdapter / TouchableObject 移除。
            // 注意顺序：adapter 用 [RequireComponent(typeof(TouchableObject))]，必须先删 adapter 再删 touchable。
            var rootAdapter = knobGO.GetComponent<GrabbableKnobAdapter>();
            if (rootAdapter != null)
            {
                Undo.DestroyObjectImmediate(rootAdapter);
                summary.Add($"Removed stale GrabbableKnobAdapter from '{KnobName}' root (it would have hijacked the grab path).");
            }
            var rootTouchable = knobGO.GetComponent<TouchableObject>();
            if (rootTouchable != null)
            {
                Undo.DestroyObjectImmediate(rootTouchable);
                summary.Add($"Removed stale TouchableObject from '{KnobName}' root (no longer needed; grab now lives on '{GrabZoneChild}').");
            }
            // Knob_01 根上的 Rigidbody 在没有 TouchableObject 后也不再被任何东西需要，但它无害；保留它以免破坏其他可能的引用。
            // 如果你不想让 Knob_01 根带 Rigidbody，可以手动在 Inspector 里删除。

            // 3) 配置 GrabZone
            var collider = grabZoneGO.GetComponent<Collider>();
            if (collider == null)
            {
                var sphere = Undo.AddComponent<SphereCollider>(grabZoneGO);
                // 默认 radius=0.5 已与 Unity 原生 Sphere mesh 的本地半径匹配；
                // GrabZone 通过自身 transform.localScale 缩放到旋钮尺寸。
                sphere.radius = 0.5f;
                collider = sphere;
                summary.Add($"Added SphereCollider to {GrabZoneChild}.");
            }
            Undo.RecordObject(collider, UndoLabel);
            collider.isTrigger = false;

            var meshRenderer = grabZoneGO.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.enabled)
            {
                Undo.RecordObject(meshRenderer, UndoLabel);
                meshRenderer.enabled = false;
                summary.Add($"Disabled {GrabZoneChild} MeshRenderer (invisible grab volume).");
            }

            var rb = GetOrAddComponent<Rigidbody>(grabZoneGO, summary, GrabZoneChild);
            Undo.RecordObject(rb, UndoLabel);
            rb.isKinematic = true;
            rb.useGravity = false;

            var touchable = GetOrAddComponent<TouchableObject>(grabZoneGO, summary, GrabZoneChild);
            var adapter   = GetOrAddComponent<GrabbableKnobAdapter>(grabZoneGO, summary, GrabZoneChild);

            // 4) 接 GrabbableKnobAdapter 的引用
            var handDriver = UnityEngine.Object.FindFirstObjectByType<DataGloveHandDriver>();

            Undo.RecordObject(adapter, UndoLabel);
            adapter.touchable      = touchable;
            adapter.targetKnob     = rotaryKnob;
            adapter.handDriver     = handDriver;
            adapter.handLockTarget = visualTransform; // snap-to-grab 用 Visual 中心而不是 GrabZone，更"贴"在旋钮上
            EditorUtility.SetDirty(adapter);
            summary.Add($"Wired GrabbableKnobAdapter (touchable, targetKnob, handDriver={(handDriver != null ? "found" : "null")}, handLockTarget={VisualChild}).");

            if (handDriver == null)
            {
                Debug.LogWarning("[KnobInteractionSetup] No DataGloveHandDriver found in scene. " +
                                 "adapter.handDriver and rotaryKnob.positionSource left null — wrist locking and raw-position fallback are disabled. " +
                                 "Drop a DataGloveHandDriver into the scene and run this menu again.");
            }

            // 4b) 接 RotaryKnob 的 v7 字段
            Undo.RecordObject(rotaryKnob, UndoLabel);
            rotaryKnob.positionSource       = handDriver;
            rotaryKnob.useRawWristPosition  = true;
            EditorUtility.SetDirty(rotaryKnob);
            summary.Add($"Wired RotaryKnob.positionSource={(handDriver != null ? "DataGloveHandDriver" : "null")}, useRawWristPosition=true.");

            // 5) MasterController + MasterBrightnessController
            var masterGO = GameObject.Find(MasterControllerName);
            if (masterGO == null)
            {
                masterGO = new GameObject(MasterControllerName);
                Undo.RegisterCreatedObjectUndo(masterGO, UndoLabel);
                summary.Add($"Created '{MasterControllerName}' GameObject at scene root.");
            }

            var master = GetOrAddComponent<MasterBrightnessController>(masterGO, summary, MasterControllerName);

            // 找全场景里所有 LampController（含未启用的，方便用户把灯先关着也能配齐）
            var lamps = UnityEngine.Object.FindObjectsByType<LampController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            Undo.RecordObject(master, UndoLabel);
            master.lamps = lamps;
            EditorUtility.SetDirty(master);
            summary.Add($"Filled MasterBrightnessController.lamps with {lamps.Length} LampController(s).");

            // 6) onValueChanged → master.SetGlobalBrightness（持久绑定）
            // 先清掉所有现存 persistent listener，避免重复执行时叠加。
            // 注意：如果用户手动添加过其他 persistent 监听，本步会一并清掉——这是"一键工具"的取舍。
            Undo.RecordObject(rotaryKnob, UndoLabel);
            int existing = rotaryKnob.onValueChanged.GetPersistentEventCount();
            for (int i = existing - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(rotaryKnob.onValueChanged, i);
            }

            // dynamic-float 重载：method group 必须显式转成 UnityAction<float>，
            // 否则编译器会去找 UnityAction（无参版）的重载，导致选中"static value"模式而不是 dynamic。
            UnityAction<float> action = master.SetGlobalBrightness;
            UnityEventTools.AddPersistentListener(rotaryKnob.onValueChanged, action);
            EditorUtility.SetDirty(rotaryKnob);
            summary.Add($"Bound RotaryKnob.onValueChanged → MasterBrightnessController.SetGlobalBrightness " +
                        $"(removed {existing} previous persistent listener(s)).");

            // 7) 标记场景脏
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            // 折叠所有 Undo 操作为一组，Ctrl+Z 一次撤销整次配置
            Undo.CollapseUndoOperations(undoGroup);

            // 8) 反馈
            string body = string.Join("\n• ", summary.ToArray());
            string fullMessage = "Setup complete:\n• " + body;
            Debug.Log("[KnobInteractionSetup] " + fullMessage, knobGO);
            EditorUtility.DisplayDialog("Setup Knob Interaction", fullMessage, "OK");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog(
                "Setup Knob Interaction failed",
                $"Setup aborted with an exception:\n\n{ex.GetType().Name}: {ex.Message}\n\nSee Console for full stack trace.",
                "OK");
        }
    }

    private static T GetOrAddComponent<T>(GameObject go, List<string> summary, string ownerLabel) where T : Component
    {
        var existing = go.GetComponent<T>();
        if (existing != null) return existing;
        var added = Undo.AddComponent<T>(go);
        summary.Add($"Added {typeof(T).Name} to {ownerLabel}.");
        return added;
    }

    private static void Fail(string message)
    {
        Debug.LogError("[KnobInteractionSetup] " + message);
        EditorUtility.DisplayDialog("Setup Knob Interaction failed", message, "OK");
    }
}
