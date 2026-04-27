// Basic usage example for CsmTcpRouterClient.
//
// Prerequisites
// -------------
// A running CSM-TCP-Router server (LabVIEW app). The reference app defaults
// to port 30007. Start it from CSM-TCP-Router(Server).vi.
//
// Run this example:
//   dotnet run --project examples/BasicUsage

using System;
using CsmTcpRouter;

namespace CsmTcpRouter.Examples.BasicUsage
{
    internal static class Program
    {
        private const string Host = "localhost";
        private const int Port = 30007;

        private static int Main()
        {
            // 1. Wait until the server is ready (optional).
            Console.Write("Waiting for server ... ");
            using (var probe = new TcpRouterClient())
            {
                if (!probe.WaitForServer(Host, Port, timeoutSeconds: 30, retryIntervalSeconds: 0.5))
                {
                    Console.WriteLine("TIMEOUT - server did not start within 30 s.");
                    return 1;
                }
            }
            Console.WriteLine("ready.");

            // 2. Connect (use using-block so Disconnect is always called).
            using var client = new TcpRouterClient();
            try
            {
                client.Connect(Host, Port);
            }
            catch (RouterConnectionError ex)
            {
                Console.WriteLine($"Connection failed: {ex.Message}");
                return 1;
            }

            Console.WriteLine($"Connected to {Host}:{Port}");

            // 3. Ping – verify round-trip latency.
            var (ok, elapsed) = client.Ping();
            Console.WriteLine(ok
                ? $"Ping OK  latency={elapsed * 1000:F1} ms"
                : "Ping failed.");

            // 4. List CSM modules loaded on the server.
            string modules = client.ListModules();
            Console.WriteLine($"\nLoaded modules:\n{modules}");

            // 5. List the API for the first module (if any).
            string trimmed = modules.Trim();
            if (trimmed.Length > 0)
            {
                string firstModule = trimmed.Split('\n')[0].Trim();
                string apiText = client.ListApi(firstModule);
                Console.WriteLine($"\nAPI for '{firstModule}':\n{apiText}");
            }

            // 6. Send a synchronous command (uncomment with a real API of yours):
            //    var resp = client.SendAndWait("API: Read -@ DAQmx");
            //    Console.WriteLine($"\nSync response: {resp.Text}");

            // 7. Send an asynchronous command (uncomment as needed):
            //    client.Post("API: Start Sampling -> DAQmx");

            // 8. Send a no-reply command:
            //    client.PostNoReply("API: Reset ->| DAQmx");

            Console.WriteLine("\nDone.");
            return 0;
        }
    }
}
