@echo off
setlocal
cd /d "%~dp0"
if not exist "backend\.venv\Scripts\python.exe" (
  echo [FAIL] Python environment not found. Run RUN_LOCAL_WINDOWS.bat once, close it, then retry.
  pause
  exit /b 1
)
echo Verifying one real Google ADK function call...
set "PRVR_PROBE_LOG=%TEMP%\protocolrun_gemini_probe_%RANDOM%_%RANDOM%.log"
"backend\.venv\Scripts\python.exe" "deploy\verify_gemini_tool.py" > "%PRVR_PROBE_LOG%" 2>&1
set "PRVR_PROBE_EXIT=%ERRORLEVEL%"
type "%PRVR_PROBE_LOG%"
findstr /C:"Failed to detach context" /C:"GeneratorExit" /C:"Root node diagnosis_recovery was cancelled" "%PRVR_PROBE_LOG%" >nul
if not errorlevel 1 (
  set "PRVR_PROBE_EXIT=1"
  echo [FAIL] Google ADK tool call returned, but its event stream did not close cleanly.
)
del /q "%PRVR_PROBE_LOG%" >nul 2>&1
if not "%PRVR_PROBE_EXIT%"=="0" (
  echo.
  echo Do not start another Quest session yet. Copy only the FAIL line and error type.
) else (
  echo.
  echo Gemini Tool Calling is ready. You may start RUN_LOCAL_WINDOWS.bat and create a new session.
)
pause
exit /b %PRVR_PROBE_EXIT%
