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
    [InlineData("Pattern")]
    [InlineData("Blob")]
    [InlineData("Dimension")]
    public void Allowed_categories_pass(string category)
    {
        var dto = Valid();
        dto.Category = category;
        _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
    }

    // "Angle" 등 구 목록은 UI(다이얼로그/프리셋)에서 만들 수 없는 값 —
    // 허용 목록이 UI 체계(Pattern/Blob/Dimension)와 어긋나면 안 된다는 회귀 방지
    [Theory]
    [InlineData("Unknown")]
    [InlineData("Angle")]
    [InlineData("Count")]
    [InlineData("Area")]
    [InlineData("Color")]
    [InlineData("Other")]
    [InlineData("")]
    public void Category_outside_ui_taxonomy_fails(string category)
    {
        var dto = Valid();
        dto.Category = category;
        _validator.TestValidate(dto).ShouldHaveValidationErrorFor(x => x.Category);
    }

    // 프리셋 일괄 추가(/api/parameters/batch)가 쓰는 카테고리 전체가 검증기를
    // 통과해야 함 — 400 회귀(2026-07-20 현장 보고)의 직접 재현 케이스
    [Fact]
    public void All_preset_group_categories_are_allowed()
    {
        foreach (var group in ParameterPresetGroup.PresetGroups)
        {
            var dto = Valid();
            dto.Category = group.Category;
            _validator.TestValidate(dto).ShouldNotHaveAnyValidationErrors();
        }
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
