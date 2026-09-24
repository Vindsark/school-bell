@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (echo csc.exe not found & exit /b 1)
if not exist dist mkdir dist
set OPTS=/nologo /target:winexe /optimize+ /codepage:65001 /win32manifest:src\app.manifest /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
if not exist src\bell.ico (
  "%CSC%" %OPTS% /out:dist\SchoolBell.exe src\*.cs || exit /b 1
  start "" /wait dist\SchoolBell.exe /makeicon src\bell.ico
)
"%CSC%" %OPTS% /win32icon:src\bell.ico /out:dist\SchoolBell.exe src\*.cs || exit /b 1
echo OK: dist\SchoolBell.exe
