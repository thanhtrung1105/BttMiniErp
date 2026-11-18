BEGIN TRANSACTION;
CREATE TABLE [SalesOrders] (
    [Id] int NOT NULL IDENTITY,
    [OrderNumber] nvarchar(40) NOT NULL,
    [CustomerId] int NOT NULL,
    [OrderDate] datetimeoffset NOT NULL,
    [Note] nvarchar(1000) NULL,
    [Status] int NOT NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(128) NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    [UpdatedByUserId] nvarchar(128) NULL,
    [CancelledAt] datetimeoffset NULL,
    [CancelledByUserId] nvarchar(128) NULL,
    [ConfirmedAt] datetimeoffset NULL,
    [ConfirmedByUserId] nvarchar(128) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_SalesOrders] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SalesOrders_Status] CHECK ([Status] IN (0,1,2)),
    CONSTRAINT [CK_SalesOrders_Total] CHECK ([TotalAmount] >= 0),
    CONSTRAINT [FK_SalesOrders_AspNetUsers_CancelledByUserId] FOREIGN KEY ([CancelledByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_SalesOrders_AspNetUsers_ConfirmedByUserId] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_SalesOrders_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_SalesOrders_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_SalesOrders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id])
);

CREATE TABLE [SalesOrderLines] (
    [Id] int NOT NULL IDENTITY,
    [SalesOrderId] int NOT NULL,
    [LineNumber] int NOT NULL,
    [ItemId] int NOT NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [UnitPrice] decimal(18,2) NOT NULL,
    [LineTotal] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_SalesOrderLines] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_SalesOrderLines_LineNumber] CHECK ([LineNumber] > 0),
    CONSTRAINT [CK_SalesOrderLines_LineTotal] CHECK ([LineTotal] >= 0),
    CONSTRAINT [CK_SalesOrderLines_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_SalesOrderLines_UnitPrice] CHECK ([UnitPrice] >= 0),
    CONSTRAINT [FK_SalesOrderLines_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [Items] ([Id]),
    CONSTRAINT [FK_SalesOrderLines_SalesOrders_SalesOrderId] FOREIGN KEY ([SalesOrderId]) REFERENCES [SalesOrders] ([Id])
);

CREATE INDEX [IX_SalesOrderLines_ItemId] ON [SalesOrderLines] ([ItemId]);

CREATE UNIQUE INDEX [IX_SalesOrderLines_SalesOrderId_ItemId] ON [SalesOrderLines] ([SalesOrderId], [ItemId]);

CREATE UNIQUE INDEX [IX_SalesOrderLines_SalesOrderId_LineNumber] ON [SalesOrderLines] ([SalesOrderId], [LineNumber]);

CREATE INDEX [IX_SalesOrders_CancelledByUserId] ON [SalesOrders] ([CancelledByUserId]);

CREATE INDEX [IX_SalesOrders_ConfirmedByUserId] ON [SalesOrders] ([ConfirmedByUserId]);

CREATE INDEX [IX_SalesOrders_CreatedByUserId] ON [SalesOrders] ([CreatedByUserId]);

CREATE INDEX [IX_SalesOrders_CustomerId] ON [SalesOrders] ([CustomerId]);

CREATE INDEX [IX_SalesOrders_OrderDate_Id] ON [SalesOrders] ([OrderDate], [Id]);

CREATE UNIQUE INDEX [IX_SalesOrders_OrderNumber] ON [SalesOrders] ([OrderNumber]);

CREATE INDEX [IX_SalesOrders_Status_OrderDate_Id] ON [SalesOrders] ([Status], [OrderDate], [Id]);

CREATE INDEX [IX_SalesOrders_UpdatedByUserId] ON [SalesOrders] ([UpdatedByUserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261118194736_AddSalesOrders', N'10.0.13');

COMMIT;
GO
