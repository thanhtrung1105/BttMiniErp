# Kế hoạch API dự kiến

## P5.2 — Xác nhận đơn bán

`POST /api/v1/sales-orders/{id}/confirm` chỉ nhận `{"rowVersion":"…"}` từ lần đọc chi tiết. Admin, Manager, SalesStaff và WarehouseStaff được xác nhận. Thành công trả 200 với chi tiết đơn Confirmed, phiên bản mới và audit. Body hoặc phiên bản sai định dạng trả 400, thiếu JWT 401, thiếu quyền 403, không tìm thấy đơn 404. Phiên bản cũ, đơn đã kết thúc, đơn rỗng, khách hàng hoặc thành phẩm ngừng hoạt động và thiếu tồn trả 409 ProblemDetails. [Luồng xử lý](features/sales.md).

## P5.1 — Soạn đơn bán thành phẩm

Mọi route dùng JWT. Admin, Manager, SalesStaff đọc và ghi. WarehouseStaff chỉ đọc. PurchasingStaff, ProductionStaff và Viewer không có quyền. Thiếu JWT trả 401, thiếu quyền trả 403.

| Method | Route | Request và kết quả |
|---|---|---|
| GET | `/api/v1/sales-orders` | Lọc customerId, status, from (bao gồm), to (loại trừ) theo OrderDate UTC. Page từ 1, pageSize 1–100, sortBy orderDate/id, sortDirection asc/desc. Mặc định 20 dòng, orderDate giảm dần rồi Id giảm dần. Trả 200, items/page/pageSize/totalCount. |
| POST | `/api/v1/sales-orders` | CustomerId và lines bắt buộc; orderDate và note tùy chọn. Trả 201, Location và chi tiết Draft. |
| GET | `/api/v1/sales-orders/{id}` | Trả 200 kèm các dòng theo LineNumber. |
| PUT | `/api/v1/sales-orders/{id}` | Thay toàn bộ nội dung Draft, gửi rowVersion trong body. Trả 200 cùng phiên bản mới. |
| POST | `/api/v1/sales-orders/{id}/cancel` | Chỉ nhận rowVersion. Trả 200 cùng chi tiết Cancelled. |

Ví dụ body tạo đơn (dùng ID khách hàng và thành phẩm thực tế):

```json
{"customerId":1,"lines":[{"itemId":2,"quantity":0.125,"unitPrice":0.20}]}
```

Server tính lineTotal = 0.03. Trường ngoài DTO, dòng rỗng/trùng, precision sai, RowVersion sai định dạng trả 400 ProblemDetails. Customer/Item/đơn không tồn tại trả 404. Tham chiếu inactive/sai loại, trùng mã server, trạng thái hoặc phiên bản xung đột trả 409. Không có DELETE hoặc confirm trong P5.1.

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
