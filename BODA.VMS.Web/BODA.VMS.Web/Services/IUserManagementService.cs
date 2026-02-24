using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IUserManagementService
{
    Task<List<UserDto>> GetPendingUsersAsync();
    Task<List<UserDto>> GetAllUsersAsync();
    Task<bool> ApproveUserAsync(int userId, string role);
    Task<bool> RejectUserAsync(int userId);
    Task<bool> ResetPasswordAsync(int userId, string newPassword);
}
