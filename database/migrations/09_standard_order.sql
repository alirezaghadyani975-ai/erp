/* =====================================================================
   09 - ترتیب استاندارد کدینگ (از سخت به نقد) و اصلاح قواعد تفصیلی

   - گروه‌ها به ترتیب استاندارد ۱ تجدیدنظرشده: ۱ دارایی غیرجاری، ۲ دارایی جاری، ۳ حقوق مالکانه،
     ۴ بدهی غیرجاری، ۵ بدهی جاری (۶ تا ۹ بدون تغییر). کل‌ها داخل هر گروه هم به ترتیب صورت‌های مالی.
   - کد معین = کد کل جدید + همان رقم‌های آخر قبلی (صندوق 1001 ← 2801). شناسه حساب‌ها عوض نمی‌شود،
     پس سندها، الگوها و قواعد سر جایشان می‌مانند؛ حساب‌های اختصاصی شرکت هم با همین قاعده جابه‌جا می‌شوند.
   - همه سطح‌های تفصیلی اجباری: سند یک معین با تعداد سطح متفاوت در ردیف‌های مختلف ثبت نمی‌شود.
   - گروه تفصیلی «ماه سررسید» (۱۲ ماه، بدون سال: 150001 فروردین تا 150012 اسفند) برای اسناد پرداختنی.
   - اسناد دریافتنی به تفکیک صندوق، اسناد در جریان وصول به تفکیک حساب بانکی.
   - دارایی ثابت، استهلاک انباشته و موجودی‌ها بدون تفصیلی (زیرسیستم دارایی ثابت و انبار جزئیات را دارند)؛
     گروه‌های تفصیلی «انبارها» و «دارایی‌های ثابت» غیرفعال می‌شوند.
   - اشخاص از سپرده ضمانت‌نامه و ضمانت‌نامه‌های انتظامی حذف شد؛ قرارداد تسهیلات از هزینه مالی حذف شد.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------------------------------------------------------------------
   ۱) گروه «ماه سررسید» و غیرفعال کردن گروه‌های بدون استفاده
   --------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM fin.DetailTypes WHERE LEN(CodeFrom) = 6 AND CodeFrom <= '150012' AND '150001' <= CodeTo)
    THROW 50090, N'بازه 150001 تا 150012 (ماه سررسید) با یک گروه تفصیلی دیگر هم‌پوشانی دارد.', 1;

INSERT fin.DetailTypes (Title, OwnerModule, CodeFrom, CodeTo) VALUES (N'ماه سررسید', 'treasury', '150001', '150012');
DECLARE @Due SMALLINT = SCOPE_IDENTITY();
INSERT fin.DetailAccounts (DetailTypeId, Code, Title)
SELECT @Due, '1500' + m.No, m.Title
FROM (VALUES ('01', N'فروردین'), ('02', N'اردیبهشت'), ('03', N'خرداد'), ('04', N'تیر'), ('05', N'مرداد'), ('06', N'شهریور'),
             ('07', N'مهر'), ('08', N'آبان'), ('09', N'آذر'), ('10', N'دی'), ('11', N'بهمن'), ('12', N'اسفند')) m (No, Title);
GO

/* ---------------------------------------------------------------------
   ۲) قواعد تفصیلی (با کدهای قبلی؛ کدها در بخش ۳ عوض می‌شوند)
   --------------------------------------------------------------------- */
DECLARE @T TABLE (TypeKey VARCHAR(20), DetailTypeId SMALLINT);
INSERT @T
SELECT 'cash', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '100001' UNION ALL
SELECT 'bank', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '110001' UNION ALL
SELECT 'due',  Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '150001' UNION ALL
SELECT 'person', Id FROM fin.DetailTypes WHERE OwnerModule = 'persons' AND IsActive = 1;

-- بدون تفصیلی: دارایی ثابت، استهلاک انباشته، موجودی‌ها
DELETE l FROM fin.AccountDetailLevels l JOIN fin.Accounts a ON a.Id = l.AccountId
WHERE a.[Level] = 3 AND a.Code IN ('2401', '2402', '2403', '2404', '2405', '2406', '2451', '2701', '1901',
                                   '2202', '2203', '2204', '2205', '2206', '6501',
                                   '1601', '1602', '1603', '1604', '1605', '1606', '1609');

-- حذف سطح ۲: اشخاص از ضمانت‌نامه‌ها، قرارداد تسهیلات از هزینه مالی
DELETE l FROM fin.AccountDetailLevels l JOIN fin.Accounts a ON a.Id = l.AccountId
WHERE a.[Level] = 3 AND l.LevelNo = 2 AND a.Code IN ('1401', '9152', '8990');

-- قاعده تازه برای چند معین
DECLARE @R TABLE (Code VARCHAR(20), LevelNo TINYINT, TypeKey VARCHAR(20), RoleCode VARCHAR(30));
INSERT @R (Code, LevelNo, TypeKey, RoleCode) VALUES
    ('1201', 1, 'cash', NULL),          -- اسناد دریافتنی ← صندوق
    ('2001', 1, 'cash', NULL),          -- اسناد دریافتنی بلندمدت ← صندوق
    ('1202', 1, 'bank', NULL),          -- اسناد در جریان وصول ← حساب بانکی
    ('3201', 1, 'due', NULL),           -- اسناد پرداختنی ← ماه سررسید
    ('3301', 1, 'due', NULL),
    ('4001', 1, 'due', NULL),
    ('1607', 1, 'person', 'contractor'); -- موجودی نزد سازندگان ← پیمانکار

DELETE l FROM fin.AccountDetailLevels l JOIN fin.Accounts a ON a.Id = l.AccountId
WHERE a.[Level] = 3 AND a.Code IN (SELECT Code FROM @R);

INSERT fin.AccountDetailLevels (AccountId, LevelNo, IsRequired)
SELECT DISTINCT a.Id, r.LevelNo, 1
FROM @R r JOIN fin.Accounts a ON a.Code = r.Code AND a.[Level] = 3;

INSERT fin.AccountDetailLevelTypes (AccountId, LevelNo, DetailTypeId)
SELECT DISTINCT a.Id, r.LevelNo, t.DetailTypeId
FROM @R r JOIN fin.Accounts a ON a.Code = r.Code AND a.[Level] = 3
JOIN @T t ON t.TypeKey = r.TypeKey;

INSERT fin.AccountDetailLevelRoles (AccountId, LevelNo, RoleId)
SELECT a.Id, r.LevelNo, rt.Id
FROM @R r JOIN fin.Accounts a ON a.Code = r.Code AND a.[Level] = 3
JOIN base.PersonRoleTypes rt ON rt.Code = r.RoleCode;

-- همه سطح‌ها اجباری
UPDATE fin.AccountDetailLevels SET IsRequired = 1 WHERE IsRequired = 0;

IF EXISTS (SELECT 1 FROM fin.AccountDetailLevels l
           WHERE NOT EXISTS (SELECT 1 FROM fin.AccountDetailLevelTypes x WHERE x.AccountId = l.AccountId AND x.LevelNo = l.LevelNo))
    THROW 50091, N'قاعده تفصیلی بدون گروه مجاز ساخته شد (گروه تفصیلی پیدا نشد).', 1;

-- گروه‌هایی که دیگر در هیچ قاعده‌ای نیستند
UPDATE t SET IsActive = 0
FROM fin.DetailTypes t
WHERE t.OwnerModule IN ('assets', 'inventory')
  AND NOT EXISTS (SELECT 1 FROM fin.AccountDetailLevelTypes x WHERE x.DetailTypeId = t.Id);
GO

/* ---------------------------------------------------------------------
   ۳) کدهای جدید به ترتیب استاندارد
   --------------------------------------------------------------------- */
DECLARE @M TABLE (OldCode VARCHAR(20) PRIMARY KEY, NewCode VARCHAR(20) NOT NULL, Title NVARCHAR(200) NULL);
INSERT @M (OldCode, NewCode, Title) VALUES
    -- گروه‌ها
    ('2', '1', N'دارایی‌های غیرجاری'), ('1', '2', N'دارایی‌های جاری'), ('5', '3', N'حقوق مالکانه'),
    ('4', '4', N'بدهی‌های غیرجاری'),   ('3', '5', N'بدهی‌های جاری'),
    -- دارایی‌های غیرجاری
    ('24', '10', NULL), ('22', '11', NULL), ('27', '12', NULL), ('23', '13', NULL), ('26', '14', NULL),
    ('21', '15', NULL), ('20', '16', NULL), ('25', '17', NULL), ('28', '18', NULL),
    -- دارایی‌های جاری
    ('18', '20', NULL), ('17', '21', NULL), ('16', '22', NULL), ('15', '23', NULL), ('12', '24', NULL),
    ('13', '25', NULL), ('14', '26', NULL), ('11', '27', NULL), ('10', '28', NULL), ('19', '29', NULL),
    -- حقوق مالکانه
    ('50', '30', NULL), ('59', '31', NULL), ('51', '32', NULL), ('52', '33', NULL), ('53', '34', NULL),
    ('54', '35', NULL), ('55', '36', NULL), ('56', '37', NULL), ('57', '38', NULL), ('58', '39', NULL),
    -- بدهی‌های جاری
    ('32', '50', NULL), ('33', '51', NULL), ('34', '52', NULL), ('35', '53', NULL), ('36', '54', NULL),
    ('37', '55', NULL), ('39', '56', NULL), ('38', '57', NULL);

-- کد جدید هر حساب: از جدول بالا، وگرنه کد جدید والد + همان رقم‌های آخر
SELECT g.Id, g.Code AS OldCode, ISNULL(mg.NewCode, g.Code) AS NewCode, mg.Title
INTO #New
FROM fin.Accounts g LEFT JOIN @M mg ON mg.OldCode = g.Code
WHERE g.[Level] = 1;

INSERT #New (Id, OldCode, NewCode, Title)
SELECT k.Id, k.Code, ISNULL(mk.NewCode, p.NewCode + SUBSTRING(k.Code, LEN(g.Code) + 1, 20)), NULL
FROM fin.Accounts k
JOIN fin.Accounts g ON g.Id = k.ParentId
JOIN #New p ON p.Id = g.Id
LEFT JOIN @M mk ON mk.OldCode = k.Code
WHERE k.[Level] = 2;

INSERT #New (Id, OldCode, NewCode, Title)
SELECT m.Id, m.Code, p.NewCode + SUBSTRING(m.Code, LEN(k.Code) + 1, 20), NULL
FROM fin.Accounts m
JOIN fin.Accounts k ON k.Id = m.ParentId
JOIN #New p ON p.Id = k.Id
WHERE m.[Level] = 3;

IF (SELECT COUNT(*) FROM #New) <> (SELECT COUNT(*) FROM fin.Accounts)
    THROW 50092, N'کد جدید همه حساب‌ها ساخته نشد.', 1;
IF EXISTS (SELECT NewCode FROM #New GROUP BY NewCode HAVING COUNT(*) > 1)
    THROW 50093, N'کد جدید یک حساب اختصاصی با حساب دیگری یکی شد؛ کد آن حساب را قبل از اجرای اسکریپت ۰۹ عوض کنید.', 1;

UPDATE a SET Code = n.NewCode, Title = ISNULL(n.Title, a.Title)
FROM fin.Accounts a JOIN #New n ON n.Id = a.Id;

DROP TABLE #New;

IF (SELECT Code FROM fin.Accounts WHERE [Level] = 3 AND Title = N'وجوه نزد صندوق' AND IsStandard = 1) <> '2801'
    THROW 50094, N'کدگذاری دوباره درست انجام نشد.', 1;

PRINT N'کدینگ به ترتیب استاندارد مرتب شد و قواعد تفصیلی اصلاح شد.';
GO
