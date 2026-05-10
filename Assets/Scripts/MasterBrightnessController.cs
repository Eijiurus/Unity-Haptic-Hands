using UnityEngine;

/// <summary>
/// 简单 fan-out：把单个亮度值同步分发到一组 LampController。
/// 通常挂在场景里一个空 GameObject（如 MasterController）上，
/// 由 RotaryKnob.onValueChanged 调用 SetGlobalBrightness。
/// 自身不持有任何 brightness 状态——亮度的 source of truth 是旋钮（或键盘测试）。
/// </summary>
public class MasterBrightnessController : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("受控的灯。任意数量；空槽会被跳过。所有 lamp 共享同一个亮度值。")]
    public LampController[] lamps;

    [Header("Keyboard Test")]
    [Tooltip("启用后，在 Play Mode 中按数字键 0~9 可以直接设置亮度（0=全灭，1=10%，…，9=90%），按 = 设为 100%。\n" +
             "这是独立于旋钮的 fallback 通道，用来验证'亮度链路本身'是否畅通：\n" +
             "  · 键盘能改亮度 ⇒ 灯+master 配置 OK，问题在旋钮一侧（onValueChanged 没接好 / RotaryKnob.positionSource 缺失）；\n" +
             "  · 键盘也改不了 ⇒ master.lamps 没填，或 LampController 本身不工作。")]
    public bool enableKeyboardTest = true;

    [Tooltip("启用后，每次按键变化都会在 Console 打印 'Key X → SetGlobalBrightness(0.xx) on N lamps'。便于诊断。")]
    public bool logKeyboardChanges = true;

    /// <summary>
    /// 把 0~1 的亮度值同步到所有 lamps（LampController.SetBrightness 内部已 Clamp01）。
    /// 当 value > 0 时，同时调用 lamp.SetOn(true)，
    /// 这样旋钮从 0 拧上来时灯会被隐式点亮，不需要额外的开关操作。
    /// 接入 RotaryKnob.onValueChanged 时，请在 Inspector 选择带 dynamic float 参数的版本。
    /// </summary>
    public void SetGlobalBrightness(float value)
    {
        if (lamps == null) return;
        bool turnOn = value > 0f;
        for (int i = 0; i < lamps.Length; i++)
        {
            var lamp = lamps[i];
            if (lamp == null) continue;
            lamp.SetBrightness(value);
            if (turnOn) lamp.SetOn(true);
        }
    }

    private void Start()
    {
        // 这条 log 是用来证明 MasterBrightnessController 确实存在并在跑的——
        // 如果 Console 里没有它、却又按数字键灯不亮，那就是 'MasterController' 这个 GameObject
        // 根本没在场景里（或者它/本组件被禁用了）。
        // 解决：跑一遍 Tools/Setup Knob Interaction，它会创建 MasterController 并填好 lamps[]。
        int n = lamps != null ? lamps.Length : 0;
        Debug.Log(
            $"[MasterBrightnessController] Started on '{name}' | " +
            $"lamps={n} | keyboardTest={(enableKeyboardTest ? "ON" : "OFF")} | " +
            $"按 0~9 设置 0~90% 亮度，按 = 设为 100%（Game 视图必须是焦点窗口才会响应键盘）",
            this);
    }

    private void Update()
    {
        if (!enableKeyboardTest) return;

        // 数字键 0~9 → 0%, 10%, …, 90%
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                float v = i / 10f;
                SetGlobalBrightness(v);
                if (logKeyboardChanges)
                {
                    int n = lamps != null ? lamps.Length : 0;
                    Debug.Log($"[MasterBrightnessController] Key {i} → SetGlobalBrightness({v:F2}) on {n} lamp(s).", this);
                }
                return; // 一帧只处理一个数字键
            }
        }

        // = / + 键 → 100%（数字键里凑不出 1.0，单独留个键给"全亮"）
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus)
            || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            SetGlobalBrightness(1f);
            if (logKeyboardChanges)
            {
                int n = lamps != null ? lamps.Length : 0;
                Debug.Log($"[MasterBrightnessController] Key '=' → SetGlobalBrightness(1.00) on {n} lamp(s).", this);
            }
        }
    }
}
