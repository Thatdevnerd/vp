#!/bin/bash

echo "Building VPN Application..."

echo ""
echo "Building VPNCore..."
cd VPNCore
dotnet build -c Release
if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi
cd ..

echo ""
echo "Building VPNServer..."
cd VPNServer
dotnet build -c Release
if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi
cd ..

echo ""
echo "Building VPNClient..."
cd VPNClient
dotnet build -c Release
if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi
cd ..

echo ""
echo "Building VPNClient.GUI..."
cd VPNClient.GUI
dotnet build -c Release
if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi
cd ..

echo ""
echo "Running tests..."
cd Tests
dotnet test -c Release
if [ $? -ne 0 ]; then
    echo "Tests failed!"
    exit 1
fi
cd ..

echo ""
echo "Build completed successfully!"