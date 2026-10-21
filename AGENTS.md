# Quy ước ổn định của BttMiniERP

## Phạm vi và kiến trúc

- Dùng .NET 10, ASP.NET Core Web API, EF Core và SQL Server.
- Giữ sáu dự án: `BttMiniERP.Api`, `BttMiniERP.Application`, `BttMiniERP.Domain`, `BttMiniERP.Infrastructure`, `BttMiniERP.UnitTests`, `BttMiniERP.IntegrationTests`.
- Domain không tham chiếu dự án khác. Application chỉ tham chiếu Domain. Infrastructure tham chiếu Application và Domain. Api là composition root, chỉ tham chiếu Application và Infrastructure. Test tham chiếu đúng lớp đang kiểm thử.
- Không đưa `DbContext`, kiểu EF Core, controller, DTO HTTP hoặc chi tiết JWT vào Domain.
- Application định nghĩa use case, DTO/command/query, abstraction repository/clock/current-user và validation. Infrastructure hiện thực persistence, Identity, JWT và tích hợp ngoài. Api chỉ lo HTTP, ủy quyền và ánh xạ request/response.

## Quy ước mã nguồn

- Bật nullable reference types; dùng `async`/`await` và truyền `CancellationToken` ở biên API/use case.
- Tên public bằng tiếng Anh, PascalCase; biến cục bộ và tham số camelCase. Mã số chứng từ do server tạo, không nhận từ client.
- Dùng DTO request/response, không trả trực tiếp entity EF Core. Controller mỏng; không viết luật nghiệp vụ trong controller.
- Dùng `decimal(18,2)` cho tiền và `decimal(18,3)` cho số lượng/đơn giá khi migration được tạo. Thời gian lưu UTC (`DateTimeOffset`).
- Mọi lỗi nghiệp vụ dự kiến phải trở thành ProblemDetails nhất quán; không nuốt exception.

## Bất biến nghiệp vụ

- Chứng từ Draft không thay đổi tồn kho. Chỉ thao tác xác nhận hợp lệ được phép tạo `InventoryTransaction` và cập nhật `InventoryBalance`.
- Mỗi PurchaseReceipt/SalesOrder chỉ xác nhận một lần. Toàn bộ đổi trạng thái, số dư và lịch sử kho phải nằm trong một transaction cơ sở dữ liệu.
- Không cho tồn kho âm. Phải kiểm tra đồng thời với thao tác giảm số dư, không chỉ kiểm tra từ giá trị đã đọc trước đó.
- Chứng từ Confirmed/Cancelled không được sửa hoặc xóa tùy ý. Không xóa lịch sử giao dịch kho.
- Mọi stock-changing use case phải ghi người thực hiện và thời điểm.

## Kiểm thử và tài liệu

- Mỗi luật nghiệp vụ mới cần unit test; luồng persistence, transaction và cạnh tranh tồn kho cần integration test với SQL Server phù hợp.
- Test phải độc lập, đặt tên theo hành vi, và không công bố kết quả là đã chạy khi chưa chạy thật.
- Toàn bộ tài liệu dự án viết bằng tiếng Việt và phải phản ánh mã nguồn hiện có. Khi chưa triển khai, ghi rõ là “dự kiến”.
- Mỗi thay đổi API/schema phải cập nhật tài liệu liên quan trong `docs/`, bao gồm endpoint, quyền, migration và cách kiểm thử/gỡ lỗi.
- Không chạy migration, tạo/sửa dữ liệu hay thay đổi SQL Server nếu chưa có phê duyệt rõ ràng của người dùng.
