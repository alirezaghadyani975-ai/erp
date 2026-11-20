/* =====================================================================
   16 - کالا و خدمات (schema inv)

   - گروه کالا (درختی) معین‌های فروش، برگشت از فروش و خرید/موجودی را تعیین می‌کند؛ کالا معین ندارد
     تا تغییر حساب یک دسته کالا یک‌جا انجام شود. گروه خدمات معین خرید ندارد (خرید خدمت = شناسایی هزینه).
   - کالا: کد یکتا، نوع (کالا/خدمت، همان نوع گروه)، واحد اصلی، شناسه کالا/خدمت سامانه مودیان (۱۳ رقم)،
     مشمول یا معاف ارزش افزوده و قیمت پیش‌فرض فروش. همین جدول بعداً کالای انبار می‌شود.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE SCHEMA inv;
GO

CREATE TABLE inv.ItemGroups (
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemGroups PRIMARY KEY,
    ParentId             INT           NULL CONSTRAINT FK_ItemGroups_Parent REFERENCES inv.ItemGroups (Id),
    Title                NVARCHAR(100) NOT NULL,
    Kind                 TINYINT       NOT NULL,                                                  -- 1 کالا، 2 خدمت
    SalesAccountId       INT           NOT NULL CONSTRAINT FK_ItemGroups_Sales    REFERENCES fin.Accounts (Id),
    SalesReturnAccountId INT           NOT NULL CONSTRAINT FK_ItemGroups_Return   REFERENCES fin.Accounts (Id),
    PurchaseAccountId    INT           NULL     CONSTRAINT FK_ItemGroups_Purchase REFERENCES fin.Accounts (Id),
    IsActive             BIT           NOT NULL CONSTRAINT DF_ItemGroups_IsActive DEFAULT (1),
    CONSTRAINT UQ_ItemGroups_Title UNIQUE (Title),
    CONSTRAINT CK_ItemGroups_Kind  CHECK (Kind IN (1, 2)),
    -- خدمت معین خرید ندارد؛ کالا باید داشته باشد
    CONSTRAINT CK_ItemGroups_Purchase CHECK ((Kind = 1 AND PurchaseAccountId IS NOT NULL) OR (Kind = 2 AND PurchaseAccountId IS NULL))
);
GO

-- معین‌های گروه باید معین (سطح ۳) و فعال باشند
CREATE TRIGGER inv.trg_ItemGroups_Accounts ON inv.ItemGroups AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN fin.Accounts a ON a.Id IN (i.SalesAccountId, i.SalesReturnAccountId, i.PurchaseAccountId)
               WHERE a.[Level] <> 3 OR a.IsActive = 0)
        THROW 50200, N'معین‌های گروه کالا باید معین (سطح ۳) و فعال باشند.', 1;
END;
GO

CREATE TABLE inv.Items (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Items PRIMARY KEY,
    Code          NVARCHAR(30)  NOT NULL CONSTRAINT UQ_Items_Code UNIQUE,
    Title         NVARCHAR(200) NOT NULL,
    Kind          TINYINT       NOT NULL,                                               -- 1 کالا، 2 خدمت
    GroupId       INT           NOT NULL CONSTRAINT FK_Items_Group REFERENCES inv.ItemGroups (Id),
    UnitId        SMALLINT      NOT NULL CONSTRAINT FK_Items_Unit  REFERENCES base.Units (Id),
    TaxItemId     VARCHAR(13)   NULL,                                                   -- شناسه کالا/خدمت سامانه مودیان
    VatExempt     BIT           NOT NULL CONSTRAINT DF_Items_VatExempt DEFAULT (0),
    Barcode       VARCHAR(50)   NULL,
    SalesPrice    DECIMAL(24,4) NULL,
    IsActive      BIT           NOT NULL CONSTRAINT DF_Items_IsActive DEFAULT (1),
    Notes         NVARCHAR(500) NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_Items_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Items_Kind      CHECK (Kind IN (1, 2)),
    CONSTRAINT CK_Items_TaxItemId CHECK (TaxItemId IS NULL OR (LEN(TaxItemId) = 13 AND TaxItemId NOT LIKE '%[^0-9]%')),
    CONSTRAINT CK_Items_Price     CHECK (SalesPrice IS NULL OR SalesPrice >= 0)
);
CREATE INDEX IX_Items_Group ON inv.Items (GroupId);
CREATE UNIQUE INDEX UX_Items_Barcode ON inv.Items (Barcode) WHERE Barcode IS NOT NULL;
GO

-- نوع کالا همان نوع گروهش است
CREATE TRIGGER inv.trg_Items_Kind ON inv.Items AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN inv.ItemGroups g ON g.Id = i.GroupId WHERE g.Kind <> i.Kind)
        THROW 50201, N'نوع کالا/خدمت با نوع گروهش یکی نیست.', 1;
END;
GO

CREATE TRIGGER inv.trg_ItemGroups_Kind ON inv.ItemGroups AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted g JOIN inv.Items i ON i.GroupId = g.Id WHERE i.Kind <> g.Kind)
        THROW 50202, N'گروهی که کالا دارد نوعش عوض نمی‌شود.', 1;
END;
GO

/* ---------------------------------------------------------------------
   گروه‌های پیش‌فرض
   --------------------------------------------------------------------- */
DECLARE @A TABLE (Code VARCHAR(10) PRIMARY KEY, Id INT);
INSERT @A (Code, Id)
SELECT Code, Id FROM fin.Accounts WHERE [Level] = 3 AND Code IN ('6001','6002','6003','6101','6201','6202','2201','2203','2206');
IF (SELECT COUNT(*) FROM @A) <> 9
    THROW 50203, N'معین‌های پیش‌فرض گروه‌های کالا (6001، 6002، 6003، 6101، 6201، 6202، 2201، 2203، 2206) پیدا نشد.', 1;

INSERT inv.ItemGroups (Title, Kind, SalesAccountId, SalesReturnAccountId, PurchaseAccountId)
SELECT g.Title, g.Kind, s.Id, r.Id, p.Id
FROM (VALUES
        (N'کالای بازرگانی',          1, '6003', '6202', '2206'),
        (N'محصولات',                 1, '6001', '6201', '2203'),
        (N'مواد اولیه و قطعات',      1, '6002', '6002', '2201'),   -- برگشت از فروش مواد معین جدا ندارد
        (N'خدمات',                   2, '6101', '6101', NULL)
     ) g (Title, Kind, SalesCode, ReturnCode, PurchaseCode)
JOIN @A s ON s.Code = g.SalesCode
JOIN @A r ON r.Code = g.ReturnCode
LEFT JOIN @A p ON p.Code = g.PurchaseCode;

PRINT N'کالا و خدمات آماده شد.';
GO
