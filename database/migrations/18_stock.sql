/* =====================================================================
   18 - انبار: انبارها، رسید و حواله (مرحله ۱ طرح docs/inventory-design.md)

   - همیشه اول جابه‌جایی فیزیکی، بعد فاکتور: رسید خرید ← فاکتور خرید، حواله فروش ← فاکتور فروش،
     رسید برگشت از فروش ← برگشت از فروش، حواله برگشت از خرید ← برگشت از خرید.
   - هر ردیف سند انبار دقیقاً یک بار و با همان مقدار فاکتور می‌شود (ردیف فاکتور = ردیف سند انبار).
   - برگشت انباری فقط از ردیفی که فاکتور شده؛ مقدار برگشتی هر ردیف از مقدار ردیف مبدأ بیشتر نمی‌شود.
   - موجودی هیچ کالا در هیچ انبار و هیچ تاریخی منفی نمی‌شود (در یک روز رسیدها قبل از حواله‌ها).
   - رسید و حواله سند حسابداری ندارند؛ بها و سند با «محاسبه بهای تمام‌شده» ماهانه (مرحله ۲) می‌آید.
   - داده‌های آزمایشی خرید و فروش قبلی (و سندهای حسابداری‌شان) پاک می‌شوند؛ ردیف کالا بدون سند انبار دیگر مجاز نیست.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------------------------------------------------------------------
   ۰) پاک کردن فاکتورهای قبلی خرید و فروش (بدون سند انبار)
   --------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM fin.Vouchers WHERE SourceModule IN ('purchases', 'sales') AND [Status] = 3)
    THROW 50300, N'سند دائم خرید یا فروش وجود دارد؛ فاکتورهای قبلی بدون سند انبار پاک نمی‌شوند.', 1;

DELETE l FROM fin.VoucherLines l JOIN fin.Vouchers v ON v.Id = l.VoucherId WHERE v.SourceModule IN ('purchases', 'sales');
DELETE fin.Vouchers WHERE SourceModule IN ('purchases', 'sales');
DELETE sls.SalesReturns;      -- ردیف‌ها cascade
DELETE sls.SalesInvoices;
DELETE pur.PurchaseReturns;
DELETE pur.PurchaseInvoices;
GO

/* ---------------------------------------------------------------------
   ۱) انبارها
   --------------------------------------------------------------------- */
CREATE TABLE inv.Warehouses (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY,
    Code      NVARCHAR(10)  NOT NULL CONSTRAINT UQ_Warehouses_Code UNIQUE,
    Title     NVARCHAR(100) NOT NULL CONSTRAINT UQ_Warehouses_Title UNIQUE,
    KeeperId  INT           NULL CONSTRAINT FK_Warehouses_Keeper REFERENCES base.Persons (Id),   -- انباردار
    IsActive  BIT           NOT NULL CONSTRAINT DF_Warehouses_IsActive DEFAULT (1),
    Notes     NVARCHAR(500) NULL
);
INSERT inv.Warehouses (Code, Title) VALUES (N'01', N'انبار مرکزی');
GO

/* ---------------------------------------------------------------------
   ۲) رسید و حواله
   نوع: 1 موجودی اول دوره، 2 رسید خرید، 3 رسید برگشت از فروش (رسیدها زیر ۱۰)
        11 حواله فروش، 12 حواله برگشت از خرید، 13 حواله مصرف غیرتولیدی
   --------------------------------------------------------------------- */
CREATE TABLE inv.StockDocs (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockDocs PRIMARY KEY,
    DocType       TINYINT       NOT NULL,
    DocNo         INT           NOT NULL,
    DocDate       DATE          NOT NULL,
    WarehouseId   INT           NOT NULL CONSTRAINT FK_StockDocs_Warehouse REFERENCES inv.Warehouses (Id),
    PartyId       INT           NULL     CONSTRAINT FK_StockDocs_Party REFERENCES base.Persons (Id),
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_StockDocs_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt     DATETIME2(0)  NULL,
    RowVer        ROWVERSION    NOT NULL,
    CONSTRAINT UQ_StockDocs_No   UNIQUE (DocType, DocNo),
    CONSTRAINT CK_StockDocs_Type CHECK (DocType IN (1, 2, 3, 11, 12, 13)),
    -- خرید، فروش و برگشت‌ها طرف حساب دارند؛ اول دوره و مصرف ندارند
    CONSTRAINT CK_StockDocs_Party CHECK ((DocType IN (2, 3, 11, 12) AND PartyId IS NOT NULL) OR (DocType IN (1, 13) AND PartyId IS NULL))
);
CREATE INDEX IX_StockDocs_Date ON inv.StockDocs (DocDate);
CREATE INDEX IX_StockDocs_Warehouse ON inv.StockDocs (WarehouseId, DocDate);
CREATE INDEX IX_StockDocs_Party ON inv.StockDocs (PartyId, DocType) WHERE PartyId IS NOT NULL;
-- هر انبار فقط یک موجودی اول دوره
CREATE UNIQUE INDEX UX_StockDocs_Opening ON inv.StockDocs (WarehouseId) WHERE DocType = 1;
GO

CREATE TABLE inv.StockDocLines (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockDocLines PRIMARY KEY,
    DocId         BIGINT        NOT NULL CONSTRAINT FK_SDL_Doc REFERENCES inv.StockDocs (Id) ON DELETE CASCADE,
    RowNo         SMALLINT      NOT NULL,
    ItemId        INT           NOT NULL CONSTRAINT FK_SDL_Item REFERENCES inv.Items (Id),
    Quantity      DECIMAL(24,6) NOT NULL,
    SourceLineId  BIGINT        NULL     CONSTRAINT FK_SDL_Source REFERENCES inv.StockDocLines (Id),  -- برگشت: ردیف حواله فروش / رسید خرید
    CostFactorId  INT           NULL     CONSTRAINT FK_SDL_Factor REFERENCES cost.CostFactors (Id),   -- مصرف
    CostCenterId  INT           NULL     CONSTRAINT FK_SDL_Center REFERENCES cost.CostCenters (Id),   -- مصرف
    EnteredPrice  DECIMAL(24,4) NULL,   -- اول دوره: فی؛ رسید خرید: فی تقریبی تا رسیدن فاکتور
    UnitCost      DECIMAL(24,4) NULL,   -- نتیجه محاسبه بهای تمام‌شده (مرحله ۲)
    Amount        DECIMAL(24,4) NULL,
    [Description] NVARCHAR(500) NULL,
    CONSTRAINT UQ_SDL_Row    UNIQUE (DocId, RowNo),
    CONSTRAINT CK_SDL_Values CHECK (Quantity > 0 AND (EnteredPrice IS NULL OR EnteredPrice >= 0))
);
CREATE INDEX IX_SDL_Item ON inv.StockDocLines (ItemId);
CREATE INDEX IX_SDL_Source ON inv.StockDocLines (SourceLineId) WHERE SourceLineId IS NOT NULL;
GO

/* کنترل ردیف‌ها: فقط کالا، ارجاع برگشت‌ها، مرکز/عامل مصرف، فی اول دوره و موجودی منفی.
   حذف ردیف (مثلاً حذف رسید) را سرویس پیش از حذف کنترل می‌کند؛ اینجا فقط درج و اصلاح. */
CREATE TRIGGER inv.trg_StockDocLines_Check ON inv.StockDocLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
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

    -- برگشت: مبدأ حواله فروش (برای رسید برگشت از فروش) یا رسید خرید (برای حواله برگشت از خرید)، همان کالا و طرف حساب
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

    -- موجودی منفی: مانده پایان هر روز (رسیدهای هر روز قبل از حواله‌هایش)
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
   ۳) ارجاع فاکتورها به سند انبار (هر ردیف سند انبار یک بار، با همان کالا و مقدار)
   --------------------------------------------------------------------- */
ALTER TABLE pur.PurchaseInvoiceLines ADD ReceiptLineId BIGINT NULL CONSTRAINT FK_PIL_Receipt REFERENCES inv.StockDocLines (Id);
ALTER TABLE pur.PurchaseReturnLines  ADD IssueLineId   BIGINT NULL CONSTRAINT FK_PRL_Issue   REFERENCES inv.StockDocLines (Id);
ALTER TABLE sls.SalesInvoiceLines    ADD IssueLineId   BIGINT NULL CONSTRAINT FK_SIL_Issue   REFERENCES inv.StockDocLines (Id);
ALTER TABLE sls.SalesReturnLines     ADD ReceiptLineId BIGINT NULL CONSTRAINT FK_SRL_Receipt REFERENCES inv.StockDocLines (Id);
GO
CREATE UNIQUE INDEX UX_PIL_Receipt ON pur.PurchaseInvoiceLines (ReceiptLineId) WHERE ReceiptLineId IS NOT NULL;
CREATE UNIQUE INDEX UX_PRL_Issue   ON pur.PurchaseReturnLines (IssueLineId)    WHERE IssueLineId IS NOT NULL;
CREATE UNIQUE INDEX UX_SIL_Issue   ON sls.SalesInvoiceLines (IssueLineId)      WHERE IssueLineId IS NOT NULL;
CREATE UNIQUE INDEX UX_SRL_Receipt ON sls.SalesReturnLines (ReceiptLineId)     WHERE ReceiptLineId IS NOT NULL;
-- یک برگشت می‌تواند دو ردیف انباری از یک ردیف فاکتور داشته باشد
ALTER TABLE pur.PurchaseReturnLines DROP CONSTRAINT UQ_PRL_Line;
ALTER TABLE sls.SalesReturnLines    DROP CONSTRAINT UQ_SRL_Line;
GO

CREATE TRIGGER pur.trg_PurchaseInvoiceLines_Stock ON pur.PurchaseInvoiceLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               LEFT JOIN inv.StockDocLines s ON s.Id = i.ReceiptLineId
               LEFT JOIN inv.StockDocs d ON d.Id = s.DocId
               WHERE s.Id IS NULL OR d.DocType <> 2 OR s.ItemId <> i.ItemId OR s.Quantity <> i.Quantity)
        THROW 50310, N'هر ردیف فاکتور خرید باید از یک ردیف رسید خرید با همان کالا و مقدار باشد.', 1;
END;
GO

CREATE TRIGGER sls.trg_SalesInvoiceLines_Stock ON sls.SalesInvoiceLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    -- خدمت سند انبار ندارد؛ کالا باید از حواله فروش بیاید
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN inv.Items it ON it.Id = i.ItemId
               LEFT JOIN inv.StockDocLines s ON s.Id = i.IssueLineId
               LEFT JOIN inv.StockDocs d ON d.Id = s.DocId
               WHERE (it.Kind = 2 AND i.IssueLineId IS NOT NULL)
                  OR (it.Kind = 1 AND (s.Id IS NULL OR d.DocType <> 11 OR s.ItemId <> i.ItemId OR s.Quantity <> i.Quantity)))
        THROW 50311, N'هر ردیف کالای فاکتور فروش باید از یک ردیف حواله فروش با همان کالا و مقدار باشد.', 1;
END;
GO

CREATE TRIGGER pur.trg_PurchaseReturnLines_Stock ON pur.PurchaseReturnLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN pur.PurchaseInvoiceLines il ON il.Id = i.InvoiceLineId
               LEFT JOIN inv.StockDocLines s ON s.Id = i.IssueLineId
               LEFT JOIN inv.StockDocs d ON d.Id = s.DocId
               WHERE s.Id IS NULL OR d.DocType <> 12 OR s.SourceLineId <> il.ReceiptLineId OR s.Quantity <> i.Quantity)
        THROW 50312, N'هر ردیف برگشت از خرید باید از یک ردیف حواله برگشت از خرید همان رسید و با همان مقدار باشد.', 1;
END;
GO

CREATE TRIGGER sls.trg_SalesReturnLines_Stock ON sls.SalesReturnLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN sls.SalesInvoiceLines il ON il.Id = i.InvoiceLineId
               JOIN inv.Items it ON it.Id = il.ItemId
               LEFT JOIN inv.StockDocLines s ON s.Id = i.ReceiptLineId
               LEFT JOIN inv.StockDocs d ON d.Id = s.DocId
               WHERE (it.Kind = 2 AND i.ReceiptLineId IS NOT NULL)
                  OR (it.Kind = 1 AND (s.Id IS NULL OR d.DocType <> 3 OR s.SourceLineId <> il.IssueLineId OR s.Quantity <> i.Quantity)))
        THROW 50313, N'هر ردیف کالای برگشت از فروش باید از یک ردیف رسید برگشت از فروش همان حواله و با همان مقدار باشد.', 1;
END;
GO

PRINT N'انبار آماده شد.';
GO
