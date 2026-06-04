using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// POST /api/parameters/batch 의 CollectionValidationEndpointFilter 동작 검증.
/// 단건 endpoint 는 ValidationEndpointFilter 가, batch endpoint 는 본 PR 의
/// CollectionValidationEndpointFilter 가 각 항목 검증 — 잘못된 항목이 섞여
/// 통과하면 GS 인증 입력 검증 항목 탈락.
/// </summary>
public class RecipeParameterBatchValidationTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public RecipeParameterBatchValidationTests()
    {
        _factory = new DbBackedIntegrationTestFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> SeedAndLoginAsync(string role = "User")
    {
        using (var db = _factory.CreateScopedDbContext())
        {
            db.Users.Add(new User
            {
                Username = "batch_user",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret12"),
                DisplayName = "Batch User",
                Role = role,
                IsApproved = true
            });
            // RecipeParameter FK 부모 — Recipe (Recipe.ClientId → Client 도 필요).
            // VisionClient/Recipe 는 ValueGeneratedNever → Id 명시.
            db.Clients.Add(new VisionClient
            {
                Id = 1, Name = "L0", IpAddress = "127.0.0.1", ClientIndex = 0,
                IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            db.Recipes.Add(new Recipe
            {
                Id = 1, Name = "R0", ClientId = 1, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var resp = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = "batch_user", Password = "secret12" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private static RecipeParameterDto ValidDto(int paramCode, int recipeId = 1)
        => new()
        {
            RecipeId = recipeId,
            ParamCode = paramCode,
            ParamValue = 0.5,
            Description = $"Param {paramCode}",
            Category = "Dimension",
            Unit = "mm",
            LowerLimit = 0,
            UpperLimit = 10,
            IsActive = true
        };

    private async Task<HttpResponseMessage> PostBatchAsync(string token, List<RecipeParameterDto> items)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/parameters/batch");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(items);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task POST_batch_without_token_returns_401()
    {
        var resp = await _client.PostAsJsonAsync("/api/parameters/batch", new List<RecipeParameterDto>());
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_batch_with_all_valid_items_returns_201()
    {
        var token = await SeedAndLoginAsync();
        var resp = await PostBatchAsync(token, new List<RecipeParameterDto>
        {
            ValidDto(1),
            ValidDto(2),
            ValidDto(3)
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        using var db = _factory.CreateScopedDbContext();
        db.RecipeParameters.Should().HaveCount(3);
    }

    [Fact]
    public async Task POST_batch_with_invalid_item_returns_400_with_indexed_error_key()
    {
        var token = await SeedAndLoginAsync();

        var invalid = ValidDto(2);
        invalid.Description = ""; // NotEmpty 위반

        var resp = await PostBatchAsync(token, new List<RecipeParameterDto>
        {
            ValidDto(1),
            invalid,
            ValidDto(3)
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var errors = doc.RootElement.GetProperty("errors");

        // [1].Description 키 (0-based index) 가 존재해야 함
        errors.TryGetProperty("[1].Description", out var msgs).Should().BeTrue(
            "이유: 두번째 항목(index=1)의 Description NotEmpty 위반이 indexed key 로 보고돼야 함");
        msgs.GetArrayLength().Should().BeGreaterThan(0);

        // 다른 항목은 valid 라 키가 없어야 함
        errors.TryGetProperty("[0].Description", out _).Should().BeFalse();
        errors.TryGetProperty("[2].Description", out _).Should().BeFalse();

        // 1 행도 저장되지 않음 — 검증 단계에서 전체 거부
        using var db = _factory.CreateScopedDbContext();
        db.RecipeParameters.Should().BeEmpty();
    }

    [Fact]
    public async Task POST_batch_with_multiple_invalid_items_aggregates_all_errors()
    {
        var token = await SeedAndLoginAsync();

        var bad0 = ValidDto(1);
        bad0.Description = ""; // NotEmpty

        var bad2 = ValidDto(3);
        bad2.Category = "Unknown"; // Must in allowed list 위반

        var resp = await PostBatchAsync(token, new List<RecipeParameterDto>
        {
            bad0,
            ValidDto(2),
            bad2
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var errors = doc.RootElement.GetProperty("errors");

        errors.TryGetProperty("[0].Description", out _).Should().BeTrue();
        errors.TryGetProperty("[2].Category", out _).Should().BeTrue();
        errors.TryGetProperty("[1].Description", out _).Should().BeFalse();
    }

    [Fact]
    public async Task POST_batch_with_empty_list_returns_201_with_zero_inserts()
    {
        var token = await SeedAndLoginAsync();

        // 빈 리스트는 필터가 통과시키고 서비스가 0개 생성 — 명시적 약속 보존
        var resp = await PostBatchAsync(token, new List<RecipeParameterDto>());

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        using var db = _factory.CreateScopedDbContext();
        db.RecipeParameters.Should().BeEmpty();
    }
}
