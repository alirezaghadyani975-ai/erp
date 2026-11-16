/* =====================================================================
   13 - لاگ تغییرات (audit.ChangeLog) برای همه فرم‌ها و تعریف‌ها

   - برنامه هر درج/اصلاح/حذف را با مقدار قبلی و جدید (فقط فیلدهای تغییرکرده) ثبت می‌کند.
   - OperationId: همه تغییرات یک عملیات کاربر (مثلاً ذخیره پرداخت با ردیف‌ها، چک‌ها و سندش) یک شناسه دارند.
   - ParentEntity/ParentId: ردیف‌ها به فرم اصلی‌شان وصل‌اند تا «تاریخچه» فرم، تغییر ردیف‌ها را هم نشان دهد.
   - UserName و Ip همان لحظه ثبت می‌شوند (کاربران در دیتابیس مرکزی‌اند).
   - لاگ فقط درج می‌شود؛ تریگر جلوی ویرایش و حذف را می‌گیرد.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

ALTER TABLE audit.ChangeLog ADD
    OperationId  UNIQUEIDENTIFIER NULL,
    ParentEntity VARCHAR(50)      NULL,
    ParentId     BIGINT           NULL,
    UserName     NVARCHAR(50)     NULL,
    Ip           VARCHAR(45)      NULL;
GO

CREATE INDEX IX_ChangeLog_Parent ON audit.ChangeLog (ParentEntity, ParentId, [At]) WHERE ParentEntity IS NOT NULL;
CREATE INDEX IX_ChangeLog_At ON audit.ChangeLog ([At]);
CREATE INDEX IX_ChangeLog_Actor ON audit.ChangeLog (ActorId, [At]);
CREATE INDEX IX_ChangeLog_Operation ON audit.ChangeLog (OperationId) WHERE OperationId IS NOT NULL;
GO

CREATE TRIGGER audit.TR_ChangeLog_ReadOnly ON audit.ChangeLog
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50130, N'لاگ تغییرات قابل ویرایش یا حذف نیست.', 1;
END;
GO
