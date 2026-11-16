/* =====================================================================
   دیتابیس مرکزی (ErpCentral) - 01: کاربران، نقش‌ها، شرکت‌ها و لاگ امنیتی

   این دیتابیس بین همه شرکت‌ها مشترک است. در دیتابیس هر شرکت فقط شناسه کاربر ثبت می‌شود
   (CreatedBy، audit.ChangeLog.ActorId) و کلید خارجی ندارد.

   - رمز عبور هرگز ذخیره نمی‌شود؛ فقط هش یک‌طرفه PBKDF2-SHA512 با salt تصادفی هر کاربر
     (قالب PBKDF2$SHA512$تکرار$salt$hash). از هش نمی‌شود رمز را درآورد؛ رمز فراموش‌شده فقط بازنشانی می‌شود.
     CHECK روی ستون جلوی ثبت متن خام را هم می‌گیرد.
   - نقش‌ها سراسری‌اند (مجموعه مجوز) و برای هر شرکت جدا به کاربر داده می‌شوند.
     مجوز کاربر در یک شرکت = اجتماع مجوزهای نقش‌هایش در آن شرکت؛ مجوز مستقیم کاربر نداریم.
   - محدوده داده (DataScopes): اگر بیت نوع در UserCompanies.ScopeLimits روشن باشد، کاربر فقط موارد ثبت‌شده
     در DataScopes را می‌بیند (۱ صندوق، ۲ حساب بانکی، ۳ تنخواه‌گردان، ۴ سال مالی)؛ خاموش = همه.
   - لاگ امنیتی فقط درج می‌شود (تریگر جلوی ویرایش و حذف را می‌گیرد).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE SCHEMA sec;
GO

/* شرکت‌ها و رشته اتصال دیتابیس هرکدام (کاتالوگ شرکت‌ها) */
CREATE TABLE sec.Companies (
    Id               SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Companies PRIMARY KEY,
    [Key]            VARCHAR(50)    NOT NULL CONSTRAINT UQ_Companies_Key UNIQUE,
    Title            NVARCHAR(150)  NOT NULL,
    ConnectionString NVARCHAR(500)  NOT NULL,
    IsActive         BIT            NOT NULL CONSTRAINT DF_Companies_Active DEFAULT (1),
    CreatedAt        DATETIME2(0)   NOT NULL CONSTRAINT DF_Companies_At DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Companies_Key CHECK (LEN([Key]) > 0 AND [Key] NOT LIKE '%[^A-Za-z0-9_-]%')
);
GO

CREATE TABLE sec.Users (
    Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    UserName           NVARCHAR(50)     NOT NULL CONSTRAINT UQ_Users_UserName UNIQUE,
    DisplayName        NVARCHAR(150)    NOT NULL,
    PasswordHash       VARCHAR(256)     NOT NULL,
    SecurityStamp      UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Users_Stamp DEFAULT (NEWID()),
    IsActive           BIT              NOT NULL CONSTRAINT DF_Users_Active DEFAULT (1),
    IsSystemAdmin      BIT              NOT NULL CONSTRAINT DF_Users_Admin DEFAULT (0),
    MustChangePassword BIT              NOT NULL CONSTRAINT DF_Users_MustChange DEFAULT (1),
    FailedCount        SMALLINT         NOT NULL CONSTRAINT DF_Users_Failed DEFAULT (0),
    LockoutUntil       DATETIME2(0)     NULL,
    LastLoginAt        DATETIME2(0)     NULL,
    PasswordChangedAt  DATETIME2(0)     NULL,
    CreatedAt          DATETIME2(0)     NOT NULL CONSTRAINT DF_Users_At DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Users_Hash CHECK (PasswordHash LIKE 'PBKDF2$SHA512$%$%$%'),
    CONSTRAINT CK_Users_Failed CHECK (FailedCount >= 0)
);
GO

CREATE TABLE sec.Roles (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    Title       NVARCHAR(100) NOT NULL CONSTRAINT UQ_Roles_Title UNIQUE,
    Description NVARCHAR(300) NULL,
    IsActive    BIT           NOT NULL CONSTRAINT DF_Roles_Active DEFAULT (1)
);
GO

/* کلید مجوز مثل trs.payment.create؛ فهرست کلیدها در کد هر ماژول تعریف شده است */
CREATE TABLE sec.RolePermissions (
    RoleId        INT         NOT NULL CONSTRAINT FK_RolePermissions_Role REFERENCES sec.Roles (Id) ON DELETE CASCADE,
    PermissionKey VARCHAR(80) NOT NULL,
    CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleId, PermissionKey)
);
GO

/* دسترسی کاربر به شرکت؛ OwnDocsOnly = فقط فرم‌ها و اسنادی که خودش ثبت کرده */
CREATE TABLE sec.UserCompanies (
    UserId      INT      NOT NULL CONSTRAINT FK_UserCompanies_User REFERENCES sec.Users (Id) ON DELETE CASCADE,
    CompanyId   SMALLINT NOT NULL CONSTRAINT FK_UserCompanies_Company REFERENCES sec.Companies (Id),
    OwnDocsOnly BIT      NOT NULL CONSTRAINT DF_UserCompanies_Own DEFAULT (0),
    ScopeLimits TINYINT  NOT NULL CONSTRAINT DF_UserCompanies_Scope DEFAULT (0),  -- بیت ۱ صندوق، ۲ بانک، ۴ تنخواه، ۸ سال مالی
    CONSTRAINT PK_UserCompanies PRIMARY KEY (UserId, CompanyId),
    CONSTRAINT CK_UserCompanies_Scope CHECK (ScopeLimits BETWEEN 0 AND 15)
);
GO

CREATE TABLE sec.UserCompanyRoles (
    UserId    INT      NOT NULL,
    CompanyId SMALLINT NOT NULL,
    RoleId    INT      NOT NULL CONSTRAINT FK_UserCompanyRoles_Role REFERENCES sec.Roles (Id),
    CONSTRAINT PK_UserCompanyRoles PRIMARY KEY (UserId, CompanyId, RoleId),
    CONSTRAINT FK_UserCompanyRoles_UserCompany FOREIGN KEY (UserId, CompanyId)
        REFERENCES sec.UserCompanies (UserId, CompanyId) ON DELETE CASCADE
);
CREATE INDEX IX_UserCompanyRoles_Role ON sec.UserCompanyRoles (RoleId);
GO

CREATE TABLE sec.DataScopes (
    UserId    INT      NOT NULL,
    CompanyId SMALLINT NOT NULL,
    Kind      TINYINT  NOT NULL,   -- 1 صندوق، 2 حساب بانکی، 3 تنخواه‌گردان، 4 سال مالی
    ItemId    INT      NOT NULL,
    CONSTRAINT PK_DataScopes PRIMARY KEY (UserId, CompanyId, Kind, ItemId),
    CONSTRAINT FK_DataScopes_UserCompany FOREIGN KEY (UserId, CompanyId)
        REFERENCES sec.UserCompanies (UserId, CompanyId) ON DELETE CASCADE,
    CONSTRAINT CK_DataScopes_Kind CHECK (Kind BETWEEN 1 AND 4)
);
GO

/* لاگ امنیتی: ورود موفق/ناموفق، خروج، قفل، تغییر رمز، تغییر کاربر/نقش/شرکت، تغییر شرکت جاری، دسترسی غیرمجاز */
CREATE TABLE sec.SecurityLog (
    Id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SecurityLog PRIMARY KEY,
    [At]       DATETIME2(3)  NOT NULL CONSTRAINT DF_SecurityLog_At DEFAULT (SYSUTCDATETIME()),
    EventType  TINYINT       NOT NULL,
    UserId     INT           NULL,      -- کاربری که رویداد درباره اوست
    UserName   NVARCHAR(50)  NULL,      -- نام واردشده (برای ورود ناموفق با نام ناموجود هم)
    ActorId    INT           NULL,      -- کسی که کار را انجام داد (مثلاً مدیری که رمز را بازنشانی کرد)
    CompanyKey VARCHAR(50)   NULL,
    Ip         VARCHAR(45)   NULL,
    Details    NVARCHAR(1000) NULL
);
CREATE INDEX IX_SecurityLog_At ON sec.SecurityLog ([At]);
CREATE INDEX IX_SecurityLog_User ON sec.SecurityLog (UserId, [At]);
GO

CREATE TRIGGER sec.TR_SecurityLog_ReadOnly ON sec.SecurityLog
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50200, N'لاگ امنیتی قابل ویرایش یا حذف نیست.', 1;
END;
GO

CREATE TABLE sec.Settings (
    [Key]  VARCHAR(50)   NOT NULL CONSTRAINT PK_Settings PRIMARY KEY,
    Value  INT           NOT NULL,
    Title  NVARCHAR(150) NOT NULL
);
GO

INSERT sec.Settings ([Key], Value, Title) VALUES
    ('MaxFailedLogins',   5,  N'تعداد ورود ناموفق تا قفل شدن حساب'),
    ('LockoutMinutes',    15, N'مدت قفل حساب (دقیقه)'),
    ('PasswordMinLength', 8,  N'حداقل طول رمز عبور'),
    ('IdleMinutes',       30, N'خروج خودکار بعد از بی‌کاری (دقیقه)');
GO
