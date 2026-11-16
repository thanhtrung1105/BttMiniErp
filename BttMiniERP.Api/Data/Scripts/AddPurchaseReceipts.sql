BEGIN TRANSACTION;
CREATE TABLE [PurchaseReceipts] (
    [Id] int NOT NULL IDENTITY,
    [ReceiptNumber] nvarchar(40) NOT NULL,
    [SupplierId] int NOT NULL,
    [ReceiptDate] datetimeoffset NOT NULL,
    [Note] nvarchar(1000) NULL,
    [Status] int NOT NULL,
    [TotalAmount] decimal(18,2) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(128) NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    [UpdatedByUserId] nvarchar(128) NULL,
    [CancelledAt] datetimeoffset NULL,
    [CancelledByUserId] nvarchar(128) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_PurchaseReceipts] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_PurchaseReceipts_Status] CHECK ([Status] IN (0,1,2)),
    CONSTRAINT [CK_PurchaseReceipts_Total] CHECK ([TotalAmount] >= 0),
    CONSTRAINT [FK_PurchaseReceipts_AspNetUsers_CancelledByUserId] FOREIGN KEY ([CancelledByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_PurchaseReceipts_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_PurchaseReceipts_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_PurchaseReceipts_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id])
);

CREATE TABLE [PurchaseReceiptLines] (
    [Id] int NOT NULL IDENTITY,
    [PurchaseReceiptId] int NOT NULL,
    [LineNumber] int NOT NULL,
    [ItemId] int NOT NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [UnitPrice] decimal(18,2) NOT NULL,
    [LineTotal] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_PurchaseReceiptLines] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_PurchaseReceiptLines_LineNumber] CHECK ([LineNumber] > 0),
    CONSTRAINT [CK_PurchaseReceiptLines_LineTotal] CHECK ([LineTotal] >= 0),
    CONSTRAINT [CK_PurchaseReceiptLines_Quantity] CHECK ([Quantity] > 0),
    CONSTRAINT [CK_PurchaseReceiptLines_UnitPrice] CHECK ([UnitPrice] >= 0),
    CONSTRAINT [FK_PurchaseReceiptLines_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [Items] ([Id]),
    CONSTRAINT [FK_PurchaseReceiptLines_PurchaseReceipts_PurchaseReceiptId] FOREIGN KEY ([PurchaseReceiptId]) REFERENCES [PurchaseReceipts] ([Id])
);

CREATE INDEX [IX_PurchaseReceiptLines_ItemId] ON [PurchaseReceiptLines] ([ItemId]);

CREATE UNIQUE INDEX [IX_PurchaseReceiptLines_PurchaseReceiptId_ItemId] ON [PurchaseReceiptLines] ([PurchaseReceiptId], [ItemId]);

CREATE UNIQUE INDEX [IX_PurchaseReceiptLines_PurchaseReceiptId_LineNumber] ON [PurchaseReceiptLines] ([PurchaseReceiptId], [LineNumber]);

CREATE INDEX [IX_PurchaseReceipts_CancelledByUserId] ON [PurchaseReceipts] ([CancelledByUserId]);

CREATE INDEX [IX_PurchaseReceipts_CreatedByUserId] ON [PurchaseReceipts] ([CreatedByUserId]);

CREATE INDEX [IX_PurchaseReceipts_ReceiptDate_Id] ON [PurchaseReceipts] ([ReceiptDate], [Id]);

CREATE UNIQUE INDEX [IX_PurchaseReceipts_ReceiptNumber] ON [PurchaseReceipts] ([ReceiptNumber]);

CREATE INDEX [IX_PurchaseReceipts_Status_ReceiptDate_Id] ON [PurchaseReceipts] ([Status], [ReceiptDate], [Id]);

CREATE INDEX [IX_PurchaseReceipts_SupplierId] ON [PurchaseReceipts] ([SupplierId]);

CREATE INDEX [IX_PurchaseReceipts_UpdatedByUserId] ON [PurchaseReceipts] ([UpdatedByUserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261116054509_AddPurchaseReceipts', N'10.0.13');

COMMIT;
GO
