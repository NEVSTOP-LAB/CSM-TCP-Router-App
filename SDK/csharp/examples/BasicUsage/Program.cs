using System;
using CsmTcpRouter;

namespace CsmTcpRouter.Examples.BasicUsage
{
    /// <summary>
    /// csm-tcp-router-client（C#）的基本用法示例。
    /// 镜像 SDK/python/examples/basic_usage.py。
    ///
    /// 前提条件：正在运行的 CSM-TCP-Router 服务器（LabVIEW 应用程序）。
    /// 参考应用程序默认使用端口 30007。
    /// </summary>
    public static class Program
    {
        private const string Host = "localhost";
        private const int Port = 30007;

        public static int Main(string[] args)
        {
            // 1. 等待服务器就绪。
            Console.Write("Waiting for server ... ");
            using (var probe = new TcpRouterClient())
            {
                bool ok = probe.WaitForServer(Host, Port, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
                if (!ok)
                {
                    Console.WriteLine("TIMEOUT - server did not start within 30 s.");
                    return 1;
                }
            }
            Console.WriteLine("ready.");

            // 2. 连接（使用 IDisposable，确保始终调用 Disconnect）。
            using (var client = new TcpRouterClient())
            {
                try
                {
                    client.Connect(Host, Port, TimeSpan.FromSeconds(5));
                }
                catch (RouterConnectionException exc)
                {
                    Console.WriteLine($"Connection failed: {exc.Message}");
                    return 1;
                }

                Console.WriteLine($"Connected to {Host}:{Port}");

                // 3. Ping 测试
                var (ok, elapsed) = client.Ping(TimeSpan.FromSeconds(2));
                Console.WriteLine(ok
                    ? $"Ping OK  latency={elapsed.TotalMilliseconds:F1} ms"
                    : "Ping failed.");

                // 4. 列出 CSM 模块
                string modules = client.ListModules();
                Console.WriteLine($"\nLoaded modules:\n{modules}");

                // 5. 列出第一个模块的 API（如有）
                string firstModule = null;
                foreach (var line in modules.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        firstModule = trimmed;
                        break;
                    }
                }
                if (firstModule != null)
                {
                    string api = client.ListApi(firstModule);
                    Console.WriteLine($"\nAPI for '{firstModule}':\n{api}");
                }

                // 6. 发送同步命令（取消注释并适配您的 CSM）：
                // var resp = client.SendAndWait("API: Read -@ DAQmx");
                // Console.WriteLine($"\nSync response: {resp.Text}");

                // 7. 发送异步命令并等待 cmd-resp 握手：
                // client.Post("API: Start Sampling -> DAQmx");

                // 8. 发送无回复命令：
                // client.PostNoReply("API: Reset ->| DAQmx");

                Console.WriteLine("\nDone.");
            }
            Console.WriteLine("Disconnected.");
            return 0;
        }
    }
}
