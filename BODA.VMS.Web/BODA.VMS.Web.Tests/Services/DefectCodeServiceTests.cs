using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Tests.Services;

public class DefectCodeServiceTests
{
    [Fact]
    public async Task GetAllAsync_excludes_inactive_by_default()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.DefectCodes.Add(new Data.Entities.DefectCode
        {
            Code = "ACT-001", Description = "Active", Severity = "Major", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        ctx.Db.DefectCodes.Add(new Data.Entities.DefectCode
        {
            Code = "OBS-001", Description = "Obsolete", Severity = "Minor", IsActive = false, CreatedAt = DateTime.UtcNow
        });
        await ctx.Db.SaveChangesAsync();

        var svc = new DefectCodeService(ctx.Db);
        var rows = await svc.GetAllAsync();

        rows.Should().HaveCount(1);
        rows[0].Code.Should().Be("ACT-001");
    }

    [Fact]
    public async Task GetAllAsync_includeInactive_returns_both()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.DefectCodes.Add(new Data.Entities.DefectCode
        {
            Code = "A", Description = "x", Severity = "Major", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        ctx.Db.DefectCodes.Add(new Data.Entities.DefectCode
        {
            Code = "B", Description = "y", Severity = "Minor", IsActive = false, CreatedAt = DateTime.UtcNow
        });
        await ctx.Db.SaveChangesAsync();

        var svc = new DefectCodeService(ctx.Db);
        (await svc.GetAllAsync(includeInactive: true)).Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_trims_whitespace()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new DefectCodeService(ctx.Db);

        var created = await svc.CreateAsync(new DefectCodeDto
        {
            Code = "  TRIM-001  ",
            Description = " 외경 NG ",
            Severity = "Major",
            IsActive = true
        });

        created.Code.Should().Be("TRIM-001");
        created.Description.Should().Be("외경 NG");
    }

    [Fact]
    public async Task CreateAsync_with_duplicate_code_throws_UniqueConstraint()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.DefectCodes.Add(new Data.Entities.DefectCode
        {
            Code = "DUP-001", Description = "first", Severity = "Major", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await ctx.Db.SaveChangesAsync();

        var svc = new DefectCodeService(ctx.Db);
        // 동일 Code 재삽입 — DB UNIQUE 제약 위반시 DbUpdateException 예상
        var act = () => svc.CreateAsync(new DefectCodeDto
        {
            Code = "DUP-001", Description = "second", Severity = "Minor", IsActive = true
        });
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UpdateAsync_returns_null_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new DefectCodeService(ctx.Db);

        var result = await svc.UpdateAsync(999, new DefectCodeDto
        {
            Code = "X", Description = "y", Severity = "Major"
        });
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_modifies_existing_and_sets_UpdatedAt()
    {
        using var ctx = new InMemorySqliteDbContext();
        var entity = new Data.Entities.DefectCode
        {
            Code = "U-001", Description = "old", Severity = "Minor", IsActive = true, CreatedAt = DateTime.UtcNow
        };
        ctx.Db.DefectCodes.Add(entity);
        await ctx.Db.SaveChangesAsync();

        var svc = new DefectCodeService(ctx.Db);
        var updated = await svc.UpdateAsync(entity.Id, new DefectCodeDto
        {
            Code = "U-001", Description = "new", Severity = "Critical", IsActive = false
        });

        updated.Should().NotBeNull();
        updated!.Description.Should().Be("new");
        updated.Severity.Should().Be("Critical");
        updated.IsActive.Should().BeFalse();
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_returns_false_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new DefectCodeService(ctx.Db);
        (await svc.DeleteAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_removes_existing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var entity = new Data.Entities.DefectCode
        {
            Code = "D-001", Description = "to delete", Severity = "Minor", IsActive = true, CreatedAt = DateTime.UtcNow
        };
        ctx.Db.DefectCodes.Add(entity);
        await ctx.Db.SaveChangesAsync();

        var svc = new DefectCodeService(ctx.Db);
        (await svc.DeleteAsync(entity.Id)).Should().BeTrue();
        ctx.Db.DefectCodes.Any(d => d.Id == entity.Id).Should().BeFalse();
    }
}
