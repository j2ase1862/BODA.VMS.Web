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
    private readonly RefreshTokenOptions _refresh;

    public AuthService(
        BodaVmsDbContext db,
        JwtTokenService jwt,
        ICurrentUserService currentUser,
        IOptions<AccountLockoutOptions> lockoutOptions,
        IOptions<RefreshTokenOptions> refreshOptions)
    {
        _db = db;
        _jwt = jwt;
        _currentUser = currentUser;
        _lockout = lockoutOptions.Value;
        _refresh = refreshOptions.Value;
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

        return await IssueTokensAsync(user);
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;

        var hash = RefreshTokenHasher.Hash(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

        // 무효/만료/이미 폐기 — 모두 동일하게 401. (폐기된 토큰 재사용은 탈취 흔적)
        if (stored is null || !stored.IsActive)
        {
            await AuditAuthAsync(AuditAction.TokenRefresh, string.Empty, stored?.UserId,
                "invalid, expired, or revoked refresh token");
            return null;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId);
        var lockedOut = user?.LockoutUntil is { } until && until > DateTime.UtcNow;
        if (user is null || !user.IsApproved || lockedOut)
        {
            // 사용자 상태가 더 이상 유효하지 않으면 토큰도 폐기
            stored.RevokedAt = DateTime.UtcNow;
            await AuditAuthAsync(AuditAction.TokenRefresh, user?.Username ?? string.Empty,
                stored.UserId, "user not eligible (unapproved / locked / removed)");
            return null;
        }

        // 로테이션 — 기존 토큰 폐기 후 새 access + refresh 발급
        var rotated = await IssueTokensAsync(user, rotatedFrom: stored);
        await AuditAuthAsync(AuditAction.TokenRefresh, user.Username, user.Id, "rotated");
        return rotated;
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var hash = RefreshTokenHasher.Hash(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (stored is null || stored.RevokedAt is not null) return;

        stored.RevokedAt = DateTime.UtcNow;
        await AuditAuthAsync(AuditAction.Logout, string.Empty, stored.UserId, "refresh token revoked");
    }

    /// <summary>
    /// access token + (활성화 시) refresh token 발급. rotatedFrom 이 주어지면 해당 토큰을
    /// 폐기하고 새 토큰으로 연결(로테이션 체인). SaveChanges 는 호출자(AuditAuthAsync)가 수행.
    /// </summary>
    private async Task<LoginResponse> IssueTokensAsync(User user, RefreshToken? rotatedFrom = null)
    {
        var (accessToken, expiresAt) =
            _jwt.GenerateAccessToken(user.Id, user.Username, user.DisplayName, user.Role);

        var response = new LoginResponse
        {
            Token = accessToken,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            AccessTokenExpiresAt = expiresAt
        };

        if (_refresh.ExpireDays > 0)
        {
            var raw = RefreshTokenHasher.Generate();
            var newHash = RefreshTokenHasher.Hash(raw);

            if (rotatedFrom is not null)
            {
                rotatedFrom.RevokedAt = DateTime.UtcNow;
                rotatedFrom.ReplacedByTokenHash = newHash;
            }

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = newHash,
                ExpiresAt = DateTime.UtcNow.AddDays(_refresh.ExpireDays),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            response.RefreshToken = raw;
        }

        return response;
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
