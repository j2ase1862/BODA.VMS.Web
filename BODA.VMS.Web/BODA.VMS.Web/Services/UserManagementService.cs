using System.Security.Cryptography;
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

    public async Task<(bool Success, string? TempPassword)> ResetPasswordAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return (false, null);

        var tempPassword = GenerateTempPassword();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
        user.MustChangePassword = true;   // 다음 로그인 시 변경 강제
        user.FailedLoginCount = 0;        // 초기화는 잠금 해제도 겸함
        user.LockoutUntil = null;
        await _db.SaveChangesAsync();
        return (true, tempPassword);
    }

    /// <summary>
    /// 임시 비밀번호 생성 — 12자, 대문자/소문자/숫자/특수 4종 각 1자 이상 보장
    /// (KISA 복잡도 정책 충족). 혼동 문자(I/l/O/0/1) 제외, CSPRNG 사용.
    /// </summary>
    private static string GenerateTempPassword()
    {
        const string upper = "ABCDEFGHJKMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^*-_=+";
        const int length = 12;

        var all = upper + lower + digits + special;
        var chars = new char[length];
        chars[0] = Pick(upper);
        chars[1] = Pick(lower);
        chars[2] = Pick(digits);
        chars[3] = Pick(special);
        for (var i = 4; i < length; i++)
            chars[i] = Pick(all);

        // Fisher–Yates 셔플 — 문자 종류별 위치가 고정되지 않도록
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);

        static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
    }
}
