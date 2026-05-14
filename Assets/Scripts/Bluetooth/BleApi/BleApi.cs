using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

public class BleApi
{
    // dll calls
    public enum ScanStatus { PROCESSING, AVAILABLE, FINISHED }; // ScanStatus是扫描状态 PROCESSING是正在扫描，AVAILABLE是可用，FINISHED是完成

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] // StructLayout是结构体布局 DeviceUpdate是设备更新
    public struct DeviceUpdate
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] // MarshalAs是MarshalAs UnmanagedType.ByValTStr是字符串 SizeConst是大小 Const是常量
        public string id; // id是设备ID
        [MarshalAs(UnmanagedType.I1)] // MarshalAs是MarshalAs UnmanagedType.I1是布尔值
        public bool isConnectable; // isConnectable是设备是否可连接
        [MarshalAs(UnmanagedType.I1)] // MarshalAs是MarshalAs UnmanagedType.I1是布尔值
        public bool isConnectableUpdated; // isConnectableUpdated是设备是否可连接更新
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 50)] // MarshalAs是MarshalAs UnmanagedType.ByValTStr是字符串 SizeConst是大小 Const是常量
        public string name; // name是设备名称
        [MarshalAs(UnmanagedType.I1)] // MarshalAs是MarshalAs UnmanagedType.I1是布尔值
        public bool nameUpdated; // nameUpdated是设备名称更新
    }

    [DllImport("BleWinrtDll.dll", EntryPoint = "StartDeviceScan")] // DllImport是DllImport EntryPoint是入口点
    public static extern void StartDeviceScan(); 

    [DllImport("BleWinrtDll.dll", EntryPoint = "PollDevice")] // DllImport是DllImport EntryPoint是入口点    
    public static extern ScanStatus PollDevice(ref DeviceUpdate device, bool block); // PollDevice是轮询设备

    [DllImport("BleWinrtDll.dll", EntryPoint = "StopDeviceScan")] // DllImport是DllImport EntryPoint是入口点    
    public static extern void StopDeviceScan(); // StopDeviceScan是停止设备扫描

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] // StructLayout是结构体布局 Service是服务
    public struct Service
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] // MarshalAs是MarshalAs UnmanagedType.ByValTStr是字符串 SizeConst是大小 Const是常量
        public string uuid;
    } // uuid是服务UUID

    [DllImport("BleWinrtDll.dll", EntryPoint = "ScanServices", CharSet = CharSet.Unicode)] // DllImport是DllImport EntryPoint是入口点    
    public static extern void ScanServices(string deviceId); // ScanServices是扫描服务

    [DllImport("BleWinrtDll.dll", EntryPoint = "PollService")] // DllImport是DllImport EntryPoint是入口点    
    public static extern ScanStatus PollService(out Service service, bool block); // PollService是轮询服务

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] // StructLayout是结构体布局 Characteristic是特征
    public struct Characteristic
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] // MarshalAs是MarshalAs UnmanagedType.ByValTStr是字符串 SizeConst是大小 Const是常量
        public string uuid; // uuid是特征UUID
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] // MarshalAs是MarshalAs UnmanagedType.ByValTStr是字符串 SizeConst是大小 Const是常量
        public string userDescription; // userDescription是用户描述
    }

    [DllImport("BleWinrtDll.dll", EntryPoint = "ScanCharacteristics", CharSet = CharSet.Unicode)] // DllImport是DllImport EntryPoint是入口点    
    public static extern void ScanCharacteristics(string deviceId, string serviceId); // ScanCharacteristics是扫描特征

    [DllImport("BleWinrtDll.dll", EntryPoint = "PollCharacteristic")] // DllImport是DllImport EntryPoint是入口点    
    public static extern ScanStatus PollCharacteristic(out Characteristic characteristic, bool block); // PollCharacteristic是轮询特征

    [DllImport("BleWinrtDll.dll", EntryPoint = "SubscribeCharacteristic", CharSet = CharSet.Unicode)] // DllImport是DllImport EntryPoint是入口点    
    public static extern bool SubscribeCharacteristic(string deviceId, string serviceId, string characteristicId, bool block); // SubscribeCharacteristic是订阅特征

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BLEData
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] // MarshalAs是MarshalAs UnmanagedType.ByValArray是字节数组 SizeConst是大小 Const是常量
        public byte[] buf;
        [MarshalAs(UnmanagedType.I2)]
        public short size; // size是大小
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] // MarshalAs是MarshalAs UnmanagedType.ByValTStr是字符串 SizeConst是大小 Const是常量
        public string deviceId; // deviceId是设备ID
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string serviceUuid; // serviceUuid是服务UUID
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string characteristicUuid; // characteristicUuid是特征UUID
    }

    [DllImport("BleWinrtDll.dll", EntryPoint = "PollData")] 
    public static extern bool PollData(out BLEData data, bool block); // PollData是轮询数据

    [DllImport("BleWinrtDll.dll", EntryPoint = "SendData")]
    public static extern bool SendData(in BLEData data, bool block); // SendData是发送数据

    [DllImport("BleWinrtDll.dll", EntryPoint = "Quit")]
    public static extern void Quit(); // Quit是退出

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ErrorMessage
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] // MarshalAs是MarshalAs UnmanagedType.ByValTStr是字符串 SizeConst是大小 Const是常量
        public string msg; // msg是消息
    }

    [DllImport("BleWinrtDll.dll", EntryPoint = "GetError")] 
    public static extern void GetError(out ErrorMessage buf); // GetError是获取错误
}