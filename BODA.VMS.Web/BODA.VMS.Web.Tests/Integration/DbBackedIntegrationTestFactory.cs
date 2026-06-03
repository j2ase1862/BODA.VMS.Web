using BODA.VMS.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// DB 쓰기 endpoint integration 테스트용 factory — 운영 DbContext 등록을 in-memory
/// SQLite 로 교체. AuditInterceptor 도 함께 제거 (테스트에서는 감사 로깅 불필요).
///
/// SqliteConnection(":memory:") 은 연결 동안만 schema/data 유지 — factory 가 보유.
/// Dispose 시 자동 해제. 각 테스트 클래스가 IClassFixture 로 공유 → 클래스 단위 격리.
/// </summary>
public class DbBackedIntegrationTestFactory : IntegrationTestFactory
{
    private SqliteConnection? _connection;

    /// <summary>테스트에서 직접 DB 접근(시드/검증)할 때 사용 — 새 scope 의 DbContext.</summary>
    public BodaVmsDbContext CreateScopedDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        builder.ConfigureServices(services =>
        {
            // 기존 DbContextOptions 와 AuditInterceptor 등록 제거
            var optionsDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<BodaVmsDbContext>));
            if (optionsDescriptor != null) services.Remove(optionsDescriptor);

            var interceptorDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(AuditInterceptor));
            if (interceptorDescriptor != null) services.Remove(interceptorDescriptor);

            services.AddDbContext<BodaVmsDbContext>(opts => opts.UseSqlite(_connection!));
            // Schema 생성은 Program.cs 의 raw SQL bootstrap (CREATE TABLE IF NOT EXISTS)
            // 에 위임 — EnsureCreated 와 동시 사용시 컬럼명 충돌(예: Clients.Index vs
            // ClientIndex) 로 UNIQUE 제약 위반 발생.
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Dispose();
        }
        base.Dispose(disposing);
    }
}
