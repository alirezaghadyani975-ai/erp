@echo off
chcp 65001 >nul
cd /d "%~dp0.."
set "LOG=%cd%\tools\git-log.txt"

set "GIT=git"
where git >nul 2>&1 || set "GIT=D:\git\cmd\git.exe"

echo ===== %date% %time% > "%LOG%"
"%GIT%" --version >> "%LOG%" 2>&1

rem هویت Git فقط اگر قبلاً تنظیم نشده باشد
set "GNAME="
set "GMAIL="
for /f "delims=" %%a in ('"%GIT%" config --global user.name 2^>nul') do set "GNAME=%%a"
for /f "delims=" %%a in ('"%GIT%" config --global user.email 2^>nul') do set "GMAIL=%%a"
if not defined GNAME "%GIT%" config --global user.name "Alireza Ghadyanieh"
if not defined GMAIL "%GIT%" config --global user.email "kkalasghar@gmail.com"
"%GIT%" config --global core.quotepath off
echo identity: >> "%LOG%"
"%GIT%" config --global user.name >> "%LOG%" 2>&1
"%GIT%" config --global user.email >> "%LOG%" 2>&1

if not exist .git "%GIT%" init -b main >> "%LOG%" 2>&1
"%GIT%" add -A >> "%LOG%" 2>&1
echo ----- files to commit >> "%LOG%"
"%GIT%" status --short >> "%LOG%" 2>&1
"%GIT%" commit -F tools\commit-message.txt >> "%LOG%" 2>&1
echo ----- log >> "%LOG%"
"%GIT%" log --oneline -3 >> "%LOG%" 2>&1
echo ===== done >> "%LOG%"
