/* =====================================================================
   04 - تست سریع بعد از نصب
   ساختار و قاعده‌های پشتیبان دیتابیس را امتحان می‌کند و در آخر داده‌های تست را پاک می‌کند.
   روی دیتابیس دمو اجرا کنید، نه روی دیتابیس واقعی یک شرکت.
   خروجی: جدول نتیجه؛ همه ردیف‌ها باید «قبول» باشند.
   برای تست از سال ۱۴۱۹ استفاده می‌شود تا با سال مالی واقعی تداخل نکند.
   ===================================================================== */
SET NOCOUNT ON;

DECLARE @R TABLE (Seq INT IDENTITY(1,1), Test NVARCHAR(200), Expected NVARCHAR(50), Got NVARCHAR(50));
DECLARE @err INT, @YearId SMALLINT, @PersonId INT, @CustType SMALLINT, @Cust INT,
        @AR INT, @Sales INT, @Kol23 INT, @V1 BIGINT, @V2 BIGINT, @L1 BIGINT,
        @D1 DATE, @D2 DATE, @Y0 DATE, @Y1 DATE;

SELECT @D1 = [Date] FROM base.Calendar WHERE PersianDate = '1419/01/12';
SELECT @D2 = [Date] FROM base.Calendar WHERE PersianDate = '1419/01/05';
SELECT @Y0 = MIN([Date]), @Y1 = MAX([Date]) FROM base.Calendar WHERE PersianYear = 1419;
SELECT @AR = Id FROM fin.Accounts WHERE Code = '2305';     -- دریافتنی تجاری اشخاص حقیقی
SELECT @Sales = Id FROM fin.Accounts WHERE Code = '6101';  -- فروش
SELECT @Kol23 = Id FROM fin.Accounts WHERE Code = '23';    -- یک حساب کل (غیرمعین)
SELECT TOP (1) @CustType = Id FROM fin.DetailTypes WHERE OwnerModule = 'persons' AND PersonKind = 1 AND IsActive = 1;

-- کد آزاد بعدی در گروه اشخاص حقیقی (دیتابیس ممکن است اشخاص واقعی هم داشته باشد)
DECLARE @CustCode VARCHAR(10), @OutCode VARCHAR(10);
SELECT @CustCode = RIGHT(REPLICATE('0', LEN(t.CodeFrom))
                         + CAST(ISNULL(MAX(CAST(d.Code AS BIGINT)), CAST(t.CodeFrom AS BIGINT) - 1) + 1 AS VARCHAR(20)),
                         LEN(t.CodeFrom)),
       @OutCode = RIGHT(REPLICATE('0', LEN(t.CodeFrom)) + CAST(CAST(t.CodeTo AS BIGINT) + 1 AS VARCHAR(20)), LEN(t.CodeFrom))
FROM fin.DetailTypes t LEFT JOIN fin.DetailAccounts d ON d.DetailTypeId = t.Id
WHERE t.Id = @CustType
GROUP BY t.CodeFrom, t.CodeTo;

-- 1) ساخت سال مالی با ۱۲ دوره
EXEC fin.usp_CreateFiscalYear @PersianYear = 1419;
SELECT @YearId = Id FROM fin.FiscalYears WHERE Title = N'1419';
INSERT @R VALUES (N'سال مالی ۱۴۱۹ با ۱۲ دوره و تاریخ شروع و پایان درست', N'قبول',
    CASE WHEN (SELECT COUNT(*) FROM fin.FiscalPeriods WHERE FiscalYearId = @YearId) = 12
          AND EXISTS (SELECT 1 FROM fin.FiscalYears WHERE Id = @YearId AND StartDate = @Y0 AND EndDate = @Y1)
         THEN N'قبول' ELSE N'رد' END);

-- 2) شخص و تفصیلی مشتری
INSERT base.Persons (PersonKind, DisplayName) VALUES (1, N'مشتری تست');
SET @PersonId = SCOPE_IDENTITY();
INSERT fin.DetailAccounts (DetailTypeId, Code, Title, PersonId, SourceModule) VALUES (@CustType, @CustCode, N'مشتری تست', @PersonId, 'persons');
SET @Cust = SCOPE_IDENTITY();

BEGIN TRY
    INSERT fin.DetailAccounts (DetailTypeId, Code, Title) VALUES (@CustType, @OutCode, N'خارج از بازه');
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد کد تفصیلی خارج از بازه نوع', N'50002', CAST(@err AS NVARCHAR(50)));

-- 3) سند تراز: پیش‌نویس ← موقت ← دائم
INSERT fin.Vouchers (FiscalYearId, TempNo, VoucherDate, CreatedBy, [Description]) VALUES (@YearId, 1, @D1, 1, N'سند تست');
SET @V1 = SCOPE_IDENTITY();
INSERT fin.VoucherLines (VoucherId, RowNo, AccountId, Detail1Id, Debit) VALUES (@V1, 1, @AR, @Cust, 1000000);
SET @L1 = SCOPE_IDENTITY();
INSERT fin.VoucherLines (VoucherId, RowNo, AccountId, Credit) VALUES (@V1, 2, @Sales, 1000000);

BEGIN TRY
    INSERT fin.VoucherLines (VoucherId, RowNo, AccountId, Debit) VALUES (@V1, 3, @Kol23, 10);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد ثبت در حساب کل', N'50012', CAST(@err AS NVARCHAR(50)));

BEGIN TRY
    INSERT fin.VoucherLines (VoucherId, RowNo, AccountId, Debit, Credit) VALUES (@V1, 3, @Sales, 10, 10);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد ردیفی که هم بدهکار و هم بستانکار دارد', N'547', CAST(@err AS NVARCHAR(50)));

BEGIN TRY
    UPDATE fin.Vouchers SET [Status] = 2 WHERE Id = @V1;
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'موقت کردن سند تراز', N'0', CAST(@err AS NVARCHAR(50)));

-- 4) سند ناتراز نباید موقت شود
INSERT fin.Vouchers (FiscalYearId, TempNo, VoucherDate, CreatedBy) VALUES (@YearId, 2, @D1, 1);
SET @V2 = SCOPE_IDENTITY();
INSERT fin.VoucherLines (VoucherId, RowNo, AccountId, Debit) VALUES (@V2, 1, @AR, 500);
INSERT fin.VoucherLines (VoucherId, RowNo, AccountId, Credit) VALUES (@V2, 2, @Sales, 400);
BEGIN TRY
    UPDATE fin.Vouchers SET [Status] = 2 WHERE Id = @V2;
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد موقت کردن سند ناتراز', N'50013', CAST(@err AS NVARCHAR(50)));

-- 5) دائم کردن و قفل بودن سند دائم
BEGIN TRY
    UPDATE fin.Vouchers SET [Status] = 3, VoucherNo = 1, ApprovedBy = 1, ApprovedAt = SYSUTCDATETIME() WHERE Id = @V1;
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'دائم کردن سند موقت', N'0', CAST(@err AS NVARCHAR(50)));

BEGIN TRY
    UPDATE fin.Vouchers SET [Description] = N'تغییر' WHERE Id = @V1;
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد ویرایش سربرگ سند دائم', N'50010', CAST(@err AS NVARCHAR(50)));

BEGIN TRY
    UPDATE fin.VoucherLines SET Debit = 2000000 WHERE Id = @L1;
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد ویرایش ردیف سند دائم', N'50011', CAST(@err AS NVARCHAR(50)));

-- 6) تاریخ و دوره
BEGIN TRY
    INSERT fin.Vouchers (FiscalYearId, TempNo, VoucherDate, CreatedBy) VALUES (@YearId, 3, DATEADD(DAY, -1, @Y0), 1);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد تاریخ سند خارج از سال مالی', N'50014', CAST(@err AS NVARCHAR(50)));

UPDATE fin.FiscalPeriods SET [Status] = 2 WHERE FiscalYearId = @YearId AND PeriodNo = 1;
BEGIN TRY
    INSERT fin.Vouchers (FiscalYearId, TempNo, VoucherDate, CreatedBy) VALUES (@YearId, 4, @D2, 1);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد سند در دوره قفل', N'50016', CAST(@err AS NVARCHAR(50)));
UPDATE fin.FiscalPeriods SET [Status] = 1 WHERE FiscalYearId = @YearId AND PeriodNo = 1;

-- 7) نمای دفتر
INSERT @R VALUES (N'نمای دفتر: دو ردیف با تاریخ شمسی ۱۴۱۹/۰۱/۱۲', N'قبول',
    CASE WHEN (SELECT COUNT(*) FROM fin.vLedger WHERE VoucherId = @V1 AND PersianDate = '1419/01/12') = 2
         THEN N'قبول' ELSE N'رد' END);

-- نتیجه
SELECT Seq, Test, Expected, Got, CASE WHEN Expected = Got THEN N'قبول' ELSE N'رد' END AS Result
FROM @R ORDER BY Seq;

-- پاک کردن داده‌های تست (برای سند دائم، محافظ موقتاً خاموش می‌شود)
DISABLE TRIGGER fin.trg_Vouchers_Protect ON fin.Vouchers;
UPDATE fin.Vouchers SET [Status] = 1, VoucherNo = NULL WHERE FiscalYearId = @YearId;
ENABLE TRIGGER fin.trg_Vouchers_Protect ON fin.Vouchers;
DELETE fin.VoucherLines WHERE VoucherId IN (SELECT Id FROM fin.Vouchers WHERE FiscalYearId = @YearId);
DELETE fin.Vouchers WHERE FiscalYearId = @YearId;
DELETE fin.DetailAccounts WHERE PersonId = @PersonId;
DELETE base.Persons WHERE Id = @PersonId;
DELETE fin.FiscalPeriods WHERE FiscalYearId = @YearId;
DELETE fin.FiscalYears WHERE Id = @YearId;
PRINT N'داده‌های تست پاک شد.';
