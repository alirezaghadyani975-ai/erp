/* =====================================================================
   05 - اشخاص کامل، نقش‌ها و تفصیلی مشترک اشخاص

   تصمیم طراحی:
   - هر شخص (مشتری، تأمین‌کننده، پیمانکار، سهامدار، شرکت وابسته، ...) فقط یک تفصیلی مشترک
     در گروه «اشخاص حقیقی» یا «اشخاص حقوقی» دارد تا مانده کامل او یک‌جا دیده شود.
   - نقش‌ها فقط برچسب‌اند (base.PersonRoles) و برای فیلتر فهرست اشخاص در فرم‌ها به کار می‌روند.
   - نقش «کارمند» استثناست: تفصیلی جدا در گروه «جاری کارکنان» (مالک: حقوق و دستمزد) می‌گیرد.
   - در قاعده هر سطح تفصیلی معین، نقش‌های مجاز (fin.AccountDetailLevelRoles) فهرست را فیلتر می‌کنند
     (فیلتر نرم: ثبت شخص بدون آن نقش ممنوع نیست).

   این اسکریپت انواع تفصیلی قدیمی مشتری/تأمین‌کننده/سهامدار/شرکت وابسته را در دو گروه مشترک ادغام
   می‌کند؛ اگر شخصی چند تفصیلی داشت، ردیف‌های سند به تفصیلی باقی‌مانده او منتقل می‌شوند.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE SCHEMA hr;   -- اطلاعات پرسنلی (ماژول حقوق و دستمزد)
GO

/* ---------------------------------------------------------------------
   ۱) اطلاعات تکمیلی اشخاص
   --------------------------------------------------------------------- */
ALTER TABLE base.Persons ADD
    -- حقیقی
    FirstName           NVARCHAR(100) NULL,
    LastName            NVARCHAR(100) NULL,
    FatherName          NVARCHAR(100) NULL,
    Gender              TINYINT       NULL,   -- 1 مرد، 2 زن
    BirthDate           DATE          NULL,
    BirthPlace          NVARCHAR(100) NULL,
    IdNumber            VARCHAR(10)   NULL,   -- شماره شناسنامه
    IdIssuePlace        NVARCHAR(100) NULL,   -- محل صدور
    MaritalStatus       TINYINT       NULL,   -- 1 مجرد، 2 متأهل
    IsForeign           BIT           NOT NULL CONSTRAINT DF_Persons_IsForeign DEFAULT (0),
    Nationality         NVARCHAR(50)  NULL,
    ForeignNationalCode VARCHAR(12)   NULL,   -- کد فراگیر اتباع
    PassportNo          VARCHAR(20)   NULL,
    -- حقوقی
    LegalType           TINYINT       NULL,   -- نوع شرکت (جدول در README)
    RegistrationNo      NVARCHAR(30)  NULL,
    RegistrationDate    DATE          NULL,
    RegistrationPlace   NVARCHAR(100) NULL,
    -- مالیات و سامانه مودیان
    TaxPayerType        TINYINT       NULL,   -- 1 حقیقی، 2 حقوقی، 3 مشارکت مدنی، 4 اتباع غیرایرانی، 5 مصرف‌کننده نهایی
    BranchCode          VARCHAR(4)    NULL,   -- کد شعبه در سامانه مودیان
    -- تماس
    Mobile              VARCHAR(11)   NULL,
    Phone               VARCHAR(11)   NULL,
    Email               VARCHAR(100)  NULL,
    Website             VARCHAR(200)  NULL,
    Notes               NVARCHAR(500) NULL,
    CreatedAt           DATETIME2(0)  NOT NULL CONSTRAINT DF_Persons_CreatedAt DEFAULT (SYSUTCDATETIME());
GO
ALTER TABLE base.Persons ADD
    CONSTRAINT CK_Persons_Gender        CHECK (Gender IN (1, 2)),
    CONSTRAINT CK_Persons_Marital       CHECK (MaritalStatus IN (1, 2)),
    CONSTRAINT CK_Persons_TaxPayerType  CHECK (TaxPayerType BETWEEN 1 AND 5),
    CONSTRAINT CK_Persons_LegalType     CHECK (LegalType BETWEEN 1 AND 20);
CREATE UNIQUE INDEX UX_Persons_ForeignCode ON base.Persons (ForeignNationalCode) WHERE ForeignNationalCode IS NOT NULL;
CREATE INDEX IX_Persons_Name ON base.Persons (DisplayName);
GO

CREATE TABLE base.PersonAddresses (
    Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PersonAddresses PRIMARY KEY,
    PersonId   INT           NOT NULL CONSTRAINT FK_PersonAddresses_Person REFERENCES base.Persons (Id) ON DELETE CASCADE,
    Title      NVARCHAR(50)  NULL,       -- دفتر مرکزی، کارخانه، منزل، ...
    Province   NVARCHAR(50)  NULL,
    City       NVARCHAR(50)  NULL,
    [Address]  NVARCHAR(400) NOT NULL,
    PostalCode VARCHAR(10)   NULL,
    Phone      VARCHAR(11)   NULL,
    IsDefault  BIT           NOT NULL CONSTRAINT DF_PersonAddresses_IsDefault DEFAULT (0)
);
CREATE INDEX IX_PersonAddresses_Person ON base.PersonAddresses (PersonId);
GO

CREATE TABLE base.PersonBankAccounts (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PersonBankAccounts PRIMARY KEY,
    PersonId  INT          NOT NULL CONSTRAINT FK_PersonBankAccounts_Person REFERENCES base.Persons (Id) ON DELETE CASCADE,
    BankName  NVARCHAR(50) NULL,
    Iban      VARCHAR(26)  NULL,          -- شبا: IR + ۲۴ رقم
    AccountNo VARCHAR(30)  NULL,
    CardNo    VARCHAR(16)  NULL,
    IsDefault BIT          NOT NULL CONSTRAINT DF_PersonBankAccounts_IsDefault DEFAULT (0),
    CONSTRAINT CK_PersonBankAccounts_Any CHECK (Iban IS NOT NULL OR AccountNo IS NOT NULL OR CardNo IS NOT NULL)
);
CREATE INDEX IX_PersonBankAccounts_Person ON base.PersonBankAccounts (PersonId);
GO

/* اطلاعات پرسنلی (حقوق و دستمزد، بیمه، مالیات حقوق). فقط برای اشخاص با نقش کارمند. */
CREATE TABLE hr.Employees (
    PersonId           INT           NOT NULL CONSTRAINT PK_Employees PRIMARY KEY
                                     CONSTRAINT FK_Employees_Person REFERENCES base.Persons (Id) ON DELETE CASCADE,
    EmployeeNo         VARCHAR(20)   NULL,
    HireDate           DATE          NULL,
    EndDate            DATE          NULL,
    ContractType       TINYINT       NULL,  -- 1 دائم، 2 قراردادی، 3 پیمانی، 4 ساعتی، 5 پاره‌وقت، 6 کارآموز
    JobTitle           NVARCHAR(100) NULL,
    InsuranceType      TINYINT       NULL,  -- 1 تأمین اجتماعی، 2 خدمات درمانی، 3 نیروهای مسلح، 4 سایر، 9 بدون بیمه
    InsuranceNo        VARCHAR(10)   NULL,
    InsuranceStartDate DATE          NULL,
    InsuranceJobCode   VARCHAR(10)   NULL,  -- کد شغل بیمه
    WorkshopCode       VARCHAR(10)   NULL,  -- کد کارگاه
    EducationLevel     TINYINT       NULL,  -- 1 زیر دیپلم، 2 دیپلم، 3 کاردانی، 4 کارشناسی، 5 کارشناسی ارشد، 6 دکترا
    EducationField     NVARCHAR(100) NULL,
    MilitaryStatus     TINYINT       NULL,  -- 1 پایان خدمت، 2 معاف، 3 مشمول، 4 در حال خدمت
    ChildrenCount      TINYINT       NOT NULL CONSTRAINT DF_Employees_Children DEFAULT (0),
    DependentsCount    TINYINT       NOT NULL CONSTRAINT DF_Employees_Dependents DEFAULT (0),
    TaxExemptionType   TINYINT       NULL,  -- 1 ایثارگر/جانباز، 2 آزاده، 3 فرزند شهید، 4 مناطق محروم، 9 سایر
    CONSTRAINT CK_Employees_Dates CHECK (EndDate IS NULL OR HireDate IS NULL OR EndDate >= HireDate)
);
CREATE UNIQUE INDEX UX_Employees_No ON hr.Employees (EmployeeNo) WHERE EmployeeNo IS NOT NULL;
GO

/* ---------------------------------------------------------------------
   ۲) نقش‌ها
   --------------------------------------------------------------------- */
CREATE TABLE base.PersonRoleTypes (
    Id        SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PersonRoleTypes PRIMARY KEY,
    Code      VARCHAR(30)  NULL CONSTRAINT UQ_PersonRoleTypes_Code UNIQUE,  -- نقش‌های سیستمی
    Title     NVARCHAR(50) NOT NULL CONSTRAINT UQ_PersonRoleTypes_Title UNIQUE,
    IsSystem  BIT          NOT NULL CONSTRAINT DF_PersonRoleTypes_IsSystem DEFAULT (0),
    IsActive  BIT          NOT NULL CONSTRAINT DF_PersonRoleTypes_IsActive DEFAULT (1),
    SortOrder SMALLINT     NOT NULL CONSTRAINT DF_PersonRoleTypes_Sort DEFAULT (100)
);
GO
INSERT base.PersonRoleTypes (Code, Title, IsSystem, SortOrder) VALUES
    ('customer',    N'مشتری',          1, 1),
    ('supplier',    N'تأمین‌کننده',     1, 2),
    ('contractor',  N'پیمانکار',        1, 3),
    ('shareholder', N'سهامدار',         1, 4),
    ('affiliate',   N'شرکت وابسته',     1, 5),
    ('employee',    N'کارمند',          1, 6),
    ('other',       N'سایر طرف‌حساب‌ها', 1, 9);
GO

CREATE TABLE base.PersonRoles (
    PersonId INT      NOT NULL CONSTRAINT FK_PersonRoles_Person REFERENCES base.Persons (Id) ON DELETE CASCADE,
    RoleId   SMALLINT NOT NULL CONSTRAINT FK_PersonRoles_Role   REFERENCES base.PersonRoleTypes (Id),
    CONSTRAINT PK_PersonRoles PRIMARY KEY (PersonId, RoleId)
);
CREATE INDEX IX_PersonRoles_Role ON base.PersonRoles (RoleId);
GO

/* نقش‌های مجاز هر سطح تفصیلی معین (برای فیلتر فهرست اشخاص در ردیف سند) */
CREATE TABLE fin.AccountDetailLevelRoles (
    AccountId INT      NOT NULL,
    LevelNo   TINYINT  NOT NULL,
    RoleId    SMALLINT NOT NULL CONSTRAINT FK_ADLR_Role REFERENCES base.PersonRoleTypes (Id),
    CONSTRAINT PK_AccountDetailLevelRoles PRIMARY KEY (AccountId, LevelNo, RoleId),
    CONSTRAINT FK_ADLR_Level FOREIGN KEY (AccountId, LevelNo)
        REFERENCES fin.AccountDetailLevels (AccountId, LevelNo) ON DELETE CASCADE
);
GO

/* ---------------------------------------------------------------------
   ۳) کنترل تغییر بازه نوع تفصیلی: کدهای موجود نباید بیرون بمانند
   --------------------------------------------------------------------- */
CREATE OR ALTER TRIGGER fin.trg_DetailTypes_Overlap ON fin.DetailTypes AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN fin.DetailTypes t
                 ON t.Id <> i.Id AND LEN(t.CodeFrom) = LEN(i.CodeFrom)
                AND t.CodeFrom <= i.CodeTo AND i.CodeFrom <= t.CodeTo)
        THROW 50003, N'بازه کد این نوع تفصیلی با نوع دیگری هم‌پوشانی دارد.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN fin.DetailAccounts d ON d.DetailTypeId = i.Id
               WHERE LEN(d.Code) <> LEN(i.CodeFrom) OR d.Code < i.CodeFrom OR d.Code > i.CodeTo)
        THROW 50004, N'تفصیلی‌هایی با کد خارج از بازه جدید وجود دارند؛ بازه را نمی‌توان این‌قدر کوچک کرد.', 1;
END;
GO

/* ---------------------------------------------------------------------
   ۴) ادغام انواع تفصیلی اشخاص در دو گروه مشترک
   هر شخص یک تفصیلی باقی‌مانده (کوچک‌ترین شناسه) می‌گیرد؛ ردیف‌های سندی که به تفصیلی‌های دیگر
   همان شخص اشاره داشتند به آن منتقل می‌شوند. تریگرهای پشتیبان در این مرحله موقتاً خاموش‌اند
   و نتیجه در پایان دوباره کنترل می‌شود.
   --------------------------------------------------------------------- */
DECLARE @Map TABLE (OldTypeId SMALLINT PRIMARY KEY, RoleCode VARCHAR(30) NOT NULL, Kind TINYINT NULL);
INSERT @Map (OldTypeId, RoleCode, Kind)
SELECT t.Id,
       CASE WHEN t.Title LIKE N'%مشتری%'      THEN 'customer'
            WHEN t.Title LIKE N'%تامین%' OR t.Title LIKE N'%تأمین%' THEN 'supplier'
            WHEN t.Title LIKE N'%سهامدار%'    THEN 'shareholder'
            ELSE 'affiliate' END,
       t.PersonKind
FROM fin.DetailTypes t
WHERE t.OwnerModule = 'persons' OR t.Title LIKE N'%سهامدار%';

-- نقش‌های اشخاص موجود از روی نوع تفصیلی‌هایشان
DECLARE @PersonRoles TABLE (PersonId INT NOT NULL, RoleId SMALLINT NOT NULL, PRIMARY KEY (PersonId, RoleId));
INSERT @PersonRoles
SELECT DISTINCT d.PersonId, r.Id
FROM fin.DetailAccounts d
JOIN @Map m ON m.OldTypeId = d.DetailTypeId
JOIN base.PersonRoleTypes r ON r.Code = m.RoleCode
WHERE d.PersonId IS NOT NULL;

INSERT @PersonRoles
SELECT DISTINCT d.PersonId, r.Id
FROM fin.DetailAccounts d
JOIN fin.DetailTypes t ON t.Id = d.DetailTypeId AND t.OwnerModule = 'payroll'
JOIN base.PersonRoleTypes r ON r.Code = 'employee'
WHERE d.PersonId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM @PersonRoles x WHERE x.PersonId = d.PersonId AND x.RoleId = r.Id);

-- قواعد معین‌هایی که نوع‌های قدیمی را داشتند
DECLARE @Rules TABLE (AccountId INT, LevelNo TINYINT, Kind TINYINT NULL, RoleCode VARCHAR(30));
INSERT @Rules
SELECT lt.AccountId, lt.LevelNo, m.Kind, m.RoleCode
FROM fin.AccountDetailLevelTypes lt JOIN @Map m ON m.OldTypeId = lt.DetailTypeId;

-- تفصیلی باقی‌مانده هر شخص و تفصیلی‌هایی که در آن ادغام می‌شوند
DECLARE @Redirect TABLE (OldId INT PRIMARY KEY, KeepId INT NOT NULL);
INSERT @Redirect
SELECT d.Id, MIN(d.Id) OVER (PARTITION BY d.PersonId)
FROM fin.DetailAccounts d
WHERE d.PersonId IS NOT NULL AND d.DetailTypeId IN (SELECT OldTypeId FROM @Map);

DISABLE TRIGGER fin.trg_DetailTypes_Overlap ON fin.DetailTypes;
DISABLE TRIGGER fin.trg_DetailAccounts_Range ON fin.DetailAccounts;
DISABLE TRIGGER fin.trg_VoucherLines_Validate ON fin.VoucherLines;

UPDATE l SET Detail1Id = r.KeepId FROM fin.VoucherLines l JOIN @Redirect r ON r.OldId = l.Detail1Id AND r.OldId <> r.KeepId;
UPDATE l SET Detail2Id = r.KeepId FROM fin.VoucherLines l JOIN @Redirect r ON r.OldId = l.Detail2Id AND r.OldId <> r.KeepId;
UPDATE l SET Detail3Id = r.KeepId FROM fin.VoucherLines l JOIN @Redirect r ON r.OldId = l.Detail3Id AND r.OldId <> r.KeepId;
UPDATE l SET Detail4Id = r.KeepId FROM fin.VoucherLines l JOIN @Redirect r ON r.OldId = l.Detail4Id AND r.OldId <> r.KeepId;
UPDATE l SET Detail5Id = r.KeepId FROM fin.VoucherLines l JOIN @Redirect r ON r.OldId = l.Detail5Id AND r.OldId <> r.KeepId;
UPDATE l SET Detail6Id = r.KeepId FROM fin.VoucherLines l JOIN @Redirect r ON r.OldId = l.Detail6Id AND r.OldId <> r.KeepId;

DELETE fin.DetailAccounts WHERE Id IN (SELECT OldId FROM @Redirect WHERE OldId <> KeepId);
UPDATE fin.DetailAccounts SET Code = 'T' + CAST(Id AS VARCHAR(9)) WHERE Id IN (SELECT KeepId FROM @Redirect);
DELETE fin.AccountDetailLevelTypes WHERE DetailTypeId IN (SELECT OldTypeId FROM @Map);

INSERT fin.DetailTypes (Title, OwnerModule, PersonKind, CodeFrom, CodeTo) VALUES
    (N'اشخاص حقیقی', 'persons', 1, '01001', '03999'),
    (N'اشخاص حقوقی', 'persons', 2, '04001', '06999');

DECLARE @Natural SMALLINT = (SELECT Id FROM fin.DetailTypes WHERE Title = N'اشخاص حقیقی');
DECLARE @Legal   SMALLINT = (SELECT Id FROM fin.DetailTypes WHERE Title = N'اشخاص حقوقی');

-- تفصیلی‌های باقی‌مانده به گروه مشترک با کد جدید پشت سر هم
WITH k AS (
    SELECT d.Id, t.Id AS TypeId, t.CodeFrom,
           ROW_NUMBER() OVER (PARTITION BY t.Id ORDER BY d.PersonId) AS rn
    FROM fin.DetailAccounts d
    JOIN base.Persons p ON p.Id = d.PersonId
    JOIN fin.DetailTypes t ON t.Id = CASE p.PersonKind WHEN 1 THEN @Natural ELSE @Legal END
    WHERE d.Id IN (SELECT KeepId FROM @Redirect)
)
UPDATE d SET DetailTypeId = k.TypeId, SourceModule = 'persons', SourceEntityId = d.PersonId,
             Code = RIGHT(REPLICATE('0', LEN(k.CodeFrom))
                          + CAST(CAST(k.CodeFrom AS BIGINT) + k.rn - 1 AS VARCHAR(20)), LEN(k.CodeFrom))
FROM fin.DetailAccounts d JOIN k ON k.Id = d.Id;

-- نوع‌های قدیمی بدون تفصیلی حذف می‌شوند؛ اگر نوعی (مثل سهامداران دستی) تفصیلی بدون شخص داشت می‌ماند
DELETE fin.DetailTypes
WHERE Id IN (SELECT OldTypeId FROM @Map)
  AND NOT EXISTS (SELECT 1 FROM fin.DetailAccounts d WHERE d.DetailTypeId = fin.DetailTypes.Id);
UPDATE fin.DetailTypes SET OwnerModule = 'manual', PersonKind = NULL
WHERE Id IN (SELECT OldTypeId FROM @Map) AND OwnerModule = 'persons';

ENABLE TRIGGER fin.trg_DetailTypes_Overlap ON fin.DetailTypes;
ENABLE TRIGGER fin.trg_DetailAccounts_Range ON fin.DetailAccounts;
ENABLE TRIGGER fin.trg_VoucherLines_Validate ON fin.VoucherLines;

-- کنترل نهایی همان قواعد تریگرها
IF EXISTS (SELECT 1 FROM fin.DetailTypes a JOIN fin.DetailTypes b
             ON a.Id < b.Id AND LEN(a.CodeFrom) = LEN(b.CodeFrom) AND a.CodeFrom <= b.CodeTo AND b.CodeFrom <= a.CodeTo)
    THROW 50003, N'بعد از ادغام، بازه کد دو نوع تفصیلی هم‌پوشانی دارد.', 1;
IF EXISTS (SELECT 1 FROM fin.DetailAccounts d JOIN fin.DetailTypes t ON t.Id = d.DetailTypeId
           WHERE LEN(d.Code) <> LEN(t.CodeFrom) OR d.Code < t.CodeFrom OR d.Code > t.CodeTo)
    THROW 50002, N'بعد از ادغام، کد تفصیلی خارج از بازه نوعش ماند (بازه اشخاص پر است؟).', 1;

-- قواعد معین‌ها: نوع مشترک + نقش
INSERT fin.AccountDetailLevelTypes (AccountId, LevelNo, DetailTypeId)
SELECT DISTINCT r.AccountId, r.LevelNo, x.TypeId
FROM @Rules r
CROSS APPLY (SELECT @Natural AS TypeId WHERE ISNULL(r.Kind, 1) = 1
             UNION SELECT @Legal WHERE ISNULL(r.Kind, 2) = 2) x;

INSERT fin.AccountDetailLevelRoles (AccountId, LevelNo, RoleId)
SELECT DISTINCT r.AccountId, r.LevelNo, rt.Id
FROM @Rules r JOIN base.PersonRoleTypes rt ON rt.Code = r.RoleCode;

-- نقش اشخاص موجود؛ کارمندان ردیف اطلاعات پرسنلی خالی می‌گیرند
INSERT base.PersonRoles (PersonId, RoleId) SELECT PersonId, RoleId FROM @PersonRoles;

INSERT hr.Employees (PersonId)
SELECT pr.PersonId FROM @PersonRoles pr JOIN base.PersonRoleTypes rt ON rt.Id = pr.RoleId AND rt.Code = 'employee';
GO

PRINT N'اشخاص کامل، نقش‌ها و تفصیلی مشترک اشخاص اضافه شد.';
