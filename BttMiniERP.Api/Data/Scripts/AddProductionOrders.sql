BEGIN TRANSACTION;
CREATE TABLE [ProductionOrders] (
    [Id] int NOT NULL IDENTITY,
    [OrderNumber] nvarchar(40) NOT NULL,
    [BomId] int NOT NULL,
    [BomVersionSnapshot] int NOT NULL,
    [FinishedProductId] int NOT NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [Note] nvarchar(1000) NULL,
    [Status] int NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(128) NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    [UpdatedByUserId] nvarchar(128) NULL,
    [CancelledAt] datetimeoffset NULL,
    [CancelledByUserId] nvarchar(128) NULL,
    [ConfirmedAt] datetimeoffset NULL,
    [ConfirmedByUserId] nvarchar(128) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_ProductionOrders] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ProductionOrders_BomVersion] CHECK ([BomVersionSnapshot] >= 1),
    CONSTRAINT [CK_ProductionOrders_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_ProductionOrders_Status] CHECK ([Status] IN (0,1,2)),
    CONSTRAINT [FK_ProductionOrders_AspNetUsers_CancelledByUserId] FOREIGN KEY ([CancelledByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_ProductionOrders_AspNetUsers_ConfirmedByUserId] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_ProductionOrders_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_ProductionOrders_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_ProductionOrders_BillOfMaterials_BomId] FOREIGN KEY ([BomId]) REFERENCES [BillOfMaterials] ([Id]),
    CONSTRAINT [FK_ProductionOrders_Items_FinishedProductId] FOREIGN KEY ([FinishedProductId]) REFERENCES [Items] ([Id])
);

CREATE TABLE [ProductionOrderMaterials] (
    [Id] int NOT NULL IDENTITY,
    [ProductionOrderId] int NOT NULL,
    [RawMaterialId] int NOT NULL,
    [QuantityPerUnitSnapshot] decimal(18,3) NOT NULL,
    [RequiredQuantity] decimal(18,3) NOT NULL,
    [ActualQuantity] decimal(18,3) NULL,
    CONSTRAINT [PK_ProductionOrderMaterials] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ProductionOrderMaterials_Actual] CHECK ([ActualQuantity] IS NULL OR [ActualQuantity] > 0),
    CONSTRAINT [CK_ProductionOrderMaterials_PerUnit] CHECK ([QuantityPerUnitSnapshot] > 0),
    CONSTRAINT [CK_ProductionOrderMaterials_Required] CHECK ([RequiredQuantity] > 0),
    CONSTRAINT [FK_ProductionOrderMaterials_Items_RawMaterialId] FOREIGN KEY ([RawMaterialId]) REFERENCES [Items] ([Id]),
    CONSTRAINT [FK_ProductionOrderMaterials_ProductionOrders_ProductionOrderId] FOREIGN KEY ([ProductionOrderId]) REFERENCES [ProductionOrders] ([Id])
);

CREATE UNIQUE INDEX [IX_ProductionOrderMaterials_ProductionOrderId_RawMaterialId] ON [ProductionOrderMaterials] ([ProductionOrderId], [RawMaterialId]);

CREATE INDEX [IX_ProductionOrderMaterials_RawMaterialId] ON [ProductionOrderMaterials] ([RawMaterialId]);

CREATE INDEX [IX_ProductionOrders_BomId] ON [ProductionOrders] ([BomId]);

CREATE INDEX [IX_ProductionOrders_CancelledByUserId] ON [ProductionOrders] ([CancelledByUserId]);

CREATE INDEX [IX_ProductionOrders_ConfirmedByUserId] ON [ProductionOrders] ([ConfirmedByUserId]);

CREATE INDEX [IX_ProductionOrders_CreatedAt_Id] ON [ProductionOrders] ([CreatedAt], [Id]);

CREATE INDEX [IX_ProductionOrders_CreatedByUserId] ON [ProductionOrders] ([CreatedByUserId]);

CREATE INDEX [IX_ProductionOrders_FinishedProductId] ON [ProductionOrders] ([FinishedProductId]);

CREATE UNIQUE INDEX [IX_ProductionOrders_OrderNumber] ON [ProductionOrders] ([OrderNumber]);

CREATE INDEX [IX_ProductionOrders_Status_CreatedAt_Id] ON [ProductionOrders] ([Status], [CreatedAt], [Id]);

CREATE INDEX [IX_ProductionOrders_UpdatedByUserId] ON [ProductionOrders] ([UpdatedByUserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261118152743_AddProductionOrders', N'10.0.13');

COMMIT;
GO
