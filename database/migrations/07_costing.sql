/* =====================================================================
   07 - ساختار هزینه‌ها، اطلاعات پایه بهای تمام‌شده و تفصیلی پیش‌فرض معین‌ها

   ساختار گروه ۷:
     کل = ماهیت هزینه، معین = وظیفه (تولیدی، خدماتی = پشتیبانی تولید، اداری، فروش و توزیع)
       70 مواد مصرفی                → 7001 تولیدی، 7002 پیمانکاران (بدون تفصیلی؛ بعداً از بهای تمام‌شده می‌آید)
       71 حقوق و دستمزد            → 7101 تا 7104
       72 استهلاک                   → 7201 تا 7204
       73 سایر هزینه‌های عملیاتی    → 7301 تا 7304
       76 هزینه‌های مالی (تأمین مالی) → 7601 (بدون تفکیک وظیفه)
     74، 75 و 77 بدون تغییر.
   تفصیلی معین‌های هزینه: سطح ۱ عامل هزینه، سطح ۲ مرکز هزینه (مالی: سطح ۲ قرارداد تسهیلات، اختیاری).
   معین‌های قبلی (حقوق پایه، اضافه‌کاری، آب و برق، ...) عامل هزینه پیش‌فرض شدند.

   اطلاعات پایه بهای تمام‌شده (schema cost):
     نوع هزینه (زیر یکی از ماهیت‌ها) ← عامل هزینه (رفتار: ثابت، متغیر، نیمه‌متغیر با درصد)
     نوع مرکز هزینه (با وظیفه) ← مرکز هزینه (مبنای تسهیم، ترتیب تسهیم، مراکز گیرنده با سهم، مبنای جذب سربار)
   تفصیلی عامل‌ها و مراکز را ماژول بهای تمام‌شده می‌سازد (مثل اشخاص). ماهیت عامل و وظیفه مرکز روی
   تفصیلی هم نگه داشته می‌شود تا ثبت عامل در کل نامربوط یا مرکز در معین نامربوط رد شود (خطای 50060/50061).

   قاعده تفصیلی پیش‌فرض برای همه معین‌هایی که هنوز قاعده ندارند.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------------------------------------------------------------------
   ۱) ستون‌های ماهیت و وظیفه هزینه
      ماهیت (CostNature): 1 حقوق و دستمزد، 2 استهلاک، 3 سایر عملیاتی، 4 مالی
      وظیفه (CostFunction): 1 تولیدی، 2 خدماتی (پشتیبانی تولید)، 3 اداری، 4 فروش و توزیع
   --------------------------------------------------------------------- */
ALTER TABLE fin.Accounts ADD
    CostNature   TINYINT NULL CONSTRAINT CK_Accounts_CostNature   CHECK (CostNature BETWEEN 1 AND 4),
    CostFunction TINYINT NULL CONSTRAINT CK_Accounts_CostFunction CHECK (CostFunction BETWEEN 1 AND 4);
ALTER TABLE fin.DetailAccounts ADD
    CostNature   TINYINT NULL CONSTRAINT CK_DetailAccounts_CostNature   CHECK (CostNature BETWEEN 1 AND 4),
    CostFunction TINYINT NULL CONSTRAINT CK_DetailAccounts_CostFunction CHECK (CostFunction BETWEEN 1 AND 4);
GO
-- هر رکورد منبع (عامل، مرکز، ...) در هر نوع تفصیلی فقط یک تفصیلی دارد
CREATE UNIQUE INDEX UX_DetailAccounts_Source ON fin.DetailAccounts (DetailTypeId, SourceEntityId)
    WHERE SourceEntityId IS NOT NULL;
GO

/* ---------------------------------------------------------------------
   ۲) بازسازی گروه ۷
   --------------------------------------------------------------------- */
DECLARE @Old TABLE (Id INT PRIMARY KEY);
INSERT @Old (Id)
SELECT a.Id FROM fin.Accounts a JOIN fin.Accounts k ON k.Id = a.ParentId
WHERE a.[Level] = 3 AND k.Code IN ('71', '72', '73', '76');

IF EXISTS (SELECT 1 FROM fin.VoucherLines l JOIN @Old o ON o.Id = l.AccountId)
    THROW 50070, N'در معین‌های قدیمی هزینه (کل‌های 71، 72، 73، 76) سند ثبت شده است؛ ساختار جدید هزینه‌ها اجرا نشد. ابتدا آن ردیف‌ها را به حساب دیگری منتقل کنید.', 1;
IF EXISTS (SELECT 1 FROM fin.PostingTemplateLines l JOIN @Old o ON o.Id = l.AccountId)
    THROW 50071, N'معین‌های قدیمی هزینه در الگوی صدور سند استفاده شده‌اند.', 1;

DELETE r FROM fin.AccountDetailLevels r JOIN @Old o ON o.Id = r.AccountId;  -- انواع و نقش‌ها cascade
DELETE a FROM fin.Accounts a JOIN @Old o ON o.Id = a.Id;

UPDATE fin.Accounts SET Title = N'هزینه حقوق و دستمزد', CostNature = 1 WHERE Code = '71' AND [Level] = 2;
UPDATE fin.Accounts SET Title = N'هزینه استهلاک', CostNature = 2 WHERE Code = '72' AND [Level] = 2;
UPDATE fin.Accounts SET Title = N'سایر هزینه های عملیاتی', CostNature = 3 WHERE Code = '73' AND [Level] = 2;
UPDATE fin.Accounts SET Title = N'هزینه های مالی (تامین مالی)', CostNature = 4 WHERE Code = '76' AND [Level] = 2;

INSERT fin.Accounts (ParentId, [Level], Code, Title, IsStandard)
SELECT Id, 2, '70', N'مواد مصرفی', 1 FROM fin.Accounts WHERE Code = '7' AND [Level] = 1;

INSERT fin.Accounts (ParentId, [Level], Code, Title, Nature, AccountType, IsStandard, CostNature, CostFunction)
SELECT p.Id, 3, v.Code, v.Title, 1, 2, 1, p.CostNature, v.Fn
FROM (VALUES
    ('7001', '70', N'مواد مصرفی تولیدی', NULL),
    ('7002', '70', N'مواد مصرفی پیمانکاران', NULL),
    ('7101', '71', N'حقوق و دستمزد تولیدی', 1),
    ('7102', '71', N'حقوق و دستمزد خدماتی (پشتیبانی تولید)', 2),
    ('7103', '71', N'حقوق و دستمزد اداری', 3),
    ('7104', '71', N'حقوق و دستمزد فروش و توزیع', 4),
    ('7201', '72', N'استهلاک تولیدی', 1),
    ('7202', '72', N'استهلاک خدماتی (پشتیبانی تولید)', 2),
    ('7203', '72', N'استهلاک اداری', 3),
    ('7204', '72', N'استهلاک فروش و توزیع', 4),
    ('7301', '73', N'سایر هزینه های عملیاتی تولیدی', 1),
    ('7302', '73', N'سایر هزینه های عملیاتی خدماتی (پشتیبانی تولید)', 2),
    ('7303', '73', N'سایر هزینه های عملیاتی اداری', 3),
    ('7304', '73', N'سایر هزینه های عملیاتی فروش و توزیع', 4),
    ('7601', '76', N'هزینه های تامین مالی', NULL)
) AS v (Code, ParentCode, Title, Fn)
JOIN fin.Accounts p ON p.Code = v.ParentCode AND p.[Level] = 2;
GO

/* عامل هزینه در کلِ ماهیت خودش و مرکز هزینه در معینِ وظیفه خودش */
CREATE TRIGGER fin.trg_VoucherLines_CostDims ON fin.VoucherLines AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN fin.Accounts a ON a.Id = i.AccountId
               CROSS APPLY (VALUES (i.Detail1Id), (i.Detail2Id), (i.Detail3Id),
                                   (i.Detail4Id), (i.Detail5Id), (i.Detail6Id)) d (DetailId)
               JOIN fin.DetailAccounts da ON da.Id = d.DetailId
               WHERE da.CostNature IS NOT NULL AND a.CostNature IS NOT NULL AND da.CostNature <> a.CostNature)
        THROW 50060, N'عامل هزینه با ماهیت این معین (حقوق، استهلاک، سایر عملیاتی یا مالی) نمی‌خواند.', 1;

    IF EXISTS (SELECT 1 FROM inserted i
               JOIN fin.Accounts a ON a.Id = i.AccountId
               CROSS APPLY (VALUES (i.Detail1Id), (i.Detail2Id), (i.Detail3Id),
                                   (i.Detail4Id), (i.Detail5Id), (i.Detail6Id)) d (DetailId)
               JOIN fin.DetailAccounts da ON da.Id = d.DetailId
               WHERE da.CostFunction IS NOT NULL AND a.CostFunction IS NOT NULL AND da.CostFunction <> a.CostFunction)
        THROW 50061, N'نوع مرکز هزینه با وظیفه این معین (تولیدی، خدماتی، اداری یا فروش) نمی‌خواند.', 1;
END;
GO

/* ---------------------------------------------------------------------
   ۳) schema cost: اطلاعات پایه بهای تمام‌شده
   --------------------------------------------------------------------- */
IF SCHEMA_ID('cost') IS NULL EXEC ('CREATE SCHEMA cost');
GO

-- نوع هزینه: زیر یکی از ماهیت‌ها (کل‌های 71، 72، 73، 76)
CREATE TABLE cost.ExpenseTypes (
    Id         SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExpenseTypes PRIMARY KEY,
    Title      NVARCHAR(100) NOT NULL,
    Nature     TINYINT       NOT NULL CONSTRAINT CK_ExpenseTypes_Nature CHECK (Nature BETWEEN 1 AND 4),
    SortOrder  INT           NOT NULL CONSTRAINT DF_ExpenseTypes_Sort DEFAULT (0),
    IsActive   BIT           NOT NULL CONSTRAINT DF_ExpenseTypes_IsActive DEFAULT (1),
    CONSTRAINT UQ_ExpenseTypes_Title UNIQUE (Title)
);
GO

-- عامل هزینه: رفتار 1 ثابت، 2 متغیر، 3 نیمه‌متغیر (با درصد جزء متغیر)
CREATE TABLE cost.CostFactors (
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CostFactors PRIMARY KEY,
    Title           NVARCHAR(200) NOT NULL CONSTRAINT UQ_CostFactors_Title UNIQUE,
    ExpenseTypeId   SMALLINT      NOT NULL CONSTRAINT FK_CostFactors_Type REFERENCES cost.ExpenseTypes (Id),
    Behavior        TINYINT       NOT NULL CONSTRAINT CK_CostFactors_Behavior CHECK (Behavior BETWEEN 1 AND 3),
    VariablePercent DECIMAL(5,2)  NULL,
    IsActive        BIT           NOT NULL CONSTRAINT DF_CostFactors_IsActive DEFAULT (1),
    Notes           NVARCHAR(500) NULL,
    CONSTRAINT CK_CostFactors_Percent CHECK ((Behavior = 3 AND VariablePercent > 0 AND VariablePercent < 100)
                                          OR (Behavior <> 3 AND VariablePercent IS NULL))
);
CREATE INDEX IX_CostFactors_Type ON cost.CostFactors (ExpenseTypeId);
GO

-- نوع مرکز هزینه با وظیفه: 1 تولیدی، 2 خدماتی (پشتیبانی تولید)، 3 اداری، 4 فروش و توزیع
CREATE TABLE cost.CostCenterTypes (
    Id         SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CostCenterTypes PRIMARY KEY,
    Title      NVARCHAR(100) NOT NULL CONSTRAINT UQ_CostCenterTypes_Title UNIQUE,
    [Function] TINYINT       NOT NULL CONSTRAINT CK_CostCenterTypes_Function CHECK ([Function] BETWEEN 1 AND 4),
    SortOrder  INT           NOT NULL CONSTRAINT DF_CostCenterTypes_Sort DEFAULT (0),
    IsActive   BIT           NOT NULL CONSTRAINT DF_CostCenterTypes_IsActive DEFAULT (1),
    IsSystem   BIT           NOT NULL CONSTRAINT DF_CostCenterTypes_IsSystem DEFAULT (0)
);
GO

-- مرکز هزینه؛ مبنای تسهیم و جذب از جدول پایه «مبنای تسهیم و جذب» (base.LookupItems)
CREATE TABLE cost.CostCenters (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CostCenters PRIMARY KEY,
    Title            NVARCHAR(200) NOT NULL CONSTRAINT UQ_CostCenters_Title UNIQUE,
    CenterTypeId     SMALLINT      NOT NULL CONSTRAINT FK_CostCenters_Type REFERENCES cost.CostCenterTypes (Id),
    ParentId         INT           NULL CONSTRAINT FK_CostCenters_Parent REFERENCES cost.CostCenters (Id),
    ManagerPersonId  INT           NULL CONSTRAINT FK_CostCenters_Manager REFERENCES base.Persons (Id),
    AllocationBaseId INT           NULL CONSTRAINT FK_CostCenters_AllocBase REFERENCES base.LookupItems (Id),
    AllocationOrder  SMALLINT      NULL,   -- ترتیب در تسهیم یک‌طرفه (کوچک‌تر زودتر)
    AbsorptionBaseId INT           NULL CONSTRAINT FK_CostCenters_AbsorbBase REFERENCES base.LookupItems (Id),
    IsActive         BIT           NOT NULL CONSTRAINT DF_CostCenters_IsActive DEFAULT (1),
    Notes            NVARCHAR(500) NULL,
    CONSTRAINT CK_CostCenters_Parent CHECK (ParentId <> Id)
);
CREATE INDEX IX_CostCenters_Type ON cost.CostCenters (CenterTypeId);
GO

-- مراکز گیرنده و سهم پیش‌فرض هر کدام در تسهیم (جمع سهم‌های یک مرکز ۱۰۰ درصد)؛
-- با رسیدن مقدار مبنا در هر دوره، سهم واقعی از مبنا حساب می‌شود
CREATE TABLE cost.CostCenterAllocations (
    CenterId       INT          NOT NULL CONSTRAINT FK_CCA_Center REFERENCES cost.CostCenters (Id) ON DELETE CASCADE,
    TargetCenterId INT          NOT NULL CONSTRAINT FK_CCA_Target REFERENCES cost.CostCenters (Id),
    [Percent]      DECIMAL(5,2) NOT NULL CONSTRAINT CK_CCA_Percent CHECK ([Percent] > 0 AND [Percent] <= 100),
    CONSTRAINT PK_CostCenterAllocations PRIMARY KEY (CenterId, TargetCenterId),
    CONSTRAINT CK_CCA_Self CHECK (CenterId <> TargetCenterId)
);
CREATE INDEX IX_CCA_Target ON cost.CostCenterAllocations (TargetCenterId);
GO

/* ---------------------------------------------------------------------
   ۴) داده پیش‌فرض بهای تمام‌شده
   --------------------------------------------------------------------- */
INSERT base.LookupCategories (Code, Title, [Description], SortOrder) VALUES
    ('cost_base', N'مبنای تسهیم و جذب', N'مبنای تسهیم هزینه مراکز خدماتی و جذب سربار مراکز تولیدی', 7);
DECLARE @Base SMALLINT = SCOPE_IDENTITY();
INSERT base.LookupItems (CategoryId, Code, Title, SortOrder, IsSystem) VALUES
    (@Base, N'headcount',    N'تعداد پرسنل (نفر)', 1, 1),
    (@Base, N'area',         N'متراژ (متر مربع)', 2, 1),
    (@Base, N'machine_hour', N'ساعت کار ماشین', 3, 1),
    (@Base, N'labor_hour',   N'ساعت کار مستقیم', 4, 1),
    (@Base, N'kwh',          N'کیلووات‌ساعت برق مصرفی', 5, 1),
    (@Base, N'output_qty',   N'مقدار تولید', 6, 1),
    (@Base, N'output_kg',    N'وزن تولید', 7, 1),
    (@Base, N'fixed_pct',    N'سهم ثابت (درصد)', 8, 1);

INSERT cost.CostCenterTypes (Title, [Function], SortOrder, IsSystem) VALUES
    (N'تولیدی', 1, 1, 1),
    (N'خدماتی (پشتیبانی تولید)', 2, 2, 1),
    (N'اداری', 3, 3, 1),
    (N'فروش و توزیع', 4, 4, 1);

INSERT cost.ExpenseTypes (Title, Nature, SortOrder) VALUES
    (N'حقوق و مزایای نقدی', 1, 1),
    (N'اضافه‌کاری و نوبت‌کاری', 1, 2),
    (N'بیمه سهم کارفرما', 1, 3),
    (N'عیدی، پاداش و سنوات', 1, 4),
    (N'مزایای غیرنقدی', 1, 5),
    (N'استهلاک دارایی‌های مشهود', 2, 1),
    (N'استهلاک دارایی‌های نامشهود', 2, 2),
    (N'تعمیر و نگهداری', 3, 1),
    (N'انرژی و آب', 3, 2),
    (N'مواد و ملزومات غیرمستقیم', 3, 3),
    (N'حمل‌ونقل و ایاب‌وذهاب', 3, 4),
    (N'اجاره و بیمه', 3, 5),
    (N'رفاه کارکنان', 3, 6),
    (N'خدمات و مشاوره', 3, 7),
    (N'اداری و عمومی', 3, 8),
    (N'بازاریابی و فروش', 3, 9),
    (N'سود و کارمزد تأمین مالی', 4, 1),
    (N'سایر هزینه‌های تأمین مالی', 4, 2);

-- عوامل پیش‌فرض (از معین‌های قبلی کدینگ)؛ رفتار: 1 ثابت، 2 متغیر، 3 نیمه‌متغیر
DECLARE @F TABLE (Seq INT IDENTITY(1,1), Title NVARCHAR(150), TypeTitle NVARCHAR(100), Nature TINYINT,
                  Behavior TINYINT, Pct DECIMAL(5,2));
INSERT @F (Title, TypeTitle, Nature, Behavior, Pct) VALUES
    (N'حقوق پایه', N'حقوق و مزایای نقدی', 1, 1, NULL),
    (N'حق مسکن', N'حقوق و مزایای نقدی', 1, 1, NULL),
    (N'حق اولاد', N'حقوق و مزایای نقدی', 1, 1, NULL),
    (N'کمک‌هزینه اقلام مصرفی خانوار (بن)', N'حقوق و مزایای نقدی', 1, 1, NULL),
    (N'حق‌الجذب', N'حقوق و مزایای نقدی', 1, 1, NULL),
    (N'اضافه‌کاری', N'اضافه‌کاری و نوبت‌کاری', 1, 2, NULL),
    (N'نوبت‌کاری و شب‌کاری', N'اضافه‌کاری و نوبت‌کاری', 1, 2, NULL),
    (N'حق بیمه سهم کارفرما', N'بیمه سهم کارفرما', 1, 1, NULL),
    (N'عیدی و پاداش', N'عیدی، پاداش و سنوات', 1, 1, NULL),
    (N'بازخرید مرخصی استفاده‌نشده', N'عیدی، پاداش و سنوات', 1, 1, NULL),
    (N'مزایای پایان خدمت (سنوات)', N'عیدی، پاداش و سنوات', 1, 1, NULL),
    (N'کمک‌های غیرنقدی', N'مزایای غیرنقدی', 1, 1, NULL),
    (N'استهلاک ساختمان', N'استهلاک دارایی‌های مشهود', 2, 1, NULL),
    (N'استهلاک تأسیسات', N'استهلاک دارایی‌های مشهود', 2, 1, NULL),
    (N'استهلاک ماشین‌آلات و تجهیزات', N'استهلاک دارایی‌های مشهود', 2, 1, NULL),
    (N'استهلاک وسایل نقلیه', N'استهلاک دارایی‌های مشهود', 2, 1, NULL),
    (N'استهلاک اثاثه و منصوبات', N'استهلاک دارایی‌های مشهود', 2, 1, NULL),
    (N'استهلاک ابزارآلات کارگاهی', N'استهلاک دارایی‌های مشهود', 2, 1, NULL),
    (N'استهلاک نرم‌افزار', N'استهلاک دارایی‌های نامشهود', 2, 1, NULL),
    (N'تعمیر و نگهداری ساختمان و تأسیسات', N'تعمیر و نگهداری', 3, 3, 30),
    (N'تعمیر و نگهداری ماشین‌آلات و تجهیزات', N'تعمیر و نگهداری', 3, 3, 50),
    (N'تعمیر و نگهداری وسایل نقلیه', N'تعمیر و نگهداری', 3, 3, 50),
    (N'تعمیر و نگهداری اثاثه و منصوبات', N'تعمیر و نگهداری', 3, 1, NULL),
    (N'سوخت و انرژی', N'انرژی و آب', 3, 2, NULL),
    (N'آب، برق و گاز', N'انرژی و آب', 3, 3, 70),
    (N'تلفن و اینترنت', N'انرژی و آب', 3, 1, NULL),
    (N'ملزومات مصرفی تولید', N'مواد و ملزومات غیرمستقیم', 3, 2, NULL),
    (N'هزینه‌های بسته‌بندی', N'مواد و ملزومات غیرمستقیم', 3, 2, NULL),
    (N'حمل‌ونقل', N'حمل‌ونقل و ایاب‌وذهاب', 3, 2, NULL),
    (N'ایاب‌وذهاب', N'حمل‌ونقل و ایاب‌وذهاب', 3, 1, NULL),
    (N'پیک و پست', N'حمل‌ونقل و ایاب‌وذهاب', 3, 1, NULL),
    (N'اجاره مکان', N'اجاره و بیمه', 3, 1, NULL),
    (N'بیمه دارایی‌ها', N'اجاره و بیمه', 3, 1, NULL),
    (N'بیمه موجودی کالا', N'اجاره و بیمه', 3, 1, NULL),
    (N'غذای کارکنان', N'رفاه کارکنان', 3, 1, NULL),
    (N'پوشاک کارکنان', N'رفاه کارکنان', 3, 1, NULL),
    (N'بهداشت و درمان', N'رفاه کارکنان', 3, 1, NULL),
    (N'آبدارخانه', N'رفاه کارکنان', 3, 1, NULL),
    (N'آموزش کارکنان', N'خدمات و مشاوره', 3, 1, NULL),
    (N'حق‌المشاوره', N'خدمات و مشاوره', 3, 1, NULL),
    (N'خدمات حسابداری و حسابرسی', N'خدمات و مشاوره', 3, 1, NULL),
    (N'هزینه‌های رایانه‌ای و نرم‌افزار', N'خدمات و مشاوره', 3, 1, NULL),
    (N'ملزومات و نوشت‌افزار', N'اداری و عمومی', 3, 1, NULL),
    (N'سفر و فوق‌العاده مأموریت', N'اداری و عمومی', 3, 1, NULL),
    (N'کارمزدهای بانکی', N'اداری و عمومی', 3, 1, NULL),
    (N'هزینه ثبتی، تمبر و وکالت', N'اداری و عمومی', 3, 1, NULL),
    (N'عوارض و هزینه‌های قانونی', N'اداری و عمومی', 3, 1, NULL),
    (N'پاداش هیئت‌مدیره', N'اداری و عمومی', 3, 1, NULL),
    (N'تبلیغات، کاتالوگ و بروشور', N'بازاریابی و فروش', 3, 1, NULL),
    (N'حمل کالای فروش‌رفته', N'بازاریابی و فروش', 3, 2, NULL),
    (N'حق‌العمل‌کاری و کمیسیون فروش', N'بازاریابی و فروش', 3, 2, NULL),
    (N'گارانتی محصولات', N'بازاریابی و فروش', 3, 2, NULL),
    (N'انبارداری', N'بازاریابی و فروش', 3, 1, NULL),
    (N'سود تضمین‌شده و کارمزد تسهیلات', N'سود و کارمزد تأمین مالی', 4, 1, NULL),
    (N'سود اوراق مشارکت', N'سود و کارمزد تأمین مالی', 4, 1, NULL),
    (N'هزینه تعهدات اجاره سرمایه‌ای (لیزینگ)', N'سود و کارمزد تأمین مالی', 4, 1, NULL),
    (N'هزینه اوراق خرید دین (تنزیل اسناد)', N'سایر هزینه‌های تأمین مالی', 4, 2, NULL),
    (N'خرید سفته', N'سایر هزینه‌های تأمین مالی', 4, 1, NULL),
    (N'کارشناسی و ترهین املاک تسهیلات', N'سایر هزینه‌های تأمین مالی', 4, 1, NULL);

INSERT cost.CostFactors (Title, ExpenseTypeId, Behavior, VariablePercent)
SELECT f.Title, t.Id, f.Behavior, f.Pct
FROM @F f JOIN cost.ExpenseTypes t ON t.Title = f.TypeTitle AND t.Nature = f.Nature
ORDER BY f.Seq;

IF (SELECT COUNT(*) FROM cost.CostFactors) <> (SELECT COUNT(*) FROM @F)
    THROW 50072, N'عوامل هزینه پیش‌فرض کامل بارگذاری نشد (نوع هزینه پیدا نشد).', 1;
GO

/* ---------------------------------------------------------------------
   ۵) گروه‌های تفصیلی عوامل و مراکز هزینه
   --------------------------------------------------------------------- */
-- عوامل هزینه: 09001 تا 09999
INSERT fin.DetailTypes (Title, OwnerModule, PersonKind, CodeFrom, CodeTo)
VALUES (N'عوامل هزینه', 'cost_factors', NULL, '09001', '09999');
DECLARE @FactorType SMALLINT = SCOPE_IDENTITY();

INSERT fin.DetailAccounts (DetailTypeId, Code, Title, SourceModule, SourceEntityId, CostNature)
SELECT @FactorType, RIGHT('0000' + CAST(9000 + ROW_NUMBER() OVER (ORDER BY f.Id) AS VARCHAR(5)), 5),
       f.Title, 'costing', f.Id, t.Nature
FROM cost.CostFactors f JOIN cost.ExpenseTypes t ON t.Id = f.ExpenseTypeId;

-- مراکز هزینه: گروه موجود «مراکز هزینه و مسئولیت» به مالکیت بهای تمام‌شده درمی‌آید.
-- اگر تفصیلی ندارد بازه 08101 تا 08999 می‌گیرد؛ وگرنه کدهای فعلی حفظ و بازه تا 00999 بزرگ می‌شود
-- و تفصیلی‌های موجود به‌عنوان مرکز «اداری» ثبت می‌شوند (نوع را بعداً اصلاح کنید).
DECLARE @CenterType SMALLINT = (SELECT TOP (1) Id FROM fin.DetailTypes WHERE OwnerModule = 'manual' AND CodeFrom = '00901');
IF @CenterType IS NULL
BEGIN
    INSERT fin.DetailTypes (Title, OwnerModule, PersonKind, CodeFrom, CodeTo)
    VALUES (N'مراکز هزینه', 'cost_centers', NULL, '08101', '08999');
END
ELSE IF NOT EXISTS (SELECT 1 FROM fin.DetailAccounts WHERE DetailTypeId = @CenterType)
BEGIN
    UPDATE fin.DetailTypes SET Title = N'مراکز هزینه', OwnerModule = 'cost_centers', CodeFrom = '08101', CodeTo = '08999'
    WHERE Id = @CenterType;
END
ELSE
BEGIN
    UPDATE fin.DetailTypes SET Title = N'مراکز هزینه', OwnerModule = 'cost_centers', CodeTo = '00999'
    WHERE Id = @CenterType;

    DECLARE @Admin SMALLINT = (SELECT Id FROM cost.CostCenterTypes WHERE [Function] = 3);
    INSERT cost.CostCenters (Title, CenterTypeId, IsActive)
    SELECT d.Title, @Admin, d.IsActive FROM fin.DetailAccounts d WHERE d.DetailTypeId = @CenterType;

    UPDATE d SET SourceModule = 'costing', SourceEntityId = c.Id, CostFunction = 3
    FROM fin.DetailAccounts d JOIN cost.CostCenters c ON c.Title = d.Title
    WHERE d.DetailTypeId = @CenterType;
END
GO

/* ---------------------------------------------------------------------
   ۶) قاعده تفصیلی پیش‌فرض معین‌ها (فقط معین‌هایی که هنوز قاعده ندارند)
      کلید نوع: cash، bank، employee، asset، lc، loan، project، natural، legal، person (هر دو)، factor، center
   --------------------------------------------------------------------- */
DECLARE @T TABLE (TypeKey VARCHAR(20), DetailTypeId SMALLINT);
INSERT @T
SELECT 'cash', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '00101' UNION ALL
SELECT 'bank', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '00111' UNION ALL
SELECT 'lc', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '00801' UNION ALL
SELECT 'loan', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '07001' UNION ALL
SELECT 'asset', Id FROM fin.DetailTypes WHERE OwnerModule = 'assets' AND IsActive = 1 UNION ALL
SELECT 'project', Id FROM fin.DetailTypes WHERE OwnerModule = 'manual' AND CodeFrom = '07501' UNION ALL
SELECT 'employee', Id FROM fin.DetailTypes WHERE OwnerModule = 'payroll' AND IsActive = 1 UNION ALL
SELECT 'natural', Id FROM fin.DetailTypes WHERE OwnerModule = 'persons' AND PersonKind = 1 AND IsActive = 1 UNION ALL
SELECT 'legal', Id FROM fin.DetailTypes WHERE OwnerModule = 'persons' AND PersonKind = 2 AND IsActive = 1 UNION ALL
SELECT 'person', Id FROM fin.DetailTypes WHERE OwnerModule = 'persons' AND IsActive = 1 UNION ALL
SELECT 'factor', Id FROM fin.DetailTypes WHERE OwnerModule = 'cost_factors' UNION ALL
SELECT 'center', Id FROM fin.DetailTypes WHERE OwnerModule = 'cost_centers';

-- Types و Roles: فهرست کلیدها با کاما
DECLARE @R TABLE (Code VARCHAR(20), LevelNo TINYINT, IsRequired BIT, Types VARCHAR(100), Roles VARCHAR(100));
INSERT @R (Code, LevelNo, IsRequired, Types, Roles) VALUES
    -- دارایی ثابت
    ('1101', 1, 0, 'asset', NULL), ('1102', 1, 0, 'asset', NULL), ('1103', 1, 0, 'asset', NULL),
    ('1104', 1, 0, 'asset', NULL), ('1105', 1, 0, 'asset', NULL), ('1106', 1, 0, 'asset', NULL),
    ('1107', 1, 0, 'asset', NULL), ('1109', 1, 0, 'asset', NULL),
    ('1122', 1, 0, 'asset', NULL), ('1123', 1, 0, 'asset', NULL), ('1124', 1, 0, 'asset', NULL),
    ('1125', 1, 0, 'asset', NULL), ('1126', 1, 0, 'asset', NULL), ('1127', 1, 0, 'asset', NULL),
    -- سرمایه‌گذاری و دریافتنی بلندمدت
    ('1401', 1, 0, 'legal', 'affiliate'),
    ('1403', 1, 1, 'bank', NULL),
    ('1501', 1, 1, 'person', 'customer'), ('1502', 1, 1, 'person', 'customer'),
    ('1503', 1, 1, 'person', NULL), ('1504', 1, 1, 'person', NULL),
    ('1601', 1, 1, 'bank', NULL),
    -- سفارشات و پیش‌پرداخت
    ('2101', 1, 1, 'lc,person', 'supplier'), ('2102', 1, 1, 'lc,person', 'supplier'),
    ('2103', 1, 1, 'person', 'supplier'),
    ('2104', 1, 0, 'person', NULL), ('2106', 1, 0, 'person', NULL),
    -- دریافتنی‌های تجاری
    ('2301', 1, 1, 'person', 'customer'),
    ('2302', 1, 1, 'person', 'customer'), ('2302', 2, 1, 'bank', NULL),
    ('2303', 1, 1, 'person', 'customer'), ('2303', 2, 1, 'bank', NULL),
    ('2304', 1, 1, 'legal', 'customer'),
    ('2305', 1, 1, 'natural', 'customer'),
    ('2306', 1, 1, 'person', 'customer'),
    -- سایر دریافتنی‌ها
    ('2401', 1, 1, 'legal', NULL), ('2402', 1, 1, 'natural', NULL),
    ('2403', 1, 1, 'employee', NULL), ('2404', 1, 1, 'employee', NULL),
    ('2406', 1, 0, 'legal', 'affiliate'),
    ('2407', 1, 1, 'person', NULL),
    ('2408', 1, 1, 'bank', NULL), ('2408', 2, 0, 'person', NULL),
    ('2409', 1, 0, 'person', NULL), ('2410', 1, 1, 'person', NULL),
    ('2505', 1, 1, 'bank', NULL),
    -- موجودی نقد
    ('2601', 1, 1, 'bank', NULL), ('2602', 1, 1, 'bank', NULL),
    ('2603', 1, 1, 'cash', NULL), ('2604', 1, 1, 'cash', NULL), ('2605', 1, 1, 'cash', NULL),
    ('2606', 1, 0, 'bank', NULL),
    -- حقوق مالکانه
    ('3001', 1, 0, 'person', 'shareholder'), ('3101', 1, 0, 'person', 'shareholder'),
    ('3102', 1, 0, 'person', 'shareholder'),
    -- بدهی‌های بلندمدت
    ('4101', 1, 1, 'person', 'supplier'), ('4102', 1, 1, 'person', 'supplier'),
    ('4103', 1, 1, 'person', NULL), ('4104', 1, 1, 'person', NULL),
    ('4201', 1, 1, 'loan', NULL), ('4204', 1, 0, 'person', NULL),
    ('4301', 1, 0, 'employee', NULL),
    -- پرداختنی‌ها
    ('5101', 1, 1, 'person', 'supplier'), ('5101', 2, 1, 'bank', NULL),
    ('5102', 1, 1, 'legal', 'supplier'), ('5103', 1, 1, 'natural', 'supplier'),
    ('5104', 1, 0, 'person', 'supplier'),
    ('5201', 1, 1, 'legal', NULL), ('5202', 1, 1, 'natural', NULL),
    ('5203', 1, 0, 'employee', NULL),
    ('5208', 1, 1, 'person', 'shareholder'),
    ('5209', 1, 0, 'person', NULL), ('5210', 1, 1, 'person', NULL),
    ('5401', 1, 0, 'person', 'shareholder'), ('5402', 1, 0, 'person', 'shareholder'),
    ('5501', 1, 1, 'loan', NULL), ('5502', 1, 1, 'person', NULL), ('5505', 1, 0, 'person', NULL),
    ('5701', 1, 1, 'person', 'customer'), ('5702', 1, 1, 'person', 'customer'), ('5703', 1, 1, 'person', NULL),
    -- درآمد و بهای تمام‌شده خدمات
    ('6201', 1, 0, 'project', NULL), ('6202', 1, 0, 'project', NULL), ('6203', 1, 0, 'project', NULL),
    ('8111', 1, 0, 'project', NULL),
    -- هزینه‌ها: سطح ۱ عامل، سطح ۲ مرکز
    ('7101', 1, 1, 'factor', NULL), ('7101', 2, 1, 'center', NULL),
    ('7102', 1, 1, 'factor', NULL), ('7102', 2, 1, 'center', NULL),
    ('7103', 1, 1, 'factor', NULL), ('7103', 2, 1, 'center', NULL),
    ('7104', 1, 1, 'factor', NULL), ('7104', 2, 1, 'center', NULL),
    ('7201', 1, 1, 'factor', NULL), ('7201', 2, 1, 'center', NULL),
    ('7202', 1, 1, 'factor', NULL), ('7202', 2, 1, 'center', NULL),
    ('7203', 1, 1, 'factor', NULL), ('7203', 2, 1, 'center', NULL),
    ('7204', 1, 1, 'factor', NULL), ('7204', 2, 1, 'center', NULL),
    ('7301', 1, 1, 'factor', NULL), ('7301', 2, 1, 'center', NULL),
    ('7302', 1, 1, 'factor', NULL), ('7302', 2, 1, 'center', NULL),
    ('7303', 1, 1, 'factor', NULL), ('7303', 2, 1, 'center', NULL),
    ('7304', 1, 1, 'factor', NULL), ('7304', 2, 1, 'center', NULL),
    ('7601', 1, 1, 'factor', NULL), ('7601', 2, 0, 'loan', NULL),
    -- حساب‌های انتظامی
    ('9101', 1, 1, 'person', NULL), ('9102', 1, 1, 'person', NULL),
    ('9103', 1, 1, 'person', NULL), ('9103', 2, 0, 'bank', NULL),
    ('9104', 1, 1, 'person', NULL),
    ('9201', 1, 1, 'person', NULL), ('9202', 1, 1, 'person', NULL),
    ('9203', 1, 1, 'person', NULL), ('9203', 2, 0, 'bank', NULL),
    ('9204', 1, 1, 'person', NULL);

-- نوع‌های هر قاعده (کلیدهای پیدانشده نادیده گرفته می‌شوند)
DECLARE @RT TABLE (AccountId INT, LevelNo TINYINT, DetailTypeId SMALLINT, PRIMARY KEY (AccountId, LevelNo, DetailTypeId));
INSERT @RT
SELECT DISTINCT a.Id, r.LevelNo, t.DetailTypeId
FROM @R r
JOIN fin.Accounts a ON a.Code = r.Code AND a.[Level] = 3
CROSS APPLY STRING_SPLIT(r.Types, ',') k
JOIN @T t ON t.TypeKey = k.value
WHERE NOT EXISTS (SELECT 1 FROM fin.AccountDetailLevels x WHERE x.AccountId = a.Id);

-- سطح فقط وقتی ساخته می‌شود که سطح‌های قبلی هم ساخته شده باشند (بدون جای خالی)
INSERT fin.AccountDetailLevels (AccountId, LevelNo, IsRequired)
SELECT a.Id, r.LevelNo, r.IsRequired
FROM @R r JOIN fin.Accounts a ON a.Code = r.Code AND a.[Level] = 3
WHERE EXISTS (SELECT 1 FROM @RT x WHERE x.AccountId = a.Id AND x.LevelNo = r.LevelNo)
  AND (r.LevelNo = 1 OR EXISTS (SELECT 1 FROM @RT x WHERE x.AccountId = a.Id AND x.LevelNo = r.LevelNo - 1));

INSERT fin.AccountDetailLevelTypes (AccountId, LevelNo, DetailTypeId)
SELECT x.AccountId, x.LevelNo, x.DetailTypeId
FROM @RT x JOIN fin.AccountDetailLevels l ON l.AccountId = x.AccountId AND l.LevelNo = x.LevelNo;

INSERT fin.AccountDetailLevelRoles (AccountId, LevelNo, RoleId)
SELECT DISTINCT l.AccountId, l.LevelNo, rt.Id
FROM @R r
JOIN fin.Accounts a ON a.Code = r.Code AND a.[Level] = 3
JOIN fin.AccountDetailLevels l ON l.AccountId = a.Id AND l.LevelNo = r.LevelNo
CROSS APPLY STRING_SPLIT(r.Roles, ',') k
JOIN base.PersonRoleTypes rt ON rt.Code = k.value
WHERE r.Roles IS NOT NULL
  AND EXISTS (SELECT 1 FROM @RT x WHERE x.AccountId = a.Id)
  AND NOT EXISTS (SELECT 1 FROM fin.AccountDetailLevelRoles y WHERE y.AccountId = l.AccountId AND y.LevelNo = l.LevelNo);

PRINT N'ساختار هزینه‌ها، اطلاعات پایه بهای تمام‌شده و قواعد تفصیلی پیش‌فرض بارگذاری شد.';
GO
