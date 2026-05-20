using BODA.VMS.Web.Client.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BODA.VMS.Web.Services;

public class ReportService : IReportService
{
    private readonly IOeeService _oee;
    private readonly IDefectCodeService _defect;
    private readonly IShiftService _shift;

    public ReportService(IOeeService oee, IDefectCodeService defect, IShiftService shift)
    {
        _oee = oee;
        _defect = defect;
        _shift = shift;
    }

    public async Task<(byte[] Pdf, string FileName)> GenerateAsync(ReportRequestDto req)
    {
        var (start, end, title, fileSuffix) = ResolvePeriod(req);

        // 데이터 수집 (재사용 가능한 기존 서비스 활용)
        var oee = await _oee.CalculateAsync(new OeeRequestDto
        {
            StartDate = start,
            EndDate = end,
            ClientId = req.ClientId
        });

        var pareto = await _defect.GetParetoAsync(
            startDate: start,
            endDate: end,
            clientId: req.ClientId,
            workOrderId: null,
            topN: 10);

        var shiftRows = await _shift.GetReportAsync(new ShiftReportRequestDto
        {
            StartDate = start,
            EndDate = end,
            ClientId = req.ClientId
        });

        var totalInspections = oee.Clients.Sum(c => c.TotalInspections);
        var totalPass = oee.Clients.Sum(c => c.PassCount);
        var totalNg = oee.Clients.Sum(c => c.NgCount);
        var avgOee = oee.Clients.Count > 0 ? oee.Clients.Average(c => c.Oee) : 0;

        // PDF 생성
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x
                    .FontFamily("Helvetica", "Malgun Gothic", "Arial")
                    .FontSize(9)
                    .FontColor("#1a2538"));

                page.Header().Element(h => ComposeHeader(h, title, start, end));
                page.Content().Element(c => ComposeContent(c, oee, pareto, shiftRows, totalInspections, totalPass, totalNg, avgOee));
                page.Footer().Element(ComposeFooter);
            });
        });

        var pdfBytes = doc.GeneratePdf();
        var fileName = $"BODA_VMS_{req.Type}_{fileSuffix}.pdf";
        return (pdfBytes, fileName);
    }

    private static (DateTime start, DateTime end, string title, string suffix) ResolvePeriod(ReportRequestDto req)
    {
        var refDate = req.ReferenceDate.Date;
        switch (req.Type)
        {
            case ReportType.Daily:
                {
                    var s = refDate;
                    var e = refDate.AddDays(1).AddTicks(-1);
                    return (s, e, $"Daily Production Report — {s:yyyy-MM-dd}", s.ToString("yyyyMMdd"));
                }
            case ReportType.Weekly:
                {
                    // 주의 시작 = 월요일
                    var dow = (int)refDate.DayOfWeek;
                    var offset = (dow + 6) % 7; // Mon=0
                    var s = refDate.AddDays(-offset);
                    var e = s.AddDays(7).AddTicks(-1);
                    return (s, e, $"Weekly Production Report — {s:yyyy-MM-dd} ~ {s.AddDays(6):yyyy-MM-dd}", $"{s:yyyyMMdd}_w");
                }
            case ReportType.Monthly:
                {
                    var s = new DateTime(refDate.Year, refDate.Month, 1);
                    var e = s.AddMonths(1).AddTicks(-1);
                    return (s, e, $"Monthly Production Report — {s:yyyy-MM}", s.ToString("yyyyMM"));
                }
            default:
                throw new ArgumentException($"Unknown report type: {req.Type}");
        }
    }

    // ===== 레이아웃 컴포저 =====

    private static void ComposeHeader(IContainer h, string title, DateTime start, DateTime end)
    {
        h.PaddingBottom(10).BorderBottom(2).BorderColor("#4fc3f7").Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text("BODA VMS").FontSize(18).Bold().FontColor("#4fc3f7");
                c.Item().Text("Vision Management System").FontSize(8).FontColor("#95a4b3");
            });
            row.RelativeItem().AlignRight().Column(c =>
            {
                c.Item().Text(title).FontSize(13).Bold();
                c.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                    .FontSize(8).FontColor("#95a4b3");
            });
        });
    }

    private static void ComposeContent(
        IContainer c,
        OeeResultDto oee,
        ParetoResultDto pareto,
        List<ShiftReportEntryDto> shiftRows,
        int totalInspections, int totalPass, int totalNg, double avgOee)
    {
        c.PaddingVertical(10).Column(col =>
        {
            col.Spacing(15);

            // === Section 1: KPI Summary ===
            col.Item().Element(e => SectionTitle(e, "1. Overall KPI"));
            col.Item().Row(row =>
            {
                row.RelativeItem().Element(b => KpiCard(b, "Total Inspections", totalInspections.ToString("N0"), "#1a2538"));
                row.ConstantItem(8);
                row.RelativeItem().Element(b => KpiCard(b, "Pass", totalPass.ToString("N0"), "#4caf50"));
                row.ConstantItem(8);
                row.RelativeItem().Element(b => KpiCard(b, "NG", totalNg.ToString("N0"), "#ef5350"));
                row.ConstantItem(8);
                var ngRate = totalInspections > 0 ? (double)totalNg / totalInspections * 100 : 0;
                row.RelativeItem().Element(b => KpiCard(b, "NG Rate", $"{ngRate:F2}%", NgRateColor(ngRate)));
                row.ConstantItem(8);
                row.RelativeItem().Element(b => KpiCard(b, "Avg OEE", $"{avgOee * 100:F1}%", OeeColor(avgOee)));
            });

            // === Section 2: Per-Line OEE ===
            col.Item().Element(e => SectionTitle(e, "2. Per-Line OEE"));
            if (oee.Clients.Count == 0)
            {
                col.Item().Text("No active lines in this period.").FontColor("#95a4b3").Italic();
            }
            else
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(40); // #
                        cd.RelativeColumn(2);  // name
                        cd.RelativeColumn(1);  // A
                        cd.RelativeColumn(1);  // P
                        cd.RelativeColumn(1);  // Q
                        cd.RelativeColumn(1);  // OEE
                        cd.RelativeColumn(1);  // Total
                        cd.RelativeColumn(1);  // NG
                    });
                    table.Header(h =>
                    {
                        TableHeader(h.Cell(), "#");
                        TableHeader(h.Cell(), "Line");
                        TableHeaderRight(h.Cell(), "A%");
                        TableHeaderRight(h.Cell(), "P%");
                        TableHeaderRight(h.Cell(), "Q%");
                        TableHeaderRight(h.Cell(), "OEE%");
                        TableHeaderRight(h.Cell(), "Total");
                        TableHeaderRight(h.Cell(), "NG");
                    });
                    foreach (var line in oee.Clients)
                    {
                        TableCell(table.Cell(), $"{line.ClientIndex:D2}");
                        TableCell(table.Cell(), line.ClientName);
                        TableCellRight(table.Cell(), $"{line.Availability * 100:F1}");
                        TableCellRight(table.Cell(), $"{line.Performance * 100:F1}");
                        TableCellRight(table.Cell(), $"{line.Quality * 100:F1}");
                        table.Cell().Element(cellEl =>
                            cellEl.PaddingVertical(3).PaddingHorizontal(4).AlignRight()
                                .Text($"{line.Oee * 100:F1}").Bold().FontColor(OeeColor(line.Oee)));
                        TableCellRight(table.Cell(), line.TotalInspections.ToString("N0"));
                        TableCellRight(table.Cell(), line.NgCount.ToString("N0"));
                    }
                });
            }

            // === Section 3: Pareto Top Defects ===
            col.Item().Element(e => SectionTitle(e, "3. Pareto Analysis (Top NG Codes)"));
            if (pareto.Entries.Count == 0)
            {
                col.Item().Text("No NG codes recorded.").FontColor("#95a4b3").Italic();
            }
            else
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(30);
                        cd.ConstantColumn(60);
                        cd.RelativeColumn(3);
                        cd.RelativeColumn(1);
                        cd.RelativeColumn(1);
                        cd.RelativeColumn(1);
                        cd.RelativeColumn(1);
                    });
                    table.Header(h =>
                    {
                        TableHeader(h.Cell(), "Rank");
                        TableHeader(h.Cell(), "Code");
                        TableHeader(h.Cell(), "Description");
                        TableHeader(h.Cell(), "Severity");
                        TableHeaderRight(h.Cell(), "Count");
                        TableHeaderRight(h.Cell(), "Share");
                        TableHeaderRight(h.Cell(), "Cumul.");
                    });
                    int rank = 1;
                    foreach (var p in pareto.Entries)
                    {
                        TableCell(table.Cell(), rank++.ToString());
                        TableCell(table.Cell(), p.NgCode);
                        TableCell(table.Cell(), p.Description ?? "(unregistered)");
                        TableCell(table.Cell(), p.Severity ?? "—");
                        TableCellRight(table.Cell(), p.Count.ToString("N0"));
                        TableCellRight(table.Cell(), $"{p.Percentage:F2}%");
                        table.Cell().Element(cellEl =>
                            cellEl.PaddingVertical(3).PaddingHorizontal(4).AlignRight()
                                .Text($"{p.CumulativePercentage:F2}%").Bold().FontColor(CumulativeColor(p.CumulativePercentage)));
                    }
                });
            }

            // === Section 4: Shift Breakdown ===
            if (shiftRows.Count > 0)
            {
                col.Item().Element(e => SectionTitle(e, "4. Shift Breakdown"));
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(1);
                        cd.RelativeColumn(1);
                        cd.RelativeColumn(1);
                        cd.RelativeColumn(1);
                        cd.RelativeColumn(1);
                        cd.RelativeColumn(1);
                    });
                    table.Header(h =>
                    {
                        TableHeader(h.Cell(), "Date");
                        TableHeader(h.Cell(), "Shift");
                        TableHeaderRight(h.Cell(), "Total");
                        TableHeaderRight(h.Cell(), "Pass");
                        TableHeaderRight(h.Cell(), "NG");
                        TableHeaderRight(h.Cell(), "NG%");
                    });
                    foreach (var s in shiftRows)
                    {
                        TableCell(table.Cell(), s.Date.ToString("MM-dd"));
                        TableCell(table.Cell(), s.ShiftName);
                        TableCellRight(table.Cell(), s.TotalCount.ToString("N0"));
                        TableCellRight(table.Cell(), s.PassCount.ToString("N0"));
                        TableCellRight(table.Cell(), s.NgCount.ToString("N0"));
                        table.Cell().Element(cellEl =>
                            cellEl.PaddingVertical(3).PaddingHorizontal(4).AlignRight()
                                .Text($"{s.NgRate:F2}").Bold().FontColor(NgRateColor(s.NgRate)));
                    }
                });
            }
        });
    }

    private static void ComposeFooter(IContainer f)
    {
        f.AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(x => x.FontSize(8).FontColor("#95a4b3"));
            t.Span("BODA VMS · Page ");
            t.CurrentPageNumber();
            t.Span(" / ");
            t.TotalPages();
        });
    }

    // ===== 헬퍼 =====
    private static void SectionTitle(IContainer e, string text) =>
        e.BorderBottom(1).BorderColor("#cccccc").PaddingBottom(3).PaddingTop(5)
            .Text(text).FontSize(11).Bold().FontColor("#1a2538");

    private static void KpiCard(IContainer e, string label, string value, string accent)
    {
        e.Border(1).BorderColor("#e0e0e0").Padding(8).Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor("#95a4b3");
            c.Item().Text(value).FontSize(16).Bold().FontColor(accent);
        });
    }

    private static void TableHeader(IContainer cell, string text) =>
        cell.Background("#f0f4f8").BorderBottom(1).BorderColor("#cccccc")
            .PaddingVertical(4).PaddingHorizontal(4).Text(text).Bold().FontSize(8);

    private static void TableHeaderRight(IContainer cell, string text) =>
        cell.Background("#f0f4f8").BorderBottom(1).BorderColor("#cccccc")
            .PaddingVertical(4).PaddingHorizontal(4).AlignRight().Text(text).Bold().FontSize(8);

    private static void TableCell(IContainer cell, string text) =>
        cell.BorderBottom(1).BorderColor("#eeeeee").PaddingVertical(3).PaddingHorizontal(4).Text(text).FontSize(9);

    private static void TableCellRight(IContainer cell, string text) =>
        cell.BorderBottom(1).BorderColor("#eeeeee").PaddingVertical(3).PaddingHorizontal(4).AlignRight().Text(text).FontSize(9);

    private static string OeeColor(double oee)
    {
        if (oee >= 0.85) return "#2e7d32";
        if (oee >= 0.60) return "#0277bd";
        if (oee >= 0.40) return "#ed6c02";
        return "#c62828";
    }

    private static string NgRateColor(double rate)
    {
        if (rate >= 5) return "#c62828";
        if (rate >= 1) return "#ed6c02";
        return "#2e7d32";
    }

    private static string CumulativeColor(double pct)
    {
        if (pct >= 80) return "#c62828";
        if (pct >= 50) return "#ed6c02";
        return "#0277bd";
    }
}
