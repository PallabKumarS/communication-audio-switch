@echo off
setlocal enabledelayedexpansion
title Building Communication Switch...

echo ========================================================
echo   Communication Switch - Build Script
echo ========================================================
echo.

set CSC=
if exist "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
    set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
) else if exist "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
    set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if "%CSC%"=="" (
    echo [ERROR] Microsoft .NET Framework 4.0/4.8 C# compiler (csc.exe) was not found.
    echo Please ensure .NET Framework is installed.
    pause
    exit /b 1
)

echo Using C# Compiler: %CSC%
echo.

echo Compiling CommunicationSwitch.exe (Windowless GUI & System Tray)...
"%CSC%" /target:winexe /optimize+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /out:"CommunicationSwitch.exe" "CommunicationSwitch.cs"

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Build failed! Check the error messages above.
    pause
    exit /b %errorlevel%
)

echo.
echo Compiling CommunicationSwitch-CLI.exe (Console Companion)...
"%CSC%" /target:exe /optimize+ /platform:anycpu /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /out:"CommunicationSwitch-CLI.exe" "CommunicationSwitch.cs"

echo.
echo ========================================================
echo   [SUCCESS] Build Completed Successfully!
echo   Output: CommunicationSwitch.exe
echo ========================================================
echo.
pause
