@echo off
chcp 65001 > nul
setlocal

pushd "%~dp0"

set "CONFIGURATION=%~1"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"

set "RUNTIME=win-x64"
set "LOG_FILE=%~dp0build.log"
set "PUBLISH_DIR=%~dp0src\PlcScope.App\bin\%CONFIGURATION%\net10.0-windows\%RUNTIME%\publish"

echo Publishing PLC Scope single-file EXE (%CONFIGURATION%, %RUNTIME%)...
echo.
echo Log:
echo   %LOG_FILE%
echo.

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet restore ".\src\PlcScope.App\PlcScope.App.csproj" -r "%RUNTIME%" > "%LOG_FILE%" 2>&1
if errorlevel 1 goto :failed

dotnet publish ".\src\PlcScope.App\PlcScope.App.csproj" ^
  -c "%CONFIGURATION%" ^
  -r "%RUNTIME%" ^
  --self-contained true ^
  --no-restore ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -p:PublishDocumentationFile=false >> "%LOG_FILE%" 2>&1
if errorlevel 1 goto :failed

echo.
echo Publish completed.
echo Output:
echo   %PUBLISH_DIR%\PlcScope.exe
echo.
echo Full log:
echo   %LOG_FILE%

popd
endlocal
exit /b 0

:failed
echo.
echo Build failed.
echo.
echo Last log lines:
echo ------------------------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Test-Path '%LOG_FILE%') { Get-Content -Path '%LOG_FILE%' -Tail 80 }"
echo ------------------------------------------------------------
echo.
echo Full log:
echo   %LOG_FILE%
echo.
pause
popd
endlocal
exit /b 1
