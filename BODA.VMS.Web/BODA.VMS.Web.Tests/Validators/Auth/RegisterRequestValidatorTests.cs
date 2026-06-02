using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Auth;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Auth;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    private static RegisterRequest Valid() => new()
    {
        Username = "alice_2026",
        Password = "P@ssw0rd1!",
        DisplayName = "Alice"
    };

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("alice user")]   // space
    [InlineData("alice!")]       // special char
    [InlineData("한국어")]        // non-ASCII
    public void Username_non_alphanumeric_fails(string username)
    {
        var dto = Valid();
        dto.Username = username;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void Password_less_than_8_chars_fails()
    {
        var dto = Valid();
        dto.Password = "abc1234"; // 7 chars
        var result = _validator.TestValidate(dto);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must be at least 8 characters");
    }

    [Fact]
    public void DisplayName_required()
    {
        var dto = Valid();
        dto.DisplayName = "";
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.DisplayName);
    }
}
