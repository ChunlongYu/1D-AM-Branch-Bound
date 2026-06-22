@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

REM ===== time limit per instance (s). Default 3600 (fair vs MILP). Quick: run_exp.bat 600 =====
set "TL=%~1"
if "%TL%"=="" set "TL=3600"
set "OUT=results_new_oracle.txt"
set "CAP=25000000"

if exist "oracle.exe" goto run

echo Building oracle.exe ...
REM --- 1) try g++ if present ---
where g++ >nul 2>nul
if not errorlevel 1 (
  g++ -std=c++17 -O2 -o oracle.exe oracle.cpp
  if exist "oracle.exe" goto run
)

REM --- 2) try Visual Studio (cl) auto-located by vswhere ---
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "!VSWHERE!" (
  for /f "usebackq tokens=*" %%i in (`"!VSWHERE!" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSPATH=%%i"
  if exist "!VSPATH!\VC\Auxiliary\Build\vcvars64.bat" (
    echo Using Visual Studio at: !VSPATH!
    call "!VSPATH!\VC\Auxiliary\Build\vcvars64.bat" >nul
    cl /O2 /EHsc /std:c++17 oracle.cpp /Fe:oracle.exe
    if exist "oracle.exe" goto run
  )
)

echo.
echo [!] Auto-build failed (no g++, and VS auto-detect did not work).
echo     Easiest manual fix:
echo       1) Start menu -^> open "x64 Native Tools Command Prompt for VS"
echo       2) cd /d "%~dp0"
echo       3) cl /O2 /EHsc /std:c++17 oracle.cpp /Fe:oracle.exe
echo     then double-click run_exp.bat again.
echo.
pause
exit /b 1

:run
echo ============================================================
echo  new_oracle batch run   ^| time limit = %TL%s per instance
echo  results -^> %OUT%
echo ============================================================
echo new_oracle run  TL=%TL%s  %DATE% %TIME%> "%OUT%"
for %%F in (exp_instances\*.txt) do (
  echo   running %%~nxF ...
  >> "%OUT%" echo === %%~nxF ===
  oracle.exe "%%F" %TL% 200000 50000 1 1 0 0.5 %CAP% >> "%OUT%"
)
echo.
echo DONE. Open %OUT% for results (obj / proven-or-TIMEOUT / lb / gap / time).
pause
