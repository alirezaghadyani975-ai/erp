@echo off
chcp 65001 >nul
rem اجرای برنامه بدون build (بعد از run-test.bat): رابط کاربری http://localhost:5227/ و API در /scalar
cd /d "%~dp0.."
taskkill /f /im Erp.Api.exe >nul 2>&1
start "Erp API" cmd /k "dotnet run --no-build --project src\Host\Erp.Api --launch-profile http"
set UP=0
for /l %%i in (1,1,90) do (
  curl -s -o nul http://localhost:5227/api/base/units && (set "UP=1" & goto :up)
  timeout /t 2 /nobreak >nul
)
:up
if "%UP%"=="1" (start "" http://localhost:5227/) else (echo برنامه بالا نیامد؛ پنجره «Erp API» را ببینید. & pause)
