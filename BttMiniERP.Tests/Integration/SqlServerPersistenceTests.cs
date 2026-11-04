using BttMiniERP.Api.Data;
using BttMiniERP.Api.Entities;
using BttMiniERP.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BttMiniERP.Tests.Integration;

[Collection("SQL Server")]
[Trait("Category", "SqlServer")]
[Trait("SqlSuite", "Persistence")]
public sealed class SqlServerPersistenceTests(SqlServerFixture fixture)
{
    [SqlServerFact]
    public async Task PersistsAllCompositeKeysAt128CodeUnitsAndRollsBack()
    {
        await using var context = fixture.CreateContext();
        await fixture.PreparePersistenceAsync(context);
        var userId = Guid.NewGuid().ToString().PadRight(128, 'u');
        var roleId = Guid.NewGuid().ToString().PadRight(128, 'r');
        var provider = Guid.NewGuid().ToString().PadRight(128, 'p');
        var providerKey = new string('k', 128);
        var tokenName = new string('n', 128);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.Users.Add(new ApplicationUser { Id = userId, UserName = userId, NormalizedUserName = userId.ToUpperInvariant() });
            context.Roles.Add(new IdentityRole { Id = roleId, Name = roleId, NormalizedName = roleId.ToUpperInvariant() });
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
            context.UserLogins.Add(new IdentityUserLogin<string> { UserId = userId, LoginProvider = provider, ProviderKey = providerKey });
            context.UserTokens.Add(new IdentityUserToken<string> { UserId = userId, LoginProvider = provider, Name = tokenName, Value = "temporary-test-value" });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            Assert.True(await context.UserRoles.AnyAsync(link => link.UserId == userId && link.RoleId == roleId));
            Assert.True(await context.UserLogins.AnyAsync(login => login.UserId == userId && login.ProviderKey == providerKey));
            Assert.Equal("temporary-test-value", (await context.UserTokens.SingleAsync(token => token.UserId == userId)).Value);
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        Assert.False(await context.Users.AnyAsync(user => user.Id == userId));
        Assert.False(await context.Roles.AnyAsync(role => role.Id == roleId));
        Assert.False(await context.UserRoles.AnyAsync(link => link.UserId == userId));
        Assert.False(await context.UserLogins.AnyAsync(login => login.UserId == userId));
        Assert.False(await context.UserTokens.AnyAsync(token => token.UserId == userId));
    }

    [SqlServerFact]
    public async Task RejectsIdentityIdLongerThan128CodeUnitsWithoutTruncation()
    {
        await using var context = fixture.CreateContext();
        await fixture.PreparePersistenceAsync(context);
        var id = Guid.NewGuid().ToString().PadRight(129, 'x');
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.Users.Add(new ApplicationUser { Id = id, UserName = id });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains(Assert.IsType<Microsoft.Data.SqlClient.SqlException>(exception.InnerException).Number, new[] { 2628, 8152 });
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        Assert.False(await context.Users.AnyAsync(user => user.Id == id || user.Id == id.Substring(0, 128)));
    }

    [SqlServerFact]
    public async Task ConnectsToDedicatedTestDatabase()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        Assert.Equal(fixture.Settings.DatabaseName, context.Database.GetDbConnection().Database);
        Assert.True(await context.Database.CanConnectAsync());
    }

    [SqlServerFact]
    public async Task RepeatedPreparationKeepsApprovedSchemaAndHistory()
    {
        await using var context = fixture.CreateContext();
        await fixture.PreparePersistenceAsync(context);
        await fixture.PreparePersistenceAsync(context);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(), name => name.EndsWith("_CreateIdentitySchema"));
    }

    [SqlServerFact]
    public async Task RejectsUserRoleWithMissingParents()
    {
        await using var context = fixture.CreateContext();
        await fixture.PreparePersistenceAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = Guid.NewGuid().ToString(), RoleId = Guid.NewGuid().ToString() });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<Microsoft.Data.SqlClient.SqlException>(exception.InnerException).Number);
        await transaction.RollbackAsync();
    }

    [SqlServerFact]
    public async Task PersistsUserRoleAndRollsBackTestData()
    {
        await using var context = fixture.CreateContext();
        await fixture.PreparePersistenceAsync(context);
        var id = Guid.NewGuid().ToString();
        var roleId = Guid.NewGuid().ToString();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
            context.Roles.Add(new IdentityRole { Id = roleId, Name = roleId, NormalizedName = roleId.ToUpperInvariant() });
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = roleId });
            await context.SaveChangesAsync();

            await using var reader = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(context.Database.GetDbConnection()).Options);
            await reader.Database.UseTransactionAsync(transaction.GetDbTransaction());
            Assert.Equal(id, (await reader.Users.AsNoTracking().SingleAsync(user => user.Id == id)).UserName);
            Assert.True(await reader.UserRoles.AnyAsync(link => link.UserId == id && link.RoleId == roleId));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        Assert.False(await context.Users.AnyAsync(user => user.Id == id));
        Assert.False(await context.Roles.AnyAsync(role => role.Id == roleId));
        Assert.False(await context.UserRoles.AnyAsync(link => link.UserId == id));
    }
}
