using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Setup;

/// <summary>부팅 시 admin 계정 처리 결과 — 테스트/로그 판정용.</summary>
public enum AdminBootstrapResult
{
    /// <summary>admin 이 없어 Initial:AdminPassword 로 신규 시드.</summary>
    Seeded,
    /// <summary>Initial:ForceAdminPasswordReset=true — 비밀번호 강제 재설정 + 잠금 해제.</summary>
    ResetApplied,
    /// <summary>admin 존재, 설정 없음 또는 설정값이 현재 비밀번호와 일치 — 변경 없음.</summary>
    SkippedExisting,
    /// <summary>admin 존재 + Initial:AdminPassword 가 현재 비밀번호와 불일치 — 경고만 (401 흔한 원인).</summary>
    MismatchWarned,
}

/// <summary>
/// 초기 admin 계정 부트스트랩 — Option C2 (사용자 결정 2026-06-04) + 현장 복구 경로.
///
/// 동작 (부팅 시 1회, Program.cs DB 부트스트랩 마지막 단계):
/// 1. admin 없음 → Initial:AdminPassword 로 시드. 미설정/8자 미만이면 부팅 차단
///    (RequireExplicit — 디폴트 admin/admin 자동 시드는 GS 보안성 위반).
/// 2. admin 존재 + Initial:ForceAdminPasswordReset=true → 비밀번호를
///    Initial:AdminPassword 로 강제 재설정 + 실패 카운트/잠금 해제 + 감사 로그.
///    비밀번호 분실 시 DB 를 지우지 않는 공식 복구 경로 (deploy\Reset-AdminPassword.ps1 이 사용).
///    적용 후 플래그를 설정에서 제거할 것 — 켜둔 채 두면 매 부팅마다 재적용되어
///    UI 에서 바꾼 비밀번호가 되돌아간다 (경고 로그로 안내).
/// 3. admin 존재 + 플래그 없음 + Initial:AdminPassword 설정됨 → BCrypt 비교해
///    불일치면 경고 로그. "설정 파일 값 = 현재 비밀번호" 로 오인한 401 문의의
///    최다 원인이라, 원인과 복구 경로를 로그에 명시한다. 시드값은 계정 생성
///    후에는 반영되지 않는 것이 설계 — 이 메서드는 그 사실을 드러내는 역할.
/// </summary>
public static class AdminAccountBootstrap
{
    public static async Task<AdminBootstrapResult> EnsureAdminAsync(
        BodaVmsDbContext db, IConfiguration config, ILogger logger)
    {
        var username = config["Initial:AdminUsername"];
        if (string.IsNullOrWhiteSpace(username)) username = "admin";

        var password = config["Initial:AdminPassword"];
        var forceReset = string.Equals(
            config["Initial:ForceAdminPasswordReset"], "true", StringComparison.OrdinalIgnoreCase);

        var admin = await db.Users.FirstOrDefaultAsync(u => u.Username == username);

        if (admin is null)
        {
            ValidatePassword(password,
                "초기 admin 계정이 DB 에 없고 Initial:AdminPassword 가 미설정. " +
                "다음 중 하나로 명시:\n" +
                "  - 운영: setx Initial__AdminPassword \"<강한 비밀번호>\" /M\n" +
                "  - 개발: dotnet user-secrets set Initial:AdminPassword <비밀번호>\n" +
                "  - 디폴트 비밀번호 자동 시드는 GS 보안성 위반 — 절대 금지.");

            db.Users.Add(new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                DisplayName = "Administrator",
                Role = "Admin",
                IsApproved = true,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            logger.LogInformation("초기 admin 계정 '{Username}' 시드 완료.", username);
            return AdminBootstrapResult.Seeded;
        }

        if (forceReset)
        {
            ValidatePassword(password,
                "Initial:ForceAdminPasswordReset=true 인데 Initial:AdminPassword 가 미설정. " +
                "재설정할 비밀번호를 함께 명시하세요 (appsettings.Production.json 의 Initial 섹션 " +
                "또는 환경변수 Initial__AdminPassword).");

            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            admin.FailedLoginCount = 0;
            admin.LockoutUntil = null;
            admin.IsApproved = true;
            db.AuditLogs.Add(new AuditLog
            {
                EntityName = "User",
                EntityId = admin.Id.ToString(),
                Action = "AdminPasswordReset",
                UserName = username,
                Changes = "Initial:ForceAdminPasswordReset — 부팅 시 비밀번호 강제 재설정 + 잠금/실패 카운트 초기화",
                Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            logger.LogWarning(
                "admin 계정 '{Username}' 비밀번호를 Initial:AdminPassword 값으로 강제 재설정했습니다 " +
                "(잠금/실패 카운트 초기화 포함). 보안: 지금 설정에서 Initial:ForceAdminPasswordReset 를 " +
                "제거하세요 — 켜둔 채 두면 매 부팅마다 재적용되어 이후 UI 에서 변경한 비밀번호가 되돌아갑니다.",
                username);
            return AdminBootstrapResult.ResetApplied;
        }

        if (!string.IsNullOrWhiteSpace(password) && !Verifies(password, admin.PasswordHash))
        {
            logger.LogWarning(
                "Initial:AdminPassword 가 설정돼 있지만 admin 계정 '{Username}' 이 이미 존재해 무시됩니다 — " +
                "설정값이 현재 DB 비밀번호와 다릅니다 (로그인 401 의 흔한 원인). 시드값은 계정 생성 시 " +
                "1회만 사용됩니다. 설정값으로 비밀번호를 맞추려면 Initial:ForceAdminPasswordReset=true 로 " +
                "재시작하거나 deploy\\Reset-AdminPassword.ps1 을 실행하세요.",
                username);
            return AdminBootstrapResult.MismatchWarned;
        }

        return AdminBootstrapResult.SkippedExisting;
    }

    private static void ValidatePassword(string? password, string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(missingMessage);
        if (password.Length < 8)
            throw new InvalidOperationException(
                "Initial:AdminPassword 가 8자 미만입니다. 강한 비밀번호 (12자 이상 권장) 사용.");
    }

    private static bool Verifies(string password, string storedHash)
    {
        // 손상되거나 BCrypt 형식이 아닌 해시는 "불일치" 로 취급 — 진단 경고가 뜨는 편이 낫다.
        try { return BCrypt.Net.BCrypt.Verify(password, storedHash); }
        catch { return false; }
    }
}
