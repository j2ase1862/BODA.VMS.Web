using BODA.VMS.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Tests.Helpers;

/// <summary>
/// 서비스 통합 테스트용 in-memory SQLite DbContext factory.
/// 운영의 file-backed SQLite 와 동일 SQL 엔진 — EF Core InMemory provider 보다
/// 현실적(UNIQUE 제약/FK 등 실제 SQL 동작 검증).
/// 각 테스트가 IDisposable Connection 을 보유 — using 패턴으로 격리.
/// </summary>
public sealed class InMemorySqliteDbContext : IDisposable
{
    public BodaVmsDbContext Db { get; }
    private readonly SqliteConnection _connection;

    public InMemorySqliteDbContext()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();  // :memory: 는 연결 동안만 유지

        var options = new DbContextOptionsBuilder<BodaVmsDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new BodaVmsDbContext(options);
        Db.Database.EnsureCreated();  // EF 가 스키마 생성
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
