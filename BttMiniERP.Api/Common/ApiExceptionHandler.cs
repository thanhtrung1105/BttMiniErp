using Microsoft.AspNetCore.Diagnostics;

namespace BttMiniERP.Api.Common;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var error = exception as ApiException;
        if (error == null)
        {
            // .NET 10 tắt diagnostics mặc định khi handler trả true.
            // Không log exception.Message/ToString/body vì có thể chứa secrets.
            logger.LogError(new EventId(1400, "UnexpectedApiError"),
                "Lỗi API không dự kiến. TraceId={TraceId}, ExceptionType={ExceptionType}",
                context.TraceIdentifier, exception.GetType().FullName);
        }
        await Results.Problem(statusCode: error?.StatusCode ?? 500,
            title: error?.Message ?? "Có lỗi khi xử lý yêu cầu.",
            instance: context.Request.Path,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        return true;
    }
}
