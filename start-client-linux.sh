#!/bin/bash

# Simple VPN Client startup script for Linux
# For development and testing purposes

set -e

echo "🐧 Starting VPN Client on Linux"
echo "==============================="

# Default values
SERVER="127.0.0.1"
PORT="1194"

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -s|--server)
            SERVER="$2"
            shift 2
            ;;
        -p|--port)
            PORT="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -s, --server <address>  VPN server address (default: 127.0.0.1)"
            echo "  -p, --port <port>       VPN server port (default: 1194)"
            echo "  -h, --help              Show this help message"
            echo ""
            echo "Examples:"
            echo "  $0                                    # Connect to localhost"
            echo "  $0 --server 192.168.1.100           # Connect to specific server"
            echo "  $0 --server vpn.example.com --port 443"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Check if running as root
if [[ $EUID -ne 0 ]]; then
    echo "⚠️  Warning: Not running as root"
    echo "   VPN client may not be able to create TUN interfaces"
    echo "   Use 'sudo ./start-client-linux.sh' for full functionality"
    echo ""
fi

# Check TUN/TAP support
if [ -c /dev/net/tun ]; then
    echo "✅ TUN/TAP device is available"
else
    echo "⚠️  TUN/TAP device not found"
    if [[ $EUID -eq 0 ]]; then
        echo "   Attempting to load TUN module..."
        modprobe tun 2>/dev/null || echo "   Could not load TUN module"
    else
        echo "   Run as root to load TUN module"
    fi
fi

# Build the application
echo "🔨 Building VPN Client..."
cd "$(dirname "$0")"
dotnet build VPNClient --configuration Release --verbosity quiet

if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

echo "✅ Build successful"

# Start the client
echo "🚀 Starting VPN Client..."
echo "   Connecting to: $SERVER:$PORT"
echo "   Press any key to disconnect after connection"
echo ""

cd VPNClient
exec dotnet run --configuration Release -- --server "$SERVER" --port "$PORT"
EOF