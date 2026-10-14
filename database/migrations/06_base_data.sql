/* =====================================================================
   06 - جداول پایه، بانک‌ها، رابطین و پیوست‌های اشخاص

   - جدول پایه عمومی (base.LookupCategories / base.LookupItems): فهرست‌های ساده با کد، عنوان و والد
     (کشور، استان، شهر زیر استان، نوع نشانی، گروه اشخاص درختی، سمت رابط). کاربر فهرست‌ها را کامل می‌کند.
   - بانک‌ها (base.Banks) با کد بانک در شبا و پیش‌شماره کارت؛ تشخیص بانک از این جدول است.
   - فیلدهای متنی شخص که باید قابل گزارش باشند به این جداول وصل می‌شوند و داده متنی موجود منتقل می‌شود.
   - رابطین شخص و پیوست مدارک.
   - اطلاعات پرسنلی خامِ اسکریپت ۰۵ (hr.Employees) حذف می‌شود؛ کارگزینی (دوره‌های استخدام، حکم، ...)
     بعداً در ماژول خودش طراحی می‌شود. schema hr باقی می‌ماند.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------------------------------------------------------------------
   ۱) جدول پایه عمومی
   --------------------------------------------------------------------- */
CREATE TABLE base.LookupCategories (
    Id               SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LookupCategories PRIMARY KEY,
    Code             VARCHAR(40)   NOT NULL CONSTRAINT UQ_LookupCategories_Code UNIQUE,
    Title            NVARCHAR(100) NOT NULL,
    -- اقلام این فهرست زیر اقلام فهرست دیگری‌اند (شهر ← استان)
    ParentCategoryId SMALLINT      NULL CONSTRAINT FK_LookupCategories_Parent REFERENCES base.LookupCategories (Id),
    IsTree           BIT           NOT NULL CONSTRAINT DF_LookupCategories_IsTree DEFAULT (0), -- اقلام درختی در همان فهرست
    IsSystem         BIT           NOT NULL CONSTRAINT DF_LookupCategories_IsSystem DEFAULT (1),
    [Description]    NVARCHAR(300) NULL,
    SortOrder        SMALLINT      NOT NULL CONSTRAINT DF_LookupCategories_Sort DEFAULT (100),
    CONSTRAINT CK_LookupCategories_ParentOrTree CHECK (NOT (ParentCategoryId IS NOT NULL AND IsTree = 1))
);
GO

CREATE TABLE base.LookupItems (
    Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LookupItems PRIMARY KEY,
    CategoryId SMALLINT      NOT NULL CONSTRAINT FK_LookupItems_Category REFERENCES base.LookupCategories (Id),
    Code       NVARCHAR(20)  NULL,
    Title      NVARCHAR(150) NOT NULL,
    ParentId   INT           NULL CONSTRAINT FK_LookupItems_Parent REFERENCES base.LookupItems (Id),
    SortOrder  INT           NOT NULL CONSTRAINT DF_LookupItems_Sort DEFAULT (0),
    IsActive   BIT           NOT NULL CONSTRAINT DF_LookupItems_IsActive DEFAULT (1),
    IsSystem   BIT           NOT NULL CONSTRAINT DF_LookupItems_IsSystem DEFAULT (0),
    ParentKey  AS ISNULL(ParentId, 0) PERSISTED
);
CREATE UNIQUE INDEX UX_LookupItems_Title ON base.LookupItems (CategoryId, ParentKey, Title);
CREATE UNIQUE INDEX UX_LookupItems_Code ON base.LookupItems (CategoryId, Code) WHERE Code IS NOT NULL;
CREATE INDEX IX_LookupItems_Parent ON base.LookupItems (ParentId);
GO

/* والد قلم باید از فهرست والد (یا برای فهرست درختی از همان فهرست) باشد */
CREATE TRIGGER base.trg_LookupItems_Parent ON base.LookupItems AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN base.LookupCategories c ON c.Id = i.CategoryId
               LEFT JOIN base.LookupItems p ON p.Id = i.ParentId
               WHERE (i.ParentId IS NOT NULL AND c.ParentCategoryId IS NULL AND c.IsTree = 0)
                  OR (i.ParentId IS NOT NULL AND c.ParentCategoryId IS NOT NULL AND p.CategoryId <> c.ParentCategoryId)
                  OR (i.ParentId IS NOT NULL AND c.IsTree = 1 AND p.CategoryId <> i.CategoryId)
                  OR i.ParentId = i.Id)
        THROW 50050, N'والد این قلم از فهرست درستی نیست.', 1;
END;
GO

INSERT base.LookupCategories (Code, Title, IsTree, [Description], SortOrder) VALUES
    ('country',      N'کشور / تابعیت', 0, N'برای تابعیت اتباع خارجی و نشانی خارج از کشور', 1),
    ('province',     N'استان',          0, NULL, 2),
    ('address_type', N'نوع نشانی',      0, NULL, 4),
    ('person_group', N'گروه اشخاص',     1, N'گروه‌بندی درختی اشخاص برای گزارش (مثلاً منطقه، صنف، گروه مشتری)', 5),
    ('contact_role', N'سمت رابط',       0, N'سمت رابطین اشخاص حقوقی', 6);
INSERT base.LookupCategories (Code, Title, ParentCategoryId, SortOrder)
SELECT 'city', N'شهر', Id, 3 FROM base.LookupCategories WHERE Code = 'province';
GO

DECLARE @Country SMALLINT = (SELECT Id FROM base.LookupCategories WHERE Code = 'country');
DECLARE @Province SMALLINT = (SELECT Id FROM base.LookupCategories WHERE Code = 'province');
DECLARE @City SMALLINT = (SELECT Id FROM base.LookupCategories WHERE Code = 'city');

INSERT base.LookupItems (CategoryId, Code, Title, SortOrder, IsSystem) VALUES
    (@Country, N'IR', N'ایران', 1, 1), (@Country, N'AF', N'افغانستان', 2, 1), (@Country, N'IQ', N'عراق', 3, 1),
    (@Country, N'TR', N'ترکیه', 4, 1), (@Country, N'AE', N'امارات متحده عربی', 5, 1), (@Country, N'PK', N'پاکستان', 6, 1),
    (@Country, N'AZ', N'جمهوری آذربایجان', 7, 1), (@Country, N'AM', N'ارمنستان', 8, 1), (@Country, N'TM', N'ترکمنستان', 9, 1),
    (@Country, N'TJ', N'تاجیکستان', 10, 1), (@Country, N'CN', N'چین', 11, 1), (@Country, N'IN', N'هند', 12, 1),
    (@Country, N'RU', N'روسیه', 13, 1), (@Country, N'DE', N'آلمان', 14, 1), (@Country, N'IT', N'ایتالیا', 15, 1),
    (@Country, N'OM', N'عمان', 16, 1), (@Country, N'QA', N'قطر', 17, 1), (@Country, N'KR', N'کره جنوبی', 18, 1);

-- استان‌ها و مرکز هر استان
DECLARE @P TABLE (Seq INT IDENTITY(1,1), Province NVARCHAR(50), Capital NVARCHAR(50));
INSERT @P (Province, Capital) VALUES
    (N'آذربایجان شرقی', N'تبریز'), (N'آذربایجان غربی', N'ارومیه'), (N'اردبیل', N'اردبیل'), (N'اصفهان', N'اصفهان'),
    (N'البرز', N'کرج'), (N'ایلام', N'ایلام'), (N'بوشهر', N'بوشهر'), (N'تهران', N'تهران'),
    (N'چهارمحال و بختیاری', N'شهرکرد'), (N'خراسان جنوبی', N'بیرجند'), (N'خراسان رضوی', N'مشهد'), (N'خراسان شمالی', N'بجنورد'),
    (N'خوزستان', N'اهواز'), (N'زنجان', N'زنجان'), (N'سمنان', N'سمنان'), (N'سیستان و بلوچستان', N'زاهدان'),
    (N'فارس', N'شیراز'), (N'قزوین', N'قزوین'), (N'قم', N'قم'), (N'کردستان', N'سنندج'),
    (N'کرمان', N'کرمان'), (N'کرمانشاه', N'کرمانشاه'), (N'کهگیلویه و بویراحمد', N'یاسوج'), (N'گلستان', N'گرگان'),
    (N'گیلان', N'رشت'), (N'لرستان', N'خرم‌آباد'), (N'مازندران', N'ساری'), (N'مرکزی', N'اراک'),
    (N'هرمزگان', N'بندرعباس'), (N'همدان', N'همدان'), (N'یزد', N'یزد');

INSERT base.LookupItems (CategoryId, Title, SortOrder, IsSystem)
SELECT @Province, Province, Seq, 1 FROM @P ORDER BY Seq;

INSERT base.LookupItems (CategoryId, Title, ParentId, SortOrder, IsSystem)
SELECT @City, p.Capital, i.Id, 1, 1
FROM @P p JOIN base.LookupItems i ON i.CategoryId = @Province AND i.Title = p.Province;

-- چند شهر بزرگ دیگر؛ بقیه را کاربر اضافه می‌کند
DECLARE @C TABLE (Province NVARCHAR(50), City NVARCHAR(50));
INSERT @C VALUES
    (N'تهران', N'شهریار'), (N'تهران', N'اسلامشهر'), (N'تهران', N'ری'), (N'تهران', N'ورامین'), (N'تهران', N'قدس'),
    (N'تهران', N'پاکدشت'), (N'تهران', N'رباط‌کریم'), (N'البرز', N'فردیس'), (N'البرز', N'نظرآباد'), (N'البرز', N'هشتگرد'),
    (N'اصفهان', N'کاشان'), (N'اصفهان', N'نجف‌آباد'), (N'اصفهان', N'خمینی‌شهر'), (N'اصفهان', N'شاهین‌شهر'),
    (N'خراسان رضوی', N'نیشابور'), (N'خراسان رضوی', N'سبزوار'), (N'فارس', N'مرودشت'), (N'فارس', N'کازرون'),
    (N'خوزستان', N'دزفول'), (N'خوزستان', N'آبادان'), (N'خوزستان', N'ماهشهر'), (N'آذربایجان شرقی', N'مراغه'),
    (N'آذربایجان غربی', N'خوی'), (N'آذربایجان غربی', N'میاندوآب'), (N'مازندران', N'بابل'), (N'مازندران', N'آمل'),
    (N'مازندران', N'قائم‌شهر'), (N'گیلان', N'بندر انزلی'), (N'گیلان', N'لاهیجان'), (N'مرکزی', N'ساوه'),
    (N'کرمان', N'سیرجان'), (N'کرمان', N'رفسنجان'), (N'هرمزگان', N'قشم'), (N'هرمزگان', N'کیش'),
    (N'قزوین', N'الوند'), (N'یزد', N'میبد'), (N'همدان', N'ملایر'), (N'لرستان', N'بروجرد'), (N'گلستان', N'گنبد کاووس');

INSERT base.LookupItems (CategoryId, Title, ParentId, SortOrder, IsSystem)
SELECT @City, c.City, i.Id, 10, 1
FROM @C c JOIN base.LookupItems i ON i.CategoryId = @Province AND i.Title = c.Province;

INSERT base.LookupItems (CategoryId, Title, SortOrder, IsSystem)
SELECT c.Id, v.Title, v.Sort, 1
FROM base.LookupCategories c
CROSS APPLY (VALUES (N'دفتر مرکزی', 1), (N'کارخانه', 2), (N'انبار', 3), (N'شعبه', 4), (N'فروشگاه', 5),
                    (N'منزل', 6), (N'محل کار', 7)) v(Title, Sort)
WHERE c.Code = 'address_type';

INSERT base.LookupItems (CategoryId, Title, SortOrder, IsSystem)
SELECT c.Id, v.Title, v.Sort, 1
FROM base.LookupCategories c
CROSS APPLY (VALUES (N'مدیرعامل', 1), (N'عضو هیئت‌مدیره', 2), (N'مدیر مالی', 3), (N'حسابدار', 4),
                    (N'مدیر خرید / کارپرداز', 5), (N'مدیر فروش', 6), (N'مالک', 7), (N'نماینده', 8)) v(Title, Sort)
WHERE c.Code = 'contact_role';
GO

/* ---------------------------------------------------------------------
   ۲) بانک‌ها
   --------------------------------------------------------------------- */
CREATE TABLE base.Banks (
    Id           SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Banks PRIMARY KEY,
    Title        NVARCHAR(60)  NOT NULL CONSTRAINT UQ_Banks_Title UNIQUE,
    IbanCode     CHAR(3)       NULL,     -- رقم‌های ۵ تا ۷ شبا
    CardPrefixes VARCHAR(300)  NULL,     -- شش رقم اول کارت، با کاما جدا
    IsActive     BIT           NOT NULL CONSTRAINT DF_Banks_IsActive DEFAULT (1),
    SortOrder    SMALLINT      NOT NULL CONSTRAINT DF_Banks_Sort DEFAULT (100),
    CONSTRAINT CK_Banks_IbanCode CHECK (IbanCode IS NULL OR IbanCode NOT LIKE '%[^0-9]%')
);
CREATE UNIQUE INDEX UX_Banks_IbanCode ON base.Banks (IbanCode) WHERE IbanCode IS NOT NULL;
GO
INSERT base.Banks (Title, IbanCode, CardPrefixes, SortOrder) VALUES
    (N'ملی ایران', '017', '603799', 1), (N'ملت', '012', '610433,991975', 2), (N'تجارت', '018', '627353,585983', 3),
    (N'صادرات ایران', '019', '603769', 4), (N'سپه', '015', '589210', 5), (N'کشاورزی', '016', '603770,639217', 6),
    (N'مسکن', '014', '628023', 7), (N'رفاه کارگران', '013', '589463', 8), (N'پست بانک', '021', '627760', 9),
    (N'صنعت و معدن', '011', '627961', 10), (N'توسعه صادرات', '020', '627648,207177', 11), (N'توسعه تعاون', '022', '502908', 12),
    (N'پارسیان', '054', '622106,639194,627884', 13), (N'پاسارگاد', '057', '502229,639347', 14), (N'سامان', '056', '621986', 15),
    (N'اقتصاد نوین', '055', '627412', 16), (N'کارآفرین', '053', '627488,502910', 17), (N'سینا', '059', '639346', 18),
    (N'سرمایه', '058', '639607', 19), (N'شهر', '061', '502806,504706', 20), (N'آینده', '062', '636214', 21),
    (N'دی', '066', '502938', 22), (N'ایران زمین', '069', '505785', 23), (N'گردشگری', '064', '505416', 24),
    (N'خاورمیانه', '078', '585947', 25), (N'قرض‌الحسنه مهر ایران', '060', '606373', 26),
    (N'قرض‌الحسنه رسالت', '070', '504172', 27), (N'ملل', '075', '606256', 28), (N'کوثر', '073', '505801', 29),
    (N'نور', '080', '507677', 30), (N'حکمت ایرانیان', '065', '636949', 31), (N'ایران و ونزوئلا', '095', '581874', 32),
    (N'مؤسسه اعتباری توسعه', '051', NULL, 33), (N'مهر اقتصاد', '079', NULL, 34), (N'بانک مرکزی', '010', NULL, 35);
GO

/* ---------------------------------------------------------------------
   ۳) فیلدهای شخص ← جداول پایه (با انتقال داده متنی موجود)
   --------------------------------------------------------------------- */
ALTER TABLE base.Persons ADD
    NationalityId      INT NULL CONSTRAINT FK_Persons_Nationality REFERENCES base.LookupItems (Id),
    BirthCityId        INT NULL CONSTRAINT FK_Persons_BirthCity REFERENCES base.LookupItems (Id),
    IdIssueCityId      INT NULL CONSTRAINT FK_Persons_IdIssueCity REFERENCES base.LookupItems (Id),
    RegistrationCityId INT NULL CONSTRAINT FK_Persons_RegistrationCity REFERENCES base.LookupItems (Id),
    PersonGroupId      INT NULL CONSTRAINT FK_Persons_Group REFERENCES base.LookupItems (Id);
ALTER TABLE base.PersonAddresses ADD
    AddressTypeId INT NULL CONSTRAINT FK_PersonAddresses_Type REFERENCES base.LookupItems (Id),
    ProvinceId    INT NULL CONSTRAINT FK_PersonAddresses_Province REFERENCES base.LookupItems (Id),
    CityId        INT NULL CONSTRAINT FK_PersonAddresses_City REFERENCES base.LookupItems (Id);
ALTER TABLE base.PersonBankAccounts ADD
    BankId SMALLINT NULL CONSTRAINT FK_PersonBankAccounts_Bank REFERENCES base.Banks (Id);
GO

DECLARE @Country SMALLINT = (SELECT Id FROM base.LookupCategories WHERE Code = 'country');
DECLARE @Province SMALLINT = (SELECT Id FROM base.LookupCategories WHERE Code = 'province');
DECLARE @City SMALLINT = (SELECT Id FROM base.LookupCategories WHERE Code = 'city');
DECLARE @AddrType SMALLINT = (SELECT Id FROM base.LookupCategories WHERE Code = 'address_type');

-- متن‌هایی که در فهرست نیستند به فهرست اضافه می‌شوند (شهر بدون استان)
INSERT base.LookupItems (CategoryId, Title)
SELECT DISTINCT @Country, p.Nationality FROM base.Persons p
WHERE p.Nationality IS NOT NULL AND NOT EXISTS (SELECT 1 FROM base.LookupItems i WHERE i.CategoryId = @Country AND i.Title = p.Nationality);

INSERT base.LookupItems (CategoryId, Title)
SELECT DISTINCT @Province, a.Province FROM base.PersonAddresses a
WHERE a.Province IS NOT NULL AND NOT EXISTS (SELECT 1 FROM base.LookupItems i WHERE i.CategoryId = @Province AND i.Title = a.Province);

INSERT base.LookupItems (CategoryId, Title)
SELECT DISTINCT @AddrType, a.Title FROM base.PersonAddresses a
WHERE a.Title IS NOT NULL AND NOT EXISTS (SELECT 1 FROM base.LookupItems i WHERE i.CategoryId = @AddrType AND i.Title = a.Title);

INSERT base.LookupItems (CategoryId, Title)
SELECT DISTINCT @City, t.Title
FROM (SELECT BirthPlace AS Title FROM base.Persons UNION SELECT IdIssuePlace FROM base.Persons
      UNION SELECT RegistrationPlace FROM base.Persons UNION SELECT City FROM base.PersonAddresses) t
WHERE t.Title IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM base.LookupItems i WHERE i.CategoryId = @City AND i.Title = t.Title);

UPDATE p SET
    NationalityId      = (SELECT MIN(Id) FROM base.LookupItems WHERE CategoryId = @Country AND Title = p.Nationality),
    BirthCityId        = (SELECT MIN(Id) FROM base.LookupItems WHERE CategoryId = @City AND Title = p.BirthPlace),
    IdIssueCityId      = (SELECT MIN(Id) FROM base.LookupItems WHERE CategoryId = @City AND Title = p.IdIssuePlace),
    RegistrationCityId = (SELECT MIN(Id) FROM base.LookupItems WHERE CategoryId = @City AND Title = p.RegistrationPlace)
FROM base.Persons p;

UPDATE a SET
    AddressTypeId = (SELECT MIN(Id) FROM base.LookupItems WHERE CategoryId = @AddrType AND Title = a.Title),
    ProvinceId    = (SELECT MIN(Id) FROM base.LookupItems WHERE CategoryId = @Province AND Title = a.Province),
    CityId        = (SELECT MIN(Id) FROM base.LookupItems WHERE CategoryId = @City AND Title = a.City)
FROM base.PersonAddresses a;

UPDATE b SET BankId = COALESCE(
    (SELECT k.Id FROM base.Banks k WHERE k.Title = b.BankName),
    (SELECT k.Id FROM base.Banks k WHERE k.IbanCode = SUBSTRING(b.Iban, 5, 3)))
FROM base.PersonBankAccounts b;
GO

ALTER TABLE base.Persons DROP COLUMN Nationality, BirthPlace, IdIssuePlace, RegistrationPlace;
ALTER TABLE base.PersonAddresses DROP COLUMN Title, Province, City;
ALTER TABLE base.PersonBankAccounts DROP COLUMN BankName;
GO

/* ---------------------------------------------------------------------
   ۴) رابطین و پیوست‌ها
   --------------------------------------------------------------------- */
CREATE TABLE base.PersonContacts (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PersonContacts PRIMARY KEY,
    PersonId      INT           NOT NULL CONSTRAINT FK_PersonContacts_Person REFERENCES base.Persons (Id) ON DELETE CASCADE,
    FullName      NVARCHAR(150) NOT NULL,
    ContactRoleId INT           NULL CONSTRAINT FK_PersonContacts_Role REFERENCES base.LookupItems (Id),
    Mobile        VARCHAR(11)   NULL,
    Phone         VARCHAR(11)   NULL,
    Email         VARCHAR(100)  NULL,
    Notes         NVARCHAR(300) NULL
);
CREATE INDEX IX_PersonContacts_Person ON base.PersonContacts (PersonId);
GO

CREATE TABLE base.PersonAttachments (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PersonAttachments PRIMARY KEY,
    PersonId    INT            NOT NULL CONSTRAINT FK_PersonAttachments_Person REFERENCES base.Persons (Id) ON DELETE CASCADE,
    Title       NVARCHAR(150)  NOT NULL,
    [FileName]  NVARCHAR(260)  NOT NULL,
    ContentType VARCHAR(100)   NOT NULL,
    SizeBytes   INT            NOT NULL,
    Content     VARBINARY(MAX) NOT NULL,
    UploadedAt  DATETIME2(0)   NOT NULL CONSTRAINT DF_PersonAttachments_At DEFAULT (SYSUTCDATETIME()),
    UploadedBy  INT            NOT NULL,
    CONSTRAINT CK_PersonAttachments_Size CHECK (SizeBytes BETWEEN 1 AND 10485760)   -- حداکثر ۱۰ مگابایت
);
CREATE INDEX IX_PersonAttachments_Person ON base.PersonAttachments (PersonId);
GO

/* ---------------------------------------------------------------------
   ۵) اطلاعات پرسنلی خام حذف می‌شود (کارگزینی بعداً در ماژول خودش)
   --------------------------------------------------------------------- */
DROP TABLE hr.Employees;
GO

PRINT N'جداول پایه، بانک‌ها، رابطین و پیوست‌ها اضافه شد.';
