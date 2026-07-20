using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Master;

public sealed class RecipeParameterDtoValidator : AbstractValidator<RecipeParameterDto>
{
    // UI 체계와 반드시 일치해야 함: InspectionItemFormDialog 카테고리 선택지 +
    // ParameterPresetGroup.PresetGroups (Pattern Tool / Blob Tool / Dimension Tool)
    private static readonly string[] AllowedCategories =
        { "Pattern", "Blob", "Dimension" };

    public RecipeParameterDtoValidator()
    {
        RuleFor(x => x.RecipeId).GreaterThan(0).WithMessage("RecipeId must be > 0");
        RuleFor(x => x.ParamCode).GreaterThan(0).WithMessage("ParamCode must be > 0");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .MaximumLength(200);

        RuleFor(x => x.Category)
            .Must(c => AllowedCategories.Contains(c))
            .WithMessage($"Category must be one of: {string.Join(", ", AllowedCategories)}");

        RuleFor(x => x.Unit)
            .MaximumLength(20).When(x => !string.IsNullOrEmpty(x.Unit));

        // 하한 ≤ 상한 (둘 다 지정시)
        RuleFor(x => x)
            .Must(p => !(p.LowerLimit.HasValue && p.UpperLimit.HasValue)
                      || p.LowerLimit <= p.UpperLimit)
            .WithMessage("LowerLimit must be <= UpperLimit");
    }
}
