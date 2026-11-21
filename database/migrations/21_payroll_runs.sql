/* =====================================================================
   21 - حقوق و دستمزد مرحله ۲ (محاسبه): اجرای ماهانه هر کارگاه، کارکرد، تقسیم مرکز هزینه، ورودی‌های ماه و نتیجه
   (طرح docs/payroll-design.md بند ۴)

   - pay.Runs: یک اجرا برای هر کارگاه در هر ماه شمسی؛ وضعیت 1 پیش‌نویس (نیازمند محاسبه)، 2 محاسبه‌شده، 3 قطعی.
   - pay.Attendance: کارکرد هر کارمند (روز کارکرد، روز بیمه، ساعت‌ها، غیبت).
   - pay.AttendanceCenters: تقسیم هزینه کارکرد بین مراکز به درصد (پیش‌فرض: مرکز پرونده ۱۰۰٪).
   - pay.AttendanceInputs: مبلغ اقلام «ورود ماهانه»، «وام و مساعده» و «سامانه مالیات» (مالیات بعد از ورود نتیجه سامانه).
   - pay.RunLines: نتیجه محاسبه (کارمند × قلم). اجرای قطعی تغییر نمی‌کند.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE TABLE pay.Runs (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Runs PRIMARY KEY,
    [Year]        SMALLINT      NOT NULL,
    [Month]       TINYINT       NOT NULL,
    WorkshopId    INT           NOT NULL CONSTRAINT FK_Runs_Workshop REFERENCES hr.Workshops (Id),
    [Status]      TINYINT       NOT NULL CONSTRAINT DF_Runs_Status DEFAULT (1),
    MonthDays     TINYINT       NOT NULL,           -- MONTH_DAYS هنگام ساخت (طبق تنظیمات)
    [Description] NVARCHAR(300) NULL,
    CreatedBy     INT           NOT NULL,
    CreatedAt     DATETIME2(0)  NOT NULL CONSTRAINT DF_Runs_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CalculatedBy  INT           NULL,
    CalculatedAt  DATETIME2(0)  NULL,
    CONSTRAINT UQ_Runs_Period UNIQUE ([Year], [Month], WorkshopId),
    CONSTRAINT CK_Runs_Month  CHECK ([Month] BETWEEN 1 AND 12),
    CONSTRAINT CK_Runs_Status CHECK ([Status] BETWEEN 1 AND 3),
    CONSTRAINT CK_Runs_Days   CHECK (MonthDays BETWEEN 29 AND 31)
);
GO

CREATE TABLE pay.Attendance (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Attendance PRIMARY KEY,
    RunId         INT          NOT NULL CONSTRAINT FK_Attendance_Run REFERENCES pay.Runs (Id) ON DELETE CASCADE,
    EmployeeId    INT          NOT NULL CONSTRAINT FK_Attendance_Employee REFERENCES hr.Employees (Id),
    WorkDays      DECIMAL(5,2) NOT NULL,
    InsuranceDays TINYINT      NOT NULL,
    OtHours       DECIMAL(6,2) NOT NULL CONSTRAINT DF_Attendance_Ot DEFAULT (0),
    NightHours    DECIMAL(6,2) NOT NULL CONSTRAINT DF_Attendance_Night DEFAULT (0),
    HolidayHours  DECIMAL(6,2) NOT NULL CONSTRAINT DF_Attendance_Holiday DEFAULT (0),
    AbsentDays    DECIMAL(5,2) NOT NULL CONSTRAINT DF_Attendance_Absent DEFAULT (0),
    CONSTRAINT UQ_Attendance UNIQUE (RunId, EmployeeId),
    CONSTRAINT CK_Attendance_Days  CHECK (WorkDays BETWEEN 0 AND 31 AND InsuranceDays <= 31 AND AbsentDays BETWEEN 0 AND 31),
    CONSTRAINT CK_Attendance_Hours CHECK (OtHours >= 0 AND NightHours >= 0 AND HolidayHours >= 0)
);
GO

CREATE TABLE pay.AttendanceCenters (
    AttendanceId INT          NOT NULL CONSTRAINT FK_AttCenters_Att REFERENCES pay.Attendance (Id) ON DELETE CASCADE,
    CostCenterId INT          NOT NULL CONSTRAINT FK_AttCenters_Center REFERENCES cost.CostCenters (Id),
    [Percent]    DECIMAL(5,2) NOT NULL,
    CONSTRAINT PK_AttendanceCenters PRIMARY KEY (AttendanceId, CostCenterId),
    CONSTRAINT CK_AttCenters_Percent CHECK ([Percent] > 0 AND [Percent] <= 100)
);
GO

CREATE TABLE pay.AttendanceInputs (
    AttendanceId INT           NOT NULL CONSTRAINT FK_AttInputs_Att REFERENCES pay.Attendance (Id) ON DELETE CASCADE,
    PayItemId    INT           NOT NULL CONSTRAINT FK_AttInputs_Item REFERENCES pay.PayItems (Id),
    Amount       DECIMAL(24,0) NOT NULL,
    CONSTRAINT PK_AttendanceInputs PRIMARY KEY (AttendanceId, PayItemId),
    CONSTRAINT CK_AttInputs_Amount CHECK (Amount >= 0)
);
GO

CREATE TABLE pay.RunLines (
    RunId      INT           NOT NULL CONSTRAINT FK_RunLines_Run REFERENCES pay.Runs (Id) ON DELETE CASCADE,
    EmployeeId INT           NOT NULL CONSTRAINT FK_RunLines_Employee REFERENCES hr.Employees (Id),
    PayItemId  INT           NOT NULL CONSTRAINT FK_RunLines_Item REFERENCES pay.PayItems (Id),
    Amount     DECIMAL(24,4) NOT NULL,
    CONSTRAINT PK_RunLines PRIMARY KEY (RunId, EmployeeId, PayItemId)
);
GO

-- ورودی ماه فقط برای اقلامی که منبعشان ورود ماهانه، وام و مساعده یا سامانه مالیات است
CREATE TRIGGER pay.trg_AttendanceInputs_Source ON pay.AttendanceInputs AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN pay.PayItems p ON p.Id = i.PayItemId WHERE p.Source NOT IN (3, 4, 5))
        THROW 50420, N'ورودی ماه فقط برای اقلام «ورود ماهانه»، «وام و مساعده» و «سامانه مالیات» است.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN pay.Attendance a ON a.Id = i.AttendanceId JOIN pay.Runs r ON r.Id = a.RunId WHERE r.[Status] = 3)
        THROW 50421, N'اجرای حقوق قطعی است و کارکرد آن تغییر نمی‌کند.', 1;
END;
GO

-- اجرای قطعی: کارکرد، ورودی‌ها و نتیجه تغییر نمی‌کند
CREATE TRIGGER pay.trg_Attendance_Final ON pay.Attendance AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM pay.Runs r WHERE r.[Status] = 3
               AND r.Id IN (SELECT RunId FROM inserted UNION SELECT RunId FROM deleted))
        THROW 50421, N'اجرای حقوق قطعی است و کارکرد آن تغییر نمی‌کند.', 1;
END;
GO

CREATE TRIGGER pay.trg_RunLines_Final ON pay.RunLines AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM pay.Runs r WHERE r.[Status] = 3
               AND r.Id IN (SELECT RunId FROM inserted UNION SELECT RunId FROM deleted))
        THROW 50422, N'اجرای حقوق قطعی است و نتیجه محاسبه آن تغییر نمی‌کند.', 1;
END;
GO

-- کارکرد فقط برای کارمندِ همان کارگاه اجرا
CREATE TRIGGER pay.trg_Attendance_Workshop ON pay.Attendance AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN pay.Runs r ON r.Id = i.RunId JOIN hr.Employees e ON e.Id = i.EmployeeId
               WHERE e.WorkshopId <> r.WorkshopId)
        THROW 50423, N'کارمند در کارگاه این اجرای حقوق نیست.', 1;
END;
GO

PRINT N'اجرای ماهانه حقوق آماده شد.';
GO
