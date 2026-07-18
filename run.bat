@echo off
chcp 65001 > nul
setlocal

pushd "%~dp0"

set "CONFIGURATION=%~1"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"

set "RUNTIME=win-x64"
set "EXE=%~dp0src\PlcScope.App\bin\%CONFIGURATION%\net10.0-windows\%RUNTIME%\publish\PlcScope.exe"

if not exist "%EXE%" (
  echo PLC Scope EXE was not found.
  echo Building first...
  echo.
  call "%~dp0build.bat" "%CONFIGURATION%"
  if errorlevel 1 goto :failed
)

echo Starting:
echo   %EXE%
echo.
start "" "%EXE%"

popd
endlocal
exit /b 0

:failed
echo.
echo Could not build PLC Scope.
echo.
pause
popd
endlocal
exit /b 1
