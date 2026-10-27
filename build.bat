@echo off
rem Builds dist\RappelzPingTracker.exe with the .NET Framework 4 compiler that ships with Windows (no SDK needed).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist dist mkdir dist
"%CSC%" -nologo -codepage:65001 -optimize+ -target:winexe -platform:anycpu ^
  -out:dist\RappelzPingTracker.exe -win32manifest:src\app.manifest -win32icon:src\app.ico ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  src\*.cs
if errorlevel 1 exit /b 1
echo Done: dist\RappelzPingTracker.exe
