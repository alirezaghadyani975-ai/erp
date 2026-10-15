/* =====================================================================
   11 - خزانه‌داری، نرخ ارزش افزوده و شماره سیستمی فرم‌ها

   - base.VatRates: نرخ سالانه مالیات و عوارض ارزش افزوده (محاسبه خودکار در فرم‌ها).
   - fin.DetailTypes.SystemKey: کلید سیستمی گروه‌ها (cash، imprest، bank، lc، loan، due_month) تا زیرسیستم‌ها
     گروهشان را بدون وابستگی به بازه کد پیدا کنند. تفصیلی صندوق، بانک و تنخواه از این به بعد فقط از خزانه‌داری.
   - قاعده معین «source» در الگوی سند: معین را خود فرم می‌دهد (از نوع دریافت/پرداخت).
   - schema trs: صندوق، حساب بانکی، تنخواه‌گردان، انواع دریافت و پرداخت، دریافت، پرداخت، انتقال وجه،
     صورت‌هزینه تنخواه، چک‌های دریافتی و صادره، عملیات چک، اسناد تضمینی.
   - تفصیلی‌های موجود صندوق، بانک و تنخواه به موجودیت خزانه‌داری تبدیل می‌شوند.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------------------------------------------------------------------
   ۱) نرخ ارزش افزوده
   --------------------------------------------------------------------- */
CREATE TABLE base.VatRates (
    Id          SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VatRates PRIMARY KEY,
    PersianYear INT          NOT NULL CONSTRAINT UQ_VatRates_Year UNIQUE,
    TaxPercent  DECIMAL(5,2) NOT NULL,
    TollPercent DECIMAL(5,2) NOT NULL,
    CONSTRAINT CK_VatRates_Percent CHECK (TaxPercent BETWEEN 0 AND 100 AND TollPercent BETWEEN 0 AND 100)
);
GO

/* ---------------------------------------------------------------------
   ۲) کلید سیستمی گروه‌های تفصیلی و قاعده source
   --------------------------------------------------------------------- */
ALTER TABLE fin.DetailTypes ADD SystemKey VARCHAR(30) NULL;
GO
UPDATE fin.DetailTypes SET SystemKey = CASE CodeFrom
        WHEN '100001' THEN 'cash' WHEN '101001' THEN 'imprest' WHEN '110001' THEN 'bank'
        WHEN '120001' THEN 'lc'   WHEN '130001' THEN 'loan'    WHEN '150001' THEN 'due_month' END
WHERE OwnerModule = 'treasury';
CREATE UNIQUE INDEX UX_DetailTypes_SystemKey ON fin.DetailTypes (SystemKey) WHERE SystemKey IS NOT NULL;

IF (SELECT COUNT(*) FROM fin.DetailTypes WHERE SystemKey IN ('cash', 'imprest', 'bank', 'due_month')) <> 4
    THROW 50110, N'گروه‌های تفصیلی صندوق، تنخواه، بانک یا ماه سررسید پیدا نشد.', 1;

ALTER TABLE fin.PostingTemplateLines DROP CONSTRAINT CK_PTL_Rule;
ALTER TABLE fin.PostingTemplateLines ADD CONSTRAINT CK_PTL_Rule CHECK (AccountRule IN ('cost', 'source'));
GO

/* ---------------------------------------------------------------------
   ۳) تعاریف خزانه‌داری
   --------------------------------------------------------------------- */
CREATE SCHEMA trs;
GO

CREATE TABLE trs.CashBoxes (
    Id       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashBoxes PRIMARY KEY,
    Title    NVARCHAR(100) NOT NULL CONSTRAINT UQ_CashBoxes_Title UNIQUE,
    IsActive BIT           NOT NULL CONSTRAINT DF_CashBoxes_IsActive DEFAULT (1),
    Notes    NVARCHAR(500) NULL
);

CREATE TABLE trs.BankAccounts (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankAccounts PRIMARY KEY,
    Title       NVARCHAR(150) NOT NULL CONSTRAINT UQ_BankAccounts_Title UNIQUE,
    BankId      SMALLINT      NULL     CONSTRAINT FK_BankAccounts_Bank REFERENCES base.Banks (Id),
    Branch      NVARCHAR(100) NULL,
    AccountNo   VARCHAR(30)   NULL,
    Iban        VARCHAR(26)   NULL,
    AccountKind TINYINT       NOT NULL CONSTRAINT DF_BankAccounts_Kind DEFAULT (1),  -- 1 جاری، 2 کوتاه‌مدت، 3 قرض‌الحسنه
    IsActive    BIT           NOT NULL CONSTRAINT DF_BankAccounts_IsActive DEFAULT (1),
    Notes       NVARCHAR(500) NULL,
    CONSTRAINT CK_BankAccounts_Kind CHECK (AccountKind IN (1, 2, 3))
);

CREATE TABLE trs.ImprestFunds (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ImprestFunds PRIMARY KEY,
    Title          NVARCHAR(100) NOT NULL CONSTRAINT UQ_ImprestFunds_Title UNIQUE,
    HolderPersonId INT           NULL     CONSTRAINT FK_ImprestFunds_Holder REFERENCES base.Persons (Id),
    IsActive       BIT           NOT NULL CONSTRAINT DF_ImprestFunds_IsActive DEFAULT (1),
    Notes          NVARCHAR(500) NULL
);

-- نوع دریافت/پرداخت: معین طرف مقابل (مثلاً «دریافت از مشتری» ← دریافتنی تجاری)
CREATE TABLE trs.TransactionTypes (
    Id        SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TransactionTypes PRIMARY KEY,
    Kind      TINYINT       NOT NULL,                  -- 1 دریافت، 2 پرداخت
    Title     NVARCHAR(100) NOT NULL,
    AccountId INT           NOT NULL CONSTRAINT FK_TransactionTypes_Account REFERENCES fin.Accounts (Id),
    SortOrder INT           NOT NULL CONSTRAINT DF_TransactionTypes_Sort DEFAULT (0),
    IsActive  BIT           NOT NULL CONSTRAINT DF_TransactionTypes_IsActive DEFAULT (1),
    CONSTRAINT CK_TransactionTypes_Kind CHECK (Kind IN (1, 2)),
    CONSTRAINT UQ_TransactionTypes_Title UNIQUE (Kind, Title)
);
GO

/* ---------------------------------------------------------------------
   ۴) دریافت، پرداخت، چک‌ها
   --------------------------------------------------------------------- */
CREATE TABLE trs.Receipts (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Receipts PRIMARY KEY,
    DocNo         INT           NOT NULL CONSTRAINT UQ_Receipts_No UNIQUE,
    DocDate       DATE          NOT NULL,
    PersonId      INT           NOT NULL CONSTRAINT FK_Receipts_Person REFERENCES base.Persons (Id),
    TypeId        SMALLINT      NOT NULL CONSTRAINT FK_Receipts_Type REFERENCES trs.TransactionTypes (Id),
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_Receipts_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt     DATETIME2(0)  NULL,
    RowVer        ROWVERSION    NOT NULL
);
CREATE INDEX IX_Receipts_Date ON trs.Receipts (DocDate);
CREATE INDEX IX_Receipts_Person ON trs.Receipts (PersonId, DocDate);

-- ابزار: 1 نقد (صندوق)، 2 بانکی (واریز، کارت‌خوان، انتقال)، 3 چک
CREATE TABLE trs.ReceiptLines (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReceiptLines PRIMARY KEY,
    ReceiptId     BIGINT        NOT NULL CONSTRAINT FK_RL_Receipt REFERENCES trs.Receipts (Id) ON DELETE CASCADE,
    RowNo         SMALLINT      NOT NULL,
    Instrument    TINYINT       NOT NULL,
    CashBoxId     INT           NULL     CONSTRAINT FK_RL_CashBox REFERENCES trs.CashBoxes (Id),
    BankAccountId INT           NULL     CONSTRAINT FK_RL_Bank REFERENCES trs.BankAccounts (Id),
    Amount        DECIMAL(24,4) NOT NULL,
    RefNo         NVARCHAR(50)  NULL,
    [Description] NVARCHAR(500) NULL,
    CONSTRAINT UQ_RL_Row        UNIQUE (ReceiptId, RowNo),
    CONSTRAINT CK_RL_Amount     CHECK (Amount > 0),
    CONSTRAINT CK_RL_Instrument CHECK ((Instrument IN (1, 3) AND CashBoxId IS NOT NULL AND BankAccountId IS NULL)
                                    OR (Instrument = 2 AND BankAccountId IS NOT NULL AND CashBoxId IS NULL))
);

-- وضعیت چک دریافتی: 1 در صندوق، 2 در جریان وصول، 3 وصول‌شده، 4 برگشتی، 5 حقوقی، 6 عودت‌شده، 7 خرج‌شده
CREATE TABLE trs.ReceivedCheques (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReceivedCheques PRIMARY KEY,
    ReceiptLineId    BIGINT        NOT NULL CONSTRAINT FK_RC_Line REFERENCES trs.ReceiptLines (Id) ON DELETE CASCADE,
    PersonId         INT           NOT NULL CONSTRAINT FK_RC_Person REFERENCES base.Persons (Id),
    CounterAccountId INT           NOT NULL CONSTRAINT FK_RC_Counter REFERENCES fin.Accounts (Id),
    BankId           SMALLINT      NULL     CONSTRAINT FK_RC_Bank REFERENCES base.Banks (Id),
    Branch           NVARCHAR(100) NULL,
    DrawerName       NVARCHAR(150) NULL,
    AccountNo        VARCHAR(30)   NULL,
    SerialNo         NVARCHAR(30)  NOT NULL,
    SayadId          CHAR(16)      NULL,
    DueDate          DATE          NOT NULL,
    Amount           DECIMAL(24,4) NOT NULL,
    [Status]         TINYINT       NOT NULL CONSTRAINT DF_RC_Status DEFAULT (1),
    CashBoxId        INT           NULL     CONSTRAINT FK_RC_CashBox REFERENCES trs.CashBoxes (Id),
    BankAccountId    INT           NULL     CONSTRAINT FK_RC_BankAccount REFERENCES trs.BankAccounts (Id),
    [Description]    NVARCHAR(500) NULL,
    CONSTRAINT UQ_RC_Line   UNIQUE (ReceiptLineId),
    CONSTRAINT CK_RC_Amount CHECK (Amount > 0),
    CONSTRAINT CK_RC_Status CHECK ([Status] BETWEEN 1 AND 7),
    CONSTRAINT CK_RC_Sayad  CHECK (SayadId IS NULL OR SayadId NOT LIKE '%[^0-9]%')
);
CREATE INDEX IX_RC_Status ON trs.ReceivedCheques ([Status], DueDate);
CREATE INDEX IX_RC_Person ON trs.ReceivedCheques (PersonId);
CREATE UNIQUE INDEX UX_RC_Sayad ON trs.ReceivedCheques (SayadId) WHERE SayadId IS NOT NULL;
GO

CREATE TABLE trs.Payments (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payments PRIMARY KEY,
    DocNo         INT           NOT NULL CONSTRAINT UQ_Payments_No UNIQUE,
    DocDate       DATE          NOT NULL,
    PersonId      INT           NOT NULL CONSTRAINT FK_Payments_Person REFERENCES base.Persons (Id),
    TypeId        SMALLINT      NOT NULL CONSTRAINT FK_Payments_Type REFERENCES trs.TransactionTypes (Id),
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_Payments_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt     DATETIME2(0)  NULL,
    RowVer        ROWVERSION    NOT NULL
);
CREATE INDEX IX_Payments_Date ON trs.Payments (DocDate);
CREATE INDEX IX_Payments_Person ON trs.Payments (PersonId, DocDate);

-- ابزار: 1 نقد، 2 بانکی، 3 چک صادره، 4 خرج چک دریافتی
CREATE TABLE trs.PaymentLines (
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentLines PRIMARY KEY,
    PaymentId         BIGINT        NOT NULL CONSTRAINT FK_PL_Payment REFERENCES trs.Payments (Id) ON DELETE CASCADE,
    RowNo             SMALLINT      NOT NULL,
    Instrument        TINYINT       NOT NULL,
    CashBoxId         INT           NULL     CONSTRAINT FK_PL_CashBox REFERENCES trs.CashBoxes (Id),
    BankAccountId     INT           NULL     CONSTRAINT FK_PL_Bank REFERENCES trs.BankAccounts (Id),
    ReceivedChequeId  BIGINT        NULL     CONSTRAINT FK_PL_Cheque REFERENCES trs.ReceivedCheques (Id),
    Amount            DECIMAL(24,4) NOT NULL,
    RefNo             NVARCHAR(50)  NULL,
    [Description]     NVARCHAR(500) NULL,
    CONSTRAINT UQ_PL_Row        UNIQUE (PaymentId, RowNo),
    CONSTRAINT CK_PL_Amount     CHECK (Amount > 0),
    CONSTRAINT CK_PL_Instrument CHECK ((Instrument = 1 AND CashBoxId IS NOT NULL AND BankAccountId IS NULL AND ReceivedChequeId IS NULL)
                                    OR (Instrument IN (2, 3) AND BankAccountId IS NOT NULL AND CashBoxId IS NULL AND ReceivedChequeId IS NULL)
                                    OR (Instrument = 4 AND ReceivedChequeId IS NOT NULL AND CashBoxId IS NULL AND BankAccountId IS NULL))
);
CREATE UNIQUE INDEX UX_PL_Cheque ON trs.PaymentLines (ReceivedChequeId) WHERE ReceivedChequeId IS NOT NULL;

-- وضعیت چک صادره: 1 صادره (پاس‌نشده)، 2 پاس‌شده، 3 باطل/عودت به ما
CREATE TABLE trs.IssuedCheques (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IssuedCheques PRIMARY KEY,
    PaymentLineId    BIGINT        NOT NULL CONSTRAINT FK_IC_Line REFERENCES trs.PaymentLines (Id) ON DELETE CASCADE,
    PersonId         INT           NOT NULL CONSTRAINT FK_IC_Person REFERENCES base.Persons (Id),
    CounterAccountId INT           NOT NULL CONSTRAINT FK_IC_Counter REFERENCES fin.Accounts (Id),
    BankAccountId    INT           NOT NULL CONSTRAINT FK_IC_BankAccount REFERENCES trs.BankAccounts (Id),
    SerialNo         NVARCHAR(30)  NOT NULL,
    SayadId          CHAR(16)      NULL,
    DueDate          DATE          NOT NULL,
    Amount           DECIMAL(24,4) NOT NULL,
    [Status]         TINYINT       NOT NULL CONSTRAINT DF_IC_Status DEFAULT (1),
    [Description]    NVARCHAR(500) NULL,
    CONSTRAINT UQ_IC_Line   UNIQUE (PaymentLineId),
    CONSTRAINT CK_IC_Amount CHECK (Amount > 0),
    CONSTRAINT CK_IC_Status CHECK ([Status] BETWEEN 1 AND 3),
    CONSTRAINT CK_IC_Sayad  CHECK (SayadId IS NULL OR SayadId NOT LIKE '%[^0-9]%'),
    CONSTRAINT UQ_IC_Serial UNIQUE (BankAccountId, SerialNo)
);
CREATE INDEX IX_IC_Status ON trs.IssuedCheques ([Status], DueDate);
GO

/* ---------------------------------------------------------------------
   ۵) انتقال وجه، صورت‌هزینه تنخواه
   --------------------------------------------------------------------- */
-- نوع منبع/مقصد: 1 صندوق، 2 حساب بانکی، 3 تنخواه‌گردان
CREATE TABLE trs.Transfers (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Transfers PRIMARY KEY,
    DocNo         INT           NOT NULL CONSTRAINT UQ_Transfers_No UNIQUE,
    DocDate       DATE          NOT NULL,
    FromKind      TINYINT       NOT NULL,
    FromId        INT           NOT NULL,
    ToKind        TINYINT       NOT NULL,
    ToId          INT           NOT NULL,
    Amount        DECIMAL(24,4) NOT NULL,
    Fee           DECIMAL(24,4) NOT NULL CONSTRAINT DF_Transfers_Fee DEFAULT (0),
    FeeFactorId   INT           NULL     CONSTRAINT FK_Transfers_FeeFactor REFERENCES cost.CostFactors (Id),
    FeeCenterId   INT           NULL     CONSTRAINT FK_Transfers_FeeCenter REFERENCES cost.CostCenters (Id),
    RefNo         NVARCHAR(50)  NULL,
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_Transfers_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt     DATETIME2(0)  NULL,
    RowVer        ROWVERSION    NOT NULL,
    CONSTRAINT CK_Transfers_Kinds  CHECK (FromKind IN (1, 2, 3) AND ToKind IN (1, 2, 3)),
    CONSTRAINT CK_Transfers_Same   CHECK (NOT (FromKind = ToKind AND FromId = ToId)),
    CONSTRAINT CK_Transfers_Amount CHECK (Amount > 0 AND Fee >= 0),
    CONSTRAINT CK_Transfers_Fee    CHECK (Fee = 0 OR FeeFactorId IS NOT NULL)
);
CREATE INDEX IX_Transfers_Date ON trs.Transfers (DocDate);

CREATE TABLE trs.ImprestSettlements (
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ImprestSettlements PRIMARY KEY,
    DocNo          INT           NOT NULL CONSTRAINT UQ_ImprestSettlements_No UNIQUE,
    DocDate        DATE          NOT NULL,
    ImprestFundId  INT           NOT NULL CONSTRAINT FK_IS_Fund REFERENCES trs.ImprestFunds (Id),
    VatAmount      DECIMAL(24,4) NOT NULL CONSTRAINT DF_IS_Vat DEFAULT (0),
    [Description]  NVARCHAR(500) NULL,
    CreatedBy      INT           NOT NULL,
    CreatedAt      DATETIME2(0)  NOT NULL CONSTRAINT DF_IS_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt      DATETIME2(0)  NULL,
    RowVer         ROWVERSION    NOT NULL,
    CONSTRAINT CK_IS_Vat CHECK (VatAmount >= 0)
);
CREATE INDEX IX_IS_Fund ON trs.ImprestSettlements (ImprestFundId, DocDate);

CREATE TABLE trs.ImprestSettlementLines (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ImprestSettlementLines PRIMARY KEY,
    SettlementId  BIGINT        NOT NULL CONSTRAINT FK_ISL_Settlement REFERENCES trs.ImprestSettlements (Id) ON DELETE CASCADE,
    RowNo         SMALLINT      NOT NULL,
    CostFactorId  INT           NOT NULL CONSTRAINT FK_ISL_Factor REFERENCES cost.CostFactors (Id),
    CostCenterId  INT           NULL     CONSTRAINT FK_ISL_Center REFERENCES cost.CostCenters (Id),
    Amount        DECIMAL(24,4) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    CONSTRAINT UQ_ISL_Row    UNIQUE (SettlementId, RowNo),
    CONSTRAINT CK_ISL_Amount CHECK (Amount > 0)
);
GO

/* ---------------------------------------------------------------------
   ۶) عملیات چک و اسناد تضمینی
   --------------------------------------------------------------------- */
-- طرف: 1 چک دریافتنی، 2 چک پرداختنی. عملیات دریافتنی: 1 واگذاری به بانک، 2 وصول، 3 برگشت، 4 عودت به صاحب چک،
-- 5 حقوقی، 6 برگشت از واگذاری به صندوق؛ پرداختنی: 11 پاس، 12 ابطال/عودت به ما
CREATE TABLE trs.ChequeOperations (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChequeOperations PRIMARY KEY,
    Side          TINYINT       NOT NULL,
    DocNo         INT           NOT NULL,
    Operation     TINYINT       NOT NULL,
    DocDate       DATE          NOT NULL,
    CashBoxId     INT           NULL     CONSTRAINT FK_CO_CashBox REFERENCES trs.CashBoxes (Id),
    BankAccountId INT           NULL     CONSTRAINT FK_CO_Bank REFERENCES trs.BankAccounts (Id),
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_CO_CreatedAt DEFAULT (SYSUTCDATETIME()),
    RowVer        ROWVERSION    NOT NULL,
    CONSTRAINT UQ_CO_No   UNIQUE (Side, DocNo),
    CONSTRAINT CK_CO_Side CHECK (Side IN (1, 2)),
    CONSTRAINT CK_CO_Op   CHECK ((Side = 1 AND Operation BETWEEN 1 AND 6) OR (Side = 2 AND Operation IN (11, 12)))
);
CREATE INDEX IX_CO_Date ON trs.ChequeOperations (DocDate);

CREATE TABLE trs.ChequeOperationLines (
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChequeOperationLines PRIMARY KEY,
    OperationId       BIGINT        NOT NULL CONSTRAINT FK_COL_Operation REFERENCES trs.ChequeOperations (Id) ON DELETE CASCADE,
    ReceivedChequeId  BIGINT        NULL     CONSTRAINT FK_COL_Received REFERENCES trs.ReceivedCheques (Id),
    IssuedChequeId    BIGINT        NULL     CONSTRAINT FK_COL_Issued REFERENCES trs.IssuedCheques (Id),
    FromStatus        TINYINT       NOT NULL,
    ToStatus          TINYINT       NOT NULL,
    FromCashBoxId     INT           NULL,
    FromBankAccountId INT           NULL,
    CONSTRAINT CK_COL_Cheque CHECK ((ReceivedChequeId IS NULL AND IssuedChequeId IS NOT NULL) OR (ReceivedChequeId IS NOT NULL AND IssuedChequeId IS NULL))
);
CREATE INDEX IX_COL_Received ON trs.ChequeOperationLines (ReceivedChequeId) WHERE ReceivedChequeId IS NOT NULL;
CREATE INDEX IX_COL_Issued ON trs.ChequeOperationLines (IssuedChequeId) WHERE IssuedChequeId IS NOT NULL;
GO

-- جهت: 1 دریافتی (از دیگران نزد ما)، 2 پرداختی (از ما نزد دیگران)؛ نوع: 1 چک، 2 سفته، 3 ضمانت‌نامه بانکی، 4 سایر
CREATE TABLE trs.Guarantees (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Guarantees PRIMARY KEY,
    DocNo         INT           NOT NULL CONSTRAINT UQ_Guarantees_No UNIQUE,
    Direction     TINYINT       NOT NULL,
    Kind          TINYINT       NOT NULL,
    DocDate       DATE          NOT NULL,
    PersonId      INT           NOT NULL CONSTRAINT FK_Guarantees_Person REFERENCES base.Persons (Id),
    BankAccountId INT           NULL     CONSTRAINT FK_Guarantees_Bank REFERENCES trs.BankAccounts (Id),
    Number        NVARCHAR(50)  NULL,
    Amount        DECIMAL(24,4) NOT NULL,
    DueDate       DATE          NULL,
    [Subject]     NVARCHAR(300) NULL,
    [Status]      TINYINT       NOT NULL CONSTRAINT DF_Guarantees_Status DEFAULT (1),   -- 1 جاری، 2 مسترد
    ReturnDate    DATE          NULL,
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_Guarantees_CreatedAt DEFAULT (SYSUTCDATETIME()),
    RowVer        ROWVERSION    NOT NULL,
    CONSTRAINT CK_Guarantees_Dir    CHECK (Direction IN (1, 2)),
    CONSTRAINT CK_Guarantees_Kind   CHECK (Kind BETWEEN 1 AND 4),
    CONSTRAINT CK_Guarantees_Amount CHECK (Amount > 0),
    CONSTRAINT CK_Guarantees_Status CHECK (([Status] = 1 AND ReturnDate IS NULL) OR ([Status] = 2 AND ReturnDate IS NOT NULL)),
    CONSTRAINT CK_Guarantees_Bank   CHECK (Kind <> 3 OR Direction <> 2 OR BankAccountId IS NOT NULL)
);
CREATE INDEX IX_Guarantees_Person ON trs.Guarantees (PersonId, [Status]);
GO

/* ---------------------------------------------------------------------
   ۷) تبدیل تفصیلی‌های موجود صندوق، بانک و تنخواه به موجودیت خزانه‌داری
   --------------------------------------------------------------------- */
DECLARE @DetailId INT, @Title NVARCHAR(200), @Key VARCHAR(30), @NewId INT, @Unique NVARCHAR(200);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT d.Id, d.Title, t.SystemKey
    FROM fin.DetailAccounts d JOIN fin.DetailTypes t ON t.Id = d.DetailTypeId
    WHERE t.SystemKey IN ('cash', 'bank', 'imprest') AND d.SourceEntityId IS NULL
    ORDER BY d.Code;
OPEN c;
FETCH NEXT FROM c INTO @DetailId, @Title, @Key;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Unique = LEFT(@Title, 90);
    IF @Key = 'cash'
    BEGIN
        IF EXISTS (SELECT 1 FROM trs.CashBoxes WHERE Title = @Unique) SET @Unique = CONCAT(@Unique, N' ', @DetailId);
        INSERT trs.CashBoxes (Title, IsActive) SELECT @Unique, IsActive FROM fin.DetailAccounts WHERE Id = @DetailId;
    END
    ELSE IF @Key = 'bank'
    BEGIN
        IF EXISTS (SELECT 1 FROM trs.BankAccounts WHERE Title = @Unique) SET @Unique = CONCAT(@Unique, N' ', @DetailId);
        INSERT trs.BankAccounts (Title, IsActive) SELECT @Unique, IsActive FROM fin.DetailAccounts WHERE Id = @DetailId;
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM trs.ImprestFunds WHERE Title = @Unique) SET @Unique = CONCAT(@Unique, N' ', @DetailId);
        INSERT trs.ImprestFunds (Title, IsActive) SELECT @Unique, IsActive FROM fin.DetailAccounts WHERE Id = @DetailId;
    END
    SET @NewId = SCOPE_IDENTITY();
    UPDATE fin.DetailAccounts SET SourceModule = 'treasury', SourceEntityId = @NewId, Title = @Unique WHERE Id = @DetailId;
    FETCH NEXT FROM c INTO @DetailId, @Title, @Key;
END
CLOSE c;
DEALLOCATE c;
GO

/* ---------------------------------------------------------------------
   ۸) انواع دریافت و پرداخت پیش‌فرض
   --------------------------------------------------------------------- */
INSERT trs.TransactionTypes (Kind, Title, AccountId, SortOrder)
SELECT v.Kind, v.Title, a.Id, v.SortOrder
FROM (VALUES
    (1, N'دریافت از مشتری',             '2411', 1),
    (1, N'پیش‌دریافت فروش',             '5701', 2),
    (1, N'سایر دریافت‌ها',              '2511', 3),
    (2, N'پرداخت به تأمین‌کننده',        '5011', 1),
    (2, N'پرداخت سایر بدهی‌ها',          '5111', 2),
    (2, N'پیش‌پرداخت خرید کالا و خدمات', '2001', 3),
    (2, N'مساعده کارکنان',               '2523', 4),
    (2, N'پرداخت حقوق',                  '5125', 5)
) v (Kind, Title, Code, SortOrder)
JOIN fin.Accounts a ON a.Code = v.Code AND a.[Level] = 3;

IF (SELECT COUNT(*) FROM trs.TransactionTypes) <> 8
    THROW 50111, N'معین‌های پیش‌فرض انواع دریافت و پرداخت پیدا نشد.', 1;
GO

/* ---------------------------------------------------------------------
   ۹) الگوهای صدور سند خزانه‌داری
      طرف ۱ بدهکار، ۲ بستانکار؛ در الگوهای «عملیات» و «انتقال» همه بدهکارند و مبلغ منفی بستانکار می‌شود.
   --------------------------------------------------------------------- */
DECLARE @T TABLE (DocType VARCHAR(30), Title NVARCHAR(100));
INSERT @T VALUES
    ('trs_receipt',          N'دریافت'),
    ('trs_payment',          N'پرداخت'),
    ('trs_transfer',         N'انتقال وجه'),
    ('trs_imprest',          N'صورت‌هزینه تنخواه'),
    ('trs_cheque_in',        N'عملیات چک دریافتنی'),
    ('trs_cheque_out',       N'عملیات چک پرداختنی'),
    ('trs_guarantee',        N'اسناد تضمینی'),
    ('trs_guarantee_return', N'استرداد اسناد تضمینی');
INSERT fin.PostingTemplates (SourceDocType, Title, SourceModule) SELECT DocType, Title, 'treasury' FROM @T;

DECLARE @L TABLE (DocType VARCHAR(30), LineKey VARCHAR(30), Title NVARCHAR(100), Side TINYINT, Code VARCHAR(20) NULL,
                  RuleKey VARCHAR(30) NULL, Hint NVARCHAR(200) NULL, SortOrder TINYINT);
INSERT @L VALUES
    ('trs_receipt', 'cash',    N'نقد به صندوق',               1, '2801', NULL, N'صندوق', 1),
    ('trs_receipt', 'bank',    N'واریز به حساب بانکی',         1, '2811', NULL, N'حساب بانکی', 2),
    ('trs_receipt', 'cheque',  N'چک دریافتی',                  1, '2401', NULL, N'صندوق نگهداری چک', 3),
    ('trs_receipt', 'counter', N'طرف حساب (نوع دریافت)',       2, NULL, 'source', N'پرداخت‌کننده', 4),

    ('trs_payment', 'counter', N'طرف حساب (نوع پرداخت)',       1, NULL, 'source', N'دریافت‌کننده', 1),
    ('trs_payment', 'cash',    N'نقد از صندوق',                2, '2801', NULL, N'صندوق', 2),
    ('trs_payment', 'bank',    N'پرداخت از حساب بانکی',        2, '2811', NULL, N'حساب بانکی', 3),
    ('trs_payment', 'cheque',  N'چک صادره',                    2, '5001', NULL, N'ماه سررسید چک', 4),
    ('trs_payment', 'spend',   N'خرج چک دریافتی',              2, '2401', NULL, N'صندوق نگهداری چک', 5),

    ('trs_transfer', 'cash',    N'صندوق',                       1, '2801', NULL, N'صندوق', 1),
    ('trs_transfer', 'bank',    N'حساب بانکی',                  1, '2811', NULL, N'حساب بانکی', 2),
    ('trs_transfer', 'imprest', N'تنخواه‌گردان',                1, '2821', NULL, N'تنخواه‌گردان', 3),
    ('trs_transfer', 'fee',     N'کارمزد بانکی',                1, NULL, 'cost', N'عامل و مرکز هزینه کارمزد', 4),

    ('trs_imprest', 'expense', N'هزینه',                        1, NULL, 'cost', N'عامل و مرکز هزینه ردیف', 1),
    ('trs_imprest', 'vat',     N'مالیات و عوارض ارزش افزوده',   1, '2541', NULL, NULL, 2),
    ('trs_imprest', 'imprest', N'تنخواه‌گردان',                 2, '2821', NULL, N'تنخواه‌گردان', 3),

    ('trs_cheque_in', 'box',        N'اسناد دریافتنی (در صندوق)',  1, '2401', NULL, N'صندوق', 1),
    ('trs_cheque_in', 'collection', N'اسناد در جریان وصول',        1, '2402', NULL, N'حساب بانکی', 2),
    ('trs_cheque_in', 'bank',       N'حساب بانکی',                 1, '2811', NULL, N'حساب بانکی', 3),
    ('trs_cheque_in', 'cash',       N'صندوق',                      1, '2801', NULL, N'صندوق', 4),
    ('trs_cheque_in', 'bounced',    N'اسناد برگشتی',               1, '2501', NULL, N'صاحب چک', 5),
    ('trs_cheque_in', 'legal',      N'اسناد حقوقی‌شده',            1, '2502', NULL, N'صاحب چک', 6),
    ('trs_cheque_in', 'counter',    N'طرف حساب (عودت چک)',          1, NULL, 'source', N'صاحب چک', 7),

    ('trs_cheque_out', 'notes',   N'اسناد پرداختنی',             1, '5001', NULL, N'ماه سررسید چک', 1),
    ('trs_cheque_out', 'bank',    N'حساب بانکی',                 1, '2811', NULL, N'حساب بانکی', 2),
    ('trs_cheque_out', 'counter', N'طرف حساب (ابطال چک)',        1, NULL, 'source', N'دریافت‌کننده چک', 3),

    ('trs_guarantee', 'received',       N'اسناد تضمینی دریافتی',          1, '9101', NULL, N'شخص', 1),
    ('trs_guarantee', 'received_party', N'طرف اسناد تضمینی دریافتی',      2, '9201', NULL, N'شخص', 2),
    ('trs_guarantee', 'issued',         N'اسناد تضمینی پرداختی',          1, '9151', NULL, N'شخص', 3),
    ('trs_guarantee', 'issued_bank',    N'ضمانت‌نامه بانکی پرداختی',       1, '9152', NULL, N'حساب بانکی', 4),
    ('trs_guarantee', 'issued_party',   N'طرف اسناد تضمینی پرداختی',      2, '9251', NULL, N'شخص', 5),

    ('trs_guarantee_return', 'received',       N'اسناد تضمینی دریافتی',     2, '9101', NULL, N'شخص', 1),
    ('trs_guarantee_return', 'received_party', N'طرف اسناد تضمینی دریافتی', 1, '9201', NULL, N'شخص', 2),
    ('trs_guarantee_return', 'issued',         N'اسناد تضمینی پرداختی',     2, '9151', NULL, N'شخص', 3),
    ('trs_guarantee_return', 'issued_bank',    N'ضمانت‌نامه بانکی پرداختی',  2, '9152', NULL, N'حساب بانکی', 4),
    ('trs_guarantee_return', 'issued_party',   N'طرف اسناد تضمینی پرداختی', 1, '9251', NULL, N'شخص', 5);

INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder)
SELECT t.Id, l.LineKey, l.Title, l.Side, a.Id, l.RuleKey, l.Hint, l.SortOrder
FROM @L l
JOIN fin.PostingTemplates t ON t.SourceDocType = l.DocType
LEFT JOIN fin.Accounts a ON a.Code = l.Code AND a.[Level] = 3;

IF EXISTS (SELECT 1 FROM fin.PostingTemplateLines WHERE AccountId IS NULL AND AccountRule IS NULL)
    THROW 50112, N'معین یکی از ردیف‌های الگوهای خزانه‌داری پیدا نشد.', 1;

PRINT N'خزانه‌داری و نرخ ارزش افزوده آماده شد.';
GO
