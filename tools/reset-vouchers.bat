@echo off
chcp 65001 >nul
rem  Backup ErpCompany_Default to D:\Backup\ERP, then delete ALL vouchers (old coding)
rem  so that migration 08 (new standard coding) can run. Persons, banks, cost centers stay.
cd /d "%~dp0.."
set "LOG=%cd%\tools\reset-log.txt"
set "DB=ErpCompany_Default"
set "BK=D:\Backup\ERP"

taskkill /f /im Erp.Api.exe >nul 2>&1
echo ===== %date% %time% > "%LOG%"

if not exist "%BK%" mkdir "%BK%"
icacls "%BK%" /grant "NT Service\MSSQL$ERP":(OI)(CI)M >> "%LOG%" 2>&1

echo ----- [1] backup >> "%LOG%"
sqlcmd -S .\ERP -E -C -I -b -Q "BACKUP DATABASE [%DB%] TO DISK = N'%BK%\%DB%_before08.bak' WITH COPY_ONLY, INIT, CHECKSUM" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo BACKUP FAILED - nothing was deleted. >> "%LOG%"
  echo BACKUP FAILED - nothing was deleted. See tools\reset-log.txt
  pause
  exit /b 1
)
dir "%BK%\%DB%_before08.bak" | findstr /i ".bak" >> "%LOG%"

echo ----- [2] before >> "%LOG%"
sqlcmd -S .\ERP -E -C -I -b -d %DB% -h -1 -W -Q "SET NOCOUNT ON; SELECT 'vouchers: ' + CAST(COUNT(*) AS varchar) FROM fin.Vouchers; SELECT 'lines: ' + CAST(COUNT(*) AS varchar) FROM fin.VoucherLines; SELECT 'posting template lines: ' + CAST(COUNT(*) AS varchar) FROM fin.PostingTemplateLines;" >> "%LOG%" 2>&1

echo ----- [3] delete vouchers >> "%LOG%"
sqlcmd -S .\ERP -E -C -I -b -d %DB% -h -1 -W -Q "SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRAN; DISABLE TRIGGER fin.trg_VoucherLines_Validate ON fin.VoucherLines; DISABLE TRIGGER fin.trg_Vouchers_Protect ON fin.Vouchers; DELETE fin.VoucherLines; DELETE fin.Vouchers; ENABLE TRIGGER fin.trg_VoucherLines_Validate ON fin.VoucherLines; ENABLE TRIGGER fin.trg_Vouchers_Protect ON fin.Vouchers; COMMIT; SELECT 'vouchers after: ' + CAST(COUNT(*) AS varchar) FROM fin.Vouchers;" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo DELETE FAILED - transaction rolled back. >> "%LOG%"
  echo DELETE FAILED. See tools\reset-log.txt
  pause
  exit /b 1
)
echo ===== done, starting run-test nobuild >> "%LOG%"
call "%~dp0run-test.bat" nobuild
