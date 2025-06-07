# VPN Application in C#

A full-featured VPN (Virtual Private Network) application built in C# with .NET 8, featuring both server and client components with a graphical user interface.

## Features

### Core Features
- **Secure Encryption**: AES-256-GCM encryption with ECDH key exchange
- **Cross-Platform**: Supports Windows and Linux
- **Multiple Clients**: Server can handle multiple concurrent connections
- **TUN/TAP Interface**: Virtual network interface for packet routing
- **Real-time Monitoring**: Connection status, bandwidth usage, and client management
- **Configurable**: Extensive configuration options for both server and client

### Security Features
- **Strong Encryption**: AES-256-GCM for data encryption
- **Key Exchange**: Elliptic Curve Diffie-Hellman (ECDH) for secure key exchange
- **Authentication**: Digital signatures for packet authentication
- **Session Management**: Secure session handling with automatic cleanup

### Network Features
- **UDP/TCP Support**: Configurable transport protocol
- **IP Address Pool**: Automatic virtual IP assignment
- **Routing**: Automatic route configuration
- **DNS Support**: Custom DNS server configuration
- **Keep-Alive**: Connection monitoring and automatic reconnection

## Project Structure

```
VPNApp/
├── VPNCore/                    # Core VPN library
│   ├── Models/                 # Data models and configurations
│   ├── Interfaces/             # Service interfaces
│   ├── Cryptography/           # Encryption and security
│   ├── Networking/             # Network protocols and tunneling
│   └── Utils/                  # Utility functions
├── VPNServer/                  # VPN Server application
├── VPNClient/                  # Console VPN Client
├── VPNClient.GUI/              # GUI VPN Client (Windows Forms)
├── Tests/                      # Unit and integration tests
├── Documentation/              # Additional documentation
└── Scripts/                    # Installation and setup scripts
```

## Requirements

### System Requirements
- **.NET 8.0** or later
- **Windows 10/11** or **Linux** (Ubuntu 20.04+)
- **Administrator/Root privileges** (for network interface creation)

### Windows-Specific Requirements
- **TAP-Windows driver** (for TUN/TAP interface)
- **Visual Studio 2022** or **Visual Studio Code** (for development)

### Linux-Specific Requirements
- **TUN/TAP support** in kernel
- **iproute2** package (`ip` command)

## Installation

### 1. Clone the Repository
```bash
git clone <repository-url>
cd VPNApp
```

### 2. Build the Solution
```bash
dotnet build VPNApp.sln
```

### 3. Install Dependencies (Linux)
```bash
# Ubuntu/Debian
sudo apt-get update
sudo apt-get install iproute2

# Enable TUN/TAP module
sudo modprobe tun
```

### 4. Install TAP-Windows (Windows)
Download and install the TAP-Windows driver from the OpenVPN website.

## Usage

### Running the VPN Server

```bash
cd VPNServer
sudo dotnet run
```

The server will start on port 1194 by default. You can configure the port and other settings in `appsettings.json`.

### Running the Console Client

```bash
cd VPNClient
sudo dotnet run -- --server 127.0.0.1 --port 1194
```

### Running the GUI Client (Windows)

```bash
cd VPNClient.GUI
dotnet run
```

## Configuration

### Server Configuration (`VPNServer/appsettings.json`)

```json
{
  "VPN": {
    "ServerPort": 1194,
    "Protocol": "UDP",
    "VirtualNetworkAddress": "10.8.0.0",
    "VirtualNetworkMask": "255.255.255.0",
    "MaxClients": 100,
    "EncryptionAlgorithm": "AES-256-GCM",
    "DNSServers": ["8.8.8.8", "8.8.4.4"]
  }
}
```

## Quick Start

1. **Build the solution**:
   ```bash
   dotnet build VPNApp.sln
   ```

2. **Start the server** (in one terminal):
   ```bash
   cd VPNServer
   sudo dotnet run
   ```

3. **Connect a client** (in another terminal):
   ```bash
   cd VPNClient
   sudo dotnet run -- --server 127.0.0.1
   ```

## Security Considerations

- Uses AES-256-GCM encryption for data protection
- ECDH key exchange for secure key agreement
- Digital signatures for packet authentication
- Automatic session cleanup and timeout handling

## Development

### Building from Source

```bash
# Restore packages
dotnet restore

# Build all projects
dotnet build

# Run tests (when available)
dotnet test
```

## Troubleshooting

### Common Issues

1. **Permission Denied**: Run with administrator/root privileges
2. **TAP Interface Not Found**: Install TAP-Windows driver (Windows)
3. **Network Unreachable**: Check firewall settings and routing
4. **Connection Timeouts**: Verify server is running and port is open

## License

This project is licensed under the MIT License.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request# vpn-project
# vp
