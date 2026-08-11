using BODA.VMS.Web.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace BODA.VMS.Web.Tests.Migrations;

/// <summary>
/// Connections FK CASCADE 보강 마이그레이션 —
/// 레시피 삭제가 Cameras→Steps→InspectionTools 연쇄 후 Connections 에서
/// FK 실패하던 문제(2026-08-11 현장 405 보고)의 회귀 방지.
/// </summary>
public class VisionServerSchemaMigrationsTests : IDisposable
{
    private readonly SqliteConnection _conn;

    public VisionServerSchemaMigrationsTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _conn.Open();
    }

    public void Dispose() => _conn.Dispose();

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>운영 DB 와 동일한 레거시 스키마(Connections 만 CASCADE 누락) + 레시피 1개 체인 시드.</summary>
    private void SeedLegacySchema()
    {
        Exec("""
            CREATE TABLE "Recipes" ("RecipeID" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "RecipeName" TEXT);
            CREATE TABLE "Cameras" ("CameraID" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "RecipeID" INTEGER NOT NULL,
                FOREIGN KEY ("RecipeID") REFERENCES "Recipes" ("RecipeID") ON DELETE CASCADE);
            CREATE TABLE "Steps" ("StepID" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "CameraID" INTEGER NOT NULL,
                FOREIGN KEY ("CameraID") REFERENCES "Cameras" ("CameraID") ON DELETE CASCADE);
            CREATE TABLE "InspectionTools" ("ID" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "StepId" INTEGER NOT NULL,
                FOREIGN KEY ("StepId") REFERENCES "Steps" ("StepID") ON DELETE CASCADE);
            CREATE TABLE "Connections" (
                "Id"              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "StepId"          INTEGER NOT NULL,
                "SourceToolId"    INTEGER NOT NULL,
                "TargetToolId"    INTEGER NOT NULL,
                "ConnectionType"  INTEGER NOT NULL DEFAULT 0,
                "SourcePositionX" REAL    NOT NULL DEFAULT 0,
                "SourcePositionY" REAL    NOT NULL DEFAULT 0,
                "TargetPositionX" REAL    NOT NULL DEFAULT 0,
                "TargetPositionY" REAL    NOT NULL DEFAULT 0,
                FOREIGN KEY ("SourceToolId") REFERENCES "InspectionTools" ("ID"),
                FOREIGN KEY ("TargetToolId") REFERENCES "InspectionTools" ("ID"));
            INSERT INTO Recipes (RecipeName) VALUES ('A1');
            INSERT INTO Cameras (RecipeID) VALUES (1);
            INSERT INTO Steps (CameraID) VALUES (1);
            INSERT INTO InspectionTools (StepId) VALUES (1), (1);
            INSERT INTO Connections (StepId, SourceToolId, TargetToolId) VALUES (1, 1, 2);
            """);
    }

    [Fact]
    public async Task Legacy_schema_blocks_recipe_delete_then_migration_fixes_it()
    {
        SeedLegacySchema();

        // 보정 전: 연쇄 삭제가 Connections 에서 FK 실패 (현장 증상 재현)
        var deleteBefore = () => Exec("DELETE FROM Recipes WHERE RecipeID = 1;");
        deleteBefore.Should().Throw<SqliteException>()
            .Which.SqliteErrorCode.Should().Be(19); // SQLITE_CONSTRAINT

        await VisionServerSchemaMigrations.EnsureConnectionsCascadeAsync(_conn);

        // 보정 후: 데이터 보존 + 연쇄 삭제가 Connections 까지 완주
        Scalar("SELECT COUNT(*) FROM Connections").Should().Be(1);
        Exec("DELETE FROM Recipes WHERE RecipeID = 1;");
        Scalar("SELECT COUNT(*) FROM Cameras").Should().Be(0);
        Scalar("SELECT COUNT(*) FROM Steps").Should().Be(0);
        Scalar("SELECT COUNT(*) FROM InspectionTools").Should().Be(0);
        Scalar("SELECT COUNT(*) FROM Connections").Should().Be(0);
    }

    [Fact]
    public async Task Migration_is_idempotent()
    {
        SeedLegacySchema();
        await VisionServerSchemaMigrations.EnsureConnectionsCascadeAsync(_conn);
        await VisionServerSchemaMigrations.EnsureConnectionsCascadeAsync(_conn); // 2회차 no-op

        Scalar("SELECT COUNT(*) FROM Connections").Should().Be(1);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE name='Connections';";
        ((string)cmd.ExecuteScalar()!).Should().Contain("ON DELETE CASCADE");
    }

    [Fact]
    public async Task Migration_skips_when_table_absent()
    {
        // 신규 설치 — VisionServer 가 아직 테이블을 만들기 전이면 아무것도 하지 않음
        await VisionServerSchemaMigrations.EnsureConnectionsCascadeAsync(_conn);
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='Connections'").Should().Be(0);
    }
}
