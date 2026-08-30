@echo off
setlocal
cd /d "%~dp0backend"

where py >nul 2>nul
if errorlevel 1 (
  echo Python 3.12 is required. Install it from python.org, then run this file again.
  pause
  exit /b 1
)

if not exist ".venv\Scripts\python.exe" (
  echo Creating the local ProtocolRun-VR environment...
  py -3.12 -m venv .venv
  if errorlevel 1 goto :failed
)

echo Installing verified server dependencies...
".venv\Scripts\python.exe" -m pip install -r requirements.txt
if errorlevel 1 goto :failed

if not exist ".env.local" (
  echo A private server configuration will be created on first start.
)

echo Starting ProtocolRun-VR at http://127.0.0.1:8080/console/
".venv\Scripts\python.exe" run_local.py
exit /b %errorlevel%

:failed
echo.
echo Setup failed. Copy the complete error shown above before closing this window.
pause
exit /b 1
