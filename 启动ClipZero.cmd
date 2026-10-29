@echo off
setlocal DisableDelayedExpansion
if not exist "%~dp0src\ClipZero\bin\Release\net10.0-windows\ClipZero.exe" (
  echo Development build is missing. Build ClipZero.slnx in Release first.
  pause
  exit /b 1
)
if exist "%~dp0.tools\dotnet\dotnet.exe" (
  set "DOTNET_ROOT=%~dp0.tools\dotnet"
  set "DOTNET_ROOT_X64=%~dp0.tools\dotnet"
)
start "" /D "%~dp0" "%~dp0src\ClipZero\bin\Release\net10.0-windows\ClipZero.exe" --show
exit /b 0
