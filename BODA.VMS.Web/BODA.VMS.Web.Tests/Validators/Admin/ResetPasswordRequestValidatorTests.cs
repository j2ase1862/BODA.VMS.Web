using BODA.VMS.Web.Endpoints;
using BODA.VMS.Web.Validators.Admin;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Admin;

public class ResetPasswordRequestValidatorTests
{
    private readonly ResetPasswordRequestValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(new ResetPasswordRequest { UserId = 1, NewPassword = "newSecret8" })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UserId_invalid_fails(int userId)
    {
        _validator.TestValidate(new ResetPasswordRequest { UserId = userId, NewPassword = "newSecret8" })
            .ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Password_less_than_8_fails()
    {
        _validator.TestValidate(new ResetPasswordRequest { UserId = 1, NewPassword = "abc1234" })
            .ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void Empty_password_fails()
    {
        _validator.TestValidate(new ResetPasswordRequest { UserId = 1, NewPassword = "" })
            .ShouldHaveValidationErrorFor(x => x.NewPassword);
    }
}
