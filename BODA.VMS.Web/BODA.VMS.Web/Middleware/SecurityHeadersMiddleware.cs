namespace BODA.VMS.Web.Middleware;

/// <summary>
/// GS 인증 baseline 보안 헤더 추가.
/// X-Frame-Options / X-Content-Type-Options / Referrer-Policy / Permissions-Policy /
/// Content-Security-Policy (Blazor WASM 호환).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Blazor WASM 호환 CSP. 핵심 결정:
    /// - script-src 'self' 'wasm-unsafe-eval' — WebAssembly 인스턴스화에 필수
    /// - style-src 'self' 'unsafe-inline' — MudBlazor 컴포넌트가 인라인 스타일 사용 (CSP 가이드상 style 인라인은 허용 가능)
    /// - connect-src 'self' ws: wss: — SignalR WebSocket / LongPolling
    /// - img-src 'self' data: blob: — 검사 이미지 + Blazor 로고 등 data URI
    /// - font-src 'self' data: — 로컬 폰트 + data URI
    /// - object-src 'none' — Flash 등 플러그인 차단
    /// - base-uri 'self' — base 태그 hijack 방어
    /// - frame-ancestors 'self' — X-Frame-Options 와 중복 보강 (CSP3 표준)
    /// - form-action 'self' — 외부 폼 제출 차단
    /// </summary>
    public const string DefaultContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self' ws: wss:; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'self'; " +
        "form-action 'self'";

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // 클릭재킹 방어 — 동일 origin iframe 만 허용 (Blazor 자체 호스팅 시 SAMEORIGIN, 외부 임베드 없으면 DENY)
        headers["X-Frame-Options"] = "SAMEORIGIN";

        // MIME 스니핑 방어 — 서버 명시 Content-Type 강제
        headers["X-Content-Type-Options"] = "nosniff";

        // Referrer 정보 노출 최소화
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // 기능 권한 최소화 — 카메라/마이크/위치 비활성
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        // CSP — Blazor WASM 호환 ('wasm-unsafe-eval'). MudBlazor 인라인 스타일을 위해 'unsafe-inline' 허용.
        // 운영에서 정책 조정 필요시 SecurityHeaders:ContentSecurityPolicy 환경변수로 override.
        var customCsp = context.RequestServices
            .GetService<IConfiguration>()?
            ["SecurityHeaders:ContentSecurityPolicy"];
        headers["Content-Security-Policy"] = string.IsNullOrWhiteSpace(customCsp)
            ? DefaultContentSecurityPolicy
            : customCsp;

        return _next(context);
    }
}
