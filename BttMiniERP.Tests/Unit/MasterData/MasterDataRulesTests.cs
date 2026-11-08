using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.Categories;
using BttMiniERP.Api.DTOs.Items;
using BttMiniERP.Api.DTOs.MasterData;
using BttMiniERP.Api.Enums;
using BttMiniERP.Api.Services;

namespace BttMiniERP.Tests.Unit.MasterData;

public sealed class MasterDataRulesTests
{
    [Theory]
    [InlineData(" nvl-01 ", "NVL-01")]
    [InlineData("tp-02", "TP-02")]
    public void CodesAreTrimmedAndUppercased(string input, string expected)
        => Assert.Equal(expected, MasterDataRules.NormalizeCode(input));

    [Theory]
    [InlineData("0", true)][InlineData("12.34", true)][InlineData("1.2300", false)]
    [InlineData("9999999999999999.99", true)][InlineData("10000000000000000", false)]
    [InlineData("-0.01", false)][InlineData("1.001", false)]
    public void PriceChecksPrecisionAndScale(string input, bool valid)
        => Assert.Equal(valid, new PriceAttribute().IsValid(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("AAAAAAAAAAA=")][InlineData("W/\"AAAAAAAAAAA=\"")]
    [InlineData("*")][InlineData("\"AAAAAAAAAAA=\",\"AAAAAAAAAAA=\"")][InlineData("\"AAAA\"")]
    public void IfMatchRejectsMissingWeakWildcardAndMultipleVersions(string? input)
        => Assert.Equal(400, Assert.Throws<ApiException>(() => MasterDataRules.ParseIfMatch(input)).StatusCode);

    [Fact]
    public void IfMatchAcceptsOnlyStrongEightByteVersion()
        => Assert.Equal("AAAAAAAAAAA=", MasterDataRules.ParseIfMatch("\"AAAAAAAAAAA=\""));

    [Theory]
    [InlineData(0, true)][InlineData(1, true)][InlineData(2, false)][InlineData(-1, false)]
    public void ValidatesItemType(int type, bool valid)
    {
        var dto = new CreateItemRequest { Code = "a", Name = "a", Unit = "kg", CategoryId = 1, ItemType = (ItemType)type };
        Assert.Equal(valid, Validator.TryValidateObject(dto, new ValidationContext(dto), [], true));
    }

    [Fact]
    public void RejectsMissingFieldsWhitespaceAndInvalidQueries()
    {
        foreach (var request in new object[] { new CreateCategoryRequest(), new CreateCategoryRequest { Code = " ", Name = "a" },
            new CreateCategoryRequest { Code = "a", Name = new string('a', 201) }, new CreateItemRequest(),
            new CreateItemRequest { Code = "a", Name = "a", CategoryId = 1, Unit = "kg" },
            new UpdateCategoryRequest { Code = "a", Name = "a" }, new MasterDataQuery { Page = 0 },
            new MasterDataQuery { PageSize = 101 }, new MasterDataQuery { SortBy = "bad" },
            new MasterDataQuery { SortDirection = "bad" }, new ItemQuery { CategoryId = 0 }, new ItemQuery { ItemType = (ItemType)9 } })
            Assert.Equal(400, Assert.Throws<ApiException>(() => MasterDataRules.Validate(request)).StatusCode);
    }

    [Theory]
    [InlineData(true, 1, "kg", false)][InlineData(true, 0, "box", false)]
    [InlineData(true, 0, " kg ", true)][InlineData(false, 1, "box", true)]
    public void ClassificationRuleProtectsUsedItems(bool used, int type, string unit, bool allowed)
    {
        var error = Record.Exception(() => MasterDataRules.EnsureClassificationChangeAllowed(ItemType.RawMaterial, "kg", (ItemType)type, unit, used));
        if (allowed) Assert.Null(error);
        else Assert.Equal(409, Assert.IsType<ApiException>(error).StatusCode);
    }
}
