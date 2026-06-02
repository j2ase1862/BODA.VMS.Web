using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Master;

public sealed class ProductDtoValidator : AbstractValidator<ProductDto>
{
    public ProductDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Product code is required")
            .MaximumLength(50)
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Product code must be alphanumeric (_- allowed)");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required")
            .MaximumLength(100);

        RuleFor(x => x.Specification)
            .MaximumLength(500).When(x => !string.IsNullOrEmpty(x.Specification));

        RuleFor(x => x.DefaultRecipeId)
            .GreaterThan(0).When(x => x.DefaultRecipeId.HasValue);
    }
}
