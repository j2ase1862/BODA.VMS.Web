using System.Net.Http.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// SignalR Hub 연결 인증 검증.
///
/// 현재 운영 코드 상태:
/// - VmsHub (/hubs/vms): [Authorize] 없음 — 익명 접근 가능 (★ GS 갭 후보)
/// - VmsPublicHub (/hubs/vms-public): 의도된 익명 — 운영 흐름 푸시용
/// - JwtBearerEvents.OnMessageReceived: /hubs 경로의 ?access_token= 쿼리스트링을
///   Bearer 토큰으로 변환 — SignalR WebSocket 인증 표준 패턴
///
/// 본 PR 테스트:
/// 1) 두 Hub 모두 익명 연결 가능 (현재 동작 문서화)
/// 2) JWT 쿼리스트링이 정상 연결 흐름을 깨지 않음 (회귀 방어)
///
/// 후속: VmsHub 에 [Authorize] 추가 후 음성(토큰 없음 → 연결 실패) +
/// 양성(유효 토큰 → 연결 + ClaimsPrincipal) 시나리오로 확장.
/// </summary>
public class SignalRHubTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory;

    public SignalRHubTests()
    {
        _factory = new DbBackedIntegrationTestFactory();
    }

    public void Dispose() => _factory.Dispose();

    private HubConnection BuildConnection(string hubPath, string? accessToken = null)
    {
        return new HubConnectionBuilder()
            .WithUrl($"http://localhost{hubPath}", options =>
            {
                // WebApplicationFactory 의 in-memory test server 사용
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // WebSocket 은 in-memory server 에서 미지원 — LongPolling 강제
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();
    }

    [Fact]
    public async Task Public_hub_anonymous_connect_succeeds()
    {
        await using var conn = BuildConnection("/hubs/vms-public");
        await conn.StartAsync();
        conn.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Private_hub_anonymous_connect_currently_succeeds()
    {
        // 현재 운영 코드 상태 문서화 — VmsHub 에 [Authorize] 없음
        // 후속 작업: [Authorize] 추가 후 본 테스트를 음성(연결 실패)로 변경
        await using var conn = BuildConnection("/hubs/vms");
        await conn.StartAsync();
        conn.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Private_hub_with_jwt_access_token_connects_successfully()
    {
        // 시드 + login → JWT 발급
        using (var db = _factory.CreateScopedDbContext())
        {
            db.Users.Add(new User
            {
                Username = "hub_user",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret12"),
                DisplayName = "Hub User",
                Role = "User",
                IsApproved = true
            });
            await db.SaveChangesAsync();
        }

        using var http = _factory.CreateClient();
        var login = await http.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = "hub_user", Password = "secret12" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        // AccessTokenProvider 가 ?access_token=... 으로 부착
        // → JwtBearerEvents.OnMessageReceived 가 인식해 Bearer 로 변환
        await using var conn = BuildConnection("/hubs/vms", token);
        await conn.StartAsync();
        conn.State.Should().Be(HubConnectionState.Connected);
    }
}
