using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BttMiniERP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseConfirmationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                table: "PurchaseReceipts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedByUserId",
                table: "PurchaseReceipts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_ConfirmedByUserId",
                table: "PurchaseReceipts",
                column: "ConfirmedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReceipts_AspNetUsers_ConfirmedByUserId",
                table: "PurchaseReceipts",
                column: "ConfirmedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReceipts_AspNetUsers_ConfirmedByUserId",
                table: "PurchaseReceipts");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceipts_ConfirmedByUserId",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "PurchaseReceipts");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "PurchaseReceipts");
        }
    }
}
