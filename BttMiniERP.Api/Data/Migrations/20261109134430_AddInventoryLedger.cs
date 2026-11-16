using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BttMiniERP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryBalances",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBalances", x => x.ItemId);
                    table.CheckConstraint("CK_InventoryBalances_Quantity", "[QuantityOnHand] >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryBalances_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryBalances_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryBalances_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    TransactionType = table.Column<int>(type: "int", nullable: false),
                    QuantityDelta = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SourceDocumentType = table.Column<int>(type: "int", nullable: false),
                    SourceDocumentId = table.Column<int>(type: "int", nullable: false),
                    SourceLineId = table.Column<int>(type: "int", nullable: false),
                    PerformedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransactions", x => x.Id);
                    table.CheckConstraint("CK_InventoryTransactions_BalanceAfter", "[BalanceAfter] >= 0");
                    table.CheckConstraint("CK_InventoryTransactions_Delta", "[QuantityDelta] <> 0");
                    table.CheckConstraint("CK_InventoryTransactions_Source", "[SourceDocumentId] > 0 AND (\n    ([TransactionType] = 0 AND [SourceDocumentType] = 0 AND [SourceLineId] > 0 AND [QuantityDelta] > 0) OR\n    ([TransactionType] = 1 AND [SourceDocumentType] = 1 AND [SourceLineId] > 0 AND [QuantityDelta] < 0) OR\n    ([TransactionType] = 2 AND [SourceDocumentType] = 1 AND [SourceLineId] = 0 AND [QuantityDelta] > 0) OR\n    ([TransactionType] = 3 AND [SourceDocumentType] = 2 AND [SourceLineId] > 0 AND [QuantityDelta] < 0))");
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_CreatedByUserId",
                table: "InventoryBalances",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_UpdatedByUserId",
                table: "InventoryBalances",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ItemId_OccurredAt_Id",
                table: "InventoryTransactions",
                columns: new[] { "ItemId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_PerformedByUserId",
                table: "InventoryTransactions",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_Source",
                table: "InventoryTransactions",
                columns: new[] { "SourceDocumentType", "SourceDocumentId", "SourceLineId", "TransactionType" },
                unique: true);
            // Item cũ chưa có nghiệp vụ kho: giữ audit tạo của Item, không tạo ledger/actor giả.
            migrationBuilder.Sql("""
                INSERT INTO [InventoryBalances] ([ItemId], [QuantityOnHand], [CreatedAt], [CreatedByUserId])
                SELECT i.[Id], 0, i.[CreatedAt], i.[CreatedByUserId] FROM [Items] i
                WHERE NOT EXISTS (SELECT 1 FROM [InventoryBalances] b WHERE b.[ItemId] = i.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryBalances");

            migrationBuilder.DropTable(
                name: "InventoryTransactions");
        }
    }
}
