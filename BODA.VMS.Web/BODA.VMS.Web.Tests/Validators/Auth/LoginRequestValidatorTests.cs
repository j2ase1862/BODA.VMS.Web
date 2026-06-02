using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Auth;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Auth;

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var dto = new LoginRequest { Username = "alice", Password = "secret123" };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]      // empty
    [InlineData("ab")]    // too short (2)
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXY")] // 51 chars — over limit (max 50)
    public void Username_invalid_length_fails(string username)
    {
        var dto = new LoginRequest { Username = username, Password = "secret123" };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void Empty_password_fails()
    {
        var dto = new LoginRequest { Username = "alice", Password = "" };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Password_over_200_chars_fails()
    {
        var dto = new LoginRequest { Username = "alice", Password = new string('x', 201) };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Password);
    }
}
