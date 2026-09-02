@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul || (echo .NET 10 SDK is required.&pause&exit /b 1)
where py >nul 2>nul || where python >nul 2>nul || (echo Python 3.10+ is required for the GVAS engine.&pause&exit /b 1)
echo Building Retro Rewind Modhub...
dotnet publish RetroRewindModhub.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
set "OUT=bin\Release\net10.0-windows\win-x64\publish"
if exist "%~dp0RRModHubTextureInjector.exe" (
    if not exist "%OUT%\RetroRewindModHub_Data" mkdir "%OUT%\RetroRewindModHub_Data"
    copy /Y "%~dp0RRModHubTextureInjector.exe" "%OUT%\RetroRewindModHub_Data\RRModHubTextureInjector.exe" >nul
)
if errorlevel 1 (echo BUILD FAILED&pause&exit /b 1)
echo BUILD COMPLETE
echo bin\Release\net10.0-windows\win-x64\publish\RetroRewindModhub.exe
pause
