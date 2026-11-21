/* =====================================================================
   26 - نوع کالا (پیش‌نیاز تولید؛ تصمیم مالک ۱۴۰۵/۰۸/۳۰، docs/production-design.md بند ۰)

   - inv.ItemCategories: شش نوع ثابت (مواد، نیم‌ساخته، محصول، ضایعات، کالای بازرگانی، اقلام مصرفی) با معین موجودی و قاعده‌های مجاز.
   - inv.Items.CategoryId: برای کالا اجباری، برای خدمت خالی. معین موجودی سندها از نوع کالا می‌آید (نه گروه).
   - تریگر ردیف سند انبار: رسید خرید فقط نوع «قابل خرید»، حواله فروش فقط «قابل فروش»، حواله مصرف فقط «قابل مصرف».
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE TABLE inv.ItemCategories (
    Id                  TINYINT       NOT NULL CONSTRAINT PK_ItemCategories PRIMARY KEY,
    Title               NVARCHAR(50)  NOT NULL CONSTRAINT UQ_ItemCategories_Title UNIQUE,
    InventoryAccountId  INT           NULL CONSTRAINT FK_ItemCategories_Account REFERENCES fin.Accounts (Id),
    CanPurchase         BIT           NOT NULL,
    CanSell             BIT           NOT NULL,
    CanConsume          BIT           NOT NULL,   -- حواله مصرف غیرتولیدی
    CanProductionInput  BIT           NOT NULL,   -- مصرف در تولید
    CanProductionOutput BIT           NOT NULL,   -- محصول گزارش تولید
    IsScrap             BIT           NOT NULL,   -- ردیف ضایعات گزارش تولید
    SortOrder           TINYINT       NOT NULL
);
GO

INSERT inv.ItemCategories (Id, Title, InventoryAccountId, CanPurchase, CanSell, CanConsume, CanProductionInput, CanProductionOutput, IsScrap, SortOrder)
SELECT x.Id, x.Title, a.Id, x.P, x.S, x.C, x.I, x.O, x.Scrap, x.Id
FROM (VALUES
    (1, N'مواد اولیه و کمکی', '2201', 1, 1, 1, 1, 0, 0),
    (2, N'نیم‌ساخته',         '2202', 0, 1, 0, 1, 1, 0),
    (3, N'محصول',             '2203', 0, 1, 0, 0, 1, 0),
    (4, N'ضایعات',            '2205', 0, 1, 1, 0, 0, 1),
    (5, N'کالای بازرگانی',     '2206', 1, 1, 1, 0, 0, 0),
    (6, N'اقلام مصرفی',        '2209', 1, 0, 1, 0, 0, 0)
) x (Id, Title, Account, P, S, C, I, O, Scrap)
LEFT JOIN fin.Accounts a ON a.Code = x.Account AND a.[Level] = 3;
GO

ALTER TABLE inv.Items ADD CategoryId TINYINT NULL CONSTRAINT FK_Items_Category REFERENCES inv.ItemCategories (Id);
GO

-- کالاهای موجود: از معین موجودی گروهشان
UPDATE i SET CategoryId = CASE a.Code WHEN '2201' THEN 1 WHEN '2203' THEN 3 WHEN '2205' THEN 4 WHEN '2209' THEN 6 ELSE 5 END
FROM inv.Items i
JOIN inv.ItemGroups g ON g.Id = i.GroupId
LEFT JOIN fin.Accounts a ON a.Id = g.PurchaseAccountId
WHERE i.Kind = 1;
GO

ALTER TABLE inv.Items ADD CONSTRAINT CK_Items_Category CHECK ((Kind = 1 AND CategoryId IS NOT NULL) OR (Kind = 2 AND CategoryId IS NULL));
GO

-- نوع سند انبار: 2 رسید خرید، 11 حواله فروش، 13 حواله مصرف
CREATE TRIGGER inv.trg_StockDocLines_Category ON inv.StockDocLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @msg NVARCHAR(300);
    SELECT TOP (1) @msg = CONCAT(N'کالای «', i.Title, N'» از نوع «', c.Title, N'» است و ',
        CASE d.DocType WHEN 2 THEN N'خریدنی نیست (فقط تولید می‌شود).' WHEN 11 THEN N'فروختنی نیست.' ELSE N'در حواله مصرف نمی‌آید.' END)
    FROM inserted l
    JOIN inv.StockDocs d ON d.Id = l.DocId
    JOIN inv.Items i ON i.Id = l.ItemId
    JOIN inv.ItemCategories c ON c.Id = i.CategoryId
    WHERE (d.DocType = 2 AND c.CanPurchase = 0) OR (d.DocType = 11 AND c.CanSell = 0) OR (d.DocType = 13 AND c.CanConsume = 0);
    IF @msg IS NOT NULL THROW 50320, @msg, 1;
END;
GO

PRINT N'نوع کالا آماده شد.';
GO
