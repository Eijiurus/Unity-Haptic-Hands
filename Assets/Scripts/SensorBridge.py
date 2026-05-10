import os
import serial
import socket
import time

# ================= 配置区 =================
# 也可用环境变量覆盖，例如 PowerShell: $env:SERIAL_PORT = "COM4"
SERIAL_PORT = os.environ.get("SERIAL_PORT", "COM4")
BAUD_RATE = 115200
UDP_IP = "127.0.0.1"
UDP_PORT = 5005
# 每帧 print 会非常拖慢环路；仅在排查问题时设为 True
VERBOSE_PRINT = False
# 读超时：换 COM/驱动后若仍用 readline() 等换行，易反复卡满超时 → 体感延迟大
SERIAL_READ_TIMEOUT_SEC = 0.02
# ==========================================


def main():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    try:
        ser = serial.Serial(
            SERIAL_PORT,
            BAUD_RATE,
            timeout=SERIAL_READ_TIMEOUT_SEC,
            write_timeout=0.1,
        )
        print(f"✅ 成功连接到串口 {SERIAL_PORT}！等待接收分号结尾的数据...")
        try:
            ser.set_low_latency_mode(True)
        except (AttributeError, NotImplementedError):
            pass
        ser.reset_input_buffer()
        time.sleep(0.1)

        while True:
            # 协议是分号结尾，用 read_until 不依赖 CRLF（换蓝牙虚拟串口后常见差异）
            # pyserial 老版本只用 expected；terminator 是较新版本的关键字名
            chunk = ser.read_until(expected=b";")
            # 超时可能只读到半包，没有分号则不打 UDP，避免 Unity 侧解析异常/抖动
            if not chunk or not chunk.endswith(b";"):
                continue
            raw_data = (
                chunk.decode("utf-8", errors="ignore")
                .strip()
                .replace("\r", "")
                .replace("\n", "")
                .rstrip(";")
            )

            if raw_data:
                if VERBOSE_PRINT:
                    print(f"实时采集: {raw_data}")
                sock.sendto(raw_data.encode("utf-8"), (UDP_IP, UDP_PORT))

    except Exception as e:
        print(f"❌ 运行错误: {e}\n(请确保 OneCOM 等上位机软件已彻底关闭！)")
    finally:
        if 'ser' in locals() and ser.is_open:
            ser.close()
        sock.close()

if __name__ == '__main__':
    main()