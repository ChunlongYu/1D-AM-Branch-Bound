@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "TL=%~1"
if "%TL%"=="" set "TL=3600"
REM ---- dominance-frontier memory cap (number of keys). Lower if you still run out of RAM. ----
set "CAP=25000000"
set "OUT=results_big.txt"

REM oracle.cpp changed -> force a fresh build
if exist oracle.exe del /q oracle.exe
echo Building oracle.exe ...
where g++ >nul 2>nul
if not errorlevel 1 ( g++ -std=c++17 -O2 -o oracle.exe oracle.cpp )
if not exist oracle.exe (
  set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
  if exist "!VSWHERE!" (
    for /f "usebackq tokens=*" %%i in (`"!VSWHERE!" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSPATH=%%i"
    if exist "!VSPATH!\VC\Auxiliary\Build\vcvars64.bat" (
      call "!VSPATH!\VC\Auxiliary\Build\vcvars64.bat" >nul
      cl /O2 /EHsc /std:c++17 oracle.cpp /Fe:oracle.exe
    )
  )
)
if not exist oracle.exe (
  echo Build failed. Open "x64 Native Tools Command Prompt", cd here, run:
  echo   cl /O2 /EHsc /std:c++17 oracle.cpp /Fe:oracle.exe
  pause & exit /b 1
)

echo Re-running big instances  TL=%TL%s  frontier-cap=%CAP% keys > "%OUT%"
for %%F in (19part 20part_3-S 20part_4-S) do (
  echo   running %%F ...
  >> "%OUT%" echo === %%F.txt ===
  oracle.exe "exp_instances\%%F.txt" %TL% 200000 50000 1 1 0 0.5 %CAP% >> "%OUT%"
)
echo.
echo DONE. See %OUT%
pause
