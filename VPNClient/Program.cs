using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VPNCore.Cryptography;
using VPNCore.Interfaces;
using VPNCore.Models;
using VPNCore.Networking;
using VPNClient;

var builder = Host.CreateApplicationBuilder(args);

// Configure services
builder.Services.AddSingleton<VPNConfiguration>(provider =>
{
    var config = new VPNConfiguration();
    
    // Parse command line arguments
    var args = Environment.GetCommandLineArgs();
    for (int i = 1; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--server":
                config.ServerAddress = args[i + 1];
                i++;
                break;
            case "--port":
                if (int.TryParse(args[i + 1], out var port))
                    config.ServerPort = port;
                i++;
                break;
        }
    }

    // Default server if not specified
    if (string.IsNullOrEmpty(config.ServerAddress))
        config.ServerAddress = "127.0.0.1";

    return config;
});

builder.Services.AddSingleton<IVPNCryptography, VPNCryptography>();
builder.Services.AddSingleton<IVPNTunnel, VPNTunnel>();
builder.Services.AddSingleton<IVPNClient, VPNClientService>();

// Configure logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
});

var host = builder.Build();

Console.WriteLine("VPN Client v1.0.0");
Console.WriteLine("Use --server <address> --port <port> to specify server");
Console.WriteLine("Press Ctrl+C to disconnect");

var client = host.Services.GetRequiredService<IVPNClient>();
var config = host.Services.GetRequiredService<VPNConfiguration>();

client.StatusChanged += (sender, status) =>
{
    Console.WriteLine($"Status: {status}");
};

client.LogMessage += (sender, message) =>
{
    Console.WriteLine($"[LOG] {message}");
};

try
{
    Console.WriteLine($"Connecting to {config.ServerAddress}:{config.ServerPort}...");
    await client.ConnectAsync(config);
    
    Console.WriteLine("Connected! Press any key to disconnect...");
    Console.ReadKey();
    
    await client.DisconnectAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Client error: {ex.Message}");
    return 1;
}

return 0;