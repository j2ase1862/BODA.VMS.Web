using System.Text;
using BODA.VMS.Web.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// Endpoint integration 테스트용 공통 WebApplicationFactory base.
/// - Program.cs 시작 검증(Jwt:Key 32자+) 통과를 위해 in-memory dummy 키 주입
/// - JwtBearerOptions.TokenValidationParameters 의 IssuerSigningKey 를 시작 후 재설정
///   (Program.cs 가 builder.Configuration 에서 키를 캡처하는 시점과 WebApplicationFactory
///   의 ConfigureAppConfiguration 적용 시점 차이로 키 불일치 발생 — Configure&lt;JwtBearerOptions&gt;
///   는 post-build 시점에 적용되므로 테스트용 키로 확실히 덮어쓴다)
/// - 파생 클래스에서 Extra config 추가 가능 (ClientApiKey 등)
/// </summary>
public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    internal const string TestJwtKey = "TestSecretKey_AtLeast32CharsLong_ForIntegrationTesting_2026";
    internal const string TestJwtIssuer = "BODA.VMS.Web";
    internal const string TestJwtAudience = "BODA.VMS.Web.Client";

    // 인스턴스마다 격리된 임시 DB 파일 — appsettings 의 절대경로(C:\ProgramData\...)에
    // 의존하지 않도록(hermetic) + 병렬 테스트 클래스가 같은 fresh DB 에서 스키마/시드를
    // 경쟁하지 않도록. DbBackedIntegrationTestFactory 는 in-memory 로 교체하므로 무관.
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"boda-vms-it-{Guid.NewGuid():N}.db");

    /// <summary>파생 클래스가 추가 설정 주입 (override 가능).</summary>
    protected virtual Dictionary<string, string?> ExtraConfig => new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var baseConfig = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:Issuer"] = TestJwtIssuer,
                ["Jwt:Audience"] = TestJwtAudience,
                ["Jwt:ExpireMinutes"] = "480",
                // 인스턴스 격리 DB — appsettings 의 절대경로 의존 제거 (CI 등 clean 환경 호환)
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
                // Serilog 파일 sink 비활성 — 테스트 bin/Logs 폴더 부작용 차단
                ["SerilogObservability:Disabled"] = "true",
                // DB 자동 백업 비활성 — 테스트 bin 옆 backups 폴더 부작용 차단
                ["DatabaseBackup:Enabled"] = "false",
                // Option C2 (2026-06-04): admin 디폴트 시드 제거 후 환경변수 기반.
                // 테스트는 명시 비밀번호 제공 — 부팅 차단 회피.
                ["Initial:AdminPassword"] = "TestInitialAdminPassword_1234",
                // 로그인 rate limit 완화 — TestServer 는 RemoteIpAddress 가 없어 모든
                // 테스트가 같은 파티션("unknown")을 공유. 전용 테스트만 ExtraConfig 로 낮춤.
                ["LoginRateLimit:PermitLimit"] = "100000"
            };
            // ExtraConfig 가 같은 키를 가지면 override
            foreach (var kv in ExtraConfig)
            {
                baseConfig[kv.Key] = kv.Value;
            }
            config.AddInMemoryCollection(baseConfig);
        });

        builder.ConfigureServices(services =>
        {
            // DB 를 인스턴스 격리 파일로 교체 — Program.cs 의 AddDbContext 가 캡처한
            // appsettings 절대경로(C:\ProgramData\...) 대신 사용. config 주입만으로는
            // 캡처 시점 차이로 불확실하므로 서비스 자체를 교체 (DbBacked 와 동일 패턴, 단
            // 운영 동작 유지 위해 AuditInterceptor 는 보존). 병렬 테스트 클래스 간 DB 격리.
            var optionsDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BodaVmsDbContext>));
            if (optionsDescriptor != null) services.Remove(optionsDescriptor);

            services.AddDbContext<BodaVmsDbContext>((sp, options) =>
            {
                options.UseSqlite($"Data Source={_dbPath}");
                options.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
            });

            // Configure<JwtBearerOptions> 는 post-Configure 단계에서 호출되므로 Program.cs 의
            // AddJwtBearer 가 캡처한 IssuerSigningKey 를 테스트 키로 강제 교체.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
            {
                opts.TokenValidationParameters.IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
                opts.TokenValidationParameters.ValidIssuer = TestJwtIssuer;
                opts.TokenValidationParameters.ValidAudience = TestJwtAudience;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        // 격리 DB 파일 정리 (WAL/SHM 동반). 풀이 핸들을 잡고 있어 먼저 비움 — best effort.
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch { /* 임시 폴더라 무방 */ }
        }
    }
}
