using System.ComponentModel.DataAnnotations;
using BttMiniERP.Api.DTOs.Inventory;
using BttMiniERP.Api.Enums;

namespace BttMiniERP.Tests.Unit.Inventory;

public sealed class QueryValidationTests
{
    public static TheoryData<InventoryQuery> InvalidQueries => new()
    {
        new InventoryBalanceQuery { Page = 0 }, new InventoryTransactionQuery { Page = 1000001 },
        new InventoryBalanceQuery { PageSize = 101 }, new InventoryTransactionQuery { PageSize = 0 },
        new InventoryBalanceQuery { ItemId = 0 }, new InventoryBalanceQuery { ItemType = (ItemType)99 },
        new InventoryBalanceQuery { Search = new string('x', 201) }, new InventoryBalanceQuery { SortBy = "name" },
        new InventoryTransactionQuery { SourceDocumentId = -1 }, new InventoryTransactionQuery { SortBy = "quantityDelta" },
        new InventoryTransactionQuery { SortDirection = "up" }, new InventoryTransactionQuery { SourceDocumentType = (SourceDocumentType)9 },
        new InventoryTransactionQuery { TransactionType = (InventoryTransactionType)9 },
        new InventoryTransactionQuery { From = DateTimeOffset.UnixEpoch, To = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(7)) },
        new InventoryTransactionQuery { From = DateTimeOffset.UnixEpoch.AddTicks(1), To = DateTimeOffset.UnixEpoch }
    };

    [Theory, MemberData(nameof(InvalidQueries))]
    public void InvalidFilterOrPageIsRejected(InventoryQuery query)
        => Assert.False(Validator.TryValidateObject(query, new ValidationContext(query), [], true));

    [Fact]
    public void OpenEndedDateRangeAndMaximumPageAreValid()
    {
        foreach (var query in new[] { new InventoryTransactionQuery { From = DateTimeOffset.UnixEpoch, Page = 1000000 },
            new InventoryTransactionQuery { To = DateTimeOffset.UnixEpoch, PageSize = 100 } })
            Assert.True(Validator.TryValidateObject(query, new ValidationContext(query), [], true));
    }
}
