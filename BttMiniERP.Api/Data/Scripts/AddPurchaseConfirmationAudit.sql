BEGIN TRANSACTION;
ALTER TABLE [PurchaseReceipts] ADD [ConfirmedAt] datetimeoffset NULL;

ALTER TABLE [PurchaseReceipts] ADD [ConfirmedByUserId] nvarchar(128) NULL;

CREATE INDEX [IX_PurchaseReceipts_ConfirmedByUserId] ON [PurchaseReceipts] ([ConfirmedByUserId]);

ALTER TABLE [PurchaseReceipts] ADD CONSTRAINT [FK_PurchaseReceipts_AspNetUsers_ConfirmedByUserId] FOREIGN KEY ([ConfirmedByUserId]) REFERENCES [AspNetUsers] ([Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261116112725_AddPurchaseConfirmationAudit', N'10.0.13');

COMMIT;
GO
