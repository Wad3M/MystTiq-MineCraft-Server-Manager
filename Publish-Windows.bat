@echo off
setlocal
cd /d "%~dp0src\ASimpleMinecraftServer"
echo Publishing Windows x64...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
if errorlevel 1 goto :fail
echo.
echo Published to bin\Release\net10.0-windows\win-x64\publish
pause
exit /b 0
:fail
echo.
echo Publish failed. Review the compiler output above.
pause
exit /b 1
