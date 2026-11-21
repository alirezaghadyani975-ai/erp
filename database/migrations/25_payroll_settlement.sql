/* =====================================================================
   25 - حقوق و دستمزد مرحله ۴: عیدی، سنوات، مرخصی و تسویه حساب (تصمیم‌های مالک ۱۴۰۵/۰۸/۳۰)

   - عیدی و سنوات ماهانه ذخیره می‌شوند (اقلام BONUS_PROV و SEVERANCE_PROV از قبل).
   - عیدی در ماه ۱۲ (و در تسویه) با حقوق پرداخت می‌شود: BONUS_PAY، از محل ذخیره 5126.
   - بازخرید سنوات فقط در تسویه: SEVERANCE_PAY، از محل ذخیره 4201.
   - مرخصی: استفاده‌شده چیزی ثبت نمی‌کند (فقط روز آن در کارکرد)؛ مانده فقط در تسویه بازخرید و همان‌جا هزینه می‌شود: LEAVE_PAY.
   - مزایای «از محل ذخیره» (حساب روی قلم مزایا): در سند کل مانده ذخیره کارمند آزاد و اختلاف به هزینه می‌رود (ردیف provision_use).
   - pay.Attendance: LeaveDays (مرخصی استحقاقی استفاده‌شده) و IsSettlement (این ماه تسویه حساب کارمند است).
   - پارامتر LEAVE_DAYS: مرخصی استحقاقی سالانه (۲۶ روز کاری؛ ماده ۶۴ قانون کار).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

ALTER TABLE pay.Attendance ADD
    LeaveDays    DECIMAL(5,2) NOT NULL CONSTRAINT DF_Attendance_Leave DEFAULT (0),
    IsSettlement BIT          NOT NULL CONSTRAINT DF_Attendance_Settlement DEFAULT (0);
GO
ALTER TABLE pay.Attendance ADD CONSTRAINT CK_Attendance_Leave CHECK (LeaveDays BETWEEN 0 AND 31);
GO

INSERT pay.YearParams ([Year], Code, Title, Value)
SELECT [Year], 'LEAVE_DAYS', N'مرخصی استحقاقی سالانه (روز کاری)', 26
FROM (SELECT DISTINCT [Year] FROM pay.YearParams) y
WHERE NOT EXISTS (SELECT 1 FROM pay.YearParams p WHERE p.[Year] = y.[Year] AND p.Code = 'LEAVE_DAYS');
GO

DECLARE @F TABLE (Title NVARCHAR(200) PRIMARY KEY, Id INT);
INSERT @F SELECT Title, Id FROM cost.CostFactors;
DECLARE @A TABLE (Code VARCHAR(10) PRIMARY KEY, Id INT);
INSERT @A SELECT Code, Id FROM fin.Accounts WHERE [Level] = 3 AND Code IN ('5126', '4201');

INSERT pay.PayItems (Code, Title, Kind, Source, Formula, InsuranceSubject, TaxSubject, CostFactorId, AccountId, OnPayslip, SortOrder, IsSystem, TaxField)
SELECT x.Code, x.Title, 1, 2, x.Formula, 0, 1, f.Id, a.Id, 1, x.Sort, 1, x.TaxField
FROM (VALUES
    ('BONUS_PAY',     N'عیدی و پاداش',
     N'IF(MAX(MONTH = 12, SETTLEMENT) = 1, ROUND(MIN(DAILY_WAGE * BONUS_DAYS, MIN_WAGE * BONUS_CAP_DAYS) * YEAR_WORK_DAYS / YEAR_DAYS, 0), 0)',
     N'عیدی و پاداش', '5126', 30, 23),
    ('SEVERANCE_PAY', N'بازخرید سنوات',
     N'IF(SETTLEMENT = 1, ROUND((DAILY_WAGE + SENIORITY_DAILY) * SEVERANCE_DAYS * SERVICE_DAYS / 365, 0), 0)',
     N'مزایای پایان خدمت (سنوات)', '4201', 31, 27),
    ('LEAVE_PAY',     N'بازخرید مرخصی استفاده‌نشده',
     N'IF(SETTLEMENT = 1, ROUND((DAILY_WAGE + SENIORITY_DAILY) * MAX(LEAVE_BALANCE, 0), 0), 0)',
     N'بازخرید مرخصی استفاده‌نشده', NULL, 32, 28)
) x (Code, Title, Formula, Factor, Account, Sort, TaxField)
LEFT JOIN @F f ON f.Title = x.Factor
LEFT JOIN @A a ON a.Code = x.Account
WHERE NOT EXISTS (SELECT 1 FROM pay.PayItems p WHERE p.Code = x.Code);
GO

DECLARE @T INT = (SELECT Id FROM fin.PostingTemplates WHERE SourceDocType = 'pay_run');
INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder)
VALUES (@T, 'provision_use', N'استفاده از ذخیره (عیدی، سنوات)', 1, NULL, 'source', N'کارمند', 5);
GO

PRINT N'عیدی، سنوات، مرخصی و تسویه حساب آماده شد.';
GO
