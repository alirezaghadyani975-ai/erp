@echo off
chcp 65001 >nul
cd /d "%~dp0.."
rem حذف فایل‌هایی که در نسخه جدید جابه‌جا یا حذف شده‌اند (اشخاص به ماژول BaseData رفت). اجرای چندباره بی‌خطر است.
rem نسخه قبلی این فایل‌ها در git (commit قبلی) باقی است.
if exist "src\Modules\Accounting\Erp.Accounting.Application\Abstractions\NotFoundException.cs" del /q "src\Modules\Accounting\Erp.Accounting.Application\Abstractions\NotFoundException.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Application\Common\ErrorMessages.cs" del /q "src\Modules\Accounting\Erp.Accounting.Application\Common\ErrorMessages.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Application\Persons\PersonDtos.cs" del /q "src\Modules\Accounting\Erp.Accounting.Application\Persons\PersonDtos.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Application\Persons\PersonService.cs" del /q "src\Modules\Accounting\Erp.Accounting.Application\Persons\PersonService.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Domain\BaseData\Employee.cs" del /q "src\Modules\Accounting\Erp.Accounting.Domain\BaseData\Employee.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Domain\BaseData\Person.cs" del /q "src\Modules\Accounting\Erp.Accounting.Domain\BaseData\Person.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Domain\BaseData\PersonEnums.cs" del /q "src\Modules\Accounting\Erp.Accounting.Domain\BaseData\PersonEnums.cs"
if exist "src\Modules\Accounting\Erp.Accounting.Application\Common" rmdir "src\Modules\Accounting\Erp.Accounting.Application\Common" 2>nul
if exist "src\Modules\Accounting\Erp.Accounting.Application\Persons" rmdir "src\Modules\Accounting\Erp.Accounting.Application\Persons" 2>nul
if exist "src\Host\Erp.Api\Components\Pages\Costing\ExpenseTypes.razor" del /q "src\Host\Erp.Api\Components\Pages\Costing\ExpenseTypes.razor"
echo فایل‌های قدیمی حذف شد.
