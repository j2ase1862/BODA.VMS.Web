using System.Net.Http.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// 파라미터 변경 → /hubs/vms-public "RecipeParametersChanged" 즉시 푸시 (2026-08-11).
/// VisionSetup 이 이 이벤트를 받아 60초 폴링 없이 ParamCode 콤보를 갱신한다.
/// </summary>
public class ParamChangePushTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record ParamsChangedPayload(int RecipeId);

    [Fact]
    public async Task Parameter_create_broadcasts_RecipeParametersChanged()
    {
        // 시드: 사용자 + 클라이언트 + 레시피
        using (var db = _factory.CreateScopedDbContext())
        {
            db.Users.Add(new User
            {
                Username = "push_user",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret12"),
                DisplayName = "Push User",
                Role = "User",
                IsApproved = true
            });
            db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "127.0.0.1", ClientIndex = 1 });
            db.Recipes.Add(new Recipe { Id = 10, Name = "R", ClientId = 1 });
            await db.SaveChangesAsync();
        }

        using var http = _factory.CreateClient();
        var login = await http.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Username = "push_user", Password = "secret12" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // 익명 공개 허브 구독
        var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var conn = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/vms-public", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();
        conn.On<ParamsChangedPayload>("RecipeParametersChanged", p => received.TrySetResult(p.RecipeId));
        await conn.StartAsync();

        // 파라미터 생성 → 푸시 수신 확인
        var create = await http.PostAsJsonAsync("/api/parameters/", new RecipeParameterDto
        {
            RecipeId = 10,
            ParamCode = 1,
            ParamValue = 0,
            Description = "Total Area Reference",
            Category = "Blob"
        });
        create.EnsureSuccessStatusCode();

        var completed = await Task.WhenAny(received.Task, Task.Delay(5000));
        completed.Should().Be(received.Task, "이유: 파라미터 생성이 RecipeParametersChanged 를 브로드캐스트해야 함");
        (await received.Task).Should().Be(10);
    }
}
