#!/bin/bash

# Simple VPN Server startup script for Linux
# For development and testing purposes

set -e

echo "🐧 Starting VPN Server on Linux"
echo "==============================="

# Check if running as root
if [[ $EUID -ne 0 ]]; then
    echo "⚠️  Warning: Not running as root"
    echo "   VPN server may not be able to create TUN interfaces"
    echo "   Use 'sudo ./start-server-linux.sh' for full functionality"
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

# Check if port 1194 is available
if command -v ss &> /dev/null; then
    if ss -tuln | grep -q ":1194 "; then
        echo "⚠️  Warning: Port 1194 is already in use"
        echo "   Another VPN server might be running"
    fi
elif command -v netstat &> /dev/null; then
    if netstat -tuln | grep -q ":1194 "; then
        echo "⚠️  Warning: Port 1194 is already in use"
        echo "   Another VPN server might be running"
    fi
fi

# Build the application
echo "🔨 Building VPN Server..."
cd "$(dirname "$0")"
dotnet build VPNServer --configuration Release --verbosity quiet

if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

echo "✅ Build successful"

# Start the server
echo "🚀 Starting VPN Server..."
echo "   Server will listen on port 1194 (UDP)"
echo "   Press Ctrl+C to stop"
echo ""

cd VPNServer
exec dotnet run --configuration Release
EOF