/* =====================================================================
   17 - خرید و فروش (schema pur و sls)

   - فاکتور خرید داخلی کالا و برگشت از خرید؛ فاکتور فروش و برگشت از فروش. همه با سند خودکار روی موتور سند.
   - ردیف: کالا، مقدار، فی، تخفیف ردیف، مبلغ (= مقدار × فی − تخفیف، به ریال گرد)، ارزش افزوده ردیف.
     سربرگ جمع‌ها را نگه می‌دارد (جمع ردیف‌ها).
   - برگشت همیشه به فاکتور اصلی و ردیف آن ارجاع دارد؛ مقدار برگشتی هر ردیف از مقدار فاکتور بیشتر نمی‌شود.
     ردیف فاکتوری که برگشت دارد حذف نمی‌شود (پس فاکتور دارای برگشت اصلاح و حذف نمی‌شود).
   - فاکتور فروش: نوع صورتحساب و روش تسویه برای سامانه مودیان؛ TaxStatus برای ارسال (اسکریپت بعدی).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE SCHEMA pur;
GO
CREATE SCHEMA sls;
GO

/* ---------------------------------------------------------------------
   ۱) خرید
   --------------------------------------------------------------------- */
CREATE TABLE pur.PurchaseInvoices (
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseInvoices PRIMARY KEY,
    DocNo          INT           NOT NULL CONSTRAINT UQ_PurchaseInvoices_No UNIQUE,
    DocDate        DATE          NOT NULL,
    SupplierId     INT           NOT NULL CONSTRAINT FK_PurchaseInvoices_Supplier REFERENCES base.Persons (Id),
    PayableKind    TINYINT       NOT NULL CONSTRAINT DF_PurchaseInvoices_Payable DEFAULT (1),  -- 1 تجاری، 2 سایر
    InvoiceNo      NVARCHAR(50)  NULL,                                                         -- شماره فاکتور فروشنده
    InvoiceDate    DATE          NULL,
    SellerTaxId    VARCHAR(22)   NULL,                                                         -- شماره مالیاتی صورتحساب فروشنده
    GrossAmount    DECIMAL(24,4) NOT NULL,
    DiscountAmount DECIMAL(24,4) NOT NULL,
    VatAmount      DECIMAL(24,4) NOT NULL,
    [Description]  NVARCHAR(500) NULL,
    CreatedBy      INT           NOT NULL,
    CreatedAt      DATETIME2(0)  NOT NULL CONSTRAINT DF_PurchaseInvoices_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt      DATETIME2(0)  NULL,
    RowVer         ROWVERSION    NOT NULL,
    CONSTRAINT CK_PurchaseInvoices_Payable CHECK (PayableKind IN (1, 2)),
    CONSTRAINT CK_PurchaseInvoices_Amounts CHECK (GrossAmount >= 0 AND DiscountAmount >= 0 AND VatAmount >= 0)
);
CREATE INDEX IX_PurchaseInvoices_Date ON pur.PurchaseInvoices (DocDate);
CREATE INDEX IX_PurchaseInvoices_Supplier ON pur.PurchaseInvoices (SupplierId, DocDate);
CREATE UNIQUE INDEX UX_PurchaseInvoices_SupplierInvoice ON pur.PurchaseInvoices (SupplierId, InvoiceNo) WHERE InvoiceNo IS NOT NULL;
GO

CREATE TABLE pur.PurchaseInvoiceLines (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseInvoiceLines PRIMARY KEY,
    InvoiceId     BIGINT        NOT NULL CONSTRAINT FK_PIL_Invoice REFERENCES pur.PurchaseInvoices (Id) ON DELETE CASCADE,
    RowNo         SMALLINT      NOT NULL,
    ItemId        INT           NOT NULL CONSTRAINT FK_PIL_Item REFERENCES inv.Items (Id),
    Quantity      DECIMAL(24,6) NOT NULL,
    UnitPrice     DECIMAL(24,4) NOT NULL,
    Discount      DECIMAL(24,4) NOT NULL,
    Amount        DECIMAL(24,4) NOT NULL,   -- پس از تخفیف
    VatAmount     DECIMAL(24,4) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    CONSTRAINT UQ_PIL_Row     UNIQUE (InvoiceId, RowNo),
    CONSTRAINT CK_PIL_Values  CHECK (Quantity > 0 AND UnitPrice >= 0 AND Discount >= 0 AND Amount >= 0 AND VatAmount >= 0)
);
CREATE INDEX IX_PIL_Item ON pur.PurchaseInvoiceLines (ItemId);
GO

CREATE TABLE pur.PurchaseReturns (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseReturns PRIMARY KEY,
    DocNo         INT           NOT NULL CONSTRAINT UQ_PurchaseReturns_No UNIQUE,
    DocDate       DATE          NOT NULL,
    InvoiceId     BIGINT        NOT NULL CONSTRAINT FK_PurchaseReturns_Invoice REFERENCES pur.PurchaseInvoices (Id),
    Amount        DECIMAL(24,4) NOT NULL,
    VatAmount     DECIMAL(24,4) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_PurchaseReturns_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt     DATETIME2(0)  NULL,
    RowVer        ROWVERSION    NOT NULL
);
CREATE INDEX IX_PurchaseReturns_Invoice ON pur.PurchaseReturns (InvoiceId);
CREATE INDEX IX_PurchaseReturns_Date ON pur.PurchaseReturns (DocDate);
GO

CREATE TABLE pur.PurchaseReturnLines (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseReturnLines PRIMARY KEY,
    ReturnId      BIGINT        NOT NULL CONSTRAINT FK_PRL_Return REFERENCES pur.PurchaseReturns (Id) ON DELETE CASCADE,
    RowNo         SMALLINT      NOT NULL,
    InvoiceLineId BIGINT        NOT NULL CONSTRAINT FK_PRL_InvoiceLine REFERENCES pur.PurchaseInvoiceLines (Id),
    Quantity      DECIMAL(24,6) NOT NULL,
    Amount        DECIMAL(24,4) NOT NULL,
    VatAmount     DECIMAL(24,4) NOT NULL,
    CONSTRAINT UQ_PRL_Row    UNIQUE (ReturnId, RowNo),
    CONSTRAINT UQ_PRL_Line   UNIQUE (ReturnId, InvoiceLineId),
    CONSTRAINT CK_PRL_Values CHECK (Quantity > 0 AND Amount >= 0 AND VatAmount >= 0)
);
CREATE INDEX IX_PRL_InvoiceLine ON pur.PurchaseReturnLines (InvoiceLineId);
GO

CREATE TRIGGER pur.trg_PurchaseReturnLines_Quantity ON pur.PurchaseReturnLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1
               FROM (SELECT DISTINCT InvoiceLineId FROM inserted) i
               JOIN pur.PurchaseInvoiceLines l ON l.Id = i.InvoiceLineId
               WHERE (SELECT SUM(r.Quantity) FROM pur.PurchaseReturnLines r WHERE r.InvoiceLineId = i.InvoiceLineId) > l.Quantity)
        THROW 50210, N'مقدار برگشتی از مقدار ردیف فاکتور خرید بیشتر است.', 1;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN pur.PurchaseReturns r ON r.Id = i.ReturnId
               JOIN pur.PurchaseInvoiceLines l ON l.Id = i.InvoiceLineId
               WHERE l.InvoiceId <> r.InvoiceId)
        THROW 50211, N'ردیف برگشت از فاکتور دیگری است.', 1;
END;
GO

/* ---------------------------------------------------------------------
   ۲) فروش
   --------------------------------------------------------------------- */
CREATE TABLE sls.SalesInvoices (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoices PRIMARY KEY,
    DocNo            INT           NOT NULL CONSTRAINT UQ_SalesInvoices_No UNIQUE,
    DocDate          DATE          NOT NULL,
    CustomerId       INT           NOT NULL CONSTRAINT FK_SalesInvoices_Customer REFERENCES base.Persons (Id),
    InvoiceType      TINYINT       NOT NULL CONSTRAINT DF_SalesInvoices_Type DEFAULT (1),        -- مودیان: 1 نوع اول، 2 نوع دوم
    SettlementMethod TINYINT       NOT NULL CONSTRAINT DF_SalesInvoices_Settle DEFAULT (2),      -- 1 نقد، 2 نسیه، 3 نقد و نسیه
    CashAmount       DECIMAL(24,4) NULL,                                                         -- سهم نقد در «نقد و نسیه»
    ProjectDetailId  INT           NULL CONSTRAINT FK_SalesInvoices_Project REFERENCES fin.DetailAccounts (Id),
    GrossAmount      DECIMAL(24,4) NOT NULL,
    DiscountAmount   DECIMAL(24,4) NOT NULL,
    VatAmount        DECIMAL(24,4) NOT NULL,
    TaxStatus        TINYINT       NOT NULL CONSTRAINT DF_SalesInvoices_TaxStatus DEFAULT (0),   -- 0 ارسال‌نشده (مودیان)
    [Description]    NVARCHAR(500) NULL,
    CreatedBy        INT           NOT NULL,
    CreatedAt        DATETIME2(0)  NOT NULL CONSTRAINT DF_SalesInvoices_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt        DATETIME2(0)  NULL,
    RowVer           ROWVERSION    NOT NULL,
    CONSTRAINT CK_SalesInvoices_Type    CHECK (InvoiceType IN (1, 2)),
    CONSTRAINT CK_SalesInvoices_Settle  CHECK (SettlementMethod IN (1, 2, 3)),
    CONSTRAINT CK_SalesInvoices_Cash    CHECK ((SettlementMethod = 3 AND CashAmount > 0) OR (SettlementMethod <> 3 AND CashAmount IS NULL)),
    CONSTRAINT CK_SalesInvoices_Amounts CHECK (GrossAmount >= 0 AND DiscountAmount >= 0 AND VatAmount >= 0)
);
CREATE INDEX IX_SalesInvoices_Date ON sls.SalesInvoices (DocDate);
CREATE INDEX IX_SalesInvoices_Customer ON sls.SalesInvoices (CustomerId, DocDate);
GO

CREATE TABLE sls.SalesInvoiceLines (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesInvoiceLines PRIMARY KEY,
    InvoiceId     BIGINT        NOT NULL CONSTRAINT FK_SIL_Invoice REFERENCES sls.SalesInvoices (Id) ON DELETE CASCADE,
    RowNo         SMALLINT      NOT NULL,
    ItemId        INT           NOT NULL CONSTRAINT FK_SIL_Item REFERENCES inv.Items (Id),
    Quantity      DECIMAL(24,6) NOT NULL,
    UnitPrice     DECIMAL(24,4) NOT NULL,
    Discount      DECIMAL(24,4) NOT NULL,
    Amount        DECIMAL(24,4) NOT NULL,   -- پس از تخفیف
    VatAmount     DECIMAL(24,4) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    CONSTRAINT UQ_SIL_Row    UNIQUE (InvoiceId, RowNo),
    CONSTRAINT CK_SIL_Values CHECK (Quantity > 0 AND UnitPrice >= 0 AND Discount >= 0 AND Amount >= 0 AND VatAmount >= 0)
);
CREATE INDEX IX_SIL_Item ON sls.SalesInvoiceLines (ItemId);
GO

CREATE TABLE sls.SalesReturns (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesReturns PRIMARY KEY,
    DocNo         INT           NOT NULL CONSTRAINT UQ_SalesReturns_No UNIQUE,
    DocDate       DATE          NOT NULL,
    InvoiceId     BIGINT        NOT NULL CONSTRAINT FK_SalesReturns_Invoice REFERENCES sls.SalesInvoices (Id),
    GrossAmount   DECIMAL(24,4) NOT NULL,
    DiscountAmount DECIMAL(24,4) NOT NULL,
    VatAmount     DECIMAL(24,4) NOT NULL,
    TaxStatus     TINYINT       NOT NULL CONSTRAINT DF_SalesReturns_TaxStatus DEFAULT (0),
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_SalesReturns_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt     DATETIME2(0)  NULL,
    RowVer        ROWVERSION    NOT NULL
);
CREATE INDEX IX_SalesReturns_Invoice ON sls.SalesReturns (InvoiceId);
CREATE INDEX IX_SalesReturns_Date ON sls.SalesReturns (DocDate);
GO

CREATE TABLE sls.SalesReturnLines (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesReturnLines PRIMARY KEY,
    ReturnId      BIGINT        NOT NULL CONSTRAINT FK_SRL_Return REFERENCES sls.SalesReturns (Id) ON DELETE CASCADE,
    RowNo         SMALLINT      NOT NULL,
    InvoiceLineId BIGINT        NOT NULL CONSTRAINT FK_SRL_InvoiceLine REFERENCES sls.SalesInvoiceLines (Id),
    Quantity      DECIMAL(24,6) NOT NULL,
    GrossAmount   DECIMAL(24,4) NOT NULL,
    Discount      DECIMAL(24,4) NOT NULL,
    Amount        DECIMAL(24,4) NOT NULL,
    VatAmount     DECIMAL(24,4) NOT NULL,
    CONSTRAINT UQ_SRL_Row    UNIQUE (ReturnId, RowNo),
    CONSTRAINT UQ_SRL_Line   UNIQUE (ReturnId, InvoiceLineId),
    CONSTRAINT CK_SRL_Values CHECK (Quantity > 0 AND GrossAmount >= 0 AND Discount >= 0 AND Amount >= 0 AND VatAmount >= 0)
);
CREATE INDEX IX_SRL_InvoiceLine ON sls.SalesReturnLines (InvoiceLineId);
GO

CREATE TRIGGER sls.trg_SalesReturnLines_Quantity ON sls.SalesReturnLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1
               FROM (SELECT DISTINCT InvoiceLineId FROM inserted) i
               JOIN sls.SalesInvoiceLines l ON l.Id = i.InvoiceLineId
               WHERE (SELECT SUM(r.Quantity) FROM sls.SalesReturnLines r WHERE r.InvoiceLineId = i.InvoiceLineId) > l.Quantity)
        THROW 50220, N'مقدار برگشتی از مقدار ردیف فاکتور فروش بیشتر است.', 1;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN sls.SalesReturns r ON r.Id = i.ReturnId
               JOIN sls.SalesInvoiceLines l ON l.Id = i.InvoiceLineId
               WHERE l.InvoiceId <> r.InvoiceId)
        THROW 50221, N'ردیف برگشت از فاکتور دیگری است.', 1;
END;
GO

/* ---------------------------------------------------------------------
   ۳) الگوهای صدور سند
   معین ردیف‌های کالا (موجودی، فروش، برگشت) از گروه کالا می‌آید (قاعده source).
   --------------------------------------------------------------------- */
DECLARE @VatIn INT  = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '2541');
DECLARE @VatOut INT = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '5141');
DECLARE @Trade INT  = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '5011');
DECLARE @Other INT  = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '5111');
DECLARE @Recv INT   = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '2411');
DECLARE @Disc INT   = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '6301');
IF @VatIn IS NULL OR @VatOut IS NULL OR @Trade IS NULL OR @Other IS NULL OR @Recv IS NULL OR @Disc IS NULL
    THROW 50230, N'معین‌های پیش‌فرض الگوهای خرید و فروش (2541، 5141، 5011، 5111، 2411، 6301) پیدا نشد.', 1;

DECLARE @T INT;

INSERT fin.PostingTemplates (SourceDocType, Title, SourceModule) VALUES ('pur_invoice', N'فاکتور خرید', 'purchases');
SET @T = SCOPE_IDENTITY();
INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder) VALUES
    (@T, 'inventory',     N'خرید کالا (معین خرید گروه کالا)', 1, NULL,   'source', NULL,             1),
    (@T, 'vat',           N'مالیات و عوارض ارزش افزوده',      1, @VatIn, NULL,     NULL,             2),
    (@T, 'payable_trade', N'بستانکار: پرداختنی تجاری',        2, @Trade, NULL,     N'تأمین‌کننده',   3),
    (@T, 'payable_other', N'بستانکار: سایر پرداختنی‌ها',       2, @Other, NULL,     N'تأمین‌کننده',   4);

INSERT fin.PostingTemplates (SourceDocType, Title, SourceModule) VALUES ('pur_return', N'برگشت از خرید', 'purchases');
SET @T = SCOPE_IDENTITY();
INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder) VALUES
    (@T, 'payable_trade', N'بدهکار: پرداختنی تجاری',          1, @Trade, NULL,     N'تأمین‌کننده',   1),
    (@T, 'payable_other', N'بدهکار: سایر پرداختنی‌ها',         1, @Other, NULL,     N'تأمین‌کننده',   2),
    (@T, 'inventory',     N'برگشت کالا (معین خرید گروه کالا)', 2, NULL,   'source', NULL,             3),
    (@T, 'vat',           N'مالیات و عوارض ارزش افزوده',      2, @VatIn, NULL,     NULL,             4);

INSERT fin.PostingTemplates (SourceDocType, Title, SourceModule) VALUES ('sls_invoice', N'فاکتور فروش', 'sales');
SET @T = SCOPE_IDENTITY();
INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder) VALUES
    (@T, 'receivable', N'بدهکار: دریافتنی تجاری',             1, @Recv,   NULL,     N'مشتری',          1),
    (@T, 'discount',   N'تخفیف فروش',                         1, @Disc,   NULL,     NULL,              2),
    (@T, 'revenue',    N'فروش (معین فروش گروه کالا)',         2, NULL,    'source', N'پروژه فاکتور',   3),
    (@T, 'vat',        N'مالیات و عوارض ارزش افزوده',         2, @VatOut, NULL,     NULL,              4);

INSERT fin.PostingTemplates (SourceDocType, Title, SourceModule) VALUES ('sls_return', N'برگشت از فروش', 'sales');
SET @T = SCOPE_IDENTITY();
INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder) VALUES
    (@T, 'return',     N'برگشت از فروش (معین برگشت گروه کالا)', 1, NULL,  'source', N'پروژه فاکتور',   1),
    (@T, 'vat',        N'مالیات و عوارض ارزش افزوده',         1, @VatOut, NULL,     NULL,              2),
    (@T, 'discount',   N'تخفیف فروش',                         2, @Disc,   NULL,     NULL,              3),
    (@T, 'receivable', N'بستانکار: دریافتنی تجاری',           2, @Recv,   NULL,     N'مشتری',          4);

PRINT N'خرید و فروش آماده شد.';
GO
