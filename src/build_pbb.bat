@echo off
REM Build pbb.exe (parallel branch-and-bound) on Windows.
REM Double-click, or run from a terminal. Produces src\pbb.exe.
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "SRCS=main.cpp ParallelBranchBound.cpp BranchBound.cpp InstanceData.cpp"

echo === Building pbb.exe from: %SRCS% ===

REM --- 1) try g++ if present ---
where g++ >nul 2>nul
if !errorlevel! == 0 (
  echo [g++] compiling...
  g++ -std=c++17 -O2 -o pbb.exe %SRCS%
  if exist pbb.exe (echo [OK] pbb.exe built with g++. & goto :done)
)

REM --- 2) try Visual Studio (cl) auto-located by vswhere ---
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "!VSWHERE!" (
  for /f "usebackq tokens=*" %%i in (`"!VSWHERE!" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSPATH=%%i"
  if exist "!VSPATH!\VC\Auxiliary\Build\vcvars64.bat" (
    echo [MSVC] using !VSPATH!
    call "!VSPATH!\VC\Auxiliary\Build\vcvars64.bat" >nul
    cl /utf-8 /O2 /EHsc /std:c++17 %SRCS% /Fe:pbb.exe /nologo
    if exist pbb.exe (echo [OK] pbb.exe built with MSVC. & del /q *.obj >nul 2>nul & goto :done)
  )
)

echo.
echo [!] Auto-build failed (no g++ on PATH, and Visual Studio auto-detect did not work).
echo     Build manually in a "Developer Command Prompt for VS":
echo       cl /utf-8 /O2 /EHsc /std:c++17 %SRCS% /Fe:pbb.exe
echo     or install MinGW g++ and run:
echo       g++ -std=c++17 -O2 -o pbb.exe %SRCS%
goto :end

:done
echo.
echo Run the experiments:  cd ..\experiments\yu2022 ^&^& python run_yu2022.py
:end
pause
