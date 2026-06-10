using BODA.VMS.Web.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// DatabaseBackupService — SQLite 온라인 백업 + retention 정책 검증.
/// 백업 파일이 원본 행 수까지 그대로 복제되는지, retention 이 오래된 파일부터
/// 정확히 N 만 남기는지 확인. GS 인증 잔여 #7 (백업/복구 자동화) 의 보장점.
/// </summary>
public class DatabaseBackupServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DatabaseBackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "boda-backup-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private string CreateSampleDbAsync(int rowCount)
    {
        var dbPath = Path.Combine(_tempDir, $"src-{Guid.NewGuid():N}.db");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)";
            cmd.ExecuteNonQuery();
        }
        using (var tx = conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Items (Name) VALUES ($n)";
            var p = cmd.CreateParameter();
            p.ParameterName = "$n";
            cmd.Parameters.Add(p);
            for (var i = 0; i < rowCount; i++)
            {
                p.Value = $"item-{i}";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        return dbPath;
    }

    [Fact]
    public async Task PerformBackupAsync_creates_file_with_same_row_count()
    {
        var src = CreateSampleDbAsync(rowCount: 42);
        var destDir = Path.Combine(_tempDir, "backups");

        var backupPath = await DatabaseBackupService.PerformBackupAsync(src, destDir);

        File.Exists(backupPath).Should().BeTrue();
        Path.GetFileName(backupPath).Should().StartWith("boda-vision-").And.EndWith(".db");

        // 복제본 행 수 확인 — 페이지 단위 복사라 정확히 일치
        using var conn = new SqliteConnection($"Data Source={backupPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Items";
        var count = (long)cmd.ExecuteScalar()!;
        count.Should().Be(42);
    }

    [Fact]
    public async Task PerformBackupAsync_throws_when_source_missing()
    {
        var fake = Path.Combine(_tempDir, "does-not-exist.db");
        var destDir = Path.Combine(_tempDir, "backups");

        var act = () => DatabaseBackupService.PerformBackupAsync(fake, destDir);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public void PruneOldBackups_keeps_N_most_recent_and_deletes_rest()
    {
        var destDir = Path.Combine(_tempDir, "backups");
        Directory.CreateDirectory(destDir);

        // 5 개 백업 파일 생성 — LastWriteTime 분리해 정렬 안정성 확보
        var paths = new List<string>();
        var baseTime = DateTime.UtcNow.AddDays(-5);
        for (var i = 0; i < 5; i++)
        {
            var p = Path.Combine(destDir, $"boda-vision-2026010{i}-120000.db");
            File.WriteAllText(p, $"dummy-{i}");
            File.SetLastWriteTimeUtc(p, baseTime.AddHours(i));
            paths.Add(p);
        }
        // 패턴 외 파일 — 건드리지 않아야 함
        var unrelated = Path.Combine(destDir, "README.txt");
        File.WriteAllText(unrelated, "keep me");

        var deleted = DatabaseBackupService.PruneOldBackups(destDir, retainCount: 2);

        deleted.Should().Be(3);
        // 최신 2 개만 남음 — paths[4], paths[3]
        File.Exists(paths[4]).Should().BeTrue();
        File.Exists(paths[3]).Should().BeTrue();
        File.Exists(paths[2]).Should().BeFalse();
        File.Exists(paths[1]).Should().BeFalse();
        File.Exists(paths[0]).Should().BeFalse();
        File.Exists(unrelated).Should().BeTrue("이유: boda-vision-*.db 외 파일은 패턴 매칭 안돼 보존");
    }

    [Fact]
    public void PruneOldBackups_noop_when_retain_zero_or_negative()
    {
        var destDir = Path.Combine(_tempDir, "backups");
        Directory.CreateDirectory(destDir);
        var p = Path.Combine(destDir, "boda-vision-20260101-000000.db");
        File.WriteAllText(p, "x");

        DatabaseBackupService.PruneOldBackups(destDir, retainCount: 0).Should().Be(0);
        File.Exists(p).Should().BeTrue();
    }

    [Fact]
    public void PruneOldBackups_safe_when_destination_missing()
    {
        var missing = Path.Combine(_tempDir, "never-created");
        var act = () => DatabaseBackupService.PruneOldBackups(missing, retainCount: 5);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task PerformBackupAsync_can_run_twice_creating_distinct_files()
    {
        var src = CreateSampleDbAsync(rowCount: 3);
        var destDir = Path.Combine(_tempDir, "backups");

        var p1 = await DatabaseBackupService.PerformBackupAsync(src, destDir);
        await Task.Delay(1100); // 파일명 타임스탬프(초 단위)가 겹치지 않도록 대기
        var p2 = await DatabaseBackupService.PerformBackupAsync(src, destDir);

        p1.Should().NotBe(p2);
        File.Exists(p1).Should().BeTrue();
        File.Exists(p2).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyDatabaseIntegrityAsync_returns_true_for_valid_backup()
    {
        var src = CreateSampleDbAsync(rowCount: 10);
        var destDir = Path.Combine(_tempDir, "backups");
        var backupPath = await DatabaseBackupService.PerformBackupAsync(src, destDir);

        (await DatabaseBackupService.VerifyDatabaseIntegrityAsync(backupPath))
            .Should().BeTrue("정상 백업은 PRAGMA integrity_check 가 ok");
    }

    [Fact]
    public async Task VerifyDatabaseIntegrityAsync_returns_false_for_corrupted_file()
    {
        // SQLite 헤더가 아닌 임의 바이트 → 유효한 DB 가 아님
        var garbage = Path.Combine(_tempDir, "corrupt.db");
        await File.WriteAllBytesAsync(garbage, new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xAB, 0xCD });

        (await DatabaseBackupService.VerifyDatabaseIntegrityAsync(garbage))
            .Should().BeFalse("손상/비-DB 파일은 무결성 검증 실패");
    }

    [Fact]
    public async Task VerifyDatabaseIntegrityAsync_returns_false_for_missing_file()
    {
        var missing = Path.Combine(_tempDir, "nope.db");
        (await DatabaseBackupService.VerifyDatabaseIntegrityAsync(missing)).Should().BeFalse();
    }

    /// <summary>
    /// 문서 §9.3 복구 절차(손상 DB 를 백업으로 교체)를 자동 검증 —
    /// 백업 → 원본 손실 시뮬레이션 → 백업으로 복구 → 행 무결성 + integrity_check 확인.
    /// GS 신뢰성: 백업이 실제로 복원 가능함을 보장 (생성만 검증하던 기존 갭 해소).
    /// </summary>
    [Fact]
    public async Task Backup_then_restore_roundtrip_preserves_data()
    {
        var src = CreateSampleDbAsync(rowCount: 137);
        var destDir = Path.Combine(_tempDir, "backups");

        // 1) 백업 생성
        var backupPath = await DatabaseBackupService.PerformBackupAsync(src, destDir);
        (await DatabaseBackupService.VerifyDatabaseIntegrityAsync(backupPath)).Should().BeTrue();

        // 2) 원본 손실 시뮬레이션 (운영 DB 손상/삭제)
        // SQLite 커넥션 풀이 파일 핸들을 잡고 있어 삭제 전 풀 비움 (운영에선 서비스 중지에 해당)
        SqliteConnection.ClearAllPools();
        File.Delete(src);
        File.Exists(src).Should().BeFalse();

        // 3) 복구 — 백업을 운영 DB 경로로 복사 (문서 §9.3 의 파일 교체 단계)
        var restored = Path.Combine(_tempDir, "restored.db");
        File.Copy(backupPath, restored);

        // 4) 복구본 검증 — 무결성 + 행 수 완전 보존
        (await DatabaseBackupService.VerifyDatabaseIntegrityAsync(restored)).Should().BeTrue();

        using var conn = new SqliteConnection($"Data Source={restored}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Items";
        ((long)cmd.ExecuteScalar()!).Should().Be(137, "복구본은 백업 시점 모든 행을 보존");
    }
}
