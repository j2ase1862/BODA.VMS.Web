using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BODA.VMS.Web.Hubs;

/// <summary>
/// VMS 클라이언트 (익명 endpoint 사용자) 와 관리자 라이브 모니터링 화면을 위한 SignalR Hub.
/// VmsHub 는 JWT 인증이 필요하지만 VmsPublicHub 는 익명 — 운영 흐름의 진행률 푸시용.
///
/// 송신 이벤트:
/// - WorkOrderUpdated   : 검사 결과 업로드로 WO 진행률 변경 시 (모든 클라이언트 broadcast)
/// - WorkOrderCompleted : 계획 수량 도달로 막 Completed 전이된 WO (모든 클라이언트 broadcast)
///
/// 페이로드는 InspectionItemEndpoints 의 workOrder dict 와 동일 형태.
/// </summary>
[AllowAnonymous]
public class VmsPublicHub : Hub
{
    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        return base.OnDisconnectedAsync(exception);
    }
}
