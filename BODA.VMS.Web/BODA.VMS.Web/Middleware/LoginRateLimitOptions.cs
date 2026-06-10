namespace BODA.VMS.Web.Middleware;

/// <summary>
/// 로그인 endpoint (/api/auth/login, /api/kiosk/login) rate limiting 설정.
/// IP 단위 fixed window — brute-force 1차 방어선 (2차는 계정 단위 잠금
/// <see cref="Services.AccountLockoutOptions"/>).
/// 키오스크 PIN(4-8자리 숫자)은 조합 공간이 작아 IP rate limit 이 핵심 방어.
/// </summary>
public class LoginRateLimitOptions
{
    public const string SectionName = "LoginRateLimit";

    /// <summary>RequireRateLimiting 에 사용하는 정책 이름.</summary>
    public const string PolicyName = "login";

    /// <summary>윈도우당 허용 시도 횟수 (기본 5). 초과 시 429.</summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>윈도우 길이 (초, 기본 60).</summary>
    public int WindowSeconds { get; set; } = 60;
}
