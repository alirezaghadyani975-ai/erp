/* =====================================================================
   12 - ارزش افزوده در ردیف، ردیف‌های غیرهزینه‌ای تنخواه، دسته‌چک، دفتر سفته و ضمانت‌نامه بانکی

   - ارزش افزوده از سربرگ به ردیف می‌رود (یک فرم می‌تواند ردیف مشمول و معاف با هم داشته باشد).
     ستون سربرگ VatAmount می‌ماند و جمع ردیف‌هاست. مبلغ قبلی سربرگ به ردیف اول منتقل می‌شود.
   - ردیف صورت‌هزینه تنخواه دو نوع دارد: 1 هزینه (عامل + مرکز)، 2 پرداخت به طرف حساب (نوع پرداخت + شخص)،
     مثلاً پرداخت بدهی یا مساعده از تنخواه.
   - اسناد پرداختنی از قبل تعریف می‌شوند: دسته‌چک (حساب بانکی + بازه سریال) و دفتر سفته (بازه شماره) با برگ‌هایشان،
     و ضمانت‌نامه بانکی صادرشده برای شرکت. چک صادره و سند تضمینی پرداختی فقط از این‌ها انتخاب می‌شوند.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------------------------------------------------------------------
   ۱) ارزش افزوده ردیفی
   --------------------------------------------------------------------- */
ALTER TABLE exp.ExpenseInvoiceLines ADD VatAmount DECIMAL(24,4) NOT NULL CONSTRAINT DF_EIL_Vat DEFAULT (0)
    CONSTRAINT CK_EIL_Vat CHECK (VatAmount >= 0);
ALTER TABLE trs.ImprestSettlementLines ADD VatAmount DECIMAL(24,4) NOT NULL CONSTRAINT DF_ISL_Vat DEFAULT (0)
    CONSTRAINT CK_ISL_Vat CHECK (VatAmount >= 0);
GO

UPDATE l SET VatAmount = i.VatAmount
FROM exp.ExpenseInvoiceLines l
JOIN exp.ExpenseInvoices i ON i.Id = l.InvoiceId
WHERE i.VatAmount > 0 AND l.RowNo = (SELECT MIN(x.RowNo) FROM exp.ExpenseInvoiceLines x WHERE x.InvoiceId = l.InvoiceId);

UPDATE l SET VatAmount = s.VatAmount
FROM trs.ImprestSettlementLines l
JOIN trs.ImprestSettlements s ON s.Id = l.SettlementId
WHERE s.VatAmount > 0 AND l.RowNo = (SELECT MIN(x.RowNo) FROM trs.ImprestSettlementLines x WHERE x.SettlementId = l.SettlementId);
GO

/* ---------------------------------------------------------------------
   ۲) ردیف دو نوعی صورت‌هزینه تنخواه
   --------------------------------------------------------------------- */
ALTER TABLE trs.ImprestSettlementLines ALTER COLUMN CostFactorId INT NULL;
ALTER TABLE trs.ImprestSettlementLines ADD
    LineKind  TINYINT  NOT NULL CONSTRAINT DF_ISL_Kind DEFAULT (1),
    TypeId    SMALLINT NULL CONSTRAINT FK_ISL_Type REFERENCES trs.TransactionTypes (Id),
    PersonId  INT      NULL CONSTRAINT FK_ISL_Person REFERENCES base.Persons (Id),
    AccountId INT      NULL CONSTRAINT FK_ISL_Account REFERENCES fin.Accounts (Id);
GO
ALTER TABLE trs.ImprestSettlementLines ADD CONSTRAINT CK_ISL_Kind CHECK (
    (LineKind = 1 AND CostFactorId IS NOT NULL AND TypeId IS NULL AND PersonId IS NULL AND AccountId IS NULL)
    OR (LineKind = 2 AND CostFactorId IS NULL AND CostCenterId IS NULL AND TypeId IS NOT NULL AND PersonId IS NOT NULL
        AND AccountId IS NOT NULL AND VatAmount = 0));
GO

-- ردیف «پرداخت به طرف حساب» در الگوی صورت‌هزینه تنخواه: معین از نوع پرداخت
INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder)
SELECT t.Id, 'counter', N'پرداخت به طرف حساب', 1, NULL, 'source', N'شخص (معین از نوع پرداخت)', 4
FROM fin.PostingTemplates t
WHERE t.SourceDocType = 'trs_imprest'
  AND NOT EXISTS (SELECT 1 FROM fin.PostingTemplateLines x WHERE x.TemplateId = t.Id AND x.LineKey = 'counter');
GO

/* ---------------------------------------------------------------------
   ۳) دسته‌چک و دفتر سفته
   --------------------------------------------------------------------- */
-- نوع: 1 دسته‌چک، 2 دفتر سفته. برگ‌ها: 1 سفید (قابل استفاده)، 2 استفاده‌شده، 3 باطل
CREATE TABLE trs.InstrumentBooks (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InstrumentBooks PRIMARY KEY,
    Kind          TINYINT       NOT NULL,
    BankAccountId INT           NULL CONSTRAINT FK_IB_Bank REFERENCES trs.BankAccounts (Id),
    Title         NVARCHAR(100) NOT NULL,
    Series        NVARCHAR(20)  NULL,
    FromNo        BIGINT        NOT NULL,
    ToNo          BIGINT        NOT NULL,
    Digits        TINYINT       NOT NULL CONSTRAINT DF_IB_Digits DEFAULT (0),
    ReceivedDate  DATE          NOT NULL,
    MaxAmount     DECIMAL(24,4) NULL,   -- سقف مبلغ سفته
    IsActive      BIT           NOT NULL CONSTRAINT DF_IB_Active DEFAULT (1),
    Notes         NVARCHAR(500) NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_IB_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_IB_Kind  CHECK (Kind IN (1, 2)),
    CONSTRAINT CK_IB_Bank  CHECK (Kind <> 1 OR BankAccountId IS NOT NULL),
    CONSTRAINT CK_IB_Range CHECK (FromNo > 0 AND ToNo >= FromNo AND ToNo - FromNo < 1000)
);
GO

CREATE TABLE trs.InstrumentLeaves (
    Id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_InstrumentLeaves PRIMARY KEY,
    BookId     INT           NOT NULL CONSTRAINT FK_IL_Book REFERENCES trs.InstrumentBooks (Id) ON DELETE CASCADE,
    No         BIGINT        NOT NULL,
    SerialNo   NVARCHAR(30)  NOT NULL,
    [Status]   TINYINT       NOT NULL CONSTRAINT DF_IL_Status DEFAULT (1),
    VoidDate   DATE          NULL,
    VoidReason NVARCHAR(200) NULL,
    CONSTRAINT UQ_IL_No     UNIQUE (BookId, No),
    CONSTRAINT CK_IL_Status CHECK ([Status] IN (1, 2, 3)),
    CONSTRAINT CK_IL_Void   CHECK ([Status] = 3 OR VoidDate IS NULL)
);
CREATE INDEX IX_IL_Status ON trs.InstrumentLeaves (BookId, [Status], No);
GO

-- چک صادره از برگ دسته‌چک (چک‌های قبلی بدون برگ می‌مانند)
ALTER TABLE trs.IssuedCheques ADD LeafId BIGINT NULL CONSTRAINT FK_IC_Leaf REFERENCES trs.InstrumentLeaves (Id);
GO
CREATE UNIQUE INDEX UX_IC_Leaf ON trs.IssuedCheques (LeafId) WHERE LeafId IS NOT NULL;
GO

/* ---------------------------------------------------------------------
   ۴) ضمانت‌نامه بانکی صادرشده برای شرکت
   --------------------------------------------------------------------- */
-- وضعیت: 1 نزد ما، 2 نزد ذی‌نفع (تحویل با سند تضمینی)، 3 باطل/عودت به بانک
CREATE TABLE trs.BankGuarantees (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankGuarantees PRIMARY KEY,
    BankAccountId INT           NOT NULL CONSTRAINT FK_BG_Bank REFERENCES trs.BankAccounts (Id),
    Number        NVARCHAR(50)  NOT NULL,
    Amount        DECIMAL(24,4) NOT NULL,
    IssueDate     DATE          NOT NULL,
    ExpiryDate    DATE          NULL,
    [Subject]     NVARCHAR(300) NULL,
    [Status]      TINYINT       NOT NULL CONSTRAINT DF_BG_Status DEFAULT (1),
    CancelDate    DATE          NULL,
    Notes         NVARCHAR(500) NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_BG_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_BG_Number UNIQUE (BankAccountId, Number),
    CONSTRAINT CK_BG_Amount CHECK (Amount > 0),
    CONSTRAINT CK_BG_Status CHECK ([Status] IN (1, 2, 3)),
    CONSTRAINT CK_BG_Dates  CHECK (ExpiryDate IS NULL OR ExpiryDate >= IssueDate)
);
GO

/* ---------------------------------------------------------------------
   ۵) سند تضمینی پرداختی از برگ یا ضمانت‌نامه تعریف‌شده
   --------------------------------------------------------------------- */
ALTER TABLE trs.Guarantees ADD
    LeafId          BIGINT NULL CONSTRAINT FK_Guarantees_Leaf REFERENCES trs.InstrumentLeaves (Id),
    BankGuaranteeId INT    NULL CONSTRAINT FK_Guarantees_BG REFERENCES trs.BankGuarantees (Id);
GO
CREATE UNIQUE INDEX UX_Guarantees_Leaf ON trs.Guarantees (LeafId) WHERE LeafId IS NOT NULL;
CREATE INDEX IX_Guarantees_BG ON trs.Guarantees (BankGuaranteeId) WHERE BankGuaranteeId IS NOT NULL;
ALTER TABLE trs.Guarantees ADD CONSTRAINT CK_Guarantees_Source CHECK (
    (LeafId IS NULL OR (Direction = 2 AND Kind IN (1, 2)))
    AND (BankGuaranteeId IS NULL OR (Direction = 2 AND Kind = 3)));
GO

IF NOT EXISTS (SELECT 1 FROM fin.PostingTemplateLines l JOIN fin.PostingTemplates t ON t.Id = l.TemplateId
               WHERE t.SourceDocType = 'trs_imprest' AND l.LineKey = 'counter')
    THROW 50120, N'ردیف «پرداخت به طرف حساب» الگوی صورت‌هزینه تنخواه ساخته نشد.', 1;

PRINT N'ارزش افزوده ردیفی، تنخواه و اسناد پرداختنی آماده شد.';
GO
