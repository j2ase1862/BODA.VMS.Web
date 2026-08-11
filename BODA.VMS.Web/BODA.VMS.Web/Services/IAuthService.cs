using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);

    /// <summary>refresh token 으로 access token 갱신 (로테이션). 무효/만료/폐기/부적격 시 null.</summary>
    Task<LoginResponse?> RefreshAsync(string refreshToken);

    /// <summary>refresh token 폐기 (로그아웃). 없거나 이미 폐기됐으면 no-op.</summary>
    Task RevokeRefreshTokenAsync(string refreshToken);

    Task<(bool Success, string Message)> RegisterAsync(RegisterRequest request);
    Task<UserDto?> GetUserByIdAsync(int userId);

    /// <summary>
    /// 본인 비밀번호 변경 (2026-08-11) — 현재 비밀번호 검증 후 해시 갱신,
    /// MustChangePassword 해제. 실패 시 사유 메시지 반환.
    /// </summary>
    Task<(bool Success, string? Error)> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}
