@echo off
cd /d "%~dp0"
if exist "bin\CodexVPet.exe" (
    start "" "%~dp0bin\CodexVPet.exe"
    exit /b 0
)
if not exist "bin\CodexDragonPet.exe" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if not exist "bin\CodexDragonPet.exe" exit /b 1
start "" "%~dp0bin\CodexDragonPet.exe"
