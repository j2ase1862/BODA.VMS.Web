namespace BODA.VMS.Web.Services;

/// <summary>
/// JWT refresh token 정책. access token(Jwt:ExpireMinutes, 기본 8h)이 만료돼도
/// 이 기간 동안은 재로그인 없이 /api/auth/refresh 로 갱신 가능.
/// 폐기(revocation)는 /api/auth/logout 또는 로테이션으로 처리.
/// </summary>
public class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    /// <summary>refresh token 유효 기간 (일, 기본 7). 0 이하면 refresh token 발급 비활성.</summary>
    public int ExpireDays { get; set; } = 7;
}
