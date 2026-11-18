using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BttMiniERP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BomId = table.Column<int>(type: "int", nullable: false),
                    BomVersionSnapshot = table.Column<int>(type: "int", nullable: false),
                    FinishedProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrders", x => x.Id);
                    table.CheckConstraint("CK_ProductionOrders_BomVersion", "[BomVersionSnapshot] >= 1");
                    table.CheckConstraint("CK_ProductionOrders_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_ProductionOrders_Status", "[Status] IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_ProductionOrders_AspNetUsers_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductionOrders_AspNetUsers_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductionOrders_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductionOrders_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductionOrders_BillOfMaterials_BomId",
                        column: x => x.BomId,
                        principalTable: "BillOfMaterials",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductionOrders_Items_FinishedProductId",
                        column: x => x.FinishedProductId,
                        principalTable: "Items",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionOrderId = table.Column<int>(type: "int", nullable: false),
                    RawMaterialId = table.Column<int>(type: "int", nullable: false),
                    QuantityPerUnitSnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RequiredQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderMaterials", x => x.Id);
                    table.CheckConstraint("CK_ProductionOrderMaterials_Actual", "[ActualQuantity] IS NULL OR [ActualQuantity] > 0");
                    table.CheckConstraint("CK_ProductionOrderMaterials_PerUnit", "[QuantityPerUnitSnapshot] > 0");
                    table.CheckConstraint("CK_ProductionOrderMaterials_Required", "[RequiredQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_ProductionOrderMaterials_Items_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "Items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductionOrderMaterials_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderMaterials_ProductionOrderId_RawMaterialId",
                table: "ProductionOrderMaterials",
                columns: new[] { "ProductionOrderId", "RawMaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderMaterials_RawMaterialId",
                table: "ProductionOrderMaterials",
                column: "RawMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_BomId",
                table: "ProductionOrders",
                column: "BomId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_CancelledByUserId",
                table: "ProductionOrders",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_ConfirmedByUserId",
                table: "ProductionOrders",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_CreatedAt_Id",
                table: "ProductionOrders",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_CreatedByUserId",
                table: "ProductionOrders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_FinishedProductId",
                table: "ProductionOrders",
                column: "FinishedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_OrderNumber",
                table: "ProductionOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_Status_CreatedAt_Id",
                table: "ProductionOrders",
                columns: new[] { "Status", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_UpdatedByUserId",
                table: "ProductionOrders",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionOrderMaterials");

            migrationBuilder.DropTable(
                name: "ProductionOrders");
        }
    }
}
