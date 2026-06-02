using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Admin;

public sealed class ApprovalDtoValidator : AbstractValidator<ApprovalDto>
{
    private static readonly string[] AllowedRoles = { "Admin", "Manager", "User" };

    public ApprovalDtoValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0).WithMessage("UserId must be > 0");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required")
            .Must(r => AllowedRoles.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", AllowedRoles)}");
    }
}
