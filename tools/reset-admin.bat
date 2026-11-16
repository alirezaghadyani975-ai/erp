@echo off
rem Reset the admin password of the development central database (ErpCentral on .\ERP) to a temporary one.
rem The user must change it at first login. Output: tools\reset-admin-log.txt
chcp 65001 >nul
cd /d "%~dp0.."
set "LOG=%cd%\tools\reset-admin-log.txt"
echo ===== %date% %time% > "%LOG%"
sqlcmd -S .\ERP -E -C -I -d ErpCentral -Q "SET NOCOUNT ON; UPDATE sec.Users SET PasswordHash='PBKDF2$SHA512$210000$ZRNhhlI66+Ql9oN/l8IFCA==$+aziOOlgvn+npVTwQa0QVvI2gwWASOWECCXUJ45WB1gJ/3Yodn4ljDii3CCUO/OUBt4XWRDpZT2+77ZSdiKw4A==', MustChangePassword=1, FailedCount=0, LockoutUntil=NULL, IsActive=1, SecurityStamp=NEWID() WHERE UserName='admin'; SELECT 'rows updated: ' + CAST(@@ROWCOUNT AS varchar); INSERT sec.SecurityLog (EventType, UserName, Details) VALUES (6, 'admin', N'reset-admin.bat');" >> "%LOG%" 2>&1
echo ===== done >> "%LOG%"
