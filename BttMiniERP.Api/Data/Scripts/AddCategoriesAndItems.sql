BEGIN TRANSACTION;
CREATE TABLE [Categories] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(128) NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    [UpdatedByUserId] nvarchar(128) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Categories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Categories_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_Categories_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id])
);

CREATE TABLE [Items] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [CategoryId] int NOT NULL,
    [Unit] nvarchar(20) NOT NULL,
    [ItemType] int NOT NULL,
    [PurchasePrice] decimal(18,2) NOT NULL,
    [SalePrice] decimal(18,2) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(128) NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    [UpdatedByUserId] nvarchar(128) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Items] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Items_ItemType] CHECK ([ItemType] IN (0, 1)),
    CONSTRAINT [CK_Items_PurchasePrice] CHECK ([PurchasePrice] >= 0),
    CONSTRAINT [CK_Items_SalePrice] CHECK ([SalePrice] >= 0),
    CONSTRAINT [FK_Items_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_Items_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_Items_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id])
);

CREATE UNIQUE INDEX [IX_Categories_Code] ON [Categories] ([Code]);

CREATE INDEX [IX_Categories_CreatedByUserId] ON [Categories] ([CreatedByUserId]);

CREATE INDEX [IX_Categories_UpdatedByUserId] ON [Categories] ([UpdatedByUserId]);

CREATE INDEX [IX_Items_CategoryId] ON [Items] ([CategoryId]);

CREATE UNIQUE INDEX [IX_Items_Code] ON [Items] ([Code]);

CREATE INDEX [IX_Items_CreatedByUserId] ON [Items] ([CreatedByUserId]);

CREATE INDEX [IX_Items_UpdatedByUserId] ON [Items] ([UpdatedByUserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108141123_AddCategoriesAndItems', N'10.0.13');

COMMIT;
GO
