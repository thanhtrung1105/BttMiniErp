BEGIN TRANSACTION;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
IF @@TRANCOUNT = 0
    THROW 51000, 'Identity key migration requires a transaction.', 1;
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
    THROW 51001, 'Missing Identity migration baseline.', 1;
IF (SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory]) <> 1
    OR NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261103102340_CreateIdentitySchema')
    OR EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] NOT IN
        (N'20261103102340_CreateIdentitySchema', N'20261103140045_LimitIdentityKeyLengths'))
    THROW 51001, 'Unexpected migration baseline.', 1;
IF EXISTS (
    SELECT 1 FROM (VALUES
        (N'AspNetUsers', N'Id'), (N'AspNetRoles', N'Id'),
        (N'AspNetUserClaims', N'UserId'), (N'AspNetRoleClaims', N'RoleId'),
        (N'AspNetUserRoles', N'UserId'), (N'AspNetUserRoles', N'RoleId'),
        (N'AspNetUserLogins', N'UserId'), (N'AspNetUserLogins', N'LoginProvider'), (N'AspNetUserLogins', N'ProviderKey'),
        (N'AspNetUserTokens', N'UserId'), (N'AspNetUserTokens', N'LoginProvider'), (N'AspNetUserTokens', N'Name')
    ) AS expected(TableName, ColumnName)
    LEFT JOIN sys.columns AS c ON c.object_id = OBJECT_ID(N'[dbo].[' + expected.TableName + N']', N'U')
        AND c.name = expected.ColumnName
    WHERE c.column_id IS NULL OR c.system_type_id <> TYPE_ID(N'nvarchar')
        OR c.max_length <> 900 OR c.is_nullable <> 0)
    THROW 51003, 'Unexpected Identity column schema.', 1;

IF EXISTS (SELECT 1 FROM [AspNetUsers] WITH (TABLOCKX, HOLDLOCK) WHERE DATALENGTH([Id]) > 256)
    THROW 51002, 'AspNetUsers: Identity key exceeds 128 UTF-16 code units. No data changed.', 1;
IF EXISTS (SELECT 1 FROM [AspNetRoles] WITH (TABLOCKX, HOLDLOCK) WHERE DATALENGTH([Id]) > 256)
    THROW 51002, 'AspNetRoles: Identity key exceeds 128 UTF-16 code units. No data changed.', 1;
IF EXISTS (SELECT 1 FROM [AspNetUserClaims] WITH (TABLOCKX, HOLDLOCK) WHERE DATALENGTH([UserId]) > 256)
    THROW 51002, 'AspNetUserClaims: Identity key exceeds 128 UTF-16 code units. No data changed.', 1;
IF EXISTS (SELECT 1 FROM [AspNetRoleClaims] WITH (TABLOCKX, HOLDLOCK) WHERE DATALENGTH([RoleId]) > 256)
    THROW 51002, 'AspNetRoleClaims: Identity key exceeds 128 UTF-16 code units. No data changed.', 1;
IF EXISTS (SELECT 1 FROM [AspNetUserRoles] WITH (TABLOCKX, HOLDLOCK) WHERE DATALENGTH([UserId]) > 256 OR DATALENGTH([RoleId]) > 256)
    THROW 51002, 'AspNetUserRoles: Identity key exceeds 128 UTF-16 code units. No data changed.', 1;
IF EXISTS (SELECT 1 FROM [AspNetUserLogins] WITH (TABLOCKX, HOLDLOCK) WHERE DATALENGTH([UserId]) > 256 OR DATALENGTH([LoginProvider]) > 256 OR DATALENGTH([ProviderKey]) > 256)
    THROW 51002, 'AspNetUserLogins: Identity key exceeds 128 UTF-16 code units. No data changed.', 1;
IF EXISTS (SELECT 1 FROM [AspNetUserTokens] WITH (TABLOCKX, HOLDLOCK) WHERE DATALENGTH([UserId]) > 256 OR DATALENGTH([LoginProvider]) > 256 OR DATALENGTH([Name]) > 256)
    THROW 51002, 'AspNetUserTokens: Identity key exceeds 128 UTF-16 code units. No data changed.', 1;

ALTER TABLE [AspNetUserClaims] DROP CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId];

ALTER TABLE [AspNetUserLogins] DROP CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId];

ALTER TABLE [AspNetUserTokens] DROP CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId];

ALTER TABLE [AspNetUserRoles] DROP CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId];

ALTER TABLE [AspNetUserRoles] DROP CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId];

ALTER TABLE [AspNetRoleClaims] DROP CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId];

ALTER TABLE [AspNetUsers] DROP CONSTRAINT [PK_AspNetUsers];

ALTER TABLE [AspNetRoles] DROP CONSTRAINT [PK_AspNetRoles];

ALTER TABLE [AspNetUserLogins] DROP CONSTRAINT [PK_AspNetUserLogins];

ALTER TABLE [AspNetUserRoles] DROP CONSTRAINT [PK_AspNetUserRoles];

ALTER TABLE [AspNetUserTokens] DROP CONSTRAINT [PK_AspNetUserTokens];

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUserTokens]') AND [c].[name] = N'Name');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [AspNetUserTokens] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [AspNetUserTokens] ALTER COLUMN [Name] nvarchar(128) NOT NULL;

DECLARE @var1 nvarchar(max);
SELECT @var1 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUserTokens]') AND [c].[name] = N'LoginProvider');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUserTokens] DROP CONSTRAINT ' + @var1 + ';');
ALTER TABLE [AspNetUserTokens] ALTER COLUMN [LoginProvider] nvarchar(128) NOT NULL;

DECLARE @var2 nvarchar(max);
SELECT @var2 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUserTokens]') AND [c].[name] = N'UserId');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUserTokens] DROP CONSTRAINT ' + @var2 + ';');
ALTER TABLE [AspNetUserTokens] ALTER COLUMN [UserId] nvarchar(128) NOT NULL;

DECLARE @var3 nvarchar(max);
SELECT @var3 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUsers]') AND [c].[name] = N'Id');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUsers] DROP CONSTRAINT ' + @var3 + ';');
ALTER TABLE [AspNetUsers] ALTER COLUMN [Id] nvarchar(128) NOT NULL;

DROP INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles];
DECLARE @var4 nvarchar(max);
SELECT @var4 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUserRoles]') AND [c].[name] = N'RoleId');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUserRoles] DROP CONSTRAINT ' + @var4 + ';');
ALTER TABLE [AspNetUserRoles] ALTER COLUMN [RoleId] nvarchar(128) NOT NULL;
CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

DECLARE @var5 nvarchar(max);
SELECT @var5 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUserRoles]') AND [c].[name] = N'UserId');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUserRoles] DROP CONSTRAINT ' + @var5 + ';');
ALTER TABLE [AspNetUserRoles] ALTER COLUMN [UserId] nvarchar(128) NOT NULL;

DROP INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins];
DECLARE @var6 nvarchar(max);
SELECT @var6 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUserLogins]') AND [c].[name] = N'UserId');
IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUserLogins] DROP CONSTRAINT ' + @var6 + ';');
ALTER TABLE [AspNetUserLogins] ALTER COLUMN [UserId] nvarchar(128) NOT NULL;
CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

DECLARE @var7 nvarchar(max);
SELECT @var7 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUserLogins]') AND [c].[name] = N'ProviderKey');
IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUserLogins] DROP CONSTRAINT ' + @var7 + ';');
ALTER TABLE [AspNetUserLogins] ALTER COLUMN [ProviderKey] nvarchar(128) NOT NULL;

DECLARE @var8 nvarchar(max);
SELECT @var8 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUserLogins]') AND [c].[name] = N'LoginProvider');
IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUserLogins] DROP CONSTRAINT ' + @var8 + ';');
ALTER TABLE [AspNetUserLogins] ALTER COLUMN [LoginProvider] nvarchar(128) NOT NULL;

DROP INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims];
DECLARE @var9 nvarchar(max);
SELECT @var9 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUserClaims]') AND [c].[name] = N'UserId');
IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUserClaims] DROP CONSTRAINT ' + @var9 + ';');
ALTER TABLE [AspNetUserClaims] ALTER COLUMN [UserId] nvarchar(128) NOT NULL;
CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

DECLARE @var10 nvarchar(max);
SELECT @var10 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetRoles]') AND [c].[name] = N'Id');
IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [AspNetRoles] DROP CONSTRAINT ' + @var10 + ';');
ALTER TABLE [AspNetRoles] ALTER COLUMN [Id] nvarchar(128) NOT NULL;

DROP INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims];
DECLARE @var11 nvarchar(max);
SELECT @var11 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetRoleClaims]') AND [c].[name] = N'RoleId');
IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [AspNetRoleClaims] DROP CONSTRAINT ' + @var11 + ';');
ALTER TABLE [AspNetRoleClaims] ALTER COLUMN [RoleId] nvarchar(128) NOT NULL;
CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

ALTER TABLE [AspNetUsers] ADD CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id]);

ALTER TABLE [AspNetRoles] ADD CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id]);

ALTER TABLE [AspNetUserLogins] ADD CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]);

ALTER TABLE [AspNetUserRoles] ADD CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]);

ALTER TABLE [AspNetUserTokens] ADD CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]);

ALTER TABLE [AspNetUserClaims] ADD CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]);

ALTER TABLE [AspNetUserLogins] ADD CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]);

ALTER TABLE [AspNetUserTokens] ADD CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]);

ALTER TABLE [AspNetUserRoles] ADD CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]);

ALTER TABLE [AspNetUserRoles] ADD CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]);

ALTER TABLE [AspNetRoleClaims] ADD CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261103140045_LimitIdentityKeyLengths', N'10.0.13');

COMMIT;
GO
