# Luật nghiệp vụ dự kiến

> Chưa có mã triển khai ở Phase 0. Tên entity và use case trong tài liệu là mục tiêu triển khai, không phải file/class/method hiện hữu.

## Trạng thái chứng từ

| Trạng thái | Ý nghĩa | Có sửa/xóa? | Tác động kho |
|---|---|---|---|
| `Draft` | Đang soạn, chưa chốt | Được sửa line và hủy theo quyền | Không |
| `Confirmed` | Đã chốt thành công | Không sửa/xóa tùy ý | Nhập: tăng; bán: giảm, đúng một lần |
| `Cancelled` | Bị hủy trước xác nhận | Không sửa; không xóa vật lý | Không |

Chỉ cho phép `Draft -> Confirmed` hoặc `Draft -> Cancelled`. Không hỗ trợ hủy/hoàn kho đối với chứng từ đã xác nhận trong phạm vi Phase 0; nếu cần, phải thiết kế chứng từ điều chỉnh/return độc lập thay vì âm thầm sửa lịch sử.

## Quy tắc master data

- Category, product, customer và supplier cần mã duy nhất, tên bắt buộc và trạng thái hoạt động.
- Product bắt buộc category và đơn vị tính; số lượng, đơn giá không âm. Product không hoạt động không được thêm vào chứng từ Draft mới.
- Không hard delete master data đang được tham chiếu; ưu tiên `IsActive = false`.

## Quy tắc purchase receipt và sales order

- Line bắt buộc product, quantity > 0, unit price >= 0; server tính tổng dòng/tổng chứng từ.
- Supplier bắt buộc cho phiếu nhập, customer bắt buộc cho đơn bán.
- Confirm yêu cầu ít nhất một line, trạng thái Draft và quyền phù hợp.
- Confirm phiếu nhập tạo giao dịch kho dương trên từng line và tăng số dư đúng một lần.
- Confirm đơn bán chỉ thành công khi mọi product đủ số lượng; tạo giao dịch âm và giảm số dư đúng một lần.
- Confirmed/Cancelled không được cập nhật line, đối tác hay ngày chứng từ, và không bị xóa vật lý.

## Tồn kho, transaction và cạnh tranh

Một lần confirm thực hiện atomically: đọc/xử lý balance của toàn bộ product theo thứ tự `ProductId`; tạo balance còn thiếu đối với nhập; kiểm tra đủ hàng đối với bán; cập nhật balance; thêm `InventoryTransactions`; đổi status và audit; rồi commit. Mọi lỗi rollback toàn bộ.

Đối với sales, thao tác giảm phải có điều kiện chống âm tại database (ví dụ `UPDATE ... SET QuantityOnHand = QuantityOnHand - @qty WHERE ProductId = @id AND QuantityOnHand >= @qty`) hoặc mức isolation/locking tương đương. `rowversion` phát hiện xung đột optimistic; khi conflict, API trả lỗi conflict có thể retry an toàn. Unique index của nguồn giao dịch cùng status check bảo vệ việc confirm lặp lại/retry.

## Validation, bảo mật, lỗi

- Validate request phía server trước use case; không tin giá, total, trạng thái, user id hay mã chứng từ do client gửi.
- JWT bắt buộc cho mọi API nghiệp vụ. Role/policy kiểm tra tại API và use case nhạy cảm.
- Trả 400 cho input không hợp lệ, 401/403 cho xác thực/quyền, 404 khi không thấy tài nguyên được phép lộ, 409 khi trạng thái/xung đột tồn kho, và 500 chỉ cho lỗi không dự kiến. Format là ProblemDetails.

## Kiểm thử bắt buộc khi triển khai

Unit test: Draft không đổi kho; confirm nhập/bán một lần; bán thiếu hàng bị từ chối; chuyển trạng thái không hợp lệ; confirmed không sửa/xóa. Integration test với SQL Server: rollback khi một line lỗi, index idempotency, authorization, và hai yêu cầu bán đồng thời không tạo số dư âm.
