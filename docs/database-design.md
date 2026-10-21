# Thiết kế cơ sở dữ liệu dự kiến

> Phase 0 chưa tạo database, migration hoặc bảng nào. Mọi thay đổi SQL Server cần phê duyệt trước.

## ERD logic

```text
Category 1 --- * Product * --- 1 InventoryBalance
                         |             |
                         |             * --- * InventoryTransaction
Supplier 1 --- * PurchaseReceipt 1 --- * PurchaseReceiptLine --- 1 Product
Customer 1 --- * SalesOrder      1 --- * SalesOrderLine      --- 1 Product
IdentityUser 1 --- * PurchaseReceipt / SalesOrder / InventoryTransaction (CreatedBy/ConfirmedBy)
```

## Bảng miền nghiệp vụ

| Bảng | Khóa/chỉ mục chính | Trường quan trọng | Quan hệ và ràng buộc |
|---|---|---|---|
| `Categories` | PK `Id`; unique `Code` | Name, Description, IsActive | Một category có nhiều product; không xóa khi còn product. |
| `Products` | PK `Id`; unique `Code` | Name, CategoryId, Unit, PurchasePrice, SalePrice, IsActive, RowVersion | Thuộc một category; giá và số lượng không âm. |
| `Customers` | PK `Id`; unique `Code` | Name, Phone, Email, Address, IsActive | Có nhiều sales order. |
| `Suppliers` | PK `Id`; unique `Code` | Name, Phone, Email, Address, TaxCode, IsActive | Có nhiều purchase receipt. |
| `PurchaseReceipts` | PK `Id`; unique `ReceiptNumber` | SupplierId, Status, ReceiptDate, Note, ConfirmedAt, ConfirmedByUserId | Có nhiều line; Draft/Confirmed/Cancelled. |
| `PurchaseReceiptLines` | PK `Id`; unique `(PurchaseReceiptId, LineNumber)` | ProductId, Quantity, UnitPrice, LineTotal | Quantity > 0, UnitPrice >= 0; không sửa khi chứng từ đã Confirmed. |
| `SalesOrders` | PK `Id`; unique `OrderNumber` | CustomerId, Status, OrderDate, Note, ConfirmedAt, ConfirmedByUserId | Có nhiều line; Draft/Confirmed/Cancelled. |
| `SalesOrderLines` | PK `Id`; unique `(SalesOrderId, LineNumber)` | ProductId, Quantity, UnitPrice, LineTotal | Quantity > 0, UnitPrice >= 0. |
| `InventoryBalances` | PK `ProductId` | QuantityOnHand, RowVersion, UpdatedAt | Một dòng mỗi product; `QuantityOnHand >= 0`. |
| `InventoryTransactions` | PK `Id`; unique `(SourceDocumentType, SourceDocumentId, SourceLineId, TransactionType)` | ProductId, QuantityDelta, BalanceAfter, OccurredAt, PerformedByUserId | Lịch sử bất biến; FK tới Product. Unique index bảo vệ idempotency xác nhận. |

Identity tạo các bảng chuẩn (người dùng, role, user-role, claims, tokens). Khóa người xác nhận/người thực hiện tham chiếu người dùng Identity bằng ID chuỗi.

## Kiểu dữ liệu và audit

- PK miền: `uniqueidentifier` do server tạo; mã nghiệp vụ là chuỗi có unique index.
- Số lượng: `decimal(18,3)`; tiền và tổng: `decimal(18,2)`; không dùng `float`/`double`.
- Thời điểm: `datetimeoffset` UTC. Các bảng thay đổi được có `CreatedAt`, `CreatedByUserId`, `UpdatedAt`, `UpdatedByUserId` khi phù hợp.
- `RowVersion` là SQL Server `rowversion`/EF Core concurrency token ở `InventoryBalances`, và nên có ở dữ liệu master/chứng từ để phát hiện ghi đè.
- LineTotal tính/lưu phía server từ Quantity × UnitPrice; quy tắc làm tròn phải thống nhất trước khi triển khai.

## Migration và vận hành

Migration sẽ do Infrastructure chứa và chạy có kiểm soát. Trong development có thể dùng `dotnet ef database update` sau khi được phê duyệt; production nên áp dụng qua pipeline/script đã review, backup và kế hoạch rollback. Không dùng `EnsureCreated` cho database quản lý migration.

## Kiểm tra SQL Server hiện tại

Máy có dịch vụ mặc định `SQL Server (MSSQLSERVER)` đang chạy. Phase 0 không tìm thấy connection string, database dự án, `sqlcmd`, hoặc thông tin xác thực; vì vậy chưa thể xác nhận ứng dụng có thể kết nối. Cần cung cấp/tạo theo quy trình được duyệt một connection string an toàn trước integration test/migration.
