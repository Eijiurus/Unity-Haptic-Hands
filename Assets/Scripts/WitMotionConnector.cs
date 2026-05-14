using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Assets.Device;

/// <summary>
/// 管理 WitMotion WT9011DCL-BT50 蓝牙传感器的扫描和连接。
/// 
/// 直接调用 BleApi 底层函数，在 Update 中持续轮询扫描结果，
/// 确保不会错过任何 BLE 广播——一次性的扫描函数轮询窗口偏短容易漏掉设备，
/// 改成在 MonoBehaviour.Update 内不断 Poll。
/// </summary>
public class WitMotionConnector : MonoBehaviour
{
    [Header("自动连接")]
    [Tooltip("启动时自动扫描")]
    [SerializeField] private bool autoScan = true;

    [Tooltip("发现 WT 设备后自动连接")]
    [SerializeField] private bool autoConnect = true;

    [Tooltip("Windows 有时对 WitMotion 广播报 connectable=False，但仍可 GATT 连接。\n" +
             "勾选后：只要名称含 WT 就尝试连接（建议开启）。")]
    [SerializeField] private bool connectEvenIfNotConnectable = true;

    [Header("扫描设置")]
    [Tooltip("扫描超时时间（秒），超过后停止扫描")]
    [SerializeField] private float scanTimeout = 30f;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;
    [Tooltip("输出扫描到的所有 BLE 设备更新日志")]
    [SerializeField] private bool verboseScanLog = true;

    public bool IsConnected { get; private set; }
    public bool IsScanning { get; private set; }

    private float _scanTimer;

    private class ScannedDevice
    {
        public string name = "";
        public bool isConnectable;
    }
    private readonly Dictionary<string, ScannedDevice> _scannedDevices = new Dictionary<string, ScannedDevice>();
    private readonly HashSet<string> _connectAttemptedIds = new HashSet<string>();

    void Start()
    {
        if (autoScan)
            Scan();
    }

    void Update()
    {
        if (!IsScanning || IsConnected) return;

        _scanTimer += Time.deltaTime;

        BleApi.DeviceUpdate res = new BleApi.DeviceUpdate();
        BleApi.ScanStatus status = BleApi.PollDevice(ref res, false);

        if (status == BleApi.ScanStatus.AVAILABLE)
        {
            if (!_scannedDevices.ContainsKey(res.id))
                _scannedDevices[res.id] = new ScannedDevice();

            var dev = _scannedDevices[res.id];

            if (res.nameUpdated && !string.IsNullOrEmpty(res.name))
                dev.name = res.name;
            if (res.isConnectableUpdated)
                dev.isConnectable = res.isConnectable;

            if (verboseScanLog)
            {
                string name = string.IsNullOrEmpty(dev.name) ? "<unknown>" : dev.name;
                Log($"[仅扫描] 设备广播更新（≠已连接）: name={name}, id={res.id}, " +
                    $"connectable={dev.isConnectable}, " +
                    $"nameUpdated={res.nameUpdated}, connectableUpdated={res.isConnectableUpdated}");
            }

            bool nameIsWt = !string.IsNullOrEmpty(dev.name) && dev.name.Contains("WT");
            bool mayConnect = nameIsWt && (dev.isConnectable || connectEvenIfNotConnectable);

            if (nameIsWt && !dev.isConnectable && connectEvenIfNotConnectable && verboseScanLog)
            {
                Log($"名称匹配 WT 但 connectable=False，将仍尝试 GATT 连接（connectEvenIfNotConnectable 已开启）。");
            }

            if (mayConnect && autoConnect && !IsConnected && !_connectAttemptedIds.Contains(res.id))
            {
                _connectAttemptedIds.Add(res.id);
                Log($"准备连接 WT 设备: {dev.name} ({res.id})");
                StopScan();
                ConnectDevice(dev.name, res.id);
            }
        }
        else if (status == BleApi.ScanStatus.FINISHED)
        {
            Log("本轮扫描结束，继续扫描...");
            BleApi.StartDeviceScan();
        }

        if (_scanTimer > scanTimeout && !IsConnected)
        {
            StopScan();
            Log($"扫描超时（{scanTimeout}s），未对任何 WT 设备发起连接。");
        }
    }

    /// <summary>
    /// 开始 BLE 扫描。可绑定到 UI 按钮。
    /// </summary>
    public void Scan()
    {
        if (IsScanning)
        {
            Log("已在扫描中...");
            return;
        }
        StartCoroutine(ScanCoroutine());
    }

    private IEnumerator ScanCoroutine()
    {
        _scannedDevices.Clear();
        _connectAttemptedIds.Clear();
        _scanTimer = 0f;

        Log("正在重置 BLE...");
        try { BleApi.Quit(); } catch { }

        yield return new WaitForSeconds(0.5f);

        IsScanning = true;
        Log("开始扫描蓝牙设备...（持续扫描中，最长等待 " + scanTimeout + "s）");
        BleApi.StartDeviceScan();
        CheckBleError("扫描启动");
    }

    /// <summary>
    /// 停止扫描。
    /// </summary>
    public void StopScan()
    {
        if (!IsScanning) return;
        try
        {
            BleApi.StopDeviceScan();
        }
        catch { }
        IsScanning = false;
    }

    private void ConnectDevice(string deviceName, string deviceId)
    {
        Log($">>> 正在发起 GATT 连接（真正连接阶段）: {deviceName} ...");

        DeviceModel device = new DeviceModel(deviceName, deviceId);
        DevicesManager.Instance.AddDevice(device);
        DevicesManager.Instance.currentKey = deviceId;
        device.OpenDevice();
        IsConnected = true;

        Log($">>> GATT 已调用 OpenDevice（若数据仍无变化，检查是否被其他程序占用）\n" +
            $"已标记连接: {deviceName} ({deviceId})\n" +
            "  移动传感器测试手腕旋转。\n" +
            "  方向不对请调 DataGloveHandDriver 的 Axis Sign / Axis Remap。");
    }

    /// <summary>
    /// 断开所有设备。
    /// </summary>
    public void Disconnect()
    {
        DevicesManager.Instance.ClearDevice();
        IsConnected = false;
        Log("已断开所有设备。");
    }

    private void CheckBleError(string context)
    {
        try
        {
            BleApi.ErrorMessage err;
            BleApi.GetError(out err);
            if (!string.IsNullOrEmpty(err.msg) && err.msg != "Ok")
                Debug.LogWarning($"[WitMotion BLE 错误 @ {context}] {err.msg}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WitMotion] BleWinrtDll.dll 调用失败: {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        StopScan();
        if (IsConnected) Disconnect();
        try { BleApi.Quit(); } catch { }
    }

    void OnDestroy()
    {
        StopScan();
        if (IsConnected) Disconnect();
    }

    private void Log(string msg)
    {
        if (showDebugLog)
            Debug.Log($"[WitMotion] {msg}");
    }
}
