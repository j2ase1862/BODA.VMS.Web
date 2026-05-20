using System.Text.Json;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BODA.VMS.Web.Data;

/// <summary>
/// EF SaveChanges 인터셉터로 마스터/설정 엔티티 변경을 AuditLog에 자동 기록.
///
/// **트래킹 대상**: 자주 변경되지 않으면서 추적이 중요한 엔티티 (마스터/설정).
/// **제외**: 시계열 (InspectionHistory, ParameterMeasurement, EquipmentStatusLog),
///           감사로그 자체 (재귀 방지), 자동 카운터 갱신 노이즈.
/// </summary>
public class AuditInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> _tracked = new(StringComparer.Ordinal)
    {
        nameof(BodaVmsDbContext.Recipes),
        nameof(BodaVmsDbContext.RecipeParameters),
        nameof(BodaVmsDbContext.Products),
        nameof(BodaVmsDbContext.WorkOrders),
        nameof(BodaVmsDbContext.Lots),
        nameof(BodaVmsDbContext.DefectCodes),
        nameof(BodaVmsDbContext.Shifts),
        nameof(BodaVmsDbContext.Users),
        nameof(BodaVmsDbContext.Clients)
    };

    // 위 DbSet 이름과 실제 entity 타입 이름 매핑 (단수형으로 변환)
    private static readonly HashSet<string> _trackedEntityNames = new(StringComparer.Ordinal)
    {
        nameof(Recipe),
        nameof(RecipeParameter),
        nameof(Product),
        nameof(WorkOrder),
        nameof(Lot),
        nameof(DefectCode),
        nameof(Shift),
        nameof(User),
        nameof(VisionClient)
    };

    /// <summary>변경 추적에서 제외할 속성들 (노이즈 감소)</summary>
    private static readonly HashSet<string> _ignoredProps = new(StringComparer.Ordinal)
    {
        "UpdatedAt", "CreatedAt", "LastSeenAt", "LastHeartbeatIp",
        "ProducedQuantity", "PassQuantity", "NgQuantity", // WorkOrder 자동 카운터
        "Quantity", "PassCount", "NgCount" // Lot 자동 카운터
    };

    private readonly ICurrentUserService _currentUser;

    public AuditInterceptor(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context;
        if (ctx is null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var logs = new List<AuditLog>();
        var userId = _currentUser.UserId;
        var userName = _currentUser.UserName;
        var ip = _currentUser.IpAddress;

        foreach (var entry in ctx.ChangeTracker.Entries())
        {
            var entityName = entry.Entity.GetType().Name;
            if (!_trackedEntityNames.Contains(entityName)) continue;
            if (entry.State == EntityState.Unchanged || entry.State == EntityState.Detached) continue;

            string? changes = null;
            string action;

            switch (entry.State)
            {
                case EntityState.Added:
                    action = AuditAction.Create;
                    changes = SerializeNewState(entry);
                    break;
                case EntityState.Modified:
                    action = AuditAction.Update;
                    changes = SerializeChanges(entry);
                    if (changes is null) continue; // 추적할 변경 없음 (모두 ignored 속성)
                    break;
                case EntityState.Deleted:
                    action = AuditAction.Delete;
                    changes = SerializeOldState(entry);
                    break;
                default:
                    continue;
            }

            logs.Add(new AuditLog
            {
                EntityName = entityName,
                EntityId = GetEntityId(entry),
                Action = action,
                Changes = changes,
                Timestamp = DateTime.UtcNow,
                UserId = userId,
                UserName = userName ?? "system",
                IpAddress = ip
            });
        }

        if (logs.Count > 0)
        {
            ctx.Set<AuditLog>().AddRange(logs);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static string? GetEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return null;
        var values = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .Where(v => v is not null)
            .ToList();
        return string.Join(",", values);
    }

    private static string? SerializeChanges(EntityEntry entry)
    {
        var diff = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            if (!prop.IsModified) continue;
            if (_ignoredProps.Contains(prop.Metadata.Name)) continue;
            // 값이 실제로 같으면 EF가 Modified로 표시해도 의미 없음
            if (Equals(prop.OriginalValue, prop.CurrentValue)) continue;

            diff[prop.Metadata.Name] = new
            {
                old = prop.OriginalValue,
                @new = prop.CurrentValue
            };
        }
        return diff.Count == 0 ? null : JsonSerializer.Serialize(diff);
    }

    private static string SerializeNewState(EntityEntry entry)
    {
        var snap = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            if (_ignoredProps.Contains(prop.Metadata.Name)) continue;
            snap[prop.Metadata.Name] = prop.CurrentValue;
        }
        return JsonSerializer.Serialize(snap);
    }

    private static string SerializeOldState(EntityEntry entry)
    {
        var snap = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            if (_ignoredProps.Contains(prop.Metadata.Name)) continue;
            snap[prop.Metadata.Name] = prop.OriginalValue;
        }
        return JsonSerializer.Serialize(snap);
    }
}
