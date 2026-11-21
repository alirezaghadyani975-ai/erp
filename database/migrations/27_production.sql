/* =====================================================================
   27 - تولید مرحله ۱: فرمول ساخت (BOM) و گزارش تولید (docs/production-design.md)

   انبار:
   - نوع سند تازه: 4 رسید تولید (محصول و ضایعات)، 14 حواله مصرف تولید؛ فقط از گزارش تولید (SourceModule).
   - inv.StockDocs.SourceModule/SourceId: سند ساخته‌شده از ماژول دیگر.
   تولید (schema prd):
   - Boms: محصول، مقدار پایه، انبار رسید و انبار پیش‌فرض برداشت، اعتبار، پیش‌فرض محصول.
   - BomMaterials (+ BomSubstitutes: گزینه‌های جایگزین هر ردیف، هر گزینه یک یا چند ماده با ضریب)،
     BomScraps (ردیف‌های ضایعات با نوع: 1 فقط مواد، 2 مواد + دستمزد و سربار)، BomOperations (زمان استاندارد مراکز به ثانیه).
   - ProductionReports: گزارش تولید یک‌مرحله‌ای از روی فرمول با مصرف واقعی، ضایعات واقعی، زمان واقعی و اپراتورها.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------------- انبار ---------------- */
ALTER TABLE inv.StockDocs DROP CONSTRAINT CK_StockDocs_Type;
ALTER TABLE inv.StockDocs DROP CONSTRAINT CK_StockDocs_Party;
GO
ALTER TABLE inv.StockDocs ADD
    SourceModule VARCHAR(20) NULL,
    SourceId     BIGINT      NULL;
GO
ALTER TABLE inv.StockDocs ADD
    CONSTRAINT CK_StockDocs_Type CHECK (DocType IN (1, 2, 3, 4, 11, 12, 13, 14)),
    CONSTRAINT CK_StockDocs_Party CHECK ((DocType IN (2, 3, 11, 12) AND PartyId IS NOT NULL) OR (DocType IN (1, 4, 13, 14) AND PartyId IS NULL)),
    CONSTRAINT CK_StockDocs_Source CHECK (DocType NOT IN (4, 14) OR SourceModule IS NOT NULL);
GO

-- قاعده نوع کالا برای سندهای تولید هم
ALTER TRIGGER inv.trg_StockDocLines_Category ON inv.StockDocLines AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @msg NVARCHAR(300);
    SELECT TOP (1) @msg = CONCAT(N'کالای «', i.Title, N'» از نوع «', c.Title, N'» است و ',
        CASE d.DocType WHEN 2 THEN N'خریدنی نیست (فقط تولید می‌شود).' WHEN 11 THEN N'فروختنی نیست.'
                       WHEN 14 THEN N'در تولید مصرف نمی‌شود.' WHEN 4 THEN N'محصول یا ضایعات تولید نیست.'
                       ELSE N'در حواله مصرف نمی‌آید.' END)
    FROM inserted l
    JOIN inv.StockDocs d ON d.Id = l.DocId
    JOIN inv.Items i ON i.Id = l.ItemId
    JOIN inv.ItemCategories c ON c.Id = i.CategoryId
    WHERE (d.DocType = 2 AND c.CanPurchase = 0) OR (d.DocType = 11 AND c.CanSell = 0) OR (d.DocType = 13 AND c.CanConsume = 0)
       OR (d.DocType = 14 AND c.CanProductionInput = 0) OR (d.DocType = 4 AND c.CanProductionOutput = 0 AND c.IsScrap = 0);
    IF @msg IS NOT NULL THROW 50320, @msg, 1;
END;
GO

/* ---------------- فرمول ساخت ---------------- */
CREATE SCHEMA prd;
GO

CREATE TABLE prd.Boms (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Boms PRIMARY KEY,
    Code              NVARCHAR(30)  NOT NULL CONSTRAINT UQ_Boms_Code UNIQUE,
    Title             NVARCHAR(150) NOT NULL,
    ProductId         INT           NOT NULL CONSTRAINT FK_Boms_Product REFERENCES inv.Items (Id),
    BaseQuantity      DECIMAL(18,6) NOT NULL,
    OutputWarehouseId INT           NOT NULL CONSTRAINT FK_Boms_OutWh REFERENCES inv.Warehouses (Id),
    IssueWarehouseId  INT           NOT NULL CONSTRAINT FK_Boms_InWh REFERENCES inv.Warehouses (Id),
    ValidFrom         DATE          NULL,
    ValidTo           DATE          NULL,
    IsDefault         BIT           NOT NULL CONSTRAINT DF_Boms_Default DEFAULT (0),
    IsActive          BIT           NOT NULL CONSTRAINT DF_Boms_Active DEFAULT (1),
    Notes             NVARCHAR(500) NULL,
    CreatedAt         DATETIME2(0)  NOT NULL CONSTRAINT DF_Boms_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Boms_Base CHECK (BaseQuantity > 0),
    CONSTRAINT CK_Boms_Valid CHECK (ValidTo IS NULL OR ValidFrom IS NULL OR ValidTo >= ValidFrom)
);
CREATE UNIQUE INDEX UX_Boms_Default ON prd.Boms (ProductId) WHERE IsDefault = 1;
GO

CREATE TABLE prd.BomMaterials (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BomMaterials PRIMARY KEY,
    BomId       INT           NOT NULL CONSTRAINT FK_BomMaterials_Bom REFERENCES prd.Boms (Id) ON DELETE CASCADE,
    RowNo       SMALLINT      NOT NULL,
    ItemId      INT           NOT NULL CONSTRAINT FK_BomMaterials_Item REFERENCES inv.Items (Id),
    Quantity    DECIMAL(18,6) NOT NULL,
    WarehouseId INT           NULL CONSTRAINT FK_BomMaterials_Wh REFERENCES inv.Warehouses (Id),   -- خالی = انبار سربرگ
    LossPercent DECIMAL(5,2)  NOT NULL CONSTRAINT DF_BomMaterials_Loss DEFAULT (0),
    Notes       NVARCHAR(200) NULL,
    CONSTRAINT UQ_BomMaterials_Row UNIQUE (BomId, RowNo),
    CONSTRAINT CK_BomMaterials_Qty CHECK (Quantity > 0 AND LossPercent >= 0 AND LossPercent < 100)
);
GO

CREATE TABLE prd.BomSubstitutes (
    Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BomSubstitutes PRIMARY KEY,
    MaterialId INT           NOT NULL CONSTRAINT FK_BomSubstitutes_Material REFERENCES prd.BomMaterials (Id) ON DELETE CASCADE,
    OptionNo   TINYINT       NOT NULL,                 -- گزینه جایگزین (اولویت)
    ItemId     INT           NOT NULL CONSTRAINT FK_BomSubstitutes_Item REFERENCES inv.Items (Id),
    Factor     DECIMAL(18,6) NOT NULL,                 -- مقدار این ماده به ازای ۱ واحد ماده اصلی
    CONSTRAINT UQ_BomSubstitutes UNIQUE (MaterialId, OptionNo, ItemId),
    CONSTRAINT CK_BomSubstitutes CHECK (Factor > 0 AND OptionNo BETWEEN 1 AND 20)
);
GO

CREATE TABLE prd.BomScraps (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BomScraps PRIMARY KEY,
    BomId     INT           NOT NULL CONSTRAINT FK_BomScraps_Bom REFERENCES prd.Boms (Id) ON DELETE CASCADE,
    RowNo     SMALLINT      NOT NULL,
    ItemId    INT           NOT NULL CONSTRAINT FK_BomScraps_Item REFERENCES inv.Items (Id),
    Quantity  DECIMAL(18,6) NOT NULL,
    ScrapType TINYINT       NOT NULL,                  -- 1 فقط مواد، 2 مواد + دستمزد و سربار
    CONSTRAINT UQ_BomScraps_Row UNIQUE (BomId, RowNo),
    CONSTRAINT CK_BomScraps CHECK (Quantity > 0 AND ScrapType IN (1, 2))
);
GO

CREATE TABLE prd.BomOperations (
    Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BomOperations PRIMARY KEY,
    BomId          INT           NOT NULL CONSTRAINT FK_BomOperations_Bom REFERENCES prd.Boms (Id) ON DELETE CASCADE,
    RowNo          SMALLINT      NOT NULL,
    CostCenterId   INT           NOT NULL CONSTRAINT FK_BomOperations_Center REFERENCES cost.CostCenters (Id),
    ManSeconds     DECIMAL(18,2) NOT NULL,             -- نفر-ثانیه برای مقدار پایه
    MachineSeconds DECIMAL(18,2) NOT NULL,             -- ماشین-ثانیه برای مقدار پایه
    Notes          NVARCHAR(200) NULL,
    CONSTRAINT UQ_BomOperations_Row UNIQUE (BomId, RowNo),
    CONSTRAINT CK_BomOperations CHECK (ManSeconds >= 0 AND MachineSeconds >= 0 AND ManSeconds + MachineSeconds > 0)
);
GO

/* ---------------- گزارش تولید ---------------- */
CREATE TABLE prd.ProductionReports (
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductionReports PRIMARY KEY,
    DocNo             INT           NOT NULL CONSTRAINT UQ_ProductionReports_No UNIQUE,
    ReportDate        DATE          NOT NULL,
    BomId             INT           NOT NULL CONSTRAINT FK_ProductionReports_Bom REFERENCES prd.Boms (Id),
    ProductId         INT           NOT NULL CONSTRAINT FK_ProductionReports_Product REFERENCES inv.Items (Id),
    Quantity          DECIMAL(18,6) NOT NULL,           -- مقدار سالم
    OutputWarehouseId INT           NOT NULL CONSTRAINT FK_ProductionReports_Wh REFERENCES inv.Warehouses (Id),
    [Description]     NVARCHAR(500) NULL,
    CreatedBy         INT           NOT NULL,
    CreatedAt         DATETIME2(0)  NOT NULL CONSTRAINT DF_ProductionReports_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt         DATETIME2(0)  NULL,
    CONSTRAINT CK_ProductionReports_Qty CHECK (Quantity > 0)
);
CREATE INDEX IX_ProductionReports_Date ON prd.ProductionReports (ReportDate);
GO

CREATE TABLE prd.ProductionMaterials (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductionMaterials PRIMARY KEY,
    ReportId      BIGINT        NOT NULL CONSTRAINT FK_ProductionMaterials_Report REFERENCES prd.ProductionReports (Id) ON DELETE CASCADE,
    RowNo         SMALLINT      NOT NULL,
    ItemId        INT           NOT NULL CONSTRAINT FK_ProductionMaterials_Item REFERENCES inv.Items (Id),
    WarehouseId   INT           NOT NULL CONSTRAINT FK_ProductionMaterials_Wh REFERENCES inv.Warehouses (Id),
    StandardQty   DECIMAL(18,6) NOT NULL,              -- مصرف استاندارد (صفر برای ماده خارج از فرمول)
    ActualQty     DECIMAL(18,6) NOT NULL,
    BomMaterialId INT           NULL CONSTRAINT FK_ProductionMaterials_BomMat REFERENCES prd.BomMaterials (Id),
    OptionNo      TINYINT       NULL,                  -- مصرف به‌جای ماده اصلی از گزینه جایگزین
    CONSTRAINT UQ_ProductionMaterials_Row UNIQUE (ReportId, RowNo),
    CONSTRAINT CK_ProductionMaterials CHECK (StandardQty >= 0 AND ActualQty >= 0)
);
GO

CREATE TABLE prd.ProductionScraps (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductionScraps PRIMARY KEY,
    ReportId    BIGINT        NOT NULL CONSTRAINT FK_ProductionScraps_Report REFERENCES prd.ProductionReports (Id) ON DELETE CASCADE,
    RowNo       SMALLINT      NOT NULL,
    ItemId      INT           NOT NULL CONSTRAINT FK_ProductionScraps_Item REFERENCES inv.Items (Id),
    StandardQty DECIMAL(18,6) NOT NULL,
    ActualQty   DECIMAL(18,6) NOT NULL,
    ScrapType   TINYINT       NOT NULL,
    CONSTRAINT UQ_ProductionScraps_Row UNIQUE (ReportId, RowNo),
    CONSTRAINT CK_ProductionScraps CHECK (StandardQty >= 0 AND ActualQty >= 0 AND ScrapType IN (1, 2))
);
GO

CREATE TABLE prd.ProductionOperations (
    ReportId             BIGINT        NOT NULL CONSTRAINT FK_ProductionOps_Report REFERENCES prd.ProductionReports (Id) ON DELETE CASCADE,
    CostCenterId         INT           NOT NULL CONSTRAINT FK_ProductionOps_Center REFERENCES cost.CostCenters (Id),
    StdManSeconds        DECIMAL(18,2) NOT NULL,
    StdMachineSeconds    DECIMAL(18,2) NOT NULL,
    ActualManSeconds     DECIMAL(18,2) NOT NULL,
    ActualMachineSeconds DECIMAL(18,2) NOT NULL,
    CONSTRAINT PK_ProductionOperations PRIMARY KEY (ReportId, CostCenterId),
    CONSTRAINT CK_ProductionOps CHECK (StdManSeconds >= 0 AND StdMachineSeconds >= 0 AND ActualManSeconds >= 0 AND ActualMachineSeconds >= 0)
);
GO

CREATE TABLE prd.ProductionOperators (
    ReportId     BIGINT       NOT NULL CONSTRAINT FK_ProductionOperators_Report REFERENCES prd.ProductionReports (Id) ON DELETE CASCADE,
    EmployeeId   INT          NOT NULL CONSTRAINT FK_ProductionOperators_Employee REFERENCES hr.Employees (Id),
    CostCenterId INT          NULL CONSTRAINT FK_ProductionOperators_Center REFERENCES cost.CostCenters (Id),
    [Hours]      DECIMAL(8,2) NOT NULL,
    CONSTRAINT PK_ProductionOperators PRIMARY KEY (ReportId, EmployeeId),
    CONSTRAINT CK_ProductionOperators CHECK ([Hours] > 0)
);
GO

CREATE TABLE prd.ProductionStockDocs (
    ReportId   BIGINT NOT NULL CONSTRAINT FK_ProductionStockDocs_Report REFERENCES prd.ProductionReports (Id),
    StockDocId BIGINT NOT NULL CONSTRAINT FK_ProductionStockDocs_Doc REFERENCES inv.StockDocs (Id),
    CONSTRAINT PK_ProductionStockDocs PRIMARY KEY (ReportId, StockDocId)
);
GO

/* ---------------- کنترل نوع کالا در فرمول ---------------- */
CREATE TRIGGER prd.trg_Boms_Product ON prd.Boms AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted b JOIN inv.Items i ON i.Id = b.ProductId JOIN inv.ItemCategories c ON c.Id = i.CategoryId
               WHERE c.CanProductionOutput = 0)
        THROW 50500, N'محصول فرمول ساخت باید از نوعی باشد که تولید می‌شود (محصول یا نیم‌ساخته).', 1;
END;
GO

CREATE TRIGGER prd.trg_BomMaterials_Item ON prd.BomMaterials AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted m JOIN inv.Items i ON i.Id = m.ItemId JOIN inv.ItemCategories c ON c.Id = i.CategoryId
               WHERE c.CanProductionInput = 0)
        THROW 50501, N'مواد فرمول ساخت باید از نوعی باشد که در تولید مصرف می‌شود (مواد یا نیم‌ساخته).', 1;
    IF EXISTS (SELECT 1 FROM inserted m JOIN prd.Boms b ON b.Id = m.BomId WHERE m.ItemId = b.ProductId)
        THROW 50502, N'محصول در مواد فرمول خودش نمی‌آید.', 1;
END;
GO

CREATE TRIGGER prd.trg_BomScraps_Item ON prd.BomScraps AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted s JOIN inv.Items i ON i.Id = s.ItemId JOIN inv.ItemCategories c ON c.Id = i.CategoryId
               WHERE c.IsScrap = 0)
        THROW 50503, N'ردیف ضایعات فقط کالای نوع «ضایعات» می‌پذیرد.', 1;
END;
GO

PRINT N'فرمول ساخت و گزارش تولید آماده شد.';
GO
