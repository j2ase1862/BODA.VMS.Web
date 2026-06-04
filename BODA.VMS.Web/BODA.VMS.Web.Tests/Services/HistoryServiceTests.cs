using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using ClosedXML.Excel;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// HistoryService — 검사 이력 조회/요약/Excel 내보내기.
/// 추적성(IATF) 의 근간 — 잘못된 필터/요약/Excel 컬럼 매핑은 출하 후 NG 추적 실패로 이어짐.
/// ToolResults JSON 역직렬화 안전성, 일자별 PASS/NG 집계 정확성, ClosedXML
/// xlsx 가 재해석 가능한지(헤더 6 열) 검증.
/// </summary>
public class HistoryServiceTests
{
    private const int ClientA = 1;
    private const int ClientB = 2;

    private static async Task SeedClientsAsync(BodaVmsDbContext db)
    {
        db.Clients.AddRange(
            new VisionClient { Id = ClientA, Name = "L0", IpAddress = "127.0.0.1", ClientIndex = 0, IsActive = true, CreatedAt = DateTime.UtcNow },
            new VisionClient { Id = ClientB, Name = "L1", IpAddress = "127.0.0.2", ClientIndex = 1, IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static InspectionHistory MakeHistory(
        int clientId,
        bool isPass = true,
        string? ngCode = null,
        DateTime? inspectedAt = null,
        string? toolResultsJson = null,
        string? recipeName = null,
        string? imagePath = null)
        => new()
        {
            ClientId = clientId,
            IsPass = isPass,
            NgCode = ngCode,
            InspectedAt = inspectedAt ?? DateTime.UtcNow,
            ToolResults = toolResultsJson,
            RecipeName = recipeName,
            ImagePath = imagePath
        };

    // ─── GetHistory: filters + pagination ───────────────────

    [Fact]
    public async Task GetHistoryAsync_filters_by_client_pass_ngcode_and_date()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAsync(ctx.Db);

        var t = new DateTime(2026, 6, 4, 9, 0, 0, DateTimeKind.Utc);
        ctx.Db.InspectionHistories.AddRange(
            MakeHistory(ClientA, isPass: true, inspectedAt: t),
            MakeHistory(ClientA, isPass: false, ngCode: "NG-001", inspectedAt: t.AddMinutes(1)),
            MakeHistory(ClientB, isPass: false, ngCode: "NG-002", inspectedAt: t.AddMinutes(2)),
            MakeHistory(ClientA, isPass: false, ngCode: "NG-001", inspectedAt: t.AddDays(2))
        );
        await ctx.Db.SaveChangesAsync();

        var svc = new HistoryService(ctx.Db);

        // ClientA + NG + NG-001 + 윈도우 = 2 건 중 1 건만 (날짜 윈도우로 마지막 1건 제외)
        var page = await svc.GetHistoryAsync(new HistoryFilterDto
        {
            ClientId = ClientA,
            IsPass = false,
            NgCode = "NG-001",
            StartDate = t,
            EndDate = t.AddHours(1)
        });

        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle();
        page.Items[0].ClientName.Should().Be("L0");
        page.Items[0].NgCode.Should().Be("NG-001");
    }

    [Fact]
    public async Task GetHistoryAsync_orders_by_inspected_desc_with_pagination()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAsync(ctx.Db);

        var t = new DateTime(2026, 6, 4, 9, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
        {
            ctx.Db.InspectionHistories.Add(MakeHistory(
                ClientA, inspectedAt: t.AddMinutes(i), recipeName: $"R{i}"));
        }
        await ctx.Db.SaveChangesAsync();

        var svc = new HistoryService(ctx.Db);
        var page1 = await svc.GetHistoryAsync(new HistoryFilterDto { Page = 1, PageSize = 2 });

        page1.TotalCount.Should().Be(5);
        page1.Items.Should().HaveCount(2);
        page1.Items.Select(i => i.RecipeName).Should().ContainInOrder("R4", "R3");

        var page2 = await svc.GetHistoryAsync(new HistoryFilterDto { Page = 2, PageSize = 2 });
        page2.Items.Select(i => i.RecipeName).Should().ContainInOrder("R2", "R1");
    }

    [Fact]
    public async Task GetHistoryAsync_deserializes_tool_results_json()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAsync(ctx.Db);

        var tools = new List<ToolResultItem>
        {
            new() { ToolName = "Diameter", ToolType = "Caliper", Value = 12.5, Min = 12, Max = 13, IsPass = true },
            new() { ToolName = "Height",   ToolType = "Caliper", Value = 9.8,  Min = 9.5, Max = 10.5, IsPass = true }
        };
        ctx.Db.InspectionHistories.Add(MakeHistory(ClientA, toolResultsJson: JsonSerializer.Serialize(tools)));
        await ctx.Db.SaveChangesAsync();

        var svc = new HistoryService(ctx.Db);
        var page = await svc.GetHistoryAsync(new HistoryFilterDto());

        page.Items.Should().ContainSingle();
        page.Items[0].ToolResults.Should().NotBeNull();
        page.Items[0].ToolResults!.Should().HaveCount(2);
        page.Items[0].ToolResults![0].ToolName.Should().Be("Diameter");
    }

    [Fact]
    public async Task GetHistoryAsync_handles_null_tool_results_safely()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAsync(ctx.Db);

        ctx.Db.InspectionHistories.Add(MakeHistory(ClientA, toolResultsJson: null));
        await ctx.Db.SaveChangesAsync();

        var svc = new HistoryService(ctx.Db);
        var page = await svc.GetHistoryAsync(new HistoryFilterDto());

        page.Items[0].ToolResults.Should().BeNull();
    }

    // ─── GetHistoryDetail ──────────────────────────────────

    [Fact]
    public async Task GetHistoryDetailAsync_returns_dto_with_client_name()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAsync(ctx.Db);
        ctx.Db.InspectionHistories.Add(MakeHistory(ClientB, isPass: false, ngCode: "NG-X"));
        await ctx.Db.SaveChangesAsync();

        var svc = new HistoryService(ctx.Db);
        var id = ctx.Db.InspectionHistories.Single().Id;

        var detail = await svc.GetHistoryDetailAsync(id);
        detail.Should().NotBeNull();
        detail!.ClientId.Should().Be(ClientB);
        detail.ClientName.Should().Be("L1");
        detail.NgCode.Should().Be("NG-X");
    }

    [Fact]
    public async Task GetHistoryDetailAsync_returns_null_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new HistoryService(ctx.Db);
        (await svc.GetHistoryDetailAsync(999)).Should().BeNull();
    }

    // ─── GetDailySummary ───────────────────────────────────

    [Fact]
    public async Task GetDailySummaryAsync_groups_by_date_with_pass_ng_counts()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAsync(ctx.Db);

        var d1 = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var d2 = d1.AddDays(1);
        ctx.Db.InspectionHistories.AddRange(
            MakeHistory(ClientA, isPass: true,  inspectedAt: d1),
            MakeHistory(ClientA, isPass: false, inspectedAt: d1.AddHours(1)),
            MakeHistory(ClientA, isPass: false, inspectedAt: d1.AddHours(2)),
            MakeHistory(ClientA, isPass: true,  inspectedAt: d2)
        );
        await ctx.Db.SaveChangesAsync();

        var svc = new HistoryService(ctx.Db);
        var summary = await svc.GetDailySummaryAsync(null, d1.AddDays(-1), d2.AddDays(1));

        summary.Should().HaveCount(2);
        summary.Should().BeInAscendingOrder(s => s.Date);
        var day1 = summary[0];
        day1.TotalCount.Should().Be(3);
        day1.PassCount.Should().Be(1);
        day1.NgCount.Should().Be(2);
        day1.NgRate.Should().BeApproximately(66.67, 0.01);
    }

    [Fact]
    public async Task GetDailySummaryAsync_filters_by_client_when_provided()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAsync(ctx.Db);

        var d = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        ctx.Db.InspectionHistories.AddRange(
            MakeHistory(ClientA, isPass: true, inspectedAt: d),
            MakeHistory(ClientB, isPass: false, inspectedAt: d.AddMinutes(1)),
            MakeHistory(ClientB, isPass: false, inspectedAt: d.AddMinutes(2))
        );
        await ctx.Db.SaveChangesAsync();

        var svc = new HistoryService(ctx.Db);
        var lineB = await svc.GetDailySummaryAsync(ClientB, d.AddDays(-1), d.AddDays(1));

        lineB.Should().ContainSingle();
        lineB[0].TotalCount.Should().Be(2);
        lineB[0].NgCount.Should().Be(2);
    }

    // ─── ExportToExcel ─────────────────────────────────────

    [Fact]
    public async Task ExportToExcelAsync_produces_valid_xlsx_with_headers_and_rows()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAsync(ctx.Db);

        var t = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        ctx.Db.InspectionHistories.AddRange(
            MakeHistory(ClientA, isPass: true,  inspectedAt: t,             recipeName: "R-PASS"),
            MakeHistory(ClientA, isPass: false, ngCode: "NG-7", inspectedAt: t.AddMinutes(1), recipeName: "R-NG")
        );
        await ctx.Db.SaveChangesAsync();

        var svc = new HistoryService(ctx.Db);
        var bytes = await svc.ExportToExcelAsync(new HistoryFilterDto { ClientId = ClientA });

        bytes.Should().NotBeEmpty();
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        // 6 헤더 검증 — UI/외부 통합 깨짐 조기 발견
        ws.Cell(1, 1).GetString().Should().Be("ID");
        ws.Cell(1, 4).GetString().Should().Be("Result");
        ws.Cell(1, 6).GetString().Should().Be("Inspected At");

        // 2 행 데이터 — 최신순 정렬: R-NG, R-PASS
        ws.Cell(2, 3).GetString().Should().Be("R-NG");
        ws.Cell(2, 4).GetString().Should().Be("NG");
        ws.Cell(3, 3).GetString().Should().Be("R-PASS");
        ws.Cell(3, 4).GetString().Should().Be("PASS");
    }
}
