using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class AndonService : IAndonService
{
    private readonly BodaVmsDbContext _db;
    private readonly IPredictionService _prediction;

    // Plan §8 Open Q #5 — 알람화 안 함, 임계 초과만 Insight 채널로
    // /forecast 카드 임계와 동일한 의미 유지(operator 가 같은 색·임계 학습)
    private const double InsightHighThreshold   = 0.10;  // ≥10% → high
    private const double InsightMediumThreshold = 0.05;  // 5~10% → medium

    public AndonService(BodaVmsDbContext db, IPredictionService prediction)
    {
        _db = db;
        _prediction = prediction;
    }

    public async Task<AndonSnapshotDto> GetSnapshotAsync()
    {
        var todayStart = DateTime.UtcNow.Date;
        var asOf = DateTime.UtcNow;

        var clients = await _db.Clients
            .Where(c => c.IsActive)
            .OrderBy(c => c.ClientIndex)
            .ToListAsync();

        var clientIds = clients.Select(c => c.Id).ToList();

        // 현재 상태 (open log) 일괄 조회 — ClientId당 EndedAt이 null인 row
        var currentStatuses = await _db.EquipmentStatusLogs
            .Where(e => clientIds.Contains(e.ClientId) && e.EndedAt == null)
            .ToDictionaryAsync(e => e.ClientId, e => e);

        // 오늘 검사 집계 (Client별)
        var todayInspections = await _db.InspectionHistories
            .Where(h => clientIds.Contains(h.ClientId) && h.InspectedAt >= todayStart)
            .GroupBy(h => h.ClientId)
            .Select(g => new
            {
                ClientId = g.Key,
                Total = g.Count(),
                Pass = g.Count(h => h.IsPass)
            })
            .ToDictionaryAsync(x => x.ClientId, x => x);

        // 활성 WorkOrder (InProgress) — Client별 가장 최근 1개
        var activeWos = await _db.WorkOrders
            .Include(w => w.Product)
            .Where(w => clientIds.Contains(w.ClientId) && w.Status == WorkOrderStatus.InProgress)
            .OrderByDescending(w => w.ActualStartAt)
            .ToListAsync();
        var activeWoByClient = activeWos
            .GroupBy(w => w.ClientId)
            .ToDictionary(g => g.Key, g => g.First());

        // 미해제 알람 (Client별 카운트)
        var openAlarms = await _db.AlarmEvents
            .Where(a => a.ResolvedAt == null && a.ClientId != null && clientIds.Contains(a.ClientId.Value))
            .Select(a => new { a.ClientId, a.Severity })
            .ToListAsync();
        var alarmByClient = openAlarms
            .GroupBy(a => a.ClientId!.Value)
            .ToDictionary(g => g.Key, g => new
            {
                Total = g.Count(),
                Critical = g.Count(x => x.Severity == AlarmSeverity.Critical)
            });

        // === 라인별 DTO 조립 ===
        var lines = new List<AndonLineDto>();
        foreach (var c in clients)
        {
            currentStatuses.TryGetValue(c.Id, out var statusLog);
            todayInspections.TryGetValue(c.Id, out var insp);
            activeWoByClient.TryGetValue(c.Id, out var wo);
            alarmByClient.TryGetValue(c.Id, out var alarm);

            var line = new AndonLineDto
            {
                ClientId = c.Id,
                ClientIndex = c.ClientIndex,
                ClientName = c.Name,
                Status = statusLog?.Status ?? EquipmentStatus.Down,
                StatusSince = statusLog?.StartedAt,
                LastSeenAt = c.LastSeenAt,
                TodayTotal = insp?.Total ?? 0,
                TodayPass = insp?.Pass ?? 0,
                TodayNg = (insp?.Total ?? 0) - (insp?.Pass ?? 0),
                ActiveWorkOrderNo = wo?.OrderNo,
                ActiveProductCode = wo?.Product?.Code,
                ActiveWorkOrderProgress = wo is null || wo.PlannedQuantity == 0
                    ? null
                    : (int)Math.Round((double)wo.ProducedQuantity / wo.PlannedQuantity * 100),
                OpenAlarmCount = alarm?.Total ?? 0,
                CriticalAlarmCount = alarm?.Critical ?? 0
            };
            lines.Add(line);
        }

        // === 전체 KPI ===
        var totalToday = lines.Sum(l => l.TodayTotal);
        var passToday = lines.Sum(l => l.TodayPass);
        var ngToday = totalToday - passToday;

        // 활성 알람 Top 10 (미해제 + Critical/Major 우선)
        var activeAlarmRaws = await _db.AlarmEvents
            .Include(a => a.Client)
            .Where(a => a.ResolvedAt == null)
            .OrderByDescending(a => a.Severity == AlarmSeverity.Critical)
            .ThenByDescending(a => a.OccurredAt)
            .Take(10)
            .ToListAsync();

        var activeAlarmDtos = activeAlarmRaws.Select(a => new AlarmEventDto
        {
            Id = a.Id,
            ClientId = a.ClientId,
            ClientName = a.Client?.Name,
            ClientIndex = a.Client?.ClientIndex,
            AlarmType = a.AlarmType,
            Severity = a.Severity,
            Title = a.Title,
            Message = a.Message,
            OccurredAt = a.OccurredAt,
            AcknowledgedAt = a.AcknowledgedAt,
            AcknowledgedByName = a.AcknowledgedByName,
            ResolvedAt = a.ResolvedAt,
            Resolution = a.Resolution
        }).ToList();

        // === Predictive Insights (Plan §6 Phase E + §8 Open Q #5) ===
        // PredictionService.GetSnapshotAsync 는 active 클라이언트 모두에 대해 60s 캐시 hit 위주로
        // 병렬 호출 — 안돈 응답 추가 latency 미미. ClientName 매핑 위해 lines 를 참조.
        var clientNameByIdx = lines.ToDictionary(l => l.ClientIndex, l => l.ClientName);
        var clientIdByIdx   = lines.ToDictionary(l => l.ClientIndex, l => l.ClientId);
        var predictiveInsights = new List<PredictiveInsightDto>();
        try
        {
            var predSnap = await _prediction.GetSnapshotAsync();
            foreach (var item in predSnap.Items)
            {
                if (item.Status != "ok" || !item.PredictedNgRate.HasValue) continue;
                var rate = item.PredictedNgRate.Value;
                if (rate < InsightMediumThreshold) continue;  // 정상 — Insight 채널 미포함

                var severity = rate >= InsightHighThreshold ? "high" : "medium";
                clientNameByIdx.TryGetValue(item.ClientIndex, out var name);
                clientIdByIdx.TryGetValue(item.ClientIndex, out var cid);

                predictiveInsights.Add(new PredictiveInsightDto
                {
                    ClientId = cid,
                    ClientIndex = item.ClientIndex,
                    ClientName = name ?? $"Client#{item.ClientIndex}",
                    RecipeName = item.RecipeName,
                    PredictedNgRate = rate,
                    Severity = severity,
                    WindowStart = item.WindowStart,
                    ModelName = item.ModelName,
                    ModelVersion = item.ModelVersion,
                });
            }
            // High 가 위로
            predictiveInsights = predictiveInsights
                .OrderByDescending(i => i.Severity == "high")
                .ThenByDescending(i => i.PredictedNgRate)
                .ToList();
        }
        catch
        {
            // 예측 인프라 장애가 안돈 전체 응답을 막아선 안 됨 — insights 빈 리스트로 폴백
        }

        return new AndonSnapshotDto
        {
            AsOf = asOf,
            Lines = lines,
            TotalToday = totalToday,
            PassToday = passToday,
            NgToday = ngToday,
            RunningLines = lines.Count(l => l.Status == EquipmentStatus.Running),
            IdleLines = lines.Count(l => l.Status == EquipmentStatus.Idle),
            DownLines = lines.Count(l => l.Status == EquipmentStatus.Down),
            ActiveAlarms = activeAlarmDtos,
            PredictiveInsights = predictiveInsights,
        };
    }
}
