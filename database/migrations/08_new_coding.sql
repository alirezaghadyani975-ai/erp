/* =====================================================================
   08 - کدینگ استاندارد جدید، بازه‌های تفصیلی ۶ رقمی، گروه «هزینه کارکنان»

   - کدینگ بر پایه کدینگ عملیاتی سپیدار (پاک‌سازی‌شده): گروه‌ها به ترتیب صورت‌های مالی
     (۱ دارایی جاری، ۲ غیرجاری، ۳ بدهی جاری، ۴ غیرجاری، ۵ حقوق مالکانه، ۶ درآمد، ۷ بهای تمام‌شده،
     ۸ هزینه‌ها، ۹ پایانی و انتظامی)؛ کد معین‌ها با فاصله تا شرکت بتواند معین اختصاصی بین‌شان بگذارد.
   - هزینه‌ها: کل = ماهیت (80 مواد مصرفی، 81 حقوق و دستمزد، 82 هزینه کارکنان، 83 استهلاک،
     84 سایر عملیاتی، 89 مالی)، معین = وظیفه (..10 تولیدی، ..40 خدماتی، ..60 فروش و توزیع، ..80 اداری).
   - گروه هزینه پنجم: هزینه کارکنان (CostNature = 5).
   - «نوع هزینه» به‌عنوان سطح میانی حذف شد؛ عامل هزینه مستقیم گروه (ماهیت) دارد.
   - گروه‌های تفصیلی پیش‌فرض ۶ رقمی شدند و تفصیلی‌های موجود دوباره شماره‌گذاری شدند؛ تنخواه‌گردان و انبار اضافه شد.
   - قاعده تفصیلی همه معین‌ها از نو.

   فقط وقتی اجرا می‌شود که هنوز هیچ سندی ثبت نشده باشد (کدینگ را نمی‌شود زیر پای سندها عوض کرد).
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF EXISTS (SELECT 1 FROM fin.VoucherLines) OR EXISTS (SELECT 1 FROM fin.Vouchers)
    THROW 50080, N'در این شرکت سند ثبت شده است؛ کدینگ استاندارد جدید (اسکریپت ۰۸) فقط روی شرکت بدون سند اجرا می‌شود.', 1;
IF EXISTS (SELECT 1 FROM fin.PostingTemplateLines)
    THROW 50081, N'الگوی صدور سند تعریف شده است؛ کدینگ استاندارد جدید اجرا نشد.', 1;
GO

/* ---------------------------------------------------------------------
   ۱) ماهیت پنجم و پاک کردن کدینگ قبلی
   --------------------------------------------------------------------- */
ALTER TABLE fin.Accounts DROP CONSTRAINT CK_Accounts_CostNature;
ALTER TABLE fin.Accounts ADD CONSTRAINT CK_Accounts_CostNature CHECK (CostNature BETWEEN 1 AND 5);
ALTER TABLE fin.DetailAccounts DROP CONSTRAINT CK_DetailAccounts_CostNature;
ALTER TABLE fin.DetailAccounts ADD CONSTRAINT CK_DetailAccounts_CostNature CHECK (CostNature BETWEEN 1 AND 5);
GO

DELETE FROM fin.AccountDetailLevels;            -- انواع و نقش‌های مجاز cascade
DELETE FROM fin.Accounts WHERE [Level] = 3;
DELETE FROM fin.Accounts WHERE [Level] = 2;
DELETE FROM fin.Accounts WHERE [Level] = 1;
GO

/* ---------------------------------------------------------------------
   ۲) کدینگ جدید
   --------------------------------------------------------------------- */
INSERT fin.Accounts ([Level], Code, Title, IsStandard) VALUES
    (1, '1', N'دارایی‌های جاری', 1),
    (1, '2', N'دارایی‌های غیرجاری', 1),
    (1, '3', N'بدهی‌های جاری', 1),
    (1, '4', N'بدهی‌های غیرجاری', 1),
    (1, '5', N'حقوق مالکانه', 1),
    (1, '6', N'درآمدها', 1),
    (1, '7', N'بهای تمام‌شده', 1),
    (1, '8', N'هزینه‌ها', 1),
    (1, '9', N'حساب‌های پایانی و انتظامی', 1);

INSERT fin.Accounts (ParentId, [Level], Code, Title, IsStandard, CostNature)
SELECT p.Id, 2, v.Code, v.Title, 1, v.Nature
FROM (VALUES
    ('10', '1', N'موجودی نقد', NULL),
    ('11', '1', N'سرمایه‌گذاری‌های کوتاه‌مدت', NULL),
    ('12', '1', N'حساب‌ها و اسناد دریافتنی تجاری', NULL),
    ('13', '1', N'سایر حساب‌ها و اسناد دریافتنی', NULL),
    ('14', '1', N'سپرده‌های دریافتنی', NULL),
    ('15', '1', N'پیمان‌های در جریان', NULL),
    ('16', '1', N'موجودی مواد و کالا', NULL),
    ('17', '1', N'سفارشات خارجی', NULL),
    ('18', '1', N'پیش‌پرداخت‌ها', NULL),
    ('19', '1', N'دارایی‌های نگهداری‌شده برای فروش', NULL),
    ('20', '2', N'دریافتنی‌های بلندمدت', NULL),
    ('21', '2', N'سرمایه‌گذاری‌های بلندمدت', NULL),
    ('22', '2', N'استهلاک انباشته دارایی‌های ثابت مشهود', NULL),
    ('23', '2', N'دارایی‌های نامشهود', NULL),
    ('24', '2', N'دارایی‌های ثابت مشهود', NULL),
    ('25', '2', N'پیمان‌های بلندمدت', NULL),
    ('26', '2', N'استهلاک انباشته دارایی‌های نامشهود', NULL),
    ('27', '2', N'سرمایه‌گذاری در املاک', NULL),
    ('28', '2', N'سایر دارایی‌ها', NULL),
    ('32', '3', N'حساب‌ها و اسناد پرداختنی تجاری', NULL),
    ('33', '3', N'سایر حساب‌ها و اسناد پرداختنی', NULL),
    ('34', '3', N'سپرده‌های پرداختنی', NULL),
    ('35', '3', N'مالیات پرداختنی', NULL),
    ('36', '3', N'سود سهام پرداختنی', NULL),
    ('37', '3', N'تسهیلات مالی', NULL),
    ('38', '3', N'پیش‌دریافت‌ها', NULL),
    ('39', '3', N'ذخایر', NULL),
    ('40', '4', N'پرداختنی‌های بلندمدت', NULL),
    ('41', '4', N'تسهیلات مالی بلندمدت', NULL),
    ('42', '4', N'ذخیره مزایای پایان خدمت کارکنان', NULL),
    ('50', '5', N'سرمایه', NULL),
    ('51', '5', N'صرف سهام', NULL),
    ('52', '5', N'اندوخته قانونی', NULL),
    ('53', '5', N'سایر اندوخته‌ها', NULL),
    ('54', '5', N'مازاد تجدید ارزیابی دارایی‌ها', NULL),
    ('55', '5', N'تفاوت تسعیر ارز عملیات خارجی', NULL),
    ('56', '5', N'سود (زیان) انباشته', NULL),
    ('57', '5', N'سود (زیان) جاری', NULL),
    ('58', '5', N'سهام خزانه', NULL),
    ('59', '5', N'افزایش سرمایه در جریان', NULL),
    ('60', '6', N'فروش', NULL),
    ('61', '6', N'درآمد حاصل از ارائه خدمات', NULL),
    ('62', '6', N'برگشت از فروش', NULL),
    ('63', '6', N'تخفیفات', NULL),
    ('64', '6', N'سایر درآمدها و هزینه‌های عملیاتی', NULL),
    ('65', '6', N'سایر درآمدها و هزینه‌های غیرعملیاتی', NULL),
    ('70', '7', N'بهای تمام‌شده کالای فروش‌رفته', NULL),
    ('71', '7', N'بهای تمام‌شده خدمات', NULL),
    ('76', '7', N'جذب هزینه‌ها', NULL),
    ('80', '8', N'هزینه مواد مصرفی', NULL),
    ('81', '8', N'هزینه حقوق و دستمزد', 1),
    ('82', '8', N'هزینه کارکنان', 5),
    ('83', '8', N'هزینه استهلاک', 2),
    ('84', '8', N'سایر هزینه‌های عملیاتی', 3),
    ('89', '8', N'هزینه‌های مالی (تأمین مالی)', 4),
    ('91', '9', N'حساب‌های انتظامی', NULL),
    ('92', '9', N'طرف حساب‌های انتظامی', NULL),
    ('98', '9', N'تراز افتتاحیه و اختتامیه', NULL)
) AS v (Code, ParentCode, Title, Nature)
JOIN fin.Accounts p ON p.Code = v.ParentCode AND p.[Level] = 1;

INSERT fin.Accounts (ParentId, [Level], Code, Title, Nature, AccountType, IsStandard, CostNature, CostFunction)
SELECT p.Id, 3, v.Code, v.Title, v.Nature, v.AccountType, 1, v.CostNature, v.CostFunction
FROM (VALUES
    ('1001', '10', N'وجوه نزد صندوق', 1, 1, NULL, NULL),
    ('1011', '10', N'موجودی نزد بانک‌ها', 1, 1, NULL, NULL),
    ('1012', '10', N'موجودی ارزی نزد بانک‌ها', 1, 1, NULL, NULL),
    ('1021', '10', N'تنخواه‌گردان‌ها', 1, 1, NULL, NULL),
    ('1031', '10', N'وجوه در راه', 1, 1, NULL, NULL),
    ('1101', '11', N'سهام شرکت‌های پذیرفته‌شده در بورس', 1, 1, NULL, NULL),
    ('1102', '11', N'اوراق بهادار سریع‌المعامله', 1, 1, NULL, NULL),
    ('1111', '11', N'سهام سایر شرکت‌ها', 1, 1, NULL, NULL),
    ('1112', '11', N'سایر اوراق بهادار', 1, 1, NULL, NULL),
    ('1121', '11', N'سپرده‌های سرمایه‌گذاری کوتاه‌مدت بانکی', 1, 1, NULL, NULL),
    ('1198', '11', N'ذخیره کاهش ارزش سرمایه‌گذاری‌های کوتاه‌مدت', 2, 1, NULL, NULL),
    ('1201', '12', N'اسناد دریافتنی', 1, 1, NULL, NULL),
    ('1202', '12', N'اسناد در جریان وصول', 1, 1, NULL, NULL),
    ('1211', '12', N'حساب‌های دریافتنی تجاری', 1, 1, NULL, NULL),
    ('1298', '12', N'ذخیره کاهش ارزش دریافتنی‌های تجاری', 2, 1, NULL, NULL),
    ('1301', '13', N'اسناد برگشتی', 1, 1, NULL, NULL),
    ('1302', '13', N'اسناد حقوقی‌شده', 1, 1, NULL, NULL),
    ('1311', '13', N'سایر حساب‌های دریافتنی', 1, 1, NULL, NULL),
    ('1321', '13', N'طلب از کارکنان', 1, 1, NULL, NULL),
    ('1322', '13', N'وام کارکنان', 1, 1, NULL, NULL),
    ('1323', '13', N'مساعده کارکنان', 1, 1, NULL, NULL),
    ('1341', '13', N'مالیات و عوارض ارزش افزوده دریافتنی', 1, 1, NULL, NULL),
    ('1351', '13', N'سازمان امور مالیاتی', 1, 1, NULL, NULL),
    ('1361', '13', N'سازمان تأمین اجتماعی', 1, 1, NULL, NULL),
    ('1371', '13', N'طلب از سهامداران', 1, 1, NULL, NULL),
    ('1398', '13', N'ذخیره کاهش ارزش سایر دریافتنی‌ها', 2, 1, NULL, NULL),
    ('1401', '14', N'سپرده ضمانت‌نامه‌های بانکی', 1, 1, NULL, NULL),
    ('1402', '14', N'سپرده حسن انجام کار', 1, 1, NULL, NULL),
    ('1403', '14', N'سپرده حسن انجام تعهدات', 1, 1, NULL, NULL),
    ('1404', '14', N'سپرده شرکت در مناقصه', 1, 1, NULL, NULL),
    ('1405', '14', N'سپرده شرکت در مزایده', 1, 1, NULL, NULL),
    ('1406', '14', N'سپرده شرکت در نمایشگاه', 1, 1, NULL, NULL),
    ('1411', '14', N'سپرده ودیعه اجاره', 1, 1, NULL, NULL),
    ('1461', '14', N'سپرده بیمه سازمان تأمین اجتماعی', 1, 1, NULL, NULL),
    ('1501', '15', N'پیمان‌های در جریان', 1, 1, NULL, NULL),
    ('1601', '16', N'مواد اولیه و کمکی', 1, 1, NULL, NULL),
    ('1602', '16', N'کالای در جریان ساخت', 1, 1, NULL, NULL),
    ('1603', '16', N'کالای ساخته‌شده', 1, 1, NULL, NULL),
    ('1604', '16', N'قطعات و لوازم یدکی', 1, 1, NULL, NULL),
    ('1605', '16', N'ضایعات', 1, 1, NULL, NULL),
    ('1606', '16', N'کالای بازرگانی', 1, 1, NULL, NULL),
    ('1607', '16', N'موجودی نزد سازندگان', 1, 1, NULL, NULL),
    ('1609', '16', N'سایر موجودی‌ها', 1, 1, NULL, NULL),
    ('1650', '16', N'کالای در راه', 1, 1, NULL, NULL),
    ('1680', '16', N'کالای امانی ما نزد دیگران', 1, 1, NULL, NULL),
    ('1698', '16', N'ذخیره کاهش ارزش موجودی‌ها', 2, 1, NULL, NULL),
    ('1701', '17', N'ثبت سفارش و مجوزها', 1, 1, NULL, NULL),
    ('1702', '17', N'بیمه‌نامه‌های سفارش', 1, 1, NULL, NULL),
    ('1703', '17', N'خرید ارز', 1, 1, NULL, NULL),
    ('1704', '17', N'هزینه‌های حمل سفارش', 1, 1, NULL, NULL),
    ('1705', '17', N'هزینه‌های گمرکی', 1, 1, NULL, NULL),
    ('1706', '17', N'هزینه‌های ترخیص', 1, 1, NULL, NULL),
    ('1707', '17', N'هزینه‌های ناظر و بازرسی', 1, 1, NULL, NULL),
    ('1708', '17', N'سایر هزینه‌های سفارش', 1, 1, NULL, NULL),
    ('1801', '18', N'پیش‌پرداخت خرید کالا و خدمات', 1, 1, NULL, NULL),
    ('1802', '18', N'پیش‌پرداخت خرید دارایی‌ها', 1, 1, NULL, NULL),
    ('1803', '18', N'پیش‌پرداخت بیمه دارایی‌ها', 1, 1, NULL, NULL),
    ('1804', '18', N'پیش‌پرداخت اجاره', 1, 1, NULL, NULL),
    ('1805', '18', N'پیش‌پرداخت مالیات', 1, 1, NULL, NULL),
    ('1806', '18', N'پیش‌پرداخت سازمان تأمین اجتماعی', 1, 1, NULL, NULL),
    ('1809', '18', N'سایر پیش‌پرداخت‌ها', 1, 1, NULL, NULL),
    ('1901', '19', N'دارایی‌های غیرجاری نگهداری‌شده برای فروش', 1, 1, NULL, NULL),
    ('2001', '20', N'اسناد دریافتنی بلندمدت', 1, 1, NULL, NULL),
    ('2011', '20', N'حساب‌های دریافتنی تجاری بلندمدت', 1, 1, NULL, NULL),
    ('2012', '20', N'سایر حساب‌های دریافتنی بلندمدت', 1, 1, NULL, NULL),
    ('2101', '21', N'سهام شرکت‌های پذیرفته‌شده در بورس', 1, 1, NULL, NULL),
    ('2111', '21', N'سهام سایر شرکت‌ها', 1, 1, NULL, NULL),
    ('2112', '21', N'سایر اوراق بهادار', 1, 1, NULL, NULL),
    ('2121', '21', N'سپرده‌های سرمایه‌گذاری بلندمدت بانکی', 1, 1, NULL, NULL),
    ('2198', '21', N'ذخیره کاهش ارزش سرمایه‌گذاری‌های بلندمدت', 2, 1, NULL, NULL),
    ('2202', '22', N'استهلاک انباشته ساختمان و تأسیسات', 2, 1, NULL, NULL),
    ('2203', '22', N'استهلاک انباشته ماشین‌آلات', 2, 1, NULL, NULL),
    ('2204', '22', N'استهلاک انباشته وسایط نقلیه', 2, 1, NULL, NULL),
    ('2205', '22', N'استهلاک انباشته ابزار و تجهیزات', 2, 1, NULL, NULL),
    ('2206', '22', N'استهلاک انباشته اثاثه و منصوبات', 2, 1, NULL, NULL),
    ('2301', '23', N'حق امتیاز خدمات عمومی', 1, 1, NULL, NULL),
    ('2302', '23', N'سرقفلی محل کسب', 1, 1, NULL, NULL),
    ('2303', '23', N'دانش فنی', 1, 1, NULL, NULL),
    ('2304', '23', N'نرم‌افزار رایانه‌ای', 1, 1, NULL, NULL),
    ('2305', '23', N'علامت و نام تجاری', 1, 1, NULL, NULL),
    ('2306', '23', N'فرمول‌ها، مدل‌ها و طرح‌ها', 1, 1, NULL, NULL),
    ('2401', '24', N'زمین', 1, 1, NULL, NULL),
    ('2402', '24', N'ساختمان و تأسیسات', 1, 1, NULL, NULL),
    ('2403', '24', N'ماشین‌آلات', 1, 1, NULL, NULL),
    ('2404', '24', N'وسایط نقلیه', 1, 1, NULL, NULL),
    ('2405', '24', N'ابزار و تجهیزات', 1, 1, NULL, NULL),
    ('2406', '24', N'اثاثه و منصوبات', 1, 1, NULL, NULL),
    ('2451', '24', N'دارایی‌های در جریان تکمیل', 1, 1, NULL, NULL),
    ('2461', '24', N'پیش‌پرداخت‌های سرمایه‌ای', 1, 1, NULL, NULL),
    ('2501', '25', N'پیمان‌های بلندمدت', 1, 1, NULL, NULL),
    ('2602', '26', N'استهلاک انباشته دانش فنی', 2, 1, NULL, NULL),
    ('2603', '26', N'استهلاک انباشته نرم‌افزار رایانه‌ای', 2, 1, NULL, NULL),
    ('2604', '26', N'استهلاک انباشته علامت و نام تجاری', 2, 1, NULL, NULL),
    ('2605', '26', N'استهلاک انباشته فرمول‌ها، مدل‌ها و طرح‌ها', 2, 1, NULL, NULL),
    ('2701', '27', N'سرمایه‌گذاری در املاک', 1, 1, NULL, NULL),
    ('2801', '28', N'سود و کارمزد تسهیلات سال‌های آتی', 1, 1, NULL, NULL),
    ('2802', '28', N'وجوه بانکی مسدودشده', 1, 1, NULL, NULL),
    ('2803', '28', N'سپرده نزد دادگستری', 1, 1, NULL, NULL),
    ('3201', '32', N'اسناد پرداختنی', 2, 1, NULL, NULL),
    ('3211', '32', N'حساب‌های پرداختنی تجاری', 2, 1, NULL, NULL),
    ('3249', '32', N'حساب معلق خرید', 2, 1, NULL, NULL),
    ('3301', '33', N'سایر اسناد پرداختنی', 2, 1, NULL, NULL),
    ('3311', '33', N'سایر حساب‌های پرداختنی', 2, 1, NULL, NULL),
    ('3321', '33', N'بدهی به کارکنان', 2, 1, NULL, NULL),
    ('3322', '33', N'سایر کسور کارکنان', 2, 1, NULL, NULL),
    ('3323', '33', N'رند حقوق', 2, 1, NULL, NULL),
    ('3324', '33', N'بیمه تکمیلی سهم کارکنان', 2, 1, NULL, NULL),
    ('3325', '33', N'حقوق پرداختنی', 2, 1, NULL, NULL),
    ('3326', '33', N'عیدی و پاداش پرداختنی', 2, 1, NULL, NULL),
    ('3341', '33', N'مالیات و عوارض ارزش افزوده پرداختنی', 2, 1, NULL, NULL),
    ('3351', '33', N'مالیات حقوق پرداختنی', 2, 1, NULL, NULL),
    ('3352', '33', N'مالیات تکلیفی پرداختنی', 2, 1, NULL, NULL),
    ('3361', '33', N'حق بیمه پرداختنی', 2, 1, NULL, NULL),
    ('3371', '33', N'بدهی به سهامداران', 2, 1, NULL, NULL),
    ('3401', '34', N'سپرده‌های دریافتی بابت ضمانت‌نامه', 2, 1, NULL, NULL),
    ('3402', '34', N'سپرده حسن انجام کار دریافتی', 2, 1, NULL, NULL),
    ('3403', '34', N'سپرده حسن انجام تعهدات دریافتی', 2, 1, NULL, NULL),
    ('3404', '34', N'سپرده شرکت در مناقصه دریافتی', 2, 1, NULL, NULL),
    ('3405', '34', N'سپرده شرکت در مزایده دریافتی', 2, 1, NULL, NULL),
    ('3461', '34', N'سپرده بیمه پیمانکاران', 2, 1, NULL, NULL),
    ('3501', '35', N'مالیات بر درآمد پرداختنی', 2, 1, NULL, NULL),
    ('3601', '36', N'سود سهام پرداختنی', 2, 1, NULL, NULL),
    ('3711', '37', N'تسهیلات دریافتی از بانک‌ها', 2, 1, NULL, NULL),
    ('3712', '37', N'قرض‌الحسنه دریافتی', 2, 1, NULL, NULL),
    ('3713', '37', N'تسهیلات دریافتی از اشخاص', 2, 1, NULL, NULL),
    ('3801', '38', N'پیش‌دریافت فروش', 2, 1, NULL, NULL),
    ('3809', '38', N'سایر پیش‌دریافت‌ها', 2, 1, NULL, NULL),
    ('3901', '39', N'ذخیره هزینه‌های تحقق‌یافته پرداخت‌نشده', 2, 1, NULL, NULL),
    ('3902', '39', N'ذخیره تضمین محصولات', 2, 1, NULL, NULL),
    ('3909', '39', N'سایر ذخایر', 2, 1, NULL, NULL),
    ('4001', '40', N'اسناد پرداختنی بلندمدت', 2, 1, NULL, NULL),
    ('4011', '40', N'حساب‌های پرداختنی تجاری بلندمدت', 2, 1, NULL, NULL),
    ('4012', '40', N'سایر حساب‌های پرداختنی بلندمدت', 2, 1, NULL, NULL),
    ('4111', '41', N'تسهیلات بلندمدت از بانک‌ها', 2, 1, NULL, NULL),
    ('4112', '41', N'تعهدات اجاره سرمایه‌ای', 2, 1, NULL, NULL),
    ('4201', '42', N'ذخیره بازخرید سنوات خدمت', 2, 1, NULL, NULL),
    ('4202', '42', N'ذخیره بازخرید مرخصی', 2, 1, NULL, NULL),
    ('5001', '50', N'سرمایه پرداخت‌شده', 2, 1, NULL, NULL),
    ('5002', '50', N'سرمایه تعهدشده پرداخت‌نشده', 1, 1, NULL, NULL),
    ('5101', '51', N'صرف سهام', 2, 1, NULL, NULL),
    ('5102', '51', N'صرف سهام خزانه', 2, 1, NULL, NULL),
    ('5201', '52', N'اندوخته قانونی', 2, 1, NULL, NULL),
    ('5301', '53', N'اندوخته عمومی', 2, 1, NULL, NULL),
    ('5302', '53', N'اندوخته طرح توسعه', 2, 1, NULL, NULL),
    ('5401', '54', N'مازاد تجدید ارزیابی دارایی‌ها', 2, 1, NULL, NULL),
    ('5501', '55', N'تفاوت تسعیر ارز عملیات خارجی', 3, 1, NULL, NULL),
    ('5601', '56', N'سود (زیان) انباشته', 3, 1, NULL, NULL),
    ('5602', '56', N'اصلاح اشتباهات', 3, 1, NULL, NULL),
    ('5603', '56', N'تغییر در رویه‌های حسابداری', 3, 1, NULL, NULL),
    ('5701', '57', N'سود (زیان) جاری', 3, 1, NULL, NULL),
    ('5801', '58', N'سهام خزانه', 1, 1, NULL, NULL),
    ('5901', '59', N'افزایش سرمایه در جریان', 2, 1, NULL, NULL),
    ('6001', '60', N'فروش محصول', 2, 2, NULL, NULL),
    ('6002', '60', N'فروش مواد اولیه و قطعات یدکی', 2, 2, NULL, NULL),
    ('6003', '60', N'فروش کالای بازرگانی', 2, 2, NULL, NULL),
    ('6004', '60', N'فروش صادراتی', 2, 2, NULL, NULL),
    ('6101', '61', N'درآمد ارائه خدمات', 2, 2, NULL, NULL),
    ('6102', '61', N'درآمد پیمانکاری', 2, 2, NULL, NULL),
    ('6201', '62', N'برگشت از فروش محصول', 1, 2, NULL, NULL),
    ('6202', '62', N'برگشت از فروش کالای بازرگانی', 1, 2, NULL, NULL),
    ('6301', '63', N'تخفیف فروش', 1, 2, NULL, NULL),
    ('6302', '63', N'تخفیف خدمات', 1, 2, NULL, NULL),
    ('6401', '64', N'فروش ضایعات', 2, 2, NULL, NULL),
    ('6402', '64', N'تسعیر دارایی‌ها و بدهی‌های ارزی عملیاتی', 3, 2, NULL, NULL),
    ('6403', '64', N'هزینه‌های جذب‌نشده در تولید', 1, 2, NULL, NULL),
    ('6404', '64', N'ضایعات غیرعادی تولید', 1, 2, NULL, NULL),
    ('6405', '64', N'درآمد اجاره', 2, 2, NULL, NULL),
    ('6406', '64', N'زیان کاهش ارزش موجودی‌ها', 1, 2, NULL, NULL),
    ('6407', '64', N'خالص کسری و اضافی انبار', 3, 2, NULL, NULL),
    ('6408', '64', N'هزینه کاهش ارزش دریافتنی‌ها', 1, 2, NULL, NULL),
    ('6501', '65', N'سود (زیان) فروش دارایی‌های ثابت مشهود', 3, 2, NULL, NULL),
    ('6502', '65', N'سود (زیان) فروش دارایی‌های نامشهود', 3, 2, NULL, NULL),
    ('6503', '65', N'سود فروش مواد اولیه و سایر موجودی‌ها', 3, 2, NULL, NULL),
    ('6504', '65', N'سود سایر اوراق بهادار', 2, 2, NULL, NULL),
    ('6505', '65', N'سود سپرده‌های بانکی', 2, 2, NULL, NULL),
    ('6506', '65', N'سود سهام', 2, 2, NULL, NULL),
    ('6507', '65', N'زیان کاهش ارزش سرمایه‌گذاری‌های بلندمدت', 1, 2, NULL, NULL),
    ('6508', '65', N'سود (زیان) فروش سرمایه‌گذاری‌ها', 3, 2, NULL, NULL),
    ('6509', '65', N'ارزیابی سرمایه‌گذاری‌های جاری سریع‌المعامله', 3, 2, NULL, NULL),
    ('6510', '65', N'تسعیر دارایی‌ها و بدهی‌های ارزی غیرعملیاتی', 3, 2, NULL, NULL),
    ('7001', '70', N'بهای تمام‌شده کالای فروش‌رفته', 1, 2, NULL, NULL),
    ('7101', '71', N'بهای تمام‌شده خدمات', 1, 2, NULL, NULL),
    ('7601', '76', N'جذب مواد', 2, 2, NULL, NULL),
    ('7602', '76', N'جذب دستمزد', 2, 2, NULL, NULL),
    ('7603', '76', N'جذب سربار', 2, 2, NULL, NULL),
    ('8001', '80', N'مواد مصرفی تولید', 1, 2, NULL, NULL),
    ('8002', '80', N'مواد مصرفی پیمانکاران', 1, 2, NULL, NULL),
    ('8110', '81', N'حقوق و دستمزد - مراکز تولیدی', 1, 2, 1, 1),
    ('8140', '81', N'حقوق و دستمزد - مراکز خدماتی (پشتیبانی تولید)', 1, 2, 1, 2),
    ('8160', '81', N'حقوق و دستمزد - مراکز فروش و توزیع', 1, 2, 1, 4),
    ('8180', '81', N'حقوق و دستمزد - مراکز اداری', 1, 2, 1, 3),
    ('8210', '82', N'هزینه کارکنان - مراکز تولیدی', 1, 2, 5, 1),
    ('8240', '82', N'هزینه کارکنان - مراکز خدماتی (پشتیبانی تولید)', 1, 2, 5, 2),
    ('8260', '82', N'هزینه کارکنان - مراکز فروش و توزیع', 1, 2, 5, 4),
    ('8280', '82', N'هزینه کارکنان - مراکز اداری', 1, 2, 5, 3),
    ('8310', '83', N'استهلاک - مراکز تولیدی', 1, 2, 2, 1),
    ('8340', '83', N'استهلاک - مراکز خدماتی (پشتیبانی تولید)', 1, 2, 2, 2),
    ('8360', '83', N'استهلاک - مراکز فروش و توزیع', 1, 2, 2, 4),
    ('8380', '83', N'استهلاک - مراکز اداری', 1, 2, 2, 3),
    ('8410', '84', N'سایر هزینه‌های عملیاتی - مراکز تولیدی', 1, 2, 3, 1),
    ('8440', '84', N'سایر هزینه‌های عملیاتی - مراکز خدماتی (پشتیبانی تولید)', 1, 2, 3, 2),
    ('8460', '84', N'سایر هزینه‌های عملیاتی - مراکز فروش و توزیع', 1, 2, 3, 4),
    ('8480', '84', N'سایر هزینه‌های عملیاتی - مراکز اداری', 1, 2, 3, 3),
    ('8990', '89', N'هزینه‌های تأمین مالی', 1, 2, 4, NULL),
    ('9101', '91', N'اسناد تضمینی دریافتی از اشخاص', 1, 3, NULL, NULL),
    ('9102', '91', N'اسناد تضمینی دریافتی از کارکنان', 1, 3, NULL, NULL),
    ('9151', '91', N'اسناد تضمینی پرداختی به اشخاص', 1, 3, NULL, NULL),
    ('9152', '91', N'ضمانت‌نامه‌ها و اسناد پرداختی به بانک‌ها', 1, 3, NULL, NULL),
    ('9201', '92', N'طرف اسناد تضمینی دریافتی', 2, 3, NULL, NULL),
    ('9251', '92', N'طرف اسناد تضمینی پرداختی', 2, 3, NULL, NULL),
    ('9801', '98', N'تراز افتتاحیه و اختتامیه', 3, 1, NULL, NULL)
) AS v (Code, ParentCode, Title, Nature, AccountType, CostNature, CostFunction)
JOIN fin.Accounts p ON p.Code = v.ParentCode AND p.[Level] = 2;

IF (SELECT COUNT(*) FROM fin.Accounts) <> 9 + 58 + 211
    THROW 50082, N'تعداد حساب‌های کدینگ جدید درست بارگذاری نشد.', 1;
GO

/* ---------------------------------------------------------------------
   ۳) گروه‌های تفصیلی ۶ رقمی و شماره‌گذاری دوباره تفصیلی‌ها
   --------------------------------------------------------------------- */
DECLARE @G TABLE (TypeKey VARCHAR(20) PRIMARY KEY, Owner VARCHAR(30), PersonKind TINYINT NULL, OldFrom VARCHAR(10) NULL,
                  Title NVARCHAR(100), CodeFrom VARCHAR(10), CodeTo VARCHAR(10), Id SMALLINT NULL);
INSERT @G (TypeKey, Owner, PersonKind, OldFrom, Title, CodeFrom, CodeTo) VALUES
    ('cash',      'treasury',     NULL, '00101', N'صندوق‌ها',             '100001', '100999'),
    ('imprest',   'treasury',     NULL, '101001', N'تنخواه‌گردان‌ها',       '101001', '101999'),
    ('bank',      'treasury',     NULL, '00111', N'حساب‌های بانکی',        '110001', '119999'),
    ('lc',        'treasury',     NULL, '00801', N'اعتبارات اسنادی',       '120001', '129999'),
    ('loan',      'treasury',     NULL, '07001', N'قراردادهای تسهیلات',    '130001', '139999'),
    ('warehouse', 'inventory',    NULL, '140001', N'انبارها',               '140001', '140999'),
    ('asset',     'assets',       NULL, '00701', N'دارایی‌های ثابت',       '200001', '299999'),
    ('employee',  'payroll',      1,    NULL,    N'کارکنان',               '300001', '349999'),
    ('center',    'cost_centers', NULL, NULL,    N'مراکز هزینه',           '400001', '409999'),
    ('factor',    'cost_factors', NULL, NULL,    N'عوامل هزینه',           '410001', '419999'),
    ('project',   'manual',       NULL, '07501', N'پروژه‌ها و پیمان‌ها',    '500001', '599999'),
    ('natural',   'persons',      1,    NULL,    N'اشخاص حقیقی',           '600001', '799999'),
    ('legal',     'persons',      2,    NULL,    N'اشخاص حقوقی',           '800001', '999999');

-- گروه موجود: با مالک و بازه قبلی (یا برای اشخاص، کارکنان، عوامل و مراکز: گروه فعال همان مالک)
UPDATE g SET Id = t.Id
FROM @G g
CROSS APPLY (SELECT TOP (1) x.Id FROM fin.DetailTypes x
             WHERE x.OwnerModule = g.Owner
               AND (g.OldFrom IS NULL OR x.CodeFrom = g.OldFrom)
               AND (g.Owner <> 'persons' OR x.PersonKind = g.PersonKind)
             ORDER BY x.IsActive DESC, x.Id) t;

DISABLE TRIGGER fin.trg_DetailTypes_Overlap ON fin.DetailTypes;
DISABLE TRIGGER fin.trg_DetailAccounts_Range ON fin.DetailAccounts;

UPDATE t SET Title = g.Title, CodeFrom = g.CodeFrom, CodeTo = g.CodeTo
FROM fin.DetailTypes t JOIN @G g ON g.Id = t.Id;

INSERT fin.DetailTypes (Title, OwnerModule, PersonKind, CodeFrom, CodeTo)
SELECT Title, Owner, CASE WHEN Owner IN ('persons', 'payroll') THEN PersonKind END, CodeFrom, CodeTo
FROM @G WHERE Id IS NULL;

-- کد جدید = اول بازه + ترتیب کد قبلی
UPDATE d SET Code = n.NewCode
FROM fin.DetailAccounts d
JOIN (SELECT d2.Id,
             RIGHT(REPLICATE('0', LEN(t.CodeFrom))
                   + CAST(CAST(t.CodeFrom AS BIGINT) + ROW_NUMBER() OVER (PARTITION BY d2.DetailTypeId ORDER BY d2.Code) - 1 AS VARCHAR(20)),
                   LEN(t.CodeFrom)) AS NewCode
      FROM fin.DetailAccounts d2 JOIN fin.DetailTypes t ON t.Id = d2.DetailTypeId
      JOIN @G g ON g.Id = t.Id) n ON n.Id = d.Id;

ENABLE TRIGGER fin.trg_DetailTypes_Overlap ON fin.DetailTypes;
ENABLE TRIGGER fin.trg_DetailAccounts_Range ON fin.DetailAccounts;

-- همان کنترل‌های تریگر، یک بار برای همه
IF EXISTS (SELECT 1 FROM fin.DetailTypes a JOIN fin.DetailTypes b
             ON a.Id < b.Id AND LEN(a.CodeFrom) = LEN(b.CodeFrom) AND a.CodeFrom <= b.CodeTo AND b.CodeFrom <= a.CodeTo)
    THROW 50083, N'بازه گروه‌های تفصیلی جدید با یک گروه تعریف‌شده کاربر هم‌پوشانی دارد.', 1;
IF EXISTS (SELECT 1 FROM fin.DetailAccounts d JOIN fin.DetailTypes t ON t.Id = d.DetailTypeId
           WHERE LEN(d.Code) <> LEN(t.CodeFrom) OR d.Code < t.CodeFrom OR d.Code > t.CodeTo)
    THROW 50084, N'کد تفصیلی بعد از شماره‌گذاری دوباره خارج از بازه است.', 1;
GO

/* ---------------------------------------------------------------------
   ۴) عامل هزینه مستقیم با گروه (ماهیت)؛ حذف «نوع هزینه»
   --------------------------------------------------------------------- */
ALTER TABLE cost.CostFactors ADD Nature TINYINT NULL;
GO
UPDATE f SET Nature = t.Nature FROM cost.CostFactors f JOIN cost.ExpenseTypes t ON t.Id = f.ExpenseTypeId;

-- هزینه‌های رفاهی کارکنان به گروه جدید «هزینه کارکنان»
UPDATE cost.CostFactors SET Nature = 5
WHERE Title IN (N'غذای کارکنان', N'پوشاک کارکنان', N'بهداشت و درمان', N'کمک‌های غیرنقدی', N'آموزش کارکنان', N'ایاب‌وذهاب');
ALTER TABLE cost.CostFactors ALTER COLUMN Nature TINYINT NOT NULL;
ALTER TABLE cost.CostFactors ADD CONSTRAINT CK_CostFactors_Nature CHECK (Nature BETWEEN 1 AND 5);
DROP INDEX IX_CostFactors_Type ON cost.CostFactors;
ALTER TABLE cost.CostFactors DROP CONSTRAINT FK_CostFactors_Type;
ALTER TABLE cost.CostFactors DROP COLUMN ExpenseTypeId;
DROP TABLE cost.ExpenseTypes;
CREATE INDEX IX_CostFactors_Nature ON cost.CostFactors (Nature);
GO

INSERT cost.CostFactors (Title, Behavior, Nature) VALUES
    (N'بیمه تکمیلی سهم کارفرما', 1, 5),
    (N'هزینه‌های ورزشی و رفاهی', 1, 5),
    (N'هدایا و مناسبت‌ها', 1, 5);
GO

-- تفصیلی عوامل جدید و هم‌سازی ماهیت تفصیلی عوامل
DECLARE @FactorType SMALLINT = (SELECT TOP (1) Id FROM fin.DetailTypes WHERE OwnerModule = 'cost_factors' ORDER BY IsActive DESC, Id);
DECLARE @Last INT = (SELECT ISNULL(MAX(CAST(Code AS INT)), 410000) FROM fin.DetailAccounts WHERE DetailTypeId = @FactorType);
INSERT fin.DetailAccounts (DetailTypeId, Code, Title, SourceModule, SourceEntityId, CostNature)
SELECT @FactorType, CAST(@Last + ROW_NUMBER() OVER (ORDER BY f.Id) AS VARCHAR(10)), f.Title, 'costing', f.Id, f.Nature
FROM cost.CostFactors f
WHERE NOT EXISTS (SELECT 1 FROM fin.DetailAccounts d WHERE d.DetailTypeId = @FactorType AND d.SourceEntityId = f.Id);

UPDATE d SET CostNature = f.Nature
FROM fin.DetailAccounts d JOIN cost.CostFactors f ON f.Id = d.SourceEntityId
WHERE d.DetailTypeId = @FactorType AND d.SourceModule = 'costing';
GO

/* ---------------------------------------------------------------------
   ۵) قاعده تفصیلی معین‌ها
      کلید نوع: cash، imprest، bank، lc، loan، warehouse، asset، employee، center، factor، project،
      natural، legal، person (هر دو نوع شخص)
   --------------------------------------------------------------------- */
DECLARE @T TABLE (TypeKey VARCHAR(20), DetailTypeId SMALLINT);
INSERT @T
SELECT 'cash', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '100001' UNION ALL
SELECT 'imprest', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '101001' UNION ALL
SELECT 'bank', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '110001' UNION ALL
SELECT 'lc', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '120001' UNION ALL
SELECT 'loan', Id FROM fin.DetailTypes WHERE OwnerModule = 'treasury' AND CodeFrom = '130001' UNION ALL
SELECT 'warehouse', Id FROM fin.DetailTypes WHERE OwnerModule = 'inventory' AND CodeFrom = '140001' UNION ALL
SELECT 'asset', Id FROM fin.DetailTypes WHERE OwnerModule = 'assets' AND CodeFrom = '200001' UNION ALL
SELECT 'employee', Id FROM fin.DetailTypes WHERE OwnerModule = 'payroll' AND CodeFrom = '300001' UNION ALL
SELECT 'center', Id FROM fin.DetailTypes WHERE OwnerModule = 'cost_centers' AND CodeFrom = '400001' UNION ALL
SELECT 'factor', Id FROM fin.DetailTypes WHERE OwnerModule = 'cost_factors' AND CodeFrom = '410001' UNION ALL
SELECT 'project', Id FROM fin.DetailTypes WHERE OwnerModule = 'manual' AND CodeFrom = '500001' UNION ALL
SELECT 'natural', Id FROM fin.DetailTypes WHERE OwnerModule = 'persons' AND CodeFrom = '600001' UNION ALL
SELECT 'legal', Id FROM fin.DetailTypes WHERE OwnerModule = 'persons' AND CodeFrom = '800001' UNION ALL
SELECT 'person', Id FROM fin.DetailTypes WHERE OwnerModule = 'persons' AND CodeFrom IN ('600001', '800001');

DECLARE @R TABLE (Code VARCHAR(20), LevelNo TINYINT, IsRequired BIT, TypeKey VARCHAR(20), RoleCode VARCHAR(30));
INSERT @R (Code, LevelNo, IsRequired, TypeKey, RoleCode) VALUES
    ('1001', 1, 1, 'cash', NULL),
    ('1011', 1, 1, 'bank', NULL),
    ('1012', 1, 1, 'bank', NULL),
    ('1021', 1, 1, 'imprest', NULL),
    ('1031', 1, 0, 'bank', NULL),
    ('1101', 1, 0, 'legal', NULL),
    ('1102', 1, 0, 'person', NULL),
    ('1111', 1, 1, 'legal', NULL),
    ('1112', 1, 0, 'person', NULL),
    ('1121', 1, 1, 'bank', NULL),
    ('1201', 1, 1, 'person', 'customer'),
    ('1202', 1, 1, 'person', 'customer'),
    ('1202', 2, 1, 'bank', NULL),
    ('1211', 1, 1, 'person', 'customer'),
    ('1298', 1, 0, 'person', NULL),
    ('1301', 1, 1, 'person', NULL),
    ('1302', 1, 1, 'person', NULL),
    ('1311', 1, 1, 'person', NULL),
    ('1321', 1, 1, 'employee', NULL),
    ('1322', 1, 1, 'employee', NULL),
    ('1323', 1, 1, 'employee', NULL),
    ('1351', 1, 0, 'legal', NULL),
    ('1361', 1, 0, 'legal', NULL),
    ('1371', 1, 1, 'person', 'shareholder'),
    ('1398', 1, 0, 'person', NULL),
    ('1401', 1, 1, 'bank', NULL),
    ('1401', 2, 0, 'person', NULL),
    ('1402', 1, 1, 'person', NULL),
    ('1403', 1, 1, 'person', NULL),
    ('1404', 1, 1, 'person', NULL),
    ('1405', 1, 1, 'person', NULL),
    ('1406', 1, 1, 'person', NULL),
    ('1411', 1, 1, 'person', NULL),
    ('1461', 1, 1, 'person', NULL),
    ('1501', 1, 1, 'project', NULL),
    ('1601', 1, 0, 'warehouse', NULL),
    ('1602', 1, 0, 'warehouse', NULL),
    ('1603', 1, 0, 'warehouse', NULL),
    ('1604', 1, 0, 'warehouse', NULL),
    ('1605', 1, 0, 'warehouse', NULL),
    ('1606', 1, 0, 'warehouse', NULL),
    ('1607', 1, 0, 'warehouse', NULL),
    ('1607', 2, 0, 'person', 'contractor'),
    ('1609', 1, 0, 'warehouse', NULL),
    ('1650', 1, 0, 'lc', NULL),
    ('1680', 1, 0, 'person', NULL),
    ('1701', 1, 1, 'lc', NULL),
    ('1702', 1, 1, 'lc', NULL),
    ('1703', 1, 1, 'lc', NULL),
    ('1704', 1, 1, 'lc', NULL),
    ('1705', 1, 1, 'lc', NULL),
    ('1706', 1, 1, 'lc', NULL),
    ('1707', 1, 1, 'lc', NULL),
    ('1708', 1, 1, 'lc', NULL),
    ('1801', 1, 1, 'person', 'supplier'),
    ('1802', 1, 1, 'person', NULL),
    ('1803', 1, 1, 'person', NULL),
    ('1804', 1, 1, 'person', NULL),
    ('1805', 1, 0, 'legal', NULL),
    ('1806', 1, 0, 'legal', NULL),
    ('1809', 1, 1, 'person', NULL),
    ('1901', 1, 0, 'asset', NULL),
    ('2001', 1, 1, 'person', NULL),
    ('2011', 1, 1, 'person', 'customer'),
    ('2012', 1, 1, 'person', NULL),
    ('2101', 1, 0, 'legal', NULL),
    ('2111', 1, 1, 'legal', 'affiliate'),
    ('2112', 1, 0, 'person', NULL),
    ('2121', 1, 1, 'bank', NULL),
    ('2202', 1, 0, 'asset', NULL),
    ('2203', 1, 0, 'asset', NULL),
    ('2204', 1, 0, 'asset', NULL),
    ('2205', 1, 0, 'asset', NULL),
    ('2206', 1, 0, 'asset', NULL),
    ('2401', 1, 0, 'asset', NULL),
    ('2402', 1, 0, 'asset', NULL),
    ('2403', 1, 0, 'asset', NULL),
    ('2404', 1, 0, 'asset', NULL),
    ('2405', 1, 0, 'asset', NULL),
    ('2406', 1, 0, 'asset', NULL),
    ('2451', 1, 0, 'asset', NULL),
    ('2461', 1, 1, 'person', NULL),
    ('2501', 1, 1, 'project', NULL),
    ('2701', 1, 0, 'asset', NULL),
    ('2801', 1, 1, 'loan', NULL),
    ('2802', 1, 1, 'bank', NULL),
    ('2803', 1, 0, 'person', NULL),
    ('3201', 1, 1, 'person', 'supplier'),
    ('3201', 2, 1, 'bank', NULL),
    ('3211', 1, 1, 'person', 'supplier'),
    ('3249', 1, 0, 'person', 'supplier'),
    ('3301', 1, 1, 'person', NULL),
    ('3301', 2, 1, 'bank', NULL),
    ('3311', 1, 1, 'person', NULL),
    ('3321', 1, 1, 'employee', NULL),
    ('3322', 1, 1, 'employee', NULL),
    ('3324', 1, 1, 'employee', NULL),
    ('3325', 1, 0, 'employee', NULL),
    ('3326', 1, 0, 'employee', NULL),
    ('3352', 1, 1, 'person', NULL),
    ('3371', 1, 1, 'person', 'shareholder'),
    ('3401', 1, 1, 'person', NULL),
    ('3402', 1, 1, 'person', 'contractor'),
    ('3403', 1, 1, 'person', NULL),
    ('3404', 1, 1, 'person', NULL),
    ('3405', 1, 1, 'person', NULL),
    ('3461', 1, 1, 'person', 'contractor'),
    ('3601', 1, 0, 'person', 'shareholder'),
    ('3711', 1, 1, 'loan', NULL),
    ('3712', 1, 1, 'loan', NULL),
    ('3713', 1, 1, 'person', NULL),
    ('3801', 1, 1, 'person', 'customer'),
    ('3809', 1, 1, 'person', NULL),
    ('3901', 1, 0, 'person', NULL),
    ('4001', 1, 1, 'person', NULL),
    ('4001', 2, 1, 'bank', NULL),
    ('4011', 1, 1, 'person', 'supplier'),
    ('4012', 1, 1, 'person', NULL),
    ('4111', 1, 1, 'loan', NULL),
    ('4112', 1, 1, 'person', NULL),
    ('4201', 1, 0, 'employee', NULL),
    ('4202', 1, 0, 'employee', NULL),
    ('5001', 1, 0, 'person', 'shareholder'),
    ('5002', 1, 0, 'person', 'shareholder'),
    ('5901', 1, 0, 'person', 'shareholder'),
    ('6101', 1, 0, 'project', NULL),
    ('6102', 1, 0, 'project', NULL),
    ('6501', 1, 0, 'asset', NULL),
    ('6505', 1, 0, 'bank', NULL),
    ('6506', 1, 0, 'legal', NULL),
    ('7101', 1, 0, 'project', NULL),
    ('8110', 1, 1, 'factor', NULL),
    ('8110', 2, 1, 'center', NULL),
    ('8140', 1, 1, 'factor', NULL),
    ('8140', 2, 1, 'center', NULL),
    ('8160', 1, 1, 'factor', NULL),
    ('8160', 2, 1, 'center', NULL),
    ('8180', 1, 1, 'factor', NULL),
    ('8180', 2, 1, 'center', NULL),
    ('8210', 1, 1, 'factor', NULL),
    ('8210', 2, 1, 'center', NULL),
    ('8240', 1, 1, 'factor', NULL),
    ('8240', 2, 1, 'center', NULL),
    ('8260', 1, 1, 'factor', NULL),
    ('8260', 2, 1, 'center', NULL),
    ('8280', 1, 1, 'factor', NULL),
    ('8280', 2, 1, 'center', NULL),
    ('8310', 1, 1, 'factor', NULL),
    ('8310', 2, 1, 'center', NULL),
    ('8340', 1, 1, 'factor', NULL),
    ('8340', 2, 1, 'center', NULL),
    ('8360', 1, 1, 'factor', NULL),
    ('8360', 2, 1, 'center', NULL),
    ('8380', 1, 1, 'factor', NULL),
    ('8380', 2, 1, 'center', NULL),
    ('8410', 1, 1, 'factor', NULL),
    ('8410', 2, 1, 'center', NULL),
    ('8440', 1, 1, 'factor', NULL),
    ('8440', 2, 1, 'center', NULL),
    ('8460', 1, 1, 'factor', NULL),
    ('8460', 2, 1, 'center', NULL),
    ('8480', 1, 1, 'factor', NULL),
    ('8480', 2, 1, 'center', NULL),
    ('8990', 1, 1, 'factor', NULL),
    ('8990', 2, 0, 'loan', NULL),
    ('9101', 1, 1, 'person', NULL),
    ('9102', 1, 1, 'employee', NULL),
    ('9151', 1, 1, 'person', NULL),
    ('9152', 1, 1, 'bank', NULL),
    ('9152', 2, 0, 'person', NULL),
    ('9201', 1, 0, 'person', NULL),
    ('9251', 1, 0, 'person', NULL);

INSERT fin.AccountDetailLevels (AccountId, LevelNo, IsRequired)
SELECT a.Id, r.LevelNo, r.IsRequired
FROM @R r JOIN fin.Accounts a ON a.Code = r.Code AND a.[Level] = 3;

INSERT fin.AccountDetailLevelTypes (AccountId, LevelNo, DetailTypeId)
SELECT DISTINCT a.Id, r.LevelNo, t.DetailTypeId
FROM @R r JOIN fin.Accounts a ON a.Code = r.Code AND a.[Level] = 3
JOIN @T t ON t.TypeKey = r.TypeKey;

INSERT fin.AccountDetailLevelRoles (AccountId, LevelNo, RoleId)
SELECT a.Id, r.LevelNo, rt.Id
FROM @R r JOIN fin.Accounts a ON a.Code = r.Code AND a.[Level] = 3
JOIN base.PersonRoleTypes rt ON rt.Code = r.RoleCode
WHERE r.RoleCode IS NOT NULL;

IF EXISTS (SELECT 1 FROM fin.AccountDetailLevels l
           WHERE NOT EXISTS (SELECT 1 FROM fin.AccountDetailLevelTypes x WHERE x.AccountId = l.AccountId AND x.LevelNo = l.LevelNo))
    THROW 50085, N'قاعده تفصیلی بدون گروه مجاز ساخته شد (گروه تفصیلی پیدا نشد).', 1;

PRINT N'کدینگ استاندارد جدید، گروه‌های تفصیلی ۶ رقمی و قواعد تفصیلی بارگذاری شد.';
GO
