import hid
import time

VID = 0x674e
PID = 0x000a

# 打开设备
device = hid.device()
try:
    device.open(VID, PID)
    print("板子连接成功！")
    print(f"厂商: {device.get_manufacturer_string()}")
    print(f"产品: {device.get_product_string()}")
except Exception as e:
    print(f"连接失败: {e}")
    print("请确认板子已插上，或者尝试以管理员身份运行")
    exit()

# 发送 0xAA 命令：左右都震动"射击"效果
data = [0x00] * 65  # HID 报文：第一个字节是 Report ID
data[0] = 0x00      # Report ID (通常为 0)
data[1] = 0xAA      # 命令：内部库振动
data[2] = 0x01      # L路：射击
data[3] = 0x01      # R路：射击

print("\n发送振动命令...")
try:
    device.write(data)
    print("命令发送成功！（如果接了马达应该能感觉到振动）")
except Exception as e:
    print(f"发送失败: {e}")

# 等一下看看有没有回复
time.sleep(0.5)

# 尝试读取回复
try:
    response = device.read(64, timeout_ms=1000)
    if response:
        hex_str = ' '.join(f'{b:02x}' for b in response)
        print(f"板子回复: {hex_str}")
    else:
        print("没有收到回复（也可能是正常的）")
except Exception as e:
    print(f"读取失败: {e}")

device.close()
print("\n测试完成！")