@echo off
:: ============================================================
::   SmartCleaner — Build & Development Script
::   Usage: build.bat [0-7] or run without args for menu
:: ============================================================

setlocal enabledelayedexpansion
chcp 65001 >nul 2>&1

set "PROJECT=SmartCleaner.App"
set "SOLUTION_DIR=%~dp0"
set "SOLUTION=%SOLUTION_DIR%SmartCleaner.sln"
set "PUBLISH_DIR=%SOLUTION_DIR%publish"
set "DOTNET=dotnet"

:: ── ANSI-цвета: ESC-байт вычисляется во время выполнения ──
:: (литеральный 0x1B в .bat-файле легко теряется редакторами и git,
::  из-за чего в меню выводился мусор вида «[92m»)
set "ESC="
for /f %%E in ('forfiles /p "%~dp0." /m "%~nx0" /c "cmd /c echo 0x1B" 2^>nul') do set "ESC=%%E"
if defined ESC (
    set "GREEN=!ESC![92m"
    set "YELLOW=!ESC![93m"
    set "RED=!ESC![91m"
    set "CYAN=!ESC![96m"
    set "RESET=!ESC![0m"
) else (
    set "GREEN="
    set "YELLOW="
    set "RED="
    set "CYAN="
    set "RESET="
)

:: If argument passed, jump directly
if not "%~1"=="" (
    set "CHOICE=%~1"
    goto :execute
)

:menu
cls
echo.
echo  %CYAN%╔══════════════════════════════════════════════╗%RESET%
echo  %CYAN%║%RESET%   %GREEN%SmartCleaner — Build Script%RESET%              %CYAN%║%RESET%
echo  %CYAN%╠══════════════════════════════════════════════╣%RESET%
echo  %CYAN%║%RESET%                                              %CYAN%║%RESET%
echo  %CYAN%║%RESET%   %YELLOW%1%RESET% - Build (Debug)                        %CYAN%║%RESET%
echo  %CYAN%║%RESET%   %YELLOW%2%RESET% - Build (Release)                      %CYAN%║%RESET%
echo  %CYAN%║%RESET%   %YELLOW%3%RESET% - Run Tests                           %CYAN%║%RESET%
echo  %CYAN%║%RESET%   %YELLOW%4%RESET% - Publish Portable (single exe)        %CYAN%║%RESET%
echo  %CYAN%║%RESET%   %YELLOW%5%RESET% - Run (Debug)                          %CYAN%║%RESET%
echo  %CYAN%║%RESET%   %YELLOW%6%RESET% - Clean (bin/obj/publish/release)      %CYAN%║%RESET%
echo  %CYAN%║%RESET%   %YELLOW%7%RESET% - Restore NuGet packages               %CYAN%║%RESET%
echo  %CYAN%║%RESET%   %YELLOW%0%RESET% - Exit                                 %CYAN%║%RESET%
echo  %CYAN%║%RESET%                                              %CYAN%║%RESET%
echo  %CYAN%╚══════════════════════════════════════════════╝%RESET%
echo.

set /p "CHOICE=  Select option [0-7]: "

:execute

if "%CHOICE%"=="1" goto :build_debug
if "%CHOICE%"=="2" goto :build_release
if "%CHOICE%"=="3" goto :run_tests
if "%CHOICE%"=="4" goto :publish_portable
if "%CHOICE%"=="5" goto :run_debug
if "%CHOICE%"=="6" goto :clean
if "%CHOICE%"=="7" goto :restore
if "%CHOICE%"=="0" goto :eof

echo  %RED%Invalid option: %CHOICE%%RESET%
timeout /t 2 >nul
goto :menu

:: ════════════════════════════════════════════
:: 1. BUILD DEBUG
:: ════════════════════════════════════════════
:build_debug
echo.
echo  %CYAN%[1/1]%RESET% Building SmartCleaner.sln (Debug)...
echo  ────────────────────────────────
%DOTNET% build "%SOLUTION%" -c Debug
if errorlevel 1 (
    echo.
    echo  %RED%✗ Build FAILED%RESET%
    goto :done
)
echo.
echo  %GREEN%✓ Build successful (Debug)%RESET%
echo  Output: %PROJECT%\bin\Debug\net8.0-windows\
goto :done

:: ════════════════════════════════════════════
:: 2. BUILD RELEASE
:: ════════════════════════════════════════════
:build_release
echo.
echo  %CYAN%[1/1]%RESET% Building SmartCleaner.sln (Release)...
echo  ────────────────────────────────
%DOTNET% build "%SOLUTION%" -c Release
if errorlevel 1 (
    echo.
    echo  %RED%✗ Build FAILED%RESET%
    goto :done
)
echo.
echo  %GREEN%✓ Build successful (Release)%RESET%
echo  Output: %PROJECT%\bin\Release\net8.0-windows\
goto :done

:: ════════════════════════════════════════════
:: 3. RUN TESTS
:: ════════════════════════════════════════════
:run_tests
echo.
echo  %CYAN%[1/1]%RESET% Running unit tests (SmartCleaner.Core.Tests)...
echo  ────────────────────────────────
%DOTNET% test "%SOLUTION%" -v minimal
if errorlevel 1 (
    echo.
    echo  %RED%✗ Tests FAILED%RESET%
    goto :done
)
echo.
echo  %GREEN%✓ All tests passed%RESET%
goto :done

:: ════════════════════════════════════════════
:: 4. PUBLISH PORTABLE
:: ════════════════════════════════════════════
:publish_portable
echo.
echo  %CYAN%[1/2]%RESET% Publishing Portable version...
echo  Config stored next to exe
echo  ────────────────────────────────
%DOTNET% publish "%SOLUTION_DIR%%PROJECT%" -c Release -r win-x64 --self-contained ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%PUBLISH_DIR%\portable"
if errorlevel 1 (
    echo.
    echo  %RED%✗ Publish FAILED%RESET%
    goto :done
)

echo  %CYAN%[2/2]%RESET% Creating portable marker...
echo Portable mode> "%PUBLISH_DIR%\portable\portable.txt"

echo.
echo  %GREEN%✓ Portable build ready%RESET%
echo  Output: publish\portable\SmartCleaner.App.exe
goto :done

:: ════════════════════════════════════════════
:: 5. RUN DEBUG
:: ════════════════════════════════════════════
:run_debug
echo.
echo  %CYAN%[1/2]%RESET% Building SmartCleaner.sln (Debug)...
echo  ────────────────────────────────
%DOTNET% build "%SOLUTION%" -c Debug
if errorlevel 1 (
    echo.
    echo  %RED%✗ Build FAILED — cannot run%RESET%
    goto :done
)
echo.
echo  %CYAN%[2/2]%RESET% Launching SmartCleaner...
echo  ────────────────────────────────
%DOTNET% run --project "%SOLUTION_DIR%%PROJECT%" -c Debug --no-build
goto :done

:: ════════════════════════════════════════════
:: 6. CLEAN
:: ════════════════════════════════════════════
:clean
echo.
echo  %YELLOW%Cleaning build artifacts...%RESET%
echo  ────────────────────────────────

for %%P in (SmartCleaner.App SmartCleaner.Core SmartCleaner.Core.Tests) do (
    echo  Removing bin/obj in %%P...
    if exist "%SOLUTION_DIR%%%P\bin" rmdir /s /q "%SOLUTION_DIR%%%P\bin"
    if exist "%SOLUTION_DIR%%%P\obj" rmdir /s /q "%SOLUTION_DIR%%%P\obj"
)

echo  Removing publish/ and release/...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%SOLUTION_DIR%release" rmdir /s /q "%SOLUTION_DIR%release"

echo  Removing TestResults/...
if exist "%SOLUTION_DIR%TestResults" rmdir /s /q "%SOLUTION_DIR%TestResults"

echo  Removing build logs...
if exist "%SOLUTION_DIR%build_log.txt" del /q "%SOLUTION_DIR%build_log.txt"
if exist "%SOLUTION_DIR%build_output.txt" del /q "%SOLUTION_DIR%build_output.txt"

echo.
echo  %GREEN%✓ Clean complete%RESET%
goto :done

:: ════════════════════════════════════════════
:: 7. RESTORE
:: ════════════════════════════════════════════
:restore
echo.
echo  %CYAN%[1/1]%RESET% Restoring NuGet packages...
echo  ────────────────────────────────
%DOTNET% restore "%SOLUTION%"
if errorlevel 1 (
    echo.
    echo  %RED%✗ Restore FAILED%RESET%
    goto :done
)
echo.
echo  %GREEN%✓ Packages restored%RESET%
goto :done

:: ════════════════════════════════════════════
:done
echo.
echo  ════════════════════════════════════════
echo.

:: If launched with argument, don't loop back
if not "%~1"=="" goto :eof

pause
goto :menu
