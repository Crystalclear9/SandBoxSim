@echo off
where pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 is required. Run tools/godot.ps1 from PowerShell 7.
    pause
    exit /b 1
)
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\godot.ps1" -Mode run
if errorlevel 1 pause
