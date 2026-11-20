/* =====================================================================
   14 - افتتاحیه و اطلاعات اول دوره

   - هر سال مالی فقط یک سند افتتاحیه (نوع 1) غیرباطل دارد. اگر در دیتابیس فعلی بیش از یکی هست
     (از قبل از این قاعده) ایندکس ساخته نمی‌شود و برنامه همچنان جلوی سند تکراری جدید را می‌گیرد.
   - دریافت و پرداخت «اول دوره»: چک‌های در جریانِ قبل از شروع کار با نرم‌افزار ثبت می‌شوند تا عملیات
     بعدی (وصول، برگشت، پاس) رویشان انجام شود؛ سند حسابداری نمی‌خورند چون مانده‌شان در سند افتتاحیه است.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT FiscalYearId FROM fin.Vouchers WHERE VoucherType = 1 AND [Status] IN (1, 2, 3)
               GROUP BY FiscalYearId HAVING COUNT(*) > 1)
    CREATE UNIQUE INDEX UX_Vouchers_Opening ON fin.Vouchers (FiscalYearId) WHERE VoucherType = 1 AND [Status] IN (1, 2, 3);
GO

ALTER TABLE trs.Receipts ADD IsOpening BIT NOT NULL CONSTRAINT DF_Receipts_IsOpening DEFAULT (0);
ALTER TABLE trs.Payments ADD IsOpening BIT NOT NULL CONSTRAINT DF_Payments_IsOpening DEFAULT (0);
GO
