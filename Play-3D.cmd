@echo off
setlocal
if not defined SANDBOXSIM_ORIGINAL_APPDATA set "SANDBOXSIM_ORIGINAL_APPDATA=%APPDATA%"
set "APPDATA=%~dp0.tools\godot-state\config"
set "LOCALAPPDATA=%~dp0.tools\godot-state\cache"
if not exist "%APPDATA%" mkdir "%APPDATA%"
if not exist "%LOCALAPPDATA%" mkdir "%LOCALAPPDATA%"
if exist "%~dp0artifacts\game\windows\SandBoxSim.exe" (
    start "SandBoxSim" /D "%~dp0artifacts\game\windows" "%~dp0artifacts\game\windows\SandBoxSim.exe"
    exit /b 0
)
where pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 is required. Run tools/godot.ps1 from PowerShell 7.
    pause
    exit /b 1
)
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\godot.ps1" -Mode run
if errorlevel 1 pause
