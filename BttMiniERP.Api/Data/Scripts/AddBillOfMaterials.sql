BEGIN TRANSACTION;
CREATE TABLE [BillOfMaterials] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [FinishedProductId] int NOT NULL,
    [Version] int NOT NULL,
    [IsActive] bit NOT NULL,
    [Note] nvarchar(1000) NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(128) NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    [UpdatedByUserId] nvarchar(128) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_BillOfMaterials] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_BillOfMaterials_Version] CHECK ([Version] >= 1),
    CONSTRAINT [FK_BillOfMaterials_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_BillOfMaterials_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_BillOfMaterials_Items_FinishedProductId] FOREIGN KEY ([FinishedProductId]) REFERENCES [Items] ([Id])
);

CREATE TABLE [BillOfMaterialDetails] (
    [Id] int NOT NULL IDENTITY,
    [BillOfMaterialId] int NOT NULL,
    [RawMaterialId] int NOT NULL,
    [QuantityPerUnit] decimal(18,3) NOT NULL,
    CONSTRAINT [PK_BillOfMaterialDetails] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_BillOfMaterialDetails_QuantityPerUnit] CHECK ([QuantityPerUnit] > 0),
    CONSTRAINT [FK_BillOfMaterialDetails_BillOfMaterials_BillOfMaterialId] FOREIGN KEY ([BillOfMaterialId]) REFERENCES [BillOfMaterials] ([Id]),
    CONSTRAINT [FK_BillOfMaterialDetails_Items_RawMaterialId] FOREIGN KEY ([RawMaterialId]) REFERENCES [Items] ([Id])
);

CREATE UNIQUE INDEX [IX_BillOfMaterialDetails_BillOfMaterialId_RawMaterialId] ON [BillOfMaterialDetails] ([BillOfMaterialId], [RawMaterialId]);

CREATE INDEX [IX_BillOfMaterialDetails_RawMaterialId] ON [BillOfMaterialDetails] ([RawMaterialId]);

CREATE UNIQUE INDEX [IX_BillOfMaterials_Code] ON [BillOfMaterials] ([Code]);

CREATE INDEX [IX_BillOfMaterials_CreatedByUserId] ON [BillOfMaterials] ([CreatedByUserId]);

CREATE UNIQUE INDEX [IX_BillOfMaterials_FinishedProductId] ON [BillOfMaterials] ([FinishedProductId]);

CREATE INDEX [IX_BillOfMaterials_UpdatedByUserId] ON [BillOfMaterials] ([UpdatedByUserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261116183941_AddBillOfMaterials', N'10.0.13');

COMMIT;
GO
