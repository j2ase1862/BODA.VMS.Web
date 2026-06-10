using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BODA.VMS.Web.Services;

public class AuthService : IAuthService
{
    private readonly BodaVmsDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly ICurrentUserService _currentUser;
    private readonly AccountLockoutOptions _lockout;

    public AuthService(
        BodaVmsDbContext db,
        JwtTokenService jwt,
        ICurrentUserService currentUser,
        IOptions<AccountLockoutOptions> lockoutOptions)
    {
        _db = db;
        _jwt = jwt;
        _currentUser = currentUser;
        _lockout = lockoutOptions.Value;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        // 모든 실패 경로가 동일하게 null(→401) — 계정 존재 여부 열거(enumeration) 방지.
        // 사유 구분은 감사 로그에만 기록.
        if (user is null)
        {
            await AuditAuthAsync(AuditAction.LoginFailed, request.Username, null, "unknown username");
            return null;
        }

        if (user.LockoutUntil is { } lockedUntil && lockedUntil > DateTime.UtcNow)
        {
            await AuditAuthAsync(AuditAction.LoginFailed, request.Username, user.Id,
                $"locked out until {lockedUntil:O}");
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (_lockout.MaxFailedAttempts > 0 && user.FailedLoginCount >= _lockout.MaxFailedAttempts)
            {
                user.LockoutUntil = DateTime.UtcNow.AddMinutes(_lockout.LockoutMinutes);
                user.FailedLoginCount = 0; // 잠금 만료 후 다시 MaxFailedAttempts 회 허용
                await AuditAuthAsync(AuditAction.Lockout, request.Username, user.Id,
                    $"{_lockout.MaxFailedAttempts} consecutive failures — locked for {_lockout.LockoutMinutes} min");
            }
            else
            {
                await AuditAuthAsync(AuditAction.LoginFailed, request.Username, user.Id,
                    $"wrong password ({user.FailedLoginCount}/{_lockout.MaxFailedAttempts})");
            }
            return null;
        }

        if (!user.IsApproved)
        {
            // 자격증명 자체는 유효 — 실패 카운트 미증가 (승인 대기 중 잠금 방지)
            await AuditAuthAsync(AuditAction.LoginFailed, request.Username, user.Id, "not approved");
            return null;
        }

        if (user.FailedLoginCount != 0 || user.LockoutUntil is not null)
        {
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
        }
        await AuditAuthAsync(AuditAction.LoginSuccess, request.Username, user.Id, null);

        var token = _jwt.GenerateToken(user.Id, user.Username, user.DisplayName, user.Role);

        return new LoginResponse
        {
            Token = token,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role
        };
    }

    public async Task<(bool Success, string Message)> RegisterAsync(RegisterRequest request)
    {
        var exists = await _db.Users.AnyAsync(u => u.Username == request.Username);
        if (exists)
            return (false, "Username already exists");

        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            DisplayName = request.DisplayName,
            Role = "Pending",
            IsApproved = false
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return (true, "Registration successful. Please wait for admin approval.");
    }

    public async Task<UserDto?> GetUserByIdAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return null;

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsApproved = user.IsApproved,
            CreatedAt = user.CreatedAt,
            ApprovedAt = user.ApprovedAt
        };
    }

    /// <summary>
    /// 인증 이벤트 감사 기록 (GS 보안 — 로그인 성공/실패/잠금 추적).
    /// 같은 SaveChanges 로 User 의 실패 카운트/잠금 변경도 함께 영속화.
    /// </summary>
    private async Task AuditAuthAsync(string action, string username, int? userId, string? detail)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EntityName = "Auth",
            EntityId = userId?.ToString(),
            Action = action,
            Changes = detail,
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            UserName = username,
            IpAddress = _currentUser.IpAddress
        });
        await _db.SaveChangesAsync();
    }
}
