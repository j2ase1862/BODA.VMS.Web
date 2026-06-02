using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Master;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Master;

public class OperatorUpsertDtoValidatorTests
{
    private readonly OperatorUpsertDtoValidator _validator = new();

    private static OperatorUpsertDto Valid() => new()
    {
        EmployeeNumber = "E001",
        Name = "홍길동",
        Role = "Operator"
    };

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Lead")]
    [InlineData("Supervisor")]
    public void Allowed_roles_pass(string role)
    {
        var dto = Valid();
        dto.Role = role;
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("operator")]
    public void Invalid_role_fails(string role)
    {
        var dto = Valid();
        dto.Role = role;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Role);
    }

    [Fact]
    public void Null_pin_passes()
    {
        // Pin nullable — 편집 시 비워두면 기존 유지
        var dto = Valid();
        dto.Pin = null;
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("123")]      // 3 digits
    [InlineData("123456789")] // 9 digits
    [InlineData("12ab")]      // non-digit
    public void Invalid_pin_format_fails(string pin)
    {
        var dto = Valid();
        dto.Pin = pin;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Pin);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("12345678")]
    public void Valid_pin_passes(string pin)
    {
        var dto = Valid();
        dto.Pin = pin;
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_employee_number_fails()
    {
        var dto = Valid();
        dto.EmployeeNumber = "";
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.EmployeeNumber);
    }
}
