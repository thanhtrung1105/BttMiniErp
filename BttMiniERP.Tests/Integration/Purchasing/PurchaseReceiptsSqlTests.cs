using System.Data.Common;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.Data;
using BttMiniERP.Api.DTOs.PurchaseReceipts;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace BttMiniERP.Tests.Integration.Purchasing;

[Collection("MasterData SQL"), Trait("Category","SqlServer"), Trait("SqlSuite","Purchasing")]
public sealed class PurchaseReceiptsSqlTests
{
    private static async Task<(int Supplier,int Item)> Setup(MasterDataSqlFixture f)
    {
        await using var db=f.Context();
        var supplier=await f.Suppliers(db).CreateAsync(new(){Code="SUP",Name="Supplier"},default);
        var category=await f.Categories(db).CreateAsync(new(){Code="CAT",Name="Category"},default);
        var item=await f.Items(db).CreateAsync(new(){Code="RAW",Name="Raw",CategoryId=category.Id,Unit="kg",ItemType=ItemType.RawMaterial},default);
        return(supplier.Id,item.Id);
    }
    private static CreatePurchaseReceiptRequest Create(int supplier,int item) => new(){SupplierId=supplier,ReceiptDate=DateTimeOffset.UnixEpoch,
        Lines=[new(){ItemId=item,Quantity=.125m,UnitPrice=.20m}]};
    private static Task<List<string>> Stock(ApplicationDbContext db) => IdentityDatabaseVerifier.ReadRowsAsync(db,"""
        SELECT CONCAT('B|',ItemId,'|',QuantityOnHand,'|',CreatedAt,'|',CreatedByUserId,'|',UpdatedAt,'|',UpdatedByUserId,'|',CONVERT(varchar(18),RowVersion,1)) FROM InventoryBalances
        UNION ALL SELECT CONCAT('T|',Id,'|',ItemId,'|',QuantityDelta,'|',BalanceAfter,'|',TransactionType,'|',SourceDocumentType,'|',SourceDocumentId,'|',SourceLineId,'|',PerformedByUserId,'|',OccurredAt) FROM InventoryTransactions ORDER BY 1
        """);
    private static async Task Conflict(Func<Task> call) => Assert.Equal(409,(await Assert.ThrowsAsync<ApiException>(call)).StatusCode);

    [PurchasingSqlFact]
    public async Task CreateReplaceLinesCancelAndQueriesNeverChangeStock()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Purchasing"); var ids=await Setup(f);
        int secondItem;
        await using(var setup=f.Context()) secondItem=(await f.Items(setup).CreateAsync(new(){Code="RAW2",Name="Raw 2",CategoryId=await setup.Categories.Select(x=>x.Id).SingleAsync(),Unit="kg",ItemType=ItemType.RawMaterial},default)).Id;
        await using var before=f.Context(); await before.Database.OpenConnectionAsync(); var stock=await Stock(before);
        await using var db=f.Context(); var service=f.Receipts(db);
        var request=Create(ids.Supplier,ids.Item); request.Lines.Add(new(){ItemId=secondItem,Quantity=2,UnitPrice=3});
        var draft=await service.CreateAsync(request,default);
        Assert.Equal(6.03m,draft.TotalAmount); Assert.Equal(DocumentStatus.Draft,draft.Status); Assert.Equal(f.ActorId,draft.CreatedByUserId);
        Assert.Matches("^PR-[A-F0-9]{32}$",draft.ReceiptNumber); Assert.Equal(2,draft.Lines.Count);
        Assert.Equal(stock,await Stock(before));
        var updated=await service.UpdateAsync(draft.Id,new(){SupplierId=ids.Supplier,Note="New",RowVersion=draft.RowVersion,Lines=[]},default);
        Assert.Empty(updated.Lines); Assert.Equal(0m,updated.TotalAmount); Assert.NotEqual(draft.RowVersion,updated.RowVersion);
        Assert.Equal(draft.ReceiptNumber,updated.ReceiptNumber); Assert.Equal(stock,await Stock(before));
        Assert.Empty(await before.PurchaseReceiptLines.ToArrayAsync());
        await Conflict(()=>service.UpdateAsync(draft.Id,new(){SupplierId=ids.Supplier,RowVersion=draft.RowVersion},default));
        await Conflict(()=>service.CancelAsync(draft.Id,draft.RowVersion,default));
        updated=await service.UpdateAsync(draft.Id,new(){SupplierId=ids.Supplier,RowVersion=updated.RowVersion,
            Lines=[new(){ItemId=secondItem,Quantity=4,UnitPrice=5},new(){ItemId=ids.Item,Quantity=6,UnitPrice=7}]},default);
        Assert.Equal(62m,updated.TotalAmount); Assert.Equal(stock,await Stock(before));
        var cancelled=await service.CancelAsync(draft.Id,updated.RowVersion,default);
        Assert.Equal(DocumentStatus.Cancelled,cancelled.Status); Assert.Equal(f.ActorId,cancelled.CancelledByUserId); Assert.NotNull(cancelled.CancelledAt);
        var persisted=await service.GetAsync(draft.Id,default);
        Assert.Equal(updated.Lines.ToArray(),persisted.Lines.ToArray());
        Assert.Equal(2,await before.PurchaseReceiptLines.CountAsync());
        Assert.Equal(stock,await Stock(before)); Assert.Equal(DocumentStatus.Cancelled,(await before.PurchaseReceipts.AsNoTracking().SingleAsync()).Status);
        await Conflict(()=>service.CancelAsync(draft.Id,cancelled.RowVersion,default));
        await Conflict(()=>service.UpdateAsync(draft.Id,new(){SupplierId=ids.Supplier,RowVersion=cancelled.RowVersion},default));
        var page=await service.ListAsync(new(){SupplierId=ids.Supplier,Status=DocumentStatus.Cancelled,From=DateTimeOffset.UnixEpoch,To=DateTimeOffset.UnixEpoch.AddDays(1),PageSize=1,SortBy="id",SortDirection="asc"},default);
        Assert.Equal(draft.Id,Assert.Single(page.Items).Id);
        Assert.Empty((await service.ListAsync(new(){To=DateTimeOffset.UnixEpoch},default)).Items);
        Assert.Empty((await service.ListAsync(new(){Page=2,PageSize=1},default)).Items);
        Assert.Equal(404,(await Assert.ThrowsAsync<ApiException>(()=>service.GetAsync(int.MaxValue,default))).StatusCode);
    }
    [PurchasingSqlFact]
    public async Task InactiveSupplierFinishedOrInactiveItemAreRejectedAndUsedItemCannotChangeClassification()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Purchasing"); var ids=await Setup(f);
        await using(var db=f.Context()) { var supplier=await db.Suppliers.SingleAsync(); supplier.IsActive=false; await db.SaveChangesAsync(); }
        await using(var db=f.Context()) await Conflict(()=>f.Receipts(db).CreateAsync(Create(ids.Supplier,ids.Item),default));
        await using(var db=f.Context()) { (await db.Suppliers.SingleAsync()).IsActive=true; var item=await db.Items.SingleAsync(); item.ItemType=ItemType.FinishedProduct; await db.SaveChangesAsync(); }
        await using(var db=f.Context()) await Conflict(()=>f.Receipts(db).CreateAsync(Create(ids.Supplier,ids.Item),default));
        await using(var db=f.Context()) { var item=await db.Items.SingleAsync(); item.ItemType=ItemType.RawMaterial; item.IsActive=false; await db.SaveChangesAsync(); }
        await using(var db=f.Context()) await Conflict(()=>f.Receipts(db).CreateAsync(Create(ids.Supplier,ids.Item),default));
        await using(var db=f.Context()) { (await db.Items.SingleAsync()).IsActive=true; await db.SaveChangesAsync(); await f.Receipts(db).CreateAsync(Create(ids.Supplier,ids.Item),default); }
        await using var check=f.Context(); var dto=await f.Items(check).GetAsync(ids.Item,default);
        await Conflict(()=>f.Items(check).UpdateAsync(dto.Id,new(){Code=dto.Code,Name=dto.Name,CategoryId=dto.CategoryId,Unit="box",ItemType=ItemType.RawMaterial,RowVersion=dto.RowVersion},default));
    }
    [PurchasingSqlFact]
    public async Task RollbackAfterHeaderAndLineRemovalRestoresWholeReceiptAndStock()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Purchasing"); var ids=await Setup(f);
        PurchaseReceiptResponse original;
        await using(var db=f.Context()) original=await f.Receipts(db).CreateAsync(Create(ids.Supplier,ids.Item),default);
        await using var before=f.Context(); await before.Database.OpenConnectionAsync(); var stock=await Stock(before);
        var failure=new FailNewLines();
        await using(var db=f.Context(failure)) await Assert.ThrowsAsync<DbUpdateException>(()=>f.Receipts(db).UpdateAsync(original.Id,new(){SupplierId=ids.Supplier,RowVersion=original.RowVersion,
            Lines=[new(){ItemId=ids.Item,Quantity=9,UnitPrice=2}]},default));
        Assert.Equal(2,failure.Saves);
        await using var verify=f.Context(); await verify.Database.OpenConnectionAsync(); var current=await f.Receipts(verify).GetAsync(original.Id,default);
        Assert.Equal(original.RowVersion,current.RowVersion); Assert.Equal(original.TotalAmount,current.TotalAmount); Assert.Equal(original.Lines.ToArray(),current.Lines.ToArray());
        Assert.Equal(stock,await Stock(verify));
        await using(var db=f.Context(new InvalidLineOnCreate()))
        {
            var error=await Assert.ThrowsAsync<DbUpdateException>(()=>f.Receipts(db).CreateAsync(Create(ids.Supplier,ids.Item),default));
            Assert.Contains("CK_PurchaseReceiptLines_Quantity",Assert.IsType<SqlException>(error.InnerException).Message);
        }
        await using(var db=f.Context(new FailAfterSave())) await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Receipts(db).CreateAsync(Create(ids.Supplier,ids.Item),default));
        Assert.Equal(1,await verify.PurchaseReceipts.CountAsync()); Assert.Equal(1,await verify.PurchaseReceiptLines.CountAsync()); Assert.Equal(stock,await Stock(verify));
    }
    [PurchasingSqlFact]
    public async Task ConcurrentCreatesHaveDifferentNumbersAndForcedNumberCollisionIs409()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Purchasing"); var ids=await Setup(f); var barrier=new TwoCreates();
        async Task<PurchaseReceiptResponse> CreateOne() { await using var db=f.Context(barrier); return await f.Receipts(db).CreateAsync(new(){SupplierId=ids.Supplier},default); }
        var pair=await Task.WhenAll(CreateOne(),CreateOne()); Assert.NotEqual(pair[0].ReceiptNumber,pair[1].ReceiptNumber);
        await using(var db=f.Context(new ForceNumber(pair[0].ReceiptNumber))) await Conflict(()=>f.Receipts(db).CreateAsync(new(){SupplierId=ids.Supplier},default));
        await using var verify=f.Context(); Assert.Equal(2,await verify.PurchaseReceipts.CountAsync());
    }
    [PurchasingSqlFact]
    public async Task ConcurrentUpdatesWithSameVersionHaveOneWinner()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Purchasing"); var ids=await Setup(f);
        PurchaseReceiptResponse receipt; await using(var db=f.Context()) receipt=await f.Receipts(db).CreateAsync(Create(ids.Supplier,ids.Item),default);
        var barrier=new TwoHeaderLocks();
        async Task<(int Status,string Note)> Edit(string note) { await using var db=f.Context(barrier); try { await f.Receipts(db).UpdateAsync(receipt.Id,new(){SupplierId=ids.Supplier,RowVersion=receipt.RowVersion,Note=note},default);return(200,note); }catch(ApiException e){return(e.StatusCode,note);} }
        var results=await Task.WhenAll(Edit("one"),Edit("two")); Assert.Equal(new[]{200,409},results.Select(x=>x.Status).Order());
        await using var verify=f.Context(); Assert.Equal(results.Single(x=>x.Status==200).Note,(await verify.PurchaseReceipts.SingleAsync()).Note);
    }
    [PurchasingSqlFact]
    public async Task SqlConstraintsRejectDuplicateLinesInvalidForeignKeysQuantityAndPrice()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Purchasing"); var ids=await Setup(f);
        PurchaseReceiptResponse receipt; await using(var db=f.Context()) receipt=await f.Receipts(db).CreateAsync(Create(ids.Supplier,ids.Item),default);
        int secondItem;
        await using(var setup=f.Context()) secondItem=(await f.Items(setup).CreateAsync(new(){Code="SECOND",Name="Second",CategoryId=await setup.Categories.Select(x=>x.Id).SingleAsync(),Unit="kg",ItemType=ItemType.RawMaterial},default)).Id;
        foreach(var kind in new[]{"itemDuplicate","lineDuplicate","itemFk","receiptFk","quantity","price"})
        {
            await using var db=f.Context(); await using var tx=await db.Database.BeginTransactionAsync();
            var line=new PurchaseReceiptLine{PurchaseReceiptId=receipt.Id,ItemId=ids.Item,LineNumber=2,Quantity=1,UnitPrice=1,LineTotal=1};
            if(kind=="lineDuplicate") { line.LineNumber=1; line.ItemId=secondItem; }
            if(kind=="itemFk") line.ItemId=int.MaxValue;
            if(kind=="receiptFk") line.PurchaseReceiptId=int.MaxValue;
            if(kind=="quantity") line.Quantity=0;
            if(kind=="price") line.UnitPrice=-1;
            // Xóa dòng sở hữu trong transaction để CHECK/FK không bị unique che lỗi; rollback cuối case.
            if(kind is not ("itemDuplicate" or "lineDuplicate")) await db.PurchaseReceiptLines.Where(x=>x.PurchaseReceiptId==receipt.Id).ExecuteDeleteAsync();
            db.PurchaseReceiptLines.Add(line); var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
            var sql=Assert.IsType<SqlException>(error.InnerException);
            if(kind.EndsWith("Duplicate")) Assert.Contains(sql.Number,new[]{2601,2627}); else Assert.Equal(547,sql.Number);
            Assert.Contains(kind switch {
                "itemDuplicate"=>"IX_PurchaseReceiptLines_PurchaseReceiptId_ItemId",
                "lineDuplicate"=>"IX_PurchaseReceiptLines_PurchaseReceiptId_LineNumber",
                "itemFk"=>"FK_PurchaseReceiptLines_Items_ItemId",
                "receiptFk"=>"FK_PurchaseReceiptLines_PurchaseReceipts_PurchaseReceiptId",
                "quantity"=>"CK_PurchaseReceiptLines_Quantity",
                _=>"CK_PurchaseReceiptLines_UnitPrice" },sql.Message);
            await tx.RollbackAsync();
        }
        await using(var db=f.Context()) { var row=await db.PurchaseReceipts.SingleAsync(); row.SupplierId=int.MaxValue; var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync()); Assert.Equal(547,Assert.IsType<SqlException>(error.InnerException).Number); }
    }
    private sealed class FailNewLines : SaveChangesInterceptor
    {
        public int Saves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        { if(++Saves==2) foreach(var entry in data.Context!.ChangeTracker.Entries<PurchaseReceiptLine>().Where(x=>x.State==EntityState.Added)) entry.Entity.Quantity=0; return ValueTask.FromResult(result); }
    }
    private sealed class InvalidLineOnCreate : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        { foreach(var entry in data.Context!.ChangeTracker.Entries<PurchaseReceiptLine>().Where(x=>x.State==EntityState.Added)) entry.Entity.Quantity=0; return ValueTask.FromResult(result); }
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken ct=default) => throw new InvalidOperationException("Test-only failure before commit.");
    }
    private sealed class ForceNumber(string number) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        { foreach(var e in data.Context!.ChangeTracker.Entries<PurchaseReceipt>().Where(x=>x.State==EntityState.Added)) e.Entity.ReceiptNumber=number; return ValueTask.FromResult(result); }
    }
    private sealed class TwoCreates : SaveChangesInterceptor
    {
        private int count; private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        { if(Interlocked.Increment(ref count)==2) ready.TrySetResult(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(20),ct); return result; }
    }
    private sealed class TwoHeaderLocks : DbCommandInterceptor
    {
        private int count; private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        { if(command.CommandText.Contains("FROM [PurchaseReceipts] WITH (UPDLOCK, HOLDLOCK)")) { if(Interlocked.Increment(ref count)==2) ready.TrySetResult(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(20),ct); } return result; }
    }
}
