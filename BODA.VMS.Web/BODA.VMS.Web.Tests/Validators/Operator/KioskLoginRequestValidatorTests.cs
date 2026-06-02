using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Operator;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Operator;

public class KioskLoginRequestValidatorTests
{
    private readonly KioskLoginRequestValidator _validator = new();

    [Fact]
    public void Valid_4_digit_pin_passes()
    {
        var dto = new KioskLoginRequest { ClientIndex = 0, EmployeeNumber = "E001", Pin = "1234" };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Valid_8_digit_pin_passes()
    {
        var dto = new KioskLoginRequest { ClientIndex = 5, EmployeeNumber = "E001", Pin = "12345678" };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("123")]      // 3 digits — too short
    [InlineData("123456789")] // 9 digits — too long
    [InlineData("12ab")]      // non-digit
    [InlineData("")]          // empty
    public void Pin_invalid_format_fails(string pin)
    {
        var dto = new KioskLoginRequest { ClientIndex = 0, EmployeeNumber = "E001", Pin = pin };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Pin);
    }

    [Fact]
    public void Negative_client_index_fails()
    {
        var dto = new KioskLoginRequest { ClientIndex = -1, EmployeeNumber = "E001", Pin = "1234" };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.ClientIndex);
    }
}
