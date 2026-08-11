using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IUserManagementService
{
    Task<List<UserDto>> GetPendingUsersAsync();
    Task<List<UserDto>> GetAllUsersAsync();
    Task<bool> ApproveUserAsync(int userId, string role);
    Task<bool> RejectUserAsync(int userId);

    /// <summary>
    /// 비밀번호 초기화 (2026-08-11 재설계) — 서버가 임시 비밀번호를 생성해 해시만 저장하고
    /// 평문은 반환값으로 1회 노출. MustChangePassword=true 로 다음 로그인 시 변경 강제,
    /// 잠금 상태(FailedLoginCount/LockoutUntil)도 함께 해제. 사용자 없으면 (false, null).
    /// </summary>
    Task<(bool Success, string? TempPassword)> ResetPasswordAsync(int userId);

    /// <summary>
    /// 승인된 사용자의 역할 변경. 실패 사유를 메시지로 반환 —
    /// 마지막 Admin 강등은 차단 (관리 불능 상태 방지).
    /// </summary>
    Task<(bool Success, string? Error)> ChangeRoleAsync(int userId, string role);

    /// <summary>
    /// 사용자 삭제 (Admin 전용, 2026-08-11). 자기 자신·최초 admin 계정·
    /// 마지막 Admin 은 차단. 승인 대기 사용자는 기존 Reject 와 동일 효과.
    /// </summary>
    Task<(bool Success, string? Error)> DeleteUserAsync(int userId, int actingUserId);
}
