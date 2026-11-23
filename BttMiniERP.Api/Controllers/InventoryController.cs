using BttMiniERP.Api.Common.Authorization;
using BttMiniERP.Api.DTOs.Inventory;
using BttMiniERP.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BttMiniERP.Api.Controllers;

[ApiController, Route("api/v1/inventory"), Authorize(Policy = AppRoles.ReadInventory)]
public sealed class InventoryController(InventoryQueryService service) : ControllerBase
{
    [HttpGet("balances")]
    public Task<InventoryPage<BalanceRow>> Balances([FromQuery] InventoryBalanceQuery query, CancellationToken ct)
        => service.BalancesAsync(query, ct);

    [HttpGet("transactions")]
    public Task<InventoryPage<TransactionRow>> Transactions([FromQuery] InventoryTransactionQuery query, CancellationToken ct)
        => service.TransactionsAsync(query, ct);
}
