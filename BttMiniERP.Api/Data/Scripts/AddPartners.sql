BEGIN TRANSACTION;
CREATE TABLE [Customers] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Phone] nvarchar(30) NULL,
    [Email] nvarchar(254) NULL,
    [Address] nvarchar(500) NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(128) NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    [UpdatedByUserId] nvarchar(128) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customers_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_Customers_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id])
);

CREATE TABLE [Suppliers] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Phone] nvarchar(30) NULL,
    [Email] nvarchar(254) NULL,
    [Address] nvarchar(500) NULL,
    [TaxCode] nvarchar(50) NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [CreatedByUserId] nvarchar(128) NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    [UpdatedByUserId] nvarchar(128) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_Suppliers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Suppliers_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
    CONSTRAINT [FK_Suppliers_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id])
);

CREATE UNIQUE INDEX [IX_Customers_Code] ON [Customers] ([Code]);

CREATE INDEX [IX_Customers_CreatedByUserId] ON [Customers] ([CreatedByUserId]);

CREATE INDEX [IX_Customers_Name] ON [Customers] ([Name]);

CREATE INDEX [IX_Customers_UpdatedByUserId] ON [Customers] ([UpdatedByUserId]);

CREATE UNIQUE INDEX [IX_Suppliers_Code] ON [Suppliers] ([Code]);

CREATE INDEX [IX_Suppliers_CreatedByUserId] ON [Suppliers] ([CreatedByUserId]);

CREATE INDEX [IX_Suppliers_Name] ON [Suppliers] ([Name]);

CREATE INDEX [IX_Suppliers_UpdatedByUserId] ON [Suppliers] ([UpdatedByUserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261109110025_AddPartners', N'10.0.13');

COMMIT;
GO
