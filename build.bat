@echo off
setlocal

set PROJECT=%~dp0MicroSIPCompanion.csproj
set OUTDIR=%~dp0bin\Release\net48

echo [1/3] Cleaning...
if exist "%~dp0bin"  rd /s /q "%~dp0bin"
if exist "%~dp0obj"  rd /s /q "%~dp0obj"

echo [2/3] Building...
dotnet build "%PROJECT%" -c Release --nologo
if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    pause
    exit /b 1
)

echo [3/3] Output: %OUTDIR%
echo.
echo BUILD SUCCEEDED.
echo Run with: %OUTDIR%\MicroSIPCompanion.exe
echo.
pause
