using System.Data.Common;

namespace BODA.VMS.Web.Data;

/// <summary>
/// VisionServer(VMS 레시피 업로드)가 소유한 레시피 구조 테이블의 스키마 보정.
/// Program.cs 시작 시 호출 — Web 이 테이블을 만들지는 않지만, 공유 DB 이므로
/// 잘못된 제약이 Web 기능(레시피 삭제 등)을 깨뜨릴 때 여기서 제자리 보정한다.
/// </summary>
public static class VisionServerSchemaMigrations
{
    /// <summary>
    /// Connections.SourceToolId/TargetToolId FK 에 ON DELETE CASCADE 보강.
    ///
    /// 레시피 삭제는 Recipes → Cameras → Steps → InspectionTools 로 전부 CASCADE 인데
    /// 마지막 Connections 두 FK 만 NO ACTION 이라 SQLite Error 19 로 전체가 롤백되던
    /// 문제(2026-08-11 현장 405 보고)의 근본 원인. SQLite 는 FK ALTER 가 불가하므로
    /// 표준 테이블 재작성 절차를 쓴다. 이미 CASCADE 면 아무것도 하지 않는다 (멱등).
    /// </summary>
    public static async Task EnsureConnectionsCascadeAsync(DbConnection conn, ILogger? logger = null)
    {
        string? ddl;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='Connections';";
            ddl = (string?)await cmd.ExecuteScalarAsync();
        }

        // 테이블이 아직 없거나(신규 설치 — VisionServer 가 만들 때까지 대기) 이미 보정됨
        if (ddl is null || ddl.Contains("ON DELETE CASCADE", StringComparison.OrdinalIgnoreCase))
            return;

        logger?.LogInformation("[SchemaMigration] Connections FK에 ON DELETE CASCADE 보강 — 테이블 재작성");

        foreach (var sql in new[]
        {
            "PRAGMA foreign_keys=OFF;",
            @"CREATE TABLE ""Connections_new"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""StepId""          INTEGER NOT NULL,
                ""SourceToolId""    INTEGER NOT NULL,
                ""TargetToolId""    INTEGER NOT NULL,
                ""ConnectionType""  INTEGER NOT NULL DEFAULT 0,
                ""SourcePositionX"" REAL    NOT NULL DEFAULT 0,
                ""SourcePositionY"" REAL    NOT NULL DEFAULT 0,
                ""TargetPositionX"" REAL    NOT NULL DEFAULT 0,
                ""TargetPositionY"" REAL    NOT NULL DEFAULT 0,
                FOREIGN KEY (""SourceToolId"") REFERENCES ""InspectionTools"" (""ID"") ON DELETE CASCADE,
                FOREIGN KEY (""TargetToolId"") REFERENCES ""InspectionTools"" (""ID"") ON DELETE CASCADE
            );",
            @"INSERT INTO ""Connections_new"" SELECT * FROM ""Connections"";",
            @"DROP TABLE ""Connections"";",
            @"ALTER TABLE ""Connections_new"" RENAME TO ""Connections"";",
            "PRAGMA foreign_keys=ON;"
        })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
