using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Data;

public class BodaVmsDbContext : DbContext
{
    public BodaVmsDbContext(DbContextOptions<BodaVmsDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<VisionClient> Clients => Set<VisionClient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Step> Steps => Set<Step>();
    public DbSet<InspectionTool> InspectionTools => Set<InspectionTool>();
    public DbSet<InspectionHistory> InspectionHistories => Set<InspectionHistory>();
    public DbSet<RecipeParameter> RecipeParameters => Set<RecipeParameter>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<DefectCode> DefectCodes => Set<DefectCode>();
    public DbSet<ParameterMeasurement> ParameterMeasurements => Set<ParameterMeasurement>();
    public DbSet<EquipmentStatusLog> EquipmentStatusLogs => Set<EquipmentStatusLog>();
    public DbSet<AlarmEvent> AlarmEvents => Set<AlarmEvent>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<OperatorSession> OperatorSessions => Set<OperatorSession>();
    public DbSet<MaintenanceSchedule> MaintenanceSchedules => Set<MaintenanceSchedule>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<MLModel> MLModels => Set<MLModel>();
    public DbSet<PredictionLog> PredictionLogs => Set<PredictionLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        // VisionServer가 AUTOINCREMENT로 Id 부여 — Web은 읽기 전용으로 사용
        modelBuilder.Entity<VisionClient>()
            .Property(c => c.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<VisionClient>()
            .HasIndex(c => c.ClientIndex)
            .IsUnique();

        // Recipe도 VisionServer가 ID 관리
        modelBuilder.Entity<Recipe>()
            .Property(r => r.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => h.InspectedAt);

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => new { h.ClientId, h.InspectedAt });

        modelBuilder.Entity<RecipeParameter>()
            .HasIndex(p => new { p.RecipeId, p.ParamCode })
            .IsUnique();

        modelBuilder.Entity<Product>()
            .HasIndex(p => p.Code)
            .IsUnique();

        modelBuilder.Entity<WorkOrder>()
            .HasIndex(w => w.OrderNo)
            .IsUnique();

        modelBuilder.Entity<WorkOrder>()
            .HasIndex(w => new { w.Status, w.PlannedStartAt });

        modelBuilder.Entity<Lot>()
            .HasIndex(l => l.LotNumber)
            .IsUnique();

        modelBuilder.Entity<Lot>()
            .HasIndex(l => new { l.WorkOrderId, l.Sequence })
            .IsUnique();

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => h.WorkOrderId);

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => h.LotId);

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => h.SerialNumber);

        modelBuilder.Entity<DefectCode>()
            .HasIndex(d => d.Code)
            .IsUnique();

        modelBuilder.Entity<ParameterMeasurement>()
            .HasIndex(m => new { m.RecipeId, m.ParamCode, m.InspectedAt });

        modelBuilder.Entity<ParameterMeasurement>()
            .HasIndex(m => m.HistoryId);

        modelBuilder.Entity<ParameterMeasurement>()
            .HasIndex(m => m.WorkOrderId);

        modelBuilder.Entity<EquipmentStatusLog>()
            .HasIndex(e => new { e.ClientId, e.StartedAt });

        modelBuilder.Entity<EquipmentStatusLog>()
            .HasIndex(e => new { e.ClientId, e.EndedAt });

        modelBuilder.Entity<AlarmEvent>()
            .HasIndex(a => a.OccurredAt);

        modelBuilder.Entity<AlarmEvent>()
            .HasIndex(a => new { a.ClientId, a.OccurredAt });

        modelBuilder.Entity<AlarmEvent>()
            .HasIndex(a => new { a.AcknowledgedAt, a.ResolvedAt });

        modelBuilder.Entity<Shift>()
            .HasIndex(s => s.Name)
            .IsUnique();

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => h.ShiftId);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.Timestamp);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.EntityName, a.EntityId });

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.UserId);

        modelBuilder.Entity<Operator>()
            .HasIndex(o => o.EmployeeNumber)
            .IsUnique();

        modelBuilder.Entity<OperatorSession>()
            .HasIndex(s => new { s.ClientId, s.EndedAt });

        modelBuilder.Entity<OperatorSession>()
            .HasIndex(s => new { s.OperatorId, s.StartedAt });

        modelBuilder.Entity<MaintenanceSchedule>()
            .HasIndex(m => new { m.IsActive, m.NextDueAt });

        modelBuilder.Entity<MaintenanceRecord>()
            .HasIndex(r => new { r.ScheduleId, r.PerformedAt });

        // Predictive_DefectRate_Plan §3.2/§4.2 — 예측 인프라
        modelBuilder.Entity<SensorReading>()
            .HasIndex(s => new { s.ClientId, s.Timestamp });

        modelBuilder.Entity<MLModel>()
            .HasIndex(m => new { m.Name, m.IsActive });

        modelBuilder.Entity<MLModel>()
            .HasIndex(m => new { m.Name, m.Version })
            .IsUnique();

        modelBuilder.Entity<PredictionLog>()
            .HasIndex(p => new { p.ClientId, p.RecipeId, p.WindowStart });

        modelBuilder.Entity<PredictionLog>()
            .HasIndex(p => p.MLModelId);

        // GS 보안 — refresh token 해시 조회 인덱스 (UNIQUE: 해시 충돌 불가 + 빠른 검증)
        modelBuilder.Entity<RefreshToken>()
            .HasIndex(t => t.TokenHash)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(t => t.UserId);
    }
}
