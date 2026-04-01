using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// 接收手套传感器数据，提供归一化的五指弯曲值给 DataGloveHandDriver。
///
/// 数据链路：
///   Manu-5D 手套 → 蓝牙(115200) → PC 串口
///   → SensorBridge.py（读取串口，UDP 转发）
///   → 本脚本（UDP 接收，解析，归一化）
///   → DataGloveHandDriver（驱动骨骼旋转）
///
/// 数据格式（来自 SensorBridge.py）：
///   "1504,900,100,0,0,0,0,0,0,0,0"
///   前 5 个值为手指弯曲（0-1800 = 0.0°-180.0°），以逗号分隔。
///   传感器通道顺序：CH1=小指, CH2=无名指, CH3=中指, CH4=食指, CH5=拇指
/// </summary>
public class GloveDataReceiver : MonoBehaviour
{
    [Header("数据源模式")]
    [Tooltip("勾选后使用键盘模拟手指弯曲（1-5 键），无需连接硬件")]
    [SerializeField] private bool useKeyboardSimulation = true;

    [Header("UDP 设置（接收 SensorBridge.py 数据）")]
    [Tooltip("SensorBridge.py 发送的 UDP 端口")]
    [SerializeField] private int udpPort = 5005;

    [Header("数据映射")]
    [Tooltip("传感器原始值上限（1800 = 180.0°）")]
    [SerializeField] private float rawMax = 1800f;

    [Tooltip("传感器通道顺序为 CH1=小指→CH5=拇指，需反转为拇指在前")]
    [SerializeField] private bool reverseFingerOrder = true;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = false;

    /// <summary>
    /// 归一化后的五指弯曲值 (0 = 伸直, 1 = 完全弯曲)。
    /// 顺序：[0] Thumb, [1] Index, [2] Middle, [3] Ring, [4] Pinky
    /// </summary>
    public float[] FingerValues { get; private set; } = new float[5];

    private UdpClient _udpClient;
    private Thread _receiveThread;
    private volatile bool _keepReading;
    private readonly float[] _threadBuffer = new float[5];
    private volatile bool _newDataAvailable;

    private float[] _simTargets = new float[5];

    void OnEnable()
    {
        if (!useKeyboardSimulation)
            StartUdpReceiver();
    }

    void OnDisable()
    {
        StopUdpReceiver();
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
        StopUdpReceiver();
    }

    // ─────────────── 键盘模拟模式 ───────────────

    private void UpdateKeyboardSimulation()
    {
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

    // ─────────────── UDP 接收 ───────────────

    private void StartUdpReceiver()
    {
        if (_udpClient != null) return;
        try
        {
            _udpClient = new UdpClient(udpPort);
            _keepReading = true;
            _receiveThread = new Thread(UdpReadLoop) { IsBackground = true };
            _receiveThread.Start();
            Debug.Log($"[GloveDataReceiver] UDP 监听已启动 @ 端口 {udpPort}，等待 SensorBridge.py 数据...");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GloveDataReceiver] UDP 启动失败: {e.Message}");
        }
    }

    private void StopUdpReceiver()
    {
        _keepReading = false;
        _udpClient?.Close();
        _udpClient = null;
        if (_receiveThread != null && _receiveThread.IsAlive)
            _receiveThread.Join(300);
        _receiveThread = null;
    }

    private void UdpReadLoop()
    {
        while (_keepReading)
        {
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _udpClient.Receive(ref remoteEP);
                string msg = Encoding.UTF8.GetString(data).Trim().TrimEnd(';');
                ParseSensorData(msg);
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { if (!_keepReading) break; }
            catch (Exception e)
            {
                if (_keepReading)
                    Debug.LogWarning($"[GloveDataReceiver] UDP 读取异常: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 解析 "1504,900,100,0,0,0,0,0,0,0,0" 格式。
    /// 前 5 个值为手指弯曲度（0-1800 = 0.0°-180.0°）。
    /// </summary>
    private void ParseSensorData(string line)
    {
        string[] parts = line.Split(',');
        if (parts.Length < 5) return;

        lock (_threadBuffer)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!float.TryParse(parts[i].Trim(), out float raw)) continue;
                float normalized = Mathf.Clamp01(raw / rawMax);

                int targetIndex = reverseFingerOrder ? (4 - i) : i;
                _threadBuffer[targetIndex] = normalized;
            }
        }
        _newDataAvailable = true;
    }
}
