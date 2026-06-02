using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Master;

public sealed class DefectCodeDtoValidator : AbstractValidator<DefectCodeDto>
{
    private static readonly string[] AllowedSeverities = { "Critical", "Major", "Minor" };

    public DefectCodeDtoValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Defect code is required")
            .MaximumLength(30)
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Code must be alphanumeric (_- allowed)");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .MaximumLength(200);

        RuleFor(x => x.Category)
            .MaximumLength(50).When(x => !string.IsNullOrEmpty(x.Category));

        RuleFor(x => x.Severity)
            .Must(s => AllowedSeverities.Contains(s))
            .WithMessage($"Severity must be one of: {string.Join(", ", AllowedSeverities)}");
    }
}
