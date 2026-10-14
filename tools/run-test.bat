@echo off
chcp 65001 >nul
cd /d "%~dp0.."
set "LOG=%cd%\tools\test-log.txt"
set "APILOG=%cd%\tools\api-log.txt"
set "DB=ErpCompany_Default"
set "API=http://localhost:5227/api"

taskkill /f /im Erp.Api.exe >nul 2>&1

echo ===== %date% %time% > "%LOG%"
echo ----- [1] .NET SDKs >> "%LOG%"
dotnet --list-sdks >> "%LOG%" 2>&1

echo ----- [2] SQL Server (.\ERP) >> "%LOG%"
sqlcmd -S .\ERP -E -C -I -h -1 -W -Q "SET NOCOUNT ON; SELECT @@SERVERNAME + ' | ' + CAST(SERVERPROPERTY('ProductVersion') AS varchar(20)) + ' | ' + CAST(SERVERPROPERTY('Edition') AS varchar(60))" >> "%LOG%" 2>&1

echo ----- [3] build >> "%LOG%"
dotnet build -nologo -v q >> "%LOG%" 2>&1

echo ----- [4] unit tests >> "%LOG%"
dotnet test -nologo -v q --no-build >> "%LOG%" 2>&1

echo ----- [5] start API (creates/updates database) >> "%LOG%"
start "Erp API" cmd /k "dotnet run --no-build --project src\Host\Erp.Api --launch-profile http > %APILOG% 2>&1"

set UP=0
for /l %%i in (1,1,150) do (
  curl -s -o nul %API%/base/units && (set "UP=1" & goto :up)
  timeout /t 2 /nobreak >nul
)
:up
echo API up: %UP% >> "%LOG%"
findstr /c:"دیتابیس" /c:"اسکریپت" /c:"fail" "%APILOG%" >> "%LOG%" 2>&1

echo ----- [6] API smoke test >> "%LOG%"
echo POST fiscal-years/1405 (second run says it already exists): >> "%LOG%"
curl -s -X POST %API%/accounting/fiscal-years/1405 >> "%LOG%" 2>&1
echo. >> "%LOG%"
echo GET fiscal-years: >> "%LOG%"
curl -s %API%/accounting/fiscal-years >> "%LOG%" 2>&1
echo. >> "%LOG%"
echo GET account 2305: >> "%LOG%"
curl -s %API%/accounting/accounts/by-code/2305 >> "%LOG%" 2>&1
echo. >> "%LOG%"
echo GET detail types: >> "%LOG%"
curl -s %API%/accounting/details/types >> "%LOG%" 2>&1
echo. >> "%LOG%"

echo ----- [7] database contents >> "%LOG%"
sqlcmd -S .\ERP -E -C -I -d %DB% -h -1 -W -Q "SET NOCOUNT ON; SELECT 'scripts: ' + STRING_AGG(ScriptName, ', ') FROM dbo.SchemaVersions; SELECT 'accounts: ' + CAST(COUNT(*) AS varchar) FROM fin.Accounts; SELECT 'detail types: ' + CAST(COUNT(*) AS varchar) FROM fin.DetailTypes; SELECT 'calendar days: ' + CAST(COUNT(*) AS varchar) FROM base.Calendar; SELECT 'collation: ' + CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS varchar(60));" >> "%LOG%" 2>&1

echo ----- [8] SQL smoke test (all rows must be pass) >> "%LOG%"
sqlcmd -S .\ERP -E -C -I -f 65001 -d %DB% -W -s "|" -i database\tests\04_smoke_test.sql >> "%LOG%" 2>&1

echo ===== done >> "%LOG%"
if "%UP%"=="1" start "" http://localhost:5227/
