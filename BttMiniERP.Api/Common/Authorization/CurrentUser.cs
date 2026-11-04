using System.Security.Claims;

namespace BttMiniERP.Api.Common.Authorization;

public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public string Id => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } principal
        && principal.FindFirstValue("sub") is { Length: > 0 } id
        ? id : throw new ApiException(401, "Yêu cầu đăng nhập.");
}
