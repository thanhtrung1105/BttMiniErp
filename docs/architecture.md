# Kiến trúc dự kiến

> Trạng thái Phase 0: repository chưa có mã nguồn hay solution. Nội dung này là thiết kế để triển khai, không mô tả các lớp/method đã tồn tại.

## Mục tiêu

Backend phục vụ doanh nghiệp thương mại nhỏ, ưu tiên tính đúng đắn của chứng từ và tồn kho. Kiến trúc Clean Architecture thực dụng tách luật nghiệp vụ khỏi HTTP và SQL Server, nhưng tránh thêm CQRS/event bus phức tạp khi chưa cần.

## Sáu dự án

| Dự án | Trách nhiệm | Tham chiếu được phép |
|---|---|---|
| `BttMiniERP.Domain` | Entity, enum, value object nhẹ, luật bất biến cốt lõi | Không có |
| `BttMiniERP.Application` | Use case, DTO, command/query, validation, interface persistence/dịch vụ | Domain |
| `BttMiniERP.Infrastructure` | `ErpDbContext`, EF mapping, repository/use-case support, Identity, JWT | Application, Domain |
| `BttMiniERP.Api` | Controller/minimal endpoint, DI, authentication, Swagger, exception mapping | Application, Infrastructure |
| `BttMiniERP.UnitTests` | Kiểm thử entity và application không cần DB | Domain, Application |
| `BttMiniERP.IntegrationTests` | Kiểm thử HTTP + EF + SQL Server, transaction và đồng thời | Api, Infrastructure |

## Luồng xử lý

```text
HTTP request -> API Controller -> Application use case -> repository/DbContext abstraction
                         |                                      |
                      JWT/role                             Infrastructure/EF Core
                                                                |
                                                           SQL Server
```

Ví dụ xác nhận phiếu nhập: `PurchaseReceiptsController` (dự kiến) nhận lệnh confirm, gọi use case Application. Use case mở transaction qua abstraction, khóa/cập nhật số dư, tạo lịch sử kho, đổi trạng thái và lưu một lần. Infrastructure hiện thực bằng EF Core/SQL Server. API chuyển lỗi validation, unauthorized, conflict và business rule thành ProblemDetails.

## Xác thực và quan sát

- ASP.NET Core Identity quản lý người dùng, mật khẩu, role; JWT bearer dùng cho API.
- Role ban đầu: `Admin`, `Manager`, `WarehouseStaff`, `SalesStaff`, `PurchasingStaff`, `Viewer`.
- Swagger/OpenAPI khai báo Bearer security scheme; chỉ bật giao diện phù hợp theo môi trường khi triển khai.
- Log cấu trúc cần chứa correlation/trace id, user id, document id nhưng không ghi mật khẩu, JWT hay connection string.

## Cấu hình dự kiến

`Api` sẽ đọc `ConnectionStrings:DefaultConnection` và phần cấu hình JWT từ biến môi trường/secret store ở môi trường thật. Không commit mật khẩu hoặc JWT signing key. `appsettings.Development.json` chỉ chứa mẫu hoặc dùng User Secrets. Chưa có bất kỳ tệp cấu hình nào trong Phase 0.

## Hướng dẫn triển khai/gỡ lỗi sau này

Khi solution tồn tại, chạy `dotnet test` tại root để chạy test; chạy riêng project test khi cần cô lập lỗi. Các lỗi thường gặp: connection string thiếu/sai quyền, JWT issuer/audience/key không khớp, migration chưa áp dụng, hoặc package EF/Identity lệch major version với .NET. Tài liệu này phải được sửa để nêu tên lớp/method thật sau khi chúng được tạo.
