@echo off
setlocal
cd /d "%~dp0"

set "PUBLISH=%~dp0release_build"
set "OUT=%~dp0RetroRewindModhub"

if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

dotnet publish "%~dp0RetroRewindModhub.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH%"
if errorlevel 1 (
    echo.
    echo Publish failed.
	pause
    exit /b 1
)

copy /y "%PUBLISH%\RetroRewindModhub.exe" "%OUT%\RetroRewindModhub.exe" >nul
if not exist "%OUT%\RetroRewindModHub_Data" mkdir "%OUT%\RetroRewindModHub_Data"
if exist "%~dp0Localization" (
    if not exist "%OUT%\RetroRewindModHub_Data\Localization" mkdir "%OUT%\RetroRewindModHub_Data\Localization"
    xcopy /e /i /y "%~dp0Localization\*.json" "%OUT%\RetroRewindModHub_Data\Localization\" >nul
)

mkdir "%OUT%\RetroRewindModHub_Data\Engine"
copy /y "%~dp0Engine\engine.py" "%OUT%\RetroRewindModHub_Data\Engine\engine.py" >nul
copy /y "%~dp0Engine\vanilla_store_style.json" "%OUT%\RetroRewindModHub_Data\Engine\vanilla_store_style.json" >nul
if exist "%~dp0RRModHubTextureInjector.exe" copy /y "%~dp0RRModHubTextureInjector.exe" "%OUT%\RetroRewindModHub_Data\RRModHubTextureInjector.exe" >nul
if errorlevel 1 (echo Could not copy vanilla_store_style.json.&pause&exit /b 1)

rmdir /s /q "%PUBLISH%"

echo.
echo Release created at:
echo %OUT%
echo.
echo Root contents:
echo   RetroRewindModhub.exe
echo   RetroRewindModHub_Data\Localization\
echo   RetroRewindModHub_Data\Engine\
