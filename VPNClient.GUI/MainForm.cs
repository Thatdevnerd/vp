using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VPNClient;
using VPNCore.Cryptography;
using VPNCore.Interfaces;
using VPNCore.Models;
using VPNCore.Networking;

namespace VPNClient.GUI;

public partial class MainForm : Form
{
    private readonly IServiceProvider _serviceProvider;
    private VPNClientService? _vpnClient;
    private IHost? _host;
    private bool _isConnected = false;

    public MainForm()
    {
        InitializeComponent();
        _serviceProvider = ConfigureServices();
        UpdateUI();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        
        services.AddSingleton<VPNConfiguration>();
        services.AddSingleton<IVPNCryptography, VPNCryptography>();
        services.AddSingleton<IVPNTunnel, VPNTunnel>();
        services.AddTransient<VPNClientService>();
        
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        return services.BuildServiceProvider();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // Form
        Text = "VPN Client";
        Size = new Size(500, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        // Server Address
        var lblServer = new Label
        {
            Text = "Server Address:",
            Location = new Point(20, 20),
            Size = new Size(100, 23)
        };
        Controls.Add(lblServer);

        var txtServerAddress = new TextBox
        {
            Name = "txtServerAddress",
            Text = "127.0.0.1",
            Location = new Point(130, 20),
            Size = new Size(200, 23)
        };
        Controls.Add(txtServerAddress);

        // Server Port
        var lblPort = new Label
        {
            Text = "Port:",
            Location = new Point(350, 20),
            Size = new Size(40, 23)
        };
        Controls.Add(lblPort);

        var txtPort = new TextBox
        {
            Name = "txtPort",
            Text = "1194",
            Location = new Point(400, 20),
            Size = new Size(60, 23)
        };
        Controls.Add(txtPort);

        // Connect Button
        var btnConnect = new Button
        {
            Name = "btnConnect",
            Text = "Connect",
            Location = new Point(20, 60),
            Size = new Size(100, 30),
            BackColor = Color.LightGreen
        };
        btnConnect.Click += BtnConnect_Click;
        Controls.Add(btnConnect);

        // Disconnect Button
        var btnDisconnect = new Button
        {
            Name = "btnDisconnect",
            Text = "Disconnect",
            Location = new Point(130, 60),
            Size = new Size(100, 30),
            BackColor = Color.LightCoral,
            Enabled = false
        };
        btnDisconnect.Click += BtnDisconnect_Click;
        Controls.Add(btnDisconnect);

        // Status Label
        var lblStatus = new Label
        {
            Name = "lblStatus",
            Text = "Status: Disconnected",
            Location = new Point(20, 100),
            Size = new Size(200, 23),
            Font = new Font("Arial", 10, FontStyle.Bold)
        };
        Controls.Add(lblStatus);

        // Virtual IP Label
        var lblVirtualIP = new Label
        {
            Name = "lblVirtualIP",
            Text = "Virtual IP: Not assigned",
            Location = new Point(20, 130),
            Size = new Size(200, 23)
        };
        Controls.Add(lblVirtualIP);

        // Statistics Group
        var grpStats = new GroupBox
        {
            Text = "Statistics",
            Location = new Point(20, 160),
            Size = new Size(440, 120)
        };
        Controls.Add(grpStats);

        var lblBytesSent = new Label
        {
            Name = "lblBytesSent",
            Text = "Bytes Sent: 0",
            Location = new Point(10, 25),
            Size = new Size(200, 23)
        };
        grpStats.Controls.Add(lblBytesSent);

        var lblBytesReceived = new Label
        {
            Name = "lblBytesReceived",
            Text = "Bytes Received: 0",
            Location = new Point(10, 50),
            Size = new Size(200, 23)
        };
        grpStats.Controls.Add(lblBytesReceived);

        var lblConnectionTime = new Label
        {
            Name = "lblConnectionTime",
            Text = "Connection Time: 00:00:00",
            Location = new Point(10, 75),
            Size = new Size(200, 23)
        };
        grpStats.Controls.Add(lblConnectionTime);

        // Log TextBox
        var txtLog = new TextBox
        {
            Name = "txtLog",
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Location = new Point(20, 290),
            Size = new Size(440, 80),
            BackColor = Color.Black,
            ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 8)
        };
        Controls.Add(txtLog);

        ResumeLayout(false);
    }

    private async void BtnConnect_Click(object? sender, EventArgs e)
    {
        try
        {
            var txtServerAddress = Controls.Find("txtServerAddress", true)[0] as TextBox;
            var txtPort = Controls.Find("txtPort", true)[0] as TextBox;

            if (string.IsNullOrWhiteSpace(txtServerAddress?.Text))
            {
                MessageBox.Show("Please enter a server address.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!int.TryParse(txtPort?.Text, out var port) || port <= 0 || port > 65535)
            {
                MessageBox.Show("Please enter a valid port number (1-65535).", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Configure VPN settings
            var config = _serviceProvider.GetRequiredService<VPNConfiguration>();
            config.ServerAddress = txtServerAddress.Text;
            config.ServerPort = port;

            // Create and start VPN client
            _vpnClient = _serviceProvider.GetRequiredService<VPNClientService>();
            _vpnClient.StatusChanged += OnConnectionStatusChanged;

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(config);
                    services.AddSingleton<IVPNCryptography, VPNCryptography>();
                    services.AddSingleton<IVPNTunnel, VPNTunnel>();
                    services.AddSingleton<IVPNClient>(_vpnClient);
                });

            _host = hostBuilder.Build();
            
            LogMessage("Connecting to VPN server...");
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            LogMessage($"Connection failed: {ex.Message}");
        }
    }

    private async void BtnDisconnect_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_host != null)
            {
                LogMessage("Disconnecting from VPN server...");
                await _host.StopAsync();
                _host.Dispose();
                _host = null;
            }

            if (_vpnClient != null)
            {
                _vpnClient.StatusChanged -= OnConnectionStatusChanged;
                _vpnClient = null;
            }

            _isConnected = false;
            UpdateUI();
            LogMessage("Disconnected from VPN server");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during disconnect: {ex.Message}", "Disconnect Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            LogMessage($"Disconnect error: {ex.Message}");
        }
    }

    private void OnConnectionStatusChanged(object? sender, VPNConnectionStatus e)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnConnectionStatusChanged(sender, e)));
            return;
        }

        switch (e)
        {
            case VPNConnectionStatus.Connecting:
                LogMessage("Connecting to VPN server...");
                break;

            case VPNConnectionStatus.Connected:
                _isConnected = true;
                LogMessage("Connected to VPN server");
                break;

            case VPNConnectionStatus.Disconnected:
                _isConnected = false;
                LogMessage("Disconnected from VPN server");
                break;

            case VPNConnectionStatus.Error:
                _isConnected = false;
                LogMessage($"Connection error: {e}");
                break;
        }

        UpdateUI();
    }

    private void UpdateUI()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(UpdateUI));
            return;
        }

        var btnConnect = Controls.Find("btnConnect", true)[0] as Button;
        var btnDisconnect = Controls.Find("btnDisconnect", true)[0] as Button;
        var lblStatus = Controls.Find("lblStatus", true)[0] as Label;

        if (btnConnect != null && btnDisconnect != null && lblStatus != null)
        {
            btnConnect.Enabled = !_isConnected;
            btnDisconnect.Enabled = _isConnected;
            lblStatus.Text = _isConnected ? "Status: Connected" : "Status: Disconnected";
            lblStatus.ForeColor = _isConnected ? Color.Green : Color.Red;
        }
    }

    private void LogMessage(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => LogMessage(message)));
            return;
        }

        var txtLog = Controls.Find("txtLog", true)[0] as TextBox;
        if (txtLog != null)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText($"[{timestamp}] {message}\r\n");
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_isConnected)
        {
            BtnDisconnect_Click(null, EventArgs.Empty);
        }
        base.OnFormClosing(e);
    }
}