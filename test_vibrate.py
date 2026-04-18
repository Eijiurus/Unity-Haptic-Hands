import hid
import time

VID = 0x674e
PID = 0x000a

device = hid.device()
device.open(VID, PID)
print("板子已连接，开始遍历 DRV2605 效果")
print("按 Ctrl+C 可以停止\n")

# 可以自定义要测试的效果列表
effects_to_test = [1, 10, 14, 24, 27, 47, 48, 52, 58, 64, 82, 118]

try:
    for effect in effects_to_test:
        print(f"播放效果 {effect} (0x{effect:02X})...")
        data = [0x00] * 65
        data[0] = 0x00
        data[1] = 0xBB
        data[2] = effect    # L路 DRV2605 寄存器 0x04
        data[3] = 0x00      # 结束标记
        data[10] = effect   # R路
        data[11] = 0x00
        device.write(data)
        time.sleep(2)  # 每个效果间隔 2 秒
except KeyboardInterrupt:
    print("\n用户中断")

device.close()
print("测试结束")