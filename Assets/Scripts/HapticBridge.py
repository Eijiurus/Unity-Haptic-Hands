"""
HapticBridge.py
================
把 Unity 通过 UDP 发来的振动指令转发给 DRV2605 振动板（USB HID）。

架构和 SensorBridge.py 反向：
    Unity (HapticOutput.cs) ──UDP→ HapticBridge.py ──HID→ DRV2605 ──→ L/R 马达

UDP 协议（纯文本，UTF-8）
    EFFECT,<L>,<R>      触发 DRV2605 库振动；L、R 是 0~123 的库 ID（0 = 该路不振）
    STOP                两路都停（发 0 效果）
    PING                返回 PONG（用于 Unity 端做"bridge 是否在跑"的诊断）

HID 协议（来自 test_vibrate.py 和『手机多马达振动驱动板.docx』第 5.2 节『立即输出 DRV2605 库振动』）
    65 字节 report，字节布局：
        [0] = 0x00          report ID
        [1] = 0xBB          命令类型：DRV2605 库振动
        [2] = L 路 effect    (写入 L 路 DRV2605 寄存器 0x04)
        [3] = 0x00          L 路 slot 2 = 终止
        [10] = R 路 effect   (写入 R 路 DRV2605 寄存器 0x04)
        [11] = 0x00         R 路 slot 2 = 终止
        其余字节 = 0x00

依赖：
    pip install hid
（如果 import hid 失败，试 pip install hidapi 然后还是 import hid）

环境变量覆盖：
    HAPTIC_UDP_PORT    默认 5006
    HAPTIC_HID_VID     默认 0x674e
    HAPTIC_HID_PID     默认 0x000a
    HAPTIC_VERBOSE     "1" 打开每帧日志（默认关闭，避免影响 60Hz 触发频率）
"""

import os
import socket
import sys
import time

try:
    import hid
except ImportError:
    print("[HapticBridge] 缺少 hid 包。先运行：pip install hid")
    sys.exit(1)

UDP_IP = os.environ.get("HAPTIC_UDP_IP", "127.0.0.1")
UDP_PORT = int(os.environ.get("HAPTIC_UDP_PORT", "5006"))
VID = int(os.environ.get("HAPTIC_HID_VID", "0x674e"), 0)
PID = int(os.environ.get("HAPTIC_HID_PID", "0x000a"), 0)
VERBOSE = os.environ.get("HAPTIC_VERBOSE", "0") == "1"

# HID 设备断开后多久重试一次（秒）。设为 0 关闭重连。
RECONNECT_INTERVAL = 1.5


def open_hid_device():
    """打开 DRV2605 振动板。失败返回 None（让上层走重连循环），不抛异常。"""
    try:
        d = hid.device()
        d.open(VID, PID)
        try:
            d.set_nonblocking(True)
        except Exception:
            pass
        print(f"[HapticBridge] HID OK (VID=0x{VID:04X} PID=0x{PID:04X}).")
        return d
    except Exception as e:
        print(f"[HapticBridge] HID 打开失败: {e}")
        return None


def build_library_effect_report(left_effect: int, right_effect: int) -> list:
    """构造一帧 0xBB 库振动 HID 报文。0~127 范围；超出会被钳制。"""
    left_effect = max(0, min(127, int(left_effect)))
    right_effect = max(0, min(127, int(right_effect)))
    data = [0x00] * 65
    data[0] = 0x00       # report id
    data[1] = 0xBB       # 库振动
    data[2] = left_effect
    data[3] = 0x00       # L 路 slot 2 = 终止
    data[10] = right_effect
    data[11] = 0x00      # R 路 slot 2 = 终止
    return data


def write_with_retry(device, payload, reopen_callback):
    """
    写一帧，失败则尝试重开设备一次。reopen_callback 由调用者用来更新外部 device 引用。
    返回新的 device（可能是同一个，也可能是重开后的，或 None）。
    """
    if device is None:
        return reopen_callback()

    try:
        device.write(payload)
        return device
    except Exception as e:
        print(f"[HapticBridge] HID write 失败: {e} — 重连中...")
        try:
            device.close()
        except Exception:
            pass
        return reopen_callback()


def parse_command(text: str):
    """
    返回 ('EFFECT', L, R) 或 ('STOP',) 或 ('PING',) 或 None（无法解析）。
    容错：忽略前后空白、忽略大小写、允许 EFFECT 单参数（视作两路同效果）。
    """
    text = text.strip()
    if not text:
        return None

    head, _, tail = text.partition(",")
    head = head.strip().upper()

    if head == "STOP":
        return ("STOP",)
    if head == "PING":
        return ("PING",)
    if head == "EFFECT":
        parts = [p.strip() for p in tail.split(",") if p.strip()]
        try:
            if len(parts) == 1:
                v = int(parts[0])
                return ("EFFECT", v, v)
            if len(parts) >= 2:
                return ("EFFECT", int(parts[0]), int(parts[1]))
        except ValueError:
            return None
    return None


def main():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((UDP_IP, UDP_PORT))
    print(f"[HapticBridge] UDP listening on {UDP_IP}:{UDP_PORT}")

    device = open_hid_device()
    last_reconnect_attempt = 0.0

    def try_reopen():
        nonlocal device, last_reconnect_attempt
        now = time.time()
        if RECONNECT_INTERVAL <= 0:
            return None
        if now - last_reconnect_attempt < RECONNECT_INTERVAL:
            return None
        last_reconnect_attempt = now
        device = open_hid_device()
        return device

    sent_count = 0
    last_log_time = time.time()

    try:
        while True:
            try:
                data, addr = sock.recvfrom(256)
            except KeyboardInterrupt:
                raise
            except Exception as e:
                print(f"[HapticBridge] UDP recv 出错: {e}")
                continue

            text = data.decode("utf-8", errors="ignore")
            cmd = parse_command(text)
            if cmd is None:
                if VERBOSE:
                    print(f"[HapticBridge] 无法解析: {text!r}")
                continue

            if cmd[0] == "PING":
                try:
                    sock.sendto(b"PONG", addr)
                except Exception:
                    pass
                continue

            if cmd[0] == "STOP":
                left, right = 0, 0
            else:  # EFFECT
                _, left, right = cmd

            payload = build_library_effect_report(left, right)
            device = write_with_retry(device, payload, try_reopen)

            sent_count += 1
            if VERBOSE:
                print(f"[HapticBridge] L={left:>3} R={right:>3} "
                      f"(from {addr[0]}:{addr[1]}, hid={'OK' if device else 'OFFLINE'})")
            elif time.time() - last_log_time > 5.0:
                print(f"[HapticBridge] {sent_count} effect(s) in last 5s "
                      f"(hid={'OK' if device else 'OFFLINE'})")
                sent_count = 0
                last_log_time = time.time()

    except KeyboardInterrupt:
        print("\n[HapticBridge] 用户中断，退出。")
    finally:
        if device is not None:
            try:
                device.close()
            except Exception:
                pass
        sock.close()


if __name__ == "__main__":
    main()
