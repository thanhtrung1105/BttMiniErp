using System.Reflection;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.Data.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BttMiniERP.Tests.Fixtures;

// Catalog chỉ đọc của test: giữ migration thật, thêm một ID chưa có trong history SQL thật.
// Không sửa __EFMigrationsHistory, không sinh/chạy migration hoặc thay schema để tạo pending.
public sealed class BootstrapPendingMigrationsAssembly : IMigrationsAssembly
{
    public const string PendingId = "20990101000000_BootstrapTestPendingOnly";
    public Assembly Assembly => typeof(ApplicationDbContext).Assembly;
    public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } = BuildCatalog();
    public ModelSnapshot ModelSnapshot => (ModelSnapshot)Activator.CreateInstance(
        Assembly.DefinedTypes.Single(type => type.IsSubclassOf(typeof(ModelSnapshot))).AsType(), nonPublic: true)!;
    public string? FindMigrationId(string nameOrId)
        => Migrations.Keys.SingleOrDefault(id => id == nameOrId || id[(id.IndexOf('_') + 1)..] == nameOrId);
    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
        => throw new InvalidOperationException("Catalog bootstrap test chỉ đọc, cấm thực thi migration.");

    private static Dictionary<string, TypeInfo> BuildCatalog()
    {
        var catalog = typeof(ApplicationDbContext).Assembly.DefinedTypes
            .Where(type => type.GetCustomAttribute<MigrationAttribute>() != null)
            .ToDictionary(type => type.GetCustomAttribute<MigrationAttribute>()!.Id);
        catalog.Add(PendingId, typeof(CreateIdentitySchema).GetTypeInfo());
        return catalog;
    }
}
