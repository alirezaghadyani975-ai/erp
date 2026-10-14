/* =====================================================================
   01 - جدول‌های هسته مالی
   اجرا: داخل دیتابیس یک شرکت، بعد از 00_create_database.sql
   نسخه: SQL Server 2019 یا جدیدتر (Express هم کافی است)

   قاعده‌های اصلی در موتور سند (C#) اجرا می‌شوند؛ تریگرهای این فایل فقط
   پشتیبان آخرند تا داده خراب حتی با دسترسی مستقیم به دیتابیس ثبت نشود.

   کاربران در دیتابیس مرکزی‌اند، پس CreatedBy و ApprovedBy کلید خارجی ندارند.
   تاریخ‌ها میلادی (DATE) ذخیره می‌شوند؛ نمایش و گزارش شمسی از base.Calendar.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- لازم برای ایندکس‌های فیلترشده (sqlcmd پیش‌فرض OFF است)
SET ANSI_NULLS ON;
GO

CREATE SCHEMA base;   -- داده‌های پایه مشترک بین ماژول‌ها
GO
CREATE SCHEMA fin;    -- هسته مالی
GO
CREATE SCHEMA audit;  -- ردپای تغییرات
GO

/* ---------------------------------------------------------------------
   base: ارز، واحد، اشخاص، تقویم شمسی
   --------------------------------------------------------------------- */
CREATE TABLE base.Currencies (
    Id            SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Currencies PRIMARY KEY,
    Code          CHAR(3)       NOT NULL CONSTRAINT UQ_Currencies_Code UNIQUE,
    Title         NVARCHAR(50)  NOT NULL,
    DecimalPlaces TINYINT       NOT NULL CONSTRAINT DF_Currencies_Decimals DEFAULT (0),
    IsBase        BIT           NOT NULL CONSTRAINT DF_Currencies_IsBase   DEFAULT (0),
    IsActive      BIT           NOT NULL CONSTRAINT DF_Currencies_IsActive DEFAULT (1),
    CONSTRAINT CK_Currencies_Decimals CHECK (DecimalPlaces <= 4)
);
-- فقط یک ارز پایه
CREATE UNIQUE INDEX UX_Currencies_Base ON base.Currencies (IsBase) WHERE IsBase = 1;
GO

CREATE TABLE base.Units (
    Id       SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Units PRIMARY KEY,
    Title    NVARCHAR(50) NOT NULL CONSTRAINT UQ_Units_Title UNIQUE,
    IsActive BIT          NOT NULL CONSTRAINT DF_Units_IsActive DEFAULT (1)
);
GO

-- نسخه حداقلی؛ ماژول اشخاص بعداً ستون‌های کامل (آدرس، تماس، نقش‌ها) را اضافه می‌کند
CREATE TABLE base.Persons (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Persons PRIMARY KEY,
    PersonKind   TINYINT       NOT NULL,          -- 1 حقیقی، 2 حقوقی
    DisplayName  NVARCHAR(200) NOT NULL,
    NationalId   VARCHAR(12)   NULL,              -- کد ملی، شناسه ملی یا کد فراگیر
    EconomicCode VARCHAR(20)   NULL,
    IsActive     BIT           NOT NULL CONSTRAINT DF_Persons_IsActive DEFAULT (1),
    CONSTRAINT CK_Persons_Kind CHECK (PersonKind IN (1, 2))
);
CREATE UNIQUE INDEX UX_Persons_NationalId ON base.Persons (NationalId) WHERE NationalId IS NOT NULL;
GO

CREATE TABLE base.Calendar (
    [Date]       DATE     NOT NULL CONSTRAINT PK_Calendar PRIMARY KEY,
    PersianYear  SMALLINT NOT NULL,
    PersianMonth TINYINT  NOT NULL,
    PersianDay   TINYINT  NOT NULL,
    PersianDate  CHAR(10) NOT NULL,                -- 1405/01/01
    WeekDayNo    TINYINT  NOT NULL,                -- 1 شنبه ... 7 جمعه
    CONSTRAINT UQ_Calendar_Persian UNIQUE (PersianYear, PersianMonth, PersianDay),
    CONSTRAINT CK_Calendar_Month   CHECK (PersianMonth BETWEEN 1 AND 12),
    CONSTRAINT CK_Calendar_Day     CHECK (PersianDay BETWEEN 1 AND 31),
    CONSTRAINT CK_Calendar_WeekDay CHECK (WeekDayNo BETWEEN 1 AND 7)
);
CREATE UNIQUE INDEX UX_Calendar_PersianDate ON base.Calendar (PersianDate);
GO

/* ---------------------------------------------------------------------
   fin: کدینگ حساب‌ها (گروه / کل / معین در یک جدول درختی)
   --------------------------------------------------------------------- */
CREATE TABLE fin.Accounts (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Accounts PRIMARY KEY,
    ParentId    INT           NULL CONSTRAINT FK_Accounts_Parent REFERENCES fin.Accounts (Id),
    [Level]     TINYINT       NOT NULL,           -- 1 گروه، 2 کل، 3 معین
    Code        VARCHAR(20)   NOT NULL CONSTRAINT UQ_Accounts_Code UNIQUE,   -- کد کامل، مثل 1101
    Title       NVARCHAR(200) NOT NULL,
    Nature      TINYINT       NULL,               -- 1 بدهکار، 2 بستانکار، 3 هر دو
    AccountType TINYINT       NULL,               -- 1 ترازنامه‌ای، 2 سود و زیانی، 3 انتظامی
    IsStandard  BIT           NOT NULL CONSTRAINT DF_Accounts_IsStandard DEFAULT (0),
    IsActive    BIT           NOT NULL CONSTRAINT DF_Accounts_IsActive   DEFAULT (1),
    CONSTRAINT CK_Accounts_Level  CHECK ([Level] IN (1, 2, 3)),
    CONSTRAINT CK_Accounts_Parent CHECK (([Level] = 1 AND ParentId IS NULL) OR ([Level] > 1 AND ParentId IS NOT NULL)),
    CONSTRAINT CK_Accounts_Nature CHECK (Nature IN (1, 2, 3)),
    CONSTRAINT CK_Accounts_Type   CHECK (AccountType IN (1, 2, 3)),
    CONSTRAINT CK_Accounts_Moein  CHECK ([Level] < 3 OR (Nature IS NOT NULL AND AccountType IS NOT NULL)),
    CONSTRAINT CK_Accounts_Code   CHECK (Code NOT LIKE '%[^0-9]%')
);
CREATE INDEX IX_Accounts_Parent ON fin.Accounts (ParentId);
GO

CREATE TRIGGER fin.trg_Accounts_ParentLevel ON fin.Accounts AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN fin.Accounts p ON p.Id = i.ParentId
               WHERE p.[Level] <> i.[Level] - 1)
        THROW 50001, N'حساب والد باید دقیقاً یک سطح بالاتر باشد.', 1;
END;
GO

/* ---------------------------------------------------------------------
   fin: تفصیلی شناور
   --------------------------------------------------------------------- */
CREATE TABLE fin.DetailTypes (
    Id          SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DetailTypes PRIMARY KEY,
    Title       NVARCHAR(100) NOT NULL CONSTRAINT UQ_DetailTypes_Title UNIQUE,
    OwnerModule VARCHAR(30)   NOT NULL,           -- persons, treasury, payroll, assets, manual
    PersonKind  TINYINT       NULL,               -- برای انواع شخص‌محور: 1 حقیقی، 2 حقوقی
    CodeFrom    VARCHAR(10)   NOT NULL,
    CodeTo      VARCHAR(10)   NOT NULL,
    IsActive    BIT           NOT NULL CONSTRAINT DF_DetailTypes_IsActive DEFAULT (1),
    CONSTRAINT CK_DetailTypes_Range CHECK (LEN(CodeFrom) = LEN(CodeTo) AND CodeFrom <= CodeTo
                                           AND CodeFrom NOT LIKE '%[^0-9]%' AND CodeTo NOT LIKE '%[^0-9]%'),
    CONSTRAINT CK_DetailTypes_PersonKind CHECK (PersonKind IN (1, 2))
);
GO

CREATE TRIGGER fin.trg_DetailTypes_Overlap ON fin.DetailTypes AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN fin.DetailTypes t
                 ON t.Id <> i.Id AND LEN(t.CodeFrom) = LEN(i.CodeFrom)
                AND t.CodeFrom <= i.CodeTo AND i.CodeFrom <= t.CodeTo)
        THROW 50003, N'بازه کد این نوع تفصیلی با نوع دیگری هم‌پوشانی دارد.', 1;
END;
GO

CREATE TABLE fin.DetailAccounts (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DetailAccounts PRIMARY KEY,
    DetailTypeId   SMALLINT      NOT NULL CONSTRAINT FK_DetailAccounts_Type   REFERENCES fin.DetailTypes (Id),
    Code           VARCHAR(10)   NOT NULL CONSTRAINT UQ_DetailAccounts_Code   UNIQUE,
    Title          NVARCHAR(200) NOT NULL,
    PersonId       INT           NULL     CONSTRAINT FK_DetailAccounts_Person REFERENCES base.Persons (Id),
    SourceModule   VARCHAR(30)   NULL,            -- ماژولی که تفصیلی را ساخته
    SourceEntityId BIGINT        NULL,
    IsActive       BIT           NOT NULL CONSTRAINT DF_DetailAccounts_IsActive DEFAULT (1)
);
-- هر شخص در هر نقش فقط یک تفصیلی دارد؛ نقش‌های مختلف یک شخص با PersonId به هم وصل‌اند
CREATE UNIQUE INDEX UX_DetailAccounts_PersonType ON fin.DetailAccounts (PersonId, DetailTypeId) WHERE PersonId IS NOT NULL;
CREATE INDEX IX_DetailAccounts_Type ON fin.DetailAccounts (DetailTypeId);
GO

CREATE TRIGGER fin.trg_DetailAccounts_Range ON fin.DetailAccounts AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN fin.DetailTypes t ON t.Id = i.DetailTypeId
               WHERE LEN(i.Code) <> LEN(t.CodeFrom) OR i.Code < t.CodeFrom OR i.Code > t.CodeTo)
        THROW 50002, N'کد تفصیلی خارج از بازه کد نوع آن است.', 1;
END;
GO

-- قاعده هر معین: کدام سطح‌ها (۱ تا ۶) فعال‌اند و کدام الزامی
CREATE TABLE fin.AccountDetailLevels (
    AccountId  INT     NOT NULL CONSTRAINT FK_ADL_Account REFERENCES fin.Accounts (Id),
    LevelNo    TINYINT NOT NULL,
    IsRequired BIT     NOT NULL CONSTRAINT DF_ADL_IsRequired DEFAULT (1),
    CONSTRAINT PK_AccountDetailLevels PRIMARY KEY (AccountId, LevelNo),
    CONSTRAINT CK_ADL_LevelNo CHECK (LevelNo BETWEEN 1 AND 6)
);
GO

-- انواع تفصیلی مجاز در هر سطح؛ یک سطح می‌تواند چند نوع بپذیرد (مثلاً مشتری حقیقی و حقوقی)
CREATE TABLE fin.AccountDetailLevelTypes (
    AccountId    INT      NOT NULL,
    LevelNo      TINYINT  NOT NULL,
    DetailTypeId SMALLINT NOT NULL CONSTRAINT FK_ADLT_Type REFERENCES fin.DetailTypes (Id),
    CONSTRAINT PK_AccountDetailLevelTypes PRIMARY KEY (AccountId, LevelNo, DetailTypeId),
    CONSTRAINT FK_ADLT_Level FOREIGN KEY (AccountId, LevelNo)
        REFERENCES fin.AccountDetailLevels (AccountId, LevelNo) ON DELETE CASCADE
);
CREATE INDEX IX_ADLT_Type ON fin.AccountDetailLevelTypes (DetailTypeId);
GO

/* ---------------------------------------------------------------------
   fin: سال و دوره مالی
   --------------------------------------------------------------------- */
CREATE TABLE fin.FiscalYears (
    Id        SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FiscalYears PRIMARY KEY,
    Title     NVARCHAR(50) NOT NULL CONSTRAINT UQ_FiscalYears_Title UNIQUE,
    StartDate DATE         NOT NULL,
    EndDate   DATE         NOT NULL,
    [Status]  TINYINT      NOT NULL CONSTRAINT DF_FiscalYears_Status DEFAULT (1),   -- 1 باز، 2 قفل، 3 بسته
    CONSTRAINT CK_FiscalYears_Dates  CHECK (StartDate < EndDate),
    CONSTRAINT CK_FiscalYears_Status CHECK ([Status] IN (1, 2, 3))
);
GO

CREATE TABLE fin.FiscalPeriods (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FiscalPeriods PRIMARY KEY,
    FiscalYearId SMALLINT     NOT NULL CONSTRAINT FK_FiscalPeriods_Year REFERENCES fin.FiscalYears (Id),
    PeriodNo     TINYINT      NOT NULL,
    Title        NVARCHAR(50) NOT NULL,
    StartDate    DATE         NOT NULL,
    EndDate      DATE         NOT NULL,
    [Status]     TINYINT      NOT NULL CONSTRAINT DF_FiscalPeriods_Status DEFAULT (1),  -- 1 باز، 2 قفل، 3 بسته
    CONSTRAINT UQ_FiscalPeriods_No     UNIQUE (FiscalYearId, PeriodNo),
    CONSTRAINT CK_FiscalPeriods_Dates  CHECK (StartDate <= EndDate),
    CONSTRAINT CK_FiscalPeriods_Status CHECK ([Status] IN (1, 2, 3))
);
GO

/* ---------------------------------------------------------------------
   fin: سند حسابداری
   مسیر زیرسیستم‌ها: سربرگ با وضعیت پیش‌نویس ← درج ردیف‌ها ← تغییر وضعیت به موقت
   شماره قطعی (VoucherNo) فقط هنگام دائم شدن داده می‌شود؛ تا آن موقع TempNo
   --------------------------------------------------------------------- */
CREATE TABLE fin.Vouchers (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Vouchers PRIMARY KEY,
    FiscalYearId     SMALLINT      NOT NULL CONSTRAINT FK_Vouchers_Year REFERENCES fin.FiscalYears (Id),
    TempNo           INT           NOT NULL,
    VoucherNo        INT           NULL,
    VoucherDate      DATE          NOT NULL,
    VoucherType      TINYINT       NOT NULL CONSTRAINT DF_Vouchers_Type   DEFAULT (2),  -- 1 افتتاحیه، 2 عادی، 3 بستن حساب‌های موقت، 4 اختتامیه
    [Status]         TINYINT       NOT NULL CONSTRAINT DF_Vouchers_Status DEFAULT (1),  -- 1 پیش‌نویس، 2 موقت، 3 دائم، 9 باطل
    SourceModule     VARCHAR(30)   NULL,
    SourceDocType    VARCHAR(30)   NULL,
    SourceDocumentId BIGINT        NULL,
    [Description]    NVARCHAR(500) NULL,
    CreatedBy        INT           NOT NULL,
    CreatedAt        DATETIME2(0)  NOT NULL CONSTRAINT DF_Vouchers_CreatedAt DEFAULT (SYSUTCDATETIME()),
    ApprovedBy       INT           NULL,
    ApprovedAt       DATETIME2(0)  NULL,
    RowVer           ROWVERSION    NOT NULL,
    CONSTRAINT CK_Vouchers_TempNo CHECK (TempNo > 0),
    CONSTRAINT UQ_Vouchers_YearTempNo UNIQUE (FiscalYearId, TempNo),
    CONSTRAINT CK_Vouchers_Type   CHECK (VoucherType IN (1, 2, 3, 4)),
    CONSTRAINT CK_Vouchers_Status CHECK ([Status] IN (1, 2, 3, 9)),
    CONSTRAINT CK_Vouchers_No     CHECK (([Status] = 3 AND VoucherNo IS NOT NULL) OR ([Status] <> 3 AND VoucherNo IS NULL)),
    CONSTRAINT CK_Vouchers_Source CHECK ((SourceModule IS NULL AND SourceDocType IS NULL AND SourceDocumentId IS NULL)
                                      OR (SourceModule IS NOT NULL AND SourceDocType IS NOT NULL AND SourceDocumentId IS NOT NULL))
);
CREATE UNIQUE INDEX UX_Vouchers_No ON fin.Vouchers (FiscalYearId, VoucherNo) WHERE VoucherNo IS NOT NULL;
CREATE INDEX IX_Vouchers_Date ON fin.Vouchers (FiscalYearId, VoucherDate);
CREATE INDEX IX_Vouchers_Source ON fin.Vouchers (SourceModule, SourceDocType, SourceDocumentId) WHERE SourceDocumentId IS NOT NULL;
GO

CREATE TABLE fin.VoucherLines (
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VoucherLines PRIMARY KEY,
    VoucherId      BIGINT        NOT NULL CONSTRAINT FK_VoucherLines_Voucher REFERENCES fin.Vouchers (Id),
    RowNo          SMALLINT      NOT NULL,
    AccountId      INT           NOT NULL CONSTRAINT FK_VoucherLines_Account REFERENCES fin.Accounts (Id),
    Detail1Id      INT           NULL     CONSTRAINT FK_VoucherLines_D1 REFERENCES fin.DetailAccounts (Id),
    Detail2Id      INT           NULL     CONSTRAINT FK_VoucherLines_D2 REFERENCES fin.DetailAccounts (Id),
    Detail3Id      INT           NULL     CONSTRAINT FK_VoucherLines_D3 REFERENCES fin.DetailAccounts (Id),
    Detail4Id      INT           NULL     CONSTRAINT FK_VoucherLines_D4 REFERENCES fin.DetailAccounts (Id),
    Detail5Id      INT           NULL     CONSTRAINT FK_VoucherLines_D5 REFERENCES fin.DetailAccounts (Id),
    Detail6Id      INT           NULL     CONSTRAINT FK_VoucherLines_D6 REFERENCES fin.DetailAccounts (Id),
    Debit          DECIMAL(24,4) NOT NULL CONSTRAINT DF_VoucherLines_Debit  DEFAULT (0),
    Credit         DECIMAL(24,4) NOT NULL CONSTRAINT DF_VoucherLines_Credit DEFAULT (0),
    [Description]  NVARCHAR(500) NULL,
    CurrencyId     SMALLINT      NULL     CONSTRAINT FK_VoucherLines_Currency REFERENCES base.Currencies (Id),
    ExchangeRate   DECIMAL(19,6) NULL,
    CurrencyAmount DECIMAL(24,4) NULL,
    Quantity       DECIMAL(19,4) NULL,
    UnitId         SMALLINT      NULL     CONSTRAINT FK_VoucherLines_Unit REFERENCES base.Units (Id),
    FollowUpNo     NVARCHAR(50)  NULL,
    FollowUpDate   DATE          NULL,
    CONSTRAINT CK_VoucherLines_RowNo CHECK (RowNo > 0),
    CONSTRAINT UQ_VoucherLines_VoucherRow UNIQUE (VoucherId, RowNo),
    -- هر ردیف فقط بدهکار یا فقط بستانکار، بدون منفی و بدون صفر
    CONSTRAINT CK_VoucherLines_Amount   CHECK (Debit >= 0 AND Credit >= 0 AND (Debit = 0 OR Credit = 0) AND (Debit > 0 OR Credit > 0)),
    CONSTRAINT CK_VoucherLines_Currency CHECK ((CurrencyId IS NULL AND ExchangeRate IS NULL AND CurrencyAmount IS NULL)
                                            OR (CurrencyId IS NOT NULL AND ExchangeRate > 0 AND CurrencyAmount > 0)),
    CONSTRAINT CK_VoucherLines_Quantity CHECK ((Quantity IS NULL AND UnitId IS NULL) OR (Quantity IS NOT NULL AND UnitId IS NOT NULL)),
    -- سطح خالی بین دو سطح پر مجاز نیست
    CONSTRAINT CK_VoucherLines_DetailGap CHECK ((Detail2Id IS NULL OR Detail1Id IS NOT NULL)
                                             AND (Detail3Id IS NULL OR Detail2Id IS NOT NULL)
                                             AND (Detail4Id IS NULL OR Detail3Id IS NOT NULL)
                                             AND (Detail5Id IS NULL OR Detail4Id IS NOT NULL)
                                             AND (Detail6Id IS NULL OR Detail5Id IS NOT NULL))
);
CREATE INDEX IX_VoucherLines_AccountDetail ON fin.VoucherLines (AccountId, Detail1Id) INCLUDE (VoucherId, Debit, Credit);
-- ایندکس ستونی برای گزارش‌های تجمیعی (تراز، گردش تفصیلی، داشبورد)
CREATE NONCLUSTERED COLUMNSTORE INDEX NCCI_VoucherLines ON fin.VoucherLines
    (VoucherId, AccountId, Detail1Id, Detail2Id, Detail3Id, Detail4Id, Detail5Id, Detail6Id,
     Debit, Credit, CurrencyId, CurrencyAmount, Quantity);
GO

-- تاریخ داخل سال و دوره باز، و تراز بودن سند هنگام موقت یا دائم شدن
CREATE TRIGGER fin.trg_Vouchers_Validate ON fin.Vouchers AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN fin.FiscalYears y ON y.Id = i.FiscalYearId
               WHERE i.VoucherDate < y.StartDate OR i.VoucherDate > y.EndDate)
        THROW 50014, N'تاریخ سند خارج از سال مالی است.', 1;

    IF EXISTS (SELECT 1 FROM inserted i JOIN fin.FiscalYears y ON y.Id = i.FiscalYearId
               WHERE y.[Status] <> 1)
        THROW 50015, N'سال مالی سند باز نیست.', 1;

    IF EXISTS (SELECT 1 FROM inserted i JOIN fin.FiscalPeriods p
                 ON p.FiscalYearId = i.FiscalYearId AND i.VoucherDate BETWEEN p.StartDate AND p.EndDate
               WHERE p.[Status] <> 1)
        THROW 50016, N'دوره مالی تاریخ سند باز نیست.', 1;

    IF EXISTS (SELECT 1 FROM inserted i
               WHERE i.[Status] IN (2, 3)
                 AND NOT EXISTS (SELECT 1 FROM fin.VoucherLines l
                                 WHERE l.VoucherId = i.Id
                                 GROUP BY l.VoucherId
                                 HAVING COUNT(*) >= 2 AND SUM(l.Debit) = SUM(l.Credit)))
        THROW 50013, N'سند موقت یا دائم باید حداقل دو ردیف داشته باشد و تراز باشد.', 1;
END;
GO

-- سند دائم هیچ‌وقت ویرایش یا حذف نمی‌شود
CREATE TRIGGER fin.trg_Vouchers_Protect ON fin.Vouchers AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted WHERE [Status] = 3)
        THROW 50010, N'سند دائم قابل ویرایش یا حذف نیست؛ اصلاح فقط با سند اصلاحی.', 1;
END;
GO

CREATE TRIGGER fin.trg_VoucherLines_Validate ON fin.VoucherLines AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1
               FROM (SELECT VoucherId FROM inserted UNION SELECT VoucherId FROM deleted) x
               JOIN fin.Vouchers v ON v.Id = x.VoucherId
               WHERE v.[Status] = 3)
        THROW 50011, N'ردیف‌های سند دائم قابل تغییر نیستند.', 1;

    IF EXISTS (SELECT 1 FROM inserted i JOIN fin.Accounts a ON a.Id = i.AccountId
               WHERE a.[Level] <> 3)
        THROW 50012, N'ثبت سند فقط در حساب معین مجاز است.', 1;
END;
GO

/* ---------------------------------------------------------------------
   fin: الگوی صدور سند خودکار از زیرسیستم‌ها
   --------------------------------------------------------------------- */
CREATE TABLE fin.PostingTemplates (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PostingTemplates PRIMARY KEY,
    SourceDocType   VARCHAR(30)   NOT NULL CONSTRAINT UQ_PostingTemplates_DocType UNIQUE,
    Title           NVARCHAR(100) NOT NULL,
    AggregationMode TINYINT       NOT NULL CONSTRAINT DF_PostingTemplates_Agg      DEFAULT (1),  -- 1 به ازای هر سند منبع، 2 تجمیعی روزانه
    IsActive        BIT           NOT NULL CONSTRAINT DF_PostingTemplates_IsActive DEFAULT (1),
    CONSTRAINT CK_PostingTemplates_Agg CHECK (AggregationMode IN (1, 2))
);
GO

-- هر کلید ردیف (طرف حساب، فروش، مالیات، تخفیف...) به یک معین؛ هر سطح تفصیلی از یک فیلد سند منبع
CREATE TABLE fin.PostingTemplateLines (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PostingTemplateLines PRIMARY KEY,
    TemplateId    INT         NOT NULL CONSTRAINT FK_PTL_Template REFERENCES fin.PostingTemplates (Id) ON DELETE CASCADE,
    LineKey       VARCHAR(30) NOT NULL,
    AccountId     INT         NOT NULL CONSTRAINT FK_PTL_Account  REFERENCES fin.Accounts (Id),
    DetailSource1 VARCHAR(50) NULL,
    DetailSource2 VARCHAR(50) NULL,
    DetailSource3 VARCHAR(50) NULL,
    DetailSource4 VARCHAR(50) NULL,
    DetailSource5 VARCHAR(50) NULL,
    DetailSource6 VARCHAR(50) NULL,
    CONSTRAINT UQ_PTL_TemplateKey UNIQUE (TemplateId, LineKey)
);
GO

/* ---------------------------------------------------------------------
   audit: ردپای تغییرات (کاربر، دستیار هوش مصنوعی یا سیستم)
   --------------------------------------------------------------------- */
CREATE TABLE audit.ChangeLog (
    Id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChangeLog PRIMARY KEY,
    EntityName VARCHAR(50)   NOT NULL,
    EntityId   BIGINT        NOT NULL,
    [Action]   CHAR(1)       NOT NULL,            -- I درج، U ویرایش، D حذف، A تأیید
    OldValue   NVARCHAR(MAX) NULL,                -- JSON
    NewValue   NVARCHAR(MAX) NULL,                -- JSON
    ActorType  TINYINT       NOT NULL,            -- 1 کاربر، 2 دستیار هوش مصنوعی، 3 سیستم
    ActorId    INT           NULL,
    Reason     NVARCHAR(500) NULL,
    [At]       DATETIME2(3)  NOT NULL CONSTRAINT DF_ChangeLog_At DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_ChangeLog_Action CHECK ([Action] IN ('I', 'U', 'D', 'A')),
    CONSTRAINT CK_ChangeLog_Actor  CHECK (ActorType IN (1, 2, 3)),
    CONSTRAINT CK_ChangeLog_Json   CHECK ((OldValue IS NULL OR ISJSON(OldValue) = 1) AND (NewValue IS NULL OR ISJSON(NewValue) = 1))
);
CREATE INDEX IX_ChangeLog_Entity ON audit.ChangeLog (EntityName, EntityId, [At]);
GO

/* ---------------------------------------------------------------------
   نماها برای گزارش و Power BI
   --------------------------------------------------------------------- */
-- جمع هر سند؛ سندهای ناتراز را با TotalDebit <> TotalCredit پیدا کنید
CREATE VIEW fin.vVoucherTotals AS
SELECT v.Id AS VoucherId, v.FiscalYearId, v.TempNo, v.VoucherNo, v.VoucherDate, v.[Status],
       COUNT(l.Id)               AS LineCount,
       ISNULL(SUM(l.Debit), 0)   AS TotalDebit,
       ISNULL(SUM(l.Credit), 0)  AS TotalCredit
FROM fin.Vouchers v
LEFT JOIN fin.VoucherLines l ON l.VoucherId = v.Id
GROUP BY v.Id, v.FiscalYearId, v.TempNo, v.VoucherNo, v.VoucherDate, v.[Status];
GO

-- دفتر: هر ردیف سند موقت یا دائم با گروه، کل، معین و تاریخ شمسی
CREATE VIEW fin.vLedger AS
SELECT l.Id AS LineId, v.Id AS VoucherId, v.FiscalYearId, v.TempNo, v.VoucherNo, v.VoucherType,
       v.[Status] AS VoucherStatus, v.VoucherDate,
       c.PersianDate, c.PersianYear, c.PersianMonth,
       g.Code AS GroupCode, g.Title AS GroupTitle,
       k.Code AS KolCode,   k.Title AS KolTitle,
       a.Id   AS AccountId, a.Code AS MoeinCode, a.Title AS MoeinTitle,
       l.Detail1Id, l.Detail2Id, l.Detail3Id, l.Detail4Id, l.Detail5Id, l.Detail6Id,
       l.Debit, l.Credit, l.Debit - l.Credit AS Balance,
       l.CurrencyId, l.CurrencyAmount, l.Quantity, l.UnitId,
       ISNULL(l.[Description], v.[Description]) AS [Description]
FROM fin.VoucherLines l
JOIN fin.Vouchers v ON v.Id = l.VoucherId
JOIN fin.Accounts a ON a.Id = l.AccountId
JOIN fin.Accounts k ON k.Id = a.ParentId
JOIN fin.Accounts g ON g.Id = k.ParentId
LEFT JOIN base.Calendar c ON c.[Date] = v.VoucherDate
WHERE v.[Status] IN (2, 3);
GO

/* ---------------------------------------------------------------------
   ساخت سال مالی با ۱۲ دوره ماهانه از روی تقویم شمسی
   مثال: EXEC fin.usp_CreateFiscalYear @PersianYear = 1405;
         EXEC fin.usp_CreateFiscalYear @PersianYear = 1405, @StartMonth = 4;  -- تیر ۱۴۰۵ تا خرداد ۱۴۰۶
   --------------------------------------------------------------------- */
CREATE PROCEDURE fin.usp_CreateFiscalYear
    @PersianYear SMALLINT,
    @StartMonth  TINYINT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @StartMonth NOT BETWEEN 1 AND 12
        THROW 50020, N'ماه شروع باید بین ۱ تا ۱۲ باشد.', 1;

    DECLARE @Periods TABLE (
        PeriodNo  TINYINT  NOT NULL PRIMARY KEY,
        PYear     SMALLINT NOT NULL,
        PMonth    TINYINT  NOT NULL,
        StartDate DATE     NULL,
        EndDate   DATE     NULL
    );

    INSERT @Periods (PeriodNo, PYear, PMonth)
    SELECT n.k,
           @PersianYear + (CAST(@StartMonth AS INT) + n.k - 2) / 12,
           (CAST(@StartMonth AS INT) + n.k - 2) % 12 + 1
    FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) AS n(k);

    UPDATE p
    SET StartDate = m.StartDate, EndDate = m.EndDate
    FROM @Periods p
    JOIN (SELECT PersianYear, PersianMonth, MIN([Date]) AS StartDate, MAX([Date]) AS EndDate
          FROM base.Calendar
          GROUP BY PersianYear, PersianMonth) m
      ON m.PersianYear = p.PYear AND m.PersianMonth = p.PMonth;

    IF EXISTS (SELECT 1 FROM @Periods WHERE StartDate IS NULL)
        THROW 50021, N'تقویم شمسی برای این بازه بارگذاری نشده است.', 1;

    DECLARE @Start DATE = (SELECT MIN(StartDate) FROM @Periods);
    DECLARE @End   DATE = (SELECT MAX(EndDate)   FROM @Periods);

    IF EXISTS (SELECT 1 FROM fin.FiscalYears WHERE StartDate <= @End AND EndDate >= @Start)
        THROW 50022, N'این بازه با یک سال مالی موجود هم‌پوشانی دارد.', 1;

    DECLARE @Title NVARCHAR(50) =
        CASE WHEN @StartMonth = 1 THEN CAST(@PersianYear AS NVARCHAR(10))
             ELSE CONCAT(@PersianYear, N'-', @PersianYear + 1) END;
    DECLARE @YearId SMALLINT;

    BEGIN TRANSACTION;

    INSERT fin.FiscalYears (Title, StartDate, EndDate) VALUES (@Title, @Start, @End);
    SET @YearId = CAST(SCOPE_IDENTITY() AS SMALLINT);

    INSERT fin.FiscalPeriods (FiscalYearId, PeriodNo, Title, StartDate, EndDate)
    SELECT @YearId, PeriodNo,
           CONCAT(CHOOSE(PMonth, N'فروردین', N'اردیبهشت', N'خرداد', N'تیر', N'مرداد', N'شهریور',
                                 N'مهر', N'آبان', N'آذر', N'دی', N'بهمن', N'اسفند'), N' ', PYear),
           StartDate, EndDate
    FROM @Periods;

    COMMIT TRANSACTION;

    SELECT @YearId AS FiscalYearId, @Title AS Title, @Start AS StartDate, @End AS EndDate;
END;
GO
