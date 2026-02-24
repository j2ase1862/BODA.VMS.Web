using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<(bool Success, string Message)> RegisterAsync(RegisterRequest request);
    Task<UserDto?> GetUserByIdAsync(int userId);
}
