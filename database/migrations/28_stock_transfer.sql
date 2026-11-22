/* =====================================================================
   28 - انتقال بین انبارها

   - نوع سند تازه: 5 رسید انتقال، 15 حواله انتقال؛ هر انتقال یک جفت سند هم‌تاریخ است که فقط فرم انتقال می‌سازد:
     SourceModule = 'transfer' و SourceId = شناسه حواله انتقال (در هر دو سند)؛ ردیف‌های هم‌شماره جفت یکدیگرند.
   - بهای رسید انتقال = بهای حواله انتقال همان ردیف (میانگین انبار مبدأ در محاسبه ماهانه)؛ سند حسابداری ندارد
     چون معین موجودی از نوع کالاست، نه انبار.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

ALTER TABLE inv.StockDocs DROP CONSTRAINT CK_StockDocs_Type;
ALTER TABLE inv.StockDocs DROP CONSTRAINT CK_StockDocs_Party;
ALTER TABLE inv.StockDocs DROP CONSTRAINT CK_StockDocs_Source;
GO
ALTER TABLE inv.StockDocs ADD
    CONSTRAINT CK_StockDocs_Type CHECK (DocType IN (1, 2, 3, 4, 5, 11, 12, 13, 14, 15)),
    CONSTRAINT CK_StockDocs_Party CHECK ((DocType IN (2, 3, 11, 12) AND PartyId IS NOT NULL) OR (DocType IN (1, 4, 5, 13, 14, 15) AND PartyId IS NULL)),
    CONSTRAINT CK_StockDocs_Source CHECK (DocType NOT IN (4, 5, 14, 15) OR SourceModule IS NOT NULL);
GO

-- جفت‌های انتقال: هر دو سند یک SourceId دارند (حواله)
CREATE INDEX IX_StockDocs_Source ON inv.StockDocs (SourceModule, SourceId) WHERE SourceModule IS NOT NULL;
GO

-- انتقال: انبار مبدأ و مقصد یکی نباشد و جفت هم‌تاریخ باشد
CREATE TRIGGER inv.trg_StockDocs_Transfer ON inv.StockDocs AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1
               FROM inserted r
               JOIN inv.StockDocs i ON i.Id = r.SourceId AND i.DocType = 15
               WHERE r.DocType = 5 AND r.SourceModule = 'transfer'
                 AND (i.WarehouseId = r.WarehouseId OR i.DocDate <> r.DocDate))
        THROW 50330, N'انبار مبدأ و مقصد انتقال نباید یکی باشد و رسید و حواله انتقال باید هم‌تاریخ باشند.', 1;
END;
GO
