/* =====================================================================
   24 - حقوق و دستمزد: پرداخت گروهی حقوق
   - pay.SalaryPayments: پرداخت خالص حقوق یک اجرای قطعی از یک حساب بانکی (یک یا چند پرداخت برای هر اجرا).
   - pay.SalaryPaymentLines: مبلغ و شبای هر کارمند؛ جمع پرداخت هر کارمند از خالص حقوقش در آن اجرا بیشتر نمی‌شود.
   - الگوی pay_payment: بدهکار حقوق پرداختنی 5125 (تفصیلی کارمند)، بستانکار بانک 2811 (تفصیلی حساب بانکی).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE TABLE pay.SalaryPayments (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryPayments PRIMARY KEY,
    RunId         INT           NOT NULL CONSTRAINT FK_SalaryPayments_Run REFERENCES pay.Runs (Id),
    DocNo         INT           NOT NULL CONSTRAINT UQ_SalaryPayments_No UNIQUE,
    PayDate       DATE          NOT NULL,
    BankAccountId INT           NOT NULL CONSTRAINT FK_SalaryPayments_Bank REFERENCES trs.BankAccounts (Id),
    [Description] NVARCHAR(300) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_SalaryPayments_CreatedAt DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_SalaryPayments_Run ON pay.SalaryPayments (RunId);
GO

CREATE TABLE pay.SalaryPaymentLines (
    PaymentId  INT           NOT NULL CONSTRAINT FK_SalaryPaymentLines_Payment REFERENCES pay.SalaryPayments (Id) ON DELETE CASCADE,
    EmployeeId INT           NOT NULL CONSTRAINT FK_SalaryPaymentLines_Employee REFERENCES hr.Employees (Id),
    Amount     DECIMAL(24,0) NOT NULL,
    Iban       VARCHAR(26)   NULL,
    CONSTRAINT PK_SalaryPaymentLines PRIMARY KEY (PaymentId, EmployeeId),
    CONSTRAINT CK_SalaryPaymentLines_Amount CHECK (Amount > 0)
);
GO

-- پرداخت فقط برای اجرای قطعی؛ جمع پرداخت هر کارمند ≤ خالص حقوق او در همان اجرا
CREATE TRIGGER pay.trg_SalaryPaymentLines ON pay.SalaryPaymentLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN pay.SalaryPayments p ON p.Id = i.PaymentId JOIN pay.Runs r ON r.Id = p.RunId
               WHERE r.[Status] <> 3)
        THROW 50440, N'حقوق فقط بعد از قطعی شدن اجرا پرداخت می‌شود.', 1;
    IF EXISTS (
        SELECT 1
        FROM (SELECT DISTINCT p.RunId, i.EmployeeId FROM inserted i JOIN pay.SalaryPayments p ON p.Id = i.PaymentId) k
        WHERE (SELECT SUM(l.Amount) FROM pay.SalaryPaymentLines l JOIN pay.SalaryPayments p ON p.Id = l.PaymentId
               WHERE p.RunId = k.RunId AND l.EmployeeId = k.EmployeeId)
            > (SELECT ISNULL(SUM(CASE it.Kind WHEN 1 THEN rl.Amount WHEN 2 THEN -rl.Amount ELSE 0 END), 0)
               FROM pay.RunLines rl JOIN pay.PayItems it ON it.Id = rl.PayItemId
               WHERE rl.RunId = k.RunId AND rl.EmployeeId = k.EmployeeId))
        THROW 50441, N'جمع پرداخت کارمند از خالص حقوقش در این ماه بیشتر می‌شود.', 1;
END;
GO

-- اجرایی که پرداخت دارد از قطعی برنمی‌گردد
CREATE TRIGGER pay.trg_Runs_Paid ON pay.Runs AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
               WHERE d.[Status] = 3 AND i.[Status] <> 3 AND EXISTS (SELECT 1 FROM pay.SalaryPayments p WHERE p.RunId = i.Id))
        THROW 50442, N'برای این ماه حقوق پرداخت شده است؛ اول پرداخت‌ها را حذف کنید.', 1;
END;
GO

INSERT fin.PostingTemplates (SourceDocType, Title, SourceModule) VALUES ('pay_payment', N'پرداخت حقوق', 'payroll');
DECLARE @T INT = SCOPE_IDENTITY();
INSERT fin.PostingTemplateLines (TemplateId, LineKey, Title, Side, AccountId, AccountRule, DetailHint, SortOrder) VALUES
    (@T, 'payable', N'حقوق پرداختنی', 1, (SELECT Id FROM fin.Accounts WHERE Code = '5125' AND [Level] = 3), NULL, N'کارمند', 1),
    (@T, 'bank',    N'بانک',          2, (SELECT Id FROM fin.Accounts WHERE Code = '2811' AND [Level] = 3), NULL, N'حساب بانکی', 2);
GO

PRINT N'پرداخت گروهی حقوق آماده شد.';
GO
