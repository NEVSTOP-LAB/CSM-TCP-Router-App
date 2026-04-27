using System;
using CsmTcpRouter;

namespace CsmTcpRouter.Examples.BasicUsage
{
    /// <summary>
    /// Basic usage example for csm-tcp-router-client (C#).
    /// Mirrors SDK/python/examples/basic_usage.py.
    ///
    /// Prerequisites: a running CSM-TCP-Router server (LabVIEW app).
    /// The reference app defaults to port 30007.
    /// </summary>
    public static class Program
    {
        private const string Host = "localhost";
        private const int Port = 30007;

        public static int Main(string[] args)
        {
            // 1. Wait until the server is ready.
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

            // 2. Connect (use as IDisposable so Disconnect is always called).
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

                // 3. Ping
                var (ok, elapsed) = client.Ping(TimeSpan.FromSeconds(2));
                Console.WriteLine(ok
                    ? $"Ping OK  latency={elapsed.TotalMilliseconds:F1} ms"
                    : "Ping failed.");

                // 4. List CSM modules
                string modules = client.ListModules();
                Console.WriteLine($"\nLoaded modules:\n{modules}");

                // 5. List API for the first module (if any)
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

                // 6. Send a synchronous command (uncomment & adapt for your CSM):
                // var resp = client.SendAndWait("API: Read -@ DAQmx");
                // Console.WriteLine($"\nSync response: {resp.Text}");

                // 7. Send an async command + wait for cmd-resp handshake:
                // client.Post("API: Start Sampling -> DAQmx");

                // 8. Send a no-reply command:
                // client.PostNoReply("API: Reset ->| DAQmx");

                Console.WriteLine("\nDone.");
            }
            Console.WriteLine("Disconnected.");
            return 0;
        }
    }
}
