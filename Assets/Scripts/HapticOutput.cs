using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// 振动输出：把"播放某个 DRV2605 库振动效果"的请求通过 UDP 发到本机
/// 跑着的 HapticBridge.py，由它写到振动板的 USB HID。
///
/// 协议（纯文本 UTF-8，单包一条命令）
///     EFFECT,&lt;L&gt;,&lt;R&gt;     L/R 对应左右两个 LRA 马达的库振动 ID（0~127，0=不振）
///     STOP                两路停振
///     PING                诊断用，bridge 会回 PONG
///
/// 使用方式：
///   1) 场景里放一个 GameObject，挂上本组件。整个场景共用这一个实例（Singleton）。
///   2) 在按钮/旋钮的 UnityEvent 上拖拽：
///        PressableButton.onPressed → HapticOutput.PlayButtonClick()
///        RotaryKnob.onStepClicked  → HapticOutput.PlayKnobStep()
///      或者用 Tools/Setup Knob Interaction 一键自动接好。
///   3) 在 Python 端开 HapticBridge.py（与 SensorBridge.py 并存）。
///
/// 注意：本组件不依赖任何 HID 库——所有硬件交互都在 HapticBridge.py 里完成，
/// 与 SensorBridge.py 的"硬件→Unity"完全对偶。
/// </summary>
[DisallowMultipleComponent]
public class HapticOutput : MonoBehaviour
{
    [Header("UDP Endpoint")]
    [Tooltip("HapticBridge.py 监听的 IP。本机请保持 127.0.0.1。")]
    [SerializeField] private string host = "127.0.0.1";

    [Tooltip("HapticBridge.py 监听的端口。需与 HAPTIC_UDP_PORT 一致，默认 5006。")]
    [SerializeField] private int port = 5006;

    [Header("Effect Presets (DRV2605 library IDs)")]
    [Tooltip("按钮按下时的效果 ID。默认 1 = Strong Click 100%（清脆、短促，最像物理按钮）。\n" +
             "其他常用：24 Sharp Click / 7 Soft Click / 14 Strong Buzz。\n" +
             "完整列表见 DRV2605L Datasheet Table 12。")]
    [Range(0, 123)] public int buttonClickEffect = 1;

    [Tooltip("旋钮步进咔嗒效果 ID。默认 7 = Soft Click 30%（轻、短，连续触发时不糊在一起）。\n" +
             "其他可选：1 Strong Click（重）/ 10 Double Click（双咔嗒）。")]
    [Range(0, 123)] public int knobStepEffect = 7;

    [Tooltip("抓住旋钮的瞬间额外触发的效果（一次性）。0 = 关闭。\n" +
             "默认 14 = Strong Buzz 100%，给一次明显的『抓到了』反馈。")]
    [Range(0, 123)] public int knobGrabEffect = 14;

    [Tooltip("Touch（指尖刚进入触发区，但还没按下/抓取）时的轻反馈。0 = 关闭。\n" +
             "默认 0；如果你想要『手刚碰到任何可交互体就有反馈』，可改成 7 (Soft Click)。")]
    [Range(0, 123)] public int touchEnterEffect = 0;

    [Header("Throttling")]
    [Tooltip("两次 UDP 发送之间的最小间隔（毫秒）。\n" +
             "目的：旋钮快速旋转时 onStepClicked 可能在同一帧内多次触发，bridge 也未必能这么快驱动 DRV2605。\n" +
             "30~80ms 是 LRA 的合理上限；调到 0 关闭节流。")]
    [Range(0, 300)] public int minIntervalMillis = 35;

    [Header("Debug")]
    [Tooltip("启用后每次发送都在 Console 打印一行。排查『按下没振』时打开。")]
    public bool logSends = false;

    // ---------- Singleton (轻量) ----------
    private static HapticOutput _instance;

    /// <summary>取场景里的 HapticOutput 实例；没有则返回 null（不会自动创建——避免后台无声失败）。</summary>
    public static HapticOutput Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<HapticOutput>();
            return _instance;
        }
    }

    // ---------- 内部状态 ----------
    private UdpClient _udp;
    private IPEndPoint _endpoint;
    private float _lastSendTime = -10f;
    private int _droppedDueToThrottle;
    private float _lastDropLogTime;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning(
                $"[HapticOutput] 场景里已有一个实例 '{_instance.name}'，本实例 '{name}' 将被禁用以避免重复发送。",
                this);
            enabled = false;
            return;
        }
        _instance = this;
        EnsureSocket();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        try { _udp?.Close(); } catch { /* ignore */ }
        _udp = null;
    }

    private void EnsureSocket()
    {
        if (_udp != null && _endpoint != null) return;
        try
        {
            _udp = new UdpClient();
            _endpoint = new IPEndPoint(IPAddress.Parse(host), port);
        }
        catch (Exception e)
        {
            Debug.LogError($"[HapticOutput] UDP 初始化失败: {e.Message} (host={host} port={port})", this);
            _udp = null;
            _endpoint = null;
        }
    }

    // ---------- 公开 API（直接接 UnityEvent / 代码调用） ----------

    /// <summary>按预设的 buttonClickEffect 触发一次按钮咔嗒。无参版方便接到 UnityEvent.onPressed。</summary>
    public void PlayButtonClick()
    {
        PlayEffect(buttonClickEffect, buttonClickEffect);
    }

    /// <summary>按预设的 knobStepEffect 触发一次旋钮步进咔嗒。接到 RotaryKnob.onStepClicked。</summary>
    public void PlayKnobStep()
    {
        PlayEffect(knobStepEffect, knobStepEffect);
    }

    /// <summary>按预设的 knobGrabEffect 触发一次抓握反馈。0 时静默不发。</summary>
    public void PlayKnobGrab()
    {
        if (knobGrabEffect <= 0) return;
        PlayEffect(knobGrabEffect, knobGrabEffect);
    }

    /// <summary>按预设的 touchEnterEffect 触发轻触反馈。0 时静默不发。</summary>
    public void PlayTouchEnter()
    {
        if (touchEnterEffect <= 0) return;
        PlayEffect(touchEnterEffect, touchEnterEffect);
    }

    /// <summary>左右两路播放同一效果。</summary>
    public void PlayEffect(int bothChannels)
    {
        PlayEffect(bothChannels, bothChannels);
    }

    /// <summary>左右两路独立播放。effect=0 表示该路不振。</summary>
    public void PlayEffect(int leftEffect, int rightEffect)
    {
        Send($"EFFECT,{leftEffect},{rightEffect}");
    }

    /// <summary>立即停振两路。</summary>
    public void Stop()
    {
        Send("STOP");
    }

    /// <summary>诊断 ping —— bridge 在跑就会回 PONG（本组件不读应答；用来确认 bridge 活着）。</summary>
    public void Ping()
    {
        Send("PING");
    }

    // ---------- 内部 ----------

    private void Send(string text)
    {
        if (!enabled) return;

        // 节流：在节流窗口内的请求直接丢弃。我们更倾向于"丢一次咔嗒"而不是"咔嗒糊成一团"。
        if (minIntervalMillis > 0)
        {
            float minInterval = minIntervalMillis * 0.001f;
            float now = Time.unscaledTime;
            if (now - _lastSendTime < minInterval)
            {
                _droppedDueToThrottle++;
                if (logSends && now - _lastDropLogTime > 1f)
                {
                    _lastDropLogTime = now;
                    Debug.Log($"[HapticOutput] 节流丢弃: {_droppedDueToThrottle} 次/上一秒。", this);
                    _droppedDueToThrottle = 0;
                }
                return;
            }
            _lastSendTime = now;
        }

        EnsureSocket();
        if (_udp == null || _endpoint == null) return;

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            _udp.Send(payload, payload.Length, _endpoint);
            if (logSends)
                Debug.Log($"[HapticOutput] sent '{text}' → {host}:{port}", this);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HapticOutput] UDP 发送失败 ('{text}'): {e.Message}", this);
        }
    }
}
