@echo off
setlocal

set "PROJECT=%~dp0src\ASimpleMinecraftServer\ASimpleMinecraftServer.csproj"

if not exist "%PROJECT%" (
    echo Project file not found:
    echo %PROJECT%
    pause
    exit /b 1
)

echo Restoring .NET 10 project...
dotnet restore "%PROJECT%"
if errorlevel 1 goto :fail

echo Building...
dotnet build "%PROJECT%" -c Release --no-restore
if errorlevel 1 goto :fail

echo Running...
dotnet run --project "%PROJECT%" -c Release --no-build
if errorlevel 1 goto :runfail

exit /b 0

:runfail
echo.
echo The application exited with an error.
pause
exit /b 1

:fail
echo.
echo Build failed. Review the compiler output above.
pause
exit /b 1
