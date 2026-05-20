using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class SpcService : ISpcService
{
    private readonly BodaVmsDbContext _db;

    public SpcService(BodaVmsDbContext db)
    {
        _db = db;
    }

    /// <summary>AIAG SPC 표준 부분군 계수 (n=2..10). A2, D3, D4, d2</summary>
    private static readonly Dictionary<int, (double A2, double D3, double D4, double d2)> _factors = new()
    {
        { 2,  (1.880, 0.000, 3.267, 1.128) },
        { 3,  (1.023, 0.000, 2.574, 1.693) },
        { 4,  (0.729, 0.000, 2.282, 2.059) },
        { 5,  (0.577, 0.000, 2.114, 2.326) },
        { 6,  (0.483, 0.000, 2.004, 2.534) },
        { 7,  (0.419, 0.076, 1.924, 2.704) },
        { 8,  (0.373, 0.136, 1.864, 2.847) },
        { 9,  (0.337, 0.184, 1.816, 2.970) },
        { 10, (0.308, 0.223, 1.777, 3.078) }
    };

    public async Task<SpcResultDto> ComputeAsync(SpcRequestDto req)
    {
        var n = Math.Clamp(req.SubgroupSize, 2, 10);
        var maxSubgroups = Math.Clamp(req.MaxSubgroups, 5, 100);

        // 파라미터 마스터 조회 (USL/LSL/Target)
        var param = await _db.RecipeParameters
            .FirstOrDefaultAsync(p => p.RecipeId == req.RecipeId && p.ParamCode == req.ParamCode);

        var result = new SpcResultDto
        {
            RecipeId = req.RecipeId,
            ParamCode = req.ParamCode,
            ParamDescription = param?.Description,
            Unit = param?.Unit,
            LowerLimit = param?.LowerLimit,
            UpperLimit = param?.UpperLimit,
            TargetValue = param?.ParamValue,
            SubgroupSize = n
        };

        // 측정값 조회 (필터)
        var q = _db.ParameterMeasurements
            .Where(m => m.RecipeId == req.RecipeId && m.ParamCode == req.ParamCode);
        if (req.StartDate.HasValue) q = q.Where(m => m.InspectedAt >= req.StartDate.Value);
        if (req.EndDate.HasValue) q = q.Where(m => m.InspectedAt <= req.EndDate.Value);
        if (req.ClientId.HasValue) q = q.Where(m => m.ClientId == req.ClientId.Value);
        if (req.WorkOrderId.HasValue) q = q.Where(m => m.WorkOrderId == req.WorkOrderId.Value);

        // 시간순으로 가장 최근 N*MaxSubgroups개만
        var take = n * maxSubgroups;
        var rawMeasurements = await q
            .OrderByDescending(m => m.InspectedAt)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new { m.MeasuredValue, m.InspectedAt })
            .ToListAsync();

        // 시간순(오름차순) 재정렬
        rawMeasurements.Reverse();
        result.TotalMeasurements = rawMeasurements.Count;

        if (rawMeasurements.Count < n)
        {
            return result; // 부분군 1개도 못 만들 만큼 측정값 부족
        }

        // 부분군화: 순서대로 n개씩 묶음 (남는 자투리 버림)
        var subgroups = new List<SpcSubgroup>();
        for (int i = 0; i + n <= rawMeasurements.Count; i += n)
        {
            var chunk = rawMeasurements.GetRange(i, n);
            var values = chunk.Select(c => c.MeasuredValue).ToList();
            var mean = values.Average();
            var range = values.Max() - values.Min();
            subgroups.Add(new SpcSubgroup
            {
                Index = subgroups.Count + 1,
                StartTime = chunk[0].InspectedAt,
                Mean = mean,
                Range = range,
                Values = values
            });
        }

        result.Subgroups = subgroups;
        result.SubgroupCount = subgroups.Count;

        if (subgroups.Count == 0) return result;

        // 통계량
        var allValues = rawMeasurements.Select(r => r.MeasuredValue).ToList();
        result.GrandMean = allValues.Average();
        result.MeanOfRanges = subgroups.Average(s => s.Range);
        result.StdDev = StdDev(allValues);

        var (a2, d3, d4, d2) = _factors[n];
        result.EstimatedSigma = d2 > 0 ? result.MeanOfRanges / d2 : 0;

        // 관리한계
        result.UclXbar = result.GrandMean + a2 * result.MeanOfRanges;
        result.LclXbar = result.GrandMean - a2 * result.MeanOfRanges;
        result.UclR = d4 * result.MeanOfRanges;
        result.LclR = d3 * result.MeanOfRanges;

        // 공정능력 (USL/LSL이 둘 다 있을 때만)
        if (param?.LowerLimit is double lsl && param?.UpperLimit is double usl && usl > lsl)
        {
            // Cp = (USL - LSL) / (6σ_short)  ← 부분군 기반 σ
            if (result.EstimatedSigma > 0)
            {
                result.Cp = (usl - lsl) / (6 * result.EstimatedSigma);
                var cpu = (usl - result.GrandMean) / (3 * result.EstimatedSigma);
                var cpl = (result.GrandMean - lsl) / (3 * result.EstimatedSigma);
                result.Cpk = Math.Min(cpu, cpl);
            }

            // Pp = (USL - LSL) / (6σ_long)  ← 전체 σ
            if (result.StdDev > 0)
            {
                result.Pp = (usl - lsl) / (6 * result.StdDev);
                var ppu = (usl - result.GrandMean) / (3 * result.StdDev);
                var ppl = (result.GrandMean - lsl) / (3 * result.StdDev);
                result.Ppk = Math.Min(ppu, ppl);
            }
        }

        return result;
    }

    private static double StdDev(List<double> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        var sumSq = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumSq / (values.Count - 1));
    }
}
