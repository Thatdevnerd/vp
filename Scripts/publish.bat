@echo off
echo Publishing VPN Application...

set OUTPUT_DIR=..\Published

if exist %OUTPUT_DIR% rmdir /s /q %OUTPUT_DIR%
mkdir %OUTPUT_DIR%

echo.
echo Publishing VPNServer...
cd VPNServer
dotnet publish -c Release -o %OUTPUT_DIR%\VPNServer --self-contained false
if %ERRORLEVEL% neq 0 goto :error
cd ..

echo.
echo Publishing VPNClient...
cd VPNClient
dotnet publish -c Release -o %OUTPUT_DIR%\VPNClient --self-contained false
if %ERRORLEVEL% neq 0 goto :error
cd ..

echo.
echo Publishing VPNClient.GUI...
cd VPNClient.GUI
dotnet publish -c Release -o %OUTPUT_DIR%\VPNClient.GUI --self-contained false
if %ERRORLEVEL% neq 0 goto :error
cd ..

echo.
echo Copying configuration files...
copy VPNServer\appsettings.json %OUTPUT_DIR%\VPNServer\
copy VPNClient\appsettings.json %OUTPUT_DIR%\VPNClient\
copy README.md %OUTPUT_DIR%\

echo.
echo Creating start scripts...
echo @echo off > %OUTPUT_DIR%\start-server.bat
echo echo Starting VPN Server... >> %OUTPUT_DIR%\start-server.bat
echo cd VPNServer >> %OUTPUT_DIR%\start-server.bat
echo VPNServer.exe >> %OUTPUT_DIR%\start-server.bat

echo @echo off > %OUTPUT_DIR%\start-client.bat
echo echo Starting VPN Client... >> %OUTPUT_DIR%\start-client.bat
echo cd VPNClient >> %OUTPUT_DIR%\start-client.bat
echo VPNClient.exe >> %OUTPUT_DIR%\start-client.bat

echo @echo off > %OUTPUT_DIR%\start-gui.bat
echo echo Starting VPN GUI Client... >> %OUTPUT_DIR%\start-gui.bat
echo cd VPNClient.GUI >> %OUTPUT_DIR%\start-gui.bat
echo VPNClient.GUI.exe >> %OUTPUT_DIR%\start-gui.bat

echo.
echo Publish completed successfully!
echo Output directory: %OUTPUT_DIR%
goto :end

:error
echo Publish failed!
exit /b 1

:end