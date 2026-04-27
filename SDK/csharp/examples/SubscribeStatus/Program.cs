// Subscribe-status example for CsmTcpRouterClient.
//
// Demonstrates registering a callback for status broadcasts emitted by a
// subscribed CSM module, plus polling the StatusQueue.
//
// Run: dotnet run --project examples/SubscribeStatus

using System;
using System.Threading;
using CsmTcpRouter;

namespace CsmTcpRouter.Examples.SubscribeStatus
{
    internal static class Program
    {
        private const string Host = "localhost";
        private const int Port = 30007;
        private const string StatusName = "Status";   // status to subscribe to
        private const string ModuleName = "AI";       // CSM module name

        private static int Main()
        {
            using var client = new TcpRouterClient();
            try
            {
                client.Connect(Host, Port, timeoutSeconds: 5);
            }
            catch (RouterConnectionError ex)
            {
                Console.WriteLine($"Connection failed: {ex.Message}");
                return 1;
            }

            try
            {
                // Subscribe with a callback (runs on the receive task – keep it fast).
                client.SubscribeStatus(StatusName, ModuleName, callback: notif =>
                {
                    Console.WriteLine(
                        $"[callback] {notif.StatusName} = {notif.Data} from {notif.ModuleName}");
                });

                // Poll the queue for up to 30 seconds.
                Console.WriteLine($"Subscribed to {StatusName}@{ModuleName}. " +
                                  "Polling for 30 s...");
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    if (client.StatusQueue.TryTake(out var notif, TimeSpan.FromMilliseconds(500)))
                    {
                        Console.WriteLine(
                            $"[queue]    {notif.StatusName} = {notif.Data} from {notif.ModuleName}");
                    }
                }

                client.UnsubscribeStatus(StatusName, ModuleName);
                Console.WriteLine("Unsubscribed. Done.");
            }
            catch (TcpRouterError ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }

            return 0;
        }
    }
}
