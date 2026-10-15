/* =====================================================================
   10 - موتور صدور سند خودکار و فرم شناسایی هزینه

   - الگوی سند (fin.PostingTemplates / PostingTemplateLines): هر نوع سند منبع یک الگو؛ هر ردیف الگو یک کلید
     (مثل expense، vat، payable_trade) با طرف بدهکار/بستانکار و معین ثابت، یا قاعده تعیین معین
     (cost: معین هزینه از روی گروه عامل و وظیفه مرکز). تفصیلی‌ها از داده‌های سند منبع و قاعده تفصیلی معین
     خودکار پر می‌شوند.
   - هر سند منبع حداکثر یک سند حسابداری زنده (غیرباطل) دارد.
   - schema exp: شناسایی هزینه (فاکتور خدماتی) با ردیف‌های عامل + مرکز هزینه.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------------------------------------------------------------------
   ۱) الگوی سند
   --------------------------------------------------------------------- */
ALTER TABLE fin.PostingTemplates ADD SourceModule VARCHAR(30) NULL;
GO

ALTER TABLE fin.PostingTemplateLines DROP CONSTRAINT FK_PTL_Account;
ALTER TABLE fin.PostingTemplateLines ALTER COLUMN AccountId INT NULL;
ALTER TABLE fin.PostingTemplateLines ADD CONSTRAINT FK_PTL_Account FOREIGN KEY (AccountId) REFERENCES fin.Accounts (Id);
ALTER TABLE fin.PostingTemplateLines ADD
    Title       NVARCHAR(100) NOT NULL CONSTRAINT DF_PTL_Title DEFAULT (N''),
    Side        TINYINT       NOT NULL CONSTRAINT DF_PTL_Side  DEFAULT (1),   -- 1 بدهکار، 2 بستانکار
    AccountRule VARCHAR(30)   NULL,                                          -- cost: معین هزینه از عامل و مرکز
    DetailHint  NVARCHAR(200) NULL,                                          -- تفصیلی‌هایی که سند منبع می‌دهد
    SortOrder   TINYINT       NOT NULL CONSTRAINT DF_PTL_Sort  DEFAULT (0);
GO

ALTER TABLE fin.PostingTemplateLines ADD
    CONSTRAINT CK_PTL_Side    CHECK (Side IN (1, 2)),
    CONSTRAINT CK_PTL_Rule    CHECK (AccountRule IN ('cost')),
    CONSTRAINT CK_PTL_Account CHECK ((AccountId IS NULL AND AccountRule IS NOT NULL) OR (AccountId IS NOT NULL AND AccountRule IS NULL));

-- هر سند منبع فقط یک سند حسابداری زنده
CREATE UNIQUE INDEX UX_Vouchers_LiveSource ON fin.Vouchers (SourceModule, SourceDocType, SourceDocumentId)
    WHERE SourceDocumentId IS NOT NULL AND [Status] <> 9;
GO

/* ---------------------------------------------------------------------
   ۲) شناسایی هزینه
   --------------------------------------------------------------------- */
CREATE SCHEMA exp;
GO

CREATE TABLE exp.ExpenseInvoices (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpenseInvoices PRIMARY KEY,
    DocNo         INT           NOT NULL CONSTRAINT UQ_ExpenseInvoices_No UNIQUE,
    DocDate       DATE          NOT NULL,
    SupplierId    INT           NOT NULL CONSTRAINT FK_ExpenseInvoices_Supplier REFERENCES base.Persons (Id),
    PayableKind   TINYINT       NOT NULL CONSTRAINT DF_ExpenseInvoices_Payable DEFAULT (1),  -- 1 تجاری، 2 سایر
    InvoiceNo     NVARCHAR(50)  NULL,                                                         -- شماره فاکتور فروشنده
    InvoiceDate   DATE          NULL,
    VatAmount     DECIMAL(24,4) NOT NULL CONSTRAINT DF_ExpenseInvoices_Vat DEFAULT (0),
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_ExpenseInvoices_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt     DATETIME2(0)  NULL,
    RowVer        ROWVERSION    NOT NULL,
    CONSTRAINT CK_ExpenseInvoices_Payable CHECK (PayableKind IN (1, 2)),
    CONSTRAINT CK_ExpenseInvoices_Vat     CHECK (VatAmount >= 0)
);
CREATE INDEX IX_ExpenseInvoices_Date ON exp.ExpenseInvoices (DocDate);
CREATE INDEX IX_ExpenseInvoices_Supplier ON exp.ExpenseInvoices (SupplierId, DocDate);
-- یک فاکتور فروشنده دو بار ثبت نشود
CREATE UNIQUE INDEX UX_ExpenseInvoices_SupplierInvoice ON exp.ExpenseInvoices (SupplierId, InvoiceNo) WHERE InvoiceNo IS NOT NULL;
GO

CREATE TABLE exp.ExpenseInvoiceLines (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpenseInvoiceLines PRIMARY KEY,
    InvoiceId     BIGINT        NOT NULL CONSTRAINT FK_EIL_Invoice REFERENCES exp.ExpenseInvoices (Id) ON DELETE CASCADE,
    RowNo         SMALLINT      NOT NULL,
    CostFactorId  INT           NOT NULL CONSTRAINT FK_EIL_Factor REFERENCES cost.CostFactors (Id),
    CostCenterId  INT           NULL     CONSTRAINT FK_EIL_Center REFERENCES cost.CostCenters (Id),  -- هزینه مالی مرکز ندارد
    Amount        DECIMAL(24,4) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    CONSTRAINT UQ_EIL_Row    UNIQUE (InvoiceId, RowNo),
    CONSTRAINT CK_EIL_Amount CHECK (Amount > 0)
);
CREATE INDEX IX_EIL_Factor ON exp.ExpenseInvoiceLines (CostFactorId);
CREATE INDEX IX_EIL_Center ON exp.ExpenseInvoiceLines (CostCenterId);
GO

/* ---------------------------------------------------------------------
   ۳) الگوی سند شناسایی هزینه
   --------------------------------------------------------------------- */
DECLARE @Vat INT = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '2541');
DECLARE @Trade INT = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '5011');
DECLARE @Other INT = (SELECT Id FROM fin.Accounts WHERE [Level] = 3 AND Code = '5111');
IF @Vat IS NULL OR @Trade IS NULL OR @Other IS NULL
    THROW 50100, N'معین‌های پیش‌فرض الگوی شناسایی هزینه (2541، 5011، 5111) پیدا نشد.', 1;

INSERT fin.PostingTemplates (SourceDocType, Title, SourceModule) VALUES ('expense_invoice', N'شناسایی هزینه (فاکتور خدماتی)', 'expenses');
DECLARE @Template INT = SCOPE_IDENTITY();

INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder) VALUES
    (@Template, 'expense',       N'هزینه',                        1, NULL,   'cost', N'عامل و مرکز هزینه ردیف', 1),
    (@Template, 'vat',           N'مالیات و عوارض ارزش افزوده',   1, @Vat,   NULL,   NULL,                      2),
    (@Template, 'payable_trade', N'بستانکار: پرداختنی تجاری',     2, @Trade, NULL,   N'تأمین‌کننده',            3),
    (@Template, 'payable_other', N'بستانکار: سایر پرداختنی‌ها',    2, @Other, NULL,   N'تأمین‌کننده',            4);

PRINT N'موتور صدور سند و شناسایی هزینه آماده شد.';
GO
