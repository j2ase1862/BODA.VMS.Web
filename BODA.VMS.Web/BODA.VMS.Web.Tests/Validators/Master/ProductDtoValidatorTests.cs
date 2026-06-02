using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Master;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Master;

public class ProductDtoValidatorTests
{
    private readonly ProductDtoValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var dto = new ProductDto { Code = "PROD-001", Name = "Widget A" };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("PROD 001")]  // space
    [InlineData("프로덕트")]    // non-ASCII
    [InlineData("PROD#1")]    // special char
    public void Code_non_alphanumeric_fails(string code)
    {
        var dto = new ProductDto { Code = code, Name = "X" };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public void Empty_name_fails()
    {
        var dto = new ProductDto { Code = "PROD-001", Name = "" };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Zero_default_recipe_id_fails()
    {
        var dto = new ProductDto { Code = "PROD-001", Name = "X", DefaultRecipeId = 0 };
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.DefaultRecipeId);
    }

    [Fact]
    public void Null_default_recipe_id_passes()
    {
        var dto = new ProductDto { Code = "PROD-001", Name = "X", DefaultRecipeId = null };
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }
}
