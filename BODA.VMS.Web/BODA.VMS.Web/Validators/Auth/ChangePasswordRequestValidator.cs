using BODA.VMS.Web.Client.Models;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Auth;

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("CurrentPassword is required");

        // GS 보안 정책: 회원가입과 동일 — 최소 8자 + KISA 복잡도 (3종 8자+ / 2종 10자+)
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("NewPassword is required")
            .MinimumLength(8).WithMessage("NewPassword must be at least 8 characters")
            .MaximumLength(200).WithMessage("NewPassword too long")
            .MustSatisfyPasswordComplexity();
    }
}
