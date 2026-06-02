using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Alarm;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Alarm;

public class AlarmActionRequestValidatorTests
{
    private readonly AlarmActionRequestValidator _validator = new();

    [Fact]
    public void Acknowledge_without_resolution_passes()
    {
        var dto = new AlarmActionRequest { Action = "Acknowledge" };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Resolve_with_resolution_passes()
    {
        var dto = new AlarmActionRequest { Action = "Resolve", Resolution = "센서 교체 완료" };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Resolve_without_resolution_fails()
    {
        // 조건부 검증 — Resolve 시 Resolution 필수
        var dto = new AlarmActionRequest { Action = "Resolve", Resolution = null };
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.Resolution)
            .WithErrorMessage("Resolution is required when Action is 'Resolve'");
    }

    [Theory]
    [InlineData("Dismiss")]
    [InlineData("acknowledge")] // case-sensitive
    public void Unknown_action_fails(string action)
    {
        var dto = new AlarmActionRequest { Action = action };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Action);
    }
}
