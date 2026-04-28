using System;
using CsmTcpRouter;

namespace CsmTcpRouter.Examples.ClientConsole
{
    /// <summary>
    /// 交互式客户端控制台示例。连接到正在运行的 CSM-TCP-Router 服务器，
    /// 接收用户从 stdin 输入的命令，并通过 SDK 转发。
    ///
    /// 同样的命令集、提示符和输出格式也在 Python（examples/client_console.py）
    /// 和 C（examples/client_console.c）SDK 示例中实现，因此三种语言的行为一致。
    /// </summary>
    public static class Program
    {
        private const string DefaultHost = "localhost";
        private const int DefaultPort = 30007;

        private const string HelpText =
            "Available commands:\n" +
            "  help                 Show this help text\n" +
            "  quit / exit          Disconnect and exit\n" +
            "  ping                 Measure round-trip latency\n" +
            "  list                 List CSM modules loaded on the server\n" +
            "  api <module>         List the API of a module\n" +
            "  state <module>       List the states of a module\n" +
            "  mhelp <module>       Server-side Help for a module\n" +
            "  send <command>       Send a synchronous command and print the response\n" +
            "  post <command>       Send an asynchronous command (-> suffix)\n" +
            "  nopost <command>     Send a no-reply asynchronous command (->|)\n" +
            "  sub <status>@<mod>   Subscribe to a status broadcast\n" +
            "  unsub <status>@<mod> Unsubscribe from a status broadcast";

        public static int Main(string[] args)
        {
            string host = args.Length > 0 ? args[0] : DefaultHost;
            int port = args.Length > 1 ? int.Parse(args[1]) : DefaultPort;

            Console.WriteLine("CSM-TCP-Router Client Console");
            Console.WriteLine($"Connecting to {host}:{port} ...");

            using var client = new TcpRouterClient();
            try
            {
                client.Connect(host, port);
            }
            catch (RouterConnectionException exc)
            {
                Console.WriteLine($"Error: {exc.Message}");
                return 1;
            }

            Console.WriteLine($"Connected to {host}:{port}.  Type 'help' for commands, 'quit' to exit.");

            while (true)
            {
                Console.Write("csm> ");
                string line = Console.ReadLine();
                if (line == null)
                {
                    Console.WriteLine();
                    break;
                }

                bool keepRunning;
                try
                {
                    keepRunning = Dispatch(client, line);
                }
                catch (CsmTcpRouterException exc)
                {
                    Console.WriteLine($"Error: {exc.Message}");
                    continue;
                }
                catch (ArgumentException exc)
                {
                    Console.WriteLine($"Error: {exc.Message}");
                    continue;
                }

                if (!keepRunning)
                {
                    break;
                }
            }

            client.Disconnect();
            Console.WriteLine("Disconnected.");
            return 0;
        }

        private static bool Dispatch(TcpRouterClient client, string line)
        {
            line = line.Trim();
            if (line.Length == 0)
            {
                return true;
            }

            int spaceIndex = line.IndexOf(' ');
            string cmd = (spaceIndex < 0 ? line : line.Substring(0, spaceIndex)).ToLowerInvariant();
            string arg = spaceIndex < 0 ? string.Empty : line.Substring(spaceIndex + 1).Trim();

            switch (cmd)
            {
                case "quit":
                case "exit":
                    return false;

                case "help":
                    Console.WriteLine(HelpText);
                    return true;

                case "ping":
                {
                    var (ok, elapsed) = client.Ping();
                    Console.WriteLine(ok
                        ? $"Ping OK  latency={elapsed.TotalMilliseconds:F1} ms"
                        : "Ping failed.");
                    return true;
                }

                case "list":
                    Console.WriteLine(client.ListModules());
                    return true;

                case "api":
                    if (arg.Length == 0) Console.WriteLine("Error: usage: api <module>");
                    else Console.WriteLine(client.ListApi(arg));
                    return true;

                case "state":
                    if (arg.Length == 0) Console.WriteLine("Error: usage: state <module>");
                    else Console.WriteLine(client.ListStates(arg));
                    return true;

                case "mhelp":
                    if (arg.Length == 0) Console.WriteLine("Error: usage: mhelp <module>");
                    else Console.WriteLine(client.Help(arg));
                    return true;

                case "send":
                    if (arg.Length == 0)
                    {
                        Console.WriteLine("Error: usage: send <command>");
                    }
                    else
                    {
                        var resp = client.SendAndWait(arg);
                        Console.WriteLine($"Response: {resp.Text}");
                    }
                    return true;

                case "post":
                    if (arg.Length == 0)
                    {
                        Console.WriteLine("Error: usage: post <command>");
                    }
                    else
                    {
                        client.RegisterAsyncCallback(arg, OnAsync);
                        client.Post(arg);
                        Console.WriteLine("Async command sent.");
                    }
                    return true;

                case "nopost":
                    if (arg.Length == 0)
                    {
                        Console.WriteLine("Error: usage: nopost <command>");
                    }
                    else
                    {
                        client.PostNoReply(arg);
                        Console.WriteLine("No-reply command sent.");
                    }
                    return true;

                case "sub":
                {
                    var (status, module) = SplitStatusModule(arg);
                    client.SubscribeStatus(status, module, OnStatus);
                    Console.WriteLine($"Subscribed to {status}@{module}");
                    return true;
                }

                case "unsub":
                {
                    var (status, module) = SplitStatusModule(arg);
                    client.UnsubscribeStatus(status, module);
                    Console.WriteLine($"Unsubscribed from {status}@{module}");
                    return true;
                }

                default:
                    Console.WriteLine($"Error: unknown command '{cmd}'.  Type 'help' for the command list.");
                    return true;
            }
        }

        private static (string Status, string Module) SplitStatusModule(string arg)
        {
            int at = arg.IndexOf('@');
            if (at < 0)
            {
                throw new ArgumentException("expected '<status>@<module>'");
            }
            string status = arg.Substring(0, at).Trim();
            string module = arg.Substring(at + 1).Trim();
            if (status.Length == 0 || module.Length == 0)
            {
                throw new ArgumentException("expected '<status>@<module>'");
            }
            return (status, module);
        }

        private static void OnStatus(StatusNotification notification)
        {
            Console.WriteLine();
            Console.WriteLine($"[STATUS] {notification.StatusName}@{notification.ModuleName}: {notification.Data}");
        }

        private static void OnAsync(AsyncResponse response)
        {
            Console.WriteLine();
            Console.WriteLine($"[ASYNC] {response.Text}  (cmd={response.OriginalCommand})");
        }
    }
}
