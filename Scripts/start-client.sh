#!/bin/bash

# VPN Client Startup Script
echo "Starting VPN Client..."
echo "Note: This requires administrator/root privileges"

cd "$(dirname "$0")/../VPNClient"

# Check if running as root/admin
if [ "$EUID" -ne 0 ]; then
    echo "Warning: VPN Client typically requires root privileges for network interface creation"
    echo "You may need to run: sudo ./start-client.sh"
fi

# Default server settings
SERVER_ADDRESS=${1:-"127.0.0.1"}
SERVER_PORT=${2:-"1194"}

echo "Building VPN Client..."
dotnet build

echo "Connecting to VPN Server at $SERVER_ADDRESS:$SERVER_PORT..."
echo "Press any key to disconnect"

dotnet run -- --server "$SERVER_ADDRESS" --port "$SERVER_PORT"