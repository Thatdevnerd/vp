@echo off
echo Building VPN Application...

echo.
echo Building VPNCore...
cd VPNCore
dotnet build -c Release
if %ERRORLEVEL% neq 0 goto :error
cd ..

echo.
echo Building VPNServer...
cd VPNServer
dotnet build -c Release
if %ERRORLEVEL% neq 0 goto :error
cd ..

echo.
echo Building VPNClient...
cd VPNClient
dotnet build -c Release
if %ERRORLEVEL% neq 0 goto :error
cd ..

echo.
echo Building VPNClient.GUI...
cd VPNClient.GUI
dotnet build -c Release
if %ERRORLEVEL% neq 0 goto :error
cd ..

echo.
echo Running tests...
cd Tests
dotnet test -c Release
if %ERRORLEVEL% neq 0 goto :error
cd ..

echo.
echo Build completed successfully!
goto :end

:error
echo Build failed!
exit /b 1

:end