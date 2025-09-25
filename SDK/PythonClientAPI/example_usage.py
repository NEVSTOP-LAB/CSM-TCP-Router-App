import time
from tcp_router_client import TcpRouterClient

"""CSM-TCP-Router Python客户端API使用示例"""

def main():
    # 创建客户端实例
    client = TcpRouterClient()

    print("CSM-TCP-Router Python客户端API示例")
    print("================================")

    # 示例1: 连接到服务器
    print("\n示例1: 连接到服务器")
    if client.connect("localhost", 30007):
        print("✅ 成功连接到服务器")
    else:
        print("❌ 连接服务器失败，请确保服务器已启动")
        return

    # 示例2: Ping服务器
    print("\n示例2: Ping服务器")
    success, elapsed = client.ping(timeout=2)
    if success:
        print(f"✅ Ping成功，延迟: {elapsed*1000:.2f}ms")
    else:
        print("❌ Ping失败")

    # 示例3: 发送同步命令并等待回复
    print("\n示例3: 发送同步命令并等待回复")
    # 列出所有CSM模块
    response = client.send_message_and_wait_for_reply("List")
    print(f"命令: List")
    print(f"回复: {response}")

    # 列出特定模块的API
    # 注意：这里假设存在名为"AI"的模块，如果不存在，您需要修改为实际存在的模块名
    module_name = "AI"
    response = client.send_message_and_wait_for_reply(f"List API {module_name}")
    print(f"\n命令: List API {module_name}")
    print(f"回复: {response}")

    # 示例4: 发送异步命令
    print("\n示例4: 发送异步命令")
    # 注意：这里的命令需要根据实际的CSM模块进行调整
    async_cmd = "API: Read Channels -> AI"
    success = client.post_message(async_cmd)
    print(f"命令: {async_cmd}")
    print(f"发送结果: {'✅ 成功' if success else '❌ 失败'}")

    # 示例5: 发送无返回异步命令
    print("\n示例5: 发送无返回异步命令")
    # 注意：这里的命令需要根据实际的CSM模块进行调整
    no_rep_cmd = "API: Refresh ->| System"
    success = client.post_no_rep_message(no_rep_cmd)
    print(f"命令: {no_rep_cmd}")
    print(f"发送结果: {'✅ 成功' if success else '❌ 失败'}")

    # 示例6: 订阅状态变化
    print("\n示例6: 订阅状态变化")
    # 状态变化回调函数
    def status_callback(status_data):
        print(f"📢 收到状态更新: {status_data}")

    # 注册状态变化通知
    # 注意：这里假设存在名为"AI"的模块和"Status"状态，如果不存在，您需要修改为实际存在的模块名和状态名
    success = client.register_status_change("Status", "AI", status_callback)
    print(f"订阅 'Status@AI' 结果: {'✅ 成功' if success else '❌ 失败'}")

    # 保持连接一段时间，等待状态更新
    print("\n等待5秒，观察状态更新...")
    time.sleep(5)

    # 取消订阅
    success = client.unregister_status_change("Status", "AI")
    print(f"取消订阅 'Status@AI' 结果: {'✅ 成功' if success else '❌ 失败'}")

    # 示例7: 等待服务器可用
    print("\n示例7: 等待服务器可用（演示用，当前已连接）")
    # 断开当前连接
    client.disconnect()
    print("已断开连接")

    # 等待服务器可用
    print("等待服务器可用，最多等待10秒...")
    # 注意：如果服务器未运行，这个调用将会超时
    success = client.wait_for_server("localhost", 9999, timeout=10)
    print(f"服务器可用检查结果: {'✅ 服务器可用' if success else '❌ 服务器不可用'}")

    # 重新连接（如果服务器可用）
    if success:
        client.connect("localhost", 9999)
        print("✅ 已重新连接到服务器")

    # 示例8: 释放资源
    print("\n示例8: 释放资源")
    client.release()
    print("✅ 客户端资源已释放")

    print("\n示例执行完毕")

if __name__ == "__main__":
    main()