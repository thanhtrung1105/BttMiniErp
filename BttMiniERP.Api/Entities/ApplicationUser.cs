using Microsoft.AspNetCore.Identity;

namespace BttMiniERP.Api.Entities;

// P1.3 chỉ có mô hình lưu trữ; chưa đăng ký login, JWT hoặc seed tài khoản.
public sealed class ApplicationUser : IdentityUser;
