/* =====================================================================
   23 - حقوق و دستمزد: وام و مساعده با اقساط، قطعی کردن اجرا و سند حسابداری حقوق (الگوی pay_run)

   - pay.Loans: وام (اقساطی) و مساعده (یک‌جا) هر کارمند. پرداخت خود وام/مساعده از خزانه‌داری ثبت می‌شود
     (نوع پرداخت با معین 2522/2523 و تفصیلی کارمند)؛ این‌جا فقط مبلغ و برنامه کسر است.
   - pay.RunLoanDeductions: قسط کسرشده هر وام در هر اجرا (با محاسبه دوباره ساخته می‌شود؛ اجرای قطعی تغییر نمی‌کند).
   - الگوی pay_run: هزینه (قاعده cost: عامل × مرکز)، حقوق پرداختنی 5125 (تفصیلی کارمند)، کسور و بدهی‌ها (قاعده source: معین قلم).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

-- نوع: 1 وام، 2 مساعده
CREATE TABLE pay.Loans (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Loans PRIMARY KEY,
    EmployeeId        INT           NOT NULL CONSTRAINT FK_Loans_Employee REFERENCES hr.Employees (Id),
    Kind              TINYINT       NOT NULL,
    DocNo             INT           NOT NULL,
    LoanDate          DATE          NOT NULL,
    Amount            DECIMAL(24,0) NOT NULL,
    Installments      SMALLINT      NOT NULL,
    InstallmentAmount DECIMAL(24,0) NOT NULL,
    StartYear         SMALLINT      NOT NULL,
    StartMonth        TINYINT       NOT NULL,
    [Description]     NVARCHAR(300) NULL,
    CreatedBy         INT           NOT NULL,
    CreatedAt         DATETIME2(0)  NOT NULL CONSTRAINT DF_Loans_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Loans_No UNIQUE (Kind, DocNo),
    CONSTRAINT CK_Loans_Kind CHECK (Kind IN (1, 2)),
    CONSTRAINT CK_Loans_Amount CHECK (Amount > 0 AND InstallmentAmount > 0 AND InstallmentAmount <= Amount),
    CONSTRAINT CK_Loans_Installments CHECK (Installments BETWEEN 1 AND 240 AND (Kind = 1 OR Installments = 1)),
    CONSTRAINT CK_Loans_Start CHECK (StartMonth BETWEEN 1 AND 12)
);
CREATE INDEX IX_Loans_Employee ON pay.Loans (EmployeeId);
GO

CREATE TABLE pay.RunLoanDeductions (
    RunId  INT           NOT NULL CONSTRAINT FK_RunLoan_Run REFERENCES pay.Runs (Id) ON DELETE CASCADE,
    LoanId INT           NOT NULL CONSTRAINT FK_RunLoan_Loan REFERENCES pay.Loans (Id),
    Amount DECIMAL(24,0) NOT NULL,
    CONSTRAINT PK_RunLoanDeductions PRIMARY KEY (RunId, LoanId),
    CONSTRAINT CK_RunLoan_Amount CHECK (Amount > 0)
);
GO

-- اجرای قطعی: اقساط کسرشده تغییر نمی‌کند؛ جمع کسر هر وام از مبلغش بیشتر نمی‌شود
CREATE TRIGGER pay.trg_RunLoanDeductions ON pay.RunLoanDeductions AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM pay.Runs r WHERE r.[Status] = 3
               AND r.Id IN (SELECT RunId FROM inserted UNION SELECT RunId FROM deleted))
        THROW 50422, N'اجرای حقوق قطعی است و نتیجه محاسبه آن تغییر نمی‌کند.', 1;
    IF EXISTS (SELECT 1 FROM pay.Loans l
               WHERE l.Id IN (SELECT LoanId FROM inserted)
                 AND (SELECT SUM(d.Amount) FROM pay.RunLoanDeductions d WHERE d.LoanId = l.Id) > l.Amount)
        THROW 50430, N'جمع اقساط کسرشده از مبلغ وام بیشتر می‌شود.', 1;
END;
GO

-- وامی که قسطش کسر شده حذف نمی‌شود و مبلغش از کسرشده کمتر نمی‌شود
CREATE TRIGGER pay.trg_Loans_Protect ON pay.Loans AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted)
               AND EXISTS (SELECT 1 FROM pay.RunLoanDeductions x WHERE x.LoanId = d.Id))
        THROW 50431, N'از این وام در حقوق قسط کسر شده و حذف نمی‌شود.', 1;
    IF EXISTS (SELECT 1 FROM inserted i
               WHERE i.Amount < (SELECT ISNULL(SUM(x.Amount), 0) FROM pay.RunLoanDeductions x WHERE x.LoanId = i.Id)
                  OR (i.EmployeeId <> (SELECT d.EmployeeId FROM deleted d WHERE d.Id = i.Id)
                      AND EXISTS (SELECT 1 FROM pay.RunLoanDeductions x WHERE x.LoanId = i.Id)))
        THROW 50432, N'مبلغ وام از اقساط کسرشده کمتر نمی‌شود و کارمندش عوض نمی‌شود.', 1;
END;
GO

-- الگوی سند حقوق ماه
INSERT fin.PostingTemplates (SourceDocType, Title, SourceModule) VALUES ('pay_run', N'حقوق و دستمزد ماه', 'payroll');
DECLARE @T INT = SCOPE_IDENTITY();
DECLARE @Payable INT = (SELECT Id FROM fin.Accounts WHERE Code = '5125' AND [Level] = 3);
INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder) VALUES
    (@T, 'expense',   N'هزینه حقوق، سهم کارفرما و ذخیره‌ها',  1, NULL,     'cost',   N'عامل و مرکز هزینه',  1),
    (@T, 'net',       N'حقوق پرداختنی (خالص)',              2, @Payable, NULL,     N'کارمند',             2),
    (@T, 'deduction', N'کسور حقوق (بیمه، مالیات، وام، ...)',  2, NULL,     'source', N'کارمند',             3),
    (@T, 'liability', N'بیمه سهم کارفرما و ذخیره‌ها',         2, NULL,     'source', N'کارمند',             4);
GO

PRINT N'وام و مساعده و سند حقوق آماده شد.';
GO
