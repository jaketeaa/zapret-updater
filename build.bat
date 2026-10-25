@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set ICON=
if exist "icon.ico" set ICON=/win32icon:icon.ico
if not exist "%CSC%" (
  echo Compiler csc.exe not found. Install .NET Framework 4.x
  pause
  exit /b 1
)
"%CSC%" /nologo /target:exe /codepage:65001 /out:zapret-updater.exe /win32manifest:app.manifest %ICON% /r:System.dll /r:System.Core.dll /r:System.ServiceProcess.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll Program.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED - send the errors above to Claude
  pause
  exit /b 1
)
echo.
echo OK: zapret-updater.exe is ready
pause
