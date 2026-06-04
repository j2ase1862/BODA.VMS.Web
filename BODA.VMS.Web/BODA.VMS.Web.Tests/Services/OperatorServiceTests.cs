using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// OperatorService — 작업자 마스터 + BCrypt PIN 인증 검증.
/// 보안: PinHash 가 평문/약한 PIN 으로 저장되지 않도록 보장 + Authenticate 가
/// 잘못된 PIN/비활성 작업자에 대해 null 반환.
/// 추적성: 세션 이력 존재 작업자 삭제 차단.
/// </summary>
public class OperatorServiceTests
{
    private static OperatorUpsertDto MakeUpsert(
        string emp = "E001",
        string name = "Alice",
        string? pin = "1234",
        string role = OperatorRole.Operator,
        bool active = true,
        string? department = null)
        => new()
        {
            EmployeeNumber = emp,
            Name = name,
            Pin = pin,
            Role = role,
            IsActive = active,
            Department = department
        };

    // ─── Create ─────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_hashes_pin_and_trims_text_fields()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);

        var created = await svc.CreateAsync(MakeUpsert(emp: "  E007  ", name: "  Alice  ", department: "  QA  ", pin: "5678"));

        created.EmployeeNumber.Should().Be("E007");
        created.Name.Should().Be("Alice");
        created.Department.Should().Be("QA");
        var stored = ctx.Db.Operators.Single();
        // PinHash 는 BCrypt 해시 — 평문 보존 금지
        stored.PinHash.Should().NotBe("5678");
        stored.PinHash.Should().StartWith("$2");
        BCrypt.Net.BCrypt.Verify("5678", stored.PinHash).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_throws_when_pin_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);

        var act = () => svc.CreateAsync(MakeUpsert(pin: null));
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*PIN*");
    }

    [Fact]
    public async Task CreateAsync_throws_when_pin_too_short()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);

        var act = () => svc.CreateAsync(MakeUpsert(pin: "12"));
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*4*");
    }

    [Theory]
    [InlineData("Operator", "Operator")]
    [InlineData("Lead", "Lead")]
    [InlineData("Supervisor", "Supervisor")]
    [InlineData("Admin", "Operator")]      // 알 수 없는 값 → Operator 정규화
    [InlineData(null, "Operator")]
    public async Task CreateAsync_normalizes_unknown_role_to_operator(string? role, string expected)
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);

        var created = await svc.CreateAsync(MakeUpsert(role: role!));
        created.Role.Should().Be(expected);
    }

    [Fact]
    public async Task CreateAsync_normalizes_blank_department_to_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);

        var created = await svc.CreateAsync(MakeUpsert(department: "   "));
        created.Department.Should().BeNull();
    }

    // ─── Read ───────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_default_excludes_inactive_and_orders_by_employee_number()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        await svc.CreateAsync(MakeUpsert(emp: "E003", name: "C"));
        await svc.CreateAsync(MakeUpsert(emp: "E001", name: "A"));
        await svc.CreateAsync(MakeUpsert(emp: "E002", name: "B", active: false));

        var defaultList = await svc.GetAllAsync();
        defaultList.Select(o => o.EmployeeNumber).Should().ContainInOrder("E001", "E003");

        var withInactive = await svc.GetAllAsync(includeInactive: true);
        withInactive.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetByEmployeeNumberAsync_returns_match_or_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        await svc.CreateAsync(MakeUpsert(emp: "E100", name: "X"));

        (await svc.GetByEmployeeNumberAsync("E100"))!.Name.Should().Be("X");
        (await svc.GetByEmployeeNumberAsync("MISSING")).Should().BeNull();
    }

    // ─── Update ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_returns_null_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);

        (await svc.UpdateAsync(999, MakeUpsert())).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_keeps_pinhash_when_pin_blank()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);

        var created = await svc.CreateAsync(MakeUpsert(pin: "1234"));
        var originalHash = ctx.Db.Operators.Single().PinHash;

        var upd = MakeUpsert(emp: "E001", name: "AliceRenamed", pin: null);
        var updated = await svc.UpdateAsync(created.Id, upd);

        updated!.Name.Should().Be("AliceRenamed");
        ctx.Db.Operators.Single().PinHash.Should().Be(originalHash);
    }

    [Fact]
    public async Task UpdateAsync_rehashes_pin_when_provided()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);

        var created = await svc.CreateAsync(MakeUpsert(pin: "1234"));
        var originalHash = ctx.Db.Operators.Single().PinHash;

        var upd = MakeUpsert(pin: "9999");
        await svc.UpdateAsync(created.Id, upd);

        var newHash = ctx.Db.Operators.Single().PinHash;
        newHash.Should().NotBe(originalHash);
        BCrypt.Net.BCrypt.Verify("9999", newHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("1234", newHash).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_throws_when_pin_provided_but_too_short()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        var created = await svc.CreateAsync(MakeUpsert());

        var act = () => svc.UpdateAsync(created.Id, MakeUpsert(pin: "12"));
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*4*");
    }

    // ─── Delete ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_returns_false_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        (await svc.DeleteAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_succeeds_when_no_session_history()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        var created = await svc.CreateAsync(MakeUpsert());

        (await svc.DeleteAsync(created.Id)).Should().BeTrue();
        ctx.Db.Operators.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_blocks_when_session_history_exists()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        var created = await svc.CreateAsync(MakeUpsert());

        // OperatorSession FK 부모(VisionClient) seed
        ctx.Db.Clients.Add(new VisionClient
        {
            Id = 1, Name = "L0", IpAddress = "127.0.0.1", ClientIndex = 0,
            IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await ctx.Db.SaveChangesAsync();

        ctx.Db.OperatorSessions.Add(new OperatorSession
        {
            OperatorId = created.Id,
            ClientId = 1,
            StartedAt = DateTime.UtcNow
        });
        await ctx.Db.SaveChangesAsync();

        // 감사 추적성: 세션 이력은 보존 — 비활성화로 처리
        var act = () => svc.DeleteAsync(created.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*비활성화*");
    }

    // ─── Authenticate ──────────────────────────────────────

    [Fact]
    public async Task AuthenticateAsync_returns_dto_with_correct_pin()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        await svc.CreateAsync(MakeUpsert(emp: "E100", pin: "8765"));

        var result = await svc.AuthenticateAsync("E100", "8765");
        result.Should().NotBeNull();
        result!.EmployeeNumber.Should().Be("E100");
    }

    [Fact]
    public async Task AuthenticateAsync_returns_null_for_wrong_pin()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        await svc.CreateAsync(MakeUpsert(emp: "E100", pin: "8765"));

        (await svc.AuthenticateAsync("E100", "0000")).Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_returns_null_for_unknown_employee()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        await svc.CreateAsync(MakeUpsert(emp: "E100", pin: "8765"));

        (await svc.AuthenticateAsync("UNKNOWN", "8765")).Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_returns_null_for_inactive_operator()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new OperatorService(ctx.Db);
        await svc.CreateAsync(MakeUpsert(emp: "E100", pin: "8765", active: false));

        // 보안: 퇴직 등 비활성 작업자는 올바른 PIN 으로도 로그인 차단
        (await svc.AuthenticateAsync("E100", "8765")).Should().BeNull();
    }
}
