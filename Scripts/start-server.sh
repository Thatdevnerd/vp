#!/bin/bash

# VPN Server Startup Script
echo "Starting VPN Server..."
echo "Note: This requires administrator/root privileges"

cd "$(dirname "$0")/../VPNServer"

# Check if running as root/admin
if [ "$EUID" -ne 0 ]; then
    echo "Warning: VPN Server typically requires root privileges for network interface creation"
    echo "You may need to run: sudo ./start-server.sh"
fi

echo "Building VPN Server..."
dotnet build

echo "Starting VPN Server on port 1194..."
echo "Press Ctrl+C to stop the server"

dotnet run