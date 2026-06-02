using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Validators.Master;
using FluentValidation.TestHelper;

namespace BODA.VMS.Web.Tests.Validators.Master;

public class RecipeParameterDtoValidatorTests
{
    private readonly RecipeParameterDtoValidator _validator = new();

    private static RecipeParameterDto Valid() => new()
    {
        RecipeId = 1,
        ParamCode = 1,
        Description = "외경 측정",
        Category = "Dimension"
    };

    [Fact]
    public void Valid_request_passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void Invalid_ids_fail(int recipeId, int paramCode)
    {
        var dto = Valid();
        dto.RecipeId = recipeId;
        dto.ParamCode = paramCode;
        var result = _validator.TestValidate(dto);
        if (recipeId <= 0) result.ShouldHaveValidationErrorFor(x => x.RecipeId);
        if (paramCode <= 0) result.ShouldHaveValidationErrorFor(x => x.ParamCode);
    }

    [Theory]
    [InlineData("Dimension")]
    [InlineData("Angle")]
    [InlineData("Count")]
    [InlineData("Area")]
    [InlineData("Color")]
    [InlineData("Other")]
    public void Allowed_categories_pass(string category)
    {
        var dto = Valid();
        dto.Category = category;
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Unknown_category_fails()
    {
        var dto = Valid();
        dto.Category = "Unknown";
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Category);
    }

    [Fact]
    public void Lower_greater_than_upper_fails()
    {
        var dto = Valid();
        dto.LowerLimit = 10;
        dto.UpperLimit = 5;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Only_lower_or_only_upper_passes()
    {
        var dto = Valid();
        dto.LowerLimit = 10;
        // UpperLimit null
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }
}
