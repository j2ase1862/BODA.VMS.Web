using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Endpoints;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// /api/settings/visionserver PUT 의 인가/검증 흐름 검증.
/// 양성 (성공 → appsettings.json 쓰기) 시나리오는 disk 부작용이라 통합 테스트에서 제외 —
/// Validator 단위 테스트가 양성/음성 입력 매트릭스 커버.
/// </summary>
public class VisionServerSettingsValidationTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public VisionServerSettingsValidationTests()
    {
        _factory = new DbBackedIntegrationTestFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> SeedAndLoginAsync(string role = "Admin")
    {
        using (var db = _factory.CreateScopedDbContext())
        {
            db.Users.Add(new User
            {
                Username = "settings_user",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret12"),
                DisplayName = "Settings User",
                Role = role,
                IsApproved = true
            });
            await db.SaveChangesAsync();
        }

        var resp = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = "settings_user", Password = "secret12" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private async Task<HttpResponseMessage> PutVisionServerAsync(string? token, VisionServerSettingsRequest request)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/settings/visionserver");
        if (token is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(request);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task PUT_visionserver_without_token_returns_401()
    {
        var resp = await PutVisionServerAsync(null, new VisionServerSettingsRequest(true, "http://x"));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_visionserver_with_user_role_returns_403()
    {
        var token = await SeedAndLoginAsync("User");
        var resp = await PutVisionServerAsync(token, new VisionServerSettingsRequest(true, "http://x"));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("vision.example.com")]
    [InlineData("ftp://server/path")]
    [InlineData("file:///C:/secret.txt")]
    public async Task PUT_visionserver_invalid_baseurl_returns_400(string invalidUrl)
    {
        var token = await SeedAndLoginAsync("Admin");
        var resp = await PutVisionServerAsync(token, new VisionServerSettingsRequest(true, invalidUrl));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("errors")
            .TryGetProperty("BaseUrl", out _).Should().BeTrue();
    }
}
