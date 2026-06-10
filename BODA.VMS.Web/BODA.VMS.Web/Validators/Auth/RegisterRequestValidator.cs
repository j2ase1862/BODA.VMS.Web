using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .Length(3, 50).WithMessage("Username must be 3-50 characters")
            .Matches("^[A-Za-z0-9_.-]+$").WithMessage("Username must be alphanumeric (._- allowed)");

        // GS 보안 정책: 최소 8자 + KISA 복잡도 (3종 조합 8자+ / 2종 조합 10자+)
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(200).WithMessage("Password too long")
            .MustSatisfyPasswordComplexity();

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MaximumLength(100).WithMessage("Display name max 100 characters");
    }
}
