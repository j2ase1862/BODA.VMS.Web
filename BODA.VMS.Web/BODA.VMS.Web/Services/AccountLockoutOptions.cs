namespace BODA.VMS.Web.Services;

/// <summary>
/// 웹 계정(Users) 연속 로그인 실패 잠금 정책 — brute-force 2차 방어선
/// (1차는 IP 단위 <see cref="Middleware.LoginRateLimitOptions"/>).
/// 키오스크 Operator 에는 적용하지 않음 — 현장 라인 가용성 우선 (잠금으로
/// 생산 라인이 멈추는 것 방지, PIN brute-force 는 IP rate limit 으로 방어).
/// </summary>
public class AccountLockoutOptions
{
    public const string SectionName = "AccountLockout";

    /// <summary>연속 실패 허용 횟수 — 도달 시 잠금 (기본 5). 0 이하면 잠금 비활성.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>잠금 유지 시간 (분, 기본 15).</summary>
    public int LockoutMinutes { get; set; } = 15;
}
