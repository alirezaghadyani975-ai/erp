/* =====================================================================
   15 - شماره‌گذاری مجدد اسناد دائم

   سند دائم همچنان ویرایش و حذف نمی‌شود، با یک استثنا: فقط شماره قطعی (VoucherNo) عوض شود
   تا شماره‌ها به ترتیب تاریخ مرتب شوند (پیش از تحریر دفاتر قانونی). تریگر fin.trg_Vouchers_Validate
   همچنان جلوی هر تغییری در سال غیرباز را می‌گیرد، پس سال بسته‌شده هرگز شماره‌گذاری مجدد نمی‌شود.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE OR ALTER TRIGGER fin.trg_Vouchers_Protect ON fin.Vouchers AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    -- حذف سند دائم
    IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted WHERE [Status] = 3)
        THROW 50010, N'سند دائم قابل ویرایش یا حذف نیست؛ اصلاح فقط با سند اصلاحی.', 1;

    -- ویرایش سند دائم: فقط تغییر شماره قطعی مجاز است
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.Id = d.Id
               WHERE d.[Status] = 3
                 AND (i.[Status] <> d.[Status] OR i.FiscalYearId <> d.FiscalYearId OR i.TempNo <> d.TempNo
                      OR i.VoucherDate <> d.VoucherDate OR i.VoucherType <> d.VoucherType
                      OR ISNULL(i.[Description], N'') <> ISNULL(d.[Description], N'')
                      OR ISNULL(i.SourceModule, '') <> ISNULL(d.SourceModule, '')
                      OR ISNULL(i.SourceDocType, '') <> ISNULL(d.SourceDocType, '')
                      OR ISNULL(i.SourceDocumentId, 0) <> ISNULL(d.SourceDocumentId, 0)
                      OR i.CreatedBy <> d.CreatedBy OR i.CreatedAt <> d.CreatedAt
                      OR ISNULL(i.ApprovedBy, 0) <> ISNULL(d.ApprovedBy, 0)
                      OR ISNULL(i.ApprovedAt, '19000101') <> ISNULL(d.ApprovedAt, '19000101')))
        THROW 50010, N'سند دائم قابل ویرایش یا حذف نیست؛ اصلاح فقط با سند اصلاحی.', 1;
END;
GO
