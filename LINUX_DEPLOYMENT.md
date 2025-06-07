# VPN Application - Linux Deployment Guide

## 🐧 Overview

This guide covers deploying and running the VPN application on Linux systems. The application has been tested and optimized for Linux environments with full support for TUN/TAP interfaces and system integration.

## 📋 System Requirements

### Minimum Requirements
- **Operating System**: Linux kernel 2.6+ with TUN/TAP support
- **Runtime**: .NET 8.0 Runtime
- **Memory**: 512MB RAM
- **Storage**: 100MB free space
- **Network**: Network interface with internet connectivity

### Recommended Requirements
- **Operating System**: Ubuntu 20.04+, CentOS 8+, or equivalent
- **Runtime**: .NET 8.0 SDK (for development)
- **Memory**: 2GB RAM
- **Storage**: 1GB free space
- **Network**: Gigabit network interface

### Supported Distributions
- ✅ Ubuntu 18.04+
- ✅ Debian 10+
- ✅ CentOS 8+
- ✅ RHEL 8+
- ✅ Fedora 32+
- ✅ openSUSE Leap 15+
- ✅ Arch Linux
- ✅ Alpine Linux 3.12+

## 🔧 Prerequisites

### 1. Install .NET 8.0

#### Ubuntu/Debian
```bash
# Add Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install .NET 8.0
sudo apt update
sudo apt install -y dotnet-sdk-8.0
```

#### CentOS/RHEL/Fedora
```bash
# Add Microsoft package repository
sudo rpm -Uvh https://packages.microsoft.com/config/centos/8/packages-microsoft-prod.rpm

# Install .NET 8.0
sudo dnf install -y dotnet-sdk-8.0
```

#### Arch Linux
```bash
sudo pacman -S dotnet-sdk
```

### 2. Install Required System Packages

#### Ubuntu/Debian
```bash
sudo apt update
sudo apt install -y iproute2 iptables net-tools
```

#### CentOS/RHEL/Fedora
```bash
sudo dnf install -y iproute iptables net-tools
```

### 3. Enable TUN/TAP Support

#### Load TUN Module
```bash
# Load the module
sudo modprobe tun

# Make it persistent
echo 'tun' | sudo tee -a /etc/modules-load.d/tun.conf

# Verify TUN device exists
ls -la /dev/net/tun
```

#### Enable IP Forwarding
```bash
# Enable for current session
sudo sysctl net.ipv4.ip_forward=1

# Make it persistent
echo 'net.ipv4.ip_forward = 1' | sudo tee -a /etc/sysctl.conf
```

## 🚀 Quick Start

### 1. Download and Build
```bash
# Clone the repository
git clone https://github.com/Thatdevnerd/vpn-project.git
cd vpn-project

# Build the application
dotnet build --configuration Release
```

### 2. Start VPN Server
```bash
# Using the Linux startup script
sudo ./start-server-linux.sh

# Or manually
cd VPNServer
sudo dotnet run --configuration Release
```

### 3. Connect VPN Client
```bash
# In another terminal
sudo ./start-client-linux.sh

# Or connect to remote server
sudo ./start-client-linux.sh --server 192.168.1.100 --port 1194
```

## 🔧 Production Deployment

### 1. Automated Setup
```bash
# Run the comprehensive setup script
sudo ./setup-linux.sh
```

This script will:
- ✅ Check system requirements
- ✅ Install TUN/TAP support
- ✅ Create system user and directories
- ✅ Build the application
- ✅ Create systemd service files
- ✅ Configure firewall rules
- ✅ Enable IP forwarding

### 2. Manual Setup

#### Create VPN User
```bash
sudo useradd -r -s /bin/false vpnuser
sudo mkdir -p /var/log/vpn /etc/vpn
sudo chown vpnuser:vpnuser /var/log/vpn
```

#### Create Systemd Service
```bash
sudo tee /etc/systemd/system/vpn-server.service > /dev/null << EOF
[Unit]
Description=VPN Server
After=network.target

[Service]
Type=notify
User=root
Group=root
WorkingDirectory=/opt/vpn/VPNServer
ExecStart=/usr/bin/dotnet /opt/vpn/VPNServer/bin/Release/net8.0/VPNServer.dll
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
```

#### Deploy Application
```bash
# Create deployment directory
sudo mkdir -p /opt/vpn
sudo cp -r . /opt/vpn/
sudo chown -R root:root /opt/vpn

# Build for production
cd /opt/vpn
sudo dotnet build --configuration Release

# Enable and start service
sudo systemctl daemon-reload
sudo systemctl enable vpn-server
sudo systemctl start vpn-server
```

## 🔥 Firewall Configuration

### Basic iptables Rules
```bash
# Allow VPN traffic
sudo iptables -A INPUT -p udp --dport 1194 -j ACCEPT

# Allow forwarding for VPN clients
sudo iptables -A FORWARD -i tun+ -j ACCEPT
sudo iptables -A FORWARD -o tun+ -j ACCEPT

# NAT for VPN clients (replace eth0 with your internet interface)
sudo iptables -t nat -A POSTROUTING -s 10.8.0.0/24 -o eth0 -j MASQUERADE

# Save rules (Ubuntu/Debian)
sudo iptables-save | sudo tee /etc/iptables/rules.v4

# Save rules (CentOS/RHEL)
sudo iptables-save | sudo tee /etc/sysconfig/iptables
```

### UFW Configuration (Ubuntu)
```bash
# Enable UFW
sudo ufw enable

# Allow VPN port
sudo ufw allow 1194/udp

# Allow forwarding
sudo ufw route allow in on tun0
sudo ufw route allow out on tun0

# Enable NAT
echo 'net/ipv4/ip_forward=1' | sudo tee -a /etc/ufw/sysctl.conf
```

### firewalld Configuration (CentOS/RHEL)
```bash
# Add VPN service
sudo firewall-cmd --permanent --add-port=1194/udp
sudo firewall-cmd --permanent --add-masquerade
sudo firewall-cmd --reload
```

## 📊 Service Management

### Systemd Commands
```bash
# Start service
sudo systemctl start vpn-server

# Stop service
sudo systemctl stop vpn-server

# Restart service
sudo systemctl restart vpn-server

# Enable auto-start
sudo systemctl enable vpn-server

# Check status
sudo systemctl status vpn-server

# View logs
sudo journalctl -u vpn-server -f

# View recent logs
sudo journalctl -u vpn-server --since "1 hour ago"
```

### Log Management
```bash
# View server logs
sudo tail -f /var/log/vpn/server.log

# View system logs
sudo journalctl -u vpn-server -f

# Rotate logs
sudo logrotate -f /etc/logrotate.d/vpn
```

## 🔍 Monitoring and Troubleshooting

### Health Checks
```bash
# Check if server is listening
sudo netstat -tuln | grep 1194

# Check TUN interfaces
ip tuntap list

# Check routing table
ip route show

# Check active connections
sudo ss -tuln | grep 1194
```

### Common Issues

#### 1. Permission Denied
```bash
# Error: Permission denied when creating TUN interface
# Solution: Run as root or add CAP_NET_ADMIN capability
sudo setcap cap_net_admin+ep /usr/bin/dotnet
```

#### 2. TUN/TAP Not Available
```bash
# Error: /dev/net/tun not found
# Solution: Load TUN module
sudo modprobe tun
echo 'tun' | sudo tee -a /etc/modules-load.d/tun.conf
```

#### 3. Port Already in Use
```bash
# Error: Port 1194 already in use
# Solution: Check for other VPN services
sudo netstat -tuln | grep 1194
sudo systemctl stop openvpn
```

#### 4. IP Forwarding Disabled
```bash
# Error: Clients can't access internet
# Solution: Enable IP forwarding
sudo sysctl net.ipv4.ip_forward=1
echo 'net.ipv4.ip_forward = 1' | sudo tee -a /etc/sysctl.conf
```

### Performance Monitoring
```bash
# Monitor CPU and memory usage
top -p $(pgrep -f VPNServer)

# Monitor network traffic
sudo iftop -i tun0

# Monitor connections
watch 'sudo ss -tuln | grep 1194'

# Check system resources
sudo systemctl status vpn-server
```

## 🔒 Security Considerations

### System Hardening
```bash
# Disable unnecessary services
sudo systemctl disable bluetooth
sudo systemctl disable cups

# Update system packages
sudo apt update && sudo apt upgrade -y

# Configure automatic security updates
sudo apt install unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
```

### Network Security
```bash
# Disable IPv6 if not needed
echo 'net.ipv6.conf.all.disable_ipv6 = 1' | sudo tee -a /etc/sysctl.conf

# Enable SYN flood protection
echo 'net.ipv4.tcp_syncookies = 1' | sudo tee -a /etc/sysctl.conf

# Disable ICMP redirects
echo 'net.ipv4.conf.all.accept_redirects = 0' | sudo tee -a /etc/sysctl.conf
```

### File Permissions
```bash
# Secure configuration files
sudo chmod 600 /etc/vpn/server.json
sudo chown root:root /etc/vpn/server.json

# Secure log files
sudo chmod 640 /var/log/vpn/*.log
sudo chown vpnuser:adm /var/log/vpn/*.log
```

## 📈 Performance Optimization

### Kernel Parameters
```bash
# Optimize network buffers
echo 'net.core.rmem_max = 16777216' | sudo tee -a /etc/sysctl.conf
echo 'net.core.wmem_max = 16777216' | sudo tee -a /etc/sysctl.conf
echo 'net.ipv4.udp_mem = 102400 873800 16777216' | sudo tee -a /etc/sysctl.conf

# Apply changes
sudo sysctl -p
```

### Application Settings
```json
{
  "VPN": {
    "MaxClients": 1000,
    "BufferSize": 65536,
    "WorkerThreads": 4,
    "LogLevel": "Warning"
  }
}
```

## 🔄 Backup and Recovery

### Configuration Backup
```bash
# Backup configuration
sudo tar -czf vpn-config-$(date +%Y%m%d).tar.gz /etc/vpn/

# Backup application
sudo tar -czf vpn-app-$(date +%Y%m%d).tar.gz /opt/vpn/
```

### Disaster Recovery
```bash
# Restore configuration
sudo tar -xzf vpn-config-20231201.tar.gz -C /

# Restore application
sudo tar -xzf vpn-app-20231201.tar.gz -C /

# Restart services
sudo systemctl restart vpn-server
```

## 📚 Additional Resources

### Documentation
- [.NET on Linux](https://docs.microsoft.com/en-us/dotnet/core/install/linux)
- [TUN/TAP Documentation](https://www.kernel.org/doc/Documentation/networking/tuntap.txt)
- [systemd Service Files](https://www.freedesktop.org/software/systemd/man/systemd.service.html)

### Monitoring Tools
- **htop**: Process monitoring
- **iftop**: Network traffic monitoring
- **netstat/ss**: Network connections
- **tcpdump**: Packet capture
- **wireshark**: Network analysis

### Security Tools
- **fail2ban**: Intrusion prevention
- **rkhunter**: Rootkit detection
- **lynis**: Security auditing
- **nmap**: Network scanning

This guide provides comprehensive instructions for deploying the VPN application on Linux systems with production-ready configurations and security best practices.