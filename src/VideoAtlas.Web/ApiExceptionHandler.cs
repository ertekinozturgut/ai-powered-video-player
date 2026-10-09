using Microsoft.AspNetCore.Diagnostics;
using VideoAtlas.Application;
using VideoAtlas.Infrastructure;

namespace VideoAtlas.Web;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, message) = exception switch
        {
            AppException ex => (ex.Status, ex.Code, ex.Message),
            AiException ex => (503, ex.Code, "AI hizmeti yanıt veremedi. Sunucu yapılandırmasını/kotasını kontrol edip tekrar deneyin."),
            HttpRequestException or System.Text.Json.JsonException => (503, "ai_unavailable", "AI hizmetinden geçerli bir yanıt alınamadı. Lütfen tekrar deneyin."),
            OperationCanceledException => (504, "request_timeout", "İşlemin yanıt süresi doldu. Lütfen tekrar deneyin."),
            BadHttpRequestException => (400, "bad_request", "İstek biçimi veya boyutu geçersiz."),
            _ => (500, "server_error", "İşlem tamamlanamadı. Lütfen tekrar deneyin.")
        };
        logger.LogWarning("Request failed: {ErrorCode} {TraceId}", code, context.TraceIdentifier);
        if (status == 500) logger.LogError("Unhandled {ExceptionType} at {StackTrace}", exception.GetType().Name, exception.StackTrace);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new()
        {
            HttpContext = context,
            ProblemDetails = new() { Status = status, Title = message, Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier } }
        });
    }
}
