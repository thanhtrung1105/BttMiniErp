using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace BttMiniERP.Tests.Fixtures;

public static class IdentityDatabaseVerifier
{
    public const string InitialMigration = "20261103102340_CreateIdentitySchema";
    public const string KeyMigration = "20261103140045_LimitIdentityKeyLengths";
    private static readonly CreateTableOperation[] Tables = new CreateIdentitySchema().UpOperations.OfType<CreateTableOperation>().ToArray();
    private static string TableFilter => string.Join(",", Tables.Select(t => $"N'{t.Name}'"));

    public static async Task<List<string>> ReadRowsAsync(ApplicationDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
        return rows;
    }

    public static async Task AssertSchemaAsync(ApplicationDbContext context, int length)
    {
        var history = await ReadRowsAsync(context, "SELECT MigrationId + '|' + ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId");
        Assert.Equal(length == 128
            ? new[] { InitialMigration + "|10.0.13", KeyMigration + "|10.0.13" }
            : new[] { InitialMigration + "|10.0.13" }, history);
        var pk = await ReadRowsAsync(context, $"""
            SELECT t.name,i.name,ic.key_ordinal,c.name,i.type_desc,
                CAST(i.is_unique AS int),CAST(i.is_disabled AS int),CAST(ic.is_descending_key AS int)
            FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id AND i.is_primary_key=1
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE SCHEMA_NAME(t.schema_id)='dbo' AND t.name IN ({TableFilter})
            """);
        var expectedPk = Tables.SelectMany(t => t.PrimaryKey!.Columns.Select((c,i) => $"{t.Name}|{t.PrimaryKey.Name}|{i+1}|{c}|CLUSTERED|1|0|0"));
        Assert.Equal(expectedPk.Order(), pk.Order());
        var fk = await ReadRowsAsync(context, $"""
            SELECT t.name,f.name,c.name,p.name,pc.name,f.delete_referential_action_desc,
                f.update_referential_action_desc,CAST(f.is_disabled AS int),CAST(f.is_not_trusted AS int),SCHEMA_NAME(p.schema_id)
            FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id=f.parent_object_id
            JOIN sys.tables p ON p.object_id=f.referenced_object_id
            JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
            JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=fc.parent_column_id
            JOIN sys.columns pc ON pc.object_id=p.object_id AND pc.column_id=fc.referenced_column_id
            WHERE SCHEMA_NAME(t.schema_id)='dbo' AND t.name IN ({TableFilter})
            """);
        Assert.Equal(Tables.SelectMany(t => t.ForeignKeys.Select(f => $"{t.Name}|{f.Name}|{f.Columns.Single()}|{f.PrincipalTable}|{f.PrincipalColumns!.Single()}|NO_ACTION|NO_ACTION|0|0|dbo")).Order(), fk.Order());
        var indexes = await ReadRowsAsync(context, $"""
            SELECT t.name,i.name,ic.key_ordinal,c.name,CAST(i.is_unique AS int),i.filter_definition,
                i.type_desc,CAST(i.is_disabled AS int),CAST(ic.is_included_column AS int),CAST(ic.is_descending_key AS int)
            FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id AND i.is_primary_key=0 AND i.index_id>0
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE SCHEMA_NAME(t.schema_id)='dbo' AND t.name IN ({TableFilter})
            """);
        Assert.Equal(new CreateIdentitySchema().UpOperations.OfType<CreateIndexOperation>()
            .SelectMany(i => i.Columns.Select((c,n) => $"{i.Table}|{i.Name}|{n+1}|{c}|{(i.IsUnique ? 1 : 0)}|{NormalizeIndexFilter(i.Filter)}|NONCLUSTERED|0|0|0")).Order(),
            indexes.Select(row =>
            {
                var fields = row.Split('|');
                fields[5] = NormalizeIndexFilter(fields[5]);
                return string.Join('|', fields);
            }).Order());
        var columns = new LimitIdentityKeyLengths().UpOperations.OfType<AlterColumnOperation>().ToArray();
        var columnFilter = string.Join(" OR ", columns.Select(c => $"(t.name=N'{c.Table}' AND c.name=N'{c.Name}')"));
        var actualColumns = await ReadRowsAsync(context, $"""
            SELECT t.name,c.name,TYPE_NAME(c.system_type_id),c.max_length,CAST(c.is_nullable AS int)
            FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
            WHERE SCHEMA_NAME(t.schema_id)='dbo' AND ({columnFilter})
            """);
        Assert.Equal(columns.Select(c => $"{c.Table}|{c.Name}|nvarchar|{length*2}|0").Order(), actualColumns.Order());
    }

    public sealed record TableFingerprint(string Table, long Rows, string Sha256);

    public static string NormalizeIndexFilter(string? filter) => filter switch
    {
        null => "",
        "([NormalizedName] IS NOT NULL)" => "[NormalizedName] IS NOT NULL",
        "([NormalizedUserName] IS NOT NULL)" => "[NormalizedUserName] IS NOT NULL",
        _ => filter // Không loại ngoặc tùy ý: giữ nguyên mọi biểu thức khác để phát hiện schema sai.
    };

    public static async Task<TableFingerprint[]> CaptureDataAsync(ApplicationDbContext context)
    {
        var fingerprints = new List<TableFingerprint>();
        foreach (var table in Tables.OrderBy(t => t.Name))
        {
            // JSON từng dòng bao gồm NULL, thứ tự PK ổn định; chỉ lưu hash, không lưu password/token.
            var order = string.Join(",", table.PrimaryKey!.Columns.Select(c => $"t.[{c}]"));
            var rows = await ReadRowsAsync(context,
                $"SELECT (SELECT t.* FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER) FROM [dbo].[{table.Name}] t WITH (TABLOCK,HOLDLOCK) ORDER BY {order}");
            fingerprints.Add(Fingerprint(table.Name, rows));
        }
        return fingerprints.ToArray();
    }

    public static TableFingerprint Fingerprint(string table, IReadOnlyList<string> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows)
        {
            var bytes = Encoding.UTF8.GetBytes(row);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        return new(table, rows.Count, Convert.ToHexString(hash.GetHashAndReset()));
    }
}
