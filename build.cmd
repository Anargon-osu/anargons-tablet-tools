@echo off
rem Builds AnargonsTabletTools.exe, next to this file, with the C# compiler that ships with Windows (.NET Framework 4).
rem Requires the Windows .NET Framework compiler and the separate gd32emu source.
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
rem GD32EMU_DIR from .env (see .env.example) unless it is set in the environment
if not defined GD32EMU_DIR if exist ".env" for /f "usebackq eol=# tokens=1,* delims==" %%A in (".env") do if /i "%%A"=="GD32EMU_DIR" if not "%%B"=="" set "GD32EMU_DIR=%%~B"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo The C# compiler was not found at "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
  exit /b 1
)
set "EMU=%GD32EMU_DIR%"
if "%EMU%"=="" if exist "gd32emu\gd32emu.cs" set "EMU=gd32emu"
if "%EMU%"=="" if exist "..\gd32emu\gd32emu.cs" set "EMU=..\gd32emu"
if not exist "%EMU%\gd32emu.cs" (
  echo The gd32emu source was not found. Keep the separate projects side by side,
  echo or set GD32EMU_DIR to its source folder. See DOCS\EMULATOR.md.
  exit /b 1
)
echo Using the emulator from "%EMU%"
"%CSC%" /nologo /langversion:5 /optimize+ /out:AnargonsTabletTools.exe "%EMU%\gd32emu.cs" atf_*.cs
if errorlevel 1 exit /b 1
.\AnargonsTabletTools.exe twin --selftest
exit /b %errorlevel%
