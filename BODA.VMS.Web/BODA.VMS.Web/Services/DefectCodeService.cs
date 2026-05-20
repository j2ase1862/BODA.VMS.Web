using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class DefectCodeService : IDefectCodeService
{
    private readonly BodaVmsDbContext _db;

    public DefectCodeService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<DefectCodeDto>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.DefectCodes.AsQueryable();
        if (!includeInactive)
            query = query.Where(d => d.IsActive);

        return await query
            .OrderBy(d => d.Code)
            .Select(d => ToDto(d))
            .ToListAsync();
    }

    public async Task<DefectCodeDto?> GetByIdAsync(int id)
    {
        var e = await _db.DefectCodes.FindAsync(id);
        return e is null ? null : ToDto(e);
    }

    public async Task<DefectCodeDto?> GetByCodeAsync(string code)
    {
        var e = await _db.DefectCodes.FirstOrDefaultAsync(d => d.Code == code);
        return e is null ? null : ToDto(e);
    }

    public async Task<DefectCodeDto> CreateAsync(DefectCodeDto dto)
    {
        var e = new DefectCode
        {
            Code = dto.Code.Trim(),
            Description = dto.Description.Trim(),
            Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim(),
            Severity = dto.Severity,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        _db.DefectCodes.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<DefectCodeDto?> UpdateAsync(int id, DefectCodeDto dto)
    {
        var e = await _db.DefectCodes.FindAsync(id);
        if (e is null) return null;

        e.Code = dto.Code.Trim();
        e.Description = dto.Description.Trim();
        e.Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        e.Severity = dto.Severity;
        e.IsActive = dto.IsActive;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var e = await _db.DefectCodes.FindAsync(id);
        if (e is null) return false;
        _db.DefectCodes.Remove(e);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ParetoResultDto> GetParetoAsync(
        DateTime? startDate, DateTime? endDate,
        int? clientId, int? workOrderId, int topN)
    {
        var query = _db.InspectionHistories.AsQueryable();
        if (startDate.HasValue) query = query.Where(h => h.InspectedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(h => h.InspectedAt <= endDate.Value);
        if (clientId.HasValue) query = query.Where(h => h.ClientId == clientId.Value);
        if (workOrderId.HasValue) query = query.Where(h => h.WorkOrderId == workOrderId.Value);

        var totalInspections = await query.CountAsync();

        // NgCode가 비어있지 않은 것만 필터, 쉼표 split 위해 일단 메모리로 가져옴
        var ngRows = await query
            .Where(h => h.NgCode != null && h.NgCode != "")
            .Select(h => h.NgCode!)
            .ToListAsync();

        // "2001,3001" 형식 분해 → 코드별 빈도
        var codeCount = new Dictionary<string, int>();
        foreach (var row in ngRows)
        {
            foreach (var raw in row.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var code = raw.Trim();
                if (code.Length == 0) continue;
                codeCount[code] = codeCount.GetValueOrDefault(code) + 1;
            }
        }

        var totalNgCount = codeCount.Values.Sum();

        // 마스터에서 description/severity 조회
        var allCodes = codeCount.Keys.ToList();
        var masters = await _db.DefectCodes
            .Where(d => allCodes.Contains(d.Code))
            .ToDictionaryAsync(d => d.Code, d => d);

        var ordered = codeCount
            .OrderByDescending(kv => kv.Value)
            .Take(topN)
            .ToList();

        var entries = new List<ParetoEntryDto>(ordered.Count);
        double cumulative = 0;
        foreach (var (code, count) in ordered)
        {
            var pct = totalNgCount > 0 ? (double)count / totalNgCount * 100 : 0;
            cumulative += pct;
            masters.TryGetValue(code, out var master);
            entries.Add(new ParetoEntryDto
            {
                NgCode = code,
                Description = master?.Description,
                Category = master?.Category,
                Severity = master?.Severity,
                Count = count,
                Percentage = Math.Round(pct, 2),
                CumulativePercentage = Math.Round(cumulative, 2)
            });
        }

        return new ParetoResultDto
        {
            TotalNgCount = totalNgCount,
            TotalInspections = totalInspections,
            Entries = entries
        };
    }

    public async Task<List<DefectCodeCandidateDto>> GetCandidatesAsync(int recentDays = 90)
    {
        var registeredCodes = await _db.DefectCodes
            .Select(d => d.Code)
            .ToListAsync();
        var registeredSet = new HashSet<string>(registeredCodes, StringComparer.OrdinalIgnoreCase);

        // 1) RecipeParameter.ParamCode 풀 — 사전 정의된 코드
        var paramCodes = await _db.RecipeParameters
            .GroupBy(p => p.ParamCode)
            .Select(g => new
            {
                Code = g.Key.ToString(),
                Description = g.Select(p => p.Description).FirstOrDefault(),
                Category = g.Select(p => p.Category).FirstOrDefault()
            })
            .ToListAsync();

        // 2) InspectionHistory.NgCode 풀 — 실제 발생한 코드 (최근 N일)
        var since = DateTime.UtcNow.AddDays(-recentDays);
        var ngRows = await _db.InspectionHistories
            .Where(h => h.InspectedAt >= since && h.NgCode != null && h.NgCode != "")
            .Select(h => h.NgCode!)
            .ToListAsync();

        var historyCount = new Dictionary<string, int>();
        foreach (var row in ngRows)
        {
            foreach (var raw in row.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var code = raw.Trim();
                if (code.Length == 0) continue;
                historyCount[code] = historyCount.GetValueOrDefault(code) + 1;
            }
        }

        // 통합 (Code 기준 dedup)
        var bag = new Dictionary<string, DefectCodeCandidateDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in paramCodes)
        {
            bag[p.Code] = new DefectCodeCandidateDto
            {
                Code = p.Code,
                Source = "Param",
                SuggestedDescription = p.Description,
                SuggestedCategory = p.Category,
                OccurrenceCount = historyCount.GetValueOrDefault(p.Code),
                IsRegistered = registeredSet.Contains(p.Code)
            };
        }

        foreach (var (code, count) in historyCount)
        {
            if (bag.TryGetValue(code, out var existing))
            {
                // 이미 Param 소스로 있음 — 카운트만 갱신
                existing.OccurrenceCount = count;
            }
            else
            {
                bag[code] = new DefectCodeCandidateDto
                {
                    Code = code,
                    Source = "History",
                    OccurrenceCount = count,
                    IsRegistered = registeredSet.Contains(code)
                };
            }
        }

        // 정렬: 미등록 + 발생빈도 높은 순 우선 → 그 다음 Param 사전정의
        return bag.Values
            .OrderBy(c => c.IsRegistered)               // 미등록 먼저
            .ThenByDescending(c => c.OccurrenceCount)   // 발생 많은 것 우선
            .ThenBy(c => c.Code)
            .ToList();
    }

    public async Task<List<UnregisteredNgCodeDto>> GetUnregisteredAsync(int days = 30)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        var registeredSet = (await _db.DefectCodes.Select(d => d.Code).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ngRows = await _db.InspectionHistories
            .Where(h => h.InspectedAt >= since && h.NgCode != null && h.NgCode != "")
            .Select(h => new { h.NgCode, h.InspectedAt })
            .ToListAsync();

        // 분해 → 코드별 빈도/최근 발생
        var stats = new Dictionary<string, (int count, DateTime lastSeen)>();
        foreach (var row in ngRows)
        {
            foreach (var raw in row.NgCode!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var code = raw.Trim();
                if (code.Length == 0) continue;
                if (registeredSet.Contains(code)) continue; // 등록된 건 제외

                if (stats.TryGetValue(code, out var prev))
                {
                    stats[code] = (prev.count + 1,
                        row.InspectedAt > prev.lastSeen ? row.InspectedAt : prev.lastSeen);
                }
                else
                {
                    stats[code] = (1, row.InspectedAt);
                }
            }
        }

        if (stats.Count == 0) return new();

        // RecipeParameter에서 추천 설명 조회 (있으면)
        var codeList = stats.Keys.ToList();
        var paramHints = await _db.RecipeParameters
            .Where(p => codeList.Contains(p.ParamCode.ToString()))
            .GroupBy(p => p.ParamCode)
            .Select(g => new
            {
                Code = g.Key.ToString(),
                Description = g.Select(p => p.Description).FirstOrDefault(),
                Category = g.Select(p => p.Category).FirstOrDefault()
            })
            .ToDictionaryAsync(p => p.Code, p => p);

        return stats
            .OrderByDescending(kv => kv.Value.count)
            .Select(kv =>
            {
                paramHints.TryGetValue(kv.Key, out var hint);
                return new UnregisteredNgCodeDto
                {
                    Code = kv.Key,
                    OccurrenceCount = kv.Value.count,
                    LastSeenAt = kv.Value.lastSeen,
                    SuggestedDescription = hint?.Description,
                    SuggestedCategory = hint?.Category
                };
            })
            .ToList();
    }

    private static DefectCodeDto ToDto(DefectCode e) => new()
    {
        Id = e.Id,
        Code = e.Code,
        Description = e.Description,
        Category = e.Category,
        Severity = e.Severity,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
