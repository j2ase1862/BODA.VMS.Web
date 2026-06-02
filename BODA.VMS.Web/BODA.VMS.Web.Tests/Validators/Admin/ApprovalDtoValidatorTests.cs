using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Admin;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Admin;

public class ApprovalDtoValidatorTests
{
    private readonly ApprovalDtoValidator _validator = new();

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("User")]
    public void Allowed_roles_pass(string role)
    {
        _validator.TestValidate(new ApprovalDto { UserId = 1, Role = role })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UserId_invalid_fails(int userId)
    {
        _validator.TestValidate(new ApprovalDto { UserId = userId, Role = "User" })
            .ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Theory]
    [InlineData("SuperUser")]
    [InlineData("admin")]   // case-sensitive
    [InlineData("")]
    public void Invalid_role_fails(string role)
    {
        _validator.TestValidate(new ApprovalDto { UserId = 1, Role = role })
            .ShouldHaveValidationErrorFor(x => x.Role);
    }
}
