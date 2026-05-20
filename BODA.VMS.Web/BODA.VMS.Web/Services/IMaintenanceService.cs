using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IMaintenanceService
{
    Task<List<MaintenanceScheduleDto>> GetSchedulesAsync(bool includeInactive = false, int? clientId = null);
    Task<MaintenanceScheduleDto?> GetScheduleByIdAsync(int id);
    Task<MaintenanceScheduleDto> CreateScheduleAsync(MaintenanceScheduleUpsertDto dto);
    Task<MaintenanceScheduleDto?> UpdateScheduleAsync(int id, MaintenanceScheduleUpsertDto dto);
    Task<bool> DeleteScheduleAsync(int id);

    /// <summary>곧 수행할 일정 (Overdue + Soon + DueToday)</summary>
    Task<List<MaintenanceScheduleDto>> GetDueSchedulesAsync(int withinDays = 7);

    /// <summary>PM 수행 기록. NextDueAt = PerformedAt + IntervalDays 자동 갱신.</summary>
    Task<MaintenanceRecordDto> PerformAsync(int scheduleId, PerformMaintenanceRequest req,
        int? userId, string? userName);

    Task<List<MaintenanceRecordDto>> GetRecordsAsync(
        int? scheduleId, int? clientId,
        DateTime? startDate, DateTime? endDate, int limit = 200);
}
