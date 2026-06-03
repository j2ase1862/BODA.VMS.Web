using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BODA.VMS.Web.Hubs;

/// <summary>
/// 인증된 클라이언트 전용 운영 hub. JWT Bearer 토큰 필수 — SignalR WebSocket 은
/// Authorization 헤더 제약으로 JwtBearerEvents.OnMessageReceived 가 ?access_token=
/// 쿼리스트링을 Bearer 로 변환해 처리.
/// 익명 푸시가 필요한 경우는 VmsPublicHub (/hubs/vms-public) 사용.
/// </summary>
[Authorize]
public class VmsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
