using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BODA.VMS.Web.Middleware;

/// <summary>
/// API 엔드포인트(/api/*, /hubs/* 등) 의 unhandled exception 을 RFC 7807 ProblemDetails JSON 으로 반환.
/// Razor 페이지 / 컴포넌트 요청은 기존 UseExceptionHandler("/Error") 가 처리하므로 false 반환해 fall-through.
/// .NET 8 의 IExceptionHandler 패턴 사용.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;
    private readonly IHostEnvironment _env;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // API/Hub 요청만 처리 — Razor 페이지/컴포넌트는 기존 /Error 핸들러로 위임
        var path = httpContext.Request.Path.Value ?? string.Empty;
        var acceptsJson = httpContext.Request.Headers.Accept.ToString()
            .Contains("application/json", StringComparison.OrdinalIgnoreCase);
        var isApiLikePath = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase);

        if (!isApiLikePath && !acceptsJson)
        {
            return false;
        }

        _logger.LogError(exception,
            "Unhandled exception in API request {Method} {Path}",
            httpContext.Request.Method, path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            Instance = path,
            Detail = _env.IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred. Refer to server logs with the trace id."
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problem.Status.Value;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
