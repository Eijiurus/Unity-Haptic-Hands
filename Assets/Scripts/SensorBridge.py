import serial
import socket
import time

# ================= 配置区 =================
SERIAL_PORT = 'COM3'      # 对应你电脑的串口号
BAUD_RATE = 115200        # 蓝牙波特率
UDP_IP = "127.0.0.1"      
UDP_PORT = 5005           
# ==========================================

def main():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    
    try:
        ser = serial.Serial(SERIAL_PORT, BAUD_RATE, timeout=0.05)
        print(f"✅ 成功连接到串口 {SERIAL_PORT}！等待接收分号结尾的数据...")
        ser.reset_input_buffer()
        time.sleep(0.1) 

        while True:
            if ser.in_waiting > 0:
                # 读取一行，解码，并用 strip() 和 rstrip(';') 剔除回车换行以及末尾的分号
                raw_data = ser.readline().decode('utf-8', errors='ignore').strip().rstrip(';')
                
                if raw_data:
                    # 此时打印的应该是纯净的 11 个数字，如: 1504,900,100,0,0,0,0,0,0,0,0
                    print(f"实时采集: {raw_data}")
                    sock.sendto(raw_data.encode('utf-8'), (UDP_IP, UDP_PORT))

    except Exception as e:
        print(f"❌ 运行错误: {e}\n(请确保 OneCOM 等上位机软件已彻底关闭！)")
    finally:
        if 'ser' in locals() and ser.is_open:
            ser.close()
        sock.close()

if __name__ == '__main__':
    main()