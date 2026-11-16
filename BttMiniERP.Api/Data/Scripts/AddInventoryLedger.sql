BEGIN TRANSACTION;
CREATE TABLE [InventoryBalances] (
    [ItemId] int NOT NULL,
    [QuantityOnHand] decimal(18,3) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(128) NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    [UpdatedByUserId] nvarchar(128) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_InventoryBalances] PRIMARY KEY ([ItemId]),
    CONSTRAINT [CK_InventoryBalances_Quantity] CHECK ([QuantityOnHand] >= 0),
    CONSTRAINT [FK_InventoryBalances_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_InventoryBalances_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_InventoryBalances_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [Items] ([Id])
);

CREATE TABLE [InventoryTransactions] (
    [Id] int NOT NULL IDENTITY,
    [ItemId] int NOT NULL,
    [TransactionType] int NOT NULL,
    [QuantityDelta] decimal(18,3) NOT NULL,
    [BalanceAfter] decimal(18,3) NOT NULL,
    [SourceDocumentType] int NOT NULL,
    [SourceDocumentId] int NOT NULL,
    [SourceLineId] int NOT NULL,
    [PerformedByUserId] nvarchar(128) NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_InventoryTransactions] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_InventoryTransactions_BalanceAfter] CHECK ([BalanceAfter] >= 0),
    CONSTRAINT [CK_InventoryTransactions_Delta] CHECK ([QuantityDelta] <> 0),
    CONSTRAINT [CK_InventoryTransactions_Source] CHECK ([SourceDocumentId] > 0 AND (
    ([TransactionType] = 0 AND [SourceDocumentType] = 0 AND [SourceLineId] > 0 AND [QuantityDelta] > 0) OR
    ([TransactionType] = 1 AND [SourceDocumentType] = 1 AND [SourceLineId] > 0 AND [QuantityDelta] < 0) OR
    ([TransactionType] = 2 AND [SourceDocumentType] = 1 AND [SourceLineId] = 0 AND [QuantityDelta] > 0) OR
    ([TransactionType] = 3 AND [SourceDocumentType] = 2 AND [SourceLineId] > 0 AND [QuantityDelta] < 0))),
    CONSTRAINT [FK_InventoryTransactions_AspNetUsers_PerformedByUserId] FOREIGN KEY ([PerformedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_InventoryTransactions_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [Items] ([Id])
);

CREATE INDEX [IX_InventoryBalances_CreatedByUserId] ON [InventoryBalances] ([CreatedByUserId]);

CREATE INDEX [IX_InventoryBalances_UpdatedByUserId] ON [InventoryBalances] ([UpdatedByUserId]);

CREATE INDEX [IX_InventoryTransactions_ItemId_OccurredAt_Id] ON [InventoryTransactions] ([ItemId], [OccurredAt], [Id]);

CREATE INDEX [IX_InventoryTransactions_PerformedByUserId] ON [InventoryTransactions] ([PerformedByUserId]);

CREATE UNIQUE INDEX [IX_InventoryTransactions_Source] ON [InventoryTransactions] ([SourceDocumentType], [SourceDocumentId], [SourceLineId], [TransactionType]);

INSERT INTO [InventoryBalances] ([ItemId], [QuantityOnHand], [CreatedAt], [CreatedByUserId])
SELECT i.[Id], 0, i.[CreatedAt], i.[CreatedByUserId] FROM [Items] i
WHERE NOT EXISTS (SELECT 1 FROM [InventoryBalances] b WHERE b.[ItemId] = i.[Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261109134430_AddInventoryLedger', N'10.0.13');

COMMIT;
GO
