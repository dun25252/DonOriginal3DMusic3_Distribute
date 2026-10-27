@echo off
setlocal
for %%F in ("%~dp0*GUI.ps1") do (
  if exist "%%~fF" (
    start "" powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%%~fF"
    exit /b 0
  )
)
echo Ver1.0 GUI launcher script was not found next to this file.
pause
exit /b 1
