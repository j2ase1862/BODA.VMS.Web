using System.Net;
using System.Net.Http.Json;
using BODA.VMS.Web.Client.Models;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// ValidationEndpointFilter 가 잘못된 DTO 를 ProblemDetails JSON + 400 으로
/// 반환하는지 검증 — endpoint handler / DB 도달 전 차단.
/// </summary>
public class ValidationFilterTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public ValidationFilterTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task POST_register_with_empty_username_returns_400_problem_details()
    {
        var dto = new RegisterRequest
        {
            Username = "",        // ValidationFilter 가 reject
            Password = "secret12",
            DisplayName = "X"
        };

        var response = await _client.PostAsJsonAsync("/api/auth/register", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().BeOneOf(
            "application/problem+json", "application/json");
    }

    [Fact]
    public async Task POST_register_with_short_password_returns_400()
    {
        // RegisterRequestValidator 의 강화된 정책 — MinLength=8
        var dto = new RegisterRequest
        {
            Username = "alice",
            Password = "abc123",  // 6 자
            DisplayName = "Alice"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/register", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_login_with_valid_format_passes_filter()
    {
        // ValidationFilter 통과 후 AuthService.LoginAsync 가 user 없으면 401 반환
        // (filter 가 throw 하지 않고 통과시킨다는 것을 401 응답으로 간접 확인)
        var dto = new LoginRequest { Username = "nonexistent", Password = "secret123" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
