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
SELECT @AR = Id FROM fin.Accounts WHERE Code = '2411';     -- حساب‌های دریافتنی تجاری
SELECT @Sales = Id FROM fin.Accounts WHERE Code = '6001';  -- فروش محصول
SELECT @Kol23 = Id FROM fin.Accounts WHERE Code = '24';    -- یک حساب کل (غیرمعین)
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

-- 8) عامل هزینه فقط در کلِ ماهیت خودش (اسکریپت‌های ۰۷ و ۰۸)
DECLARE @V3 BIGINT, @Salary INT, @PayFactor INT, @DepFactor INT;
SELECT @Salary = Id FROM fin.Accounts WHERE Code = '8110';
SELECT TOP (1) @PayFactor = Id FROM fin.DetailAccounts WHERE SourceModule = 'costing' AND CostNature = 1;
SELECT TOP (1) @DepFactor = Id FROM fin.DetailAccounts WHERE SourceModule = 'costing' AND CostNature = 2;
INSERT fin.Vouchers (FiscalYearId, TempNo, VoucherDate, CreatedBy, [Description]) VALUES (@YearId, 3, @D1, 1, N'تست عامل هزینه');
SET @V3 = SCOPE_IDENTITY();
BEGIN TRY
    INSERT fin.VoucherLines (VoucherId, RowNo, AccountId, Detail1Id, Debit) VALUES (@V3, 1, @Salary, @PayFactor, 100);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'ثبت عامل حقوقی در معین حقوق تولیدی', N'0', CAST(@err AS NVARCHAR(50)));
BEGIN TRY
    INSERT fin.VoucherLines (VoucherId, RowNo, AccountId, Detail1Id, Debit) VALUES (@V3, 2, @Salary, @DepFactor, 100);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد عامل استهلاک در معین حقوق', N'50060', CAST(@err AS NVARCHAR(50)));

-- 9) هر سند منبع فقط یک سند حسابداری زنده (اسکریپت ۱۰)
INSERT fin.Vouchers (FiscalYearId, TempNo, VoucherDate, CreatedBy, SourceModule, SourceDocType, SourceDocumentId)
VALUES (@YearId, 5, @D1, 1, 'expenses', 'expense_invoice', -1);
BEGIN TRY
    INSERT fin.Vouchers (FiscalYearId, TempNo, VoucherDate, CreatedBy, SourceModule, SourceDocType, SourceDocumentId)
    VALUES (@YearId, 6, @D1, 1, 'expenses', 'expense_invoice', -1);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد سند دوم برای یک سند منبع', N'2601', CAST(@err AS NVARCHAR(50)));

-- 10) لاگ تغییرات فقط درج می‌شود (اسکریپت ۱۳)
BEGIN TRY
    UPDATE audit.ChangeLog SET Reason = Reason WHERE 1 = 0;
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد ویرایش لاگ تغییرات', N'50130', CAST(@err AS NVARCHAR(50)));

-- 11) نوع کالا همان نوع گروهش (اسکریپت ۱۶)
DECLARE @Unit SMALLINT = (SELECT TOP (1) Id FROM base.Units ORDER BY Id);
DECLARE @GoodsGroup INT = (SELECT TOP (1) Id FROM inv.ItemGroups WHERE Kind = 1 ORDER BY Id);
DECLARE @ServiceGroup INT = (SELECT TOP (1) Id FROM inv.ItemGroups WHERE Kind = 2 ORDER BY Id);
DECLARE @Item INT, @PInv BIGINT, @PLine BIGINT, @PRet BIGINT;
BEGIN TRY
    INSERT inv.Items (Code, Title, Kind, GroupId, UnitId) VALUES (N'SMOKE-X', N'تست نوع', 1, @ServiceGroup, @Unit);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد کالا با نوع متفاوت از گروه', N'50201', CAST(@err AS NVARCHAR(50)));

-- 12) برگشت از خرید بیش از مقدار فاکتور (اسکریپت ۱۷)؛ فاکتور از روی رسید خرید (اسکریپت ۱۸)
INSERT inv.Items (Code, Title, Kind, GroupId, UnitId) VALUES (N'SMOKE-1', N'کالای تست', 1, @GoodsGroup, @Unit);
SET @Item = SCOPE_IDENTITY();
DECLARE @Wh INT = (SELECT TOP (1) Id FROM inv.Warehouses ORDER BY Id), @SRec BIGINT, @SRecLine BIGINT, @SIss BIGINT;
INSERT inv.StockDocs (DocType, DocNo, DocDate, WarehouseId, PartyId, CreatedBy) VALUES (2, -1, @D1, @Wh, @PersonId, 1);
SET @SRec = SCOPE_IDENTITY();
INSERT inv.StockDocLines (DocId, RowNo, ItemId, Quantity) VALUES (@SRec, 1, @Item, 2);
SET @SRecLine = SCOPE_IDENTITY();
INSERT pur.PurchaseInvoices (DocNo, DocDate, SupplierId, GrossAmount, DiscountAmount, VatAmount, CreatedBy)
VALUES (-1, @D1, @PersonId, 200, 0, 0, 1);
SET @PInv = SCOPE_IDENTITY();
INSERT pur.PurchaseInvoiceLines (InvoiceId, RowNo, ItemId, Quantity, UnitPrice, Discount, Amount, VatAmount, ReceiptLineId)
VALUES (@PInv, 1, @Item, 2, 100, 0, 200, 0, @SRecLine);
SET @PLine = SCOPE_IDENTITY();
INSERT pur.PurchaseReturns (DocNo, DocDate, InvoiceId, Amount, VatAmount, CreatedBy) VALUES (-1, @D1, @PInv, 300, 0, 1);
SET @PRet = SCOPE_IDENTITY();
BEGIN TRY
    INSERT pur.PurchaseReturnLines (ReturnId, RowNo, InvoiceLineId, Quantity, Amount, VatAmount) VALUES (@PRet, 1, @PLine, 3, 300, 0);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد برگشت از خرید بیش از مقدار فاکتور', N'50210', CAST(@err AS NVARCHAR(50)));

-- 13) ردیف کالای فاکتور خرید بدون رسید (اسکریپت ۱۸)
BEGIN TRY
    INSERT pur.PurchaseInvoiceLines (InvoiceId, RowNo, ItemId, Quantity, UnitPrice, Discount, Amount, VatAmount)
    VALUES (@PInv, 2, @Item, 1, 100, 0, 100, 0);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد ردیف فاکتور خرید بدون رسید انبار', N'50310', CAST(@err AS NVARCHAR(50)));

-- 14) حواله بیش از موجودی (اسکریپت ۱۸)
INSERT inv.StockDocs (DocType, DocNo, DocDate, WarehouseId, PartyId, CreatedBy) VALUES (11, -1, @D1, @Wh, @PersonId, 1);
SET @SIss = SCOPE_IDENTITY();
BEGIN TRY
    INSERT inv.StockDocLines (DocId, RowNo, ItemId, Quantity) VALUES (@SIss, 1, @Item, 3);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد حواله‌ای که موجودی را منفی می‌کند', N'50305', CAST(@err AS NVARCHAR(50)));

-- 15) سند انبار تازه، محاسبه بهای تمام‌شده همان ماه را «نیازمند محاسبه دوباره» می‌کند (اسکریپت ۱۹)
INSERT inv.CostRuns (FiscalPeriodId, StartDate, EndDate, [Status], RunAt, RunBy)
SELECT TOP (1) Id, StartDate, EndDate, 1, SYSUTCDATETIME(), 1 FROM fin.FiscalPeriods WHERE @D1 BETWEEN StartDate AND EndDate AND FiscalYearId = @YearId;
INSERT inv.StockDocLines (DocId, RowNo, ItemId, Quantity) VALUES (@SRec, 2, @Item, 1);
INSERT @R VALUES (N'علامت «نیازمند محاسبه دوباره» بعد از تغییر سند انبار', N'2',
    (SELECT CAST(MAX([Status]) AS NVARCHAR(50)) FROM inv.CostRuns WHERE @D1 BETWEEN StartDate AND EndDate));

-- 16) پرونده پرسنلی فقط برای شخص با نقش کارمند؛ ردیف حکم فقط از اقلام حکمی (اسکریپت ۲۰)
DECLARE @Ws INT, @Ctr INT, @Emp INT, @Dec INT;
INSERT hr.Workshops (Code, Title) VALUES ('9999999999', N'کارگاه تست');
SET @Ws = SCOPE_IDENTITY();
INSERT cost.CostCenters (Title, CenterTypeId) SELECT TOP (1) N'مرکز تست حقوق', Id FROM cost.CostCenterTypes ORDER BY Id;
SET @Ctr = SCOPE_IDENTITY();
BEGIN TRY
    INSERT hr.Employees (PersonId, PersonnelNo, WorkshopId, HireDate, CostCenterId) VALUES (@PersonId, N'SMOKE1', @Ws, @D1, @Ctr);
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد پرونده پرسنلی شخص بدون نقش کارمند', N'50401', CAST(@err AS NVARCHAR(50)));

INSERT base.PersonRoles (PersonId, RoleId) SELECT @PersonId, Id FROM base.PersonRoleTypes WHERE Code = 'employee';
INSERT hr.Employees (PersonId, PersonnelNo, WorkshopId, HireDate, CostCenterId) VALUES (@PersonId, N'SMOKE1', @Ws, @D1, @Ctr);
SET @Emp = SCOPE_IDENTITY();
INSERT pay.Decrees (EmployeeId, DocNo, EffectiveDate, DecreeType, CreatedBy) VALUES (@Emp, -1, @D1, 1, 1);
SET @Dec = SCOPE_IDENTITY();
BEGIN TRY
    INSERT pay.DecreeItems (DecreeId, PayItemId, Amount) SELECT @Dec, Id, 1 FROM pay.PayItems WHERE Code = 'BASE';
    SET @err = 0;
END TRY BEGIN CATCH SET @err = ERROR_NUMBER(); END CATCH;
INSERT @R VALUES (N'رد قلم فرمولی در ردیف حکم', N'50410', CAST(@err AS NVARCHAR(50)));

-- 17) اقلام قانونی حقوق با عامل هزینه و حساب درست
INSERT @R VALUES (N'۱۸ قلم قانونی حقوق با عامل هزینه و حساب', N'18', (SELECT CAST(COUNT(*) AS NVARCHAR(50)) FROM pay.PayItems
    WHERE IsSystem = 1 AND ((Kind = 2 AND AccountId IS NOT NULL) OR (Kind = 1 AND CostFactorId IS NOT NULL)
        OR (Kind IN (3, 4) AND CostFactorId IS NOT NULL AND AccountId IS NOT NULL) OR Kind = 5)));

DELETE pay.Decrees WHERE Id = @Dec;
DELETE hr.Employees WHERE Id = @Emp;
DELETE base.PersonRoles WHERE PersonId = @PersonId;
DELETE hr.Workshops WHERE Id = @Ws;
DELETE cost.CostCenters WHERE Id = @Ctr;

DELETE pur.PurchaseReturns WHERE Id = @PRet;
DELETE pur.PurchaseInvoices WHERE Id = @PInv;
DELETE inv.StockDocs WHERE Id IN (@SRec, @SIss);
DELETE inv.Items WHERE Code IN (N'SMOKE-1', N'SMOKE-X');

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
DELETE inv.CostRuns WHERE FiscalPeriodId IN (SELECT Id FROM fin.FiscalPeriods WHERE FiscalYearId = @YearId);
DELETE fin.FiscalPeriods WHERE FiscalYearId = @YearId;
DELETE fin.FiscalYears WHERE Id = @YearId;
PRINT N'داده‌های تست پاک شد.';
