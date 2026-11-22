/* =====================================================================
   دیتابیس مرکزی (ErpCentral) - 02: محدوده داده انبار

   نوع ۵ = انبار (بیت ۱۶ در UserCompanies.ScopeLimits). کاربری که بیت انبارش روشن باشد فقط سندها، کاردکس،
   موجودی و گزارش انبارهای ثبت‌شده در DataScopes را می‌بیند و فقط روی همان‌ها سند می‌زند.
   ===================================================================== */

ALTER TABLE sec.UserCompanies DROP CONSTRAINT CK_UserCompanies_Scope;
ALTER TABLE sec.UserCompanies ADD CONSTRAINT CK_UserCompanies_Scope CHECK (ScopeLimits BETWEEN 0 AND 31);
GO

ALTER TABLE sec.DataScopes DROP CONSTRAINT CK_DataScopes_Kind;
ALTER TABLE sec.DataScopes ADD CONSTRAINT CK_DataScopes_Kind CHECK (Kind BETWEEN 1 AND 5);
GO
