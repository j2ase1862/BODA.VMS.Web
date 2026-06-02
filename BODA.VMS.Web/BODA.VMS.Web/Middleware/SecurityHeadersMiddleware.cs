namespace BODA.VMS.Web.Middleware;

/// <summary>
/// GS 인증 baseline 보안 헤더 추가.
/// X-Frame-Options / X-Content-Type-Options / Referrer-Policy / Permissions-Policy.
/// Content-Security-Policy 는 Blazor WASM 의 wasm-unsafe-eval 호환성 검토 필요해 별도 도입.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

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

        return _next(context);
    }
}
