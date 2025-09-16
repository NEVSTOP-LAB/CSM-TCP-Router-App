import socket
import struct
import threading
import queue
import json
import time
from enum import Enum

class PacketType(Enum):
    INFO = 0x00
    ERROR = 0x01
    CMD = 0x02
    RESP = 0x03
    ASYNC_RESP = 0x04
    STATUS = 0x05

class TcpRouterClient:
    def __init__(self):
        self.socket = None
        self.connected = False
        self.host = ""
        self.port = 0
        self.recv_thread = None
        self.stop_event = threading.Event()
        self.response_queue = queue.Queue()
        self.async_response_callbacks = {}
        self.status_callbacks = {}
        self.async_response_queue = queue.Queue()
        self.status_queue = queue.Queue()
        self.lock = threading.Lock()

    def connect(self, host, port, timeout=5):
        """连接到CSM-TCP-Router服务器"""
        try:
            self.host = host
            self.port = port
            self.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.socket.settimeout(timeout)
            self.socket.connect((host, port))
            self.connected = True
            self.stop_event.clear()
            self.recv_thread = threading.Thread(target=self._receive_thread)
            self.recv_thread.daemon = True
            self.recv_thread.start()
            return True
        except Exception as e:
            print(f"连接失败: {e}")
            self.connected = False
            return False

    def disconnect(self):
        """断开与服务器的连接"""
        if self.connected:
            self.stop_event.set()
            try:
                if self.socket:
                    self.socket.close()
            except:
                pass
            self.connected = False
            if self.recv_thread:
                self.recv_thread.join(timeout=2)

    def send_message(self, message, packet_type=PacketType.CMD, flag1=0, flag2=0):
        """发送消息到服务器"""
        if not self.connected:
            print("未连接到服务器")
            return False

        try:
            # 计算数据长度
            data_len = len(message.encode('utf-8'))
            # 构建数据包
            header = struct.pack('!IBBBB', data_len, 0x01, packet_type.value, flag1, flag2)
            # 发送数据包
            with self.lock:
                self.socket.sendall(header)
                self.socket.sendall(message.encode('utf-8'))
            return True
        except Exception as e:
            print(f"发送消息失败: {e}")
            self.connected = False
            return False

    def send_message_and_wait_for_reply(self, message, timeout=5):
        """发送消息并等待回复"""
        if not self.send_message(message):
            return None

        try:
            response = self.response_queue.get(timeout=timeout)
            return response
        except queue.Empty:
            print("等待回复超时")
            return None

    def post_message(self, message):
        """发送异步消息"""
        return self.send_message(message)

    def post_no_rep_message(self, message):
        """发送无返回异步消息"""
        return self.send_message(message)

    def ping(self, timeout=2):
        """Ping服务器"""
        start_time = time.time()
        response = self.send_message_and_wait_for_reply("Ping", timeout=timeout)
        if response:
            elapsed = time.time() - start_time
            return True, elapsed
        return False, 0

    def register_status_change(self, status_name, module_name, callback=None):
        """注册状态变化通知"""
        cmd = f"{status_name}@{module_name} -><register>"
        success = self.send_message(cmd)
        if success and callback:
            with self.lock:
                self.status_callbacks[(status_name, module_name)] = callback
        return success

    def unregister_status_change(self, status_name, module_name):
        """取消注册状态变化通知"""
        cmd = f"{status_name}@{module_name} -><unregister>"
        success = self.send_message(cmd)
        if success:
            with self.lock:
                key = (status_name, module_name)
                if key in self.status_callbacks:
                    del self.status_callbacks[key]
        return success

    def wait_for_server(self, host, port, timeout=30):
        """等待服务器可用"""
        start_time = time.time()
        while time.time() - start_time < timeout:
            if self.connect(host, port, timeout=1):
                self.disconnect()
                return True
            time.sleep(0.5)
        return False

    def _receive_thread(self):
        """接收线程，处理来自服务器的消息"""
        while not self.stop_event.is_set():
            try:
                # 接收包头
                header = self._receive_all(8)  # 4+1+1+1+1=8字节
                if not header:
                    break

                # 解析包头
                data_len, version, packet_type, flag1, flag2 = struct.unpack('!IBBBB', header)

                # 接收数据
                data = self._receive_all(data_len).decode('utf-8')

                # 处理不同类型的数据包
                if packet_type == PacketType.RESP.value:
                    self.response_queue.put(data)
                elif packet_type == PacketType.ASYNC_RESP.value:
                    self._handle_async_response(data)
                elif packet_type == PacketType.STATUS.value:
                    self._handle_status(data)
                elif packet_type == PacketType.INFO.value:
                    print(f"[INFO] {data}")
                elif packet_type == PacketType.ERROR.value:
                    print(f"[ERROR] {data}")

            except Exception as e:
                if not self.stop_event.is_set():
                    print(f"接收数据错误: {e}")
                break

        # 线程结束，标记断开连接
        self.connected = False

    def _receive_all(self, size):
        """接收指定大小的数据"""
        data = b''
        while len(data) < size:
            packet = self.socket.recv(size - len(data))
            if not packet:
                return b''
            data += packet
        return data

    def _handle_async_response(self, data):
        """处理异步响应"""
        self.async_response_queue.put(data)
        # 这里可以根据需要调用注册的回调函数
        # 例如，可以解析data中的信息，找到对应的回调函数并调用

    def _handle_status(self, data):
        """处理状态更新"""
        self.status_queue.put(data)
        # 解析状态数据并调用相应的回调函数
        # 简化处理，实际应用中可能需要更复杂的解析逻辑
        parts = data.split(' >> ', 1)
        if len(parts) == 2:
            status_info, _ = parts
            status_parts = status_info.split(' <- ', 1)
            if len(status_parts) == 2:
                status_name, module_name = status_parts
                with self.lock:
                    callback = self.status_callbacks.get((status_name, module_name))
                    if callback:
                        callback(data)

    def obtain(self):
        """获取客户端实例（模拟LabVIEW的Obtain.vi）"""
        # 在Python中，这个方法可以简单返回自身实例
        return self

    def release(self):
        """释放客户端资源（模拟LabVIEW的Release.vi）"""
        self.disconnect()

# 示例用法
if __name__ == "__main__":
    client = TcpRouterClient()

    # 连接服务器
    if client.connect("localhost", 30007):
        print("连接成功")

        # 发送Ping命令
        success, elapsed = client.ping()
        if success:
            print(f"Ping成功，延迟: {elapsed*1000:.2f}ms")

        # 发送命令并等待回复
        response = client.send_message_and_wait_for_reply("List")
        print(f"List命令回复: {response}")

        # 订阅状态变化
        def status_callback(data):
            print(f"收到状态更新: {data}")

        client.register_status_change("Status", "AI", status_callback)

        # 保持连接一段时间
        time.sleep(5)

        # 取消订阅
        client.unregister_status_change("Status", "AI")

        # 断开连接
        client.disconnect()
        print("已断开连接")
    else:
        print("连接失败")