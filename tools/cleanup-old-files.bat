@echo off
chcp 65001 >nul
cd /d "%~dp0.."
rem حذف فایل‌های نسخه قبلی (EF Migrations و کلاس‌های قدیمی) — یک بار اجرا کنید
if exist "tests\Erp.Accounting.Tests\DetailAndCalendarTests.cs" del /q "tests\Erp.Accounting.Tests\DetailAndCalendarTests.cs"
if exist "src\BuildingBlocks\Erp.SharedKernel\Domain\IAuditable.cs" del /q "src\BuildingBlocks\Erp.SharedKernel\Domain\IAuditable.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Domain\Currencies\Currency.cs" del /q "src\Modules\Accounting\Erp.Accounting.Domain\Currencies\Currency.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\AuditInterceptor.cs" del /q "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\AuditInterceptor.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\Migrations\20261013081934_InitialAccounting.cs" del /q "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\Migrations\20261013081934_InitialAccounting.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\Migrations\20261013081934_InitialAccounting.Designer.cs" del /q "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\Migrations\20261013081934_InitialAccounting.Designer.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\Migrations\AccountingDbContextModelSnapshot.cs" del /q "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\Migrations\AccountingDbContextModelSnapshot.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\Migrations" rmdir "src\Modules\Accounting\Erp.Accounting.Infrastructure\Persistence\Migrations"
if exist "src\Modules\Accounting\Erp.Accounting.Domain\Currencies" rmdir "src\Modules\Accounting\Erp.Accounting.Domain\Currencies"
echo فایل‌های قدیمی حذف شد.
pause
