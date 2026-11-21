/* =====================================================================
   20 - کارگزینی پایه (schema hr) و پایه‌های حقوق و دستمزد (schema pay) — مرحله ۱ طرح docs/payroll-design.md

   - hr: شغل (با کد شغل تأمین اجتماعی)، کارگاه بیمه (نرخ‌های بیمه کارگاه)، پرونده پرسنلی (شخص با نقش کارمند،
     مرکز هزینه پیش‌فرض، وضعیت بیمه، فرزندان، شبا، ترک کار؛ وضعیت تأهل از خود شخص).
   - pay: تنظیمات (روزهای مزد ماه)، پارامترهای قانونی هر سال (کد ← مقدار؛ متغیر فرمول‌ها)، اقلام حقوق با فرمول،
     حکم حقوقی با مقدار اقلام حکمی. حقوق فقط از راه Contracts به کارگزینی وصل است.
   - پیش‌فرض ۱۴۰۵ از مصوبه شورای عالی کار (۱۴۰۴/۱۲/۲۴) و بخشنامه دستمزد تأمین اجتماعی طبق اخبار؛ باید با متن رسمی تطبیق شود.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

-- schema hr را اسکریپت ۰۵ ساخته و جدولش را ۰۶ حذف کرده است
IF SCHEMA_ID('hr') IS NULL EXEC (N'CREATE SCHEMA hr');
GO
CREATE SCHEMA pay;
GO

/* ---------------------------------------------------------------------
   ۱) کارگزینی پایه
   --------------------------------------------------------------------- */
CREATE TABLE hr.Jobs (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Jobs PRIMARY KEY,
    Title            NVARCHAR(100) NOT NULL CONSTRAINT UQ_Jobs_Title UNIQUE,
    InsuranceJobCode VARCHAR(6)    NULL,          -- کد شغل در لیست بیمه تأمین اجتماعی
    IsActive         BIT           NOT NULL CONSTRAINT DF_Jobs_IsActive DEFAULT (1),
    CONSTRAINT CK_Jobs_Code CHECK (InsuranceJobCode IS NULL OR InsuranceJobCode NOT LIKE '%[^0-9]%')
);
GO

CREATE TABLE hr.Workshops (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Workshops PRIMARY KEY,
    Code              VARCHAR(10)   NOT NULL CONSTRAINT UQ_Workshops_Code UNIQUE,   -- کد کارگاه تأمین اجتماعی
    Title             NVARCHAR(100) NOT NULL,
    EmployerName      NVARCHAR(100) NULL,                                           -- نام کارفرما در لیست بیمه
    Branch            NVARCHAR(100) NULL,                                           -- شعبه تأمین اجتماعی
    ContractRow       VARCHAR(3)    NOT NULL CONSTRAINT DF_Workshops_Row DEFAULT ('000'),  -- ردیف پیمان
    Address           NVARCHAR(300) NULL,
    EmployeeRate      DECIMAL(5,2)  NOT NULL CONSTRAINT DF_Workshops_EmpRate DEFAULT (7),
    EmployerRate      DECIMAL(5,2)  NOT NULL CONSTRAINT DF_Workshops_ErRate DEFAULT (20),
    UnemploymentRate  DECIMAL(5,2)  NOT NULL CONSTRAINT DF_Workshops_UnRate DEFAULT (3),
    IsActive          BIT           NOT NULL CONSTRAINT DF_Workshops_IsActive DEFAULT (1),
    CONSTRAINT CK_Workshops_Code  CHECK (Code NOT LIKE '%[^0-9]%'),
    CONSTRAINT CK_Workshops_Row   CHECK (ContractRow NOT LIKE '%[^0-9]%'),
    CONSTRAINT CK_Workshops_Rates CHECK (EmployeeRate BETWEEN 0 AND 100 AND EmployerRate BETWEEN 0 AND 100 AND UnemploymentRate BETWEEN 0 AND 100)
);
GO

-- نوع قرارداد: 1 دائم، 2 موقت، 3 ساعتی/پاره‌وقت، 4 پیمانی؛ وضعیت بیمه: 1 مشمول، 2 بازنشسته شاغل، 3 بدون بیمه
-- وضعیت: 1 فعال، 2 تعلیق، 3 ترک کار
CREATE TABLE hr.Employees (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    PersonId         INT           NOT NULL CONSTRAINT UQ_Employees_Person UNIQUE CONSTRAINT FK_Employees_Person REFERENCES base.Persons (Id),
    PersonnelNo      NVARCHAR(20)  NOT NULL CONSTRAINT UQ_Employees_No UNIQUE,
    WorkshopId       INT           NOT NULL CONSTRAINT FK_Employees_Workshop REFERENCES hr.Workshops (Id),
    JobId            INT           NULL     CONSTRAINT FK_Employees_Job REFERENCES hr.Jobs (Id),
    ContractType     TINYINT       NOT NULL CONSTRAINT DF_Employees_Contract DEFAULT (2),
    HireDate         DATE          NOT NULL,
    LeaveDate        DATE          NULL,
    LeaveReason      NVARCHAR(200) NULL,
    InsuranceNo      VARCHAR(10)   NULL,
    InsuranceStatus  TINYINT       NOT NULL CONSTRAINT DF_Employees_Ins DEFAULT (1),
    ChildrenCount    TINYINT       NOT NULL CONSTRAINT DF_Employees_Children DEFAULT (0),
    CostCenterId     INT           NOT NULL CONSTRAINT FK_Employees_Center REFERENCES cost.CostCenters (Id),  -- پیش‌فرض کارکرد ماه
    Iban             VARCHAR(26)   NULL,
    TaxExemptPercent DECIMAL(5,2)  NULL,          -- معافیت مالیاتی فردی (مثلاً ایثارگری)
    [Status]         TINYINT       NOT NULL CONSTRAINT DF_Employees_Status DEFAULT (1),
    Notes            NVARCHAR(500) NULL,
    CreatedAt        DATETIME2(0)  NOT NULL CONSTRAINT DF_Employees_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Employees_Contract CHECK (ContractType BETWEEN 1 AND 4),
    CONSTRAINT CK_Employees_Ins      CHECK (InsuranceStatus BETWEEN 1 AND 3),
    CONSTRAINT CK_Employees_Status   CHECK ([Status] BETWEEN 1 AND 3),
    CONSTRAINT CK_Employees_Leave    CHECK ((LeaveDate IS NULL AND [Status] <> 3) OR (LeaveDate IS NOT NULL AND LeaveDate >= HireDate AND [Status] = 3)),
    CONSTRAINT CK_Employees_InsNo    CHECK (InsuranceNo IS NULL OR InsuranceNo NOT LIKE '%[^0-9]%'),
    CONSTRAINT CK_Employees_Exempt   CHECK (TaxExemptPercent IS NULL OR TaxExemptPercent BETWEEN 0 AND 100)
);
CREATE INDEX IX_Employees_Workshop ON hr.Employees (WorkshopId);
GO

-- پرونده پرسنلی فقط برای شخص حقیقی با نقش «کارمند»
CREATE TRIGGER hr.trg_Employees_Person ON hr.Employees AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN base.Persons p ON p.Id = i.PersonId WHERE p.PersonKind <> 1)
        THROW 50400, N'پرونده پرسنلی فقط برای شخص حقیقی است.', 1;
    IF EXISTS (SELECT 1 FROM inserted i
               WHERE NOT EXISTS (SELECT 1 FROM base.PersonRoles r JOIN base.PersonRoleTypes t ON t.Id = r.RoleId
                                 WHERE r.PersonId = i.PersonId AND t.Code = 'employee'))
        THROW 50401, N'شخص پرونده پرسنلی باید نقش «کارمند» داشته باشد.', 1;
END;
GO

/* ---------------------------------------------------------------------
   ۲) تنظیمات و پارامترهای قانونی سال
   --------------------------------------------------------------------- */
CREATE TABLE pay.Settings (
    Id            TINYINT NOT NULL CONSTRAINT PK_PaySettings PRIMARY KEY CONSTRAINT CK_PaySettings_One CHECK (Id = 1),
    MonthDaysMode TINYINT NOT NULL CONSTRAINT CK_PaySettings_Days CHECK (MonthDaysMode IN (1, 2))  -- 1 سی روز ثابت، 2 روز واقعی ماه
);
INSERT pay.Settings (Id, MonthDaysMode) VALUES (1, 1);
GO

-- هر کد یک متغیر در فرمول اقلام است (مثلاً MIN_WAGE)
CREATE TABLE pay.YearParams (
    [Year]  SMALLINT      NOT NULL,                  -- سال شمسی
    Code    VARCHAR(30)   NOT NULL,
    Title   NVARCHAR(100) NOT NULL,
    Value   DECIMAL(24,6) NOT NULL,
    CONSTRAINT PK_YearParams PRIMARY KEY ([Year], Code),
    CONSTRAINT CK_YearParams_Code CHECK (Code NOT LIKE '%[^A-Z0-9_]%' AND Code LIKE '[A-Z]%')
);
INSERT pay.YearParams ([Year], Code, Title, Value) VALUES
    (1405, 'MIN_WAGE',          N'حداقل مزد روزانه (ریال)',                       5541850),
    (1405, 'HOUSING',           N'حق مسکن ماهانه (ریال)',                         30000000),
    (1405, 'FOOD',              N'بن خواربار ماهانه (ریال)',                      22000000),
    (1405, 'CHILD_FACTOR',      N'حق اولاد: ضریب حداقل مزد روزانه برای هر فرزند', 3),
    (1405, 'INS_CAP_FACTOR',    N'سقف مشمول بیمه: ضریب حداقل مزد روزانه',          7),
    (1405, 'DAY_HOURS',         N'ساعت کار روزانه (۴۴ ساعت در هفته)',             7.33),
    (1405, 'OT_RATE',           N'ضریب اضافه‌کار',                                 1.4),
    (1405, 'NIGHT_RATE',        N'ضریب شب‌کاری',                                   0.35),
    (1405, 'HOLIDAY_RATE',      N'ضریب تعطیل‌کاری (افزوده)',                       0.4),
    (1405, 'BONUS_DAYS',        N'عیدی و پاداش: روز مزد',                          60),
    (1405, 'BONUS_CAP_DAYS',    N'سقف عیدی: روز حداقل مزد',                        90),
    (1405, 'SEVERANCE_DAYS',    N'سنوات: روز مزد برای هر سال',                     30);
GO

/* ---------------------------------------------------------------------
   ۳) اقلام حقوق با فرمول
   نوع: 1 مزایا، 2 کسور، 3 سهم کارفرما، 4 ذخیره، 5 اطلاعاتی
   منبع: 1 حکم، 2 فرمول، 3 ورود ماهانه، 4 وام و مساعده، 5 سامانه مالیات
   --------------------------------------------------------------------- */
CREATE TABLE pay.PayItems (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayItems PRIMARY KEY,
    Code             VARCHAR(30)    NOT NULL CONSTRAINT UQ_PayItems_Code UNIQUE,
    Title            NVARCHAR(100)  NOT NULL CONSTRAINT UQ_PayItems_Title UNIQUE,
    Kind             TINYINT        NOT NULL,
    Source           TINYINT        NOT NULL,
    Formula          NVARCHAR(1000) NULL,
    InsuranceSubject BIT            NOT NULL CONSTRAINT DF_PayItems_Ins DEFAULT (0),
    TaxSubject       BIT            NOT NULL CONSTRAINT DF_PayItems_Tax DEFAULT (0),
    CostFactorId     INT            NULL CONSTRAINT FK_PayItems_Factor REFERENCES cost.CostFactors (Id),   -- هزینه: مزایا، سهم کارفرما، ذخیره
    AccountId        INT            NULL CONSTRAINT FK_PayItems_Account REFERENCES fin.Accounts (Id),      -- بستانکار کسور، سهم کارفرما و ذخیره
    OnPayslip        BIT            NOT NULL CONSTRAINT DF_PayItems_Slip DEFAULT (1),
    SortOrder        INT            NOT NULL CONSTRAINT DF_PayItems_Sort DEFAULT (100),
    IsSystem         BIT            NOT NULL CONSTRAINT DF_PayItems_System DEFAULT (0),   -- قانونی: کد و نوع عوض نمی‌شود، حذف نمی‌شود
    IsActive         BIT            NOT NULL CONSTRAINT DF_PayItems_Active DEFAULT (1),
    CONSTRAINT CK_PayItems_Kind    CHECK (Kind BETWEEN 1 AND 5),
    CONSTRAINT CK_PayItems_Source  CHECK (Source BETWEEN 1 AND 5),
    CONSTRAINT CK_PayItems_Code    CHECK (Code NOT LIKE '%[^A-Z0-9_]%' AND Code LIKE '[A-Z]%'),
    CONSTRAINT CK_PayItems_Formula CHECK ((Source = 2 AND Formula IS NOT NULL) OR (Source <> 2 AND Formula IS NULL))
);
GO

DECLARE @F TABLE (Title NVARCHAR(200) PRIMARY KEY, Id INT);
INSERT @F SELECT Title, Id FROM cost.CostFactors;
DECLARE @A TABLE (Code VARCHAR(10) PRIMARY KEY, Id INT);
INSERT @A SELECT Code, Id FROM fin.Accounts WHERE [Level] = 3 AND Code IN ('5161', '5151', '2522', '2523', '5126', '4201', '5122');

INSERT pay.PayItems (Code, Title, Kind, Source, Formula, InsuranceSubject, TaxSubject, CostFactorId, AccountId, OnPayslip, SortOrder, IsSystem)
SELECT x.Code, x.Title, x.Kind, x.Source, x.Formula, x.Ins, x.Tax, f.Id, a.Id, x.Slip, x.Sort, 1
FROM (VALUES
    ('DAILY_WAGE',     N'مزد روزانه (حکم)',               5, 1, NULL, 0, 0, NULL, NULL, 0, 1),
    ('SENIORITY_DAILY',N'پایه سنوات روزانه (حکم)',         5, 1, NULL, 0, 0, NULL, NULL, 0, 2),
    ('BASE',           N'حقوق پایه',                      1, 2, N'ROUND(DAILY_WAGE * DAYS, 0)', 1, 1, N'حقوق پایه', NULL, 1, 10),
    ('SENIORITY',      N'پایه سنوات',                     1, 2, N'ROUND(SENIORITY_DAILY * DAYS, 0)', 1, 1, N'حقوق پایه', NULL, 1, 11),
    ('HOUSING_PAY',    N'حق مسکن',                        1, 2, N'ROUND(HOUSING * DAYS / MONTH_DAYS, 0)', 1, 1, N'حق مسکن', NULL, 1, 12),
    ('FOOD_PAY',       N'بن خواربار',                     1, 2, N'ROUND(FOOD * DAYS / MONTH_DAYS, 0)', 1, 1, N'کمک‌هزینه اقلام مصرفی خانوار (بن)', NULL, 1, 13),
    ('CHILD_PAY',      N'حق اولاد',                       1, 2, N'ROUND(MIN_WAGE * CHILD_FACTOR * CHILDREN * DAYS / MONTH_DAYS, 0)', 0, 1, N'حق اولاد', NULL, 1, 14),
    ('OVERTIME',       N'اضافه‌کار',                       1, 2, N'ROUND(DAILY_WAGE / DAY_HOURS * OT_RATE * OT_HOURS, 0)', 1, 1, N'اضافه‌کاری', NULL, 1, 20),
    ('NIGHT_PAY',      N'شب‌کاری',                        1, 2, N'ROUND(DAILY_WAGE / DAY_HOURS * NIGHT_RATE * NIGHT_HOURS, 0)', 1, 1, N'نوبت‌کاری و شب‌کاری', NULL, 1, 21),
    ('HOLIDAY_PAY',    N'تعطیل‌کاری',                     1, 2, N'ROUND(DAILY_WAGE / DAY_HOURS * (1 + HOLIDAY_RATE) * HOLIDAY_HOURS, 0)', 1, 1, N'اضافه‌کاری', NULL, 1, 22),
    ('INS_WORKER',     N'حق بیمه سهم کارگر',              2, 2, N'ROUND(MIN(INS_SUBJECT, MIN_WAGE * INS_CAP_FACTOR * INS_DAYS) * INS_EMPLOYEE_RATE / 100, 0)', 0, 0, NULL, '5161', 1, 50),
    ('TAX',            N'مالیات حقوق',                    2, 5, NULL, 0, 0, NULL, '5151', 1, 51),
    ('LOAN',           N'قسط وام',                        2, 4, NULL, 0, 0, NULL, '2522', 1, 52),
    ('ADVANCE',        N'مساعده',                         2, 4, NULL, 0, 0, NULL, '2523', 1, 53),
    ('INS_EMPLOYER',   N'حق بیمه سهم کارفرما',            3, 2, N'ROUND(MIN(INS_SUBJECT, MIN_WAGE * INS_CAP_FACTOR * INS_DAYS) * INS_EMPLOYER_RATE / 100, 0)', 0, 0, N'حق بیمه سهم کارفرما', '5161', 0, 60),
    ('INS_UNEMP',      N'بیمه بیکاری',                    3, 2, N'ROUND(MIN(INS_SUBJECT, MIN_WAGE * INS_CAP_FACTOR * INS_DAYS) * INS_UNEMPLOYMENT_RATE / 100, 0)', 0, 0, N'حق بیمه سهم کارفرما', '5161', 0, 61),
    ('BONUS_PROV',     N'ذخیره عیدی و پاداش',             4, 2, N'ROUND(MIN(DAILY_WAGE * BONUS_DAYS, MIN_WAGE * BONUS_CAP_DAYS) / 12 * DAYS / MONTH_DAYS, 0)', 0, 0, N'عیدی و پاداش', '5126', 0, 70),
    ('SEVERANCE_PROV', N'ذخیره سنوات',                    4, 2, N'ROUND((DAILY_WAGE + SENIORITY_DAILY) * SEVERANCE_DAYS / 12 * DAYS / MONTH_DAYS, 0)', 0, 0, N'مزایای پایان خدمت (سنوات)', '4201', 0, 71)
) x (Code, Title, Kind, Source, Formula, Ins, Tax, Factor, Account, Slip, Sort)
LEFT JOIN @F f ON f.Title = x.Factor
LEFT JOIN @A a ON a.Code = x.Account;
GO

/* ---------------------------------------------------------------------
   ۴) حکم حقوقی: مقدار اقلام حکمی از تاریخ اجرا
   --------------------------------------------------------------------- */
-- نوع حکم: 1 استخدام، 2 افزایش سالانه، 3 ترفیع/تغییر، 4 سایر
CREATE TABLE pay.Decrees (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Decrees PRIMARY KEY,
    EmployeeId    INT           NOT NULL CONSTRAINT FK_Decrees_Employee REFERENCES hr.Employees (Id),
    DocNo         INT           NOT NULL CONSTRAINT UQ_Decrees_No UNIQUE,
    EffectiveDate DATE          NOT NULL,
    DecreeType    TINYINT       NOT NULL,
    [Description] NVARCHAR(500) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_Decrees_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Decrees_Effective UNIQUE (EmployeeId, EffectiveDate),
    CONSTRAINT CK_Decrees_Type CHECK (DecreeType BETWEEN 1 AND 4)
);
GO

CREATE TABLE pay.DecreeItems (
    DecreeId  INT           NOT NULL CONSTRAINT FK_DecreeItems_Decree REFERENCES pay.Decrees (Id) ON DELETE CASCADE,
    PayItemId INT           NOT NULL CONSTRAINT FK_DecreeItems_Item REFERENCES pay.PayItems (Id),
    Amount    DECIMAL(24,4) NOT NULL,
    CONSTRAINT PK_DecreeItems PRIMARY KEY (DecreeId, PayItemId),
    CONSTRAINT CK_DecreeItems_Amount CHECK (Amount >= 0)
);
GO

-- ردیف حکم فقط از اقلامی که منبعشان «حکم» است
CREATE TRIGGER pay.trg_DecreeItems_Source ON pay.DecreeItems AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN pay.PayItems p ON p.Id = i.PayItemId WHERE p.Source <> 1)
        THROW 50410, N'در حکم فقط اقلامی می‌آیند که منبع مقدارشان «حکم» است.', 1;
END;
GO

PRINT N'کارگزینی پایه و پایه‌های حقوق آماده شد.';
GO
