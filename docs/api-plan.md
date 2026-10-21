# Kế hoạch API dự kiến

Base path dự kiến: `/api/v1`. Tất cả body/response là JSON, lỗi dùng RFC 7807 ProblemDetails. Các endpoint nghiệp vụ cần JWT Bearer trừ đăng nhập/refresh (nếu refresh token được phê duyệt khi triển khai).

| Nhóm | Endpoint dự kiến | Mục đích | Role tối thiểu |
|---|---|---|---|
| Auth | `POST /auth/login` | Đổi thông tin đăng nhập lấy JWT | Anonymous |
| Auth | `POST /auth/refresh` | Làm mới access token, nếu triển khai refresh token | Anonymous (token refresh hợp lệ) |
| Users | `GET /users`, `POST /users`, `PUT /users/{id}/roles` | Quản lý tài khoản/role | Admin |
| Categories | `GET/POST /categories`, `GET/PUT/DELETE /categories/{id}` | Quản lý loại hàng | Admin, Manager |
| Products | `GET/POST /products`, `GET/PUT/DELETE /products/{id}` | Quản lý hàng hóa | Admin, Manager |
| Customers | `GET/POST /customers`, `GET/PUT/DELETE /customers/{id}` | Quản lý khách hàng | Admin, Manager, SalesStaff |
| Suppliers | `GET/POST /suppliers`, `GET/PUT/DELETE /suppliers/{id}` | Quản lý nhà cung cấp | Admin, Manager, PurchasingStaff |
| Purchase receipts | `GET/POST /purchase-receipts`, `GET/PUT/DELETE /purchase-receipts/{id}` | Xem/tạo/sửa/hủy phiếu Draft | Admin, Manager, PurchasingStaff |
| Purchase receipts | `POST /purchase-receipts/{id}/confirm` | Xác nhận phiếu nhập, tăng kho | Admin, Manager, WarehouseStaff, PurchasingStaff |
| Sales orders | `GET/POST /sales-orders`, `GET/PUT/DELETE /sales-orders/{id}` | Xem/tạo/sửa/hủy đơn Draft | Admin, Manager, SalesStaff |
| Sales orders | `POST /sales-orders/{id}/confirm` | Xác nhận đơn bán, giảm kho | Admin, Manager, WarehouseStaff, SalesStaff |
| Inventory | `GET /inventory/balances`, `GET /inventory/transactions` | Xem số dư và lịch sử kho | Admin, Manager, WarehouseStaff, Viewer |
| Reports | `GET /reports/inventory`, `GET /reports/sales`, `GET /reports/purchases` | Báo cáo theo khoảng ngày/filter | Admin, Manager, Viewer |

## Ma trận quyền chi tiết

| Role | Đọc master | Ghi master | Soạn/chỉnh Draft | Confirm kho | Xem báo cáo |
|---|---:|---:|---:|---:|---:|
| Admin | Có | Có | Có | Có | Có |
| Manager | Có | Có | Có | Có | Có |
| WarehouseStaff | Có | Không | Không | Có | Tồn kho |
| SalesStaff | Product/Customer | Customer | SalesOrder | Có (sales) | Không |
| PurchasingStaff | Product/Supplier | Supplier | PurchaseReceipt | Có (purchase) | Không |
| Viewer | Có | Không | Không | Không | Có |

`DELETE` dự kiến chỉ cho Draft và thực chất chuyển `Cancelled` hoặc soft-delete theo thiết kế cuối cùng; endpoint không bao giờ xóa Confirmed. Cần quyết định rõ tên endpoint `cancel` so với dùng DELETE trước khi bắt đầu implement, để Swagger phản ánh chính xác hành vi.

## Hợp đồng chung cần chốt lúc triển khai

- Danh sách có phân trang, sort và filter với giới hạn kích thước trang.
- Create/Update sử dụng request DTO; client không gửi `Status`, balance, transaction, audit, total tính toán hay mã chứng từ server-generated.
- `POST .../confirm` nên chấp nhận `Idempotency-Key` để retry mạng an toàn; header/cơ chế lưu key cần được thiết kế cùng schema nếu chọn áp dụng.
- Swagger sẽ liệt kê schema thật và ví dụ sau khi controller/DTO tồn tại. Hiện chưa có endpoint nào khả dụng.
