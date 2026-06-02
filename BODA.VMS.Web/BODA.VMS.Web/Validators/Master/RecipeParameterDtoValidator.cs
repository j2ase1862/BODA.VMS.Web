using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Master;

public sealed class RecipeParameterDtoValidator : AbstractValidator<RecipeParameterDto>
{
    private static readonly string[] AllowedCategories =
        { "Dimension", "Angle", "Count", "Area", "Color", "Other" };

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
