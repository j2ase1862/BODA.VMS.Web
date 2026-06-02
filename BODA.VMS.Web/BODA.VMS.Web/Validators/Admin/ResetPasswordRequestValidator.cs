using BODA.VMS.Web.Endpoints;
using FluentValidation;

namespace BODA.VMS.Web.Validators.Admin;

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0).WithMessage("UserId must be > 0");

        // GS baseline: 회원가입과 동일 정책 — 최소 8자.
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("NewPassword is required")
            .MinimumLength(8).WithMessage("NewPassword must be at least 8 characters")
            .MaximumLength(200).WithMessage("NewPassword too long");
    }
}
