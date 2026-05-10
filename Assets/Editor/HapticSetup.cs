using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键把场景里的按钮/旋钮接到 HapticOutput 上：
///   1) 找/建 HapticController (空 GameObject + HapticOutput 组件)
///   2) 给每个 PressableButton 的 onPressed 接 HapticOutput.PlayButtonClick
///   3) 给每个 RotaryKnob 的 onStepClicked 接 HapticOutput.PlayKnobStep
///
/// 重复执行：会先把 PlayButtonClick / PlayKnobStep 的旧 persistent listener 清掉再重接，
/// 不会污染你已经手动加的其他事件监听（按 method name 精确匹配，只删自己加的）。
///
/// 注意：本菜单只配 Unity 端。HID 实际驱动靠 Assets/Scripts/HapticBridge.py，
/// 需要在另一个终端里执行：
///     python "Assets/Scripts/HapticBridge.py"
/// 它会监听 127.0.0.1:5006 并把指令转给 DRV2605 板子。
/// </summary>
public static class HapticSetup
{
    private const string UndoLabel = "Setup Haptics";
    private const string ControllerName = "HapticController";

    [MenuItem("Tools/Setup Haptics")]
    public static void Run()
    {
        try
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);

            var summary = new List<string>();

            // 1) HapticController GameObject + HapticOutput 组件
            var controllerGO = GameObject.Find(ControllerName);
            if (controllerGO == null)
            {
                controllerGO = new GameObject(ControllerName);
                Undo.RegisterCreatedObjectUndo(controllerGO, UndoLabel);
                summary.Add($"Created '{ControllerName}' at scene root.");
            }

            var output = controllerGO.GetComponent<HapticOutput>();
            if (output == null)
            {
                output = Undo.AddComponent<HapticOutput>(controllerGO);
                summary.Add($"Added HapticOutput to '{ControllerName}'.");
            }

            // 2) 所有 PressableButton.onPressed → HapticOutput.PlayButtonClick
            var buttons = UnityEngine.Object.FindObjectsByType<PressableButton>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            int wiredButtons = 0;
            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                Undo.RecordObject(btn, UndoLabel);
                if (RewirePersistent(btn.onPressed, output, nameof(HapticOutput.PlayButtonClick)))
                    wiredButtons++;
                EditorUtility.SetDirty(btn);
            }
            summary.Add(
                $"Wired {wiredButtons}/{buttons.Length} PressableButton.onPressed → " +
                $"HapticOutput.PlayButtonClick.");

            // 3) 所有 RotaryKnob.onStepClicked → HapticOutput.PlayKnobStep
            var knobs = UnityEngine.Object.FindObjectsByType<RotaryKnob>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            int wiredKnobs = 0;
            foreach (var knob in knobs)
            {
                if (knob == null) continue;
                Undo.RecordObject(knob, UndoLabel);
                if (RewirePersistent(knob.onStepClicked, output, nameof(HapticOutput.PlayKnobStep)))
                    wiredKnobs++;
                EditorUtility.SetDirty(knob);
            }
            summary.Add(
                $"Wired {wiredKnobs}/{knobs.Length} RotaryKnob.onStepClicked → " +
                $"HapticOutput.PlayKnobStep.");

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Undo.CollapseUndoOperations(undoGroup);

            string body = string.Join("\n• ", summary.ToArray());
            string msg =
                "Haptics 配齐：\n• " + body + "\n\n" +
                "下一步：在终端里跑\n" +
                "    python \"Assets/Scripts/HapticBridge.py\"\n\n" +
                "然后 Play —— 触按钮 zone 应该有清脆 click，转旋钮每跨一个 angleStep 也会咔嗒一次。\n" +
                "调整效果 ID / 节流：选中 HapticController 在 Inspector 改 HapticOutput 字段。";
            Debug.Log("[HapticSetup] " + msg, controllerGO);
            EditorUtility.DisplayDialog("Setup Haptics", msg, "好的");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog(
                "Setup Haptics failed",
                $"Setup aborted with an exception:\n\n{ex.GetType().Name}: {ex.Message}\n\nSee Console for full stack trace.",
                "OK");
        }
    }

    /// <summary>
    /// 在 UnityEvent 上『去重接线』：
    ///   1) 把所有指向 (target, methodName) 的现存 persistent 监听删掉（避免重复执行后叠加）；
    ///   2) 添加一个新的 persistent 监听调用 target.methodName（无参 void 重载）。
    /// 不会触碰其他指向不同 target/method 的监听 —— 用户手工加的其他事件不会被吃掉。
    /// </summary>
    private static bool RewirePersistent(UnityEvent evt, UnityEngine.Object target, string methodName)
    {
        if (evt == null || target == null || string.IsNullOrEmpty(methodName)) return false;

        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEngine.Object t = evt.GetPersistentTarget(i);
            string m = evt.GetPersistentMethodName(i);
            if (ReferenceEquals(t, target) && m == methodName)
            {
                UnityEventTools.RemovePersistentListener(evt, i);
            }
        }

        UnityAction action = Delegate.CreateDelegate(typeof(UnityAction), target, methodName, false, false)
                             as UnityAction;
        if (action == null)
        {
            Debug.LogWarning(
                $"[HapticSetup] 找不到方法 {target.GetType().Name}.{methodName}() (无参 void 重载)，" +
                "此条监听跳过。",
                target);
            return false;
        }

        UnityEventTools.AddPersistentListener(evt, action);
        return true;
    }
}
