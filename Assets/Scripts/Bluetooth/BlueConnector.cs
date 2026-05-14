using Assets.Device;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Bluetooth
{
    /*
     * 蓝牙连接器，连接并接受蓝牙数据
     * Bluetooth connector, connecting and receiving Bluetooth data
     */
    public class BlueConnector
    {
        private static BlueConnector _instance;

        // 蓝牙传感器UUID Bluetooth sensor UUID
        public static readonly string UUID_SERVICE = "0000ffe5-0000-1000-8000-00805f9a34fb";
        public static readonly string UUID_READ = "0000ffe4-0000-1000-8000-00805f9a34fb";
        public static readonly string UUID_WRITE = "0000ffe9-0000-1000-8000-00805f9a34fb";

        // 接收数据线程  Receive data thread
        private Thread receiveTh;

        public bool isConnect = false;

        // 收到数据事件 Received data event
        public delegate void ReceiveEventHandler(string deviceId, byte[] data); // ReceiveEventHandler是收到数据事件
        public event ReceiveEventHandler OnReceive; // OnReceive是收到数据事件

        private BlueConnector() { } // BlueConnector是蓝牙连接器

        public static BlueConnector Instance // Instance是实例
        {
            get
            {
                if (_instance == null) // 如果实例为空，则创建实例
                {
                    _instance = new BlueConnector();
                }
                return _instance; // 返回实例
            }
        }

        /// <summary>
        /// 开始连接 Start connecting
        /// </summary>
        public void Connect(string deviceId) // Connect是连接
        {
            try // 尝试连接
            {
                BleApi.SubscribeCharacteristic(deviceId, UUID_SERVICE, UUID_READ, false); // 订阅特征
                Debug.Log("连接设备成功");
                if (isConnect) { // 如果已连接，则返回
                    return;
                }
                isConnect = true; // 设置为已连接   
                receiveTh = new Thread(ReceiveData); // 创建接收数据线程
                receiveTh.IsBackground = true; // 设置为后台线程
                receiveTh.Start(); // 启动接收数据线程
            }
            catch (Exception ex) // 捕捉异常
            {
                Debug.LogError(ex.Message); // 打印错误信息
                Debug.Log("连接设备失败");
            } // 捕捉异常
        }

        /// <summary>
        /// 接收数据线程 Receive data thread
        /// </summary>
        private void ReceiveData() { // ReceiveData是接收数据
            BleApi.BLEData res = new BleApi.BLEData(); // 创建BLE数据
            while (true) {
                while (isConnect && BleApi.PollData(out res, false)) // 如果已连接，则轮询数据
                {
                    OnReceive?.Invoke(res.deviceId, res.buf); // 调用收到数据事件
                }
                Thread.Sleep(1); // 睡眠1毫秒
            }
        }

        /// <summary>
        /// 关闭连接 Close Connection
        /// </summary>
        public void Disconnect() { // Disconnect是关闭连接
            if (DevicesManager.Instance.isHaveOpenDevice()) { // 如果存在连接中的设备，则返回
                return;
            }
            try // 尝试关闭连接
            {
                isConnect = false; // 设置为未连接
                Thread.Sleep(200); // 睡眠200毫秒
                receiveTh.Abort(); // 终止接收数据线程
                receiveTh = null; // 设置为null
            }
            catch (Exception) // 捕捉异常
            {
                // 捕捉异常但不处理 捕捉异常但不处理
            }
        }    // 捕捉异常但不处理
    } // 关闭连接
}
