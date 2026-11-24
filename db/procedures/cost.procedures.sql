/*
    LT ODM Costing - prices per IO (roadmap 3.16; tables in tables\cost.tables.sql)

    cost.fn_IoPrice          : the costing that stands for each IO
    cost.usp_Costing_List    : styles with their prices per IO, most recently costed first (also one style's summary)
    cost.usp_StylePrices_Get : one style's IOs
    cost.usp_Import_Save     : the ERP costing export (a costing version already here is replaced)
    cost.fn_StylePrices      : a style's prices over its IOs, per currency
    cost.fn_PriceFor         : the prices that best stand for a style: its own, else the same style in another season, else the style it was copied from
    cost.usp_Price_Hints     : those prices for a list of styles (Similar, Concepts)
    cost.usp_Quote_List / _Save / _Prices / _Check : quotes on package styles (pack.PackageItemQuote, roadmap 5.19)
    cost.fn_PlantRate, usp_PlantRate_List / _Save / _Delete : plant cost per minute (cost.PlantCostPerMinute)
    cost.fn_Estimate, usp_Estimate : an estimated FOB for a garment with no IO prices, from similar styles and the plant's cost per minute
    cost.usp_Costing_Guide / _MyConcepts / _Overview : home dashboard panels

    Requires tables\style.tables.sql, tables\pack.tables.sql and tables\cost.tables.sql. Idempotent.

      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -E -b -I -i procedures\cost.procedures.sql
*/
SET NOCOUNT ON;
GO

/*
    The costing that stands for each IO: the latest final costing whose main fabric has been computed, else the latest
    pre-costing, else whatever is left. A costing without a FOB is not finished and never stands for an IO.
    FinalPending = the IO has a final costing whose main fabric is not computed yet (its net balance would be overstated).
*/
CREATE OR ALTER FUNCTION cost.fn_IoPrice()
RETURNS TABLE
AS
RETURN
    SELECT i.IoId, c.CostingVersion, c.CostType, c.CurrencyCode, c.CostDate, c.Fob, c.NetBalance, c.PercToFob,
           FinalPending = CAST(CASE WHEN EXISTS (
                SELECT 1 FROM cost.IoCosting f
                WHERE f.IoId = i.IoId AND f.CostType = 'FCOSTING'
                  AND ISNULL((SELECT l.Amount FROM cost.IoCostingLine l WHERE l.CostingVersion = f.CostingVersion AND l.CategoryCode = 'MAINFAB'), 0) = 0)
                THEN 1 ELSE 0 END AS bit),
           g.Fabric, g.Trims, g.Making, g.Other
    FROM cost.Io i
    CROSS APPLY
    (
        SELECT TOP (1) k.CostingVersion, k.CostType, k.CurrencyCode, k.CostDate, k.Fob, k.NetBalance, k.PercToFob
        FROM cost.IoCosting k
        WHERE k.IoId = i.IoId AND k.Fob > 0
        ORDER BY CASE WHEN k.CostType = 'FCOSTING'
                       AND ISNULL((SELECT l.Amount FROM cost.IoCostingLine l WHERE l.CostingVersion = k.CostingVersion AND l.CategoryCode = 'MAINFAB'), 0) > 0 THEN 0
                      WHEN k.CostType = 'PCOSTING' THEN 1
                      ELSE 2 END,
                 k.CostingVersion DESC
    ) c
    CROSS APPLY
    (
        SELECT Fabric = ISNULL(SUM(CASE WHEN ca.GroupCode = 'Fabric' THEN l.Amount END), 0),
               Trims  = ISNULL(SUM(CASE WHEN ca.GroupCode = 'Trims'  THEN l.Amount END), 0),
               Making = ISNULL(SUM(CASE WHEN ca.GroupCode = 'Making' THEN l.Amount END), 0),
               Other  = ISNULL(SUM(CASE WHEN ca.GroupCode = 'Other'  THEN l.Amount END), 0)
        FROM cost.IoCostingLine l
        JOIN cost.Category ca ON ca.CategoryCode = l.CategoryCode
        WHERE l.CostingVersion = c.CostingVersion
    ) g;
GO

/*
    A row per style and currency (@StyleId NULL = every style). IOs below @SmallQty (samples, one-offs) are left out of the figures,
    unless the style has nothing else. AvgFob and the Avg cost groups are weighted by order quantity.
*/
CREATE OR ALTER FUNCTION cost.fn_StylePrices(@StyleId int, @SmallQty int)
RETURNS TABLE
AS
RETURN
    WITH p AS
    (
        SELECT i.StyleId, i.IoNo, i.OrderQty, x.CurrencyCode, x.CostDate, x.CostingVersion, x.Fob, x.Fabric, x.Trims, x.Making, x.Other, x.NetBalance,
               Biggest = MAX(ISNULL(i.OrderQty, 0)) OVER (PARTITION BY i.StyleId)
        FROM cost.Io i
        JOIN cost.fn_IoPrice() x ON x.IoId = i.IoId
        WHERE (@StyleId IS NULL OR i.StyleId = @StyleId)
    ),
    u AS
    (
        SELECT *, Q = CAST(ISNULL(OrderQty, 0) AS decimal(18, 5)),
               Rn = ROW_NUMBER() OVER (PARTITION BY StyleId, CurrencyCode ORDER BY CostDate DESC, CostingVersion DESC)
        FROM p
        WHERE ISNULL(OrderQty, 0) >= @SmallQty OR Biggest < @SmallQty
    )
    SELECT StyleId, CurrencyCode, Ios = COUNT(*), TotalQty = SUM(CAST(ISNULL(OrderQty, 0) AS bigint)),
           LatestFob = MAX(CASE WHEN Rn = 1 THEN Fob END), LatestIoNo = MAX(CASE WHEN Rn = 1 THEN IoNo END), LatestCostDate = MAX(CostDate),
           MinFob = MIN(Fob), MaxFob = MAX(Fob),
           AvgFob    = COALESCE(CAST(SUM(Fob * Q) / NULLIF(SUM(Q), 0) AS decimal(18, 5)), CAST(AVG(Fob) AS decimal(18, 5))),
           AvgFabric = COALESCE(CAST(SUM(Fabric * Q) / NULLIF(SUM(Q), 0) AS decimal(18, 5)), CAST(AVG(Fabric) AS decimal(18, 5))),
           AvgTrims  = COALESCE(CAST(SUM(Trims * Q) / NULLIF(SUM(Q), 0) AS decimal(18, 5)), CAST(AVG(Trims) AS decimal(18, 5))),
           AvgMaking = COALESCE(CAST(SUM(Making * Q) / NULLIF(SUM(Q), 0) AS decimal(18, 5)), CAST(AVG(Making) AS decimal(18, 5))),
           AvgOther  = COALESCE(CAST(SUM(Other * Q) / NULLIF(SUM(Q), 0) AS decimal(18, 5)), CAST(AVG(Other) AS decimal(18, 5))),
           AvgNet    = COALESCE(CAST(SUM(NetBalance * Q) / NULLIF(SUM(CASE WHEN NetBalance IS NOT NULL THEN Q END), 0) AS decimal(18, 5)), CAST(AVG(NetBalance) AS decimal(18, 5)))
    FROM u
    GROUP BY StyleId, CurrencyCode;
GO

/*
    The prices that best stand for a style, one row at most: its own IOs; else the same style in another season of the same
    customer (same base style number); else the style it was copied from. Latest costed currency first.
*/
CREATE OR ALTER FUNCTION cost.fn_PriceFor(@StyleId int, @SmallQty int)
RETURNS TABLE
AS
RETURN
    SELECT TOP (1) StyleId = @StyleId, x.Source, SourceStyleNo = src.StyleNo, p.CurrencyCode, p.LatestFob, p.MinFob, p.MaxFob, p.AvgFob, p.Ios, p.TotalQty, p.LatestCostDate, p.LatestIoNo
    FROM
    (
        SELECT SourceId = s.StyleId, Source = 'Own', Pri = 1 FROM style.Style s WHERE s.StyleId = @StyleId
        UNION ALL
        SELECT f.StyleId, 'Family', 2 FROM style.Style s
        JOIN style.Style f ON f.BaseStyleNo = s.BaseStyleNo AND f.CustomerId = s.CustomerId AND f.StyleId <> s.StyleId
        WHERE s.StyleId = @StyleId AND s.BaseStyleNo IS NOT NULL
        UNION ALL
        SELECT s.OriginStyleId, 'Origin', 3 FROM style.Style s WHERE s.StyleId = @StyleId AND s.OriginStyleId IS NOT NULL
    ) x
    JOIN style.Style src ON src.StyleId = x.SourceId
    CROSS APPLY cost.fn_StylePrices(x.SourceId, @SmallQty) p
    ORDER BY x.Pri, p.LatestCostDate DESC;
GO

/* cost.fn_PriceFor for a list of styles (@StyleIds: JSON array of ids). Styles with no prices anywhere are left out. */
CREATE OR ALTER PROCEDURE cost.usp_Price_Hints
    @StyleIds nvarchar(max),
    @SmallQty int = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.StyleId, p.Source, p.SourceStyleNo, CurrencyCode = RTRIM(p.CurrencyCode), p.LatestFob, p.MinFob, p.MaxFob, p.AvgFob, p.Ios, p.TotalQty, p.LatestCostDate, p.LatestIoNo
    FROM (SELECT DISTINCT StyleId = TRY_CAST([value] AS int) FROM OPENJSON(ISNULL(@StyleIds, N'[]'))) s
    CROSS APPLY cost.fn_PriceFor(s.StyleId, @SmallQty) p
    WHERE s.StyleId IS NOT NULL;
END;
GO

/*
    A package's quotes. First result set: the client's price band, when its profile has one. Second: each style with what its IO
    prices suggest. Third: every quote version, newest first.
*/
CREATE OR ALTER PROCEDURE cost.usp_Quote_List
    @PackageId int,
    @SmallQty  int = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customer nvarchar(32) = (SELECT c.PartnerCode FROM pack.Package p JOIN partner.Partner c ON c.PartnerId = p.CustomerId WHERE p.PackageId = @PackageId);

    SELECT cp.PriceMin, cp.PriceMax, Currency = RTRIM(cp.Currency)
    FROM pack.ClientProfile cp
    WHERE cp.ClientKey = pack.fn_ClientKey(@customer) AND (cp.PriceMin IS NOT NULL OR cp.PriceMax IS NOT NULL);

    SELECT i.PackageItemId, i.StyleId, h.Source, h.SourceStyleNo, CurrencyCode = RTRIM(h.CurrencyCode), h.LatestFob, h.MinFob, h.MaxFob, h.AvgFob, h.Ios, h.TotalQty, h.LatestCostDate, h.LatestIoNo,
           EstCategory = e.Category, EstPeers = e.Peers, EstPeerStyles = e.PeerStyles, EstCurrency = e.CurrencyCode, EstFob = e.EstimatedFob, EstSmv = e.Smv, EstPlant = e.PlantCode,
           EstCpm = e.Cpm, EstCpmFromRate = e.CpmFromRate, EstMaking = e.Making, EstFabric = e.Fabric, EstTrims = e.Trims, EstOther = e.Other, EstNetShare = e.NetShare
    FROM pack.PackageItem i
    JOIN style.Style s ON s.StyleId = i.StyleId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    OUTER APPLY cost.fn_PriceFor(i.StyleId, @SmallQty) h
    OUTER APPLY (SELECT * FROM cost.fn_Estimate(ISNULL(style.fn_GarmentCategory(pt.Name), style.fn_GarmentCategory(pt.ProductTypeCode)), @customer, @SmallQty) WHERE h.Source IS NULL) e
    WHERE i.PackageId = @PackageId;

    SELECT q.PackageItemId, q.Version, q.Fob, CurrencyCode = RTRIM(q.CurrencyCode), q.BasisKind, q.BasisText, q.QuotedBy, q.QuotedUtc
    FROM pack.PackageItemQuote q
    JOIN pack.PackageItem i ON i.PackageItemId = q.PackageItemId
    WHERE i.PackageId = @PackageId
    ORDER BY q.PackageItemId, q.Version DESC;
END;
GO

/* A new version of the style's quote. The same price and currency as the current quote changes nothing. */
CREATE OR ALTER PROCEDURE cost.usp_Quote_Save
    @PackageId     int,
    @PackageItemId int,
    @Fob           decimal(18, 5),
    @CurrencyCode  char(3),
    @BasisKind     varchar(10),
    @BasisText     nvarchar(300) = NULL,
    @QuotedBy      nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM pack.PackageItem WITH (UPDLOCK, HOLDLOCK) WHERE PackageItemId = @PackageItemId AND PackageId = @PackageId)
        THROW 50404, 'This style is no longer in the package.', 1;

    IF NOT EXISTS (SELECT 1 FROM pack.PackageItemQuote q
                   WHERE q.PackageItemId = @PackageItemId AND q.Fob = @Fob AND q.CurrencyCode = @CurrencyCode
                     AND q.Version = (SELECT MAX(x.Version) FROM pack.PackageItemQuote x WHERE x.PackageItemId = @PackageItemId))
        INSERT pack.PackageItemQuote (PackageItemId, Version, Fob, CurrencyCode, BasisKind, BasisText, QuotedBy)
        SELECT @PackageItemId, ISNULL(MAX(Version), 0) + 1, @Fob, @CurrencyCode, @BasisKind, @BasisText, @QuotedBy
        FROM pack.PackageItemQuote WHERE PackageItemId = @PackageItemId;
    COMMIT TRANSACTION;
END;
GO

/* The current quote of each style of the package (for what the client sees). */
CREATE OR ALTER PROCEDURE cost.usp_Quote_Prices
    @PackageId int
AS
BEGIN
    SET NOCOUNT ON;
    SELECT q.PackageItemId, q.Fob, CurrencyCode = RTRIM(q.CurrencyCode)
    FROM pack.PackageItemQuote q
    JOIN pack.PackageItem i ON i.PackageItemId = q.PackageItemId
    WHERE i.PackageId = @PackageId
      AND q.Version = (SELECT MAX(x.Version) FROM pack.PackageItemQuote x WHERE x.PackageItemId = q.PackageItemId);
END;
GO

/* Quotes outside the client's price band (same currency only: rates are not kept). Shaped like pack.usp_ClientProfile_Check's issues. */
CREATE OR ALTER PROCEDURE cost.usp_Quote_Check
    @PackageId int
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @customer nvarchar(32) = (SELECT c.PartnerCode FROM pack.Package p JOIN partner.Partner c ON c.PartnerId = p.CustomerId WHERE p.PackageId = @PackageId);
    DECLARE @min decimal(18, 5), @max decimal(18, 5), @currency char(3);
    SELECT @min = PriceMin, @max = PriceMax, @currency = Currency FROM pack.ClientProfile WHERE ClientKey = pack.fn_ClientKey(@customer);

    SELECT i.StyleId, Kind = 'Price',
           Detail = CONCAT(FORMAT(q.Fob, '0.00', 'en-US'), N' ', RTRIM(q.CurrencyCode), N' (band ',
                           ISNULL(FORMAT(@min, '0.00', 'en-US'), N'-'), N' to ', ISNULL(FORMAT(@max, '0.00', 'en-US'), N'-'), N')')
    FROM pack.PackageItemQuote q
    JOIN pack.PackageItem i ON i.PackageItemId = q.PackageItemId
    WHERE i.PackageId = @PackageId AND q.CurrencyCode = @currency
      AND q.Version = (SELECT MAX(x.Version) FROM pack.PackageItemQuote x WHERE x.PackageItemId = q.PackageItemId)
      AND (q.Fob < @min OR q.Fob > @max);
END;
GO

/*
    A row per style and currency, most recently costed first (cost.fn_StylePrices). @StyleId = just that style.
    Second result set: the latest import.
*/
CREATE OR ALTER PROCEDURE cost.usp_Costing_List
    @Search   nvarchar(100) = NULL,
    @Customer nvarchar(32)  = NULL,
    @Season   nvarchar(32)  = NULL,
    @StyleId  int           = NULL,
    @SmallQty int           = 50,
    @Skip     int           = 0,
    @Take     int           = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @like nvarchar(102) = CASE WHEN NULLIF(LTRIM(RTRIM(@Search)), N'') IS NOT NULL THEN N'%' + LTRIM(RTRIM(@Search)) + N'%' END;

    SELECT g.StyleId, s.StyleNo, s.SeasonCode, CustomerCode = c.PartnerCode, s.ModelName, ProductTypeName = pt.Name, s.ImageUrl, s.SketchUrl,
           g.CurrencyCode, g.Ios, g.TotalQty, g.LatestFob, g.LatestCostDate, g.MinFob, g.MaxFob, g.AvgFob,
           g.AvgFabric, g.AvgTrims, g.AvgMaking, g.AvgOther, g.AvgNet,
           Total = COUNT(*) OVER ()
    FROM cost.fn_StylePrices(@StyleId, @SmallQty) g
    JOIN style.Style s ON s.StyleId = g.StyleId
    JOIN partner.Partner c ON c.PartnerId = s.CustomerId
    LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    WHERE (@like IS NULL OR s.StyleNo LIKE @like OR s.ModelName LIKE @like)
      AND (@Customer IS NULL OR c.PartnerCode = @Customer)
      AND (@Season IS NULL OR s.SeasonCode = @Season)
    ORDER BY g.LatestCostDate DESC, s.StyleNo, g.CurrencyCode
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

    SELECT TOP (1) ImportId, FileName, RowsRead, Added, Updated, Unmatched, ImportedBy, ImportedUtc
    FROM cost.Import ORDER BY ImportId DESC;
END;
GO

/* One style's IOs, latest costing first, and how many IOs have no finished costing yet (no FOB). */
CREATE OR ALTER PROCEDURE cost.usp_StylePrices_Get
    @StyleId  int,
    @SmallQty int = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT i.IoNo, i.PlantCode, PlantName = pl.Name, i.OrderQty, i.LcoDate, i.PlanDeliveryDate, i.TcSmv,
           x.CostingVersion, x.CostType, x.CurrencyCode, x.CostDate, x.Fob, x.Fabric, x.Trims, x.Making, x.Other, x.NetBalance, x.PercToFob, x.FinalPending,
           IsSmall = CAST(CASE WHEN ISNULL(i.OrderQty, 0) < @SmallQty THEN 1 ELSE 0 END AS bit)
    FROM cost.Io i
    JOIN cost.fn_IoPrice() x ON x.IoId = i.IoId
    LEFT JOIN ref.Plant pl ON pl.PlantCode = i.PlantCode
    WHERE i.StyleId = @StyleId
    ORDER BY x.CostDate DESC, x.CostingVersion DESC;

    SELECT Unfinished = COUNT(*)
    FROM cost.Io i
    WHERE i.StyleId = @StyleId AND NOT EXISTS (SELECT 1 FROM cost.IoCosting k WHERE k.IoId = i.IoId AND k.Fob > 0);
END;
GO

/*
    Saves the ERP costing export. @Rows: JSON array, one object per costing version (see CostingRepository). A row is matched
    to its library style by customer, season and style number; rows without a style are not saved but listed, so the style
    can be imported first. A costing version already here is replaced, so the file can be loaded again.
    First result set: the import. Second: the styles not in the library.
*/
CREATE OR ALTER PROCEDURE cost.usp_Import_Save
    @FileName   nvarchar(260),
    @ImportedBy nvarchar(256),
    @Rows       nvarchar(max)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    CREATE TABLE #r
    (
        Cust nvarchar(32) COLLATE DATABASE_DEFAULT, Season nvarchar(32) COLLATE DATABASE_DEFAULT, StyleNo nvarchar(100) COLLATE DATABASE_DEFAULT,
        IoNo nvarchar(30) COLLATE DATABASE_DEFAULT, PlantCode nvarchar(20) COLLATE DATABASE_DEFAULT, PlantName nvarchar(120) COLLATE DATABASE_DEFAULT,
        OrderQty int, LcoDate date, PlanDeliveryDate date, MerchSmv decimal(12, 5), TcSmv decimal(12, 5),
        CostingVersion int, CostType varchar(10) COLLATE DATABASE_DEFAULT, Status nvarchar(30) COLLATE DATABASE_DEFAULT,
        CurrencyCode char(3) COLLATE DATABASE_DEFAULT, CostDate datetime2(3), Fob decimal(18, 5), NetBalance decimal(18, 5), PercToFob decimal(18, 5),
        Lines nvarchar(max) COLLATE DATABASE_DEFAULT, StyleId int NULL
    );
    INSERT #r (Cust, Season, StyleNo, IoNo, PlantCode, PlantName, OrderQty, LcoDate, PlanDeliveryDate, MerchSmv, TcSmv, CostingVersion, CostType, Status, CurrencyCode, CostDate, Fob, NetBalance, PercToFob, Lines)
    SELECT Cust, Season, StyleNo, IoNo, PlantCode, PlantName, OrderQty, LcoDate, PlanDeliveryDate, MerchSmv, TcSmv, CostingVersion, CostType, Status, CurrencyCode, CostDate, Fob, NetBalance, PercToFob, Lines
    FROM OPENJSON(@Rows) WITH
    (
        Cust nvarchar(32) '$.cust', Season nvarchar(32) '$.season', StyleNo nvarchar(100) '$.styleNo', IoNo nvarchar(30) '$.ioNo',
        PlantCode nvarchar(20) '$.plantCode', PlantName nvarchar(120) '$.plantName', OrderQty int '$.orderQty', LcoDate date '$.lcoDate',
        PlanDeliveryDate date '$.planDeliveryDate', MerchSmv decimal(12, 5) '$.merchSmv', TcSmv decimal(12, 5) '$.tcSmv',
        CostingVersion int '$.costingVersion', CostType varchar(10) '$.costType', Status nvarchar(30) '$.status', CurrencyCode char(3) '$.currency',
        CostDate datetime2(3) '$.costDate', Fob decimal(18, 5) '$.fob', NetBalance decimal(18, 5) '$.netBalance', PercToFob decimal(18, 5) '$.percToFob',
        Lines nvarchar(max) '$.lines' AS JSON
    );

    UPDATE r SET StyleId = s.StyleId
    FROM #r r
    JOIN partner.Partner c ON c.PartnerCode = r.Cust
    JOIN style.Style s ON s.CustomerId = c.PartnerId AND s.SeasonCode = r.Season AND s.StyleNo = r.StyleNo;

    BEGIN TRANSACTION;

    INSERT cost.Import (FileName, ImportedBy) VALUES (@FileName, @ImportedBy);
    DECLARE @importId int = SCOPE_IDENTITY();

    /* A plant seen for the first time joins the list; a name already there is kept (Settings > Reference lists owns it). */
    INSERT ref.Plant (PlantCode, Name)
    SELECT p.PlantCode, ISNULL(p.PlantName, p.PlantCode)
    FROM (SELECT PlantCode, PlantName = MAX(PlantName) FROM #r WHERE PlantCode IS NOT NULL AND StyleId IS NOT NULL GROUP BY PlantCode) p
    WHERE NOT EXISTS (SELECT 1 FROM ref.Plant x WHERE x.PlantCode = p.PlantCode);

    /* An IO appears once per costing version: its latest row carries the IO's details. */
    ;WITH latest AS
    (
        SELECT *, Rn = ROW_NUMBER() OVER (PARTITION BY IoNo ORDER BY CostingVersion DESC) FROM #r WHERE StyleId IS NOT NULL
    )
    MERGE cost.Io AS t
    USING (SELECT * FROM latest WHERE Rn = 1) AS r ON t.IoNo = r.IoNo
    WHEN MATCHED THEN UPDATE SET StyleId = r.StyleId, PlantCode = r.PlantCode, OrderQty = r.OrderQty, LcoDate = r.LcoDate,
        PlanDeliveryDate = r.PlanDeliveryDate, MerchSmv = r.MerchSmv, TcSmv = r.TcSmv, ImportId = @importId, UpdatedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (IoNo, StyleId, PlantCode, OrderQty, LcoDate, PlanDeliveryDate, MerchSmv, TcSmv, ImportId)
        VALUES (r.IoNo, r.StyleId, r.PlantCode, r.OrderQty, r.LcoDate, r.PlanDeliveryDate, r.MerchSmv, r.TcSmv, @importId);

    DECLARE @updated int = (SELECT COUNT(*) FROM #r r JOIN cost.IoCosting k ON k.CostingVersion = r.CostingVersion WHERE r.StyleId IS NOT NULL);

    MERGE cost.IoCosting AS t
    USING (SELECT r.*, i.IoId FROM #r r JOIN cost.Io i ON i.IoNo = r.IoNo WHERE r.StyleId IS NOT NULL) AS r ON t.CostingVersion = r.CostingVersion
    WHEN MATCHED THEN UPDATE SET IoId = r.IoId, CostType = r.CostType, Status = r.Status, CurrencyCode = r.CurrencyCode, CostDate = r.CostDate, Fob = r.Fob,
        NetBalance = r.NetBalance, PercToFob = r.PercToFob, ImportId = @importId, UpdatedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (CostingVersion, IoId, CostType, Status, CurrencyCode, CostDate, Fob, NetBalance, PercToFob, ImportId)
        VALUES (r.CostingVersion, r.IoId, r.CostType, r.Status, r.CurrencyCode, r.CostDate, r.Fob, r.NetBalance, r.PercToFob, @importId);

    DELETE l FROM cost.IoCostingLine l JOIN #r r ON r.CostingVersion = l.CostingVersion WHERE r.StyleId IS NOT NULL;

    INSERT cost.IoCostingLine (CostingVersion, CategoryCode, Amount)
    SELECT r.CostingVersion, j.Code, j.Amount
    FROM #r r
    CROSS APPLY OPENJSON(r.Lines) WITH (Code varchar(12) '$.code', Amount decimal(18, 5) '$.amount') j
    WHERE r.StyleId IS NOT NULL;

    DECLARE @read int = (SELECT COUNT(*) FROM #r), @matched int = (SELECT COUNT(*) FROM #r WHERE StyleId IS NOT NULL);
    UPDATE cost.Import SET RowsRead = @read, Added = @matched - @updated, Updated = @updated, Unmatched = @read - @matched WHERE ImportId = @importId;

    COMMIT;

    SELECT ImportId, FileName, RowsRead, Added, Updated, Unmatched, ImportedBy, ImportedUtc FROM cost.Import WHERE ImportId = @importId;

    SELECT Customer = Cust, Season, StyleNo, Rows = COUNT(*)
    FROM #r WHERE StyleId IS NULL GROUP BY Cust, Season, StyleNo ORDER BY StyleNo;
END;
GO

/* ---------- Plant cost per minute (cost.PlantCostPerMinute) ---------- */

/*
    The rate in force for a plant on a date, for an order of @Qty pieces (NULL = any) in @Currency (NULL = any): the latest
    effective date on or before @OnDate, then the highest minimum quantity the order reaches. Deleted rates are never used.
*/
CREATE OR ALTER FUNCTION cost.fn_PlantRate(@PlantCode nvarchar(20), @OnDate date, @Qty int, @Currency char(3))
RETURNS TABLE
AS
RETURN
    SELECT TOP (1) r.RateId, r.EffectiveDate, r.MinOrderQty, r.SalesTerm, CurrencyCode = RTRIM(r.CurrencyCode), r.Cost
    FROM cost.PlantCostPerMinute r
    WHERE r.PlantCode = @PlantCode AND r.IsDeleted = 0 AND r.EffectiveDate <= @OnDate
      AND (@Qty IS NULL OR r.MinOrderQty <= @Qty) AND (@Currency IS NULL OR r.CurrencyCode = @Currency)
    ORDER BY r.EffectiveDate DESC, r.MinOrderQty DESC;
GO

/* Every rate, plant by plant, newest first (deleted ones too when @WithDeleted = 1); then the plants to choose from. */
CREATE OR ALTER PROCEDURE cost.usp_PlantRate_List
    @WithDeleted bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RateId, r.PlantCode, PlantName = p.Name, r.EffectiveDate, r.MinOrderQty, r.SalesTerm, CurrencyCode = RTRIM(r.CurrencyCode), r.Cost, r.IsDeleted,
           InForce = CAST(IIF(r.IsDeleted = 0 AND r.RateId = (SELECT f.RateId FROM cost.fn_PlantRate(r.PlantCode, CAST(SYSUTCDATETIME() AS date), NULL, r.CurrencyCode) f), 1, 0) AS bit),
           r.CreatedBy, r.CreatedUtc, r.UpdatedBy, r.UpdatedUtc
    FROM cost.PlantCostPerMinute r
    JOIN ref.Plant p ON p.PlantCode = r.PlantCode
    WHERE @WithDeleted = 1 OR r.IsDeleted = 0
    ORDER BY r.PlantCode, r.EffectiveDate DESC, r.MinOrderQty DESC;

    SELECT Code = PlantCode, Name FROM ref.Plant WHERE IsActive = 1 ORDER BY PlantCode;
END;
GO

/* Adds (@RateId NULL) or changes a rate. 50404: unknown plant or rate; 50409: the same plant, date, quantity, term and currency exists. Returns the RateId. */
CREATE OR ALTER PROCEDURE cost.usp_PlantRate_Save
    @RateId        int           = NULL,
    @PlantCode     nvarchar(20),
    @EffectiveDate date,
    @MinOrderQty   int,
    @SalesTerm     varchar(10),
    @CurrencyCode  char(3),
    @Cost          decimal(18, 5),
    @ChangedBy     nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM ref.Plant WHERE PlantCode = @PlantCode) THROW 50404, 'This plant is not in Settings > Reference lists > Plants.', 1;
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM cost.PlantCostPerMinute WITH (UPDLOCK, HOLDLOCK)
               WHERE PlantCode = @PlantCode AND EffectiveDate = @EffectiveDate AND MinOrderQty = @MinOrderQty AND SalesTerm = @SalesTerm
                 AND CurrencyCode = @CurrencyCode AND IsDeleted = 0 AND RateId <> ISNULL(@RateId, 0))
        THROW 50409, 'This plant already has a rate from that date for that quantity, sales term and currency.', 1;
    IF @RateId IS NULL
    BEGIN
        INSERT cost.PlantCostPerMinute (PlantCode, EffectiveDate, MinOrderQty, SalesTerm, CurrencyCode, Cost, CreatedBy)
        VALUES (@PlantCode, @EffectiveDate, @MinOrderQty, @SalesTerm, @CurrencyCode, @Cost, @ChangedBy);
        SET @RateId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE cost.PlantCostPerMinute
        SET PlantCode = @PlantCode, EffectiveDate = @EffectiveDate, MinOrderQty = @MinOrderQty, SalesTerm = @SalesTerm, CurrencyCode = @CurrencyCode,
            Cost = @Cost, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME()
        WHERE RateId = @RateId AND IsDeleted = 0;
        IF @@ROWCOUNT = 0 THROW 50404, 'This rate no longer exists. Reload the list.', 1;
    END
    COMMIT TRANSACTION;
    SELECT RateId = @RateId;
END;
GO

/* Marks a rate deleted (kept, never used again). */
CREATE OR ALTER PROCEDURE cost.usp_PlantRate_Delete
    @RateId    int,
    @ChangedBy nvarchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE cost.PlantCostPerMinute SET IsDeleted = 1, UpdatedBy = @ChangedBy, UpdatedUtc = SYSUTCDATETIME() WHERE RateId = @RateId AND IsDeleted = 0;
    IF @@ROWCOUNT = 0 THROW 50404, 'This rate no longer exists. Reload the list.', 1;
END;
GO

/* ---------- Estimates (no IO prices for the style) ---------- */

/*
    An estimated FOB for a garment (@Category: the codes of style.fn_GarmentCategory, as concepts use) for a client, from the priced
    styles of that garment: the client's own (brand, else customer) when it has two or more, else every client's; in the currency
    most of them use. Fabric, trims and other = their average per piece; making = the plant's cost per minute in force today
    (cost.fn_PlantRate, the plant most of them were made at) x their average TC SMV, else their average making cost; net balance =
    their average share of FOB. EstimatedFob = the costs / (1 - that share). No row when no priced style is of that garment.
*/
CREATE OR ALTER FUNCTION cost.fn_Estimate(@Category nvarchar(20), @CustomerCode nvarchar(32), @SmallQty int)
RETURNS TABLE
AS
RETURN
    WITH peers AS
    (
        SELECT p.StyleId, s.StyleNo, p.CurrencyCode, p.AvgFob, p.AvgFabric, p.AvgTrims, p.AvgMaking, p.AvgOther, p.AvgNet,
               Smv = (SELECT AVG(i.TcSmv) FROM cost.Io i WHERE i.StyleId = p.StyleId AND i.TcSmv > 0),
               Plant = (SELECT TOP (1) i.PlantCode FROM cost.Io i WHERE i.StyleId = p.StyleId AND i.PlantCode IS NOT NULL GROUP BY i.PlantCode ORDER BY COUNT(*) DESC),
               SameClient = CASE WHEN pack.fn_ClientKey(c.PartnerCode) = pack.fn_ClientKey(@CustomerCode) THEN 1 ELSE 0 END
        FROM cost.fn_StylePrices(NULL, @SmallQty) p
        JOIN style.Style s ON s.StyleId = p.StyleId
        JOIN partner.Partner c ON c.PartnerId = s.CustomerId
        LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
        WHERE @Category IS NOT NULL AND p.AvgFob > 0
          AND ISNULL(style.fn_GarmentCategory(pt.Name), style.fn_GarmentCategory(pt.ProductTypeCode)) = @Category
    ),
    cur AS (SELECT TOP (1) CurrencyCode FROM peers GROUP BY CurrencyCode ORDER BY SUM(SameClient) DESC, COUNT(*) DESC),
    inCur AS (SELECT pe.* FROM peers pe JOIN cur ON cur.CurrencyCode = pe.CurrencyCode),
    pick AS (SELECT * FROM inCur WHERE SameClient = 1 OR (SELECT COUNT(*) FROM inCur x WHERE x.SameClient = 1) < 2),
    plant AS (SELECT TOP (1) Plant FROM pick WHERE Plant IS NOT NULL GROUP BY Plant ORDER BY COUNT(*) DESC),
    agg AS
    (
        SELECT Peers = COUNT(*), PeerStyles = STRING_AGG(CAST(StyleNo AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY StyleNo), CurrencyCode = MAX(RTRIM(CurrencyCode)),
               Smv = CAST(AVG(Smv) AS decimal(12, 5)), Fabric = CAST(AVG(AvgFabric) AS decimal(18, 5)), Trims = CAST(AVG(AvgTrims) AS decimal(18, 5)),
               Other = CAST(AVG(AvgOther) AS decimal(18, 5)), PeerMaking = CAST(AVG(AvgMaking) AS decimal(18, 5)), NetShare = CAST(AVG(AvgNet / AvgFob) AS decimal(9, 5))
        FROM pick
        HAVING COUNT(*) > 0
    )
    SELECT Category = ISNULL((SELECT pc.Name FROM ref.ProductCategory pc WHERE pc.ProductCategoryCode = @Category), @Category), a.Peers, a.PeerStyles, a.CurrencyCode, a.Smv, PlantCode = pl.Plant, Cpm = r.Cost, CpmFromRate = CAST(IIF(r.Cost IS NULL, 0, 1) AS bit),
           Making = CAST(COALESCE(r.Cost * a.Smv, a.PeerMaking) AS decimal(18, 5)), a.Fabric, a.Trims, a.Other, a.NetShare,
           EstimatedFob = CAST((a.Fabric + a.Trims + a.Other + COALESCE(r.Cost * a.Smv, a.PeerMaking)) / NULLIF(1 - ISNULL(a.NetShare, 0), 0) AS decimal(18, 5))
    FROM agg a
    LEFT JOIN plant pl ON 1 = 1
    OUTER APPLY cost.fn_PlantRate(pl.Plant, CAST(SYSUTCDATETIME() AS date), NULL, a.CurrencyCode) r;
GO

CREATE OR ALTER PROCEDURE cost.usp_Estimate
    @Category     nvarchar(20),
    @CustomerCode nvarchar(32) = NULL,
    @SmallQty     int = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM cost.fn_Estimate(@Category, @CustomerCode, @SmallQty);
END;
GO

/* ---------- Dashboards ---------- */

/*
    Price guide by garment (home dashboards): per garment (style.fn_GarmentCategory) and currency, the styles with IO prices and the low, typical
    (median) and high of their latest FOB and of their TC SMV.
*/
CREATE OR ALTER PROCEDURE cost.usp_Costing_Guide
    @SmallQty int = 50
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH p AS
    (
        SELECT Category = ISNULL(style.fn_GarmentCategory(pt.Name), style.fn_GarmentCategory(pt.ProductTypeCode)), CurrencyCode = RTRIM(g.CurrencyCode), g.LatestFob,
               Smv = (SELECT AVG(i.TcSmv) FROM cost.Io i WHERE i.StyleId = g.StyleId AND i.TcSmv > 0)
        FROM cost.fn_StylePrices(NULL, @SmallQty) g
        JOIN style.Style s ON s.StyleId = g.StyleId
        LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
    ),
    m AS
    (
        SELECT Category, CurrencyCode, LatestFob, Smv,
               TypicalFob = PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY LatestFob) OVER (PARTITION BY Category, CurrencyCode),
               TypicalSmv = PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Smv) OVER (PARTITION BY Category, CurrencyCode)
        FROM p WHERE Category IS NOT NULL
    )
    SELECT m.Category, CategoryName = ISNULL(MAX(pc.Name), m.Category), m.CurrencyCode, Styles = COUNT(*),
           LowFob = MIN(m.LatestFob), TypicalFob = CAST(MAX(m.TypicalFob) AS decimal(18, 5)), HighFob = MAX(m.LatestFob),
           LowSmv = CAST(MIN(m.Smv) AS decimal(12, 5)), TypicalSmv = CAST(MAX(m.TypicalSmv) AS decimal(12, 5)), HighSmv = CAST(MAX(m.Smv) AS decimal(12, 5))
    FROM m
    LEFT JOIN ref.ProductCategory pc ON pc.ProductCategoryCode = m.Category
    GROUP BY m.Category, m.CurrencyCode
    ORDER BY COUNT(*) DESC, m.Category;
END;
GO

/* The user's concepts (not dropped), latest change first, each with its estimated FOB (cost.fn_Estimate) for comparing with its target price. */
CREATE OR ALTER PROCEDURE cost.usp_Costing_MyConcepts
    @UserName nvarchar(64),
    @SmallQty int = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (20) k.ConceptId, k.Name, k.ProductCategoryCode, CategoryName = pc.Name, CustomerCode = c.PartnerCode, k.TargetPrice,
           e.EstimatedFob, e.CurrencyCode, e.Peers
    FROM concept.Concept k
    LEFT JOIN partner.Partner c ON c.PartnerId = k.CustomerId
    LEFT JOIN ref.ProductCategory pc ON pc.ProductCategoryCode = k.ProductCategoryCode
    OUTER APPLY cost.fn_Estimate(k.ProductCategoryCode, c.PartnerCode, @SmallQty) e
    WHERE k.OwnerName = @UserName AND k.Status <> 'Dropped'
    ORDER BY ISNULL(k.UpdatedUtc, k.CreatedUtc) DESC;
END;
GO

/*
    Costing overview (Admin and Costing). Result sets:
      1. data health: last import, styles with IO prices and active styles, IOs with no finished costing, final costings waiting on main
         fabric, small IOs, plants on IOs with no rate in force;
      2. net balance by garment (IOs from @SmallQty pieces, weighted by quantity);
      3. IOs whose net balance share is @NetGap points or more below their style's (top 10);
      4. quote counts in open packages (not Ordered or Closed): styles without a quote, quotes outside the client's price band, quotes from an estimate;
      5. those styles (top 15).
*/
CREATE OR ALTER PROCEDURE cost.usp_Costing_Overview
    @SmallQty int = 50,
    @NetGap   decimal(9, 2) = 5
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @today date = CAST(SYSUTCDATETIME() AS date);

    SELECT LastImportUtc = li.ImportedUtc, LastImportFile = li.FileName, LastImportBy = li.ImportedBy, LastImportUnmatched = li.Unmatched,
           StylesWithPrices = (SELECT COUNT(DISTINCT i.StyleId) FROM cost.Io i JOIN cost.fn_IoPrice() x ON x.IoId = i.IoId),
           ActiveStyles = (SELECT COUNT(*) FROM style.Style WHERE IsActive = 1),
           Ios = (SELECT COUNT(*) FROM cost.Io),
           IosUnfinished = (SELECT COUNT(*) FROM cost.Io i WHERE NOT EXISTS (SELECT 1 FROM cost.IoCosting k WHERE k.IoId = i.IoId AND k.Fob > 0)),
           FinalPending = (SELECT COUNT(*) FROM cost.fn_IoPrice() x WHERE x.FinalPending = 1),
           SmallIos = (SELECT COUNT(*) FROM cost.Io WHERE ISNULL(OrderQty, 0) < @SmallQty),
           PlantsWithoutRate = (SELECT COUNT(DISTINCT i.PlantCode) FROM cost.Io i
                                WHERE i.PlantCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM cost.fn_PlantRate(i.PlantCode, @today, NULL, NULL))),
           PlantsWithoutRateList = (SELECT STRING_AGG(PlantCode, N', ') FROM (SELECT DISTINCT i.PlantCode FROM cost.Io i
                                    WHERE i.PlantCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM cost.fn_PlantRate(i.PlantCode, @today, NULL, NULL))) z)
    FROM (SELECT 1 AS One) one
    OUTER APPLY (SELECT TOP (1) ImportedUtc, FileName, ImportedBy, Unmatched FROM cost.Import ORDER BY ImportId DESC) li;

    ;WITH io AS
    (
        SELECT i.IoId, i.IoNo, i.StyleId, s.StyleNo, x.CostDate, x.Fob, x.NetBalance, Q = CAST(i.OrderQty AS decimal(18, 5)), x.CurrencyCode,
               Category = ISNULL(style.fn_GarmentCategory(pt.Name), style.fn_GarmentCategory(pt.ProductTypeCode))
        FROM cost.Io i
        JOIN cost.fn_IoPrice() x ON x.IoId = i.IoId
        JOIN style.Style s ON s.StyleId = i.StyleId
        LEFT JOIN ref.ProductType pt ON pt.ProductTypeCode = s.ProductTypeCode
        WHERE ISNULL(i.OrderQty, 0) >= @SmallQty AND x.NetBalance IS NOT NULL
    )
    SELECT Category = ISNULL(MAX(pc.Name), ISNULL(io.Category, N'-')), io.CurrencyCode, Styles = COUNT(DISTINCT io.StyleId), Ios = COUNT(*),
           Qty = CAST(SUM(io.Q) AS bigint), Value = CAST(SUM(io.Fob * io.Q) AS decimal(18, 2)),
           NetPct = CAST(100 * SUM(io.NetBalance * io.Q) / NULLIF(SUM(io.Fob * io.Q), 0) AS decimal(9, 2))
    FROM io LEFT JOIN ref.ProductCategory pc ON pc.ProductCategoryCode = io.Category
    GROUP BY io.Category, io.CurrencyCode
    ORDER BY SUM(io.Q) DESC;

    ;WITH io AS
    (
        SELECT i.IoNo, i.StyleId, s.StyleNo, x.CostDate, x.Fob, x.NetBalance, Q = CAST(i.OrderQty AS decimal(18, 5)), x.CurrencyCode
        FROM cost.Io i
        JOIN cost.fn_IoPrice() x ON x.IoId = i.IoId
        JOIN style.Style s ON s.StyleId = i.StyleId
        WHERE ISNULL(i.OrderQty, 0) >= @SmallQty AND x.NetBalance IS NOT NULL AND x.Fob > 0
    ),
    st AS
    (
        SELECT io.*, IoPct = 100 * io.NetBalance / io.Fob,
               StylePct = 100 * SUM(io.NetBalance * io.Q) OVER (PARTITION BY io.StyleId, io.CurrencyCode) / NULLIF(SUM(io.Fob * io.Q) OVER (PARTITION BY io.StyleId, io.CurrencyCode), 0),
               StyleIos = COUNT(*) OVER (PARTITION BY io.StyleId, io.CurrencyCode)
        FROM io
    )
    SELECT TOP (10) StyleId, StyleNo, IoNo, CostDate, Fob, CurrencyCode, IoPct = CAST(IoPct AS decimal(9, 2)), StylePct = CAST(StylePct AS decimal(9, 2))
    FROM st
    WHERE StyleIos > 1 AND StylePct - IoPct >= @NetGap
    ORDER BY StylePct - IoPct DESC;

    ;WITH items AS
    (
        SELECT p.PackageId, PackageName = p.Name, i.PackageItemId, s.StyleNo, cp.PriceMin, cp.PriceMax, BandCurrency = cp.Currency,
               q.Fob, q.CurrencyCode, q.BasisKind
        FROM pack.Package p
        JOIN pack.PackageItem i ON i.PackageId = p.PackageId
        JOIN style.Style s ON s.StyleId = i.StyleId
        JOIN partner.Partner c ON c.PartnerId = p.CustomerId
        LEFT JOIN pack.ClientProfile cp ON cp.ClientKey = pack.fn_ClientKey(c.PartnerCode)
        OUTER APPLY (SELECT TOP (1) Fob, CurrencyCode, BasisKind FROM pack.PackageItemQuote x WHERE x.PackageItemId = i.PackageItemId ORDER BY x.Version DESC) q
        WHERE p.Status NOT IN ('Ordered', 'Closed')
    ),
    k AS
    (
        SELECT *, Kind = CASE WHEN Fob IS NULL THEN 'NoQuote'
                              WHEN CurrencyCode = BandCurrency AND (Fob < PriceMin OR Fob > PriceMax) THEN 'OutOfBand'
                              WHEN BasisKind = 'Estimate' THEN 'Estimate' END
        FROM items
    )
    SELECT NoQuote = COUNT(CASE WHEN Kind = 'NoQuote' THEN 1 END), OutOfBand = COUNT(CASE WHEN Kind = 'OutOfBand' THEN 1 END),
           Estimated = COUNT(CASE WHEN Kind = 'Estimate' THEN 1 END), Packages = COUNT(DISTINCT PackageId)
    FROM k;

    ;WITH items AS
    (
        SELECT p.PackageId, PackageName = p.Name, s.StyleNo, cp.PriceMin, cp.PriceMax, BandCurrency = cp.Currency, q.Fob, q.CurrencyCode, q.BasisKind, Changed = ISNULL(p.UpdatedUtc, p.CreatedUtc)
        FROM pack.Package p
        JOIN pack.PackageItem i ON i.PackageId = p.PackageId
        JOIN style.Style s ON s.StyleId = i.StyleId
        JOIN partner.Partner c ON c.PartnerId = p.CustomerId
        LEFT JOIN pack.ClientProfile cp ON cp.ClientKey = pack.fn_ClientKey(c.PartnerCode)
        OUTER APPLY (SELECT TOP (1) Fob, CurrencyCode, BasisKind FROM pack.PackageItemQuote x WHERE x.PackageItemId = i.PackageItemId ORDER BY x.Version DESC) q
        WHERE p.Status NOT IN ('Ordered', 'Closed')
    ),
    k AS
    (
        SELECT *, Kind = CASE WHEN Fob IS NULL THEN 'NoQuote'
                              WHEN CurrencyCode = BandCurrency AND (Fob < PriceMin OR Fob > PriceMax) THEN 'OutOfBand'
                              WHEN BasisKind = 'Estimate' THEN 'Estimate' END
        FROM items
    )
    SELECT TOP (15) PackageId, PackageName, StyleNo, Kind, Fob, CurrencyCode, PriceMin, PriceMax
    FROM k WHERE Kind IS NOT NULL
    ORDER BY CASE Kind WHEN 'OutOfBand' THEN 0 WHEN 'NoQuote' THEN 1 ELSE 2 END, Changed DESC, StyleNo;
END;
GO
