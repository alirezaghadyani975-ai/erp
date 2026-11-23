/* =====================================================================
   29 - حقوق: گروه استخدامی، الگوی محاسبه و اتصال کارکنان (docs/payroll-templates-design.md)

   - EmploymentGroups: قواعد هر گروه کارکنان؛ GroupParams مقدار پارامترهای سال (BONUS_DAYS، SEVERANCE_DAYS، LEAVE_DAYS،
     OT_RATE، DAY_HOURS، ...) را برای این گروه جایگزین می‌کند؛ نرخ‌های بیمه اختیاری جای نرخ کارگاه را می‌گیرد.
   - CalcTemplates: الگوی محاسبه = گروه + اقلام فعال (TemplateItems)؛ Formula خالی = فرمول پیش‌فرض قلم.
   - EmployeeTemplates: الگوی هر کارمند از تاریخ (تاریخچه)؛ محاسبه ماه الگوی معتبر پایان ماه را می‌گیرد.
   - Attendance.TemplateId: الگوی به‌کاررفته در محاسبه (حقوق قطعی‌شده با همان می‌ماند).
   - مهاجرت: گروه و الگوی «پیش‌فرض» با همه اقلام فعال و فرمول فعلی؛ همه کارکنان از تاریخ استخدام به آن وصل می‌شوند.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE TABLE pay.EmploymentGroups (
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmploymentGroups PRIMARY KEY,
    Code             NVARCHAR(20)  NOT NULL CONSTRAINT UQ_EmploymentGroups_Code UNIQUE,
    Title            NVARCHAR(100) NOT NULL,
    -- نرخ‌های بیمه برای این گروه (درصد)؛ NULL = نرخ کارگاه
    EmployeeRate     DECIMAL(5,2)  NULL,
    EmployerRate     DECIMAL(5,2)  NULL,
    UnemploymentRate DECIMAL(5,2)  NULL,
    IsActive         BIT           NOT NULL CONSTRAINT DF_EmploymentGroups_Active DEFAULT (1),
    Notes            NVARCHAR(500) NULL,
    CONSTRAINT CK_EmploymentGroups_Rates CHECK ((EmployeeRate IS NULL OR EmployeeRate BETWEEN 0 AND 100)
        AND (EmployerRate IS NULL OR EmployerRate BETWEEN 0 AND 100) AND (UnemploymentRate IS NULL OR UnemploymentRate BETWEEN 0 AND 100))
);
GO

CREATE TABLE pay.GroupParams (
    GroupId INT           NOT NULL CONSTRAINT FK_GroupParams_Group REFERENCES pay.EmploymentGroups (Id) ON DELETE CASCADE,
    Code    VARCHAR(30)   NOT NULL,
    Value   DECIMAL(24,6) NOT NULL,
    CONSTRAINT PK_GroupParams PRIMARY KEY (GroupId, Code)
);
GO

CREATE TABLE pay.CalcTemplates (
    Id       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CalcTemplates PRIMARY KEY,
    Code     NVARCHAR(20)  NOT NULL CONSTRAINT UQ_CalcTemplates_Code UNIQUE,
    Title    NVARCHAR(100) NOT NULL,
    GroupId  INT           NOT NULL CONSTRAINT FK_CalcTemplates_Group REFERENCES pay.EmploymentGroups (Id),
    IsActive BIT           NOT NULL CONSTRAINT DF_CalcTemplates_Active DEFAULT (1),
    Notes    NVARCHAR(500) NULL
);
GO

CREATE TABLE pay.TemplateItems (
    TemplateId INT            NOT NULL CONSTRAINT FK_TemplateItems_Template REFERENCES pay.CalcTemplates (Id) ON DELETE CASCADE,
    PayItemId  INT            NOT NULL CONSTRAINT FK_TemplateItems_Item REFERENCES pay.PayItems (Id),
    Formula    NVARCHAR(2000) NULL,   -- NULL = فرمول پیش‌فرض قلم
    CONSTRAINT PK_TemplateItems PRIMARY KEY (TemplateId, PayItemId)
);
GO

CREATE TABLE pay.EmployeeTemplates (
    Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeTemplates PRIMARY KEY,
    EmployeeId INT          NOT NULL CONSTRAINT FK_EmployeeTemplates_Employee REFERENCES hr.Employees (Id),
    TemplateId INT          NOT NULL CONSTRAINT FK_EmployeeTemplates_Template REFERENCES pay.CalcTemplates (Id),
    FromDate   DATE         NOT NULL,
    CreatedBy  INT          NOT NULL,
    CreatedAt  DATETIME2(0) NOT NULL CONSTRAINT DF_EmployeeTemplates_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_EmployeeTemplates UNIQUE (EmployeeId, FromDate)
);
GO

ALTER TABLE pay.Attendance ADD TemplateId INT NULL CONSTRAINT FK_Attendance_Template REFERENCES pay.CalcTemplates (Id);
GO

/* ---------------- مهاجرت: پیش‌فرض ---------------- */
INSERT pay.EmploymentGroups (Code, Title, Notes) VALUES (N'DEFAULT', N'پیش‌فرض', N'قواعد پارامترهای سال؛ ساخته‌شده در مهاجرت ۲۹');
DECLARE @G INT = SCOPE_IDENTITY();
INSERT pay.CalcTemplates (Code, Title, GroupId, Notes) VALUES (N'DEFAULT', N'پیش‌فرض', @G, N'همه اقلام فعال با فرمول پیش‌فرض؛ ساخته‌شده در مهاجرت ۲۹');
DECLARE @T INT = SCOPE_IDENTITY();
INSERT pay.TemplateItems (TemplateId, PayItemId) SELECT @T, Id FROM pay.PayItems WHERE IsActive = 1;
INSERT pay.EmployeeTemplates (EmployeeId, TemplateId, FromDate, CreatedBy)
    SELECT Id, @T, HireDate, 1 FROM hr.Employees;
GO
