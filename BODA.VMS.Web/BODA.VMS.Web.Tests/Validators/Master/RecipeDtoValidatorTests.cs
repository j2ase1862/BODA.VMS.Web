using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Master;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Master;

public class RecipeDtoValidatorTests
{
    private readonly RecipeDtoValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(new RecipeDto { Name = "RecipeA", ClientId = 1 })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_name_fails()
    {
        _validator.TestValidate(new RecipeDto { Name = "", ClientId = 1 })
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Zero_client_id_fails()
    {
        _validator.TestValidate(new RecipeDto { Name = "RecipeA", ClientId = 0 })
            .ShouldHaveValidationErrorFor(x => x.ClientId);
    }

    [Fact]
    public void Description_over_500_fails()
    {
        _validator.TestValidate(new RecipeDto { Name = "RecipeA", ClientId = 1, Description = new string('x', 501) })
            .ShouldHaveValidationErrorFor(x => x.Description);
    }
}
