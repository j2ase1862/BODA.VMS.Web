using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IUserManagementService
{
    Task<List<UserDto>> GetPendingUsersAsync();
    Task<List<UserDto>> GetAllUsersAsync();
    Task<bool> ApproveUserAsync(int userId, string role);
    Task<bool> RejectUserAsync(int userId);
    Task<bool> ResetPasswordAsync(int userId, string newPassword);

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
