using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Master;

public sealed class OperatorUpsertDtoValidator : AbstractValidator<OperatorUpsertDto>
{
    private static readonly string[] AllowedRoles = { "Operator", "Lead", "Supervisor" };

    public OperatorUpsertDtoValidator()
    {
        RuleFor(x => x.EmployeeNumber)
            .NotEmpty().WithMessage("EmployeeNumber is required")
            .MaximumLength(50);

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100);

        RuleFor(x => x.Department)
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.Department));

        RuleFor(x => x.Role)
            .Must(r => AllowedRoles.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", AllowedRoles)}");

        // Pin nullable — 편집 시 비워두면 기존 유지. 값이 있으면 4-8 자리 숫자.
        RuleFor(x => x.Pin)
            .Matches(@"^\d{4,8}$")
            .When(x => !string.IsNullOrEmpty(x.Pin))
            .WithMessage("Pin must be 4-8 digits when provided");
    }
}
