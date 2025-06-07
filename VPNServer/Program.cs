using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VPNCore.Cryptography;
using VPNCore.Interfaces;
using VPNCore.Models;
using VPNCore.Networking;
using VPNServer;

var builder = Host.CreateApplicationBuilder(args);

// Configure services
builder.Services.AddSingleton<VPNConfiguration>(provider =>
{
    var config = new VPNConfiguration();
    // Load configuration from appsettings.json or command line
    return config;
});

builder.Services.AddSingleton<IVPNCryptography, VPNCryptography>();
builder.Services.AddSingleton<IVPNServer, VPNServerService>();
builder.Services.AddHostedService<VPNServerService>();

// Configure logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
});

var host = builder.Build();

Console.WriteLine("Starting VPN Server...");
Console.WriteLine("Press Ctrl+C to stop the server");

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Server error: {ex.Message}");
    return 1;
}

return 0;