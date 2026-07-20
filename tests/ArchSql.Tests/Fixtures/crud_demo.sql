CREATE TABLE [dbo].[Orders] (
    [OrderId] INT IDENTITY(1,1) NOT NULL,
    [Total] DECIMAL(18,2) NOT NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([OrderId])
);
GO

CREATE TABLE [dbo].[OrderLine] (
    [OrderLineId] INT IDENTITY(1,1) NOT NULL,
    [OrderId] INT NOT NULL,
    [Qty] INT NOT NULL,
    CONSTRAINT [PK_OrderLine] PRIMARY KEY ([OrderLineId])
);
GO

CREATE PROCEDURE [dbo].[usp_PlaceOrder]
    @OrderId INT
AS
BEGIN
    INSERT INTO [dbo].[Orders] (Total) VALUES (0);
    UPDATE o SET o.Total = o.Total + 1 FROM [dbo].[Orders] AS o WHERE o.OrderId = @OrderId;
END
GO

CREATE PROCEDURE [dbo].[usp_MergeOrderLine]
AS
BEGIN
    MERGE [dbo].[OrderLine] AS target
    USING (SELECT 1 AS OrderId, 2 AS Qty) AS src
    ON target.OrderId = src.OrderId
    WHEN MATCHED THEN UPDATE SET Qty = src.Qty
    WHEN NOT MATCHED THEN INSERT (OrderId, Qty) VALUES (src.OrderId, src.Qty);
END
GO

CREATE PROCEDURE [dbo].[usp_DynamicReport]
    @TableName NVARCHAR(100)
AS
BEGIN
    EXEC('SELECT * FROM ' + @TableName);
END
GO

CREATE PROCEDURE [dbo].[usp_UsesTemp]
AS
BEGIN
    CREATE TABLE #tmp (Id INT);
    INSERT INTO #tmp (Id) VALUES (1);
END
GO
