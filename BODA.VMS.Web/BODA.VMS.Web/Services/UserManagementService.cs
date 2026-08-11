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

    public async Task<(bool Success, string? Error)> ChangeRoleAsync(int userId, string role)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return (false, "User not found");
        if (!user.IsApproved) return (false, "가입 승인 대기 중인 사용자 — 승인 시 역할을 지정하세요");
        if (user.Role == role) return (true, null);   // no-op

        // 마지막 Admin 강등 차단 — Admin 이 0명이 되면 승인/권한 관리가 불가능해진다
        if (user.Role == "Admin" && role != "Admin")
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == "Admin" && u.IsApproved);
            if (adminCount <= 1)
                return (false, "마지막 Admin 계정은 강등할 수 없습니다");
        }

        user.Role = role;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteUserAsync(int userId, int actingUserId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return (false, "User not found");
        if (userId == actingUserId)
            return (false, "자기 자신은 삭제할 수 없습니다");
        if (user.Username == "admin")
            return (false, "최초 admin 계정은 삭제할 수 없습니다");

        // 마지막 Admin 삭제 차단 — 승인/권한 관리 불능 상태 방지 (ChangeRoleAsync 와 동일 원칙)
        if (user.Role == "Admin" && user.IsApproved)
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == "Admin" && u.IsApproved);
            if (adminCount <= 1)
                return (false, "마지막 Admin 계정은 삭제할 수 없습니다");
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return (true, null);
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
