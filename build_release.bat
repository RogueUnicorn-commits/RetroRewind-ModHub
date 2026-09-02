@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul || (echo .NET 10 SDK is required.&pause&exit /b 1)
where python >nul 2>nul || (echo Python 3.10+ is required.&pause&exit /b 1)
echo Building bundled GVAS engine...
python -m PyInstaller --version >nul 2>nul
if errorlevel 1 (echo PyInstaller is required. Install with: python -m pip install pyinstaller&pause&exit /b 1)
python -m PyInstaller --onefile --clean --name engine Engine\engine.py
if errorlevel 1 (echo Engine build failed.&pause&exit /b 1)
echo Building Windows application...
dotnet publish RetroRewindModhub.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (echo WPF build failed.&pause&exit /b 1)
set "OUT=bin\Release\net10.0-windows\win-x64\publish"
if not exist "%OUT%\RetroRewindModHub_Data\Engine" mkdir "%OUT%\RetroRewindModHub_Data\Engine"
copy /Y dist\engine.exe "%OUT%\RetroRewindModHub_Data\Engine\engine.exe" >nul
copy /Y "%~dp0Engine\vanilla_store_style.json" "%OUT%\RetroRewindModHub_Data\Engine\vanilla_store_style.json" >nul
if exist "%~dp0RRModHubTextureInjector.exe" copy /Y "%~dp0RRModHubTextureInjector.exe" "%OUT%\RetroRewindModHub_Data\RRModHubTextureInjector.exe" >nul
if errorlevel 1 (echo Could not copy vanilla_store_style.json.&pause&exit /b 1)
if errorlevel 1 (echo Could not copy engine.exe.&pause&exit /b 1)
if exist "%~dp0Localization" (
    if not exist "%OUT%\RetroRewindModHub_Data\Localization" mkdir "%OUT%\RetroRewindModHub_Data\Localization"
    xcopy /e /i /y "%~dp0Localization\*.json" "%OUT%\RetroRewindModHub_Data\Localization\" >nul
)
echo.
echo RELEASE READY:
echo %OUT%\RetroRewindModhub.exe
echo.
echo Keep the RetroRewindModHub_Data folder beside the EXE. The app will use the bundled engine.exe and will not need Python at runtime.
pause
