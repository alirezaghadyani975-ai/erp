/* =====================================================================
   19 - انبار مرحله ۲: محاسبه بهای تمام‌شده ماهانه (طرح docs/inventory-design.md بخش ۴)

   - میانگین موزون متحرک به تفکیک کالا × انبار؛ اجرا برای هر دوره مالی (ماه) و قابل اجرای دوباره.
   - بهای هر ردیف در inv.StockDocLines (UnitCost، Amount) و به تفکیک عنصر بها در inv.StockLineCosts
     (فعلاً فقط ۱ مواد؛ دستمزد و سربار برای تولید بعدی). مانده پایان هر ماه در inv.CostBalances.
   - سند ماه با موتور سند (الگوی inv_cost): بهای تمام‌شده فروش، برگشت آن، هزینه مصرف و کاهش/افزایش موجودی.
     رسید خرید و حواله برگشت از خرید ردیفی ندارند (فاکتورها همان را ثبت کرده‌اند)؛ اول دوره هم سند ندارد.
   - هر تغییر در سند انبار یا فاکتور خرید، اجرای همان ماه و ماه‌های بعد را «نیازمند محاسبه دوباره» می‌کند.
     سند انبار در ماهی که سند بهای تمام‌شده‌اش (یا ماه‌های بعدش) دائم شده تغییر نمی‌کند.
   - گروه کالا معین بهای تمام‌شده می‌گیرد (پیش‌فرض 7001).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------------------------------------------------------------------
   ۱) معین بهای تمام‌شده روی گروه کالا
   --------------------------------------------------------------------- */
ALTER TABLE inv.ItemGroups ADD CostAccountId INT NULL CONSTRAINT FK_ItemGroups_Cost REFERENCES fin.Accounts (Id);
GO
DECLARE @Cogs INT = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '7001');
IF @Cogs IS NULL THROW 50330, N'معین بهای تمام‌شده کالای فروش‌رفته (7001) پیدا نشد.', 1;
UPDATE inv.ItemGroups SET CostAccountId = @Cogs WHERE Kind = 1;
ALTER TABLE inv.ItemGroups ADD CONSTRAINT CK_ItemGroups_Cost
    CHECK ((Kind = 1 AND CostAccountId IS NOT NULL) OR (Kind = 2 AND CostAccountId IS NULL));
GO

ALTER TRIGGER inv.trg_ItemGroups_Accounts ON inv.ItemGroups AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN fin.Accounts a ON a.Id IN (i.SalesAccountId, i.SalesReturnAccountId, i.PurchaseAccountId, i.CostAccountId)
               WHERE a.[Level] <> 3 OR a.IsActive = 0)
        THROW 50200, N'معین‌های گروه کالا باید معین (سطح ۳) و فعال باشند.', 1;
END;
GO

/* ---------------------------------------------------------------------
   ۲) اجرای محاسبه، مانده‌ها و بها به تفکیک عنصر
   --------------------------------------------------------------------- */
CREATE TABLE inv.CostRuns (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CostRuns PRIMARY KEY,
    FiscalPeriodId INT          NOT NULL CONSTRAINT UQ_CostRuns_Period UNIQUE
                                CONSTRAINT FK_CostRuns_Period REFERENCES fin.FiscalPeriods (Id),
    StartDate      DATE         NOT NULL,
    EndDate        DATE         NOT NULL,
    [Status]       TINYINT      NOT NULL,                      -- 1 محاسبه‌شده، 2 نیازمند محاسبه دوباره
    RunAt          DATETIME2(0) NOT NULL,
    RunBy          INT          NOT NULL,
    CONSTRAINT CK_CostRuns_Status CHECK ([Status] IN (1, 2)),
    CONSTRAINT CK_CostRuns_Dates  CHECK (StartDate <= EndDate)
);
CREATE INDEX IX_CostRuns_End ON inv.CostRuns (EndDate);
GO

CREATE TABLE inv.CostBalances (
    RunId       INT           NOT NULL CONSTRAINT FK_CostBalances_Run REFERENCES inv.CostRuns (Id) ON DELETE CASCADE,
    ItemId      INT           NOT NULL CONSTRAINT FK_CostBalances_Item REFERENCES inv.Items (Id),
    WarehouseId INT           NOT NULL CONSTRAINT FK_CostBalances_Warehouse REFERENCES inv.Warehouses (Id),
    Quantity    DECIMAL(24,6) NOT NULL,
    Amount      DECIMAL(24,4) NOT NULL,
    CONSTRAINT PK_CostBalances PRIMARY KEY (RunId, ItemId, WarehouseId)
);
GO

-- عنصر بها: 1 مواد؛ بعداً 2 دستمزد مستقیم، 3 سربار (جذب از مراکز هزینه در تولید)
CREATE TABLE inv.StockLineCosts (
    LineId   BIGINT        NOT NULL CONSTRAINT FK_StockLineCosts_Line REFERENCES inv.StockDocLines (Id) ON DELETE CASCADE,
    Element  TINYINT       NOT NULL,
    Amount   DECIMAL(24,4) NOT NULL,
    CONSTRAINT PK_StockLineCosts PRIMARY KEY (LineId, Element),
    CONSTRAINT CK_StockLineCosts_Element CHECK (Element BETWEEN 1 AND 3)
);
GO

/* ---------------------------------------------------------------------
   ۳) کنترل ردیف‌ها: نوشتن بها (UnitCost/Amount) کنترل‌ها را دوباره اجرا نمی‌کند
   --------------------------------------------------------------------- */
ALTER TRIGGER inv.trg_StockDocLines_Check ON inv.StockDocLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT (UPDATE(DocId) OR UPDATE(ItemId) OR UPDATE(Quantity) OR UPDATE(SourceLineId) OR UPDATE(CostFactorId)
            OR UPDATE(CostCenterId) OR UPDATE(EnteredPrice))
        RETURN;

    IF EXISTS (SELECT 1 FROM inserted i JOIN inv.Items it ON it.Id = i.ItemId WHERE it.Kind <> 1)
        THROW 50301, N'در رسید و حواله انبار فقط «کالا» می‌آید، نه خدمت.', 1;

    IF EXISTS (SELECT 1 FROM inserted i JOIN inv.StockDocs d ON d.Id = i.DocId
               WHERE (d.DocType IN (3, 12) AND i.SourceLineId IS NULL)
                  OR (d.DocType NOT IN (3, 12) AND i.SourceLineId IS NOT NULL)
                  OR (d.DocType = 13 AND i.CostFactorId IS NULL)
                  OR (d.DocType <> 13 AND (i.CostFactorId IS NOT NULL OR i.CostCenterId IS NOT NULL))
                  OR (d.DocType = 1 AND i.EnteredPrice IS NULL)
                  OR (d.DocType NOT IN (1, 2) AND i.EnteredPrice IS NOT NULL))
        THROW 50302, N'ردیف با نوع سند انبار سازگار نیست (ارجاع برگشت، عامل و مرکز هزینه مصرف یا فی).', 1;

    IF EXISTS (SELECT 1 FROM inserted i
               JOIN inv.StockDocs d ON d.Id = i.DocId
               JOIN inv.StockDocLines s ON s.Id = i.SourceLineId
               JOIN inv.StockDocs sd ON sd.Id = s.DocId
               WHERE s.ItemId <> i.ItemId OR sd.PartyId <> d.PartyId OR sd.DocDate > d.DocDate
                  OR (d.DocType = 3 AND sd.DocType <> 11) OR (d.DocType = 12 AND sd.DocType <> 2))
        THROW 50303, N'ردیف برگشت با ردیف مبدأ (کالا، طرف حساب، نوع یا تاریخ) سازگار نیست.', 1;

    IF EXISTS (SELECT 1
               FROM (SELECT DISTINCT SourceLineId FROM inserted WHERE SourceLineId IS NOT NULL) x
               JOIN inv.StockDocLines s ON s.Id = x.SourceLineId
               WHERE (SELECT SUM(r.Quantity) FROM inv.StockDocLines r WHERE r.SourceLineId = x.SourceLineId) > s.Quantity)
        THROW 50304, N'مقدار برگشتی از مقدار ردیف مبدأ بیشتر است.', 1;

    IF EXISTS (
        SELECT 1
        FROM (SELECT SUM(daily.Qty) OVER (PARTITION BY daily.ItemId, daily.WarehouseId ORDER BY daily.DocDate
                                          ROWS UNBOUNDED PRECEDING) AS Balance
              FROM (SELECT l.ItemId, d.WarehouseId, d.DocDate,
                           SUM(CASE WHEN d.DocType < 10 THEN l.Quantity ELSE -l.Quantity END) AS Qty
                    FROM inv.StockDocLines l
                    JOIN inv.StockDocs d ON d.Id = l.DocId
                    JOIN (SELECT DISTINCT i.ItemId, d2.WarehouseId
                          FROM inserted i JOIN inv.StockDocs d2 ON d2.Id = i.DocId) k
                      ON k.ItemId = l.ItemId AND k.WarehouseId = d.WarehouseId
                    GROUP BY l.ItemId, d.WarehouseId, d.DocDate) daily) r
        WHERE r.Balance < 0)
        THROW 50305, N'موجودی کالا در انبار منفی می‌شود.', 1;
END;
GO

/* ---------------------------------------------------------------------
   ۴) تغییر سند انبار یا فاکتور خرید ← اجرای ماه و ماه‌های بعد «نیازمند محاسبه دوباره»
   --------------------------------------------------------------------- */
CREATE TRIGGER inv.trg_StockDocLines_CostRuns ON inv.StockDocLines AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT (UPDATE(DocId) OR UPDATE(ItemId) OR UPDATE(Quantity) OR UPDATE(SourceLineId) OR UPDATE(CostFactorId)
            OR UPDATE(CostCenterId) OR UPDATE(EnteredPrice))
       AND EXISTS (SELECT 1 FROM inserted)
        RETURN;  -- فقط نوشتن بها

    DECLARE @From DATE = (SELECT MIN(d.DocDate)
                          FROM (SELECT DocId FROM inserted UNION SELECT DocId FROM deleted) x
                          JOIN inv.StockDocs d ON d.Id = x.DocId);
    IF @From IS NULL RETURN;  -- حذف کامل سند: تریگر سربرگ رسیدگی می‌کند

    IF EXISTS (SELECT 1 FROM inv.CostRuns r
               JOIN fin.Vouchers v ON v.SourceModule = 'inventory' AND v.SourceDocType = 'inv_cost' AND v.SourceDocumentId = r.Id
               WHERE r.EndDate >= @From AND v.[Status] = 3)
        THROW 50320, N'سند بهای تمام‌شده این ماه یا ماه‌های بعد دائم شده است؛ سند انبار این تاریخ تغییر نمی‌کند.', 1;

    UPDATE inv.CostRuns SET [Status] = 2 WHERE EndDate >= @From AND [Status] = 1;
END;
GO

CREATE TRIGGER inv.trg_StockDocs_CostRuns ON inv.StockDocs AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    IF EXISTS (SELECT 1 FROM inserted) AND NOT (UPDATE(DocDate) OR UPDATE(WarehouseId)) RETURN;

    DECLARE @From DATE = (SELECT MIN(DocDate) FROM (SELECT DocDate FROM inserted UNION ALL SELECT DocDate FROM deleted) x);

    IF EXISTS (SELECT 1 FROM inv.CostRuns r
               JOIN fin.Vouchers v ON v.SourceModule = 'inventory' AND v.SourceDocType = 'inv_cost' AND v.SourceDocumentId = r.Id
               WHERE r.EndDate >= @From AND v.[Status] = 3)
        THROW 50320, N'سند بهای تمام‌شده این ماه یا ماه‌های بعد دائم شده است؛ سند انبار این تاریخ تغییر نمی‌کند.', 1;

    UPDATE inv.CostRuns SET [Status] = 2 WHERE EndDate >= @From AND [Status] = 1;
END;
GO

-- فی فاکتور خرید بهای رسید را تعیین می‌کند؛ فاکتور خرید مسدود نمی‌شود، فقط ماه‌های باز علامت می‌خورند
CREATE TRIGGER pur.trg_PurchaseInvoiceLines_CostRuns ON pur.PurchaseInvoiceLines AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @From DATE = (SELECT MIN(d.DocDate)
                          FROM (SELECT ReceiptLineId FROM inserted UNION SELECT ReceiptLineId FROM deleted) x
                          JOIN inv.StockDocLines l ON l.Id = x.ReceiptLineId
                          JOIN inv.StockDocs d ON d.Id = l.DocId);
    IF @From IS NULL RETURN;
    UPDATE inv.CostRuns SET [Status] = 2 WHERE EndDate >= @From AND [Status] = 1;
END;
GO

/* ---------------------------------------------------------------------
   ۵) الگوی سند بهای تمام‌شده ماه (معین‌ها از گروه کالا و عامل/مرکز هزینه)
   --------------------------------------------------------------------- */
INSERT fin.PostingTemplates (SourceDocType, Title, SourceModule) VALUES ('inv_cost', N'بهای تمام‌شده ماه (انبار)', 'inventory');
DECLARE @T INT = SCOPE_IDENTITY();
INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder) VALUES
    (@T, 'cogs',          N'بهای تمام‌شده کالای فروش‌رفته (معین گروه کالا)', 1, NULL, 'source', NULL,                   1),
    (@T, 'consumption',   N'هزینه مصرف کالا',                               1, NULL, 'cost',   N'عامل و مرکز هزینه ردیف', 2),
    (@T, 'inventory_in',  N'افزایش موجودی (برگشت از فروش)',                 1, NULL, 'source', NULL,                   3),
    (@T, 'inventory_out', N'کاهش موجودی (فروش و مصرف)',                     2, NULL, 'source', NULL,                   4),
    (@T, 'cogs_return',   N'برگشت بهای تمام‌شده فروش',                      2, NULL, 'source', NULL,                   5);

PRINT N'محاسبه بهای تمام‌شده انبار آماده شد.';
GO
