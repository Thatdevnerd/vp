using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VPNCore.Cryptography;
using VPNCore.Interfaces;
using VPNCore.Models;
using VPNCore.Networking;
using VPNCore.Services;

namespace VPNHealthMonitorDemo;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("VPN Health Monitor Demo");
        Console.WriteLine("======================");

        var host = CreateHostBuilder(args).Build();
        
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var transport = scope.ServiceProvider.GetRequiredService<IVPNTransport>();
        
        // Create health monitor
        var healthMonitor = new VPNClientHealthMonitor(
            scope.ServiceProvider.GetRequiredService<ILogger<VPNClientHealthMonitor>>(),
            transport);

        // Subscribe to events
        healthMonitor.ClientTimedOut += (sender, client) =>
        {
            Console.WriteLine($"🔴 CLIENT TIMED OUT: {client.ClientId} (Last heartbeat: {client.LastHeartbeat:HH:mm:ss})");
        };

        healthMonitor.ClientHealthChanged += (sender, client) =>
        {
            var status = client.IsHealthy ? "🟢 HEALTHY" : "🟡 UNHEALTHY";
            Console.WriteLine($"{status}: {client.ClientId} (RTT: {client.AverageRoundTripTime}ms, Missed: {client.MissedHeartbeats})");
        };

        // Simulate multiple clients connecting
        var clients = new List<VPNClientInfo>();
        
        for (int i = 1; i <= 5; i++)
        {
            var client = new VPNClientInfo
            {
                ClientId = $"client-{i:D2}",
                Status = VPNConnectionStatus.Connected,
                AssignedIP = System.Net.IPAddress.Parse($"10.0.0.{i + 10}"),
                ConnectedAt = DateTime.UtcNow.AddMinutes(-i),
                Username = $"user{i}"
            };
            
            clients.Add(client);
            healthMonitor.RegisterClient(client);
            Console.WriteLine($"✅ Registered client: {client.ClientId} ({client.AssignedIP})");
        }

        Console.WriteLine("\n📊 Starting health monitoring simulation...\n");

        // Simulate heartbeats and client behavior
        var random = new Random();
        var simulationTime = 0;

        while (simulationTime < 300) // Run for 5 minutes
        {
            await Task.Delay(2000); // Check every 2 seconds
            simulationTime += 2;

            Console.WriteLine($"\n⏰ Time: {simulationTime}s");

            foreach (var client in clients.ToList())
            {
                // Simulate different client behaviors
                var behavior = random.Next(1, 101);

                if (behavior <= 70) // 70% - Normal heartbeat
                {
                    var heartbeat = new VPNHeartbeatPacket
                    {
                        SessionId = client.ClientId,
                        ClientTime = DateTime.UtcNow.AddMilliseconds(-random.Next(10, 200)),
                        ClientUptime = (long)(DateTime.UtcNow - client.ConnectedAt).TotalSeconds,
                        ClientStatus = "Active"
                    };

                    healthMonitor.UpdateClientHeartbeat(client.ClientId, heartbeat);
                    Console.WriteLine($"💓 {client.ClientId}: Heartbeat (RTT: {client.AverageRoundTripTime}ms)");
                }
                else if (behavior <= 85) // 15% - Delayed heartbeat
                {
                    // Skip this heartbeat to simulate network delay
                    client.MissedHeartbeats++;
                    Console.WriteLine($"⏳ {client.ClientId}: Delayed heartbeat (Missed: {client.MissedHeartbeats})");
                }
                else if (behavior <= 95) // 10% - Intermittent issues
                {
                    if (random.Next(1, 3) == 1) // 50% chance to send heartbeat
                    {
                        var heartbeat = new VPNHeartbeatPacket
                        {
                            SessionId = client.ClientId,
                            ClientTime = DateTime.UtcNow.AddMilliseconds(-random.Next(200, 1000)),
                            ClientUptime = (long)(DateTime.UtcNow - client.ConnectedAt).TotalSeconds,
                            ClientStatus = "Unstable"
                        };

                        healthMonitor.UpdateClientHeartbeat(client.ClientId, heartbeat);
                        Console.WriteLine($"⚠️  {client.ClientId}: Unstable connection (RTT: {client.AverageRoundTripTime}ms)");
                    }
                    else
                    {
                        client.MissedHeartbeats++;
                        Console.WriteLine($"📶 {client.ClientId}: Connection issues (Missed: {client.MissedHeartbeats})");
                    }
                }
                else // 5% - Client disconnects
                {
                    Console.WriteLine($"🔌 {client.ClientId}: Simulating disconnect");
                    healthMonitor.UnregisterClient(client.ClientId);
                    clients.Remove(client);
                }
            }

            // Show health summary
            var healthyCount = healthMonitor.GetHealthyClients().Count();
            var unhealthyCount = healthMonitor.GetUnhealthyClients().Count();
            
            Console.WriteLine($"📈 Health Summary: {healthyCount} healthy, {unhealthyCount} unhealthy");

            // Occasionally add new clients
            if (simulationTime % 60 == 0 && clients.Count < 8)
            {
                var newClientId = $"client-{DateTime.Now.Ticks % 1000:D3}";
                var newClient = new VPNClientInfo
                {
                    ClientId = newClientId,
                    Status = VPNConnectionStatus.Connected,
                    AssignedIP = System.Net.IPAddress.Parse($"10.0.0.{random.Next(50, 100)}"),
                    ConnectedAt = DateTime.UtcNow,
                    Username = $"user{newClientId}"
                };
                
                clients.Add(newClient);
                healthMonitor.RegisterClient(newClient);
                Console.WriteLine($"🆕 New client connected: {newClient.ClientId}");
            }
        }

        Console.WriteLine("\n🏁 Simulation completed!");
        
        // Final health report
        Console.WriteLine("\n📋 Final Health Report:");
        Console.WriteLine("========================");
        
        foreach (var client in healthMonitor.GetHealthyClients())
        {
            Console.WriteLine($"🟢 {client.ClientId}: Healthy (RTT: {client.AverageRoundTripTime}ms, Connected: {(DateTime.UtcNow - client.ConnectedAt).TotalMinutes:F1}min)");
        }
        
        foreach (var client in healthMonitor.GetUnhealthyClients())
        {
            Console.WriteLine($"🔴 {client.ClientId}: Unhealthy (Missed: {client.MissedHeartbeats}, Last: {(DateTime.UtcNow - client.LastHeartbeat).TotalSeconds:F0}s ago)");
        }

        healthMonitor.Dispose();
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                
                services.AddSingleton<IVPNCryptography, VPNCryptography>();
                services.AddSingleton<IVPNTransport>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<UDPVPNTransport>>();
                    return new UDPVPNTransport(null, logger); // Mock transport for demo
                });
            });
}