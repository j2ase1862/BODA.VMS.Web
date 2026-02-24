using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class UserManagementService : IUserManagementService
{
    private readonly BodaVmsDbContext _db;

    public UserManagementService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<UserDto>> GetPendingUsersAsync()
    {
        return await _db.Users
            .Where(u => !u.IsApproved && u.Role == "Pending")
            .OrderBy(u => u.CreatedAt)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                Role = u.Role,
                IsApproved = u.IsApproved,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        return await _db.Users
            .OrderBy(u => u.Username)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                Role = u.Role,
                IsApproved = u.IsApproved,
                CreatedAt = u.CreatedAt,
                ApprovedAt = u.ApprovedAt
            })
            .ToListAsync();
    }

    public async Task<bool> ApproveUserAsync(int userId, string role)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return false;

        user.IsApproved = true;
        user.Role = role;
        user.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RejectUserAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return false;

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync();
        return true;
    }
}
