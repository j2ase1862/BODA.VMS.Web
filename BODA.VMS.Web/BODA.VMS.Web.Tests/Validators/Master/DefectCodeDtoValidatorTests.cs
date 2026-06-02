using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Master;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Master;

public class DefectCodeDtoValidatorTests
{
    private readonly DefectCodeDtoValidator _validator = new();

    [Theory]
    [InlineData("Critical")]
    [InlineData("Major")]
    [InlineData("Minor")]
    public void Allowed_severities_pass(string severity)
    {
        var dto = new DefectCodeDto { Code = "DC-001", Description = "스크래치", Severity = severity };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("DC 001")]
    [InlineData("DC#001")]
    public void Code_non_alphanumeric_fails(string code)
    {
        var dto = new DefectCodeDto { Code = code, Description = "X", Severity = "Major" };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void Empty_description_fails()
    {
        var dto = new DefectCodeDto { Code = "DC-001", Description = "", Severity = "Major" };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Theory]
    [InlineData("Hyper-Critical")]
    [InlineData("major")]   // case-sensitive
    public void Invalid_severity_fails(string severity)
    {
        var dto = new DefectCodeDto { Code = "DC-001", Description = "X", Severity = severity };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Severity);
    }
}
