using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Tests.Services;

public class ProductServiceTests
{
    [Fact]
    public async Task GetAllAsync_excludes_inactive_by_default()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Products.Add(new Product { Code = "ACT", Name = "Active", IsActive = true });
        ctx.Db.Products.Add(new Product { Code = "INA", Name = "Inactive", IsActive = false });
        await ctx.Db.SaveChangesAsync();

        var svc = new ProductService(ctx.Db);
        var rows = await svc.GetAllAsync();

        rows.Should().HaveCount(1);
        rows[0].Code.Should().Be("ACT");
    }

    [Fact]
    public async Task GetAllAsync_includeInactive_returns_both_ordered_by_code()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Products.Add(new Product { Code = "B", Name = "x", IsActive = false });
        ctx.Db.Products.Add(new Product { Code = "A", Name = "y", IsActive = true });
        await ctx.Db.SaveChangesAsync();

        var svc = new ProductService(ctx.Db);
        var rows = await svc.GetAllAsync(includeInactive: true);

        rows.Should().HaveCount(2);
        rows.Select(r => r.Code).Should().Equal("A", "B");
    }

    [Fact]
    public async Task GetByIdAsync_returns_dto_with_default_recipe_name()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "10.0.0.1", ClientIndex = 1 });
        ctx.Db.Recipes.Add(new Recipe { Id = 10, Name = "R-DEFAULT", ClientId = 1 });
        ctx.Db.Products.Add(new Product { Code = "P1", Name = "Pro1", DefaultRecipeId = 10, IsActive = true });
        await ctx.Db.SaveChangesAsync();

        var svc = new ProductService(ctx.Db);
        var created = await ctx.Db.Products.FirstAsync();
        var dto = await svc.GetByIdAsync(created.Id);

        dto.Should().NotBeNull();
        dto!.DefaultRecipeName.Should().Be("R-DEFAULT");
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new ProductService(ctx.Db);
        (await svc.GetByIdAsync(9999)).Should().BeNull();
    }

    [Fact]
    public async Task GetByCodeAsync_returns_dto()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Products.Add(new Product { Code = "FIND-ME", Name = "x", IsActive = true });
        await ctx.Db.SaveChangesAsync();

        var svc = new ProductService(ctx.Db);
        var dto = await svc.GetByCodeAsync("FIND-ME");

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("x");
    }

    [Fact]
    public async Task GetByCodeAsync_returns_null_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new ProductService(ctx.Db);
        (await svc.GetByCodeAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_trims_code_and_name_whitespace()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new ProductService(ctx.Db);

        var created = await svc.CreateAsync(new ProductDto
        {
            Code = "  TRIM-001  ",
            Name = " 외경 NG ",
            IsActive = true
        });

        created.Code.Should().Be("TRIM-001");
        created.Name.Should().Be("외경 NG");
        created.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateAsync_modifies_fields_and_sets_updatedAt()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Products.Add(new Product { Code = "ORIG", Name = "Original", IsActive = true });
        await ctx.Db.SaveChangesAsync();

        var svc = new ProductService(ctx.Db);
        var existing = await ctx.Db.Products.FirstAsync();

        var updated = await svc.UpdateAsync(existing.Id, new ProductDto
        {
            Code = "  NEW  ",
            Name = " Renamed ",
            IsActive = false
        });

        updated.Should().NotBeNull();
        updated!.Code.Should().Be("NEW");
        updated.Name.Should().Be("Renamed");
        updated.IsActive.Should().BeFalse();
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_returns_null_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new ProductService(ctx.Db);
        var result = await svc.UpdateAsync(9999, new ProductDto { Code = "X", Name = "Y" });
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_removes_entity()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Products.Add(new Product { Code = "DEL", Name = "x", IsActive = true });
        await ctx.Db.SaveChangesAsync();

        var svc = new ProductService(ctx.Db);
        var existing = await ctx.Db.Products.FirstAsync();
        var ok = await svc.DeleteAsync(existing.Id);

        ok.Should().BeTrue();
        (await ctx.Db.Products.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_returns_false_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new ProductService(ctx.Db);
        (await svc.DeleteAsync(9999)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_throws_when_referenced_by_workOrder()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Clients.Add(new VisionClient { Id = 1, Name = "L1", IpAddress = "10.0.0.1", ClientIndex = 1 });
        ctx.Db.Recipes.Add(new Recipe { Id = 10, Name = "R", ClientId = 1 });
        ctx.Db.Products.Add(new Product { Code = "BUSY", Name = "x", IsActive = true });
        await ctx.Db.SaveChangesAsync();
        var product = await ctx.Db.Products.FirstAsync();
        ctx.Db.WorkOrders.Add(new WorkOrder
        {
            OrderNo = "WO-001",
            ProductId = product.Id,
            ClientId = 1,
            RecipeId = 10,
            PlannedQuantity = 100
        });
        await ctx.Db.SaveChangesAsync();

        var svc = new ProductService(ctx.Db);
        var act = async () => await svc.DeleteAsync(product.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WorkOrder*");
    }
}
