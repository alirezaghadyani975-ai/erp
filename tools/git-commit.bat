@echo off
rem commit همه تغییرات با پیام tools\commit-message.txt؛ خروجی در tools\git-log.txt
chcp 65001 >nul
cd /d "%~dp0.."
set "LOG=%cd%\tools\git-log.txt"
set "GIT=git"
where git >nul 2>&1 || set "GIT=D:\Git\cmd\git.exe"

echo ===== %date% %time% > "%LOG%"
"%GIT%" add -A >> "%LOG%" 2>&1
echo ----- files to commit >> "%LOG%"
"%GIT%" status --short >> "%LOG%" 2>&1
"%GIT%" commit -q -F tools\commit-message.txt >> "%LOG%" 2>&1
echo ----- log >> "%LOG%"
"%GIT%" log --oneline -5 >> "%LOG%" 2>&1
echo ----- remote >> "%LOG%"
"%GIT%" remote -v >> "%LOG%" 2>&1
echo ===== done >> "%LOG%"
