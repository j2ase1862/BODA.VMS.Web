using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 작업자 출근/교대 세션 시계열. 라인(Client)별로 EndedAt이 NULL인 row가 최대 1개 — 그게 현재 작업자.
/// 검사 결과 수신 시 ClientId의 현재 세션을 조회해 OperatorId를 자동 부여합니다.
/// </summary>
public class OperatorSession
{
    [Key]
    public int Id { get; set; }

    public int OperatorId { get; set; }

    public int ClientId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>NULL이면 현재 작업중. 다음 작업자가 로그인하거나 명시적 로그아웃 시 채워짐.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Logout / ShiftChange / Auto (다른 작업자 로그인으로 강제 종료)</summary>
    [MaxLength(20)]
    public string? EndReason { get; set; }

    [ForeignKey(nameof(OperatorId))]
    public Operator Operator { get; set; } = null!;

    [ForeignKey(nameof(ClientId))]
    public VisionClient Client { get; set; } = null!;
}

public static class SessionEndReason
{
    public const string Logout = "Logout";
    public const string ShiftChange = "ShiftChange";
    public const string Auto = "Auto";
    /// <summary>VMS 가 graceful shutdown(/api/clients/disconnect) 으로 종료한 경우.</summary>
    public const string Disconnect = "Disconnect";
    /// <summary>Web startup 시 heartbeat 끊긴 클라이언트의 stale 세션 자동 정리.</summary>
    public const string Stale = "Stale";
}
