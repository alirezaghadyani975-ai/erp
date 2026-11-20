@echo off
rem باز کردن فایل‌های به‌روزرسانی روی پوشه پروژه
cd /d D:\Projects\ERp
powershell -NoProfile -Command "Expand-Archive -Force -Path 'D:\Projects\ERp\tools\erp12.zip' -DestinationPath 'D:\Projects\ERp'" > tools\apply-log.txt 2>&1
echo done >> tools\apply-log.txt
