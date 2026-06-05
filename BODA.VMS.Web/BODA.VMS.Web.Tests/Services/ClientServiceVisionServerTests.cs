using System.Net;
using System.Text;
using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// VisionServer 활성 (Enabled=true) 분기의 HttpClient 호출 검증 —
/// CreateClient / UpdateClient / DeleteClient / CreateRecipe / DeleteRecipe 5 메서드.
/// HttpMessageHandler stub 으로 외부 네트워크 없이 분기 + 응답 + 실패 경로 검증.
/// Standalone 분기는 ClientServiceTests 가 담당.
/// </summary>
public class ClientServiceVisionServerTests
{
    private const string VisionServerBaseUrl = "http://vision-server.test";

    private static (ClientService Svc, StubHandler Handler) BuildService(
        InMemorySqliteDbContext ctx,
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? baseUrl = VisionServerBaseUrl)
    {
        var stub = new StubHandler(respond);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientMonitor:HeartbeatTimeoutSeconds"] = "30"
            })
            .Build();

        var visionOptions = Substitute.For<IOptionsMonitor<VisionServerOptions>>();
        visionOptions.CurrentValue.Returns(new VisionServerOptions
        {
            Enabled = true,
            BaseUrl = baseUrl ?? string.Empty
        });

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient().Returns(_ => new HttpClient(stub, disposeHandler: false));

        var svc = new ClientService(ctx.Db, config, visionOptions, httpFactory,
            NullLogger<ClientService>.Instance);
        return (svc, stub);
    }

    // ── CreateClient ────────────────────────────────────────────

    [Fact]
    public async Task CreateClientAsync_persists_visionServer_assigned_id()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (svc, handler) = BuildService(ctx, _ => JsonOk(new
        {
            id = 42, name = "Line-A", ipAddress = "10.0.0.10", index = 7
        }));

        var created = await svc.CreateClientAsync(new ClientDto
        {
            Name = "Line-A", IpAddress = "10.0.0.10", ClientIndex = 7, IsActive = true
        });

        created.Id.Should().Be(42);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Be($"{VisionServerBaseUrl}/api/v1/Clients");
        (await ctx.Db.Clients.FindAsync(42)).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateClientAsync_throws_when_visionServer_returns_error_status()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (svc, _) = BuildService(ctx, _ => Fail(HttpStatusCode.BadRequest, "duplicate index"));

        var act = async () => await svc.CreateClientAsync(new ClientDto
        {
            Name = "X", IpAddress = "y", ClientIndex = 1
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*VisionServer Client creation failed*");
        (await ctx.Db.Clients.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateClientAsync_throws_when_visionServer_returns_invalid_id()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (svc, _) = BuildService(ctx, _ => JsonOk(new { id = 0, name = "x", ipAddress = "y", index = 1 }));

        var act = async () => await svc.CreateClientAsync(new ClientDto
        {
            Name = "X", IpAddress = "y", ClientIndex = 1
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid Client data*");
    }

    [Fact]
    public async Task CreateClientAsync_throws_when_baseUrl_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (svc, _) = BuildService(ctx, _ => JsonOk(new { }), baseUrl: "");

        var act = async () => await svc.CreateClientAsync(new ClientDto
        {
            Name = "X", IpAddress = "y", ClientIndex = 1
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*VisionServer:BaseUrl is not configured*");
    }

    // ── UpdateClient ────────────────────────────────────────────

    [Fact]
    public async Task UpdateClientAsync_posts_to_visionServer_with_id()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 5, Name = "Old", IpAddress = "10.0.0.5", ClientIndex = 5 });
        await ctx.Db.SaveChangesAsync();

        var (svc, handler) = BuildService(ctx, _ => new HttpResponseMessage(HttpStatusCode.OK));

        var updated = await svc.UpdateClientAsync(5, new ClientDto
        {
            Name = "New", IpAddress = "10.0.0.99", ClientIndex = 9, IsActive = true
        });

        updated.Should().NotBeNull();
        updated!.Name.Should().Be("New");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain("\"Id\":5").And.Contain("\"Index\":9");
    }

    [Fact]
    public async Task UpdateClientAsync_updates_web_db_even_when_visionServer_fails()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 5, Name = "Old", IpAddress = "10.0.0.5", ClientIndex = 5 });
        await ctx.Db.SaveChangesAsync();

        var (svc, _) = BuildService(ctx, _ => Fail(HttpStatusCode.InternalServerError, "boom"));

        var updated = await svc.UpdateClientAsync(5, new ClientDto
        {
            Name = "Renamed", IpAddress = "10.0.0.99", ClientIndex = 9, IsActive = false
        });

        updated.Should().NotBeNull();
        var entity = await ctx.Db.Clients.FindAsync(5);
        entity!.Name.Should().Be("Renamed");
        entity.IsActive.Should().BeFalse();
    }

    // ── DeleteClient ────────────────────────────────────────────

    [Fact]
    public async Task DeleteClientAsync_calls_visionServer_then_removes_web_db()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 7, Name = "x", IpAddress = "y", ClientIndex = 7 });
        await ctx.Db.SaveChangesAsync();

        var (svc, handler) = BuildService(ctx, _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        (await svc.DeleteClientAsync(7)).Should().BeTrue();
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[0].RequestUri!.ToString().Should().Be($"{VisionServerBaseUrl}/api/v1/Clients/7");
        (await ctx.Db.Clients.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteClientAsync_removes_from_web_db_even_when_visionServer_fails()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 7, Name = "x", IpAddress = "y", ClientIndex = 7 });
        await ctx.Db.SaveChangesAsync();

        var (svc, _) = BuildService(ctx, _ => throw new HttpRequestException("network down"));

        (await svc.DeleteClientAsync(7)).Should().BeTrue();  // Web DB 삭제는 성공
        (await ctx.Db.Clients.CountAsync()).Should().Be(0);
    }

    // ── CreateRecipe ────────────────────────────────────────────

    [Fact]
    public async Task CreateRecipeAsync_persists_visionServer_assigned_recipeId()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "10.0.0.1", ClientIndex = 1 });
        await ctx.Db.SaveChangesAsync();

        var (svc, handler) = BuildService(ctx, _ => JsonOk(new
        {
            recipeId = 99, recipeName = "R-99", clientId = 1, recipeIndex = 1
        }));

        var created = await svc.CreateRecipeAsync(1, new RecipeDto { Name = "R-99" });

        created.Id.Should().Be(99);
        created.Name.Should().Be("R-99");
        handler.Requests[0].RequestUri!.ToString().Should().Be($"{VisionServerBaseUrl}/api/v1/Recipes");
        (await ctx.Db.Recipes.FindAsync(99)).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateRecipeAsync_throws_when_visionServer_fails()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "10.0.0.1", ClientIndex = 1 });
        await ctx.Db.SaveChangesAsync();

        var (svc, _) = BuildService(ctx, _ => Fail(HttpStatusCode.BadGateway, "downstream error"));

        var act = async () => await svc.CreateRecipeAsync(1, new RecipeDto { Name = "X" });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*VisionServer Recipe creation failed*");
    }

    // ── DeleteRecipe ────────────────────────────────────────────

    [Fact]
    public async Task DeleteRecipeAsync_calls_visionServer_then_removes_web_db()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "10.0.0.1", ClientIndex = 1 });
        ctx.Db.Recipes.Add(new Recipe { Id = 50, Name = "R", ClientId = 1 });
        await ctx.Db.SaveChangesAsync();

        var (svc, handler) = BuildService(ctx, _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        (await svc.DeleteRecipeAsync(50)).Should().BeTrue();
        handler.Requests[0].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[0].RequestUri!.ToString().Should().Be($"{VisionServerBaseUrl}/api/v1/Recipes/50");
        (await ctx.Db.Recipes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteRecipeAsync_removes_from_web_db_even_when_visionServer_fails()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "10.0.0.1", ClientIndex = 1 });
        ctx.Db.Recipes.Add(new Recipe { Id = 50, Name = "R", ClientId = 1 });
        await ctx.Db.SaveChangesAsync();

        var (svc, _) = BuildService(ctx, _ => throw new HttpRequestException("timeout"));

        (await svc.DeleteRecipeAsync(50)).Should().BeTrue();
        (await ctx.Db.Recipes.CountAsync()).Should().Be(0);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static HttpResponseMessage JsonOk(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Fail(HttpStatusCode code, string message) => new(code)
    {
        Content = new StringContent(message, Encoding.UTF8, "text/plain")
    };

    /// <summary>요청을 캡처하고 미리 정의된 응답을 반환하는 stub.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }
}
