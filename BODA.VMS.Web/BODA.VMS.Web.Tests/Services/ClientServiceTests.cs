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
/// VisionServer 비활성 (Standalone) 모드만 검증 — VisionServer 활성 분기는
/// HttpMessageHandler stub 필요 + 별도 테스트셋이 적합 (현재 미구현).
/// </summary>
public class ClientServiceTests
{
    private static ClientService BuildService(InMemorySqliteDbContext ctx)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientMonitor:HeartbeatTimeoutSeconds"] = "30"
            })
            .Build();

        var visionOptions = Substitute.For<IOptionsMonitor<VisionServerOptions>>();
        visionOptions.CurrentValue.Returns(new VisionServerOptions { Enabled = false });

        var httpFactory = Substitute.For<IHttpClientFactory>();  // standalone 모드는 미호출

        return new ClientService(
            ctx.Db,
            config,
            visionOptions,
            httpFactory,
            NullLogger<ClientService>.Instance);
    }

    [Fact]
    public async Task GetAllClientsAsync_orders_by_clientIndex()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 2, Name = "B", IpAddress = "10.0.0.2", ClientIndex = 2 });
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "A", IpAddress = "10.0.0.1", ClientIndex = 1 });
        await ctx.Db.SaveChangesAsync();

        var svc = BuildService(ctx);
        var rows = await svc.GetAllClientsAsync();

        rows.Select(c => c.Name).Should().Equal("A", "B");
    }

    [Fact]
    public async Task GetAllClientsAsync_marks_isAlive_when_recent_heartbeat()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient
        {
            Id = 1, Name = "Alive", IpAddress = "10.0.0.1", ClientIndex = 1,
            LastSeenAt = DateTime.UtcNow.AddSeconds(-5)
        });
        ctx.Db.Clients.Add(new VisionClient
        {
            Id = 2, Name = "Dead", IpAddress = "10.0.0.2", ClientIndex = 2,
            LastSeenAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await ctx.Db.SaveChangesAsync();

        var svc = BuildService(ctx);
        var rows = await svc.GetAllClientsAsync();

        rows.First(c => c.Name == "Alive").IsAlive.Should().BeTrue();
        rows.First(c => c.Name == "Dead").IsAlive.Should().BeFalse();
    }

    [Fact]
    public async Task GetClientByIdAsync_returns_null_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx);
        (await svc.GetClientByIdAsync(9999)).Should().BeNull();
    }

    [Fact]
    public async Task CreateClientAsync_assigns_next_id_in_standalone_mode()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 5, Name = "Existing", IpAddress = "10.0.0.5", ClientIndex = 5 });
        await ctx.Db.SaveChangesAsync();

        var svc = BuildService(ctx);
        var created = await svc.CreateClientAsync(new ClientDto
        {
            Name = "New", IpAddress = "10.0.0.6", ClientIndex = 6, IsActive = true
        });

        created.Id.Should().Be(6);  // max(5) + 1
        created.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateClientAsync_assigns_id_1_when_empty()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx);
        var created = await svc.CreateClientAsync(new ClientDto
        {
            Name = "First", IpAddress = "10.0.0.1", ClientIndex = 1, IsActive = true
        });

        created.Id.Should().Be(1);
    }

    [Fact]
    public async Task UpdateClientAsync_modifies_fields()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient
        {
            Id = 1, Name = "Old", IpAddress = "10.0.0.1", ClientIndex = 1, IsActive = true
        });
        await ctx.Db.SaveChangesAsync();

        var svc = BuildService(ctx);
        var updated = await svc.UpdateClientAsync(1, new ClientDto
        {
            Name = "Renamed", IpAddress = "10.0.0.99", ClientIndex = 9, IsActive = false
        });

        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Renamed");
        updated.IpAddress.Should().Be("10.0.0.99");
        updated.ClientIndex.Should().Be(9);
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateClientAsync_returns_null_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx);
        var result = await svc.UpdateClientAsync(9999, new ClientDto { Name = "x", IpAddress = "y", ClientIndex = 1 });
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteClientAsync_removes_entity()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "X", IpAddress = "y", ClientIndex = 1 });
        await ctx.Db.SaveChangesAsync();

        var svc = BuildService(ctx);
        (await svc.DeleteClientAsync(1)).Should().BeTrue();
        (await ctx.Db.Clients.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteClientAsync_returns_false_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx);
        (await svc.DeleteClientAsync(9999)).Should().BeFalse();
    }

    [Fact]
    public async Task GetRecipesByClientIdAsync_filters_and_orders_by_recipeIndex()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "10.0.0.1", ClientIndex = 1 });
        ctx.Db.Clients.Add(new VisionClient { Id = 2, Name = "L2", IpAddress = "10.0.0.2", ClientIndex = 2 });
        ctx.Db.Recipes.Add(new Recipe { Id = 10, Name = "R-B", ClientId = 1, RecipeIndex = 2 });
        ctx.Db.Recipes.Add(new Recipe { Id = 11, Name = "R-A", ClientId = 1, RecipeIndex = 1 });
        ctx.Db.Recipes.Add(new Recipe { Id = 20, Name = "OTHER", ClientId = 2, RecipeIndex = 1 });
        await ctx.Db.SaveChangesAsync();

        var svc = BuildService(ctx);
        var rows = await svc.GetRecipesByClientIdAsync(1);

        rows.Select(r => r.Name).Should().Equal("R-A", "R-B");
    }

    [Fact]
    public async Task CreateRecipeAsync_assigns_next_id_and_index_in_standalone_mode()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "10.0.0.1", ClientIndex = 1 });
        ctx.Db.Recipes.Add(new Recipe { Id = 10, Name = "Existing", ClientId = 1, RecipeIndex = 3 });
        await ctx.Db.SaveChangesAsync();

        var svc = BuildService(ctx);
        var created = await svc.CreateRecipeAsync(1, new RecipeDto
        {
            Name = "NewRecipe", Description = "desc"
        });

        created.Id.Should().Be(11);  // max(10) + 1
        created.Name.Should().Be("NewRecipe");
        created.ClientId.Should().Be(1);
    }

    [Fact]
    public async Task DeleteRecipeAsync_removes_recipe_and_associated_parameters()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "10.0.0.1", ClientIndex = 1 });
        ctx.Db.Recipes.Add(new Recipe { Id = 10, Name = "R", ClientId = 1 });
        ctx.Db.RecipeParameters.Add(new RecipeParameter { RecipeId = 10, ParamCode = 2001, Description = "x" });
        ctx.Db.RecipeParameters.Add(new RecipeParameter { RecipeId = 10, ParamCode = 2002, Description = "y" });
        await ctx.Db.SaveChangesAsync();

        var svc = BuildService(ctx);
        (await svc.DeleteRecipeAsync(10)).Should().BeTrue();
        (await ctx.Db.Recipes.CountAsync()).Should().Be(0);
        (await ctx.Db.RecipeParameters.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteRecipeAsync_returns_false_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx);
        (await svc.DeleteRecipeAsync(9999)).Should().BeFalse();
    }
}
