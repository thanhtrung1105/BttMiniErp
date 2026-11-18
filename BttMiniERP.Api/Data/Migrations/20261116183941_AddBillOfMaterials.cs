using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BttMiniERP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillOfMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillOfMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FinishedProductId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillOfMaterials", x => x.Id);
                    table.CheckConstraint("CK_BillOfMaterials_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_BillOfMaterials_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BillOfMaterials_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BillOfMaterials_Items_FinishedProductId",
                        column: x => x.FinishedProductId,
                        principalTable: "Items",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BillOfMaterialDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BillOfMaterialId = table.Column<int>(type: "int", nullable: false),
                    RawMaterialId = table.Column<int>(type: "int", nullable: false),
                    QuantityPerUnit = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillOfMaterialDetails", x => x.Id);
                    table.CheckConstraint("CK_BillOfMaterialDetails_QuantityPerUnit", "[QuantityPerUnit] > 0");
                    table.ForeignKey(
                        name: "FK_BillOfMaterialDetails_BillOfMaterials_BillOfMaterialId",
                        column: x => x.BillOfMaterialId,
                        principalTable: "BillOfMaterials",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BillOfMaterialDetails_Items_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "Items",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterialDetails_BillOfMaterialId_RawMaterialId",
                table: "BillOfMaterialDetails",
                columns: new[] { "BillOfMaterialId", "RawMaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterialDetails_RawMaterialId",
                table: "BillOfMaterialDetails",
                column: "RawMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_Code",
                table: "BillOfMaterials",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_CreatedByUserId",
                table: "BillOfMaterials",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_FinishedProductId",
                table: "BillOfMaterials",
                column: "FinishedProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_UpdatedByUserId",
                table: "BillOfMaterials",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillOfMaterialDetails");

            migrationBuilder.DropTable(
                name: "BillOfMaterials");
        }
    }
}
