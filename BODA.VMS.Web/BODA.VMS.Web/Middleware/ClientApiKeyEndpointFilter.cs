using Microsoft.Extensions.Options;

namespace BODA.VMS.Web.Middleware;

/// <summary>
/// Minimal API endpoint filter — VMS 머신 endpoint 의 X-API-Key 헤더 검증.
///
/// 동작 (ClientApiKeyOptions.Required 기준):
/// - true:  헤더 없거나 불일치 → 401 Unauthorized
/// - false: 헤더 있으면 일치 검증 (불일치시 401), 헤더 없으면 통과 (호환 모드)
///
/// 사용:
///   app.MapPost("/api/clients/heartbeat", ...)
///      .AllowAnonymous()
///      .AddEndpointFilter&lt;ClientApiKeyEndpointFilter&gt;();
/// </summary>
public sealed class ClientApiKeyEndpointFilter : IEndpointFilter
{
    private readonly IOptions<ClientApiKeyOptions> _options;
    private readonly ILogger<ClientApiKeyEndpointFilter> _logger;

    public ClientApiKeyEndpointFilter(
        IOptions<ClientApiKeyOptions> options,
        ILogger<ClientApiKeyEndpointFilter> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var opts = _options.Value;
        var http = context.HttpContext;

        // 서버에 키가 설정 안 됐으면 검증 자체 불가 — Required=true 시 startup 검증으로
        // 사전 차단 권장이지만, 안전한 fallback: 통과 + 경고 로그
        if (string.IsNullOrWhiteSpace(opts.Value))
        {
            if (opts.Required)
            {
                _logger.LogError("ClientApiKey:Required=true 인데 Value 가 설정되지 않음. " +
                                "user-secrets 또는 환경변수 ClientApiKey__Value 설정 필요.");
                return Results.Problem(
                    title: "Server misconfiguration",
                    detail: "ClientApiKey not configured on server.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
            return await next(context);
        }

        var provided = http.Request.Headers[ClientApiKeyOptions.HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(provided))
        {
            // 헤더 없음 → Required 면 차단, 아니면 호환 모드로 통과
            if (opts.Required)
            {
                var remoteIp = http.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";
                _logger.LogWarning("Missing {Header} for {Method} {Path} from {Ip}",
                    ClientApiKeyOptions.HeaderName, http.Request.Method, http.Request.Path, remoteIp);
                return Results.Unauthorized();
            }
            return await next(context);
        }

        // 헤더 존재 — 항상 검증 (상수 시간 비교)
        if (!CryptographicEquals(provided, opts.Value))
        {
            var remoteIp = http.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";
            _logger.LogWarning("Invalid {Header} for {Method} {Path} from {Ip}",
                ClientApiKeyOptions.HeaderName, http.Request.Method, http.Request.Path, remoteIp);
            return Results.Unauthorized();
        }

        return await next(context);
    }

    /// <summary>
    /// 상수 시간 문자열 비교 — timing attack 방지.
    /// </summary>
    private static bool CryptographicEquals(string a, string b)
    {
        var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
        var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
