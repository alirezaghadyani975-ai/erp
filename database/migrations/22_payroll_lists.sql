/* =====================================================================
   22 - حقوق و دستمزد مرحله ۳: لیست بیمه تأمین اجتماعی (DSKKAR00/DSKWOR00) و فایل مالیات حقوق (WP/WH)

   - hr.Employees: اقلام لازم فایل مالیات حقوق (جدول‌های راهنمای نسخه 1.7.0.5): مدرک تحصیلی (۲)، رسته (۶)، نوع استخدام (۷)،
     نوع معافیت (۴)، وضعیت محل خدمت (۹).
   - pay.PayItems: InsuranceWage = «دستمزد» لیست بیمه (مزد روزانه و پایه سنوات × روز بیمه؛ بقیه اقلام مشمول = «مزایا»)،
     TaxField = شماره ستون قلم در فایل WH (ریالی).
   - عامل هزینه جدا برای بیمه بیکاری (۳٪) از سهم کارفرما (۲۰٪)، و قلم قانونی حق تأهل با عامل هزینه خودش.
   - پارامتر MARRIAGE (حق تأهل ماهانه) ۱۴۰۵ = ۵٬۰۰۰٬۰۰۰ ریال مانند ۱۴۰۴؛ با مصوبه ۱۴۰۵ تطبیق شود.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

ALTER TABLE hr.Employees ADD
    Education       TINYINT NULL,                                              -- جدول ۲ مالیات: ۱ زیردیپلم ... ۷ فوق دکتری
    JobCategory     TINYINT NULL,                                              -- جدول ۶ مالیات: رسته
    EmploymentType  TINYINT NOT NULL CONSTRAINT DF_Employees_EmpType DEFAULT (1),   -- جدول ۷: ۱ قراردادی
    TaxExemption    TINYINT NOT NULL CONSTRAINT DF_Employees_TaxExempt DEFAULT (1), -- جدول ۴: ۱ عدم معافیت
    ServiceLocation TINYINT NOT NULL CONSTRAINT DF_Employees_Location DEFAULT (1);  -- جدول ۹: ۱ عادی
GO
ALTER TABLE hr.Employees ADD
    CONSTRAINT CK_Employees_Education CHECK (Education IS NULL OR Education BETWEEN 1 AND 7),
    CONSTRAINT CK_Employees_JobCategory CHECK (JobCategory IS NULL OR JobCategory BETWEEN 1 AND 19),
    CONSTRAINT CK_Employees_EmpType CHECK (EmploymentType BETWEEN 1 AND 12),
    CONSTRAINT CK_Employees_TaxExempt CHECK (TaxExemption BETWEEN 1 AND 11),
    CONSTRAINT CK_Employees_Location CHECK (ServiceLocation BETWEEN 1 AND 4);
GO

ALTER TABLE pay.PayItems ADD
    InsuranceWage BIT     NOT NULL CONSTRAINT DF_PayItems_InsWage DEFAULT (0),
    TaxField      TINYINT NULL;
GO
-- ستون‌های مبلغ فایل WH ریالی: 7 مستمر نقدی، 15 تا 29 غیرمستمر، 33 و 34 کسور قانونی (بیمه ماده ۱۳۷)، 36 تا 39 اقلام تازه
ALTER TABLE pay.PayItems ADD
    CONSTRAINT CK_PayItems_TaxField CHECK (TaxField IS NULL OR TaxField = 7 OR TaxField BETWEEN 15 AND 29
                                           OR TaxField IN (33, 34) OR TaxField BETWEEN 36 AND 39),
    CONSTRAINT CK_PayItems_InsWage CHECK (InsuranceWage = 0 OR (Kind = 1 AND InsuranceSubject = 1));
GO

UPDATE pay.PayItems SET InsuranceWage = 1 WHERE Code IN ('BASE', 'SENIORITY');
UPDATE pay.PayItems SET TaxField = 7  WHERE Code IN ('BASE', 'SENIORITY', 'HOUSING_PAY', 'FOOD_PAY', 'CHILD_PAY', 'NIGHT_PAY');
UPDATE pay.PayItems SET TaxField = 17 WHERE Code IN ('OVERTIME', 'HOLIDAY_PAY');
UPDATE pay.PayItems SET TaxField = 33 WHERE Code = 'INS_WORKER';
GO

-- عوامل هزینه تازه (ماهیت حقوق و دستمزد) با تفصیلی عامل
INSERT cost.CostFactors (Title, Behavior, Nature)
SELECT x.Title, 1, 1
FROM (VALUES (N'بیمه بیکاری سهم کارفرما'), (N'حق تأهل')) x (Title)
WHERE NOT EXISTS (SELECT 1 FROM cost.CostFactors f WHERE f.Title = x.Title);

DECLARE @FactorType SMALLINT = (SELECT TOP (1) Id FROM fin.DetailTypes WHERE OwnerModule = 'cost_factors' ORDER BY IsActive DESC, Id);
DECLARE @Last INT = (SELECT ISNULL(MAX(CAST(Code AS INT)), 410000) FROM fin.DetailAccounts WHERE DetailTypeId = @FactorType);
INSERT fin.DetailAccounts (DetailTypeId, Code, Title, SourceModule, SourceEntityId, CostNature)
SELECT @FactorType, CAST(@Last + ROW_NUMBER() OVER (ORDER BY f.Id) AS VARCHAR(10)), f.Title, 'costing', f.Id, f.Nature
FROM cost.CostFactors f
WHERE @FactorType IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM fin.DetailAccounts d WHERE d.DetailTypeId = @FactorType AND d.SourceEntityId = f.Id);

UPDATE pay.PayItems SET CostFactorId = (SELECT Id FROM cost.CostFactors WHERE Title = N'بیمه بیکاری سهم کارفرما')
WHERE Code = 'INS_UNEMP';
GO

INSERT pay.YearParams ([Year], Code, Title, Value)
SELECT [Year], 'MARRIAGE', N'حق تأهل ماهانه (ریال)', 5000000
FROM (SELECT DISTINCT [Year] FROM pay.YearParams) y
WHERE NOT EXISTS (SELECT 1 FROM pay.YearParams p WHERE p.[Year] = y.[Year] AND p.Code = 'MARRIAGE');

INSERT pay.PayItems (Code, Title, Kind, Source, Formula, InsuranceSubject, TaxSubject, CostFactorId, OnPayslip, SortOrder, IsSystem, TaxField)
SELECT 'MARRIAGE_PAY', N'حق تأهل', 1, 2, N'ROUND(MARRIAGE * MARRIED * DAYS / MONTH_DAYS, 0)', 0, 1,
       (SELECT Id FROM cost.CostFactors WHERE Title = N'حق تأهل'), 1, 15, 1, 7
WHERE NOT EXISTS (SELECT 1 FROM pay.PayItems WHERE Code = 'MARRIAGE_PAY');
GO

PRINT N'لیست بیمه و فایل مالیات حقوق آماده شد.';
GO
