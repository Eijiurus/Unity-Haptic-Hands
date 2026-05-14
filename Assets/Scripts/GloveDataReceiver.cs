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
    [Header("数据源模式")] // 数据源模式
    [Tooltip("勾选后使用键盘模拟手指弯曲（1-5 键），无需连接硬件")] // 使用键盘模拟手指弯曲
    [SerializeField] private bool useKeyboardSimulation = false;

    [Header("UDP 设置（接收 SensorBridge.py 数据）")] // UDP 设置   
    [Tooltip("SensorBridge.py 发送的 UDP 端口")] // SensorBridge.py 发送的 UDP 端口
    [SerializeField] private int udpPort = 5005;

    [Header("数据映射")] // 数据映射
    [Tooltip("传感器原始值上限（1800 = 180.0°）")] // 传感器原始值上限
    [SerializeField] private float rawMax = 1800f;

    [Tooltip("传感器通道顺序为 CH1=小指→CH5=拇指，需反转为拇指在前")] // 传感器通道顺序为 CH1=小指→CH5=拇指，需反转为拇指在前
    [SerializeField] private bool reverseFingerOrder = true;

    [Header("调试")] // 调试
    [SerializeField] private bool showDebugLog = false; // 显示调试日志

    /// <summary>
    /// 归一化后的五指弯曲值 (0 = 伸直, 1 = 完全弯曲)。
    /// 顺序：[0] Thumb, [1] Index, [2] Middle, [3] Ring, [4] Pinky
    /// </summary>
    public float[] FingerValues { get; private set; } = new float[5]; // 五指弯曲值

    private UdpClient _udpClient; // UDP客户端
    private Thread _receiveThread; // 接收线程
    private volatile bool _keepReading;
    private readonly float[] _threadBuffer = new float[5]; // 线程缓冲区
    private volatile bool _newDataAvailable; // 是否有新数据

    private float[] _simTargets = new float[5]; // 键盘模拟的目标值

    void OnEnable() // 没有使用键盘模拟就传入手套信息到DataGloveHandDriver
    {
        if (!useKeyboardSimulation) // 如果没有使用键盘模拟，就启动UDP接收
            StartUdpReceiver(); // 启动UDP接收
    }

    void OnDisable() // 停止UDP接收
    {
        StopUdpReceiver(); // 停止UDP接收
    }

    void Update() // 更新手套信息
    {
        if (useKeyboardSimulation) // 如果使用键盘模拟，就更新键盘模拟的值
        {
            UpdateKeyboardSimulation(); // 更新键盘模拟的值
            return;
        }

        if (!_newDataAvailable) return; // 如果没有新数据，就返回
        _newDataAvailable = false; // 设置为false，表示没有新数据   
        lock (_threadBuffer) // 锁定线程缓冲区
        {
            Array.Copy(_threadBuffer, FingerValues, 5); // 拷贝手套信息到FingerValues
        }

        if (showDebugLog) // 如果显示调试日志，就打印手套信息
        {
            Debug.Log($"[Glove] T:{FingerValues[0]:F2}  I:{FingerValues[1]:F2}  " + // 打印手套信息
                      $"M:{FingerValues[2]:F2}  R:{FingerValues[3]:F2}  P:{FingerValues[4]:F2}");
        }
    }

    void OnDestroy()
    {
        StopUdpReceiver();
    }

    // ─────────────── 键盘模拟模式 ───────────────

    private void UpdateKeyboardSimulation() // 更新键盘模拟的值
    {
        KeyCode[] keys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5 };
        bool allGrip = Input.GetKey(KeyCode.Space); // 如果按下空格，就握拳

        for (int i = 0; i < 5; i++)
        {
            _simTargets[i] = (Input.GetKey(keys[i]) || allGrip) ? 1f : 0f; // 如果按下对应的键，就弯曲
            FingerValues[i] = Mathf.Lerp(FingerValues[i], _simTargets[i], Time.deltaTime * 10f); // 平滑过渡
        }

        if (showDebugLog && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Alpha1))) // 如果显示调试日志，就打印键盘模拟的值
        {
            Debug.Log($"[Glove 模拟] T:{FingerValues[0]:F2}  I:{FingerValues[1]:F2}  " + // 打印键盘模拟的值
                      $"M:{FingerValues[2]:F2}  R:{FingerValues[3]:F2}  P:{FingerValues[4]:F2}");
        }
    }

    // ─────────────── UDP 接收 ───────────────

    private void StartUdpReceiver() // 启动UDP接收
    {
        if (_udpClient != null) return;
        try // 尝试启动UDP接收
        {
            _udpClient = new UdpClient(udpPort); // 创建一个UDP客户端
            _keepReading = true; // 设置为true，表示保持接收
            _receiveThread = new Thread(UdpReadLoop) { IsBackground = true }; // 创建一个接收线程
            _receiveThread.Start(); // 启动接收线程
            Debug.Log($"[GloveDataReceiver] UDP 监听已启动 @ 端口 {udpPort}，等待 SensorBridge.py 数据...");
        }
        catch (Exception e) // 如果启动失败，就打印错误信息
        {
            Debug.LogError($"[GloveDataReceiver] UDP 启动失败: {e.Message}"); // 打印错误信息
        }
    }

    private void StopUdpReceiver() // 停止UDP接收
    {
        _keepReading = false; // 设置为false，表示停止接收
        _udpClient?.Close(); // 关闭UDP客户端
        _udpClient = null; // 关闭UDP客户端
        if (_receiveThread != null && _receiveThread.IsAlive)
            _receiveThread.Join(300); // 等待接收线程结束
        _receiveThread = null; // 设置为null，表示接收线程已经结束
    }

    private void UdpReadLoop() // 接收线程
    {
        while (_keepReading) // 如果保持接收，就继续接收
        {
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0); // 创建一个IP端点
                byte[] data = _udpClient.Receive(ref remoteEP);
                string msg = Encoding.UTF8.GetString(data).Trim().TrimEnd(';'); // 获取数据
                ParseSensorData(msg); // 解析数据   
            }
            catch (ObjectDisposedException) { break; } // 如果对象被销毁，就停止接收
            catch (SocketException) { if (!_keepReading) break; } // 如果socket异常，就停止接收
            catch (Exception e)
            {
                if (_keepReading) // 如果保持接收，就打印错误信息
                    Debug.LogWarning($"[GloveDataReceiver] UDP 读取异常: {e.Message}"); // 打印错误信息 
            }
        }
    }

    /// <summary>
    /// 解析 "1504,900,100,0,0,0,0,0,0,0,0" 格式。
    /// 前 5 个值为手指弯曲度（0-1800 = 0.0°-180.0°）。
    /// </summary>
    private void ParseSensorData(string line) // 解析数据
    {
        string[] parts = line.Split(','); // 分割数据
        if (parts.Length < 5) return; // 如果数据长度小于5，就返回

        lock (_threadBuffer) // 锁定线程缓冲区
        {
            for (int i = 0; i < 5; i++) // 遍历数据
            {
                if (!float.TryParse(parts[i].Trim(), out float raw)) continue; // 如果数据转换失败，就返回
                float normalized = Mathf.Clamp01(raw / rawMax); // 归一化数据

                int targetIndex = reverseFingerOrder ? (4 - i) : i; // 反转手指顺序
                _threadBuffer[targetIndex] = normalized; // 存储数据
            }
        }
        _newDataAvailable = true; // 设置为true，表示有新数据
    }
}
