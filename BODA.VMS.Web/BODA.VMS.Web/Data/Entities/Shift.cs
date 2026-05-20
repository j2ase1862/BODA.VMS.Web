using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 교대조 마스터. 24시간을 N개 교대로 분할.
///
/// 야간 교대 처리: EndHour &lt; StartHour 이면 자정 넘어가는 교대로 간주
/// (예: 2조 22:00-06:00 → StartHour=22, EndHour=6)
/// </summary>
public class Shift
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>0-23 (정수 시간 단위로 단순화 — 분 단위 필요 시 추후 확장)</summary>
    public int StartHour { get; set; }

    /// <summary>0-23. EndHour &lt; StartHour 이면 야간 교대 (자정 wrap-around)</summary>
    public int EndHour { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>주어진 시각이 이 교대조에 속하는지 판단</summary>
    public bool Contains(DateTime time)
    {
        var h = time.ToLocalTime().Hour;
        if (StartHour < EndHour)
        {
            return h >= StartHour && h < EndHour;
        }
        // 야간 교대 (예: 22-6) → 22-23 또는 0-5
        return h >= StartHour || h < EndHour;
    }

    public int DurationHours => StartHour < EndHour
        ? EndHour - StartHour
        : 24 - StartHour + EndHour;
}
