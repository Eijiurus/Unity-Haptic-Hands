using System;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// 从 Arduino 串口读取数据手套的五指弯曲数据。
/// 包含键盘模拟模式，无需硬件即可测试手指弯曲。
///
/// 数据链路：
///   Manu-5D 手套传感器 → 采集盒模拟输出(0-5V) → Arduino ADC
///   → Arduino 串口输出 "thumb,index,middle,ring,pinky\n" → Unity
/// </summary>
public class GloveDataReceiver : MonoBehaviour
{
    [Header("模式选择")]
    [Tooltip("勾选后使用键盘模拟手指弯曲（1-5 键），无需连接 Arduino")]
    [SerializeField] private bool useKeyboardSimulation = true;

    [Header("串口设置（连接 Arduino）")]
    [Tooltip("Arduino 所在的 COM 端口")]
    [SerializeField] private string portName = "COM3";
    [SerializeField] private int baudRate = 9600;

    [Header("数据映射")]
    [Tooltip("Arduino ADC 最小值（手指伸直时的读数）")]
    [SerializeField] private float rawMin = 0f;
    [Tooltip("Arduino ADC 最大值（手指完全弯曲时的读数），10位 ADC 为 1023")]
    [SerializeField] private float rawMax = 1023f;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = false;

    /// <summary>
    /// 归一化后的五指弯曲值 (0 = 伸直, 1 = 完全弯曲)。
    /// 顺序：[0] Thumb, [1] Index, [2] Middle, [3] Ring, [4] Pinky
    /// </summary>
    public float[] FingerValues { get; private set; } = new float[5];

    public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

    private SerialPort _serialPort;
    private Thread _readThread;
    private volatile bool _keepReading;
    private readonly float[] _threadBuffer = new float[5];
    private volatile bool _newDataAvailable;

    // 键盘模拟用
    private float[] _simTargets = new float[5];

    void OnEnable()
    {
        if (!useKeyboardSimulation)
            OpenPort();
    }

    void OnDisable()
    {
        ClosePort();
    }

    void Update()
    {
        if (useKeyboardSimulation)
        {
            UpdateKeyboardSimulation();
            return;
        }

        if (!_newDataAvailable) return;
        _newDataAvailable = false;
        lock (_threadBuffer)
        {
            Array.Copy(_threadBuffer, FingerValues, 5);
        }

        if (showDebugLog)
        {
            Debug.Log($"[Glove] T:{FingerValues[0]:F2}  I:{FingerValues[1]:F2}  " +
                      $"M:{FingerValues[2]:F2}  R:{FingerValues[3]:F2}  P:{FingerValues[4]:F2}");
        }
    }

    void OnDestroy()
    {
        ClosePort();
    }

    // ─────────────── 键盘模拟模式 ───────────────

    private void UpdateKeyboardSimulation()
    {
        // 按住 1-5 键 → 对应手指弯曲，松开 → 伸直
        // 1=拇指, 2=食指, 3=中指, 4=无名指, 5=小指
        // 按 Space → 所有手指同时握拳
        KeyCode[] keys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5 };

        bool allGrip = Input.GetKey(KeyCode.Space);

        for (int i = 0; i < 5; i++)
        {
            _simTargets[i] = (Input.GetKey(keys[i]) || allGrip) ? 1f : 0f;
            FingerValues[i] = Mathf.Lerp(FingerValues[i], _simTargets[i], Time.deltaTime * 10f);
        }

        if (showDebugLog && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Alpha1)))
        {
            Debug.Log($"[Glove 模拟] T:{FingerValues[0]:F2}  I:{FingerValues[1]:F2}  " +
                      $"M:{FingerValues[2]:F2}  R:{FingerValues[3]:F2}  P:{FingerValues[4]:F2}");
        }
    }

    // ─────────────── 串口管理 ───────────────

    private void OpenPort()
    {
        if (_serialPort != null && _serialPort.IsOpen) return;
        try
        {
            _serialPort = new SerialPort(portName, baudRate)
            {
                ReadTimeout = 100,
                DtrEnable = true,
                RtsEnable = true
            };
            _serialPort.Open();
            _serialPort.DiscardInBuffer();

            _keepReading = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();

            Debug.Log($"[GloveDataReceiver] 已连接 Arduino @ {portName} ({baudRate})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GloveDataReceiver] 无法打开 {portName}: {e.Message}");
        }
    }

    private void ClosePort()
    {
        _keepReading = false;
        if (_readThread != null && _readThread.IsAlive)
            _readThread.Join(200);
        _readThread = null;
        if (_serialPort != null && _serialPort.IsOpen)
        {
            try { _serialPort.Close(); } catch { }
        }
        _serialPort = null;
    }

    private void ReadLoop()
    {
        while (_keepReading)
        {
            try
            {
                string line = _serialPort.ReadLine().Trim();
                if (string.IsNullOrEmpty(line)) continue;
                ParseLine(line);
            }
            catch (TimeoutException) { }
            catch (Exception e)
            {
                if (_keepReading)
                    Debug.LogWarning($"[GloveDataReceiver] 读取异常: {e.Message}");
            }
        }
    }

    private void ParseLine(string line)
    {
        string[] parts = line.Split(',');
        if (parts.Length < 5) return;
        lock (_threadBuffer)
        {
            for (int i = 0; i < 5; i++)
            {
                if (float.TryParse(parts[i].Trim(), out float raw))
                    _threadBuffer[i] = Mathf.InverseLerp(rawMin, rawMax, raw);
            }
        }
        _newDataAvailable = true;
    }
}
