# Kế hoạch triển khai Phase 0

## Phát hiện môi trường

- Repository mới: chỉ có thư mục `.git`, branch `main` chưa có commit và chưa có source/solution/cấu hình.
- Có .NET SDK `10.0.402`, runtime ASP.NET Core `10.0.13`; có thể target `net10.0`.
- Có Git `2.56.0.windows.2`; remote `origin` đã cấu hình nhưng remote hiện báo `[gone]` trong trạng thái local.
- Dịch vụ `SQL Server (MSSQLSERVER)` đang chạy tự động. Không có `sqlcmd`, connection string, database dự án hoặc thông tin đăng nhập được phát hiện; không thể xác nhận kết nối.
- Không có workload .NET nào được cài; không phải blocker hiển nhiên cho Web API, nhưng package restore và tooling EF sẽ cần được xác nhận ở Phase 1.

## Roadmap tám tuần

| Tuần | Kết quả dự kiến |
|---|---|
| 1 | Tạo solution/sáu project, dependency direction, CI cơ bản, cấu hình dev secrets và Identity/JWT skeleton. |
| 2 | Migration đầu tiên, master data Category/Product/Customer/Supplier, validation, Swagger và unit test nền. |
| 3 | Purchase receipt Draft/confirm, transaction kho tăng, integration test SQL Server. |
| 4 | Sales order Draft/confirm, chặn tồn âm, test cạnh tranh và idempotency. |
| 5 | Inventory balance/history, truy vấn phân trang/filter, hardening authorization/audit/error handling. |
| 6 | Báo cáo mua/bán/tồn, tối ưu index/truy vấn, kiểm thử API end-to-end. |
| 7 | Tăng độ phủ test, review bảo mật, load/concurrency checks có kiểm soát, tài liệu API và vận hành. |
| 8 | UAT/demo, sửa lỗi, chuẩn bị migration triển khai, README và rà soát toàn bộ tài liệu tiếng Việt. |

## Checklist triển khai

- [ ] Được phê duyệt connection string và chiến lược database dev/test.
- [ ] Tạo solution/project và khóa dependency đúng `AGENTS.md`.
- [ ] Cấu hình packages tương thích .NET 10, Identity, EF Core SQL Server, JWT, Swagger và xUnit.
- [ ] Tạo entity/mapping/migration đã review; xin phê duyệt trước khi áp dụng database.
- [ ] Implement auth/role policy và ProblemDetails thống nhất.
- [ ] Implement master data và test validation.
- [ ] Implement Draft/confirm purchase, sales và invariant inventory.
- [ ] Viết unit + integration tests, gồm concurrent sales và rollback.
- [ ] Implement reports, Swagger thực tế, logging và tài liệu khớp mã.
- [ ] Chạy/test thật, ghi kết quả thực tế, review trước demo/release.

## Rủi ro kỹ thuật và giảm thiểu

| Rủi ro | Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Thiếu connection string/quyền SQL | Chặn migration và integration test | Xin cấu hình dev an toàn; xác nhận kết nối trước Phase 1. |
| Confirm đồng thời | Tồn kho âm hoặc lệch lịch sử | Atomic transaction, conditional update/lock, rowversion, test song song. |
| Retry HTTP | Ghi kho hai lần | Status check, unique source transaction và cân nhắc Idempotency-Key. |
| Sửa dữ liệu đã chốt | Sai sổ kho/báo cáo | State machine, authorization, không hard delete; adjustment document về sau. |
| Secrets bị commit | Lộ DB/JWT | User Secrets/biến môi trường, `.gitignore`, review. |
| Lệch phiên bản package/tooling .NET 10 | Restore/build lỗi | Pin phiên bản tương thích, chạy restore/build sớm ở Phase 1. |
| Test không giống SQL Server thật | Bỏ sót transaction/concurrency | Integration test trên SQL Server riêng, không chỉ EF InMemory. |

## Bước tiếp theo chính xác

Sau khi bạn review và phê duyệt Phase 0, Phase 1 bắt đầu bằng việc tạo solution sáu project target `net10.0`, thêm cấu trúc dependency rỗng và thiết lập test; chưa áp dụng migration hay tạo database cho đến khi có phê duyệt connection string/database.
