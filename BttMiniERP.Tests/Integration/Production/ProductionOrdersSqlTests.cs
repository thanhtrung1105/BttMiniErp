using System.Data.Common;
using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.BillOfMaterials;
using BttMiniERP.Api.DTOs.ProductionOrders;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;
using BttMiniERP.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BttMiniERP.Tests.Integration.Production;

[Collection("MasterData SQL"), Trait("Category", "SqlServer"), Trait("SqlSuite", "Production")]
public sealed class ProductionOrdersSqlTests
{
    private static async Task<BillOfMaterialResponse> Setup(MasterDataSqlFixture f)
    {
        await using var db=f.Context();
        var category=await f.Categories(db).CreateAsync(new() { Code="PROD",Name="Production" },default);
        async Task<int> Item(string code,ItemType type) => (await f.Items(db).CreateAsync(new() {
            Code=code,Name=code,Unit="kg",CategoryId=category.Id,ItemType=type },default)).Id;
        var product=await Item("PRODUCT",ItemType.FinishedProduct);
        var raw=await Item("RAW",ItemType.RawMaterial); var raw2=await Item("RAW2",ItemType.RawMaterial);
        return await f.Boms(db).CreateAsync(new() { Code="BOM",FinishedProductId=product,
            Details=[new(){RawMaterialId=raw,QuantityPerUnit=2},new(){RawMaterialId=raw2,QuantityPerUnit=0.333m}] },default);
    }
    private static UpdateBillOfMaterialRequest EditBom(BillOfMaterialResponse b) => new() {
        Code=b.Code,FinishedProductId=b.FinishedProductId,RowVersion=b.RowVersion,
        Details=b.Details.Select(x=>new BillOfMaterialDetailRequest {RawMaterialId=x.RawMaterialId,QuantityPerUnit=x.QuantityPerUnit+2}).ToList() };
    private static async Task<ProductionOrderResponse> Create(MasterDataSqlFixture f,int bom,decimal quantity=3)
    {
        await using var db=f.Context(); return await f.Production(db).CreateAsync(new(){BomId=bom,Quantity=quantity},default);
    }
    private static async Task Conflict(Func<Task> action) => Assert.Equal(409,(await Assert.ThrowsAsync<ApiException>(action)).StatusCode);
    private static async Task<string[]> Snapshot(MasterDataSqlFixture f)
    {
        await using var db=f.Context(); await db.Database.OpenConnectionAsync();
        var rows=new List<string>();
        foreach(var table in new[]{"ProductionOrders","ProductionOrderMaterials","BillOfMaterials","BillOfMaterialDetails","Items","InventoryBalances","InventoryTransactions"})
            rows.Add(string.Concat(await IdentityDatabaseVerifier.ReadRowsAsync(db,$"SELECT * FROM [{table}] ORDER BY 1 FOR JSON PATH, INCLUDE_NULL_VALUES")));
        return rows.ToArray();
    }

    [ProductionSqlFact]
    public async Task SnapshotSurvivesBomEditAndInactivationAndQuantityUsesOriginalPerUnit()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Production"); var bom=await Setup(f);
        var order=await Create(f,bom.Id);
        Assert.Matches("^PO-[0-9A-F]{32}$",order.OrderNumber); Assert.Equal(DocumentStatus.Draft,order.Status);
        Assert.Equal(f.ActorId,order.CreatedByUserId); Assert.Equal(bom.Version,order.BomVersionSnapshot);
        Assert.Equal(new[]{6m,0.999m},order.Materials.Select(x=>x.RequiredQuantity));
        BillOfMaterialResponse changed;
        await using(var db=f.Context()) changed=await f.Boms(db).UpdateAsync(bom.Id,EditBom(bom),default);
        await using(var db=f.Context()) await f.Boms(db).DeactivateAsync(changed.Id,changed.RowVersion,default);
        await using(var db=f.Context()) {
            var read=await f.Production(db).GetAsync(order.Id,default);
            Assert.Equal(order,read with {Materials=order.Materials});
            Assert.Equal(order.Materials.ToArray(),read.Materials.ToArray());
        }
        ProductionOrderResponse updated;
        await using(var db=f.Context()) updated=await f.Production(db).UpdateAsync(order.Id,new(){Quantity=4,Note="Updated",RowVersion=order.RowVersion},default);
        Assert.Equal(new[]{8m,1.332m},updated.Materials.Select(x=>x.RequiredQuantity));
        Assert.Equal(order.Materials.Select(x=>x.Id),updated.Materials.Select(x=>x.Id));
        Assert.Equal(order.Materials.Select(x=>x.QuantityPerUnitSnapshot),updated.Materials.Select(x=>x.QuantityPerUnitSnapshot));
        Assert.Equal(order.BomVersionSnapshot,updated.BomVersionSnapshot); Assert.NotEqual(order.RowVersion,updated.RowVersion);
        Assert.Equal(f.ActorId,updated.UpdatedByUserId); Assert.Equal(order.CreatedAt,updated.CreatedAt);
        await using(var db=f.Context()) {
            var page=await f.Production(db).ListAsync(new(){BomId=bom.Id,FinishedProductId=bom.FinishedProductId,Status=DocumentStatus.Draft,From=order.CreatedAt,To=order.CreatedAt.AddSeconds(1),PageSize=1},default);
            Assert.Equal(order.Id,Assert.Single(page.Items).Id);
            Assert.Empty((await f.Production(db).ListAsync(new(){Page=2,PageSize=1},default)).Items);
            Assert.Empty((await f.Production(db).ListAsync(new(){To=order.CreatedAt},default)).Items);
        }
        await using(var db=f.Context()) await Conflict(()=>f.Production(db).UpdateAsync(order.Id,new(){Quantity=9,RowVersion=order.RowVersion},default));
        await using(var db=f.Context()) await Conflict(()=>f.Production(db).CancelAsync(order.Id,order.RowVersion,default));
        ProductionOrderResponse cancelled;
        await using(var db=f.Context()) cancelled=await f.Production(db).CancelAsync(order.Id,updated.RowVersion,default);
        Assert.Equal(DocumentStatus.Cancelled,cancelled.Status); Assert.Equal(f.ActorId,cancelled.CancelledByUserId);
        Assert.NotNull(cancelled.CancelledAt); Assert.Equal(updated.Materials.ToArray(),cancelled.Materials.ToArray());
        await using(var db=f.Context()) await Conflict(()=>f.Production(db).CancelAsync(order.Id,cancelled.RowVersion,default));
        await using(var db=f.Context()) await Conflict(()=>f.Production(db).UpdateAsync(order.Id,new(){Quantity=1,RowVersion=cancelled.RowVersion},default));
        await using var verify=f.Context();
        Assert.Equal(1,await verify.ProductionOrders.CountAsync()); Assert.Equal(2,await verify.ProductionOrderMaterials.CountAsync());
        Assert.All(await verify.ProductionOrderMaterials.ToArrayAsync(),x=>Assert.Null(x.ActualQuantity));
        Assert.All(await verify.InventoryBalances.ToArrayAsync(),x=>Assert.Equal(0,x.QuantityOnHand));
        Assert.Empty(await verify.InventoryTransactions.ToArrayAsync());
    }

    [ProductionSqlFact]
    public async Task SavedChangesFailureRollsBackCreateAndUpdateIncludingAuditAndSnapshot()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Production"); var bom=await Setup(f);
        var before=await Snapshot(f);
        await using(var db=f.Context(new InvalidMaterialBeforeSave()))
            await Assert.ThrowsAsync<DbUpdateException>(()=>f.Production(db).CreateAsync(new(){BomId=bom.Id,Quantity=3},default));
        Assert.Equal(before,await Snapshot(f));
        await using(var db=f.Context(new FailAfterSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Production(db).CreateAsync(new(){BomId=bom.Id,Quantity=3},default));
        Assert.Equal(before,await Snapshot(f));
        var order=await Create(f,bom.Id); before=await Snapshot(f);
        await using(var db=f.Context(new FailAfterSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Production(db).UpdateAsync(order.Id,new(){Quantity=4,Note="Must rollback",RowVersion=order.RowVersion},default));
        Assert.Equal(before,await Snapshot(f));
        await using(var db=f.Context(new FailAfterSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Production(db).CancelAsync(order.Id,order.RowVersion,default));
        Assert.Equal(before,await Snapshot(f));
    }

    [ProductionSqlFact]
    public async Task ConcurrentUpdateAndCancelUseIndependentConnectionsAndOneVersionWinner()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Production"); var bom=await Setup(f);
        var order=await Create(f,bom.Id); var barrier=new TwoHeaders("FROM [ProductionOrders] WITH (UPDLOCK, HOLDLOCK)");
        async Task<(string Operation,int Status)> Execute(bool cancel) {
            await using var db=f.Context(barrier);
            try {
                if(cancel) await f.Production(db).CancelAsync(order.Id,order.RowVersion,default);
                else await f.Production(db).UpdateAsync(order.Id,new(){Quantity=4,RowVersion=order.RowVersion},default);
                return(cancel ? "cancel" : "update",200);
            } catch(ApiException e) {return(cancel ? "cancel" : "update",e.StatusCode);}
        }
        var results=await Task.WhenAll(Execute(false),Execute(true));
        Assert.Equal(new[]{200,409},results.Select(x=>x.Status).Order());
        Assert.Equal(2,barrier.ConnectionIds.Count);
        await using var verify=f.Context(); var saved=await f.Production(verify).GetAsync(order.Id,default);
        var cancelled=results.Single(x=>x.Status==200).Operation=="cancel";
        Assert.Equal(cancelled ? DocumentStatus.Cancelled : DocumentStatus.Draft,saved.Status);
        Assert.Equal(cancelled ? 3m : 4m,saved.Quantity);
        Assert.Equal(cancelled ? 6m : 8m,saved.Materials[0].RequiredQuantity);
        Assert.NotEqual(order.RowVersion,saved.RowVersion);
        Assert.Empty(await verify.InventoryTransactions.ToArrayAsync());
    }

    [ProductionSqlFact]
    public async Task CreatingOrderRacingBomEditCapturesOneCompleteVersion()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Production"); var bom=await Setup(f);
        var barrier=new TwoHeaders("FROM [BillOfMaterials] WITH (UPDLOCK, HOLDLOCK)");
        async Task<ProductionOrderResponse> CreateOrder() {
            await using var db=f.Context(barrier); return await f.Production(db).CreateAsync(new(){BomId=bom.Id,Quantity=3},default);
        }
        async Task Edit() {await using var db=f.Context(barrier); await f.Boms(db).UpdateAsync(bom.Id,EditBom(bom),default);}
        var create=CreateOrder(); var edit=Edit(); await Task.WhenAll(create,edit);
        Assert.Equal(2,barrier.ConnectionIds.Count); var order=await create;
        Assert.Contains(order.BomVersionSnapshot,new[]{bom.Version,bom.Version+1});
        var expected=bom.Details.Select(x=>x.QuantityPerUnit+(order.BomVersionSnapshot==bom.Version ? 0 : 2)).ToArray();
        Assert.Equal(expected,order.Materials.Select(x=>x.QuantityPerUnitSnapshot));
        Assert.Equal(expected.Select(x=>decimal.Ceiling(x*3*1000)/1000),order.Materials.Select(x=>x.RequiredQuantity));
        await using var verify=f.Context(); var persisted=await f.Production(verify).GetAsync(order.Id,default);
        Assert.Equal(order.Materials.ToArray(),persisted.Materials.ToArray());
        Assert.Equal(bom.Version+1,(await f.Boms(verify).GetAsync(bom.Id,default)).Version);
    }

    [ProductionSqlFact]
    public async Task ReferencedBomProductAndRemovedMaterialMetadataRemainProtected()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Production"); var bom=await Setup(f);
        var order=await Create(f,bom.Id);
        await using(var db=f.Context()) await f.Production(db).CancelAsync(order.Id,order.RowVersion,default);
        int newProduct;
        await using(var db=f.Context()) newProduct=(await f.Items(db).CreateAsync(new(){Code="PRODUCT2",Name="P2",Unit="kg",
            CategoryId=await db.Categories.Select(x=>x.Id).SingleAsync(),ItemType=ItemType.FinishedProduct},default)).Id;
        await using(var db=f.Context()) await Conflict(()=>f.Boms(db).UpdateAsync(bom.Id,new(){Code=bom.Code,FinishedProductId=newProduct,
            RowVersion=bom.RowVersion,Details=EditBom(bom).Details},default));
        await using(var db=f.Context()) await f.Boms(db).UpdateAsync(bom.Id,new(){Code=bom.Code,FinishedProductId=bom.FinishedProductId,
            RowVersion=bom.RowVersion,Details=[new(){RawMaterialId=bom.Details[1].RawMaterialId,QuantityPerUnit=1}]},default);
        foreach(var changeType in new[]{false,true}) {
            await using var db=f.Context(); var item=await f.Items(db).GetAsync(bom.Details[0].RawMaterialId,default);
            await Conflict(()=>f.Items(db).UpdateAsync(item.Id,new(){Code=item.Code,Name=item.Name,CategoryId=item.CategoryId,
                Unit=changeType ? item.Unit : "box",ItemType=changeType ? ItemType.FinishedProduct : item.ItemType,RowVersion=item.RowVersion},default));
        }
        await using var verify=f.Context();
        Assert.Equal(bom.FinishedProductId,(await f.Boms(verify).GetAsync(bom.Id,default)).FinishedProductId);
        Assert.Equal(order.Materials.ToArray(),(await f.Production(verify).GetAsync(order.Id,default)).Materials.ToArray());
    }

    [ProductionSqlFact]
    public async Task InvalidBomAndInactiveOrWrongTypeItemsDoNotLeavePartialOrders()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Production"); var bom=await Setup(f);
        await using(var db=f.Context()) Assert.Equal(404,(await Assert.ThrowsAsync<ApiException>(()=>f.Production(db).CreateAsync(new(){BomId=int.MaxValue,Quantity=1},default))).StatusCode);
        foreach(var id in bom.Details.Select(x=>x.RawMaterialId).Append(bom.FinishedProductId)) {
            foreach(var inactive in new[]{true,false}) {
                await using var db=f.Context();
                // Chỉ thay Item setup do fixture sở hữu, phục hồi giữa các case.
                var item=await db.Items.SingleAsync(x=>x.Id==id);
                var originalType=item.ItemType;
                if(inactive) item.IsActive=false; else item.ItemType=id==bom.FinishedProductId ? ItemType.RawMaterial : ItemType.FinishedProduct;
                await db.SaveChangesAsync();
                await using(var attempt=f.Context())
                    await Conflict(()=>f.Production(attempt).CreateAsync(new(){BomId=bom.Id,Quantity=1},default));
                Assert.Empty(await db.ProductionOrders.ToArrayAsync());
                Assert.Empty(await db.ProductionOrderMaterials.ToArrayAsync());
                item.IsActive=true; item.ItemType=originalType; await db.SaveChangesAsync();
            }
        }
        // Inactive BOM được kiểm tra bằng service thực; dữ liệu setup thuộc fixture.
        await using(var db=f.Context()) await f.Boms(db).DeactivateAsync(bom.Id,bom.RowVersion,default);
        await using(var db=f.Context()) await Conflict(()=>f.Production(db).CreateAsync(new(){BomId=bom.Id,Quantity=1},default));
        await using var verify=f.Context(); Assert.Empty(await verify.ProductionOrders.ToArrayAsync()); Assert.Empty(await verify.ProductionOrderMaterials.ToArrayAsync());
    }

    [ProductionSqlFact]
    public async Task DemandOverflowAndConfirmedStateDoNotMutateAnOrder()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Production"); var bom=await Setup(f);
        var before=await Snapshot(f);
        await using(var db=f.Context()) Assert.Equal(400,(await Assert.ThrowsAsync<ApiException>(()=>f.Production(db).CreateAsync(new(){BomId=bom.Id,Quantity=999999999999999.999m},default))).StatusCode);
        Assert.Equal(before,await Snapshot(f));
        var order=await Create(f,bom.Id); before=await Snapshot(f);
        await using(var db=f.Context()) Assert.Equal(400,(await Assert.ThrowsAsync<ApiException>(()=>f.Production(db).UpdateAsync(order.Id,new(){Quantity=999999999999999.999m,RowVersion=order.RowVersion},default))).StatusCode);
        Assert.Equal(before,await Snapshot(f));
        // Chỉ dựng trạng thái terminal bằng dữ liệu fixture để thử guard, không triển khai confirm.
        await using(var db=f.Context()) {
            var entity=await db.ProductionOrders.Include(x=>x.Materials).SingleAsync();
            entity.Status=DocumentStatus.Confirmed; entity.ConfirmedAt=DateTimeOffset.UtcNow; entity.ConfirmedByUserId=f.ActorId;
            foreach(var m in entity.Materials) m.ActualQuantity=m.RequiredQuantity;
            await db.SaveChangesAsync();
        }
        before=await Snapshot(f);
        await using(var db=f.Context()) {
            var current=await f.Production(db).GetAsync(order.Id,default);
            await Conflict(()=>f.Production(db).UpdateAsync(order.Id,new(){Quantity=4,RowVersion=current.RowVersion},default));
        }
        await using(var db=f.Context()) {
            var current=await f.Production(db).GetAsync(order.Id,default);
            await Conflict(()=>f.Production(db).CancelAsync(order.Id,current.RowVersion,default));
        }
        Assert.Equal(before,await Snapshot(f));
    }

    [ProductionSqlFact]
    public async Task SqlConstraintsDeleteBehaviorPrecisionAndHistoryMatchDesign()
    {
        await using var f=await MasterDataSqlFixture.OpenAsync("Production"); var bom=await Setup(f);
        var order=await Create(f,bom.Id,1.001m);
        Assert.Equal(0.334m,order.Materials[1].RequiredQuantity);
        var cases=new (string Sql,string Constraint,int[] Errors)[] {
            ("INSERT INTO ProductionOrders(OrderNumber,BomId,BomVersionSnapshot,FinishedProductId,Quantity,Status,CreatedAt,CreatedByUserId) SELECT OrderNumber,BomId,BomVersionSnapshot,FinishedProductId,Quantity,Status,CreatedAt,CreatedByUserId FROM ProductionOrders","IX_ProductionOrders_OrderNumber",[2601,2627]),
            ("INSERT INTO ProductionOrderMaterials(ProductionOrderId,RawMaterialId,QuantityPerUnitSnapshot,RequiredQuantity) SELECT ProductionOrderId,RawMaterialId,QuantityPerUnitSnapshot,RequiredQuantity FROM ProductionOrderMaterials","IX_ProductionOrderMaterials_ProductionOrderId_RawMaterialId",[2601,2627]),
            ("UPDATE ProductionOrders SET BomId=2147483647","FK_ProductionOrders_BillOfMaterials_BomId",[547]),
            ("UPDATE ProductionOrders SET FinishedProductId=2147483647","FK_ProductionOrders_Items_FinishedProductId",[547]),
            ("UPDATE ProductionOrderMaterials SET ProductionOrderId=2147483647","FK_ProductionOrderMaterials_ProductionOrders_ProductionOrderId",[547]),
            ("UPDATE ProductionOrderMaterials SET RawMaterialId=2147483647 WHERE Id=(SELECT MIN(Id) FROM ProductionOrderMaterials)","FK_ProductionOrderMaterials_Items_RawMaterialId",[547]),
            ("UPDATE ProductionOrders SET Quantity=0","CK_ProductionOrders_Quantity",[547]),
            ("UPDATE ProductionOrders SET Status=99","CK_ProductionOrders_Status",[547]),
            ("UPDATE ProductionOrders SET BomVersionSnapshot=0","CK_ProductionOrders_BomVersion",[547]),
            ("UPDATE ProductionOrderMaterials SET QuantityPerUnitSnapshot=0","CK_ProductionOrderMaterials_PerUnit",[547]),
            ("UPDATE ProductionOrderMaterials SET RequiredQuantity=0","CK_ProductionOrderMaterials_Required",[547]),
            ("UPDATE ProductionOrderMaterials SET ActualQuantity=-1","CK_ProductionOrderMaterials_Actual",[547]),
            ("DELETE FROM ProductionOrders","FK_ProductionOrderMaterials_ProductionOrders_ProductionOrderId",[547]) };
        foreach(var test in cases) {
            await using var db=f.Context(); await using var tx=await db.Database.BeginTransactionAsync();
            var error=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlRawAsync(test.Sql));
            Assert.Contains(error.Number,test.Errors); Assert.Contains(test.Constraint,error.Message); await tx.RollbackAsync();
        }
        await using var verify=f.Context(); Assert.Equal(order.RowVersion,(await f.Production(verify).GetAsync(order.Id,default)).RowVersion);
        await verify.Database.OpenConnectionAsync();
        var fks=await IdentityDatabaseVerifier.ReadRowsAsync(verify,"SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('ProductionOrders'),OBJECT_ID('ProductionOrderMaterials'))");
        Assert.Equal(8,fks.Count); Assert.All(fks,x=>Assert.Equal("NO_ACTION",x));
        Assert.Equal(9,(await verify.Database.GetAppliedMigrationsAsync()).Count()); Assert.Empty(await verify.Database.GetPendingMigrationsAsync());
    }

    private sealed class InvalidMaterialBeforeSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            data.Context!.ChangeTracker.Entries<ProductionOrderMaterial>().First(x=>x.State==EntityState.Added).Entity.RequiredQuantity=0;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken ct=default)
            => throw new InvalidOperationException("Test-only failure before commit.");
    }
    private sealed class TwoHeaders(string fragment) : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Collections.Concurrent.ConcurrentDictionary<Guid,bool> ConnectionIds {get;}=new();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        {
            if(command.CommandText.Contains(fragment,StringComparison.Ordinal)) {
                ConnectionIds.TryAdd(((SqlConnection)command.Connection!).ClientConnectionId,true);
                if(Interlocked.Increment(ref arrivals)==2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20),ct);
            }
            return result;
        }
    }
}
