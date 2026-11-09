using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.Suppliers;
using BttMiniERP.Api.DTOs.Customers;
using BttMiniERP.Api.Services;

namespace BttMiniERP.Tests.Unit.Partners;

public sealed class PartnerRulesTests
{
    [Theory]
    [InlineData(null, true)][InlineData("person@example.com", true)]
    [InlineData("invalid", false)][InlineData("", false)][InlineData("a@@example.com", false)]
    public void OptionalEmailIsValidatedAtServiceBoundary(string? email, bool valid)
    {
        foreach (var request in new object[] { new CreateSupplierRequest { Code = "a", Name = "a", Email = email },
            new CreateCustomerRequest { Code = "a", Name = "a", Email = email } })
        {
            var error = Record.Exception(() => MasterDataRules.Validate(request));
            if (valid) Assert.Null(error);
            else Assert.Equal(400, Assert.IsType<ApiException>(error).StatusCode);
        }
    }

    [Theory]
    [InlineData("", "Name")][InlineData("code", " ")][InlineData(" ", "Name")]
    public async Task ServicesRejectMissingFieldsBeforeAccessingDatabase(string code, string name)
    {
        var suppliers = new SupplierService(null!, null!, null!);
        var customers = new CustomerService(null!, null!, null!);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => suppliers.CreateAsync(new() { Code = code, Name = name }, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => customers.CreateAsync(new() { Code = code, Name = name }, default))).StatusCode);
    }

    [Fact]
    public async Task UpdatesAndDeletesRequireValidVersionBeforeDatabaseAccess()
    {
        var suppliers = new SupplierService(null!, null!, null!);
        var customers = new CustomerService(null!, null!, null!);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => suppliers.UpdateAsync(1, new() { Code = "a", Name = "a" }, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => customers.UpdateAsync(1, new() { Code = "a", Name = "a" }, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => suppliers.DeactivateAsync(1, "bad", default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => customers.DeactivateAsync(1, "bad", default))).StatusCode);
        Assert.Equal("PARTNER-01", MasterDataRules.NormalizeCode(" partner-01 "));
    }
}
