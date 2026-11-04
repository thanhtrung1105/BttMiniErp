using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BttMiniERP.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LimitIdentityKeyLengths : Migration
    {
        private static void CheckBaseline(MigrationBuilder migrationBuilder, bool reverting)
        {
            var expectedBytes = reverting ? 256 : 900;
            var expectedHistoryCount = reverting ? 2 : 1;
            migrationBuilder.Sql($"""
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
                IF (SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory]) <> {expectedHistoryCount}
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
                        OR c.max_length <> {expectedBytes} OR c.is_nullable <> 0)
                    THROW 51003, 'Unexpected Identity column schema.', 1;
                """);
        }

        // Chỉ tháo/ràng buộc lại khóa và index, không xóa bảng hoặc bản ghi.
        private static void DropKeyConstraints(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_AspNetUserClaims_AspNetUsers_UserId", table: "AspNetUserClaims");
            migrationBuilder.DropForeignKey(name: "FK_AspNetUserLogins_AspNetUsers_UserId", table: "AspNetUserLogins");
            migrationBuilder.DropForeignKey(name: "FK_AspNetUserTokens_AspNetUsers_UserId", table: "AspNetUserTokens");
            migrationBuilder.DropForeignKey(name: "FK_AspNetUserRoles_AspNetUsers_UserId", table: "AspNetUserRoles");
            migrationBuilder.DropForeignKey(name: "FK_AspNetUserRoles_AspNetRoles_RoleId", table: "AspNetUserRoles");
            migrationBuilder.DropForeignKey(name: "FK_AspNetRoleClaims_AspNetRoles_RoleId", table: "AspNetRoleClaims");
            migrationBuilder.DropPrimaryKey(name: "PK_AspNetUsers", table: "AspNetUsers");
            migrationBuilder.DropPrimaryKey(name: "PK_AspNetRoles", table: "AspNetRoles");
            migrationBuilder.DropPrimaryKey(name: "PK_AspNetUserLogins", table: "AspNetUserLogins");
            migrationBuilder.DropPrimaryKey(name: "PK_AspNetUserRoles", table: "AspNetUserRoles");
            migrationBuilder.DropPrimaryKey(name: "PK_AspNetUserTokens", table: "AspNetUserTokens");
        }

        private static void AddKeyConstraints(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddPrimaryKey(name: "PK_AspNetUsers", table: "AspNetUsers", columns: new[] { "Id" });
            migrationBuilder.AddPrimaryKey(name: "PK_AspNetRoles", table: "AspNetRoles", columns: new[] { "Id" });
            migrationBuilder.AddPrimaryKey(name: "PK_AspNetUserLogins", table: "AspNetUserLogins", columns: new[] { "LoginProvider", "ProviderKey" });
            migrationBuilder.AddPrimaryKey(name: "PK_AspNetUserRoles", table: "AspNetUserRoles", columns: new[] { "UserId", "RoleId" });
            migrationBuilder.AddPrimaryKey(name: "PK_AspNetUserTokens", table: "AspNetUserTokens", columns: new[] { "UserId", "LoginProvider", "Name" });
            migrationBuilder.AddForeignKey(name: "FK_AspNetUserClaims_AspNetUsers_UserId", table: "AspNetUserClaims", column: "UserId", principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
            migrationBuilder.AddForeignKey(name: "FK_AspNetUserLogins_AspNetUsers_UserId", table: "AspNetUserLogins", column: "UserId", principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
            migrationBuilder.AddForeignKey(name: "FK_AspNetUserTokens_AspNetUsers_UserId", table: "AspNetUserTokens", column: "UserId", principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
            migrationBuilder.AddForeignKey(name: "FK_AspNetUserRoles_AspNetUsers_UserId", table: "AspNetUserRoles", column: "UserId", principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
            migrationBuilder.AddForeignKey(name: "FK_AspNetUserRoles_AspNetRoles_RoleId", table: "AspNetUserRoles", column: "RoleId", principalTable: "AspNetRoles", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
            migrationBuilder.AddForeignKey(name: "FK_AspNetRoleClaims_AspNetRoles_RoleId", table: "AspNetRoleClaims", column: "RoleId", principalTable: "AspNetRoles", principalColumn: "Id", onDelete: ReferentialAction.NoAction);
        }
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            CheckBaseline(migrationBuilder, reverting: false);
            // Chặn dữ liệu quá dài trước DDL; giữ khóa bảng đến cuối transaction migration.
            migrationBuilder.Sql("""
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
                """);
            DropKeyConstraints(migrationBuilder);
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AspNetUserTokens",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserTokens",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserTokens",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetUsers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetUserRoles",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserRoles",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserLogins",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                table: "AspNetUserLogins",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserLogins",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserClaims",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetRoles",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetRoleClaims",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
            AddKeyConstraints(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            CheckBaseline(migrationBuilder, reverting: true);
            DropKeyConstraints(migrationBuilder);
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AspNetUserTokens",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserTokens",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserTokens",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetUserRoles",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserRoles",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserLogins",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                table: "AspNetUserLogins",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserLogins",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserClaims",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetRoles",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetRoleClaims",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);
            AddKeyConstraints(migrationBuilder);
        }
    }
}
