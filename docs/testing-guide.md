## P6.1 — Tra cứu kho

Chạy offline `QueryValidationTests` và `InventoryQueryHttpTests`. HTTP test dùng query sai để phân biệt truy cập bị từ chối (401/403) với role được vào MVC validation (400). Phản hồi 200 có dữ liệu thật thuộc test SQL riêng.

Suite `Category=SqlServer&SqlSuite=InventoryQuery` có một test Kestrel. Chỉ chạy khi chủ dự án phê duyệt riêng `localhost/BttMiniERP_SalesConfirmTest`, baseline trống ngoài 10 migration, dữ liệu tạm và cleanup đúng fixture. Test cần các opt-in của fixture Sales/SalesConfirm cùng `BTT_RUN_INVENTORY_QUERY_SQL_TESTS=1`. Fixture tạo mặt hàng/đối tác, nhập 6 NVL, sản xuất 3 thành phẩm rồi bán 2; Viewer phải đọc được 4 biến động và tồn cuối 0 NVL, 1 thành phẩm. Kiểm tra thêm Draft, phân trang, filter nguồn, giới hạn ngày UTC và quyền khi mở chứng từ.
