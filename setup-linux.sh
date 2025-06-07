#!/bin/bash

# VPN Application Linux Setup Script
# This script prepares the Linux environment for running the VPN application

set -e

echo "🐧 VPN Application Linux Setup"
echo "=============================="

# Check if running as root
if [[ $EUID -eq 0 ]]; then
    echo "⚠️  Warning: Running as root. This is required for VPN operations."
else
    echo "❌ Error: This script must be run as root (use sudo)"
    echo "   VPN applications require root privileges to:"
    echo "   - Create TUN/TAP interfaces"
    echo "   - Modify routing tables"
    echo "   - Bind to privileged ports"
    exit 1
fi

# Check Linux distribution
if [ -f /etc/os-release ]; then
    . /etc/os-release
    echo "📋 Detected OS: $NAME $VERSION"
else
    echo "⚠️  Warning: Could not detect Linux distribution"
fi

# Check .NET installation
echo "🔍 Checking .NET installation..."
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    echo "✅ .NET $DOTNET_VERSION is installed"
else
    echo "❌ Error: .NET is not installed"
    echo "   Please install .NET 8.0 SDK:"
    echo "   https://docs.microsoft.com/en-us/dotnet/core/install/linux"
    exit 1
fi

# Check TUN/TAP support
echo "🔍 Checking TUN/TAP support..."
if [ -c /dev/net/tun ]; then
    echo "✅ TUN/TAP device is available"
else
    echo "⚠️  TUN/TAP device not found, attempting to load module..."
    modprobe tun
    if [ -c /dev/net/tun ]; then
        echo "✅ TUN/TAP module loaded successfully"
    else
        echo "❌ Error: Could not load TUN/TAP module"
        echo "   Please ensure your kernel supports TUN/TAP"
        exit 1
    fi
fi

# Check required tools
echo "🔍 Checking required tools..."
REQUIRED_TOOLS=("ip" "iptables" "ss")
for tool in "${REQUIRED_TOOLS[@]}"; do
    if command -v $tool &> /dev/null; then
        echo "✅ $tool is available"
    else
        echo "❌ Error: $tool is not installed"
        echo "   Please install iproute2 and iptables packages"
        exit 1
    fi
done

# Create VPN user and group (optional, for better security)
echo "👤 Setting up VPN user..."
if ! id "vpnuser" &>/dev/null; then
    useradd -r -s /bin/false vpnuser
    echo "✅ Created vpnuser account"
else
    echo "✅ vpnuser account already exists"
fi

# Set up directories
echo "📁 Setting up directories..."
mkdir -p /var/log/vpn
mkdir -p /etc/vpn
chown vpnuser:vpnuser /var/log/vpn
echo "✅ Directories created"

# Build the application
echo "🔨 Building VPN application..."
cd "$(dirname "$0")"
dotnet build --configuration Release
if [ $? -eq 0 ]; then
    echo "✅ Application built successfully"
else
    echo "❌ Error: Failed to build application"
    exit 1
fi

# Create systemd service files
echo "⚙️  Creating systemd service files..."

# VPN Server service
cat > /etc/systemd/system/vpn-server.service << EOF
[Unit]
Description=VPN Server
After=network.target

[Service]
Type=notify
User=root
Group=root
WorkingDirectory=$(pwd)/VPNServer
ExecStart=/usr/bin/dotnet $(pwd)/VPNServer/bin/Release/net8.0/VPNServer.dll
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal
SyslogIdentifier=vpn-server

# Security settings
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/log/vpn /etc/vpn

# Network capabilities
AmbientCapabilities=CAP_NET_ADMIN CAP_NET_RAW
CapabilityBoundingSet=CAP_NET_ADMIN CAP_NET_RAW

[Install]
WantedBy=multi-user.target
EOF

echo "✅ Created vpn-server.service"

# Create configuration file
cat > /etc/vpn/server.json << EOF
{
  "VPN": {
    "ServerPort": 1194,
    "Protocol": "UDP",
    "VirtualNetworkAddress": "10.8.0.0",
    "VirtualNetworkMask": "255.255.255.0",
    "MaxClients": 100,
    "EncryptionAlgorithm": "AES-256-CBC",
    "DNSServers": ["8.8.8.8", "8.8.4.4"],
    "LogLevel": "Information"
  }
}
EOF

echo "✅ Created default configuration"

# Set up firewall rules (basic example)
echo "🔥 Setting up basic firewall rules..."
cat > /etc/vpn/firewall-rules.sh << 'EOF'
#!/bin/bash
# Basic VPN firewall rules
# Customize these rules based on your security requirements

# Allow VPN traffic
iptables -A INPUT -p udp --dport 1194 -j ACCEPT

# Allow forwarding for VPN clients
iptables -A FORWARD -i tun+ -j ACCEPT
iptables -A FORWARD -o tun+ -j ACCEPT

# NAT for VPN clients (replace eth0 with your internet interface)
# iptables -t nat -A POSTROUTING -s 10.8.0.0/24 -o eth0 -j MASQUERADE

echo "Firewall rules applied (basic setup)"
echo "Please customize /etc/vpn/firewall-rules.sh for your environment"
EOF

chmod +x /etc/vpn/firewall-rules.sh
echo "✅ Created firewall rules script"

# Enable IP forwarding
echo "🔀 Enabling IP forwarding..."
echo 'net.ipv4.ip_forward = 1' > /etc/sysctl.d/99-vpn.conf
sysctl -p /etc/sysctl.d/99-vpn.conf
echo "✅ IP forwarding enabled"

# Reload systemd
systemctl daemon-reload

echo ""
echo "🎉 Linux setup completed successfully!"
echo ""
echo "📋 Next steps:"
echo "   1. Review configuration in /etc/vpn/server.json"
echo "   2. Customize firewall rules in /etc/vpn/firewall-rules.sh"
echo "   3. Start the VPN server:"
echo "      sudo systemctl start vpn-server"
echo "      sudo systemctl enable vpn-server"
echo ""
echo "📊 Service management:"
echo "   Status:  sudo systemctl status vpn-server"
echo "   Logs:    sudo journalctl -u vpn-server -f"
echo "   Stop:    sudo systemctl stop vpn-server"
echo ""
echo "⚠️  Security notes:"
echo "   - Review and customize firewall rules"
echo "   - Consider using certificates for authentication"
echo "   - Monitor logs regularly"
echo "   - Keep the system updated"
echo ""
echo "🔧 Manual start (for testing):"
echo "   cd VPNServer && sudo dotnet run"
echo ""
EOF